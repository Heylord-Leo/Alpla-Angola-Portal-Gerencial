using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.SupplierCredit;
using AlplaPortal.Application.Interfaces.Suppliers;
using AlplaPortal.Domain.Configuration;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Infrastructure.Services.Finance;

/// <summary>
/// Workflow side of supplier credit (stage 1: QUOTATION, Pós-Pago + 100 % Antecipado, full and partial coverage).
/// Every public method runs INSIDE the caller's open transaction and never commits; the caller's SaveChanges persists
/// the credit facts with its own facts. Lock order: group row → account row → the group's reservations → caller's locks.
/// On a non-SQL-Server provider (InMemory tests) the lock hints are skipped; the rules are identical.
/// </summary>
public sealed class SupplierCreditSettlementService : ISupplierCreditSettlementService
{
    private readonly ApplicationDbContext _context;
    private readonly SupplierCreditOptions _options;
    private readonly ILogger<SupplierCreditSettlementService> _logger;

    public SupplierCreditSettlementService(ApplicationDbContext context, IOptions<SupplierCreditOptions> options, ILogger<SupplierCreditSettlementService> logger)
    {
        _context = context;
        _options = options?.Value ?? new SupplierCreditOptions();
        _logger = logger;
    }

    private static readonly string[] ReservableGroupStatuses =
    {
        RequestConstants.PoGroupStatuses.WaitingPo,
        RequestConstants.PoGroupStatuses.WaitingPoCorrection,
    };

    // ═════════════════════════════════════════ Eligibility ═════════════════════════════════════════

    public async Task<SupplierCreditEligibilityDto> EvaluateEligibilityAsync(
        Request request, RequestPoGroup group, string? paymentConditionCode, bool actorMayReserve, CancellationToken ct = default)
    {
        var currency = ResolveCurrency(request, group);
        var dto = new SupplierCreditEligibilityDto
        {
            Enabled = _options.NewReservationsEnabled,
            SupportedPaymentConditions = SC.Stage1PaymentConditions.ToList(),
            GroupTotalAmount = group.TotalAmount,
            CurrencyCode = currency,
            CompanyId = request.CompanyId,
        };

        var live = await GetLiveReservationAsync(group.Id, ct);
        SupplierCreditAccount? account = null;
        if (live != null)
            account = await _context.SupplierCreditAccounts.AsNoTracking().Include(a => a.Company).FirstOrDefaultAsync(a => a.Id == live.AccountId, ct);
        else if (group.SupplierId.HasValue && currency != null)
            account = await _context.SupplierCreditAccounts.AsNoTracking().Include(a => a.Company)
                .FirstOrDefaultAsync(a => a.SupplierId == group.SupplierId.Value && a.CompanyId == request.CompanyId && a.CurrencyCode == currency, ct);

        if (account != null)
        {
            var f = await FiguresAsync(account.Id, ct);
            dto.Account = new SupplierCreditAccountDto
            {
                Id = account.Id, SupplierId = account.SupplierId, CompanyId = account.CompanyId, CompanyName = account.Company?.Name ?? string.Empty,
                CompanyCode = account.Company?.Code, CurrencyCode = account.CurrencyCode, AccountNumber = account.AccountNumber, Status = account.Status,
                Balance = f.Balance, Reserved = f.Reserved, Available = f.Available, CreatedAtUtc = account.CreatedAtUtc,
                RowVersion = Convert.ToBase64String(account.RowVersion ?? Array.Empty<byte>()),
            };
            var ownHold = live == null ? 0m : SupplierCreditPolicy.Hold(live.Status, live.Amount, live.AppliedAmount);
            dto.AvailableForGroup = Math.Max(0m, f.Available + ownHold);
            dto.MaxCoverage = Math.Min(dto.AvailableForGroup, group.TotalAmount);
        }
        if (live != null) dto.CurrentReservation = Summarize(live, account);

        string? code = null, message = null;
        if (!actorMayReserve) { code = SC.ErrorCodes.NotResponsibleBuyer; message = "Apenas o comprador responsável pelo pedido pode utilizar crédito em conta corrente."; }
        else if (!SupplierCreditPolicy.IsRequestTypeSupported(request.RequestType?.Code)) { code = SC.ErrorCodes.UnsupportedRequestType; message = "Nesta fase o crédito em conta corrente aplica-se apenas a pedidos de cotação."; }
        else if (!ReservableGroupStatuses.Contains(group.Status)) { code = SC.ErrorCodes.ReservationConflict; message = $"O grupo P.O está em {group.Status}; o crédito só pode ser definido no registo ou na correção da P.O."; }
        else if (paymentConditionCode != null && !SupplierCreditPolicy.IsPaymentConditionSupported(paymentConditionCode)) { code = SC.ErrorCodes.UnsupportedTerms; message = "Nesta fase o crédito em conta corrente aplica-se apenas a Pós-Pago e 100 % Antecipado."; }
        else if (!group.SupplierId.HasValue) { code = SC.ErrorCodes.AccountNotFound; message = "O grupo não tem fornecedor definido."; }
        else if (account == null) { code = SC.ErrorCodes.AccountNotFound; message = "O fornecedor não tem conta corrente de crédito com esta empresa nesta moeda."; }
        else if (account.Status != SC.AccountStatuses.Active) { code = SC.ErrorCodes.AccountInactive; message = "A conta corrente de crédito do fornecedor está encerrada."; }
        else if (live != null && live.Status != SC.ReservationStatuses.Reserved) { code = SC.ErrorCodes.ReservationConflict; message = "O crédito deste grupo já foi aplicado; não pode ser alterado na correção."; }
        else if (!_options.NewReservationsEnabled && live == null) { code = SC.ErrorCodes.Disabled; message = "A utilização de crédito em conta corrente em novas P.O. não está ativa neste ambiente."; }
        else if (dto.MaxCoverage <= 0m && live == null) { code = SC.ErrorCodes.Insufficient; message = "A conta corrente não tem saldo disponível."; }

        dto.CanReserve = code == null;
        dto.ReasonCode = code; dto.ReasonMessage = message;
        return dto;
    }

