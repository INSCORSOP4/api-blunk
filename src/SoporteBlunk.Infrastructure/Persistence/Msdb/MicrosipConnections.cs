using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Common.Exceptions;

namespace SoporteBlunk.Infrastructure.Persistence.Msdb;

/// <summary>
/// Conexiones al servidor donde están los linked servers de MICROSIP y FORTIA. Lectura y escritura van por cadenas
/// distintas: la de escritura es opcional y, si no se configura, se usa la de lectura (que normalmente no puede escribir).
/// </summary>
public interface IMicrosipConnections
{
    Task<SqlConnection> OpenReadAsync(CancellationToken ct);
    Task<SqlConnection> OpenWriteAsync(CancellationToken ct);
}

internal sealed class MicrosipConnections(string? lectura, string? escritura) : IMicrosipConnections
{
    public Task<SqlConnection> OpenReadAsync(CancellationToken ct) => AbrirAsync(lectura, "ConnectionStrings:Microsip", ct);

    public Task<SqlConnection> OpenWriteAsync(CancellationToken ct) =>
        AbrirAsync(string.IsNullOrWhiteSpace(escritura) ? lectura : escritura, "ConnectionStrings:MicrosipEscritura", ct);

    private static async Task<SqlConnection> AbrirAsync(string? cadena, string nombre, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cadena))
            throw new UtileriaNoDisponibleException($"La utilería de usuarios MICROSIP no está configurada ({nombre}).");

        var connection = new SqlConnection(cadena);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            throw new UtileriaNoDisponibleException("No se pudo conectar con el servidor de MICROSIP/FORTIA.", ex);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
