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

        builder.HasMany(r => r.PoGroups)
               .WithOne(g => g.Request)
               .HasForeignKey(g => g.RequestId)
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

        builder.HasOne(li => li.RequestPoGroup)
               .WithMany(g => g.LineItems)
               .HasForeignKey(li => li.RequestPoGroupId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(li => li.SelectedQuotationItem)
               .WithMany()
               .HasForeignKey(li => li.SelectedQuotationItemId)
               .OnDelete(DeleteBehavior.NoAction);

        // ── Provenance & idempotency (buyer reconciliation workaround) ──
        builder.Property(li => li.CreationOrigin).HasMaxLength(50);
        builder.Property(li => li.CreationIdempotencyKey).HasMaxLength(100);

        // Same-operation idempotency: a client-supplied token uniquely identifies one
        // "add as requested item" operation WITHIN a request. Scoped to (RequestId, key) so the same
        // key in different requests are independent operations and can never cross-resolve.
        // Filtered so it only applies to reconciliation-created rows (legacy/standard rows keep NULL
        // and are exempt from the uniqueness constraint).
        builder.HasIndex(li => new { li.RequestId, li.CreationIdempotencyKey })
               .IsUnique()
               .HasFilter("[CreationIdempotencyKey] IS NOT NULL");

        // Cross-session probable-duplicate detection lookup (not unique — legitimate lines may repeat).
        builder.HasIndex(li => new { li.RequestId, li.SourceProformaAttachmentId });
    }
}

public class RequestLineItemAllocationConfiguration : IEntityTypeConfiguration<RequestLineItemAllocation>
{
    public void Configure(EntityTypeBuilder<RequestLineItemAllocation> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Percentage)
               .HasColumnType("decimal(9,4)");

        builder.Property(a => a.Comment)
               .HasMaxLength(500);

