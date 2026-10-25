using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Kb;
using SoporteBlunk.Infrastructure.Persistence.Sql;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class KbReadRepository(IKbConnectionFactory factory) : IKbReadRepository
{
    // Búsqueda sin distinguir mayúsculas ni acentos ("contrasena" encuentra "Contraseña").
    private const string Cotejo = "Latin1_General_100_CI_AI";
    private const int MaxPalabras = 6;
    private const int MaxLargoPalabra = 50;

    private sealed record ResumenRow(
        int Id, string Ruta, string Titulo, string Carpeta, string? Resumen, string? Estado,
        DateTime ActualizadoEn, string ActualizadoPor, int SecretosEnmascarados, string? Fragmento);

    private sealed record EtiquetaDeDocRow(int DocumentoId, string Nombre);

    private sealed record DetalleRow(
        int Id, string Ruta, string Titulo, string Carpeta, string? Resumen, string? Estado, string ContenidoMd,
        int Version, int SecretosEnmascarados, DateTime CreadoEn, string CreadoPor, DateTime ActualizadoEn, string ActualizadoPor);

    private sealed record EnlaceRow(string Destino, string? Seccion, string? TextoVisible, int? DestinoId, string? TituloDestino, string? RutaDestino, string? CarpetaDestino);
    private sealed record RelacionadoRow(int Id, string Titulo, string Ruta, string Carpeta);
    private sealed record VersionRow(int Version, DateTime Fecha, string Usuario);

    private static CommandDefinition Cmd(string sql, object? p, CancellationToken ct) =>
        new(sql, p, commandTimeout: TicketSql.CommandTimeoutSeconds, cancellationToken: ct);

    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    private static async Task<T> Proteger<T>(Func<Task<T>> accion)
    {
        try { return await accion(); }
        catch (SqlException ex)
        {
            // Típico: el esquema "kb" no existe porque no se ejecutó 001_crear_SoporteKB.sql en esa base.
            throw new KbNoDisponibleException("La base de conocimiento no tiene el esquema esperado.", ex);
        }
    }

    // ------------------------------------------------------------------ estado
    public Task<KbEstado> EstadoAsync(CancellationToken ct) => Proteger(async () =>
    {
        const string sql = """
            SELECT
                ISNULL((SELECT MAX(Version) FROM kb.VersionEsquema), 0) AS VersionEsquema,
                CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'kb.Documento'))
                          THEN 1 ELSE 0 END AS bit) AS TextoCompleto,
                (SELECT COUNT(*) FROM kb.Documento WHERE Eliminado = 0) AS Documentos
            """;
        await using var cn = await factory.OpenAsync(ct);
        return await cn.QuerySingleAsync<KbEstado>(Cmd(sql, null, ct));
    });

    // ------------------------------------------------------------------ listado y búsqueda
    public Task<PagedResult<DocumentoResumen>> ListarDocumentosAsync(DocumentoFiltro filtro, int page, int pageSize, CancellationToken ct) => Proteger(async () =>
    {
        var palabras = Palabras(filtro.Buscar);
        var p = new DynamicParameters();
        var donde = new List<string> { "d.Eliminado = 0" };

        if (!string.IsNullOrWhiteSpace(filtro.Carpeta))
        {
            var carpeta = filtro.Carpeta.Trim().Trim('/');
            donde.Add(@"(d.Carpeta = @carpeta OR d.Carpeta LIKE @carpetaHijas ESCAPE '\')");
            p.Add("carpeta", carpeta);
            p.Add("carpetaHijas", EscaparLike(carpeta) + "/%");
        }

        if (!string.IsNullOrWhiteSpace(filtro.Etiqueta))
        {
            donde.Add("""
                EXISTS (SELECT 1 FROM kb.DocumentoEtiqueta de JOIN kb.Etiqueta e ON e.EtiquetaId = de.EtiquetaId
                        WHERE de.DocumentoId = d.DocumentoId AND e.Nombre = @etiqueta)
                """);
            p.Add("etiqueta", filtro.Etiqueta.Trim().TrimStart('#'));
        }

        var coincideTitulo = new List<string>();
        for (var i = 0; i < palabras.Count; i++)
        {
            p.Add($"t{i}", $"%{EscaparLike(palabras[i])}%");
            donde.Add($$"""
                (d.Titulo COLLATE {{Cotejo}} LIKE @t{{i}} ESCAPE '\'
                 OR d.ContenidoTexto COLLATE {{Cotejo}} LIKE @t{{i}} ESCAPE '\'
                 OR EXISTS (SELECT 1 FROM kb.DocumentoAlias a WHERE a.DocumentoId = d.DocumentoId AND a.Alias COLLATE {{Cotejo}} LIKE @t{{i}} ESCAPE '\')
                 OR EXISTS (SELECT 1 FROM kb.DocumentoEtiqueta de JOIN kb.Etiqueta e ON e.EtiquetaId = de.EtiquetaId
                            WHERE de.DocumentoId = d.DocumentoId AND e.Nombre COLLATE {{Cotejo}} LIKE @t{{i}} ESCAPE '\'))
                """);
            coincideTitulo.Add($@"d.Titulo COLLATE {Cotejo} LIKE @t{i} ESCAPE '\'");
        }

        var whereSql = string.Join(" AND ", donde);
        var prioridad = coincideTitulo.Count > 0 ? $"CASE WHEN {string.Join(" AND ", coincideTitulo)} THEN 0 ELSE 1 END, " : "";
        var orden = filtro.Orden == OrdenDocumentos.Titulo
            ? $"{prioridad}d.Titulo, d.DocumentoId"
            : $"{prioridad}d.ActualizadoEn DESC, d.DocumentoId DESC";

        // Fragmento: ~240 caracteres alrededor de la primera coincidencia con la primera palabra (solo si hay búsqueda).
        string fragmento, aplicar;
        if (palabras.Count > 0)
        {
            p.Add("r0", palabras[0]);
            aplicar = $"OUTER APPLY (SELECT CHARINDEX(@r0, d.ContenidoTexto COLLATE {Cotejo}) AS pos) f";
            fragmento = "CASE WHEN f.pos = 0 THEN NULL ELSE SUBSTRING(d.ContenidoTexto, CASE WHEN f.pos > 60 THEN f.pos - 60 ELSE 1 END, 240) END";
        }
        else
        {
            aplicar = "";
            fragmento = "CAST(NULL AS nvarchar(240))";
        }

        p.Add("offset", (page - 1) * pageSize);
        p.Add("pageSize", pageSize);

        var sqlLista = $"""
            SELECT d.DocumentoId AS Id, d.RutaRelativa AS Ruta, d.Titulo, d.Carpeta, d.Resumen, d.Estado,
                   d.ActualizadoEn, d.ActualizadoPorNombre AS ActualizadoPor, d.SecretosEnmascarados,
                   {fragmento} AS Fragmento
            FROM kb.Documento d
            {aplicar}
            WHERE {whereSql}
            ORDER BY {orden}
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
            """;
        var sqlTotal = $"SELECT COUNT(*) FROM kb.Documento d WHERE {whereSql}";

        await using var cn = await factory.OpenAsync(ct);
        var total = await cn.ExecuteScalarAsync<int>(Cmd(sqlTotal, p, ct));
        var filas = (await cn.QueryAsync<ResumenRow>(Cmd(sqlLista, p, ct))).ToList();

        var etiquetas = new Dictionary<int, List<string>>();
        if (filas.Count > 0)
        {
            var ids = JsonSerializer.Serialize(filas.Select(f => f.Id));
            var tags = await cn.QueryAsync<EtiquetaDeDocRow>(Cmd("""
                SELECT de.DocumentoId, e.Nombre FROM kb.DocumentoEtiqueta de JOIN kb.Etiqueta e ON e.EtiquetaId = de.EtiquetaId
                WHERE de.DocumentoId IN (SELECT CAST(value AS int) FROM OPENJSON(@ids)) ORDER BY e.Nombre
                """, new { ids }, ct));
            foreach (var t in tags)
            {
                if (!etiquetas.TryGetValue(t.DocumentoId, out var lista)) etiquetas[t.DocumentoId] = lista = [];
                lista.Add(t.Nombre);
            }
        }

        var items = filas.Select(f => new DocumentoResumen(
            f.Id, f.Ruta, f.Titulo, f.Carpeta, f.Resumen, f.Estado,
            etiquetas.GetValueOrDefault(f.Id) ?? [], Utc(f.ActualizadoEn), f.ActualizadoPor, f.SecretosEnmascarados, f.Fragmento)).ToList();
        return new PagedResult<DocumentoResumen>(items, page, pageSize, total);
    });

    // ------------------------------------------------------------------ detalle
    public Task<DocumentoDetalle?> ObtenerDocumentoAsync(int id, CancellationToken ct) => Proteger<DocumentoDetalle?>(async () =>
    {
        const string sql = """
            SELECT DocumentoId AS Id, RutaRelativa AS Ruta, Titulo, Carpeta, Resumen, Estado, ContenidoMd, Version,
                   SecretosEnmascarados, CreadoEn, CreadoPorNombre AS CreadoPor, ActualizadoEn, ActualizadoPorNombre AS ActualizadoPor
            FROM kb.Documento WHERE DocumentoId = @id AND Eliminado = 0;

            SELECT e.Nombre FROM kb.DocumentoEtiqueta de JOIN kb.Etiqueta e ON e.EtiquetaId = de.EtiquetaId
            WHERE de.DocumentoId = @id ORDER BY e.Nombre;

            SELECT Alias FROM kb.DocumentoAlias WHERE DocumentoId = @id ORDER BY Alias;

            SELECT e.Destino, e.Seccion, e.TextoVisible, e.DocumentoDestinoId AS DestinoId,
                   t.Titulo AS TituloDestino, t.RutaRelativa AS RutaDestino, t.Carpeta AS CarpetaDestino
            FROM kb.Enlace e LEFT JOIN kb.Documento t ON t.DocumentoId = e.DocumentoDestinoId AND t.Eliminado = 0
            WHERE e.DocumentoOrigenId = @id ORDER BY e.EnlaceId;

            SELECT DISTINCT o.DocumentoId AS Id, o.Titulo, o.RutaRelativa AS Ruta, o.Carpeta
            FROM kb.Enlace e JOIN kb.Documento o ON o.DocumentoId = e.DocumentoOrigenId
            WHERE e.DocumentoDestinoId = @id AND o.Eliminado = 0 AND o.DocumentoId <> @id ORDER BY o.Titulo;

            SELECT Version, CreadoEn AS Fecha, UsuarioNombre AS Usuario FROM kb.DocumentoVersion
            WHERE DocumentoId = @id ORDER BY Version DESC;
            """;

        await using var cn = await factory.OpenAsync(ct);
        using var multi = await cn.QueryMultipleAsync(Cmd(sql, new { id }, ct));

        var d = await multi.ReadSingleOrDefaultAsync<DetalleRow>();
        if (d is null) return null;

        var etiquetas = (await multi.ReadAsync<string>()).ToList();
        var alias = (await multi.ReadAsync<string>()).ToList();
        var enlaces = (await multi.ReadAsync<EnlaceRow>()).Select(e => new EnlaceDocumento(
            e.Destino, e.Seccion, e.TextoVisible,
            e.DestinoId is { } destinoId ? new DocumentoRelacionado(destinoId, e.TituloDestino!, e.RutaDestino!, e.CarpetaDestino!) : null)).ToList();
        var desde = (await multi.ReadAsync<RelacionadoRow>()).Select(r => new DocumentoRelacionado(r.Id, r.Titulo, r.Ruta, r.Carpeta)).ToList();
        var versiones = (await multi.ReadAsync<VersionRow>()).Select(v => new VersionDocumento(v.Version, Utc(v.Fecha), v.Usuario)).ToList();

        return new DocumentoDetalle(
            d.Id, d.Ruta, d.Titulo, d.Carpeta, d.Resumen, d.Estado, d.ContenidoMd, etiquetas, alias, enlaces, desde, versiones,
            d.Version, d.SecretosEnmascarados, Utc(d.CreadoEn), d.CreadoPor, Utc(d.ActualizadoEn), d.ActualizadoPor);
    });

    // ------------------------------------------------------------------ carpetas y etiquetas
    public Task<IReadOnlyList<CarpetaConteo>> CarpetasAsync(CancellationToken ct) => Proteger<IReadOnlyList<CarpetaConteo>>(async () =>
    {
        await using var cn = await factory.OpenAsync(ct);
        return (await cn.QueryAsync<CarpetaConteo>(Cmd(
            "SELECT Carpeta, COUNT(*) AS Documentos FROM kb.Documento WHERE Eliminado = 0 GROUP BY Carpeta ORDER BY Carpeta", null, ct))).ToList();
    });

    public Task<IReadOnlyList<EtiquetaConteo>> EtiquetasAsync(CancellationToken ct) => Proteger<IReadOnlyList<EtiquetaConteo>>(async () =>
    {
        await using var cn = await factory.OpenAsync(ct);
        return (await cn.QueryAsync<EtiquetaConteo>(Cmd("""
            SELECT e.Nombre, COUNT(*) AS Documentos
            FROM kb.Etiqueta e
            JOIN kb.DocumentoEtiqueta de ON de.EtiquetaId = e.EtiquetaId
            JOIN kb.Documento d ON d.DocumentoId = de.DocumentoId AND d.Eliminado = 0
            GROUP BY e.Nombre ORDER BY COUNT(*) DESC, e.Nombre
            """, null, ct))).ToList();
    });

    // ------------------------------------------------------------------ utilidades
    /// <summary>Palabras de la búsqueda: sin repetidas, con tope de cantidad y de largo.</summary>
    internal static List<string> Palabras(string? buscar) =>
        (buscar ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > MaxLargoPalabra ? w[..MaxLargoPalabra] : w)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPalabras)
            .ToList();

    /// <summary>Escapa los comodines de LIKE (% _ [ \) para que la búsqueda sea literal.</summary>
    internal static string EscaparLike(string s) =>
        s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");
}
