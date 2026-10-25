using SoporteBlunk.Domain.Entities.ServiceDesk;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

/// <summary>Una solicitud con su estado y su técnico (WorkOrder + WorkOrderStates + StatusDefinition obligatorios; el usuario, LEFT JOIN).</summary>
internal sealed class TecnicoFila
{
    public required WorkOrder W { get; init; }
    public required WorkOrderStates S { get; init; }
    public required StatusDefinition St { get; init; }
    public AaaUser? U { get; init; }
}

internal sealed class TecnicoIdNombreFila
{
    public long? TecnicoId { get; init; }
    public string? Nombre { get; init; }
    public string? Apellido { get; init; }
}

internal sealed class CargaFila
{
    public long? TecnicoId { get; init; }
    public string? Nombre { get; init; }
    public string? Apellido { get; init; }
    public int Abiertos { get; init; }
    public int Vencidos { get; init; }
    public int Total { get; init; }
}

internal sealed class ProductividadFila
{
    public long? TecnicoId { get; init; }
    public string? Nombre { get; init; }
    public string? Apellido { get; init; }
    public int Asignados { get; init; }
    public int Cerrados { get; init; }
    public int Vencidos { get; init; }
    public double? PromedioHorasPrimeraRespuesta { get; init; }
    public double? PromedioHorasResolucion { get; init; }
}

/// <summary>Columnas mínimas de cada solicitud para armar la tendencia por periodo en código.</summary>
internal sealed class TendenciaFila
{
    public long? Creado { get; init; }
    public long? Respondido { get; init; }
    public long? Asignado { get; init; }
    public long? Completado { get; init; }
    public bool? Pendiente { get; init; }
    public bool? Vencido { get; init; }
}

/// <summary>
/// En ServiceDesk no hay una tabla simple de técnicos: se consideran técnicos los usuarios que aparecen como propietario
/// (OWNERID) de algún ticket.
/// </summary>
internal static class TecnicoConsultas
{
    private const double MsPorHora = 3600000.0;

    /// <summary>WorkOrderStates con su usuario (LEFT JOIN), solo de solicitudes con técnico asignado.</summary>
    public static IQueryable<TecnicoIdNombreFila> TecnicosConNombre(ServiceDeskDbContext db) =>
        from s in db.TicketEstados
        where s.TecnicoId != null
        join u0 in db.Usuarios on s.TecnicoId equals (long?)u0.Id into us
        from u in us.DefaultIfEmpty()
        select new TecnicoIdNombreFila { TecnicoId = s.TecnicoId, Nombre = u!.Nombre, Apellido = u!.Apellido };

    /// <summary>Técnicos distintos ordenados por nombre; el nombre vacío se reemplaza por "Usuario {id}" dentro del SQL para conservar el orden.</summary>
    public static IQueryable<TecnicoIdNombreFila> ListaOrdenada(ServiceDeskDbContext db) =>
        TecnicosConNombre(db)
            .GroupBy(x => new { x.TecnicoId, x.Nombre, x.Apellido })
            .Select(g => new TecnicoIdNombreFila { TecnicoId = g.Key.TecnicoId, Nombre = g.Key.Nombre, Apellido = g.Key.Apellido })
            .OrderBy(x => ((x.Nombre ?? "") + " " + (x.Apellido ?? "")).Trim() == ""
                ? "Usuario " + x.TecnicoId
                : ((x.Nombre ?? "") + " " + (x.Apellido ?? "")).Trim());

    public static IQueryable<TecnicoFila> Base(ServiceDeskDbContext db) =>
        from w in db.Tickets
        join s in db.TicketEstados on w.Id equals s.TicketId
        join st in db.Estados on s.EstadoId equals (long?)st.Id
        join u0 in db.Usuarios on s.TecnicoId equals (long?)u0.Id into us
        from u in us.DefaultIfEmpty()
        select new TecnicoFila { W = w, S = s, St = st, U = u };

