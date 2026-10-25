using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SoporteBlunk.Application.Features.Utilerias.Commands.GuardarDatosMicrosip;
using SoporteBlunk.Application.Features.Utilerias.Queries.Archivos;
using SoporteBlunk.Application.Features.Utilerias.Queries.Jobs;
using SoporteBlunk.Application.Features.Utilerias.Queries.Microsip;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/utilerias")]
public sealed class UtileriasController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Estado de los jobs de SQL Server Agent (último resultado de cada job y de cada paso). Solo lectura.
    /// Por defecto solo los vigilados (Utilerias:JobsVigilados); <c>soloVigilados=false</c> lista todos.
    /// </summary>
    [HttpGet("jobs")]
    [ProducesResponseType<JobsResultado>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Jobs([FromQuery] bool soloVigilados = true, CancellationToken ct = default) =>
        Ok(await sender.Send(new ListJobsQuery(soloVigilados), ct));

    /// <summary>Usuarios de MICROSIP (tipo U) con su clave de FORTIA, empresa y puesto.</summary>
    [HttpGet("microsip/usuarios")]
    [ProducesResponseType<IReadOnlyList<UsuarioMicrosip>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UsuariosMicrosip(CancellationToken ct) =>
        Ok(await sender.Send(new ListUsuariosMicrosipQuery(), ct));

    /// <summary>Trabajador de FORTIA por clave (nombre, empresa, puesto). 404 si no existe.</summary>
    [HttpGet("microsip/trabajadores/{clave:int:min(1)}")]
    [ProducesResponseType<TrabajadorFortia>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TrabajadorFortia(int clave, CancellationToken ct) =>
        Ok(await sender.Send(new GetTrabajadorFortiaQuery(clave), ct));

    /// <summary>
    /// Asigna a un usuario de MICROSIP la clave de FORTIA; nombre, empresa y puesto se toman de FORTIA en el servidor.
    /// Es la única escritura sobre MICROSIP y queda en la bitácora (quién, antes y después).
    /// </summary>
    [HttpPut("microsip/usuarios/{usuarioId:int:min(1)}/datos-adicionales")]
    [EnableRateLimiting("escritura")]
    [ProducesResponseType<UsuarioMicrosip>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GuardarDatosAdicionales(int usuarioId, [FromBody] GuardarDatosAdicionalesRequest body, CancellationToken ct) =>
        Ok(await sender.Send(new GuardarDatosMicrosipCommand(usuarioId, body.ClaveFortia), ct));

    /// <summary>Bitácora de los cambios hechos en MICROSIP desde la consola.</summary>
    [HttpGet("microsip/cambios")]
    [ProducesResponseType<IReadOnlyList<CambioMicrosip>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cambios([FromQuery] int maximo = 50, CancellationToken ct = default) =>
        Ok(await sender.Send(new ListCambiosMicrosipQuery(maximo), ct));

    /// <summary>
    /// Catálogo de archivos de confianza (SIA/repbi/olap) con el estado de su última revisión
    /// conocida. Lo llena un script externo que corre en el 10.0.3.8; aquí solo se lee.
    /// </summary>
    [HttpGet("archivos")]
    [ProducesResponseType<IReadOnlyList<ArchivoRevisado>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Archivos(CancellationToken ct) =>
        Ok(await sender.Send(new ListArchivosQuery(), ct));
}

public sealed record GuardarDatosAdicionalesRequest(int ClaveFortia);
