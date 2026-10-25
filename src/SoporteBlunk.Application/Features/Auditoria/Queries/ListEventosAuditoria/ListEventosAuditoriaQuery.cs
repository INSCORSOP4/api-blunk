using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Auditoria.Queries.ListEventosAuditoria;

/// <summary>Línea de tiempo con los eventos de Conocimiento, MICROSIP y Bitácora juntos, el más reciente primero.</summary>
public sealed record ListEventosAuditoriaQuery(int Maximo = 100) : IRequest<IReadOnlyList<EventoAuditoria>>;

public sealed class ListEventosAuditoriaValidator : AbstractValidator<ListEventosAuditoriaQuery>
{
    public ListEventosAuditoriaValidator() => RuleFor(x => x.Maximo).InclusiveBetween(1, 300);
}

public sealed class ListEventosAuditoriaHandler(
    IKbImportacionRepository conocimiento,
    IAuditoriaMicrosipRepository microsip,
    IBitacoraRepository bitacora,
    ILogger<ListEventosAuditoriaHandler> logger) : IRequestHandler<ListEventosAuditoriaQuery, IReadOnlyList<EventoAuditoria>>
{
    public async Task<IReadOnlyList<EventoAuditoria>> Handle(ListEventosAuditoriaQuery request, CancellationToken ct)
    {
        var eventos = new List<EventoAuditoria>();
        var algunaFuenteRespondio = false;
        Exception? ultimoError = null;

        // Las tres fuentes viven en SoporteKB, pero son tablas/esquemas distintos (algunos opcionales según qué
        // scripts se hayan corrido en el ambiente). Si una falla, se muestra lo que sí se pudo leer, no se cae todo.
        async Task LeerAsync(string fuente, Func<Task> trabajo)
        {
            try
            {
                await trabajo();
                algunaFuenteRespondio = true;
            }
            catch (Exception ex)
            {
                ultimoError = ex;
                logger.LogWarning(ex, "No se pudo leer la auditoría de {Fuente}", fuente);
            }
        }

        await LeerAsync("Conocimiento", async () =>
        {
            var importaciones = await conocimiento.ListarAsync(request.Maximo, ct);
            eventos.AddRange(importaciones.Select(MapearConocimiento));
        });
        await LeerAsync("Microsip", async () =>
        {
            var cambios = await microsip.ListarAsync(request.Maximo, ct);
            eventos.AddRange(cambios.Select(MapearMicrosip));
        });
        await LeerAsync("Bitacora", async () =>
        {
            var cambios = await bitacora.UltimosCambiosAsync(request.Maximo, ct);
            eventos.AddRange(cambios.Select(MapearBitacora));
        });

        // Si ninguna fuente respondió, no hay nada que mostrar: se propaga el error en vez de un 200 vacío engañoso.
        if (!algunaFuenteRespondio && ultimoError is not null) throw ultimoError;

        return eventos.OrderByDescending(e => e.Fecha).Take(request.Maximo).ToList();
    }

    private static EventoAuditoria MapearConocimiento(ImportacionResumen i) => new(
        i.TerminadaEn ?? i.IniciadaEn, ModuloAuditoria.Conocimiento, "Importación de notas",
        $"{i.Origen} · {i.Nuevos} nueva{(i.Nuevos == 1 ? "" : "s")}, {i.Actualizados} actualizada{(i.Actualizados == 1 ? "" : "s")}"
            + (i.Errores > 0 ? $", {i.Errores} con error" : ""),
        i.UsuarioNombre, i.UsuarioEmail, i.Estado);

    private static EventoAuditoria MapearMicrosip(CambioMicrosip c) => new(
        c.Fecha, ModuloAuditoria.Microsip, c.Accion == AccionMicrosip.Insertar ? "Alta en MICROSIP" : "Actualización en MICROSIP",
        c.MicrosipNombre + (c.Error is null ? "" : $" — {c.Error}"),
        c.UsuarioNombre, c.UsuarioEmail, c.Resultado.ToString());

    private static EventoAuditoria MapearBitacora(CambioChecklist c) => new(
        c.Cuando, ModuloAuditoria.Bitacora, c.ActividadNombre, c.Observacion, c.Usuario, null, c.Estado.ToString());
}
