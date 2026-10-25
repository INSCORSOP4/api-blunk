using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoporteBlunk.Domain.Entities.Kb;

namespace SoporteBlunk.Infrastructure.Persistence.Configurations.Kb;

internal sealed class BitacoraActividadConfiguration : IEntityTypeConfiguration<BitacoraActividad>
{
    public void Configure(EntityTypeBuilder<BitacoraActividad> b)
    {
        b.ToTable("Actividad", "bitacora");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("ActividadId").ValueGeneratedOnAdd();
        b.Property(x => x.Grupo).HasColumnType("varchar(40)");
        b.Property(x => x.Nombre).HasMaxLength(200);
        // Orden y Activa no declaran default: el repositorio siempre los escribe, y un default de la base (Activa = 1) haría que EF
        // omitiera un false explícito.
        b.Property(x => x.CreadoEn).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSDATETIME()").ValueGeneratedOnAdd();
        b.Property(x => x.CreadoPor).HasMaxLength(200);
        b.Property(x => x.ActualizadoEn).HasColumnType("datetime2(0)");
        b.Property(x => x.ActualizadoPor).HasMaxLength(200);
        b.HasIndex(x => new { x.Grupo, x.Nombre }).IsUnique().HasDatabaseName("UQ_bitacora_Actividad_Grupo_Nombre");
    }
}

internal sealed class BitacoraChecklistConfiguration : IEntityTypeConfiguration<BitacoraChecklist>
{
    public void Configure(EntityTypeBuilder<BitacoraChecklist> b)
    {
        b.ToTable("ChecklistDiario", "bitacora");
        b.HasKey(x => new { x.Fecha, x.ActividadId });
        b.Property(x => x.Fecha).HasColumnType("date").ValueGeneratedNever();
        b.Property(x => x.ActividadId).ValueGeneratedNever();
        b.Property(x => x.Estado).HasColumnType("varchar(12)").HasConversion<string>();
        b.Property(x => x.Observacion).HasMaxLength(1000);
        b.Property(x => x.CreadoEn).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSDATETIME()").ValueGeneratedOnAdd();
        b.Property(x => x.CreadoPor).HasMaxLength(200);
        b.Property(x => x.ActualizadoEn).HasColumnType("datetime2(0)");
        b.Property(x => x.ActualizadoPor).HasMaxLength(200);
    }
}
