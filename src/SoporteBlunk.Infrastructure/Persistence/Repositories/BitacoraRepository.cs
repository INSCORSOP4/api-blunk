using FluentValidation;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Entities.Kb;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Queries;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class BitacoraRepository(KbDbContext db) : IBitacoraRepository
{
    public async Task<IReadOnlyList<ActividadBitacora>> ListarActividadesAsync(string grupo, bool incluirInactivas, CancellationToken ct)
    {
        var filas = await BitacoraConsultas.Actividades(db, grupo, incluirInactivas).ToListAsync(ct);
        return filas.ConvertAll(Mapear);
    }

    public async Task<ActividadBitacora?> ObtenerActividadAsync(int actividadId, CancellationToken ct)
    {
        var fila = await db.BitacoraActividades.FirstOrDefaultAsync(a => a.Id == actividadId, ct);
        return fila is null ? null : Mapear(fila);
    }

    public async Task<ActividadBitacora> CrearActividadAsync(string grupo, string nombre, string usuario, CancellationToken ct)
    {
        int id;
        try
        {
            // El siguiente Orden es el máximo actual del grupo + 1 (se agrega al final).
            var maximo = await db.BitacoraActividades.Where(a => a.Grupo == grupo).MaxAsync(a => (int?)a.Orden, ct) ?? 0;
            var nueva = new BitacoraActividad { Grupo = grupo, Nombre = nombre, Orden = maximo + 1, Activa = true, CreadoPor = usuario };
            db.BitacoraActividades.Add(nueva);
            await db.SaveChangesAsync(ct);
            id = nueva.Id;
        }
        catch (Exception ex) when (EsLlaveDuplicada(ex)) // violación de UQ (Grupo, Nombre)
        {
            db.ChangeTracker.Clear();
            throw new ValidationException($"Ya existe una actividad llamada \"{nombre}\" en esta bitácora.");
        }
        catch (Exception ex) when (EsErrorDeBase(ex))
        {
            db.ChangeTracker.Clear();
            throw new KbNoDisponibleException("No se pudo crear la actividad en la bitácora.", ex);
        }
        return (await ObtenerActividadAsync(id, ct))!;
    }

    public async Task<ActividadBitacora> ActualizarActividadAsync(
        int actividadId, string? nombre, bool? activa, int? orden, string usuario, CancellationToken ct)
    {
        try
        {
            // Los parámetros nulos no se tocan (COALESCE con el valor actual). La hora la pone el servidor SQL (GETDATE()).
            await db.BitacoraActividades.Where(a => a.Id == actividadId).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Nombre, a => nombre ?? a.Nombre)
                .SetProperty(a => a.Activa, a => activa ?? a.Activa)
                .SetProperty(a => a.Orden, a => orden ?? a.Orden)
                .SetProperty(a => a.ActualizadoPor, usuario)
                .SetProperty(a => a.ActualizadoEn, a => DateTime.Now), ct);
        }
        catch (Exception ex) when (EsLlaveDuplicada(ex))
        {
            throw new ValidationException($"Ya existe una actividad llamada \"{nombre}\" en esta bitácora.");
        }
        catch (Exception ex) when (EsErrorDeBase(ex))
        {
            throw new KbNoDisponibleException("No se pudo actualizar la actividad de la bitácora.", ex);
        }
        return (await ObtenerActividadAsync(actividadId, ct))!;
    }

    public async Task<ChecklistDia> ObtenerChecklistAsync(string grupo, DateOnly fecha, CancellationToken ct)
    {
        var filas = await BitacoraConsultas.Checklist(db, grupo, fecha).ToListAsync(ct);
        return new ChecklistDia(grupo, fecha, filas.ConvertAll(f => f.ToDomain()));
    }

    public async Task<ItemChecklist> GuardarChecklistAsync(
        int actividadId, DateOnly fecha, EstadoChecklist estado, string? observacion, string usuario, CancellationToken ct)
    {
        try
        {
            // Equivale al MERGE anterior: corrige el registro del día si ya existe y, si no, lo crea.
            if (await CorregirAsync(actividadId, fecha, estado, observacion, usuario, ct) == 0)
            {
                try
                {
                    db.BitacoraChecklist.Add(new BitacoraChecklist
                    {
                        Fecha = fecha, ActividadId = actividadId, Estado = estado, Observacion = observacion, CreadoPor = usuario,
                    });
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (EsLlaveDuplicada(ex))
                {
                    // Otro usuario creó el mismo registro entre el UPDATE y el INSERT: ahora existe, se corrige.
                    db.ChangeTracker.Clear();
                    await CorregirAsync(actividadId, fecha, estado, observacion, usuario, ct);
                }
            }
        }
        catch (Exception ex) when (EsErrorDeBase(ex))
        {
            db.ChangeTracker.Clear();
            throw new KbNoDisponibleException("No se pudo guardar el estado en la bitácora.", ex);
        }

        var fila = await BitacoraConsultas.ItemGuardado(db, actividadId, fecha).SingleAsync(ct);
        return fila.ToDomain();
    }

    public async Task<IReadOnlyList<ResumenDia>> ObtenerResumenAsync(string grupo, DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var filas = await BitacoraConsultas.ResumenPorDia(db, grupo, desde, hasta).ToListAsync(ct);

        // El total de actividades activas de hoy sirve para calcular "pendientes" aunque el día no tenga ningún registro.
        var totalActivas = await db.BitacoraActividades.CountAsync(a => a.Grupo == grupo && a.Activa, ct);

        var porFecha = filas.ToDictionary(f => f.Fecha);
        var resultado = new List<ResumenDia>();
        for (var fecha = desde; fecha <= hasta; fecha = fecha.AddDays(1))
        {
            if (porFecha.TryGetValue(fecha, out var f))
                resultado.Add(new ResumenDia(fecha, f.Ok, f.Advertencia, f.Falla, f.Critico, f.NoAplica,
                    Math.Max(0, totalActivas - f.Total), totalActivas));
            else
                resultado.Add(new ResumenDia(fecha, 0, 0, 0, 0, 0, totalActivas, totalActivas));
        }
        return resultado;
    }

    public async Task<IReadOnlyList<CambioChecklist>> UltimosCambiosAsync(int maximo, CancellationToken ct)
    {
        var filas = await BitacoraConsultas.UltimosCambios(db, maximo).ToListAsync(ct);
        return filas.ConvertAll(f => new CambioChecklist(
            f.Grupo, f.ActividadNombre, f.Fecha, f.Estado, f.Observacion, f.Usuario, KbTiempo.ComoOffset(f.Cuando)!.Value));
    }

    private Task<int> CorregirAsync(int actividadId, DateOnly fecha, EstadoChecklist estado, string? observacion, string usuario, CancellationToken ct) =>
        db.BitacoraChecklist.Where(c => c.Fecha == fecha && c.ActividadId == actividadId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Estado, estado)
            .SetProperty(c => c.Observacion, observacion)
            .SetProperty(c => c.ActualizadoPor, usuario)
            .SetProperty(c => c.ActualizadoEn, c => DateTime.Now), ct);

    private static ActividadBitacora Mapear(BitacoraActividad a) => new(a.Id, a.Grupo, a.Nombre, a.Orden, a.Activa);

    /// <summary>Violación de una restricción UNIQUE o de la clave primaria (2627), venga directa o envuelta por EF.</summary>
    internal static bool EsLlaveDuplicada(Exception ex) => Sql(ex) is { Number: 2627 or 2601 };

    /// <summary>Error de la base (conexión, restricción, tiempo de espera), con o sin el envoltorio de EF. Las excepciones propias pasan de largo.</summary>
    internal static bool EsErrorDeBase(Exception ex) => ex is DbUpdateException or SqlException;

    private static SqlException? Sql(Exception ex) => ex as SqlException ?? ex.InnerException as SqlException;
}
