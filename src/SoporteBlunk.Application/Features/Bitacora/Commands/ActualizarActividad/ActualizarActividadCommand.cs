using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Bitacora.Commands.ActualizarActividad;

/// <summary>Renombra, activa/desactiva o reordena una actividad. Los campos en null no se tocan.</summary>
public sealed record ActualizarActividadCommand(int ActividadId, string? Nombre, bool? Activa, int? Orden)
    : IRequest<ActividadBitacora>;

public sealed class ActualizarActividadValidator : AbstractValidator<ActualizarActividadCommand>
{
    public ActualizarActividadValidator()
    {
        RuleFor(x => x.ActividadId).GreaterThan(0);
        RuleFor(x => x.Nombre!).NotEmpty().MaximumLength(200).When(x => x.Nombre is not null);
        RuleFor(x => x).Must(x => x.Nombre is not null || x.Activa is not null || x.Orden is not null)
            .WithMessage("No hay ningún cambio que guardar.");
    }
}

public sealed class ActualizarActividadHandler(IBitacoraRepository repo, IUsuarioActualProvider usuarioActual)
    : IRequestHandler<ActualizarActividadCommand, ActividadBitacora>
{
    public async Task<ActividadBitacora> Handle(ActualizarActividadCommand request, CancellationToken ct)
    {
        _ = await repo.ObtenerActividadAsync(request.ActividadId, ct)
            ?? throw new NotFoundException("la actividad de la bitácora", request.ActividadId);

        return await repo.ActualizarActividadAsync(
            request.ActividadId, request.Nombre?.Trim(), request.Activa, request.Orden,
            usuarioActual.Obtener().Nombre, ct);
    }
}
