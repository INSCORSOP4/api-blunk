using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Domain.Entities.ServiceDesk;
using SoporteBlunk.Infrastructure.Persistence.Sql;

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
    public DbSet<WorkOrder> Tickets => Set<WorkOrder>();
    public DbSet<WorkOrderStates> TicketEstados => Set<WorkOrderStates>();
    public DbSet<WorkOrderQueue> TicketGrupos => Set<WorkOrderQueue>();
    public DbSet<WorkOrderToDescription> TicketDescripciones => Set<WorkOrderToDescription>();
    public DbSet<RequestResolution> TicketResoluciones => Set<RequestResolution>();
    public DbSet<WorkOrderHistory> TicketHistorial => Set<WorkOrderHistory>();
    public DbSet<WoAssessment> TicketEvaluaciones => Set<WoAssessment>();
    public DbSet<WoStatusInfo> TiempoPorEstado => Set<WoStatusInfo>();
    public DbSet<WoTechInfo> TiempoPorTecnico => Set<WoTechInfo>();
    public DbSet<WoGroupInfo> TiempoPorGrupo => Set<WoGroupInfo>();
    public DbSet<AaaUser> Usuarios => Set<AaaUser>();

    /// <summary>
    /// Configuración única del contexto (la usan la API y los tests). Sin seguimiento de cambios, con el mismo timeout que
    /// tenía Dapper y con semántica de nulos de SQL (<c>NULL = x</c> no es verdadero), para que los filtros se comporten
    /// igual que el SQL anterior y no agreguen comprobaciones de IS NULL.
    /// </summary>
    internal static void Configurar(DbContextOptionsBuilder opciones, string cadena) => opciones
        .UseSqlServer(cadena, sql => sql.CommandTimeout(TicketSql.CommandTimeoutSeconds).UseRelationalNulls())
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceDeskDbContext).Assembly,
            t => t.Namespace?.EndsWith(".ServiceDesk", StringComparison.Ordinal) == true);
}
