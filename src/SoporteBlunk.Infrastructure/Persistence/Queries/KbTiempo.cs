using SoporteBlunk.Application.Common;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

internal static class KbTiempo
{
    /// <summary>
    /// Las fechas de SoporteKB son datetime2 (hora local del servidor, sin zona); se interpretan como hora de México, con el
    /// desplazamiento que tenía México en esa fecha.
    /// </summary>
    public static DateTimeOffset? ComoOffset(DateTime? valor) =>
        valor is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(valor.Value, DateTimeKind.Unspecified), ZonaHoraria.Zona.GetUtcOffset(valor.Value));
}
