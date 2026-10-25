using System.Text;
using Dapper;
using FluentAssertions;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Conocimiento;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Kb;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

public class KbBusquedaUtilidadesTests
{
    [Fact]
    public void Las_palabras_se_limpian_sin_repetir_y_con_tope()
    {
        KbReadRepository.Palabras("  Docker   docker\tRESPALDO  ").Should().Equal("Docker", "RESPALDO");
        KbReadRepository.Palabras("a b c d e f g h").Should().HaveCount(6);
        KbReadRepository.Palabras(new string('x', 200)).Single().Length.Should().Be(50);
        KbReadRepository.Palabras(null).Should().BeEmpty();
        KbReadRepository.Palabras("   ").Should().BeEmpty();
    }

    [Theory]
    [InlineData("100%", @"100\%")]
    [InlineData("con_guion", @"con\_guion")]
    [InlineData("[a]", @"\[a]")]
    [InlineData(@"a\b", @"a\\b")]
    public void Escapa_los_comodines_de_like(string entrada, string esperado) =>
        KbReadRepository.EscaparLike(entrada).Should().Be(esperado);
}

/// <summary>Búsqueda y lectura contra SoporteKB real (servidor de pruebas). Cada prueba crea sus notas y las borra al terminar.</summary>
[Collection(ColeccionSoporteKb.Nombre)]
public sealed class KbReadRepositoryTests : IAsyncLifetime
{
    private readonly string _cadena = Environment.GetEnvironmentVariable("ConnectionStrings__SoporteKB") ?? "";
    private readonly string _id = Guid.NewGuid().ToString("N")[..10];
    private readonly UsuarioActual _usuario;
    private readonly ProcesadorNotas _procesador = new(new ReglasOmisionService([]), new ConocimientoOptions());

    public KbReadRepositoryTests() => _usuario = new UsuarioActual(Guid.NewGuid(), "Usuario de Prueba", $"lectura+{_id}@example.test");

    private string P => $"__test_{_id}";
    private string Tag => $"zz{_id}";
    private readonly List<SoporteBlunk.Infrastructure.Persistence.Contexts.KbDbContext> _contextos = [];

    private KbReadRepository Lectura()
    {
        var db = ContextoDePrueba.Kb(_cadena);
        _contextos.Add(db);
        return new KbReadRepository(db);
    }

    private async Task Sembrar()
    {
        string[] archivos =
        [
            $"{P}/Docker/Respaldo.md|---\ntitle: Importar respaldo\n---\n# Importar respaldo\nLa Contraseña del usuario sa va en secreto. Restaurar el BAK en SQL Server. #{Tag}",
            $"{P}/Docker/Imagenes.md|# Imágenes de Docker\nSe usan con [[Respaldo]] para volver a levantar todo.",
            $"{P}/SICPRO/Banco.md|# Catálogo de bancos\nEl 100% de los bancos usa con_guion_bajo en su clave.",
            $"{P}/Raiz.md|---\naliases: [Inicio]\n---\nPágina de inicio del vault.",
        ];
        var notas = archivos.Select(a => a.Split('|', 2)).Select(a =>
            _procesador.ProcesarArchivo(new ArchivoSubido(a[0], Encoding.UTF8.GetBytes(a[1]))).Nota!).ToList();

        var dbImportacion = ContextoDePrueba.Kb(_cadena);
        _contextos.Add(dbImportacion);
        await new KbImportacionRepository(dbImportacion).GuardarAsync(
            new ContextoImportacion(_usuario, "archivos"), notas, new ResumenArchivos(notas.Count, 0, 0), _ => "{}", default);
    }

