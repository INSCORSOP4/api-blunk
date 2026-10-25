using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using SoporteBlunk.Application.Abstractions;
using SoporteBlunk.Application.Common.Options;
using SoporteBlunk.Application.Conocimiento;

namespace SoporteBlunk.Application.Features.Conocimiento.Commands.ImportarNotas;

public sealed record ImportarNotasCommand(IReadOnlyList<ArchivoSubido> Archivos, string Origen) : IRequest<InformeImportacionDto>;

/// <summary>Estados posibles de cada archivo en el informe que ve el usuario.</summary>
public static class EstadoInforme
{
    public const string Nueva = "Nueva";
    public const string Actualizada = "Actualizada";
    public const string SinCambios = "SinCambios";
    public const string Omitida = "Omitida";
    public const string Error = "Error";
}

public sealed record ArchivoInformeDto(
    string Ruta,
    string Estado,
    string? Motivo,
    string? Titulo,
    int SecretosEnmascarados,
    IReadOnlyList<int> LineasConSecretos,
    IReadOnlyList<string> Advertencias,
    int EnlacesRotos);

public sealed record InformeImportacionDto(
    int? ImportacionId,
    int Total,
    int Nuevas,
    int Actualizadas,
    int SinCambios,
    int Omitidas,
    int Errores,
    int ConSecretos,
    int SecretosEnmascarados,
    int EnlacesRotos,
    IReadOnlyList<ArchivoInformeDto> Archivos);

public sealed class ImportarNotasValidator : AbstractValidator<ImportarNotasCommand>
{
    public ImportarNotasValidator()
    {
        RuleFor(x => x.Archivos).NotEmpty().WithMessage("Selecciona al menos un archivo.");
        RuleFor(x => x.Origen).Must(o => o is "archivos" or "zip" or "carpeta");
    }
}

public sealed class ImportarNotasHandler(
    IKbImportacionRepository repo,
    IUsuarioActualProvider usuarioActual,
    IOptions<ConocimientoOptions> opciones) : IRequestHandler<ImportarNotasCommand, InformeImportacionDto>
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<InformeImportacionDto> Handle(ImportarNotasCommand request, CancellationToken ct)
    {
        var usuario = usuarioActual.Obtener();

        // Los archivos se procesan (y se enmascaran) ANTES de tocar la base: nada sin revisar llega a SoporteKB.
        var reglas = new ReglasOmisionService(await repo.ReglasOmisionAsync(ct));
        var procesado = new ProcesadorNotas(reglas, opciones.Value).ProcesarLote(request.Archivos);

        var notas = procesado.Archivos.Select(a => a.Nota).OfType<NotaProcesada>().ToList();
        IReadOnlyList<NotaGuardada> guardadas = [];
        int? importacionId = null;

        if (notas.Count > 0)
        {
            var resumen = new ResumenArchivos(procesado.Total, procesado.Omitidos, procesado.Errores);
            var guardado = await repo.GuardarAsync(
                new ContextoImportacion(usuario, request.Origen), notas, resumen,
                g => JsonSerializer.Serialize(ConstruirArchivos(procesado, g), Json), ct);
            importacionId = guardado.ImportacionId;
            guardadas = guardado.Notas;
        }

        var archivos = ConstruirArchivos(procesado, guardadas);
        return new InformeImportacionDto(
            importacionId,
            procesado.Total,
            guardadas.Count(g => g.Resultado == ResultadoNota.Nueva),
            guardadas.Count(g => g.Resultado == ResultadoNota.Actualizada),
            guardadas.Count(g => g.Resultado == ResultadoNota.SinCambios),
            procesado.Omitidos,
            procesado.Errores,
            procesado.ConSecretos,
            procesado.SecretosEnmascarados,
            guardadas.Sum(g => g.EnlacesRotos),
            archivos);
    }

    /// <summary>Informe por archivo. No incluye contenido, hashes ni valores enmascarados: solo qué pasó y en qué líneas revisar.</summary>
    internal static IReadOnlyList<ArchivoInformeDto> ConstruirArchivos(InformeProcesamiento procesado, IReadOnlyList<NotaGuardada> guardadas)
    {
        var porRuta = guardadas.ToDictionary(g => g.Ruta, StringComparer.OrdinalIgnoreCase);
        return procesado.Archivos.Select(a =>
        {
            if (a.Nota is { } n && porRuta.TryGetValue(n.RutaRelativa, out var g))
                return new ArchivoInformeDto(n.RutaRelativa, g.Resultado switch
                {
                    ResultadoNota.Nueva => EstadoInforme.Nueva,
                    ResultadoNota.Actualizada => EstadoInforme.Actualizada,
                    _ => EstadoInforme.SinCambios,
                }, null, n.Titulo, n.SecretosEnmascarados, n.LineasConSecretos, n.Advertencias, g.EnlacesRotos);

            return new ArchivoInformeDto(
                a.Ruta,
                a.Estado == EstadoArchivo.Omitido ? EstadoInforme.Omitida : EstadoInforme.Error,
                a.Motivo, null, 0, [], [], 0);
        }).ToList();
    }
}
