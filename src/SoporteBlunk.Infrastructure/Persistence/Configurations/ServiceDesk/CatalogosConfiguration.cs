using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoporteBlunk.Domain.Entities.ServiceDesk;

namespace SoporteBlunk.Infrastructure.Persistence.Configurations.ServiceDesk;

/// <summary>
/// Mapea una tabla de catálogo de ServiceDesk: nombres de columna del producto, sin generación de claves.
/// Todas las entidades comparten las propiedades Id, Nombre y Eliminado.
/// </summary>
internal static class CatalogoMapeo
{
    public static void Mapear<T>(EntityTypeBuilder<T> b, string tabla, string id, string nombre) where T : class
    {
        b.ToTable(tabla);
        b.HasKey("Id");
        b.Property<long>("Id").HasColumnName(id).ValueGeneratedNever();
        b.Property<string?>("Nombre").HasColumnName(nombre);
        b.Property<bool>("Eliminado").HasColumnName("ISDELETED");
    }
}

internal sealed class StatusDefinitionConfiguration : IEntityTypeConfiguration<StatusDefinition>
{
    public void Configure(EntityTypeBuilder<StatusDefinition> b) =>
        CatalogoMapeo.Mapear(b, "StatusDefinition", "STATUSID", "STATUSNAME");
}

internal sealed class PriorityDefinitionConfiguration : IEntityTypeConfiguration<PriorityDefinition>
{
    public void Configure(EntityTypeBuilder<PriorityDefinition> b)
    {
        CatalogoMapeo.Mapear(b, "PriorityDefinition", "PRIORITYID", "PRIORITYNAME");
        b.Property(x => x.Orden).HasColumnName("PRIORITYORDER");
    }
}

internal sealed class QueueDefinitionConfiguration : IEntityTypeConfiguration<QueueDefinition>
{
    public void Configure(EntityTypeBuilder<QueueDefinition> b) =>
        CatalogoMapeo.Mapear(b, "QueueDefinition", "QUEUEID", "QUEUENAME");
}

internal sealed class CategoryDefinitionConfiguration : IEntityTypeConfiguration<CategoryDefinition>
{
    public void Configure(EntityTypeBuilder<CategoryDefinition> b) =>
        CatalogoMapeo.Mapear(b, "CategoryDefinition", "CATEGORYID", "CATEGORYNAME");
}

internal sealed class SubCategoryDefinitionConfiguration : IEntityTypeConfiguration<SubCategoryDefinition>
{
    public void Configure(EntityTypeBuilder<SubCategoryDefinition> b)
    {
        CatalogoMapeo.Mapear(b, "SubCategoryDefinition", "SUBCATEGORYID", "NAME");
        b.Property(x => x.CategoriaId).HasColumnName("CATEGORYID");
    }
}

internal sealed class ItemDefinitionConfiguration : IEntityTypeConfiguration<ItemDefinition>
{
    public void Configure(EntityTypeBuilder<ItemDefinition> b)
    {
        CatalogoMapeo.Mapear(b, "ItemDefinition", "ITEMID", "NAME");
        b.Property(x => x.SubCategoriaId).HasColumnName("SUBCATEGORYID");
    }
}

internal sealed class UrgencyDefinitionConfiguration : IEntityTypeConfiguration<UrgencyDefinition>
{
    public void Configure(EntityTypeBuilder<UrgencyDefinition> b) =>
        CatalogoMapeo.Mapear(b, "UrgencyDefinition", "URGENCYID", "NAME");
}

internal sealed class ImpactDefinitionConfiguration : IEntityTypeConfiguration<ImpactDefinition>
{
    public void Configure(EntityTypeBuilder<ImpactDefinition> b) =>
        CatalogoMapeo.Mapear(b, "ImpactDefinition", "IMPACTID", "NAME");
}

internal sealed class ModeDefinitionConfiguration : IEntityTypeConfiguration<ModeDefinition>
{
    public void Configure(EntityTypeBuilder<ModeDefinition> b) =>
        CatalogoMapeo.Mapear(b, "ModeDefinition", "MODEID", "MODENAME");
}

internal sealed class RequestTypeDefinitionConfiguration : IEntityTypeConfiguration<RequestTypeDefinition>
{
    public void Configure(EntityTypeBuilder<RequestTypeDefinition> b) =>
        CatalogoMapeo.Mapear(b, "RequestTypeDefinition", "REQUESTTYPEID", "NAME");
}

internal sealed class LevelDefinitionConfiguration : IEntityTypeConfiguration<LevelDefinition>
{
    public void Configure(EntityTypeBuilder<LevelDefinition> b) =>
        CatalogoMapeo.Mapear(b, "LevelDefinition", "LEVELID", "LEVELNAME");
}

internal sealed class DepartmentDefinitionConfiguration : IEntityTypeConfiguration<DepartmentDefinition>
{
    public void Configure(EntityTypeBuilder<DepartmentDefinition> b) =>
        CatalogoMapeo.Mapear(b, "DepartmentDefinition", "DEPTID", "DEPTNAME");
}
