using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Domain.Interfaces;

public interface IJobsReadRepository
{
    /// <summary>
    /// Jobs del SQL Server Agent con el resultado de su última ejecución y de cada paso. <c>nombres</c> null = todos.
    /// Lanza <c>UtileriaNoDisponibleException</c> (Application) si msdb no está configurada o no responde.
    /// </summary>
    Task<IReadOnlyList<JobResumen>> ListarAsync(IReadOnlyCollection<string>? nombres, CancellationToken ct);
}
