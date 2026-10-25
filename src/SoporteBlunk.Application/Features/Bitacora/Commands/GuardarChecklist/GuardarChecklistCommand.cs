using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Bitacora.Commands.GuardarChecklist;

public sealed record GuardarChecklistCommand(int ActividadId, DateOnly Fecha, EstadoChecklist Estado, string? Observacion)
    : IRequest<ItemChecklist>;

public sealed class GuardarChecklistValidator : AbstractValidator<GuardarChecklistCommand>
{
    public GuardarChecklistValidator()
    {
        RuleFor(x => x.ActividadId).GreaterThan(0);
        RuleFor(x => x.Fecha).LessThanOrEqualTo(_ => ZonaHoraria.Hoy()).WithMessage("La fecha no puede ser futura.")
            .GreaterThanOrEqualTo(new DateOnly(2020, 1, 1)).WithMessage("Fecha fuera de rango.");
        RuleFor(x => x.Observacion).MaximumLength(1000);
        // Advertencia/Falla/Critico necesitan explicar qué pasó; Ok/NoAplica no la piden (la base también lo exige).
        RuleFor(x => x.Observacion)
            .Must(o => !string.IsNullOrWhiteSpace(o))
            .WithMessage("Esta observación es obligatoria cuando el estado no es 'Ok' ni 'No aplica'.")
            .When(x => x.Estado is EstadoChecklist.Advertencia or EstadoChecklist.Falla or EstadoChecklist.Critico);
    }
}

public sealed class GuardarChecklistHandler(IBitacoraRepository repo, IUsuarioActualProvider usuarioActual)
    : IRequestHandler<GuardarChecklistCommand, ItemChecklist>
{
    public async Task<ItemChecklist> Handle(GuardarChecklistCommand request, CancellationToken ct)
    {
        _ = await repo.ObtenerActividadAsync(request.ActividadId, ct)
            ?? throw new NotFoundException("la actividad de la bitácora", request.ActividadId);

        var observacion = string.IsNullOrWhiteSpace(request.Observacion) ? null : request.Observacion.Trim();
        return await repo.GuardarChecklistAsync(
            request.ActividadId, request.Fecha, request.Estado, observacion, usuarioActual.Obtener().Nombre, ct);
    }
}
