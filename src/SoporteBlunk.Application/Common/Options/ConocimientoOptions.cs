namespace SoporteBlunk.Application.Common.Options;

/// <summary>Límites de la subida de notas. Se aplican siempre en el servidor, aunque el navegador diga otra cosa.</summary>
public sealed class ConocimientoOptions
{
    public const string Section = "Conocimiento";

    public int MaxArchivos { get; set; } = 500;
    public int MaxBytesPorArchivo { get; set; } = 1_048_576; // 1 MB
    public int MaxBytesFrontmatter { get; set; } = 32_768;
    public int MaxEtiquetas { get; set; } = 50;
    public int MaxEnlaces { get; set; } = 1000;

    /// <summary>Tamaño máximo de la petición de subida (archivos comprimidos incluidos).</summary>
    public long MaxBytesPeticion { get; set; } = 60L * 1024 * 1024;

    /// <summary>Tope de bytes que se aceptan al descomprimir todos los zips de una subida (anti bomba de compresión).</summary>
    public long MaxBytesDescomprimidos { get; set; } = 200L * 1024 * 1024;

    public int MaxEntradasZip { get; set; } = 2000;
}
