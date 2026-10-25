using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace SoporteBlunk.Application.Conocimiento;

/// <summary>Análisis de una nota de Obsidian: frontmatter, título, etiquetas, enlaces, resumen y texto plano.</summary>
public static partial class MarkdownNota
{
    private const int TimeoutMs = 2000;
    private const RegexOptions Base = RegexOptions.CultureInvariant;

    public sealed record Separado(IReadOnlyDictionary<string, object?> Datos, string Cuerpo, string? Advertencia);

    public sealed record LineaMd(int Numero, string Texto, bool EnCodigo);

    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();

    // ------------------------------------------------------------------ frontmatter
    /// <summary>Separa el bloque YAML inicial (---) del cuerpo. Un YAML roto o enorme no tumba la nota.</summary>
    public static Separado SepararFrontmatter(string texto, int maxBytes = 32_768)
    {
        var vacio = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!texto.StartsWith("---\n", StringComparison.Ordinal)) return new Separado(vacio, texto, null);

        var cierre = CierreFrontmatter().Match(texto, 3);
        if (!cierre.Success) return new Separado(vacio, texto, null);

        var yaml = texto.Substring(4, Math.Max(0, cierre.Index - 4));
        var cuerpo = texto[(cierre.Index + cierre.Length)..].TrimStart('\n');

        if (Encoding.UTF8.GetByteCount(yaml) > maxBytes)
            return new Separado(vacio, texto, "El frontmatter supera el tamaño permitido; se conservó como texto.");

        try
        {
            var crudo = Yaml.Deserialize<Dictionary<string, object?>>(yaml);
            var datos = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (crudo is not null)
                foreach (var (k, v) in crudo) datos.TryAdd(k, v);
            return new Separado(datos, cuerpo, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new Separado(vacio, cuerpo, "El frontmatter no es YAML válido; se ignoró.");
        }
    }

