using Microsoft.EntityFrameworkCore;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

/// <summary>
/// Contexto de solo lectura para bases ajenas (ServiceDesk, msdb): no se migran ni se escriben.
/// Un intento de guardar cambios falla de inmediato en lugar de llegar a la base.
/// </summary>
internal abstract class ReadOnlyDbContext(DbContextOptions options) : DbContext(options)
{
    private const string Mensaje = "Este contexto es de solo lectura: no se pueden guardar cambios.";

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw new InvalidOperationException(Mensaje);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Mensaje);
}
