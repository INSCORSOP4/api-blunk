using SoporteBlunk.Domain.Entities.Kb;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

internal sealed class ItemChecklistFila
{
    public int ActividadId { get; init; }
    public string Nombre { get; init; } = string.Empty;
    public int Orden { get; init; }
    public bool ActividadActiva { get; init; }
    public EstadoChecklist? Estado { get; init; }
    public string? Observacion { get; init; }
    public string? RevisadoPor { get; init; }
    public DateTime? RevisadoEn { get; init; }
    public string? ActualizadoPor { get; init; }
    public DateTime? ActualizadoEn { get; init; }

    public ItemChecklist ToDomain() => new(
        ActividadId, Nombre, Orden, ActividadActiva, Estado, Observacion, RevisadoPor,
        KbTiempo.ComoOffset(RevisadoEn), ActualizadoPor, KbTiempo.ComoOffset(ActualizadoEn));
}

internal sealed class ResumenDiaFila
{
    public DateOnly Fecha { get; init; }
    public int Ok { get; init; }
    public int Advertencia { get; init; }
    public int Falla { get; init; }
    public int Critico { get; init; }
    public int NoAplica { get; init; }
    public int Total { get; init; }
}

internal sealed class CambioChecklistFila
{
    public string Grupo { get; init; } = string.Empty;
    public string ActividadNombre { get; init; } = string.Empty;
    public DateOnly Fecha { get; init; }
    public EstadoChecklist Estado { get; init; }
    public string? Observacion { get; init; }
    public string Usuario { get; init; } = string.Empty;
    public DateTime Cuando { get; init; }
}

internal static class BitacoraConsultas
{
    public static IQueryable<BitacoraActividad> Actividades(KbDbContext db, string grupo, bool incluirInactivas) =>
        db.BitacoraActividades
            .Where(a => a.Grupo == grupo && (incluirInactivas || a.Activa))
            .OrderBy(a => a.Orden).ThenBy(a => a.Nombre);

    /// <summary>
    /// Actividades activas hoy + las que tengan registro ese día aunque ya se hayan desactivado (no se pierde historial).
    /// El registro del día entra por LEFT JOIN: una actividad sin registro queda pendiente (Estado nulo).
    /// </summary>
    public static IQueryable<ItemChecklistFila> Checklist(KbDbContext db, string grupo, DateOnly fecha) =>
        from a in db.BitacoraActividades
        where a.Grupo == grupo
        join c0 in db.BitacoraChecklist.Where(c => c.Fecha == fecha) on a.Id equals c0.ActividadId into cs
        from c in cs.DefaultIfEmpty()
        where a.Activa || c != null
        orderby a.Orden, a.Nombre
        select new ItemChecklistFila
        {
            ActividadId = a.Id,
            Nombre = a.Nombre,
            Orden = a.Orden,
            ActividadActiva = a.Activa,
            Estado = c == null ? null : c.Estado,
            Observacion = c!.Observacion,
            RevisadoPor = c.CreadoPor,
            RevisadoEn = c == null ? null : c.CreadoEn,
            ActualizadoPor = c.ActualizadoPor,
            ActualizadoEn = c.ActualizadoEn,
        };

    /// <summary>El registro de una actividad en un día, ya guardado (INNER JOIN: tiene que existir).</summary>
    public static IQueryable<ItemChecklistFila> ItemGuardado(KbDbContext db, int actividadId, DateOnly fecha) =>
        from a in db.BitacoraActividades
        join c in db.BitacoraChecklist on a.Id equals c.ActividadId
        where a.Id == actividadId && c.Fecha == fecha
        select new ItemChecklistFila
        {
            ActividadId = a.Id,
            Nombre = a.Nombre,
            Orden = a.Orden,
            ActividadActiva = a.Activa,
            Estado = c.Estado,
            Observacion = c.Observacion,
            RevisadoPor = c.CreadoPor,
            RevisadoEn = c.CreadoEn,
            ActualizadoPor = c.ActualizadoPor,
            ActualizadoEn = c.ActualizadoEn,
        };

    public static IQueryable<ResumenDiaFila> ResumenPorDia(KbDbContext db, string grupo, DateOnly desde, DateOnly hasta) =>
        from c in db.BitacoraChecklist
        join a in db.BitacoraActividades on c.ActividadId equals a.Id
        where a.Grupo == grupo && c.Fecha >= desde && c.Fecha <= hasta
        group c by c.Fecha into g
        select new ResumenDiaFila
        {
            Fecha = g.Key,
            Ok = g.Count(x => x.Estado == EstadoChecklist.Ok),
            Advertencia = g.Count(x => x.Estado == EstadoChecklist.Advertencia),
            Falla = g.Count(x => x.Estado == EstadoChecklist.Falla),
            Critico = g.Count(x => x.Estado == EstadoChecklist.Critico),
            NoAplica = g.Count(x => x.Estado == EstadoChecklist.NoAplica),
            Total = g.Count(),
        };

    /// <summary>Los últimos guardados de cualquier checklist: la fecha del cambio es la de actualización, o la de creación si nunca se corrigió.</summary>
    public static IQueryable<CambioChecklistFila> UltimosCambios(KbDbContext db, int maximo) =>
        (from c in db.BitacoraChecklist
         join a in db.BitacoraActividades on c.ActividadId equals a.Id
         orderby (c.ActualizadoEn ?? c.CreadoEn) descending
         select new CambioChecklistFila
         {
             Grupo = a.Grupo,
             ActividadNombre = a.Nombre,
             Fecha = c.Fecha,
             Estado = c.Estado,
             Observacion = c.Observacion,
             Usuario = c.ActualizadoPor ?? c.CreadoPor,
             Cuando = c.ActualizadoEn ?? c.CreadoEn,
         }).Take(maximo);
}
