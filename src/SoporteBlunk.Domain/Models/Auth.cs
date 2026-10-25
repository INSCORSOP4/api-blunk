namespace SoporteBlunk.Domain.Models;

/// <summary>Usuario autenticado en la consola. Proviene de SEGURIDAD_PROSUR (CAT_USUARIOS).</summary>
public sealed record UsuarioSesion(Guid Id, string Nombre, string Email, Guid RolId);

/// <summary>Datos que devuelve el login del servicio central de Seguridad (sin contraseña ni token propio).</summary>
public sealed record UsuarioSeguridad(
    Guid Id,
    string Nombre,
    string Email,
    string Tipo,
    string Status,
    Guid RolId);
