using FluentAssertions;
using Moq;
using SoporteBlunk.Application.Features.Tecnicos.Queries;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Tests.Tecnicos;

public class TecnicoQueriesTests
{
    [Fact]
    public void Validador_rechaza_desde_posterior_a_hasta()
    {
        var v = new GetProductividadTecnicosValidator();
        v.Validate(new GetProductividadTecnicosQuery(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1))).IsValid.Should().BeFalse();
        v.Validate(new GetProductividadTecnicosQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))).IsValid.Should().BeTrue();
        v.Validate(new GetProductividadTecnicosQuery(null, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handler_delega_al_repositorio_con_el_rango_recibido()
    {
        var repo = new Mock<ITecnicoReadRepository>();
        var desde = new DateOnly(2026, 1, 1);
        var hasta = new DateOnly(2026, 1, 31);
        var esperado = new List<TecnicoProductividad> { new(1, "Ana", 10, 8, 1, 90, 1.5, 4.2) };
        repo.Setup(r => r.ListarProductividadAsync(desde, hasta, It.IsAny<CancellationToken>())).ReturnsAsync(esperado);

        var r = await new GetProductividadTecnicosHandler(repo.Object).Handle(new GetProductividadTecnicosQuery(desde, hasta), default);

        r.Should().BeSameAs(esperado);
    }

    [Fact]
    public void TendenciaValidator_rechaza_tecnico_invalido_o_rango_invertido()
    {
        var v = new GetTendenciaProductividadValidator();
        v.Validate(new GetTendenciaProductividadQuery(0, null, null, Agrupacion.Semana)).IsValid.Should().BeFalse();
        v.Validate(new GetTendenciaProductividadQuery(1, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), Agrupacion.Semana))
            .IsValid.Should().BeFalse();
        v.Validate(new GetTendenciaProductividadQuery(1, null, null, Agrupacion.Semana)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task TendenciaHandler_sin_fechas_usa_30_dias_salvo_que_sea_por_mes()
    {
        var repo = new Mock<ITecnicoReadRepository>();
        DateOnly? desdeRecibido = null, hastaRecibido = null;
        repo.Setup(r => r.TendenciaProductividadAsync(1, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Agrupacion>(), It.IsAny<CancellationToken>()))
            .Callback<long, DateOnly, DateOnly, Agrupacion, CancellationToken>((_, d, h, _, _) => { desdeRecibido = d; hastaRecibido = h; })
            .ReturnsAsync([]);
        var handler = new GetTendenciaProductividadHandler(repo.Object);

        await handler.Handle(new GetTendenciaProductividadQuery(1, null, null, Agrupacion.Semana), default);
        (hastaRecibido!.Value.DayNumber - desdeRecibido!.Value.DayNumber).Should().Be(29);

        await handler.Handle(new GetTendenciaProductividadQuery(1, null, null, Agrupacion.Mes), default);
        desdeRecibido.Should().Be(hastaRecibido!.Value.AddMonths(-11));
    }
}
