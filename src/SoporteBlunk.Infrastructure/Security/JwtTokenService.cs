using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Auth";

    /// <summary>Clave de firma (mínimo 32 caracteres). Va en secretos/variables de entorno, nunca en el repositorio.</summary>
    public string JwtKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "soporte-blunk-api";
    public string Audience { get; set; } = "soporte-blunk-consola";

    public SymmetricSecurityKey BuildKey() => new(Encoding.UTF8.GetBytes(JwtKey));
}

public static class ClaimNames
{
    public const string RolId = "rol_id";
}

/// <summary>Emite la sesión propia de esta consola. No reutiliza el token de Seguridad: así no necesitamos su clave.</summary>
internal sealed class JwtTokenService(IOptions<JwtOptions> jwt, IOptions<AuthOptions> auth, TimeProvider clock) : ITokenService
{
    public SesionEmitida Emitir(UsuarioSesion usuario)
    {
        var ahora = clock.GetUtcNow();
        var expira = ahora.AddHours(auth.Value.HorasSesion);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Name, usuario.Nombre),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
            new Claim(ClaimNames.RolId, usuario.RolId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var token = new JwtSecurityToken(
            issuer: jwt.Value.Issuer,
            audience: jwt.Value.Audience,
            claims: claims,
            notBefore: ahora.UtcDateTime,
            expires: expira.UtcDateTime,
            signingCredentials: new SigningCredentials(jwt.Value.BuildKey(), SecurityAlgorithms.HmacSha256));

        return new SesionEmitida(new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
