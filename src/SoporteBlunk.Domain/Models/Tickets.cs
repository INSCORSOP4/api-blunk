namespace SoporteBlunk.Domain.Models;

public sealed record TicketFiltro
{
    public long? EstadoId { get; init; }
    public long? TecnicoId { get; init; }
    public long? GrupoId { get; init; }
    public long? CategoriaId { get; init; }
    public long? PrioridadId { get; init; }
    public DateOnly? Desde { get; init; }
    public DateOnly? Hasta { get; init; }
    public bool? Vencido { get; init; }
    /// <summary>true: abiertos sin primera respuesta todavía. false: ya respondidos (o cerrados).</summary>
    public bool? SinResponder { get; init; }
    public string? Buscar { get; init; }
}

public enum TicketOrden { CreadoDesc, CreadoAsc, VenceAsc, IdDesc }

public sealed record TicketResumen(
    long Id,
    string Titulo,
    long EstadoId,
    string Estado,
    string? Prioridad,
    string? Categoria,
    string? Grupo,
    long? TecnicoId,
    string? Tecnico,
    string? Solicitante,
    bool Vencido,
    DateTimeOffset? Creado,
    DateTimeOffset? Vence,
    DateTimeOffset? Completado);

public sealed record TicketDetalle(
    TicketResumen Resumen,
    string? Descripcion,
    string? Resolucion,
    string? Subcategoria,
    string? Item,
    string? Urgencia,
    string? Impacto,
    string? Modo,
    string? TipoSolicitud,
    string? Nivel,
    string? Departamento,
    bool Reabierto,
    DateTimeOffset? Respondido,
    DateTimeOffset? Resuelto);

public sealed record TicketHistorialItem(
    long Id,
    DateTimeOffset? Fecha,
    string? Operacion,
    string? Descripcion,
    string? Usuario);

// TiempoRegistrado es TIMESPENT tal cual lo guarda ServiceDesk; su unidad no está confirmada (no es ms).
public sealed record TicketTiempoEstado(string Estado, DateTimeOffset? Fin, long TiempoRegistrado);
public sealed record TicketTiempoTecnico(string? Tecnico, DateTimeOffset? Fin, long TiempoRegistrado);
public sealed record TicketTiempoGrupo(string? Grupo, DateTimeOffset? Fin, long TiempoRegistrado);

public sealed record TicketTiempos(
    IReadOnlyList<TicketTiempoEstado> PorEstado,
    IReadOnlyList<TicketTiempoTecnico> PorTecnico,
    IReadOnlyList<TicketTiempoGrupo> PorGrupo);
