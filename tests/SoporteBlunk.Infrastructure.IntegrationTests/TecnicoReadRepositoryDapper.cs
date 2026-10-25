using Dapper;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Sql;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// En ServiceDesk no hay una tabla simple de técnicos: se consideran técnicos los usuarios
/// que aparecen como propietario (OWNERID) de algún ticket.
/// </summary>
// Implementación Dapper original, conservada solo como referencia para el test de paridad contra EF Core.
internal sealed class TecnicoReadRepositoryDapper(Infrastructure.Persistence.ISqlConnectionFactory factory) : ITecnicoReadRepository
{
    private static CommandDefinition Cmd(string sql, object? p, CancellationToken ct) =>
        new(sql, p, commandTimeout: TicketSql.CommandTimeoutSeconds, cancellationToken: ct);

    private static readonly string NombreTecnico = TicketSql.FullName("u");

    public async Task<IReadOnlyList<Tecnico>> ListarAsync(CancellationToken ct)
    {
        var sql = $"""
            SELECT s.OWNERID AS Id, ISNULL({NombreTecnico}, CONCAT('Usuario ', s.OWNERID)) AS Nombre
            FROM WorkOrderStates s
            LEFT JOIN AaaUser u ON u.USER_ID = s.OWNERID
            WHERE s.OWNERID IS NOT NULL
            GROUP BY s.OWNERID, u.FIRST_NAME, u.LAST_NAME
            ORDER BY 2
            """;

        await using var cn = await factory.OpenAsync(ct);
        return (await cn.QueryAsync<Tecnico>(Cmd(sql, null, ct))).AsList();
    }

    public async Task<IReadOnlyList<TecnicoCarga>> CargaAsync(CancellationToken ct)
    {
        await using var cn = await factory.OpenAsync(ct);
        return (await cn.QueryAsync<TecnicoCarga>(Cmd(CargaSql(filtrar: false), null, ct))).AsList();
    }

    public async Task<TecnicoCarga?> CargaAsync(long tecnicoId, CancellationToken ct)
    {
        await using var cn = await factory.OpenAsync(ct);
        return await cn.QueryFirstOrDefaultAsync<TecnicoCarga>(Cmd(CargaSql(filtrar: true), new { tecnicoId }, ct));
    }

    private static string CargaSql(bool filtrar) => $"""
        SELECT s.OWNERID AS TecnicoId,
               ISNULL({NombreTecnico}, CONCAT('Usuario ', s.OWNERID)) AS Tecnico,
               ISNULL(SUM(CASE WHEN st.ISPENDING = 1 THEN 1 ELSE 0 END), 0) AS Abiertos,
               ISNULL(SUM(CASE WHEN st.ISPENDING = 1 AND s.ISOVERDUE = 1 THEN 1 ELSE 0 END), 0) AS Vencidos,
               COUNT(*) AS Total
        FROM WorkOrderStates s
        JOIN StatusDefinition st ON st.STATUSID = s.STATUSID
        LEFT JOIN AaaUser u ON u.USER_ID = s.OWNERID
        WHERE s.OWNERID IS NOT NULL {(filtrar ? "AND s.OWNERID = @tecnicoId" : "")}
        GROUP BY s.OWNERID, u.FIRST_NAME, u.LAST_NAME
        ORDER BY Abiertos DESC, Total DESC
        """;

    public async Task<TecnicoProductividad?> ProductividadAsync(long tecnicoId, DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var p = new DynamicParameters();
        p.Add("tecnicoId", tecnicoId);
        var where = new List<string> { "s.OWNERID = @tecnicoId" };
        SqlTime.AddCreatedRange(where, p, desde, hasta);

        await using var cn = await factory.OpenAsync(ct);
        var row = await cn.QuerySingleOrDefaultAsync<FilaProductividad>(Cmd(ProductividadSql(where), p, ct));
        if (row is not null && row.Asignados > 0) return Mapear(row);

        // Sin tickets en el rango: distinguir "técnico inexistente" de "técnico sin actividad".
        var nombre = await cn.ExecuteScalarAsync<string?>(Cmd(
            $"SELECT TOP 1 ISNULL({NombreTecnico}, CONCAT('Usuario ', s.OWNERID)) FROM WorkOrderStates s LEFT JOIN AaaUser u ON u.USER_ID = s.OWNERID WHERE s.OWNERID = @tecnicoId",
            new { tecnicoId }, ct));
        return nombre is null ? null : new TecnicoProductividad(tecnicoId, nombre, 0, 0, 0, 100, null, null);
    }

