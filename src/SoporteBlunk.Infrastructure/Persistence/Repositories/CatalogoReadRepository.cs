using Dapper;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Sql;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class CatalogoReadRepository(ISqlConnectionFactory factory) : ICatalogoReadRepository
{
    // Clave pública -> consulta fija (nunca se concatena entrada del usuario).
    private static readonly Dictionary<string, string> Queries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["estados"] = "SELECT STATUSID AS Id, STATUSNAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM StatusDefinition WHERE ISDELETED = 0 ORDER BY STATUSID",
        ["prioridades"] = "SELECT PRIORITYID AS Id, PRIORITYNAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM PriorityDefinition WHERE ISDELETED = 0 ORDER BY PRIORITYORDER, PRIORITYID",
        ["grupos"] = "SELECT QUEUEID AS Id, QUEUENAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM QueueDefinition WHERE ISDELETED = 0 ORDER BY QUEUENAME",
        ["categorias"] = "SELECT CATEGORYID AS Id, CATEGORYNAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM CategoryDefinition WHERE ISDELETED = 0 ORDER BY CATEGORYNAME",
        ["subcategorias"] = "SELECT SUBCATEGORYID AS Id, NAME AS Nombre, CATEGORYID AS PadreId FROM SubCategoryDefinition WHERE ISDELETED = 0 ORDER BY NAME",
        ["items"] = "SELECT ITEMID AS Id, NAME AS Nombre, SUBCATEGORYID AS PadreId FROM ItemDefinition WHERE ISDELETED = 0 ORDER BY NAME",
        ["urgencias"] = "SELECT URGENCYID AS Id, NAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM UrgencyDefinition WHERE ISDELETED = 0 ORDER BY URGENCYID",
        ["impactos"] = "SELECT IMPACTID AS Id, NAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM ImpactDefinition WHERE ISDELETED = 0 ORDER BY IMPACTID",
        ["modos"] = "SELECT MODEID AS Id, MODENAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM ModeDefinition WHERE ISDELETED = 0 ORDER BY MODEID",
        ["tipos-solicitud"] = "SELECT REQUESTTYPEID AS Id, NAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM RequestTypeDefinition WHERE ISDELETED = 0 ORDER BY REQUESTTYPEID",
        ["niveles"] = "SELECT LEVELID AS Id, LEVELNAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM LevelDefinition WHERE ISDELETED = 0 ORDER BY LEVELID",
        ["departamentos"] = "SELECT DEPTID AS Id, DEPTNAME AS Nombre, CAST(NULL AS bigint) AS PadreId FROM DepartmentDefinition WHERE ISDELETED = 0 ORDER BY DEPTNAME",
    };

    public async Task<IReadOnlyList<CatalogoItem>> ObtenerAsync(string catalogo, CancellationToken ct)
    {
        if (!Queries.TryGetValue(catalogo, out var sql))
            throw new ArgumentException($"Catálogo desconocido: {catalogo}", nameof(catalogo));

        await using var cn = await factory.OpenAsync(ct);
        var rows = await cn.QueryAsync<CatalogoItem>(new CommandDefinition(sql, commandTimeout: TicketSql.CommandTimeoutSeconds, cancellationToken: ct));
        return rows.AsList();
    }
}
