using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Domain.Interfaces;

public interface ICatalogoReadRepository
{
    Task<IReadOnlyList<CatalogoItem>> ObtenerAsync(string catalogo, CancellationToken ct);
}

public interface ITicketReadRepository
{
    Task<PagedResult<TicketResumen>> ListarAsync(TicketFiltro filtro, TicketOrden orden, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<TicketResumen>> ListarParaExportAsync(TicketFiltro filtro, int maximo, CancellationToken ct);
    Task<TicketDetalle?> ObtenerAsync(long id, CancellationToken ct);
    Task<bool> ExisteAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<TicketHistorialItem>> HistorialAsync(long id, CancellationToken ct);
    Task<TicketTiempos> TiemposAsync(long id, CancellationToken ct);
}

public interface IDashboardReadRepository
{
    Task<DashboardResumen> ResumenAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct);
    Task<IReadOnlyList<TendenciaPunto>> TendenciaAsync(DateOnly desde, DateOnly hasta, Agrupacion agrupacion, CancellationToken ct);
    Task<IReadOnlyList<ConteoPorClave>> PorAsync(string dimension, DateOnly? desde, DateOnly? hasta, CancellationToken ct);
    Task<SlaResumen> SlaAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct);
}

public interface ITecnicoReadRepository
{
    Task<IReadOnlyList<Tecnico>> ListarAsync(CancellationToken ct);
    Task<IReadOnlyList<TecnicoCarga>> CargaAsync(CancellationToken ct);
    Task<TecnicoCarga?> CargaAsync(long tecnicoId, CancellationToken ct);
    Task<TecnicoProductividad?> ProductividadAsync(long tecnicoId, DateOnly? desde, DateOnly? hasta, CancellationToken ct);

    /// <summary>Productividad de todos los técnicos con tickets asignados en el periodo (para comparar al equipo).</summary>
    Task<IReadOnlyList<TecnicoProductividad>> ListarProductividadAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct);

    /// <summary>Productividad de un técnico por periodo (día/semana/mes), para ver su evolución en el tiempo.</summary>
    Task<IReadOnlyList<ProductividadPunto>> TendenciaProductividadAsync(
        long tecnicoId, DateOnly desde, DateOnly hasta, Agrupacion agrupacion, CancellationToken ct);
}
