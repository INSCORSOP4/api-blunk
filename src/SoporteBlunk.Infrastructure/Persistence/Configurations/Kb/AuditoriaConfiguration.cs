using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoporteBlunk.Domain.Entities.Kb;

namespace SoporteBlunk.Infrastructure.Persistence.Configurations.Kb;

internal sealed class AuditoriaCambioMicrosipConfiguration : IEntityTypeConfiguration<AuditoriaCambioMicrosip>
{
    public void Configure(EntityTypeBuilder<AuditoriaCambioMicrosip> b)
    {
        b.ToTable("CambioMicrosip", "aud");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("CambioId").ValueGeneratedOnAdd();
        b.Property(x => x.Fecha).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSDATETIME()").ValueGeneratedOnAdd();
        b.Property(x => x.UsuarioEmail).HasMaxLength(256);
        b.Property(x => x.UsuarioNombre).HasMaxLength(200);
        b.Property(x => x.MicrosipNombre).HasMaxLength(200);
        b.Property(x => x.Accion).HasColumnType("varchar(12)").HasConversion<string>();
        b.Property(x => x.Resultado).HasColumnType("varchar(12)").HasConversion<string>();
        b.Property(x => x.Antes).HasMaxLength(1000);
        b.Property(x => x.Despues).HasMaxLength(1000);
        b.Property(x => x.Error).HasMaxLength(1000);
    }
}
