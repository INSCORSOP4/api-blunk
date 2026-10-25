using System.Text.RegularExpressions;

namespace SoporteBlunk.Application.Common.Options;

/// <summary>Configuración de las utilerías de Soporte.</summary>
public sealed class UtileriasOptions
{
    public const string Section = "Utilerias";

    /// <summary>Jobs de SQL Server Agent que el equipo vigila de forma habitual (nombres exactos).</summary>
    public List<string> JobsVigilados { get; set; } = [];

    public MicrosipOptions Microsip { get; set; } = new();
}

/// <summary>
/// Nombres de los linked servers que usa la utilería de usuarios MICROSIP. Se concatenan en SQL (no se pueden
/// parametrizar), por eso se validan al arrancar: solo letras, números y guion bajo.
/// </summary>
public sealed partial class MicrosipOptions
{
    /// <summary>Linked server (ODBC/Firebird) hacia la base de configuración de MICROSIP.</summary>
    public string ConfigLinkedServer { get; set; } = "MICROSIP_CONFIG";

    /// <summary>Linked server hacia el SQL Server de FORTIA (RH).</summary>
    public string FortiaLinkedServer { get; set; } = "FORTIA";

    public string FortiaBaseDatos { get; set; } = "PROSUR_PRIME";

    public static bool EsIdentificadorSeguro(string? valor) => !string.IsNullOrEmpty(valor) && IdentificadorRegex().IsMatch(valor);

    public bool EsValida() =>
        EsIdentificadorSeguro(ConfigLinkedServer) && EsIdentificadorSeguro(FortiaLinkedServer) && EsIdentificadorSeguro(FortiaBaseDatos);

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex IdentificadorRegex();
}
