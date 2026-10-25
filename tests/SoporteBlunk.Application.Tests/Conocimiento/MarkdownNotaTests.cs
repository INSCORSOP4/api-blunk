using FluentAssertions;
using SoporteBlunk.Application.Conocimiento;

namespace SoporteBlunk.Application.Tests.Conocimiento;

public class MarkdownNotaTests
{
    private static IReadOnlyList<MarkdownNota.LineaMd> L(string s) => MarkdownNota.Lineas(s);

    // ---------------------------------------------------------------- frontmatter
    [Fact]
    public void Separa_el_frontmatter_y_lee_titulo_etiquetas_alias_y_estado()
    {
        var texto = "---\ntitle: Módulo Banco\ntags:\n  - sicpro\n  - api\naliases: [Bancos, Catálogo de bancos]\nstatus: vigente\n---\n# Otro título\nCuerpo";

        var s = MarkdownNota.SepararFrontmatter(texto);

        s.Cuerpo.Should().Be("# Otro título\nCuerpo");
        s.Advertencia.Should().BeNull();
        MarkdownNota.Texto(s.Datos, "title").Should().Be("Módulo Banco");
        MarkdownNota.Lista(s.Datos, true, "tags").Should().Equal("sicpro", "api");
        MarkdownNota.Lista(s.Datos, false, "aliases").Should().Equal("Bancos", "Catálogo de bancos");
        MarkdownNota.Texto(s.Datos, "STATUS").Should().Be("vigente", "las claves no distinguen mayúsculas");
    }

    [Theory]
    [InlineData("tags: a, b c", new[] { "a", "b", "c" })]
    [InlineData("tags: solo", new[] { "solo" })]
    public void Las_etiquetas_pueden_venir_como_texto(string yaml, string[] esperadas)
    {
        var s = MarkdownNota.SepararFrontmatter($"---\n{yaml}\n---\ncuerpo");
        MarkdownNota.Lista(s.Datos, true, "tags").Should().Equal(esperadas);
    }

    [Fact]
    public void Un_yaml_roto_no_tumba_la_nota_y_deja_una_advertencia()
    {
        var s = MarkdownNota.SepararFrontmatter("---\ntitle: [sin cerrar\n---\nCuerpo útil");

        s.Cuerpo.Should().Be("Cuerpo útil");
        s.Advertencia.Should().NotBeNull();
        s.Datos.Should().BeEmpty();
    }

    [Fact]
    public void Un_frontmatter_sin_cierre_es_parte_del_cuerpo()
    {
        var texto = "---\ntitle: algo\nsin cierre";
        MarkdownNota.SepararFrontmatter(texto).Cuerpo.Should().Be(texto);
    }

    [Fact]
    public void Un_frontmatter_demasiado_grande_se_conserva_como_texto()
    {
        var texto = "---\nnota: " + new string('x', 200) + "\n---\ncuerpo";
        var s = MarkdownNota.SepararFrontmatter(texto, maxBytes: 100);
        s.Cuerpo.Should().Be(texto);
        s.Advertencia.Should().NotBeNull();
    }

    [Fact]
    public void Un_frontmatter_vacio_o_que_no_es_un_mapa_no_rompe()
    {
        MarkdownNota.SepararFrontmatter("---\n---\ncuerpo").Cuerpo.Should().Be("cuerpo");
        MarkdownNota.SepararFrontmatter("---\n- a\n- b\n---\ncuerpo").Cuerpo.Should().Be("cuerpo");
    }

    // ---------------------------------------------------------------- código
    [Fact]
    public void Marca_como_codigo_las_lineas_dentro_de_vallas_de_distinto_largo()
    {
        var lineas = L("antes\n```bash\n# comentario\n```\ndespués\n~~~~\n#x\n~~~\nsigue en código\n~~~~\nfin");

        lineas.Select(l => l.EnCodigo).Should().Equal(false, true, true, true, false, true, true, true, true, true, false);
    }

    // ---------------------------------------------------------------- título
    [Fact]
    public void El_titulo_sale_del_primer_h1_fuera_de_bloques_de_codigo()
    {
        var t = MarkdownNota.TituloDeEncabezado(L("```bash\n# esto es un comentario\n```\n\n# **Guía** de [[Docker]]\n## Sección"));
        t.Should().Be("Guía de Docker");
    }

    [Fact]
    public void Sin_h1_no_hay_titulo_de_encabezado() =>
        MarkdownNota.TituloDeEncabezado(L("## Solo h2\ntexto")).Should().BeNull();

    // ---------------------------------------------------------------- etiquetas
    [Fact]
    public void Extrae_etiquetas_en_linea_ignorando_codigo_urls_encabezados_y_enlaces()
    {
        var cuerpo = string.Join('\n',
            "# Título",
            "Notas sobre #sicpro y #api/v1 con #Cero-Papel.",
            "Número #123 y año #2024 no son etiquetas, #fff sí.",
            "Código `#nada` y https://ejemplo.com/#ancla y [[Nota#Sección]] no cuentan.",
            "Entidad &#39; tampoco.",
            "```",
            "#dentro-de-codigo",
            "```");

        MarkdownNota.EtiquetasEnLineas(L(cuerpo)).Should().BeEquivalentTo("sicpro", "api/v1", "Cero-Papel", "fff");
    }

