using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Entities.Kb;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Queries;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

/// <summary>Bitácora de cambios en MICROSIP, guardada en SoporteKB (aud.CambioMicrosip).</summary>
internal sealed class AuditoriaMicrosipRepository(KbDbContext db) : IAuditoriaMicrosipRepository
{
    private const string Aviso =
        "La bitácora de cambios no está disponible (¿se ejecutó db/002_auditoria_microsip.sql en SoporteKB?). No se escribió nada en MICROSIP.";

    public async Task<long> RegistrarAsync(
        UsuarioActual quien, UsuarioMicrosip objetivo, AccionMicrosip accion, string? antes, string despues, CancellationToken ct)
    {
        try
        {
            var cambio = new AuditoriaCambioMicrosip
            {
                UsuarioEmail = quien.Email,
                UsuarioNombre = quien.Nombre,
                MicrosipUsuarioId = objetivo.UsuarioId,
                MicrosipNombre = objetivo.Nombre,
                Accion = accion,
                Resultado = ResultadoCambio.Pendiente,
                Antes = antes,
                Despues = despues,
            };
            db.CambiosMicrosip.Add(cambio);
            await db.SaveChangesAsync(ct);
            return cambio.Id;
        }
        catch (Exception ex) when (BitacoraRepository.EsErrorDeBase(ex))
        {
            db.ChangeTracker.Clear();
            throw new UtileriaNoDisponibleException(Aviso, ex);
        }
    }

    public async Task MarcarAsync(long cambioId, ResultadoCambio resultado, string? error, CancellationToken ct)
    {
        try
        {
            await db.CambiosMicrosip.Where(c => c.Id == cambioId).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Resultado, resultado)
                .SetProperty(c => c.Error, error), ct);
        }
        catch (Exception ex) when (BitacoraRepository.EsErrorDeBase(ex))
        {
            throw new UtileriaNoDisponibleException("No se pudo anotar el resultado en la bitácora de cambios.", ex);
        }
    }

    public async Task<IReadOnlyList<CambioMicrosip>> ListarAsync(int maximo, CancellationToken ct)
    {
        try
        {
            var filas = await db.CambiosMicrosip.OrderByDescending(c => c.Id).Take(maximo).ToListAsync(ct);

            // Fecha se guarda con SYSDATETIME() (hora local del servidor, sin zona) y se interpreta como hora de México.
            return filas.ConvertAll(f => new CambioMicrosip(
                f.Id, KbTiempo.ComoOffset(f.Fecha)!.Value, f.UsuarioEmail, f.UsuarioNombre, f.MicrosipUsuarioId, f.MicrosipNombre,
                f.Accion, f.Resultado, f.Antes, f.Despues, f.Error));
        }
        catch (Exception ex) when (BitacoraRepository.EsErrorDeBase(ex))
        {
            throw new UtileriaNoDisponibleException(Aviso, ex);
        }
    }
}
