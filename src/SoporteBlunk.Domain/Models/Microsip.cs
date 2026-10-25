namespace SoporteBlunk.Domain.Models;

/// <summary>Usuario de MICROSIP (USUARIOS tipo 'U') con sus datos adicionales (clave de FORTIA, empresa, puesto).</summary>
public sealed record UsuarioMicrosip(
    int UsuarioId,
    string Nombre,
    string Estatus,
    bool Activo,
    string? ClaveFortia,
    string? NombreUsuario,
    string? Empresa,
    string? Puesto,
    bool TieneDatosAdicionales);

/// <summary>Trabajador en FORTIA (RH_TRAB) con su empresa y puesto.</summary>
public sealed record TrabajadorFortia(int Clave, string NombreCompleto, string Empresa, string Puesto);

/// <summary>Lo que se escribe en USUARIOS_DATOS_ADICIONALES. Los límites son los de las columnas de MICROSIP.</summary>
public sealed record DatosAdicionales(string ClaveFortia, string NombreUsuario, string Empresa, string Puesto)
{
    public const int MaxClave = 10;
    public const int MaxNombre = 100;
    public const int MaxEmpresa = 50;
    public const int MaxPuesto = 100;
}

public enum AccionMicrosip { Insertar, Actualizar }

public enum ResultadoCambio { Pendiente, Aplicado, Fallido, NoVerificado }

public sealed record CambioMicrosip(
    long Id,
    DateTimeOffset Fecha,
    string UsuarioEmail,
    string UsuarioNombre,
    int MicrosipUsuarioId,
    string MicrosipNombre,
    AccionMicrosip Accion,
    ResultadoCambio Resultado,
    string? Antes,
    string Despues,
    string? Error);
