using FluentAssertions;
using SoporteBlunk.Application.Common.Options;

namespace SoporteBlunk.Application.Tests.Utilerias;

public class MicrosipOptionsTests
{
    [Theory]
    [InlineData("MICROSIP_CONFIG", true)]
    [InlineData("FORTIA", true)]
    [InlineData("PROSUR_PRIME", true)]
    [InlineData("", false)]
    [InlineData("FORTIA]; DROP TABLE x;--", false)]
    [InlineData("a b", false)]
    [InlineData("srv.dominio", false)]
    [InlineData("con'comilla", false)]
    public void Solo_acepta_identificadores_seguros(string valor, bool esperado) =>
        MicrosipOptions.EsIdentificadorSeguro(valor).Should().Be(esperado);

    [Fact]
    public void Los_valores_por_defecto_son_validos_y_uno_malo_invalida_todo()
    {
        new MicrosipOptions().EsValida().Should().BeTrue();
        new MicrosipOptions { FortiaLinkedServer = "X;--" }.EsValida().Should().BeFalse();
    }
}
