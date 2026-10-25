using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

internal sealed class ResumenDashboardFila
{
    public int Total { get; init; }
    public int Abiertos { get; init; }
    public int Cerrados { get; init; }
    public int Vencidos { get; init; }
    public int CreadosHoy { get; init; }
    public int CerradosHoy { get; init; }
    public int SinAsignar { get; init; }
    public double? PromedioHorasResolucion { get; init; }
}

internal sealed class ConteoFila
{
    public long? Id { get; init; }
    public string? Nombre { get; init; }
    /// <summary>Solo la dimensión "tecnico": el nombre completo se arma con nombre y apellido.</summary>
    public string? Apellido { get; init; }
    public int Total { get; init; }
}

/// <summary>Cantidad de solicitudes por hora UTC (<c>Hora</c> = epoch ms / 3 600 000).</summary>
internal sealed class HoraConteoFila
{
    public long? Hora { get; init; }
    public int Total { get; init; }
}

internal sealed class SlaFila
{
    public int Total { get; init; }
    public int Vencidos { get; init; }
}

internal static class DashboardConsultas
{
    private const double MsPorHora = 3600000.0;
    public const long MsPorHoraEntera = 3600000;

    public static IQueryable<TicketFila> EnRango(ServiceDeskDbContext db, DateOnly? desde, DateOnly? hasta) =>
        TicketConsultas.Base(db).Filtrar(new TicketFiltro { Desde = desde, Hasta = hasta });

    public static IQueryable<ResumenDashboardFila> Resumen(IQueryable<TicketFila> consulta, long hoyMs) =>
        consulta
            .GroupBy(_ => 1)
            .Select(g => new ResumenDashboardFila
            {
                Total = g.Count(),
                Abiertos = g.Count(x => x.St.EsPendiente == true),
                Cerrados = g.Count(x => x.St.EsPendiente == false),
                Vencidos = g.Count(x => x.St.EsPendiente == true && x.S.Vencido == true),
                CreadosHoy = g.Count(x => x.W.Creado >= hoyMs),
                CerradosHoy = g.Count(x => x.St.EsPendiente == false && x.W.Completado >= hoyMs),
                SinAsignar = g.Count(x => x.St.EsPendiente == true && x.S.TecnicoId == null),
                PromedioHorasResolucion = g.Average(x => x.St.EsPendiente == false && x.W.Completado > x.W.Creado
                    ? (double?)(x.W.Completado - x.W.Creado) / MsPorHora : null),
            });

    public static IQueryable<ConteoFila> PorEstado(IQueryable<TicketFila> consulta) =>
        consulta
            .GroupBy(x => new { Id = x.S.EstadoId, x.St.Nombre })
            .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() })
            .OrderBy(x => x.Id);

    public static IQueryable<SlaFila> Sla(IQueryable<TicketFila> consulta) =>
        consulta
            .GroupBy(_ => 1)
            .Select(g => new SlaFila { Total = g.Count(), Vencidos = g.Count(x => x.S.Vencido == true) });

    public static IQueryable<HoraConteoFila> CreadosPorHora(ServiceDeskDbContext db, long desdeMs, long hastaMs) =>
        db.Tickets
            .Where(w => w.Creado >= desdeMs && w.Creado < hastaMs)
            .GroupBy(w => w.Creado / MsPorHoraEntera)
            .Select(g => new HoraConteoFila { Hora = g.Key, Total = g.Count() });

    public static IQueryable<HoraConteoFila> CerradosPorHora(ServiceDeskDbContext db, long desdeMs, long hastaMs) =>
        (from w in db.Tickets
         join s in db.TicketEstados on w.Id equals s.TicketId
         join st in db.Estados on s.EstadoId equals (long?)st.Id
         where st.EsPendiente == false && w.Completado >= desdeMs && w.Completado < hastaMs
         select w)
        .GroupBy(w => w.Completado / MsPorHoraEntera)
        .Select(g => new HoraConteoFila { Hora = g.Key, Total = g.Count() });

    /// <summary>Dimensión pública -> agrupación. Todo fijo, sin entrada del usuario. Los catálogos opcionales se unen con LEFT JOIN.</summary>
    public static readonly Dictionary<string, Func<ServiceDeskDbContext, IQueryable<TicketFila>, IQueryable<ConteoFila>>> Dimensiones =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["estado"] = (_, q) => q
                .GroupBy(x => new { Id = x.S.EstadoId, x.St.Nombre })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() }),
            ["categoria"] = (_, q) => q
                .GroupBy(x => new { Id = x.S.CategoriaId, Nombre = x.Cat!.Nombre })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() }),
            ["prioridad"] = (_, q) => q
                .GroupBy(x => new { Id = x.S.PrioridadId, Nombre = x.Pr!.Nombre })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() }),
            ["grupo"] = (_, q) => q
                .GroupBy(x => new { Id = x.Q!.GrupoId, Nombre = x.Qd!.Nombre })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() }),
            ["tecnico"] = (_, q) => q
                .GroupBy(x => new { Id = x.S.TecnicoId, Nombre = x.Tu!.Nombre, Apellido = x.Tu!.Apellido })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Apellido = g.Key.Apellido, Total = g.Count() }),
            ["modo"] = (db, q) =>
                (from x in q
                 join c0 in db.Modos on x.W.ModoId equals (long?)c0.Id into cs
                 from c in cs.DefaultIfEmpty()
                 select new { Id = x.W.ModoId, Nombre = c!.Nombre })
                .GroupBy(x => new { x.Id, x.Nombre })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() }),
            ["urgencia"] = (db, q) =>
                (from x in q
                 join c0 in db.Urgencias on x.S.UrgenciaId equals (long?)c0.Id into cs
                 from c in cs.DefaultIfEmpty()
                 select new { Id = x.S.UrgenciaId, Nombre = c!.Nombre })
                .GroupBy(x => new { x.Id, x.Nombre })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() }),
            ["impacto"] = (db, q) =>
                (from x in q
                 join c0 in db.Impactos on x.S.ImpactoId equals (long?)c0.Id into cs
                 from c in cs.DefaultIfEmpty()
                 select new { Id = x.S.ImpactoId, Nombre = c!.Nombre })
                .GroupBy(x => new { x.Id, x.Nombre })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() }),
            ["tipo-solicitud"] = (db, q) =>
                (from x in q
                 join c0 in db.TiposSolicitud on x.S.TipoSolicitudId equals (long?)c0.Id into cs
                 from c in cs.DefaultIfEmpty()
                 select new { Id = x.S.TipoSolicitudId, Nombre = c!.Nombre })
                .GroupBy(x => new { x.Id, x.Nombre })
                .Select(g => new ConteoFila { Id = g.Key.Id, Nombre = g.Key.Nombre, Total = g.Count() }),
        };
}
