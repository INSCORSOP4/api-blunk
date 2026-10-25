using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SoporteBlunk.Application.Features.Bitacora.Commands.ActualizarActividad;
using SoporteBlunk.Application.Features.Bitacora.Commands.CrearActividad;
using SoporteBlunk.Application.Features.Bitacora.Commands.GuardarChecklist;
using SoporteBlunk.Application.Features.Bitacora.Queries.GetChecklist;
using SoporteBlunk.Application.Features.Bitacora.Queries.GetResumen;
using SoporteBlunk.Application.Features.Bitacora.Queries.ListActividades;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Controllers.v1;

/// <summary>
/// Bitácora de actividades de Soporte (reemplazo del Excel). "Grupo" identifica la bitácora; hoy solo existe
/// "actividades-soft". Es la única sección de la API, junto con Conocimiento y Utilerías/MICROSIP, que escribe.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/bitacora")]
public sealed class BitacoraController(ISender sender) : ControllerBase
{
    [HttpGet("actividades")]
    [ProducesResponseType<IReadOnlyList<ActividadBitacora>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarActividades(
        [FromQuery] string grupo, [FromQuery] bool incluirInactivas = false, CancellationToken ct = default) =>
        Ok(await sender.Send(new ListActividadesQuery(grupo, incluirInactivas), ct));

    [HttpPost("actividades")]
    [EnableRateLimiting("escritura")]
    [ProducesResponseType<ActividadBitacora>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CrearActividad([FromBody] CrearActividadRequest body, CancellationToken ct) =>
        Ok(await sender.Send(new CrearActividadCommand(body.Grupo, body.Nombre), ct));

    [HttpPut("actividades/{actividadId:int:min(1)}")]
    [EnableRateLimiting("escritura")]
    [ProducesResponseType<ActividadBitacora>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ActualizarActividad(
        int actividadId, [FromBody] ActualizarActividadRequest body, CancellationToken ct) =>
        Ok(await sender.Send(new ActualizarActividadCommand(actividadId, body.Nombre, body.Activa, body.Orden), ct));

    [HttpGet("checklist")]
    [ProducesResponseType<ChecklistDia>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ObtenerChecklist([FromQuery] string grupo, [FromQuery] DateOnly fecha, CancellationToken ct) =>
        Ok(await sender.Send(new GetChecklistQuery(grupo, fecha), ct));

    [HttpPut("checklist/{actividadId:int:min(1)}")]
    [EnableRateLimiting("escritura")]
    [ProducesResponseType<ItemChecklist>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GuardarChecklist(
        int actividadId, [FromQuery] DateOnly fecha, [FromBody] GuardarChecklistRequest body, CancellationToken ct) =>
        Ok(await sender.Send(new GuardarChecklistCommand(actividadId, fecha, body.Estado, body.Observacion), ct));

    [HttpGet("resumen")]
    [ProducesResponseType<IReadOnlyList<ResumenDia>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ObtenerResumen(
        [FromQuery] string grupo, [FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, CancellationToken ct) =>
        Ok(await sender.Send(new GetResumenQuery(grupo, desde, hasta), ct));
}

public sealed record CrearActividadRequest(string Grupo, string Nombre);
public sealed record ActualizarActividadRequest(string? Nombre, bool? Activa, int? Orden);
public sealed record GuardarChecklistRequest(EstadoChecklist Estado, string? Observacion);
