using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<SesionDto>;

public sealed record SesionDto(string Token, DateTimeOffset ExpiraEn, UsuarioDto Usuario);

public sealed record UsuarioDto(Guid Id, string Nombre, string Email);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(100).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public sealed class LoginHandler(
    ISeguridadClient seguridad,
    ITokenService tokens,
    ILoginThrottle throttle,
    IOptions<AuthOptions> options) : IRequestHandler<LoginCommand, SesionDto>
{
    public async Task<SesionDto> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (throttle.ObtenerBloqueo(email) is { } espera)
            throw new DemasiadosIntentosException(espera);

        var usuario = await seguridad.LoginAsync(email, request.Password, ct);
        if (usuario is null)
        {
            throttle.RegistrarFallo(email);
            throw new CredencialesInvalidasException();
        }

        // La contraseña es válida; ahora se comprueba que pueda usar ESTA consola. El login de Seguridad
        // no filtra por tipo ni estado (la fila del rol o un usuario inactivo también recibirían token).
        if (!PuedeEntrar(usuario, options.Value))
            throw new AccesoDenegadoException();

        throttle.Reiniciar(email);

        var sesion = new UsuarioSesion(usuario.Id, usuario.Nombre, usuario.Email, usuario.RolId);
        var emitida = tokens.Emitir(sesion);
        return new SesionDto(emitida.Token, emitida.ExpiraEn, new UsuarioDto(usuario.Id, usuario.Nombre, usuario.Email));
    }

    public static bool PuedeEntrar(UsuarioSeguridad u, AuthOptions opciones) =>
        string.Equals(u.Tipo, "U", StringComparison.OrdinalIgnoreCase)
        && string.Equals(u.Status, "A", StringComparison.OrdinalIgnoreCase)
        && u.RolId == opciones.RolSistemasId;
}
