using System.IdentityModel.Tokens.Jwt;
using SoporteBlunk.Application.Abstractions;

namespace SoporteBlunk.Api.Services;

/// <summary>Lee quién es el usuario de la petición desde los claims del token ya validado.</summary>
internal sealed class UsuarioActualProvider(IHttpContextAccessor accessor) : IUsuarioActualProvider
{
    public UsuarioActual Obtener()
    {
        var user = accessor.HttpContext?.User;
        string? Claim(params string[] tipos) => tipos.Select(t => user?.FindFirst(t)?.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        var id = Claim(JwtRegisteredClaimNames.Sub, "sub");
        if (user?.Identity?.IsAuthenticated != true || !Guid.TryParse(id, out var guid))
            throw new UnauthorizedAccessException("No hay una sesión válida.");

        return new UsuarioActual(
            guid,
            Claim(JwtRegisteredClaimNames.Name, "name") ?? "(sin nombre)",
            Claim(JwtRegisteredClaimNames.Email, "email") ?? "(sin correo)");
    }
}
