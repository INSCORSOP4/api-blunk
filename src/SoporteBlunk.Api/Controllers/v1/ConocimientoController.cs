using System.Text.Json;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Conocimiento;
using SoporteBlunk.Application.Features.Conocimiento.Commands.ImportarNotas;
using SoporteBlunk.Application.Features.Conocimiento.Queries.Documentos;
using SoporteBlunk.Application.Features.Conocimiento.Queries.GetEstado;
using SoporteBlunk.Application.Features.Conocimiento.Queries.Importaciones;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Api.Controllers.v1;

[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/conocimiento")]
public sealed class ConocimientoController(ISender sender, IOptions<ConocimientoOptions> opciones) : ControllerBase
{
    // Tope duro de la petición (el configurable en ConocimientoOptions solo puede ser menor).
    private const long TopeDuro = 60L * 1024 * 1024;
    private const int MaxArchivosEnPeticion = 2000;

    /// <summary>Estado de la base de conocimiento: versión del esquema, si hay búsqueda de texto completo y cuántas notas hay.</summary>
    [HttpGet("estado")]
    [ProducesResponseType<KbEstado>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Estado(CancellationToken ct) => Ok(await sender.Send(new GetKbEstadoQuery(), ct));

    /// <summary>
    /// Sube notas .md (varios archivos, una carpeta o un .zip) como multipart/form-data.
    /// Campos: <c>archivos</c> (uno o más archivos) y, opcional, <c>rutas</c> (la ruta relativa de cada archivo, en el mismo orden)
    /// y <c>modificados</c> (fecha de modificación en milisegundos Unix, en el mismo orden).
    /// </summary>
    [HttpPost("importaciones")]
    [EnableRateLimiting("importar")]
    [RequestSizeLimit(TopeDuro)]
    [RequestFormLimits(MultipartBodyLengthLimit = TopeDuro, ValueCountLimit = 4000, MultipartHeadersLengthLimit = 16 * 1024)]
    [ProducesResponseType<InformeImportacionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Importar(CancellationToken ct)
    {
        var limites = opciones.Value;
        if (!Request.HasFormContentType)
            throw new SubidaInvalidaException("La subida debe enviarse como multipart/form-data.");
        if (Request.ContentLength is { } largo && largo > Math.Min(limites.MaxBytesPeticion, TopeDuro))
            throw new SubidaInvalidaException($"La subida supera el máximo de {Math.Min(limites.MaxBytesPeticion, TopeDuro) / (1024 * 1024)} MB.");

        var form = await Request.ReadFormAsync(ct);
        var archivosForm = form.Files;
        if (archivosForm.Count == 0) throw new SubidaInvalidaException("Selecciona al menos un archivo.");
        if (archivosForm.Count > MaxArchivosEnPeticion)
            throw new SubidaInvalidaException($"Se enviaron demasiados archivos (máximo {MaxArchivosEnPeticion} por petición).");

        var rutas = form["rutas"].ToArray();
        var modificados = form["modificados"].ToArray();
        var presupuesto = new LectorZip.Presupuesto(limites.MaxBytesDescomprimidos);
        var archivos = new List<ArchivoSubido>();
        var hayZip = false;
        var hayCarpeta = false;

        for (var i = 0; i < archivosForm.Count; i++)
        {
            var archivo = archivosForm[i];
            var nombre = i < rutas.Length && !string.IsNullOrWhiteSpace(rutas[i]) ? rutas[i]! : archivo.FileName;

            if (nombre.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                hayZip = true;
                await using var zip = archivo.OpenReadStream();
                archivos.AddRange(LectorZip.Leer(zip, limites, presupuesto, nombre));
                continue;
            }

            if (nombre.Contains('/') || nombre.Contains('\\')) hayCarpeta = true;
            await using var flujo = archivo.OpenReadStream();
            var contenido = LectorZip.LeerAcotado(flujo, limites.MaxBytesPorArchivo + 1);
            presupuesto.Consumir(contenido.Length);
            if (presupuesto.BytesRestantes < 0)
                throw new SubidaInvalidaException("El contenido de la subida supera el tamaño total permitido.");

            DateTimeOffset? modificado = i < modificados.Length && long.TryParse(modificados[i], out var ms) && ms is > 0 and < 253_402_300_799_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : null;
            archivos.Add(new ArchivoSubido(nombre, contenido, modificado));
        }

        // El origen lo decide el servidor según lo recibido; no se confía en lo que diga el navegador.
        var origen = hayZip ? "zip" : hayCarpeta ? "carpeta" : "archivos";
        return Ok(await sender.Send(new ImportarNotasCommand(archivos, origen), ct));
    }

    /// <summary>
    /// Lista y busca notas. <c>buscar</c>: cada palabra debe aparecer (sin distinguir acentos ni mayúsculas) en el título, contenido,
    /// alias o etiquetas. <c>carpeta</c> incluye subcarpetas. <c>orden</c>: Recientes (por defecto) o Titulo.
    /// </summary>
    [HttpGet("documentos")]
    [ProducesResponseType<PagedResult<DocumentoResumen>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Documentos(
        [FromQuery] string? buscar, [FromQuery] string? carpeta, [FromQuery] string? etiqueta,
        [FromQuery] OrdenDocumentos orden = OrdenDocumentos.Recientes,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new ListDocumentosQuery(
            new DocumentoFiltro { Buscar = buscar, Carpeta = carpeta, Etiqueta = etiqueta, Orden = orden }, page, pageSize), ct));

    /// <summary>Una nota con su contenido (ya sin secretos), etiquetas, enlaces, quién la enlaza y su historial.</summary>
    [HttpGet("documentos/{id:int}")]
    [ProducesResponseType<DocumentoDetalle>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Documento(int id, CancellationToken ct) => Ok(await sender.Send(new GetDocumentoQuery(id), ct));

    /// <summary>Carpetas con el número de notas de cada una (el árbol se arma con las rutas).</summary>
    [HttpGet("carpetas")]
    [ProducesResponseType<IReadOnlyList<CarpetaConteo>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Carpetas(CancellationToken ct) => Ok(await sender.Send(new ListCarpetasQuery(), ct));

    [HttpGet("etiquetas")]
    [ProducesResponseType<IReadOnlyList<EtiquetaConteo>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Etiquetas(CancellationToken ct) => Ok(await sender.Send(new ListEtiquetasQuery(), ct));

    /// <summary>Bitácora: quién subió notas, cuándo y con qué resultado (las más recientes primero).</summary>
    [HttpGet("importaciones")]
    [ProducesResponseType<IReadOnlyList<ImportacionResumen>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Importaciones([FromQuery] int maximo = 50, CancellationToken ct = default) =>
        Ok(await sender.Send(new ListImportacionesQuery(maximo), ct));

    /// <summary>Detalle de una importación con su informe por archivo (sin contenido ni valores enmascarados).</summary>
    [HttpGet("importaciones/{id:int}")]
    public async Task<IActionResult> Importacion(int id, CancellationToken ct = default)
    {
        var detalle = await sender.Send(new GetImportacionQuery(id), ct);
        object? informe = null;
        if (!string.IsNullOrWhiteSpace(detalle.InformeJson))
            informe = JsonDocument.Parse(detalle.InformeJson).RootElement.Clone();
        return Ok(new { resumen = detalle.Resumen, informe });
    }
}
