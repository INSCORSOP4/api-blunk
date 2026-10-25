using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Conocimiento.Queries.Documentos;

public sealed record ListDocumentosQuery(DocumentoFiltro Filtro, int Page, int PageSize) : IRequest<PagedResult<DocumentoResumen>>;

public sealed class ListDocumentosValidator : AbstractValidator<ListDocumentosQuery>
{
    public const int MaxPageSize = 100;

    public ListDocumentosValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Filtro.Buscar).MaximumLength(200);
        RuleFor(x => x.Filtro.Carpeta).MaximumLength(450);
        RuleFor(x => x.Filtro.Etiqueta).MaximumLength(100);
        RuleFor(x => x.Filtro.Orden).IsInEnum();
    }
}

public sealed class ListDocumentosHandler(IKbReadRepository repo) : IRequestHandler<ListDocumentosQuery, PagedResult<DocumentoResumen>>
{
    public Task<PagedResult<DocumentoResumen>> Handle(ListDocumentosQuery r, CancellationToken ct) =>
        repo.ListarDocumentosAsync(r.Filtro, r.Page, r.PageSize, ct);
}

public sealed record GetDocumentoQuery(int Id) : IRequest<DocumentoDetalle>;

public sealed class GetDocumentoValidator : AbstractValidator<GetDocumentoQuery>
{
    public GetDocumentoValidator() => RuleFor(x => x.Id).GreaterThan(0);
}

public sealed class GetDocumentoHandler(IKbReadRepository repo) : IRequestHandler<GetDocumentoQuery, DocumentoDetalle>
{
    public async Task<DocumentoDetalle> Handle(GetDocumentoQuery r, CancellationToken ct) =>
        await repo.ObtenerDocumentoAsync(r.Id, ct) ?? throw new NotFoundException("la nota", r.Id);
}

public sealed record ListCarpetasQuery : IRequest<IReadOnlyList<CarpetaConteo>>;

public sealed class ListCarpetasHandler(IKbReadRepository repo) : IRequestHandler<ListCarpetasQuery, IReadOnlyList<CarpetaConteo>>
{
    public Task<IReadOnlyList<CarpetaConteo>> Handle(ListCarpetasQuery r, CancellationToken ct) => repo.CarpetasAsync(ct);
}

public sealed record ListEtiquetasQuery : IRequest<IReadOnlyList<EtiquetaConteo>>;

public sealed class ListEtiquetasHandler(IKbReadRepository repo) : IRequestHandler<ListEtiquetasQuery, IReadOnlyList<EtiquetaConteo>>
{
    public Task<IReadOnlyList<EtiquetaConteo>> Handle(ListEtiquetasQuery r, CancellationToken ct) => repo.EtiquetasAsync(ct);
}
