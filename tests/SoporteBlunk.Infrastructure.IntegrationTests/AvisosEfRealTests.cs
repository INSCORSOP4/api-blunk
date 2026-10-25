using FluentAssertions;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// Ejecuta de verdad las operaciones de lectura contra las bases reales y comprueba que EF Core no emite ningún aviso (por ejemplo
/// "First/FirstOrDefault sin OrderBy ni filtro", o evaluación en el cliente). A diferencia de <see cref="AvisosEfTests"/>, que usa un
/// servidor caído y solo llega a traducir la primera consulta de cada operación, aquí se recorren todas. Se omite sin cadenas.
/// </summary>
public class AvisosEfRealTests
{
    [SkippableFact]
    public async Task Las_lecturas_de_ServiceDesk_no_emiten_avisos_de_EF()
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__ServiceDesk");
        Skip.If(string.IsNullOrWhiteSpace(cs), "Falta ConnectionStrings__ServiceDesk");
        var avisos = new List<string>();
        await using var db = ContextoDePrueba.ServiceDesk(cs!, avisos);
        var tickets = new TicketReadRepository(db);
        var tecnicos = new TecnicoReadRepository(db);
        var dashboard = new DashboardReadRepository(db);
        var catalogos = new CatalogoReadRepository(db);
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var desde = hoy.AddDays(-60);

        var filtro = new TicketFiltro
        {
            EstadoId = (await catalogos.ObtenerAsync("estados", default)).First().Id, Vencido = false, SinResponder = false,
            Desde = desde, Hasta = hoy, Buscar = "a",
        };
        foreach (var orden in Enum.GetValues<TicketOrden>()) await tickets.ListarAsync(filtro, orden, 1, 10, default);
        var primero = (await tickets.ListarAsync(new TicketFiltro(), TicketOrden.CreadoDesc, 1, 1, default)).Items.Single();
        await tickets.ListarParaExportAsync(filtro, 20, default);
        await tickets.ObtenerAsync(primero.Id, default);
        await tickets.ExisteAsync(primero.Id, default);
        await tickets.HistorialAsync(primero.Id, default);
        await tickets.TiemposAsync(primero.Id, default);

        foreach (var c in new[] { "estados", "prioridades", "grupos", "categorias", "subcategorias", "items", "urgencias", "impactos", "modos", "tipos-solicitud", "niveles", "departamentos" })
            await catalogos.ObtenerAsync(c, default);

        var tecnico = (await tecnicos.CargaAsync(default)).First().TecnicoId;
        await tecnicos.ListarAsync(default);
        await tecnicos.CargaAsync(tecnico, default);
        await tecnicos.ProductividadAsync(tecnico, desde, hoy, default);
        await tecnicos.ProductividadAsync(-1, desde, hoy, default);
        await tecnicos.ListarProductividadAsync(desde, hoy, default);
        foreach (var a in Enum.GetValues<Agrupacion>()) await tecnicos.TendenciaProductividadAsync(tecnico, desde, hoy, a, default);

        await dashboard.ResumenAsync(desde, hoy, default);
        await dashboard.ResumenAsync(new DateOnly(1990, 1, 1), new DateOnly(1990, 1, 2), default);
        await dashboard.SlaAsync(desde, hoy, default);
        foreach (var a in Enum.GetValues<Agrupacion>()) await dashboard.TendenciaAsync(desde, hoy, a, default);
        foreach (var d in new[] { "estado", "categoria", "prioridad", "grupo", "tecnico", "modo", "urgencia", "impacto", "tipo-solicitud" })
            await dashboard.PorAsync(d, desde, hoy, default);

        avisos.Should().BeEmpty("ninguna consulta de ServiceDesk debe emitir avisos de EF Core");
    }

    [SkippableFact]
    public async Task Las_lecturas_de_SoporteKB_no_emiten_avisos_de_EF()
    {
        var cs = KbReal.Requerir();
        var avisos = new List<string>();
        await using var db = ContextoDePrueba.Kb(cs, avisos);
        var conocimiento = new KbReadRepository(db);
        var importaciones = new KbImportacionRepository(db);
        var bitacora = new BitacoraRepository(db);
        var hoy = SoporteBlunk.Application.Common.ZonaHoraria.Hoy();

        await conocimiento.EstadoAsync(default);
        var docs = (await conocimiento.ListarDocumentosAsync(new DocumentoFiltro(), 1, 20, default)).Items;
        var palabra = docs.First().Titulo.Split(' ')[0];
        foreach (var orden in new[] { OrdenDocumentos.Recientes, OrdenDocumentos.Titulo })
        {
            await conocimiento.ListarDocumentosAsync(new DocumentoFiltro { Buscar = palabra, Orden = orden }, 1, 10, default);
            await conocimiento.ListarDocumentosAsync(new DocumentoFiltro { Buscar = $"{palabra} nota otra", Orden = orden }, 1, 10, default);
        }
        var carpeta = (await conocimiento.CarpetasAsync(default)).First().Carpeta;
        await conocimiento.ListarDocumentosAsync(new DocumentoFiltro { Carpeta = carpeta }, 1, 10, default);
        var etiquetas = await conocimiento.EtiquetasAsync(default);
        if (etiquetas.Count > 0) await conocimiento.ListarDocumentosAsync(new DocumentoFiltro { Etiqueta = etiquetas[0].Nombre }, 1, 10, default);
        foreach (var d in docs.Take(5)) await conocimiento.ObtenerDocumentoAsync(d.Id, default);
        await conocimiento.ObtenerDocumentoAsync(-1, default);

        await importaciones.ReglasOmisionAsync(default);
        var lista = await importaciones.ListarAsync(10, default);
        if (lista.Count > 0) await importaciones.ObtenerAsync(lista[0].ImportacionId, default);

        foreach (var g in new[] { "actividades-soft", "respaldos", "camaras" })
        {
            await bitacora.ListarActividadesAsync(g, true, default);
            await bitacora.ObtenerChecklistAsync(g, hoy, default);
            await bitacora.ObtenerResumenAsync(g, hoy.AddDays(-10), hoy, default);
        }
        await bitacora.ObtenerActividadAsync(1, default);
        await bitacora.UltimosCambiosAsync(10, default);
        await new AuditoriaMicrosipRepository(db).ListarAsync(10, default);
        await new ArchivosReadRepository(db).ListarAsync(default);

        avisos.Should().BeEmpty("ninguna consulta de SoporteKB debe emitir avisos de EF Core");
    }
}
