using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Domain.Models;
using SoporteBlunk.Infrastructure.Security;

namespace SoporteBlunk.Infrastructure.IntegrationTests;

internal sealed class FakeTime(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _ahora = inicio;
    public override DateTimeOffset GetUtcNow() => _ahora;
    public void Avanzar(TimeSpan t) => _ahora += t;
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public HttpRequestMessage? Ultima { get; private set; }
    public string? CuerpoUltimo { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Ultima = request;
        CuerpoUltimo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return responder(request);
    }
}

public class SeguridadClientTests
{
    private const string Id = "a5eee025-db4c-4f39-82ef-03dce55eb0ee";
    private const string Rol = "20367a46-aa41-480a-9fc0-44e0fa439c64";

    private static (SeguridadClient cliente, StubHandler handler) Crear(Func<HttpRequestMessage, HttpResponseMessage> r)
    {
        var handler = new StubHandler(r);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://seguridad.test:8819/") };
        return (new SeguridadClient(http), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Usuario(string estilo) => estilo == "MAYUS"
        ? $$"""{"USUARIO_ID":"{{Id}}","NOMBRE":"Ana","EMAIL":"a@x.mx","TIPO":"U","STATUS":"A","ID_ROL":"{{Rol}}","TOKEN":"t"}"""
        : $$"""{"usuarioId":"{{Id}}","nombre":"Ana","email":"a@x.mx","tipo":"U","status":"A","idRol":"{{Rol}}"}""";

    [Theory]
    [InlineData("MAYUS")]
    [InlineData("camel")]
    public async Task Mapea_la_respuesta_sin_importar_el_estilo_de_los_nombres(string estilo)
    {
        var (cliente, _) = Crear(_ => Json(Usuario(estilo)));

        var u = await cliente.LoginAsync("a@x.mx", "pw", default);

        u.Should().Be(new UsuarioSeguridad(Guid.Parse(Id), "Ana", "a@x.mx", "U", "A", Guid.Parse(Rol)));
    }

    [Fact]
    public async Task Envia_USER_y_PASSWORD_a_la_ruta_de_login()
    {
        var (cliente, handler) = Crear(_ => Json(Usuario("MAYUS")));

        await cliente.LoginAsync("a@x.mx", "pw", default);

        handler.Ultima!.RequestUri!.AbsoluteUri.Should().Be("https://seguridad.test:8819/api/Seguridad/Login");
        handler.Ultima.Method.Should().Be(HttpMethod.Post);
        handler.CuerpoUltimo.Should().Contain("\"USER\":\"a@x.mx\"").And.Contain("\"PASSWORD\":\"pw\"");
    }

    [Fact]
    public async Task Mensaje_de_usuario_no_encontrado_es_credencial_incorrecta()
    {
        var (cliente, _) = Crear(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("No se encontró el usuario con los datos ingresados"),
        });

        (await cliente.LoginAsync("a@x.mx", "mal", default)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Error de conexion a la base de datos")] // Seguridad usa 400 también para fallos internos
    [InlineData(HttpStatusCode.InternalServerError, "x")]
    [InlineData(HttpStatusCode.BadGateway, "x")]
    public async Task Fallos_internos_son_servicio_no_disponible_y_no_credenciales_incorrectas(HttpStatusCode code, string cuerpo)
    {
        var (cliente, _) = Crear(_ => new HttpResponseMessage(code) { Content = new StringContent(cuerpo) });

        var act = () => cliente.LoginAsync("a@x.mx", "pw", default);

        await act.Should().ThrowAsync<ServicioNoDisponibleException>();
    }

    [Fact]
    public async Task Respuesta_sin_los_campos_esperados_es_servicio_no_disponible()
    {
        var (cliente, _) = Crear(_ => Json("""{"otro":1}"""));

        var act = () => cliente.LoginAsync("a@x.mx", "pw", default);

        await act.Should().ThrowAsync<ServicioNoDisponibleException>();
    }

    [Fact]
    public async Task Error_de_red_es_servicio_no_disponible()
    {
        var (cliente, _) = Crear(_ => throw new HttpRequestException("sin red"));

        var act = () => cliente.LoginAsync("a@x.mx", "pw", default);

        await act.Should().ThrowAsync<ServicioNoDisponibleException>();
    }
}

public class JwtTokenServiceTests
{
    [Fact]
    public void El_token_lleva_los_datos_del_usuario_y_expira_en_las_horas_configuradas()
    {
        var tiempo = new FakeTime(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        var jwt = new JwtOptions { JwtKey = new string('k', 40) };
        var svc = new JwtTokenService(Options.Create(jwt), Options.Create(new AuthOptions { HorasSesion = 8 }), tiempo);
        var usuario = new UsuarioSesion(Guid.NewGuid(), "Ana", "a@x.mx", Guid.NewGuid());

        var sesion = svc.Emitir(usuario);

        sesion.ExpiraEn.Should().Be(tiempo.GetUtcNow().AddHours(8));
        var leido = new JwtSecurityTokenHandler().ReadJwtToken(sesion.Token);
        leido.Subject.Should().Be(usuario.Id.ToString());
        leido.Claims.Should().Contain(c => c.Type == ClaimNames.RolId && c.Value == usuario.RolId.ToString());
        leido.Claims.Should().Contain(c => c.Type == "email" && c.Value == "a@x.mx");
        leido.Claims.Select(c => c.Type).Should().NotContain(t => t.Contains("pass", StringComparison.OrdinalIgnoreCase));
    }
}

public class LoginThrottleTests
{
    private static (LoginThrottle t, FakeTime tiempo) Crear(int max = 3, int minutos = 10)
    {
        var tiempo = new FakeTime(DateTimeOffset.UtcNow);
        var opciones = Options.Create(new AuthOptions { MaxIntentosFallidos = max, MinutosBloqueo = minutos });
        return (new LoginThrottle(opciones, tiempo), tiempo);
    }

    [Fact]
    public void Bloquea_al_alcanzar_el_maximo_de_fallos()
    {
        var (t, _) = Crear();
        t.RegistrarFallo("a");
        t.RegistrarFallo("a");
        t.ObtenerBloqueo("a").Should().BeNull();

        t.RegistrarFallo("a");

        t.ObtenerBloqueo("a").Should().NotBeNull().And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        t.ObtenerBloqueo("b").Should().BeNull("el bloqueo es por correo");
    }

    [Fact]
    public void El_bloqueo_termina_con_el_tiempo()
    {
        var (t, tiempo) = Crear();
        for (var i = 0; i < 3; i++) t.RegistrarFallo("a");

        tiempo.Avanzar(TimeSpan.FromMinutes(11));

        t.ObtenerBloqueo("a").Should().BeNull();
    }

    [Fact]
    public void Un_login_correcto_reinicia_el_conteo()
    {
        var (t, _) = Crear();
        t.RegistrarFallo("a");
        t.RegistrarFallo("a");
        t.Reiniciar("a");
        t.RegistrarFallo("a");
        t.RegistrarFallo("a");

        t.ObtenerBloqueo("a").Should().BeNull();
    }

    [Fact]
    public void Los_fallos_viejos_no_se_acumulan_entre_ventanas()
    {
        var (t, tiempo) = Crear();
        t.RegistrarFallo("a");
        t.RegistrarFallo("a");
        tiempo.Avanzar(TimeSpan.FromMinutes(11));

        t.RegistrarFallo("a");

        t.ObtenerBloqueo("a").Should().BeNull();
    }
}
