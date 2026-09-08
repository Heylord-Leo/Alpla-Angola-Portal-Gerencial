using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Admin;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Infrastructure.Services.Repairs;

/// <summary>
/// v2.241.0 — controlled, tightly-scoped repair for the confirmed LEGACY MONETARY-SCALE defect
/// (incident REQ-11/08/2026-228). A pre-fix monetary input produced a x1000-scaled declared total
/// that was reconciled against a correct line subtotal by persisting a NEGATIVE
/// <see cref="RequestLineItem.DiscountAmount"/>; the backend line formula
/// (<c>TotalAmount = (Quantity*UnitPrice) - DiscountAmount</c>) then inflated the line total ×1000,
/// and every downstream snapshot (request estimated/approved, PO-group total, scheduled payment)
/// inherited it.
///
/// <para>This is NOT a generic financial editor. The caller supplies only the wrong total it expects
/// to find and the correct total it intends; the service DERIVES every dependent value through the
/// canonical formulas and refuses unless the persisted state still matches the exact defect
/// fingerprint. It never changes any status, never touches the P.O. identity or attachments, never
/// rewrites existing history — it appends corrective audit rows — and is idempotent.</para>
///
/// <para>Atomicity: all four entity corrections and the audit rows are committed in a SINGLE
/// <c>SaveChangesAsync</c>, so a failure at any point (including the audit insert) rolls the whole
/// set back. Optimistic concurrency is enforced by SQL Server rowversion on Request and PoGroup;
/// Line and Payment (no rowversion) are guarded by re-verifying their exact pre-repair values.</para>
/// </summary>
public sealed class LegacyMonetaryScaleRepairService
{
    private readonly ApplicationDbContext _context;
    public LegacyMonetaryScaleRepairService(ApplicationDbContext context) => _context = context;

    private const string Aoa = "AOA";
    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static string Inv(decimal v) => v.ToString("F2", CultureInfo.InvariantCulture);

    // ── Public API ────────────────────────────────────────────────────────────────────────────

    /// <summary>Dry run. Loads current state, classifies it, computes the corrected values and the
    /// safety verdict. Writes NOTHING. Expected totals are optional; when omitted they are derived
    /// from the data so the operator can inspect a request before committing to the intent.</summary>
    public async Task<LegacyMonetaryScaleRepairPreview?> PreviewAsync(
        Guid requestId, decimal? expectedCurrentTotal, decimal? expectedCorrectTotal, CancellationToken ct = default)
    {
        var a = await LoadAsync(requestId, expectedCurrentTotal, expectedCorrectTotal, ct);
        if (a == null) return null;

        return new LegacyMonetaryScaleRepairPreview
        {
            RequestNumber = a.Request.RequestNumber ?? string.Empty,
            RequestStatus = a.Request.Status?.Code ?? string.Empty,
            Currency = a.CurrencyCode,

            LineQuantity = a.Line?.Quantity ?? 0,
            LineUnitPrice = a.Line?.UnitPrice ?? 0,
            LineCurrentDiscount = a.Line?.DiscountAmount ?? 0,
            LineCurrentTotal = a.Line?.TotalAmount ?? 0,
            LineCorrectedDiscount = 0,
            LineCorrectedTotal = a.CorrectedTotal,

            RequestCurrentEstimated = a.Request.EstimatedTotalAmount,
            RequestCorrectedEstimated = a.CorrectedTotal,
            RequestCurrentApproved = a.Request.ApprovedTotalAmount,
            RequestCorrectedApproved = a.Request.ApprovedTotalAmount.HasValue ? a.CorrectedTotal : (decimal?)null,

            PoCurrentTotal = a.PoGroup?.TotalAmount ?? 0,
            PoCorrectedTotal = a.CorrectedTotal,
            PurchaseOrderNumber = a.PoGroup?.PurchaseOrderNumber,

            PaymentCurrentPlannedAmount = a.Payment?.PlannedAmount ?? 0,
            PaymentCorrectedPlannedAmount = a.CorrectedTotal,
            PaymentStatus = a.Payment?.PaymentStatus ?? string.Empty,
            ActualPaidAmount = a.Payment?.ActualPaidAmount,

            SafetyChecks = a.Checks,
            AlreadyCorrect = a.AlreadyCorrect,
            WillWrite = !a.AlreadyCorrect && a.Checks.All(c => c.Passed),
            AffectedRows = a.AlreadyCorrect ? 0 : 4,

            RequestRowVersion = ToB64(a.Request.RowVersion),
            PoGroupRowVersion = ToB64(a.PoGroup?.RowVersion),
            LineFingerprint = a.LineFingerprint,
            PaymentFingerprint = a.PaymentFingerprint,
        };
    }

