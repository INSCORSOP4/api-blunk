using FluentAssertions;
using FluentValidation;
using MediatR;
using Moq;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Behaviors;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Features.Catalogos.Queries.GetCatalogo;
using SoporteBlunk.Application.Features.Dashboard.Queries;
using SoporteBlunk.Application.Features.Tickets.Queries.ExportTickets;
using SoporteBlunk.Application.Features.Tickets.Queries.GetTicketById;
using SoporteBlunk.Application.Features.Tickets.Queries.GetTicketHistorial;
using SoporteBlunk.Application.Features.Tickets.Queries.ListTickets;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Tests;

public class ListTicketsValidatorTests
{
    private static readonly ListTicketsValidator Validator = new();

    private static ListTicketsQuery Query(int page = 1, int size = 25, TicketFiltro? filtro = null) =>
        new(filtro ?? new TicketFiltro(), TicketOrden.CreadoDesc, page, size);

    [Fact]
    public void Acepta_valores_por_defecto() => Validator.Validate(Query()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, ListTicketsValidator.MaxPageSize + 1)]
    public void Rechaza_paginacion_fuera_de_rango(int page, int size) =>
        Validator.Validate(Query(page, size)).IsValid.Should().BeFalse();

    [Fact]
    public void Rechaza_rango_de_fechas_invertido()
    {
        var filtro = new TicketFiltro { Desde = new DateOnly(2026, 10, 5), Hasta = new DateOnly(2026, 10, 1) };
        Validator.Validate(Query(filtro: filtro)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rechaza_rango_mayor_a_diez_anios()
    {
        var filtro = new TicketFiltro { Desde = new DateOnly(2010, 1, 1), Hasta = new DateOnly(2026, 1, 1) };
        Validator.Validate(Query(filtro: filtro)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rechaza_busqueda_demasiado_larga() =>
        Validator.Validate(Query(filtro: new TicketFiltro { Buscar = new string('a', 201) })).IsValid.Should().BeFalse();
}

public class TicketHandlersTests
{
    [Fact]
    public async Task GetTicketById_lanza_NotFound_si_no_existe()
    {
        var repo = new Mock<ITicketReadRepository>();
        repo.Setup(r => r.ObtenerAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((TicketDetalle?)null);

        var act = () => new GetTicketByIdHandler(repo.Object).Handle(new GetTicketByIdQuery(1), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Historial_lanza_NotFound_y_no_consulta_historial_si_el_ticket_no_existe()
    {
        var repo = new Mock<ITicketReadRepository>();
        repo.Setup(r => r.ExisteAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => new GetTicketHistorialHandler(repo.Object).Handle(new GetTicketHistorialQuery(5), default);

        await act.Should().ThrowAsync<NotFoundException>();
        repo.Verify(r => r.HistorialAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Export_marca_truncado_y_recorta_cuando_supera_el_maximo()
    {
        var filas = Enumerable.Range(1, ExportTicketsHandler.MaxFilas + 1)
            .Select(i => new TicketResumen(i, "t", 1, "Open", null, null, null, null, null, null, false, null, null, null))
            .ToList();
        var repo = new Mock<ITicketReadRepository>();
        repo.Setup(r => r.ListarParaExportAsync(It.IsAny<TicketFiltro>(), ExportTicketsHandler.MaxFilas + 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(filas);
        var exporter = new Mock<ITicketExcelExporter>();
        exporter.Setup(e => e.Exportar(It.IsAny<IReadOnlyList<TicketResumen>>())).Returns([1, 2, 3]);

        var result = await new ExportTicketsHandler(repo.Object, exporter.Object).Handle(new ExportTicketsQuery(new TicketFiltro()), default);

        result.Truncado.Should().BeTrue();
        exporter.Verify(e => e.Exportar(It.Is<IReadOnlyList<TicketResumen>>(l => l.Count == ExportTicketsHandler.MaxFilas)), Times.Once);
        result.NombreArchivo.Should().EndWith(".xlsx");
    }
}

public class CatalogoYDashboardTests
{
    [Fact]
    public async Task Catalogo_desconocido_lanza_NotFound_sin_tocar_la_base()
    {
        var repo = new Mock<ICatalogoReadRepository>();

        var act = () => new GetCatalogoHandler(repo.Object).Handle(new GetCatalogoQuery("usuarios; DROP TABLE x"), default);

        await act.Should().ThrowAsync<NotFoundException>();
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dimension_desconocida_lanza_NotFound_sin_tocar_la_base()
    {
        var repo = new Mock<IDashboardReadRepository>();

        var act = () => new GetDashboardPorHandler(repo.Object).Handle(new GetDashboardPorQuery("x", null, null), default);

        await act.Should().ThrowAsync<NotFoundException>();
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Tendencia_usa_ultimos_30_dias_por_defecto()
    {
        DateOnly desde = default, hasta = default;
        var repo = new Mock<IDashboardReadRepository>();
        repo.Setup(r => r.TendenciaAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), Agrupacion.Dia, It.IsAny<CancellationToken>()))
            .Callback<DateOnly, DateOnly, Agrupacion, CancellationToken>((d, h, _, _) => (desde, hasta) = (d, h))
            .ReturnsAsync([]);

        await new GetDashboardTendenciaHandler(repo.Object).Handle(new GetDashboardTendenciaQuery(null, null, Agrupacion.Dia), default);

        (hasta.DayNumber - desde.DayNumber).Should().Be(29);
    }
}

public class ValidationBehaviorTests
{
    private sealed record Ping(int N) : IRequest<int>;

    private sealed class PingValidator : AbstractValidator<Ping>
    {
        public PingValidator() => RuleFor(x => x.N).GreaterThan(0);
    }

    [Fact]
    public async Task Lanza_ValidationException_y_no_ejecuta_el_handler_si_hay_errores()
    {
        var behavior = new ValidationBehavior<Ping, int>([new PingValidator()]);
        var llamado = false;

        var act = () => behavior.Handle(new Ping(0), _ => { llamado = true; return Task.FromResult(1); }, default);

        await act.Should().ThrowAsync<ValidationException>();
        llamado.Should().BeFalse();
    }

    [Fact]
    public async Task Continua_si_la_solicitud_es_valida()
    {
        var behavior = new ValidationBehavior<Ping, int>([new PingValidator()]);

        var result = await behavior.Handle(new Ping(1), _ => Task.FromResult(42), default);

        result.Should().Be(42);
    }
}
