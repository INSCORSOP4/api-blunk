using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Queries;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class TicketReadRepository(ServiceDeskDbContext db) : ITicketReadRepository
{
    private const int MaxHistorial = 500;

    public async Task<PagedResult<TicketResumen>> ListarAsync(TicketFiltro filtro, TicketOrden orden, int page, int pageSize, CancellationToken ct)
    {
        var consulta = TicketConsultas.Base(db).Filtrar(filtro);

        var total = await consulta.CountAsync(ct);
        var filas = await consulta.Ordenar(orden)
            .ProyectarResumen()
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<TicketResumen>(filas.ConvertAll(f => f.ToDomain()), page, pageSize, total);
    }

    public async Task<IReadOnlyList<TicketResumen>> ListarParaExportAsync(TicketFiltro filtro, int maximo, CancellationToken ct)
    {
        var filas = await TicketConsultas.Base(db).Filtrar(filtro)
            .Ordenar(TicketOrden.CreadoDesc)
            .ProyectarResumen()
            .Take(maximo)
            .ToListAsync(ct);

        return filas.ConvertAll(f => f.ToDomain());
    }

    public async Task<TicketDetalle?> ObtenerAsync(long id, CancellationToken ct)
    {
        var fila = await TicketDetalleConsultas.Detalle(db, id).FirstOrDefaultAsync(ct);
        if (fila is null) return null;

        return new TicketDetalle(
            fila.Resumen.ToDomain(), fila.Descripcion, fila.Resolucion, fila.Subcategoria, fila.Item, fila.Urgencia,
            fila.Impacto, fila.Modo, fila.TipoSolicitud, fila.Nivel, fila.Departamento, fila.Reabierto ?? false,
            TiempoServiceDesk.AFecha(fila.Respondido), TiempoServiceDesk.AFecha(fila.Resuelto));
    }

    public Task<bool> ExisteAsync(long id, CancellationToken ct) => db.Tickets.AnyAsync(w => w.Id == id, ct);

    public async Task<IReadOnlyList<TicketHistorialItem>> HistorialAsync(long id, CancellationToken ct)
    {
        var filas = await TicketDetalleConsultas.Historial(db, id).Take(MaxHistorial).ToListAsync(ct);

        return filas.ConvertAll(f => new TicketHistorialItem(
            f.Id, TiempoServiceDesk.AFecha(f.Fecha), f.Operacion, f.Descripcion,
            TiempoServiceDesk.NombreCompleto(f.UsuarioNombre, f.UsuarioApellido)));
    }

    public async Task<TicketTiempos> TiemposAsync(long id, CancellationToken ct)
    {
        var estados = await TicketDetalleConsultas.TiempoPorEstado(db, id).ToListAsync(ct);
        var tecnicos = await TicketDetalleConsultas.TiempoPorTecnico(db, id).ToListAsync(ct);
        var grupos = await TicketDetalleConsultas.TiempoPorGrupo(db, id).ToListAsync(ct);

        return new TicketTiempos(
            estados.ConvertAll(e => new TicketTiempoEstado(e.Estado ?? string.Empty, TiempoServiceDesk.AFecha(e.Fin), e.TiempoRegistrado ?? 0)),
            tecnicos.ConvertAll(t => new TicketTiempoTecnico(
                TiempoServiceDesk.NombreCompleto(t.UsuarioNombre, t.UsuarioApellido), TiempoServiceDesk.AFecha(t.Fin), t.TiempoRegistrado ?? 0)),
            grupos.ConvertAll(g => new TicketTiempoGrupo(g.Grupo, TiempoServiceDesk.AFecha(g.Fin), g.TiempoRegistrado ?? 0)));
    }
}
