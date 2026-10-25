using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SoporteBlunk.Application.Features.Tecnicos.Queries;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/tecnicos")]
public sealed class TecnicosController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<Tecnico>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await sender.Send(new ListTecnicosQuery(), ct));

    /// <summary>Carga de trabajo de todos los técnicos (abiertos, vencidos, total).</summary>
    [HttpGet("carga")]
    [ProducesResponseType<IReadOnlyList<TecnicoCarga>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Carga(CancellationToken ct) => Ok(await sender.Send(new GetCargaTecnicosQuery(), ct));

    [HttpGet("{id:long}/carga")]
    [ProducesResponseType<TecnicoCarga>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CargaDe(long id, CancellationToken ct) => Ok(await sender.Send(new GetCargaTecnicoQuery(id), ct));

    [HttpGet("{id:long}/productividad")]
    [ProducesResponseType<TecnicoProductividad>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Productividad(long id, [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) =>
        Ok(await sender.Send(new GetProductividadTecnicoQuery(id, desde, hasta), ct));

    /// <summary>Productividad de todos los técnicos con tickets asignados en el periodo (comparativa del equipo).</summary>
    [HttpGet("productividad")]
    [ProducesResponseType<IReadOnlyList<TecnicoProductividad>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ProductividadEquipo([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct) =>
        Ok(await sender.Send(new GetProductividadTecnicosQuery(desde, hasta), ct));

    /// <summary>Evolución de la productividad de un técnico por día/semana/mes (por defecto, los últimos 30 días).</summary>
    [HttpGet("{id:long}/productividad/tendencia")]
    [ProducesResponseType<IReadOnlyList<ProductividadPunto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TendenciaProductividad(
        long id, [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        [FromQuery] Agrupacion agrupacion = Agrupacion.Semana, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetTendenciaProductividadQuery(id, desde, hasta, agrupacion), ct));
}
