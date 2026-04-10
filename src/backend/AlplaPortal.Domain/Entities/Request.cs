namespace AlplaPortal.Domain.Entities;

public class Request
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? RequestNumber { get; set; } // E.g., PRQ-2023-0001
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int RequestTypeId { get; set; }
    public RequestType RequestType { get; set; } = null!;

    public int StatusId { get; set; }
    public RequestStatus Status { get; set; } = null!;

    public Guid RequesterId { get; set; }
    public User Requester { get; set; } = null!;

    // Request Participants (Involved Parties)
    public Guid? BuyerId { get; set; }
    public User? Buyer { get; set; }

    public Guid? AreaApproverId { get; set; }
    public User? AreaApprover { get; set; }

    public Guid? FinalApproverId { get; set; }
    public User? FinalApprover { get; set; }


    public int? NeedLevelId { get; set; }
    public NeedLevel? NeedLevel { get; set; }

    public int DepartmentId { get; set; }
    public Department Department { get; set; } = null!;

    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTime RequestedDateUtc { get; set; }
    public DateTime? NeedByDateUtc { get; set; }
    public DateTime? ScheduledDateUtc { get; set; }

    // Financial
    public int? CurrencyId { get; set; }
    public Currency? Currency { get; set; }
    public decimal EstimatedTotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }

    public int? CapexOpexClassificationId { get; set; }
    public CapexOpexClassification? CapexOpexClassification { get; set; }

    // Workflow Tracking
    public string? CurrentResponsibleRole { get; set; } // Minimal V1 tracking
    public Guid? CurrentResponsibleUserId { get; set; }

    // Audit fields
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public bool IsCancelled { get; set; }
    public Guid? SelectedQuotationId { get; set; }

    // Integration Placeholders
    public string? PrimaveraReference { get; set; }
    public string? AlplaProdReference { get; set; }

    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();
    public ICollection<RequestLineItem> LineItems { get; set; } = new List<RequestLineItem>();
    public ICollection<RequestStatusHistory> StatusHistories { get; set; } = new List<RequestStatusHistory>();
    public ICollection<RequestAttachment> Attachments { get; set; } = new List<RequestAttachment>();
}
