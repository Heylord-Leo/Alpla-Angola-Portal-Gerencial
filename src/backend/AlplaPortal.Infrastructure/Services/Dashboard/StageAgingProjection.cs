using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Dashboard;

// ── Dashboard V2 B9.4 — canonical Stage Aging projection (read-only, GERENCIAL). ──
// Reads OperationalStageState directly (never a legacy summary sweep): ONE bounded query
// joins active, in-scope snapshots to the scoped requests; the few-hundred rows are then classified in
// memory (age is Africa/Luanda calendar-days, not SQL-translatable). Unknown age is first-class — only
// known-age entities contribute to severity, oldest age, and the attention/critical summary. Buyer/REQUEST
// snapshots and FIN_PAID/terminal codes are excluded by the active-stage catalog + entity-type filter.
public sealed class StageAgingProjection
{
    private readonly ApplicationDbContext _context;
    public StageAgingProjection(ApplicationDbContext context) => _context = context;

    private static readonly string[] ActiveEntityTypes =
    {
        OperationalStageEntityTypes.ApprovalBatch, OperationalStageEntityTypes.PoGroup, // NOT Request (out of scope)
    };

    private sealed record Row(string StageCode, string EntityType, Guid RequestId, DateTime? StageEnteredAtUtc);

    public async Task<DashboardV2StageAgingDto> BuildAsync(
        IQueryable<Request> scoped, bool entitled, DateTime nowUtc, CancellationToken ct = default)
    {
        if (!entitled)
            return new DashboardV2StageAgingDto { Summary = null, Stages = new(), GeneratedAtUtc = nowUtc };

        var activeCodes = StageAgingCatalog.ActiveStageCodes;

        // ONE bounded query: active in-scope snapshots joined to the scoped request set (no Include graph).
        var rows = await (
            from s in _context.Set<OperationalStageState>().AsNoTracking()
            where ActiveEntityTypes.Contains(s.EntityType) && activeCodes.Contains(s.StageCode)
            join r in scoped on s.RequestId equals r.Id
            select new Row(s.StageCode, s.EntityType, s.RequestId, s.StageEnteredAtUtc)
        ).ToListAsync(ct);

        var stages = new List<DashboardV2StageAgingStageDto>();
        int totalAttention = 0, totalCritical = 0;

        foreach (var meta in StageAgingCatalog.ActiveStages) // canonical pipeline order
        {
            var group = rows.Where(x => x.StageCode == meta.StageCode).ToList();
            if (group.Count == 0) continue; // only stages that currently contain work

            var known = group.Where(x => x.StageEnteredAtUtc != null).ToList();
            var dto = new DashboardV2StageAgingStageDto
            {
                Domain = meta.Domain,
                StageCode = meta.StageCode,
                Label = meta.Label,
                EntityType = meta.EntityType,
                SortOrder = meta.SortOrder,
                EntityCount = group.Count,
                RequestCount = group.Select(x => x.RequestId).Distinct().Count(),
                KnownAgeEntityCount = known.Count,
                UnknownAgeEntityCount = group.Count - known.Count,
                ThresholdProfile = meta.Threshold,
                CanNavigate = false,   // managerial analytics — read-only in B9.4 (see route audit)
                TargetPath = null,
            };

            if (known.Count > 0)
            {
                var oldest = known.Min(x => x.StageEnteredAtUtc!.Value);
                dto.OldestStageEnteredAtUtc = oldest;
                dto.OldestAgeDays = StageAgingPolicy.AgeDays(oldest, nowUtc);
            }

            if (meta.Threshold != null)
            {
                int normal = 0, attention = 0, critical = 0;
                foreach (var x in known)
                {
                    switch (StageAgingPolicy.Classify(StageAgingPolicy.AgeDays(x.StageEnteredAtUtc!.Value, nowUtc), meta.Threshold))
                    {
                        case StageAgingSeverity.Critical: critical++; break;
                        case StageAgingSeverity.Attention: attention++; break;
                        default: normal++; break;
                    }
                }
                dto.NormalCount = normal;
                dto.AttentionCount = attention;
                dto.CriticalCount = critical;
                totalAttention += attention;
                totalCritical += critical;
            }
            // Thresholdless stages leave Normal/Attention/Critical null (not applicable, never 0-misread).

            stages.Add(dto);
        }

        var summary = new DashboardV2StageAgingSummaryDto
        {
            TotalActiveEntities = rows.Count,
            TotalActiveRequests = rows.Select(x => x.RequestId).Distinct().Count(),
            KnownAgeEntities = rows.Count(x => x.StageEnteredAtUtc != null),
            UnknownAgeEntities = rows.Count(x => x.StageEnteredAtUtc == null),
            AttentionEntities = totalAttention,
            CriticalEntities = totalCritical,
        };

        return new DashboardV2StageAgingDto { Summary = summary, Stages = stages, GeneratedAtUtc = nowUtc };
    }
}
