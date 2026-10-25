using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Bitacora.Queries.GetChecklist;

public sealed record GetChecklistQuery(string Grupo, DateOnly Fecha) : IRequest<ChecklistDia>;

public sealed class GetChecklistValidator : AbstractValidator<GetChecklistQuery>
{
    public GetChecklistValidator()
    {
        RuleFor(x => x.Grupo).Must(BitacoraGrupos.EsValido).WithMessage("Grupo de bitácora no reconocido.");
        RuleFor(x => x.Fecha).LessThanOrEqualTo(_ => ZonaHoraria.Hoy()).WithMessage("La fecha no puede ser futura.")
            .GreaterThanOrEqualTo(new DateOnly(2020, 1, 1)).WithMessage("Fecha fuera de rango.");
    }
}

public sealed class GetChecklistHandler(IBitacoraRepository repo) : IRequestHandler<GetChecklistQuery, ChecklistDia>
{
    public Task<ChecklistDia> Handle(GetChecklistQuery request, CancellationToken ct) =>
        repo.ObtenerChecklistAsync(request.Grupo, request.Fecha, ct);
}
