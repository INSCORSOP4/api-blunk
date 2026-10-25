using Dapper;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Msdb;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

/// <summary>Lee msdb (jobs de SQL Server Agent). Solo SELECT: no ejecuta, detiene ni modifica ningún job.</summary>
internal sealed class JobsReadRepository(IMsdbConnectionFactory factory) : IJobsReadRepository
{
    private const int MaxMensaje = 1500;

    // Último resultado del job (sysjobservers), próxima ejecución (sysjobschedules) y si está corriendo ahora
    // (sysjobactivity de la sesión actual del Agent: inició y todavía no termina).
    private const string SqlJobs = """
        SELECT j.job_id        AS JobId,
               j.name          AS Nombre,
               CAST(j.enabled AS bit) AS Habilitado,
               CAST(sv.last_run_outcome AS int) AS Resultado,
               sv.last_run_date     AS Fecha,
               sv.last_run_time     AS Hora,
               sv.last_run_duration AS Duracion,
               ns.next_run_date     AS ProxFecha,
               ns.next_run_time     AS ProxHora,
               r.last_executed_step_id AS PasoActual,
               CAST(CASE WHEN r.job_id IS NULL THEN 0 ELSE 1 END AS bit) AS EnEjecucion
        FROM msdb.dbo.sysjobs AS j
        LEFT JOIN msdb.dbo.sysjobservers AS sv ON sv.job_id = j.job_id AND sv.server_id = 0
        OUTER APPLY (SELECT TOP 1 s.next_run_date, s.next_run_time
                     FROM msdb.dbo.sysjobschedules AS s
                     WHERE s.job_id = j.job_id AND s.next_run_date > 0
                     ORDER BY s.next_run_date, s.next_run_time) AS ns
        OUTER APPLY (SELECT TOP 1 a.job_id, a.last_executed_step_id
                     FROM msdb.dbo.sysjobactivity AS a
                     WHERE a.job_id = j.job_id
                       AND a.session_id = (SELECT MAX(session_id) FROM msdb.dbo.syssessions)
                       AND a.start_execution_date IS NOT NULL AND a.stop_execution_date IS NULL) AS r
        WHERE @Todos = 1 OR j.name IN @Nombres
        ORDER BY j.name;
        """;

    // Último resultado de CADA paso: la fila más reciente de sysjobhistory de ese paso.
    private const string SqlPasos = """
        SELECT s.job_id AS JobId,
               s.step_id AS PasoId,
               s.step_name AS Nombre,
               h.run_status AS Resultado,
               h.run_date AS Fecha,
               h.run_time AS Hora,
               h.run_duration AS Duracion,
               LEFT(h.message, @MaxMensaje) AS Mensaje
        FROM msdb.dbo.sysjobsteps AS s
        OUTER APPLY (SELECT TOP 1 x.run_status, x.run_date, x.run_time, x.run_duration, x.message
                     FROM msdb.dbo.sysjobhistory AS x
                     WHERE x.job_id = s.job_id AND x.step_id = s.step_id
                     ORDER BY x.instance_id DESC) AS h
        WHERE s.job_id IN @Ids
        ORDER BY s.job_id, s.step_id;
        """;

    public async Task<IReadOnlyList<JobResumen>> ListarAsync(IReadOnlyCollection<string>? nombres, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        try
        {
            var jobs = (await conn.QueryAsync<FilaJob>(new CommandDefinition(
                SqlJobs,
                new { Todos = nombres is null, Nombres = (nombres ?? []).ToArray() },
                commandTimeout: 20, cancellationToken: ct))).ToList();
            if (jobs.Count == 0) return [];

            var pasos = (await conn.QueryAsync<FilaPaso>(new CommandDefinition(
                SqlPasos,
                new { Ids = jobs.Select(j => j.JobId).ToArray(), MaxMensaje },
                commandTimeout: 20, cancellationToken: ct))).ToLookup(p => p.JobId);

            return jobs.Select(j => Mapear(j, pasos[j.JobId])).ToList();
        }
        catch (SqlException ex) when (ex.Number is 229 or 297 or 300 or 916 or 208)
        {
            // Sin permiso sobre msdb (necesita SQLAgentReaderRole o SELECT sobre sysjobs*), no un fallo del código.
            throw new UtileriaNoDisponibleException(
                "El usuario configurado no tiene permiso para leer los jobs en msdb (rol SQLAgentReaderRole).", ex);
        }
    }

    private static JobResumen Mapear(FilaJob j, IEnumerable<FilaPaso> filas)
    {
        var pasos = filas.Select(p => new JobPaso(
            p.PasoId,
            p.Nombre,
            MsdbValores.Estado(p.Resultado),
            MsdbValores.Fecha(p.Fecha, p.Hora),
            MsdbValores.DuracionSegundos(p.Duracion),
            string.IsNullOrWhiteSpace(p.Mensaje) ? null : p.Mensaje!.Trim())).ToList();

        // sysjobservers usa otro código para "nunca corrió" (5) y "desconocido": MsdbValores.Estado lo trata como sin historial.
        var estado = j.EnEjecucion ? EstadoJob.EnEjecucion : MsdbValores.Estado(j.Resultado);
        return new JobResumen(
            j.Nombre,
            j.Habilitado,
            estado,
            MsdbValores.Fecha(j.Fecha, j.Hora),
            MsdbValores.DuracionSegundos(j.Duracion),
            // Un job deshabilitado conserva su "próxima ejecución" en msdb aunque ya no vaya a correr.
            j.Habilitado ? MsdbValores.Fecha(j.ProxFecha, j.ProxHora) : null,
            j.EnEjecucion ? j.PasoActual : null,
            pasos);
    }

    private sealed record FilaJob(
        Guid JobId, string Nombre, bool Habilitado, int? Resultado, int? Fecha, int? Hora, int? Duracion,
        int? ProxFecha, int? ProxHora, int? PasoActual, bool EnEjecucion);

    private sealed record FilaPaso(
        Guid JobId, int PasoId, string Nombre, int? Resultado, int? Fecha, int? Hora, int? Duracion, string? Mensaje);
}
