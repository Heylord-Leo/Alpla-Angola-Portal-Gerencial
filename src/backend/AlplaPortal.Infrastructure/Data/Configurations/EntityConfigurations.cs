using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlplaPortal.Infrastructure.Data.Configurations;

public class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Title).IsRequired().HasMaxLength(255);
        builder.Property(r => r.RequestNumber).HasMaxLength(50);
        builder.Property(r => r.EstimatedTotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.OcrOriginalGrandTotal).HasColumnType("decimal(18,2)");

        // DEC-110: Financial snapshot & payment fields
        builder.Property(r => r.ApprovedTotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ApprovedCurrencyCode).HasMaxLength(10);
        builder.Property(r => r.ActualPaidAmount).HasColumnType("decimal(18,2)");
        
        // Strict mapping: A Request has many LineItems, Histories, Attachments
        builder.HasMany(r => r.LineItems)
               .WithOne(li => li.Request)
               .HasForeignKey(li => li.RequestId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.StatusHistories)
               .WithOne(sh => sh.Request)
               .HasForeignKey(sh => sh.RequestId)
               .OnDelete(DeleteBehavior.Restrict); // Do not cascade delete history

        builder.HasMany(r => r.Attachments)
               .WithOne(a => a.Request)
               .HasForeignKey(a => a.RequestId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RequestLineItemConfiguration : IEntityTypeConfiguration<RequestLineItem>
{
    public void Configure(EntityTypeBuilder<RequestLineItem> builder)
    {
        builder.HasKey(li => li.Id);
        
        builder.Property(li => li.Quantity).HasColumnType("decimal(18,4)");
        builder.Property(li => li.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(li => li.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(li => li.DiscountPercent).HasColumnType("decimal(9,4)");
        builder.Property(li => li.DiscountAmount).HasColumnType("decimal(18,2)");
    }
}

public class RequestAttachmentConfiguration : IEntityTypeConfiguration<RequestAttachment>
{
    public void Configure(EntityTypeBuilder<RequestAttachment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.FileExtension).HasMaxLength(10);
        
        // Fix EF warning: explicitly define precision for decimal field
        builder.Property(a => a.FileSizeMBytes)
               .HasPrecision(10, 3);

        builder.Property(a => a.StorageReference).HasMaxLength(1000);

        // Fix System.InvalidOperationException: Multiple cascade paths.
        // Prevent RequestAttachment -> User from cascading since Request -> User also cascades (conceptually).
        // Audit ownership should be preserved even if a user is hard-deleted.
        builder.HasOne(a => a.UploadedByUser)
               .WithMany()
               .HasForeignKey(a => a.UploadedByUserId)
               .OnDelete(DeleteBehavior.NoAction);
    }
}

public class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        // Composite Primary Key for junction table
        builder.HasKey(ura => new { ura.UserId, ura.RoleId });
    }
}
public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.HasKey(q => q.Id);
        builder.Property(q => q.SupplierNameSnapshot).IsRequired().HasMaxLength(255);
        builder.Property(q => q.DocumentNumber).HasMaxLength(100);
        builder.Property(q => q.Currency).IsRequired().HasMaxLength(10);
        builder.Property(q => q.TotalGrossAmount).HasColumnType("decimal(18,2)");
        builder.Property(q => q.TotalDiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(q => q.TotalTaxableBase).HasColumnType("decimal(18,2)");
        builder.Property(q => q.TotalIvaAmount).HasColumnType("decimal(18,2)");
        builder.Property(q => q.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(q => q.SourceType).IsRequired().HasMaxLength(20);
        builder.Property(q => q.SourceFileName).HasMaxLength(255);

        builder.HasOne(q => q.Request)
               .WithMany(r => r.Quotations)
               .HasForeignKey(q => q.RequestId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(q => q.Supplier)
               .WithMany()
               .HasForeignKey(q => q.SupplierId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(q => q.Items)
               .WithOne(qi => qi.Quotation)
               .HasForeignKey(qi => qi.QuotationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuotationItemConfiguration : IEntityTypeConfiguration<QuotationItem>
{
    public void Configure(EntityTypeBuilder<QuotationItem> builder)
    {
        builder.HasKey(qi => qi.Id);
        builder.Property(qi => qi.Description).IsRequired().HasMaxLength(1000);
        builder.Property(qi => qi.Quantity).HasColumnType("decimal(18,4)");
        builder.Property(qi => qi.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.GrossSubtotal).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.IvaRatePercent).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.IvaAmount).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.LineTotal).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.DiscountPercent).HasColumnType("decimal(9,4)");

        builder.HasOne(qi => qi.IvaRate)
               .WithMany()
               .HasForeignKey(qi => qi.IvaRateId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(qi => qi.ItemCatalog)
               .WithMany()
               .HasForeignKey(qi => qi.ItemCatalogId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

public class IvaRateConfiguration : IEntityTypeConfiguration<IvaRate>
{
    public void Configure(EntityTypeBuilder<IvaRate> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Code).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Name).IsRequired().HasMaxLength(100);
        builder.Property(i => i.RatePercent).HasColumnType("decimal(18,2)");
    }
}

public class EmailOutboxEntryConfiguration : IEntityTypeConfiguration<EmailOutboxEntry>
{
    public void Configure(EntityTypeBuilder<EmailOutboxEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RecipientEmail).IsRequired().HasMaxLength(256);
        builder.Property(e => e.RecipientName).HasMaxLength(256);
        builder.Property(e => e.Subject).IsRequired().HasMaxLength(512);
        builder.Property(e => e.Headline).IsRequired().HasMaxLength(256);
        builder.Property(e => e.BodyHtml).IsRequired();
        builder.Property(e => e.ActionUrl).HasMaxLength(1024);
        builder.Property(e => e.ActionLabel).HasMaxLength(128);
        builder.Property(e => e.CcEmails).HasMaxLength(1024);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20).HasDefaultValue("PENDING");
        builder.Property(e => e.MaxRetries).HasDefaultValue(3);
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.Property(e => e.RequestNumber).HasMaxLength(50);
        builder.Property(e => e.EventCode).HasMaxLength(100);

        // Primary index: processor polling — picks PENDING or retryable FAILED entries
        builder.HasIndex(e => new { e.Status, e.NextRetryAtUtc })
               .HasFilter("[Status] IN ('PENDING', 'FAILED')")
               .HasDatabaseName("IX_EmailOutbox_Status_NextRetry");

        // Dedup UNIQUE index: database-level prevention of duplicate active entries
        // for the same correlation + recipient. Only applies when CorrelationId is not null
        // and the entry is still in an active (non-terminal) state.
        builder.HasIndex(e => new { e.CorrelationId, e.RecipientEmail })
               .IsUnique()
               .HasFilter("[CorrelationId] IS NOT NULL AND [Status] IN ('PENDING', 'PROCESSING', 'FAILED')")
               .HasDatabaseName("IX_EmailOutbox_Correlation_Recipient_Active");

        // Traceability: find all outbox entries for a given request
        builder.HasIndex(e => e.RequestId)
               .HasDatabaseName("IX_EmailOutbox_RequestId");

        // Crash recovery: find entries stuck in PROCESSING
        builder.HasIndex(e => new { e.Status, e.CreatedAtUtc })
               .HasFilter("[Status] = 'PROCESSING'")
               .HasDatabaseName("IX_EmailOutbox_Processing_CreatedAt");
    }
}
