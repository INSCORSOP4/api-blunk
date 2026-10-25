using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Dtos;

/// <summary>Filtros de tickets recibidos por query string. Las fechas son días en hora de México (hasta inclusivo).</summary>
public sealed class TicketFiltroRequest
{
    public long? EstadoId { get; init; }
    public long? TecnicoId { get; init; }
    public long? GrupoId { get; init; }
    public long? CategoriaId { get; init; }
    public long? PrioridadId { get; init; }
    public DateOnly? Desde { get; init; }
    public DateOnly? Hasta { get; init; }
    public bool? Vencido { get; init; }
    public bool? SinResponder { get; init; }
    public string? Buscar { get; init; }

    public TicketFiltro ToFiltro() => new()
    {
        EstadoId = EstadoId,
        TecnicoId = TecnicoId,
        GrupoId = GrupoId,
        CategoriaId = CategoriaId,
        PrioridadId = PrioridadId,
        Desde = Desde,
        Hasta = Hasta,
        Vencido = Vencido,
        SinResponder = SinResponder,
        Buscar = Buscar,
    };
}
