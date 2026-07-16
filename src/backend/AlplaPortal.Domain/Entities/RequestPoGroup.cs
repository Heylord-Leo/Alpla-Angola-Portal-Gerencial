using System;
using System.Collections.Generic;

namespace AlplaPortal.Domain.Entities;

public class RequestPoGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string? SupplierNameSnapshot { get; set; }
    public string? SupplierNifSnapshot { get; set; }

    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public string? CurrencyCode { get; set; }

    public decimal TotalAmount { get; set; }

    public string? PaymentConditionCode { get; set; }
    public decimal? AdvancePaymentPercent { get; set; }

    public string Status { get; set; } = "PENDING";

    /// <summary>
    /// The approval batch that generated this PO group.
    /// Null for legacy groups created before the batch model, and for PAYMENT requests.
    /// </summary>
    public Guid? ApprovalBatchId { get; set; }
    public ApprovalBatch? ApprovalBatch { get; set; }

    public string? PurchaseOrderNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    // Navigation properties
    public ICollection<RequestLineItem> LineItems { get; set; } = new List<RequestLineItem>();
    public ICollection<RequestAttachment> PoAttachments { get; set; } = new List<RequestAttachment>();
    public ICollection<RequestPayment> Payments { get; set; } = new List<RequestPayment>();
    public ICollection<RequestReconciliation> Reconciliations { get; set; } = new List<RequestReconciliation>();
}
