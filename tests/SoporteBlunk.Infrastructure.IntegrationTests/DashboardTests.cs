using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence;
using SoporteBlunk.Infrastructure.Persistence.Queries;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>Dashboard de ServiceDesk migrado a EF Core: el SQL se revisa sin base de datos.</summary>
public class DashboardSqlTests
{
    private static string Normal(string sql) => Regex.Replace(sql, @"\s+", " ");

    public static TheoryData<string, string> Dimensiones => new()
    {
        { "estado", "GROUP BY [w0].[STATUSID], [s].[STATUSNAME]" },
        { "categoria", "GROUP BY [w0].[CATEGORYID], [c].[CATEGORYNAME]" },
        { "prioridad", "GROUP BY [w0].[PRIORITYID], [p].[PRIORITYNAME]" },
        { "grupo", "GROUP BY [w1].[QUEUEID], [q].[QUEUENAME]" },
        { "tecnico", "GROUP BY [w0].[OWNERID], [a].[FIRST_NAME], [a].[LAST_NAME]" },
        { "modo", "LEFT JOIN [ModeDefinition] AS [m] ON [w].[MODEID] = [m].[MODEID]" },
        { "urgencia", "LEFT JOIN [UrgencyDefinition]" },
        { "impacto", "LEFT JOIN [ImpactDefinition]" },
        { "tipo-solicitud", "LEFT JOIN [RequestTypeDefinition]" },
    };

    [Fact]
    public void El_resumen_cuenta_con_las_mismas_condiciones_sobre_los_mismos_joins()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Normal(DashboardConsultas.Resumen(DashboardConsultas.EnRango(db, new DateOnly(2026, 1, 1), null), 1000).ToQueryString());

        sql.Should().Contain("COUNT(*) AS [Total]");
        sql.Should().Contain("COUNT(CASE WHEN [s0].[ISPENDING] = CAST(1 AS bit) THEN 1 END) AS [Abiertos]");
        sql.Should().Contain("COUNT(CASE WHEN [s0].[ISPENDING] = CAST(0 AS bit) THEN 1 END) AS [Cerrados]");
        sql.Should().Contain("[s0].[ISPENDING] = CAST(1 AS bit) AND [s0].[ISOVERDUE] = CAST(1 AS bit) THEN 1 END) AS [Vencidos]");
        sql.Should().Contain("WHEN [s0].[CREATEDTIME] >= @hoyMs THEN 1 END) AS [CreadosHoy]");
        sql.Should().Contain("[s0].[ISPENDING] = CAST(0 AS bit) AND [s0].[COMPLETEDTIME] >= @hoyMs THEN 1 END) AS [CerradosHoy]");
        sql.Should().Contain("[s0].[ISPENDING] = CAST(1 AS bit) AND [s0].[OWNERID] IS NULL THEN 1 END) AS [SinAsignar]");
        sql.Should().Contain("[s0].[COMPLETEDTIME] > [s0].[CREATEDTIME] THEN CAST([s0].[COMPLETEDTIME] - [s0].[CREATEDTIME] AS float) / 3600000.0E0");
        foreach (var tabla in new[] { "WorkOrderStates", "StatusDefinition", "PriorityDefinition", "CategoryDefinition", "WorkOrder_Queue", "QueueDefinition" })
            sql.Should().Contain($"JOIN [{tabla}]");
        sql.Should().Contain("WHERE [w].[CREATEDTIME] >= @desdeMs");
    }

    [Fact]
    public void Los_estados_se_agrupan_y_ordenan_por_id()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Normal(DashboardConsultas.PorEstado(DashboardConsultas.EnRango(db, null, null)).ToQueryString());

        sql.Should().Contain("GROUP BY [w0].[STATUSID], [s].[STATUSNAME]").And.EndWith("ORDER BY [w0].[STATUSID]");
    }

    [Fact]
    public void El_SLA_cuenta_vencidos_sobre_el_total()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Normal(DashboardConsultas.Sla(DashboardConsultas.EnRango(db, null, null)).ToQueryString());

        sql.Should().Contain("COUNT(*) AS [Total]").And.Contain("COUNT(CASE WHEN [s0].[ISOVERDUE] = CAST(1 AS bit) THEN 1 END) AS [Vencidos]");
    }

    [Fact]
    public void La_tendencia_cuenta_por_hora_UTC_creados_y_cerrados_solo_de_estados_cerrados()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var creados = Normal(DashboardConsultas.CreadosPorHora(db, 1, 2).ToQueryString());
        var cerrados = Normal(DashboardConsultas.CerradosPorHora(db, 1, 2).ToQueryString());

        creados.Should().Contain("[w].[CREATEDTIME] / CAST(3600000 AS bigint)").And.Contain("WHERE [w].[CREATEDTIME] >= @desdeMs AND [w].[CREATEDTIME] < @hastaMs");
        creados.Should().NotContain("JOIN", "los creados salen de WorkOrder solamente, como antes");
        cerrados.Should().Contain("[w].[COMPLETEDTIME] / CAST(3600000 AS bigint)");
        cerrados.Should().Contain("[s].[ISPENDING] = CAST(0 AS bit) AND [w].[COMPLETEDTIME] >= @desdeMs AND [w].[COMPLETEDTIME] < @hastaMs");
    }

    [Theory]
    [MemberData(nameof(Dimensiones))]
    public void Cada_dimension_agrupa_sobre_los_joins_del_listado(string dimension, string fragmento)
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Normal(DashboardConsultas.Dimensiones[dimension](db, DashboardConsultas.EnRango(db, null, null)).ToQueryString());

        sql.Should().Contain(fragmento).And.Contain("COUNT(*) AS [Total]").And.Contain("LEFT JOIN [WorkOrder_Queue]");
    }

    [Fact]
    public void Las_dimensiones_son_las_nueve_publicas()
    {
        DashboardConsultas.Dimensiones.Keys.Should().BeEquivalentTo(
            "estado", "categoria", "prioridad", "grupo", "tecnico", "modo", "urgencia", "impacto", "tipo-solicitud");
    }
}