    public static IQueryable<CargaFila> Carga(ServiceDeskDbContext db, long? tecnicoId)
    {
        var consulta =
            from s in db.TicketEstados
            join st in db.Estados on s.EstadoId equals (long?)st.Id
            join u0 in db.Usuarios on s.TecnicoId equals (long?)u0.Id into us
            from u in us.DefaultIfEmpty()
            where s.TecnicoId != null
            select new { s, st, u };

        if (tecnicoId is { } id) consulta = consulta.Where(x => x.s.TecnicoId == id);

        return consulta
            .GroupBy(x => new { x.s.TecnicoId, x.u!.Nombre, x.u!.Apellido })
            .Select(g => new CargaFila
            {
                TecnicoId = g.Key.TecnicoId,
                Nombre = g.Key.Nombre,
                Apellido = g.Key.Apellido,
                Abiertos = g.Count(x => x.st.EsPendiente == true),
                Vencidos = g.Count(x => x.st.EsPendiente == true && x.s.Vencido == true),
                Total = g.Count(),
            })
            .OrderByDescending(x => x.Abiertos).ThenByDescending(x => x.Total);
    }

    /// <summary>Filtra las solicitudes de un técnico, o de todos los que tienen uno asignado, dentro del rango de creación.</summary>
    public static IQueryable<TecnicoFila> DelTecnico(this IQueryable<TecnicoFila> consulta, long? tecnicoId, DateOnly? desde, DateOnly? hasta)
    {
        consulta = tecnicoId is { } id
            ? consulta.Where(x => x.S.TecnicoId == id)
            : consulta.Where(x => x.S.TecnicoId != null);

        if (desde is { } d)
        {
            var desdeMs = TiempoServiceDesk.InicioDelDiaMs(d);
            consulta = consulta.Where(x => x.W.Creado >= desdeMs);
        }
        if (hasta is { } h)
        {
            var hastaMs = TiempoServiceDesk.InicioDelDiaMs(h.AddDays(1));
            consulta = consulta.Where(x => x.W.Creado < hastaMs);
        }
        return consulta;
    }

    // La primera respuesta se mide desde que el ticket quedó asignado a su técnico actual (ASSIGNEDTIME), no desde la creación:
    // si se reasignó después de que alguien ya había respondido, RESPONDEDTIME < ASSIGNEDTIME y ese ticket no cuenta para el
    // promedio de este técnico (no fue él quien dio esa respuesta). Las resoluciones solo cuentan en tickets cerrados.
    public static IQueryable<ProductividadFila> Productividad(this IQueryable<TecnicoFila> consulta) =>
        consulta
            .GroupBy(x => x.S.TecnicoId)
            .Select(g => new ProductividadFila
            {
                TecnicoId = g.Key,
                Nombre = g.Max(x => x.U!.Nombre),
                Apellido = g.Max(x => x.U!.Apellido),
                Asignados = g.Count(),
                Cerrados = g.Count(x => x.St.EsPendiente == false),
                Vencidos = g.Count(x => x.S.Vencido == true),
                PromedioHorasPrimeraRespuesta = g.Average(x => x.W.Respondido > x.S.Asignado
                    ? (double?)(x.W.Respondido - x.S.Asignado) / MsPorHora : null),
                PromedioHorasResolucion = g.Average(x => x.St.EsPendiente == false && x.W.Completado > x.W.Creado
                    ? (double?)(x.W.Completado - x.W.Creado) / MsPorHora : null),
            })
            .OrderByDescending(x => x.Asignados);

    public static IQueryable<TendenciaFila> FilasParaTendencia(ServiceDeskDbContext db, long tecnicoId, DateOnly desde, DateOnly hasta)
    {
        var desdeMs = TiempoServiceDesk.InicioDelDiaMs(desde);
        var hastaMs = TiempoServiceDesk.InicioDelDiaMs(hasta.AddDays(1));
        return
            from w in db.Tickets
            join s in db.TicketEstados on w.Id equals s.TicketId
            join st in db.Estados on s.EstadoId equals (long?)st.Id
            where s.TecnicoId == tecnicoId && w.Creado >= desdeMs && w.Creado < hastaMs
            select new TendenciaFila
            {
                Creado = w.Creado,
                Respondido = w.Respondido,
                Asignado = s.Asignado,
                Completado = w.Completado,
                Pendiente = st.EsPendiente,
                Vencido = s.Vencido,
            };
    }
}
