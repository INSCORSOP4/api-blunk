using Dapper;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Kb;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

/// <summary>Bitácora de cambios en MICROSIP, guardada en SoporteKB (aud.CambioMicrosip).</summary>
internal sealed class AuditoriaMicrosipRepository(IKbConnectionFactory factory) : IAuditoriaMicrosipRepository
{
    private const string Aviso =
        "La bitácora de cambios no está disponible (¿se ejecutó db/002_auditoria_microsip.sql en SoporteKB?). No se escribió nada en MICROSIP.";

    public async Task<long> RegistrarAsync(
        UsuarioActual quien, UsuarioMicrosip objetivo, AccionMicrosip accion, string? antes, string despues, CancellationToken ct)
    {
        try
        {
            await using var conn = await factory.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                INSERT INTO aud.CambioMicrosip (UsuarioEmail, UsuarioNombre, MicrosipUsuarioId, MicrosipNombre, Accion, Resultado, Antes, Despues)
                OUTPUT INSERTED.CambioId
                VALUES (@Email, @Nombre, @UsuarioId, @MicrosipNombre, @Accion, 'Pendiente', @Antes, @Despues);
                """,
                new
                {
                    quien.Email,
                    quien.Nombre,
                    UsuarioId = objetivo.UsuarioId,
                    MicrosipNombre = objetivo.Nombre,
                    Accion = accion.ToString(),
                    Antes = antes,
                    Despues = despues,
                },
                commandTimeout: 15, cancellationToken: ct));
        }
        catch (SqlException ex)
        {
            throw new UtileriaNoDisponibleException(Aviso, ex);
        }
    }

    public async Task MarcarAsync(long cambioId, ResultadoCambio resultado, string? error, CancellationToken ct)
    {
        try
        {
            await using var conn = await factory.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE aud.CambioMicrosip SET Resultado = @Resultado, Error = @Error WHERE CambioId = @Id;",
                new { Id = cambioId, Resultado = resultado.ToString(), Error = error },
                commandTimeout: 15, cancellationToken: ct));
        }
        catch (SqlException ex)
        {
            throw new UtileriaNoDisponibleException("No se pudo anotar el resultado en la bitácora de cambios.", ex);
        }
    }

    public async Task<IReadOnlyList<CambioMicrosip>> ListarAsync(int maximo, CancellationToken ct)
    {
        try
        {
            await using var conn = await factory.OpenAsync(ct);
            // Fecha se guarda con SYSDATETIME() (hora local del servidor, sin zona): se le agrega el desfase actual del servidor.
            var filas = await conn.QueryAsync<Fila>(new CommandDefinition(
                """
                SELECT TOP (@Maximo) CambioId,
                       TODATETIMEOFFSET(Fecha, DATEPART(TZoffset, SYSDATETIMEOFFSET())) AS Fecha,
                       UsuarioEmail, UsuarioNombre, MicrosipUsuarioId, MicrosipNombre, Accion, Resultado, Antes, Despues, Error
                FROM aud.CambioMicrosip ORDER BY CambioId DESC;
                """,
                new { Maximo = maximo }, commandTimeout: 15, cancellationToken: ct));
            return filas.Select(f => new CambioMicrosip(
                f.CambioId, f.Fecha, f.UsuarioEmail, f.UsuarioNombre, f.MicrosipUsuarioId, f.MicrosipNombre,
                Enum.Parse<AccionMicrosip>(f.Accion), Enum.Parse<ResultadoCambio>(f.Resultado), f.Antes, f.Despues, f.Error)).ToList();
        }
        catch (SqlException ex)
        {
            throw new UtileriaNoDisponibleException(Aviso, ex);
        }
    }

    private sealed record Fila(
        long CambioId, DateTimeOffset Fecha, string UsuarioEmail, string UsuarioNombre, int MicrosipUsuarioId,
        string MicrosipNombre, string Accion, string Resultado, string? Antes, string Despues, string? Error);
}
