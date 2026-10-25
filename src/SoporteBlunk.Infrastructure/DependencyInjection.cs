using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Infrastructure.Exports;
using SoporteBlunk.Infrastructure.Persistence;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Kb;
using SoporteBlunk.Infrastructure.Persistence.Msdb;
using SoporteBlunk.Infrastructure.Persistence.Repositories;
using SoporteBlunk.Infrastructure.Security;

namespace SoporteBlunk.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionName = "ServiceDesk";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Falta la cadena de conexión 'ConnectionStrings:{ConnectionName}' (variable de entorno ConnectionStrings__{ConnectionName}).");

        services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(connectionString));
        services.AddScoped<ICatalogoReadRepository, CatalogoReadRepository>();
        services.AddScoped<ITicketReadRepository, TicketReadRepository>();
        services.AddScoped<IDashboardReadRepository, DashboardReadRepository>();
        services.AddScoped<ITecnicoReadRepository, TecnicoReadRepository>();
        services.AddSingleton<ITicketExcelExporter, TicketExcelExporter>();
        // Base de conocimiento (propia, con escritura). Si no está configurada, sus endpoints responden 503.
        services.AddSingleton<IKbConnectionFactory>(new KbConnectionFactory(configuration.GetConnectionString("SoporteKB")));
        services.AddScoped<IKbReadRepository, KbReadRepository>();
        services.AddScoped<IKbImportacionRepository, KbImportacionRepository>();
        services.AddOptions<ConocimientoOptions>().Bind(configuration.GetSection(ConocimientoOptions.Section));
        // Utilerías: jobs de SQL Server Agent (msdb de otro servidor, solo lectura). Sin cadena, su endpoint responde 503.
        services.AddSingleton<IMsdbConnectionFactory>(new MsdbConnectionFactory(configuration.GetConnectionString("Msdb")));
        services.AddScoped<IJobsReadRepository, JobsReadRepository>();
        services.AddOptions<UtileriasOptions>().Bind(configuration.GetSection(UtileriasOptions.Section))
            .Validate(o => o.Microsip.EsValida(),
                "Utilerias:Microsip: los nombres de linked server y base solo pueden tener letras, números y guion bajo.")
            .ValidateOnStart();
        // Usuarios MICROSIP + FORTIA (mismo servidor que los jobs). La cadena de escritura es opcional.
        services.AddSingleton<IMicrosipConnections>(new MicrosipConnections(
            configuration.GetConnectionString("Microsip"), configuration.GetConnectionString("MicrosipEscritura")));
        services.AddScoped<IMicrosipRepository, MicrosipRepository>();
        services.AddScoped<IFortiaRepository, FortiaRepository>();
        services.AddScoped<IAuditoriaMicrosipRepository, AuditoriaMicrosipRepository>();
        // Bitácora de actividades (reemplazo del Excel): vive en SoporteKB, igual que Conocimiento.
        services.AddScoped<IBitacoraRepository, BitacoraRepository>();
        // Archivos de confianza (SIA/repbi/olap): un script externo en el 10.0.3.8 llena esta
        // tabla en SoporteKB; aquí solo se lee.
        services.AddScoped<IArchivosReadRepository, ArchivosReadRepository>();
        AddContextos(services, configuration, connectionString);
        AddAuth(services, configuration);
        return services;
    }

    /// <summary>
    /// Un DbContext por base. Los de bases ajenas (ServiceDesk, msdb) son de solo lectura y sin seguimiento de cambios.
    /// Sin cadena configurada, los contextos opcionales (SoporteKB, msdb) se crean igual, pero abrir su conexión lanza la misma
    /// excepción que hoy produce su fábrica de conexiones (la API arranca y solo ese endpoint responde 503).
    /// </summary>
    private static void AddContextos(IServiceCollection services, IConfiguration configuration, string serviceDesk)
    {
        services.AddDbContext<ServiceDeskDbContext>(o => ServiceDeskDbContext.Configurar(o, serviceDesk));

        var kb = configuration.GetConnectionString("SoporteKB");
        services.AddDbContext<KbDbContext>(o => KbDbContext.Configurar(o, kb));

        var msdb = configuration.GetConnectionString("Msdb");
        services.AddDbContext<MsdbDbContext>(o =>
        {
            if (string.IsNullOrWhiteSpace(msdb))
                o.UseSqlServer("Server=.;Database=msdb")
                    .AddInterceptors(new ConexionNoConfiguradaInterceptor(() =>
                        new UtileriaNoDisponibleException("La consulta de jobs no está configurada (ConnectionStrings:Msdb).")));
            else
                o.UseSqlServer(msdb).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
    }

    private static void AddAuth(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.Section))
            .Validate(o => o.RolSistemasId != Guid.Empty, "Auth:RolSistemasId es obligatorio.")
            .Validate(o => o.MaxIntentosFallidos > 0 && o.MinutosBloqueo > 0 && o.HorasSesion is > 0 and <= 24,
                "Auth: valores fuera de rango.")
            .ValidateOnStart();

        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section))
            .Validate(o => o.JwtKey.Length >= 32,
                "Auth:JwtKey es obligatoria y debe tener al menos 32 caracteres (variable de entorno Auth__JwtKey o user-secrets).")
            .ValidateOnStart();

        var baseUrl = configuration["Seguridad:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Falta 'Seguridad:BaseUrl' (URL base de apiseguridad).");
        // Las credenciales viajan hacia Seguridad: solo HTTPS, salvo desarrollo local.
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new InvalidOperationException("'Seguridad:BaseUrl' debe usar HTTPS (HTTP solo se permite en localhost).");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILoginThrottle, LoginThrottle>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddHttpClient<ISeguridadClient, SeguridadClient>(c =>
        {
            c.BaseAddress = uri;
            c.Timeout = TimeSpan.FromSeconds(10);
        });
    }
}
