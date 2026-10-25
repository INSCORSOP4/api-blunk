using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Domain.Entities.ServiceDesk;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

/// <summary>ServiceDesk Plus (esquema de terceros, solo lectura). Nunca se le aplican migraciones.</summary>
internal sealed class ServiceDeskDbContext(DbContextOptions<ServiceDeskDbContext> options) : ReadOnlyDbContext(options)
{
    public DbSet<StatusDefinition> Estados => Set<StatusDefinition>();
    public DbSet<PriorityDefinition> Prioridades => Set<PriorityDefinition>();
    public DbSet<QueueDefinition> Grupos => Set<QueueDefinition>();
    public DbSet<CategoryDefinition> Categorias => Set<CategoryDefinition>();
    public DbSet<SubCategoryDefinition> Subcategorias => Set<SubCategoryDefinition>();
    public DbSet<ItemDefinition> Items => Set<ItemDefinition>();
    public DbSet<UrgencyDefinition> Urgencias => Set<UrgencyDefinition>();
    public DbSet<ImpactDefinition> Impactos => Set<ImpactDefinition>();
    public DbSet<ModeDefinition> Modos => Set<ModeDefinition>();
    public DbSet<RequestTypeDefinition> TiposSolicitud => Set<RequestTypeDefinition>();
    public DbSet<LevelDefinition> Niveles => Set<LevelDefinition>();
    public DbSet<DepartmentDefinition> Departamentos => Set<DepartmentDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceDeskDbContext).Assembly,
            t => t.Namespace?.EndsWith(".ServiceDesk", StringComparison.Ordinal) == true);
}
