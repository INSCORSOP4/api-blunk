namespace SoporteBlunk.Application.Conocimiento;

/// <summary>Un archivo recibido en una subida. La ruta es la que envía el navegador (relativa al vault o a la carpeta elegida).</summary>
public sealed record ArchivoSubido(string Ruta, byte[] Contenido, DateTimeOffset? ModificadoEn = null);

/// <summary>Enlace de una nota hacia otra. <see cref="EsRuta"/> indica un enlace tipo Markdown con ruta relativa.</summary>
public sealed record EnlaceNota(string Destino, string? Seccion, string? TextoVisible, bool EsRuta = false);

/// <summary>Nota lista para guardar: sin secretos, con metadatos extraídos y texto plano para búsqueda.</summary>
public sealed record NotaProcesada(
    string RutaRelativa,
    string Titulo,
    string Carpeta,
    string? Resumen,
    string? Estado,
    IReadOnlyList<string> Alias,
    IReadOnlyList<string> Etiquetas,
    IReadOnlyList<EnlaceNota> Enlaces,
    string ContenidoMd,
    string ContenidoTexto,
    byte[] HashOrigen,
    int SecretosEnmascarados,
    IReadOnlyList<int> LineasConSecretos,
    DateTimeOffset? ModificadoEnOrigen,
    IReadOnlyList<string> Advertencias);

public enum EstadoArchivo { Procesado, Omitido, Error }

public sealed record ResultadoArchivo(string Ruta, EstadoArchivo Estado, string? Motivo, NotaProcesada? Nota);

public sealed record InformeProcesamiento(
    int Total,
    int Procesados,
    int Omitidos,
    int Errores,
    int ConSecretos,
    int SecretosEnmascarados,
    IReadOnlyList<ResultadoArchivo> Archivos);

/// <summary>Regla de omisión (misma forma que la tabla kb.ReglaOmision).</summary>
public sealed record ReglaOmision(string Tipo, string Patron, string? Motivo = null)
{
    public const string Carpeta = "carpeta";
    public const string Archivo = "archivo";
}
