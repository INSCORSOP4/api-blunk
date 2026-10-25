using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

/// <summary>
/// Compara el modelo de EF Core de un contexto contra INFORMATION_SCHEMA de la base real: que existan las tablas y columnas y que
/// el tipo de la columna sea el que SqlClient exige al leer (bigint → long, bit → bool, ...). Devuelve todos los desajustes juntos.
/// </summary>
internal static class SondaEsquema
{
    private static readonly Dictionary<Type, string[]> Compatibles = new()
    {
        [typeof(long)] = ["bigint"],
        [typeof(int)] = ["int"],
        [typeof(bool)] = ["bit"],
        [typeof(string)] = ["nvarchar", "varchar", "nchar", "char", "ntext", "text"],
        [typeof(DateTime)] = ["datetime2", "datetime", "smalldatetime"],
        [typeof(DateOnly)] = ["date"],
        [typeof(DateTimeOffset)] = ["datetimeoffset"],
    };

    public static async Task<List<string>> CompararAsync(DbContext db, string cadena)
    {
        var reales = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var cn = new SqlConnection(cadena))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS", cn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) reales[$"{r.GetString(0)}.{r.GetString(1)}.{r.GetString(2)}"] = r.GetString(3).ToLowerInvariant();
        }

        var problemas = new List<string>();
        foreach (var entidad in db.Model.GetEntityTypes())
        {
            var tabla = entidad.GetTableName()!;
            var esquema = entidad.GetSchema() ?? "dbo";
            foreach (var propiedad in entidad.GetProperties())
            {
                var columna = propiedad.GetColumnName(StoreObjectIdentifier.Table(tabla, entidad.GetSchema()))!;
                var clr = propiedad.GetProviderClrType() ?? propiedad.ClrType;
                clr = Nullable.GetUnderlyingType(clr) ?? clr;
                var nombre = $"{esquema}.{tabla}.{columna}";
                if (!reales.TryGetValue(nombre, out var tipo)) { problemas.Add($"{nombre}: no existe"); continue; }
                if (Compatibles.TryGetValue(clr, out var validos) && !validos.Contains(tipo))
                    problemas.Add($"{nombre}: el modelo espera {clr.Name} pero la columna es {tipo}");
            }
        }
        return problemas;
    }
}
