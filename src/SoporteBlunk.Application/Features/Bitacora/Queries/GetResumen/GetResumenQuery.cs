using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Bitacora.Queries.GetResumen;

public sealed record GetResumenQuery(string Grupo, DateOnly Desde, DateOnly Hasta) : IRequest<IReadOnlyList<ResumenDia>>;

public sealed class GetResumenValidator : AbstractValidator<GetResumenQuery>
{
    public GetResumenValidator()
    {
        RuleFor(x => x.Grupo).Must(BitacoraGrupos.EsValido).WithMessage("Grupo de bitácora no reconocido.");
        RuleFor(x => x).Must(x => x.Desde <= x.Hasta).WithMessage("'desde' no puede ser posterior a 'hasta'.");
        RuleFor(x => x).Must(x => x.Hasta.DayNumber - x.Desde.DayNumber <= 366)
            .WithMessage("El rango no puede superar un año.");
    }
}

public sealed class GetResumenHandler(IBitacoraRepository repo) : IRequestHandler<GetResumenQuery, IReadOnlyList<ResumenDia>>
{
    public Task<IReadOnlyList<ResumenDia>> Handle(GetResumenQuery request, CancellationToken ct) =>
        repo.ObtenerResumenAsync(request.Grupo, request.Desde, request.Hasta, ct);
}
