using System.Text;
using System.Text.RegularExpressions;

namespace SoporteBlunk.Application.Conocimiento;

/// <summary>Validación y normalización de las rutas que llegan en una subida (nunca se confía en el navegador).</summary>
public static partial class RutaNota
{
    public const int MaxLongitud = 450; // coincide con kb.Documento.RutaRelativa

    public enum Problema { Ninguno, Vacia, NoRelativa, Peligrosa, CaracteresInvalidos, NoEsMarkdown, MuyLarga }

    public static Problema TryNormalizar(string? cruda, out string ruta)
    {
        ruta = string.Empty;
        if (string.IsNullOrWhiteSpace(cruda)) return Problema.Vacia;

        var s = cruda.Normalize(NormalizationForm.FormC).Replace('\\', '/').Trim();
        if (s.Any(char.IsControl)) return Problema.CaracteresInvalidos;
        if (s.StartsWith('/') || UnidadDeDisco().IsMatch(s)) return Problema.NoRelativa;

        var segmentos = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segmentos.Length == 0) return Problema.Vacia;
        // "." y ".." (y cualquier ".." dentro de un nombre, que la base también rechaza) son rutas peligrosas.
        if (segmentos.Any(seg => seg is "." || seg.Contains("..", StringComparison.Ordinal))) return Problema.Peligrosa;
        if (segmentos.Any(seg => string.IsNullOrWhiteSpace(seg))) return Problema.CaracteresInvalidos;

        var normalizada = string.Join('/', segmentos.Select(seg => seg.Trim()));
        if (normalizada.Length > MaxLongitud) return Problema.MuyLarga;
        if (!normalizada.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return Problema.NoEsMarkdown;

        ruta = normalizada;
        return Problema.Ninguno;
    }

    public static string Describir(Problema p) => p switch
    {
        Problema.Vacia => "La ruta está vacía.",
        Problema.NoRelativa => "La ruta debe ser relativa.",
        Problema.Peligrosa => "La ruta contiene '.' o '..'.",
        Problema.CaracteresInvalidos => "La ruta contiene caracteres no permitidos.",
        Problema.NoEsMarkdown => "No es un archivo .md.",
        Problema.MuyLarga => $"La ruta supera {MaxLongitud} caracteres.",
        _ => string.Empty,
    };

    /// <summary>Carpeta contenedora ("SICPRO/Banco" para "SICPRO/Banco/Nota.md"); vacía si está en la raíz.</summary>
    public static string Carpeta(string ruta)
    {
        var i = ruta.LastIndexOf('/');
        return i < 0 ? string.Empty : ruta[..i];
    }

    public static string NombreSinExtension(string ruta)
    {
        var nombre = ruta[(ruta.LastIndexOf('/') + 1)..];
        return nombre.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? nombre[..^3] : nombre;
    }

    /// <summary>Resuelve "../" y "./" de una ruta relativa respecto a una carpeta; null si sale de la raíz.</summary>
    public static string? Combinar(string carpetaBase, string relativa)
    {
        var partes = new List<string>(carpetaBase.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var seg in relativa.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                if (partes.Count == 0) return null;
                partes.RemoveAt(partes.Count - 1);
                continue;
            }
            partes.Add(seg);
        }
        return string.Join('/', partes);
    }

    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex UnidadDeDisco();
}
