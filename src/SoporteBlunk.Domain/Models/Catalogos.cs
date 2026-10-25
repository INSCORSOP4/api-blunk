namespace SoporteBlunk.Domain.Models;

public sealed record CatalogoItem(long Id, string Nombre, long? PadreId = null);

public sealed record Tecnico(long Id, string Nombre);