    // ═════════════════════════════════════════ Reserve / correct ═════════════════════════════════════════

    public async Task<SupplierCreditOperationOutcome<ReservationOutcome>> ReserveOrCorrectAsync(ReserveSupplierCreditCommand cmd, CancellationToken ct = default)
    {
        var request = cmd.Request; var group = cmd.Group;
        var live = await LockLiveReservationAsync(group.Id, ct);
        var action = cmd.IsCorrection ? (cmd.CorrectionAction?.Trim().ToUpperInvariant() ?? SC.CorrectionActions.Keep) : null;

        // ── Nothing credit-related: bank-only registration, caller keeps its legacy path ──
        if (live == null && !cmd.UseSupplierCredit)
            return Ok(new ReservationOutcome { Reservation = null, Coverage = 0m });

        if (!SupplierCreditPolicy.IsRequestTypeSupported(request.RequestType?.Code))
            return Fail<ReservationOutcome>(400, SC.ErrorCodes.UnsupportedRequestType, "Crédito não suportado", "Nesta fase o crédito em conta corrente aplica-se apenas a pedidos de cotação.");
        if (cmd.IsCorrection && action != null && !SC.CorrectionActions.All.Contains(action))
            return Fail<ReservationOutcome>(400, SC.ErrorCodes.CorrectionActionInvalid, "Ação de correção inválida", "Indique KEEP, CHANGE ou REMOVE para a reserva de crédito existente.");

        // Group status re-read under lock (the caller validated it before opening the transaction).
        var lockedStatus = await LockGroupStatusAsync(group.Id, ct) ?? group.Status;
        if (!ReservableGroupStatuses.Contains(lockedStatus))
            return Fail<ReservationOutcome>(409, SC.ErrorCodes.ReservationConflict, "Grupo alterado entretanto", $"O grupo P.O está em {lockedStatus}; nada foi gravado.");

        // ── Correction of an existing hold ──
        if (live != null)
        {
            if (live.Status != SC.ReservationStatuses.Reserved)
                return Fail<ReservationOutcome>(409, SC.ErrorCodes.ReservationConflict, "Crédito já aplicado", "O crédito deste grupo já foi aplicado por Finanças; a correção da P.O não o pode alterar.");

            if (action == SC.CorrectionActions.Remove || (action == null && !cmd.UseSupplierCredit && cmd.IsCorrection && cmd.CorrectionAction != null))
            {
                var hold = SupplierCreditPolicy.Hold(live.Status, live.Amount, live.AppliedAmount);
                await LockAccountAsync(live.AccountId, ct);
                MarkReleased(live, SC.ReleaseReasons.CorrectionRemoved, "Reserva removida na correção da P.O pelo comprador.", cmd.ActorUserId, hold);
                group.CreditCoverageAmount = 0m;
                CancelPlannedCreditRows(group, live.Id, cmd.ActorUserId, "reserva removida na correção da P.O");
                var f = await FiguresAsync(live.AccountId, ct);
                return Ok(new ReservationOutcome
                {
                    Reservation = null, Coverage = 0m, IsFullCoverage = false, HistoryAction = SC.HistoryActions.Released,
                    HistoryText = $"Crédito em conta corrente removido na correção da P.O: reserva de {hold:N2} {group.CurrencyCode} libertada (sem movimento no saldo). Disponível na conta após: {f.Available:N2}.",
                    AccountAvailableAfter = f.Available,
                });
            }

            if (!SupplierCreditPolicy.IsPaymentConditionSupported(cmd.PaymentConditionCode))
                return Fail<ReservationOutcome>(400, SC.ErrorCodes.UnsupportedTerms, "Condição de pagamento não suportada com crédito",
                    "Nesta fase o crédito em conta corrente aplica-se apenas a Pós-Pago e 100 % Antecipado. Remova a reserva de crédito ou mantenha a condição de pagamento.");

            if (action == SC.CorrectionActions.Change)
            {
                if (!cmd.NewReservationsEnabled)
                    return Fail<ReservationOutcome>(400, SC.ErrorCodes.Disabled, "Alteração de crédito desativada", "A alteração do valor de crédito reservado não está ativa neste ambiente. Mantenha ou remova a reserva.");
                if (!cmd.RequestedAmount.HasValue)
                    return Fail<ReservationOutcome>(400, SC.ErrorCodes.InvalidAmount, "Valor obrigatório", "Indique o novo valor de crédito a utilizar.");
                var account = await LockAccountAsync(live.AccountId, ct);
                if (account == null || account.Status != SC.AccountStatuses.Active)
                    return Fail<ReservationOutcome>(409, SC.ErrorCodes.AccountInactive, "Conta indisponível", "A conta corrente de crédito do fornecedor não está ativa.");
                var figures = await FiguresAsync(account.Id, ct);
                var ownHold = SupplierCreditPolicy.Hold(live.Status, live.Amount, live.AppliedAmount);
                var decision = SupplierCreditPolicy.DecideCoverage(cmd.RequestedAmount.Value, group.TotalAmount, figures.Available + ownHold);
                if (!decision.IsValid) return Fail<ReservationOutcome>(decision.Code == SC.ErrorCodes.Insufficient ? 409 : 400, decision.Code!, "Crédito não reservado", decision.Message!);

                var previousAmount = live.Amount;
                var replacement = BuildReservation(account.Id, request, group, cmd, decision.Coverage);
                _context.SupplierCreditReservations.Add(replacement);
                MarkReleased(live, SC.ReleaseReasons.CorrectionChanged, $"Reserva substituída na correção da P.O ({previousAmount:N2} → {decision.Coverage:N2}).", cmd.ActorUserId, ownHold);
                live.SupersededByReservationId = replacement.Id;
                CancelPlannedCreditRows(group, live.Id, cmd.ActorUserId, "reserva substituída na correção da P.O");
                group.CreditCoverageAmount = decision.Coverage;
                group.SupplierCreditReservationId = replacement.Id;
                var rows = await CreateOwedRowsAsync(request, group, replacement, cmd, ct);
                var after = await FiguresAsync(account.Id, ct);
                return Ok(new ReservationOutcome
                {
                    Reservation = replacement, Coverage = decision.Coverage, IsFullCoverage = decision.IsFullCoverage, OwedRows = rows,
                    HistoryAction = SC.HistoryActions.ReservationChanged,
                    HistoryText = $"Reserva de crédito em conta corrente alterada na correção da P.O: {previousAmount:N2} → {decision.Coverage:N2} {group.CurrencyCode} (conta {account.AccountNumber}; disponível após: {after.Available:N2}). Banco: {(group.TotalAmount - decision.Coverage):N2}.",
                    AccountAvailableAfter = after.Available,
                });
            }

            // KEEP (default): same amount, split recomputed for the registered terms, owed rows recreated.
            {
                var account = await LockAccountAsync(live.AccountId, ct);
                var split = SupplierCreditPolicy.Split(group.TotalAmount, cmd.AdvancePercent, live.Amount);
                live.AdvanceCreditAmount = split.CreditAdvance; live.BalanceCreditAmount = split.CreditBalance;
                group.CreditCoverageAmount = live.Amount;
                group.SupplierCreditReservationId = live.Id;
                CancelPlannedCreditRows(group, live.Id, cmd.ActorUserId, "P.O corrigida — linhas recriadas");
                var rows = await CreateOwedRowsAsync(request, group, live, cmd, ct);
                var f = await FiguresAsync(live.AccountId, ct);
                return Ok(new ReservationOutcome
                {
                    Reservation = live, Coverage = live.Amount, IsFullCoverage = live.Amount >= group.TotalAmount, OwedRows = rows,
                    HistoryAction = null,
                    HistoryText = $"Reserva de crédito em conta corrente mantida: {live.Amount:N2} {group.CurrencyCode} (conta {account?.AccountNumber}). Banco: {(group.TotalAmount - live.Amount):N2}.",
                    AccountAvailableAfter = f.Available,
                });
            }
        }

        // ── New reservation (first registration, or a correction adding credit) ──
        if (!cmd.NewReservationsEnabled)
            return Fail<ReservationOutcome>(400, SC.ErrorCodes.Disabled, "Crédito em conta corrente desativado", "A utilização de crédito em conta corrente em novas P.O. não está ativa neste ambiente.");
        if (!SupplierCreditPolicy.IsPaymentConditionSupported(cmd.PaymentConditionCode))
            return Fail<ReservationOutcome>(400, SC.ErrorCodes.UnsupportedTerms, "Condição de pagamento não suportada com crédito", "Nesta fase o crédito em conta corrente aplica-se apenas a Pós-Pago e 100 % Antecipado.");
        if (!cmd.RequestedAmount.HasValue)
            return Fail<ReservationOutcome>(400, SC.ErrorCodes.InvalidAmount, "Valor obrigatório", "Indique o valor de crédito a utilizar.");
        if (!group.SupplierId.HasValue)
            return Fail<ReservationOutcome>(400, SC.ErrorCodes.AccountNotFound, "Fornecedor não definido", "O grupo P.O não tem fornecedor definido.");
        var currency = ResolveCurrency(request, group);
        if (currency == null)
            return Fail<ReservationOutcome>(400, SC.ErrorCodes.AccountNotFound, "Moeda não definida", "O grupo P.O não tem moeda definida.");

        var acc = await _context.SupplierCreditAccounts
            .FirstOrDefaultAsync(a => a.SupplierId == group.SupplierId.Value && a.CompanyId == request.CompanyId && a.CurrencyCode == currency, ct);
        if (acc == null)
            return Fail<ReservationOutcome>(400, SC.ErrorCodes.AccountNotFound, "Sem conta corrente", $"O fornecedor não tem conta corrente de crédito com a empresa do pedido em {currency}.");
        acc = await LockAccountAsync(acc.Id, ct) ?? acc;
        if (acc.Status != SC.AccountStatuses.Active)
            return Fail<ReservationOutcome>(409, SC.ErrorCodes.AccountInactive, "Conta encerrada", "A conta corrente de crédito do fornecedor está encerrada.");

        var fig = await FiguresAsync(acc.Id, ct);
        var cov = SupplierCreditPolicy.DecideCoverage(cmd.RequestedAmount.Value, group.TotalAmount, fig.Available);
        if (!cov.IsValid) return Fail<ReservationOutcome>(cov.Code == SC.ErrorCodes.Insufficient ? 409 : 400, cov.Code!, "Crédito não reservado", cov.Message!);

        var reservation = BuildReservation(acc.Id, request, group, cmd, cov.Coverage);
        _context.SupplierCreditReservations.Add(reservation);
        group.CreditCoverageAmount = cov.Coverage;
        group.SupplierCreditReservationId = reservation.Id;
        var owed = await CreateOwedRowsAsync(request, group, reservation, cmd, ct);
        var figAfter = await FiguresAsync(acc.Id, ct);
        return Ok(new ReservationOutcome
        {
            Reservation = reservation, Coverage = cov.Coverage, IsFullCoverage = cov.IsFullCoverage, OwedRows = owed,
            HistoryAction = SC.HistoryActions.Reserved,
            HistoryText = $"Crédito em conta corrente reservado: {cov.Coverage:N2} {currency} de {group.TotalAmount:N2} (conta {acc.AccountNumber}; disponível após reserva: {figAfter.Available:N2}). " +
                          (cov.IsFullCoverage ? "Cobertura integral — nenhum pagamento bancário devido." : $"Banco: {(group.TotalAmount - cov.Coverage):N2}."),
            AccountAvailableAfter = figAfter.Available,
        });
    }

