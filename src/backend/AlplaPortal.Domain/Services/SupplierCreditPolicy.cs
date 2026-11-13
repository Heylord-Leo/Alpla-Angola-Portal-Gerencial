using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;

namespace AlplaPortal.Domain.Services;

/// <summary>
/// Pure, side-effect-free rules of supplier account credit applied to P.O. groups. The single rulebook shared by
/// the ledger service (balances), RegisterPo (eligibility, normalisation, split, owed rows), the Finance
/// settlement endpoints (strict bank amount) and the Finance queue projection — so none of them can drift.
/// </summary>
public static class SupplierCreditPolicy
{
    /// <summary>Hold of one reservation: Amount − AppliedAmount while RESERVED / PARTIALLY_APPLIED; 0 otherwise.</summary>
    public static decimal Hold(string status, decimal amount, decimal appliedAmount)
        => SupplierCreditConstants.ReservationStatuses.Holding.Contains(status)
            ? Math.Max(0m, amount - appliedAmount)
            : 0m;

    /// <summary>Balance = Σ SignedAmount of the ledger.</summary>
    public static decimal Balance(IEnumerable<decimal> signedAmounts) => signedAmounts.Sum();

    /// <summary>Available = Balance − Σ Hold.</summary>
    public static decimal Available(decimal balance, decimal reservedTotal) => balance - reservedTotal;

    public sealed record AccountFigures(decimal Balance, decimal Reserved, decimal Available);

    public static AccountFigures Figures(IEnumerable<decimal> signedAmounts, IEnumerable<(string Status, decimal Amount, decimal Applied)> reservations)
    {
        var balance = Balance(signedAmounts);
        var reserved = reservations.Sum(r => Hold(r.Status, r.Amount, r.Applied));
        return new AccountFigures(balance, reserved, Available(balance, reserved));
    }

    // ── Eligibility ──

    /// <summary>Stage 1: QUOTATION requests only, Pós-Pago and 100 % Antecipado only.</summary>
    public static bool IsRequestTypeSupported(string? requestTypeCode)
        => requestTypeCode == RequestConstants.Types.Quotation;

    public static bool IsPaymentConditionSupported(string? paymentConditionCode)
        => paymentConditionCode != null && SupplierCreditConstants.Stage1PaymentConditions.Contains(paymentConditionCode);

    // ── Amounts ──

    /// <summary>
    /// The coverage actually reserved for a request of <paramref name="requested"/> on a group of <paramref name="total"/>
    /// with <paramref name="available"/> on the account (plus the group's own current hold during a correction).
    /// Rules: 0 &lt; requested ≤ total; requested within the near-full tolerance of total normalises to FULL coverage
    /// ONLY when available ≥ total (otherwise the exact requested amount); always ≤ available.
    /// </summary>
    public static CoverageDecision DecideCoverage(decimal requested, decimal total, decimal available)
    {
        requested = Math.Round(requested, 2, MidpointRounding.AwayFromZero);
        total = Math.Round(total, 2, MidpointRounding.AwayFromZero);
        if (total <= 0m) return CoverageDecision.Invalid(SupplierCreditConstants.ErrorCodes.InvalidAmount, "O grupo P.O não tem valor a cobrir.");
        if (requested <= 0m) return CoverageDecision.Invalid(SupplierCreditConstants.ErrorCodes.InvalidAmount, "O valor de crédito a utilizar deve ser superior a zero.");
        if (requested > total) return CoverageDecision.Invalid(SupplierCreditConstants.ErrorCodes.InvalidAmount, $"O valor de crédito ({requested:N2}) não pode exceder o valor da P.O ({total:N2}).");

        var tolerance = RequestConstants.FinancialIntegrity.CalculateTolerance(total);
        var nearFull = requested >= total - tolerance;
        var coverage = nearFull && available >= total ? total : requested;
        if (coverage > available)
            return CoverageDecision.Invalid(SupplierCreditConstants.ErrorCodes.Insufficient,
                $"Saldo de crédito insuficiente: disponível {available:N2}, solicitado {coverage:N2}.");
        return CoverageDecision.Ok(coverage, coverage == total);
    }

    public sealed record CoverageDecision(bool IsValid, decimal Coverage, bool IsFullCoverage, string? Code, string? Message)
    {
        public static CoverageDecision Ok(decimal coverage, bool full) => new(true, coverage, full, null, null);
        public static CoverageDecision Invalid(string code, string message) => new(false, 0m, false, code, message);
    }

    /// <summary>
    /// Stage-based split (rev. 4 §4.2): Adv = round2(T × pct/100), Bal = T − Adv; CredAdv = min(C, Adv), CredBal = C − CredAdv;
    /// BankAdv = Adv − CredAdv, BankBal = Bal − CredBal. Pós-Pago: pct 0 (everything is the balance); 100 % Antecipado: pct 100.
    /// </summary>
    public static CoverageSplit Split(decimal total, decimal advancePercent, decimal coverage)
    {
        var adv = Math.Round(total * (advancePercent / 100m), 2, MidpointRounding.AwayFromZero);
        adv = Math.Min(adv, total);
        var bal = total - adv;
        var credAdv = Math.Min(coverage, adv);
        var credBal = Math.Min(coverage - credAdv, bal);
        return new CoverageSplit(adv, bal, credAdv, credBal, adv - credAdv, bal - credBal);
    }

    public sealed record CoverageSplit(decimal Advance, decimal Balance, decimal CreditAdvance, decimal CreditBalance, decimal BankAdvance, decimal BankBalance);

