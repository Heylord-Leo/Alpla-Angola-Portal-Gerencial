using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;
using AlplaPortal.Application.Projections;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Projections;

/// <summary>
/// Builds the Approval Center queue for a single stage as ONE ROW PER ACTIONABLE UNIT.
///
/// <para>The unit is the actionable <c>ApprovalBatch</c> (whose status matches the stage), NOT the
/// Request. A request with two simultaneous WAITING_AREA_APPROVAL batches yields two rows sharing a
/// request number but with distinct <see cref="ApprovalQueueItemDto.ApprovalBatchId"/>. Requests
/// that are actionable purely by their own status and have NO matching batch (PAYMENT, legacy
/// whole-request QUOTATION) yield a single request-level row (ApprovalBatchId = null).</para>
///
/// <para>Extracted from the controller so the collapse-free grouping rule is directly testable
/// against a real EF query. The money for each row comes from the shared
/// <see cref="ApprovalQueueAmountResolver"/>, applied per row.</para>
/// </summary>
public static class ApprovalQueueProjection
{
    public const string StageArea = "AREA";
    public const string StageFinal = "FINAL";

    /// <param name="stageQuery">Requests already scoped/filtered to this stage's queue.</param>
    /// <param name="approvalStage">"AREA" or "FINAL".</param>
    /// <param name="statusDisplay">Status Code → (Name, BadgeColor), so a batch row can render its
    /// OWN status (e.g. WAITING_AREA_APPROVAL) even when the parent request status differs.</param>
    public static async Task<List<ApprovalQueueItemDto>> ProjectAsync(
        IQueryable<Request> stageQuery,
        string approvalStage,
        IReadOnlyDictionary<string, (string Name, string Color)> statusDisplay,
        DateTime today,
        DateTime tomorrow,
        DateTime in4Days)
    {
        var stageBatchStatus = approvalStage == StageArea
            ? RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval
            : RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval;

        // ── A. One row per actionable batch (QUOTATION partial-approval model) ──
        var batchRows = await stageQuery
            .SelectMany(
                r => r.ApprovalBatches.Where(b => b.Status == stageBatchStatus),
                (r, b) => new { r, b })
            .Select(x => new BatchRow
            {
                RequestId = x.r.Id,
                RequestNumber = x.r.RequestNumber,
                Title = x.r.Title,
                RequestStatusCode = x.r.Status!.Code ?? string.Empty,
                RequestStatusName = x.r.Status.Name ?? string.Empty,
                RequestTypeId = x.r.RequestType!.Id,
                RequestTypeCode = x.r.RequestType.Code ?? string.Empty,
                RequestTypeName = x.r.RequestType.Name ?? string.Empty,
                RequesterName = x.r.Requester.FullName ?? string.Empty,
                DepartmentId = x.r.DepartmentId,
                DepartmentName = x.r.Department != null ? x.r.Department.Name : null,
                CompanyId = x.r.CompanyId,
                CompanyName = x.r.Company != null ? x.r.Company.Name : string.Empty,
                PlantId = x.r.PlantId,
                PlantName = x.r.Plant != null ? x.r.Plant.Name : "---",
                CostCenterCode = x.r.LineItems.Where(l => !l.IsDeleted && l.CostCenter != null).Select(l => l.CostCenter!.Code).FirstOrDefault(),
                CostCenterName = x.r.LineItems.Where(l => !l.IsDeleted && l.CostCenter != null).Select(l => l.CostCenter!.Name).FirstOrDefault(),
                NeedLevelId = x.r.NeedLevelId,
                NeedByDateUtc = x.r.NeedByDateUtc,
                CreatedAtUtc = x.r.CreatedAtUtc,
                SelectedQuotationId = x.r.SelectedQuotationId,
                // ── batch identity + money ingredients (this batch only) ──
                BatchId = x.b.Id,
                BatchNumber = x.b.BatchNumber,
                BatchStatus = x.b.Status,
                BatchSnapshot = x.b.ApprovedTotalAmount,
                BatchItemSum = (decimal?)x.b.Items.Sum(i => i.SelectedQuotationItem.LineTotal),
                BatchItemCount = x.b.Items.Count,
                BatchSupplier = x.b.Items.Select(i => i.SelectedQuotationItem.Quotation.SupplierNameSnapshot).FirstOrDefault(),
                BatchCurrency = x.b.Items.Select(i => i.SelectedQuotationItem.Quotation.Currency).FirstOrDefault(),
            })
            .ToListAsync();

        // ── B. Request-level rows: actionable by request status, with NO matching batch ──
        //   Covers PAYMENT (never batched) and legacy whole-request QUOTATION approvals.
        var requestRows = await stageQuery
            .Where(r => !r.ApprovalBatches.Any(b => b.Status == stageBatchStatus))
            .Select(x => new RequestRow
            {
                RequestId = x.Id,
                RequestNumber = x.RequestNumber,
                Title = x.Title,
                RequestStatusCode = x.Status!.Code ?? string.Empty,
                RequestStatusName = x.Status.Name ?? string.Empty,
                RequestStatusBadgeColor = x.Status.BadgeColor ?? string.Empty,
                RequestTypeId = x.RequestType!.Id,
                RequestTypeCode = x.RequestType.Code ?? string.Empty,
                RequestTypeName = x.RequestType.Name ?? string.Empty,
                RequesterName = x.Requester.FullName ?? string.Empty,
                DepartmentId = x.DepartmentId,
                DepartmentName = x.Department != null ? x.Department.Name : null,
                CompanyId = x.CompanyId,
                CompanyName = x.Company != null ? x.Company.Name : string.Empty,
                PlantId = x.PlantId,
                PlantName = x.Plant != null ? x.Plant.Name : "---",
                CostCenterCode = x.LineItems.Where(l => !l.IsDeleted && l.CostCenter != null).Select(l => l.CostCenter!.Code).FirstOrDefault(),
                CostCenterName = x.LineItems.Where(l => !l.IsDeleted && l.CostCenter != null).Select(l => l.CostCenter!.Name).FirstOrDefault(),
                NeedLevelId = x.NeedLevelId,
                NeedByDateUtc = x.NeedByDateUtc,
                CreatedAtUtc = x.CreatedAtUtc,
                SelectedQuotationId = x.SelectedQuotationId,
                ActiveLineItemCount = x.LineItems.Count(l => !l.IsDeleted),
                // money ingredients (no active batch → snapshot / legacy quotation / estimate)
                HasSelectedQuotation = x.SelectedQuotationId.HasValue,
                SelectedQuotationTotal = x.Quotations.Where(q => q.Id == x.SelectedQuotationId).Select(q => (decimal?)q.TotalAmount).FirstOrDefault(),
                SelectedQuotationSupplier = x.Quotations.Where(q => q.Id == x.SelectedQuotationId).Select(q => q.SupplierNameSnapshot).FirstOrDefault(),
                SelectedQuotationCurrency = x.Quotations.Where(q => q.Id == x.SelectedQuotationId).Select(q => q.Currency).FirstOrDefault(),
                RequestApprovedTotal = x.ApprovedTotalAmount,
                RequestEstimate = x.EstimatedTotalAmount,
                LegacySupplierName = x.Supplier != null ? x.Supplier.Name : null,
                LegacyCurrencyCode = x.Currency != null ? x.Currency.Code : null,
            })
            .ToListAsync();

        var items = new List<ApprovalQueueItemDto>(batchRows.Count + requestRows.Count);

        foreach (var x in batchRows)
        {
            var res = ApprovalQueueAmountResolver.Resolve(
                x.RequestTypeCode,
                hasActiveBatch: true,
                activeBatchNumber: x.BatchNumber,
                activeBatchSnapshot: x.BatchSnapshot,
                activeBatchItemSum: x.BatchItemSum,
                activeBatchItemCount: x.BatchItemCount,
                hasSelectedQuotation: x.SelectedQuotationId.HasValue,
                selectedQuotationTotal: null,
                requestApprovedTotal: null,
                requestEstimate: 0m);

            var (statusName, statusColor) = ResolveStatusDisplay(statusDisplay, x.BatchStatus, x.RequestStatusName, string.Empty);

            items.Add(new ApprovalQueueItemDto
            {
                RequestId = x.RequestId,
                RequestNumber = x.RequestNumber,
                ApprovalBatchId = x.BatchId,
                LotNumber = x.BatchNumber,
                ApprovalStage = approvalStage,
                QueueKey = $"{x.BatchId:D}:{approvalStage}",
                BatchStatus = x.BatchStatus,
                StatusName = statusName,
                StatusBadgeColor = statusColor,
                RequestStatusCode = x.RequestStatusCode,
                RequestStatusName = x.RequestStatusName,
                Title = x.Title,
                RequestTypeId = x.RequestTypeId,
                RequestTypeCode = x.RequestTypeCode,
                RequestTypeName = x.RequestTypeName,
                RequesterName = x.RequesterName,
                DepartmentId = x.DepartmentId,
                DepartmentName = x.DepartmentName,
                CompanyId = x.CompanyId,
                CompanyName = x.CompanyName,
                PlantId = x.PlantId,
                PlantName = x.PlantName,
                SupplierDisplay = string.IsNullOrWhiteSpace(x.BatchSupplier) ? null : x.BatchSupplier,
                CostCenterCode = x.CostCenterCode,
                CostCenterName = x.CostCenterName,
                CurrencyCode = string.IsNullOrWhiteSpace(x.BatchCurrency) ? null : x.BatchCurrency,
                ItemCount = x.BatchItemCount,
                ActionableAmount = res.Amount,
                ActionableAmountSource = res.Source,
                HasAmountInconsistency = res.HasInconsistency,
                NeedLevelId = x.NeedLevelId,
                NeedByDateUtc = x.NeedByDateUtc,
                CreatedAtUtc = x.CreatedAtUtc,
                SelectedQuotationId = x.SelectedQuotationId,
            });
        }

        foreach (var x in requestRows)
        {
            var res = ApprovalQueueAmountResolver.Resolve(
                x.RequestTypeCode,
                hasActiveBatch: false,
                activeBatchNumber: null,
                activeBatchSnapshot: null,
                activeBatchItemSum: null,
                activeBatchItemCount: 0,
                hasSelectedQuotation: x.HasSelectedQuotation,
                selectedQuotationTotal: x.SelectedQuotationTotal,
                requestApprovedTotal: x.RequestApprovedTotal,
                requestEstimate: x.RequestEstimate);

            // Request-level rows show their own request status.
            var (statusName, statusColor) = (x.RequestStatusName, x.RequestStatusBadgeColor);

            var supplier = x.HasSelectedQuotation ? x.SelectedQuotationSupplier : x.LegacySupplierName;
            var currency = x.HasSelectedQuotation ? x.SelectedQuotationCurrency : x.LegacyCurrencyCode;

            items.Add(new ApprovalQueueItemDto
            {
                RequestId = x.RequestId,
                RequestNumber = x.RequestNumber,
                ApprovalBatchId = null,
                LotNumber = null,
                ApprovalStage = approvalStage,
                QueueKey = $"{x.RequestId:D}:{approvalStage}",
                BatchStatus = x.RequestStatusCode,
                StatusName = statusName,
                StatusBadgeColor = statusColor,
                RequestStatusCode = x.RequestStatusCode,
                RequestStatusName = x.RequestStatusName,
                Title = x.Title,
                RequestTypeId = x.RequestTypeId,
                RequestTypeCode = x.RequestTypeCode,
                RequestTypeName = x.RequestTypeName,
                RequesterName = x.RequesterName,
                DepartmentId = x.DepartmentId,
                DepartmentName = x.DepartmentName,
                CompanyId = x.CompanyId,
                CompanyName = x.CompanyName,
                PlantId = x.PlantId,
                PlantName = x.PlantName,
                SupplierDisplay = string.IsNullOrWhiteSpace(supplier) ? null : supplier,
                CostCenterCode = x.CostCenterCode,
                CostCenterName = x.CostCenterName,
                CurrencyCode = string.IsNullOrWhiteSpace(currency) ? null : currency,
                ItemCount = x.ActiveLineItemCount,
                ActionableAmount = res.Amount,
                ActionableAmountSource = res.Source,
                HasAmountInconsistency = res.HasInconsistency,
                NeedLevelId = x.NeedLevelId,
                NeedByDateUtc = x.NeedByDateUtc,
                CreatedAtUtc = x.CreatedAtUtc,
                SelectedQuotationId = x.SelectedQuotationId,
            });
        }

        // Defensive: dedupe strictly by batch+stage identity (should never trigger).
        var deduped = items
            .GroupBy(i => i.QueueKey)
            .Select(g => g.First())
            .ToList();

        // Same triage order the request-level query used (urgency bucket, need level, recency).
        return deduped
            .OrderByDescending(i => UrgencyBucket(i.NeedByDateUtc, i.RequestStatusCode, today, tomorrow, in4Days))
            .ThenByDescending(i => i.NeedLevelId ?? 0)
            .ThenByDescending(i => i.CreatedAtUtc)
            .ToList();
    }

