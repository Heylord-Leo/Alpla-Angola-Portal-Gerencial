namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Purchase/acquisition record for an equipment asset.
/// One-to-one relationship with ITEquipment.
/// Contains nullable future-integration fields for linking to the purchasing module.
/// </summary>
public class ITEquipmentAcquisition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EquipmentId { get; set; }
    public ITEquipment? Equipment { get; set; }

    public DateTime? AcquisitionDate { get; set; }
    public string? SupplierName { get; set; }

    // ── Future Integration Fields (nullable, not enforced yet) ──
    public int? SupplierId { get; set; }
    public Guid? PurchaseRequestId { get; set; }
    public string? PurchaseRequestNumber { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? FinancePaymentId { get; set; }
    // ────────────────────────────────────────────────────────────

    public string? InvoiceNumber { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaymentDate { get; set; }
    public decimal? PurchaseAmount { get; set; }
    public string? Currency { get; set; }

    public DateTime? WarrantyStartDate { get; set; }
    public DateTime? WarrantyEndDate { get; set; }
    public string? WarrantyNotes { get; set; }

    public string? AcquisitionNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}
