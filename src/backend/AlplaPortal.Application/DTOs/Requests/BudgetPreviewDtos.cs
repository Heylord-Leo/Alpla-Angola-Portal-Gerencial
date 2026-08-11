using System;
using System.Collections.Generic;
using AlplaPortal.Application.DTOs.Common;

namespace AlplaPortal.Application.DTOs.Requests;

public class BudgetPreviewRequestDto
{
    /// <summary>
    /// Maps RequestLineItemId -> QuotationItemId (wizard item awards)
    /// </summary>
    public Dictionary<Guid, Guid> ItemAwards { get; set; } = new();

    /// <summary>
    /// Maps RequestLineItemId -> {PlantId, CostCenterId} (wizard assignments)
    /// </summary>
    public Dictionary<Guid, ItemApprovalAssignmentDto> ItemAssignments { get; set; } = new();

    /// <summary>
    /// Maps RequestLineItemId -> List of { PlantId, CostCenterId, Percentage, Comment } (wizard allocations)
    /// </summary>
    public Dictionary<Guid, List<ItemAllocationLineDto>> ItemAllocations { get; set; } = new();

    /// <summary>
    /// Extra items decisions (from WizardStepSelection)
    /// </summary>
    public Dictionary<Guid, ExtraItemDecisionDto>? ExtraItemDecisions { get; set; }

    /// <summary>
    /// When set, scopes the preview to items in this batch only.
    /// Winners are read from ApprovalBatchItem.SelectedQuotationItemId (dto.ItemAwards is ignored).
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>
    /// Candidate model (batch-scoped previews only): the Area Approver's TENTATIVE winner
    /// selections, carried by IDENTITY only — the server values them from the FROZEN
    /// ApprovalBatchItemCandidate snapshots, never from client-sent amounts and never from the
    /// live quotation. Partial selections are allowed (unselected items contribute nothing);
    /// nothing is ever persisted by a preview.
    /// </summary>
    public List<BudgetPreviewSelectionDto>? Selections { get; set; }
}

/// <summary>One tentative winner selection inside a batch-scoped budget preview.</summary>
public class BudgetPreviewSelectionDto
{
    public Guid ApprovalBatchItemId { get; set; }
    public Guid SelectedCandidateId { get; set; }
}

public class BudgetPreviewResponseDto
{
    public BudgetPreviewSummaryDto Summary { get; set; } = new();
    public List<BudgetAllocationPreviewDto> Allocations { get; set; } = new();
    public List<AlternativeBudgetLineDto> AlternativeBudgetLines { get; set; } = new();
    public bool RequiresJustification { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class BudgetPreviewSummaryDto
{
    public decimal TotalBudget { get; set; }
    public decimal AlreadyConsumed { get; set; }
    public decimal ThisRequestAmount { get; set; }
    public decimal ProjectedBalance { get; set; }
    public decimal ProjectedUsagePercent { get; set; }
    public string OverallStatus { get; set; } = "SAFE";
    public string CurrencyCode { get; set; } = "AOA";
    public int FiscalYear { get; set; }
    
    /// <summary>
    /// Executive summary message for the approver (localized, Portuguese)
    /// </summary>
    public string ExecutiveSummary { get; set; } = string.Empty;
}

public class BudgetAllocationPreviewDto
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int PlantId { get; set; }
    public string PlantName { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int? CostCenterId { get; set; }
    public string CostCenterCode { get; set; } = string.Empty;
    public string CostCenterName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "AOA";
    public int FiscalYear { get; set; }
    public decimal AnnualBudget { get; set; }
    public decimal AlreadyConsumed { get; set; }
    public decimal ThisRequestAmount { get; set; }
    public decimal ProjectedConsumed { get; set; }
    public decimal ProjectedBalance { get; set; }
    public decimal ProjectedUsagePercent { get; set; }
    public string Status { get; set; } = "SAFE";
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// Item-level breakdown for this allocation group
    /// </summary>
    public List<BudgetAllocationItemDto> Items { get; set; } = new();
}

public class BudgetAllocationItemDto
{
    public Guid RequestLineItemId { get; set; }
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class AlternativeBudgetLineDto
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int PlantId { get; set; }
    public string PlantName { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public int? CostCenterId { get; set; }
    public string CostCenterCode { get; set; } = string.Empty;
    public string CostCenterName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "AOA";
    public int FiscalYear { get; set; }
    public decimal AnnualBudget { get; set; }
    public decimal AlreadyConsumed { get; set; }
    public decimal AvailableBefore { get; set; }
    public decimal ProjectedBalanceIfApplied { get; set; }
    public decimal ProjectedUsagePercentIfApplied { get; set; }
    public string Status { get; set; } = "SAFE";
}
