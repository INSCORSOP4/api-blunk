using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

internal sealed class ArchivoRevisionFila
{
    public int ArchivoId { get; init; }
    public string Ruta { get; init; } = string.Empty;
    public string NombreArchivo { get; init; } = string.Empty;
    public DateOnly? Fecha { get; init; }
    public string? Estado { get; init; }
    public DateTime? UltimaModificacion { get; init; }
    public string? Detalle { get; init; }
    public DateTime? RevisadoEn { get; init; }
}

internal static class ArchivosConsultas
{
    /// <summary>
    /// Cada archivo activo del catálogo con su revisión más reciente (null si nunca se ha revisado). Como la clave de
    /// RevisionArchivo es (Fecha, Archivo), la revisión con la fecha más alta es única: equivale al MAX(Fecha) + JOIN anterior.
    /// </summary>
    public static IQueryable<ArchivoRevisionFila> ConUltimaRevision(KbDbContext db) =>
        db.ArchivosEsperados
            .Where(a => a.Activo)
            .Select(a => new
            {
                Archivo = a,
                Ultima = db.RevisionesArchivo.Where(r => r.ArchivoId == a.Id).OrderByDescending(r => r.Fecha).FirstOrDefault(),
            })
            .OrderBy(x => x.Archivo.Ruta).ThenBy(x => x.Archivo.NombreArchivo)
            .Select(x => new ArchivoRevisionFila
            {
                ArchivoId = x.Archivo.Id,
                Ruta = x.Archivo.Ruta,
                NombreArchivo = x.Archivo.NombreArchivo,
                Fecha = x.Ultima == null ? null : x.Ultima.Fecha,
                Estado = x.Ultima!.Estado,
                UltimaModificacion = x.Ultima.UltimaModificacion,
                Detalle = x.Ultima.Detalle,
                RevisadoEn = x.Ultima == null ? null : x.Ultima.RevisadoEn,
            });
}
