using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.SupplierCredit;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Interfaces.Suppliers;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SC = AlplaPortal.Domain.Constants.SupplierCreditConstants;

namespace AlplaPortal.Infrastructure.Services.Suppliers;

/// <summary>
/// Purchasing side of the supplier credit ledger. Account creation allocates CCS-nnnnnn from SystemCounters under
/// UPDLOCK (same pattern as the supplier PortalCode); entries are appended under an UPDLOCK on the account row so a
/// downward adjustment can never race a reservation into a negative availability. Figures are always derived
/// (<see cref="SupplierCreditPolicy.Figures"/>), never stored.
/// </summary>
public sealed class SupplierCreditLedgerService : ISupplierCreditLedgerService
{
    private readonly ApplicationDbContext _context;
    private readonly IInternalCompanyGuard _internalCompanies;
    private readonly ILogger<SupplierCreditLedgerService> _logger;

    public SupplierCreditLedgerService(ApplicationDbContext context, IInternalCompanyGuard internalCompanies, ILogger<SupplierCreditLedgerService> logger)
    {
        _context = context;
        _internalCompanies = internalCompanies;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SupplierCreditAccountDto>> ListAccountsAsync(int supplierId, CancellationToken ct = default)
    {
        var accounts = await _context.SupplierCreditAccounts.AsNoTracking()
            .Include(a => a.Company)
            .Where(a => a.SupplierId == supplierId)
            .OrderBy(a => a.Company.Name).ThenBy(a => a.CurrencyCode)
            .ToListAsync(ct);
        var result = new List<SupplierCreditAccountDto>(accounts.Count);
        foreach (var a in accounts) result.Add(await ProjectAsync(a, ct));
        return result;
    }

    public async Task<SupplierCreditAccountDto?> GetAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var a = await _context.SupplierCreditAccounts.AsNoTracking().Include(x => x.Company).FirstOrDefaultAsync(x => x.Id == accountId, ct);
        return a == null ? null : await ProjectAsync(a, ct);
    }

