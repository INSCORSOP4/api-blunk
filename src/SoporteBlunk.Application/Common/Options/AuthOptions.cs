namespace SoporteBlunk.Application.Common.Options;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    /// <summary>ID del rol (ROL-SISTEMAS en CAT_USUARIOS) autorizado para entrar a la consola.</summary>
    public Guid RolSistemasId { get; set; }

    /// <summary>Máximo de intentos fallidos por correo antes de bloquear temporalmente.</summary>
    public int MaxIntentosFallidos { get; set; } = 5;

    public int MinutosBloqueo { get; set; } = 15;

    public int HorasSesion { get; set; } = 8;
}
