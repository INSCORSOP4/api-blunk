using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence;
using SoporteBlunk.Infrastructure.Persistence.Queries;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>Tickets de ServiceDesk migrados a EF Core: el SQL y las conversiones se revisan sin base de datos.</summary>
public class TicketsSqlTests
{
    private static string Sql(IQueryable consulta) => Regex.Replace(((IQueryable<object>)consulta.Cast<object>()).ToQueryString(), @"\s+", " ");

    [Fact]
    public void Todas_las_consultas_se_traducen_a_SQL()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var filtroCompleto = new TicketFiltro
        {
            EstadoId = 1, TecnicoId = 2, GrupoId = 3, CategoriaId = 4, PrioridadId = 5, Vencido = true, SinResponder = true,
            Desde = new DateOnly(2026, 1, 1), Hasta = new DateOnly(2026, 1, 31), Buscar = "123",
        };

        foreach (var orden in Enum.GetValues<TicketOrden>())
        {
            var sql = TicketConsultas.Base(db).Filtrar(filtroCompleto).Ordenar(orden).ProyectarResumen().Skip(0).Take(10).ToQueryString();
            sql.Should().Contain("OFFSET").And.Contain("FETCH NEXT");
        }

        TicketDetalleConsultas.Detalle(db, 1).ToQueryString().Should().Contain("WorkOrderToDescription");
        TicketDetalleConsultas.Historial(db, 1).Take(500).ToQueryString().Should().Contain("TOP(@p)");
        TicketDetalleConsultas.TiempoPorEstado(db, 1).ToQueryString().Should().Contain("WO_Status_Info");
        TicketDetalleConsultas.TiempoPorTecnico(db, 1).ToQueryString().Should().Contain("WO_Tech_Info");
        TicketDetalleConsultas.TiempoPorGrupo(db, 1).ToQueryString().Should().Contain("WO_Group_Info");
    }

    [Fact]
    public void El_listado_conserva_los_joins_del_SQL_original()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var sql = Regex.Replace(TicketConsultas.Base(db).Select(x => x.W.Id).ToQueryString(), @"\s+", " ");

        sql.Should().Contain("FROM [WorkOrder] AS [w] INNER JOIN [WorkOrderStates] AS [w0] ON [w].[WORKORDERID] = [w0].[WORKORDERID]");
        sql.Should().Contain("INNER JOIN [StatusDefinition]");
        foreach (var tabla in new[] { "PriorityDefinition", "CategoryDefinition", "WorkOrder_Queue", "QueueDefinition" })
            sql.Should().Contain($"LEFT JOIN [{tabla}]");
        Regex.Matches(sql, @"LEFT JOIN \[AaaUser\]").Should().HaveCount(2, "técnico y solicitante");
    }

    [Fact]
    public void Los_filtros_usan_las_mismas_condiciones_que_Dapper()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        string Where(TicketFiltro f) => Regex.Replace(TicketConsultas.Base(db).Filtrar(f).Select(x => x.W.Id).ToQueryString(), @"\s+", " ");

        Where(new TicketFiltro { EstadoId = 7 }).Should().Contain("WHERE [w0].[STATUSID] = @estado");
        Where(new TicketFiltro { TecnicoId = 7 }).Should().Contain("[w0].[OWNERID] = @tecnico");
        Where(new TicketFiltro { GrupoId = 7 }).Should().Contain("[w1].[QUEUEID] = @grupo");
        Where(new TicketFiltro { CategoriaId = 7 }).Should().Contain("[w0].[CATEGORYID] = @categoria");
        Where(new TicketFiltro { PrioridadId = 7 }).Should().Contain("[w0].[PRIORITYID] = @prioridad");
        Where(new TicketFiltro { Vencido = true }).Should().Contain("[w0].[ISOVERDUE] = CAST(1 AS bit)");
        Where(new TicketFiltro { Vencido = false }).Should().Contain("COALESCE([w0].[ISOVERDUE], CAST(0 AS bit)) = CAST(0 AS bit)");
        Where(new TicketFiltro { SinResponder = true })
            .Should().Contain("[s].[ISPENDING] = CAST(1 AS bit) AND [w].[RESPONDEDTIME] = CAST(0 AS bigint)");
        // NOT (a AND b) con semántica de nulos de SQL: sin comprobaciones extra de IS NULL.
        Where(new TicketFiltro { SinResponder = false })
            .Should().Contain("[s].[ISPENDING] <> CAST(1 AS bit) OR [w].[RESPONDEDTIME] <> CAST(0 AS bigint)").And.NotContain("IS NULL");
        Where(new TicketFiltro { Desde = new DateOnly(2026, 1, 1) }).Should().Contain("[w].[CREATEDTIME] >= @desdeMs");
        Where(new TicketFiltro { Hasta = new DateOnly(2026, 1, 1) }).Should().Contain("[w].[CREATEDTIME] < @hastaMs");
        Where(new TicketFiltro { Buscar = "abc" }).Should().Contain("[w].[TITLE] LIKE @patron").And.NotContain("WORKORDERID] = @id");
        Where(new TicketFiltro { Buscar = "42" }).Should().Contain("[w].[WORKORDERID] = @id OR [w].[TITLE] LIKE @patron");
    }

    [Fact]
    public void La_busqueda_escapa_los_comodines_como_antes()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        var consulta = TicketConsultas.Base(db).Filtrar(new TicketFiltro { Buscar = "100% [test]_'; --" }).Select(x => x.W.Id);

        consulta.ToQueryString().Should().Contain("N'%100[%] [[]test][_]''; --%'");
    }

    [Fact]
    public void Los_ordenes_reproducen_el_ORDER_BY_original()
    {
        using var db = ContextoDePrueba.ServiceDesk();
        string Orden(TicketOrden o) => Regex.Replace(TicketConsultas.Base(db).Ordenar(o).Select(x => x.W.Id).ToQueryString(), @"\s+", " ");

        Orden(TicketOrden.CreadoDesc).Should().EndWith("ORDER BY [w].[CREATEDTIME] DESC, [w].[WORKORDERID] DESC");
        Orden(TicketOrden.CreadoAsc).Should().EndWith("ORDER BY [w].[CREATEDTIME], [w].[WORKORDERID]");
        Orden(TicketOrden.IdDesc).Should().EndWith("ORDER BY [w].[WORKORDERID] DESC");
        Orden(TicketOrden.VenceAsc).Should().EndWith(
            "ORDER BY CASE WHEN [w].[DUEBYTIME] > CAST(0 AS bigint) THEN 0 ELSE 1 END, [w].[DUEBYTIME], [w].[WORKORDERID]");
    }

    [Fact]
    public void Las_fechas_se_convierten_a_hora_de_Mexico_y_cero_es_sin_dato()
    {
        TiempoServiceDesk.AFecha(null).Should().BeNull();
        TiempoServiceDesk.AFecha(0).Should().BeNull();

        // 2026-03-01 18:00:00 UTC (México central no usa horario de verano: UTC-6).
        var utc = new DateTimeOffset(2026, 3, 1, 18, 0, 0, TimeSpan.Zero);
        var local = TiempoServiceDesk.AFecha(utc.ToUnixTimeMilliseconds());
        local.Should().Be(utc);
        local!.Value.Offset.Should().Be(TimeSpan.FromHours(-6));
        local.Value.Hour.Should().Be(12);

        // Trunca a segundos, como el DATEADD(SECOND, ms / 1000, ...) anterior.
        TiempoServiceDesk.AFecha(utc.ToUnixTimeMilliseconds() + 999)!.Value.Millisecond.Should().Be(0);
    }

    [Fact]
    public void El_inicio_del_dia_es_la_medianoche_local_en_UTC()
    {
        TiempoServiceDesk.InicioDelDiaMs(new DateOnly(2026, 3, 1))
            .Should().Be(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds());
    }

    [Theory]
    [InlineData("Ana", "López", "Ana López")]
    [InlineData("Ana", null, "Ana")]
    [InlineData(null, "López", "López")]
    [InlineData(null, null, null)]
    [InlineData("", "", null)]
    [InlineData("  Ana ", " López  ", "Ana   López")]
    public void El_nombre_completo_equivale_a_NULLIF_LTRIM_RTRIM_CONCAT(string? nombre, string? apellido, string? esperado) =>
        TiempoServiceDesk.NombreCompleto(nombre, apellido).Should().Be(esperado);
}

