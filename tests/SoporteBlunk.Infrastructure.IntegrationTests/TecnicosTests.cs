using System.Text.RegularExpressions;
using FluentAssertions;
using FluentAssertions.Equivalency;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence;
using SoporteBlunk.Infrastructure.Persistence.Queries;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>Técnicos de ServiceDesk migrados a EF Core: el SQL se revisa sin base de datos.</summary>
public class TecnicosSqlTests
{
    private static string Normal(string sql) => Regex.Replace(sql, @"\s+", " ");

    [Fact]
    public void La_lista_agrupa_por_tecnico_y_ordena_por_nombre_dentro_del_SQL()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Normal(TecnicoConsultas.ListaOrdenada(db).ToQueryString());

        sql.Should().Contain("LEFT JOIN [AaaUser]").And.Contain("WHERE [w].[OWNERID] IS NOT NULL");
        sql.Should().Contain("GROUP BY [w].[OWNERID], [a].[FIRST_NAME], [a].[LAST_NAME]");
        sql.Should().Contain("ORDER BY CASE WHEN LTRIM(RTRIM(COALESCE([a].[FIRST_NAME], N'') + N' ' + COALESCE([a].[LAST_NAME], N''))) = N'' THEN N'Usuario '");
    }

    [Fact]
    public void La_carga_cuenta_abiertos_y_vencidos_con_las_mismas_condiciones()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var todos = Normal(TecnicoConsultas.Carga(db, null).ToQueryString());
        var uno = Normal(TecnicoConsultas.Carga(db, 5).ToQueryString());

        todos.Should().Contain("INNER JOIN [StatusDefinition]").And.Contain("LEFT JOIN [AaaUser]");
        todos.Should().Contain("COUNT(CASE WHEN [s].[ISPENDING] = CAST(1 AS bit) THEN 1 END) AS [Abiertos]");
        todos.Should().Contain("COUNT(CASE WHEN [s].[ISPENDING] = CAST(1 AS bit) AND [w].[ISOVERDUE] = CAST(1 AS bit) THEN 1 END) AS [Vencidos]");
        todos.Should().Contain("COUNT(*) AS [Total]").And.EndWith("DESC, COUNT(*) DESC");
        todos.Should().NotContain("@id");
        uno.Should().Contain("[w].[OWNERID] = @id");
    }

    [Fact]
    public void La_productividad_mide_la_primera_respuesta_desde_la_asignacion()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Normal(TecnicoConsultas.Base(db)
            .DelTecnico(5, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)).Productividad().ToQueryString());

        sql.Should().Contain("WHEN [w].[RESPONDEDTIME] > [w0].[ASSIGNEDTIME] THEN CAST([w].[RESPONDEDTIME] - [w0].[ASSIGNEDTIME] AS float) / 3600000.0E0");
        sql.Should().Contain("WHEN [s].[ISPENDING] = CAST(0 AS bit) AND [w].[COMPLETEDTIME] > [w].[CREATEDTIME] THEN CAST([w].[COMPLETEDTIME] - [w].[CREATEDTIME] AS float) / 3600000.0E0");
        sql.Should().Contain("COUNT(CASE WHEN [s].[ISPENDING] = CAST(0 AS bit) THEN 1 END) AS [Cerrados]");
        sql.Should().Contain("WHERE [w0].[OWNERID] = @id AND [w].[CREATEDTIME] >= @desdeMs AND [w].[CREATEDTIME] < @hastaMs");
        sql.Should().Contain("GROUP BY [w0].[OWNERID]").And.EndWith("ORDER BY COUNT(*) DESC");
    }

    [Fact]
    public void El_equipo_completo_solo_incluye_solicitudes_con_tecnico()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Normal(TecnicoConsultas.Base(db).DelTecnico(null, null, null).Productividad().ToQueryString());

        sql.Should().Contain("WHERE [w0].[OWNERID] IS NOT NULL").And.NotContain("@id").And.NotContain("CREATEDTIME] >=");
    }

    [Fact]
    public void La_tendencia_trae_solo_las_columnas_necesarias_del_tecnico_y_el_rango()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Normal(TecnicoConsultas.FilasParaTendencia(db, 5, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)).ToQueryString());

        sql.Should().Contain("WHERE [w0].[OWNERID] = @tecnicoId AND [w].[CREATEDTIME] >= @desdeMs AND [w].[CREATEDTIME] < @hastaMs");
        sql.Should().NotContain("AaaUser");
    }
}

