using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

public class ContextosTests
{
    // Cadenas falsas: solo se construye el contexto, nunca se abre una conexión.
    private const string Falsa = "Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True";

    private static ServiceProvider Proveedor(bool kb, bool msdb)
    {
        var valores = new Dictionary<string, string?> { ["ConnectionStrings:ServiceDesk"] = Falsa };
        if (kb) valores["ConnectionStrings:SoporteKB"] = Falsa;
        if (msdb) valores["ConnectionStrings:Msdb"] = Falsa;
        valores["Seguridad:BaseUrl"] = "https://seguridad.local/";
        valores["Auth:RolSistemasId"] = Guid.NewGuid().ToString();
        valores["Auth:JwtKey"] = new string('k', 48);
        var config = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Con_cadenas_configuradas_se_resuelven_los_tres_contextos()
    {
        using var sp = Proveedor(kb: true, msdb: true);
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<ServiceDeskDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<KbDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<MsdbDbContext>().Should().NotBeNull();
    }

    [Fact]
    public void Los_contextos_de_bases_ajenas_no_hacen_seguimiento_de_cambios()
    {
        using var sp = Proveedor(kb: true, msdb: true);
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<ServiceDeskDbContext>()
            .ChangeTracker.QueryTrackingBehavior.Should().Be(QueryTrackingBehavior.NoTracking);
        scope.ServiceProvider.GetRequiredService<MsdbDbContext>()
            .ChangeTracker.QueryTrackingBehavior.Should().Be(QueryTrackingBehavior.NoTracking);
    }

    [Fact]
    public async Task Los_contextos_de_solo_lectura_rechazan_guardar_cambios()
    {
        using var sp = Proveedor(kb: true, msdb: true);
        using var scope = sp.CreateScope();
        var serviceDesk = scope.ServiceProvider.GetRequiredService<ServiceDeskDbContext>();
        var msdb = scope.ServiceProvider.GetRequiredService<MsdbDbContext>();

        await serviceDesk.Invoking(c => c.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*solo lectura*");
        msdb.Invoking(c => c.SaveChanges()).Should().Throw<InvalidOperationException>().WithMessage("*solo lectura*");
    }

    [Fact]
    public async Task Sin_SoporteKB_el_contexto_se_resuelve_y_abrir_la_conexion_falla_con_503()
    {
        using var sp = Proveedor(kb: false, msdb: true);
        using var scope = sp.CreateScope();

        var kb = scope.ServiceProvider.GetRequiredService<KbDbContext>(); // resolverlo no falla: los demás endpoints siguen funcionando
        await kb.Database.Invoking(d => d.OpenConnectionAsync()).Should().ThrowAsync<KbNoDisponibleException>().WithMessage("*no está configurada*");
    }

    [Fact]
    public async Task Sin_Msdb_el_contexto_se_resuelve_y_abrir_la_conexion_falla_con_503()
    {
        using var sp = Proveedor(kb: true, msdb: false);
        using var scope = sp.CreateScope();

        var msdb = scope.ServiceProvider.GetRequiredService<MsdbDbContext>();
        await msdb.Database.Invoking(d => d.OpenConnectionAsync()).Should().ThrowAsync<UtileriaNoDisponibleException>().WithMessage("*no está configurada*");
    }
}
