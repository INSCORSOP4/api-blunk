using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Infrastructure.Persistence.Contexts;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>Crea los contextos con la misma configuración que usa la API.</summary>
internal static class ContextoDePrueba
{
    public static ServiceDeskDbContext ServiceDesk(string cadena = "Server=x;Database=x")
    {
        var opciones = new DbContextOptionsBuilder<ServiceDeskDbContext>();
        ServiceDeskDbContext.Configurar(opciones, cadena);
        return new ServiceDeskDbContext(opciones.Options);
    }

    /// <summary>SoporteKB con la misma configuración que la API. Sin cadena (null) queda "no configurada".</summary>
    public static KbDbContext Kb(string? cadena)
    {
        var opciones = new DbContextOptionsBuilder<KbDbContext>();
        KbDbContext.Configurar(opciones, cadena);
        return new KbDbContext(opciones.Options);
    }
}