/// <summary>Comparan EF Core contra la implementación Dapper original en la base real. Se omiten sin ConnectionStrings__ServiceDesk.</summary>
public class TecnicosParidadTests
{
    private static string? Cadena => Environment.GetEnvironmentVariable("ConnectionStrings__ServiceDesk");

    private static (SoporteBlunk.Infrastructure.Persistence.Contexts.ServiceDeskDbContext Db, ITecnicoReadRepository Nuevo, ITecnicoReadRepository Anterior) Repos()
    {
        Skip.If(string.IsNullOrWhiteSpace(Cadena), "Falta ConnectionStrings__ServiceDesk");
        var db = ContextoDePrueba.ServiceDesk(Cadena!);
        return (db, new TecnicoReadRepository(db), new TecnicoReadRepositoryDapper(new SqlConnectionFactory(Cadena!)));
    }

    // Los promedios se calculan en decimal en el SQL original y en float en EF: pueden diferir en los últimos decimales.
    private static EquivalencyAssertionOptions<T> Aprox<T>(EquivalencyAssertionOptions<T> o) => o
        .Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 1e-6)).WhenTypeIs<double>();

    private static void Igual<T>(T actual, T esperado, string motivo) =>
        actual.Should().BeEquivalentTo(esperado, o => Aprox(o).WithStrictOrdering(), motivo);

    private static void IgualAprox(TecnicoProductividad? n, TecnicoProductividad? a, string motivo)
    {
        if (a is null) { n.Should().BeNull(motivo); return; }
        n.Should().NotBeNull(motivo);
        n!.Should().BeEquivalentTo(a with { PromedioHorasPrimeraRespuesta = null, PromedioHorasResolucion = null },
            o => o.Excluding(x => x.PromedioHorasPrimeraRespuesta).Excluding(x => x.PromedioHorasResolucion), motivo);
        (n.PromedioHorasPrimeraRespuesta is null).Should().Be(a.PromedioHorasPrimeraRespuesta is null, motivo + " (primera respuesta nula)");
        (n.PromedioHorasResolucion is null).Should().Be(a.PromedioHorasResolucion is null, motivo + " (resolución nula)");
        if (a.PromedioHorasPrimeraRespuesta is { } r) n.PromedioHorasPrimeraRespuesta!.Value.Should().BeApproximately(r, 1e-6, motivo);
        if (a.PromedioHorasResolucion is { } s) n.PromedioHorasResolucion!.Value.Should().BeApproximately(s, 1e-6, motivo);
    }

    private static IEnumerable<TecnicoProductividad> Canonico(IEnumerable<TecnicoProductividad> l) => l.OrderByDescending(t => t.Asignados).ThenBy(t => t.TecnicoId);

    [SkippableFact]
    public async Task La_lista_de_tecnicos_es_identica()
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;

        var n = await nuevo.ListarAsync(default);
        var a = await anterior.ListarAsync(default);

        n.Should().NotBeEmpty();
        // El orden es por nombre; entre nombres iguales el SQL no define desempate, así que se compara con uno canónico.
        n.OrderBy(t => t.Nombre, StringComparer.Ordinal).ThenBy(t => t.Id).Should().Equal(a.OrderBy(t => t.Nombre, StringComparer.Ordinal).ThenBy(t => t.Id));
        n.Select(t => t.Nombre).Should().Equal(a.Select(t => t.Nombre), "el orden por nombre debe ser el mismo que el de SQL Server");
    }

    [SkippableFact]
    public async Task La_carga_es_identica_y_viene_ordenada()
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;

        var n = await nuevo.CargaAsync(default);
        var a = await anterior.CargaAsync(default);

        n.OrderBy(t => t.TecnicoId).Should().Equal(a.OrderBy(t => t.TecnicoId));
        n.Select(t => (t.Abiertos, t.Total)).Should().Equal(a.Select(t => (t.Abiertos, t.Total)), "mismo orden por abiertos y total");

        foreach (var t in a.Take(8)) (await nuevo.CargaAsync(t.TecnicoId, default)).Should().Be(t, $"carga de {t.TecnicoId}");
        (await nuevo.CargaAsync(-1, default)).Should().BeNull();
    }

    [SkippableFact]
    public async Task La_productividad_individual_y_del_equipo_coinciden_en_varios_rangos()
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var rangos = new (DateOnly? Desde, DateOnly? Hasta)[]
        {
            (null, null), (hoy.AddDays(-30), null), (hoy.AddDays(-365), hoy), (hoy.AddDays(-90), hoy.AddDays(-60)), (new DateOnly(1990, 1, 1), new DateOnly(1990, 1, 31)),
        };
        var tecnicos = (await anterior.CargaAsync(default)).Take(6).Select(t => t.TecnicoId).ToList();
        tecnicos.Should().NotBeEmpty("la prueba necesita técnicos reales para ser válida");
        var conActividad = 0;

        foreach (var (desde, hasta) in rangos)
        {
            var contexto = $"{desde}..{hasta}";
            var n = Canonico(await nuevo.ListarProductividadAsync(desde, hasta, default)).ToList();
            var a = Canonico(await anterior.ListarProductividadAsync(desde, hasta, default)).ToList();
            n.Should().HaveCount(a.Count, $"equipo {contexto}");
            for (var i = 0; i < a.Count; i++) IgualAprox(n[i], a[i], $"equipo {contexto} #{i}");
            conActividad += a.Count(t => t.Asignados > 0 && t.PromedioHorasPrimeraRespuesta is not null && t.PromedioHorasResolucion is not null);

            foreach (var id in tecnicos)
                IgualAprox(await nuevo.ProductividadAsync(id, desde, hasta, default), await anterior.ProductividadAsync(id, desde, hasta, default), $"técnico {id} {contexto}");
        }

        conActividad.Should().BeGreaterThan(0, "debe haber técnicos con tickets y promedios para comparar los cálculos");
        (await nuevo.ProductividadAsync(-1, null, null, default)).Should().BeNull("técnico inexistente");
    }

    [SkippableTheory]
    [InlineData(Agrupacion.Dia, 60)]
    [InlineData(Agrupacion.Semana, 200)]
    [InlineData(Agrupacion.Mes, 730)]
    public async Task La_tendencia_coincide_y_no_tiene_huecos(Agrupacion agrupacion, int dias)
    {
        var (db, nuevo, anterior) = Repos();
        using var _ = db;
        var hasta = DateOnly.FromDateTime(DateTime.Today);

        var tecnicos = (await anterior.CargaAsync(default)).Take(4).ToList();
        tecnicos.Should().NotBeEmpty("la prueba necesita técnicos reales para ser válida");
        var conDatos = 0;
        foreach (var t in tecnicos)
        {
            var n = await nuevo.TendenciaProductividadAsync(t.TecnicoId, hasta.AddDays(-dias), hasta, agrupacion, default);
            var a = await anterior.TendenciaProductividadAsync(t.TecnicoId, hasta.AddDays(-dias), hasta, agrupacion, default);

            Igual(n, a, $"tendencia de {t.TecnicoId} {agrupacion}");
            n.Select(p => p.Periodo).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
            conDatos += n.Count(p => p.Asignados > 0);
        }
        conDatos.Should().BeGreaterThan(0, "debe haber periodos con tickets para comparar los cálculos");
    }
}
