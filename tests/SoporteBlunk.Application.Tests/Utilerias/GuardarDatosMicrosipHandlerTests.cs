using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Features.Utilerias.Commands.GuardarDatosMicrosip;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Tests.Utilerias;

public class GuardarDatosMicrosipHandlerTests
{
    private static readonly UsuarioActual Quien = new(Guid.NewGuid(), "Persona Soporte", "soporte@prosur.mx");
    private static readonly TrabajadorFortia Trabajador = new(1234, "Ana Pérez López", "PROSUR SA", "Analista");

    private static UsuarioMicrosip Usuario(bool conDatos, string? clave = null) =>
        new(7, "ANA.PEREZ", "A", true, clave, conDatos ? "Nombre viejo" : null, conDatos ? "Empresa vieja" : null, conDatos ? "Puesto viejo" : null, conDatos);

    private static UsuarioMicrosip Aplicado => Usuario(true, "1234") with { NombreUsuario = Trabajador.NombreCompleto, Empresa = Trabajador.Empresa, Puesto = Trabajador.Puesto };

    private sealed class Contexto
    {
        public Mock<IMicrosipRepository> Microsip { get; } = new(MockBehavior.Strict);
        public Mock<IFortiaRepository> Fortia { get; } = new(MockBehavior.Strict);
        public Mock<IAuditoriaMicrosipRepository> Auditoria { get; } = new(MockBehavior.Strict);
        public List<string> Orden { get; } = [];

        public GuardarDatosMicrosipHandler Handler()
        {
            var usuario = new Mock<IUsuarioActualProvider>();
            usuario.Setup(u => u.Obtener()).Returns(Quien);
            return new(Microsip.Object, Fortia.Object, Auditoria.Object, usuario.Object, NullLogger<GuardarDatosMicrosipHandler>.Instance);
        }

