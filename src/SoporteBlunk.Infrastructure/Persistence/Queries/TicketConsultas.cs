using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Entities.ServiceDesk;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

/// <summary>
/// Fila intermedia de la consulta de solicitudes: las entidades unidas con los mismos joins que tenía el SQL original
/// (WorkOrder + WorkOrderStates + StatusDefinition obligatorios; el resto, LEFT JOIN).
/// </summary>
internal sealed class TicketFila
{
    public required WorkOrder W { get; init; }
    public required WorkOrderStates S { get; init; }
    public required StatusDefinition St { get; init; }
    public PriorityDefinition? Pr { get; init; }
    public CategoryDefinition? Cat { get; init; }
    public WorkOrderQueue? Q { get; init; }
    public QueueDefinition? Qd { get; init; }
    public AaaUser? Tu { get; init; }
    public AaaUser? Ru { get; init; }
}

/// <summary>Columnas de TicketResumen tal cual salen de la base (los tiempos en ms y los nombres sin armar).</summary>
internal sealed class ResumenFila
{
    public long Id { get; init; }
    public string? Titulo { get; init; }
    public long? EstadoId { get; init; }
    public string? Estado { get; init; }
    public string? Prioridad { get; init; }
    public string? Categoria { get; init; }
    public string? Grupo { get; init; }
    public long? TecnicoId { get; init; }
    public string? TecnicoNombre { get; init; }
    public string? TecnicoApellido { get; init; }
    public string? SolicitanteNombre { get; init; }
    public string? SolicitanteApellido { get; init; }
    public bool? Vencido { get; init; }
    public long? Creado { get; init; }
    public long? Vence { get; init; }
    public long? Completado { get; init; }

    public TicketResumen ToDomain() => new(
        Id, Titulo ?? string.Empty, EstadoId ?? 0, Estado ?? string.Empty, Prioridad, Categoria, Grupo,
        TecnicoId, TiempoServiceDesk.NombreCompleto(TecnicoNombre, TecnicoApellido),
        TiempoServiceDesk.NombreCompleto(SolicitanteNombre, SolicitanteApellido),
        Vencido ?? false, TiempoServiceDesk.AFecha(Creado), TiempoServiceDesk.AFecha(Vence), TiempoServiceDesk.AFecha(Completado));
}

internal static class TicketConsultas
{
    public static IQueryable<TicketFila> Base(ServiceDeskDbContext db) =>
        from w in db.Tickets
        join s in db.TicketEstados on w.Id equals s.TicketId
        join st in db.Estados on s.EstadoId equals (long?)st.Id
        join pr0 in db.Prioridades on s.PrioridadId equals (long?)pr0.Id into prs
        from pr in prs.DefaultIfEmpty()
        join cat0 in db.Categorias on s.CategoriaId equals (long?)cat0.Id into cats
        from cat in cats.DefaultIfEmpty()
        join q0 in db.TicketGrupos on w.Id equals q0.TicketId into qs
        from q in qs.DefaultIfEmpty()
        join qd0 in db.Grupos on q!.GrupoId equals (long?)qd0.Id into qds
        from qd in qds.DefaultIfEmpty()
        join tu0 in db.Usuarios on s.TecnicoId equals (long?)tu0.Id into tus
        from tu in tus.DefaultIfEmpty()
        join ru0 in db.Usuarios on w.SolicitanteId equals (long?)ru0.Id into rus
        from ru in rus.DefaultIfEmpty()
        select new TicketFila { W = w, S = s, St = st, Pr = pr, Cat = cat, Q = q, Qd = qd, Tu = tu, Ru = ru };

    public static IQueryable<TicketFila> Filtrar(this IQueryable<TicketFila> consulta, TicketFiltro f)
    {
        if (f.EstadoId is { } estado) consulta = consulta.Where(x => x.S.EstadoId == estado);
        if (f.TecnicoId is { } tecnico) consulta = consulta.Where(x => x.S.TecnicoId == tecnico);
        if (f.GrupoId is { } grupo) consulta = consulta.Where(x => x.Q!.GrupoId == grupo);
        if (f.CategoriaId is { } categoria) consulta = consulta.Where(x => x.S.CategoriaId == categoria);
        if (f.PrioridadId is { } prioridad) consulta = consulta.Where(x => x.S.PrioridadId == prioridad);

        if (f.Vencido is { } vencido)
            consulta = vencido
                ? consulta.Where(x => x.S.Vencido == true)
                : consulta.Where(x => (x.S.Vencido ?? false) == false);

        // "Sin responder" es un estado de negocio (abierto y sin primera respuesta), no una columna cruda.
        if (f.SinResponder is { } sinResponder)
            consulta = sinResponder
                ? consulta.Where(x => x.St.EsPendiente == true && x.W.Respondido == 0)
                : consulta.Where(x => !(x.St.EsPendiente == true && x.W.Respondido == 0));

        if (f.Desde is { } desde)
        {
            var desdeMs = TiempoServiceDesk.InicioDelDiaMs(desde);
            consulta = consulta.Where(x => x.W.Creado >= desdeMs);
        }
        if (f.Hasta is { } hasta)
        {
            var hastaMs = TiempoServiceDesk.InicioDelDiaMs(hasta.AddDays(1));
            consulta = consulta.Where(x => x.W.Creado < hastaMs);
        }

        if (!string.IsNullOrWhiteSpace(f.Buscar))
        {
            var texto = f.Buscar.Trim();
            // Mismo escape que antes (corchetes de SQL Server): los comodines del usuario se toman como texto literal.
            var patron = $"%{texto.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%";
            consulta = long.TryParse(texto, out var id)
                ? consulta.Where(x => x.W.Id == id || EF.Functions.Like(x.W.Titulo, patron))
                : consulta.Where(x => EF.Functions.Like(x.W.Titulo, patron));
        }

        return consulta;
    }

    public static IOrderedQueryable<TicketFila> Ordenar(this IQueryable<TicketFila> consulta, TicketOrden orden) => orden switch
    {
        TicketOrden.CreadoAsc => consulta.OrderBy(x => x.W.Creado).ThenBy(x => x.W.Id),
        TicketOrden.VenceAsc => consulta.OrderBy(x => x.W.Vence > 0 ? 0 : 1).ThenBy(x => x.W.Vence).ThenBy(x => x.W.Id),
        TicketOrden.IdDesc => consulta.OrderByDescending(x => x.W.Id),
        _ => consulta.OrderByDescending(x => x.W.Creado).ThenByDescending(x => x.W.Id),
    };

    public static IQueryable<ResumenFila> ProyectarResumen(this IQueryable<TicketFila> consulta) =>
        consulta.Select(x => new ResumenFila
        {
            Id = x.W.Id,
            Titulo = x.W.Titulo,
            EstadoId = x.S.EstadoId,
            Estado = x.St.Nombre,
            Prioridad = x.Pr!.Nombre,
            Categoria = x.Cat!.Nombre,
            Grupo = x.Qd!.Nombre,
            TecnicoId = x.S.TecnicoId,
            TecnicoNombre = x.Tu!.Nombre,
            TecnicoApellido = x.Tu!.Apellido,
            SolicitanteNombre = x.Ru!.Nombre,
            SolicitanteApellido = x.Ru!.Apellido,
            Vencido = x.S.Vencido,
            Creado = x.W.Creado,
            Vence = x.W.Vence,
            Completado = x.W.Completado,
        });
}
