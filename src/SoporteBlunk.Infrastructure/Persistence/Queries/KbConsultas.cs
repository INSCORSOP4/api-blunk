using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Domain.Entities.Kb;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.Persistence.Queries;

internal sealed class DocumentoResumenFila
{
    public int Id { get; init; }
    public string Ruta { get; init; } = string.Empty;
    public string Titulo { get; init; } = string.Empty;
    public string Carpeta { get; init; } = string.Empty;
    public string? Resumen { get; init; }
    public string? Estado { get; init; }
    public DateTime ActualizadoEn { get; init; }
    public string ActualizadoPor { get; init; } = string.Empty;
    public int SecretosEnmascarados { get; init; }
    public string? Fragmento { get; init; }
}

internal sealed class EtiquetaDeDocumentoFila
{
    public int DocumentoId { get; init; }
    public string Nombre { get; init; } = string.Empty;
}

internal sealed class DocumentoRelacionadoFila
{
    public int Id { get; init; }
    public string Titulo { get; init; } = string.Empty;
    public string Ruta { get; init; } = string.Empty;
    public string Carpeta { get; init; } = string.Empty;

    public DocumentoRelacionado ToDomain() => new(Id, Titulo, Ruta, Carpeta);
}

internal sealed class EnlaceFila
{
    public string Destino { get; init; } = string.Empty;
    public string? Seccion { get; init; }
    public string? TextoVisible { get; init; }
    public int? DestinoId { get; init; }
    public string? TituloDestino { get; init; }
    public string? RutaDestino { get; init; }
    public string? CarpetaDestino { get; init; }
}

internal static class KbConsultas
{
    /// <summary>Búsqueda sin distinguir mayúsculas ni acentos ("contrasena" encuentra "Contraseña"). Tiene que ser constante: EF la escribe en el SQL.</summary>
    public const string Cotejo = "Latin1_General_100_CI_AI";

    private const string Escape = "\\";
    private const int LargoFragmento = 240;
    private const int AntesDelFragmento = 60;

    /// <summary>Notas vigentes que cumplen el filtro. La usan el conteo y la página, para que coincidan.</summary>
    public static IQueryable<KbDocumento> Filtradas(KbDbContext db, DocumentoFiltro filtro, IReadOnlyList<string> palabras)
    {
        var consulta = db.Documentos.Where(d => !d.Eliminado);

        if (!string.IsNullOrWhiteSpace(filtro.Carpeta))
        {
            var carpeta = filtro.Carpeta.Trim().Trim('/');
            var hijas = EscaparLike(carpeta) + "/%";
            consulta = consulta.Where(d => d.Carpeta == carpeta || EF.Functions.Like(d.Carpeta, hijas, Escape));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Etiqueta))
        {
            var etiqueta = filtro.Etiqueta.Trim().TrimStart('#');
            consulta = consulta.Where(d =>
                (from de in db.DocumentoEtiquetas
                 join e in db.Etiquetas on de.EtiquetaId equals e.Id
                 where de.DocumentoId == d.Id && e.Nombre == etiqueta
                 select de.DocumentoId).Any());
        }