    // ═════════════════════════════════════════ Apply ═════════════════════════════════════════

    public async Task<SupplierCreditOperationOutcome<ApplicationOutcome>> ApplyStageAsync(
        Request request, RequestPoGroup group, string stage, Guid actorUserId, string? note, CancellationToken ct = default)
    {
        stage = (stage ?? SC.Stages.Single).Trim().ToUpperInvariant();
        await LockGroupStatusAsync(group.Id, ct);
        var live = await LockLiveReservationAsync(group.Id, ct);
        if (live == null)
        {
            var latest = await _context.SupplierCreditReservations.AsNoTracking().Where(r => r.RequestPoGroupId == group.Id).OrderByDescending(r => r.ReservedAtUtc).FirstOrDefaultAsync(ct);
            return latest?.Status == SC.ReservationStatuses.Reversed
                ? Fail<ApplicationOutcome>(409, SC.ErrorCodes.AlreadyReversed, "Crédito revertido", "O crédito deste grupo foi revertido; não há reserva a aplicar.")
                : Fail<ApplicationOutcome>(409, SC.ErrorCodes.NothingToApply, "Sem crédito reservado", "Este grupo P.O não tem crédito em conta corrente reservado.");
        }
        if (live.Status == SC.ReservationStatuses.Applied)
            return Fail<ApplicationOutcome>(409, SC.ErrorCodes.AlreadyApplied, "Crédito já aplicado", "O crédito reservado deste grupo já foi aplicado.");
        if (!SupplierCreditPolicy.CanApplyStage(live.Status, stage, live.AppliedAmount))
            return Fail<ApplicationOutcome>(409, SC.ErrorCodes.AlreadyApplied, "Fase já aplicada", $"A reserva está em {SC.ReservationStatuses.Label(live.Status)}; a fase {stage} não pode ser aplicada.");

        var key = SupplierCreditPolicy.Keys.Application(live.Id, stage);
        if (await _context.SupplierCreditEntries.AnyAsync(e => e.IdempotencyKey == key, ct))
            return Fail<ApplicationOutcome>(409, SC.ErrorCodes.AlreadyApplied, "Crédito já aplicado", "Esta aplicação de crédito já foi registada.");

        var rows = await _context.RequestPayments.Where(p => p.RequestPoGroupId == group.Id && p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit).ToListAsync(ct);
        var row = rows.FirstOrDefault(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Planned && (p.SettlementStage ?? SC.Stages.Single) == stage);
        if (row == null)
        {
            return rows.Any(p => p.PaymentStatus == RequestPayment.PaymentStatuses.Completed && (p.SettlementStage ?? SC.Stages.Single) == stage)
                ? Fail<ApplicationOutcome>(409, SC.ErrorCodes.AlreadyApplied, "Crédito já aplicado", "A linha de crédito desta fase já está concluída.")
                : Fail<ApplicationOutcome>(409, SC.ErrorCodes.NothingToApply, "Sem linha de crédito", $"Não existe linha de crédito planeada para a fase {stage} deste grupo.");
        }

        var account = await LockAccountAsync(live.AccountId, ct);
        if (account == null) return Fail<ApplicationOutcome>(409, SC.ErrorCodes.AccountNotFound, "Conta não encontrada", "A conta corrente da reserva não existe.");
        var before = await FiguresAsync(account.Id, ct);
        var stageAmount = row.PlannedAmount;
        var now = DateTime.UtcNow;

        var entry = new SupplierCreditEntry
        {
            AccountId = account.Id, EntryType = SC.EntryTypes.Application, SignedAmount = -stageAmount, BalanceAfter = before.Balance - stageAmount,
            Reason = string.IsNullOrWhiteSpace(note) ? $"Crédito aplicado à P.O {group.PurchaseOrderNumber ?? "—"} ({stage})." : note.Trim(),
            ReservationId = live.Id, RequestId = request.Id, RequestPoGroupId = group.Id, Stage = stage,
            RequestNumberSnapshot = request.RequestNumber, PurchaseOrderNumberSnapshot = group.PurchaseOrderNumber, OriginPlantId = group.PlantId ?? request.PlantId,
            IdempotencyKey = key, CreatedByUserId = actorUserId, CreatedAtUtc = now,
        };
        _context.SupplierCreditEntries.Add(entry);

        var hold = SupplierCreditPolicy.Hold(live.Status, live.Amount, live.AppliedAmount);
        // A stage never applies more than its planned row; whatever hold is left after the LAST stage is released (no entry).
        var isLastStage = stage == SC.Stages.Single || stage == SC.Stages.Balance;
        var released = isLastStage ? Math.Max(0m, hold - stageAmount) : 0m;
        live.AppliedAmount += stageAmount;
        if (released > 0m) live.ReleasedHoldAmount = (live.ReleasedHoldAmount ?? 0m) + released;
        live.Status = SupplierCreditPolicy.StatusAfterApplication(live.Amount, live.AppliedAmount - stageAmount, stageAmount, live.ReleasedHoldAmount ?? 0m);
        if (isLastStage) live.Status = SC.ReservationStatuses.Applied;
        live.AppliedByUserId = actorUserId; live.AppliedAtUtc = now;
        if (stage == SC.Stages.Balance || (stage == SC.Stages.Single && group.PaymentConditionCode != RequestConstants.PaymentConditions.AdvanceFull))
            live.BalanceAppliedEntryId = entry.Id;
        else
            live.AdvanceAppliedEntryId = entry.Id;

        row.PaymentStatus = RequestPayment.PaymentStatuses.Completed;
        row.ActualPaidAmount = stageAmount;
        row.PaidDateUtc = now; row.PaidByUserId = actorUserId;
        row.UpdatedByUserId = actorUserId; row.UpdatedAtUtc = now;
        row.SupplierCreditReservationId = live.Id;
        row.Notes = Trunc($"{row.Notes} Crédito aplicado em {now:dd/MM/yyyy HH:mm} UTC (movimento {entry.Id.ToString()[..8]}).".Trim(), 2000);

        var after = new SupplierCreditPolicy.AccountFigures(before.Balance - stageAmount, before.Reserved - stageAmount - released, before.Available + released);
        return Ok(new ApplicationOutcome
        {
            Reservation = live, CreditRow = row, Entry = entry, AppliedAmount = stageAmount, ReleasedHoldAmount = released,
            AccountBalanceAfter = after.Balance, AccountAvailableAfter = after.Available,
            HistoryText = $"Crédito em conta corrente aplicado: {stageAmount:N2} {group.CurrencyCode} (fase {stage}; conta {account.AccountNumber}; saldo após: {after.Balance:N2}; disponível: {after.Available:N2}).",
        });
    }

