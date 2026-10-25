using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Infrastructure.Security;

/// <summary>
/// Cliente de <c>POST api/Seguridad/Login</c> del servicio apiseguridad. Ese servicio responde 200 con el usuario
/// o 400 con un texto; no distingue errores de credenciales de errores internos, así que solo el mensaje
/// "No se encontró el usuario…" se trata como credenciales incorrectas y todo lo demás como servicio no disponible.
/// </summary>
internal sealed class SeguridadClient(HttpClient http) : ISeguridadClient
{
    internal const string LoginPath = "api/Seguridad/Login";
    // Sin política de nombres: el contrato de Seguridad usa USER y PASSWORD exactamente así.
    private static readonly JsonSerializerOptions ContratoSeguridad = new() { PropertyNamingPolicy = null };

    private const string MensajeUsuarioNoEncontrado = "No se encontró el usuario";

    public async Task<UsuarioSeguridad?> LoginAsync(string email, string password, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(LoginPath, new { USER = email, PASSWORD = password }, ContratoSeguridad, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ServicioNoDisponibleException("No se pudo contactar al servicio de Seguridad.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var texto = await response.Content.ReadAsStringAsync(ct);
                if (texto.Contains(MensajeUsuarioNoEncontrado, StringComparison.OrdinalIgnoreCase)) return null;
                throw new ServicioNoDisponibleException("El servicio de Seguridad rechazó la solicitud.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                throw new ServicioNoDisponibleException($"El servicio de Seguridad respondió {(int)response.StatusCode}.");

            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                return Mapear(doc.RootElement);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException)
            {
                throw new ServicioNoDisponibleException("La respuesta del servicio de Seguridad no tiene el formato esperado.", ex);
            }
        }
    }

    // Seguridad serializa con nombres como USUARIO_ID; según su configuración pueden llegar en camelCase.
    // Se comparan ignorando mayúsculas y guiones bajos para no depender de eso.
    private static UsuarioSeguridad Mapear(JsonElement raiz)
    {
        string Texto(string nombre) => Buscar(raiz, nombre).GetString() ?? throw new FormatException(nombre);
        Guid Id(string nombre) => Guid.Parse(Texto(nombre));

        return new UsuarioSeguridad(
            Id("USUARIO_ID"),
            Texto("NOMBRE"),
            Texto("EMAIL"),
            Texto("TIPO"),
            Texto("STATUS"),
            Id("ID_ROL"));
    }

    private static JsonElement Buscar(JsonElement raiz, string nombre)
    {
        static string Norm(string s) => s.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        var objetivo = Norm(nombre);
        foreach (var prop in raiz.EnumerateObject())
            if (Norm(prop.Name) == objetivo) return prop.Value;
        throw new KeyNotFoundException(nombre);
    }
}
