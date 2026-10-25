using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SoporteBlunk.Application.Features.Dashboard.Queries;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/dashboard")]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    [HttpGet("resumen")]
    [ProducesResponseType<DashboardResumen>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resumen([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) =>
        Ok(await sender.Send(new GetDashboardResumenQuery(desde, hasta), ct));

    /// <summary>Creados vs. cerrados por periodo. Por defecto, los últimos 30 días.</summary>
    [HttpGet("tendencia")]
    [ProducesResponseType<IReadOnlyList<TendenciaPunto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tendencia(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        [FromQuery] Agrupacion agrupacion = Agrupacion.Dia, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetDashboardTendenciaQuery(desde, hasta, agrupacion), ct));

    [HttpGet("por-categoria")]
    public Task<IActionResult> PorCategoria([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) => Por("categoria", desde, hasta, ct);

    [HttpGet("por-prioridad")]
    public Task<IActionResult> PorPrioridad([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) => Por("prioridad", desde, hasta, ct);

    [HttpGet("por-modo")]
    public Task<IActionResult> PorModo([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) => Por("modo", desde, hasta, ct);

    [HttpGet("por-grupo")]
    public Task<IActionResult> PorGrupo([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) => Por("grupo", desde, hasta, ct);

    [HttpGet("por-tecnico")]
    public Task<IActionResult> PorTecnico([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) => Por("tecnico", desde, hasta, ct);

    [HttpGet("por-urgencia")]
    public Task<IActionResult> PorUrgencia([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) => Por("urgencia", desde, hasta, ct);

    [HttpGet("por-tipo-solicitud")]
    public Task<IActionResult> PorTipoSolicitud([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) => Por("tipo-solicitud", desde, hasta, ct);

    [HttpGet("sla")]
    [ProducesResponseType<SlaResumen>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Sla([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) =>
        Ok(await sender.Send(new GetDashboardSlaQuery(desde, hasta), ct));

    private async Task<IActionResult> Por(string dimension, DateOnly? desde, DateOnly? hasta, CancellationToken ct) =>
        Ok(await sender.Send(new GetDashboardPorQuery(dimension, desde, hasta), ct));
}
