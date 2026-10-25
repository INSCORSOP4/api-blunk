namespace SoporteBlunk.Domain.Entities.ServiceDesk;

// Tablas de catálogo de ServiceDesk Plus (esquema de terceros, solo lectura). Todas borran de forma lógica con ISDELETED.

public sealed class StatusDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class PriorityDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public int Orden { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class QueueDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class CategoryDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class SubCategoryDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public long CategoriaId { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class ItemDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public long SubCategoriaId { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class UrgencyDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class ImpactDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class ModeDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class RequestTypeDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class LevelDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}

public sealed class DepartmentDefinition
{
    public long Id { get; init; }
    public string? Nombre { get; init; }
    public bool Eliminado { get; init; }
}
