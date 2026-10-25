using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Queries;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>Bitácora, Auditoría de Microsip y Archivos en EF Core: el SQL y el contrato de errores se revisan sin base de datos.</summary>
public class KbFase2SqlTests
{
    private static string Normal(string sql) => Regex.Replace(sql, @"\s+", " ");

    [Fact]
    public void Las_actividades_filtran_por_grupo_y_ordenan_por_orden_y_nombre()
    {
        using var db = ContextoDePrueba.Kb("Server=x;Database=x");
        var soloActivas = Normal(BitacoraConsultas.Actividades(db, "g", incluirInactivas: false).ToQueryString());
        var todas = Normal(BitacoraConsultas.Actividades(db, "g", incluirInactivas: true).ToQueryString());

        soloActivas.Should().Contain("FROM [bitacora].[Actividad]").And.Contain("[a].[Grupo] = @grupo");
        soloActivas.Should().Contain("AND [a].[Activa] = CAST(1 AS bit)").And.EndWith("ORDER BY [a].[Orden], [a].[Nombre]");
        todas.Should().Contain("[a].[Grupo] = @grupo").And.NotContain("Activa] = CAST(1 AS bit)", "incluir inactivas no filtra por Activa");
        todas.Should().EndWith("ORDER BY [a].[Orden], [a].[Nombre]");
    }

    [Fact]
    public void El_checklist_une_el_registro_del_dia_con_LEFT_JOIN_y_conserva_las_desactivadas_con_registro()
    {
        using var db = ContextoDePrueba.Kb("Server=x;Database=x");
        var sql = Normal(BitacoraConsultas.Checklist(db, "g", new DateOnly(2026, 3, 1)).ToQueryString());

        sql.Should().Contain("LEFT JOIN").And.Contain("[bitacora].[ChecklistDiario]");
        sql.Should().Contain("[c].[Fecha] = @fecha");
        sql.Should().Contain("[a].[Activa] = CAST(1 AS bit) OR ([c0].[Fecha] IS NOT NULL AND [c0].[ActividadId] IS NOT NULL)");
        sql.Should().EndWith("ORDER BY [a].[Orden], [a].[Nombre]");
    }

    [Fact]
    public void El_resumen_cuenta_cada_estado_y_el_total_por_fecha()
    {
        using var db = ContextoDePrueba.Kb("Server=x;Database=x");
        var sql = Normal(BitacoraConsultas.ResumenPorDia(db, "g", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)).ToQueryString());

