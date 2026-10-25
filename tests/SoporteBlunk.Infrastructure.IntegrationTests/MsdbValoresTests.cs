using FluentAssertions;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Persistence.Msdb;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

public class MsdbValoresTests
{
    [Theory]
    [InlineData(20261007, 93005, "2026-10-07T09:30:05")]
    [InlineData(20261007, 0, "2026-10-07T00:00:00")]
    [InlineData(20240229, 235959, "2024-02-29T23:59:59")]
    public void Fecha_convierte_yyyymmdd_y_hhmmss(int fecha, int hora, string esperado) =>
        MsdbValores.Fecha(fecha, hora).Should().Be(DateTime.Parse(esperado));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20260230)] // 30 de febrero
    [InlineData(20261301)] // mes 13
    [InlineData(99999999)]
    public void Fecha_invalida_o_cero_es_null(int fecha) => MsdbValores.Fecha(fecha, 0).Should().BeNull();

    [Fact]
    public void Fecha_null_es_null_y_hora_invalida_conserva_el_dia()
    {
        MsdbValores.Fecha(null, 100).Should().BeNull();
        MsdbValores.Fecha(20261007, 996000).Should().Be(new DateTime(2026, 10, 7));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(130, 90)]
    [InlineData(13045, 5445)]
    [InlineData(240000, 86400)]
    public void Duracion_hhmmss_a_segundos(int duracion, int segundos) =>
        MsdbValores.DuracionSegundos(duracion).Should().Be(segundos);

    [Fact]
    public void Duracion_null_o_negativa_es_null()
    {
        MsdbValores.DuracionSegundos(null).Should().BeNull();
        MsdbValores.DuracionSegundos(-5).Should().BeNull();
    }

    [Theory]
    [InlineData(0, EstadoJob.Fallido)]
    [InlineData(1, EstadoJob.Exitoso)]
    [InlineData(2, EstadoJob.Reintento)]
    [InlineData(3, EstadoJob.Cancelado)]
    [InlineData(4, EstadoJob.EnEjecucion)]
    [InlineData(5, EstadoJob.SinHistorial)] // sysjobservers: nunca ejecutado
    public void Estado_mapea_run_status(int valor, EstadoJob esperado) => MsdbValores.Estado(valor).Should().Be(esperado);

    [Fact]
    public void Estado_null_es_sin_historial() => MsdbValores.Estado(null).Should().Be(EstadoJob.SinHistorial);
}
