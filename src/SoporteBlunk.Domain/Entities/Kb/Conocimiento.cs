namespace SoporteBlunk.Domain.Entities.Kb;

// Tablas del esquema kb (base de conocimiento). Las fechas son datetime2 en UTC y las llena la base (SYSUTCDATETIME()).

/// <summary>kb.VersionEsquema: versión del esquema aplicada a la base (la última es la vigente).</summary>
public sealed class KbVersionEsquema
{
    public int Version { get; init; }
    public string Descripcion { get; init; } = string.Empty;
    public DateTime AplicadaEn { get; init; }
}

/// <summary>kb.Importacion: bitácora de cada importación (quién, cuándo, resultado e informe en JSON).</summary>
public sealed class KbImportacion
{
    public int Id { get; init; }
    public Guid UsuarioId { get; init; }
    public string UsuarioNombre { get; init; } = string.Empty;
    public string UsuarioEmail { get; init; } = string.Empty;
    public string Origen { get; init; } = string.Empty;
    public string Estado { get; init; } = EstadosImportacion.EnCurso;
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
}

public static class EstadosImportacion
{
    public const string EnCurso = "EnCurso";
    public const string Completada = "Completada";
    public const string Fallida = "Fallida";
}

/// <summary>kb.Documento: una nota. Se borra de forma lógica (<c>Eliminado</c>) y conserva su historial en kb.DocumentoVersion.</summary>
public sealed class KbDocumento
{
    public int Id { get; init; }
    public string RutaRelativa { get; init; } = string.Empty;
    public string Titulo { get; init; } = string.Empty;
    public string Carpeta { get; init; } = string.Empty;
    public string? Resumen { get; init; }
    public string? Estado { get; init; }
    public string ContenidoMd { get; init; } = string.Empty;
    public string ContenidoTexto { get; init; } = string.Empty;
    /// <summary>SHA-256 del contenido de origen (binary(32)).</summary>
    public byte[] HashOrigen { get; init; } = [];
    public int SecretosEnmascarados { get; init; }
    public DateTime? ModificadoEnOrigen { get; init; }
    public int Version { get; init; }
    public bool Eliminado { get; init; }
    public DateTime? EliminadoEn { get; init; }
    public DateTime CreadoEn { get; init; }
    public Guid CreadoPorId { get; init; }
    public string CreadoPorNombre { get; init; } = string.Empty;
    public DateTime ActualizadoEn { get; init; }
    public Guid ActualizadoPorId { get; init; }
    public string ActualizadoPorNombre { get; init; } = string.Empty;
}

/// <summary>kb.DocumentoVersion: una versión anterior de una nota, con quién la subió y desde qué importación.</summary>
public sealed class KbDocumentoVersion
{
    public long Id { get; init; }
    public int DocumentoId { get; init; }
    public int Version { get; init; }
    public string ContenidoMd { get; init; } = string.Empty;
    public byte[] HashOrigen { get; init; } = [];
    public int? ImportacionId { get; init; }
    public Guid UsuarioId { get; init; }
    public string UsuarioNombre { get; init; } = string.Empty;
    public DateTime CreadoEn { get; init; }
}

/// <summary>kb.DocumentoAlias: otros nombres con los que se puede enlazar o buscar una nota.</summary>
public sealed class KbDocumentoAlias
{
    public int DocumentoId { get; init; }
    public string Alias { get; init; } = string.Empty;
}

/// <summary>kb.Etiqueta: etiqueta (sin "#").</summary>
public sealed class KbEtiqueta
{
    public int Id { get; init; }
    public string Nombre { get; init; } = string.Empty;
}

/// <summary>kb.DocumentoEtiqueta: relación nota–etiqueta.</summary>
public sealed class KbDocumentoEtiqueta
{
    public int DocumentoId { get; init; }
    public int EtiquetaId { get; init; }
}

/// <summary>kb.Enlace: enlace entre notas. <c>DocumentoDestinoId</c> es nulo mientras el destino no existe (enlace roto).</summary>
public sealed class KbEnlace
{
    public int Id { get; init; }
    public int DocumentoOrigenId { get; init; }
    public string Destino { get; init; } = string.Empty;
    public string? Seccion { get; init; }
    public string? TextoVisible { get; init; }
    public int? DocumentoDestinoId { get; init; }
}

/// <summary>kb.ReglaOmision: carpetas y archivos que la importación ignora.</summary>
public sealed class KbReglaOmision
{
    public int Id { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public string Patron { get; init; } = string.Empty;
    public string? Motivo { get; init; }
    public bool Activa { get; init; }
}
