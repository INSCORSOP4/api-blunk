using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Tickets.Queries.ExportTickets;

public sealed record ExportTicketsQuery(TicketFiltro Filtro) : IRequest<ExportTicketsResult>;

public sealed record ExportTicketsResult(byte[] Contenido, string NombreArchivo, bool Truncado);

public sealed class ExportTicketsValidator : AbstractValidator<ExportTicketsQuery>
{
    public ExportTicketsValidator()
    {
        RuleFor(x => x.Filtro.Buscar).MaximumLength(200);
        this.RangoValido(x => x.Filtro.Desde, x => x.Filtro.Hasta);
    }
}

public sealed class ExportTicketsHandler(ITicketReadRepository repo, ITicketExcelExporter exporter)
    : IRequestHandler<ExportTicketsQuery, ExportTicketsResult>
{
    public const int MaxFilas = 10_000;

    public async Task<ExportTicketsResult> Handle(ExportTicketsQuery r, CancellationToken ct)
    {
        // Se pide una fila extra para saber si el resultado fue recortado.
        var tickets = await repo.ListarParaExportAsync(r.Filtro, MaxFilas + 1, ct);
        var truncado = tickets.Count > MaxFilas;
        if (truncado) tickets = tickets.Take(MaxFilas).ToList();

        var nombre = $"tickets_{ZonaHoraria.Hoy():yyyyMMdd}.xlsx";
        return new ExportTicketsResult(exporter.Exportar(tickets), nombre, truncado);
    }
}
