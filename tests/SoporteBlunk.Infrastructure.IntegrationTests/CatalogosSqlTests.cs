using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Infrastructure.Persistence;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// Catálogos de ServiceDesk migrados a EF Core. El primer grupo no necesita base de datos (revisa el SQL generado
/// contra el que tenía Dapper); el segundo compara los resultados reales y se omite sin ConnectionStrings__ServiceDesk.
/// </summary>
public class CatalogosSqlTests
{
    // catálogo, tabla, columna de Id, columna de nombre, columnas de ORDER BY (en orden), columna de padre (o vacío)
    public static TheoryData<string, string, string, string, string, string> Esperados => new()
    {
        { "estados", "StatusDefinition", "STATUSID", "STATUSNAME", "STATUSID", "" },
        { "prioridades", "PriorityDefinition", "PRIORITYID", "PRIORITYNAME", "PRIORITYORDER,PRIORITYID", "" },
        { "grupos", "QueueDefinition", "QUEUEID", "QUEUENAME", "QUEUENAME", "" },
        { "categorias", "CategoryDefinition", "CATEGORYID", "CATEGORYNAME", "CATEGORYNAME", "" },
        { "subcategorias", "SubCategoryDefinition", "SUBCATEGORYID", "NAME", "NAME", "CATEGORYID" },
        { "items", "ItemDefinition", "ITEMID", "NAME", "NAME", "SUBCATEGORYID" },
        { "urgencias", "UrgencyDefinition", "URGENCYID", "NAME", "URGENCYID", "" },
        { "impactos", "ImpactDefinition", "IMPACTID", "NAME", "IMPACTID", "" },
        { "modos", "ModeDefinition", "MODEID", "MODENAME", "MODEID", "" },
        { "tipos-solicitud", "RequestTypeDefinition", "REQUESTTYPEID", "NAME", "REQUESTTYPEID", "" },
        { "niveles", "LevelDefinition", "LEVELID", "LEVELNAME", "LEVELID", "" },
        { "departamentos", "DepartmentDefinition", "DEPTID", "DEPTNAME", "DEPTNAME", "" },
    };

    private static ServiceDeskDbContext Contexto(string cs = "Server=x;Database=x") => ContextoDePrueba.ServiceDesk(cs);

    [Theory]
    [MemberData(nameof(Esperados))]
    public void El_SQL_generado_equivale_al_de_Dapper(string catalogo, string tabla, string id, string nombre, string orden, string padre)
    {
        using var db = Contexto();
        var sql = Regex.Replace(CatalogoReadRepository.Consulta(db, catalogo).ToQueryString(), @"\s+", " ");

        sql.Should().Contain($"FROM [{tabla}] AS");
        sql.Should().Contain("ISDELETED] = CAST(0 AS bit)");
        sql.Should().MatchRegex($@"^SELECT \[\w\]\.\[{id}\], COALESCE\(\[\w\]\.\[{nombre}\], N''\)" +
                                (padre.Length > 0 ? $@", \[\w\]\.\[{padre}\]" : "") + " FROM");
        var columnasOrden = string.Join(", ", orden.Split(',').Select(c => $@"\[\w\]\.\[{c}\]"));
        sql.Should().MatchRegex($"ORDER BY {columnasOrden}$");
    }

    [Fact]
    public void Un_catalogo_desconocido_se_rechaza()
    {
        using var db = Contexto();
        FluentActions.Invoking(() => CatalogoReadRepository.Consulta(db, "no-existe"))
            .Should().Throw<ArgumentException>();
    }

    [SkippableTheory]
    [MemberData(nameof(Esperados))]
    public async Task Los_resultados_son_identicos_a_los_de_Dapper(string catalogo, string _, string __, string ___, string ____, string _____)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__ServiceDesk");
        Skip.If(string.IsNullOrWhiteSpace(cs), "Falta ConnectionStrings__ServiceDesk");

        await using var db = Contexto(cs!);
        var nuevo = await new CatalogoReadRepository(db).ObtenerAsync(catalogo, default);
        var anterior = await new CatalogoReadRepositoryDapper(new SqlConnectionFactory(cs!)).ObtenerAsync(catalogo, default);

        nuevo.Should().Equal(anterior);
    }
}
