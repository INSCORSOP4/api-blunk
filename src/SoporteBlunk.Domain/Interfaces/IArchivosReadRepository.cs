using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Domain.Interfaces;

/// <summary>Lectura del catalogo de archivos de confianza y su ultima revision conocida.</summary>
public interface IArchivosReadRepository
{
    Task<IReadOnlyList<ArchivoRevisado>> ListarAsync(CancellationToken ct);
}
