using Microsoft.EntityFrameworkCore;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Contexts;
using SoporteBlunk.Infrastructure.Persistence.Queries;

namespace SoporteBlunk.Infrastructure.Persistence.Repositories;

internal sealed class ArchivosReadRepository(KbDbContext db) : IArchivosReadRepository
{
    public async Task<IReadOnlyList<ArchivoRevisado>> ListarAsync(CancellationToken ct)
    {
        var filas = await ArchivosConsultas.ConUltimaRevision(db).ToListAsync(ct);
        return filas.ConvertAll(f => new ArchivoRevisado(
            f.ArchivoId, f.Ruta, f.NombreArchivo, f.Fecha,
            f.Estado is null ? EstadoArchivo.SinRevisar : Enum.Parse<EstadoArchivo>(f.Estado),
            KbTiempo.ComoOffset(f.UltimaModificacion), f.Detalle, KbTiempo.ComoOffset(f.RevisadoEn)));
    }
}
