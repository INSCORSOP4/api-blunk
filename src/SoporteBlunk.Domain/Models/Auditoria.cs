namespace SoporteBlunk.Domain.Models;

public enum ModuloAuditoria { Conocimiento, Microsip, Bitacora }

/// <summary>
/// Un evento de auditoría, ya homogeneizado desde su fuente original (Conocimiento, MICROSIP o Bitácora)
/// para poder mostrarlos juntos en una sola línea de tiempo.
/// </summary>
public sealed record EventoAuditoria(
    DateTimeOffset Fecha,
    ModuloAuditoria Modulo,
    string Accion,
    string? Detalle,
    string UsuarioNombre,
    string? UsuarioEmail,
    string? Resultado);
