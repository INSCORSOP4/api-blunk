using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SoporteBlunk.Api.Dtos;
using SoporteBlunk.Application.Features.Tickets.Queries.ExportTickets;
using SoporteBlunk.Application.Features.Tickets.Queries.GetTicketById;
using SoporteBlunk.Application.Features.Tickets.Queries.GetTicketHistorial;
using SoporteBlunk.Application.Features.Tickets.Queries.GetTicketTiempos;
using SoporteBlunk.Application.Features.Tickets.Queries.ListTickets;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/tickets")]
public sealed class TicketsController(ISender sender) : ControllerBase
{
    /// <summary>Lista paginada de tickets con filtros.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketResumen>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] TicketFiltroRequest filtro,
        [FromQuery] TicketOrden orden = TicketOrden.CreadoDesc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        Ok(await sender.Send(new ListTicketsQuery(filtro.ToFiltro(), orden, page, pageSize), ct));

    [HttpGet("{id:long}")]
    [ProducesResponseType<TicketDetalle>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(long id, CancellationToken ct) =>
        Ok(await sender.Send(new GetTicketByIdQuery(id), ct));

    [HttpGet("{id:long}/historial")]
    [ProducesResponseType<IReadOnlyList<TicketHistorialItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Historial(long id, CancellationToken ct) =>
        Ok(await sender.Send(new GetTicketHistorialQuery(id), ct));

    [HttpGet("{id:long}/tiempos")]
    [ProducesResponseType<TicketTiempos>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tiempos(long id, CancellationToken ct) =>
        Ok(await sender.Send(new GetTicketTiemposQuery(id), ct));

    /// <summary>Exporta a Excel los tickets que cumplen el filtro (máximo 10,000 filas).</summary>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export([FromQuery] TicketFiltroRequest filtro, CancellationToken ct)
    {
        var result = await sender.Send(new ExportTicketsQuery(filtro.ToFiltro()), ct);
        Response.Headers["X-Export-Truncated"] = result.Truncado ? "true" : "false";
        return File(result.Contenido, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.NombreArchivo);
    }
}
