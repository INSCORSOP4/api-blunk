namespace SoporteBlunk.Infrastructure.Persistence;

internal static class ConsultaDefaults
{
    /// <summary>Tiempo máximo de cada consulta a ServiceDesk y a SoporteKB.</summary>
    public const int TimeoutSegundos = 30;

    /// <summary>Tiempo máximo de las consultas de Bitácora, Auditoría y Archivos en SoporteKB (el que tenían con Dapper).</summary>
    public const int KbTimeoutSegundos = 15;
}
