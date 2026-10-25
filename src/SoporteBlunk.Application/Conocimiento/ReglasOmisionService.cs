using System.Text;

namespace SoporteBlunk.Application.Conocimiento;

/// <summary>Decide si una ruta debe omitirse según las reglas (carpeta o archivo, sin distinguir mayúsculas).</summary>
public sealed class ReglasOmisionService(IEnumerable<ReglaOmision> reglas)
{
    private readonly HashSet<string> _carpetas = Conjunto(reglas, ReglaOmision.Carpeta);
    private readonly HashSet<string> _archivos = Conjunto(reglas, ReglaOmision.Archivo);

    /// <returns>El motivo si debe omitirse; null si se puede procesar.</returns>
    public string? MotivoDeOmision(string rutaNormalizada)
    {
        var segmentos = rutaNormalizada.Split('/');
        for (var i = 0; i < segmentos.Length - 1; i++)
            if (_carpetas.Contains(Clave(segmentos[i]))) return $"Carpeta omitida por regla: {segmentos[i]}";

        var archivo = segmentos[^1];
        if (_archivos.Contains(Clave(archivo))) return $"Archivo omitido por regla: {archivo}";
        return null;
    }

    private static HashSet<string> Conjunto(IEnumerable<ReglaOmision> reglas, string tipo) =>
        reglas.Where(r => string.Equals(r.Tipo, tipo, StringComparison.OrdinalIgnoreCase))
              .Select(r => Clave(r.Patron))
              .ToHashSet(StringComparer.Ordinal);

    // Mayúsculas ignoradas y forma Unicode normalizada: "Contraseña" == "contraseña" aunque vengan descompuestas.
    private static string Clave(string s) => s.Normalize(NormalizationForm.FormC).Trim().ToUpperInvariant();
}
