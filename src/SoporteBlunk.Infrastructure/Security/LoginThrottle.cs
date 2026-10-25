using Microsoft.Extensions.Options;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Options;

namespace SoporteBlunk.Infrastructure.Security;

/// <summary>
/// Cuenta fallos por clave (correo) en memoria. Tras N fallos bloquea esa clave unos minutos; el éxito reinicia el conteo.
/// En memoria basta para una sola instancia de la API; con varias instancias habría que moverlo a un almacén compartido.
/// </summary>
internal sealed class LoginThrottle(IOptions<AuthOptions> options, TimeProvider clock) : ILoginThrottle
{
    private sealed class Estado
    {
        public int Fallos;
        public DateTimeOffset VentanaHasta;
        public DateTimeOffset? BloqueadoHasta;
    }

    private readonly Dictionary<string, Estado> _estados = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public TimeSpan? ObtenerBloqueo(string clave)
    {
        lock (_lock)
        {
            Limpiar();
            if (_estados.TryGetValue(clave, out var e) && e.BloqueadoHasta is { } hasta)
            {
                var restante = hasta - clock.GetUtcNow();
                if (restante > TimeSpan.Zero) return restante;
            }
            return null;
        }
    }

    public void RegistrarFallo(string clave)
    {
        lock (_lock)
        {
            var ahora = clock.GetUtcNow();
            var ventana = TimeSpan.FromMinutes(options.Value.MinutosBloqueo);

            if (!_estados.TryGetValue(clave, out var e) || e.VentanaHasta <= ahora)
                _estados[clave] = e = new Estado { VentanaHasta = ahora + ventana };

            e.Fallos++;
            if (e.Fallos >= options.Value.MaxIntentosFallidos)
                e.BloqueadoHasta = ahora + ventana;
        }
    }

    public void Reiniciar(string clave)
    {
        lock (_lock) _estados.Remove(clave);
    }

    // Evita que el diccionario crezca sin límite con correos inventados.
    private void Limpiar()
    {
        if (_estados.Count < 1000) return;
        var ahora = clock.GetUtcNow();
        foreach (var k in _estados.Where(p => p.Value.VentanaHasta <= ahora).Select(p => p.Key).ToList())
            _estados.Remove(k);
    }
}
