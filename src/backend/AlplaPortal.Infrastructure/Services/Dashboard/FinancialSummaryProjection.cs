using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Dashboard;
using AlplaPortal.Application.DTOs.Finance;
using AlplaPortal.Application.Interfaces.Finance;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Services.Finance;
using Microsoft.EntityFrameworkCore;
using Agg = AlplaPortal.Infrastructure.Services.Dashboard.FinancialCurrencyAggregator;

namespace AlplaPortal.Infrastructure.Services.Dashboard;

/// <summary>
/// Dashboard V2 B7 — canonical currency-safe Financial Summary (GERENCIAL, read-only). Builds current
/// monetary EXPOSURE per category, partitioned by currency (never summed across currencies), from the
/// SAME canonical populations the operational screens use:
///   • Em aprovação          → ApprovalBatch (WAITING_AREA/FINAL_APPROVAL), authoritative winner snapshot
///                             (ApprovedTotalAmount, else decided-candidate line sum; NO estimate fallback).
///   • Aguardando P.O.       → RequestPoGroup (WAITING_PO / WAITING_PO_CORRECTION), group TotalAmount,
///                             currency via the canonical FinanceGroupDisplayResolver.
///   • Em processamento fin. → FinanceObligationSummaryProjection obligations with ActionClass ∈
///                             {NEEDS_SCHEDULING, NEEDS_PAYMENT} (PD-B7-04), group amount.
///   • Pago / aguard. receb. → obligations with ActionClass PAID_WAITING_RECEIVING; amount = actual paid
///                             evidence = Σ RequestPayment.ActualPaidAmount (COMPLETED, owed-money types),
///                             partitioned by PAYMENT currency (PD-B7-08); refunds excluded (PD-B7-12).
///
/// No FX conversion, no urgency/action classes (that stays in B3), no paid history (B7.3), no completed
/// card (PD-B7-09). Amounts are the all-in total at each grain (quotation totals include IVA, PD-B7-13).
/// </summary>
public sealed class FinancialSummaryProjection
{
    private readonly ApplicationDbContext _context;
    private readonly IFinancePaymentEligibilityService _eligibility;

    public FinancialSummaryProjection(ApplicationDbContext context, IFinancePaymentEligibilityService eligibility)
    {
        _context = context;
        _eligibility = eligibility;
    }

    // Owed-money payment types that count as "paid" evidence — REFUND and OTHER are deliberately excluded
    // (PD-B7-12; mirrors GroupCompletionProjection.OwedMoneyPaymentTypes).
    private static readonly string[] OwedMoneyPaymentTypes =
    {
        RequestPayment.PaymentTypes.Advance,
        RequestPayment.PaymentTypes.FinalBalance,
        RequestPayment.PaymentTypes.Regularization,
    };

    public async Task<List<FinancialCategoryDto>> BuildAsync(IQueryable<Request> scoped, DateTime today)
    {
        // Materialize the canonical B3 finance obligation population ONCE; both finance categories
        // (Em processamento financeiro + Pago) derive from this single result — no duplicate sweep.
        var financeObligations = (await new FinanceObligationSummaryProjection(_eligibility)
            .BuildAsync(scoped, null, null, null, today)).Obligations;

        return new List<FinancialCategoryDto>
        {
            await BuildApprovalAsync(scoped),
            await BuildWaitingPoAsync(scoped),
            BuildFinanceProcessing(financeObligations),
            await BuildPaidAsync(financeObligations),
        };
    }

    private static FinancialCategoryDto Category(string code, string label, string entityType, Agg.Result agg) => new()
    {
        Code = code, Label = label, EntityType = entityType,
        EntityCount = agg.EntityCount, RequestCount = agg.RequestCount,
        Currencies = agg.Currencies, IsAuthoritative = agg.IsAuthoritative,
    };

