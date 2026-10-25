using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Queries;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class DashboardReadRepository(ServiceDeskDbContext db) : IDashboardReadRepository
{
    public async Task<DashboardResumen> ResumenAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var consulta = DashboardConsultas.EnRango(db, desde, hasta);
        var hoyMs = TiempoServiceDesk.InicioDelDiaMs(ZonaHoraria.Hoy());

        // Sin solicitudes en el rango el agregado devuelve ceros (como el SELECT COUNT(*) sin GROUP BY), no ninguna fila.
        var r = await DashboardConsultas.Resumen(consulta, hoyMs).FirstOrDefaultAsync(ct) ?? new ResumenDashboardFila();
        var porEstado = await DashboardConsultas.PorEstado(consulta).ToListAsync(ct);

        return new DashboardResumen(r.Total, r.Abiertos, r.Cerrados, r.Vencidos, r.CreadosHoy, r.CerradosHoy, r.SinAsignar,
            r.PromedioHorasResolucion,
            porEstado.ConvertAll(e => new ConteoPorClave(e.Id, e.Nombre ?? string.Empty, e.Total)));
    }

    public async Task<IReadOnlyList<TendenciaPunto>> TendenciaAsync(DateOnly desde, DateOnly hasta, Agrupacion agrupacion, CancellationToken ct)
    {
        var desdeMs = TiempoServiceDesk.InicioDelDiaMs(desde);
        var hastaMs = TiempoServiceDesk.InicioDelDiaMs(hasta.AddDays(1));

        // Se cuenta por hora UTC en SQL y cada hora se pasa a su día local en código (LINQ no traduce AT TIME ZONE).
        // Es exacto: los cambios de horario de México caen en horas completas.
        var creados = PorPeriodo(await DashboardConsultas.CreadosPorHora(db, desdeMs, hastaMs).ToListAsync(ct), agrupacion);
        var cerrados = PorPeriodo(await DashboardConsultas.CerradosPorHora(db, desdeMs, hastaMs).ToListAsync(ct), agrupacion);

        // Rellena con ceros los periodos sin actividad para que la serie sea continua.
        var puntos = new List<TendenciaPunto>();
        var actual = Inicio(desde, agrupacion);
        var fin = Inicio(hasta, agrupacion);
        while (actual <= fin)
        {
            puntos.Add(new TendenciaPunto(actual, creados.GetValueOrDefault(actual), cerrados.GetValueOrDefault(actual)));
            actual = agrupacion switch
            {
                Agrupacion.Mes => actual.AddMonths(1),
                Agrupacion.Semana => actual.AddDays(7),
                _ => actual.AddDays(1),
            };
        }
        return puntos;
    }

    public async Task<IReadOnlyList<ConteoPorClave>> PorAsync(string dimension, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        if (!DashboardConsultas.Dimensiones.TryGetValue(dimension, out var agrupar))
            throw new ArgumentException($"Dimensión desconocida: {dimension}", nameof(dimension));

        var filas = await agrupar(db, DashboardConsultas.EnRango(db, desde, hasta))
            .OrderByDescending(x => x.Total)
            .ToListAsync(ct);

        const string SinDato = "Sin dato";
        return filas.ConvertAll(f => new ConteoPorClave(
            f.Id,
            dimension.Equals("tecnico", StringComparison.OrdinalIgnoreCase)
                ? TiempoServiceDesk.NombreCompleto(f.Nombre, f.Apellido) ?? SinDato
                : f.Nombre ?? SinDato,
            f.Total));
    }

    public async Task<SlaResumen> SlaAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var r = await DashboardConsultas.Sla(DashboardConsultas.EnRango(db, desde, hasta)).FirstOrDefaultAsync(ct) ?? new SlaFila();
        var enTiempo = r.Total - r.Vencidos;
        var pct = r.Total == 0 ? 100d : Math.Round(enTiempo * 100d / r.Total, 2);
        return new SlaResumen(r.Total, r.Vencidos, enTiempo, pct);
    }

    private static Dictionary<DateOnly, int> PorPeriodo(List<HoraConteoFila> filas, Agrupacion agrupacion) => filas
        .GroupBy(f => Inicio(DateOnly.FromDateTime(TiempoServiceDesk.AFecha(f.Hora!.Value * DashboardConsultas.MsPorHoraEntera)!.Value.DateTime), agrupacion))
        .ToDictionary(g => g.Key, g => g.Sum(f => f.Total));

    private static DateOnly Inicio(DateOnly d, Agrupacion a) => a switch
    {
        Agrupacion.Mes => new DateOnly(d.Year, d.Month, 1),
        Agrupacion.Semana => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
        _ => d,
    };
}
