using System.Text;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Conocimiento;
using SoporteBlunk.Infrastructure.Persistence.Kb;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// Pruebas contra SoporteKB real (servidor de pruebas). Cada prueba usa rutas, etiquetas y un correo únicos
/// y borra todo lo que creó al terminar. Se omiten si no hay ConnectionStrings__SoporteKB.
/// </summary>
[Collection(ColeccionSoporteKb.Nombre)]
public sealed class KbImportacionRepositoryTests : IAsyncLifetime
{
    private readonly string _cadena = Environment.GetEnvironmentVariable("ConnectionStrings__SoporteKB") ?? "";
    private readonly string _id = Guid.NewGuid().ToString("N")[..10];
    private readonly UsuarioActual _usuario;
    private readonly ProcesadorNotas _procesador = new(new ReglasOmisionService([]), new ConocimientoOptions());

    public KbImportacionRepositoryTests() =>
        _usuario = new UsuarioActual(Guid.NewGuid(), "Usuario de Prueba", $"prueba+{_id}@example.test");

    private string Prefijo => $"__test_{_id}";
    private string Etiqueta => $"zz{_id}";

    private KbImportacionRepository Repo() => new(new KbConnectionFactory(_cadena));

    private IReadOnlyList<NotaProcesada> Notas(params (string Ruta, string Contenido)[] notas) =>
        notas.Select(n => _procesador.ProcesarArchivo(new ArchivoSubido($"{Prefijo}/{n.Ruta}", Encoding.UTF8.GetBytes(n.Contenido))).Nota!).ToList();

    private async Task<ResultadoImportacion> Importar(IReadOnlyList<NotaProcesada> notas) =>
        await Repo().GuardarAsync(new ContextoImportacion(_usuario, "archivos"), notas, new ResumenArchivos(notas.Count, 0, 0),
            g => $"{{\"guardadas\":{g.Count}}}", default);

