using System.Text.RegularExpressions;

namespace SoporteBlunk.Api.Middleware;

/// <summary>
/// Defensa en profundidad: esta API es de solo lectura. Cualquier método que no sea GET/HEAD/OPTIONS se rechaza,
/// salvo las rutas de escritura que se autorizan explícitamente aquí.
/// </summary>
public sealed partial class ReadOnlyMiddleware(RequestDelegate next)
{
    private sealed record Regla(string Metodo, Regex Ruta);

    // Cada escritura que existe en la API tiene que estar listada aquí, explícitamente.
    private static readonly Regla[] Reglas =
    [
        new("POST", ExactaRegex("/v1/auth/login")),
        new("POST", ExactaRegex("/v1/conocimiento/importaciones")),
        new("POST", ExactaRegex("/v1/bitacora/actividades")),
        new("PUT", PatronRegex(@"/v1/utilerias/microsip/usuarios/(\d{1,9})/datos-adicionales")),
        new("PUT", PatronRegex(@"/v1/bitacora/actividades/(\d{1,9})")),
        new("PUT", PatronRegex(@"/v1/bitacora/checklist/(\d{1,9})")),
    ];

    public Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return next(context);

        var ruta = context.Request.Path.Value?.TrimEnd('/') ?? "";
        if (Reglas.Any(r => string.Equals(r.Metodo, method, StringComparison.OrdinalIgnoreCase) && r.Ruta.IsMatch(ruta)))
            return next(context);

        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.Headers.Allow = "GET, HEAD, OPTIONS";
        return Task.CompletedTask;
    }

    private static Regex ExactaRegex(string ruta) =>
        new($"^{Regex.Escape(ruta)}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static Regex PatronRegex(string patron) =>
        new($"^{patron}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
