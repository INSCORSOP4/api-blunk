using MediatR;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Utilerias.Queries.Microsip;

public sealed record ListUsuariosMicrosipQuery : IRequest<IReadOnlyList<UsuarioMicrosip>>;

public sealed class ListUsuariosMicrosipHandler(IMicrosipRepository repo)
    : IRequestHandler<ListUsuariosMicrosipQuery, IReadOnlyList<UsuarioMicrosip>>
{
    public Task<IReadOnlyList<UsuarioMicrosip>> Handle(ListUsuariosMicrosipQuery request, CancellationToken ct) =>
        repo.ListarUsuariosAsync(ct);
}

public sealed record GetTrabajadorFortiaQuery(int Clave) : IRequest<TrabajadorFortia>;

public sealed class GetTrabajadorFortiaHandler(IFortiaRepository repo) : IRequestHandler<GetTrabajadorFortiaQuery, TrabajadorFortia>
{
    public async Task<TrabajadorFortia> Handle(GetTrabajadorFortiaQuery request, CancellationToken ct) =>
        await repo.ObtenerTrabajadorAsync(request.Clave, ct)
        ?? throw new NotFoundException("el trabajador de FORTIA", request.Clave);
}

public sealed record ListCambiosMicrosipQuery(int Maximo = 50) : IRequest<IReadOnlyList<CambioMicrosip>>;

public sealed class ListCambiosMicrosipHandler(IAuditoriaMicrosipRepository repo)
    : IRequestHandler<ListCambiosMicrosipQuery, IReadOnlyList<CambioMicrosip>>
{
    public Task<IReadOnlyList<CambioMicrosip>> Handle(ListCambiosMicrosipQuery request, CancellationToken ct) =>
        repo.ListarAsync(Math.Clamp(request.Maximo, 1, 200), ct);
}