/// <summary>Comparan EF Core contra la implementación Dapper original en la base real. Se omiten sin ConnectionStrings__ServiceDesk.</summary>
public class TicketsParidadTests
{
    private static string? Cadena => Environment.GetEnvironmentVariable("ConnectionStrings__ServiceDesk");

    private static (ServiceDeskDbContextHolder Holder, ITicketReadRepository Nuevo, ITicketReadRepository Anterior) Repos()
    {
        Skip.If(string.IsNullOrWhiteSpace(Cadena), "Falta ConnectionStrings__ServiceDesk");
        var holder = new ServiceDeskDbContextHolder(ContextoDePrueba.ServiceDesk(Cadena!));
        return (holder, new TicketReadRepository(holder.Db), new TicketReadRepositoryDapper(new SqlConnectionFactory(Cadena!)));
    }

    // Texto con el instante y el desplazamiento: detecta diferencias de zona horaria que la igualdad de DateTimeOffset ignora.
    private static string Firma(TicketResumen t) =>
        $"{t.Id}|{t.Titulo}|{t.EstadoId}|{t.Estado}|{t.Prioridad}|{t.Categoria}|{t.Grupo}|{t.TecnicoId}|{t.Tecnico}|{t.Solicitante}|{t.Vencido}|" +
        $"{t.Creado:o}|{t.Vence:o}|{t.Completado:o}";

