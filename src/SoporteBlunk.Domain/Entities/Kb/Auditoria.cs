using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Domain.Entities.Kb;

/// <summary>aud.CambioMicrosip: cada cambio que la consola hace en MICROSIP, registrado antes de aplicarse.</summary>
public sealed class AuditoriaCambioMicrosip
{
    public long Id { get; init; }
    public DateTime Fecha { get; init; }
    public string UsuarioEmail { get; init; } = string.Empty;
    public string UsuarioNombre { get; init; } = string.Empty;
    public int MicrosipUsuarioId { get; init; }
    public string MicrosipNombre { get; init; } = string.Empty;
    public AccionMicrosip Accion { get; init; }
    public ResultadoCambio Resultado { get; init; }
    public string? Antes { get; init; }
    public string Despues { get; init; } = string.Empty;
    public string? Error { get; init; }
}
