using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Conocimiento;
using SoporteBlunk.Domain.Entities.Kb;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class KbImportacionRepository(KbDbContext db) : IKbImportacionRepository
{
    private const int TimeoutSegundos = 120;

    private sealed record DocFila(int Id, int Version, bool Eliminado, byte[] HashOrigen);

    private sealed class ImportacionFila
    {
        public int Id { get; init; }
        public string UsuarioNombre { get; init; } = string.Empty;
        public string UsuarioEmail { get; init; } = string.Empty;
        public string Origen { get; init; } = string.Empty;
        public string Estado { get; init; } = string.Empty;
        public DateTime IniciadaEn { get; init; }
        public DateTime? TerminadaEn { get; init; }
        public int Archivos { get; init; }
        public int Nuevos { get; init; }
        public int Actualizados { get; init; }
        public int SinCambios { get; init; }
        public int Omitidos { get; init; }
        public int ConSecretos { get; init; }
        public int Errores { get; init; }
        public string? Informe { get; init; }

        public ImportacionResumen ToResumen() => new(
            Id, UsuarioNombre, UsuarioEmail, Origen, Estado, Utc(IniciadaEn), TerminadaEn is { } t ? Utc(t) : null,
            Archivos, Nuevos, Actualizados, SinCambios, Omitidos, ConSecretos, Errores);

        private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));
    }

    private Task<T> Proteger<T>(Func<Task<T>> accion) => db.ConTimeout(TimeoutSegundos, async () =>
    {
        try { return await accion(); }
        catch (SqlException ex)
        {
            throw new KbNoDisponibleException("La base de conocimiento no respondió o no tiene el esquema esperado.", ex);
        }
    });

    // ---------------------------------------------------------------- lecturas
    public Task<IReadOnlyList<ReglaOmision>> ReglasOmisionAsync(CancellationToken ct) => Proteger<IReadOnlyList<ReglaOmision>>(async () =>
    {
        var filas = await db.ReglasOmision.Where(r => r.Activa).Select(r => new { r.Tipo, r.Patron, r.Motivo }).ToListAsync(ct);
        return filas.ConvertAll(f => new ReglaOmision(f.Tipo, f.Patron, f.Motivo));
    });

    public Task<IReadOnlyList<ImportacionResumen>> ListarAsync(int maximo, CancellationToken ct) => Proteger<IReadOnlyList<ImportacionResumen>>(async () =>
    {
        // El listado no trae el informe (puede ser grande).
        var filas = await db.Importaciones.OrderByDescending(i => i.Id).Take(maximo)
            .Select(i => new ImportacionFila
            {
                Id = i.Id, UsuarioNombre = i.UsuarioNombre, UsuarioEmail = i.UsuarioEmail, Origen = i.Origen, Estado = i.Estado,
                IniciadaEn = i.IniciadaEn, TerminadaEn = i.TerminadaEn, Archivos = i.Archivos, Nuevos = i.Nuevos, Actualizados = i.Actualizados,
                SinCambios = i.SinCambios, Omitidos = i.Omitidos, ConSecretos = i.ConSecretos, Errores = i.Errores, Informe = null,
            })
            .ToListAsync(ct);
        return filas.ConvertAll(f => f.ToResumen());
    });

    public Task<ImportacionDetalle?> ObtenerAsync(int importacionId, CancellationToken ct) => Proteger<ImportacionDetalle?>(async () =>
    {
        var fila = await db.Importaciones.Where(i => i.Id == importacionId)
            .Select(i => new ImportacionFila
            {
                Id = i.Id, UsuarioNombre = i.UsuarioNombre, UsuarioEmail = i.UsuarioEmail, Origen = i.Origen, Estado = i.Estado,
                IniciadaEn = i.IniciadaEn, TerminadaEn = i.TerminadaEn, Archivos = i.Archivos, Nuevos = i.Nuevos, Actualizados = i.Actualizados,
                SinCambios = i.SinCambios, Omitidos = i.Omitidos, ConSecretos = i.ConSecretos, Errores = i.Errores, Informe = i.Informe,
            })
            .SingleOrDefaultAsync(ct);
        return fila is null ? null : new ImportacionDetalle(fila.ToResumen(), fila.Informe);
    });

    // ---------------------------------------------------------------- escritura
    public Task<ResultadoImportacion> GuardarAsync(
        ContextoImportacion contexto,
        IReadOnlyList<NotaProcesada> notas,
        ResumenArchivos resumen,
        Func<IReadOnlyList<NotaGuardada>, string> construirInforme,
        CancellationToken ct) => db.ConTimeout(TimeoutSegundos, async () =>
    {
        var u = contexto.Usuario;

        // La fila de la importación se crea FUERA de la transacción: si el guardado falla, queda registrada como Fallida.
        int importacionId;
        try
        {
            var importacion = new KbImportacion
            {
                UsuarioId = u.Id, UsuarioNombre = Recortar(u.Nombre, 100), UsuarioEmail = Recortar(u.Email, 100), Origen = contexto.Origen,
            };
            db.Importaciones.Add(importacion);
            await db.SaveChangesAsync(ct);
            importacionId = importacion.Id;
            db.ChangeTracker.Clear();
        }
        catch (Exception ex) when (BitacoraRepository.EsErrorDeBase(ex))
        {
            db.ChangeTracker.Clear();
            throw new KbNoDisponibleException("No se pudo registrar la importación en la base de conocimiento.", ex);
        }

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var guardadas = await GuardarNotasAsync(importacionId, u, notas, ct);
                var informe = construirInforme(guardadas);
                var nuevos = guardadas.Count(g => g.Resultado == ResultadoNota.Nueva);
                var actualizados = guardadas.Count(g => g.Resultado == ResultadoNota.Actualizada);
                var sinCambios = guardadas.Count(g => g.Resultado == ResultadoNota.SinCambios);
                var conSecretos = notas.Count(n => n.SecretosEnmascarados > 0);

                // La hora de cierre la pone el servidor SQL (UTC), como antes.
                await db.Importaciones.Where(i => i.Id == importacionId).ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Estado, EstadosImportacion.Completada)
                    .SetProperty(i => i.TerminadaEn, i => DateTime.UtcNow)
                    .SetProperty(i => i.Archivos, resumen.Total)
                    .SetProperty(i => i.Nuevos, nuevos)
                    .SetProperty(i => i.Actualizados, actualizados)
                    .SetProperty(i => i.SinCambios, sinCambios)
                    .SetProperty(i => i.Omitidos, resumen.Omitidos)
                    .SetProperty(i => i.ConSecretos, conSecretos)
                    .SetProperty(i => i.Errores, resumen.Errores)
                    .SetProperty(i => i.Informe, informe), ct);

                await tx.CommitAsync(ct);
                return new ResultadoImportacion(importacionId, guardadas);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (Exception ex) when (BitacoraRepository.EsErrorDeBase(ex))
        {
            db.ChangeTracker.Clear();
            await MarcarFallidaAsync(importacionId);
            throw new KbNoDisponibleException("No se pudo guardar la importación; no se guardó ninguna nota.", ex);
        }
    });

    private async Task MarcarFallidaAsync(int importacionId)
    {
        try
        {
            await db.Importaciones.Where(i => i.Id == importacionId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Estado, EstadosImportacion.Fallida)
                .SetProperty(i => i.TerminadaEn, i => DateTime.UtcNow)
                .SetProperty(i => i.Informe, "{\"error\":\"No se pudo guardar la importación.\"}"), CancellationToken.None);
        }
        catch (Exception ex) when (BitacoraRepository.EsErrorDeBase(ex))
        {
            // Si ni siquiera se puede marcar, la fila queda "EnCurso": es una pista para quien revise la bitácora.
        }
    }

    private async Task<IReadOnlyList<NotaGuardada>> GuardarNotasAsync(int importacionId, UsuarioActual u, IReadOnlyList<NotaProcesada> notas, CancellationToken ct)
    {
        // 1) Etiquetas: se crean las que falten (una sola vez para todo el lote).
        var etiquetaIds = await AsegurarEtiquetasAsync(notas, ct);

        // 2) Altas / actualizaciones / sin cambios, nota por nota.
        var resultados = new Dictionary<string, ResultadoNota>(StringComparer.OrdinalIgnoreCase);
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cambiadas = new List<(int Id, NotaProcesada Nota)>();

        foreach (var nota in notas)
        {
            var existente = await LeerBloqueadoAsync(nota.RutaRelativa, ct);

            if (existente is not null && !existente.Eliminado && existente.HashOrigen.AsSpan().SequenceEqual(nota.HashOrigen))
            {
                resultados[nota.RutaRelativa] = ResultadoNota.SinCambios;
                ids[nota.RutaRelativa] = existente.Id;
                continue;
            }

            int id, version;
            var modificado = nota.ModificadoEnOrigen is { } m && m.Year >= 1900 ? m.UtcDateTime : (DateTime?)null;
            var nombreUsuario = Recortar(u.Nombre, 100);
            if (existente is null)
            {
                var documento = new KbDocumento
                {
                    RutaRelativa = nota.RutaRelativa, Titulo = nota.Titulo, Carpeta = nota.Carpeta, Resumen = nota.Resumen, Estado = nota.Estado,
                    ContenidoMd = nota.ContenidoMd, ContenidoTexto = nota.ContenidoTexto, HashOrigen = nota.HashOrigen,
                    SecretosEnmascarados = nota.SecretosEnmascarados, ModificadoEnOrigen = modificado, Version = 1, Eliminado = false,
                    CreadoPorId = u.Id, CreadoPorNombre = nombreUsuario, ActualizadoPorId = u.Id, ActualizadoPorNombre = nombreUsuario,
                };
                db.Documentos.Add(documento);
                await db.SaveChangesAsync(ct);
                id = documento.Id;
                version = 1;
                db.ChangeTracker.Clear();
                resultados[nota.RutaRelativa] = ResultadoNota.Nueva;
            }
            else
            {
                id = existente.Id;
                // La fila está bloqueada (UPDLOCK, HOLDLOCK) dentro de la transacción: nadie más la cambió, así que la versión nueva es la leída + 1.
                version = existente.Version + 1;
                await db.Documentos.Where(d => d.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Titulo, nota.Titulo)
                    .SetProperty(d => d.Carpeta, nota.Carpeta)
                    .SetProperty(d => d.Resumen, nota.Resumen)
                    .SetProperty(d => d.Estado, nota.Estado)
                    .SetProperty(d => d.ContenidoMd, nota.ContenidoMd)
                    .SetProperty(d => d.ContenidoTexto, nota.ContenidoTexto)
                    .SetProperty(d => d.HashOrigen, nota.HashOrigen)
                    .SetProperty(d => d.SecretosEnmascarados, nota.SecretosEnmascarados)
                    .SetProperty(d => d.ModificadoEnOrigen, modificado)
                    .SetProperty(d => d.Version, version)
                    .SetProperty(d => d.Eliminado, false)
                    .SetProperty(d => d.EliminadoEn, (DateTime?)null)
                    .SetProperty(d => d.ActualizadoEn, d => DateTime.UtcNow)
                    .SetProperty(d => d.ActualizadoPorId, u.Id)
                    .SetProperty(d => d.ActualizadoPorNombre, nombreUsuario), ct);
                resultados[nota.RutaRelativa] = ResultadoNota.Actualizada;
            }

            ids[nota.RutaRelativa] = id;
            cambiadas.Add((id, nota));

            // Historial (quién subió esta versión y desde qué importación) y reemplazo de alias y etiquetas.
            await db.DocumentoAliases.Where(a => a.DocumentoId == id).ExecuteDeleteAsync(ct);
            await db.DocumentoEtiquetas.Where(de => de.DocumentoId == id).ExecuteDeleteAsync(ct);

            db.DocumentoVersiones.Add(new KbDocumentoVersion
            {
                DocumentoId = id, Version = version, ContenidoMd = nota.ContenidoMd, HashOrigen = nota.HashOrigen,
                ImportacionId = importacionId, UsuarioId = u.Id, UsuarioNombre = nombreUsuario,
            });
            // Los alias distintos se comparan sin distinguir mayúsculas, como el DISTINCT de la base (cotejo CI).
            db.DocumentoAliases.AddRange(nota.Alias.Distinct(StringComparer.OrdinalIgnoreCase).Select(a => new KbDocumentoAlias { DocumentoId = id, Alias = a }));
            db.DocumentoEtiquetas.AddRange(nota.Etiquetas.Where(etiquetaIds.ContainsKey).Select(e => etiquetaIds[e]).Distinct()
                .Select(etiquetaId => new KbDocumentoEtiqueta { DocumentoId = id, EtiquetaId = etiquetaId }));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        // 3) Enlaces: se resuelven contra TODAS las notas vigentes (las nuevas y las que ya estaban).
        var indice = await CargarIndiceAsync(ct);
        foreach (var (id, nota) in cambiadas)
        {
            await db.Enlaces.Where(e => e.DocumentoOrigenId == id).ExecuteDeleteAsync(ct);
            if (nota.Enlaces.Count == 0) continue;

            db.Enlaces.AddRange(nota.Enlaces.Select(e => new KbEnlace
            {
                DocumentoOrigenId = id,
                Destino = e.Destino,
                Seccion = e.Seccion,
                TextoVisible = e.TextoVisible,
                DocumentoDestinoId = indice.Indice.Resolver(e.Destino, nota.RutaRelativa, e.EsRuta) is { } ruta ? indice.Ids[ruta] : null,
            }));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        // 4) Enlaces rotos de notas que NO cambiaron pero que ahora pueden apuntar a una nota recién subida.
        await ResolverPendientesAsync(indice, cambiadas.Select(c => c.Id).ToList(), ct);

        // 5) Cuántos enlaces siguen rotos por nota (para el informe).
        var todosIds = ids.Values.Distinct().ToList();
        var rotos = await db.Enlaces.Where(e => e.DocumentoDestinoId == null && todosIds.Contains(e.DocumentoOrigenId))
            .GroupBy(e => e.DocumentoOrigenId)
            .Select(g => new { Id = g.Key, Total = g.Count() })
            .ToDictionaryAsync(r => r.Id, r => r.Total, ct);

        return notas.Select(n => new NotaGuardada(n.RutaRelativa, resultados[n.RutaRelativa], rotos.GetValueOrDefault(ids[n.RutaRelativa]))).ToList();
    }

    /// <summary>
    /// Busca la nota por su ruta bloqueándola (UPDLOCK, HOLDLOCK) hasta el fin de la transacción: dos importaciones simultáneas de la misma
    /// ruta no pueden crearla las dos. Es una de las dos consultas con SQL propio, porque EF Core no tiene pistas de bloqueo.
    /// </summary>
    private Task<DocFila?> LeerBloqueadoAsync(string ruta, CancellationToken ct) =>
        db.Documentos
            .FromSql($"SELECT * FROM kb.Documento WITH (UPDLOCK, HOLDLOCK) WHERE RutaRelativa = {ruta}")
            .Select(d => new DocFila(d.Id, d.Version, d.Eliminado, d.HashOrigen))
            .SingleOrDefaultAsync(ct);

    private async Task<Dictionary<string, int>> AsegurarEtiquetasAsync(IReadOnlyList<NotaProcesada> notas, CancellationToken ct)
    {
        var nombres = notas.SelectMany(n => n.Etiquetas).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (nombres.Count == 0) return resultado;

        // Segunda consulta con SQL propio: lee las etiquetas que ya existen bloqueando esas llaves (UPDLOCK, HOLDLOCK) para que dos
        // importaciones simultáneas no creen la misma etiqueta nueva.
        var json = JsonSerializer.Serialize(nombres);
        var existentes = await db.Etiquetas
            .FromSql($"SELECT * FROM kb.Etiqueta WITH (UPDLOCK, HOLDLOCK) WHERE Nombre IN (SELECT CAST(value AS nvarchar(100)) FROM OPENJSON({json}))")
            .ToListAsync(ct);
        foreach (var e in existentes) resultado[e.Nombre] = e.Id;

        var nuevas = nombres.Where(n => !resultado.ContainsKey(n)).Select(n => new KbEtiqueta { Nombre = n }).ToList();
        if (nuevas.Count > 0)
        {
            db.Etiquetas.AddRange(nuevas);
            await db.SaveChangesAsync(ct);
            foreach (var e in nuevas) resultado[e.Nombre] = e.Id;
            db.ChangeTracker.Clear();
        }
        return resultado;
    }

    private sealed record IndiceCargado(IndiceEnlaces Indice, Dictionary<string, int> Ids);

    private async Task<IndiceCargado> CargarIndiceAsync(CancellationToken ct)
    {
        var docs = await db.Documentos.Where(d => !d.Eliminado).Select(d => new { d.Id, d.RutaRelativa, d.Titulo }).ToListAsync(ct);
        var alias = (await (from a in db.DocumentoAliases
                            join d in db.Documentos on a.DocumentoId equals d.Id
                            where !d.Eliminado
                            select new { a.DocumentoId, a.Alias }).ToListAsync(ct))
            .GroupBy(a => a.DocumentoId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(a => a.Alias).ToList());

        var indice = new IndiceEnlaces(docs.Select(d => new NotaIndexada(d.RutaRelativa, d.Titulo, alias.GetValueOrDefault(d.Id) ?? [])));
        return new IndiceCargado(indice, docs.ToDictionary(d => d.RutaRelativa, d => d.Id, StringComparer.OrdinalIgnoreCase));
    }

    private async Task ResolverPendientesAsync(IndiceCargado indice, List<int> idsCambiados, CancellationToken ct)
    {
        var pendientes = await (from e in db.Enlaces
                                join o in db.Documentos on e.DocumentoOrigenId equals o.Id
                                where e.DocumentoDestinoId == null && !o.Eliminado && !idsCambiados.Contains(o.Id)
                                select new { EnlaceId = e.Id, e.Destino, RutaOrigen = o.RutaRelativa }).ToListAsync(ct);

        var resueltos = pendientes
            .Select(p => (p.EnlaceId, Ruta: indice.Indice.Resolver(p.Destino, p.RutaOrigen)))
            .Where(x => x.Ruta is not null)
            .GroupBy(x => indice.Ids[x.Ruta!], x => x.EnlaceId);

        // Un UPDATE por nota destino (en lugar de uno por enlace).
        foreach (var grupo in resueltos)
        {
            var destino = grupo.Key;
            var enlaces = grupo.ToList();
            await db.Enlaces.Where(e => enlaces.Contains(e.Id)).ExecuteUpdateAsync(s => s.SetProperty(e => e.DocumentoDestinoId, (int?)destino), ct);
        }
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
