using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Conocimiento;

namespace SoporteBlunk.Application.Tests.Conocimiento;

public class ProcesadorNotasTests
{
    private static ProcesadorNotas Crear(ConocimientoOptions? opciones = null) => new(
        new ReglasOmisionService(
        [
            new ReglaOmision(ReglaOmision.Carpeta, ".obsidian"),
            new ReglaOmision(ReglaOmision.Carpeta, "Cambios de contraseña chemicloud"),
        ]),
        opciones ?? new ConocimientoOptions());

    private static ArchivoSubido Archivo(string ruta, string contenido, bool conBom = false, DateTimeOffset? fecha = null)
    {
        var bytes = new UTF8Encoding(conBom).GetBytes(contenido);
        return new ArchivoSubido(ruta, bytes, fecha);
    }

    private const string Nota = """
        ---
        title: Módulo Banco — SICPRO
        tags: [sicpro, api]
        aliases:
          - Bancos
        status: vigente
        ---
        # Título del documento

        Este módulo administra los **bancos** del sistema y se conecta con [[Módulo Empresa]].

        Ver también [[Docker#Respaldos|restaurar]] y #cero-papel.

        ## Conexión

        ```
        Server=db01;Database=SICPRO;User Id=sa;Password=Qw3rty!2024;Encrypt=True
        ```

        La contraseña: Prosur2026

        | Campo | Valor |
        |-------|-------|
        | token | a1b2c3d4e5f6 |

        %% nota privada que no debe publicarse %%
        """;

    [Fact]
    public void Procesa_una_nota_completa_con_metadatos_enlaces_y_sin_secretos()
    {
        var fecha = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var original = Archivo("SICPRO/Módulo Banco.md", Nota.Replace("\n", "\r\n"), conBom: true, fecha);

        var r = Crear().ProcesarArchivo(original);

        r.Estado.Should().Be(EstadoArchivo.Procesado);
        var n = r.Nota!;
        n.RutaRelativa.Should().Be("SICPRO/Módulo Banco.md");
        n.Carpeta.Should().Be("SICPRO");
        n.Titulo.Should().Be("Módulo Banco — SICPRO", "el frontmatter manda sobre el h1");
        n.Estado.Should().Be("vigente");
        n.Alias.Should().Equal("Bancos");
        n.Etiquetas.Should().BeEquivalentTo("sicpro", "api", "cero-papel");
        n.Enlaces.Should().Equal(
            new EnlaceNota("Módulo Empresa", null, null),
            new EnlaceNota("Docker", "Respaldos", "restaurar"));
        n.Resumen.Should().Be("Este módulo administra los bancos del sistema y se conecta con Módulo Empresa.");
        n.ModificadoEnOrigen.Should().Be(fecha);
        n.ContenidoMd.Should().StartWith("# Título del documento", "el frontmatter no forma parte del contenido");
        n.ContenidoMd.Should().NotContain("\r").And.NotContain("---\ntitle");
        n.HashOrigen.Should().Equal(SHA256.HashData(original.Contenido), "el hash es del archivo original, no del procesado");
    }

    [Fact]
    public void Ningun_secreto_ni_comentario_privado_sobrevive_en_ninguna_parte_del_resultado()
    {
        var n = Crear().ProcesarArchivo(Archivo("SICPRO/x.md", Nota)).Nota!;

        var todo = string.Join('\n', n.ContenidoMd, n.ContenidoTexto, n.Resumen, n.Titulo, string.Join(' ', n.Etiquetas), string.Join(' ', n.Alias));
        foreach (var secreto in new[] { "Qw3rty!2024", "Prosur2026", "a1b2c3d4e5f6", "nota privada" })
            todo.Should().NotContain(secreto, $"'{secreto}' no debe guardarse");

        n.SecretosEnmascarados.Should().Be(3);
        n.LineasConSecretos.Should().HaveCount(3).And.BeInAscendingOrder();
        n.ContenidoMd.Should().Contain(EnmascaradorSecretos.Marca);
    }

    [Fact]
    public void El_texto_plano_sirve_para_buscar_y_conserva_el_codigo_no_secreto()
    {
        var n = Crear().ProcesarArchivo(Archivo("a.md", Nota)).Nota!;

        n.ContenidoTexto.Should().Contain("administra los bancos").And.Contain("Server=db01;Database=SICPRO");
        n.ContenidoTexto.Should().NotContain("**").And.NotContain("[[").And.NotContain("```");
    }

