using Serilog.Context;

namespace SoporteBlunk.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers[HeaderName].FirstOrDefault();
        // Solo se acepta un id corto y alfanumérico; si no, se genera uno (evita inyectar basura en los logs).
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !id.All(c => char.IsLetterOrDigit(c) || c == '-'))
            id = Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = id;
        using (LogContext.PushProperty("CorrelationId", id))
            await next(context);
    }
}
