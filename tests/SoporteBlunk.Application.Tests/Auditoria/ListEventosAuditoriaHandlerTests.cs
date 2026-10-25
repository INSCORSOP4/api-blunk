using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Features.Auditoria.Queries.ListEventosAuditoria;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Tests.Auditoria;

public class ListEventosAuditoriaHandlerTests
{
    private readonly Mock<IKbImportacionRepository> _conocimiento = new();
    private readonly Mock<IAuditoriaMicrosipRepository> _microsip = new();
    private readonly Mock<IBitacoraRepository> _bitacora = new();

    private ListEventosAuditoriaHandler Handler() =>
        new(_conocimiento.Object, _microsip.Object, _bitacora.Object, NullLogger<ListEventosAuditoriaHandler>.Instance);

    private static ImportacionResumen Importacion(DateTimeOffset cuando) => new(
        1, "Ana", "ana@prosur.mx", "archivos", "Completada", cuando, cuando, 5, 3, 1, 1, 0, 0, 0);

    private static CambioMicrosip Cambio(DateTimeOffset cuando) => new(
        1, cuando, "beto@prosur.mx", "Beto", 7, "JUVE", AccionMicrosip.Actualizar, ResultadoCambio.Aplicado, null, "{}", null);

    private static CambioChecklist Checklist(DateTimeOffset cuando) => new(
        "actividades-soft", "Job Chesa", DateOnly.FromDateTime(cuando.Date), EstadoChecklist.Ok, null, "Caro", cuando);

    [Fact]
    public async Task Junta_las_tres_fuentes_y_ordena_por_fecha_descendente()
    {
        var hoy = DateTimeOffset.UtcNow;
        _conocimiento.Setup(r => r.ListarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Importacion(hoy.AddHours(-3))]);
        _microsip.Setup(r => r.ListarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Cambio(hoy.AddHours(-1))]);
        _bitacora.Setup(r => r.UltimosCambiosAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Checklist(hoy.AddHours(-2))]);

        var r = await Handler().Handle(new ListEventosAuditoriaQuery(), default);

        r.Select(e => e.Modulo).Should().Equal(ModuloAuditoria.Microsip, ModuloAuditoria.Bitacora, ModuloAuditoria.Conocimiento);
    }

    [Fact]
    public async Task Si_una_fuente_falla_muestra_las_otras_dos()
    {
        var hoy = DateTimeOffset.UtcNow;
        _conocimiento.Setup(r => r.ListarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Importacion(hoy)]);
        _microsip.Setup(r => r.ListarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UtileriaNoDisponibleException("sin bitácora de MICROSIP"));
        _bitacora.Setup(r => r.UltimosCambiosAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([Checklist(hoy)]);

        var r = await Handler().Handle(new ListEventosAuditoriaQuery(), default);

        r.Should().HaveCount(2);
        r.Select(e => e.Modulo).Should().BeEquivalentTo([ModuloAuditoria.Conocimiento, ModuloAuditoria.Bitacora]);
    }

    [Fact]
    public async Task Si_fallan_las_tres_fuentes_propaga_el_error_en_vez_de_devolver_vacio()
    {
        _conocimiento.Setup(r => r.ListarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new KbNoDisponibleException("sin kb"));
        _microsip.Setup(r => r.ListarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UtileriaNoDisponibleException("sin microsip"));
        _bitacora.Setup(r => r.UltimosCambiosAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new KbNoDisponibleException("sin bitacora"));

        var act = () => Handler().Handle(new ListEventosAuditoriaQuery(), default);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Respeta_el_maximo_despues_de_juntar_todo()
    {
        var hoy = DateTimeOffset.UtcNow;
        _conocimiento.Setup(r => r.ListarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 5).Select(i => Importacion(hoy.AddMinutes(-i))).ToList());
        _microsip.Setup(r => r.ListarAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _bitacora.Setup(r => r.UltimosCambiosAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var r = await Handler().Handle(new ListEventosAuditoriaQuery(Maximo: 3), default);

        r.Should().HaveCount(3);
    }

    [Fact]
    public void Validador_limita_el_rango_de_maximo()
    {
        var v = new ListEventosAuditoriaValidator();
        v.Validate(new ListEventosAuditoriaQuery(0)).IsValid.Should().BeFalse();
        v.Validate(new ListEventosAuditoriaQuery(301)).IsValid.Should().BeFalse();
        v.Validate(new ListEventosAuditoriaQuery(100)).IsValid.Should().BeTrue();
    }
}
