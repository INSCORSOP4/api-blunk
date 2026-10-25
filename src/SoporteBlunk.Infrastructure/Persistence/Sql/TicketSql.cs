using Dapper;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Infrastructure.Persistence.Sql;

internal static class TicketSql
{
    public const int CommandTimeoutSeconds = 30;

    public const string Joins = """
        FROM WorkOrder w
        JOIN WorkOrderStates s ON s.WORKORDERID = w.WORKORDERID
        JOIN StatusDefinition st ON st.STATUSID = s.STATUSID
        LEFT JOIN PriorityDefinition pr ON pr.PRIORITYID = s.PRIORITYID
        LEFT JOIN CategoryDefinition cat ON cat.CATEGORYID = s.CATEGORYID
        LEFT JOIN WorkOrder_Queue q ON q.WORKORDERID = w.WORKORDERID
        LEFT JOIN QueueDefinition qd ON qd.QUEUEID = q.QUEUEID
        LEFT JOIN AaaUser tu ON tu.USER_ID = s.OWNERID
        LEFT JOIN AaaUser ru ON ru.USER_ID = w.REQUESTERID
        """;

    public static string FullName(string alias) =>
        $"NULLIF(LTRIM(RTRIM(CONCAT({alias}.FIRST_NAME, ' ', {alias}.LAST_NAME))), '')";

    public static readonly string ResumenColumns = $"""
        w.WORKORDERID AS Id,
        ISNULL(w.TITLE, '') AS Titulo,
        s.STATUSID AS EstadoId,
        st.STATUSNAME AS Estado,
        pr.PRIORITYNAME AS Prioridad,
        cat.CATEGORYNAME AS Categoria,
        qd.QUEUENAME AS Grupo,
        s.OWNERID AS TecnicoId,
        {FullName("tu")} AS Tecnico,
        {FullName("ru")} AS Solicitante,
        CAST(ISNULL(s.ISOVERDUE, 0) AS bit) AS Vencido,
        {SqlTime.Local("w.CREATEDTIME")} AS Creado,
        {SqlTime.Local("w.DUEBYTIME")} AS Vence,
        {SqlTime.Local("w.COMPLETEDTIME")} AS Completado
        """;

    public static string BuildWhere(TicketFiltro f, DynamicParameters p)
    {
        var where = new List<string>();

        if (f.EstadoId is { } estado) { where.Add("s.STATUSID = @estadoId"); p.Add("estadoId", estado); }
        if (f.TecnicoId is { } tecnico) { where.Add("s.OWNERID = @tecnicoId"); p.Add("tecnicoId", tecnico); }
        if (f.GrupoId is { } grupo) { where.Add("q.QUEUEID = @grupoId"); p.Add("grupoId", grupo); }
        if (f.CategoriaId is { } cat) { where.Add("s.CATEGORYID = @categoriaId"); p.Add("categoriaId", cat); }
        if (f.PrioridadId is { } prio) { where.Add("s.PRIORITYID = @prioridadId"); p.Add("prioridadId", prio); }
        if (f.Vencido is { } venc) { where.Add(venc ? "s.ISOVERDUE = 1" : "ISNULL(s.ISOVERDUE, 0) = 0"); }
        // "Sin responder" es un estado de negocio (abierto y sin primera respuesta), no una columna cruda.
        if (f.SinResponder is { } sr) where.Add(sr ? "(st.ISPENDING = 1 AND w.RESPONDEDTIME = 0)" : "NOT (st.ISPENDING = 1 AND w.RESPONDEDTIME = 0)");

        SqlTime.AddCreatedRange(where, p, f.Desde, f.Hasta);

        if (!string.IsNullOrWhiteSpace(f.Buscar))
        {
            var texto = f.Buscar.Trim();
            var escapado = texto.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
            p.Add("buscar", $"%{escapado}%");
            if (long.TryParse(texto, out var id))
            {
                where.Add("(w.WORKORDERID = @buscarId OR w.TITLE LIKE @buscar)");
                p.Add("buscarId", id);
            }
            else
            {
                where.Add("w.TITLE LIKE @buscar");
            }
        }

        return where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where);
    }

    public static string OrderBy(TicketOrden orden) => orden switch
    {
        TicketOrden.CreadoAsc => "w.CREATEDTIME ASC, w.WORKORDERID ASC",
        TicketOrden.VenceAsc => "CASE WHEN w.DUEBYTIME > 0 THEN 0 ELSE 1 END, w.DUEBYTIME ASC, w.WORKORDERID ASC",
        TicketOrden.IdDesc => "w.WORKORDERID DESC",
        _ => "w.CREATEDTIME DESC, w.WORKORDERID DESC",
    };
}
