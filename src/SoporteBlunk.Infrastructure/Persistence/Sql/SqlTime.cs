using Dapper;
using SoporteBlunk.Application.Common;

namespace SoporteBlunk.Infrastructure.Persistence.Sql;

internal static class SqlTime
{
    // El servidor de ServiceDesk corre en Windows; los tiempos se guardan en ms UTC desde 1970.
    private const string WindowsZone = "Central Standard Time (Mexico)";

    private static readonly TimeZoneInfo Zone = ZonaHoraria.Zona;

    /// <summary>Expresión SQL: epoch en ms (0 = sin dato) a datetimeoffset en hora de México.</summary>
    public static string Local(string column) =>
        $"(CASE WHEN {column} > 0 THEN CAST(DATEADD(SECOND, CAST({column} / 1000 AS int), '19700101') AS datetime2) AT TIME ZONE 'UTC' AT TIME ZONE '{WindowsZone}' END)";

    public static DateOnly Today() => ZonaHoraria.Hoy();

    public static long StartOfDayMs(DateOnly day)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Zone)).ToUnixTimeMilliseconds();
    }

    /// <summary>Agrega al WHERE el rango [desde, hasta] (días locales, hasta inclusivo) sobre CREATEDTIME.</summary>
    public static void AddCreatedRange(List<string> where, DynamicParameters p, DateOnly? desde, DateOnly? hasta)
    {
        if (desde is { } d)
        {
            where.Add("w.CREATEDTIME >= @desdeMs");
            p.Add("desdeMs", StartOfDayMs(d));
        }
        if (hasta is { } h)
        {
            where.Add("w.CREATEDTIME < @hastaMs");
            p.Add("hastaMs", StartOfDayMs(h.AddDays(1)));
        }
    }

}