    /// <summary>Applies the correction atomically. Refuses unless every safety gate passes and the
    /// persisted state still matches the exact pre-repair fingerprint; returns an idempotent no-op
    /// when the request is already correct; returns CONFLICT if a concurrent writer changed a guarded
    /// row. All writes (4 corrections + audit) commit in one SaveChanges.</summary>
    public async Task<LegacyMonetaryScaleRepairResult> ApplyAsync(
        Guid requestId, LegacyMonetaryScaleRepairRequest body, Guid actorId, string operatorLabel,
        CancellationToken ct = default)
    {
        var result = new LegacyMonetaryScaleRepairResult
        {
            RepairId = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            Operator = operatorLabel,
        };

        if (string.IsNullOrWhiteSpace(body?.Reason))
        {
            result.Status = LegacyMonetaryScaleRepairResult.Statuses.Refused;
            result.Message = "A justificativa (reason) é obrigatória.";
            return result;
        }

        // Apply MUST carry the four concurrency tokens returned by a preview — direct apply cannot
        // bypass preview. Missing a token is REFUSED (contract error), distinct from a token that is
        // present but stale, which is CONFLICT. Tokens must be PRESENT, not regenerated here; a
        // rowversion token is legitimately empty only where the provider has no rowversion (tests).
        if (body.ExpectedRequestRowVersion is null || body.ExpectedPoGroupRowVersion is null
            || body.ExpectedLineFingerprint is null || body.ExpectedPaymentFingerprint is null)
        {
            result.Status = LegacyMonetaryScaleRepairResult.Statuses.Refused;
            result.Message = "É necessário executar uma pré-visualização válida antes de aplicar a correção.";
            return result;
        }

        // Explicit transaction: BEGIN → re-read → verify concurrency/invariants → update the four
        // entities → append audit → SaveChanges → COMMIT. Default isolation (ReadCommitted) suffices —
        // the rowversion (Request/PoGroup) and value fingerprints (Line/Payment) protect the
        // read/verify/write sequence without extra locking. Any early return disposes the transaction,
        // rolling back with nothing written.
        await using var tx = await _context.Database.BeginTransactionAsync(ct);

        var a = await LoadAsync(requestId, body.ExpectedCurrentTotal, body.ExpectedCorrectTotal, ct);
        if (a == null)
        {
            result.Status = LegacyMonetaryScaleRepairResult.Statuses.Refused;
            result.Message = "Pedido não encontrado.";
            return result;
        }

        result.RequestNumber = a.Request.RequestNumber ?? string.Empty;
        result.SafetyChecks = a.Checks;

        // Cross-call concurrency guard: if the caller passed preview tokens and any guarded row moved
        // since the preview, refuse with CONFLICT before touching anything.
        static bool Moved(string? expected, string actual) => !string.IsNullOrEmpty(expected) && expected != actual;
        if (Moved(body.ExpectedRequestRowVersion, ToB64(a.Request.RowVersion))
            || Moved(body.ExpectedPoGroupRowVersion, ToB64(a.PoGroup?.RowVersion))
            || Moved(body.ExpectedLineFingerprint, a.LineFingerprint)
            || Moved(body.ExpectedPaymentFingerprint, a.PaymentFingerprint))
        {
            result.Status = LegacyMonetaryScaleRepairResult.Statuses.Conflict;
            result.Message = "O pedido foi alterado após a pré-visualização. Nenhuma alteração foi feita. Repita a pré-visualização.";
            return result;
        }

        if (a.AlreadyCorrect)
        {
            result.Status = LegacyMonetaryScaleRepairResult.Statuses.AlreadyCorrect;
            result.RowsChanged = 0;
            result.Message = "Nenhuma alteração necessária: os valores já estão corretos.";
            return result;
        }

        if (a.Checks.Any(c => !c.Passed))
        {
            result.Status = LegacyMonetaryScaleRepairResult.Statuses.Refused;
            result.Message = "Uma ou mais verificações de segurança falharam. Nenhuma alteração foi feita.";
            return result;
        }

        // ── Mutate (in-memory) ──────────────────────────────────────────────────────────────
        var now = DateTime.UtcNow;
        var line = a.Line!;
        var po = a.PoGroup!;
        var pay = a.Payment!;
        var req = a.Request;

        var oldLineDiscount = line.DiscountAmount;
        var oldLineTotal = line.TotalAmount;
        var oldEstimated = req.EstimatedTotalAmount;
        var oldApproved = req.ApprovedTotalAmount;
        var oldPo = po.TotalAmount;
        var oldPay = pay.PlannedAmount;

        line.DiscountAmount = 0m;
        line.TotalAmount = a.CorrectedTotal;   // canonical LineItemFactory formula, discount 0, no IVA
        line.UpdatedAtUtc = now; line.UpdatedByUserId = actorId;

        req.EstimatedTotalAmount = a.CorrectedTotal;                       // canonical aggregate recompute
        if (req.ApprovedTotalAmount.HasValue) req.ApprovedTotalAmount = a.CorrectedTotal; // approval snapshot
        req.UpdatedAtUtc = now; req.UpdatedByUserId = actorId;

        po.TotalAmount = a.CorrectedTotal;
        po.UpdatedAtUtc = now; po.UpdatedByUserId = actorId;

        pay.PlannedAmount = a.CorrectedTotal;
        pay.UpdatedAtUtc = now; pay.UpdatedByUserId = actorId;

        // ── Corrective audit (append-only; existing history untouched) ──────────────────────
        var statusCode = req.Status?.Code ?? RequestConstants.Statuses.PaymentScheduled;
        var statusHistory = new RequestStatusHistory
        {
            Id = Guid.NewGuid(),
            RequestId = req.Id,
            ActorUserId = actorId,
            ActionTaken = "FINANCIAL_CORRECTION",
            PreviousStatusId = req.StatusId,
            NewStatusId = req.StatusId,          // NO transition
            Comment = BuildComment(result.RepairId, oldLineTotal, a.CorrectedTotal, line, body.Reason),
            CreatedAtUtc = now,
        };
        _context.RequestStatusHistories.Add(statusHistory);
        result.AuditEntryIds.Add(statusHistory.Id);

        void Field(string entity, string field, string display, string? oldV, string? newV, Guid? lineId)
        {
            var h = new RequestFieldChangeHistory
            {
                Id = Guid.NewGuid(),
                RequestId = req.Id,
                ActorUserId = actorId,
                FieldName = field,
                FieldDisplayName = display,
                PreviousValue = oldV,
                NewValue = newV,
                StatusCodeAtChange = statusCode,
                LineItemId = lineId,
                CreatedAtUtc = now,
            };
            _context.Set<RequestFieldChangeHistory>().Add(h);
            result.AuditEntryIds.Add(h.Id);
            result.Changes.Add(new RepairChange { Entity = entity, Field = field, OldValue = oldV, NewValue = newV });
        }

        Field("RequestLineItem", "DiscountAmount", "Desconto (linha)", Inv(oldLineDiscount ?? 0), Inv(0), line.Id);
        Field("RequestLineItem", "TotalAmount", "Total da linha", Inv(oldLineTotal), Inv(a.CorrectedTotal), line.Id);
        Field("Request", "EstimatedTotalAmount", "Valor Total Estimado", Inv(oldEstimated), Inv(a.CorrectedTotal), null);
        if (oldApproved.HasValue)
            Field("Request", "ApprovedTotalAmount", "Valor Aprovado", Inv(oldApproved.Value), Inv(a.CorrectedTotal), null);
        Field("RequestPoGroup", "TotalAmount", "Total do Grupo P.O.", Inv(oldPo), Inv(a.CorrectedTotal), null);
        Field("RequestPayment", "PlannedAmount", "Valor Planeado do Pagamento", Inv(oldPay), Inv(a.CorrectedTotal), null);

        // ── Commit atomically. The four corrections and all audit rows persist in ONE SaveChanges
        //    inside the transaction; rowversion on Request/PoGroup enforces optimistic concurrency,
        //    and any failure (including the audit insert) rolls the whole transaction back. ──
        try
        {
            var affected = await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            result.FinancialRowsChanged = 4;                          // line + request + PO group + payment
            result.AuditRowsInserted = result.AuditEntryIds.Count;    // 1 status-history + N field-change
            result.TotalDbRowsAffected = affected;
            result.RowsChanged = affected;                            // back-compat alias
            result.Status = LegacyMonetaryScaleRepairResult.Statuses.Applied;
            result.Message = $"Correção aplicada: {Inv(oldLineTotal)} → {Inv(a.CorrectedTotal)} AOA.";
        }
        catch (DbUpdateConcurrencyException)
        {
            result.Status = LegacyMonetaryScaleRepairResult.Statuses.Conflict;
            result.RowsChanged = 0;
            result.Changes.Clear();
            result.AuditEntryIds.Clear();
            result.Message = "O pedido foi alterado por outra operação após a pré-visualização. Nenhuma alteração foi feita. Repita a pré-visualização.";
        }

        return result;
    }

