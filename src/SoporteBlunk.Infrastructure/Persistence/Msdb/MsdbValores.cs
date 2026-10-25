using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Infrastructure.Persistence.Msdb;

/// <summary>Conversión de los valores "raros" de msdb: fechas yyyymmdd, horas hhmmss y duraciones hhmmss, todos como int.</summary>
internal static class MsdbValores
{
    /// <summary>run_date/run_time (hora local del servidor, sin zona). Devuelve null si no hay fecha (0) o es inválida.</summary>
    public static DateTime? Fecha(int? fecha, int? hora)
    {
        if (fecha is null or <= 0) return null;
        var f = fecha.Value;
        var (anio, mes, dia) = (f / 10000, f / 100 % 100, f % 100);
        var h = Math.Max(hora ?? 0, 0);
        var (hh, mm, ss) = (h / 10000, h / 100 % 100, h % 100);
        if (anio is < 1900 or > 9999 || mes is < 1 or > 12 || dia < 1 || dia > DateTime.DaysInMonth(anio, mes)) return null;
        if (hh > 23 || mm > 59 || ss > 59) return new DateTime(anio, mes, dia);
        return new DateTime(anio, mes, dia, hh, mm, ss, DateTimeKind.Unspecified);
    }

    /// <summary>run_duration: 13045 = 1 h 30 m 45 s. Devuelve segundos.</summary>
    public static int? DuracionSegundos(int? duracion)
    {
        if (duracion is null or < 0) return null;
        var d = duracion.Value;
        return d / 10000 * 3600 + d / 100 % 100 * 60 + d % 100;
    }

    public static EstadoJob Estado(int? runStatus) => runStatus switch
    {
        0 => EstadoJob.Fallido,
        1 => EstadoJob.Exitoso,
        2 => EstadoJob.Reintento,
        3 => EstadoJob.Cancelado,
        4 => EstadoJob.EnEjecucion,
        _ => EstadoJob.SinHistorial,
    };
}
