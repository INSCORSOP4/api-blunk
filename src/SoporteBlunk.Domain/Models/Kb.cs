namespace SoporteBlunk.Domain.Models;

/// <summary>Estado de la base de conocimiento (SoporteKB).</summary>
public sealed record KbEstado(int VersionEsquema, bool TextoCompleto, int Documentos);

public enum OrdenDocumentos { Recientes, Titulo }

public sealed record DocumentoFiltro
{
    /// <summary>Texto libre: cada palabra debe aparecer en el título, el contenido, un alias o una etiqueta (sin distinguir acentos ni mayúsculas).</summary>
    public string? Buscar { get; init; }

    /// <summary>Carpeta exacta; incluye sus subcarpetas.</summary>
    public string? Carpeta { get; init; }

    public string? Etiqueta { get; init; }
    public OrdenDocumentos Orden { get; init; } = OrdenDocumentos.Recientes;
}

public sealed record DocumentoResumen(
    int Id,
    string Ruta,
    string Titulo,
    string Carpeta,
    string? Resumen,
    string? Estado,
    IReadOnlyList<string> Etiquetas,
    DateTimeOffset ActualizadoEn,
    string ActualizadoPor,
    int SecretosEnmascarados,
    string? Fragmento);

public sealed record DocumentoRelacionado(int Id, string Titulo, string Ruta, string Carpeta);

public sealed record EnlaceDocumento(string Destino, string? Seccion, string? TextoVisible, DocumentoRelacionado? Resuelto);

public sealed record VersionDocumento(int Version, DateTimeOffset Fecha, string Usuario);

public sealed record DocumentoDetalle(
    int Id,
    string Ruta,
    string Titulo,
    string Carpeta,
    string? Resumen,
    string? Estado,
    string ContenidoMd,
    IReadOnlyList<string> Etiquetas,
    IReadOnlyList<string> Alias,
    IReadOnlyList<EnlaceDocumento> Enlaces,
    IReadOnlyList<DocumentoRelacionado> EnlazadoDesde,
    IReadOnlyList<VersionDocumento> Versiones,
    int Version,
    int SecretosEnmascarados,
    DateTimeOffset CreadoEn,
    string CreadoPor,
    DateTimeOffset ActualizadoEn,
    string ActualizadoPor);

public sealed record CarpetaConteo(string Carpeta, int Documentos);

public sealed record EtiquetaConteo(string Nombre, int Documentos);
