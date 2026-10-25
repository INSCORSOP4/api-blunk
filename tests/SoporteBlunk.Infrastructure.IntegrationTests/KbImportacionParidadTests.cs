using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Conocimiento;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Kb;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// Ejecutan el mismo guion de importaciones con Dapper y con EF Core, cada uno con su propio prefijo de rutas, etiquetas y usuario, y
/// comparan el estado final de la base (documentos, versiones, alias, etiquetas, enlaces y bitácora). Se borra todo al terminar.
/// </summary>
[Collection(ColeccionSoporteKb.Nombre)]
public sealed class KbImportacionParidadTests : IAsyncLifetime
{
    private readonly string _cadena = KbReal.Cadena ?? "";
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private readonly ProcesadorNotas _procesador = new(new ReglasOmisionService([]), new ConocimientoOptions());
    private readonly List<KbDbContext> _contextos = [];

    private string Prefijo(string lado) => $"__par_{lado}_{_id}";
    private string Etiqueta(string lado, string n) => $"zzp{lado}{_id}{n}";
    private string Email(string lado) => $"par-{lado}-{_id}@example.test";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var c in _contextos) await c.DisposeAsync();
        if (string.IsNullOrWhiteSpace(_cadena)) return;
        await KbReal.Ejecutar(_cadena, """
            DELETE FROM kb.Enlace WHERE DocumentoOrigenId IN (SELECT DocumentoId FROM kb.Documento WHERE RutaRelativa LIKE @p)
                                     OR DocumentoDestinoId IN (SELECT DocumentoId FROM kb.Documento WHERE RutaRelativa LIKE @p);
            DELETE FROM kb.Documento WHERE RutaRelativa LIKE @p;
            DELETE FROM kb.Importacion WHERE UsuarioEmail LIKE @e;
            DELETE FROM kb.Etiqueta WHERE Nombre LIKE @t;
            """, ("@p", $"__par_%_{_id}/%"), ("@e", $"par-%-{_id}@example.test"), ("@t", $"zzp%{_id}%"));
    }

    private KbImportacionRepository Ef()
    {
        var db = ContextoDePrueba.Kb(_cadena);
        _contextos.Add(db);
        return new KbImportacionRepository(db);
    }

    private KbImportacionRepositoryDapper Dapper() => new(new KbConnectionFactory(_cadena));

    private NotaProcesada Nota(string prefijo, string ruta, string contenido) =>
        _procesador.ProcesarArchivo(new ArchivoSubido($"{prefijo}/{ruta}", Encoding.UTF8.GetBytes(contenido))).Nota!;

    private static Task<ResultadoImportacion> Importar(IKbImportacionRepository repo, UsuarioActual u, IReadOnlyList<NotaProcesada> notas, string origen = "archivos") =>
        repo.GuardarAsync(new ContextoImportacion(u, origen), notas, new ResumenArchivos(notas.Count + 2, 1, 1), g => $"{{\"guardadas\":{g.Count}}}", default);

    /// <summary>
    /// El guion: altas con enlaces, sin cambios, actualización, enlace roto que se resuelve, nota eliminada que revive, fechas y secretos.
    /// Los enlaces se resuelven por nombre en TODO el vault, así que cada lado usa nombres únicos (token <c>k</c>): si no, el guion de un
    /// lado resolvería enlaces contra las notas que dejó el otro.
    /// </summary>
    private async Task<List<string>> Guion(IKbImportacionRepository repo, string lado)
    {
        var p = Prefijo(lado);
        var k = lado + _id;
        var u = new UsuarioActual(Guid.NewGuid(), "Quien Importa", Email(lado));
        var t1 = Etiqueta(lado, "a"); var t2 = Etiqueta(lado, "b");
        var salida = new List<string>();
        void Anotar(string paso, ResultadoImportacion r) =>
            salida.Add($"{paso}: " + string.Join(", ", r.Notas.OrderBy(n => n.Ruta, StringComparer.Ordinal).Select(n => $"{n.Ruta.Replace(p, "P").Replace(k, "K")}={n.Resultado}/rotos={n.EnlacesRotos}")));

        var nl = "\n";
        var a = $"---{nl}title: Nota A {k}{nl}aliases: [Alfa{k}, alfa{k}, ALFA{k}]{nl}resumen: Resumen de A{nl}status: borrador{nl}---{nl}# A{nl}" +
                $"Ver [[Nota B {k}]], [[Inexistente {k}]], [[Carpeta/Nota C {k}]] y [[Nota C {k}#Seccion|texto visible]] #{t1} #{t1.ToUpperInvariant()}";
        var b = $"# B{nl}contenido de B con [[Alfa{k}]]";
        var c = $"---{nl}aliases: [Charlie{k}]{nl}---{nl}# C{nl}vuelve a [[Nota A {k}]]";
        Anotar("1 altas", await Importar(repo, u, [Nota(p, $"Nota A {k}.md", a), Nota(p, $"Nota B {k}.md", b), Nota(p, $"Carpeta/Nota C {k}.md", c)]));
        Anotar("2 sin cambios", await Importar(repo, u, [Nota(p, $"Nota A {k}.md", a), Nota(p, $"Nota B {k}.md", b), Nota(p, $"Carpeta/Nota C {k}.md", c)]));
        Anotar("3 B cambia", await Importar(repo, u, [Nota(p, $"Nota B {k}.md", b + $"{nl}nuevo #{t2}")]));
        Anotar("4 llega la nota que faltaba", await Importar(repo, u, [Nota(p, $"Inexistente {k}.md", "# Ya existo")]));

        await KbReal.Ejecutar(_cadena, "UPDATE kb.Documento SET Eliminado = 1, EliminadoEn = SYSUTCDATETIME() WHERE RutaRelativa = @r", ("@r", $"{p}/Carpeta/Nota C {k}.md"));
        Anotar("5 la eliminada revive", await Importar(repo, u, [Nota(p, $"Carpeta/Nota C {k}.md", c)]));

        var conFecha = Nota(p, "Fechas.md", "# Fechas") with { ModificadoEnOrigen = new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.FromHours(-6)) };
        var fechaAntigua = Nota(p, "Antigua.md", "# Antigua") with { ModificadoEnOrigen = new DateTimeOffset(1800, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var conSecretos = Nota(p, "Secretos.md", "# Secretos") with { SecretosEnmascarados = 3 };
        Anotar("6 fechas y secretos", await Importar(repo, u, [conFecha, fechaAntigua, conSecretos], "carpeta"));
        return salida;
    }

    /// <summary>Estado de la base tras el guion, sin ids ni horas, para comparar los dos lados.</summary>
    private async Task<List<string>> Estado(string lado)
    {
        var p = Prefijo(lado);
        var k = lado + _id;
        var lineas = new List<string>();
        await using var cn = new SqlConnection(_cadena);
        await cn.OpenAsync();
        var ahora = await ScalarAsync<DateTime>(cn, "SELECT SYSUTCDATETIME()");
        string Reciente(object? v) => v is DateTime d ? (Math.Abs((d - ahora).TotalSeconds) < 300 ? "reciente" : d.ToString("o")) : "-";

        await Leer(cn, "SELECT d.DocumentoId, d.RutaRelativa, d.Titulo, d.Carpeta, d.Resumen, d.Estado, d.Version, d.Eliminado, d.EliminadoEn, d.SecretosEnmascarados, d.ModificadoEnOrigen, " +
                       "CASE WHEN d.HashOrigen = (SELECT TOP 1 v.HashOrigen FROM kb.DocumentoVersion v WHERE v.DocumentoId = d.DocumentoId ORDER BY v.Version DESC) THEN 1 ELSE 0 END AS HashCoincide, d.CreadoEn, d.ActualizadoEn, d.CreadoPorNombre, d.ActualizadoPorNombre, d.ContenidoMd, d.ContenidoTexto FROM kb.Documento d WHERE d.RutaRelativa LIKE @p ORDER BY d.RutaRelativa",
            p + "/%", r => lineas.Add($"DOC {r["RutaRelativa"].ToString()!.Replace(p, "P")} titulo={r["Titulo"]} carpeta={r["Carpeta"]} resumen={r["Resumen"]} estado={r["Estado"]} v={r["Version"]} " +
                $"elim={r["Eliminado"]} elimEn={Reciente(r["EliminadoEn"] is DBNull ? null : r["EliminadoEn"])} secretos={r["SecretosEnmascarados"]} modif={(r["ModificadoEnOrigen"] is DBNull ? "-" : ((DateTime)r["ModificadoEnOrigen"]).ToString("o"))} " +
                $"hashCoincideConHistorial={r["HashCoincide"]} creado={Reciente(r["CreadoEn"])} actualizado={Reciente(r["ActualizadoEn"])} " +
                $"por={r["CreadoPorNombre"]}/{r["ActualizadoPorNombre"]} md={((string)r["ContenidoMd"]).Length} txt={((string)r["ContenidoTexto"]).Length}"));

        await Leer(cn, "SELECT d.RutaRelativa, v.Version, v.UsuarioNombre, v.CreadoEn, CASE WHEN v.ImportacionId IS NULL THEN 0 ELSE 1 END AS ConImportacion " +
                       "FROM kb.DocumentoVersion v JOIN kb.Documento d ON d.DocumentoId = v.DocumentoId WHERE d.RutaRelativa LIKE @p ORDER BY d.RutaRelativa, v.Version",
            p + "/%", r => lineas.Add($"VER {r["RutaRelativa"].ToString()!.Replace(p, "P")} v{r["Version"]} usuario={r["UsuarioNombre"]} creado={Reciente(r["CreadoEn"])} conImportacion={r["ConImportacion"]}"));

        await Leer(cn, "SELECT d.RutaRelativa, a.Alias FROM kb.DocumentoAlias a JOIN kb.Documento d ON d.DocumentoId = a.DocumentoId WHERE d.RutaRelativa LIKE @p ORDER BY d.RutaRelativa, a.Alias",
            p + "/%", r => lineas.Add($"ALIAS {r["RutaRelativa"].ToString()!.Replace(p, "P")} {r["Alias"]}"));

        await Leer(cn, "SELECT d.RutaRelativa, e.Nombre FROM kb.DocumentoEtiqueta de JOIN kb.Documento d ON d.DocumentoId = de.DocumentoId JOIN kb.Etiqueta e ON e.EtiquetaId = de.EtiquetaId WHERE d.RutaRelativa LIKE @p ORDER BY d.RutaRelativa, e.Nombre",
            p + "/%", r => lineas.Add($"ETIQ {r["RutaRelativa"].ToString()!.Replace(p, "P")} {r["Nombre"].ToString()!.Replace(_id, "ID").Replace("zzp" + lado, "zzp?")}"));

        await Leer(cn, "SELECT o.RutaRelativa AS Origen, e.EnlaceId, e.Destino, e.Seccion, e.TextoVisible, t.RutaRelativa AS Destinoruta FROM kb.Enlace e JOIN kb.Documento o ON o.DocumentoId = e.DocumentoOrigenId " +
                       "LEFT JOIN kb.Documento t ON t.DocumentoId = e.DocumentoDestinoId WHERE o.RutaRelativa LIKE @p ORDER BY o.RutaRelativa, e.EnlaceId",
            p + "/%", r => lineas.Add($"ENL {r["Origen"].ToString()!.Replace(p, "P")} -> {r["Destino"]} sec={r["Seccion"]} txt={r["TextoVisible"]} resuelto={(r["Destinoruta"] is DBNull ? "NO" : r["Destinoruta"].ToString()!.Replace(p, "P"))}"));

        await Leer(cn, "SELECT Origen, Estado, IniciadaEn, TerminadaEn, Archivos, Nuevos, Actualizados, SinCambios, Omitidos, ConSecretos, Errores, Informe, UsuarioNombre FROM kb.Importacion WHERE UsuarioEmail = @p ORDER BY ImportacionId",
            Email(lado), r => lineas.Add($"IMP {r["Origen"]} {r["Estado"]} ini={Reciente(r["IniciadaEn"])} fin={Reciente(r["TerminadaEn"] is DBNull ? null : r["TerminadaEn"])} archivos={r["Archivos"]} nuevos={r["Nuevos"]} act={r["Actualizados"]} sin={r["SinCambios"]} " +
                $"omit={r["Omitidos"]} secretos={r["ConSecretos"]} errores={r["Errores"]} informe={r["Informe"]} usuario={r["UsuarioNombre"]}"));
        // Sin distinguir mayúsculas: el token del lado también aparece en mayúsculas dentro de algunos textos (#ZZPD... frente a #ZZPE...).
        return lineas.ConvertAll(l => Regex.Replace(Regex.Replace(l, Regex.Escape(p), "P", RegexOptions.IgnoreCase), Regex.Escape(k), "K", RegexOptions.IgnoreCase));
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection cn, string sql) { await using var cmd = new SqlCommand(sql, cn); return (T)(await cmd.ExecuteScalarAsync())!; }

    private static async Task Leer(SqlConnection cn, string sql, string parametro, Action<SqlDataReader> fila)
    {
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@p", parametro);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) fila(r);
    }

    [SkippableFact]
    public async Task El_mismo_guion_deja_la_base_en_el_mismo_estado_con_Dapper_y_con_EF()
    {
        KbReal.Requerir();

        var dapper = await Guion(Dapper(), "d");
        var ef = await Guion(Ef(), "e");
        var estadoD = await Estado("d");
        var estadoE = await Estado("e");

        ef.Should().Equal(dapper, "lo que devuelve cada paso");
        estadoE.Should().Equal(estadoD, "el estado final de la base");

        // Salvaguardas de que el guion ejerció lo importante (no es una comparación vacía).
        ef.Should().Contain(l => l.StartsWith("1 altas") && l.Contains("Nota A K.md=Nueva/rotos=1"));
        ef.Should().Contain(l => l.StartsWith("2 sin cambios") && !l.Contains("Nueva") && !l.Contains("Actualizada"));
        ef.Should().Contain(l => l.StartsWith("3 B cambia") && l.Contains("Actualizada"));
        ef.Should().Contain(l => l.StartsWith("5 la eliminada revive") && l.Contains("Actualizada"));
        estadoE.Should().Contain(l => l.StartsWith("DOC P/Nota B K.md") && l.Contains("v=2"));
        estadoE.Should().Contain(l => l.StartsWith("DOC P/Carpeta/Nota C K.md") && l.Contains("elim=False") && l.Contains("v=2"));
        estadoE.Should().Contain(l => l.StartsWith("DOC P/Antigua.md") && l.Contains("modif=-"), "una fecha anterior a 1900 se guarda como nula");
        estadoE.Should().Contain(l => l.StartsWith("DOC P/Secretos.md") && l.Contains("secretos=3"));
        estadoE.Should().Contain(l => l.StartsWith("ENL P/Nota A K.md -> Inexistente K") && l.Contains("resuelto=P/Inexistente K.md"), "el enlace roto se resolvió al llegar la nota");
        estadoE.Count(l => l.StartsWith("ALIAS P/Nota A K.md")).Should().Be(1, "Alfa, alfa y ALFA son el mismo alias");
        estadoE.Count(l => l.StartsWith("IMP ")).Should().Be(6);
        estadoE.Where(l => l.StartsWith("DOC ")).Should().OnlyContain(l => l.Contains("hashCoincideConHistorial=1"), "el hash del documento es el de su última versión");
    }

    [SkippableFact]
    public async Task Importaciones_simultaneas_de_la_misma_nota_y_etiqueta_no_se_pisan()
    {
        KbReal.Requerir();

        async Task<List<string>> Carrera(Func<IKbImportacionRepository> crear, string lado)
        {
            var p = Prefijo(lado);
            var etiqueta = Etiqueta(lado, "x");
            // Seis importaciones a la vez de la misma nota nueva y la misma etiqueta nueva, cada una en su propia conexión.
            var tareas = Enumerable.Range(0, 6).Select(async i =>
            {
                var u = new UsuarioActual(Guid.NewGuid(), $"Hilo {i}", Email(lado));
                var r = await Importar(crear(), u, [Nota(p, "Carrera.md", $"# Carrera\nmisma nota #{etiqueta}")]);
                return r.Notas.Single().Resultado.ToString();
            });
            return (await Task.WhenAll(tareas)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        var dapper = await Carrera(Dapper, "d");
        var ef = await Carrera(Ef, "e");

        ef.Should().Equal(dapper);
        ef.Should().Equal("Nueva", "SinCambios", "SinCambios", "SinCambios", "SinCambios", "SinCambios");
        (await Estado("e")).Where(l => l.StartsWith("DOC ")).Should().ContainSingle().Which.Should().Contain("v=1");
        (await Estado("e")).Count(l => l.StartsWith("ETIQ ")).Should().Be(1);
    }

    [SkippableFact]
    public async Task La_hora_de_cierre_y_de_actualizacion_la_pone_el_servidor_SQL()
    {
        var cs = KbReal.Requerir();
        var comandos = new List<string>();
        var opciones = new DbContextOptionsBuilder<KbDbContext>();
        KbDbContext.Configurar(opciones, cs);
        opciones.LogTo(comandos.Add, (id, nivel) => id == Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted, DbContextLoggerOptions.None);
        opciones.EnableSensitiveDataLogging();
        await using var db = new KbDbContext(opciones.Options);
        _contextos.Add(db);
        var repo = new KbImportacionRepository(db);
        var p = Prefijo("e");
        var u = new UsuarioActual(Guid.NewGuid(), "Reloj", Email("e"));

        await Importar(repo, u, [Nota(p, "Reloj.md", "# Reloj")]);
        await Importar(repo, u, [Nota(p, "Reloj.md", "# Reloj\ncambia")]);

        var sql = string.Join("\n", comandos);
        sql.Should().Contain("GETUTCDATE()", "la hora de cierre y de actualización debe salir del servidor SQL, no del reloj de la API");
    }
}