    public async Task<SupplierCreditAccountDto?> FindAccountAsync(int supplierId, int companyId, string currencyCode, CancellationToken ct = default)
    {
        var code = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();
        var a = await _context.SupplierCreditAccounts.AsNoTracking().Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.SupplierId == supplierId && x.CompanyId == companyId && x.CurrencyCode == code, ct);
        return a == null ? null : await ProjectAsync(a, ct);
    }

    public async Task<SupplierCreditOperationOutcome<SupplierCreditAccountDto>> CreateAccountAsync(
        int supplierId, CreateSupplierCreditAccountDto dto, Guid actorUserId, CancellationToken ct = default)
    {
        var currency = (dto.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (currency.Length is < 3 or > 10)
            return Fail(400, SC.ErrorCodes.InvalidAmount, "Moeda inválida", "Indique o código ISO da moeda da conta (ex.: AOA, USD, EUR).");

        var supplier = await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier == null) return Fail(404, SC.ErrorCodes.AccountNotFound, "Fornecedor não encontrado", "O fornecedor indicado não existe.");
        if (await _internalCompanies.ResolveSupplierAsync(supplier.Id, ct) != null)
            return Fail(400, SC.ErrorCodes.InternalCompanySupplier, "Empresa interna", "Uma empresa do grupo ALPLA não pode ter conta corrente de crédito como fornecedor.");

        var company = await _context.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == dto.CompanyId, ct);
        if (company == null || !company.IsActive)
            return Fail(400, SC.ErrorCodes.AccountNotFound, "Empresa inválida", "A empresa compradora indicada não existe ou está inativa.");
        if (!await _context.Currencies.AsNoTracking().AnyAsync(c => c.Code == currency && c.IsActive, ct))
            return Fail(400, SC.ErrorCodes.InvalidAmount, "Moeda inválida", $"A moeda '{currency}' não está configurada no Portal.");

        if (await _context.SupplierCreditAccounts.AsNoTracking().AnyAsync(a => a.SupplierId == supplierId && a.CompanyId == dto.CompanyId && a.CurrencyCode == currency, ct))
            return Fail(409, SC.ErrorCodes.AccountExists, "Conta já existe", $"Já existe uma conta corrente de crédito deste fornecedor com {company.Name} em {currency}.");

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            var created = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                var number = await NextAccountNumberAsync(ct);
                var account = new SupplierCreditAccount
                {
                    SupplierId = supplierId, CompanyId = dto.CompanyId, CurrencyCode = currency, AccountNumber = number,
                    Status = SC.AccountStatuses.Active, Notes = Trunc(dto.Notes, 2000),
                    CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = actorUserId,
                };
                _context.SupplierCreditAccounts.Add(account);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return account.Id;
            });
            _context.ChangeTracker.Clear();
            var dtoOut = await GetAccountAsync(created, ct);
            return SupplierCreditOperationOutcome<SupplierCreditAccountDto>.Ok(dtoOut!);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_SupplierCreditAccounts", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogWarning(ex, "Supplier credit account creation lost a uniqueness race (supplier {SupplierId}, company {CompanyId}, {Currency})", supplierId, dto.CompanyId, currency);
            return Fail(409, SC.ErrorCodes.AccountExists, "Conta já existe", "A conta foi criada entretanto por outra operação. Atualize a página.");
        }
    }

    public async Task<SupplierCreditOperationOutcome<SupplierCreditAccountDto>> AddEntryAsync(
        Guid accountId, AddSupplierCreditEntryDto dto, Guid actorUserId, CancellationToken ct = default)
    {
        var type = (dto.EntryType ?? string.Empty).Trim().ToUpperInvariant();
        if (!SC.EntryTypes.PurchasingTypes.Contains(type))
            return Fail(400, SC.ErrorCodes.InvalidAmount, "Tipo de movimento inválido", "Indique CREDIT_IN, ADJUSTMENT_IN ou ADJUSTMENT_OUT.");
        var amount = Math.Round(dto.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
            return Fail(400, SC.ErrorCodes.InvalidAmount, "Valor inválido", "O valor do movimento deve ser superior a zero.");
        var reference = dto.Reference?.Trim();
        if (string.IsNullOrWhiteSpace(reference))
            return Fail(400, SC.ErrorCodes.InvalidAmount, "Referência obrigatória", "Indique a referência do documento do fornecedor (nota de crédito, acordo, e-mail…).");
        var reason = dto.Reason?.Trim() ?? string.Empty;
        if (reason.Length < SC.MinReasonLength || reason.Length > SC.MaxReasonLength)
            return Fail(400, SC.ErrorCodes.InvalidAmount, "Justificativa obrigatória", $"A justificativa deve ter entre {SC.MinReasonLength} e {SC.MaxReasonLength} caracteres.");

        var idempotencyKey = string.IsNullOrWhiteSpace(dto.ClientKey) ? null : $"ENTRY:{dto.ClientKey.Trim()}";
        if (idempotencyKey != null)
        {
            var existing = await _context.SupplierCreditEntries.AsNoTracking().FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, ct);
            if (existing != null)
            {
                var same = await GetAccountAsync(existing.AccountId, ct);
                return SupplierCreditOperationOutcome<SupplierCreditAccountDto>.Ok(same!);
            }
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        SupplierCreditOperationOutcome<SupplierCreditAccountDto>? refusal = null;
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                refusal = null;
                await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                var account = await LockAccountAsync(accountId, ct);
                if (account == null) { refusal = Fail(404, SC.ErrorCodes.AccountNotFound, "Conta não encontrada", "A conta corrente de crédito não existe."); return; }
                if (account.Status != SC.AccountStatuses.Active) { refusal = Fail(409, SC.ErrorCodes.AccountInactive, "Conta encerrada", "A conta corrente está encerrada e não aceita movimentos."); return; }

                var figures = await FiguresAsync(accountId, ct);
                var signed = type == SC.EntryTypes.AdjustmentOut ? -amount : amount;
                if (signed < 0m && figures.Available + signed < 0m)
                {
                    refusal = Fail(409, SC.ErrorCodes.AdjustmentBelowReserved, "Ajuste abaixo do valor reservado",
                        $"O ajuste de {amount:N2} deixaria a conta com disponível negativo (saldo {figures.Balance:N2}, reservado {figures.Reserved:N2}, disponível {figures.Available:N2}). Liberte ou aplique as reservas antes de reduzir o saldo.");
                    return;
                }

                _context.SupplierCreditEntries.Add(new SupplierCreditEntry
                {
                    AccountId = accountId, EntryType = type, SignedAmount = signed, BalanceAfter = figures.Balance + signed,
                    Reference = Trunc(reference, 200), DocumentDateUtc = dto.DocumentDateUtc, Reason = reason,
                    IdempotencyKey = idempotencyKey, CreatedByUserId = actorUserId, CreatedAtUtc = DateTime.UtcNow,
                });
                account.UpdatedAtUtc = DateTime.UtcNow; account.UpdatedByUserId = actorUserId;
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_SupplierCreditEntries_IdempotencyKey", StringComparison.OrdinalIgnoreCase) == true)
        {
            _context.ChangeTracker.Clear();
            var same = await GetAccountAsync(accountId, ct);
            return SupplierCreditOperationOutcome<SupplierCreditAccountDto>.Ok(same!);
        }
        if (refusal != null) { _context.ChangeTracker.Clear(); return refusal; }
        _context.ChangeTracker.Clear();
        return SupplierCreditOperationOutcome<SupplierCreditAccountDto>.Ok((await GetAccountAsync(accountId, ct))!);
    }

    public async Task<IReadOnlyList<SupplierCreditMovementDto>> ListMovementsAsync(Guid accountId, CancellationToken ct = default)
    {
        var entries = await _context.SupplierCreditEntries.AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new { e, ActorName = _context.Users.Where(u => u.Id == e.CreatedByUserId).Select(u => u.FullName).FirstOrDefault(),
                PlantName = e.OriginPlantId != null ? _context.Plants.Where(p => p.Id == e.OriginPlantId).Select(p => p.Name).FirstOrDefault() : null })
            .ToListAsync(ct);
        var reservations = await _context.SupplierCreditReservations.AsNoTracking()
            .Where(r => r.AccountId == accountId)
            .Select(r => new
            {
                r, RequestNumber = _context.Requests.Where(q => q.Id == r.RequestId).Select(q => q.RequestNumber).FirstOrDefault(),
                PoNumber = _context.RequestPoGroups.Where(g => g.Id == r.RequestPoGroupId).Select(g => g.PurchaseOrderNumber).FirstOrDefault(),
                PlantName = _context.Requests.Where(q => q.Id == r.RequestId).Select(q => q.Plant != null ? q.Plant.Name : null).FirstOrDefault(),
                ReservedBy = _context.Users.Where(u => u.Id == r.ReservedByUserId).Select(u => u.FullName).FirstOrDefault(),
                ReleasedBy = r.ReleasedByUserId != null ? _context.Users.Where(u => u.Id == r.ReleasedByUserId).Select(u => u.FullName).FirstOrDefault() : null,
                ReversedBy = r.ReversedByUserId != null ? _context.Users.Where(u => u.Id == r.ReversedByUserId).Select(u => u.FullName).FirstOrDefault() : null,
            })
            .ToListAsync(ct);

        var list = new List<SupplierCreditMovementDto>();
        foreach (var x in entries)
        {
            var e = x.e;
            list.Add(new SupplierCreditMovementDto
            {
                Id = e.Id, Kind = "ENTRY", Type = e.EntryType, TypeLabel = SC.EntryTypes.Label(e.EntryType), SignedAmount = e.SignedAmount, BalanceAfter = e.BalanceAfter,
                Reference = e.Reference, Reason = e.Reason, Stage = e.Stage, RequestId = e.RequestId, RequestNumber = e.RequestNumberSnapshot,
                RequestPoGroupId = e.RequestPoGroupId, PurchaseOrderNumber = e.PurchaseOrderNumberSnapshot, PlantName = x.PlantName,
                OccurredAtUtc = e.CreatedAtUtc, ActorName = x.ActorName,
            });
        }
        foreach (var x in reservations)
        {
            var r = x.r;
            // The reservation event — one row per hold with its CURRENT state (a hold is not a movement; it explains Available).
            list.Add(new SupplierCreditMovementDto
            {
                Id = r.Id, Kind = "RESERVATION", Type = r.Status, TypeLabel = $"Reserva — {SC.ReservationStatuses.Label(r.Status)}",
                HoldAmount = SupplierCreditPolicy.Hold(r.Status, r.Amount, r.AppliedAmount), AppliedAmount = r.AppliedAmount,
                Reason = r.ReleaseNote ?? r.ReversalReason, RequestId = r.RequestId, RequestNumber = x.RequestNumber,
                RequestPoGroupId = r.RequestPoGroupId, PurchaseOrderNumber = x.PoNumber, PlantName = x.PlantName,
                OccurredAtUtc = r.ReversedAtUtc ?? r.ReleasedAtUtc ?? r.AppliedAtUtc ?? r.ReservedAtUtc,
                ActorName = x.ReversedBy ?? x.ReleasedBy ?? x.ReservedBy,
                SignedAmount = null, BalanceAfter = null,
            });
        }
        return list.OrderByDescending(m => m.OccurredAtUtc).ToList();
    }

    // ── internals ──

    private async Task<SupplierCreditAccountDto> ProjectAsync(SupplierCreditAccount a, CancellationToken ct)
    {
        var f = await FiguresAsync(a.Id, ct);
        var live = await _context.SupplierCreditReservations.AsNoTracking().CountAsync(r => r.AccountId == a.Id && SC.ReservationStatuses.Live.Contains(r.Status), ct);
        var createdBy = await _context.Users.AsNoTracking().Where(u => u.Id == a.CreatedByUserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
        return new SupplierCreditAccountDto
        {
            Id = a.Id, SupplierId = a.SupplierId, CompanyId = a.CompanyId, CompanyName = a.Company?.Name ?? string.Empty, CompanyCode = a.Company?.Code,
            CurrencyCode = a.CurrencyCode, AccountNumber = a.AccountNumber, Status = a.Status, Notes = a.Notes,
            Balance = f.Balance, Reserved = f.Reserved, Available = f.Available, LiveReservationCount = live,
            CreatedAtUtc = a.CreatedAtUtc, CreatedByName = createdBy, UpdatedAtUtc = a.UpdatedAtUtc,
            RowVersion = Convert.ToBase64String(a.RowVersion ?? Array.Empty<byte>()),
        };
    }

    private async Task<SupplierCreditPolicy.AccountFigures> FiguresAsync(Guid accountId, CancellationToken ct)
    {
        var amounts = await _context.SupplierCreditEntries.AsNoTracking().Where(e => e.AccountId == accountId).Select(e => e.SignedAmount).ToListAsync(ct);
        var holds = await _context.SupplierCreditReservations.AsNoTracking().Where(r => r.AccountId == accountId && SC.ReservationStatuses.Holding.Contains(r.Status))
            .Select(r => new { r.Status, r.Amount, r.AppliedAmount }).ToListAsync(ct);
        return SupplierCreditPolicy.Figures(amounts, holds.Select(h => (h.Status, h.Amount, h.AppliedAmount)));
    }

    private async Task<SupplierCreditAccount?> LockAccountAsync(Guid accountId, CancellationToken ct)
    {
        if (_context.Database.IsSqlServer())
            return await _context.SupplierCreditAccounts
                .FromSqlRaw("SELECT * FROM [SupplierCreditAccounts] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {0}", accountId)
                .FirstOrDefaultAsync(ct);
        return await _context.SupplierCreditAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
    }

    /// <summary>CCS-nnnnnn from SystemCounters (UPDLOCK; self-healing against the max persisted number).</summary>
    private async Task<string> NextAccountNumberAsync(CancellationToken ct)
    {
        var counter = _context.Database.IsSqlServer()
            ? await _context.SystemCounters.FromSqlRaw("SELECT * FROM SystemCounters WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", SC.AccountNumberCounterKey).FirstOrDefaultAsync(ct)
            : await _context.SystemCounters.FirstOrDefaultAsync(c => c.Id == SC.AccountNumberCounterKey, ct);
        var existing = await _context.SupplierCreditAccounts.AsNoTracking().Select(a => a.AccountNumber).ToListAsync(ct);
        var maxInDb = existing.Select(n => int.TryParse(n.Replace(SC.AccountNumberPrefix, string.Empty), out var v) ? v : 0).DefaultIfEmpty(0).Max();
        int seq;
        if (counter == null)
        {
            seq = maxInDb + 1;
            _context.SystemCounters.Add(new SystemCounter { Id = SC.AccountNumberCounterKey, CurrentValue = seq, LastUpdatedUtc = DateTime.UtcNow });
        }
        else
        {
            if (counter.CurrentValue < maxInDb) counter.CurrentValue = maxInDb;
            counter.CurrentValue++;
            counter.LastUpdatedUtc = DateTime.UtcNow;
            seq = counter.CurrentValue;
        }
        return $"{SC.AccountNumberPrefix}{seq:D6}";
    }

    private static SupplierCreditOperationOutcome<SupplierCreditAccountDto> Fail(int status, string code, string title, string detail)
        => SupplierCreditOperationOutcome<SupplierCreditAccountDto>.Fail(status, code, title, detail);

    private static string? Trunc(string? s, int max) { var t = s?.Trim(); if (string.IsNullOrEmpty(t)) return null; return t.Length <= max ? t : t.Substring(0, max); }
}