    // ── EM_APROVACAO: ApprovalBatch grain, authoritative winner snapshot (no estimate fallback). ──
    private async Task<FinancialCategoryDto> BuildApprovalAsync(IQueryable<Request> scoped)
    {
        var rows = await scoped
            .SelectMany(
                r => r.ApprovalBatches.Where(b =>
                    b.Status == RequestConstants.ApprovalBatchStatuses.WaitingAreaApproval
                    || b.Status == RequestConstants.ApprovalBatchStatuses.WaitingFinalApproval),
                (r, b) => new
                {
                    RequestId = r.Id,
                    BatchId = b.Id,
                    Snapshot = b.ApprovedTotalAmount,
                    HasItems = b.Items.Any(),
                    AnyUndecided = b.Items.Any(i => i.SelectedCandidateId == null && i.SelectedQuotationItemId == null),
                    ItemSum = b.Items.Sum(i => i.SelectedCandidate != null
                        ? i.SelectedCandidate.LineTotal
                        : (i.SelectedQuotationItem != null ? i.SelectedQuotationItem.LineTotal : 0m)),
                    Currency = b.Items
                        .Where(i => i.SelectedCandidate != null || i.SelectedQuotationItem != null)
                        .Select(i => i.SelectedCandidate != null ? i.SelectedCandidate.Currency : i.SelectedQuotationItem!.Quotation.Currency)
                        .FirstOrDefault(),
                })
            .ToListAsync();

        var contributions = rows.Select(x =>
        {
            // Authoritative snapshot only: batch approved total, else the DECIDED candidate line sum.
            // An undecided batch has no authoritative value — counted, never fabricated (PD-B7-05/07).
            decimal? amount = x.Snapshot ?? (x.HasItems && !x.AnyUndecided ? x.ItemSum : (decimal?)null);
            return new Agg.Contribution(x.BatchId, x.RequestId, x.Currency, amount);
        });

        return Category(FinancialCategories.EmAprovacao, "Em aprovação", FinancialEntityTypes.ApprovalBatch, Agg.Aggregate(contributions));
    }

    // ── AGUARDANDO_PO: RequestPoGroup grain (group TotalAmount, canonical currency). ──
    private async Task<FinancialCategoryDto> BuildWaitingPoAsync(IQueryable<Request> scoped)
    {
        var rows = await scoped
            .SelectMany(
                r => r.PoGroups.Where(g =>
                    g.Status == RequestConstants.PoGroupStatuses.WaitingPo
                    || g.Status == RequestConstants.PoGroupStatuses.WaitingPoCorrection),
                (r, g) => new
                {
                    RequestId = r.Id,
                    GroupId = g.Id,
                    g.TotalAmount,
                    GroupCurrency = g.CurrencyCode,
                    HasSelectedQuotation = r.SelectedQuotationId != null,
                    SelectedQuotationCurrency = r.Quotations.Where(q => q.Id == r.SelectedQuotationId).Select(q => q.Currency).FirstOrDefault(),
                    RequestCurrency = r.Currency != null ? r.Currency.Code : null,
                })
            .ToListAsync();

        var contributions = rows.Select(x => new Agg.Contribution(
            x.GroupId, x.RequestId,
            FinanceGroupDisplayResolver.ResolveCurrencyCode(x.GroupCurrency, x.HasSelectedQuotation, x.SelectedQuotationCurrency, x.RequestCurrency),
            x.TotalAmount));

        return Category(FinancialCategories.AguardandoPo, "Aguardando P.O.", FinancialEntityTypes.PoGroup, Agg.Aggregate(contributions));
    }

    // ── EM_PROCESSAMENTO_FINANCEIRO: pure derivation from the already-materialized finance obligations
    //    (PD-B7-04: NEEDS_SCHEDULING + NEEDS_PAYMENT). B3's IsFinanceActionable also includes
    //    FISCAL_DOCUMENT_PENDING — deliberately NOT here; see the B7.1 report. No DB access. ──
    private static FinancialCategoryDto BuildFinanceProcessing(IReadOnlyList<FinanceObligationDto> obligations)
    {
        var rows = obligations.Where(o =>
            o.ActionClass == FinanceActionClasses.NeedsScheduling
            || o.ActionClass == FinanceActionClasses.NeedsPayment);
        var contributions = rows.Select(o => new Agg.Contribution(o.RequestPoGroupId, o.RequestId, o.CurrencyCode, o.GroupAmount));
        return Category(FinancialCategories.EmProcessamentoFinanceiro, "Em processamento financeiro", FinancialEntityTypes.PoGroup, Agg.Aggregate(contributions));
    }

