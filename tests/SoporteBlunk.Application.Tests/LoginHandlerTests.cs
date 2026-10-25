using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Features.Auth.Commands.Login;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Tests;

public class LoginHandlerTests
{
    private static readonly Guid Rol = Guid.Parse("20367A46-AA41-480A-9FC0-44E0FA439C64");
    private static readonly AuthOptions Opciones = new() { RolSistemasId = Rol };

    private readonly Mock<ISeguridadClient> _seguridad = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly Mock<ILoginThrottle> _throttle = new();

    public LoginHandlerTests() =>
        _tokens.Setup(t => t.Emitir(It.IsAny<UsuarioSesion>()))
            .Returns(new SesionEmitida("jwt", DateTimeOffset.UtcNow.AddHours(8)));

    private LoginHandler Crear() => new(_seguridad.Object, _tokens.Object, _throttle.Object, Options.Create(Opciones));

    private static UsuarioSeguridad Usuario(string tipo = "U", string status = "A", Guid? rol = null) =>
        new(Guid.NewGuid(), "Ana Pérez", "ana@prosur.com.mx", tipo, status, rol ?? Rol);

    private void ConUsuario(UsuarioSeguridad u) =>
        _seguridad.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(u);

    [Fact]
    public async Task Usuario_valido_recibe_sesion_y_reinicia_el_conteo_de_fallos()
    {
        ConUsuario(Usuario());

        var sesion = await Crear().Handle(new LoginCommand("  ANA@Prosur.com.mx ", "x"), default);

        sesion.Token.Should().Be("jwt");
        sesion.Usuario.Email.Should().Be("ana@prosur.com.mx");
        _throttle.Verify(t => t.Reiniciar("ana@prosur.com.mx"), Times.Once);
    }

    [Fact]
    public async Task Credenciales_incorrectas_registran_fallo_y_no_emiten_token()
    {
        _seguridad.Setup(s => s.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsuarioSeguridad?)null);

        var act = () => Crear().Handle(new LoginCommand("ana@prosur.com.mx", "mal"), default);

        await act.Should().ThrowAsync<CredencialesInvalidasException>();
        _throttle.Verify(t => t.RegistrarFallo("ana@prosur.com.mx"), Times.Once);
        _tokens.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("R", "A")] // la fila del rol (ROL-SISTEMAS) también tiene contraseña en CAT_USUARIOS
    [InlineData("U", "I")] // usuario inactivo
    [InlineData("U", "")]
    public async Task Tipo_o_estado_no_validos_son_rechazados_aunque_la_contrasena_sea_correcta(string tipo, string status)
    {
        ConUsuario(Usuario(tipo, status));

        var act = () => Crear().Handle(new LoginCommand("ana@prosur.com.mx", "ok"), default);

        await act.Should().ThrowAsync<AccesoDenegadoException>();
        _tokens.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Usuario_de_otro_rol_es_rechazado()
    {
        ConUsuario(Usuario(rol: Guid.NewGuid()));

        var act = () => Crear().Handle(new LoginCommand("ana@prosur.com.mx", "ok"), default);

        await act.Should().ThrowAsync<AccesoDenegadoException>();
    }

    [Fact]
    public async Task Correo_bloqueado_no_consulta_a_Seguridad()
    {
        _throttle.Setup(t => t.ObtenerBloqueo("ana@prosur.com.mx")).Returns(TimeSpan.FromMinutes(10));

        var act = () => Crear().Handle(new LoginCommand("ana@prosur.com.mx", "x"), default);

        (await act.Should().ThrowAsync<DemasiadosIntentosException>()).Which.Espera.Should().Be(TimeSpan.FromMinutes(10));
        _seguridad.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("no-es-correo", "x")]
    [InlineData("ana@prosur.com.mx", "")]
    public void Validador_rechaza_entradas_vacias_o_mal_formadas(string email, string password) =>
        new LoginValidator().Validate(new LoginCommand(email, password)).IsValid.Should().BeFalse();
}
