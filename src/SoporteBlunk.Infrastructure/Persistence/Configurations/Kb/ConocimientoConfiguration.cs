using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoporteBlunk.Domain.Entities.Kb;

namespace SoporteBlunk.Infrastructure.Persistence.Configurations.Kb;

// Esquema kb de SoporteKB. Las fechas se declaran datetime2(0) con default SYSUTCDATETIME() (las llena la base). Los defaults de
// columnas numéricas o booleanas (Version = 1, Activa = 1, ...) no se declaran: el importador siempre escribe esos valores y EF omitiría
// un 0 o un false explícito si hubiera un default en el modelo.

internal sealed class KbVersionEsquemaConfiguration : IEntityTypeConfiguration<KbVersionEsquema>
{
    public void Configure(EntityTypeBuilder<KbVersionEsquema> b)
    {
        b.ToTable("VersionEsquema", "kb");
        b.HasKey(x => x.Version);
        b.Property(x => x.Version).ValueGeneratedNever();
        b.Property(x => x.Descripcion).HasMaxLength(200);
        b.Property(x => x.AplicadaEn).HasColumnType("datetime2(0)");
    }
}

internal sealed class KbImportacionConfiguration : IEntityTypeConfiguration<KbImportacion>
{
    public void Configure(EntityTypeBuilder<KbImportacion> b)
    {
        b.ToTable("Importacion", "kb");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("ImportacionId").ValueGeneratedOnAdd();
        b.Property(x => x.UsuarioNombre).HasMaxLength(100);
        b.Property(x => x.UsuarioEmail).HasMaxLength(100);
        b.Property(x => x.Origen).HasMaxLength(20);
        b.Property(x => x.Estado).HasMaxLength(20);
        b.Property(x => x.IniciadaEn).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
        b.Property(x => x.TerminadaEn).HasColumnType("datetime2(0)");
    }
}

internal sealed class KbDocumentoConfiguration : IEntityTypeConfiguration<KbDocumento>
{
    public void Configure(EntityTypeBuilder<KbDocumento> b)
    {
        b.ToTable("Documento", "kb");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("DocumentoId").ValueGeneratedOnAdd();
        b.Property(x => x.RutaRelativa).HasMaxLength(450);
        b.Property(x => x.Titulo).HasMaxLength(300);
        b.Property(x => x.Carpeta).HasMaxLength(450);
        b.Property(x => x.Resumen).HasMaxLength(500);
        b.Property(x => x.Estado).HasMaxLength(50);
        b.Property(x => x.HashOrigen).HasColumnType("binary(32)");
        b.Property(x => x.ModificadoEnOrigen).HasColumnType("datetime2(0)");
        b.Property(x => x.EliminadoEn).HasColumnType("datetime2(0)");
        b.Property(x => x.CreadoEn).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
        b.Property(x => x.CreadoPorNombre).HasMaxLength(100);
        b.Property(x => x.ActualizadoEn).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
        b.Property(x => x.ActualizadoPorNombre).HasMaxLength(100);
        b.HasIndex(x => x.RutaRelativa).IsUnique().HasDatabaseName("UQ_Documento_Ruta");
    }
}

internal sealed class KbDocumentoVersionConfiguration : IEntityTypeConfiguration<KbDocumentoVersion>
{
    public void Configure(EntityTypeBuilder<KbDocumentoVersion> b)
    {
        b.ToTable("DocumentoVersion", "kb");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("VersionId").ValueGeneratedOnAdd();
        b.Property(x => x.HashOrigen).HasColumnType("binary(32)");
        b.Property(x => x.UsuarioNombre).HasMaxLength(100);
        b.Property(x => x.CreadoEn).HasColumnType("datetime2(0)").HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
    }
}

internal sealed class KbDocumentoAliasConfiguration : IEntityTypeConfiguration<KbDocumentoAlias>
{
    public void Configure(EntityTypeBuilder<KbDocumentoAlias> b)
    {
        b.ToTable("DocumentoAlias", "kb");
        b.HasKey(x => new { x.DocumentoId, x.Alias });
        b.Property(x => x.DocumentoId).ValueGeneratedNever();
        b.Property(x => x.Alias).HasMaxLength(300);
    }
}

internal sealed class KbEtiquetaConfiguration : IEntityTypeConfiguration<KbEtiqueta>
{
    public void Configure(EntityTypeBuilder<KbEtiqueta> b)
    {
        b.ToTable("Etiqueta", "kb");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("EtiquetaId").ValueGeneratedOnAdd();
        b.Property(x => x.Nombre).HasMaxLength(100);
    }
}

internal sealed class KbDocumentoEtiquetaConfiguration : IEntityTypeConfiguration<KbDocumentoEtiqueta>
{
    public void Configure(EntityTypeBuilder<KbDocumentoEtiqueta> b)
    {
        b.ToTable("DocumentoEtiqueta", "kb");
        b.HasKey(x => new { x.DocumentoId, x.EtiquetaId });
        b.Property(x => x.DocumentoId).ValueGeneratedNever();
        b.Property(x => x.EtiquetaId).ValueGeneratedNever();
    }
}

internal sealed class KbEnlaceConfiguration : IEntityTypeConfiguration<KbEnlace>
{
    public void Configure(EntityTypeBuilder<KbEnlace> b)
    {
        b.ToTable("Enlace", "kb");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("EnlaceId").ValueGeneratedOnAdd();
        b.Property(x => x.Destino).HasMaxLength(450);
        b.Property(x => x.Seccion).HasMaxLength(300);
        b.Property(x => x.TextoVisible).HasMaxLength(300);
    }
}

internal sealed class KbReglaOmisionConfiguration : IEntityTypeConfiguration<KbReglaOmision>
{
    public void Configure(EntityTypeBuilder<KbReglaOmision> b)
    {
        b.ToTable("ReglaOmision", "kb");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("ReglaId").ValueGeneratedOnAdd();
        b.Property(x => x.Tipo).HasMaxLength(10);
        b.Property(x => x.Patron).HasMaxLength(450);
        b.Property(x => x.Motivo).HasMaxLength(200);
    }
}