    // ── PAGO_AGUARDANDO_RECEBIMENTO: population = the SAME finance obligations with ActionClass
    //    PAID_WAITING_RECEIVING; amount = ACTUAL paid evidence from RequestPayment, partitioned by PAYMENT
    //    currency (not the group currency). The payment-evidence query still determines the money. ──
    private async Task<FinancialCategoryDto> BuildPaidAsync(IReadOnlyList<FinanceObligationDto> obligations)
    {
        var paid = obligations.Where(o => o.ActionClass == FinanceActionClasses.PaidWaitingReceiving).ToList();
        var groupToRequest = paid
            .GroupBy(o => o.RequestPoGroupId)
            .ToDictionary(g => g.Key, g => g.First().RequestId);
        var paidGroupIds = groupToRequest.Keys.ToList();

        var payRows = await _context.RequestPayments
            .Where(p => p.RequestPoGroupId != null && paidGroupIds.Contains(p.RequestPoGroupId!.Value)
                        && p.PaymentStatus == RequestPayment.PaymentStatuses.Completed
                        && OwedMoneyPaymentTypes.Contains(p.PaymentType))
            .Select(p => new { GroupId = p.RequestPoGroupId!.Value, p.CurrencyCode, p.ActualPaidAmount })
            .ToListAsync();

        var contributionsPaid = new List<Agg.Contribution>();

        // One contribution per (group, payment currency): multiple completed rows in a currency sum (PD-B7-12);
        // a group with payments in two currencies stays split by payment currency (PD-B7-08/PD-B7-11).
        foreach (var g in payRows.GroupBy(p => new { p.GroupId, Currency = Agg.NormalizeCurrency(p.CurrencyCode) }))
        {
            contributionsPaid.Add(new Agg.Contribution(
                g.Key.GroupId, groupToRequest[g.Key.GroupId], g.Key.Currency, g.Sum(p => p.ActualPaidAmount ?? 0m)));
        }

        // A paid-handoff group with no completed owed-money payment is counted but unvalued (IsAuthoritative=false).
        var valuedGroups = payRows.Select(p => p.GroupId).Distinct().ToHashSet();
        foreach (var gid in paidGroupIds.Where(id => !valuedGroups.Contains(id)))
        {
            contributionsPaid.Add(new Agg.Contribution(gid, groupToRequest[gid], null, null));
        }

        return Category(FinancialCategories.PagoAguardandoRecebimento, "Pago / aguardando recebimento", FinancialEntityTypes.PoGroup, Agg.Aggregate(contributionsPaid));
    }

    // ── B7.3 PAID HISTORY: confirmed payment evidence in a period, by payment currency. A DIRECT bounded
    //    RequestPayments query (NOT the finance obligation projection — no duplicate finance sweep). Payments
    //    are scope-bound by their owning request being inside the caller's RequestAccessScope. ──
    public async Task<PaidHistoryDto> BuildPaidHistoryAsync(IQueryable<Request> scoped, DateTime today, string? periodCode)
    {
        // B7.3 supports LAST_30_DAYS only (default); today + previous 29 days, half-open interval.
        var fromUtc = today.AddDays(-29);
        var toUtc = today.AddDays(1);

        var scopedRequestIds = scoped.Select(r => r.Id);
        var rows = await _context.RequestPayments
            .Where(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Completed
                        && OwedMoneyPaymentTypes.Contains(p.PaymentType) // ADVANCE/FINAL_BALANCE/REGULARIZATION; REFUND excluded
                        && p.PaidDateUtc != null && p.PaidDateUtc >= fromUtc && p.PaidDateUtc < toUtc
                        && scopedRequestIds.Contains(p.RequestId))
            .Select(p => new { p.RequestId, p.CurrencyCode, p.ActualPaidAmount })
            .ToListAsync();

        var currencies = rows
            .GroupBy(x => Agg.NormalizeCurrency(x.CurrencyCode))
            .OrderBy(g => g.Key == FinancialCurrency.Unknown ? 1 : 0)
            .ThenBy(g => g.Key)
            .Select(g => new CurrencyAmountDto
            {
                CurrencyCode = g.Key,
                Amount = g.Where(x => x.ActualPaidAmount.HasValue).Sum(x => x.ActualPaidAmount!.Value), // per-currency only
                EntityCount = g.Count(),                                     // payments in this currency
                RequestCount = g.Select(x => x.RequestId).Distinct().Count(),
            })
            .ToList();

        return new PaidHistoryDto
        {
            PeriodCode = FinancialPeriods.Last30Days,
            PeriodLabel = "Últimos 30 dias",
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Currencies = currencies,
            PaymentCount = rows.Count,
            RequestCount = rows.Select(x => x.RequestId).Distinct().Count(),
            IsAuthoritative = rows.All(x => x.ActualPaidAmount.HasValue),
        };
    }
}
