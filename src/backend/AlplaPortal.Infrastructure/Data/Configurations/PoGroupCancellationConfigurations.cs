using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlplaPortal.Infrastructure.Data.Configurations;

/// <summary>Phase 1 P.O. group cancellation — three additive tables (docs/PO_GROUP_CANCELLATION.md §L.1).</summary>
public class RequestPoGroupCancellationConfiguration : IEntityTypeConfiguration<RequestPoGroupCancellation>
{
    public void Configure(EntityTypeBuilder<RequestPoGroupCancellation> builder)
    {
        builder.ToTable("RequestPoGroupCancellations");
        builder.HasKey(c => c.Id);

        // One cancellation per group, ever: the group is CANCELLED afterwards and never mutated again.
        builder.HasIndex(c => c.RequestPoGroupId).IsUnique().HasDatabaseName("IX_RequestPoGroupCancellations_Group");
        builder.HasIndex(c => c.RequestId).HasDatabaseName("IX_RequestPoGroupCancellations_Request");
        // The Finance queue ("Cancelamentos a confirmar") filters on these two.
        builder.HasIndex(c => new { c.FinanceConfirmationRequired, c.FinanceConfirmationStatus })
               .HasDatabaseName("IX_RequestPoGroupCancellations_FinanceReview");

        builder.Property(c => c.Kind).IsRequired().HasMaxLength(40);
        builder.Property(c => c.ReasonCode).IsRequired().HasMaxLength(60);
        builder.Property(c => c.Reason).IsRequired().HasMaxLength(PoGroupCancellationConstants.MaxJustificationLength);
        builder.Property(c => c.PreviousGroupStatus).IsRequired().HasMaxLength(50);
        builder.Property(c => c.PurchaseOrderNumberSnapshot).HasMaxLength(100);
        builder.Property(c => c.TotalAmountSnapshot).HasColumnType("decimal(18,2)");
        builder.Property(c => c.CurrencyCodeSnapshot).HasMaxLength(10);
        builder.Property(c => c.EcfStatus).IsRequired().HasMaxLength(40);
        builder.Property(c => c.EcfReference).HasMaxLength(PoGroupCancellationConstants.MaxEcfReferenceLength);
        builder.Property(c => c.EcfDeclarationNote).HasMaxLength(PoGroupCancellationConstants.MaxJustificationLength);
        builder.Property(c => c.FinanceConfirmationStatus).IsRequired().HasMaxLength(40);
        builder.Property(c => c.FinanceConfirmationNote).HasMaxLength(PoGroupCancellationConstants.MaxJustificationLength);
        builder.Property(c => c.FinanceEmailPolicyOutcome).HasMaxLength(60);
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasOne(c => c.RequestPoGroup)
               .WithOne(g => g.Cancellation)
               .HasForeignKey<RequestPoGroupCancellation>(c => c.RequestPoGroupId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Request)
               .WithMany()
               .HasForeignKey(c => c.RequestId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Items)
               .WithOne(i => i.Cancellation)
               .HasForeignKey(i => i.RequestPoGroupCancellationId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.EcfEvents)
               .WithOne(e => e.Cancellation)
               .HasForeignKey(e => e.RequestPoGroupCancellationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RequestPoGroupCancellationItemConfiguration : IEntityTypeConfiguration<RequestPoGroupCancellationItem>
{
    public void Configure(EntityTypeBuilder<RequestPoGroupCancellationItem> builder)
    {
        builder.ToTable("RequestPoGroupCancellationItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Outcome).IsRequired().HasMaxLength(20);
        builder.Property(i => i.PreviousLineItemStatusCode).HasMaxLength(50);
        builder.Property(i => i.PreviousQuotationLifecycleStatus).HasMaxLength(50);

        // The replacement-P.O. guard looks up by line item.
        builder.HasIndex(i => i.RequestLineItemId).HasDatabaseName("IX_RequestPoGroupCancellationItems_Line");

        builder.HasOne(i => i.RequestLineItem)
               .WithMany()
               .HasForeignKey(i => i.RequestLineItemId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class RequestPoGroupCancellationEcfEventConfiguration : IEntityTypeConfiguration<RequestPoGroupCancellationEcfEvent>
{
    public void Configure(EntityTypeBuilder<RequestPoGroupCancellationEcfEvent> builder)
    {
        builder.ToTable("RequestPoGroupCancellationEcfEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(40);
        builder.Property(e => e.EcfStatusAfter).IsRequired().HasMaxLength(40);
        builder.Property(e => e.FinanceStatusAfter).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Reference).HasMaxLength(PoGroupCancellationConstants.MaxEcfReferenceLength);
        builder.Property(e => e.Note).HasMaxLength(PoGroupCancellationConstants.MaxJustificationLength);
        builder.Property(e => e.ActorRoleSnapshot).IsRequired().HasMaxLength(40);
        builder.HasIndex(e => e.RequestPoGroupCancellationId).HasDatabaseName("IX_RequestPoGroupCancellationEcfEvents_Cancellation");
    }
}