        sql.Should().Contain("INNER JOIN [bitacora].[Actividad]").And.Contain("GROUP BY [c].[Fecha]");
        sql.Should().Contain("[c].[Fecha] >= @desde AND [c].[Fecha] <= @hasta");
        foreach (var estado in new[] { "Ok", "Advertencia", "Falla", "Critico", "NoAplica" })
            sql.Should().MatchRegex($@"WHEN \[c\]\.\[Estado\] = N?'{estado}' THEN 1 END");
        sql.Should().Contain("COUNT(*) AS [Total]");
    }

    [Fact]
    public void Los_ultimos_cambios_ordenan_por_la_fecha_de_actualizacion_o_de_creacion()
    {
        using var db = ContextoDePrueba.Kb("Server=x;Database=x");
        var sql = Normal(BitacoraConsultas.UltimosCambios(db, 25).ToQueryString());

        sql.Should().StartWith("DECLARE @p int = 25; SELECT TOP(@p)");
        sql.Should().Contain("COALESCE([c].[ActualizadoEn], [c].[CreadoEn]) DESC");
        sql.Should().Contain("COALESCE([c].[ActualizadoPor], [c].[CreadoPor]) AS [Usuario]");
    }

    [Fact]
    public void Los_archivos_traen_la_revision_mas_reciente_de_cada_archivo_activo()
    {
        using var db = ContextoDePrueba.Kb("Server=x;Database=x");
        var sql = Normal(ArchivosConsultas.ConUltimaRevision(db).ToQueryString());

        sql.Should().Contain("FROM [archivos].[ArchivoEsperado]").And.Contain("[a].[Activo] = CAST(1 AS bit)");
        sql.Should().Contain("[archivos].[RevisionArchivo]").And.Contain("ORDER BY [r].[Fecha] DESC");
        sql.Should().EndWith("ORDER BY [a].[Ruta], [a].[NombreArchivo]");
    }

    // ---- Contrato de errores: lo que el middleware traduce a 503 no debe cambiar al pasar de Dapper a EF Core ----

    private const string ServidorCaido = "Server=127.0.0.1,1;Database=SoporteKB;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True";
    private static readonly UsuarioActual Quien = new(Guid.NewGuid(), "Prueba", "prueba@local");
    private static readonly UsuarioMicrosip Objetivo = new(1, "USUARIO", "A", true, null, null, null, null, false);

    private static Dictionary<string, Func<KbDbContext, Task>> Operaciones() => new()
    {
        ["Bitacora.ListarActividades"] = db => new BitacoraRepository(db).ListarActividadesAsync("g", false, default),
        ["Bitacora.ObtenerActividad"] = db => new BitacoraRepository(db).ObtenerActividadAsync(1, default),
        ["Bitacora.CrearActividad"] = db => new BitacoraRepository(db).CrearActividadAsync("g", "n", "u", default),
        ["Bitacora.ActualizarActividad"] = db => new BitacoraRepository(db).ActualizarActividadAsync(1, "n", null, null, "u", default),
        ["Bitacora.ObtenerChecklist"] = db => new BitacoraRepository(db).ObtenerChecklistAsync("g", new DateOnly(2026, 1, 1), default),
        ["Bitacora.GuardarChecklist"] = db => new BitacoraRepository(db).GuardarChecklistAsync(1, new DateOnly(2026, 1, 1), EstadoChecklist.Ok, null, "u", default),
        ["Bitacora.ObtenerResumen"] = db => new BitacoraRepository(db).ObtenerResumenAsync("g", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), default),
        ["Bitacora.UltimosCambios"] = db => new BitacoraRepository(db).UltimosCambiosAsync(10, default),
        ["Auditoria.Registrar"] = db => new AuditoriaMicrosipRepository(db).RegistrarAsync(Quien, Objetivo, AccionMicrosip.Insertar, null, "{}", default),
        ["Auditoria.Marcar"] = db => new AuditoriaMicrosipRepository(db).MarcarAsync(1, ResultadoCambio.Aplicado, null, default),
        ["Auditoria.Listar"] = db => new AuditoriaMicrosipRepository(db).ListarAsync(10, default),
        ["Archivos.Listar"] = db => new ArchivosReadRepository(db).ListarAsync(default),
    };

    public static TheoryData<string> Nombres => new(Operaciones().Keys);

    [Theory]
    [MemberData(nameof(Nombres))]
    public async Task Sin_cadena_de_SoporteKB_cada_operacion_falla_con_KbNoDisponible(string operacion)
    {
        await using var db = ContextoDePrueba.Kb(null);

        await FluentActions.Awaiting(() => Operaciones()[operacion](db))
            .Should().ThrowAsync<KbNoDisponibleException>().WithMessage("*no está configurada*");
    }

    [Theory]
    [MemberData(nameof(Nombres))]
    public async Task Con_el_servidor_caido_cada_operacion_falla_con_KbNoDisponible_y_no_con_un_aviso_enganoso(string operacion)
    {
        await using var db = ContextoDePrueba.Kb(ServidorCaido);

        // Antes la fábrica de conexiones convertía el fallo de conexión en KbNoDisponibleException; Auditoría no debe decir "¿se ejecutó db/002?".
        await FluentActions.Awaiting(() => Operaciones()[operacion](db))
            .Should().ThrowAsync<KbNoDisponibleException>().WithMessage("*No se pudo conectar con la base de conocimiento*");
    }
}
