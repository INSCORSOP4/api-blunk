using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoporteBlunk.Domain.Entities.Kb;

namespace SoporteBlunk.Infrastructure.Persistence.Configurations.Kb;

internal sealed class ArchivoEsperadoConfiguration : IEntityTypeConfiguration<ArchivoEsperado>
{
    public void Configure(EntityTypeBuilder<ArchivoEsperado> b)
    {
        b.ToTable("ArchivoEsperado", "archivos");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("ArchivoId").ValueGeneratedNever();
        b.Property(x => x.Ruta).HasMaxLength(260);
        b.Property(x => x.NombreArchivo).HasMaxLength(260);
    }
}

internal sealed class RevisionArchivoConfiguration : IEntityTypeConfiguration<RevisionArchivo>
{
    public void Configure(EntityTypeBuilder<RevisionArchivo> b)
    {
        b.ToTable("RevisionArchivo", "archivos");
        b.HasKey(x => new { x.Fecha, x.ArchivoId });
        b.Property(x => x.Fecha).HasColumnType("date").ValueGeneratedNever();
        b.Property(x => x.ArchivoId).ValueGeneratedNever();
        b.Property(x => x.Estado).HasColumnType("varchar(14)");
        b.Property(x => x.UltimaModificacion).HasColumnType("datetime2(0)");
        b.Property(x => x.Detalle).HasMaxLength(400);
        b.Property(x => x.RevisadoEn).HasColumnType("datetime2(0)");
    }
}
