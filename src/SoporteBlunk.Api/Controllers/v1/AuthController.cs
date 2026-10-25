using System.IdentityModel.Tokens.Jwt;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SoporteBlunk.Application.Features.Auth.Commands.Login;

namespace SoporteBlunk.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);

    /// <summary>Inicia sesión con el correo y la contraseña de Seguridad. Solo usuarios del rol autorizado.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    [ProducesResponseType<SesionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        Ok(await sender.Send(new LoginCommand(request.Email, request.Password), ct));

    /// <summary>Usuario de la sesión actual.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    public IActionResult Me() =>
        Ok(new UsuarioDto(
            Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst("sub")!.Value),
            User.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? User.FindFirst("name")?.Value ?? string.Empty,
            User.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? User.FindFirst("email")?.Value ?? string.Empty));
}
