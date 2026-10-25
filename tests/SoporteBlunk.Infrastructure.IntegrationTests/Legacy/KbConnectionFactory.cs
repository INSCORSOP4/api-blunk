using Microsoft.Data.SqlClient;
using SoporteBlunk.Application.Common.Exceptions;

// Fábrica de conexiones de Dapper, conservada solo para las copias de referencia de los tests de paridad.
namespace SoporteBlunk.Infrastructure.Persistence.Kb;

/// <summary>Conexión a SoporteKB (base propia, con escritura). Es independiente de la de ServiceDesk, que es solo lectura.</summary>
public interface IKbConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct);
}

internal sealed class KbConnectionFactory(string? connectionString) : IKbConnectionFactory
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new KbNoDisponibleException("La base de conocimiento no está configurada (ConnectionStrings:SoporteKB).");

        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            throw new KbNoDisponibleException("No se pudo conectar con la base de conocimiento.", ex);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
