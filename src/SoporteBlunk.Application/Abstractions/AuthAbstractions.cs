using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Abstractions;

/// <summary>Cliente del servicio central de Seguridad (apiseguridad). La contraseña se valida allá, nunca aquí.</summary>
public interface ISeguridadClient
{
    /// <returns>El usuario si las credenciales son correctas; null si son incorrectas.</returns>
    /// <exception cref="Common.Exceptions.ServicioNoDisponibleException">Si Seguridad no responde o falla.</exception>
    Task<UsuarioSeguridad?> LoginAsync(string email, string password, CancellationToken ct);
}

public sealed record SesionEmitida(string Token, DateTimeOffset ExpiraEn);

public interface ITokenService
{
    SesionEmitida Emitir(UsuarioSesion usuario);
}

/// <summary>Limita los intentos fallidos de login por clave (correo) para frenar adivinación de contraseñas.</summary>
public interface ILoginThrottle
{
    /// <returns>Tiempo restante de bloqueo, o null si puede intentar.</returns>
    TimeSpan? ObtenerBloqueo(string clave);

    void RegistrarFallo(string clave);

    void Reiniciar(string clave);
}
