using MediatR;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Utilerias.Queries.Archivos;

/// <summary>Catalogo de archivos de confianza con el estado de su ultima revision conocida.</summary>
public sealed record ListArchivosQuery : IRequest<IReadOnlyList<ArchivoRevisado>>;

public sealed class ListArchivosHandler(IArchivosReadRepository repo)
    : IRequestHandler<ListArchivosQuery, IReadOnlyList<ArchivoRevisado>>
{
    public Task<IReadOnlyList<ArchivoRevisado>> Handle(ListArchivosQuery request, CancellationToken ct) =>
        repo.ListarAsync(ct);
}
