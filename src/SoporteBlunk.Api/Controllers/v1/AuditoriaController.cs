using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SoporteBlunk.Application.Features.Auditoria.Queries.ListEventosAuditoria;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Controllers.v1;

/// <summary>Línea de tiempo unificada: quién hizo qué en Conocimiento, MICROSIP y Bitácora.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/auditoria")]
public sealed class AuditoriaController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EventoAuditoria>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int maximo = 100, CancellationToken ct = default) =>
        Ok(await sender.Send(new ListEventosAuditoriaQuery(maximo), ct));
}
