using Microsoft.EntityFrameworkCore;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

/// <summary>msdb del servidor de los jobs (solo lectura). Nunca se le aplican migraciones.</summary>
internal sealed class MsdbDbContext(DbContextOptions<MsdbDbContext> options) : ReadOnlyDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MsdbDbContext).Assembly,
            t => t.Namespace?.EndsWith(".Msdb", StringComparison.Ordinal) == true);
}
