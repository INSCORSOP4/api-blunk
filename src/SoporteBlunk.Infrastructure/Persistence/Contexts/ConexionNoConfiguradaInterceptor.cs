using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

/// <summary>
/// Para las bases opcionales sin cadena de conexión: el contexto se puede crear (la API arranca y los handlers que no usan esa
/// base siguen funcionando), pero abrir la conexión lanza la misma excepción que lanzaba la fábrica de conexiones de Dapper,
/// que el middleware traduce a 503.
/// </summary>
internal sealed class ConexionNoConfiguradaInterceptor(Func<Exception> crearExcepcion) : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
        throw crearExcepcion();

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
        throw crearExcepcion();
}
