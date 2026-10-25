namespace SoporteBlunk.Application.Common;

/// <summary>
/// Grupos de bitácora válidos. El esquema de base de datos es genérico (ver db/003_bitacora.sql); agregar una
/// bitácora nueva con la misma forma (checklist diario) es agregar su slug aquí, sin tocar el esquema.
/// </summary>
public static class BitacoraGrupos
{
    public const string ActividadesSoft = "actividades-soft";
    public const string Respaldos = "respaldos";
    public const string Camaras = "camaras";
    public const string DepuracionRespaldos = "depuracion-respaldos";

    public static readonly IReadOnlySet<string> Validos =
        new HashSet<string>(StringComparer.Ordinal) { ActividadesSoft, Respaldos, Camaras, DepuracionRespaldos };

    public static bool EsValido(string? grupo) => grupo is not null && Validos.Contains(grupo);
}