    // ═════════════════════════════════════════ Release ═════════════════════════════════════════

    public async Task<SupplierCreditOperationOutcome<ReleaseOutcome>> ReleaseAsync(
        RequestPoGroup group, string releaseReason, string? note, Guid actorUserId, CancellationToken ct = default)
    {
        var live = await LockLiveReservationAsync(group.Id, ct);
        if (live == null)
            return Fail<ReleaseOutcome>(409, SC.ErrorCodes.NothingToApply, "Sem reserva", "Este grupo P.O não tem reserva de crédito ativa.");
        if (live.Status != SC.ReservationStatuses.Reserved)
            return Fail<ReleaseOutcome>(409, SC.ErrorCodes.ReleaseNotAllowed, "Reserva já aplicada", "O crédito já foi (parcialmente) aplicado; utilize a reversão por Finanças.");
        await LockAccountAsync(live.AccountId, ct);
        var hold = SupplierCreditPolicy.Hold(live.Status, live.Amount, live.AppliedAmount);
        MarkReleased(live, releaseReason, note, actorUserId, hold);
        group.CreditCoverageAmount = 0m;
        CancelPlannedCreditRows(group, live.Id, actorUserId, "reserva libertada");
        var f = await FiguresAsync(live.AccountId, ct);
        return Ok(new ReleaseOutcome
        {
            Reservation = live, ReleasedHoldAmount = hold, AccountAvailableAfter = f.Available,
            HistoryText = $"Reserva de crédito em conta corrente libertada ({ReleaseLabel(releaseReason)}): {hold:N2} {group.CurrencyCode} voltam a estar disponíveis (sem movimento no saldo). Disponível após: {f.Available:N2}.",
        });
    }

