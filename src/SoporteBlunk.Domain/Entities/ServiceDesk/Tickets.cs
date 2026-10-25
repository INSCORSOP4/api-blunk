namespace SoporteBlunk.Domain.Entities.ServiceDesk;

// Tablas de solicitudes de ServiceDesk Plus (esquema de terceros, solo lectura). Los tiempos son epoch en ms UTC; 0 = sin dato.
// Las columnas que no son clave se declaran anulables a propósito: si el producto trae un NULL, la consulta no se cae.

/// <summary>WorkOrder: la solicitud.</summary>
public sealed class WorkOrder
{
    public long Id { get; init; }
    public string? Titulo { get; init; }
    public long? SolicitanteId { get; init; }
    public long? ModoId { get; init; }
    public long? DepartamentoId { get; init; }
    public long? Creado { get; init; }
    public long? Vence { get; init; }
    public long? Completado { get; init; }
    public long? Respondido { get; init; }
    public long? Resuelto { get; init; }
}

/// <summary>WorkOrderStates: estado actual de la solicitud (una fila por solicitud).</summary>
public sealed class WorkOrderStates
{
    public long TicketId { get; init; }
    public long? EstadoId { get; init; }
    public long? PrioridadId { get; init; }
    public long? CategoriaId { get; init; }
    public long? SubcategoriaId { get; init; }
    public long? ItemId { get; init; }
    public long? UrgenciaId { get; init; }
    public long? ImpactoId { get; init; }
    public long? TipoSolicitudId { get; init; }
    public long? NivelId { get; init; }
    public long? TecnicoId { get; init; }
    /// <summary>ASSIGNEDTIME: cuándo quedó asignada a su técnico actual (epoch ms; 0 = sin dato).</summary>
    public long? Asignado { get; init; }
    public bool? Vencido { get; init; }
    public bool? Reabierto { get; init; }
}

/// <summary>WorkOrder_Queue: grupo asignado a la solicitud.</summary>
public sealed class WorkOrderQueue
{
    public long TicketId { get; init; }
    public long? GrupoId { get; init; }
}

/// <summary>WorkOrderToDescription: descripción completa de la solicitud.</summary>
public sealed class WorkOrderToDescription
{
    public long TicketId { get; init; }
    public string? Descripcion { get; init; }
}

/// <summary>RequestResolution: resoluciones registradas (la vigente es la de LASTUPDATEDTIME más reciente).</summary>
public sealed class RequestResolution
{
    public long TicketId { get; init; }
    public string? Resolucion { get; init; }
    public long? Actualizado { get; init; }
}

/// <summary>WorkOrderHistory: bitácora de operaciones de la solicitud.</summary>
public sealed class WorkOrderHistory
{
    public long Id { get; init; }
    public long TicketId { get; init; }
    public long? Fecha { get; init; }
    public string? Operacion { get; init; }
    public string? Descripcion { get; init; }
    public long? UsuarioId { get; init; }
}

/// <summary>WO_Assessment: corte de evaluación; cuelgan de él los tiempos por estado, técnico y grupo.</summary>
public sealed class WoAssessment
{
    public long Id { get; init; }
    public long TicketId { get; init; }
    public long? Ejecutado { get; init; }
}

/// <summary>WO_Status_Info: tiempo registrado por estado.</summary>
public sealed class WoStatusInfo
{
    public long AssessmentId { get; init; }
    public long? EstadoId { get; init; }
    public long? Fin { get; init; }
    public long? TiempoRegistrado { get; init; }
}

/// <summary>WO_Tech_Info: tiempo registrado por técnico.</summary>
public sealed class WoTechInfo
{
    public long AssessmentId { get; init; }
    public long? TecnicoId { get; init; }
    public long? Fin { get; init; }
    public long? TiempoRegistrado { get; init; }
}

/// <summary>WO_Group_Info: tiempo registrado por grupo.</summary>
public sealed class WoGroupInfo
{
    public long AssessmentId { get; init; }
    public long? GrupoId { get; init; }
    public long? Fin { get; init; }
    public long? TiempoRegistrado { get; init; }
}

/// <summary>AaaUser: usuarios (técnicos y solicitantes).</summary>
public sealed class AaaUser
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public string? Apellido { get; init; }
}
