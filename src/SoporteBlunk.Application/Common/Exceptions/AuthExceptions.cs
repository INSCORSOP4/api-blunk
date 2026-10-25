namespace SoporteBlunk.Application.Common.Exceptions;

public sealed class CredencialesInvalidasException()
    : Exception("Correo o contraseña incorrectos.");

/// <summary>Credenciales correctas, pero el usuario no puede usar esta consola (rol, tipo o estado).</summary>
public sealed class AccesoDenegadoException()
    : Exception("Tu usuario no tiene acceso a esta consola.");

public sealed class DemasiadosIntentosException(TimeSpan espera)
    : Exception("Demasiados intentos fallidos. Inténtalo más tarde.")
{
    public TimeSpan Espera { get; } = espera;
}

public sealed class ServicioNoDisponibleException(string mensaje, Exception? inner = null)
    : Exception(mensaje, inner);

/// <summary>La subida de archivos no es válida (zip corrupto, demasiados archivos...). Se responde 400.</summary>
public sealed class SubidaInvalidaException(string mensaje) : Exception(mensaje);

/// <summary>Una utilería (p. ej. los jobs de SQL, que leen msdb) no está configurada o su servidor no responde.</summary>
public sealed class UtileriaNoDisponibleException(string mensaje, Exception? inner = null)
    : Exception(mensaje, inner);

/// <summary>La base de conocimiento (SoporteKB) no está configurada o no responde.</summary>
public sealed class KbNoDisponibleException(string mensaje, Exception? inner = null)
    : Exception(mensaje, inner);
