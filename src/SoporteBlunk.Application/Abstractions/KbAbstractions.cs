using SoporteBlunk.Application.Conocimiento;

namespace SoporteBlunk.Application.Abstractions;

/// <summary>Quién está haciendo la petición (sale del token de sesión).</summary>
public sealed record UsuarioActual(Guid Id, string Nombre, string Email);

public interface IUsuarioActualProvider
{
    /// <exception cref="UnauthorizedAccessException">Si no hay una sesión válida.</exception>
    UsuarioActual Obtener();
}

public sealed record ContextoImportacion(UsuarioActual Usuario, string Origen);

public enum ResultadoNota { Nueva, Actualizada, SinCambios }

public sealed record NotaGuardada(string Ruta, ResultadoNota Resultado, int EnlacesRotos);

public sealed record ResumenArchivos(int Total, int Omitidos, int Errores);

public sealed record ResultadoImportacion(int ImportacionId, IReadOnlyList<NotaGuardada> Notas);

public sealed record ImportacionResumen(
    int ImportacionId,
    string UsuarioNombre,
    string UsuarioEmail,
    string Origen,
    string Estado,
    DateTimeOffset IniciadaEn,
    DateTimeOffset? TerminadaEn,
    int Archivos,
    int Nuevos,
    int Actualizados,
    int SinCambios,
    int Omitidos,
    int ConSecretos,
    int Errores);

public sealed record ImportacionDetalle(ImportacionResumen Resumen, string? InformeJson);

/// <summary>Persistencia de las importaciones de notas en SoporteKB.</summary>
public interface IKbImportacionRepository
{
    Task<IReadOnlyList<ReglaOmision>> ReglasOmisionAsync(CancellationToken ct);

    /// <summary>
    /// Guarda las notas en una sola transacción (alta, actualización o sin cambios según el hash), registra el historial,
    /// resuelve los enlaces entre notas y deja constancia de quién importó. Si algo falla no queda nada a medias.
    /// </summary>
    Task<ResultadoImportacion> GuardarAsync(
        ContextoImportacion contexto,
        IReadOnlyList<NotaProcesada> notas,
        ResumenArchivos resumen,
        Func<IReadOnlyList<NotaGuardada>, string> construirInforme,
        CancellationToken ct);

    Task<IReadOnlyList<ImportacionResumen>> ListarAsync(int maximo, CancellationToken ct);

    Task<ImportacionDetalle?> ObtenerAsync(int importacionId, CancellationToken ct);
}
