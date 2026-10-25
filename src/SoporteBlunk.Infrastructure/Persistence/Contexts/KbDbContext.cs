using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Entities.Kb;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

/// <summary>SoporteKB: base propia (Conocimiento, Bitácora, Auditoría, Archivos), con escritura.</summary>
internal sealed class KbDbContext(DbContextOptions<KbDbContext> options) : DbContext(options)
{
    public DbSet<BitacoraActividad> BitacoraActividades => Set<BitacoraActividad>();
    public DbSet<BitacoraChecklist> BitacoraChecklist => Set<BitacoraChecklist>();
    public DbSet<AuditoriaCambioMicrosip> CambiosMicrosip => Set<AuditoriaCambioMicrosip>();
    public DbSet<ArchivoEsperado> ArchivosEsperados => Set<ArchivoEsperado>();
    public DbSet<RevisionArchivo> RevisionesArchivo => Set<RevisionArchivo>();

    /// <summary>
    /// Configuración única del contexto (la usan la API y los tests). Sin cadena, el contexto se crea pero abrir la conexión lanza
    /// <see cref="KbNoDisponibleException"/>, como la fábrica de conexiones anterior.
    /// </summary>
    internal static void Configurar(DbContextOptionsBuilder opciones, string? cadena)
    {
        if (string.IsNullOrWhiteSpace(cadena))
        {
            opciones.UseSqlServer("Server=.;Database=SoporteKB")
                .AddInterceptors(new ConexionNoConfiguradaInterceptor(() =>
                    new KbNoDisponibleException("La base de conocimiento no está configurada (ConnectionStrings:SoporteKB).")));
            return;
        }

        opciones.UseSqlServer(cadena, sql => sql.CommandTimeout(ConsultaDefaults.KbTimeoutSegundos))
            .AddInterceptors(new KbConexionInterceptor())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>
    /// EF envuelve en <see cref="DbUpdateException"/> cualquier fallo al guardar, también el de "base no configurada". Se desenvuelve aquí
    /// para que esa excepción llegue intacta al middleware (503), como llegaba con la fábrica de conexiones.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is KbNoDisponibleException inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KbDbContext).Assembly,
            t => t.Namespace?.EndsWith(".Kb", StringComparison.Ordinal) == true);
}