    [SkippableTheory]
    [InlineData(TicketOrden.CreadoDesc)]
    [InlineData(TicketOrden.CreadoAsc)]
    [InlineData(TicketOrden.VenceAsc)]
    [InlineData(TicketOrden.IdDesc)]
    public async Task El_listado_sin_filtros_es_identico_en_cada_orden(TicketOrden orden)
    {
        var (h, nuevo, anterior) = Repos();
        using var _ = h;
        foreach (var pagina in new[] { 1, 2, 7 })
        {
            var a = await anterior.ListarAsync(new TicketFiltro(), orden, pagina, 25, default);
            var n = await nuevo.ListarAsync(new TicketFiltro(), orden, pagina, 25, default);

            n.TotalCount.Should().Be(a.TotalCount);
            n.Items.Select(Firma).Should().Equal(a.Items.Select(Firma), $"página {pagina}");
        }
    }

    [SkippableFact]
    public async Task Los_filtros_dan_los_mismos_resultados()
    {
        var (h, nuevo, anterior) = Repos();
        using var _ = h;
        var catalogos = new CatalogoReadRepository(h.Db);
        async Task<long?> Primero(string c) => (await catalogos.ObtenerAsync(c, default)).FirstOrDefault()?.Id;
        var muestra = (await anterior.ListarAsync(new TicketFiltro(), TicketOrden.CreadoDesc, 1, 50, default)).Items;
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var filtros = new List<TicketFiltro>
        {
            new() { EstadoId = await Primero("estados") },
            new() { GrupoId = await Primero("grupos") },
            new() { CategoriaId = await Primero("categorias") },
            new() { PrioridadId = await Primero("prioridades") },
            new() { TecnicoId = muestra.FirstOrDefault(t => t.TecnicoId is not null)?.TecnicoId },
            new() { Vencido = true }, new() { Vencido = false },
            new() { SinResponder = true }, new() { SinResponder = false },
            new() { Desde = hoy.AddDays(-30) }, new() { Hasta = hoy.AddDays(-30) }, new() { Desde = hoy.AddDays(-90), Hasta = hoy.AddDays(-60) },
            new() { Buscar = "a" }, new() { Buscar = "100% [test]_'; --" },
            new() { Buscar = muestra.First().Id.ToString() }, new() { Buscar = muestra.First().Titulo[..Math.Min(5, muestra.First().Titulo.Length)] },
            new() { Vencido = true, SinResponder = true, Desde = hoy.AddDays(-365) },
        };

        foreach (var filtro in filtros)
        {
            foreach (var orden in new[] { TicketOrden.CreadoDesc, TicketOrden.VenceAsc })
            {
                var a = await anterior.ListarAsync(filtro, orden, 1, 25, default);
                var n = await nuevo.ListarAsync(filtro, orden, 1, 25, default);

                n.TotalCount.Should().Be(a.TotalCount, $"{filtro} / {orden}");
                n.Items.Select(Firma).Should().Equal(a.Items.Select(Firma), $"{filtro} / {orden}");
            }

            var exportA = await anterior.ListarParaExportAsync(filtro, 40, default);
            var exportN = await nuevo.ListarParaExportAsync(filtro, 40, default);
            exportN.Select(Firma).Should().Equal(exportA.Select(Firma), $"export {filtro}");
        }
    }

