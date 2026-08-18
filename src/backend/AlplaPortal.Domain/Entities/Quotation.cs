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
    /// IDENTITY of the document the supplier issued for this quotation — see
    /// RequestConstants.SourceDocumentTypes. No default; the Buyer must select explicitly.
    /// The WINNING quotation's value is propagated to the PO group by GroupBuilderService.
    /// </summary>
    public string? DocumentType { get; set; }

    /// <summary>How the classification was reached: USER_SELECTED, OCR_CONFIRMED, FINANCE_REVIEW.</summary>
    public string? DocumentTypeSource { get; set; }

    /// <summary>What document extraction proposed, if anything. Never auto-applied.</summary>
    public string? DocumentTypeOcrSuggestion { get; set; }

    /// <summary>Extraction confidence for the suggestion (0.0–1.0).</summary>
    public decimal? DocumentTypeOcrConfidence { get; set; }

    /// <summary>Serialized evidence behind the suggestion.</summary>
    public string? DocumentTypeEvidenceJson { get; set; }

    /// <summary>The Buyer was shown a conflict with the extracted evidence and proceeded anyway.</summary>
    public bool ClassificationConflictAcknowledged { get; set; }

    /// <summary>Mandatory written reason when a high-risk conflict was overridden.</summary>
    public string? ClassificationJustification { get; set; }
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
