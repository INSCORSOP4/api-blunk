using FluentAssertions;
using FluentValidation;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Kb;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>Utilidades para probar contra SoporteKB real. Se omiten sin ConnectionStrings__SoporteKB.</summary>
internal static class KbReal
{
    public static string? Cadena => Environment.GetEnvironmentVariable("ConnectionStrings__SoporteKB");

    public static string Requerir()
    {
        Skip.If(string.IsNullOrWhiteSpace(Cadena), "Falta ConnectionStrings__SoporteKB");
        return Cadena!;
    }

    public static async Task<List<string>> Columna(string cadena, string sql)
    {
        await using var cn = new SqlConnection(cadena);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        var filas = new List<string>();
        while (await r.ReadAsync()) filas.Add(r.GetString(0));
        return filas;
    }

    public static async Task Ejecutar(string cadena, string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var cn = new SqlConnection(cadena);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (n, v) in parametros) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<DateTime> AhoraServidor(string cadena)
    {
        await using var cn = new SqlConnection(cadena);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT SYSDATETIME()", cn);
        return (DateTime)(await cmd.ExecuteScalarAsync())!;
    }

    // Una fecha del servidor (sin zona) o "ahora" si es reciente y con el desplazamiento esperado de México.
    public static string Cuando(DateTimeOffset? valor, DateTime ahoraServidor)
    {
        if (valor is null) return "-";
        var local = ZonaHoraria.Zona.GetUtcOffset(valor.Value.DateTime);
        var reciente = Math.Abs((valor.Value.DateTime - ahoraServidor).TotalSeconds) < 180;
        return reciente && valor.Value.Offset == local ? "ahora" : valor.Value.ToString("o");
    }
}

/// <summary>Colección de xUnit: sus clases se ejecutan una tras otra, nunca en paralelo.</summary>
[CollectionDefinition(Nombre)]
public sealed class ColeccionSoporteKb
{
    public const string Nombre = "SoporteKB real";
}

/// <summary>El modelo de SoporteKB debe corresponder al esquema real (ver <see cref="SondaEsquema"/>).</summary>
public class EsquemaKbTests
{
    [SkippableFact]
    public async Task El_modelo_coincide_con_el_esquema_real()
    {
        var cs = KbReal.Requerir();
        await using var db = ContextoDePrueba.Kb(cs);

        var problemas = await SondaEsquema.CompararAsync(db, cs);

        problemas.Should().BeEmpty("el modelo de EF debe corresponder al esquema real de SoporteKB:\n" + string.Join("\n", problemas));
    }
}

/// <summary>
/// Comparan EF Core contra la implementación Dapper original en SoporteKB real. Comparten colección con las de escritura para no
/// ejecutarse a la vez: estas leen "los últimos N cambios" de toda la base y las otras crean y borran filas temporales.
/// </summary>
[Collection(ColeccionSoporteKb.Nombre)]
public class KbFase2LecturaParidadTests
{
    private static (SoporteBlunk.Infrastructure.Persistence.Contexts.KbDbContext Db, string Cadena) Contexto()
    {
        var cs = KbReal.Requerir();
        return (ContextoDePrueba.Kb(cs), cs);
    }

