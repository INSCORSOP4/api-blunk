using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Dashboard.Queries;

// ---------- Resumen ----------
public sealed record GetDashboardResumenQuery(DateOnly? Desde, DateOnly? Hasta) : IRequest<DashboardResumen>;

public sealed class GetDashboardResumenValidator : AbstractValidator<GetDashboardResumenQuery>
{
    public GetDashboardResumenValidator() => this.RangoValido(x => x.Desde, x => x.Hasta);
}

public sealed class GetDashboardResumenHandler(IDashboardReadRepository repo)
    : IRequestHandler<GetDashboardResumenQuery, DashboardResumen>
{
    public Task<DashboardResumen> Handle(GetDashboardResumenQuery r, CancellationToken ct) =>
        repo.ResumenAsync(r.Desde, r.Hasta, ct);
}

// ---------- Tendencia ----------
public sealed record GetDashboardTendenciaQuery(DateOnly? Desde, DateOnly? Hasta, Agrupacion Agrupacion)
    : IRequest<IReadOnlyList<TendenciaPunto>>;

public sealed class GetDashboardTendenciaValidator : AbstractValidator<GetDashboardTendenciaQuery>
{
    public GetDashboardTendenciaValidator()
    {
        this.RangoValido(x => x.Desde, x => x.Hasta);
        RuleFor(x => x.Agrupacion).IsInEnum();
    }
}

public sealed class GetDashboardTendenciaHandler(IDashboardReadRepository repo)
    : IRequestHandler<GetDashboardTendenciaQuery, IReadOnlyList<TendenciaPunto>>
{
    public Task<IReadOnlyList<TendenciaPunto>> Handle(GetDashboardTendenciaQuery r, CancellationToken ct)
    {
        // Por defecto: los últimos 30 días (o 12 meses si se agrupa por mes).
        var hasta = r.Hasta ?? ZonaHoraria.Hoy();
        var desde = r.Desde ?? (r.Agrupacion == Agrupacion.Mes ? hasta.AddMonths(-11) : hasta.AddDays(-29));
        return repo.TendenciaAsync(desde, hasta, r.Agrupacion, ct);
    }
}

// ---------- Distribución por dimensión ----------
public sealed record GetDashboardPorQuery(string Dimension, DateOnly? Desde, DateOnly? Hasta)
    : IRequest<IReadOnlyList<ConteoPorClave>>;

public sealed class GetDashboardPorHandler(IDashboardReadRepository repo)
    : IRequestHandler<GetDashboardPorQuery, IReadOnlyList<ConteoPorClave>>
{
    public static readonly IReadOnlySet<string> Dimensiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "estado", "categoria", "prioridad", "grupo", "tecnico", "modo", "urgencia", "impacto", "tipo-solicitud",
    };

    public Task<IReadOnlyList<ConteoPorClave>> Handle(GetDashboardPorQuery r, CancellationToken ct) =>
        Dimensiones.Contains(r.Dimension)
            ? repo.PorAsync(r.Dimension, r.Desde, r.Hasta, ct)
            : throw new NotFoundException("la dimensión", r.Dimension);
}

public sealed class GetDashboardPorValidator : AbstractValidator<GetDashboardPorQuery>
{
    public GetDashboardPorValidator() => this.RangoValido(x => x.Desde, x => x.Hasta);
}

// ---------- SLA ----------
public sealed record GetDashboardSlaQuery(DateOnly? Desde, DateOnly? Hasta) : IRequest<SlaResumen>;

public sealed class GetDashboardSlaValidator : AbstractValidator<GetDashboardSlaQuery>
{
    public GetDashboardSlaValidator() => this.RangoValido(x => x.Desde, x => x.Hasta);
}

public sealed class GetDashboardSlaHandler(IDashboardReadRepository repo) : IRequestHandler<GetDashboardSlaQuery, SlaResumen>
{
    public Task<SlaResumen> Handle(GetDashboardSlaQuery r, CancellationToken ct) => repo.SlaAsync(r.Desde, r.Hasta, ct);
}
