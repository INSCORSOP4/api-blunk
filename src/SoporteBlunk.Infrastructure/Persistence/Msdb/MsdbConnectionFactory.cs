using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Common.Exceptions;

namespace SoporteBlunk.Infrastructure.Persistence.Msdb;

/// <summary>Conexión a msdb del servidor donde corren los jobs. Solo se usa para leer (SELECT).</summary>
public interface IMsdbConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct);
}

internal sealed class MsdbConnectionFactory(string? connectionString) : IMsdbConnectionFactory
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new UtileriaNoDisponibleException("La consulta de jobs no está configurada (ConnectionStrings:Msdb).");

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            throw new UtileriaNoDisponibleException("No se pudo conectar con el servidor de los jobs.", ex);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