    [SkippableFact]
    public async Task Bitacora_lectura_coincide_en_todos_los_grupos_existentes()
    {
        var (db, cs) = Contexto();
        await using var _ = db;
        var nuevo = new BitacoraRepository(db);
        var anterior = new BitacoraRepositoryDapper(new KbConnectionFactory(cs));
        var grupos = await KbReal.Columna(cs, "SELECT DISTINCT Grupo FROM bitacora.Actividad WHERE Grupo NOT LIKE 'zz-t2-%' ORDER BY Grupo");
        grupos.Should().NotBeEmpty("la prueba necesita bitácoras reales para ser válida");
        var hoy = ZonaHoraria.Hoy();

        foreach (var grupo in grupos)
        {
            foreach (var incluir in new[] { false, true })
                (await nuevo.ListarActividadesAsync(grupo, incluir, default)).Should().Equal(await anterior.ListarActividadesAsync(grupo, incluir, default), $"{grupo} actividades inactivas={incluir}");

            for (var d = 0; d < 7; d++)
            {
                var dia = hoy.AddDays(-d);
                var n = await nuevo.ObtenerChecklistAsync(grupo, dia, default);
                var a = await anterior.ObtenerChecklistAsync(grupo, dia, default);
                n.Items.Should().BeEquivalentTo(a.Items, o => o.WithStrictOrdering(), $"{grupo} checklist {dia}");
                n.Grupo.Should().Be(a.Grupo);
                n.Fecha.Should().Be(a.Fecha);
            }

            (await nuevo.ObtenerResumenAsync(grupo, hoy.AddDays(-30), hoy, default))
                .Should().Equal(await anterior.ObtenerResumenAsync(grupo, hoy.AddDays(-30), hoy, default), $"{grupo} resumen");
        }

        foreach (var id in new[] { 1, 2, 3, -1 })
            (await nuevo.ObtenerActividadAsync(id, default)).Should().Be(await anterior.ObtenerActividadAsync(id, default), $"actividad {id}");

        // Con un tope grande entran todos los cambios; entre filas con la misma hora el SQL no define desempate, así que se compara el conjunto.
        string Firma(CambioChecklist c) => $"{c.Cuando:o}|{c.Grupo}|{c.ActividadNombre}|{c.Fecha}|{c.Estado}|{c.Observacion}|{c.Usuario}";
        // Se excluyen los grupos "zz-t2-": son de los tests de escritura, que corren en paralelo y crean y borran filas.
        var cambiosN = (await nuevo.UltimosCambiosAsync(100000, default)).Where(c => !c.Grupo.StartsWith("zz-t2-")).Select(Firma).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var cambiosA = (await anterior.UltimosCambiosAsync(100000, default)).Where(c => !c.Grupo.StartsWith("zz-t2-")).Select(Firma).OrderBy(x => x, StringComparer.Ordinal).ToList();
        cambiosN.Should().NotBeEmpty("la bitácora debe tener registros reales");
        cambiosN.Should().Equal(cambiosA);
        // El tope corta por la hora más reciente: con N menor que el total, EF y Dapper deben traer las mismas N filas más nuevas.
        var tope = Math.Min(2, cambiosN.Count);
        var topN = (await nuevo.UltimosCambiosAsync(tope, default)).ToList();
        topN.Should().HaveCount(tope);
        topN.Select(c => c.Cuando).Should().BeInDescendingOrder();
        topN.Select(Firma).Should().Equal((await anterior.UltimosCambiosAsync(tope, default)).Select(Firma));
    }

    [SkippableFact]
    public async Task Auditoria_de_Microsip_lectura_coincide()
    {
        var (db, cs) = Contexto();
        await using var _ = db;
        var nuevo = new AuditoriaMicrosipRepository(db);
        var anterior = new AuditoriaMicrosipRepositoryDapper(new KbConnectionFactory(cs));

        var n = await nuevo.ListarAsync(500, default);
        var a = await anterior.ListarAsync(500, default);

        n.Should().NotBeEmpty("la auditoría debe tener registros reales");
        // El desplazamiento horario lo compara a propósito: Dapper usaba el actual del servidor; EF usa el de México en esa fecha.
        n.Select(c => (c.Id, c.Fecha.Offset, c.Fecha)).Should().Equal(a.Select(c => (c.Id, c.Fecha.Offset, c.Fecha)));
        n.Should().BeEquivalentTo(a, o => o.WithStrictOrdering());
        (await nuevo.ListarAsync(3, default)).Should().HaveCount(3);
    }

    [SkippableFact]
    public async Task Archivos_coincide()
    {
        var (db, cs) = Contexto();
        await using var _ = db;

        var n = await new ArchivosReadRepository(db).ListarAsync(default);
        var a = await new ArchivosReadRepositoryDapper(new KbConnectionFactory(cs)).ListarAsync(default);

        n.Should().BeEquivalentTo(a, o => o.WithStrictOrdering());
    }
}

