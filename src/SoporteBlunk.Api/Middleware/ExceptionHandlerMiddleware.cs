using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Common.Exceptions;

namespace SoporteBlunk.Api.Middleware;

public sealed class ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger, IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // El cliente cerró la conexión: no es un error del servidor.
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        if (context.Response.HasStarted) throw ex;

        var problem = new ProblemDetails { Instance = context.Request.Path };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        switch (ex)
        {
            case ValidationException ve:
                problem.Status = StatusCodes.Status400BadRequest;
                problem.Title = "Parámetros inválidos";
                problem.Extensions["errors"] = ve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                break;
            case CredencialesInvalidasException ci:
                problem.Status = StatusCodes.Status401Unauthorized;
                problem.Title = "Credenciales incorrectas";
                problem.Detail = ci.Message;
                break;
            case AccesoDenegadoException ad:
                problem.Status = StatusCodes.Status403Forbidden;
                problem.Title = "Acceso denegado";
                problem.Detail = ad.Message;
                break;
            case DemasiadosIntentosException di:
                problem.Status = StatusCodes.Status429TooManyRequests;
                problem.Title = "Demasiados intentos";
                problem.Detail = di.Message;
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(di.Espera.TotalSeconds)).ToString();
                break;
            case ServicioNoDisponibleException sn:
                logger.LogWarning(sn, "Servicio de Seguridad no disponible");
                problem.Status = StatusCodes.Status503ServiceUnavailable;
                problem.Title = "Servicio de autenticación no disponible";
                break;
            case SubidaInvalidaException si:
                problem.Status = StatusCodes.Status400BadRequest;
                problem.Title = "Subida inválida";
                problem.Detail = si.Message;
                break;
            case KbNoDisponibleException kb:
                logger.LogWarning(kb, "Base de conocimiento no disponible");
                problem.Status = StatusCodes.Status503ServiceUnavailable;
                problem.Title = "Base de conocimiento no disponible";
                problem.Detail = kb.Message;
                break;
            case UtileriaNoDisponibleException ut:
                logger.LogWarning(ut, "Utilería no disponible");
                problem.Status = StatusCodes.Status503ServiceUnavailable;
                problem.Title = "Utilería no disponible";
                problem.Detail = ut.Message;
                break;
            case NotFoundException nf:
                problem.Status = StatusCodes.Status404NotFound;
                problem.Title = "No encontrado";
                problem.Detail = nf.Message;
                break;
            case SqlException sql when sql.Number == -2:
                logger.LogWarning(ex, "Timeout consultando ServiceDesk");
                problem.Status = StatusCodes.Status504GatewayTimeout;
                problem.Title = "La consulta a ServiceDesk tardó demasiado";
                break;
            case SqlException:
                logger.LogError(ex, "Error de base de datos ServiceDesk");
                problem.Status = StatusCodes.Status503ServiceUnavailable;
                problem.Title = "ServiceDesk no está disponible";
                break;
            default:
                logger.LogError(ex, "Error no controlado");
                problem.Status = StatusCodes.Status500InternalServerError;
                problem.Title = "Error interno";
                if (env.IsDevelopment()) problem.Detail = ex.Message;
                break;
        }

        context.Response.StatusCode = problem.Status.Value;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
