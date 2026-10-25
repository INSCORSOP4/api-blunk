using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Msdb;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

/// <summary>
/// Usuarios de MICROSIP a través del linked server (Firebird por ODBC). Las lecturas usan OPENQUERY (el texto es fijo y
/// solo lleva enteros ya validados); la escritura usa EXEC ... AT con parámetros, nunca texto concatenado.
/// </summary>
internal sealed class MicrosipRepository(IMicrosipConnections conexiones, IOptions<UtileriasOptions> options) : IMicrosipRepository
{
    // El nombre del linked server se valida al arrancar (solo letras, números y _), por eso es seguro incluirlo en el texto.
    private string Linked => options.Value.Microsip.ConfigLinkedServer;

    private string SqlUsuarios(string filtroUsuario) => $"""
        SELECT UsuarioId, CAST(Nombre AS nvarchar(200)) AS Nombre, CAST(Estatus AS varchar(5)) AS Estatus,
               CAST(ClaveFortia AS varchar(20)) AS ClaveFortia, CAST(NombreUsuario AS nvarchar(200)) AS NombreUsuario,
               CAST(Empresa AS nvarchar(100)) AS Empresa, CAST(Puesto AS nvarchar(200)) AS Puesto, DatosAdiId
        FROM OPENQUERY([{Linked}], '
            SELECT AA.USUARIO_ID AS UsuarioId, AA.NOMBRE AS Nombre, AA.ESTATUS AS Estatus,
                   BB.CLAVE_FORTIA AS ClaveFortia, BB.NOMBRE_USUARIO AS NombreUsuario, BB.EMPRESA AS Empresa,
                   BB.PUESTO AS Puesto, BB.USUARIO_DATOS_ADI_ID AS DatosAdiId
            FROM USUARIOS AA
            LEFT JOIN USUARIOS_DATOS_ADICIONALES BB ON BB.USUARIO_ID = AA.USUARIO_ID
            WHERE AA.TIPO = ''U''{filtroUsuario}
        ')
        ORDER BY Nombre;
        """;

    public async Task<IReadOnlyList<UsuarioMicrosip>> ListarUsuariosAsync(CancellationToken ct) =>
        await ConsultarAsync(SqlUsuarios(""), ct);

    public async Task<UsuarioMicrosip?> ObtenerUsuarioAsync(int usuarioId, CancellationToken ct) =>
        // usuarioId es int (no puede llevar texto): es seguro incluirlo en el texto de OPENQUERY.
        (await ConsultarAsync(SqlUsuarios($" AND AA.USUARIO_ID = {usuarioId}"), ct)).FirstOrDefault();

    private async Task<IReadOnlyList<UsuarioMicrosip>> ConsultarAsync(string sql, CancellationToken ct)
    {
        await using var conn = await conexiones.OpenReadAsync(ct);
        try
        {
            var filas = await conn.QueryAsync<Fila>(new CommandDefinition(sql, commandTimeout: 30, cancellationToken: ct));
            // Un usuario con varias filas de datos adicionales (no debería) aparece una sola vez.
            return filas.GroupBy(f => f.UsuarioId).Select(g => Mapear(g.OrderByDescending(f => f.DatosAdiId).First())).ToList();
        }
        catch (SqlException ex)
        {
            throw new UtileriaNoDisponibleException("No se pudo consultar a los usuarios de MICROSIP.", ex);
        }
    }

