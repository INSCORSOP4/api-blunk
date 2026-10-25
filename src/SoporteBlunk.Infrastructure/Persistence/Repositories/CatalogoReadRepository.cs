using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class CatalogoReadRepository(ServiceDeskDbContext db) : ICatalogoReadRepository
{
    // Clave pública -> consulta fija. Nunca se compone con entrada del usuario.
    private static readonly Dictionary<string, Func<ServiceDeskDbContext, IQueryable<CatalogoItem>>> Consultas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["estados"] = d => d.Estados.Where(x => !x.Eliminado).OrderBy(x => x.Id)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["prioridades"] = d => d.Prioridades.Where(x => !x.Eliminado).OrderBy(x => x.Orden).ThenBy(x => x.Id)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["grupos"] = d => d.Grupos.Where(x => !x.Eliminado).OrderBy(x => x.Nombre)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["categorias"] = d => d.Categorias.Where(x => !x.Eliminado).OrderBy(x => x.Nombre)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["subcategorias"] = d => d.Subcategorias.Where(x => !x.Eliminado).OrderBy(x => x.Nombre)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, x.CategoriaId)),
            ["items"] = d => d.Items.Where(x => !x.Eliminado).OrderBy(x => x.Nombre)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, x.SubCategoriaId)),
            ["urgencias"] = d => d.Urgencias.Where(x => !x.Eliminado).OrderBy(x => x.Id)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["impactos"] = d => d.Impactos.Where(x => !x.Eliminado).OrderBy(x => x.Id)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["modos"] = d => d.Modos.Where(x => !x.Eliminado).OrderBy(x => x.Id)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["tipos-solicitud"] = d => d.TiposSolicitud.Where(x => !x.Eliminado).OrderBy(x => x.Id)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["niveles"] = d => d.Niveles.Where(x => !x.Eliminado).OrderBy(x => x.Id)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
            ["departamentos"] = d => d.Departamentos.Where(x => !x.Eliminado).OrderBy(x => x.Nombre)
                .Select(x => new CatalogoItem(x.Id, x.Nombre ?? string.Empty, null)),
        };

    public async Task<IReadOnlyList<CatalogoItem>> ObtenerAsync(string catalogo, CancellationToken ct) =>
        await Consulta(db, catalogo).ToListAsync(ct);

    internal static IQueryable<CatalogoItem> Consulta(ServiceDeskDbContext db, string catalogo) =>
        Consultas.TryGetValue(catalogo, out var consulta)
            ? consulta(db)
            : throw new ArgumentException($"Catálogo desconocido: {catalogo}", nameof(catalogo));
}