    public static string? Texto(IReadOnlyDictionary<string, object?> datos, params string[] claves)
    {
        foreach (var clave in claves)
            if (datos.TryGetValue(clave, out var v) && v is not null)
            {
                var s = v switch { string t => t, IEnumerable<object?> l => l.FirstOrDefault()?.ToString(), _ => v.ToString() };
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
        return null;
    }

    /// <summary>Lee un valor que puede ser texto ("a, b") o lista YAML.</summary>
    public static IReadOnlyList<string> Lista(IReadOnlyDictionary<string, object?> datos, bool separarPorEspacios, params string[] claves)
    {
        var salida = new List<string>();
        foreach (var clave in claves)
        {
            if (!datos.TryGetValue(clave, out var v) || v is null) continue;
            switch (v)
            {
                case string s:
                    salida.AddRange(separarPorEspacios ? SepararLista().Split(s) : [s]);
                    break;
                case IEnumerable<object?> items:
                    salida.AddRange(items.Where(i => i is not null).Select(i => i!.ToString() ?? ""));
                    break;
                default:
                    salida.Add(v.ToString() ?? "");
                    break;
            }
        }
        return salida.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    // ------------------------------------------------------------------ líneas / código
    /// <summary>Divide en líneas marcando cuáles están dentro de un bloque de código (``` o ~~~).</summary>
    public static IReadOnlyList<LineaMd> Lineas(string cuerpo)
    {
        var resultado = new List<LineaMd>();
        char? valla = null;
        var largo = 0;
        var n = 0;
        foreach (var linea in cuerpo.Split('\n'))
        {
            n++;
            var m = Valla().Match(linea);
            if (valla is null)
            {
                if (m.Success)
                {
                    valla = m.Groups[1].Value[0];
                    largo = m.Groups[1].Length;
                    resultado.Add(new LineaMd(n, linea, true));
                }
                else resultado.Add(new LineaMd(n, linea, false));
                continue;
            }

            resultado.Add(new LineaMd(n, linea, true));
            if (m.Success && m.Groups[1].Value[0] == valla && m.Groups[1].Length >= largo && string.IsNullOrWhiteSpace(m.Groups[2].Value))
                valla = null;
        }
        return resultado;
    }

    /// <summary>Quita comentarios de Obsidian (%% %%) y HTML, conservando los saltos de línea.</summary>
    public static string QuitarComentarios(string s) =>
        Comentarios().Replace(s, m => new string('\n', m.Value.Count(c => c == '\n')));

    // ------------------------------------------------------------------ título, etiquetas, enlaces
    public static string? TituloDeEncabezado(IEnumerable<LineaMd> lineas)
    {
        foreach (var l in lineas.Where(l => !l.EnCodigo))
        {
            var m = EncabezadoH1().Match(l.Texto);
            if (!m.Success) continue;
            var t = AplanarInline(m.Groups["t"].Value);
            if (t.Length > 0) return t;
        }
        return null;
    }

    public static string? NormalizarEtiqueta(string cruda)
    {
        var t = cruda.Trim().TrimStart('#').Trim().Replace(' ', '-');
        if (t.Length is 0 or > 100) return null;
        return t.Any(char.IsLetter) ? t : null; // "#1" o "#2024" no son etiquetas
    }

    public static IReadOnlyList<string> EtiquetasEnLineas(IEnumerable<LineaMd> lineas)
    {
        var vistas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lineas.Where(l => !l.EnCodigo))
        {
            var limpia = BuscableSinEnlaces(l.Texto);
            foreach (Match m in EtiquetaEnTexto().Matches(limpia))
                if (NormalizarEtiqueta(m.Groups["t"].Value) is { } e) vistas.TryAdd(e, e);
        }
        return vistas.Values.ToList();
    }

    public static IReadOnlyList<EnlaceNota> EnlacesEnLineas(IEnumerable<LineaMd> lineas, string carpetaOrigen)
    {
        var enlaces = new List<EnlaceNota>();
        foreach (var l in lineas.Where(l => !l.EnCodigo))
        {
            var texto = InlineCode().Replace(l.Texto, " ");

            foreach (Match m in Wikilink().Matches(texto))
            {
                var destino = m.Groups["t"].Value.Trim();
                if (destino.Length == 0) continue; // [[#Seccion]]: enlace dentro de la misma nota
                var extension = Path.GetExtension(destino);
                if (extension.Length > 0 && !extension.Equals(".md", StringComparison.OrdinalIgnoreCase) && extension.Length <= 5)
                    continue; // adjuntos (png, pdf...): no se importan
                if (destino.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) destino = destino[..^3];

                var seccion = m.Groups["s"].Success ? m.Groups["s"].Value.Trim() : null;
                var alias = m.Groups["a"].Success ? m.Groups["a"].Value.Trim() : null;
                enlaces.Add(new EnlaceNota(destino, string.IsNullOrEmpty(seccion) ? null : seccion, string.IsNullOrEmpty(alias) ? null : alias));
            }

            foreach (Match m in EnlaceMarkdown().Matches(texto))
            {
                var url = m.Groups["url"].Value;
                if (UrlExterna().IsMatch(url) || url.StartsWith('#')) continue;

                var hash = url.IndexOf('#');
                var ruta = Uri.UnescapeDataString(hash >= 0 ? url[..hash] : url);
                var seccion = hash >= 0 ? Uri.UnescapeDataString(url[(hash + 1)..]) : null;
                if (!ruta.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

                var absoluta = RutaNota.Combinar(carpetaOrigen, ruta);
                if (absoluta is null) continue;
                var texto2 = m.Groups["txt"].Value.Trim();
                enlaces.Add(new EnlaceNota(absoluta[..^3], string.IsNullOrEmpty(seccion) ? null : seccion,
                    string.IsNullOrEmpty(texto2) ? null : texto2, EsRuta: true));
            }
        }
        return enlaces;
    }

    // ------------------------------------------------------------------ resumen y texto plano
    public static string? Resumen(IEnumerable<LineaMd> lineas, int max = 280)
    {
        var partes = new List<string>();
        foreach (var l in lineas)
        {
            var t = l.Texto.Trim();
            if (l.EnCodigo) { if (partes.Count > 0) break; continue; }
            if (t.Length == 0) { if (partes.Count > 0) break; continue; }
            if (EsEstructura(t)) { if (partes.Count > 0) break; continue; }
            partes.Add(t);
        }
        if (partes.Count == 0) return null;

        var texto = AplanarInline(string.Join(' ', partes));
        if (texto.Length == 0) return null;
        if (texto.Length <= max) return texto;
        var corte = texto.LastIndexOf(' ', max - 1);
        return texto[..(corte > max / 2 ? corte : max - 1)].TrimEnd(' ', ',', ';', ':', '.') + "…";
    }

    // Línea que no sirve como resumen: encabezado, tabla, cita, HTML, imagen, elemento de lista o línea horizontal.
    private static bool EsEstructura(string t) =>
        t[0] is '#' or '|' or '>' or '<' || t.StartsWith("![", StringComparison.Ordinal) ||
        ElementoDeLista().IsMatch(t) || SeparadorHorizontal().IsMatch(t);

    /// <summary>Convierte Markdown a texto plano para indexar la búsqueda (el código se conserva, sin las vallas).</summary>
    public static string AplanarMarkdown(string cuerpo)
    {
        var sb = new StringBuilder(cuerpo.Length);
        foreach (var l in Lineas(QuitarComentarios(cuerpo)))
        {
            var t = l.Texto;
            if (l.EnCodigo)
            {
                if (Valla().IsMatch(t)) continue;
                AgregarLinea(sb, t.Trim());
                continue;
            }
            if (SeparadorTabla().IsMatch(t) || SeparadorHorizontal().IsMatch(t.Trim())) continue;

            t = Encabezado().Replace(t, "");
            t = Cita().Replace(t, "");
            t = MarcaDeLista().Replace(t, "");
            t = AplanarInline(t).Replace('|', ' ');
            AgregarLinea(sb, ColapsarEspacios(t));
        }
        return sb.ToString().Trim();
    }

    private static void AgregarLinea(StringBuilder sb, string linea)
    {
        if (linea.Length > 0) sb.Append(linea).Append('\n');
    }

    /// <summary>Quita el formato en línea (énfasis, enlaces, imágenes, HTML, código) dejando el texto visible.</summary>
    public static string AplanarInline(string t)
    {
        // Los caracteres escapados (\* \_ ...) se protegen primero para que el quitado de énfasis no los toque.
        t = Escape().Replace(t, m => ((char)(0xE000 + m.Groups[1].Value[0])).ToString());
        t = Imagen().Replace(t, "${alt}");
        t = EnlaceMarkdownPlano().Replace(t, "${txt}");
        t = Wikilink().Replace(t, m =>
        {
            var alias = m.Groups["a"].Success ? m.Groups["a"].Value.Trim() : "";
            if (alias.Length > 0) return alias;
            var destino = m.Groups["t"].Value.Trim();
            var seccion = m.Groups["s"].Success ? m.Groups["s"].Value.Trim() : "";
            return seccion.Length > 0 ? $"{destino} {seccion}".Trim() : destino;
        });
        t = EtiquetaHtml().Replace(t, " ");
        t = InlineCodeContenido().Replace(t, "${c}");
        t = Enfasis().Replace(t, "");
        t = string.Create(t.Length, t, static (destino, origen) =>
        {
            for (var i = 0; i < origen.Length; i++)
            {
                var c = origen[i];
                destino[i] = c is >= (char)0xE000 and <= (char)0xE0FF ? (char)(c - 0xE000) : c; // restaura los escapados
            }
        });
        return ColapsarEspacios(t);
    }

    private static string ColapsarEspacios(string s) => Espacios().Replace(s, " ").Trim();

    private static string BuscableSinEnlaces(string linea)
    {
        var t = InlineCode().Replace(linea, " ");
        t = Wikilink().Replace(t, " ");
        t = EnlaceMarkdownPlano().Replace(t, " ");
        return UrlSuelta().Replace(t, " ");
    }

    // ------------------------------------------------------------------ expresiones regulares
    [GeneratedRegex(@"\n(?:---|\.\.\.)[ \t]*(?=\n|$)", Base, TimeoutMs)] private static partial Regex CierreFrontmatter();
    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})(.*)$", Base, TimeoutMs)] private static partial Regex Valla();
    [GeneratedRegex(@"%%[\s\S]*?%%|<!--[\s\S]*?-->", Base, TimeoutMs)] private static partial Regex Comentarios();
    [GeneratedRegex(@"^#(?!#)[ \t]+(?<t>.+?)[ \t]*#*[ \t]*$", Base, TimeoutMs)] private static partial Regex EncabezadoH1();
    [GeneratedRegex(@"(?<![\p{L}\p{N}_&/#\-])#(?<t>[\p{L}\p{N}_\-/]+)", Base, TimeoutMs)] private static partial Regex EtiquetaEnTexto();
    [GeneratedRegex(@"(?<embed>!)?\[\[(?<t>[^\]\|#\n]*)(?:#(?<s>[^\]\|\n]*))?(?:\|(?<a>[^\]\n]*))?\]\]", Base, TimeoutMs)] private static partial Regex Wikilink();
    [GeneratedRegex(@"(?<!!)\[(?<txt>[^\]\n]*)\]\((?<url>[^)\s]+)(?:\s+""[^""]*"")?\)", Base, TimeoutMs)] private static partial Regex EnlaceMarkdown();
    [GeneratedRegex(@"!?\[(?<txt>[^\]\n]*)\]\([^)\n]*\)", Base, TimeoutMs)] private static partial Regex EnlaceMarkdownPlano();
    [GeneratedRegex(@"!\[(?<alt>[^\]\n]*)\]\([^)\n]*\)", Base, TimeoutMs)] private static partial Regex Imagen();
    [GeneratedRegex(@"^[a-z][a-z0-9+.\-]*:", RegexOptions.IgnoreCase | Base, TimeoutMs)] private static partial Regex UrlExterna();
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase | Base, TimeoutMs)] private static partial Regex UrlSuelta();
    [GeneratedRegex(@"`[^`\n]*`", Base, TimeoutMs)] private static partial Regex InlineCode();
    [GeneratedRegex(@"`(?<c>[^`\n]*)`", Base, TimeoutMs)] private static partial Regex InlineCodeContenido();
    [GeneratedRegex(@"<[^>\n]+>", Base, TimeoutMs)] private static partial Regex EtiquetaHtml();
    [GeneratedRegex(@"\*\*|__|~~|==|(?<![\w])[*_]+|[*_]+(?![\w])", Base, TimeoutMs)] private static partial Regex Enfasis();
    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!|>~])", Base, TimeoutMs)] private static partial Regex Escape();
    [GeneratedRegex(@"^[ \t]{0,3}#{1,6}[ \t]+", Base, TimeoutMs)] private static partial Regex Encabezado();
    [GeneratedRegex(@"^[ \t]*(?:>[ \t]?)+(?:\[![^\]]*\][+\-]?[ \t]*)?", Base, TimeoutMs)] private static partial Regex Cita();
    [GeneratedRegex(@"^[ \t]*(?:[-*+]|\d+[.)])[ \t]+(?:\[[ xX]\][ \t]+)?", Base, TimeoutMs)] private static partial Regex MarcaDeLista();
    [GeneratedRegex(@"^(?:[-*+]|\d+[.)])[ \t]+", Base, TimeoutMs)] private static partial Regex ElementoDeLista();
    [GeneratedRegex(@"^[ \t]*\|?[ \t]*:?-{2,}:?[ \t]*(?:\|[ \t]*:?-{2,}:?[ \t]*)*\|?[ \t]*$", Base, TimeoutMs)] private static partial Regex SeparadorTabla();
    [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$", Base, TimeoutMs)] private static partial Regex SeparadorHorizontal();
    [GeneratedRegex(@"[ \t]+", Base, TimeoutMs)] private static partial Regex Espacios();
    [GeneratedRegex(@"[,\s]+", Base, TimeoutMs)] private static partial Regex SepararLista();
}
