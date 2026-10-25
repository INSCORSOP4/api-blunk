using MediatR;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Conocimiento.Queries.GetEstado;

public sealed record GetKbEstadoQuery : IRequest<KbEstado>;

public sealed class GetKbEstadoHandler(IKbReadRepository repo) : IRequestHandler<GetKbEstadoQuery, KbEstado>
{
    public Task<KbEstado> Handle(GetKbEstadoQuery request, CancellationToken ct) => repo.EstadoAsync(ct);
}
