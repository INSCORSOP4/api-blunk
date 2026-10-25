using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Tickets.Queries.GetTicketHistorial;

public sealed record GetTicketHistorialQuery(long Id) : IRequest<IReadOnlyList<TicketHistorialItem>>;

public sealed class GetTicketHistorialValidator : AbstractValidator<GetTicketHistorialQuery>
{
    public GetTicketHistorialValidator() => RuleFor(x => x.Id).GreaterThan(0);
}

public sealed class GetTicketHistorialHandler(ITicketReadRepository repo)
    : IRequestHandler<GetTicketHistorialQuery, IReadOnlyList<TicketHistorialItem>>
{
    public async Task<IReadOnlyList<TicketHistorialItem>> Handle(GetTicketHistorialQuery r, CancellationToken ct)
    {
        if (!await repo.ExisteAsync(r.Id, ct)) throw new NotFoundException("el ticket", r.Id);
        return await repo.HistorialAsync(r.Id, ct);
    }
}
