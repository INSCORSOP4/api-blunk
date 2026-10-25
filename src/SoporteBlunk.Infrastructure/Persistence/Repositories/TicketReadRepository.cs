using Dapper;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Sql;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class TicketReadRepository(ISqlConnectionFactory factory) : ITicketReadRepository
{
    private static CommandDefinition Cmd(string sql, object? p, CancellationToken ct) =>
        new(sql, p, commandTimeout: TicketSql.CommandTimeoutSeconds, cancellationToken: ct);

    public async Task<PagedResult<TicketResumen>> ListarAsync(TicketFiltro filtro, TicketOrden orden, int page, int pageSize, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = TicketSql.BuildWhere(filtro, p);
        p.Add("offset", (page - 1) * pageSize);
        p.Add("pageSize", pageSize);

        var countSql = $"SELECT COUNT(*) {TicketSql.Joins} {where}";
        var listSql = $"""
            SELECT {TicketSql.ResumenColumns}
            {TicketSql.Joins}
            {where}
            ORDER BY {TicketSql.OrderBy(orden)}
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
            """;

        await using var cn = await factory.OpenAsync(ct);
        var total = await cn.ExecuteScalarAsync<int>(Cmd(countSql, p, ct));
        var items = await cn.QueryAsync<TicketResumen>(Cmd(listSql, p, ct));
        return new PagedResult<TicketResumen>(items.AsList(), page, pageSize, total);
    }

    public async Task<IReadOnlyList<TicketResumen>> ListarParaExportAsync(TicketFiltro filtro, int maximo, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var where = TicketSql.BuildWhere(filtro, p);
        p.Add("maximo", maximo);
        var sql = $"""
            SELECT TOP (@maximo) {TicketSql.ResumenColumns}
            {TicketSql.Joins}
            {where}
            ORDER BY w.CREATEDTIME DESC, w.WORKORDERID DESC
            """;

        await using var cn = await factory.OpenAsync(ct);
        return (await cn.QueryAsync<TicketResumen>(Cmd(sql, p, ct))).AsList();
    }

    public async Task<TicketDetalle?> ObtenerAsync(long id, CancellationToken ct)
    {
        var sql = $"""
            SELECT {TicketSql.ResumenColumns},
                   wd.FULLDESCRIPTION AS Descripcion,
                   rr.RESOLUTION AS Resolucion,
                   sub.NAME AS Subcategoria,
                   it.NAME AS Item,
                   ur.NAME AS Urgencia,
                   im.NAME AS Impacto,
                   md.MODENAME AS Modo,
                   rt.NAME AS TipoSolicitud,
                   lv.LEVELNAME AS Nivel,
                   dp.DEPTNAME AS Departamento,
                   CAST(ISNULL(s.REOPENED, 0) AS bit) AS Reabierto,
                   {SqlTime.Local("w.RESPONDEDTIME")} AS Respondido,
                   {SqlTime.Local("w.RESOLVEDTIME")} AS Resuelto
            {TicketSql.Joins}
            LEFT JOIN SubCategoryDefinition sub ON sub.SUBCATEGORYID = s.SUBCATEGORYID
            LEFT JOIN ItemDefinition it ON it.ITEMID = s.ITEMID
            LEFT JOIN UrgencyDefinition ur ON ur.URGENCYID = s.URGENCYID
            LEFT JOIN ImpactDefinition im ON im.IMPACTID = s.IMPACTID
            LEFT JOIN ModeDefinition md ON md.MODEID = w.MODEID
            LEFT JOIN RequestTypeDefinition rt ON rt.REQUESTTYPEID = s.REQUESTTYPEID
            LEFT JOIN LevelDefinition lv ON lv.LEVELID = s.LEVELID
            LEFT JOIN DepartmentDefinition dp ON dp.DEPTID = w.DEPTID
            OUTER APPLY (SELECT TOP 1 FULLDESCRIPTION FROM WorkOrderToDescription d WHERE d.WORKORDERID = w.WORKORDERID) wd
            OUTER APPLY (SELECT TOP 1 RESOLUTION FROM RequestResolution r WHERE r.REQUESTID = w.WORKORDERID ORDER BY r.LASTUPDATEDTIME DESC) rr
            WHERE w.WORKORDERID = @id
            """;

        await using var cn = await factory.OpenAsync(ct);
        var row = await cn.QueryFirstOrDefaultAsync<DetalleRow>(Cmd(sql, new { id }, ct));
        return row?.ToDomain();
    }

    public async Task<bool> ExisteAsync(long id, CancellationToken ct)
    {
        await using var cn = await factory.OpenAsync(ct);
        return await cn.ExecuteScalarAsync<int>(Cmd("SELECT COUNT(1) FROM WorkOrder WHERE WORKORDERID = @id", new { id }, ct)) > 0;
    }

    public async Task<IReadOnlyList<TicketHistorialItem>> HistorialAsync(long id, CancellationToken ct)
    {
        var sql = $"""
            SELECT TOP (500)
                   h.HISTORYID AS Id,
                   {SqlTime.Local("h.OPERATIONTIME")} AS Fecha,
                   h.OPERATION AS Operacion,
                   h.DESCRIPTION AS Descripcion,
                   {TicketSql.FullName("u")} AS Usuario
            FROM WorkOrderHistory h
            LEFT JOIN AaaUser u ON u.USER_ID = h.OPERATIONOWNERID
            WHERE h.WORKORDERID = @id
            ORDER BY h.OPERATIONTIME DESC, h.HISTORYID DESC
            """;

        await using var cn = await factory.OpenAsync(ct);
        return (await cn.QueryAsync<TicketHistorialItem>(Cmd(sql, new { id }, ct))).AsList();
    }

    public async Task<TicketTiempos> TiemposAsync(long id, CancellationToken ct)
    {
        var estados = $"""
            SELECT sd.STATUSNAME AS Estado, {SqlTime.Local("i.ENDTIME")} AS Fin, ISNULL(i.TIMESPENT, 0) AS TiempoRegistrado
            FROM WO_Assessment a
            JOIN WO_Status_Info i ON i.ASSESSMENTID = a.ASSESSMENTID
            JOIN StatusDefinition sd ON sd.STATUSID = i.STATUSID
            WHERE a.WORKORDERID = @id
            ORDER BY a.EXECUTEDTIME, a.ASSESSMENTID
            """;
        var tecnicos = $"""
            SELECT {TicketSql.FullName("u")} AS Tecnico, {SqlTime.Local("i.ENDTIME")} AS Fin, ISNULL(i.TIMESPENT, 0) AS TiempoRegistrado
            FROM WO_Assessment a
            JOIN WO_Tech_Info i ON i.ASSESSMENTID = a.ASSESSMENTID
            LEFT JOIN AaaUser u ON u.USER_ID = i.TECHNICIANID
            WHERE a.WORKORDERID = @id
            ORDER BY a.EXECUTEDTIME, a.ASSESSMENTID
            """;
        var grupos = $"""
            SELECT qd.QUEUENAME AS Grupo, {SqlTime.Local("i.ENDTIME")} AS Fin, ISNULL(i.TIMESPENT, 0) AS TiempoRegistrado
            FROM WO_Assessment a
            JOIN WO_Group_Info i ON i.ASSESSMENTID = a.ASSESSMENTID
            LEFT JOIN QueueDefinition qd ON qd.QUEUEID = i.GROUPID
            WHERE a.WORKORDERID = @id
            ORDER BY a.EXECUTEDTIME, a.ASSESSMENTID
            """;

        await using var cn = await factory.OpenAsync(ct);
        var porEstado = await cn.QueryAsync<TicketTiempoEstado>(Cmd(estados, new { id }, ct));
        var porTecnico = await cn.QueryAsync<TicketTiempoTecnico>(Cmd(tecnicos, new { id }, ct));
        var porGrupo = await cn.QueryAsync<TicketTiempoGrupo>(Cmd(grupos, new { id }, ct));
        return new TicketTiempos(porEstado.AsList(), porTecnico.AsList(), porGrupo.AsList());
    }

    // Dapper no mapea records anidados: se aplana la fila y se arma el detalle en código.
    private sealed record DetalleRow(
        long Id, string Titulo, long EstadoId, string Estado, string? Prioridad, string? Categoria, string? Grupo,
        long? TecnicoId, string? Tecnico, string? Solicitante, bool Vencido,
        DateTimeOffset? Creado, DateTimeOffset? Vence, DateTimeOffset? Completado,
        string? Descripcion, string? Resolucion, string? Subcategoria, string? Item, string? Urgencia, string? Impacto,
        string? Modo, string? TipoSolicitud, string? Nivel, string? Departamento, bool Reabierto,
        DateTimeOffset? Respondido, DateTimeOffset? Resuelto)
    {
        public TicketDetalle ToDomain() => new(
            new TicketResumen(Id, Titulo, EstadoId, Estado, Prioridad, Categoria, Grupo, TecnicoId, Tecnico, Solicitante, Vencido, Creado, Vence, Completado),
            Descripcion, Resolucion, Subcategoria, Item, Urgencia, Impacto, Modo, TipoSolicitud, Nivel, Departamento, Reabierto, Respondido, Resuelto);
    }
}