    // ═════════════════════════════════════════ Reverse (D7, stage 1 = FULL_COVERAGE) ═════════════════════════════════════════

    public async Task<SupplierCreditOperationOutcome<ReversalOutcome>> ReverseAsync(
        Request request, RequestPoGroup group, Guid actorUserId, string reason, CancellationToken ct = default)
    {
        await LockGroupStatusAsync(group.Id, ct);
        var live = await LockLiveReservationAsync(group.Id, ct);
        if (live == null)
        {
            var latest = await _context.SupplierCreditReservations.AsNoTracking().Where(r => r.RequestPoGroupId == group.Id).OrderByDescending(r => r.ReservedAtUtc).FirstOrDefaultAsync(ct);
            return latest?.Status == SC.ReservationStatuses.Reversed
                ? Fail<ReversalOutcome>(409, SC.ErrorCodes.AlreadyReversed, "Crédito já revertido", "O crédito deste grupo já foi revertido.")
                : Fail<ReversalOutcome>(409, SC.ErrorCodes.ReversalNotApplied, "Sem crédito aplicado", "Este grupo P.O não tem crédito aplicado para reverter.");
        }
        if (live.Status == SC.ReservationStatuses.Reserved)
            return Fail<ReversalOutcome>(409, SC.ErrorCodes.ReversalNotApplied, "Crédito ainda não aplicado", "A reserva ainda não foi aplicada; utilize a devolução para Compras ou a libertação da reserva.");

        var blockers = await EvidenceBlockersAsync(request, group, ct);
        if (blockers.Count > 0)
            return Fail<ReversalOutcome>(409, SC.ErrorCodes.ReversalBlockedByEvidence, "Reversão bloqueada por evidência posterior",
                "Existe evidência posterior à aplicação do crédito: " + string.Join("; ", blockers) + ". A reversão só é possível antes de qualquer receção, fatura ou reconciliação.",
                new Dictionary<string, object?> { ["blockers"] = blockers });

        var groupRows = await _context.RequestPayments.Where(p => p.RequestPoGroupId == group.Id).ToListAsync(ct);
        var anyCash = groupRows.Any(p => p.PaymentType != RequestPayment.PaymentTypes.SupplierCredit && p.PaymentStatus == RequestPayment.PaymentStatuses.Completed && (p.ActualPaidAmount ?? 0m) > 0m);
        var kind = SupplierCreditPolicy.ReversalKind(live.Amount, group.TotalAmount, anyCash);
        if (kind != SC.ReversalKinds.FullCoverage)
            return Fail<ReversalOutcome>(409, SC.ErrorCodes.ReversalPartialUnsupported, "Reversão parcial não suportada nesta fase",
                $"A reversão de crédito com cobertura parcial ({kind}) será disponibilizada na fase 2. Nada foi alterado.");

        var key = SupplierCreditPolicy.Keys.Reversal(live.Id);
        if (await _context.SupplierCreditEntries.AnyAsync(e => e.IdempotencyKey == key, ct))
            return Fail<ReversalOutcome>(409, SC.ErrorCodes.AlreadyReversed, "Crédito já revertido", "Esta reversão já foi registada.");

        var account = await LockAccountAsync(live.AccountId, ct);
        if (account == null) return Fail<ReversalOutcome>(409, SC.ErrorCodes.AccountNotFound, "Conta não encontrada", "A conta corrente da reserva não existe.");
        var before = await FiguresAsync(account.Id, ct);
        var now = DateTime.UtcNow;
        var applied = live.AppliedAmount;
        var hold = SupplierCreditPolicy.Hold(live.Status, live.Amount, live.AppliedAmount);

        var entry = new SupplierCreditEntry
        {
            AccountId = account.Id, EntryType = SC.EntryTypes.Reversal, SignedAmount = applied, BalanceAfter = before.Balance + applied,
            Reason = Trunc($"Reversão ({kind}) de crédito aplicado à P.O {group.PurchaseOrderNumber ?? "—"}: {reason.Trim()}", 1000)!,
            ReservationId = live.Id, RequestId = request.Id, RequestPoGroupId = group.Id, Stage = group.PaymentConditionCode == RequestConstants.PaymentConditions.PostPaid ? SC.Stages.Single : SC.Stages.Single,
            RequestNumberSnapshot = request.RequestNumber, PurchaseOrderNumberSnapshot = group.PurchaseOrderNumber, OriginPlantId = group.PlantId ?? request.PlantId,
            IdempotencyKey = key, CreatedByUserId = actorUserId, CreatedAtUtc = now,
        };
        _context.SupplierCreditEntries.Add(entry);

        live.Status = SC.ReservationStatuses.Reversed;
        live.ReversedAppliedAmount = applied;
        if (hold > 0m) live.ReleasedHoldAmount = (live.ReleasedHoldAmount ?? 0m) + hold;
        live.ReversalKind = kind; live.ReversalReason = Trunc(reason, 1000); live.ReversedByUserId = actorUserId; live.ReversedAtUtc = now;

        var cancelledCredit = new List<RequestPayment>();
        foreach (var p in groupRows.Where(p => p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PaymentStatus != RequestPayment.PaymentStatuses.Cancelled))
        {
            p.PaymentStatus = RequestPayment.PaymentStatuses.Cancelled;
            p.UpdatedByUserId = actorUserId; p.UpdatedAtUtc = now;
            p.Notes = Trunc($"{p.Notes} Revertido em {now:dd/MM/yyyy HH:mm} UTC ({kind}).".Trim(), 2000);
            cancelledCredit.Add(p);
        }
        group.CreditCoverageAmount = 0m; // pointer kept: the group stays credit-touched

        // Reopen the bank obligation per the registered terms (FULL_COVERAGE: the whole total, no cash was paid).
        var reopened = new List<RequestPayment>();
        string reopenedStatus;
        if (group.PaymentConditionCode == RequestConstants.PaymentConditions.AdvanceFull)
        {
            var seq = await NextSequenceAsync(request.Id, RequestPayment.PaymentTypes.Advance, ct);
            var adv = new RequestPayment
            {
                RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = RequestPayment.PaymentTypes.Advance, PaymentSequence = seq,
                PlannedPercent = 100m, PlannedAmount = group.TotalAmount, CurrencyCode = group.CurrencyCode ?? "AOA", PaymentStatus = RequestPayment.PaymentStatuses.Planned,
                SettlementStage = SC.Stages.Single, SupplierCreditReservationId = live.Id, CreatedByUserId = actorUserId, CreatedAtUtc = now,
                Notes = "Adiantamento reaberto por reversão de crédito em conta corrente (pagamento bancário integral).",
            };
            _context.RequestPayments.Add(adv); reopened.Add(adv); live.ReopenedAdvancePaymentId = null; // id assigned at save
            reopenedStatus = RequestConstants.Statuses.AdvancePaymentRequired;
        }
        else
        {
            var seq = await NextSequenceAsync(request.Id, RequestPayment.PaymentTypes.FinalBalance, ct);
            var bal = new RequestPayment
            {
                RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = RequestPayment.PaymentTypes.FinalBalance, PaymentSequence = seq,
                PlannedAmount = group.TotalAmount, CurrencyCode = group.CurrencyCode ?? "AOA", PaymentStatus = RequestPayment.PaymentStatuses.Planned,
                SettlementStage = SC.Stages.Single, SupplierCreditReservationId = live.Id, CreatedByUserId = actorUserId, CreatedAtUtc = now,
                Notes = "Saldo reaberto por reversão de crédito em conta corrente (pagamento bancário integral).",
            };
            _context.RequestPayments.Add(bal); reopened.Add(bal);
            reopenedStatus = RequestConstants.Statuses.PoIssued;
        }

        var after = new SupplierCreditPolicy.AccountFigures(before.Balance + applied, before.Reserved - hold, before.Available + applied + hold);
        return Ok(new ReversalOutcome
        {
            Reservation = live, Entry = entry, Kind = kind, ReversedAmount = applied, ReleasedHoldAmount = hold, AccountAvailableAfter = after.Available,
            ReopenedRows = reopened, CancelledCreditRows = cancelledCredit, ReopenedGroupStatus = reopenedStatus,
            HistoryText = $"Crédito em conta corrente revertido ({kind}): {applied:N2} {group.CurrencyCode} devolvidos à conta {account.AccountNumber}" +
                          (hold > 0m ? $" e reserva de {hold:N2} libertada" : string.Empty) +
                          $" (disponível após: {after.Available:N2}). Obrigação bancária reaberta: {group.TotalAmount:N2}. Motivo: {reason.Trim()}",
        });
    }

