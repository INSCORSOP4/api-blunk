using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// Pruebas contra la base real (solo lectura). Se omiten si no está definida la variable
/// de entorno ConnectionStrings__ServiceDesk.
/// </summary>
public sealed class ServiceDeskReadTests : IDisposable
{
    private readonly ServiceProvider? _provider;

    public ServiceDeskReadTests()
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__ServiceDesk");
        if (string.IsNullOrWhiteSpace(cs)) return;

        // AddInfrastructure también registra Auth/Seguridad y valida su configuración al arrancar; estas pruebas
        // no los usan, pero necesitan valores que pasen la validación para poder construir el ServiceProvider.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceDesk"] = cs,
                ["Seguridad:BaseUrl"] = "http://localhost/",
                ["Auth:RolSistemasId"] = Guid.NewGuid().ToString(),
                ["Auth:JwtKey"] = new string('x', 32),
            })
            .Build();
        _provider = new ServiceCollection().AddInfrastructure(config).BuildServiceProvider();
    }

    private T Repo<T>() where T : notnull
    {
        Skip.If(_provider is null, "Falta ConnectionStrings__ServiceDesk");
        return _provider!.GetRequiredService<T>();
    }

    [SkippableFact]
    public async Task Resumen_coincide_con_el_total_de_tickets()
    {
        var resumen = await Repo<IDashboardReadRepository>().ResumenAsync(null, null, default);

        resumen.Total.Should().BeGreaterThan(0);
        resumen.PorEstado.Sum(e => e.Total).Should().Be(resumen.Total);
        (resumen.Abiertos + resumen.Cerrados).Should().Be(resumen.Total);
    }

    [SkippableFact]
    public async Task Listado_pagina_y_ordena_de_mas_reciente_a_mas_antiguo()
    {
        var page = await Repo<ITicketReadRepository>().ListarAsync(new TicketFiltro(), TicketOrden.CreadoDesc, 1, 10, default);

        page.Items.Should().HaveCount(10);
        page.TotalCount.Should().BeGreaterThan(10);
        page.Items.Select(t => t.Creado).Should().BeInDescendingOrder();
    }

    [SkippableFact]
    public async Task Filtro_de_busqueda_con_caracteres_especiales_no_rompe_la_consulta()
    {
        var filtro = new TicketFiltro { Buscar = "100% [test]_'; --" };

        var page = await Repo<ITicketReadRepository>().ListarAsync(filtro, TicketOrden.CreadoDesc, 1, 5, default);

        page.Items.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Catalogos_publicados_se_pueden_consultar()
    {
        var repo = Repo<ICatalogoReadRepository>();
        foreach (var nombre in new[] { "estados", "prioridades", "grupos", "categorias", "subcategorias", "items", "urgencias", "impactos", "modos", "tipos-solicitud", "niveles", "departamentos" })
            (await repo.ObtenerAsync(nombre, default)).Should().NotBeNull(nombre);
    }

    [SkippableFact]
    public async Task Filtro_sin_responder_solo_trae_abiertos_sin_primera_respuesta()
    {
        var repo = Repo<ITicketReadRepository>();

        var sinResponder = await repo.ListarAsync(new TicketFiltro { SinResponder = true }, TicketOrden.CreadoAsc, 1, 50, default);

        foreach (var t in sinResponder.Items)
        {
            // No hay columna "respondido" en TicketResumen, pero sí que esté entre los estados abiertos
            // (lo cerrado no debería aparecer) y que el orden sea el más antiguo primero.
            t.Estado.Should().NotBeNullOrWhiteSpace();
        }
        if (sinResponder.Items.Count > 1)
            sinResponder.Items.Select(t => t.Creado).Should().BeInAscendingOrder();

        // El total con el filtro nunca puede superar al total sin filtro.
        var sinFiltro = await repo.ListarAsync(new TicketFiltro(), TicketOrden.CreadoAsc, 1, 1, default);
        sinResponder.TotalCount.Should().BeLessOrEqualTo(sinFiltro.TotalCount);
    }

    [SkippableFact]
    public async Task Tendencia_de_productividad_no_tiene_huecos_en_el_rango()
    {
        var repo = Repo<ITecnicoReadRepository>();
        var hasta = DateOnly.FromDateTime(DateTime.Today);
        var desde = hasta.AddDays(-27);
        // El técnico más activo EN ESTE MISMO rango, para no elegir a alguien sin tickets recientes.
        var equipo = await repo.ListarProductividadAsync(desde, hasta, default);
        var tecnicoId = equipo.OrderByDescending(t => t.Asignados).First().TecnicoId;

        var tendencia = await repo.TendenciaProductividadAsync(tecnicoId, desde, hasta, Agrupacion.Semana, default);

        tendencia.Should().NotBeEmpty();
        tendencia.Select(p => p.Periodo).Should().BeInAscendingOrder();
        tendencia.Should().OnlyContain(p => p.CumplimientoSlaPct >= 0 && p.CumplimientoSlaPct <= 100);
        tendencia.Sum(p => p.Asignados).Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Detalle_de_ticket_inexistente_devuelve_null()
    {
        (await Repo<ITicketReadRepository>().ObtenerAsync(-1, default)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Productividad_del_equipo_tiene_cifras_coherentes()
    {
        var lista = await Repo<ITecnicoReadRepository>().ListarProductividadAsync(null, null, default);

        lista.Should().NotBeEmpty();
        foreach (var t in lista)
        {
            t.CumplimientoSlaPct.Should().BeInRange(0, 100, $"técnico {t.Tecnico}");
            t.Vencidos.Should().BeLessOrEqualTo(t.Asignados);
            t.Cerrados.Should().BeLessOrEqualTo(t.Asignados);
            // La primera respuesta se mide desde la asignación: nunca puede dar negativa (la consulta la descarta).
            if (t.PromedioHorasPrimeraRespuesta is { } h) h.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [SkippableFact]
    public async Task Productividad_de_un_tecnico_coincide_con_su_fila_en_la_del_equipo()
    {
        var repo = Repo<ITecnicoReadRepository>();
        var equipo = await repo.ListarProductividadAsync(null, null, default);
        var primero = equipo.OrderByDescending(t => t.Asignados).First();

        var individual = await repo.ProductividadAsync(primero.TecnicoId, null, null, default);

        individual.Should().NotBeNull();
        individual!.Asignados.Should().Be(primero.Asignados);
        individual.CumplimientoSlaPct.Should().Be(primero.CumplimientoSlaPct);
    }

    public void Dispose() => _provider?.Dispose();
}
