using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Finance;

/// <summary>
/// The single reusable Finance-obligations projection. It builds one obligation per non-cancelled
/// RequestPoGroup (via the pure <see cref="FinanceObligationProjectionBuilder"/> + the canonical
/// <see cref="IFinancePaymentEligibilityService"/>) over a scoped Request population, and computes the
/// currency-safe obligation summary. Extracted from FinanceController.GetObligations so BOTH the Finance
/// endpoint AND DashboardV2 consume identical results — correct layering:
///   FinanceController + DashboardV2QueryService  →  this projection  →  FinancePaymentEligibilityService.
/// No status/eligibility/date logic is re-implemented here; it delegates to the builder + eligibility
/// service. Overdue/DueToday come from the builder (ScheduledDateUtc, open payment obligation only).
/// </summary>
public sealed class FinanceObligationSummaryProjection
{
    private readonly IFinancePaymentEligibilityService _eligibility;

    public FinanceObligationSummaryProjection(IFinancePaymentEligibilityService eligibility)
        => _eligibility = eligibility;

    public sealed record BuildResult(
        List<FinanceObligationDto> Obligations,
        Dictionary<Guid, FinanceObligationContainerDto> Containers);

    // Parent statuses in the finance pipeline (with a PO attachment) OR any quotation with a group in
    // the finance-pipeline set. Identical to the previous FinanceController.GetObligations population.
    private static readonly string[] FinanceStatuses =
    {
        RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.AdvancePaymentRequired, RequestConstants.Statuses.AdvancePaymentCompleted,
        RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.Paid,
        RequestConstants.Statuses.PaymentCompleted, RequestConstants.Statuses.InFollowup,
        RequestConstants.Statuses.Completed, RequestConstants.Statuses.PoPartiallyUploaded
    };
    private static readonly string[] FinanceGroupStatuses =
    {
        RequestConstants.Statuses.PoIssued, RequestConstants.Statuses.AdvancePaymentRequired,
        RequestConstants.Statuses.AdvancePaymentScheduled, RequestConstants.Statuses.AdvancePaymentCompleted,
        RequestConstants.Statuses.WaitingSupplierDelivery, RequestConstants.Statuses.PaymentRequestSent,
        RequestConstants.Statuses.PaymentScheduled, RequestConstants.Statuses.PaymentCompleted,
        RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.Completed
    };

    /// <summary>
    /// Build every obligation (one per non-cancelled group) for the finance-relevant population within
    /// the already-scoped <paramref name="scoped"/> query, plus the per-request containers. Optional
    /// org filters mirror the Finance endpoint. <paramref name="today"/> is the business date (UTC).
    /// </summary>
    public async Task<BuildResult> BuildAsync(
        IQueryable<Request> scoped, int? plantId, int? departmentId, int? companyId, DateTime today)
    {
        var query = scoped.Where(r =>
            (FinanceStatuses.Contains(r.Status!.Code)
                && r.Attachments.Any(a => !a.IsDeleted && a.AttachmentTypeCode == AttachmentConstants.Types.PurchaseOrder))
            || (r.RequestType!.Code == RequestConstants.Types.Quotation
                && r.PoGroups.Any(g => FinanceGroupStatuses.Contains(g.Status))));

        if (plantId.HasValue) query = query.Where(r => r.PlantId == plantId.Value);
        if (departmentId.HasValue) query = query.Where(r => r.DepartmentId == departmentId.Value);
        if (companyId.HasValue) query = query.Where(r => r.CompanyId == companyId.Value);

        var loaded = await query
            .Include(r => r.Status).Include(r => r.RequestType).Include(r => r.Supplier)
            .Include(r => r.Requester).Include(r => r.Plant).Include(r => r.Department)
            .Include(r => r.Quotations).Include(r => r.Currency)
            .Include(r => r.PoGroups).ThenInclude(g => g.Payments)
            .Include(r => r.PoGroups).ThenInclude(g => g.Supplier)
            .AsSplitQuery()
            .ToListAsync();

        var obligations = new List<FinanceObligationDto>();
        var containersById = new Dictionary<Guid, FinanceObligationContainerDto>();

        foreach (var r in loaded)
        {
            var activeGroups = r.PoGroups.Where(g => g.Status != RequestConstants.PoGroupStatuses.Cancelled).ToList();
            if (activeGroups.Count == 0) continue;

            var reqInput = new FinanceObligationProjectionBuilder.RequestInput(
                r.Id, r.RequestNumber ?? string.Empty, r.RequestType!.Code, r.Title,
                r.Department?.Name, r.Plant?.Name);

            var container = new FinanceObligationContainerDto
            {
                RequestId = r.Id,
                RequestNumber = r.RequestNumber ?? string.Empty,
                RequestTypeCode = r.RequestType.Code,
                Title = r.Title,
                Department = r.Department?.Name,
                Plant = r.Plant?.Name,
                CreatedAtUtc = r.CreatedAtUtc,
                SupplierCount = activeGroups.Select(g => g.SupplierId ?? (int?)null).Where(x => x.HasValue).Distinct().Count()
            };

            foreach (var g in activeGroups)
            {
                var supplierName = FinanceGroupDisplayResolver.ResolveSupplierName(
                    g.SupplierNameSnapshot, r.SelectedQuotationId.HasValue,
                    r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.SupplierNameSnapshot : null,
                    r.Supplier?.Name);
                var currency = FinanceGroupDisplayResolver.ResolveCurrencyCode(
                    g.CurrencyCode, r.SelectedQuotationId.HasValue,
                    r.SelectedQuotationId.HasValue ? r.Quotations.FirstOrDefault(q => q.Id == r.SelectedQuotationId.Value)?.Currency : null,
                    r.Currency?.Code);

                var groupInput = new FinanceObligationProjectionBuilder.GroupInput(
                    g.Id, g.SupplierId, supplierName, g.SupplierNifSnapshot, g.Supplier?.TaxId, g.Status,
                    g.PurchaseOrderNumber, currency, g.TotalAmount,
                    _eligibility.EvaluateGroupActions(r.RequestType.Code, r.Status!.Code, g.Status),
                    g.Payments.Select(p => new FinanceObligationProjectionBuilder.PaymentInput(
                        p.Id, p.PaymentType, p.PaymentStatus, p.PlannedAmount, p.ActualPaidAmount,
                        p.ScheduledDateUtc, p.PaidDateUtc, p.PaymentProofAttachmentId.HasValue, p.CurrencyCode)).ToList());

                var o = FinanceObligationProjectionBuilder.Build(reqInput, groupInput, today);
                var dto = new FinanceObligationDto
                {
                    RequestId = o.RequestId, RequestNumber = o.RequestNumber, RequestTypeCode = o.RequestTypeCode,
                    RequestTitle = o.RequestTitle, Department = o.Department, Plant = o.Plant,
                    RequestPoGroupId = o.RequestPoGroupId, SupplierId = o.SupplierId, SupplierName = o.SupplierName,
                    SupplierNif = o.SupplierNif, SupplierTaxId = o.SupplierTaxId, GroupStatusCode = o.GroupStatusCode,
                    GroupStatusLabel = o.GroupStatusLabel, OperationalStateLabel = o.OperationalStateLabel,
                    PurchaseOrderNumber = o.PurchaseOrderNumber, CurrencyCode = o.CurrencyCode, GroupAmount = o.GroupAmount,
                    PaymentId = o.PaymentId, PaymentType = o.PaymentType, ScheduledDateUtc = o.ScheduledDateUtc,
                    PlannedAmount = o.PlannedAmount, ActualPaidAmount = o.ActualPaidAmount, PaidDateUtc = o.PaidDateUtc,
                    HasPaymentProof = o.HasPaymentProof, FinanceActions = o.FinanceActions.ToList(),
                    ActionClass = o.ActionClass, ActionClassLabel = o.ActionClassLabel, NextActionLabel = o.NextActionLabel,
                    ResponsibleRole = o.ResponsibleRole, DueDate = o.DueDate, IsOverdue = o.IsOverdue,
                    OverdueDays = o.OverdueDays, IsDueToday = o.IsDueToday, ObligationAmount = o.ObligationAmount
                };
                container.Obligations.Add(dto);
                obligations.Add(dto);
            }

            container.TotalsByCurrency = SumByCurrency(container.Obligations.Select(x => (x.CurrencyCode, x.ObligationAmount)));
            container.ExpandByDefault = container.Obligations.Count > 1;
            containersById[r.Id] = container;
        }

        return new BuildResult(obligations, containersById);
    }

