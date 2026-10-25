using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// Ninguna consulta de ServiceDesk debe producir avisos de EF Core (por ejemplo "First/FirstOrDefault sin OrderBy ni filtro"):
/// llenan el registro de la API y suelen indicar una consulta ambigua. EF los emite al traducir la consulta, antes de abrir la
/// conexión, así que se detectan sin base de datos usando un servidor inalcanzable.
/// </summary>
public class AvisosEfTests
{
    private const string ServidorCaido = "Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True";

    private static async Task<List<string>> Avisos(Func<ServiceDeskDbContext, Task> operacion)
    {
        var avisos = new List<string>();
        var opciones = new DbContextOptionsBuilder<ServiceDeskDbContext>();
        ServiceDeskDbContext.Configurar(opciones, ServidorCaido);
        opciones.LogTo(avisos.Add, (_, nivel) => nivel == LogLevel.Warning, DbContextLoggerOptions.DefaultWithLocalTime);

        await using var db = new ServiceDeskDbContext(opciones.Options);
        try { await operacion(db); }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException { InnerException: Microsoft.Data.SqlClient.SqlException }) { }
        return avisos;
    }

    private static readonly DateOnly Desde = new(2026, 1, 1);
    private static readonly DateOnly Hasta = new(2026, 1, 31);

    public static TheoryData<string> Operaciones => new(OperacionesPorNombre.Keys);

    private static readonly Dictionary<string, Func<ServiceDeskDbContext, Task>> OperacionesPorNombre = new()
    {
        ["dashboard.resumen"] = db => new DashboardReadRepository(db).ResumenAsync(Desde, Hasta, default),
        ["dashboard.sla"] = db => new DashboardReadRepository(db).SlaAsync(Desde, Hasta, default),
        ["dashboard.tendencia"] = db => new DashboardReadRepository(db).TendenciaAsync(Desde, Hasta, Agrupacion.Dia, default),
        ["dashboard.por-categoria"] = db => new DashboardReadRepository(db).PorAsync("categoria", Desde, Hasta, default),
        ["dashboard.por-tecnico"] = db => new DashboardReadRepository(db).PorAsync("tecnico", Desde, Hasta, default),
        ["tickets.listar"] = db => new TicketReadRepository(db).ListarAsync(new TicketFiltro(), TicketOrden.CreadoDesc, 1, 10, default),
        ["tickets.exportar"] = db => new TicketReadRepository(db).ListarParaExportAsync(new TicketFiltro(), 10, default),
        ["tickets.detalle"] = db => new TicketReadRepository(db).ObtenerAsync(1, default),
        ["tickets.historial"] = db => new TicketReadRepository(db).HistorialAsync(1, default),
        ["tickets.tiempos"] = db => new TicketReadRepository(db).TiemposAsync(1, default),
        ["tecnicos.listar"] = db => new TecnicoReadRepository(db).ListarAsync(default),
        ["tecnicos.carga"] = db => new TecnicoReadRepository(db).CargaAsync(default),
        ["tecnicos.carga-uno"] = db => new TecnicoReadRepository(db).CargaAsync(1, default),
        ["tecnicos.productividad"] = db => new TecnicoReadRepository(db).ProductividadAsync(1, Desde, Hasta, default),
        ["tecnicos.equipo"] = db => new TecnicoReadRepository(db).ListarProductividadAsync(Desde, Hasta, default),
        ["catalogos.estados"] = db => new CatalogoReadRepository(db).ObtenerAsync("estados", default),
    };

    [Theory]
    [MemberData(nameof(Operaciones))]
    public async Task La_consulta_no_produce_avisos_de_EF(string operacion)
    {
        var avisos = await Avisos(OperacionesPorNombre[operacion]);

        avisos.Should().BeEmpty($"{operacion} no debe emitir avisos de EF Core");
    }
}
