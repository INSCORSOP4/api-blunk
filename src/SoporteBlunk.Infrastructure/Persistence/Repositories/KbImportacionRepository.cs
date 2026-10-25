using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Conocimiento;
using SoporteBlunk.Infrastructure.Persistence.Kb;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class KbImportacionRepository(IKbConnectionFactory factory) : IKbImportacionRepository
{
    private const int TimeoutSegundos = 120;

    private sealed record ReglaRow(string Tipo, string Patron, string? Motivo);
    private sealed record DocRow(int DocumentoId, int Version, bool Eliminado, byte[] HashOrigen);
    private sealed record IndiceRow(int DocumentoId, string RutaRelativa, string Titulo);
    private sealed record AliasRow(int DocumentoId, string Alias);
    private sealed record EtiquetaRow(int EtiquetaId, string Nombre);
    private sealed record EnlacePendienteRow(int EnlaceId, string Destino, string RutaOrigen);
    private sealed record RotosRow(int DocumentoOrigenId, int Total);

    private sealed record ResumenRow(
        int ImportacionId, string UsuarioNombre, string UsuarioEmail, string Origen, string Estado,
        DateTime IniciadaEn, DateTime? TerminadaEn, int Archivos, int Nuevos, int Actualizados, int SinCambios,
        int Omitidos, int ConSecretos, int Errores, string? Informe = null)
    {
        public ImportacionResumen ToResumen() => new(
            ImportacionId, UsuarioNombre, UsuarioEmail, Origen, Estado, Utc(IniciadaEn), TerminadaEn is { } t ? Utc(t) : null,
            Archivos, Nuevos, Actualizados, SinCambios, Omitidos, ConSecretos, Errores);

        private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));
    }

    private static CommandDefinition Cmd(string sql, object? p, IDbTransaction? tx, CancellationToken ct) =>
        new(sql, p, tx, commandTimeout: TimeoutSegundos, cancellationToken: ct);

    private static async Task<T> Proteger<T>(Func<Task<T>> accion)
    {
        try { return await accion(); }
        catch (SqlException ex)
        {
            throw new KbNoDisponibleException("La base de conocimiento no respondió o no tiene el esquema esperado.", ex);
        }
    }

    // ---------------------------------------------------------------- lecturas
    public Task<IReadOnlyList<ReglaOmision>> ReglasOmisionAsync(CancellationToken ct) => Proteger<IReadOnlyList<ReglaOmision>>(async () =>
    {
        await using var cn = await factory.OpenAsync(ct);
        var filas = await cn.QueryAsync<ReglaRow>(Cmd("SELECT Tipo, Patron, Motivo FROM kb.ReglaOmision WHERE Activa = 1", null, null, ct));
        return filas.Select(f => new ReglaOmision(f.Tipo, f.Patron, f.Motivo)).ToList();
    });

    public Task<IReadOnlyList<ImportacionResumen>> ListarAsync(int maximo, CancellationToken ct) => Proteger<IReadOnlyList<ImportacionResumen>>(async () =>
    {
        const string sql = """
            SELECT TOP (@maximo) ImportacionId, UsuarioNombre, UsuarioEmail, Origen, Estado, IniciadaEn, TerminadaEn,
                   Archivos, Nuevos, Actualizados, SinCambios, Omitidos, ConSecretos, Errores,
                   CAST(NULL AS nvarchar(max)) AS Informe -- el listado no trae el informe (puede ser grande)
            FROM kb.Importacion ORDER BY ImportacionId DESC
            """;
        await using var cn = await factory.OpenAsync(ct);
        return (await cn.QueryAsync<ResumenRow>(Cmd(sql, new { maximo }, null, ct))).Select(r => r.ToResumen()).ToList();
    });

    public Task<ImportacionDetalle?> ObtenerAsync(int importacionId, CancellationToken ct) => Proteger<ImportacionDetalle?>(async () =>
    {
        const string sql = """
            SELECT ImportacionId, UsuarioNombre, UsuarioEmail, Origen, Estado, IniciadaEn, TerminadaEn,
                   Archivos, Nuevos, Actualizados, SinCambios, Omitidos, ConSecretos, Errores, Informe
            FROM kb.Importacion WHERE ImportacionId = @importacionId
            """;
        await using var cn = await factory.OpenAsync(ct);
        var fila = await cn.QuerySingleOrDefaultAsync<ResumenRow>(Cmd(sql, new { importacionId }, null, ct));
        return fila is null ? null : new ImportacionDetalle(fila.ToResumen(), fila.Informe);
    });

    // ---------------------------------------------------------------- escritura
    public async Task<ResultadoImportacion> GuardarAsync(
        ContextoImportacion contexto,
        IReadOnlyList<NotaProcesada> notas,
        ResumenArchivos resumen,
        Func<IReadOnlyList<NotaGuardada>, string> construirInforme,
        CancellationToken ct)
    {
        await using var cn = await factory.OpenAsync(ct);
        var u = contexto.Usuario;

        // La fila de la importación se crea FUERA de la transacción: si el guardado falla, queda registrada como Fallida.
        int importacionId;
        try
        {
            importacionId = await cn.ExecuteScalarAsync<int>(Cmd("""
                INSERT INTO kb.Importacion (UsuarioId, UsuarioNombre, UsuarioEmail, Origen)
                OUTPUT INSERTED.ImportacionId
                VALUES (@Id, @Nombre, @Email, @Origen)
                """, new { u.Id, Nombre = Recortar(u.Nombre, 100), Email = Recortar(u.Email, 100), contexto.Origen }, null, ct));
        }
        catch (SqlException ex)
        {
            throw new KbNoDisponibleException("No se pudo registrar la importación en la base de conocimiento.", ex);
        }

        try
        {
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            try
            {
                var guardadas = await GuardarNotasAsync(cn, tx, importacionId, u, notas, ct);

                await cn.ExecuteAsync(Cmd("""
                    UPDATE kb.Importacion
                    SET Estado = N'Completada', TerminadaEn = SYSUTCDATETIME(), Archivos = @Archivos, Nuevos = @Nuevos,
                        Actualizados = @Actualizados, SinCambios = @SinCambios, Omitidos = @Omitidos,
                        ConSecretos = @ConSecretos, Errores = @Errores, Informe = @Informe
                    WHERE ImportacionId = @importacionId
                    """, new
                {
                    importacionId,
                    Archivos = resumen.Total,
                    Nuevos = guardadas.Count(g => g.Resultado == ResultadoNota.Nueva),
                    Actualizados = guardadas.Count(g => g.Resultado == ResultadoNota.Actualizada),
                    SinCambios = guardadas.Count(g => g.Resultado == ResultadoNota.SinCambios),
                    resumen.Omitidos,
                    ConSecretos = notas.Count(n => n.SecretosEnmascarados > 0),
                    resumen.Errores,
                    Informe = construirInforme(guardadas),
                }, tx, ct));

                await tx.CommitAsync(ct);
                return new ResultadoImportacion(importacionId, guardadas);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (SqlException ex)
        {
            await MarcarFallidaAsync(cn, importacionId);
            throw new KbNoDisponibleException("No se pudo guardar la importación; no se guardó ninguna nota.", ex);
        }
    }

    private static async Task MarcarFallidaAsync(SqlConnection cn, int importacionId)
    {
        try
        {
            await cn.ExecuteAsync(Cmd(
                "UPDATE kb.Importacion SET Estado = N'Fallida', TerminadaEn = SYSUTCDATETIME(), Informe = N'{\"error\":\"No se pudo guardar la importación.\"}' WHERE ImportacionId = @importacionId",
                new { importacionId }, null, CancellationToken.None));
        }
        catch (SqlException)
        {
            // Si ni siquiera se puede marcar, la fila queda "EnCurso": es una pista para quien revise la bitácora.
        }
    }

    private static async Task<IReadOnlyList<NotaGuardada>> GuardarNotasAsync(
        SqlConnection cn, SqlTransaction tx, int importacionId, UsuarioActual u,
        IReadOnlyList<NotaProcesada> notas, CancellationToken ct)
    {
        // 1) Etiquetas: se crean las que falten (una sola vez para todo el lote).
        var etiquetaIds = await AsegurarEtiquetasAsync(cn, tx, notas, ct);

        // 2) Altas / actualizaciones / sin cambios, nota por nota.
        var resultados = new Dictionary<string, ResultadoNota>(StringComparer.OrdinalIgnoreCase);
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cambiadas = new List<(int Id, NotaProcesada Nota)>();

        foreach (var nota in notas)
        {
            var existente = await cn.QuerySingleOrDefaultAsync<DocRow>(Cmd(
                "SELECT DocumentoId, Version, Eliminado, HashOrigen FROM kb.Documento WITH (UPDLOCK, HOLDLOCK) WHERE RutaRelativa = @Ruta",
                new { Ruta = nota.RutaRelativa }, tx, ct));

            if (existente is not null && !existente.Eliminado && existente.HashOrigen.AsSpan().SequenceEqual(nota.HashOrigen))
            {
                resultados[nota.RutaRelativa] = ResultadoNota.SinCambios;
                ids[nota.RutaRelativa] = existente.DocumentoId;
                continue;
            }

            int id, version;
            var p = ParametrosDocumento(nota, u);
            if (existente is null)
            {
                id = await cn.ExecuteScalarAsync<int>(Cmd("""
                    INSERT INTO kb.Documento (RutaRelativa, Titulo, Carpeta, Resumen, Estado, ContenidoMd, ContenidoTexto, HashOrigen,
                                              SecretosEnmascarados, ModificadoEnOrigen, CreadoPorId, CreadoPorNombre, ActualizadoPorId, ActualizadoPorNombre)
                    OUTPUT INSERTED.DocumentoId
                    VALUES (@Ruta, @Titulo, @Carpeta, @Resumen, @Estado, @Md, @Texto, @Hash, @Secretos, @Modificado, @UsuarioId, @UsuarioNombre, @UsuarioId, @UsuarioNombre)
                    """, p, tx, ct));
                version = 1;
                resultados[nota.RutaRelativa] = ResultadoNota.Nueva;
            }
            else
            {
                id = existente.DocumentoId;
                p.Add("Id", id);
                version = await cn.ExecuteScalarAsync<int>(Cmd("""
                    UPDATE kb.Documento
                    SET Titulo = @Titulo, Carpeta = @Carpeta, Resumen = @Resumen, Estado = @Estado, ContenidoMd = @Md,
                        ContenidoTexto = @Texto, HashOrigen = @Hash, SecretosEnmascarados = @Secretos, ModificadoEnOrigen = @Modificado,
                        Version = Version + 1, Eliminado = 0, EliminadoEn = NULL, ActualizadoEn = SYSUTCDATETIME(),
                        ActualizadoPorId = @UsuarioId, ActualizadoPorNombre = @UsuarioNombre
                    OUTPUT INSERTED.Version
                    WHERE DocumentoId = @Id
                    """, p, tx, ct));
                resultados[nota.RutaRelativa] = ResultadoNota.Actualizada;
            }

            ids[nota.RutaRelativa] = id;
            cambiadas.Add((id, nota));

            // Historial: guarda quién subió esta versión y desde qué importación.
            var pv = new DynamicParameters();
            pv.Add("DocumentoId", id); pv.Add("Version", version); pv.Add("Md", nota.ContenidoMd);
            pv.Add("Hash", nota.HashOrigen, DbType.Binary, size: 32);
            pv.Add("importacionId", importacionId); pv.Add("UsuarioId", u.Id); pv.Add("UsuarioNombre", Recortar(u.Nombre, 100));
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO kb.DocumentoVersion (DocumentoId, Version, ContenidoMd, HashOrigen, ImportacionId, UsuarioId, UsuarioNombre)
                VALUES (@DocumentoId, @Version, @Md, @Hash, @importacionId, @UsuarioId, @UsuarioNombre)
                """, pv, tx, ct));

            await ReemplazarAliasYEtiquetasAsync(cn, tx, id, nota, etiquetaIds, ct);
        }

        // 3) Enlaces: se resuelven contra TODAS las notas vigentes (las nuevas y las que ya estaban).
        var indice = await CargarIndiceAsync(cn, tx, ct);
        foreach (var (id, nota) in cambiadas)
        {
            await cn.ExecuteAsync(Cmd("DELETE FROM kb.Enlace WHERE DocumentoOrigenId = @id", new { id }, tx, ct));
            if (nota.Enlaces.Count == 0) continue;

            var json = JsonSerializer.Serialize(nota.Enlaces.Select(e => new
            {
                d = e.Destino,
                s = e.Seccion,
                t = e.TextoVisible,
                r = indice.Indice.Resolver(e.Destino, nota.RutaRelativa, e.EsRuta) is { } ruta ? indice.Ids[ruta] : (int?)null,
            }));
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO kb.Enlace (DocumentoOrigenId, Destino, Seccion, TextoVisible, DocumentoDestinoId)
                SELECT @id, j.Destino, j.Seccion, j.Texto, j.Resuelto
                FROM OPENJSON(@json) WITH (Destino nvarchar(450) '$.d', Seccion nvarchar(300) '$.s', Texto nvarchar(300) '$.t', Resuelto int '$.r') j
                """, new { id, json }, tx, ct));
        }

        // 4) Enlaces rotos de notas que NO cambiaron pero que ahora pueden apuntar a una nota recién subida.
        await ResolverPendientesAsync(cn, tx, indice, cambiadas.Select(c => c.Id).ToList(), ct);

        // 5) Cuántos enlaces siguen rotos por nota (para el informe).
        var todosIds = ids.Values.Distinct().ToList();
        var rotos = (await cn.QueryAsync<RotosRow>(Cmd("""
            SELECT DocumentoOrigenId, COUNT(*) AS Total FROM kb.Enlace
            WHERE DocumentoDestinoId IS NULL AND DocumentoOrigenId IN (SELECT CAST(value AS int) FROM OPENJSON(@json))
            GROUP BY DocumentoOrigenId
            """, new { json = JsonSerializer.Serialize(todosIds) }, tx, ct))).ToDictionary(r => r.DocumentoOrigenId, r => r.Total);

        return notas.Select(n => new NotaGuardada(n.RutaRelativa, resultados[n.RutaRelativa], rotos.GetValueOrDefault(ids[n.RutaRelativa]))).ToList();
    }

    private static DynamicParameters ParametrosDocumento(NotaProcesada n, UsuarioActual u)
    {
        var p = new DynamicParameters();
        p.Add("Ruta", n.RutaRelativa); p.Add("Titulo", n.Titulo); p.Add("Carpeta", n.Carpeta);
        p.Add("Resumen", n.Resumen); p.Add("Estado", n.Estado);
        p.Add("Md", n.ContenidoMd); p.Add("Texto", n.ContenidoTexto);
        p.Add("Hash", n.HashOrigen, DbType.Binary, size: 32);
        p.Add("Secretos", n.SecretosEnmascarados);
        p.Add("Modificado", n.ModificadoEnOrigen is { } m && m.Year >= 1900 ? m.UtcDateTime : (DateTime?)null);
        p.Add("UsuarioId", u.Id); p.Add("UsuarioNombre", Recortar(u.Nombre, 100));
        return p;
    }

    private static async Task<Dictionary<string, int>> AsegurarEtiquetasAsync(
        SqlConnection cn, SqlTransaction tx, IReadOnlyList<NotaProcesada> notas, CancellationToken ct)
    {
        var nombres = notas.SelectMany(n => n.Etiquetas).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (nombres.Count == 0) return resultado;

        var json = JsonSerializer.Serialize(nombres);
        await cn.ExecuteAsync(Cmd("""
            INSERT INTO kb.Etiqueta (Nombre)
            SELECT CAST(j.value AS nvarchar(100)) FROM OPENJSON(@json) j
            WHERE NOT EXISTS (SELECT 1 FROM kb.Etiqueta e WITH (UPDLOCK, HOLDLOCK) WHERE e.Nombre = CAST(j.value AS nvarchar(100)))
            """, new { json }, tx, ct));

        var filas = await cn.QueryAsync<EtiquetaRow>(Cmd(
            "SELECT EtiquetaId, Nombre FROM kb.Etiqueta WHERE Nombre IN (SELECT CAST(value AS nvarchar(100)) FROM OPENJSON(@json))",
            new { json }, tx, ct));
        foreach (var f in filas) resultado[f.Nombre] = f.EtiquetaId;
        return resultado;
    }

    private static async Task ReemplazarAliasYEtiquetasAsync(
        SqlConnection cn, SqlTransaction tx, int id, NotaProcesada nota, Dictionary<string, int> etiquetaIds, CancellationToken ct)
    {
        await cn.ExecuteAsync(Cmd("DELETE FROM kb.DocumentoAlias WHERE DocumentoId = @id; DELETE FROM kb.DocumentoEtiqueta WHERE DocumentoId = @id;",
            new { id }, tx, ct));

        if (nota.Alias.Count > 0)
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO kb.DocumentoAlias (DocumentoId, Alias)
                SELECT DISTINCT @id, CAST(j.value AS nvarchar(300)) FROM OPENJSON(@json) j
                """, new { id, json = JsonSerializer.Serialize(nota.Alias) }, tx, ct));

        var ids = nota.Etiquetas.Where(etiquetaIds.ContainsKey).Select(e => etiquetaIds[e]).Distinct().ToList();
        if (ids.Count > 0)
            await cn.ExecuteAsync(Cmd("""
                INSERT INTO kb.DocumentoEtiqueta (DocumentoId, EtiquetaId)
                SELECT @id, CAST(j.value AS int) FROM OPENJSON(@json) j
                """, new { id, json = JsonSerializer.Serialize(ids) }, tx, ct));
    }

    private sealed record IndiceCargado(IndiceEnlaces Indice, Dictionary<string, int> Ids);

    private static async Task<IndiceCargado> CargarIndiceAsync(SqlConnection cn, SqlTransaction tx, CancellationToken ct)
    {
        var docs = (await cn.QueryAsync<IndiceRow>(Cmd("SELECT DocumentoId, RutaRelativa, Titulo FROM kb.Documento WHERE Eliminado = 0", null, tx, ct))).ToList();
        var alias = (await cn.QueryAsync<AliasRow>(Cmd(
            "SELECT a.DocumentoId, a.Alias FROM kb.DocumentoAlias a JOIN kb.Documento d ON d.DocumentoId = a.DocumentoId WHERE d.Eliminado = 0", null, tx, ct)))
            .GroupBy(a => a.DocumentoId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(a => a.Alias).ToList());

        var indice = new IndiceEnlaces(docs.Select(d => new NotaIndexada(d.RutaRelativa, d.Titulo, alias.GetValueOrDefault(d.DocumentoId) ?? [])));
        return new IndiceCargado(indice, docs.ToDictionary(d => d.RutaRelativa, d => d.DocumentoId, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task ResolverPendientesAsync(
        SqlConnection cn, SqlTransaction tx, IndiceCargado indice, List<int> idsCambiados, CancellationToken ct)
    {
        var pendientes = await cn.QueryAsync<EnlacePendienteRow>(Cmd("""
            SELECT e.EnlaceId, e.Destino, o.RutaRelativa AS RutaOrigen
            FROM kb.Enlace e JOIN kb.Documento o ON o.DocumentoId = e.DocumentoOrigenId
            WHERE e.DocumentoDestinoId IS NULL AND o.Eliminado = 0
              AND o.DocumentoId NOT IN (SELECT CAST(value AS int) FROM OPENJSON(@json))
            """, new { json = JsonSerializer.Serialize(idsCambiados) }, tx, ct));

        var resueltos = pendientes
            .Select(p => (p.EnlaceId, Ruta: indice.Indice.Resolver(p.Destino, p.RutaOrigen)))
            .Where(x => x.Ruta is not null)
            .Select(x => new { i = x.EnlaceId, d = indice.Ids[x.Ruta!] })
            .ToList();
        if (resueltos.Count == 0) return;

        await cn.ExecuteAsync(Cmd("""
            UPDATE e SET DocumentoDestinoId = j.Destino
            FROM kb.Enlace e JOIN OPENJSON(@json) WITH (Id int '$.i', Destino int '$.d') j ON j.Id = e.EnlaceId
            """, new { json = JsonSerializer.Serialize(resueltos) }, tx, ct));
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