    private async Task<SqlConnection> Abrir() => await new KbConnectionFactory(_cadena).OpenAsync(default);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_cadena)) return;
        await using var cn = await Abrir();
        await cn.ExecuteAsync("""
            DELETE FROM kb.Enlace WHERE DocumentoOrigenId IN (SELECT DocumentoId FROM kb.Documento WHERE RutaRelativa LIKE @p)
                                     OR DocumentoDestinoId IN (SELECT DocumentoId FROM kb.Documento WHERE RutaRelativa LIKE @p);
            DELETE FROM kb.Documento WHERE RutaRelativa LIKE @p;
            DELETE FROM kb.Importacion WHERE UsuarioEmail = @email;
            DELETE FROM kb.Etiqueta WHERE Nombre LIKE @tag;
            """, new { p = Prefijo + "/%", email = _usuario.Email, tag = Etiqueta + "%" });
    }

    private const string NotaA = "---\ntitle: Nota A\naliases: [Alfa]\n---\n# A\nVer [[Nota B]] y [[Nota Inexistente]] #ETIQ";
    private const string NotaB = "# B\ncontenido de B";

    [SkippableFact]
    public async Task Alta_sin_cambios_actualizacion_e_historial()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        var a = NotaA.Replace("#ETIQ", "#" + Etiqueta);

        // 1) Alta
        var r1 = await Importar(Notas(("Nota A.md", a), ("Nota B.md", NotaB)));
        r1.Notas.Select(n => n.Resultado).Should().OnlyContain(x => x == ResultadoNota.Nueva);
        r1.Notas.Single(n => n.Ruta.EndsWith("Nota A.md")).EnlacesRotos.Should().Be(1, "'Nota Inexistente' no existe");

        await using var cn = await Abrir();
        var enlaces = (await cn.QueryAsync<(string Destino, int? DestinoId)>(
            "SELECT e.Destino, e.DocumentoDestinoId FROM kb.Enlace e JOIN kb.Documento d ON d.DocumentoId = e.DocumentoOrigenId WHERE d.RutaRelativa = @r",
            new { r = $"{Prefijo}/Nota A.md" })).ToDictionary(x => x.Destino, x => x.DestinoId);
        enlaces["Nota B"].Should().NotBeNull("el enlace a una nota de la misma subida se resuelve");
        enlaces["Nota Inexistente"].Should().BeNull();

        (await cn.QuerySingleAsync<int>("SELECT COUNT(*) FROM kb.DocumentoAlias a JOIN kb.Documento d ON d.DocumentoId=a.DocumentoId WHERE d.RutaRelativa=@r AND a.Alias=N'Alfa'",
            new { r = $"{Prefijo}/Nota A.md" })).Should().Be(1);
        (await cn.QuerySingleAsync<int>("SELECT COUNT(*) FROM kb.DocumentoEtiqueta de JOIN kb.Documento d ON d.DocumentoId=de.DocumentoId JOIN kb.Etiqueta e ON e.EtiquetaId=de.EtiquetaId WHERE d.RutaRelativa=@r AND e.Nombre=@t",
            new { r = $"{Prefijo}/Nota A.md", t = Etiqueta })).Should().Be(1);

        // 2) Misma subida otra vez: nada cambia, no se crean versiones nuevas
        var r2 = await Importar(Notas(("Nota A.md", a), ("Nota B.md", NotaB)));
        r2.Notas.Should().OnlyContain(n => n.Resultado == ResultadoNota.SinCambios);
        (await cn.QuerySingleAsync<int>("SELECT COUNT(*) FROM kb.DocumentoVersion v JOIN kb.Documento d ON d.DocumentoId=v.DocumentoId WHERE d.RutaRelativa LIKE @p",
            new { p = Prefijo + "/%" })).Should().Be(2);

        // 3) B cambia: se actualiza, sube la versión y queda el historial
        var r3 = await Importar(Notas(("Nota B.md", NotaB + "\nnuevo párrafo")));
        r3.Notas.Single().Resultado.Should().Be(ResultadoNota.Actualizada);
        var b = await cn.QuerySingleAsync<(int Version, string ContenidoTexto)>(
            "SELECT Version, ContenidoTexto FROM kb.Documento WHERE RutaRelativa = @r", new { r = $"{Prefijo}/Nota B.md" });
        b.Version.Should().Be(2);
        b.ContenidoTexto.Should().Contain("nuevo párrafo");
        (await cn.QuerySingleAsync<int>("SELECT COUNT(*) FROM kb.DocumentoVersion v JOIN kb.Documento d ON d.DocumentoId=v.DocumentoId WHERE d.RutaRelativa=@r",
            new { r = $"{Prefijo}/Nota B.md" })).Should().Be(2);
    }

    [SkippableFact]
    public async Task Un_enlace_roto_se_resuelve_cuando_despues_se_sube_la_nota_destino_aunque_el_origen_no_cambie()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        var a = NotaA.Replace("#ETIQ", "");
        await Importar(Notas(("Nota A.md", a), ("Nota B.md", NotaB)));

        var r = await Importar(Notas(("Nota Inexistente.md", "# Ya existo")));

        r.Notas.Single().Resultado.Should().Be(ResultadoNota.Nueva);
        await using var cn = await Abrir();
        var sinResolver = await cn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM kb.Enlace e JOIN kb.Documento d ON d.DocumentoId=e.DocumentoOrigenId WHERE d.RutaRelativa=@r AND e.DocumentoDestinoId IS NULL",
            new { r = $"{Prefijo}/Nota A.md" });
        sinResolver.Should().Be(0);
    }

    [SkippableFact]
    public async Task Queda_la_bitacora_de_quien_importo_y_el_informe_es_json_valido()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        var r = await Importar(Notas(("Nota B.md", NotaB)));

        var detalle = await Repo().ObtenerAsync(r.ImportacionId, default);

        detalle.Should().NotBeNull();
        detalle!.Resumen.UsuarioEmail.Should().Be(_usuario.Email);
        detalle.Resumen.UsuarioNombre.Should().Be(_usuario.Nombre);
        (detalle.Resumen.Estado, detalle.Resumen.Nuevos, detalle.Resumen.Archivos).Should().Be(("Completada", 1, 1));
        detalle.Resumen.TerminadaEn.Should().NotBeNull();
        detalle.InformeJson.Should().Be("{\"guardadas\":1}");

        (await Repo().ListarAsync(200, default)).Should().Contain(x => x.ImportacionId == r.ImportacionId);
        (await Repo().ObtenerAsync(int.MaxValue, default)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Si_algo_falla_no_se_guarda_ninguna_nota_y_la_importacion_queda_como_fallida()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        var buena = Notas(("Buena.md", "# buena"))[0];
        var mala = buena with { RutaRelativa = $"{Prefijo}/Mala.md", Titulo = new string('T', 400) }; // excede nvarchar(300) -> error de SQL

        var act = () => Importar([buena, mala]);

        await act.Should().ThrowAsync<KbNoDisponibleException>();
        await using var cn = await Abrir();
        (await cn.QuerySingleAsync<int>("SELECT COUNT(*) FROM kb.Documento WHERE RutaRelativa LIKE @p", new { p = Prefijo + "/%" }))
            .Should().Be(0, "la transacción se revirtió completa, incluida la nota buena");
        (await cn.QuerySingleAsync<string>("SELECT TOP 1 Estado FROM kb.Importacion WHERE UsuarioEmail = @e ORDER BY ImportacionId DESC", new { e = _usuario.Email }))
            .Should().Be("Fallida");
    }

    [SkippableFact]
    public async Task Las_reglas_de_omision_vienen_de_la_base()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");

        var reglas = await Repo().ReglasOmisionAsync(default);

        reglas.Should().Contain(r => r.Tipo == ReglaOmision.Carpeta && r.Patron == ".obsidian");
    }
}
