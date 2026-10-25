using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SoporteBlunk.Domain.Entities.ServiceDesk;

namespace SoporteBlunk.Infrastructure.Persistence.Configurations.ServiceDesk;

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> b)
    {
        b.ToTable("WorkOrder");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("WORKORDERID").ValueGeneratedNever();
        b.Property(x => x.Titulo).HasColumnName("TITLE");
        b.Property(x => x.SolicitanteId).HasColumnName("REQUESTERID");
        b.Property(x => x.ModoId).HasColumnName("MODEID");
        b.Property(x => x.DepartamentoId).HasColumnName("DEPTID");
        b.Property(x => x.Creado).HasColumnName("CREATEDTIME");
        b.Property(x => x.Vence).HasColumnName("DUEBYTIME");
        b.Property(x => x.Completado).HasColumnName("COMPLETEDTIME");
        b.Property(x => x.Respondido).HasColumnName("RESPONDEDTIME");
        b.Property(x => x.Resuelto).HasColumnName("RESOLVEDTIME");
    }
}

internal sealed class WorkOrderStatesConfiguration : IEntityTypeConfiguration<WorkOrderStates>
{
    public void Configure(EntityTypeBuilder<WorkOrderStates> b)
    {
        b.ToTable("WorkOrderStates");
        b.HasKey(x => x.TicketId);
        b.Property(x => x.TicketId).HasColumnName("WORKORDERID").ValueGeneratedNever();
        b.Property(x => x.EstadoId).HasColumnName("STATUSID");
        b.Property(x => x.PrioridadId).HasColumnName("PRIORITYID");
        b.Property(x => x.CategoriaId).HasColumnName("CATEGORYID");
        b.Property(x => x.SubcategoriaId).HasColumnName("SUBCATEGORYID");
        b.Property(x => x.ItemId).HasColumnName("ITEMID");
        b.Property(x => x.UrgenciaId).HasColumnName("URGENCYID");
        b.Property(x => x.ImpactoId).HasColumnName("IMPACTID");
        b.Property(x => x.TipoSolicitudId).HasColumnName("REQUESTTYPEID");
        b.Property(x => x.NivelId).HasColumnName("LEVELID");
        b.Property(x => x.TecnicoId).HasColumnName("OWNERID");
        b.Property(x => x.Vencido).HasColumnName("ISOVERDUE");
        b.Property(x => x.Reabierto).HasColumnName("REOPENED");
    }
}

internal sealed class WorkOrderQueueConfiguration : IEntityTypeConfiguration<WorkOrderQueue>
{
    public void Configure(EntityTypeBuilder<WorkOrderQueue> b)
    {
        b.ToTable("WorkOrder_Queue");
        b.HasNoKey();
        b.Property(x => x.TicketId).HasColumnName("WORKORDERID");
        b.Property(x => x.GrupoId).HasColumnName("QUEUEID");
    }
}

internal sealed class WorkOrderToDescriptionConfiguration : IEntityTypeConfiguration<WorkOrderToDescription>
{
    public void Configure(EntityTypeBuilder<WorkOrderToDescription> b)
    {
        b.ToTable("WorkOrderToDescription");
        b.HasNoKey();
        b.Property(x => x.TicketId).HasColumnName("WORKORDERID");
        b.Property(x => x.Descripcion).HasColumnName("FULLDESCRIPTION");
    }
}

internal sealed class RequestResolutionConfiguration : IEntityTypeConfiguration<RequestResolution>
{
    public void Configure(EntityTypeBuilder<RequestResolution> b)
    {
        b.ToTable("RequestResolution");
        b.HasNoKey();
        b.Property(x => x.TicketId).HasColumnName("REQUESTID");
        b.Property(x => x.Resolucion).HasColumnName("RESOLUTION");
        b.Property(x => x.Actualizado).HasColumnName("LASTUPDATEDTIME");
    }
}

internal sealed class WorkOrderHistoryConfiguration : IEntityTypeConfiguration<WorkOrderHistory>
{
    public void Configure(EntityTypeBuilder<WorkOrderHistory> b)
    {
        b.ToTable("WorkOrderHistory");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("HISTORYID").ValueGeneratedNever();
        b.Property(x => x.TicketId).HasColumnName("WORKORDERID");
        b.Property(x => x.Fecha).HasColumnName("OPERATIONTIME");
        b.Property(x => x.Operacion).HasColumnName("OPERATION");
        b.Property(x => x.Descripcion).HasColumnName("DESCRIPTION");
        b.Property(x => x.UsuarioId).HasColumnName("OPERATIONOWNERID");
    }
}

internal sealed class WoAssessmentConfiguration : IEntityTypeConfiguration<WoAssessment>
{
    public void Configure(EntityTypeBuilder<WoAssessment> b)
    {
        b.ToTable("WO_Assessment");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("ASSESSMENTID").ValueGeneratedNever();
        b.Property(x => x.TicketId).HasColumnName("WORKORDERID");
        b.Property(x => x.Ejecutado).HasColumnName("EXECUTEDTIME");
    }
}

internal sealed class WoStatusInfoConfiguration : IEntityTypeConfiguration<WoStatusInfo>
{
    public void Configure(EntityTypeBuilder<WoStatusInfo> b)
    {
        b.ToTable("WO_Status_Info");
        b.HasNoKey();
        b.Property(x => x.AssessmentId).HasColumnName("ASSESSMENTID");
        b.Property(x => x.EstadoId).HasColumnName("STATUSID");
        b.Property(x => x.Fin).HasColumnName("ENDTIME");
        b.Property(x => x.TiempoRegistrado).HasColumnName("TIMESPENT");
    }
}

internal sealed class WoTechInfoConfiguration : IEntityTypeConfiguration<WoTechInfo>
{
    public void Configure(EntityTypeBuilder<WoTechInfo> b)
    {
        b.ToTable("WO_Tech_Info");
        b.HasNoKey();
        b.Property(x => x.AssessmentId).HasColumnName("ASSESSMENTID");
        b.Property(x => x.TecnicoId).HasColumnName("TECHNICIANID");
        b.Property(x => x.Fin).HasColumnName("ENDTIME");
        b.Property(x => x.TiempoRegistrado).HasColumnName("TIMESPENT");
    }
}

internal sealed class WoGroupInfoConfiguration : IEntityTypeConfiguration<WoGroupInfo>
{
    public void Configure(EntityTypeBuilder<WoGroupInfo> b)
    {
        b.ToTable("WO_Group_Info");
        b.HasNoKey();
        b.Property(x => x.AssessmentId).HasColumnName("ASSESSMENTID");
        b.Property(x => x.GrupoId).HasColumnName("GROUPID");
        b.Property(x => x.Fin).HasColumnName("ENDTIME");
        b.Property(x => x.TiempoRegistrado).HasColumnName("TIMESPENT");
    }
}

internal sealed class AaaUserConfiguration : IEntityTypeConfiguration<AaaUser>
{
    public void Configure(EntityTypeBuilder<AaaUser> b)
    {
        b.ToTable("AaaUser");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("USER_ID").ValueGeneratedNever();
        b.Property(x => x.Nombre).HasColumnName("FIRST_NAME");
        b.Property(x => x.Apellido).HasColumnName("LAST_NAME");
    }
}
