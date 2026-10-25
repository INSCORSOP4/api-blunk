using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Bitacora.Commands.CrearActividad;

public sealed record CrearActividadCommand(string Grupo, string Nombre) : IRequest<ActividadBitacora>;

public sealed class CrearActividadValidator : AbstractValidator<CrearActividadCommand>
{
    public CrearActividadValidator()
    {
        RuleFor(x => x.Grupo).Must(BitacoraGrupos.EsValido).WithMessage("Grupo de bitácora no reconocido.");
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(200);
    }
}

public sealed class CrearActividadHandler(IBitacoraRepository repo, IUsuarioActualProvider usuarioActual)
    : IRequestHandler<CrearActividadCommand, ActividadBitacora>
{
    public Task<ActividadBitacora> Handle(CrearActividadCommand request, CancellationToken ct) =>
        repo.CrearActividadAsync(request.Grupo, request.Nombre.Trim(), usuarioActual.Obtener().Nombre, ct);
}
