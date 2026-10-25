using FluentAssertions;
using SoporteBlunk.Application.Conocimiento;

namespace SoporteBlunk.Application.Tests.Conocimiento;

public class RutaNotaTests
{
    [Theory]
    [InlineData("SICPRO/Módulo Banco.md", "SICPRO/Módulo Banco.md")]
    [InlineData("SICPRO\\Módulo Banco.md", "SICPRO/Módulo Banco.md")]
    [InlineData("  /  ", null)]
    [InlineData("Nota.MD", "Nota.MD")]
    [InlineData("a//b///c.md", "a/b/c.md")]
    public void Normaliza_separadores_y_espacios(string cruda, string? esperada)
    {
        var p = RutaNota.TryNormalizar(cruda, out var ruta);
        if (esperada is null) p.Should().NotBe(RutaNota.Problema.Ninguno);
        else { p.Should().Be(RutaNota.Problema.Ninguno); ruta.Should().Be(esperada); }
    }

    [Theory]
    [InlineData("../secreto.md", RutaNota.Problema.Peligrosa)]
    [InlineData("a/../../b.md", RutaNota.Problema.Peligrosa)]
    [InlineData("a/./b.md", RutaNota.Problema.Peligrosa)]
    [InlineData("Notas..md", RutaNota.Problema.Peligrosa)]
    [InlineData("/etc/passwd.md", RutaNota.Problema.NoRelativa)]
    [InlineData("C:\\Users\\x\\nota.md", RutaNota.Problema.NoRelativa)]
    [InlineData("a\0b.md", RutaNota.Problema.CaracteresInvalidos)]
    [InlineData("a\nb.md", RutaNota.Problema.CaracteresInvalidos)]
    [InlineData("", RutaNota.Problema.Vacia)]
    [InlineData(null, RutaNota.Problema.Vacia)]
    [InlineData("imagen.png", RutaNota.Problema.NoEsMarkdown)]
    [InlineData("README", RutaNota.Problema.NoEsMarkdown)]
    public void Rechaza_rutas_peligrosas_o_invalidas(string? cruda, RutaNota.Problema esperado) =>
        RutaNota.TryNormalizar(cruda, out _).Should().Be(esperado);

    [Fact]
    public void Rechaza_rutas_mas_largas_que_la_columna_de_la_base()
    {
        var larga = string.Join('/', Enumerable.Repeat(new string('a', 50), 10)) + ".md";
        RutaNota.TryNormalizar(larga, out _).Should().Be(RutaNota.Problema.MuyLarga);
    }

    [Fact]
    public void Calcula_carpeta_y_nombre()
    {
        RutaNota.Carpeta("a/b/c.md").Should().Be("a/b");
        RutaNota.Carpeta("c.md").Should().Be("");
        RutaNota.NombreSinExtension("a/b/Guía Docker.md").Should().Be("Guía Docker");
    }

    [Theory]
    [InlineData("a/b", "../c.md", "a/c.md")]
    [InlineData("a/b", "./c.md", "a/b/c.md")]
    [InlineData("", "x/y.md", "x/y.md")]
    [InlineData("a", "../../c.md", null)]
    public void Combina_rutas_relativas_sin_salir_de_la_raiz(string baseDir, string relativa, string? esperada) =>
        RutaNota.Combinar(baseDir, relativa).Should().Be(esperada);
}

public class ReglasOmisionTests
{
    private static ReglasOmisionService Reglas() => new(
    [
        new ReglaOmision(ReglaOmision.Carpeta, ".obsidian"),
        new ReglaOmision(ReglaOmision.Carpeta, "Cambios de contraseña chemicloud"),
        new ReglaOmision(ReglaOmision.Archivo, "Privado.md"),
    ]);

    [Theory]
    [InlineData(".obsidian/plugins/x.md")]
    [InlineData("SICPRO/.OBSIDIAN/x.md")]
    [InlineData("Cambios de contraseña chemicloud/nota.md")]
    [InlineData("Cambios de contrasen\u0303a chemicloud/nota.md")] // "ñ" descompuesta (n + tilde)
    [InlineData("a/b/privado.md")]
    public void Omite_por_carpeta_o_archivo_sin_distinguir_mayusculas_ni_forma_unicode(string ruta) =>
        Reglas().MotivoDeOmision(ruta).Should().NotBeNull();

    [Theory]
    [InlineData("SICPRO/Nota.md")]
    [InlineData("obsidian-guia.md")]             // contiene el texto pero no es la carpeta
    [InlineData("Cambios de contraseña chemicloud.md")] // un archivo con ese nombre, no una carpeta
    public void No_omite_lo_que_no_coincide_exactamente(string ruta) =>
        Reglas().MotivoDeOmision(ruta).Should().BeNull();
}

public class IndiceEnlacesTests
{
    private static IndiceEnlaces Indice() => new(
    [
        new NotaIndexada("SICPRO/Módulo Banco.md", "Módulo Banco — SICPRO", ["Bancos"]),
        new NotaIndexada("CERO PAPEL/Módulo Banco.md", "Módulo Banco — Cero Papel", []),
        new NotaIndexada("DOCKER/Importar respaldo.md", "Importar respaldo .BAK", ["Restaurar BAK"]),
        new NotaIndexada("Contabilidad.md", "Contabilidad Microsip", []),
        new NotaIndexada("a/b/Guía.md", "Guía larga", []),
        new NotaIndexada("Guía.md", "Guía corta", []),
    ]);

    [Theory]
    [InlineData("Contabilidad", "x/y.md", "Contabilidad.md")]
    [InlineData("contabilidad", "x/y.md", "Contabilidad.md")]
    [InlineData("Contabilidad.md", "x/y.md", "Contabilidad.md")]
    [InlineData("DOCKER/Importar respaldo", "x.md", "DOCKER/Importar respaldo.md")]
    [InlineData("Importar respaldo", "x.md", "DOCKER/Importar respaldo.md")]
    [InlineData("b/Guía", "x.md", "a/b/Guía.md")]
    public void Resuelve_por_nombre_o_ruta_parcial_sin_distinguir_mayusculas(string destino, string origen, string esperada) =>
        Indice().Resolver(destino, origen).Should().Be(esperada);

    [Fact]
    public void Con_varias_coincidencias_prefiere_la_misma_carpeta_y_luego_la_ruta_mas_corta()
    {
        Indice().Resolver("Módulo Banco", "SICPRO/otra.md").Should().Be("SICPRO/Módulo Banco.md");
        Indice().Resolver("Módulo Banco", "CERO PAPEL/x.md").Should().Be("CERO PAPEL/Módulo Banco.md");
        Indice().Resolver("Guía", "otro/lugar.md").Should().Be("Guía.md");
    }

    [Fact]
    public void Si_no_hay_archivo_con_ese_nombre_usa_alias_y_despues_titulo()
    {
        Indice().Resolver("Bancos", "x.md").Should().Be("SICPRO/Módulo Banco.md");
        Indice().Resolver("restaurar bak", "x.md").Should().Be("DOCKER/Importar respaldo.md");
        Indice().Resolver("Contabilidad Microsip", "x.md").Should().Be("Contabilidad.md");
    }

    [Fact]
    public void Un_enlace_roto_devuelve_null_y_el_de_ruta_solo_acepta_coincidencia_exacta()
    {
        Indice().Resolver("No existe", "x.md").Should().BeNull();
        Indice().Resolver("Módulo Banco", "x.md", esRuta: true).Should().BeNull();
        Indice().Resolver("SICPRO/Módulo Banco", "x.md", esRuta: true).Should().Be("SICPRO/Módulo Banco.md");
    }
}