    public async Task<SupplierCreditReservation?> GetLiveReservationAsync(Guid groupId, CancellationToken ct = default)
        => await _context.SupplierCreditReservations.AsNoTracking()
            .Where(r => r.RequestPoGroupId == groupId && SC.ReservationStatuses.Live.Contains(r.Status))
            .OrderByDescending(r => r.ReservedAtUtc).FirstOrDefaultAsync(ct);

    public async Task<(decimal Balance, decimal Reserved, decimal Available)> AccountFiguresAsync(Guid accountId, CancellationToken ct = default)
    {
        var f = await FiguresAsync(accountId, ct);
        return (f.Balance, f.Reserved, f.Available);
    }

    // ═════════════════════════════════════════ internals ═════════════════════════════════════════

    private static string? ResolveCurrency(Request request, RequestPoGroup group)
        => (group.CurrencyCode ?? group.Currency?.Code ?? request.Currency?.Code ?? request.ApprovedCurrencyCode)?.Trim().ToUpperInvariant();

    private static SupplierCreditReservation BuildReservation(Guid accountId, Request request, RequestPoGroup group, ReserveSupplierCreditCommand cmd, decimal coverage)
    {
        var split = SupplierCreditPolicy.Split(group.TotalAmount, cmd.AdvancePercent, coverage);
        return new SupplierCreditReservation
        {
            AccountId = accountId, RequestId = request.Id, RequestPoGroupId = group.Id, Amount = coverage,
            AdvanceCreditAmount = split.CreditAdvance, BalanceCreditAmount = split.CreditBalance, AppliedAmount = 0m,
            Status = SC.ReservationStatuses.Reserved, ReservedByUserId = cmd.ActorUserId, ReservedAtUtc = DateTime.UtcNow,
        };
    }

