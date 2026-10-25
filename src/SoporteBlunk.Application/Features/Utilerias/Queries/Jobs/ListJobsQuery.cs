using MediatR;
using Microsoft.Extensions.Options;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Utilerias.Queries.Jobs;

/// <summary>Estado de los jobs de SQL Server Agent. Por defecto solo los vigilados (configurados); con false, todos.</summary>
public sealed record ListJobsQuery(bool SoloVigilados = true) : IRequest<JobsResultado>;

public sealed class ListJobsHandler(
    IJobsReadRepository repo,
    IOptions<UtileriasOptions> options,
    TimeProvider time) : IRequestHandler<ListJobsQuery, JobsResultado>
{
    public async Task<JobsResultado> Handle(ListJobsQuery request, CancellationToken ct)
    {
        var vigilados = options.Value.JobsVigilados
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Con "solo vigilados" y sin lista configurada no hay nada que mostrar; se evita traer todos por error.
        if (request.SoloVigilados && vigilados.Count == 0)
            return new JobsResultado(true, [], [], time.GetLocalNow().DateTime);

        var jobs = await repo.ListarAsync(request.SoloVigilados ? vigilados : null, ct);

        // Fallidos y en ejecución arriba: es lo que hay que mirar primero. Luego por nombre.
        var ordenados = jobs
            .OrderBy(j => Prioridad(j))
            .ThenBy(j => j.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Vigilados que la base no devolvió (se renombraron o no existen): se avisan en lugar de desaparecer.
        var noEncontrados = request.SoloVigilados
            ? vigilados.Where(v => !jobs.Any(j => string.Equals(j.Nombre, v, StringComparison.OrdinalIgnoreCase))).ToList()
            : [];

        return new JobsResultado(request.SoloVigilados, noEncontrados, ordenados, time.GetLocalNow().DateTime);
    }

    private static int Prioridad(JobResumen j) => j.Estado switch
    {
        EstadoJob.Fallido or EstadoJob.Cancelado => 0,
        EstadoJob.Reintento => 1,
        EstadoJob.EnEjecucion => 2,
        _ when j.Pasos.Any(p => p.Estado == EstadoJob.Fallido) => 1, // pasos fallidos aunque el job siga en verde
        _ when !j.Habilitado => 4,
        _ => 3,
    };
}