    // ── Analysis (shared by preview and apply) ─────────────────────────────────────────────────

    private sealed class Analysis
    {
        public Request Request = null!;
        public RequestLineItem? Line;
        public RequestPoGroup? PoGroup;
        public RequestPayment? Payment;
        public decimal CorrectedTotal;
        public string CurrencyCode = string.Empty;
        public bool AlreadyCorrect;
        public string LineFingerprint = string.Empty;
        public string PaymentFingerprint = string.Empty;
        public List<RepairSafetyCheck> Checks = new();
    }

    private async Task<Analysis?> LoadAsync(
        Guid requestId, decimal? expectedCurrentTotal, decimal? expectedCorrectTotal, CancellationToken ct)
    {
        var request = await _context.Requests
            .Include(r => r.Status)
            .Include(r => r.Currency)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request == null) return null;

        var lines = await _context.RequestLineItems
            .Where(l => l.RequestId == requestId && !l.IsDeleted)
            .ToListAsync(ct);
        var groups = await _context.RequestPoGroups
            .Where(g => g.RequestId == requestId)
            .ToListAsync(ct);
        var payments = await _context.RequestPayments
            .Where(p => p.RequestId == requestId)
            .ToListAsync(ct);

        var line = lines.Count == 1 ? lines[0] : null;
        var po = groups.Count == 1 ? groups[0] : null;
        var pay = payments.Count == 1 ? payments[0] : null;

