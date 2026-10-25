using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Domain.Entities.Kb;

// Tablas propias de SoporteKB (esquema bitacora). Las fechas "...En" son datetime2 en hora local del servidor, sin zona; las
// llena la base (SYSDATETIME()) y la aplicación las interpreta como hora de México.

/// <summary>bitacora.Actividad: una fila del checklist de una bitácora (por Grupo).</summary>
public sealed class BitacoraActividad
{
    public int Id { get; init; }
    public string Grupo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public int Orden { get; init; }
    public bool Activa { get; init; }
    public DateTime CreadoEn { get; init; }
    public string CreadoPor { get; init; } = string.Empty;
    public DateTime? ActualizadoEn { get; init; }
    public string? ActualizadoPor { get; init; }
}

/// <summary>bitacora.ChecklistDiario: el estado de una actividad en un día (PK: Fecha + Actividad).</summary>
public sealed class BitacoraChecklist
{
    public DateOnly Fecha { get; init; }
    public int ActividadId { get; init; }
    public EstadoChecklist Estado { get; init; }
    public string? Observacion { get; init; }
    public DateTime CreadoEn { get; init; }
    public string CreadoPor { get; init; } = string.Empty;
    public DateTime? ActualizadoEn { get; init; }
    public string? ActualizadoPor { get; init; }
}
