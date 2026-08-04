using System;
using System.Collections.Generic;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Entities;

/// <summary>
/// A Factura documenting the actual supply, received after payment against an obligation.
///
/// <para>Request-scoped, not group-scoped: a supplier may consolidate several PO groups of the same
/// request onto one invoice. The document is stored <b>once</b> and reaches groups through
/// <see cref="OperationInvoiceAllocation"/>. Storing it once per group would duplicate the
/// attachment and let the copies drift apart.</para>
///
/// <para>Cross-request consolidation is impossible to represent rather than merely rejected:
/// <see cref="RequestId"/> is the scope, and every allocation's group must belong to it.</para>
///
/// <para>Status lives here, on the document, never on the allocation. A partially-valid document is
/// not a fact about the world — so rejecting an invoice that covers two groups affects both, which
/// the UI must disclose.</para>
/// </summary>
public class OperationInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The scope. Allocations may only reach groups of this request.</summary>
    public Guid RequestId { get; set; }
    public Request? Request { get; set; }

    /// <summary>The file itself. Unique — the structural half of "never upload once per group".</summary>
    public Guid AttachmentId { get; set; }
    public RequestAttachment? Attachment { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>
    /// The NIF as printed on the document. Kept beside the resolved <see cref="SupplierId"/> because
    /// the compatibility check compares what the document SAYS against what the Portal HOLDS —
    /// storing only the resolved id would discard the evidence that justified the match.
    /// </summary>
    public string? SupplierTaxIdSnapshot { get; set; }

    /// <summary>The billed entity as printed, checked against the request's company.</summary>
    public string? BilledCompanyNameRead { get; set; }

    public string? DocumentNumber { get; set; }
    public string? DocumentSeries { get; set; }
    public DateTime? DocumentDate { get; set; }
    public string? Currency { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? TaxAmount { get; set; }

    /// <summary>The amount the allocations must reconcile to.</summary>
    public decimal? GrossAmount { get; set; }

    /// <summary>See RequestConstants.OperationInvoiceDocumentStatuses.</summary>
    public string Status { get; set; } = RequestConstants.OperationInvoiceDocumentStatuses.Uploaded;

    /// <summary>
    /// OCR failed or was corrected by hand. An unreadable scan must never be able to block a group
    /// forever, so manual entry is allowed — but Finance must see that a number was typed, not read.
    /// </summary>
    public bool AmountsEnteredManually { get; set; }

    public DateTime UploadedAtUtc { get; set; }
    public Guid UploadedByUserId { get; set; }

    public DateTime? ValidatedAtUtc { get; set; }
    public Guid? ValidatedByUserId { get; set; }

    /// <summary>Reason for a rejection or a replacement request. Mandatory for both.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Links a replacement chain so it reads as a chain, not as unrelated rows.</summary>
    public Guid? SupersededByOperationInvoiceId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<OperationInvoiceAllocation> Allocations { get; set; } = new List<OperationInvoiceAllocation>();
    public ICollection<OperationInvoiceLine> Lines { get; set; } = new List<OperationInvoiceLine>();
}

/// <summary>
/// The share of one operation invoice that lands on one PO group.
///
/// <para>The allocated amount is <b>stored</b>, unlike a payment source document's distribution
/// which is derived from its items. The asymmetry is deliberate: Finance may allocate money that is
/// not line-attributable — pro-rata tax, rounding residue, a service charge covering several groups
/// — so the split cannot always be recomputed from lines.</para>
/// </summary>
public class OperationInvoiceAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OperationInvoiceId { get; set; }
    public OperationInvoice? OperationInvoice { get; set; }

    public Guid RequestPoGroupId { get; set; }
    public RequestPoGroup? RequestPoGroup { get; set; }

    public decimal AllocatedNetAmount { get; set; }
    public decimal AllocatedTaxAmount { get; set; }

    /// <summary>The only number that feeds the group's cumulative coverage.</summary>
    public decimal AllocatedGrossAmount { get; set; }

    /// <summary>The Nth allocation received by THAT group — shown as "Fatura 1 de N".</summary>
    public int SequenceNumber { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<OperationInvoiceLine> Lines { get; set; } = new List<OperationInvoiceLine>();
}

/// <summary>
/// One line read from an operation invoice, and the baseline line it covers.
///
/// <para>This table exists for one reason: <b>cumulative duplicate protection</b>. "Has this
/// baseline line already been invoiced, and for how much of its quantity?" is a question across
/// documents and groups, so it must be queryable — it cannot live inside a JSON snapshot. Without
/// it, the only possible check is the total, which would let two invoices bill the same item twice
/// while the sum stayed under the expected value.</para>
/// </summary>
public class OperationInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OperationInvoiceId { get; set; }
    public OperationInvoice? OperationInvoice { get; set; }

    /// <summary>
    /// Nullable on purpose. OCR reads twelve lines and eleven are recognisable — the twelfth is real
    /// and must be REPRESENTABLE while still unallocated. Forcing every line into an allocation at
    /// read time would mean inventing one or dropping the line, and a dropped line is how an invoice
    /// quietly under-reports what the supplier billed. An unallocated line blocks validation.
    /// </summary>
    public Guid? OperationInvoiceAllocationId { get; set; }
    public OperationInvoiceAllocation? OperationInvoiceAllocation { get; set; }

    /// <summary>
    /// Denormalized from the allocation for the cumulative-quantity query, which runs per baseline
    /// line across every validated invoice. Set from the allocation on write, never independently,
    /// and never trusted from client input.
    /// </summary>
    public Guid? RequestPoGroupId { get; set; }

    /// <summary>The RequestLineItem or QuotationItem this line covers.</summary>
    public Guid? BaselineLineId { get; set; }
    /// <summary>REQUEST_LINE_ITEM or QUOTATION_ITEM.</summary>
    public string? BaselineLineType { get; set; }

    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>See RequestConstants.OperationInvoiceLineMatchStatuses. UNALLOCATED blocks validation.</summary>
    public string MatchStatus { get; set; } = RequestConstants.OperationInvoiceLineMatchStatuses.Unallocated;
}

/// <summary>
/// A proposal to close an obligation below its expected total, and the decision on it.
///
/// <para>Exists because real supply falls short: a partial delivery is accepted, the remainder
/// cancelled, and no further invoice will ever arrive. Without an explicit exit such a group could
/// never reach SATISFIED and would block completion forever.</para>
///
/// <para>A separate entity rather than columns on the group, because it has a review lifecycle and
/// can legitimately repeat — a rejected proposal leaves the obligation open, and a second proposal
/// is a second row with its own justification and evidence.</para>
/// </summary>
public class OperationInvoiceShortClose
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestPoGroupId { get; set; }
    public RequestPoGroup? RequestPoGroup { get; set; }

    /// <summary>PROPOSED, APPROVED or REJECTED — see RequestConstants.ShortCloseStatuses.</summary>
    public string Status { get; set; } = RequestConstants.ShortCloseStatuses.Proposed;

    public Guid ProposedByUserId { get; set; }
    public DateTime ProposedAtUtc { get; set; }
    public string ProposalJustification { get; set; } = string.Empty;
    public Guid? EvidenceAttachmentId { get; set; }

    /// <summary>What was being written off, frozen at proposal time so the audit records the truth.</summary>
    public decimal RemainingAmountAtProposal { get; set; }

    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DecisionReason { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