        var a = new Analysis
        {
            Request = request,
            Line = line,
            PoGroup = po,
            Payment = pay,
            CurrencyCode = request.Currency?.Code ?? request.ApprovedCurrencyCode ?? string.Empty,
        };

        // Corrected line total via the canonical formula: net = round2(qty*price - discount0), no IVA.
        var subtotal = line != null ? Round2(line.Quantity * line.UnitPrice) : 0m;
        a.CorrectedTotal = subtotal;

        // Expected values: caller-supplied, else derived from the data (for a body-less preview).
        var expCorrect = expectedCorrectTotal ?? subtotal;
        var expCurrent = expectedCurrentTotal ?? (line?.TotalAmount ?? 0m);

        a.LineFingerprint = line == null ? string.Empty
            : $"{Inv(line.DiscountAmount ?? 0)}|{Inv(line.TotalAmount)}|{line.UpdatedAtUtc?.Ticks ?? 0}";
        a.PaymentFingerprint = pay == null ? string.Empty
            : $"{Inv(pay.PlannedAmount)}|{pay.UpdatedAtUtc?.Ticks ?? 0}";

        // Already correct? (idempotent no-op condition — evaluated before the defect gates.)
        a.AlreadyCorrect =
            line != null && po != null && pay != null &&
            (line.DiscountAmount ?? 0) == 0m &&
            line.TotalAmount == a.CorrectedTotal &&
            request.EstimatedTotalAmount == a.CorrectedTotal &&
            (!request.ApprovedTotalAmount.HasValue || request.ApprovedTotalAmount.Value == a.CorrectedTotal) &&
            po.TotalAmount == a.CorrectedTotal &&
            pay.PlannedAmount == a.CorrectedTotal;

        a.Checks = BuildChecks(request, line, po, pay, lines.Count, groups.Count, payments.Count,
            a.CorrectedTotal, expCorrect, expCurrent, a.CurrencyCode);