    /// <summary>Currency-safe totals (never summed across currencies; null currency → "—").</summary>
    public static List<FinanceCurrencyAmountDto> SumByCurrency(IEnumerable<(string? Currency, decimal Amount)> rows) =>
        rows.GroupBy(x => string.IsNullOrWhiteSpace(x.Currency) ? "—" : x.Currency!)
            .Select(g => new FinanceCurrencyAmountDto { CurrencyCode = g.Key, Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(c => c.Amount).ToList();

    private static FinanceObligationCardDto Card(string actionClass, IEnumerable<FinanceObligationDto> obligations, Func<FinanceObligationDto, bool> predicate)
    {
        var matched = obligations.Where(predicate).ToList();
        return new FinanceObligationCardDto
        {
            ActionClass = actionClass,
            Label = FinanceActionClasses.Label(actionClass),
            Count = matched.Count,
            AmountsByCurrency = SumByCurrency(matched.Select(o => (o.CurrencyCode, o.ObligationAmount)))
        };
    }

    /// <summary>Currency-safe obligation summary — identical output to the previous controller helper.</summary>
    public static FinanceObligationSummaryDto BuildSummary(IReadOnlyList<FinanceObligationDto> obligations)
    {
        var actionable = obligations.Where(o => FinanceActionClasses.IsFinanceActionable(o.ActionClass)).ToList();

        return new FinanceObligationSummaryDto
        {
            NeedsScheduling = Card(FinanceActionClasses.NeedsScheduling, obligations, o => o.ActionClass == FinanceActionClasses.NeedsScheduling),
            NeedsPayment = Card(FinanceActionClasses.NeedsPayment, obligations, o => o.ActionClass == FinanceActionClasses.NeedsPayment),
            DueToday = new FinanceObligationCardDto
            {
                ActionClass = "DUE_TODAY", Label = "Vence Hoje",
                Count = obligations.Count(o => o.IsDueToday),
                AmountsByCurrency = SumByCurrency(obligations.Where(o => o.IsDueToday).Select(o => (o.CurrencyCode, o.ObligationAmount)))
            },
            Overdue = new FinanceObligationCardDto
            {
                ActionClass = "OVERDUE", Label = "Atrasados",
                Count = obligations.Count(o => o.IsOverdue),
                AmountsByCurrency = SumByCurrency(obligations.Where(o => o.IsOverdue).Select(o => (o.CurrencyCode, o.ObligationAmount)))
            },
            PaidWaitingReceiving = Card(FinanceActionClasses.PaidWaitingReceiving, obligations, o => o.ActionClass == FinanceActionClasses.PaidWaitingReceiving),
            ActionableTotal = actionable.Count,
            ActionableAmountsByCurrency = SumByCurrency(actionable.Select(o => (o.CurrencyCode, o.ObligationAmount)))
        };
    }
}
