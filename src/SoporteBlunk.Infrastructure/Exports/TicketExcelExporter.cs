using ClosedXML.Excel;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Infrastructure.Exports;

internal sealed class TicketExcelExporter : ITicketExcelExporter
{
    private static readonly string[] Encabezados =
    [
        "ID", "Título", "Estado", "Prioridad", "Categoría", "Grupo", "Técnico", "Solicitante", "Vencido", "Creado", "Vence", "Completado",
    ];

    public byte[] Exportar(IReadOnlyList<TicketResumen> tickets)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Tickets");

        for (var c = 0; c < Encabezados.Length; c++)
            ws.Cell(1, c + 1).Value = Encabezados[c];
        ws.Range(1, 1, 1, Encabezados.Length).Style.Font.Bold = true;

        for (var i = 0; i < tickets.Count; i++)
        {
            var t = tickets[i];
            var row = i + 2;
            ws.Cell(row, 1).Value = t.Id;
            ws.Cell(row, 2).Value = Neutralizar(t.Titulo);
            ws.Cell(row, 3).Value = t.Estado;
            ws.Cell(row, 4).Value = t.Prioridad ?? string.Empty;
            ws.Cell(row, 5).Value = t.Categoria ?? string.Empty;
            ws.Cell(row, 6).Value = t.Grupo ?? string.Empty;
            ws.Cell(row, 7).Value = t.Tecnico ?? string.Empty;
            ws.Cell(row, 8).Value = t.Solicitante ?? string.Empty;
            ws.Cell(row, 9).Value = t.Vencido ? "Sí" : "No";
            Fecha(ws.Cell(row, 10), t.Creado);
            Fecha(ws.Cell(row, 11), t.Vence);
            Fecha(ws.Cell(row, 12), t.Completado);
        }

        ws.Range(2, 10, Math.Max(2, tickets.Count + 1), 12).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 200);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Fecha(IXLCell cell, DateTimeOffset? valor)
    {
        if (valor is { } v) cell.Value = v.DateTime;
    }

    // Evita inyección de fórmulas: un título que empiece con = + - @ se guarda como texto.
    private static string Neutralizar(string texto) =>
        texto.Length > 0 && "=+-@".Contains(texto[0]) ? "'" + texto : texto;
}
