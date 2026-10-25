using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Conocimiento;
using SoporteBlunk.Application.Features.Conocimiento.Commands.ImportarNotas;

namespace SoporteBlunk.Application.Tests.Conocimiento;

public class LectorZipTests
{
    private static MemoryStream Zip(params (string Nombre, string Contenido)[] entradas)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (nombre, contenido) in entradas)
            {
                var e = z.CreateEntry(nombre);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(contenido);
            }
        ms.Position = 0;
        return ms;
    }

    private static LectorZip.Presupuesto Presupuesto(long bytes = 10_000_000) => new(bytes);

    [Fact]
    public void Lee_las_notas_y_marca_los_otros_archivos_sin_descomprimirlos()
    {
        using var zip = Zip(("SICPRO/Banco.md", "# Banco"), ("SICPRO/", ""), ("img/logo.png", "binario"), ("Raiz.md", "hola"));

        var archivos = LectorZip.Leer(zip, new ConocimientoOptions(), Presupuesto(), "vault.zip");

        archivos.Select(a => a.Ruta).Should().BeEquivalentTo("SICPRO/Banco.md", "img/logo.png", "Raiz.md");
        Encoding.UTF8.GetString(archivos.Single(a => a.Ruta == "SICPRO/Banco.md").Contenido).Should().Be("# Banco");
        archivos.Single(a => a.Ruta == "img/logo.png").Contenido.Should().BeEmpty("lo que no es .md no se descomprime");
    }

    [Fact]
    public void Un_archivo_que_supera_el_limite_se_lee_solo_un_byte_de_mas_para_poder_rechazarlo()
    {
        using var zip = Zip(("grande.md", new string('a', 5000)));

        var a = LectorZip.Leer(zip, new ConocimientoOptions { MaxBytesPorArchivo = 100 }, Presupuesto(), "z.zip").Single();

        a.Contenido.Length.Should().Be(101);
    }

    [Fact]
    public void Una_bomba_de_compresion_se_corta_por_el_presupuesto_total()
    {
        using var zip = Zip(("a.md", new string('x', 800)), ("b.md", new string('x', 800)));

        var act = () => LectorZip.Leer(zip, new ConocimientoOptions(), Presupuesto(1000), "bomba.zip");

        act.Should().Throw<SubidaInvalidaException>().WithMessage("*tamaño total*");
    }

    [Fact]
    public void Demasiadas_entradas_se_rechazan_sin_descomprimir()
    {
        using var zip = Zip(Enumerable.Range(0, 11).Select(i => ($"n{i}.md", "x")).ToArray());

        var act = () => LectorZip.Leer(zip, new ConocimientoOptions { MaxEntradasZip = 10 }, Presupuesto(), "muchos.zip");

        act.Should().Throw<SubidaInvalidaException>().WithMessage("*más de 10 entradas*");
    }

    [Fact]
    public void Un_zip_corrupto_o_que_no_es_zip_da_un_error_claro()
    {
        using var basura = new MemoryStream(Encoding.UTF8.GetBytes("esto no es un zip, solo texto"));

        var act = () => LectorZip.Leer(basura, new ConocimientoOptions(), Presupuesto(), "falso.zip");

        act.Should().Throw<SubidaInvalidaException>().WithMessage("*no es un .zip válido*");
    }

    [Fact]
    public void Los_nombres_peligrosos_pasan_tal_cual_y_los_rechaza_el_procesador()
    {
        using var zip = Zip(("../../fuera.md", "x"));

        var a = LectorZip.Leer(zip, new ConocimientoOptions(), Presupuesto(), "z.zip").Single();

        var r = new ProcesadorNotas(new ReglasOmisionService([]), new ConocimientoOptions()).ProcesarArchivo(a);
        r.Estado.Should().Be(EstadoArchivo.Error);
    }
}

public class ImportarNotasHandlerTests
{
    private static readonly UsuarioActual Usuario = new(Guid.NewGuid(), "Ana Pérez", "ana@prosur.com.mx");

    private readonly Mock<IKbImportacionRepository> _repo = new();
    private readonly Mock<IUsuarioActualProvider> _usuario = new();

