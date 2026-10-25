using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Tickets.Queries.ListTickets;

public sealed record ListTicketsQuery(TicketFiltro Filtro, TicketOrden Orden, int Page, int PageSize)
    : IRequest<PagedResult<TicketResumen>>;

public sealed class ListTicketsValidator : AbstractValidator<ListTicketsQuery>
{
    public const int MaxPageSize = 200;

    public ListTicketsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Filtro.Buscar).MaximumLength(200);
        this.RangoValido(x => x.Filtro.Desde, x => x.Filtro.Hasta);
    }
}

public sealed class ListTicketsHandler(ITicketReadRepository repo)
    : IRequestHandler<ListTicketsQuery, PagedResult<TicketResumen>>
{
    public Task<PagedResult<TicketResumen>> Handle(ListTicketsQuery r, CancellationToken ct) =>
        repo.ListarAsync(r.Filtro, r.Orden, r.Page, r.PageSize, ct);
}
