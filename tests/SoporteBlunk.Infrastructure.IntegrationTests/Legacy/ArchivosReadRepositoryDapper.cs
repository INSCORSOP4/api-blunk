using Dapper;
using Microsoft.Data.SqlClient;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Kb;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

// Implementación Dapper original, conservada solo como referencia para el test de paridad contra EF Core.
internal sealed class ArchivosReadRepositoryDapper(IKbConnectionFactory factory) : IArchivosReadRepository
{
    public async Task<IReadOnlyList<ArchivoRevisado>> ListarAsync(CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);
        var filas = await conn.QueryAsync<Fila>(new CommandDefinition(
            """
            WITH Ultima AS (
                SELECT ArchivoId, MAX(Fecha) AS UltimaFecha
                FROM archivos.RevisionArchivo
                GROUP BY ArchivoId
            )
            SELECT a.ArchivoId, a.Ruta, a.NombreArchivo, r.Fecha, r.Estado,
                   r.UltimaModificacion, r.Detalle, r.RevisadoEn
            FROM archivos.ArchivoEsperado a
            LEFT JOIN Ultima u ON u.ArchivoId = a.ArchivoId
            LEFT JOIN archivos.RevisionArchivo r ON r.ArchivoId = a.ArchivoId AND r.Fecha = u.UltimaFecha
            WHERE a.Activo = 1
            ORDER BY a.Ruta, a.NombreArchivo;
            """,
            commandTimeout: 15, cancellationToken: ct));
        return filas.Select(Mapear).ToList();
    }

    private static ArchivoRevisado Mapear(Fila f) => new(
        f.ArchivoId, f.Ruta, f.NombreArchivo,
        f.Fecha is null ? null : DateOnly.FromDateTime(f.Fecha.Value),
        f.Estado is null ? EstadoArchivo.SinRevisar : Enum.Parse<EstadoArchivo>(f.Estado),
        ComoOffset(f.UltimaModificacion), f.Detalle, ComoOffset(f.RevisadoEn));

    // Las fechas de archivos.RevisionArchivo son datetime2 (hora local del servidor, sin zona);
    // se interpretan como hora de Mexico, igual que en aud.CambioMicrosip y bitacora.*.
    private static DateTimeOffset? ComoOffset(DateTime? valor) =>
        valor is null ? null : new DateTimeOffset(DateTime.SpecifyKind(valor.Value, DateTimeKind.Unspecified), SoporteBlunk.Application.Common.ZonaHoraria.Zona.GetUtcOffset(valor.Value));

    private sealed record Fila(
        int ArchivoId, string Ruta, string NombreArchivo, DateTime? Fecha, string? Estado,
        DateTime? UltimaModificacion, string? Detalle, DateTime? RevisadoEn);
}
