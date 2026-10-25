namespace SoporteBlunk.Domain.Entities.Kb;

// Tablas del esquema archivos. Las llena un script externo (la API solo las lee).

/// <summary>archivos.ArchivoEsperado: catálogo de archivos de confianza que deben llegar por red.</summary>
public sealed class ArchivoEsperado
{
    public int Id { get; init; }
    public string Ruta { get; init; } = string.Empty;
    public string NombreArchivo { get; init; } = string.Empty;
    public bool Activo { get; init; }
}

/// <summary>archivos.RevisionArchivo: resultado de la revisión de un archivo en un día (PK: Fecha + Archivo).</summary>
public sealed class RevisionArchivo
{
    public DateOnly Fecha { get; init; }
    public int ArchivoId { get; init; }
    public string Estado { get; init; } = string.Empty;
    public DateTime? UltimaModificacion { get; init; }
    public string? Detalle { get; init; }
    public DateTime RevisadoEn { get; init; }
}