    /// <summary>
    /// The owed rows a registration materialises for a credit-touched group (rev. 5 §4.2). Single-stage terms produce
    /// stage SINGLE rows: Pós-Pago → FINAL_BALANCE (BankBal) + SUPPLIER_CREDIT (CredBal); 100 % Antecipado → ADVANCE
    /// (BankAdv) + SUPPLIER_CREDIT (CredAdv). Zero-amount rows are never created.
    /// </summary>
    public static IReadOnlyList<OwedRowPlan> PlanOwedRows(string paymentConditionCode, CoverageSplit split)
    {
        var rows = new List<OwedRowPlan>();
        if (paymentConditionCode == RequestConstants.PaymentConditions.PostPaid)
        {
            if (split.BankBalance > 0m) rows.Add(new OwedRowPlan(RequestPayment.PaymentTypes.FinalBalance, SupplierCreditConstants.Stages.Single, split.BankBalance));
            if (split.CreditBalance > 0m) rows.Add(new OwedRowPlan(RequestPayment.PaymentTypes.SupplierCredit, SupplierCreditConstants.Stages.Single, split.CreditBalance));
        }
        else if (paymentConditionCode == RequestConstants.PaymentConditions.AdvanceFull)
        {
            if (split.BankAdvance > 0m) rows.Add(new OwedRowPlan(RequestPayment.PaymentTypes.Advance, SupplierCreditConstants.Stages.Single, split.BankAdvance));
            if (split.CreditAdvance > 0m) rows.Add(new OwedRowPlan(RequestPayment.PaymentTypes.SupplierCredit, SupplierCreditConstants.Stages.Single, split.CreditAdvance));
        }
        else
        {
            // Antecipado Parcial (stage 2): two stages. Kept here so stage 2 only has to switch the eligibility on.
            if (split.BankAdvance > 0m) rows.Add(new OwedRowPlan(RequestPayment.PaymentTypes.Advance, SupplierCreditConstants.Stages.Advance, split.BankAdvance));
            if (split.CreditAdvance > 0m) rows.Add(new OwedRowPlan(RequestPayment.PaymentTypes.SupplierCredit, SupplierCreditConstants.Stages.Advance, split.CreditAdvance));
            if (split.BankBalance > 0m) rows.Add(new OwedRowPlan(RequestPayment.PaymentTypes.FinalBalance, SupplierCreditConstants.Stages.Balance, split.BankBalance));
            if (split.CreditBalance > 0m) rows.Add(new OwedRowPlan(RequestPayment.PaymentTypes.SupplierCredit, SupplierCreditConstants.Stages.Balance, split.CreditBalance));
        }
        return rows;
    }

    public sealed record OwedRowPlan(string PaymentType, string Stage, decimal PlannedAmount);

    /// <summary>
    /// Strict bank rule for a credit-touched group: the cash confirmed must equal the planned amount of the row being
    /// settled — no tolerance either way.
    /// </summary>
    public static bool CashMatchesRow(decimal actualPaid, decimal plannedAmount)
        => Math.Round(actualPaid, 2, MidpointRounding.AwayFromZero) == Math.Round(plannedAmount, 2, MidpointRounding.AwayFromZero);

    /// <summary>Reversal classification (rev. 4 §4.7).</summary>
    public static string ReversalKind(decimal coverage, decimal total, bool anyCompletedCashRow)
    {
        if (coverage >= total) return SupplierCreditConstants.ReversalKinds.FullCoverage;
        return anyCompletedCashRow ? SupplierCreditConstants.ReversalKinds.PartialWithCash : SupplierCreditConstants.ReversalKinds.PartialNoCash;
    }

    /// <summary>
    /// The one reservation status a stage application requires: SINGLE and ADVANCE applications start from RESERVED;
    /// a BALANCE application starts from PARTIALLY_APPLIED when an advance was applied before, else from RESERVED.
    /// </summary>
    public static bool CanApplyStage(string reservationStatus, string stage, decimal appliedAmount)
    {
        if (stage == SupplierCreditConstants.Stages.Balance)
            return reservationStatus == SupplierCreditConstants.ReservationStatuses.PartiallyApplied
                || (reservationStatus == SupplierCreditConstants.ReservationStatuses.Reserved && appliedAmount == 0m);
        return reservationStatus == SupplierCreditConstants.ReservationStatuses.Reserved && appliedAmount == 0m;
    }

    /// <summary>Status after applying <paramref name="stageAmount"/> on top of <paramref name="appliedBefore"/>.</summary>
    public static string StatusAfterApplication(decimal amount, decimal appliedBefore, decimal stageAmount, decimal releasedHold)
    {
        var applied = appliedBefore + stageAmount;
        return applied + releasedHold >= amount ? SupplierCreditConstants.ReservationStatuses.Applied : SupplierCreditConstants.ReservationStatuses.PartiallyApplied;
    }

    /// <summary>Idempotency keys (persisted business identifiers only — never a date, never a per-attempt GUID).</summary>
    public static class Keys
    {
        public static string Application(Guid reservationId, string stage) => $"APPLY:{reservationId:D}:{stage}";
        public static string Reversal(Guid reservationId) => $"REV:{reservationId:D}";
        public static string HistoryReserved(Guid reservationId) => $"CRES:{reservationId:D}";
        public static string HistoryChanged(Guid reservationId) => $"CCHG:{reservationId:D}";
        public static string HistoryApplied(Guid reservationId, string stage) => $"CAPP:{stage}:{reservationId:D}";
        public static string HistoryReleased(Guid reservationId) => $"CREL:{reservationId:D}";
        public static string HistoryReversed(Guid reservationId) => $"CREV:{reservationId:D}";
    }
}
