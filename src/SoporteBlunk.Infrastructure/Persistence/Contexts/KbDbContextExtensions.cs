using Microsoft.EntityFrameworkCore;

namespace SoporteBlunk.Infrastructure.Persistence.Contexts;

internal static class KbDbContextExtensions
{
    /// <summary>
    /// Ejecuta una operación con su propio tiempo máximo por comando y restaura el anterior al terminar. Cada repositorio de SoporteKB
    /// tenía el suyo con Dapper (15 s en Bitácora, 30 s en la lectura de conocimiento, 120 s en el importador) y el contexto es uno solo
    /// por petición.
    /// </summary>
    public static async Task<T> ConTimeout<T>(this KbDbContext db, int segundos, Func<Task<T>> operacion)
    {
        var anterior = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(segundos);
        try
        {
            return await operacion();
        }
        finally
        {
            db.Database.SetCommandTimeout(anterior);
        }
    }
}
