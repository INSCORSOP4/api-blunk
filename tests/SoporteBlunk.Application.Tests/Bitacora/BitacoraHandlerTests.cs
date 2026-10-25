using FluentAssertions;
using FluentValidation;
using Moq;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Features.Bitacora.Commands.ActualizarActividad;
using SoporteBlunk.Application.Features.Bitacora.Commands.CrearActividad;
using SoporteBlunk.Application.Features.Bitacora.Commands.GuardarChecklist;
using SoporteBlunk.Application.Features.Bitacora.Queries.GetChecklist;
using SoporteBlunk.Application.Features.Bitacora.Queries.GetResumen;
using SoporteBlunk.Application.Features.Bitacora.Queries.ListActividades;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Tests.Bitacora;

public class BitacoraHandlerTests
{
    private static readonly UsuarioActual Quien = new(Guid.NewGuid(), "Persona Soporte", "soporte@prosur.mx");
    private const string Grupo = BitacoraGrupos.ActividadesSoft;
    private static DateOnly Hoy => ZonaHoraria.Hoy();

    private static Mock<IUsuarioActualProvider> UsuarioMock()
    {
        var m = new Mock<IUsuarioActualProvider>();
        m.Setup(u => u.Obtener()).Returns(Quien);
        return m;
    }

    // ---------------------------------------------------------------- validadores

    [Fact]
    public void ListActividades_rechaza_grupo_desconocido()
    {
        new ListActividadesValidator().Validate(new ListActividadesQuery(Grupo, false)).IsValid.Should().BeTrue();
        new ListActividadesValidator().Validate(new ListActividadesQuery("otro-grupo", false)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void GetChecklist_acepta_hoy_y_ayer_pero_no_manana_ni_fechas_muy_antiguas()
    {
        new GetChecklistValidator().Validate(new GetChecklistQuery(Grupo, Hoy)).IsValid.Should().BeTrue();
        new GetChecklistValidator().Validate(new GetChecklistQuery(Grupo, Hoy.AddDays(-1))).IsValid.Should().BeTrue();
        new GetChecklistValidator().Validate(new GetChecklistQuery(Grupo, Hoy.AddDays(1))).IsValid.Should().BeFalse();
        new GetChecklistValidator().Validate(new GetChecklistQuery(Grupo, new DateOnly(2019, 12, 31))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void GetResumen_rechaza_rango_invertido_o_mayor_a_un_anio()
    {
        new GetResumenValidator().Validate(new GetResumenQuery(Grupo, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1))).IsValid.Should().BeFalse();
        new GetResumenValidator().Validate(new GetResumenQuery(Grupo, new DateOnly(2024, 1, 1), new DateOnly(2026, 6, 1))).IsValid.Should().BeFalse();
        new GetResumenValidator().Validate(new GetResumenQuery(Grupo, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CrearActividad_requiere_nombre_y_grupo_valido()
    {
        new CrearActividadValidator().Validate(new CrearActividadCommand(Grupo, "")).IsValid.Should().BeFalse();
        new CrearActividadValidator().Validate(new CrearActividadCommand("x", "Nombre")).IsValid.Should().BeFalse();
        new CrearActividadValidator().Validate(new CrearActividadCommand(Grupo, new string('a', 201))).IsValid.Should().BeFalse();
        new CrearActividadValidator().Validate(new CrearActividadCommand(Grupo, "Nombre válido")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ActualizarActividad_exige_al_menos_un_cambio()
    {
        new ActualizarActividadValidator().Validate(new ActualizarActividadCommand(1, null, null, null)).IsValid.Should().BeFalse();
        new ActualizarActividadValidator().Validate(new ActualizarActividadCommand(1, null, true, null)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(EstadoChecklist.Advertencia, null, false)]
    [InlineData(EstadoChecklist.Falla, "", false)]
    [InlineData(EstadoChecklist.Falla, "   ", false)]
    [InlineData(EstadoChecklist.Critico, "Se cayó el servicio", true)]
    [InlineData(EstadoChecklist.Ok, null, true)]
    [InlineData(EstadoChecklist.NoAplica, null, true)]
    public void GuardarChecklist_exige_observacion_salvo_en_Ok_y_NoAplica(EstadoChecklist estado, string? obs, bool valido) =>
        new GuardarChecklistValidator().Validate(new GuardarChecklistCommand(1, Hoy, estado, obs)).IsValid.Should().Be(valido);

    // ---------------------------------------------------------------- handlers

    [Fact]
    public async Task ActualizarActividad_404_si_no_existe()
    {
        var repo = new Mock<IBitacoraRepository>();
        repo.Setup(r => r.ObtenerActividadAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync((ActividadBitacora?)null);
        var handler = new ActualizarActividadHandler(repo.Object, UsuarioMock().Object);

        var act = () => handler.Handle(new ActualizarActividadCommand(99, "Nuevo", null, null), default);

        await act.Should().ThrowAsync<NotFoundException>();
        repo.Verify(r => r.ActualizarActividadAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActualizarActividad_pasa_el_nombre_del_usuario_actual()
    {
        var repo = new Mock<IBitacoraRepository>();
        repo.Setup(r => r.ObtenerActividadAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActividadBitacora(1, Grupo, "X", 1, true));
        repo.Setup(r => r.ActualizarActividadAsync(1, "Y", null, null, Quien.Nombre, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActividadBitacora(1, Grupo, "Y", 1, true));

        var r = await new ActualizarActividadHandler(repo.Object, UsuarioMock().Object)
            .Handle(new ActualizarActividadCommand(1, "Y", null, null), default);

        r.Nombre.Should().Be("Y");
    }

    [Fact]
    public async Task GuardarChecklist_404_si_la_actividad_no_existe()
    {
        var repo = new Mock<IBitacoraRepository>();
        repo.Setup(r => r.ObtenerActividadAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync((ActividadBitacora?)null);
        var handler = new GuardarChecklistHandler(repo.Object, UsuarioMock().Object);

        var act = () => handler.Handle(new GuardarChecklistCommand(5, Hoy, EstadoChecklist.Ok, null), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GuardarChecklist_recorta_la_observacion_y_la_vuelve_null_si_queda_vacia()
    {
        var repo = new Mock<IBitacoraRepository>();
        repo.Setup(r => r.ObtenerActividadAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActividadBitacora(1, Grupo, "X", 1, true));
        repo.Setup(r => r.GuardarChecklistAsync(1, Hoy, EstadoChecklist.Ok, null, Quien.Nombre, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemChecklist(1, "X", 1, true, EstadoChecklist.Ok, null, Quien.Nombre, DateTimeOffset.Now, null, null));

        await new GuardarChecklistHandler(repo.Object, UsuarioMock().Object)
            .Handle(new GuardarChecklistCommand(1, Hoy, EstadoChecklist.Ok, "   "), default);

        repo.Verify(r => r.GuardarChecklistAsync(1, Hoy, EstadoChecklist.Ok, null, Quien.Nombre, It.IsAny<CancellationToken>()));
    }
}
