using Microsoft.EntityFrameworkCore;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

/// <summary>SoporteKB: base propia (Conocimiento, Bitácora, Auditoría, Archivos), con escritura.</summary>
internal sealed class KbDbContext(DbContextOptions<KbDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KbDbContext).Assembly,
            t => t.Namespace?.EndsWith(".Kb", StringComparison.Ordinal) == true);
}
