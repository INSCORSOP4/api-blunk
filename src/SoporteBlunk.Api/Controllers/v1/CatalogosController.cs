using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using SoporteBlunk.Application.Features.Catalogos.Queries.GetCatalogo;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/catalogos")]
public sealed class CatalogosController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Catálogos: estados, prioridades, grupos, categorias, subcategorias, items, urgencias,
    /// impactos, modos, tipos-solicitud, niveles, departamentos.
    /// </summary>
    [HttpGet("{catalogo}")]
    [ProducesResponseType<IReadOnlyList<CatalogoItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string catalogo, CancellationToken ct) =>
        Ok(await sender.Send(new GetCatalogoQuery(catalogo), ct));
}
