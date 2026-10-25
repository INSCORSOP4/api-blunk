using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Queries;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class KbReadRepository(KbDbContext db) : IKbReadRepository
{
    private const int TimeoutSegundos = 30;
    private const int MaxPalabras = 6;
    private const int MaxLargoPalabra = 50;

    private static DateTimeOffset Utc(DateTime d) => new(DateTime.SpecifyKind(d, DateTimeKind.Utc));

    private Task<T> Proteger<T>(Func<Task<T>> accion) => db.ConTimeout(TimeoutSegundos, async () =>
    {
        try { return await accion(); }
        catch (SqlException ex)
        {
            // Típico: el esquema "kb" no existe porque no se ejecutó 001_crear_SoporteKB.sql en esa base.
            throw new KbNoDisponibleException("La base de conocimiento no tiene el esquema esperado.", ex);
        }
    });

    // ------------------------------------------------------------------ estado
    public Task<KbEstado> EstadoAsync(CancellationToken ct) => Proteger(async () =>
    {
        var version = await db.VersionesEsquema.MaxAsync(v => (int?)v.Version, ct) ?? 0;
        var documentos = await db.Documentos.CountAsync(d => !d.Eliminado, ct);

        // Único SQL propio de la lectura: si el índice de texto completo existe se pregunta al catálogo del sistema (sys.fulltext_indexes),
        // que no se mapea como entidad. Es una consulta fija, sin parámetros.
        var textoCompleto = await db.Database.SqlQuery<bool>($"""
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'kb.Documento'))
                             THEN 1 ELSE 0 END AS bit) AS [Value]
            """).SingleAsync(ct);

        return new KbEstado(version, textoCompleto, documentos);
    });

    // ------------------------------------------------------------------ listado y búsqueda
    public Task<PagedResult<DocumentoResumen>> ListarDocumentosAsync(DocumentoFiltro filtro, int page, int pageSize, CancellationToken ct) => Proteger(async () =>
    {
        var palabras = Palabras(filtro.Buscar);
        var filtradas = KbConsultas.Filtradas(db, filtro, palabras);

        var total = await filtradas.CountAsync(ct);
        var filas = await KbConsultas.ProyectarPagina(KbConsultas.Ordenadas(filtradas, palabras, filtro.Orden), palabras)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var etiquetas = new Dictionary<int, List<string>>();
        if (filas.Count > 0)
        {
            var ids = filas.Select(f => f.Id).ToList();
            foreach (var t in await KbConsultas.EtiquetasDe(db, ids).ToListAsync(ct))
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
        var d = await db.Documentos.Where(x => x.Id == id && !x.Eliminado).SingleOrDefaultAsync(ct);
        if (d is null) return null;

        var etiquetas = await (from de in db.DocumentoEtiquetas
                               join e in db.Etiquetas on de.EtiquetaId equals e.Id
                               where de.DocumentoId == id
                               orderby e.Nombre
                               select e.Nombre).ToListAsync(ct);
        var alias = await db.DocumentoAliases.Where(a => a.DocumentoId == id).OrderBy(a => a.Alias).Select(a => a.Alias).ToListAsync(ct);
        var enlaces = (await KbConsultas.EnlacesDe(db, id).ToListAsync(ct)).Select(e => new EnlaceDocumento(
            e.Destino, e.Seccion, e.TextoVisible,
            e.DestinoId is { } destinoId ? new DocumentoRelacionado(destinoId, e.TituloDestino!, e.RutaDestino!, e.CarpetaDestino!) : null)).ToList();
        var desde = (await KbConsultas.EnlazadaDesde(db, id).ToListAsync(ct)).ConvertAll(r => r.ToDomain());
        var versiones = (await db.DocumentoVersiones.Where(v => v.DocumentoId == id)
                .OrderByDescending(v => v.Version)
                .Select(v => new { v.Version, v.CreadoEn, v.UsuarioNombre })
                .ToListAsync(ct))
            .Select(v => new VersionDocumento(v.Version, Utc(v.CreadoEn), v.UsuarioNombre)).ToList();

        return new DocumentoDetalle(
            d.Id, d.RutaRelativa, d.Titulo, d.Carpeta, d.Resumen, d.Estado, d.ContenidoMd, etiquetas, alias, enlaces, desde, versiones,
            d.Version, d.SecretosEnmascarados, Utc(d.CreadoEn), d.CreadoPorNombre, Utc(d.ActualizadoEn), d.ActualizadoPorNombre);
    });

    // ------------------------------------------------------------------ carpetas y etiquetas
    public Task<IReadOnlyList<CarpetaConteo>> CarpetasAsync(CancellationToken ct) =>
        Proteger<IReadOnlyList<CarpetaConteo>>(async () => await KbConsultas.Carpetas(db).ToListAsync(ct));

    public Task<IReadOnlyList<EtiquetaConteo>> EtiquetasAsync(CancellationToken ct) =>
        Proteger<IReadOnlyList<EtiquetaConteo>>(async () => await KbConsultas.Etiquetas(db).ToListAsync(ct));

    // ------------------------------------------------------------------ utilidades
    /// <summary>Palabras de la búsqueda: sin repetidas, con tope de cantidad y de largo.</summary>
    internal static List<string> Palabras(string? buscar) => KbConsultas.Palabras(buscar, MaxPalabras, MaxLargoPalabra);

    /// <summary>Escapa los comodines de LIKE (% _ [ \) para que la búsqueda sea literal.</summary>
    internal static string EscaparLike(string s) => KbConsultas.EscaparLike(s);
}