    public ImportarNotasHandlerTests()
    {
        _usuario.Setup(u => u.Obtener()).Returns(Usuario);
        _repo.Setup(r => r.ReglasOmisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReglaOmision(ReglaOmision.Carpeta, ".obsidian")]);
    }

    private ImportarNotasHandler Crear() => new(_repo.Object, _usuario.Object, Options.Create(new ConocimientoOptions()));

    private static ArchivoSubido A(string ruta, string contenido) => new(ruta, Encoding.UTF8.GetBytes(contenido));

    [Fact]
    public async Task Si_nada_se_puede_importar_no_se_toca_la_base_de_datos()
    {
        var informe = await Crear().Handle(new ImportarNotasCommand([A(".obsidian/x.md", "a"), A("foto.png", "b"), A("../mal.md", "c")], "archivos"), default);

        informe.ImportacionId.Should().BeNull();
        informe.Total.Should().Be(3);
        informe.Omitidas.Should().Be(2);
        informe.Errores.Should().Be(1);
        _repo.Verify(r => r.GuardarAsync(It.IsAny<ContextoImportacion>(), It.IsAny<IReadOnlyList<NotaProcesada>>(),
            It.IsAny<ResumenArchivos>(), It.IsAny<Func<IReadOnlyList<NotaGuardada>, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Guarda_solo_notas_ya_enmascaradas_a_nombre_del_usuario_y_arma_el_informe()
    {
        IReadOnlyList<NotaProcesada>? recibidas = null;
        ContextoImportacion? contexto = null;
        _repo.Setup(r => r.GuardarAsync(It.IsAny<ContextoImportacion>(), It.IsAny<IReadOnlyList<NotaProcesada>>(),
                It.IsAny<ResumenArchivos>(), It.IsAny<Func<IReadOnlyList<NotaGuardada>, string>>(), It.IsAny<CancellationToken>()))
            .Callback<ContextoImportacion, IReadOnlyList<NotaProcesada>, ResumenArchivos, Func<IReadOnlyList<NotaGuardada>, string>, CancellationToken>(
                (c, n, _, _, _) => { contexto = c; recibidas = n; })
            .ReturnsAsync((ContextoImportacion _, IReadOnlyList<NotaProcesada> n, ResumenArchivos _, Func<IReadOnlyList<NotaGuardada>, string> _, CancellationToken _) =>
                new ResultadoImportacion(42, n.Select((x, i) => new NotaGuardada(x.RutaRelativa, i == 0 ? ResultadoNota.Nueva : ResultadoNota.SinCambios, i == 0 ? 2 : 0)).ToList()));

        var informe = await Crear().Handle(new ImportarNotasCommand(
            [A("a.md", "# Uno\npassword: Zx9Qm2pLk8"), A("b.md", "# Dos"), A("c.txt", "x")], "carpeta"), default);

        contexto!.Usuario.Should().Be(Usuario);
        contexto.Origen.Should().Be("carpeta");
        recibidas!.Should().HaveCount(2);
        string.Join(' ', recibidas!.Select(n => n.ContenidoMd + n.ContenidoTexto)).Should().NotContain("Zx9Qm2pLk8");

        informe.ImportacionId.Should().Be(42);
        (informe.Nuevas, informe.SinCambios, informe.Omitidas, informe.Errores).Should().Be((1, 1, 1, 0));
        informe.ConSecretos.Should().Be(1);
        informe.SecretosEnmascarados.Should().Be(1);
        informe.EnlacesRotos.Should().Be(2);
        informe.Archivos.Single(a => a.Ruta == "a.md").LineasConSecretos.Should().Equal(2);
    }

    [Fact]
    public async Task El_informe_no_contiene_contenido_ni_valores_secretos()
    {
        Func<IReadOnlyList<NotaGuardada>, string>? construir = null;
        _repo.Setup(r => r.GuardarAsync(It.IsAny<ContextoImportacion>(), It.IsAny<IReadOnlyList<NotaProcesada>>(),
                It.IsAny<ResumenArchivos>(), It.IsAny<Func<IReadOnlyList<NotaGuardada>, string>>(), It.IsAny<CancellationToken>()))
            .Callback<ContextoImportacion, IReadOnlyList<NotaProcesada>, ResumenArchivos, Func<IReadOnlyList<NotaGuardada>, string>, CancellationToken>(
                (_, _, _, f, _) => construir = f)
            .ReturnsAsync(new ResultadoImportacion(1, [new NotaGuardada("a.md", ResultadoNota.Nueva, 0)]));

        var informe = await Crear().Handle(new ImportarNotasCommand([A("a.md", "texto privado\npassword: Zx9Qm2pLk8")], "archivos"), default);
        var json = construir!([new NotaGuardada("a.md", ResultadoNota.Nueva, 0)]);

        json.Should().Contain("\"estado\":\"Nueva\"").And.Contain("\"lineasConSecretos\":[2]");
        json.Should().NotContain("Zx9Qm2pLk8").And.NotContain("texto privado");
        System.Text.Json.JsonSerializer.Serialize(informe).Should().NotContain("Zx9Qm2pLk8");
    }

    [Fact]
    public void El_validador_exige_archivos_y_un_origen_conocido()
    {
        var v = new ImportarNotasValidator();
        v.Validate(new ImportarNotasCommand([], "archivos")).IsValid.Should().BeFalse();
        v.Validate(new ImportarNotasCommand([A("a.md", "x")], "magia")).IsValid.Should().BeFalse();
        v.Validate(new ImportarNotasCommand([A("a.md", "x")], "zip")).IsValid.Should().BeTrue();
    }
}