    private async Task<List<RequestPayment>> CreateOwedRowsAsync(Request request, RequestPoGroup group, SupplierCreditReservation reservation, ReserveSupplierCreditCommand cmd, CancellationToken ct)
    {
        var split = SupplierCreditPolicy.Split(group.TotalAmount, cmd.AdvancePercent, reservation.Amount);
        var plan = SupplierCreditPolicy.PlanOwedRows(cmd.PaymentConditionCode, split);
        var rows = new List<RequestPayment>();
        var now = DateTime.UtcNow;
        foreach (var p in plan)
        {
            var row = new RequestPayment
            {
                RequestId = request.Id, RequestPoGroupId = group.Id, PaymentType = p.PaymentType,
                PaymentSequence = await NextSequenceAsync(request.Id, p.PaymentType, ct),
                PlannedPercent = p.PaymentType == RequestPayment.PaymentTypes.Advance ? cmd.AdvancePercent : null,
                PlannedAmount = p.PlannedAmount, CurrencyCode = group.CurrencyCode ?? "AOA", PaymentStatus = RequestPayment.PaymentStatuses.Planned,
                SettlementStage = p.Stage, SupplierCreditReservationId = reservation.Id, CreatedByUserId = cmd.ActorUserId, CreatedAtUtc = now,
                Notes = p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit
                    ? $"Crédito em conta corrente a aplicar por Finanças ({p.Stage}) — criado no registo da P.O."
                    : $"Parcela bancária ({p.Stage}) após crédito em conta corrente de {reservation.Amount:N2} — criada no registo da P.O.",
            };
            _context.RequestPayments.Add(row);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Request-scoped sequence (unique index RequestId + PaymentType + PaymentSequence): persisted and tracked-but-unsaved rows.</summary>
    private async Task<int> NextSequenceAsync(Guid requestId, string paymentType, CancellationToken ct)
    {
        var maxPersisted = await _context.RequestPayments.Where(p => p.RequestId == requestId && p.PaymentType == paymentType).Select(p => (int?)p.PaymentSequence).MaxAsync(ct) ?? 0;
        var maxTracked = _context.RequestPayments.Local.Where(p => p.RequestId == requestId && p.PaymentType == paymentType).Select(p => p.PaymentSequence).DefaultIfEmpty(0).Max();
        return Math.Max(maxPersisted, maxTracked) + 1;
    }

    private void CancelPlannedCreditRows(RequestPoGroup group, Guid reservationId, Guid actorUserId, string why)
    {
        var now = DateTime.UtcNow;
        var rows = _context.RequestPayments.Local
            .Where(p => p.RequestPoGroupId == group.Id && p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned)
            .ToList();
        var persisted = _context.RequestPayments
            .Where(p => p.RequestPoGroupId == group.Id && p.PaymentType == RequestPayment.PaymentTypes.SupplierCredit && p.PaymentStatus == RequestPayment.PaymentStatuses.Planned)
            .ToList();
        foreach (var p in rows.Concat(persisted).DistinctBy(p => p.Id == 0 ? (object)p : p.Id))
        {
            p.PaymentStatus = RequestPayment.PaymentStatuses.Cancelled;
            p.UpdatedByUserId = actorUserId; p.UpdatedAtUtc = now;
            p.Notes = Trunc($"{p.Notes} Cancelado em {now:dd/MM/yyyy HH:mm} UTC: {why}.".Trim(), 2000);
        }
    }

    private static void MarkReleased(SupplierCreditReservation r, string reason, string? note, Guid actorUserId, decimal hold)
    {
        r.Status = SC.ReservationStatuses.Released;
        r.ReleaseReason = reason; r.ReleaseNote = Trunc(note, 1000);
        r.ReleasedHoldAmount = (r.ReleasedHoldAmount ?? 0m) + hold;
        r.ReleasedByUserId = actorUserId; r.ReleasedAtUtc = DateTime.UtcNow;
    }

    private static string ReleaseLabel(string reason) => reason switch
    {
        SC.ReleaseReasons.GroupCancelled => "grupo P.O cancelado",
        SC.ReleaseReasons.CorrectionRemoved => "removida na correção da P.O",
        SC.ReleaseReasons.CorrectionChanged => "substituída na correção da P.O",
        SC.ReleaseReasons.AbandonedRequest => "pedido abandonado",
        _ => reason,
    };

    /// <summary>Facts that make a reversal unsafe: receiving, invoice or reconciliation evidence after the application.</summary>
    private async Task<List<string>> EvidenceBlockersAsync(Request request, RequestPoGroup group, CancellationToken ct)
    {
        var blockers = new List<string>();
        string[] laterStatuses = { RequestConstants.Statuses.WaitingReceipt, RequestConstants.Statuses.InFollowup, RequestConstants.Statuses.WaitingReconciliation, RequestConstants.Statuses.WaitingFiscalReceipt, RequestConstants.Statuses.Completed };
        if (laterStatuses.Contains(group.Status)) blockers.Add($"estado do grupo {group.Status}");
        if (group.OperationalReceiptCompletedAtUtc != null) blockers.Add("receção operacional registada");
        if (group.FiscalReceiptAttachmentId != null && group.FiscalReceiptAttachmentId != Guid.Empty) blockers.Add("recibo fiscal registado");
        var lines = await _context.RequestLineItems.AsNoTracking().Include(l => l.LineItemStatus).Where(l => l.RequestPoGroupId == group.Id && !l.IsDeleted).ToListAsync(ct);
        var received = lines.Where(l => l.ReceivedQuantity > 0m || l.LineItemStatus != null && (l.LineItemStatus.Code == "RECEIVED" || l.LineItemStatus.Code == "PARTIALLY_RECEIVED")).Select(l => $"#{l.LineNumber}").ToList();
        if (received.Count > 0) blockers.Add($"itens com receção registada ({string.Join(", ", received)})");
        if (await _context.OperationInvoiceAllocations.AsNoTracking().AnyAsync(a => a.RequestPoGroupId == group.Id, ct)) blockers.Add("fatura de operação alocada ao grupo");
        if (await _context.RequestReconciliations.AsNoTracking().AnyAsync(r => r.RequestId == request.Id && (r.RequestPoGroupId == null || r.RequestPoGroupId == group.Id), ct)) blockers.Add("reconciliação registada");
        return blockers;
    }

    private async Task<SupplierCreditPolicy.AccountFigures> FiguresAsync(Guid accountId, CancellationToken ct)
    {
        // Persisted rows plus the rows tracked in this unit of work (the caller's transaction has not committed yet).
        var persistedEntries = await _context.SupplierCreditEntries.AsNoTracking().Where(e => e.AccountId == accountId).Select(e => new { e.Id, e.SignedAmount }).ToListAsync(ct);
        var localEntries = _context.SupplierCreditEntries.Local.Where(e => e.AccountId == accountId).ToList();
        var amounts = persistedEntries.Where(p => localEntries.All(l => l.Id != p.Id)).Select(p => p.SignedAmount).Concat(localEntries.Select(l => l.SignedAmount));

        var persistedRes = await _context.SupplierCreditReservations.AsNoTracking().Where(r => r.AccountId == accountId).Select(r => new { r.Id, r.Status, r.Amount, r.AppliedAmount }).ToListAsync(ct);
        var localRes = _context.SupplierCreditReservations.Local.Where(r => r.AccountId == accountId).ToList();
        var reservations = persistedRes.Where(p => localRes.All(l => l.Id != p.Id)).Select(p => (p.Status, p.Amount, p.AppliedAmount))
            .Concat(localRes.Select(l => (l.Status, l.Amount, l.AppliedAmount)));
        return SupplierCreditPolicy.Figures(amounts, reservations);
    }

    private async Task<SupplierCreditAccount?> LockAccountAsync(Guid accountId, CancellationToken ct)
    {
        if (_context.Database.IsSqlServer())
            return await _context.SupplierCreditAccounts.FromSqlRaw("SELECT * FROM [SupplierCreditAccounts] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", accountId).FirstOrDefaultAsync(ct);
        return await _context.SupplierCreditAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
    }

    private async Task<SupplierCreditReservation?> LockLiveReservationAsync(Guid groupId, CancellationToken ct)
    {
        List<SupplierCreditReservation> rows;
        if (_context.Database.IsSqlServer())
            rows = await _context.SupplierCreditReservations
                .FromSqlRaw("SELECT * FROM [SupplierCreditReservations] WITH (UPDLOCK, ROWLOCK) WHERE [RequestPoGroupId] = {0}", groupId)
                .ToListAsync(ct);
        else
            rows = await _context.SupplierCreditReservations.Where(r => r.RequestPoGroupId == groupId).ToListAsync(ct);
        return rows.Where(r => SC.ReservationStatuses.Live.Contains(r.Status)).OrderByDescending(r => r.ReservedAtUtc).FirstOrDefault();
    }

    /// <summary>Step 1 of the lock order: the group row (UPDLOCK). Returns the committed status for revalidation.</summary>
    private async Task<string?> LockGroupStatusAsync(Guid groupId, CancellationToken ct)
    {
        if (!_context.Database.IsSqlServer()) return null;
        return await _context.Database.SqlQueryRaw<string>("SELECT [Status] AS [Value] FROM [RequestPoGroups] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", groupId).FirstOrDefaultAsync(ct);
    }

    private static SupplierCreditReservationSummaryDto Summarize(SupplierCreditReservation r, SupplierCreditAccount? account) => new()
    {
        Id = r.Id, AccountId = r.AccountId, AccountNumber = account?.AccountNumber ?? string.Empty, CurrencyCode = account?.CurrencyCode ?? string.Empty,
        Amount = r.Amount, AdvanceCreditAmount = r.AdvanceCreditAmount, BalanceCreditAmount = r.BalanceCreditAmount, AppliedAmount = r.AppliedAmount,
        HoldAmount = SupplierCreditPolicy.Hold(r.Status, r.Amount, r.AppliedAmount), Status = r.Status, StatusLabel = SC.ReservationStatuses.Label(r.Status),
        ReservedAtUtc = r.ReservedAtUtc, AppliedAtUtc = r.AppliedAtUtc, ReleasedAtUtc = r.ReleasedAtUtc, ReversedAtUtc = r.ReversedAtUtc,
        ReversalKind = r.ReversalKind, ReleaseReason = r.ReleaseReason, RowVersion = Convert.ToBase64String(r.RowVersion ?? Array.Empty<byte>()),
    };

    /// <summary>Public summary builder shared with the controllers.</summary>
    public static SupplierCreditReservationSummaryDto ToSummary(SupplierCreditReservation r, SupplierCreditAccount? account) => Summarize(r, account);

    private static SupplierCreditOperationOutcome<T> Ok<T>(T value) => SupplierCreditOperationOutcome<T>.Ok(value);
    private static SupplierCreditOperationOutcome<T> Fail<T>(int status, string code, string title, string detail, IDictionary<string, object?>? ext = null)
        => SupplierCreditOperationOutcome<T>.Fail(status, code, title, detail, ext);
    private static string? Trunc(string? s, int max) { var t = s?.Trim(); if (string.IsNullOrEmpty(t)) return null; return t.Length <= max ? t : t.Substring(0, max); }
}
