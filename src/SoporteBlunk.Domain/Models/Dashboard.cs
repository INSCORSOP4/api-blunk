namespace SoporteBlunk.Domain.Models;

public sealed record ConteoPorClave(long? Id, string Nombre, int Total);

public sealed record DashboardResumen(
    int Total,
    int Abiertos,
    int Cerrados,
    int Vencidos,
    int CreadosHoy,
    int CerradosHoy,
    int SinAsignar,
    double? PromedioHorasResolucion,
    IReadOnlyList<ConteoPorClave> PorEstado);

public enum Agrupacion { Dia, Semana, Mes }

public sealed record TendenciaPunto(DateOnly Periodo, int Creados, int Cerrados);

public sealed record SlaResumen(int Total, int Vencidos, int EnTiempo, double PorcentajeCumplimiento);

public sealed record TecnicoCarga(long TecnicoId, string Tecnico, int Abiertos, int Vencidos, int Total);

public sealed record TecnicoProductividad(
    long TecnicoId,
    string Tecnico,
    int Asignados,
    int Cerrados,
    int Vencidos,
    double CumplimientoSlaPct,
    double? PromedioHorasPrimeraRespuesta,
    double? PromedioHorasResolucion);

/// <summary>Un punto de la tendencia de productividad de un técnico (un día/semana/mes).</summary>
public sealed record ProductividadPunto(
    DateOnly Periodo,
    int Asignados,
    int Cerrados,
    double CumplimientoSlaPct,
    double? PromedioHorasPrimeraRespuesta,
    double? PromedioHorasResolucion);
