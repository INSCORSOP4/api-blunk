namespace SoporteBlunk.Domain.Models;

/// <summary>Resultado de la última ejecución de un job o de uno de sus pasos (valores de msdb.dbo.sysjobhistory.run_status).</summary>
public enum EstadoJob
{
    SinHistorial = -1,
    Fallido = 0,
    Exitoso = 1,
    Reintento = 2,
    Cancelado = 3,
    EnEjecucion = 4,
}

public sealed record JobPaso(
    int Id,
    string Nombre,
    EstadoJob Estado,
    DateTime? UltimaEjecucion,
    int? DuracionSegundos,
    string? Mensaje);

public sealed record JobResumen(
    string Nombre,
    bool Habilitado,
    EstadoJob Estado,
    DateTime? UltimaEjecucion,
    int? DuracionSegundos,
    DateTime? SiguienteEjecucion,
    int? PasoEnEjecucion,
    IReadOnlyList<JobPaso> Pasos);

public sealed record JobsResultado(
    bool SoloVigilados,
    IReadOnlyList<string> NoEncontrados,
    IReadOnlyList<JobResumen> Jobs,
    DateTime ConsultadoEn);
