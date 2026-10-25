using Dapper;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Sql;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

// Implementación Dapper original, conservada solo como referencia para el test de paridad contra EF Core.
internal sealed class DashboardReadRepositoryDapper(Infrastructure.Persistence.ISqlConnectionFactory factory) : IDashboardReadRepository
{
    // Dimensión pública -> (join adicional, expresión de id, expresión de nombre). Todo fijo, sin entrada del usuario.
    private static readonly Dictionary<string, (string Join, string Id, string Name)> Dimensiones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["estado"] = ("", "s.STATUSID", "st.STATUSNAME"),
        ["categoria"] = ("", "s.CATEGORYID", "cat.CATEGORYNAME"),
        ["prioridad"] = ("", "s.PRIORITYID", "pr.PRIORITYNAME"),
        ["grupo"] = ("", "q.QUEUEID", "qd.QUEUENAME"),
        ["tecnico"] = ("", "s.OWNERID", TicketSql.FullName("tu")),
        ["modo"] = ("LEFT JOIN ModeDefinition md ON md.MODEID = w.MODEID", "w.MODEID", "md.MODENAME"),
        ["urgencia"] = ("LEFT JOIN UrgencyDefinition ur ON ur.URGENCYID = s.URGENCYID", "s.URGENCYID", "ur.NAME"),
        ["impacto"] = ("LEFT JOIN ImpactDefinition im ON im.IMPACTID = s.IMPACTID", "s.IMPACTID", "im.NAME"),
        ["tipo-solicitud"] = ("LEFT JOIN RequestTypeDefinition rt ON rt.REQUESTTYPEID = s.REQUESTTYPEID", "s.REQUESTTYPEID", "rt.NAME"),
    };

    private static CommandDefinition Cmd(string sql, object? p, CancellationToken ct) =>
        new(sql, p, commandTimeout: TicketSql.CommandTimeoutSeconds, cancellationToken: ct);

    private static string WhereRango(DateOnly? desde, DateOnly? hasta, DynamicParameters p)
    {
        var where = new List<string>();
        SqlTime.AddCreatedRange(where, p, desde, hasta);
        return where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where);
    }

    public async Task<DashboardResumen> ResumenAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = WhereRango(desde, hasta, p);
        p.Add("hoyMs", SqlTime.StartOfDayMs(SqlTime.Today()));

        var sql = $"""
            SELECT COUNT(*) AS Total,
                   ISNULL(SUM(CASE WHEN st.ISPENDING = 1 THEN 1 ELSE 0 END), 0) AS Abiertos,
                   ISNULL(SUM(CASE WHEN st.ISPENDING = 0 THEN 1 ELSE 0 END), 0) AS Cerrados,
                   ISNULL(SUM(CASE WHEN st.ISPENDING = 1 AND s.ISOVERDUE = 1 THEN 1 ELSE 0 END), 0) AS Vencidos,
                   ISNULL(SUM(CASE WHEN w.CREATEDTIME >= @hoyMs THEN 1 ELSE 0 END), 0) AS CreadosHoy,
                   ISNULL(SUM(CASE WHEN st.ISPENDING = 0 AND w.COMPLETEDTIME >= @hoyMs THEN 1 ELSE 0 END), 0) AS CerradosHoy,
                   ISNULL(SUM(CASE WHEN st.ISPENDING = 1 AND s.OWNERID IS NULL THEN 1 ELSE 0 END), 0) AS SinAsignar,
                   CAST(AVG(CASE WHEN st.ISPENDING = 0 AND w.COMPLETEDTIME > w.CREATEDTIME
                                 THEN (w.COMPLETEDTIME - w.CREATEDTIME) / 3600000.0 END) AS float) AS PromedioHorasResolucion
            {TicketSql.Joins}
            {where}
            """;

        var porEstadoSql = $"""
            SELECT s.STATUSID AS Id, st.STATUSNAME AS Nombre, COUNT(*) AS Total
            {TicketSql.Joins}
            {where}
            GROUP BY s.STATUSID, st.STATUSNAME
            ORDER BY s.STATUSID
            """;

        await using var cn = await factory.OpenAsync(ct);
        var r = await cn.QuerySingleAsync<ResumenRow>(Cmd(sql, p, ct));
        var porEstado = (await cn.QueryAsync<ConteoPorClave>(Cmd(porEstadoSql, p, ct))).AsList();
        return new DashboardResumen(r.Total, r.Abiertos, r.Cerrados, r.Vencidos, r.CreadosHoy, r.CerradosHoy, r.SinAsignar,
            r.PromedioHorasResolucion, porEstado);
    }

    public async Task<IReadOnlyList<TendenciaPunto>> TendenciaAsync(DateOnly desde, DateOnly hasta, Agrupacion agrupacion, CancellationToken ct)
    {
        var p = new DynamicParameters();
        p.Add("desdeMs", SqlTime.StartOfDayMs(desde));
        p.Add("hastaMs", SqlTime.StartOfDayMs(hasta.AddDays(1)));

        string Bucket(string col)
        {
            var dia = $"CAST({SqlTime.Local(col)} AS date)";
            return agrupacion switch
            {
                Agrupacion.Mes => $"DATEFROMPARTS(YEAR({dia}), MONTH({dia}), 1)",
                Agrupacion.Semana => $"DATEADD(DAY, -(DATEDIFF(DAY, '19000101', {dia}) % 7), {dia})",
                _ => dia,
            };
        }

        var creados = $"""
            SELECT {Bucket("w.CREATEDTIME")} AS Periodo, COUNT(*) AS Total
            FROM WorkOrder w
            WHERE w.CREATEDTIME >= @desdeMs AND w.CREATEDTIME < @hastaMs
            GROUP BY {Bucket("w.CREATEDTIME")}
            """;
        var cerrados = $"""
            SELECT {Bucket("w.COMPLETEDTIME")} AS Periodo, COUNT(*) AS Total
            FROM WorkOrder w
            JOIN WorkOrderStates s ON s.WORKORDERID = w.WORKORDERID
            JOIN StatusDefinition st ON st.STATUSID = s.STATUSID
            WHERE st.ISPENDING = 0 AND w.COMPLETEDTIME >= @desdeMs AND w.COMPLETEDTIME < @hastaMs
            GROUP BY {Bucket("w.COMPLETEDTIME")}
            """;

        await using var cn = await factory.OpenAsync(ct);
        var c = (await cn.QueryAsync<PeriodoRow>(Cmd(creados, p, ct))).ToDictionary(x => DateOnly.FromDateTime(x.Periodo), x => x.Total);
        var k = (await cn.QueryAsync<PeriodoRow>(Cmd(cerrados, p, ct))).ToDictionary(x => DateOnly.FromDateTime(x.Periodo), x => x.Total);

        // Rellena con ceros los periodos sin actividad para que la serie sea continua.
        var puntos = new List<TendenciaPunto>();
        var actual = Inicio(desde, agrupacion);
        var fin = Inicio(hasta, agrupacion);
        while (actual <= fin)
        {
            puntos.Add(new TendenciaPunto(actual, c.GetValueOrDefault(actual), k.GetValueOrDefault(actual)));
            actual = agrupacion switch
            {
                Agrupacion.Mes => actual.AddMonths(1),
                Agrupacion.Semana => actual.AddDays(7),
                _ => actual.AddDays(1),
            };
        }
        return puntos;
    }

    private static DateOnly Inicio(DateOnly d, Agrupacion a) => a switch
    {
        Agrupacion.Mes => new DateOnly(d.Year, d.Month, 1),
        Agrupacion.Semana => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
        _ => d,
    };

    public async Task<IReadOnlyList<ConteoPorClave>> PorAsync(string dimension, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        if (!Dimensiones.TryGetValue(dimension, out var dim))
            throw new ArgumentException($"Dimensión desconocida: {dimension}", nameof(dimension));

        var p = new DynamicParameters();
        var where = WhereRango(desde, hasta, p);
        var sql = $"""
            SELECT {dim.Id} AS Id, ISNULL({dim.Name}, 'Sin dato') AS Nombre, COUNT(*) AS Total
            {TicketSql.Joins}
            {dim.Join}
            {where}
            GROUP BY {dim.Id}, {dim.Name}
            ORDER BY COUNT(*) DESC
            """;

        await using var cn = await factory.OpenAsync(ct);
        return (await cn.QueryAsync<ConteoPorClave>(Cmd(sql, p, ct))).AsList();
    }

    public async Task<SlaResumen> SlaAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = WhereRango(desde, hasta, p);
        var sql = $"""
            SELECT COUNT(*) AS Total,
                   ISNULL(SUM(CASE WHEN s.ISOVERDUE = 1 THEN 1 ELSE 0 END), 0) AS Vencidos
            {TicketSql.Joins}
            {where}
            """;

        await using var cn = await factory.OpenAsync(ct);
        var r = await cn.QuerySingleAsync<SlaRow>(Cmd(sql, p, ct));
        var enTiempo = r.Total - r.Vencidos;
        var pct = r.Total == 0 ? 100d : Math.Round(enTiempo * 100d / r.Total, 2);
        return new SlaResumen(r.Total, r.Vencidos, enTiempo, pct);
    }

    private sealed record ResumenRow(
        int Total, int Abiertos, int Cerrados, int Vencidos, int CreadosHoy, int CerradosHoy, int SinAsignar,
        double? PromedioHorasResolucion);

    private sealed record PeriodoRow(DateTime Periodo, int Total);

    private sealed record SlaRow(int Total, int Vencidos);
}