/// <summary>Comparan EF Core contra la implementación Dapper original en la base real. Se omiten sin ConnectionStrings__ServiceDesk.</summary>
public class DashboardParidadTests
{
    private static string? Cadena => Environment.GetEnvironmentVariable("ConnectionStrings__ServiceDesk");

    private static (SoporteBlunk.Infrastructure.Persistence.Contexts.ServiceDeskDbContext Db, IDashboardReadRepository Nuevo, IDashboardReadRepository Anterior) Repos()
    {
        Skip.If(string.IsNullOrWhiteSpace(Cadena), "Falta ConnectionStrings__ServiceDesk");
        var db = ContextoDePrueba.ServiceDesk(Cadena!);
        return (db, new DashboardReadRepository(db), new DashboardReadRepositoryDapper(new SqlConnectionFactory(Cadena!)));
    }

    private static (DateOnly? Desde, DateOnly? Hasta)[] Rangos()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        return
        [
            (null, null), (hoy.AddDays(-30), null), (hoy.AddDays(-365), hoy), (hoy.AddDays(-90), hoy.AddDays(-60)),
            (hoy, hoy), (null, hoy.AddDays(-400)), (new DateOnly(1990, 1, 1), new DateOnly(1990, 1, 31)),
        ];
    }

    [SkippableFact]
    public async Task El_resumen_coincide_en_todos_los_rangos()
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;
        var hubo = 0;

        foreach (var (desde, hasta) in Rangos())
        {
            var n = await nuevo.ResumenAsync(desde, hasta, default);
            var a = await anterior.ResumenAsync(desde, hasta, default);
            var motivo = $"resumen {desde}..{hasta}";

            n.Should().BeEquivalentTo(a with { PromedioHorasResolucion = null }, o => o.Excluding(x => x.PromedioHorasResolucion).WithStrictOrdering(), motivo);
            (n.PromedioHorasResolucion is null).Should().Be(a.PromedioHorasResolucion is null, motivo);
            // El promedio se calcula en decimal en el SQL original y en float en EF.
            if (a.PromedioHorasResolucion is { } prom) n.PromedioHorasResolucion!.Value.Should().BeApproximately(prom, 1e-6, motivo);
            if (a.Total > 0) hubo++;
        }
        hubo.Should().BeGreaterThan(1, "debe haber rangos con tickets para que la comparación valga");
    }

    [SkippableTheory]
    [InlineData(Agrupacion.Dia, 45)]
    [InlineData(Agrupacion.Dia, 400)]
    [InlineData(Agrupacion.Semana, 200)]
    [InlineData(Agrupacion.Mes, 760)]
    public async Task La_tendencia_coincide_y_es_continua(Agrupacion agrupacion, int dias)
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;
        var hasta = DateOnly.FromDateTime(DateTime.Today);

        // Se prueban también rangos que cruzan fin de año y desfases de un día (el límite entre dos periodos).
        foreach (var corrimiento in new[] { 0, 1, 17 })
        {
            var fin = hasta.AddDays(-corrimiento);
            var n = await nuevo.TendenciaAsync(fin.AddDays(-dias), fin, agrupacion, default);
            var a = await anterior.TendenciaAsync(fin.AddDays(-dias), fin, agrupacion, default);

            n.Should().Equal(a, $"{agrupacion} {dias} días, corrimiento {corrimiento}");
            n.Select(p => p.Periodo).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
            n.Sum(p => p.Creados).Should().BeGreaterThan(0, "debe haber tickets creados en el rango");
        }
    }

    [SkippableTheory]
    [InlineData(Agrupacion.Dia)]
    [InlineData(Agrupacion.Semana)]
    [InlineData(Agrupacion.Mes)]
    public async Task La_tendencia_es_exacta_en_los_cambios_de_horario_de_verano_de_2022(Agrupacion agrupacion)
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;

        // México usó horario de verano hasta el 30/10/2022 (cambio a las 2:00 locales, en hora completa).
        foreach (var (desde, hasta) in new[] { (new DateOnly(2022, 3, 20), new DateOnly(2022, 4, 10)), (new DateOnly(2022, 10, 15), new DateOnly(2022, 11, 15)), (new DateOnly(2021, 12, 15), new DateOnly(2022, 12, 31)) })
        {
            var n = await nuevo.TendenciaAsync(desde, hasta, agrupacion, default);
            var a = await anterior.TendenciaAsync(desde, hasta, agrupacion, default);

            n.Should().Equal(a, $"{agrupacion} {desde}..{hasta}");
            // Si la base no tiene tickets de 2022 la comparación sería trivial: se avisa en lugar de aprobar en silencio.
            Skip.If(a.Sum(p => p.Creados + p.Cerrados) == 0, "La base no tiene tickets de 2022: no se puede validar el horario de verano.");
        }
    }

    [SkippableTheory]
    [InlineData("estado")]
    [InlineData("categoria")]
    [InlineData("prioridad")]
    [InlineData("grupo")]
    [InlineData("tecnico")]
    [InlineData("modo")]
    [InlineData("urgencia")]
    [InlineData("impacto")]
    [InlineData("tipo-solicitud")]
    public async Task Los_conteos_por_dimension_coinciden(string dimension)
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        foreach (var (desde, hasta) in new (DateOnly?, DateOnly?)[] { (null, null), (hoy.AddDays(-90), null), (new DateOnly(1990, 1, 1), new DateOnly(1990, 1, 31)) })
        {
            var n = await nuevo.PorAsync(dimension, desde, hasta, default);
            var a = await anterior.PorAsync(dimension, desde, hasta, default);
            var motivo = $"{dimension} {desde}..{hasta}";

            // Entre grupos con el mismo total el SQL no define desempate: se compara con un orden canónico.
            n.OrderByDescending(x => x.Total).ThenBy(x => x.Id).ThenBy(x => x.Nombre, StringComparer.Ordinal)
                .Should().Equal(a.OrderByDescending(x => x.Total).ThenBy(x => x.Id).ThenBy(x => x.Nombre, StringComparer.Ordinal), motivo);
            n.Select(x => x.Total).Should().BeInDescendingOrder(motivo);
            if (desde is null && hasta is null) n.Sum(x => x.Total).Should().BeGreaterThan(0, "la dimensión debe traer datos");
        }

        await FluentActions.Awaiting(() => nuevo.PorAsync("no-existe", null, null, default)).Should().ThrowAsync<ArgumentException>();
    }

    [SkippableFact]
    public async Task El_SLA_coincide_en_todos_los_rangos()
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;

        foreach (var (desde, hasta) in Rangos())
            (await nuevo.SlaAsync(desde, hasta, default)).Should().Be(await anterior.SlaAsync(desde, hasta, default), $"sla {desde}..{hasta}");
    }
}
