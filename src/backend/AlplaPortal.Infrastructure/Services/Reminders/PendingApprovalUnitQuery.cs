using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Reminders;

/// <summary>One approval decision somebody owes: a batch in an approval stage, or a batch-less request in one.</summary>
public sealed class PendingApprovalUnit
{
    public Guid RequestId { get; init; }
    public Guid? ApprovalBatchId { get; init; }
    public int? BatchNumber { get; init; }
    public string RequestNumber { get; init; } = string.Empty;
    /// <summary>AREA or FINAL.</summary>
    public string Stage { get; init; } = string.Empty;
    public int DepartmentId { get; init; }
    public int? PlantId { get; init; }
    public int CompanyId { get; init; }
    /// <summary>Per-request final nominee (Request.FinalApproverId, set at submit from the company nominee). Notification target under the single-final-approver model.</summary>
    public Guid? RequestFinalNomineeId { get; init; }
    /// <summary>Null when the stage entry could not be established from reliable evidence (reported, never guessed).</summary>
    public DateTime? StageEnteredAtUtc { get; init; }
    public int DaysPending { get; set; }
}

public sealed class PendingApprovalRecipient
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}

public sealed class PendingApprovalUnitQueryResult
{
    public List<PendingApprovalUnit> Considered { get; } = new();
    /// <summary>Units older than the threshold with an established stage entry.</summary>
    public List<PendingApprovalUnit> Eligible { get; } = new();
    public List<PendingApprovalUnit> WithoutStageEntry { get; } = new();
    public List<PendingApprovalUnit> WithoutRecipient { get; } = new();
    /// <summary>Recipient → the eligible units they can decide (alternative approvers all receive the unit).</summary>
    public Dictionary<Guid, (PendingApprovalRecipient Recipient, List<PendingApprovalUnit> Units)> ByRecipient { get; } = new();
}

/// <summary>
/// Builds the set of pending approval units and their recipients, mirroring the Approvals Center
/// queue (<c>ApprovalQueueProjection</c>): one unit per batch in WAITING_AREA/FINAL_APPROVAL, plus one
/// unit per request in an approval status that has no batch in that stage (and, for QUOTATION
/// requests, no batches at all). Recipients come EXCLUSIVELY from <see cref="IApprovalRoutingService"/>
/// (DepartmentManagers cascade / company final approvers), i.e. the people actually authorized to
/// decide. Legacy per-request nominees (Request.AreaApproverId / FinalApproverId) are NOT used.
/// </summary>
public sealed class PendingApprovalUnitQuery
{
    public const string StageArea = "AREA";
    public const string StageFinal = "FINAL";

    private readonly ApplicationDbContext _context;
    private readonly IApprovalRoutingService _routing;

    public PendingApprovalUnitQuery(ApplicationDbContext context, IApprovalRoutingService routing)
    {
        _context = context;
        _routing = routing;
    }

