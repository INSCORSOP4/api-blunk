using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SoporteBlunk.Application.Common.Exceptions;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

/// <summary>
/// Abre la conexión a SoporteKB y, si el servidor no responde, lanza <see cref="KbNoDisponibleException"/> en lugar del
/// <see cref="SqlException"/> crudo: es el contrato de errores que tenía la fábrica de conexiones de Dapper, y el middleware lo
/// traduce a 503. Así un servidor caído no se confunde con una tabla que falta (que sí llega como SqlException al ejecutar).
/// </summary>
internal sealed class KbConexionInterceptor : DbConnectionInterceptor
{
    private const string Mensaje = "No se pudo conectar con la base de conocimiento.";

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        try
        {
            connection.Open();
        }
        catch (SqlException ex)
        {
            throw new KbNoDisponibleException(Mensaje, ex);
        }
        return InterceptionResult.Suppress();
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new KbNoDisponibleException(Mensaje, ex);
        }
        return InterceptionResult.Suppress();
    }
}
