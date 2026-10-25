using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

internal sealed class DetalleFila
{
    public required ResumenFila Resumen { get; init; }
    public string? Descripcion { get; init; }
    public string? Resolucion { get; init; }
    public string? Subcategoria { get; init; }
    public string? Item { get; init; }
    public string? Urgencia { get; init; }
    public string? Impacto { get; init; }
    public string? Modo { get; init; }
    public string? TipoSolicitud { get; init; }
    public string? Nivel { get; init; }
    public string? Departamento { get; init; }
    public bool? Reabierto { get; init; }
    public long? Respondido { get; init; }
    public long? Resuelto { get; init; }
}

internal sealed class HistorialFila
{
    public long Id { get; init; }
    public long? Fecha { get; init; }
    public string? Operacion { get; init; }
    public string? Descripcion { get; init; }
    public string? UsuarioNombre { get; init; }
    public string? UsuarioApellido { get; init; }
}

internal sealed class TiempoEstadoFila
{
    public string? Estado { get; init; }
    public long? Fin { get; init; }
    public long? TiempoRegistrado { get; init; }
}

internal sealed class TiempoTecnicoFila
{
    public string? UsuarioNombre { get; init; }
    public string? UsuarioApellido { get; init; }
    public long? Fin { get; init; }
    public long? TiempoRegistrado { get; init; }
}

internal sealed class TiempoGrupoFila
{
    public string? Grupo { get; init; }
    public long? Fin { get; init; }
    public long? TiempoRegistrado { get; init; }
}

/// <summary>Consultas de una solicitud concreta: detalle, historial y tiempos.</summary>
internal static class TicketDetalleConsultas
{
    // Los catálogos se consultan con subconsultas por clave primaria: equivalen al LEFT JOIN que tenía el SQL.
    public static IQueryable<DetalleFila> Detalle(ServiceDeskDbContext db, long id) =>
        TicketConsultas.Base(db)
            .Where(x => x.W.Id == id)
            .Select(x => new DetalleFila
            {
                Resumen = new ResumenFila
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
                },
                Descripcion = db.TicketDescripciones.Where(d => d.TicketId == x.W.Id).Select(d => d.Descripcion).FirstOrDefault(),
                Resolucion = db.TicketResoluciones.Where(r => r.TicketId == x.W.Id)
                    .OrderByDescending(r => r.Actualizado).Select(r => r.Resolucion).FirstOrDefault(),
                Subcategoria = db.Subcategorias.Where(c => c.Id == x.S.SubcategoriaId).Select(c => c.Nombre).FirstOrDefault(),
                Item = db.Items.Where(c => c.Id == x.S.ItemId).Select(c => c.Nombre).FirstOrDefault(),
                Urgencia = db.Urgencias.Where(c => c.Id == x.S.UrgenciaId).Select(c => c.Nombre).FirstOrDefault(),
                Impacto = db.Impactos.Where(c => c.Id == x.S.ImpactoId).Select(c => c.Nombre).FirstOrDefault(),
                Modo = db.Modos.Where(c => c.Id == x.W.ModoId).Select(c => c.Nombre).FirstOrDefault(),
                TipoSolicitud = db.TiposSolicitud.Where(c => c.Id == x.S.TipoSolicitudId).Select(c => c.Nombre).FirstOrDefault(),
                Nivel = db.Niveles.Where(c => c.Id == x.S.NivelId).Select(c => c.Nombre).FirstOrDefault(),
                Departamento = db.Departamentos.Where(c => c.Id == x.W.DepartamentoId).Select(c => c.Nombre).FirstOrDefault(),
                Reabierto = x.S.Reabierto,
                Respondido = x.W.Respondido,
                Resuelto = x.W.Resuelto,
            });

    /// <summary>Historial de la solicitud, del más reciente al más antiguo (el tope de 500 filas lo pone quien la usa).</summary>
    public static IQueryable<HistorialFila> Historial(ServiceDeskDbContext db, long id) =>
        from h in db.TicketHistorial
        where h.TicketId == id
        join u0 in db.Usuarios on h.UsuarioId equals (long?)u0.Id into us
        from u in us.DefaultIfEmpty()
        orderby h.Fecha descending, h.Id descending
        select new HistorialFila
        {
            Id = h.Id,
            Fecha = h.Fecha,
            Operacion = h.Operacion,
            Descripcion = h.Descripcion,
            UsuarioNombre = u!.Nombre,
            UsuarioApellido = u!.Apellido,
        };

    public static IQueryable<TiempoEstadoFila> TiempoPorEstado(ServiceDeskDbContext db, long id) =>
        from a in db.TicketEvaluaciones
        where a.TicketId == id
        join i in db.TiempoPorEstado on a.Id equals i.AssessmentId
        join sd in db.Estados on i.EstadoId equals (long?)sd.Id
        orderby a.Ejecutado, a.Id
        select new TiempoEstadoFila { Estado = sd.Nombre, Fin = i.Fin, TiempoRegistrado = i.TiempoRegistrado };

    public static IQueryable<TiempoTecnicoFila> TiempoPorTecnico(ServiceDeskDbContext db, long id) =>
        from a in db.TicketEvaluaciones
        where a.TicketId == id
        join i in db.TiempoPorTecnico on a.Id equals i.AssessmentId
        join u0 in db.Usuarios on i.TecnicoId equals (long?)u0.Id into us
        from u in us.DefaultIfEmpty()
        orderby a.Ejecutado, a.Id
        select new TiempoTecnicoFila
        {
            UsuarioNombre = u!.Nombre,
            UsuarioApellido = u!.Apellido,
            Fin = i.Fin,
            TiempoRegistrado = i.TiempoRegistrado,
        };

    public static IQueryable<TiempoGrupoFila> TiempoPorGrupo(ServiceDeskDbContext db, long id) =>
        from a in db.TicketEvaluaciones
        where a.TicketId == id
        join i in db.TiempoPorGrupo on a.Id equals i.AssessmentId
        join qd0 in db.Grupos on i.GrupoId equals (long?)qd0.Id into qds
        from qd in qds.DefaultIfEmpty()
        orderby a.Ejecutado, a.Id
        select new TiempoGrupoFila { Grupo = qd!.Nombre, Fin = i.Fin, TiempoRegistrado = i.TiempoRegistrado };
}
