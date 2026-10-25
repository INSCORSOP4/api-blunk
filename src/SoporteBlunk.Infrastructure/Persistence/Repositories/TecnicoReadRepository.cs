using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Queries;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

/// <summary>
/// En ServiceDesk no hay una tabla simple de técnicos: se consideran técnicos los usuarios
/// que aparecen como propietario (OWNERID) de algún ticket.
/// </summary>
internal sealed class TecnicoReadRepository(ServiceDeskDbContext db) : ITecnicoReadRepository
{
    public async Task<IReadOnlyList<Tecnico>> ListarAsync(CancellationToken ct)
    {
        var filas = await TecnicoConsultas.ListaOrdenada(db).ToListAsync(ct);
        return filas.ConvertAll(f => new Tecnico(f.TecnicoId!.Value, Nombre(f.TecnicoId, f.Nombre, f.Apellido)));
    }

    public async Task<IReadOnlyList<TecnicoCarga>> CargaAsync(CancellationToken ct)
    {
        var filas = await TecnicoConsultas.Carga(db, null).ToListAsync(ct);
        return filas.ConvertAll(ACarga);
    }

    public async Task<TecnicoCarga?> CargaAsync(long tecnicoId, CancellationToken ct)
    {
        var fila = await TecnicoConsultas.Carga(db, tecnicoId).FirstOrDefaultAsync(ct);
        return fila is null ? null : ACarga(fila);
    }

    public async Task<TecnicoProductividad?> ProductividadAsync(long tecnicoId, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var fila = await TecnicoConsultas.Base(db).DelTecnico(tecnicoId, desde, hasta).Productividad().FirstOrDefaultAsync(ct);
        if (fila is not null && fila.Asignados > 0) return AProductividad(fila);

        // Sin tickets en el rango: distinguir "técnico inexistente" de "técnico sin actividad".
        var conocido = await TecnicoConsultas.TecnicosConNombre(db).Where(x => x.TecnicoId == tecnicoId).FirstOrDefaultAsync(ct);
        return conocido is null
            ? null
            : new TecnicoProductividad(tecnicoId, Nombre(tecnicoId, conocido.Nombre, conocido.Apellido), 0, 0, 0, 100, null, null);
    }

    public async Task<IReadOnlyList<TecnicoProductividad>> ListarProductividadAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var filas = await TecnicoConsultas.Base(db).DelTecnico(null, desde, hasta).Productividad().ToListAsync(ct);
        return filas.ConvertAll(AProductividad);
    }

    public async Task<IReadOnlyList<ProductividadPunto>> TendenciaProductividadAsync(
        long tecnicoId, DateOnly desde, DateOnly hasta, Agrupacion agrupacion, CancellationToken ct)
    {
        // El periodo (día/semana/mes en hora de México) se arma en código: LINQ no traduce AT TIME ZONE.
        var filas = await TecnicoConsultas.FilasParaTendencia(db, tecnicoId, desde, hasta).ToListAsync(ct);
        var porPeriodo = filas
            .GroupBy(f => Inicio(DateOnly.FromDateTime(TiempoServiceDesk.AFecha(f.Creado)!.Value.DateTime), agrupacion))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Rellena con periodos vacíos (0 asignados, SLA 100%, sin promedio) para que la serie no tenga huecos.
        var puntos = new List<ProductividadPunto>();
        var actual = Inicio(desde, agrupacion);
        var fin = Inicio(hasta, agrupacion);
        while (actual <= fin)
        {
            puntos.Add(porPeriodo.TryGetValue(actual, out var grupo) ? Punto(actual, grupo) : new ProductividadPunto(actual, 0, 0, 100, null, null));
            actual = agrupacion switch
            {
                Agrupacion.Mes => actual.AddMonths(1),
                Agrupacion.Semana => actual.AddDays(7),
                _ => actual.AddDays(1),
            };
        }
        return puntos;
    }

    private static ProductividadPunto Punto(DateOnly periodo, List<TendenciaFila> filas)
    {
        var vencidos = filas.Count(f => f.Vencido == true);
        return new ProductividadPunto(
            periodo, filas.Count, filas.Count(f => f.Pendiente == false), SlaPct(filas.Count, vencidos),
            Promedio(filas.Where(f => f.Respondido > f.Asignado).Select(f => (f.Respondido!.Value - f.Asignado!.Value) / 3600000.0)),
            Promedio(filas.Where(f => f.Pendiente == false && f.Completado > f.Creado).Select(f => (f.Completado!.Value - f.Creado!.Value) / 3600000.0)));
    }

    private static double? Promedio(IEnumerable<double> valores)
    {
        var lista = valores.ToList();
        return lista.Count == 0 ? null : lista.Average();
    }

    private static string Nombre(long? id, string? nombre, string? apellido) =>
        TiempoServiceDesk.NombreCompleto(nombre, apellido) ?? $"Usuario {id}";

    private static TecnicoCarga ACarga(CargaFila f) =>
        new(f.TecnicoId!.Value, Nombre(f.TecnicoId, f.Nombre, f.Apellido), f.Abiertos, f.Vencidos, f.Total);

    private static TecnicoProductividad AProductividad(ProductividadFila f) => new(
        f.TecnicoId!.Value, Nombre(f.TecnicoId, f.Nombre, f.Apellido), f.Asignados, f.Cerrados, f.Vencidos,
        SlaPct(f.Asignados, f.Vencidos), f.PromedioHorasPrimeraRespuesta, f.PromedioHorasResolucion);

    private static double SlaPct(int asignados, int vencidos) =>
        asignados == 0 ? 100 : Math.Round((asignados - vencidos) * 100.0 / asignados, 2);

    // Mismo criterio de "inicio de semana/mes" que el Tablero (Application.Features.Dashboard), duplicado a propósito
    // para no acoplar este repositorio al de Dashboard por un detalle tan pequeño.
    private static DateOnly Inicio(DateOnly d, Agrupacion a) => a switch
    {
        Agrupacion.Mes => new DateOnly(d.Year, d.Month, 1),
        Agrupacion.Semana => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
        _ => d,
    };
}
