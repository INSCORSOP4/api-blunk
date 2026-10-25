using FluentValidation;
using MediatR;
using SoporteBlunk.Application.Common;
using SoporteBlunk.Application.Common.Exceptions;
using SoporteBlunk.Domain.Interfaces;
using SoporteBlunk.Domain.Models;

namespace SoporteBlunk.Application.Features.Tecnicos.Queries;

public sealed record ListTecnicosQuery : IRequest<IReadOnlyList<Tecnico>>;

public sealed class ListTecnicosHandler(ITecnicoReadRepository repo) : IRequestHandler<ListTecnicosQuery, IReadOnlyList<Tecnico>>
{
    public Task<IReadOnlyList<Tecnico>> Handle(ListTecnicosQuery r, CancellationToken ct) => repo.ListarAsync(ct);
}

public sealed record GetCargaTecnicosQuery : IRequest<IReadOnlyList<TecnicoCarga>>;

public sealed class GetCargaTecnicosHandler(ITecnicoReadRepository repo)
    : IRequestHandler<GetCargaTecnicosQuery, IReadOnlyList<TecnicoCarga>>
{
    public Task<IReadOnlyList<TecnicoCarga>> Handle(GetCargaTecnicosQuery r, CancellationToken ct) => repo.CargaAsync(ct);
}

public sealed record GetCargaTecnicoQuery(long TecnicoId) : IRequest<TecnicoCarga>;

public sealed class GetCargaTecnicoValidator : AbstractValidator<GetCargaTecnicoQuery>
{
    public GetCargaTecnicoValidator() => RuleFor(x => x.TecnicoId).GreaterThan(0);
}

public sealed class GetCargaTecnicoHandler(ITecnicoReadRepository repo) : IRequestHandler<GetCargaTecnicoQuery, TecnicoCarga>
{
    public async Task<TecnicoCarga> Handle(GetCargaTecnicoQuery r, CancellationToken ct) =>
        await repo.CargaAsync(r.TecnicoId, ct) ?? throw new NotFoundException("el técnico", r.TecnicoId);
}

public sealed record GetProductividadTecnicoQuery(long TecnicoId, DateOnly? Desde, DateOnly? Hasta) : IRequest<TecnicoProductividad>;

public sealed class GetProductividadTecnicoValidator : AbstractValidator<GetProductividadTecnicoQuery>
{
    public GetProductividadTecnicoValidator()
    {
        RuleFor(x => x.TecnicoId).GreaterThan(0);
        this.RangoValido(x => x.Desde, x => x.Hasta);
    }
}

public sealed class GetProductividadTecnicoHandler(ITecnicoReadRepository repo)
    : IRequestHandler<GetProductividadTecnicoQuery, TecnicoProductividad>
{
    public async Task<TecnicoProductividad> Handle(GetProductividadTecnicoQuery r, CancellationToken ct) =>
        await repo.ProductividadAsync(r.TecnicoId, r.Desde, r.Hasta, ct) ?? throw new NotFoundException("el técnico", r.TecnicoId);
}

/// <summary>Productividad de todo el equipo en el periodo, para comparar entre técnicos.</summary>
public sealed record GetProductividadTecnicosQuery(DateOnly? Desde, DateOnly? Hasta) : IRequest<IReadOnlyList<TecnicoProductividad>>;

public sealed class GetProductividadTecnicosValidator : AbstractValidator<GetProductividadTecnicosQuery>
{
    public GetProductividadTecnicosValidator() => this.RangoValido(x => x.Desde, x => x.Hasta);
}

public sealed class GetProductividadTecnicosHandler(ITecnicoReadRepository repo)
    : IRequestHandler<GetProductividadTecnicosQuery, IReadOnlyList<TecnicoProductividad>>
{
    public Task<IReadOnlyList<TecnicoProductividad>> Handle(GetProductividadTecnicosQuery r, CancellationToken ct) =>
        repo.ListarProductividadAsync(r.Desde, r.Hasta, ct);
}

/// <summary>Evolución de la productividad de un técnico por periodo, para ver si va mejorando o empeorando.</summary>
public sealed record GetTendenciaProductividadQuery(long TecnicoId, DateOnly? Desde, DateOnly? Hasta, Agrupacion Agrupacion)
    : IRequest<IReadOnlyList<ProductividadPunto>>;

public sealed class GetTendenciaProductividadValidator : AbstractValidator<GetTendenciaProductividadQuery>
{
    public GetTendenciaProductividadValidator()
    {
        RuleFor(x => x.TecnicoId).GreaterThan(0);
        RuleFor(x => x.Agrupacion).IsInEnum();
        this.RangoValido(x => x.Desde, x => x.Hasta);
    }
}

public sealed class GetTendenciaProductividadHandler(ITecnicoReadRepository repo)
    : IRequestHandler<GetTendenciaProductividadQuery, IReadOnlyList<ProductividadPunto>>
{
    public Task<IReadOnlyList<ProductividadPunto>> Handle(GetTendenciaProductividadQuery r, CancellationToken ct)
    {
        // Mismos valores por defecto que la tendencia del Tablero: 30 días, o 12 meses si se agrupa por mes.
        var hasta = r.Hasta ?? ZonaHoraria.Hoy();
        var desde = r.Desde ?? (r.Agrupacion == Agrupacion.Mes ? hasta.AddMonths(-11) : hasta.AddDays(-29));
        return repo.TendenciaProductividadAsync(r.TecnicoId, desde, hasta, r.Agrupacion, ct);
    }
}
