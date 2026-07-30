using System;
using System.Collections.Generic;

namespace AlplaPortal.Domain.Entities;

public class Quotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Request Request { get; set; } = null!;

    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }

    /// <summary>
    /// Post-Payment Completion Workflow (Release 1 foundation): billing document type of this
    /// quotation — PROFORMA | FINAL_INVOICE | null. No default; the Buyer must select explicitly
    /// (rule R13). Propagated to the PO group by GroupBuilderService from Release 2 onwards.
    /// </summary>
    public string? DocumentType { get; set; }
    public string Currency { get; set; } = string.Empty;

    // Financial Totals
    public decimal TotalGrossAmount { get; set; }
    public decimal TotalDiscountAmount { get; set; }
    public decimal TotalTaxableBase { get; set; }
    public decimal TotalIvaAmount { get; set; }
    public decimal DiscountAmount { get; set; } // Added manually at quotation level
    public decimal TotalAmount { get; set; } // Final Total (Taxable + IVA)

    public bool IsSelected { get; set; } = false; // Winning Quotation Selection

    public string SourceType { get; set; } = string.Empty; // OCR, MANUAL
    public string? SourceFileName { get; set; }

    public Guid? ProformaAttachmentId { get; set; }
    public RequestAttachment? ProformaAttachment { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
}
