using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using SoporteBlunk.Api.Middleware;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SoporteBlunk.Api.Services;
using SoporteBlunk.Application;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Infrastructure;
using SoporteBlunk.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/soporte-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUsuarioActualProvider, UsuarioActualProvider>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Errores de binding (p. ej. fecha mal formada) con el mismo formato ProblemDetails que el resto.
builder.Services.Configure<ApiBehaviorOptions>(o => o.InvalidModelStateResponseFactory = ctx =>
{
    var problem = new ValidationProblemDetails(ctx.ModelState) { Status = StatusCodes.Status400BadRequest, Title = "Parámetros inválidos" };
    problem.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
});

builder.Services.AddApiVersioning(o =>
{
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = false;
    o.ReportApiVersions = true;
    o.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddMvc().AddApiExplorer(o =>
{
    o.GroupNameFormat = "'v'VVV";
    o.SubstituteApiVersionInUrl = true;
});

// El futuro frontend vive en otro origen: se permite solo lo configurado en Cors:Origins.
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Length > 0) p.WithOrigins(origins).WithMethods("GET", "OPTIONS").AllowAnyHeader().WithExposedHeaders("X-Export-Truncated", "Content-Disposition");
}));

builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Token devuelto por POST /v1/auth/login",
    });
    o.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", doc)] = [],
    });
});

// Autenticación: JWT propio emitido por /v1/auth/login. Toda la API exige sesión por defecto (política de respaldo).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Value.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.Value.BuildKey(),
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
        };
    });
builder.Services.AddOptions<AuthorizationOptions>()
    .Configure<IOptions<AuthOptions>>((authz, auth) =>
        authz.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimNames.RolId, auth.Value.RolSistemasId.ToString())
            .Build());
builder.Services.AddAuthorization();

// Tope global de intentos de login contra la API (además del bloqueo por correo del caso de uso).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Las subidas son pesadas (analizan y guardan cientos de notas): pocas por minuto.
    o.AddFixedWindowLimiter("importar", w =>
    {
        w.PermitLimit = 10;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
    // Cambios en MICROSIP: una persona no necesita más de unos cuantos por minuto.
    o.AddFixedWindowLimiter("escritura", w =>
    {
        w.PermitLimit = 20;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
    o.AddFixedWindowLimiter("login", w =>
    {
        w.PermitLimit = 30;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
});
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
// El registro de peticiones va ANTES del manejador de errores: así ve el código final (401, 403, 404…) y no
// marca como "500" las excepciones esperadas que el manejador convierte en respuestas de negocio.
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Response.StatusCode >= 400 ? Serilog.Events.LogEventLevel.Warning
    : Serilog.Events.LogEventLevel.Information);
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseMiddleware<ReadOnlyMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
