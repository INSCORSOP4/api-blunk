using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Infrastructure.Persistence.Repositories;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

public class KbConnectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sin_cadena_de_conexion_responde_KbNoDisponible_sin_intentar_conectar(string? cadena)
    {
        await using var db = ContextoDePrueba.Kb(cadena);

        var act = () => new KbReadRepository(db).EstadoAsync(default);

        await act.Should().ThrowAsync<KbNoDisponibleException>().WithMessage("*no está configurada*");
    }

    [Fact]
    public async Task Servidor_inalcanzable_responde_KbNoDisponible_y_no_expone_la_cadena()
    {
        // Puerto cerrado en localhost: falla de inmediato sin depender de la red.
        await using var db = ContextoDePrueba.Kb("Server=127.0.0.1,1;Database=SoporteKB;User Id=x;Password=secreto-de-prueba;Connect Timeout=2;TrustServerCertificate=True");

        var act = () => new KbReadRepository(db).EstadoAsync(default);

        var ex = (await act.Should().ThrowAsync<KbNoDisponibleException>()).Which;
        ex.Message.Should().NotContain("secreto-de-prueba");
        // Tampoco debe quedar en la excepción interna que se registra en el log (EF no la incluye en sus mensajes).
        ex.ToString().Should().NotContain("secreto-de-prueba");
    }

    [SkippableFact]
    public async Task El_estado_se_lee_de_la_base_real_si_hay_cadena_configurada()
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__SoporteKB");
        Skip.If(string.IsNullOrWhiteSpace(cadena), "Falta ConnectionStrings__SoporteKB");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ServiceDesk"] = "Server=x;Database=x",
            ["ConnectionStrings:SoporteKB"] = cadena,
            ["Seguridad:BaseUrl"] = "https://seguridad.test/",
            ["Auth:RolSistemasId"] = Guid.NewGuid().ToString(),
            ["Auth:JwtKey"] = new string('k', 40),
        }).Build();
        using var proveedor = new ServiceCollection().AddInfrastructure(config).BuildServiceProvider();
        using var ambito = proveedor.CreateScope();

        var estado = await ambito.ServiceProvider.GetRequiredService<IKbReadRepository>().EstadoAsync(default);

        estado.VersionEsquema.Should().BeGreaterThanOrEqualTo(1);
        estado.Documentos.Should().BeGreaterThanOrEqualTo(0);
    }
}
