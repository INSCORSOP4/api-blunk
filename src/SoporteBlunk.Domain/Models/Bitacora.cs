namespace SoporteBlunk.Domain.Models;

public enum EstadoChecklist { Ok, Advertencia, Falla, Critico, NoAplica }

public sealed record ActividadBitacora(
    int ActividadId,
    string Grupo,
    string Nombre,
    int Orden,
    bool Activa);

/// <summary>
/// Una actividad dentro del checklist de un día. <c>Estado</c> es null cuando todavía no se registró
/// ese día (pendiente); nunca es null una vez guardado una vez (el registro ya no se borra, se corrige).
/// </summary>
public sealed record ItemChecklist(
    int ActividadId,
    string Nombre,
    int Orden,
    bool ActividadActiva,
    EstadoChecklist? Estado,
    string? Observacion,
    string? RevisadoPor,
    DateTimeOffset? RevisadoEn,
    string? ActualizadoPor,
    DateTimeOffset? ActualizadoEn);

public sealed record ChecklistDia(string Grupo, DateOnly Fecha, IReadOnlyList<ItemChecklist> Items);

/// <summary>Conteo por estado de un día, para el calendario/resumen.</summary>
public sealed record ResumenDia(DateOnly Fecha, int Ok, int Advertencia, int Falla, int Critico, int NoAplica, int Pendiente, int Total);

/// <summary>El último guardado de una actividad del checklist (para la auditoría unificada).</summary>
public sealed record CambioChecklist(
    string Grupo,
    string ActividadNombre,
    DateOnly Fecha,
    EstadoChecklist Estado,
    string? Observacion,
    string Usuario,
    DateTimeOffset Cuando);
