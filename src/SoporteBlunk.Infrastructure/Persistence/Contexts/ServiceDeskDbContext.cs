using Microsoft.EntityFrameworkCore;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

/// <summary>ServiceDesk Plus (esquema de terceros, solo lectura). Nunca se le aplican migraciones.</summary>
internal sealed class ServiceDeskDbContext(DbContextOptions<ServiceDeskDbContext> options) : ReadOnlyDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceDeskDbContext).Assembly,
            t => t.Namespace?.EndsWith(".ServiceDesk", StringComparison.Ordinal) == true);
}