    private async Task<PagedResult<DocumentoResumen>> Buscar(string? texto = null, string? carpeta = null, string? etiqueta = null,
        OrdenDocumentos orden = OrdenDocumentos.Recientes, int page = 1, int pageSize = 20) =>
        await Lectura().ListarDocumentosAsync(
            new DocumentoFiltro { Buscar = texto, Carpeta = carpeta ?? P, Etiqueta = etiqueta, Orden = orden }, page, pageSize, default);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var c in _contextos) await c.DisposeAsync();
        if (string.IsNullOrWhiteSpace(_cadena)) return;
        await using var cn = await new KbConnectionFactory(_cadena).OpenAsync(default);
        await cn.ExecuteAsync("""
            DELETE FROM kb.Enlace WHERE DocumentoOrigenId IN (SELECT DocumentoId FROM kb.Documento WHERE RutaRelativa LIKE @p)
                                     OR DocumentoDestinoId IN (SELECT DocumentoId FROM kb.Documento WHERE RutaRelativa LIKE @p);
            DELETE FROM kb.Documento WHERE RutaRelativa LIKE @p;
            DELETE FROM kb.Importacion WHERE UsuarioEmail = @email;
            DELETE FROM kb.Etiqueta WHERE Nombre LIKE @tag;
            """, new { p = P + "/%", email = _usuario.Email, tag = Tag + "%" });
    }

    [SkippableFact]
    public async Task La_busqueda_ignora_acentos_y_mayusculas_y_devuelve_un_fragmento()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();

        var r = await Buscar("CONTRASENA");

        r.Items.Should().ContainSingle().Which.Titulo.Should().Be("Importar respaldo");
        r.Items[0].Fragmento.Should().Contain("Contraseña", "el fragmento sale del contenido con el texto original");
        r.Items[0].Etiquetas.Should().Contain(Tag);
    }

    [SkippableFact]
    public async Task Varias_palabras_se_combinan_con_AND_y_el_titulo_va_primero()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();

        (await Buscar("respaldo")).Items.Select(i => i.Titulo)
            .Should().Equal("Importar respaldo", "Imágenes de Docker");                         // coincide en título primero, luego en contenido
        (await Buscar("respaldo docker")).Items.Should().ContainSingle().Which.Titulo.Should().Be("Imágenes de Docker");
        (await Buscar("respaldo xyzinexistente")).TotalCount.Should().Be(0);
    }

    [SkippableFact]
    public async Task Los_comodines_de_like_se_buscan_de_forma_literal()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();

        (await Buscar("100%")).Items.Should().ContainSingle().Which.Titulo.Should().Be("Catálogo de bancos");
        (await Buscar("%")).Items.Should().ContainSingle("'%' no es 'cualquier cosa'");
        (await Buscar("con_guion")).Items.Should().ContainSingle();
        (await Buscar("con?guion")).TotalCount.Should().Be(0);
        (await Buscar("con_guioX")).TotalCount.Should().Be(0);
    }

    [SkippableFact]
    public async Task Busca_tambien_por_alias_y_por_etiqueta()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();

        (await Buscar("inicio")).Items.Should().Contain(i => i.Ruta.EndsWith("Raiz.md"));
        (await Buscar(Tag)).Items.Should().ContainSingle().Which.Titulo.Should().Be("Importar respaldo");
    }

    [SkippableFact]
    public async Task Filtra_por_carpeta_con_subcarpetas_y_por_etiqueta()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();

        (await Buscar(carpeta: P)).TotalCount.Should().Be(4, "incluye las subcarpetas");
        (await Buscar(carpeta: $"{P}/Docker")).TotalCount.Should().Be(2);
        (await Buscar(carpeta: $"{P}/Doc")).TotalCount.Should().Be(0, "'Doc' no es una carpeta ni un prefijo de nombre de carpeta");
        (await Buscar(etiqueta: Tag)).Items.Should().ContainSingle();
        (await Buscar(etiqueta: "#" + Tag)).Items.Should().ContainSingle("acepta la etiqueta con #");
    }

    [SkippableFact]
    public async Task Pagina_y_ordena_por_titulo()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();

        var p1 = await Buscar(orden: OrdenDocumentos.Titulo, page: 1, pageSize: 3);
        var p2 = await Buscar(orden: OrdenDocumentos.Titulo, page: 2, pageSize: 3);

        p1.TotalCount.Should().Be(4);
        p1.Items.Should().HaveCount(3).And.BeInAscendingOrder(i => i.Titulo, StringComparer.CurrentCultureIgnoreCase);
        p2.Items.Should().ContainSingle();
        p1.Items.Select(i => i.Id).Intersect(p2.Items.Select(i => i.Id)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task El_detalle_trae_enlaces_quien_enlaza_y_el_historial()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();
        var lista = (await Buscar()).Items;
        var respaldo = lista.Single(i => i.Ruta.EndsWith("Respaldo.md"));
        var imagenes = lista.Single(i => i.Ruta.EndsWith("Imagenes.md"));

        var d = (await Lectura().ObtenerDocumentoAsync(respaldo.Id, default))!;
        d.EnlazadoDesde.Should().ContainSingle().Which.Id.Should().Be(imagenes.Id);
        d.Versiones.Should().ContainSingle().Which.Usuario.Should().Be(_usuario.Nombre);
        d.ContenidoMd.Should().Contain("Restaurar el BAK");
        d.Alias.Should().BeEmpty();

        var origen = (await Lectura().ObtenerDocumentoAsync(imagenes.Id, default))!;
        origen.Enlaces.Should().ContainSingle();
        origen.Enlaces[0].Destino.Should().Be("Respaldo");
        origen.Enlaces[0].Resuelto!.Id.Should().Be(respaldo.Id);
        origen.Enlaces[0].Resuelto!.Titulo.Should().Be("Importar respaldo");
    }

    [SkippableFact]
    public async Task Una_nota_eliminada_o_inexistente_no_aparece_ni_se_puede_abrir()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();
        var banco = (await Buscar("bancos")).Items.Single();

        await using (var cn = await new KbConnectionFactory(_cadena).OpenAsync(default))
            await cn.ExecuteAsync("UPDATE kb.Documento SET Eliminado = 1, EliminadoEn = SYSUTCDATETIME() WHERE DocumentoId = @id", new { id = banco.Id });

        (await Buscar("bancos")).TotalCount.Should().Be(0);
        (await Lectura().ObtenerDocumentoAsync(banco.Id, default)).Should().BeNull();
        (await Lectura().ObtenerDocumentoAsync(int.MaxValue, default)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Carpetas_y_etiquetas_traen_sus_conteos()
    {
        Skip.If(string.IsNullOrWhiteSpace(_cadena), "Falta ConnectionStrings__SoporteKB");
        await Sembrar();

        var carpetas = (await Lectura().CarpetasAsync(default)).Where(c => c.Carpeta.StartsWith(P)).ToList();
        carpetas.Should().Contain(new CarpetaConteo($"{P}/Docker", 2)).And.Contain(new CarpetaConteo($"{P}/SICPRO", 1)).And.Contain(new CarpetaConteo(P, 1));

        (await Lectura().EtiquetasAsync(default)).Should().Contain(new EtiquetaConteo(Tag, 1));
    }
}
