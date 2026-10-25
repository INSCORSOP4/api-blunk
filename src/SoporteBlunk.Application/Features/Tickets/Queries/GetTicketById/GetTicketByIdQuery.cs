using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Tickets.Queries.GetTicketById;

public sealed record GetTicketByIdQuery(long Id) : IRequest<TicketDetalle>;

public sealed class GetTicketByIdValidator : AbstractValidator<GetTicketByIdQuery>
{
    public GetTicketByIdValidator() => RuleFor(x => x.Id).GreaterThan(0);
}

public sealed class GetTicketByIdHandler(ITicketReadRepository repo) : IRequestHandler<GetTicketByIdQuery, TicketDetalle>
{
    public async Task<TicketDetalle> Handle(GetTicketByIdQuery r, CancellationToken ct) =>
        await repo.ObtenerAsync(r.Id, ct) ?? throw new NotFoundException("el ticket", r.Id);
}
