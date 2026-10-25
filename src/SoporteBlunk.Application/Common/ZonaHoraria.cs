namespace SoporteBlunk.Application.Common;

/// <summary>Zona horaria operativa del área (México central, sin horario de verano).</summary>
public static class ZonaHoraria
{
    public static readonly TimeZoneInfo Zona = Resolver();

    public static DateOnly Hoy() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zona));

    private static TimeZoneInfo Resolver()
    {
        foreach (var id in new[] { "America/Mexico_City", "Central Standard Time (Mexico)" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("MX", TimeSpan.FromHours(-6), "MX", "MX");
    }
}
