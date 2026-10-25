using FluentAssertions;
using Moq;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Features.Conocimiento.Queries.Documentos;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Tests.Conocimiento;

public class DocumentosQueriesTests
{
    private static readonly ListDocumentosValidator Validador = new();

    private static ListDocumentosQuery Q(int page = 1, int size = 20, string? buscar = null, string? carpeta = null, string? etiqueta = null) =>
        new(new DocumentoFiltro { Buscar = buscar, Carpeta = carpeta, Etiqueta = etiqueta }, page, size);

    [Fact]
    public void Acepta_los_valores_por_defecto() => Validador.Validate(Q()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, ListDocumentosValidator.MaxPageSize + 1)]
    public void Rechaza_paginacion_fuera_de_rango(int page, int size) => Validador.Validate(Q(page, size)).IsValid.Should().BeFalse();

    [Fact]
    public void Rechaza_textos_desmedidos()
    {
        Validador.Validate(Q(buscar: new string('a', 201))).IsValid.Should().BeFalse();
        Validador.Validate(Q(carpeta: new string('a', 451))).IsValid.Should().BeFalse();
        Validador.Validate(Q(etiqueta: new string('a', 101))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Una_nota_inexistente_responde_NotFound()
    {
        var repo = new Mock<IKbReadRepository>();
        repo.Setup(r => r.ObtenerDocumentoAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync((DocumentoDetalle?)null);

        var act = () => new GetDocumentoHandler(repo.Object).Handle(new GetDocumentoQuery(5), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
