namespace SoporteBlunk.Domain.Models;

/// <summary>
/// SinRevisar: el catalogo tiene el archivo pero nunca se ha corrido una revision sobre el.
/// Los demas vienen tal cual del script de revision (ver archivos.RevisionArchivo en SoporteKB).
/// </summary>
public enum EstadoArchivo { Actualizado, Desactualizado, Faltante, Error, SinRevisar }

/// <summary>El estado de la ultima revision conocida de un archivo del catalogo de confianza.</summary>
public sealed record ArchivoRevisado(
    int ArchivoId,
    string Ruta,
    string NombreArchivo,
    DateOnly? Fecha,
    EstadoArchivo Estado,
    DateTimeOffset? UltimaModificacion,
    string? Detalle,
    DateTimeOffset? RevisadoEn);
