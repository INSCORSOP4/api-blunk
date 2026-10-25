using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Abstractions;

public interface ITicketExcelExporter
{
    byte[] Exportar(IReadOnlyList<TicketResumen> tickets);
}
