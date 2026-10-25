using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Bitacora.Queries.ListActividades;

public sealed record ListActividadesQuery(string Grupo, bool IncluirInactivas) : IRequest<IReadOnlyList<ActividadBitacora>>;

public sealed class ListActividadesValidator : AbstractValidator<ListActividadesQuery>
{
    public ListActividadesValidator() =>
        RuleFor(x => x.Grupo).Must(BitacoraGrupos.EsValido).WithMessage("Grupo de bitácora no reconocido.");
}

public sealed class ListActividadesHandler(IBitacoraRepository repo)
    : IRequestHandler<ListActividadesQuery, IReadOnlyList<ActividadBitacora>>
{
    public Task<IReadOnlyList<ActividadBitacora>> Handle(ListActividadesQuery request, CancellationToken ct) =>
        repo.ListarActividadesAsync(request.Grupo, request.IncluirInactivas, ct);
}
