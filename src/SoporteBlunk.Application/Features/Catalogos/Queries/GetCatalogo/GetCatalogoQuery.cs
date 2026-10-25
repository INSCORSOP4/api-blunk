using MediatR;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Catalogos.Queries.GetCatalogo;

public sealed record GetCatalogoQuery(string Catalogo) : IRequest<IReadOnlyList<CatalogoItem>>;

public sealed class GetCatalogoHandler(ICatalogoReadRepository repo)
    : IRequestHandler<GetCatalogoQuery, IReadOnlyList<CatalogoItem>>
{
    public static readonly IReadOnlySet<string> Permitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "estados", "prioridades", "grupos", "categorias", "subcategorias", "items", "urgencias",
        "impactos", "modos", "tipos-solicitud", "niveles", "departamentos",
    };

    public Task<IReadOnlyList<CatalogoItem>> Handle(GetCatalogoQuery request, CancellationToken ct) =>
        Permitidos.Contains(request.Catalogo)
            ? repo.ObtenerAsync(request.Catalogo, ct)
            : throw new NotFoundException("el catálogo", request.Catalogo);
}