        builder.HasOne(a => a.RequestLineItem)
               .WithMany(li => li.Allocations)
               .HasForeignKey(a => a.RequestLineItemId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Plant)
               .WithMany()
               .HasForeignKey(a => a.PlantId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.CostCenter)
               .WithMany()
               .HasForeignKey(a => a.CostCenterId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(a => a.RequestLineItemId)
               .HasDatabaseName("IX_Allocation_LineItemId");

        builder.HasIndex(a => a.CostCenterId)
               .HasDatabaseName("IX_Allocation_CostCenterId");
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

        builder.HasOne(a => a.RequestPoGroup)
               .WithMany(g => g.PoAttachments)
               .HasForeignKey(a => a.RequestPoGroupId)
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

public class DepartmentManagerConfiguration : IEntityTypeConfiguration<DepartmentManager>
{
    public void Configure(EntityTypeBuilder<DepartmentManager> builder)
    {
        builder.HasKey(dm => dm.Id);

        // Same user may manage several plants of a department (and be global besides),
        // but never the same (department, plant) twice. SQL Server unique indexes only
        // treat NULL as a duplicate-able value with a filter, so global rows (PlantId
        // NULL) get their own filtered unique index.
        builder.HasIndex(dm => new { dm.DepartmentId, dm.PlantId, dm.UserId })
            .IsUnique()
            .HasFilter("[PlantId] IS NOT NULL");
        builder.HasIndex(dm => new { dm.DepartmentId, dm.UserId })
            .IsUnique()
            .HasFilter("[PlantId] IS NULL")
            .HasDatabaseName("IX_DepartmentManagers_DepartmentId_UserId_Global");

        // Hot paths: resolution/queue by (department, plant) and inverse lookup by user.
        builder.HasIndex(dm => new { dm.DepartmentId, dm.PlantId, dm.IsActive });
        builder.HasIndex(dm => new { dm.UserId, dm.IsActive });

        // Users/departments/plants are deactivated, never deleted — block cascade.
        builder.HasOne(dm => dm.Department)
            .WithMany(d => d.Managers)
            .HasForeignKey(dm => dm.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(dm => dm.Plant)
            .WithMany()
            .HasForeignKey(dm => dm.PlantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(dm => dm.User)
            .WithMany()
            .HasForeignKey(dm => dm.UserId)
            .OnDelete(DeleteBehavior.Restrict);
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

        // OCR-original baseline — each mirrors the precision of its live counterpart; all nullable.
        // OcrOriginalUnitId is a plain snapshot int (no FK — it must not block unit-catalog changes).
        builder.Property(qi => qi.OcrOriginalQuantity).HasColumnType("decimal(18,4)");
        builder.Property(qi => qi.OcrOriginalUnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.OcrOriginalDiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.OcrOriginalIvaRatePercent).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.OcrOriginalLineTotal).HasColumnType("decimal(18,2)");
        builder.Property(qi => qi.OcrOriginalUnitText).HasMaxLength(64);

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

public class RequestPoGroupConfiguration : IEntityTypeConfiguration<RequestPoGroup>
{
    public void Configure(EntityTypeBuilder<RequestPoGroup> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.SupplierNameSnapshot).HasMaxLength(255);
        builder.Property(g => g.SupplierNifSnapshot).HasMaxLength(50);
        builder.Property(g => g.CurrencyCode).HasMaxLength(10);
        builder.Property(g => g.PaymentConditionCode).HasMaxLength(50);
        builder.Property(g => g.Status).IsRequired().HasMaxLength(50);
        builder.Property(g => g.PurchaseOrderNumber).HasMaxLength(100);
        builder.Property(g => g.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(g => g.AdvancePaymentPercent).HasColumnType("decimal(9,4)");

        builder.HasOne(g => g.Supplier)
               .WithMany()
               .HasForeignKey(g => g.SupplierId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.Currency)
               .WithMany()
               .HasForeignKey(g => g.CurrencyId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.ApprovalBatch)
               .WithMany(b => b.PoGroups)
               .HasForeignKey(g => g.ApprovalBatchId)
               .OnDelete(DeleteBehavior.NoAction);
    }
}

// ── Partial Quotation Approval Entity Configurations ──

public class ApprovalBatchConfiguration : IEntityTypeConfiguration<ApprovalBatch>
{
    public void Configure(EntityTypeBuilder<ApprovalBatch> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Status).IsRequired().HasMaxLength(50);
        builder.Property(b => b.Comment).HasMaxLength(2000);
        builder.Property(b => b.BudgetJustification).HasMaxLength(2000);
        builder.Property(b => b.ApprovedTotalAmount).HasColumnType("decimal(18,2)");

        builder.HasOne(b => b.Request)
               .WithMany(r => r.ApprovalBatches)
               .HasForeignKey(b => b.RequestId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Items)
               .WithOne(i => i.ApprovalBatch)
               .HasForeignKey(i => i.ApprovalBatchId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.ExtraItemDecisions)
               .WithOne(d => d.ApprovalBatch)
               .HasForeignKey(d => d.ApprovalBatchId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.RequestId)
               .HasDatabaseName("IX_ApprovalBatch_RequestId");

        builder.HasIndex(b => new { b.RequestId, b.BatchNumber })
               .IsUnique()
               .HasDatabaseName("IX_ApprovalBatch_Request_BatchNumber");
    }
}

public class ApprovalBatchItemConfiguration : IEntityTypeConfiguration<ApprovalBatchItem>
{
    public void Configure(EntityTypeBuilder<ApprovalBatchItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasOne(i => i.RequestLineItem)
               .WithMany()
               .HasForeignKey(i => i.RequestLineItemId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(i => i.SelectedQuotationItem)
               .WithMany()
               .HasForeignKey(i => i.SelectedQuotationItemId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(i => i.ApprovalBatchId)
               .HasDatabaseName("IX_ApprovalBatchItem_BatchId");

        builder.HasIndex(i => i.RequestLineItemId)
               .HasDatabaseName("IX_ApprovalBatchItem_LineItemId");
    }
}

public class QuotationReuseAuthorizationConfiguration : IEntityTypeConfiguration<QuotationReuseAuthorization>
{
    public void Configure(EntityTypeBuilder<QuotationReuseAuthorization> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Reason).IsRequired().HasMaxLength(1000);
        builder.Property(a => a.RevocationReason).HasMaxLength(1000);

        builder.HasOne(a => a.Request)
               .WithMany()
               .HasForeignKey(a => a.RequestId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.Quotation)
               .WithMany()
               .HasForeignKey(a => a.QuotationId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.QuotationItem)
               .WithMany()
               .HasForeignKey(a => a.QuotationItemId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(a => a.SourceApprovalBatch)
               .WithMany()
               .HasForeignKey(a => a.SourceApprovalBatchId)
               .OnDelete(DeleteBehavior.NoAction);

        // At most ONE active authorization per (item, source cancelled batch) — consumption/
        // revocation flips IsActive so a new authorization for a NEW cancelled use stays possible.
        builder.HasIndex(a => new { a.QuotationItemId, a.SourceApprovalBatchId })
               .IsUnique()
               .HasFilter("[IsActive] = 1")
               .HasDatabaseName("UX_QuotationReuseAuth_Item_SourceBatch_Active");

        builder.HasIndex(a => a.RequestId).HasDatabaseName("IX_QuotationReuseAuth_RequestId");
        builder.HasIndex(a => a.QuotationId).HasDatabaseName("IX_QuotationReuseAuth_QuotationId");
        builder.HasIndex(a => a.QuotationItemId).HasDatabaseName("IX_QuotationReuseAuth_QuotationItemId");
        builder.HasIndex(a => a.SourceApprovalBatchId).HasDatabaseName("IX_QuotationReuseAuth_SourceBatchId");
        builder.HasIndex(a => a.IsActive).HasDatabaseName("IX_QuotationReuseAuth_IsActive");
    }
}

public class ApprovalBatchExtraItemDecisionConfiguration : IEntityTypeConfiguration<ApprovalBatchExtraItemDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalBatchExtraItemDecision> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Decision).IsRequired().HasMaxLength(20);
        builder.Property(d => d.Comment).HasMaxLength(2000);

        builder.HasOne(d => d.QuotationItem)
               .WithMany()
               .HasForeignKey(d => d.QuotationItemId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(d => d.GeneratedRequestLineItem)
               .WithMany()
               .HasForeignKey(d => d.GeneratedRequestLineItemId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(d => d.ApprovalBatchId)
               .HasDatabaseName("IX_ApprovalBatchExtraItemDecision_BatchId");
    }
}

