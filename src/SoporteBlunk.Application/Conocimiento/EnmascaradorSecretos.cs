using System.Text;
using System.Text.RegularExpressions;

namespace SoporteBlunk.Application.Conocimiento;

public sealed record ResultadoEnmascarado(string Texto, int Cantidad, IReadOnlyList<int> Lineas);

/// <summary>
/// Oculta contraseñas, tokens y llaves antes de guardar una nota. Es conservador: ante la duda enmascara,
/// pero deja intactos los marcadores obvios (&lt;tu contraseña&gt;, ${VAR}, "string", "***") y las referencias de código
/// (request.Password), para no destruir la documentación.
/// NO es infalible: no detecta un secreto escrito en prosa ("la clave de root es ..."). Por eso cada nota registra
/// los números de línea enmascaradas, para que una persona pueda revisarlas.
/// Los valores enmascarados nunca se registran ni se devuelven; solo cuántos hubo y en qué línea.
/// </summary>
public static class EnmascaradorSecretos
{
    public const string Marca = "[OCULTO]";

    // Tiempo máximo por búsqueda: si una expresión se atasca, falla la nota (no se guarda sin enmascarar).
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(2);
    private const RegexOptions Opc = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // Palabras que delatan un secreto aunque vayan dentro de un nombre (DB_PASSWORD, clientSecret, accessToken...).
    private const string Fuertes =
        @"password|passwd|passphrase|pwd|secret|api[_\-\s]?key|apikey|access[_\-\s]?key|private[_\-\s]?key|token|credential|contrase[ñn]a|connectionstring";

    // Palabras ambiguas: solo cuentan si son el nombre completo ("pass", pero no "bypass" ni "passenger").
    private const string Debiles = @"pass|passcode|clave|pin";

    private const string Clave = @"(?:[\w.\-]*(?:" + Fuertes + @")[\w.\-]*|\b(?:" + Debiles + @")\b)";

    // clave [**] [comillas] (:|=|=>|:=) [**] valor   (valor entre comillas o sin ellas)
    private static readonly Regex Asignacion = new(
        @"(?<pre>(?<![\w.\-])(?<clave>" + Clave + @")[*_]{0,2}[""'`]?[ \t]*(?:=>|:=|[:=])[ \t*_]*)" +
        @"(?:""(?<q1>[^""\r\n]{1,300})""|'(?<q2>[^'\r\n]{1,300})'|`(?<q3>[^`\r\n]{1,300})`|(?<v>[^\s;,|""'`<>(){}\[\]]{1,300}))",
        Opc, Limite);

    // | password | valor |
    private static readonly Regex FilaTabla = new(
        @"^(?<ini>[ \t]*\|[ \t]*(?:\*\*)?[^|\r\n]*?(?:" + Clave + @")[^|\r\n]*?(?:\*\*)?[ \t]*\|[ \t]*)(?<val>[^|\r\n]+?)(?<fin>[ \t]*(?:\||$))",
        Opc | RegexOptions.Multiline, Limite);

    private static readonly Regex UrlConCredenciales = new(
        @"(?<ini>\b[a-z][a-z0-9+.\-]*://[^\s:/@""'<>]+:)(?<pw>[^\s@/""'<>]+)(?<fin>@)", Opc, Limite);

    private static readonly Regex Bearer = new(@"(?<ini>\b(?:Bearer|Basic)[ \t]+)(?<tok>[A-Za-z0-9._~+/=\-]{12,})", Opc, Limite);

    private static readonly Regex BloqueLlavePrivada = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", Opc, Limite);

    // Formatos de token con prefijo propio (se ocultan completos).
    private static readonly Regex[] TokensConocidos =
    [
        new(@"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]*", Opc, Limite),    // JWT
        new(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.CultureInvariant | RegexOptions.Compiled, Limite), // AWS
        new(@"\bgh[pousr]_[A-Za-z0-9]{30,}\b", Opc, Limite),                                   // GitHub
        new(@"\bxox[abprs]-[A-Za-z0-9\-]{10,}", Opc, Limite),                                  // Slack
        new(@"\bsk-[A-Za-z0-9_\-]{20,}", Opc, Limite),                                         // llaves tipo sk-...
        new(@"\bAIza[0-9A-Za-z_\-]{35}\b", RegexOptions.CultureInvariant | RegexOptions.Compiled, Limite), // Google
    ];

    // Valores que son claramente marcadores, tipos o referencias, no secretos.
    private static readonly Regex Marcador = new(
        @"^(?:<.*>|\{.*\}|\$\{.*\}|\$\(.*\)|%.*%|\[.*\]|\*+|x{3,}|•+|#+|\.{3}|…|-+|_+|null|none|nil|n/?a|true|false|" +
        @"string|int|integer|long|bool|boolean|object|number|decimal|float|varchar.*|nvarchar.*|char.*|text|guid|uuid|date.*|" +
        @"requerid[oa]|opcional|obligatori[oa]|required|optional|secret|secreto|password|passwd|pwd|pass|contrase[ñn]a|token|clave|" +
        @"changeme|example|ejemplo|(?:tu|su|mi|your|my)[_\- ].*)$",
        Opc, Limite);

