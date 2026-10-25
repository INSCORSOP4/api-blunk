using SoporteBlunk.Application.Common;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

/// <summary>
/// Conversiones de los datos de ServiceDesk que antes hacía el SQL: tiempos (epoch en ms UTC, 0 = sin dato) y nombres.
/// Se hacen en código porque LINQ no traduce AT TIME ZONE y así no dependen de la zona horaria del servidor SQL.
/// </summary>
internal static class TiempoServiceDesk
{
    /// <summary>Epoch en ms a hora de México (con su desplazamiento). Truncado a segundos, como el DATEADD(SECOND, ...) anterior.</summary>
    public static DateTimeOffset? AFecha(long? ms) =>
        ms is > 0
            ? TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(ms.Value / 1000), ZonaHoraria.Zona)
            : null;

    /// <summary>Medianoche local del día, en epoch ms UTC.</summary>
    public static long InicioDelDiaMs(DateOnly dia)
    {
        var local = DateTime.SpecifyKind(dia.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, ZonaHoraria.Zona)).ToUnixTimeMilliseconds();
    }

    /// <summary>Equivale a NULLIF(LTRIM(RTRIM(CONCAT(nombre, ' ', apellido))), ''): solo recorta espacios y devuelve null si queda vacío.</summary>
    public static string? NombreCompleto(string? nombre, string? apellido)
    {
        var completo = string.Concat(nombre, " ", apellido).Trim(' ');
        return completo.Length == 0 ? null : completo;
    }
}
