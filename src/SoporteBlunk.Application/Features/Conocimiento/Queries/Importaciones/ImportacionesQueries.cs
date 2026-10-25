using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;

namespace SoporteBlunk.Application.Features.Conocimiento.Queries.Importaciones;

/// <summary>Bitácora de quién subió notas y cuándo (las más recientes primero).</summary>
public sealed record ListImportacionesQuery(int Maximo = 50) : IRequest<IReadOnlyList<ImportacionResumen>>;

public sealed class ListImportacionesValidator : AbstractValidator<ListImportacionesQuery>
{
    public ListImportacionesValidator() => RuleFor(x => x.Maximo).InclusiveBetween(1, 200);
}

public sealed class ListImportacionesHandler(IKbImportacionRepository repo)
    : IRequestHandler<ListImportacionesQuery, IReadOnlyList<ImportacionResumen>>
{
    public Task<IReadOnlyList<ImportacionResumen>> Handle(ListImportacionesQuery r, CancellationToken ct) => repo.ListarAsync(r.Maximo, ct);
}

public sealed record GetImportacionQuery(int Id) : IRequest<ImportacionDetalle>;

public sealed class GetImportacionValidator : AbstractValidator<GetImportacionQuery>
{
    public GetImportacionValidator() => RuleFor(x => x.Id).GreaterThan(0);
}

public sealed class GetImportacionHandler(IKbImportacionRepository repo) : IRequestHandler<GetImportacionQuery, ImportacionDetalle>
{
    public async Task<ImportacionDetalle> Handle(GetImportacionQuery r, CancellationToken ct) =>
        await repo.ObtenerAsync(r.Id, ct) ?? throw new NotFoundException("la importación", r.Id);
}