    // request.Password, config.ApiToken, process.env.DB_PASSWORD: referencias de código, no valores.
    // Solo cuenta como referencia si el último tramo NOMBRA un secreto (o es process.env/os.environ):
    // así "Soporte.Blunk" (una contraseña con punto) sigue ocultándose.
    private static readonly Regex ReferenciaDeCodigo = new(
        @"^(?:(?:process\.env|os\.environ|env|Environment)\.[A-Za-z_]\w*|[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\.[A-Za-z_]*(?:password|passwd|pwd|pass|secret|token|apikey|api_key|key|credential|clave|contrase[ñn]a)\w*)$",
        Opc, Limite);

    private static readonly HashSet<string> Conectores = new(StringComparer.OrdinalIgnoreCase)
        { "de", "del", "el", "la", "los", "las", "se", "que", "un", "una", "es", "en", "y", "o", "para", "por", "con", "al", "su", "no", "si" };

    public static ResultadoEnmascarado Enmascarar(string texto)
    {
        var lineas = new SortedSet<int>();
        var cantidad = 0;

        string Pasada(string actual, Regex rx, Func<Match, string?> reemplazo)
        {
            var inicios = InicioDeLineas(actual);
            StringBuilder? sb = null;
            var ultimo = 0;
            foreach (Match m in rx.Matches(actual))
            {
                var r = reemplazo(m);
                if (r is null) continue;
                sb ??= new StringBuilder(actual.Length);
                sb.Append(actual, ultimo, m.Index - ultimo).Append(r);
                ultimo = m.Index + m.Length;
                cantidad++;
                lineas.Add(LineaDe(inicios, m.Index));
            }
            if (sb is null) return actual;
            sb.Append(actual, ultimo, actual.Length - ultimo);
            return sb.ToString();
        }

        var t = texto;

        // 1) Formatos que se reconocen solos.
        t = Pasada(t, BloqueLlavePrivada, m => Marca + new string('\n', m.Value.Count(c => c == '\n'))); // conserva las líneas
        foreach (var rx in TokensConocidos) t = Pasada(t, rx, _ => Marca);
        t = Pasada(t, Bearer, m => EsMarcador(m.Groups["tok"].Value, sinLongitudMinima: true) ? null : m.Groups["ini"].Value + Marca);

        // 2) Contraseña dentro de una URL (postgres://usuario:CLAVE@host).
        t = Pasada(t, UrlConCredenciales, m =>
            EsMarcador(m.Groups["pw"].Value, sinLongitudMinima: true) ? null : m.Groups["ini"].Value + Marca + m.Groups["fin"].Value);

        // 3) clave = valor / clave: valor / "clave": "valor".
        t = Pasada(t, Asignacion, m =>
        {
            var pre = m.Groups["pre"].Value;
            foreach (var (grupo, comilla) in new[] { ("q1", "\""), ("q2", "'"), ("q3", "`") })
            {
                var g = m.Groups[grupo];
                if (!g.Success) continue;
                return EsMarcador(g.Value, sinLongitudMinima: true) ? null : pre + comilla + Marca + comilla;
            }
            var v = m.Groups["v"].Value;
            if (v.Length < 3 || Conectores.Contains(v) || EsMarcador(v, sinLongitudMinima: false, permitirReferencia: true)) return null;
            return pre + Marca;
        });

        // 4) Tablas Markdown: | password | valor |  (solo valores sin espacios; con espacios es una descripción).
        t = Pasada(t, FilaTabla, m =>
        {
            var val = m.Groups["val"].Value.Trim().Trim('`').Trim();
            if (val.Length == 0 || val.Contains(' ') || val.All(c => c is '-' or ':') || EsMarcador(val, sinLongitudMinima: true)) return null;
            return m.Groups["ini"].Value + Marca + m.Groups["fin"].Value;
        });

        return new ResultadoEnmascarado(t, cantidad, lineas.ToList());
    }

    // permitirReferencia: solo para valores SIN comillas; un literal entre comillas se trata siempre como valor.
    private static bool EsMarcador(string valor, bool sinLongitudMinima, bool permitirReferencia = false)
    {
        var v = valor.Trim();
        if (v.Length == 0) return true;
        if (!sinLongitudMinima && v.Length < 3) return true;
        return Marcador.IsMatch(v) || (permitirReferencia && ReferenciaDeCodigo.IsMatch(v));
    }

    private static List<int> InicioDeLineas(string s)
    {
        var inicios = new List<int> { 0 };
        for (var i = 0; i < s.Length; i++)
            if (s[i] == '\n') inicios.Add(i + 1);
        return inicios;
    }

    private static int LineaDe(List<int> inicios, int indice)
    {
        var pos = inicios.BinarySearch(indice);
        return (pos >= 0 ? pos : ~pos - 1) + 1;
    }
}
