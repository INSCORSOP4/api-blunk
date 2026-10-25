using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Abstractions;

/// <summary>
/// Bitácora de los cambios en MICROSIP (SoporteKB, tabla aud.CambioMicrosip). Si no está disponible lanza
/// <c>UtileriaNoDisponibleException</c>: sin bitácora no se escribe.
/// </summary>
public interface IAuditoriaMicrosipRepository
{
    /// <summary>Registra el cambio como Pendiente (antes de aplicarlo) y devuelve su id.</summary>
    Task<long> RegistrarAsync(
        UsuarioActual quien, UsuarioMicrosip objetivo, AccionMicrosip accion, string? antes, string despues, CancellationToken ct);

    Task MarcarAsync(long cambioId, ResultadoCambio resultado, string? error, CancellationToken ct);

    Task<IReadOnlyList<CambioMicrosip>> ListarAsync(int maximo, CancellationToken ct);
}