    private static (string Name, string Color) ResolveStatusDisplay(
        IReadOnlyDictionary<string, (string Name, string Color)> statusDisplay,
        string statusCode,
        string fallbackName,
        string fallbackColor)
    {
        if (!string.IsNullOrEmpty(statusCode) && statusDisplay.TryGetValue(statusCode, out var d))
            return d;
        return (fallbackName, fallbackColor);
    }

    private static int UrgencyBucket(DateTime? needBy, string statusCode, DateTime today, DateTime tomorrow, DateTime in4Days)
    {
        if (statusCode == "REJECTED" || statusCode == "CANCELLED" || statusCode == "COMPLETED")
            return -1;
        if (needBy.HasValue && needBy.Value < today) return 3;
        if (needBy.HasValue && needBy.Value >= today && needBy.Value < tomorrow) return 2;
        if (needBy.HasValue && needBy.Value >= tomorrow && needBy.Value < in4Days) return 1;
        return 0;
    }

    private sealed class BatchRow
    {
        public Guid RequestId { get; set; }
        public string? RequestNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string RequestStatusCode { get; set; } = string.Empty;
        public string RequestStatusName { get; set; } = string.Empty;
        public int RequestTypeId { get; set; }
        public string RequestTypeCode { get; set; } = string.Empty;
        public string RequestTypeName { get; set; } = string.Empty;
        public string RequesterName { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int? PlantId { get; set; }
        public string? PlantName { get; set; }
        public string? CostCenterCode { get; set; }
        public string? CostCenterName { get; set; }
        public int? NeedLevelId { get; set; }
        public DateTime? NeedByDateUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public Guid? SelectedQuotationId { get; set; }
        public Guid BatchId { get; set; }
        public int BatchNumber { get; set; }
        public string BatchStatus { get; set; } = string.Empty;
        public decimal? BatchSnapshot { get; set; }
        public decimal? BatchItemSum { get; set; }
        public int BatchItemCount { get; set; }
        public string? BatchSupplier { get; set; }
        public string? BatchCurrency { get; set; }
    }

    private sealed class RequestRow
    {
        public Guid RequestId { get; set; }
        public string? RequestNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string RequestStatusCode { get; set; } = string.Empty;
        public string RequestStatusName { get; set; } = string.Empty;
        public string RequestStatusBadgeColor { get; set; } = string.Empty;
        public int RequestTypeId { get; set; }
        public string RequestTypeCode { get; set; } = string.Empty;
        public string RequestTypeName { get; set; } = string.Empty;
        public string RequesterName { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int? PlantId { get; set; }
        public string? PlantName { get; set; }
        public string? CostCenterCode { get; set; }
        public string? CostCenterName { get; set; }
        public int? NeedLevelId { get; set; }
        public DateTime? NeedByDateUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public Guid? SelectedQuotationId { get; set; }
        public int ActiveLineItemCount { get; set; }
        public bool HasSelectedQuotation { get; set; }
        public decimal? SelectedQuotationTotal { get; set; }
        public string? SelectedQuotationSupplier { get; set; }
        public string? SelectedQuotationCurrency { get; set; }
        public decimal? RequestApprovedTotal { get; set; }
        public decimal RequestEstimate { get; set; }
        public string? LegacySupplierName { get; set; }
        public string? LegacyCurrencyCode { get; set; }
    }
}
