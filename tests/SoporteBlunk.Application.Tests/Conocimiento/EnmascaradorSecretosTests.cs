using FluentAssertions;
using SoporteBlunk.Application.Conocimiento;

namespace SoporteBlunk.Application.Tests.Conocimiento;

public class EnmascaradorSecretosTests
{
    // Todos los valores son inventados para las pruebas.
    private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dBjftJeZ4CVPmB92K27uhbUJU1p1rwW1gFWFOEjXk";
    private static readonly string Github = "ghp_" + new string('a', 36);

    [Theory]
    [InlineData("password: Zx9-Qm2!pL", "Zx9-Qm2!pL")]
    [InlineData("Server=db;User Id=sa;Password=Abc123xyz;Encrypt=True", "Abc123xyz")]
    [InlineData("\"password\": \"S3cr3t!\"", "S3cr3t!")]
    [InlineData("Contraseña: Prosur2024", "Prosur2024")]
    [InlineData("**Contraseña:** Prosur2024", "Prosur2024")]
    [InlineData("| password | Abc123xyz |", "Abc123xyz")]
    [InlineData("DB_PASSWORD=Qw3rty!9", "Qw3rty!9")]
    [InlineData("postgres://admin:Sup3rS3cret@10.0.0.5:5432/kb", "Sup3rS3cret")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz012345", "abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("api_key = \"sk-abcdefghijklmnopqrstuvwxyz\"", "sk-abcdefghijklmnopqrstuvwxyz")]
    [InlineData("token: 'a1b2c3d4e5'", "a1b2c3d4e5")]
    [InlineData("- clave: admin", "admin")]
    [InlineData("pwd=hunter22", "hunter22")]
    [InlineData("client_secret: 9f8e7d6c5b4a", "9f8e7d6c5b4a")]
    [InlineData("accessToken = zzTop1234", "zzTop1234")]
    [InlineData("password: Soporte.Blunk", "Soporte.Blunk")]       // una contraseña con punto no es una referencia de código
    [InlineData("password = \"request.Password\"", "request.Password")] // entre comillas siempre es un valor
    [InlineData("contraseña: admin.2026", "admin.2026")]
    [InlineData("Basic dXNlcjpwYXNzd29yZDEyMw==", "dXNlcjpwYXNzd29yZDEyMw==")]
    public void Oculta_el_valor_secreto_y_no_lo_devuelve_nunca(string entrada, string secreto)
    {
        var r = EnmascaradorSecretos.Enmascarar(entrada);

        r.Texto.Should().NotContain(secreto);
        r.Texto.Should().Contain(EnmascaradorSecretos.Marca);
        r.Cantidad.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Oculta_formatos_conocidos_de_token()
    {
        var texto = $"jwt {Jwt}\naws AKIAIOSFODNN7EXAMPLE\ngh {Github}";

        var r = EnmascaradorSecretos.Enmascarar(texto);

        r.Texto.Should().NotContain(Jwt).And.NotContain("AKIAIOSFODNN7EXAMPLE").And.NotContain(Github);
        r.Cantidad.Should().Be(3);
        r.Lineas.Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData("password: <tu contraseña>")]
    [InlineData("Password=${DB_PASSWORD};")]
    [InlineData("token: string")]
    [InlineData("password: ***")]
    [InlineData("contraseña: requerida")]
    [InlineData("El token expira en 8 horas.")]
    [InlineData("passenger: Juan Pérez")]
    [InlineData("bypass = true")]
    [InlineData("this.Password = request.Password;")]
    [InlineData("var clave = process.env.DB_PASSWORD;")]
    [InlineData("token: config.ApiToken")]
    [InlineData("Los tokens se renuevan: ver la sección de sesiones")]
    [InlineData("la clave primaria de la tabla")]
    [InlineData("| password | Contraseña del usuario | requerido |")]
    [InlineData("Authorization: Bearer <token>")]
    [InlineData("postgres://usuario:pass@host/db")]
    [InlineData("https://example.com/ruta?x=1")]
    [InlineData("password: null")]
    public void No_toca_marcadores_tipos_referencias_de_codigo_ni_prosa(string entrada)
    {
        var r = EnmascaradorSecretos.Enmascarar(entrada);

        r.Texto.Should().Be(entrada);
        r.Cantidad.Should().Be(0);
        r.Lineas.Should().BeEmpty();
    }

    [Fact]
    public void Reporta_solo_los_numeros_de_linea_de_cada_secreto()
    {
        var texto = "# Conexión\npassword: Abc12345\n\ntexto normal\nclave: Zeta9999\n";

        var r = EnmascaradorSecretos.Enmascarar(texto);

        r.Lineas.Should().Equal(2, 5);
        r.Cantidad.Should().Be(2);
        r.Texto.Split('\n').Should().HaveCount(texto.Split('\n').Length, "no se pierden ni se agregan líneas");
    }

    [Fact]
    public void Un_bloque_de_llave_privada_se_oculta_completo_y_conserva_la_numeracion_de_lineas()
    {
        var texto = string.Join('\n',
            "antes",
            "-----BEGIN RSA PRIVATE KEY-----",
            "MIIEvQIBADANBgkqhkiG9w0BAQEFAASC",
            "abcDEF123456",
            "-----END RSA PRIVATE KEY-----",
            "password: DespuesDelBloque9");

        var r = EnmascaradorSecretos.Enmascarar(texto);

        r.Texto.Should().NotContain("MIIEvQIBADANBgkqhkiG9w0BAQEFAASC").And.NotContain("BEGIN RSA").And.NotContain("DespuesDelBloque9");
        r.Lineas.Should().Equal(2, 6); // el segundo secreto sigue reportándose en su línea real
        r.Texto.Split('\n').Should().HaveCount(6);
    }

    [Fact]
    public void Oculta_tambien_dentro_de_bloques_de_codigo()
    {
        var texto = "```json\n{ \"password\": \"Ab12Cd34\" }\n```";

        var r = EnmascaradorSecretos.Enmascarar(texto);

        r.Texto.Should().NotContain("Ab12Cd34").And.Contain("```json");
    }

    [Fact]
    public void Es_idempotente_al_enmascarar_dos_veces()
    {
        var una = EnmascaradorSecretos.Enmascarar("password: Qwerty123\ntoken: Zz99Yy88");
        var dos = EnmascaradorSecretos.Enmascarar(una.Texto);

        dos.Texto.Should().Be(una.Texto);
        dos.Cantidad.Should().Be(0);
    }

    [Fact]
    public void Texto_enorme_sin_separadores_termina_rapido_o_falla_de_forma_controlada()
    {
        var texto = new string('a', 300_000) + "\npassword: Abc12345";

        var tarea = Task.Run(() =>
        {
            try { return EnmascaradorSecretos.Enmascarar(texto).Texto; }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return "TIMEOUT"; }
        });

        tarea.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("no puede colgarse");
        tarea.Result.Should().NotContain("Abc12345");
    }
}