    [Fact]
    public void Sin_titulo_en_frontmatter_ni_h1_usa_el_nombre_del_archivo()
    {
        var n = Crear().ProcesarArchivo(Archivo("DOCKER/Importar respaldo .BAK.md", "solo texto")).Nota!;
        n.Titulo.Should().Be("Importar respaldo .BAK");
    }

    [Theory]
    [InlineData(".obsidian/workspace.md")]
    [InlineData("Cambios de contraseña chemicloud/nota.md")]
    [InlineData("imagen.png")]
    [InlineData("config.json")]
    public void Omite_carpetas_reglas_y_archivos_que_no_son_markdown(string ruta)
    {
        var r = Crear().ProcesarArchivo(Archivo(ruta, "contenido"));

        r.Estado.Should().Be(EstadoArchivo.Omitido);
        r.Nota.Should().BeNull();
        r.Motivo.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Un_archivo_vacio_se_omite()
    {
        Crear().ProcesarArchivo(new ArchivoSubido("vacio.md", [])).Estado.Should().Be(EstadoArchivo.Omitido);
    }

    [Theory]
    [InlineData("../fuera.md")]
    [InlineData("/etc/x.md")]
    [InlineData("C:\\x\\nota.md")]
    [InlineData("a\0b.md")]
    public void Rechaza_rutas_peligrosas_como_error(string ruta)
    {
        var r = Crear().ProcesarArchivo(Archivo(ruta, "x"));

        r.Estado.Should().Be(EstadoArchivo.Error);
        r.Nota.Should().BeNull();
        r.Ruta.Should().NotContain("\0");
    }

    [Fact]
    public void Un_archivo_que_no_es_utf8_es_un_error_y_no_se_guarda_corrupto()
    {
        var r = Crear().ProcesarArchivo(new ArchivoSubido("latin1.md", [0x43, 0x61, 0x66, 0xE9, 0xFF, 0xFE])); // 0xFF no es UTF-8 válido

        r.Estado.Should().Be(EstadoArchivo.Error);
        r.Motivo.Should().Contain("UTF-8");
    }

    [Fact]
    public void Un_archivo_mas_grande_que_el_limite_es_un_error()
    {
        var r = Crear(new ConocimientoOptions { MaxBytesPorArchivo = 100 }).ProcesarArchivo(Archivo("grande.md", new string('a', 101)));

        r.Estado.Should().Be(EstadoArchivo.Error);
        r.Motivo.Should().Contain("máximo");
    }

    [Fact]
    public void El_lote_aisla_los_errores_cuenta_todo_y_detecta_duplicados_y_exceso()
    {
        var informe = Crear(new ConocimientoOptions { MaxArchivos = 5 }).ProcesarLote(
        [
            Archivo("a.md", "uno"),
            Archivo("A.MD", "uno repetido con otras mayúsculas"),     // duplicado (la base no distingue mayúsculas)
            Archivo("../mal.md", "x"),                                 // error
            Archivo("nota.png", "x"),                                  // omitido
            Archivo("c.md", "password: Zx9Qm2pLk"),                    // con secreto
            Archivo("d.md", "se pasa del máximo de archivos"),         // 6.º archivo: excede el máximo de 5
        ]);

        informe.Total.Should().Be(6);
        informe.Procesados.Should().Be(2);
        informe.Omitidos.Should().Be(1);
        informe.Errores.Should().Be(3);
        informe.ConSecretos.Should().Be(1);
        informe.SecretosEnmascarados.Should().Be(1);
        informe.Archivos.Single(a => a.Ruta == "A.MD").Motivo.Should().Contain("duplicada");
        informe.Archivos.Last().Motivo.Should().Contain("máximo");
        (informe.Procesados + informe.Omitidos + informe.Errores).Should().Be(informe.Total);
    }

    [Fact]
    public void El_resultado_es_determinista_para_el_mismo_archivo()
    {
        var a = Crear().ProcesarArchivo(Archivo("x.md", Nota)).Nota!;
        var b = Crear().ProcesarArchivo(Archivo("x.md", Nota)).Nota!;

        b.HashOrigen.Should().Equal(a.HashOrigen);
        b.ContenidoMd.Should().Be(a.ContenidoMd);
        b.ContenidoTexto.Should().Be(a.ContenidoTexto);
    }

    [Fact]
    public void El_titulo_y_los_campos_respetan_los_limites_de_la_base()
    {
        var n = Crear().ProcesarArchivo(Archivo("x.md", $"---\ntitle: {new string('T', 500)}\nstatus: {new string('E', 200)}\n---\ncuerpo")).Nota!;

        n.Titulo.Length.Should().BeLessThanOrEqualTo(300);
        n.Estado!.Length.Should().BeLessThanOrEqualTo(50);
    }
}
