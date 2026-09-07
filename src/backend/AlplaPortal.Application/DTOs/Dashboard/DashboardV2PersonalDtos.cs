using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Dashboard;

// ── B5: Dashboard V2 "Minha Operação" (PESSOAL) — canonical personal actions only. ──
// This plane contains ONLY work explicitly owned by the signed-in user: assigned actionable Buyer
// work (BuyerId == me), Area-approval work the user personally owns (AreaApproverId == me OR an active
// DepartmentManager scope), and the user's own DRAFT requests. Shared role membership NEVER creates a
// personal action: Final Approval (PD-01), the Finance queue, the Receiving queue and the unassigned
// Buyer pool are all excluded. No monetary amounts, and no urgency buckets in B5.1 (deferred until a
// defensible per-domain due date exists for every personal domain — truthfulness over the old layout).

public static class PersonalActionDomains
{
    public const string Buyer = "BUYER";
    public const string Approval = "APPROVAL";
    public const string Requester = "REQUESTER";
}

public static class PersonalActionEntityTypes
{
    public const string Request = "REQUEST";
    public const string ApprovalBatch = "APPROVAL_BATCH";
}

public static class PersonalActionTypes
{
    // Buyer action codes mirror BuyerQueueConstants.ActionCodes (canonical).
    public const string AreaApproval = "AREA_APPROVAL";
    public const string SubmitDraft = "SUBMIT_DRAFT";
}

/// <summary>
/// One personally-owned, currently-open action. A single request may legitimately carry several
/// distinct actions (different domain/action), so the identity is
/// Domain + EntityType + EntityId + ActionType — never RequestId alone.
/// </summary>
public sealed class PersonalActionDto
{
    public string Domain { get; set; } = string.Empty;      // BUYER | APPROVAL | REQUESTER
    public string EntityType { get; set; } = string.Empty;  // REQUEST | APPROVAL_BATCH
    public string EntityId { get; set; } = string.Empty;    // request id or batch id (string form)
    public Guid RequestId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;  // ADD_QUOTATION | SUBMIT_BATCH | RESOLVE_ADJUSTMENT | AREA_APPROVAL | SUBMIT_DRAFT
    public string? Title { get; set; }
    public string? TargetPath { get; set; }                 // canonical route, or null when none exists
    public string? DueDate { get; set; }                    // ISO date; null in B5.1 (urgency deferred)
}

public sealed class PersonalActionDomainCountDto
{
    public string Domain { get; set; } = string.Empty;
    public int Actions { get; set; }
    public int Requests { get; set; } // distinct RequestId within the domain
}

/// <summary>
/// B5.1 truthful summary: action count, distinct-request count, and a per-domain breakdown.
/// No Critical/Overdue/NearDeadline yet — those await a defensible per-domain date source (§8).
/// </summary>
public sealed class PersonalActionSummaryDto
{
    public int ActionableActions { get; set; }
    public int ActionableRequests { get; set; } // distinct RequestId across all personal actions
    public List<PersonalActionDomainCountDto> ByDomain { get; set; } = new();
}

public sealed class DashboardV2PersonalSectionDto
{
    public PersonalActionSummaryDto Summary { get; set; } = new();
    /// <summary>Bounded top-N action rows for drill-down; Summary always reflects the full set.</summary>
    public List<PersonalActionDto> Actions { get; set; } = new();
}