    [SkippableFact]
    public async Task Detalle_historial_y_tiempos_son_identicos()
    {
        var (h, nuevo, anterior) = Repos();
        using var _ = h;
        var ids = (await anterior.ListarAsync(new TicketFiltro(), TicketOrden.CreadoDesc, 1, 15, default)).Items.Select(t => t.Id)
            .Concat((await anterior.ListarAsync(new TicketFiltro(), TicketOrden.CreadoAsc, 1, 15, default)).Items.Select(t => t.Id))
            .Concat([-1L]).ToList();

        foreach (var id in ids)
        {
            (await nuevo.ExisteAsync(id, default)).Should().Be(await anterior.ExisteAsync(id, default), $"existe {id}");

            var dn = await nuevo.ObtenerAsync(id, default);
            var da = await anterior.ObtenerAsync(id, default);
            dn.Should().BeEquivalentTo(da, o => o.WithStrictOrdering(), $"detalle {id}");
            if (da?.Resumen.Creado is { } c) dn!.Resumen.Creado!.Value.Offset.Should().Be(c.Offset, $"offset {id}");

            (await nuevo.HistorialAsync(id, default)).Should().BeEquivalentTo(await anterior.HistorialAsync(id, default), o => o.WithStrictOrdering(), $"historial {id}");
            (await nuevo.TiemposAsync(id, default)).Should().BeEquivalentTo(await anterior.TiemposAsync(id, default), o => o.WithStrictOrdering(), $"tiempos {id}");
        }
    }

    private sealed class ServiceDeskDbContextHolder(SoporteBlunk.Infrastructure.Persistence.Contexts.ServiceDeskDbContext db) : IDisposable
    {
        public SoporteBlunk.Infrastructure.Persistence.Contexts.ServiceDeskDbContext Db { get; } = db;
        public void Dispose() => Db.Dispose();
    }
}

/// <summary>
/// Compara todo el modelo de EF Core contra INFORMATION_SCHEMA de la base real: que existan las tablas y columnas y que los
/// tipos coincidan con los que SqlClient exige al leer (bigint → long, bit → bool, ...). Reporta todos los desajustes juntos.
/// </summary>
public class EsquemaServiceDeskTests
{
    private static readonly Dictionary<Type, string[]> Compatibles = new()
    {
        [typeof(long)] = ["bigint"],
        [typeof(int)] = ["int"],
        [typeof(bool)] = ["bit"],
        [typeof(string)] = ["nvarchar", "varchar", "nchar", "char", "ntext", "text"],
    };

    [SkippableFact]
    public async Task El_modelo_coincide_con_el_esquema_real()
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__ServiceDesk");
        Skip.If(string.IsNullOrWhiteSpace(cs), "Falta ConnectionStrings__ServiceDesk");

        using var db = ContextoDePrueba.ServiceDesk(cs!);
        var reales = new Dictionary<(string Tabla, string Columna), string>(new ClaveInsensible());
        await using (var cn = new SqlConnection(cs))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) reales[(r.GetString(0), r.GetString(1))] = r.GetString(2).ToLowerInvariant();
        }

        var problemas = new List<string>();
        foreach (var entidad in db.Model.GetEntityTypes())
        {
            var tabla = entidad.GetTableName()!;
            foreach (var propiedad in entidad.GetProperties())
            {
                var columna = propiedad.GetColumnName(StoreObjectIdentifier.Table(tabla, entidad.GetSchema()))!;
                var clr = Nullable.GetUnderlyingType(propiedad.ClrType) ?? propiedad.ClrType;
                if (!reales.TryGetValue((tabla, columna), out var tipo)) { problemas.Add($"{tabla}.{columna}: no existe"); continue; }
                if (Compatibles.TryGetValue(clr, out var validos) && !validos.Contains(tipo))
                    problemas.Add($"{tabla}.{columna}: el modelo espera {clr.Name} pero la columna es {tipo}");
            }
        }

        problemas.Should().BeEmpty("el modelo de EF debe corresponder al esquema real de ServiceDesk:\n" + string.Join("\n", problemas));
    }

    private sealed class ClaveInsensible : IEqualityComparer<(string Tabla, string Columna)>
    {
        public bool Equals((string Tabla, string Columna) a, (string Tabla, string Columna) b) =>
            string.Equals(a.Tabla, b.Tabla, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Columna, b.Columna, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Tabla, string Columna) k) =>
            HashCode.Combine(k.Tabla.ToUpperInvariant(), k.Columna.ToUpperInvariant());
    }
}
