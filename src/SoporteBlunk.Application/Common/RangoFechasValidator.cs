using FluentValidation;

namespace SoporteBlunk.Application.Common;

public static class RangoFechasValidator
{
    /// <summary>Un rango razonable: Desde &lt;= Hasta y no más de 10 años.</summary>
    public static void RangoValido<T>(this AbstractValidator<T> v, Func<T, DateOnly?> desde, Func<T, DateOnly?> hasta)
    {
        v.RuleFor(x => x).Must(x => desde(x) is null || hasta(x) is null || desde(x) <= hasta(x))
            .WithMessage("'desde' no puede ser posterior a 'hasta'.")
            .Must(x => desde(x) is null || hasta(x) is null || hasta(x)!.Value.DayNumber - desde(x)!.Value.DayNumber <= 3660)
            .WithMessage("El rango de fechas no puede exceder 10 años.");
    }
}
