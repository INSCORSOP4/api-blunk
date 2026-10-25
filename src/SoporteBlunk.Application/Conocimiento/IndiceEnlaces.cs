using System.Text;

namespace SoporteBlunk.Application.Conocimiento;

public sealed record NotaIndexada(string Ruta, string Titulo, IReadOnlyList<string> Alias);

/// <summary>
/// Resuelve el destino de un enlace a una nota concreta, con las reglas de Obsidian:
/// ruta completa o parcial, luego nombre de archivo, luego alias y por último título (sin distinguir mayúsculas).
/// Si hay varias coincidencias gana la de la misma carpeta que el origen y, después, la ruta más corta.
/// </summary>
public sealed class IndiceEnlaces
{
    private readonly List<(NotaIndexada Nota, string SinExt)> _notas;

    public IndiceEnlaces(IEnumerable<NotaIndexada> notas) =>
        _notas = notas.Select(n => (n, Quitar(n.Ruta))).ToList();

    /// <returns>La ruta de la nota destino, o null si el enlace está roto.</returns>
    public string? Resolver(string destino, string rutaOrigen, bool esRuta = false)
    {
        var d = Normalizar(destino);
        if (d.Length == 0) return null;

        // Enlace tipo Markdown con ruta ya resuelta: solo vale la coincidencia exacta.
        if (esRuta) return _notas.FirstOrDefault(n => Igual(n.SinExt, d)).Nota?.Ruta;

        IEnumerable<NotaIndexada> candidatas;
        if (d.Contains('/'))
        {
            candidatas = _notas.Where(n => Igual(n.SinExt, d) || n.SinExt.EndsWith("/" + d, StringComparison.OrdinalIgnoreCase)).Select(n => n.Nota);
        }
        else
        {
            candidatas = _notas.Where(n => Igual(Nombre(n.SinExt), d)).Select(n => n.Nota);
            if (!candidatas.Any()) candidatas = _notas.Where(n => n.Nota.Alias.Any(a => Igual(Normalizar(a), d))).Select(n => n.Nota);
            if (!candidatas.Any()) candidatas = _notas.Where(n => Igual(Normalizar(n.Nota.Titulo), d)).Select(n => n.Nota);
        }

        var carpetaOrigen = RutaNota.Carpeta(rutaOrigen);
        return candidatas
            .OrderByDescending(n => Igual(Normalizar(RutaNota.Carpeta(n.Ruta)), Normalizar(carpetaOrigen)) ? 1 : 0)
            .ThenBy(n => n.Ruta.Count(c => c == '/'))
            .ThenBy(n => n.Ruta, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Ruta;
    }

    private static string Normalizar(string s)
    {
        var t = s.Normalize(NormalizationForm.FormC).Replace('\\', '/').Trim().Trim('/');
        while (t.StartsWith("./", StringComparison.Ordinal)) t = t[2..];
        return Quitar(t);
    }

    private static string Quitar(string ruta) => ruta.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? ruta[..^3] : ruta;
    private static string Nombre(string ruta) => ruta[(ruta.LastIndexOf('/') + 1)..];
    private static bool Igual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
