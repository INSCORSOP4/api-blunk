using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Domain.Interfaces;

public interface IKbReadRepository
{
    /// <remarks>
    /// Lanza <c>KbNoDisponibleException</c> (definida en Application) si la base no está configurada,
    /// no responde o no tiene el esquema esperado.
    /// </remarks>
    Task<KbEstado> EstadoAsync(CancellationToken ct);

    Task<PagedResult<DocumentoResumen>> ListarDocumentosAsync(DocumentoFiltro filtro, int page, int pageSize, CancellationToken ct);

    /// <returns>null si no existe o fue eliminado.</returns>
    Task<DocumentoDetalle?> ObtenerDocumentoAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<CarpetaConteo>> CarpetasAsync(CancellationToken ct);

    Task<IReadOnlyList<EtiquetaConteo>> EtiquetasAsync(CancellationToken ct);
}
