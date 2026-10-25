using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Domain.Interfaces;

/// <summary>
/// Bitácora de actividades de Soporte (reemplazo del Excel). Vive en SoporteKB. Lanza
/// <c>KbNoDisponibleException</c> (Application) si la base no está configurada o no responde.
/// </summary>
public interface IBitacoraRepository
{
    Task<IReadOnlyList<ActividadBitacora>> ListarActividadesAsync(string grupo, bool incluirInactivas, CancellationToken ct);

    Task<ActividadBitacora?> ObtenerActividadAsync(int actividadId, CancellationToken ct);

    Task<ActividadBitacora> CrearActividadAsync(string grupo, string nombre, string usuario, CancellationToken ct);

    /// <summary>Renombra, activa/desactiva o reordena. Los parámetros null no se tocan.</summary>
    Task<ActividadBitacora> ActualizarActividadAsync(
        int actividadId, string? nombre, bool? activa, int? orden, string usuario, CancellationToken ct);

    Task<ChecklistDia> ObtenerChecklistAsync(string grupo, DateOnly fecha, CancellationToken ct);

    Task<ItemChecklist> GuardarChecklistAsync(
        int actividadId, DateOnly fecha, EstadoChecklist estado, string? observacion, string usuario, CancellationToken ct);

    Task<IReadOnlyList<ResumenDia>> ObtenerResumenAsync(string grupo, DateOnly desde, DateOnly hasta, CancellationToken ct);

    /// <summary>Los últimos guardados de cualquier checklist (de cualquier grupo), para la auditoría unificada.</summary>
    Task<IReadOnlyList<CambioChecklist>> UltimosCambiosAsync(int maximo, CancellationToken ct);
}