    public async Task<PendingApprovalUnitQueryResult> RunAsync(DateTime nowUtc, int minPendingAgeDays, TimeZoneInfo zone, CancellationToken ct)
    {
        var result = new PendingApprovalUnitQueryResult();

        // ── Batch units ─────────────────────────────────────────────────────────────
        var batchStatuses = new[] { RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval, RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval };
        var batches = await _context.ApprovalBatches.AsNoTracking()
            .Where(b => batchStatuses.Contains(b.Status))
            .Select(b => new
            {
                b.Id, b.RequestId, b.BatchNumber, b.Status, b.StageEnteredAtUtc,
                b.Request.RequestNumber, b.Request.DepartmentId, b.Request.PlantId, b.Request.CompanyId, b.Request.FinalApproverId
            })
            .ToListAsync(ct);

        foreach (var b in batches)
        {
            result.Considered.Add(new PendingApprovalUnit
            {
                RequestId = b.RequestId,
                ApprovalBatchId = b.Id,
                BatchNumber = b.BatchNumber,
                RequestNumber = b.RequestNumber ?? string.Empty,
                Stage = b.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval ? StageArea : StageFinal,
                DepartmentId = b.DepartmentId,
                PlantId = b.PlantId,
                CompanyId = b.CompanyId,
                RequestFinalNomineeId = b.FinalApproverId,
                StageEnteredAtUtc = b.StageEnteredAtUtc
            });
        }

        // ── Request units (batch-less), same rule as the queue projection ──────────
        var areaStatuses = new[] { RequestConstants.Statuses.WaitingAreaApproval, RequestConstants.Statuses.WaitingCostCenter };
        var finalStatuses = new[] { RequestConstants.Statuses.WaitingFinalApproval };
        var waitingStatuses = areaStatuses.Concat(finalStatuses).ToArray();

        var requests = await _context.Requests.AsNoTracking()
            .Where(r => waitingStatuses.Contains(r.Status!.Code))
            .Where(r => r.RequestType!.Code != RequestConstants.Types.Quotation || !r.ApprovalBatches.Any())
            .Select(r => new
            {
                r.Id, r.RequestNumber, r.DepartmentId, r.PlantId, r.CompanyId, r.FinalApproverId,
                StatusCode = r.Status!.Code,
                HasAreaBatch = r.ApprovalBatches.Any(b => b.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval),
                HasFinalBatch = r.ApprovalBatches.Any(b => b.Status == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval),
                // Stage entry = latest history row that moved the request INTO its current status.
                StageEnteredAtUtc = r.StatusHistories
                    .Where(h => h.NewStatusId == r.StatusId)
                    .OrderByDescending(h => h.CreatedAtUtc)
                    .Select(h => (DateTime?)h.CreatedAtUtc)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        foreach (var r in requests)
        {
            var stage = areaStatuses.Contains(r.StatusCode) ? StageArea : StageFinal;
            if (stage == StageArea && r.HasAreaBatch) continue;
            if (stage == StageFinal && r.HasFinalBatch) continue;

            result.Considered.Add(new PendingApprovalUnit
            {
                RequestId = r.Id,
                RequestNumber = r.RequestNumber ?? string.Empty,
                Stage = stage,
                DepartmentId = r.DepartmentId,
                PlantId = r.PlantId,
                CompanyId = r.CompanyId,
                RequestFinalNomineeId = r.FinalApproverId,
                StageEnteredAtUtc = r.StageEnteredAtUtc
            });
        }

        // ── Eligibility + recipients ────────────────────────────────────────────────
        var areaCache = new Dictionary<(int, int?), List<PendingApprovalRecipient>>();
        var finalCache = new Dictionary<(Guid?, int), List<PendingApprovalRecipient>>();

        foreach (var unit in result.Considered)
        {
            if (unit.StageEnteredAtUtc is null)
            {
                result.WithoutStageEntry.Add(unit);
                continue;
            }

            unit.DaysPending = ApprovalReminderSchedule.DaysPending(unit.StageEnteredAtUtc.Value, nowUtc, zone);
            if (unit.DaysPending <= minPendingAgeDays) continue;

            result.Eligible.Add(unit);

            var recipients = unit.Stage == StageArea
                ? await ResolveAreaAsync(unit.DepartmentId, unit.PlantId, areaCache)
                : await ResolveFinalAsync(unit.RequestFinalNomineeId, unit.CompanyId, finalCache);

            if (recipients.Count == 0)
            {
                result.WithoutRecipient.Add(unit);
                continue;
            }

            foreach (var rcp in recipients)
            {
                if (!result.ByRecipient.TryGetValue(rcp.UserId, out var bucket))
                {
                    bucket = (rcp, new List<PendingApprovalUnit>());
                    result.ByRecipient[rcp.UserId] = bucket;
                }
                bucket.Units.Add(unit);
            }
        }

        return result;
    }

    private async Task<List<PendingApprovalRecipient>> ResolveAreaAsync(int departmentId, int? plantId, Dictionary<(int, int?), List<PendingApprovalRecipient>> cache)
    {
        if (cache.TryGetValue((departmentId, plantId), out var cached)) return cached;
        var resolved = await _routing.ResolveAreaManagersAsync(departmentId, plantId);
        var list = resolved.Managers
            .Where(m => !string.IsNullOrWhiteSpace(m.Email))
            .Select(m => new PendingApprovalRecipient { UserId = m.UserId, FullName = m.FullName, Email = m.Email })
            .ToList();
        cache[(departmentId, plantId)] = list;
        return list;
    }

    /// <summary>
    /// Single-final-approver model: the request nominee (Request.FinalApproverId) if notifiable, else
    /// the company's current nominee. NOT an authorization rule — see IApprovalRoutingService.
    /// </summary>
    private async Task<List<PendingApprovalRecipient>> ResolveFinalAsync(Guid? requestNomineeId, int companyId, Dictionary<(Guid?, int), List<PendingApprovalRecipient>> cache)
    {
        if (cache.TryGetValue((requestNomineeId, companyId), out var cached)) return cached;
        var resolved = await _routing.ResolveFinalNotificationRecipientsAsync(requestNomineeId, companyId);
        var list = resolved.Recipients
            .Where(a => !string.IsNullOrWhiteSpace(a.Email))
            .Select(a => new PendingApprovalRecipient { UserId = a.UserId, FullName = a.FullName, Email = a.Email })
            .ToList();
        cache[(requestNomineeId, companyId)] = list;
        return list;
    }
}
