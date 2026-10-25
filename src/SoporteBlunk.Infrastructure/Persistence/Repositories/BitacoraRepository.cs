using Dapper;
using FluentValidation;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Kb;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class BitacoraRepository(IKbConnectionFactory factory) : IBitacoraRepository
{
    public async Task<IReadOnlyList<ActividadBitacora>> ListarActividadesAsync(string grupo, bool incluirInactivas, CancellationToken ct)
    {
        await using var conn = await Abrir(ct);
        var filas = await conn.QueryAsync<FilaActividad>(new CommandDefinition(
            """
            SELECT ActividadId, Grupo, Nombre, Orden, Activa
            FROM bitacora.Actividad
            WHERE Grupo = @Grupo AND (@IncluirInactivas = 1 OR Activa = 1)
            ORDER BY Orden, Nombre;
            """,
            new { Grupo = grupo, IncluirInactivas = incluirInactivas }, commandTimeout: 15, cancellationToken: ct));
        return filas.Select(Mapear).ToList();
    }

    public async Task<ActividadBitacora?> ObtenerActividadAsync(int actividadId, CancellationToken ct)
    {
        await using var conn = await Abrir(ct);
        var fila = await conn.QueryFirstOrDefaultAsync<FilaActividad>(new CommandDefinition(
            "SELECT ActividadId, Grupo, Nombre, Orden, Activa FROM bitacora.Actividad WHERE ActividadId = @Id;",
            new { Id = actividadId }, commandTimeout: 15, cancellationToken: ct));
        return fila is null ? null : Mapear(fila);
    }

    public async Task<ActividadBitacora> CrearActividadAsync(string grupo, string nombre, string usuario, CancellationToken ct)
    {
        await using var conn = await Abrir(ct);
        try
        {
            // El siguiente Orden es el máximo actual del grupo + 1 (se agrega al final).
            var id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO bitacora.Actividad (Grupo, Nombre, Orden, Activa, CreadoPor)
                OUTPUT INSERTED.ActividadId
                SELECT @Grupo, @Nombre, ISNULL((SELECT MAX(Orden) FROM bitacora.Actividad WHERE Grupo = @Grupo), 0) + 1, 1, @Usuario;
                """,
                new { Grupo = grupo, Nombre = nombre, Usuario = usuario }, commandTimeout: 15, cancellationToken: ct));
            return (await ObtenerActividadAsync(id, ct))!;
        }
        catch (SqlException ex) when (ex.Number == 2627) // violación de UQ (Grupo, Nombre)
        {
            throw new ValidationException($"Ya existe una actividad llamada \"{nombre}\" en esta bitácora.");
        }
        catch (SqlException ex)
        {
            throw new KbNoDisponibleException("No se pudo crear la actividad en la bitácora.", ex);
        }
    }

    public async Task<ActividadBitacora> ActualizarActividadAsync(
        int actividadId, string? nombre, bool? activa, int? orden, string usuario, CancellationToken ct)
    {
        await using var conn = await Abrir(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE bitacora.Actividad
                SET Nombre = COALESCE(@Nombre, Nombre),
                    Activa = COALESCE(@Activa, Activa),
                    Orden = COALESCE(@Orden, Orden),
                    ActualizadoPor = @Usuario,
                    ActualizadoEn = SYSDATETIME()
                WHERE ActividadId = @Id;
                """,
                new { Id = actividadId, Nombre = nombre, Activa = activa, Orden = orden, Usuario = usuario },
                commandTimeout: 15, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number == 2627)
        {
            throw new ValidationException($"Ya existe una actividad llamada \"{nombre}\" en esta bitácora.");
        }
        catch (SqlException ex)
        {
            throw new KbNoDisponibleException("No se pudo actualizar la actividad de la bitácora.", ex);
        }
        return (await ObtenerActividadAsync(actividadId, ct))!;
    }

    public async Task<ChecklistDia> ObtenerChecklistAsync(string grupo, DateOnly fecha, CancellationToken ct)
    {
        await using var conn = await Abrir(ct);
        // Actividades activas hoy + las que tengan registro ese día aunque ya se hayan desactivado (no se pierde historial).
        var filas = await conn.QueryAsync<FilaItem>(new CommandDefinition(
            """
            SELECT a.ActividadId, a.Nombre, a.Orden, a.Activa AS ActividadActiva,
                   c.Estado, c.Observacion, c.CreadoPor AS RevisadoPor, c.CreadoEn AS RevisadoEn,
                   c.ActualizadoPor, c.ActualizadoEn
            FROM bitacora.Actividad a
            LEFT JOIN bitacora.ChecklistDiario c ON c.ActividadId = a.ActividadId AND c.Fecha = @Fecha
            WHERE a.Grupo = @Grupo AND (a.Activa = 1 OR c.ActividadId IS NOT NULL)
            ORDER BY a.Orden, a.Nombre;
            """,
            new { Grupo = grupo, Fecha = fecha.ToDateTime(TimeOnly.MinValue) }, commandTimeout: 15, cancellationToken: ct));

        return new ChecklistDia(grupo, fecha, filas.Select(Mapear).ToList());
    }

    public async Task<ItemChecklist> GuardarChecklistAsync(
        int actividadId, DateOnly fecha, EstadoChecklist estado, string? observacion, string usuario, CancellationToken ct)
    {
        await using var conn = await Abrir(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                MERGE bitacora.ChecklistDiario AS destino
                USING (SELECT @Fecha AS Fecha, @ActividadId AS ActividadId) AS origen
                ON destino.Fecha = origen.Fecha AND destino.ActividadId = origen.ActividadId
                WHEN MATCHED THEN
                    UPDATE SET Estado = @Estado, Observacion = @Observacion, ActualizadoPor = @Usuario, ActualizadoEn = SYSDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (Fecha, ActividadId, Estado, Observacion, CreadoPor)
                    VALUES (origen.Fecha, origen.ActividadId, @Estado, @Observacion, @Usuario);
                """,
                new
                {
                    Fecha = fecha.ToDateTime(TimeOnly.MinValue),
                    ActividadId = actividadId,
                    Estado = estado.ToString(),
                    Observacion = observacion,
                    Usuario = usuario,
                },
                commandTimeout: 15, cancellationToken: ct));
        }
        catch (SqlException ex)
        {
            throw new KbNoDisponibleException("No se pudo guardar el estado en la bitácora.", ex);
        }

        var fila = await conn.QuerySingleAsync<FilaItem>(new CommandDefinition(
            """
            SELECT a.ActividadId, a.Nombre, a.Orden, a.Activa AS ActividadActiva,
                   c.Estado, c.Observacion, c.CreadoPor AS RevisadoPor, c.CreadoEn AS RevisadoEn,
                   c.ActualizadoPor, c.ActualizadoEn
            FROM bitacora.Actividad a
            JOIN bitacora.ChecklistDiario c ON c.ActividadId = a.ActividadId
            WHERE a.ActividadId = @ActividadId AND c.Fecha = @Fecha;
            """,
            new { ActividadId = actividadId, Fecha = fecha.ToDateTime(TimeOnly.MinValue) }, commandTimeout: 15, cancellationToken: ct));
        return Mapear(fila);
    }

    public async Task<IReadOnlyList<ResumenDia>> ObtenerResumenAsync(string grupo, DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        await using var conn = await Abrir(ct);
        var filas = await conn.QueryAsync<FilaResumen>(new CommandDefinition(
            """
            SELECT c.Fecha,
                   SUM(CASE WHEN c.Estado = 'Ok' THEN 1 ELSE 0 END) AS Ok,
                   SUM(CASE WHEN c.Estado = 'Advertencia' THEN 1 ELSE 0 END) AS Advertencia,
                   SUM(CASE WHEN c.Estado = 'Falla' THEN 1 ELSE 0 END) AS Falla,
                   SUM(CASE WHEN c.Estado = 'Critico' THEN 1 ELSE 0 END) AS Critico,
                   SUM(CASE WHEN c.Estado = 'NoAplica' THEN 1 ELSE 0 END) AS NoAplica,
                   COUNT(*) AS Total
            FROM bitacora.ChecklistDiario c
            JOIN bitacora.Actividad a ON a.ActividadId = c.ActividadId
            WHERE a.Grupo = @Grupo AND c.Fecha BETWEEN @Desde AND @Hasta
            GROUP BY c.Fecha;
            """,
            new { Grupo = grupo, Desde = desde.ToDateTime(TimeOnly.MinValue), Hasta = hasta.ToDateTime(TimeOnly.MinValue) },
            commandTimeout: 15, cancellationToken: ct));

        // El total de actividades activas de hoy sirve para calcular "pendientes" aunque el día no tenga ningún registro.
        var totalActivas = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM bitacora.Actividad WHERE Grupo = @Grupo AND Activa = 1;",
            new { Grupo = grupo }, commandTimeout: 15, cancellationToken: ct));

        var porFecha = filas.ToDictionary(f => DateOnly.FromDateTime(f.Fecha));
        var resultado = new List<ResumenDia>();
        for (var fecha = desde; fecha <= hasta; fecha = fecha.AddDays(1))
        {
            if (porFecha.TryGetValue(fecha, out var f))
                resultado.Add(new ResumenDia(fecha, f.Ok, f.Advertencia, f.Falla, f.Critico, f.NoAplica,
                    Math.Max(0, totalActivas - f.Total), totalActivas));
            else
                resultado.Add(new ResumenDia(fecha, 0, 0, 0, 0, 0, totalActivas, totalActivas));
        }
        return resultado;
    }

    public async Task<IReadOnlyList<CambioChecklist>> UltimosCambiosAsync(int maximo, CancellationToken ct)
    {
        await using var conn = await Abrir(ct);
        var filas = await conn.QueryAsync<FilaCambio>(new CommandDefinition(
            """
            SELECT TOP (@Maximo) a.Grupo, a.Nombre AS ActividadNombre, c.Fecha, c.Estado, c.Observacion,
                   COALESCE(c.ActualizadoPor, c.CreadoPor) AS Usuario,
                   COALESCE(c.ActualizadoEn, c.CreadoEn) AS Cuando
            FROM bitacora.ChecklistDiario c
            JOIN bitacora.Actividad a ON a.ActividadId = c.ActividadId
            ORDER BY Cuando DESC;
            """,
            new { Maximo = maximo }, commandTimeout: 15, cancellationToken: ct));

        return filas.Select(f => new CambioChecklist(
            f.Grupo, f.ActividadNombre, DateOnly.FromDateTime(f.Fecha), Enum.Parse<EstadoChecklist>(f.Estado),
            f.Observacion, f.Usuario, ComoOffset(f.Cuando)!.Value)).ToList();
    }

    private Task<SqlConnection> Abrir(CancellationToken ct) => factory.OpenAsync(ct);

    private static ActividadBitacora Mapear(FilaActividad f) => new(f.ActividadId, f.Grupo, f.Nombre, f.Orden, f.Activa);

    private static ItemChecklist Mapear(FilaItem f) => new(
        f.ActividadId, f.Nombre, f.Orden, f.ActividadActiva,
        f.Estado is null ? null : Enum.Parse<EstadoChecklist>(f.Estado),
        f.Observacion, f.RevisadoPor, ComoOffset(f.RevisadoEn), f.ActualizadoPor, ComoOffset(f.ActualizadoEn));

    // Las fechas de SoporteKB son datetime2 (hora local del servidor, sin zona); se interpretan como hora de México.
    private static DateTimeOffset? ComoOffset(DateTime? valor) =>
        valor is null ? null : new DateTimeOffset(DateTime.SpecifyKind(valor.Value, DateTimeKind.Unspecified), SoporteBlunk.Application.Common.ZonaHoraria.Zona.GetUtcOffset(valor.Value));

    private sealed record FilaCambio(string Grupo, string ActividadNombre, DateTime Fecha, string Estado, string? Observacion, string Usuario, DateTime Cuando);

    private sealed record FilaActividad(int ActividadId, string Grupo, string Nombre, int Orden, bool Activa);

    private sealed record FilaItem(
        int ActividadId, string Nombre, int Orden, bool ActividadActiva, string? Estado, string? Observacion,
        string? RevisadoPor, DateTime? RevisadoEn, string? ActualizadoPor, DateTime? ActualizadoEn);

    private sealed record FilaResumen(DateTime Fecha, int Ok, int Advertencia, int Falla, int Critico, int NoAplica, int Total);
}