    public async Task GuardarDatosAdicionalesAsync(int usuarioId, DatosAdicionales datos, bool existe, CancellationToken ct)
    {
        // Los '?' son los parámetros que SQL Server entrega al linked server; los valores no se concatenan.
        var remoto = existe
            ? "UPDATE USUARIOS_DATOS_ADICIONALES SET CLAVE_FORTIA = ?, NOMBRE_USUARIO = ?, EMPRESA = ?, PUESTO = ? WHERE USUARIO_ID = ?"
            : "INSERT INTO USUARIOS_DATOS_ADICIONALES (USUARIO_DATOS_ADI_ID, USUARIO_ID, CLAVE_FORTIA, NOMBRE_USUARIO, EMPRESA, PUESTO) VALUES (-1, ?, ?, ?, ?, ?)";
        var sql = $"EXEC (@Remoto, @P1, @P2, @P3, @P4, @P5) AT [{Linked}];";

        var p = new DynamicParameters();
        p.Add("Remoto", remoto, System.Data.DbType.String, size: 4000);
        if (existe)
        {
            p.Add("P1", datos.ClaveFortia, System.Data.DbType.String, size: 50);
            p.Add("P2", datos.NombreUsuario, System.Data.DbType.String, size: 400);
            p.Add("P3", datos.Empresa, System.Data.DbType.String, size: 400);
            p.Add("P4", datos.Puesto, System.Data.DbType.String, size: 400);
            p.Add("P5", usuarioId, System.Data.DbType.Int32);
        }
        else
        {
            p.Add("P1", usuarioId, System.Data.DbType.Int32);
            p.Add("P2", datos.ClaveFortia, System.Data.DbType.String, size: 50);
            p.Add("P3", datos.NombreUsuario, System.Data.DbType.String, size: 400);
            p.Add("P4", datos.Empresa, System.Data.DbType.String, size: 400);
            p.Add("P5", datos.Puesto, System.Data.DbType.String, size: 400);
        }

        await using var conn = await conexiones.OpenWriteAsync(ct);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(sql, p, commandTimeout: 30, cancellationToken: ct));
        }
        catch (SqlException ex)
        {
            throw new UtileriaNoDisponibleException(
                $"MICROSIP no aceptó el cambio: {ex.Message} (el usuario configurado quizá no tiene permiso de escritura).", ex);
        }
    }

    private static UsuarioMicrosip Mapear(Fila f)
    {
        static string? Limpio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var estatus = (f.Estatus ?? "").Trim();
        return new UsuarioMicrosip(
            f.UsuarioId, (f.Nombre ?? "").Trim(), estatus, string.Equals(estatus, "A", StringComparison.OrdinalIgnoreCase),
            Limpio(f.ClaveFortia), Limpio(f.NombreUsuario), Limpio(f.Empresa), Limpio(f.Puesto),
            f.DatosAdiId is not null);
    }

    private sealed record Fila(
        int UsuarioId, string? Nombre, string? Estatus, string? ClaveFortia, string? NombreUsuario,
        string? Empresa, string? Puesto, int? DatosAdiId);
}

/// <summary>Trabajadores de FORTIA (RH_TRAB) por el linked server. Solo lectura.</summary>
internal sealed class FortiaRepository(IMicrosipConnections conexiones, IOptions<UtileriasOptions> options) : IFortiaRepository
{
    public async Task<TrabajadorFortia?> ObtenerTrabajadorAsync(int clave, CancellationToken ct)
    {
        var o = options.Value.Microsip;
        var prefijo = $"[{o.FortiaLinkedServer}].[{o.FortiaBaseDatos}].[dbo]";
        // Nombres de linked server/base validados al arrancar (solo letras, números y _); la clave va como parámetro.
        var sql = $"""
            SELECT TOP (1)
                   AA.CLA_TRAB AS Clave,
                   LTRIM(RTRIM(CONCAT(LTRIM(RTRIM(AA.NOM_TRAB)), ' ', LTRIM(RTRIM(AA.AP_PATERNO)), ' ', LTRIM(RTRIM(AA.AP_MATERNO))))) AS NombreCompleto,
                   LTRIM(RTRIM(ISNULL(CC.NOM_RAZON_SOCIAL, N''))) AS Empresa,
                   LTRIM(RTRIM(ISNULL(BB.NOM_PUESTO, N''))) AS Puesto
            FROM {prefijo}.[RH_TRAB] AS AA
            LEFT JOIN {prefijo}.[RH_PUESTO] AS BB ON BB.CLA_PUESTO = AA.CLA_PUESTO
            LEFT JOIN {prefijo}.[RH_RAZON_SOCIAL] AS CC ON CC.CLA_RAZON_SOCIAL = AA.CLA_RAZON_SOCIAL
            WHERE AA.CLA_TRAB = @Clave;
            """;

        await using var conn = await conexiones.OpenReadAsync(ct);
        try
        {
            var fila = await conn.QueryFirstOrDefaultAsync<FilaTrabajador>(
                new CommandDefinition(sql, new { Clave = clave }, commandTimeout: 30, cancellationToken: ct));
            return fila is null ? null : new TrabajadorFortia(fila.Clave, fila.NombreCompleto, fila.Empresa, fila.Puesto);
        }
        catch (SqlException ex)
        {
            throw new UtileriaNoDisponibleException("No se pudo consultar a FORTIA.", ex);
        }
    }

    private sealed record FilaTrabajador(int Clave, string NombreCompleto, string Empresa, string Puesto);
}