/// <summary>
/// Ejecutan el mismo guion de escrituras con Dapper y con EF Core, cada uno en su propio grupo de prueba, y comparan lo que devuelve cada
/// paso. Los datos se crean con nombres únicos por ejecución y se borran al terminar.
/// </summary>
[Collection(ColeccionSoporteKb.Nombre)]
public class KbFase2EscrituraParidadTests : IAsyncLifetime
{
    private readonly string _sufijo = Guid.NewGuid().ToString("N")[..8];
    private readonly string _cadena = KbReal.Cadena ?? "";
    private string GrupoDapper => $"zz-t2-d-{_sufijo}";
    private string GrupoEf => $"zz-t2-e-{_sufijo}";
    private string EmailDapper => $"zz-t2-d-{_sufijo}@prueba.local";
    private string EmailEf => $"zz-t2-e-{_sufijo}@prueba.local";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_cadena)) return;
        await KbReal.Ejecutar(_cadena, """
            DELETE c FROM bitacora.ChecklistDiario c JOIN bitacora.Actividad a ON a.ActividadId = c.ActividadId WHERE a.Grupo LIKE @g;
            DELETE FROM bitacora.Actividad WHERE Grupo LIKE @g;
            DELETE FROM aud.CambioMicrosip WHERE UsuarioEmail LIKE @e;
            """, ("@g", $"zz-t2-_-{_sufijo}"), ("@e", $"zz-t2-_-{_sufijo}@prueba.local"));
    }

    // Normaliza lo que cambia entre ejecuciones (ids, grupo, horas) para poder comparar los guiones de Dapper y de EF Core.
    private static async Task<List<string>> Guion(IBitacoraRepository repo, string grupo, DateOnly dia, DateTime ahora)
    {
        var t = new List<string>();
        var etiquetas = new Dictionary<int, string>();
        string Id(int id) => etiquetas.TryGetValue(id, out var e) ? e : $"?{id}";
        string F(DateTimeOffset? v) => KbReal.Cuando(v, ahora);
        string Act(ActividadBitacora a) => $"{Id(a.ActividadId)} {a.Nombre} grupo={(a.Grupo == grupo ? "G" : a.Grupo)} orden={a.Orden} activa={a.Activa}";
        string Item(ItemChecklist i) =>
            $"{Id(i.ActividadId)} {i.Nombre} orden={i.Orden} act={i.ActividadActiva} est={i.Estado?.ToString() ?? "-"} obs={i.Observacion ?? "-"} " +
            $"por={i.RevisadoPor ?? "-"} en={F(i.RevisadoEn)} actPor={i.ActualizadoPor ?? "-"} actEn={F(i.ActualizadoEn)}";

        async Task Paso(string nombre, Func<Task<IEnumerable<string>>> accion)
        {
            try { t.AddRange((await accion()).Select(l => $"{nombre}: {l}")); }
            catch (Exception ex) { t.Add($"{nombre}: !{ex.GetType().Name}: {ex.Message.Replace(grupo, "G")}"); }
        }

        async Task<IEnumerable<string>> Crear(string nombre)
        {
            var a = await repo.CrearActividadAsync(grupo, nombre, "usuario.uno", default);
            etiquetas[a.ActividadId] = $"#{nombre}";
            return [Act(a)];
        }

        await Paso("crear A", () => Crear("A"));
        await Paso("crear B", () => Crear("B"));
        await Paso("crear C", () => Crear("C"));
        await Paso("crear A duplicada", () => Crear("A"));
        var idA = etiquetas.First(e => e.Value == "#A").Key;
        var idB = etiquetas.First(e => e.Value == "#B").Key;
        var idC = etiquetas.First(e => e.Value == "#C").Key;

        await Paso("listar activas", async () => (await repo.ListarActividadesAsync(grupo, false, default)).Select(Act));
        await Paso("renombrar B", async () => [Act(await repo.ActualizarActividadAsync(idB, "B2", null, null, "usuario.dos", default))]);
        await Paso("desactivar C", async () => [Act(await repo.ActualizarActividadAsync(idC, null, false, null, "usuario.dos", default))]);
        await Paso("reordenar A", async () => [Act(await repo.ActualizarActividadAsync(idA, null, null, 10, "usuario.dos", default))]);
        await Paso("actualizar sin cambios", async () => [Act(await repo.ActualizarActividadAsync(idA, null, null, null, "usuario.dos", default))]);
        await Paso("renombrar B a un nombre repetido", async () => [Act(await repo.ActualizarActividadAsync(idB, "A", null, null, "usuario.dos", default))]);
        await Paso("listar activas (2)", async () => (await repo.ListarActividadesAsync(grupo, false, default)).Select(Act));
        await Paso("listar todas", async () => (await repo.ListarActividadesAsync(grupo, true, default)).Select(Act));
        await Paso("crear D (orden = max + 1)", () => Crear("D"));

        await Paso("guardar A Ok", async () => [Item(await repo.GuardarChecklistAsync(idA, dia, EstadoChecklist.Ok, null, "revisor.uno", default))]);
        await Paso("corregir A a Falla", async () => [Item(await repo.GuardarChecklistAsync(idA, dia, EstadoChecklist.Falla, "se cayo", "revisor.dos", default))]);
        await Paso("guardar B NoAplica", async () => [Item(await repo.GuardarChecklistAsync(idB, dia, EstadoChecklist.NoAplica, "no corre hoy", "revisor.uno", default))]);
        await Paso("guardar C (inactiva) Advertencia", async () => [Item(await repo.GuardarChecklistAsync(idC, dia, EstadoChecklist.Advertencia, "ojo", "revisor.uno", default))]);
        await Paso("Falla sin observación", async () => [Item(await repo.GuardarChecklistAsync(idB, dia.AddDays(1), EstadoChecklist.Falla, null, "revisor.uno", default))]);
        await Paso("actividad inexistente", async () => [Item(await repo.GuardarChecklistAsync(-1, dia, EstadoChecklist.Ok, null, "revisor.uno", default))]);

        await Paso("checklist del día", async () => (await repo.ObtenerChecklistAsync(grupo, dia, default)).Items.Select(Item));
        await Paso("checklist del día siguiente", async () => (await repo.ObtenerChecklistAsync(grupo, dia.AddDays(1), default)).Items.Select(Item));
        await Paso("resumen", async () => (await repo.ObtenerResumenAsync(grupo, dia.AddDays(-1), dia.AddDays(1), default)).Select(r => r.ToString()));
        await Paso("últimos cambios", async () => (await repo.UltimosCambiosAsync(100000, default))
            .Where(c => c.Grupo == grupo)
            .OrderBy(c => c.ActividadNombre, StringComparer.Ordinal)
            .Select(c => $"{c.ActividadNombre} {c.Fecha} {c.Estado} obs={c.Observacion ?? "-"} por={c.Usuario} cuando={F(c.Cuando)}"));
        return t;
    }

    [SkippableFact]
    public async Task La_bitacora_se_comporta_igual_que_con_Dapper()
    {
        var cs = KbReal.Requerir();
        await using var db = ContextoDePrueba.Kb(cs);
        var ahora = await KbReal.AhoraServidor(cs);
        var dia = ZonaHoraria.Hoy();

        var dapper = await Guion(new BitacoraRepositoryDapper(new KbConnectionFactory(cs)), GrupoDapper, dia, ahora);
        var ef = await Guion(new BitacoraRepository(db), GrupoEf, dia, ahora);

        ef.Should().Equal(dapper);
        // Salvaguardas de que el guion sí ejerció los casos importantes (no es una comparación vacía).
        ef.Should().Contain(l => l.StartsWith("crear A duplicada: !ValidationException"));
        ef.Should().Contain(l => l.StartsWith("renombrar B a un nombre repetido: !ValidationException"));
        ef.Should().Contain(l => l.StartsWith("Falla sin observación: !KbNoDisponibleException"));
        ef.Should().Contain(l => l.StartsWith("actividad inexistente: !KbNoDisponibleException"));
        ef.Should().Contain(l => l.StartsWith("crear D (orden = max + 1): #D D grupo=G orden=11"), "A se reordenó a 10, así que la nueva actividad queda en el máximo + 1");
        ef.Should().Contain(l => l.StartsWith("corregir A a Falla:") && l.Contains("actPor=revisor.dos") && l.Contains("actEn=ahora"));
        ef.Count(l => l.StartsWith("últimos cambios:")).Should().Be(3);
    }

    [SkippableFact]
    public async Task La_hora_de_las_actualizaciones_la_pone_el_servidor_de_base_de_datos()
    {
        var cs = KbReal.Requerir();
        await using var db = ContextoDePrueba.Kb(cs);
        var repo = new BitacoraRepository(db);
        var a = await repo.CrearActividadAsync(GrupoEf, "reloj", "u", default);

        await repo.ActualizarActividadAsync(a.ActividadId, "reloj2", null, null, "u", default);
        var ahoraServidor = await KbReal.AhoraServidor(cs);

        var filas = await KbReal.Columna(cs, $"SELECT CONVERT(varchar(30), ActualizadoEn, 126) FROM bitacora.Actividad WHERE ActividadId = {a.ActividadId}");
        var actualizadoEn = DateTime.Parse(filas.Single());
        Math.Abs((actualizadoEn - ahoraServidor).TotalSeconds).Should().BeLessThan(10, "ActualizadoEn debe ser la hora del servidor SQL, no la del servidor de la API");
    }

    [SkippableFact]
    public async Task Dos_guardados_simultaneos_del_mismo_registro_no_fallan()
    {
        var cs = KbReal.Requerir();
        await using var setup = ContextoDePrueba.Kb(cs);
        var a = await new BitacoraRepository(setup).CrearActividadAsync(GrupoEf, "carrera", "u", default);
        var dia = ZonaHoraria.Hoy();

        // Cada guardado usa su propio contexto (como dos peticiones HTTP). Los dos intentan crear el registro del día a la vez.
        var tareas = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = ContextoDePrueba.Kb(cs);
            return await new BitacoraRepository(db).GuardarChecklistAsync(a.ActividadId, dia, EstadoChecklist.Advertencia, $"hilo {i}", $"u{i}", default);
        });

        var resultados = await Task.WhenAll(tareas);

        resultados.Should().OnlyContain(r => r.Estado == EstadoChecklist.Advertencia);
        var filas = await KbReal.Columna(cs, $"SELECT CAST(COUNT(*) AS varchar(10)) FROM bitacora.ChecklistDiario WHERE ActividadId = {a.ActividadId}");
        filas.Single().Should().Be("1", "debe existir un solo registro por actividad y día");
    }

    private static async Task<List<string>> GuionAuditoria(IAuditoriaMicrosipRepository repo, string email, DateTime ahora)
    {
        var quien = new UsuarioActual(Guid.NewGuid(), "Quien Prueba", email);
        var t = new List<string>();
        var a = await repo.RegistrarAsync(quien, new UsuarioMicrosip(77, "USUARIO UNO", "A", true, null, null, null, null, false), AccionMicrosip.Insertar, null, "{\"x\":1}", default);
        var b = await repo.RegistrarAsync(quien, new UsuarioMicrosip(78, "USUARIO DOS", "A", true, null, null, null, null, true), AccionMicrosip.Actualizar, "{\"x\":1}", "{\"x\":2}", default);
        var c = await repo.RegistrarAsync(quien, new UsuarioMicrosip(79, "USUARIO TRES", "A", true, null, null, null, null, true), AccionMicrosip.Actualizar, "{}", "{\"x\":3}", default);
        await repo.MarcarAsync(a, ResultadoCambio.Aplicado, null, default);
        await repo.MarcarAsync(b, ResultadoCambio.Fallido, "no se pudo", default);
        t.Add($"ids ascendentes={a < b && b < c}");

        foreach (var f in (await repo.ListarAsync(100000, default)).Where(x => x.UsuarioEmail == email).OrderBy(x => x.MicrosipUsuarioId))
            t.Add($"{f.MicrosipUsuarioId} {f.MicrosipNombre} {f.Accion} {f.Resultado} antes={f.Antes ?? "-"} despues={f.Despues} error={f.Error ?? "-"} " +
                  $"usuario={f.UsuarioNombre} fecha={KbReal.Cuando(f.Fecha, ahora)}");
        t.Add($"top 2 trae {(await repo.ListarAsync(2, default)).Count}");
        return t;
    }

    [SkippableFact]
    public async Task La_auditoria_de_Microsip_escribe_y_marca_igual_que_con_Dapper()
    {
        var cs = KbReal.Requerir();
        await using var db = ContextoDePrueba.Kb(cs);
        var ahora = await KbReal.AhoraServidor(cs);

        var dapper = await GuionAuditoria(new AuditoriaMicrosipRepositoryDapper(new KbConnectionFactory(cs)), EmailDapper, ahora);
        var ef = await GuionAuditoria(new AuditoriaMicrosipRepository(db), EmailEf, ahora);

        ef.Should().Equal(dapper);
        ef.Should().HaveCount(5).And.Contain(l => l.Contains("Aplicado")).And.Contain(l => l.Contains("Fallido") && l.Contains("error=no se pudo"));
        ef.Should().Contain(l => l.Contains("Pendiente"));
    }
}