        // Cada palabra debe aparecer (AND) en el título, el contenido, un alias o una etiqueta.
        foreach (var palabra in palabras)
        {
            var patron = $"%{EscaparLike(palabra)}%";
            consulta = consulta.Where(d =>
                EF.Functions.Like(EF.Functions.Collate(d.Titulo, Cotejo), patron, Escape)
                || EF.Functions.Like(EF.Functions.Collate(d.ContenidoTexto, Cotejo), patron, Escape)
                || db.DocumentoAliases.Any(a => a.DocumentoId == d.Id && EF.Functions.Like(EF.Functions.Collate(a.Alias, Cotejo), patron, Escape))
                || (from de in db.DocumentoEtiquetas
                    join e in db.Etiquetas on de.EtiquetaId equals e.Id
                    where de.DocumentoId == d.Id && EF.Functions.Like(EF.Functions.Collate(e.Nombre, Cotejo), patron, Escape)
                    select de.DocumentoId).Any());
        }
        return consulta;
    }

    /// <summary>
    /// Orden de la lista. Con búsqueda, primero las notas cuyo título contiene todas las palabras; luego, el orden pedido y el Id para
    /// que la paginación sea estable.
    /// </summary>
    public static IOrderedQueryable<KbDocumento> Ordenadas(IQueryable<KbDocumento> consulta, IReadOnlyList<string> palabras, OrdenDocumentos orden)
    {
        IOrderedQueryable<KbDocumento>? ordenada = null;
        if (palabras.Count > 0)
            ordenada = consulta.OrderBy(Prioridad(palabras));

        return orden == OrdenDocumentos.Titulo
            ? (ordenada is null ? consulta.OrderBy(d => d.Titulo) : ordenada.ThenBy(d => d.Titulo)).ThenBy(d => d.Id)
            : (ordenada is null ? consulta.OrderByDescending(d => d.ActualizadoEn) : ordenada.ThenByDescending(d => d.ActualizadoEn)).ThenByDescending(d => d.Id);
    }

    /// <summary>CASE WHEN (el título contiene todas las palabras) THEN 0 ELSE 1 END.</summary>
    private static Expression<Func<KbDocumento, int>> Prioridad(IReadOnlyList<string> palabras)
    {
        var d = Expression.Parameter(typeof(KbDocumento), "d");
        Expression? todas = null;
        foreach (var palabra in palabras)
        {
            var patron = $"%{EscaparLike(palabra)}%";
            Expression<Func<KbDocumento, bool>> coincide = x => EF.Functions.Like(EF.Functions.Collate(x.Titulo, Cotejo), patron, Escape);
            var cuerpo = new ReemplazaParametro(coincide.Parameters[0], d).Visit(coincide.Body)!;
            todas = todas is null ? cuerpo : Expression.AndAlso(todas, cuerpo);
        }
        return Expression.Lambda<Func<KbDocumento, int>>(Expression.Condition(todas!, Expression.Constant(0), Expression.Constant(1)), d);
    }

    /// <summary>
    /// Proyecta la página. Con búsqueda agrega un fragmento de ~240 caracteres alrededor de la primera coincidencia de la primera palabra
    /// (se calcula en SQL para no traer el texto completo de cada nota).
    /// </summary>
    public static IQueryable<DocumentoResumenFila> ProyectarPagina(IQueryable<KbDocumento> consulta, IReadOnlyList<string> palabras)
    {
        if (palabras.Count == 0)
            return consulta.Select(d => new DocumentoResumenFila
            {
                Id = d.Id, Ruta = d.RutaRelativa, Titulo = d.Titulo, Carpeta = d.Carpeta, Resumen = d.Resumen, Estado = d.Estado,
                ActualizadoEn = d.ActualizadoEn, ActualizadoPor = d.ActualizadoPorNombre, SecretosEnmascarados = d.SecretosEnmascarados,
                Fragmento = null,
            });

        var primera = palabras[0];
        return consulta.Select(d => new
            {
                d,
                // IndexOf es base 0 (-1 si no está): CHARINDEX - 1.
                Pos = EF.Functions.Collate(d.ContenidoTexto, Cotejo).IndexOf(primera),
            })
            .Select(x => new DocumentoResumenFila
            {
                Id = x.d.Id, Ruta = x.d.RutaRelativa, Titulo = x.d.Titulo, Carpeta = x.d.Carpeta, Resumen = x.d.Resumen, Estado = x.d.Estado,
                ActualizadoEn = x.d.ActualizadoEn, ActualizadoPor = x.d.ActualizadoPorNombre, SecretosEnmascarados = x.d.SecretosEnmascarados,
                Fragmento = x.Pos < 0
                    ? null
                    : x.d.ContenidoTexto.Substring(x.Pos > AntesDelFragmento - 1 ? x.Pos - AntesDelFragmento : 0, LargoFragmento),
            });
    }

    public static IQueryable<EtiquetaDeDocumentoFila> EtiquetasDe(KbDbContext db, IReadOnlyCollection<int> ids) =>
        from de in db.DocumentoEtiquetas
        join e in db.Etiquetas on de.EtiquetaId equals e.Id
        where ids.Contains(de.DocumentoId)
        orderby e.Nombre
        select new EtiquetaDeDocumentoFila { DocumentoId = de.DocumentoId, Nombre = e.Nombre };

    /// <summary>Enlaces de salida de una nota; el destino se resuelve solo si sigue vigente (LEFT JOIN con el filtro en el ON).</summary>
    public static IQueryable<EnlaceFila> EnlacesDe(KbDbContext db, int id) =>
        from e in db.Enlaces
        where e.DocumentoOrigenId == id
        join t0 in db.Documentos.Where(t => !t.Eliminado) on e.DocumentoDestinoId equals (int?)t0.Id into ts
        from t in ts.DefaultIfEmpty()
        orderby e.Id
        select new EnlaceFila
        {
            Destino = e.Destino, Seccion = e.Seccion, TextoVisible = e.TextoVisible, DestinoId = e.DocumentoDestinoId,
            TituloDestino = t!.Titulo, RutaDestino = t.RutaRelativa, CarpetaDestino = t.Carpeta,
        };

    /// <summary>Notas vigentes (distintas de ésta) que enlazan a la nota.</summary>
    // EF no puede ordenar después de proyectar a un constructor: se proyecta a una clase con inicializador (DISTINCT + ORDER BY) y el
    // récord se arma después, en memoria.
    public static IQueryable<DocumentoRelacionadoFila> EnlazadaDesde(KbDbContext db, int id) =>
        (from e in db.Enlaces
         join o in db.Documentos on e.DocumentoOrigenId equals o.Id
         where e.DocumentoDestinoId == id && !o.Eliminado && o.Id != id
         select new DocumentoRelacionadoFila { Id = o.Id, Titulo = o.Titulo, Ruta = o.RutaRelativa, Carpeta = o.Carpeta })
        .Distinct()
        .OrderBy(r => r.Titulo);

    public static IQueryable<CarpetaConteo> Carpetas(KbDbContext db) =>
        db.Documentos.Where(d => !d.Eliminado)
            .GroupBy(d => d.Carpeta)
            .OrderBy(g => g.Key)
            .Select(g => new CarpetaConteo(g.Key, g.Count()));

    public static IQueryable<EtiquetaConteo> Etiquetas(KbDbContext db) =>
        (from e in db.Etiquetas
         join de in db.DocumentoEtiquetas on e.Id equals de.EtiquetaId
         join d in db.Documentos on de.DocumentoId equals d.Id
         where !d.Eliminado
         group e by e.Nombre into g
         orderby g.Count() descending, g.Key
         select new EtiquetaConteo(g.Key, g.Count()));

    /// <summary>Palabras de la búsqueda: sin repetidas, con tope de cantidad y de largo.</summary>
    internal static List<string> Palabras(string? buscar, int maxPalabras, int maxLargo) =>
        (buscar ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > maxLargo ? w[..maxLargo] : w)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxPalabras)
            .ToList();

    /// <summary>Escapa los comodines de LIKE (% _ [ \) para que la búsqueda sea literal.</summary>
    internal static string EscaparLike(string s) =>
        s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");

    private sealed class ReemplazaParametro(ParameterExpression origen, Expression destino) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == origen ? destino : node;
    }
}