        return a;
    }

    private static List<RepairSafetyCheck> BuildChecks(
        Request request, RequestLineItem? line, RequestPoGroup? po, RequestPayment? pay,
        int lineCount, int groupCount, int paymentCount,
        decimal correctedTotal, decimal expectedCorrect, decimal expectedCurrent, string currencyCode)
    {
        var checks = new List<RepairSafetyCheck>();
        void Check(string name, bool ok, string? detail = null) =>
            checks.Add(new RepairSafetyCheck { Name = name, Passed = ok, Detail = detail });

        Check("SingleActiveLine", lineCount == 1, $"active lines = {lineCount}");
        Check("SinglePoGroup", groupCount == 1, $"PO groups = {groupCount}");
        Check("SinglePayment", paymentCount == 1, $"payments = {paymentCount}");

        // Defect fingerprint: a NEGATIVE discount whose removal explains the inflated total exactly,
        // i.e. current TotalAmount == (qty*price) - discount, and the current total is actually wrong.
        var fingerprint = line != null
            && (line.DiscountAmount ?? 0) < 0
            && Round2((line.Quantity * line.UnitPrice) - (line.DiscountAmount ?? 0)) == line.TotalAmount
            && line.TotalAmount != correctedTotal;
        Check("LegacyScaleDefectFingerprint", fingerprint,
            line == null ? "no single line" : $"discount={Inv(line.DiscountAmount ?? 0)}, total={Inv(line.TotalAmount)}");

        Check("NoLineIva", line != null && line.IvaRateId == null);
        Check("NoLinePercentDiscount", line != null && line.DiscountPercent == null);
        Check("NoGlobalDiscount", request.DiscountAmount == 0m, $"request.DiscountAmount={Inv(request.DiscountAmount)}");

        Check("SubtotalMatchesExpectedCorrect", line != null && correctedTotal == expectedCorrect,
            $"qty*price={Inv(correctedTotal)}, expectedCorrect={Inv(expectedCorrect)}");
        Check("LineTotalMatchesExpectedCurrent", line != null && line.TotalAmount == expectedCurrent);
        Check("RequestEstimatedMatchesExpectedCurrent", request.EstimatedTotalAmount == expectedCurrent);
        Check("RequestApprovedMatchesExpectedCurrent",
            !request.ApprovedTotalAmount.HasValue || request.ApprovedTotalAmount.Value == expectedCurrent);
        Check("PoGroupTotalMatchesExpectedCurrent", po != null && po.TotalAmount == expectedCurrent);
        Check("PaymentAmountMatchesExpectedCurrent", pay != null && pay.PlannedAmount == expectedCurrent);

        Check("RequestStatusPaymentScheduled",
            string.Equals(request.Status?.Code, RequestConstants.Statuses.PaymentScheduled, StringComparison.Ordinal),
            request.Status?.Code);
        Check("PaymentScheduledAndUnpaid",
            pay != null && pay.PaymentStatus == RequestPayment.PaymentStatuses.Scheduled
            && (pay.ActualPaidAmount == null || pay.ActualPaidAmount == 0m),
            pay == null ? "no single payment" : $"{pay.PaymentStatus}, paid={pay.ActualPaidAmount?.ToString(CultureInfo.InvariantCulture) ?? "null"}");

        var aoaEverywhere =
            string.Equals(currencyCode, Aoa, StringComparison.Ordinal)
            && (po == null || string.Equals(po.CurrencyCode, Aoa, StringComparison.Ordinal))
            && (pay == null || string.Equals(pay.CurrencyCode, Aoa, StringComparison.Ordinal))
            && (request.ApprovedCurrencyCode == null || string.Equals(request.ApprovedCurrencyCode, Aoa, StringComparison.Ordinal));
        Check("CurrencyAoaEverywhere", aoaEverywhere, currencyCode);

        return checks;
    }

    private static string BuildComment(Guid repairId, decimal oldTotal, decimal newTotal, RequestLineItem line, string reason)
        => "Correção financeira controlada aplicada ao pedido legado (defeito legado de escala monetária). "
         + $"Corrigido: AOA {oldTotal:N2} → AOA {newTotal:N2}. "
         + $"Quantidade: {line.Quantity:0.####} · Preço unitário: AOA {line.UnitPrice:N2}. "
         + "Registros históricos originais preservados. "
         + $"Justificativa: {reason.Trim()}. RepairId: {repairId}.";

    private static string ToB64(byte[]? rowVersion)
        => rowVersion == null || rowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(rowVersion);
}
