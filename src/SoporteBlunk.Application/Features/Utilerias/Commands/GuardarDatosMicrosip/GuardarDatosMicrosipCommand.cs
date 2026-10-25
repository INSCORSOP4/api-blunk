using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Utilerias.Commands.GuardarDatosMicrosip;

/// <summary>
/// Asigna a un usuario de MICROSIP la clave de FORTIA. El nombre, la empresa y el puesto NO vienen del cliente:
/// se leen de FORTIA en el servidor, así nadie puede escribir datos inventados.
/// </summary>
public sealed record GuardarDatosMicrosipCommand(int UsuarioId, int ClaveFortia) : IRequest<UsuarioMicrosip>;

public sealed class GuardarDatosMicrosipValidator : AbstractValidator<GuardarDatosMicrosipCommand>
{
    public GuardarDatosMicrosipValidator()
    {
        RuleFor(x => x.UsuarioId).GreaterThan(0);
        RuleFor(x => x.ClaveFortia).GreaterThan(0).WithMessage("La clave de FORTIA debe ser un número positivo.");
    }
}

public sealed class GuardarDatosMicrosipHandler(
    IMicrosipRepository microsip,
    IFortiaRepository fortia,
    IAuditoriaMicrosipRepository auditoria,
    IUsuarioActualProvider usuarioActual,
    ILogger<GuardarDatosMicrosipHandler> logger) : IRequestHandler<GuardarDatosMicrosipCommand, UsuarioMicrosip>
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<UsuarioMicrosip> Handle(GuardarDatosMicrosipCommand request, CancellationToken ct)
    {
        var quien = usuarioActual.Obtener();

        var usuario = await microsip.ObtenerUsuarioAsync(request.UsuarioId, ct)
            ?? throw new NotFoundException("el usuario de MICROSIP", request.UsuarioId);
        var trabajador = await fortia.ObtenerTrabajadorAsync(request.ClaveFortia, ct)
            ?? throw new NotFoundException("el trabajador de FORTIA", request.ClaveFortia);

        var datos = new DatosAdicionales(
            trabajador.Clave.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trabajador.NombreCompleto,
            trabajador.Empresa,
            trabajador.Puesto);
        ValidarLongitudes(datos);

        var accion = usuario.TieneDatosAdicionales ? AccionMicrosip.Actualizar : AccionMicrosip.Insertar;
        var antes = usuario.TieneDatosAdicionales
            ? JsonSerializer.Serialize(new DatosAdicionales(usuario.ClaveFortia ?? "", usuario.NombreUsuario ?? "", usuario.Empresa ?? "", usuario.Puesto ?? ""), Json)
            : null;
        var despues = JsonSerializer.Serialize(datos, Json);

        // 1) Bitácora primero: si no se puede registrar, no se escribe nada en MICROSIP.
        var cambioId = await auditoria.RegistrarAsync(quien, usuario, accion, antes, despues, ct);

        // 2) Escritura. A partir de aquí ya no se cancela con el token de la petición: el resultado debe quedar anotado.
        try
        {
            await microsip.GuardarDatosAdicionalesAsync(usuario.UsuarioId, datos, usuario.TieneDatosAdicionales, ct);
        }
        catch (Exception ex)
        {
            await MarcarSinCancelarAsync(cambioId, ResultadoCambio.Fallido, ex.Message);
            logger.LogWarning(ex, "Falló el cambio {CambioId} en MICROSIP (usuario {UsuarioMicrosip}) pedido por {Email}", cambioId, usuario.UsuarioId, quien.Email);
            throw;
        }

        // 3) Verificación: se vuelve a leer y se compara con lo escrito.
        var actualizado = await microsip.ObtenerUsuarioAsync(usuario.UsuarioId, CancellationToken.None);
        var coincide = actualizado is { TieneDatosAdicionales: true }
            && actualizado.ClaveFortia == datos.ClaveFortia
            && actualizado.NombreUsuario == datos.NombreUsuario
            && actualizado.Empresa == datos.Empresa
            && actualizado.Puesto == datos.Puesto;

        await MarcarSinCancelarAsync(cambioId, coincide ? ResultadoCambio.Aplicado : ResultadoCambio.NoVerificado,
            coincide ? null : "Se escribió, pero al volver a leer los datos no coinciden (posible diferencia de codificación).");
        logger.LogInformation("Cambio {CambioId} en MICROSIP: usuario {UsuarioMicrosip} {Accion} por {Email} (verificado: {Verificado})",
            cambioId, usuario.UsuarioId, accion, quien.Email, coincide);

        return actualizado ?? usuario;
    }

    private async Task MarcarSinCancelarAsync(long id, ResultadoCambio resultado, string? error)
    {
        try { await auditoria.MarcarAsync(id, resultado, error is null ? null : Recortar(error), CancellationToken.None); }
        catch (Exception ex) { logger.LogError(ex, "No se pudo anotar el resultado {Resultado} del cambio {CambioId}", resultado, id); }
    }

    private static string Recortar(string s) => s.Length <= 900 ? s : s[..900];

    private static void ValidarLongitudes(DatosAdicionales d)
    {
        void Revisar(string campo, string valor, int max)
        {
            if (valor.Length > max)
                throw new ValidationException($"{campo} de FORTIA mide {valor.Length} caracteres y MICROSIP solo admite {max}; no se guardó nada.");
        }
        Revisar("La clave", d.ClaveFortia, DatosAdicionales.MaxClave);
        Revisar("El nombre", d.NombreUsuario, DatosAdicionales.MaxNombre);
        Revisar("La empresa", d.Empresa, DatosAdicionales.MaxEmpresa);
        Revisar("El puesto", d.Puesto, DatosAdicionales.MaxPuesto);
    }
}
