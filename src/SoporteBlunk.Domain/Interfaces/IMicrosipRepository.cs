using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Domain.Interfaces;

/// <summary>
/// Usuarios de MICROSIP (base de configuración, vía linked server). Lanza <c>UtileriaNoDisponibleException</c>
/// (Application) si no está configurado o el servidor no responde.
/// </summary>
public interface IMicrosipRepository
{
    Task<IReadOnlyList<UsuarioMicrosip>> ListarUsuariosAsync(CancellationToken ct);

    Task<UsuarioMicrosip?> ObtenerUsuarioAsync(int usuarioId, CancellationToken ct);

    /// <summary>
    /// Inserta o actualiza los datos adicionales del usuario (según <paramref name="existe"/>). Consulta parametrizada.
    /// Esta es la ÚNICA operación de escritura sobre MICROSIP.
    /// </summary>
    Task GuardarDatosAdicionalesAsync(int usuarioId, DatosAdicionales datos, bool existe, CancellationToken ct);
}

/// <summary>Trabajadores de FORTIA (RH). Solo lectura.</summary>
public interface IFortiaRepository
{
    Task<TrabajadorFortia?> ObtenerTrabajadorAsync(int clave, CancellationToken ct);
}