        public void Preparar(UsuarioMicrosip? previo, TrabajadorFortia? trabajador, params UsuarioMicrosip?[] lecturas)
        {
            var cola = new Queue<UsuarioMicrosip?>([previo, .. lecturas]);
            Microsip.Setup(m => m.ObtenerUsuarioAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(() => cola.Dequeue());
            Fortia.Setup(f => f.ObtenerTrabajadorAsync(1234, It.IsAny<CancellationToken>())).ReturnsAsync(trabajador);
            Auditoria.Setup(a => a.RegistrarAsync(It.IsAny<UsuarioActual>(), It.IsAny<UsuarioMicrosip>(), It.IsAny<AccionMicrosip>(),
                    It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => Orden.Add("auditoria")).ReturnsAsync(99L);
            Auditoria.Setup(a => a.MarcarAsync(99L, It.IsAny<ResultadoCambio>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<long, ResultadoCambio, string?, CancellationToken>((_, r, _, _) => Orden.Add($"marca:{r}")).Returns(Task.CompletedTask);
        }

        public void Escritura(Func<Task>? efecto = null) =>
            Microsip.Setup(m => m.GuardarDatosAdicionalesAsync(7, It.IsAny<DatosAdicionales>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback(() => Orden.Add("escritura")).Returns(efecto ?? (() => Task.CompletedTask));
    }

    [Fact]
    public async Task Inserta_los_datos_de_FORTIA_registra_antes_y_marca_aplicado()
    {
        var c = new Contexto();
        c.Preparar(Usuario(conDatos: false), Trabajador, Aplicado);
        c.Escritura();

        var r = await c.Handler().Handle(new GuardarDatosMicrosipCommand(7, 1234), default);

        c.Orden.Should().Equal("auditoria", "escritura", "marca:Aplicado"); // bitácora ANTES de escribir
        c.Microsip.Verify(m => m.GuardarDatosAdicionalesAsync(7,
            It.Is<DatosAdicionales>(d => d.ClaveFortia == "1234" && d.NombreUsuario == "Ana Pérez López" && d.Empresa == "PROSUR SA" && d.Puesto == "Analista"),
            false, It.IsAny<CancellationToken>()), Times.Once);
        c.Auditoria.Verify(a => a.RegistrarAsync(Quien, It.IsAny<UsuarioMicrosip>(), AccionMicrosip.Insertar, null, It.IsAny<string>(), It.IsAny<CancellationToken>()));
        r.ClaveFortia.Should().Be("1234");
    }

    [Fact]
    public async Task Actualiza_cuando_ya_tiene_datos_y_guarda_el_antes()
    {
        var c = new Contexto();
        c.Preparar(Usuario(conDatos: true, clave: "55"), Trabajador, Aplicado);
        c.Escritura();

        await c.Handler().Handle(new GuardarDatosMicrosipCommand(7, 1234), default);

        c.Microsip.Verify(m => m.GuardarDatosAdicionalesAsync(7, It.IsAny<DatosAdicionales>(), true, It.IsAny<CancellationToken>()));
        c.Auditoria.Verify(a => a.RegistrarAsync(Quien, It.IsAny<UsuarioMicrosip>(), AccionMicrosip.Actualizar,
            It.Is<string?>(antes => antes != null && antes.Contains("Nombre viejo") && antes.Contains("\"55\"")), It.IsAny<string>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Si_la_escritura_falla_marca_fallido_y_relanza()
    {
        var c = new Contexto();
        c.Preparar(Usuario(false), Trabajador);
        c.Escritura(() => throw new UtileriaNoDisponibleException("sin permiso"));

        var act = () => c.Handler().Handle(new GuardarDatosMicrosipCommand(7, 1234), default);

        await act.Should().ThrowAsync<UtileriaNoDisponibleException>();
        c.Orden.Should().Equal("auditoria", "escritura", "marca:Fallido");
    }

    [Fact]
    public async Task Si_la_bitacora_falla_no_se_escribe_nada()
    {
        var c = new Contexto();
        c.Microsip.Setup(m => m.ObtenerUsuarioAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Usuario(false));
        c.Fortia.Setup(f => f.ObtenerTrabajadorAsync(1234, It.IsAny<CancellationToken>())).ReturnsAsync(Trabajador);
        c.Auditoria.Setup(a => a.RegistrarAsync(It.IsAny<UsuarioActual>(), It.IsAny<UsuarioMicrosip>(), It.IsAny<AccionMicrosip>(),
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UtileriaNoDisponibleException("sin bitácora"));

        var act = () => c.Handler().Handle(new GuardarDatosMicrosipCommand(7, 1234), default);

        await act.Should().ThrowAsync<UtileriaNoDisponibleException>();
        c.Microsip.Verify(m => m.GuardarDatosAdicionalesAsync(It.IsAny<int>(), It.IsAny<DatosAdicionales>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Si_al_releer_no_coincide_queda_no_verificado()
    {
        var c = new Contexto();
        c.Preparar(Usuario(false), Trabajador, Usuario(true, "1234")); // lo leído conserva "Nombre viejo"
        c.Escritura();

        await c.Handler().Handle(new GuardarDatosMicrosipCommand(7, 1234), default);

        c.Orden.Should().EndWith("marca:NoVerificado");
    }

    [Fact]
    public async Task Usuario_o_trabajador_inexistente_es_404_sin_tocar_bitacora_ni_escritura()
    {
        var c = new Contexto();
        c.Microsip.Setup(m => m.ObtenerUsuarioAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync((UsuarioMicrosip?)null);
        await ((Func<Task>)(() => c.Handler().Handle(new GuardarDatosMicrosipCommand(7, 1234), default))).Should().ThrowAsync<NotFoundException>();

        var c2 = new Contexto();
        c2.Microsip.Setup(m => m.ObtenerUsuarioAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Usuario(false));
        c2.Fortia.Setup(f => f.ObtenerTrabajadorAsync(1234, It.IsAny<CancellationToken>())).ReturnsAsync((TrabajadorFortia?)null);
        await ((Func<Task>)(() => c2.Handler().Handle(new GuardarDatosMicrosipCommand(7, 1234), default))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Datos_de_FORTIA_mas_largos_que_las_columnas_de_MICROSIP_no_se_guardan()
    {
        var c = new Contexto();
        var largo = Trabajador with { Empresa = new string('E', DatosAdicionales.MaxEmpresa + 1) };
        c.Microsip.Setup(m => m.ObtenerUsuarioAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Usuario(false));
        c.Fortia.Setup(f => f.ObtenerTrabajadorAsync(1234, It.IsAny<CancellationToken>())).ReturnsAsync(largo);

        var act = () => c.Handler().Handle(new GuardarDatosMicrosipCommand(7, 1234), default);

        (await act.Should().ThrowAsync<ValidationException>()).WithMessage("*empresa*50*");
    }

    [Theory]
    [InlineData(0, 5, false)]
    [InlineData(7, 0, false)]
    [InlineData(7, -3, false)]
    [InlineData(7, 1234, true)]
    public void Validador_pide_ids_positivos(int usuario, int clave, bool valido) =>
        new GuardarDatosMicrosipValidator().Validate(new GuardarDatosMicrosipCommand(usuario, clave)).IsValid.Should().Be(valido);
}