    [Theory]
    [InlineData("#sicpro", "sicpro")]
    [InlineData("  mi etiqueta ", "mi-etiqueta")]
    [InlineData("2024", null)]
    [InlineData("", null)]
    public void Normaliza_etiquetas(string cruda, string? esperada) =>
        MarkdownNota.NormalizarEtiqueta(cruda).Should().Be(esperada);

    // ---------------------------------------------------------------- enlaces
    [Fact]
    public void Extrae_los_distintos_tipos_de_wikilink()
    {
        var enlaces = MarkdownNota.EnlacesEnLineas(L("Ver [[Nota A]], [[Nota B|el alias]], [[Nota C#Sección]], [[Nota D#Sec|otro]] y ![[Nota E]]."), "");

        enlaces.Should().Equal(
            new EnlaceNota("Nota A", null, null),
            new EnlaceNota("Nota B", null, "el alias"),
            new EnlaceNota("Nota C", "Sección", null),
            new EnlaceNota("Nota D", "Sec", "otro"),
            new EnlaceNota("Nota E", null, null));
    }

    [Fact]
    public void Ignora_enlaces_a_adjuntos_a_la_misma_nota_y_los_que_estan_en_codigo()
    {
        var cuerpo = "![[captura.png]] [[#Esta sección]] [[manual.pdf]] `[[En código]]`\n```\n[[Bloque]]\n```";
        MarkdownNota.EnlacesEnLineas(L(cuerpo), "").Should().BeEmpty();
    }

    [Fact]
    public void Un_wikilink_con_extension_md_se_trata_como_nota()
    {
        MarkdownNota.EnlacesEnLineas(L("[[Guía.md]]"), "").Should().Equal(new EnlaceNota("Guía", null, null));
    }

    [Fact]
    public void Resuelve_enlaces_markdown_relativos_respecto_a_la_carpeta_de_origen()
    {
        var enlaces = MarkdownNota.EnlacesEnLineas(
            L("[Banco](../otra/M%C3%B3dulo%20X.md#parte) [web](https://ejemplo.com/x.md) [pdf](a.pdf) [ancla](#x) [fuera](../../../x.md)"),
            "sicpro/banco");

        enlaces.Should().Equal(new EnlaceNota("sicpro/otra/Módulo X", "parte", "Banco", EsRuta: true));
    }

    // ---------------------------------------------------------------- resumen y texto plano
    [Fact]
    public void El_resumen_es_el_primer_parrafo_normal_sin_formato()
    {
        var cuerpo = "# Título\n\n| a | b |\n|---|---|\n\nEste módulo **administra** los [[Bancos]] y\nsus cuentas.\n\nSegundo párrafo.";
        MarkdownNota.Resumen(L(cuerpo)).Should().Be("Este módulo administra los Bancos y sus cuentas.");
    }

    [Fact]
    public void El_resumen_se_recorta_en_limite_de_palabra_y_sin_estructura_es_null()
    {
        var largo = string.Join(' ', Enumerable.Repeat("palabra", 100));
        var r = MarkdownNota.Resumen(L(largo), 50);
        r.Should().EndWith("…");
        r!.Length.Should().BeLessThanOrEqualTo(51);

        MarkdownNota.Resumen(L("# Solo título\n- lista\n- otra\n```\ncódigo\n```")).Should().BeNull();
    }

    [Fact]
    public void Aplana_markdown_conservando_el_codigo_y_quitando_el_formato()
    {
        var md = string.Join('\n',
            "## Conexión",
            "> [!warning] Cuidado",
            "> No uses **root**.",
            "- [x] Paso con [enlace](https://x.com) y [[Nota|alias]]",
            "| Campo | Valor |",
            "|-------|-------|",
            "| host  | `db01` |",
            "```sql",
            "SELECT * FROM kb.Documento;",
            "```",
            "Texto con <b>HTML</b> y ![imagen](a.png) y \\*literal\\*.",
            "%% comentario privado %%");

        var texto = MarkdownNota.AplanarMarkdown(md);

        texto.Should().Contain("Conexión").And.Contain("Cuidado").And.Contain("No uses root")
            .And.Contain("Paso con enlace y alias").And.Contain("host db01")
            .And.Contain("SELECT * FROM kb.Documento;")
            .And.Contain("Texto con HTML y imagen y *literal*.");
        texto.Should().NotContain("```").And.NotContain("[[").And.NotContain("|--").And.NotContain("comentario privado")
            .And.NotContain("**").And.NotContain("<b>").And.NotContain("[!warning]");
    }

    [Fact]
    public void QuitarComentarios_conserva_los_saltos_de_linea()
    {
        var r = MarkdownNota.QuitarComentarios("a %%x\ny%% b <!-- c\nd --> e");
        r.Count(c => c == '\n').Should().Be(2);
        r.Should().NotContain("x").And.NotContain("c").And.Contain("a").And.Contain("e");
    }
}
