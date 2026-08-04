using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
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

        // ── Post-Payment Completion Workflow (Release 1 foundation) ──
        builder.Property(r => r.SourceDocumentType).HasMaxLength(50);
        builder.Property(r => r.SourceDocumentTypeSource).HasMaxLength(30);
        builder.Property(r => r.SourceDocumentTypeOcrSuggestion).HasMaxLength(50);
        builder.Property(r => r.SourceDocumentTypeOcrConfidence).HasColumnType("decimal(5,4)");
        builder.Property(r => r.ClassificationJustification).HasMaxLength(2000);

        // Concurrency token: selects a single winner for the parent-completion transition when
        // several PO groups of the same request complete concurrently (plan v6 §11.4/§19).
        builder.Property(r => r.RowVersion).IsRowVersion();


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

        // PAYMENT multi-document: the item's source document. NoAction because removing a document
        // after submission voids it rather than deleting it — see PaymentSourceDocument.IsVoided.
        builder.HasOne(li => li.PaymentSourceDocument)
               .WithMany(d => d.LineItems)
               .HasForeignKey(li => li.PaymentSourceDocumentId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(li => li.PaymentSourceDocumentId)
               .HasDatabaseName("IX_RequestLineItem_PaymentSourceDocumentId");

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

        // Post-Payment Completion Workflow (Release 1 foundation): no default — the Buyer must
        // choose PROFORMA or FINAL_INVOICE explicitly (rule R13). Consumed from Release 2.
        builder.Property(q => q.DocumentType).HasMaxLength(50);
        builder.Property(q => q.DocumentTypeSource).HasMaxLength(30);
        builder.Property(q => q.DocumentTypeOcrSuggestion).HasMaxLength(50);
        builder.Property(q => q.DocumentTypeOcrConfidence).HasColumnType("decimal(5,4)");
        builder.Property(q => q.ClassificationJustification).HasMaxLength(2000);

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

        // ── Post-Payment Completion Workflow (Release 1 foundation) ──

        // Concurrency token: the three dimensions below are written by different roles and can
        // collide (Finance validating an invoice while Receiving confirms receipt).
        builder.Property(g => g.RowVersion).IsRowVersion();

        builder.Property(g => g.SourceDocumentType).HasMaxLength(50);

        // UNCLASSIFIED is the persisted default, for new AND pre-existing rows: a group whose
        // billing document type is unknown must never look like "no invoice required" (rule R12).
        builder.Property(g => g.OperationInvoiceStatus)
               .IsRequired()
               .HasMaxLength(50)
               .HasDefaultValue(RequestConstants.OperationInvoiceStatuses.Unclassified);

        // ── Release 3: plant is part of the group identity, coverage is cumulative ──
        builder.Property(g => g.ExpectedOperationInvoiceTotal).HasColumnType("decimal(18,2)");
        builder.Property(g => g.ExpectedOperationInvoiceCurrency).HasMaxLength(10);
        builder.Property(g => g.ExpectedTotalJustification).HasMaxLength(2000);

        builder.HasOne(g => g.Plant)
               .WithMany()
               .HasForeignKey(g => g.PlantId)
               .OnDelete(DeleteBehavior.NoAction);

        // The Finance obligations queue filters thousands of groups by aggregate state.
        builder.HasIndex(g => g.OperationInvoiceStatus)
               .HasDatabaseName("IX_RequestPoGroup_OperationInvoiceStatus");

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

// ── Post-Payment Completion Workflow — Release 1 foundation ──

/// <summary>
/// RequestStatusHistory previously relied on convention only. This configuration adds nothing to
/// the existing mapping beyond the new IdempotencyKey column and its uniqueness guarantee.
/// </summary>
public class RequestStatusHistoryConfiguration : IEntityTypeConfiguration<RequestStatusHistory>
{
    public void Configure(EntityTypeBuilder<RequestStatusHistory> builder)
    {
        builder.HasKey(h => h.Id);

        builder.Property(h => h.IdempotencyKey)
               .HasMaxLength(PostPaymentIdempotencyKeys.MaxLength);

        // The application-level "does this key already exist?" check is a fast path, not a
        // guarantee — two concurrent transactions can both pass it. This index is the actual
        // invariant. FILTERED on IS NOT NULL so that every pre-existing row, and every event
        // without a defined key, is unaffected.
        //
        // Releases 3–4 note (transaction safety): a duplicate-key violation must NOT be handled
        // by catching SQL error 2601/2627 and continuing, because the failing SaveChanges also
        // carries the business-state change and SQL Server may abort the whole transaction.
        // The centralized handling must instead reload/re-evaluate state, or isolate the history
        // insert behind a savepoint or a conditional (NOT EXISTS) insert, so a duplicate event
        // can never silently discard the state update that justified it.
        builder.HasIndex(h => h.IdempotencyKey)
               .IsUnique()
               .HasFilter("[IdempotencyKey] IS NOT NULL")
               .HasDatabaseName("UX_RequestStatusHistory_IdempotencyKey");
    }
}

/// <summary>
/// Immutable Final Invoice reconciliation snapshots. Table created in Release 1; first rows are
/// written in Release 3.
/// </summary>
public class OperationInvoiceReconciliationConfiguration : IEntityTypeConfiguration<OperationInvoiceReconciliation>
{
    public void Configure(EntityTypeBuilder<OperationInvoiceReconciliation> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.BaselineTotal).HasColumnType("decimal(18,2)");
        builder.Property(r => r.InvoiceTotal).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ResidualVariance).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ToleranceApplied).HasColumnType("decimal(18,2)");
        builder.Property(r => r.DivergenceJustification).HasMaxLength(2000);
        builder.Property(r => r.ReconciliationDataJson).IsRequired();

        builder.HasOne(r => r.RequestPoGroup)
               .WithMany()
               .HasForeignKey(r => r.RequestPoGroupId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.RequestPoGroupId)
               .HasDatabaseName("IX_OperationInvoiceReconciliation_PoGroupId");

        builder.HasIndex(r => r.OperationInvoiceAttachmentId)
               .HasDatabaseName("IX_OperationInvoiceReconciliation_AttachmentId");

        builder.Property(r => r.AllocatedTotal).HasColumnType("decimal(18,2)");
        builder.Property(r => r.CumulativeValidatedTotalBefore).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ExpectedTotalAtComparison).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ClassificationWarning).HasMaxLength(200);

        builder.HasIndex(r => r.OperationInvoiceAllocationId)
               .HasDatabaseName("IX_OperationInvoiceReconciliation_AllocationId");
    }
}

