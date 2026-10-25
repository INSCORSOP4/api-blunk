using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Features.Utilerias.Queries.Jobs;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Tests.Utilerias;

public class ListJobsHandlerTests
{
    private static JobResumen Job(string nombre, EstadoJob estado, bool habilitado = true, params EstadoJob[] pasos) =>
        new(nombre, habilitado, estado, null, null, null, null,
            pasos.Select((e, i) => new JobPaso(i + 1, $"paso {i + 1}", e, null, null, null)).ToList());

    private static (ListJobsHandler handler, Mock<IJobsReadRepository> repo) Crear(params string[] vigilados)
    {
        var repo = new Mock<IJobsReadRepository>();
        var options = Options.Create(new UtileriasOptions { JobsVigilados = [.. vigilados] });
        return (new ListJobsHandler(repo.Object, options, TimeProvider.System), repo);
    }

    [Fact]
    public async Task Ordena_fallidos_primero_luego_en_ejecucion_y_deshabilitados_al_final()
    {
        var (handler, repo) = Crear("A", "B", "C", "D", "E");
        repo.Setup(r => r.ListarAsync(It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Job("E", EstadoJob.Exitoso, habilitado: false),
                Job("D", EstadoJob.Exitoso),
                Job("C", EstadoJob.EnEjecucion),
                Job("B", EstadoJob.Exitoso, true, EstadoJob.Exitoso, EstadoJob.Fallido), // paso fallido
                Job("A", EstadoJob.Fallido),
            ]);

        var r = await handler.Handle(new ListJobsQuery(), default);

        r.Jobs.Select(j => j.Nombre).Should().Equal("A", "B", "C", "D", "E");
    }

    [Fact]
    public async Task Solo_vigilados_consulta_la_lista_configurada_y_avisa_los_que_no_existen()
    {
        var (handler, repo) = Crear(" Chesa ", "chesa", "Fantasma");
        IReadOnlyCollection<string>? recibidos = null;
        repo.Setup(r => r.ListarAsync(It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>?, CancellationToken>((n, _) => recibidos = n)
            .ReturnsAsync([Job("Chesa", EstadoJob.Exitoso)]);

        var r = await handler.Handle(new ListJobsQuery(true), default);

        recibidos.Should().Equal("Chesa", "Fantasma"); // recortados y sin repetidos
        r.NoEncontrados.Should().Equal("Fantasma");
        r.SoloVigilados.Should().BeTrue();
    }

    [Fact]
    public async Task Todos_no_filtra_por_nombre()
    {
        var (handler, repo) = Crear("Chesa");
        repo.Setup(r => r.ListarAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync([Job("X", EstadoJob.Exitoso)]);

        var r = await handler.Handle(new ListJobsQuery(false), default);

        r.Jobs.Should().HaveCount(1);
        r.NoEncontrados.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_lista_configurada_y_solo_vigilados_no_trae_todos_por_error()
    {
        var (handler, repo) = Crear();

        var r = await handler.Handle(new ListJobsQuery(true), default);

        r.Jobs.Should().BeEmpty();
        repo.Verify(x => x.ListarAsync(It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