    public async Task<IReadOnlyList<TecnicoProductividad>> ListarProductividadAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = new List<string> { "s.OWNERID IS NOT NULL" };
        SqlTime.AddCreatedRange(where, p, desde, hasta);

        await using var cn = await factory.OpenAsync(ct);
        var filas = await cn.QueryAsync<FilaProductividad>(Cmd(ProductividadSql(where), p, ct));
        return filas.Select(Mapear).ToList();
    }

    // Comparte la lógica de cálculo entre "un técnico" y "todo el equipo"; solo cambia el WHERE. Siempre agrupa por
    // OWNERID (aunque el WHERE ya filtre a uno solo): SQL Server exige que una columna fuera de función de agregado
    // esté en el GROUP BY, y agrupar por una clave ya fija en el WHERE no cambia el resultado.
    // La primera respuesta se mide desde que el ticket quedó asignado a su técnico actual (ASSIGNEDTIME), no desde la
    // creación: si se reasignó después de que alguien ya había respondido, RESPONDEDTIME < ASSIGNEDTIME y ese ticket
    // no cuenta para el promedio de este técnico (no fue él quien dio esa respuesta).
    private string ProductividadSql(List<string> where) => $"""
        SELECT s.OWNERID AS TecnicoId,
               MAX(ISNULL({NombreTecnico}, CONCAT('Usuario ', s.OWNERID))) AS Tecnico,
               COUNT(*) AS Asignados,
               ISNULL(SUM(CASE WHEN st.ISPENDING = 0 THEN 1 ELSE 0 END), 0) AS Cerrados,
               ISNULL(SUM(CASE WHEN s.ISOVERDUE = 1 THEN 1 ELSE 0 END), 0) AS Vencidos,
               CAST(AVG(CASE WHEN w.RESPONDEDTIME > s.ASSIGNEDTIME
                             THEN (w.RESPONDEDTIME - s.ASSIGNEDTIME) / 3600000.0 END) AS float) AS PromedioHorasPrimeraRespuesta,
               CAST(AVG(CASE WHEN st.ISPENDING = 0 AND w.COMPLETEDTIME > w.CREATEDTIME
                             THEN (w.COMPLETEDTIME - w.CREATEDTIME) / 3600000.0 END) AS float) AS PromedioHorasResolucion
        FROM WorkOrder w
        JOIN WorkOrderStates s ON s.WORKORDERID = w.WORKORDERID
        JOIN StatusDefinition st ON st.STATUSID = s.STATUSID
        LEFT JOIN AaaUser u ON u.USER_ID = s.OWNERID
        WHERE {string.Join(" AND ", where)}
        GROUP BY s.OWNERID
        ORDER BY Asignados DESC
        """;

    private static TecnicoProductividad Mapear(FilaProductividad f) => new(
        f.TecnicoId, f.Tecnico, f.Asignados, f.Cerrados, f.Vencidos,
        SlaPct(f.Asignados, f.Vencidos), f.PromedioHorasPrimeraRespuesta, f.PromedioHorasResolucion);

    private static double SlaPct(int asignados, int vencidos) =>
        asignados == 0 ? 100 : Math.Round((asignados - vencidos) * 100.0 / asignados, 2);

    private sealed record FilaProductividad(
        long TecnicoId, string Tecnico, int Asignados, int Cerrados, int Vencidos,
        double? PromedioHorasPrimeraRespuesta, double? PromedioHorasResolucion);

    public async Task<IReadOnlyList<ProductividadPunto>> TendenciaProductividadAsync(
        long tecnicoId, DateOnly desde, DateOnly hasta, Agrupacion agrupacion, CancellationToken ct)
    {
        var p = new DynamicParameters();
        p.Add("tecnicoId", tecnicoId);
        p.Add("desdeMs", SqlTime.StartOfDayMs(desde));
        p.Add("hastaMs", SqlTime.StartOfDayMs(hasta.AddDays(1)));

        var bucket = Bucket("w.CREATEDTIME", agrupacion);
        var sql = $"""
            SELECT {bucket} AS Periodo,
                   COUNT(*) AS Asignados,
                   ISNULL(SUM(CASE WHEN st.ISPENDING = 0 THEN 1 ELSE 0 END), 0) AS Cerrados,
                   ISNULL(SUM(CASE WHEN s.ISOVERDUE = 1 THEN 1 ELSE 0 END), 0) AS Vencidos,
                   CAST(AVG(CASE WHEN w.RESPONDEDTIME > s.ASSIGNEDTIME
                                 THEN (w.RESPONDEDTIME - s.ASSIGNEDTIME) / 3600000.0 END) AS float) AS PromedioHorasPrimeraRespuesta,
                   CAST(AVG(CASE WHEN st.ISPENDING = 0 AND w.COMPLETEDTIME > w.CREATEDTIME
                                 THEN (w.COMPLETEDTIME - w.CREATEDTIME) / 3600000.0 END) AS float) AS PromedioHorasResolucion
            FROM WorkOrder w
            JOIN WorkOrderStates s ON s.WORKORDERID = w.WORKORDERID
            JOIN StatusDefinition st ON st.STATUSID = s.STATUSID
            WHERE s.OWNERID = @tecnicoId AND w.CREATEDTIME >= @desdeMs AND w.CREATEDTIME < @hastaMs
            GROUP BY {bucket}
            """;

        await using var cn = await factory.OpenAsync(ct);
        var filas = (await cn.QueryAsync<FilaTendencia>(Cmd(sql, p, ct)))
            .ToDictionary(f => DateOnly.FromDateTime(f.Periodo));

        // Rellena con periodos vacíos (0 asignados, SLA 100%, sin promedio) para que la serie no tenga huecos.
        var puntos = new List<ProductividadPunto>();
        var actual = Inicio(desde, agrupacion);
        var fin = Inicio(hasta, agrupacion);
        while (actual <= fin)
        {
            puntos.Add(filas.TryGetValue(actual, out var f)
                ? new ProductividadPunto(actual, f.Asignados, f.Cerrados, SlaPct(f.Asignados, f.Vencidos), f.PromedioHorasPrimeraRespuesta, f.PromedioHorasResolucion)
                : new ProductividadPunto(actual, 0, 0, 100, null, null));
            actual = agrupacion switch
            {
                Agrupacion.Mes => actual.AddMonths(1),
                Agrupacion.Semana => actual.AddDays(7),
                _ => actual.AddDays(1),
            };
        }
        return puntos;
    }

    // Mismo criterio de "inicio de semana/mes" que el Tablero (Application.Features.Dashboard), duplicado a propósito
    // para no acoplar este repositorio al de Dashboard por un detalle tan pequeño.
    private static string Bucket(string columna, Agrupacion agrupacion)
    {
        var dia = $"CAST({SqlTime.Local(columna)} AS date)";
        return agrupacion switch
        {
            Agrupacion.Mes => $"DATEFROMPARTS(YEAR({dia}), MONTH({dia}), 1)",
            Agrupacion.Semana => $"DATEADD(DAY, -(DATEDIFF(DAY, '19000101', {dia}) % 7), {dia})",
            _ => dia,
        };
    }

    private static DateOnly Inicio(DateOnly d, Agrupacion a) => a switch
    {
        Agrupacion.Mes => new DateOnly(d.Year, d.Month, 1),
        Agrupacion.Semana => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
        _ => d,
    };

    private sealed record FilaTendencia(
        DateTime Periodo, int Asignados, int Cerrados, int Vencidos,
        double? PromedioHorasPrimeraRespuesta, double? PromedioHorasResolucion);
}