/// <summary>
/// Append-only audit of classifications that contradicted the document's own evidence.
///
/// <para>Every relationship is NoAction on purpose: an audit row explaining why someone overrode a
/// reading must not disappear because the quotation it referred to was later removed. The row is
/// about the decision, not the object.</para>
/// </summary>
public class DocumentClassificationOverrideConfiguration : IEntityTypeConfiguration<DocumentClassificationOverride>
{
    public void Configure(EntityTypeBuilder<DocumentClassificationOverride> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Context).IsRequired().HasMaxLength(30);
        builder.Property(o => o.SelectedType).IsRequired().HasMaxLength(50);
        builder.Property(o => o.SuggestedType).HasMaxLength(50);
        builder.Property(o => o.SuggestionSource).HasMaxLength(20);
        builder.Property(o => o.TitleFound).HasMaxLength(400);
        builder.Property(o => o.Justification).HasMaxLength(2000);
        builder.Property(o => o.Confidence).HasColumnType("decimal(5,4)");

        builder.Property(o => o.IdempotencyKey)
               .IsRequired()
               .HasMaxLength(PostPaymentIdempotencyKeys.MaxLength);

        builder.HasOne(o => o.Request)
               .WithMany()
               .HasForeignKey(o => o.RequestId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(o => o.Quotation)
               .WithMany()
               .HasForeignKey(o => o.QuotationId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(o => o.ActorUser)
               .WithMany()
               .HasForeignKey(o => o.ActorUserId)
               .OnDelete(DeleteBehavior.NoAction);

        // The invariant behind "a repeated save does not duplicate history". The application-level
        // existence check is only a fast path; two concurrent saves can both pass it, and this index
        // is what actually holds. Not filtered — unlike RequestStatusHistory, every row here has a
        // key by construction.
        builder.HasIndex(o => o.IdempotencyKey)
               .IsUnique()
               .HasDatabaseName("UX_DocumentClassificationOverride_IdempotencyKey");

        builder.HasIndex(o => o.RequestId)
               .HasDatabaseName("IX_DocumentClassificationOverride_RequestId");

        builder.HasIndex(o => o.QuotationId)
               .HasDatabaseName("IX_DocumentClassificationOverride_QuotationId");
    }
}

// ── Release 3: multi-document PAYMENT origin ──────────────────────────────────────────────

/// <summary>
/// Documents that originate a PAYMENT request. Cascade from the request (a source document has no
/// meaning without it), NoAction everywhere else — a supplier or plant must never be deletable in a
/// way that erases the record of what was paid for.
/// </summary>
public class PaymentSourceDocumentConfiguration : IEntityTypeConfiguration<PaymentSourceDocument>
{
    public void Configure(EntityTypeBuilder<PaymentSourceDocument> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.SourceDocumentType).HasMaxLength(50);
        builder.Property(d => d.DocumentNumber).HasMaxLength(100);
        builder.Property(d => d.DocumentSeries).HasMaxLength(50);
        builder.Property(d => d.Currency).HasMaxLength(10);
        builder.Property(d => d.SupplierNameSnapshot).HasMaxLength(255);
        builder.Property(d => d.SupplierTaxIdSnapshot).HasMaxLength(50);
        builder.Property(d => d.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(d => d.TaxAmount).HasColumnType("decimal(18,2)");
        builder.Property(d => d.GrossAmount).HasColumnType("decimal(18,2)");

        builder.Property(d => d.OcrSuggestion).HasMaxLength(50);
        builder.Property(d => d.OcrConfidence).HasColumnType("decimal(5,4)");
        builder.Property(d => d.OcrTitleFound).HasMaxLength(400);
        builder.Property(d => d.ClassificationSource).HasMaxLength(30);
        builder.Property(d => d.ClassificationSuggestionSource).HasMaxLength(20);
        builder.Property(d => d.ClassificationJustification).HasMaxLength(2000);
        builder.Property(d => d.VoidReason).HasMaxLength(500);

        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasOne(d => d.Request)
               .WithMany()
               .HasForeignKey(d => d.RequestId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Attachment)
               .WithMany()
               .HasForeignKey(d => d.AttachmentId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(d => d.Supplier)
               .WithMany()
               .HasForeignKey(d => d.SupplierId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(d => d.Plant)
               .WithMany()
               .HasForeignKey(d => d.PlantId)
               .OnDelete(DeleteBehavior.NoAction);

        // One attachment is one source document. Half of "the same file is never registered twice";
        // the hash check at upload is the other half.
        builder.HasIndex(d => d.AttachmentId)
               .IsUnique()
               .HasDatabaseName("UX_PaymentSourceDocument_AttachmentId");

        builder.HasIndex(d => new { d.RequestId, d.SequenceNumber })
               .IsUnique()
               .HasDatabaseName("UX_PaymentSourceDocument_RequestSequence");

        builder.HasIndex(d => new { d.RequestId, d.IsVoided })
               .HasDatabaseName("IX_PaymentSourceDocument_RequestActive");

        builder.HasIndex(d => new { d.SupplierId, d.DocumentNumber, d.DocumentSeries })
               .HasDatabaseName("IX_PaymentSourceDocument_SupplierDocument");
    }
}

// ── Release 3: operation invoices across PO groups ────────────────────────────────────────

/// <summary>
/// The operation-invoice document. Request-scoped so that cross-request consolidation is impossible
/// to represent rather than merely rejected.
/// </summary>
public class OperationInvoiceConfiguration : IEntityTypeConfiguration<OperationInvoice>
{
    public void Configure(EntityTypeBuilder<OperationInvoice> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Status).IsRequired().HasMaxLength(30);
        builder.Property(i => i.DocumentNumber).HasMaxLength(100);
        builder.Property(i => i.DocumentSeries).HasMaxLength(50);
        builder.Property(i => i.Currency).HasMaxLength(10);
        builder.Property(i => i.SupplierTaxIdSnapshot).HasMaxLength(50);
        builder.Property(i => i.BilledCompanyNameRead).HasMaxLength(255);
        builder.Property(i => i.RejectionReason).HasMaxLength(2000);
        builder.Property(i => i.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.TaxAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.GrossAmount).HasColumnType("decimal(18,2)");

        builder.Property(i => i.RowVersion).IsRowVersion();

        builder.HasOne(i => i.Request)
               .WithMany()
               .HasForeignKey(i => i.RequestId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(i => i.Attachment)
               .WithMany()
               .HasForeignKey(i => i.AttachmentId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(i => i.Supplier)
               .WithMany()
               .HasForeignKey(i => i.SupplierId)
               .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(i => i.AttachmentId)
               .IsUnique()
               .HasDatabaseName("UX_OperationInvoice_AttachmentId");

        builder.HasIndex(i => new { i.RequestId, i.Status })
               .HasDatabaseName("IX_OperationInvoice_RequestStatus");

        // Duplicate detection: same supplier reissuing the same number in the same series.
        builder.HasIndex(i => new { i.SupplierId, i.DocumentNumber, i.DocumentSeries })
               .HasDatabaseName("IX_OperationInvoice_SupplierDocument");
    }
}

public class OperationInvoiceAllocationConfiguration : IEntityTypeConfiguration<OperationInvoiceAllocation>
{
    public void Configure(EntityTypeBuilder<OperationInvoiceAllocation> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AllocatedNetAmount).HasColumnType("decimal(18,2)");
        builder.Property(a => a.AllocatedTaxAmount).HasColumnType("decimal(18,2)");
        builder.Property(a => a.AllocatedGrossAmount).HasColumnType("decimal(18,2)");
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasOne(a => a.OperationInvoice)
               .WithMany(i => i.Allocations)
               .HasForeignKey(a => a.OperationInvoiceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.RequestPoGroup)
               .WithMany()
               .HasForeignKey(a => a.RequestPoGroupId)
               .OnDelete(DeleteBehavior.NoAction);

        // One invoice reaches a group at most once: covering more of it means a LARGER allocation,
        // never a second row. Without this, two rows could double-count the same document.
        builder.HasIndex(a => new { a.OperationInvoiceId, a.RequestPoGroupId })
               .IsUnique()
               .HasDatabaseName("UX_OperationInvoiceAllocation_InvoiceGroup");

        builder.HasIndex(a => new { a.RequestPoGroupId, a.SequenceNumber })
               .IsUnique()
               .HasDatabaseName("UX_OperationInvoiceAllocation_GroupSequence");
    }
}

public class OperationInvoiceLineConfiguration : IEntityTypeConfiguration<OperationInvoiceLine>
{
    public void Configure(EntityTypeBuilder<OperationInvoiceLine> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Description).IsRequired().HasMaxLength(1000);
        builder.Property(l => l.MatchStatus).IsRequired().HasMaxLength(30);
        builder.Property(l => l.BaselineLineType).HasMaxLength(30);
        builder.Property(l => l.Quantity).HasColumnType("decimal(18,4)");
        builder.Property(l => l.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(l => l.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(l => l.TaxAmount).HasColumnType("decimal(18,2)");
        builder.Property(l => l.LineTotal).HasColumnType("decimal(18,2)");

        builder.HasOne(l => l.OperationInvoice)
               .WithMany(i => i.Lines)
               .HasForeignKey(l => l.OperationInvoiceId)
               .OnDelete(DeleteBehavior.Cascade);

        // Cascade is deliberate here too: a line has no meaning without its allocation, and removing
        // an allocation must not leave lines pointing at nothing.
        builder.HasOne(l => l.OperationInvoiceAllocation)
               .WithMany(a => a.Lines)
               .HasForeignKey(l => l.OperationInvoiceAllocationId)
               .OnDelete(DeleteBehavior.NoAction);

        // The cumulative-quantity query runs per baseline line across every validated invoice —
        // the hottest read path in the whole workflow.
        builder.HasIndex(l => l.BaselineLineId)
               .HasDatabaseName("IX_OperationInvoiceLine_BaselineLineId");

        builder.HasIndex(l => l.OperationInvoiceAllocationId)
               .HasDatabaseName("IX_OperationInvoiceLine_AllocationId");
    }
}

public class OperationInvoiceShortCloseConfiguration : IEntityTypeConfiguration<OperationInvoiceShortClose>
{
    public void Configure(EntityTypeBuilder<OperationInvoiceShortClose> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status).IsRequired().HasMaxLength(20);
        builder.Property(c => c.ProposalJustification).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.DecisionReason).HasMaxLength(2000);
        builder.Property(c => c.RemainingAmountAtProposal).HasColumnType("decimal(18,2)");
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasOne(c => c.RequestPoGroup)
               .WithMany()
               .HasForeignKey(c => c.RequestPoGroupId)
               .OnDelete(DeleteBehavior.NoAction);

        // At most one live proposal or approval per group. A REJECTED row is free to repeat, which
        // is what lets a second proposal be made after a refusal.
        builder.HasIndex(c => c.RequestPoGroupId)
               .IsUnique()
               .HasFilter("[Status] IN ('PROPOSED', 'APPROVED')")
               .HasDatabaseName("UX_OperationInvoiceShortClose_ActivePerGroup");
    }
}

