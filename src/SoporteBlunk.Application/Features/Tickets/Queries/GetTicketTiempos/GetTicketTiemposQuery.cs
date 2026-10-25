using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Tickets.Queries.GetTicketTiempos;

public sealed record GetTicketTiemposQuery(long Id) : IRequest<TicketTiempos>;

public sealed class GetTicketTiemposValidator : AbstractValidator<GetTicketTiemposQuery>
{
    public GetTicketTiemposValidator() => RuleFor(x => x.Id).GreaterThan(0);
}

public sealed class GetTicketTiemposHandler(ITicketReadRepository repo) : IRequestHandler<GetTicketTiemposQuery, TicketTiempos>
{
    public async Task<TicketTiempos> Handle(GetTicketTiemposQuery r, CancellationToken ct)
    {
        if (!await repo.ExisteAsync(r.Id, ct)) throw new NotFoundException("el ticket", r.Id);
        return await repo.TiemposAsync(r.Id, ct);
    }
}
