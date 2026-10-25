using FluentAssertions;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Kb;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// Compara la lectura de la base de conocimiento en EF Core contra la implementación Dapper original, sobre los documentos reales de
/// SoporteKB (solo lectura). Se omite sin ConnectionStrings__SoporteKB.
/// </summary>
[Collection(ColeccionSoporteKb.Nombre)]
public class KbLecturaParidadTests
{
    private static (KbDbContext Db, KbReadRepository Nuevo, KbReadRepositoryDapper Anterior) Repos()
    {
        var cs = KbReal.Requerir();
        var db = ContextoDePrueba.Kb(cs);
        return (db, new KbReadRepository(db), new KbReadRepositoryDapper(new KbConnectionFactory(cs)));
    }

    private static void Igual<T>(T actual, T esperado, string motivo) =>
        actual.Should().BeEquivalentTo(esperado, o => o.WithStrictOrdering(), motivo);

    [SkippableFact]
    public async Task El_estado_coincide()
    {
        var (db, nuevo, anterior) = Repos();
        await using var _ = db;

        var n = await nuevo.EstadoAsync(default);

        n.Should().Be(await anterior.EstadoAsync(default));
        n.Documentos.Should().BeGreaterThan(0, "la prueba necesita notas reales");
        n.VersionEsquema.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Carpetas_y_etiquetas_coinciden()
    {
        var (db, nuevo, anterior) = Repos();
        await using var _ = db;

        var carpetas = await nuevo.CarpetasAsync(default);
        var etiquetas = await nuevo.EtiquetasAsync(default);

        Igual(carpetas, await anterior.CarpetasAsync(default), "carpetas");
        Igual(etiquetas, await anterior.EtiquetasAsync(default), "etiquetas");
        carpetas.Should().NotBeEmpty();
        etiquetas.Select(e => e.Documentos).Should().BeInDescendingOrder();
    }

    [SkippableFact]
    public async Task El_listado_y_la_busqueda_coinciden_en_muchas_combinaciones()
    {
        var (db, nuevo, anterior) = Repos();
        await using var _ = db;
        var todos = (await anterior.ListarDocumentosAsync(new DocumentoFiltro(), 1, 500, default)).Items;
        todos.Should().NotBeEmpty();
        var carpetas = (await anterior.CarpetasAsync(default)).Select(c => c.Carpeta).Where(c => c.Length > 0).Take(4).ToList();
        var etiquetas = (await anterior.EtiquetasAsync(default)).Select(e => e.Nombre).Take(3).ToList();
        var palabras = todos.Take(12).SelectMany(d => d.Titulo.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(w => w.Length >= 4).Distinct().Take(10).ToList();

        // Términos reales (de los títulos) y casos límite: sin acento, mayúsculas, comodines de LIKE, corchetes, muy largo, repetido.
        var textos = new List<string?> { null, "", "  " };
        textos.AddRange(palabras);
        textos.AddRange(palabras.Take(4).Select(w => w.ToUpperInvariant()));
        textos.AddRange(palabras.Take(4).Select(QuitarAcentos));
        textos.AddRange(palabras.Take(3).Zip(palabras.Skip(1), (a, b) => $"{a} {b}"));
        textos.AddRange(["100%", "_", "[x]", "a_b", @"\", "'; DROP TABLE kb.Documento; --", new string('a', 80), "docker docker DOCKER", "no existe esta cadena zzzqq", "é", "ñ"]);

        var casos = 0;
        foreach (var texto in textos)
            foreach (var orden in new[] { OrdenDocumentos.Recientes, OrdenDocumentos.Titulo })
            {
                var filtro = new DocumentoFiltro { Buscar = texto, Orden = orden };
                Igual(await nuevo.ListarDocumentosAsync(filtro, 1, 20, default), await anterior.ListarDocumentosAsync(filtro, 1, 20, default),
                    $"buscar='{texto}' orden={orden}");
                casos++;
            }

        foreach (var carpeta in carpetas)
            foreach (var texto in new string?[] { null, palabras.FirstOrDefault() })
            {
                var filtro = new DocumentoFiltro { Carpeta = carpeta, Buscar = texto };
                Igual(await nuevo.ListarDocumentosAsync(filtro, 1, 50, default), await anterior.ListarDocumentosAsync(filtro, 1, 50, default), $"carpeta '{carpeta}' buscar='{texto}'");
                casos++;
            }
        foreach (var etiqueta in etiquetas)
            foreach (var variante in new[] { etiqueta, "#" + etiqueta, etiqueta.ToUpperInvariant() })
            {
                var filtro = new DocumentoFiltro { Etiqueta = variante };
                Igual(await nuevo.ListarDocumentosAsync(filtro, 1, 50, default), await anterior.ListarDocumentosAsync(filtro, 1, 50, default), $"etiqueta '{variante}'");
                casos++;
            }

        // Paginación: todas las páginas con tamaño pequeño, y una página fuera de rango.
        foreach (var pagina in new[] { 1, 2, 3, 4, 99 })
        {
            var filtro = new DocumentoFiltro { Buscar = palabras.FirstOrDefault() };
            Igual(await nuevo.ListarDocumentosAsync(filtro, pagina, 5, default), await anterior.ListarDocumentosAsync(filtro, pagina, 5, default), $"página {pagina}");
            casos++;
        }
        casos.Should().BeGreaterThan(60);
        todos.Count(d => d.Fragmento is null).Should().Be(todos.Count, "sin búsqueda no hay fragmento");
        (await nuevo.ListarDocumentosAsync(new DocumentoFiltro { Buscar = palabras[0] }, 1, 20, default)).Items
            .Should().Contain(d => d.Fragmento != null, "con búsqueda al menos una nota trae su fragmento");
    }

    [SkippableFact]
    public async Task El_detalle_de_todas_las_notas_coincide()
    {
        var (db, nuevo, anterior) = Repos();
        await using var _ = db;
        var ids = (await anterior.ListarDocumentosAsync(new DocumentoFiltro(), 1, 500, default)).Items.Select(d => d.Id).Concat([-1, 0, int.MaxValue]).ToList();

        var conVersiones = 0;
        foreach (var id in ids)
        {
            var n = await nuevo.ObtenerDocumentoAsync(id, default);
            var a = await anterior.ObtenerDocumentoAsync(id, default);
            Igual(n, a, $"detalle {id}");
            conVersiones += n?.Versiones.Count > 0 ? 1 : 0;
        }
        ids.Count.Should().BeGreaterThan(5);
        conVersiones.Should().BeGreaterThan(0, "la prueba debe incluir notas con historial");
    }

    private static string QuitarAcentos(string s) => new(s.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
}
