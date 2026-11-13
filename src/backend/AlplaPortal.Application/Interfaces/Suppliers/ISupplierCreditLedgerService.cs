using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.SupplierCredit;

namespace AlplaPortal.Application.Interfaces.Suppliers;

/// <summary>
/// Supplier credit ledger — the Purchasing side (accounts, entries, balances, movements). Writes run in their
/// own ReadCommitted transaction with the account row locked (UPDLOCK) so a Purchasing adjustment can never race
/// a Buyer reservation into a negative availability. Workflow writes (reserve / apply / release / reverse) live
/// in <see cref="ISupplierCreditSettlementService"/>, which runs INSIDE the caller's transaction.
/// </summary>
public interface ISupplierCreditLedgerService
{
    Task<IReadOnlyList<SupplierCreditAccountDto>> ListAccountsAsync(int supplierId, CancellationToken ct = default);

    Task<SupplierCreditAccountDto?> GetAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>The ACTIVE account for supplier × company × currency, or null.</summary>
    Task<SupplierCreditAccountDto?> FindAccountAsync(int supplierId, int companyId, string currencyCode, CancellationToken ct = default);

    Task<SupplierCreditOperationOutcome<SupplierCreditAccountDto>> CreateAccountAsync(
        int supplierId, CreateSupplierCreditAccountDto dto, Guid actorUserId, CancellationToken ct = default);

    Task<SupplierCreditOperationOutcome<SupplierCreditAccountDto>> AddEntryAsync(
        Guid accountId, AddSupplierCreditEntryDto dto, Guid actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<SupplierCreditMovementDto>> ListMovementsAsync(Guid accountId, CancellationToken ct = default);
}

/// <summary>Service-layer result: either a payload or an HTTP-shaped refusal (status + code + title + detail).</summary>
public sealed class SupplierCreditOperationOutcome<T>
{
    public bool Succeeded { get; init; }
    public T? Value { get; init; }
    public int StatusCode { get; init; }
    public string? Code { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public IDictionary<string, object?> Extensions { get; init; } = new Dictionary<string, object?>();

    public static SupplierCreditOperationOutcome<T> Ok(T value) => new() { Succeeded = true, Value = value, StatusCode = 200 };

    public static SupplierCreditOperationOutcome<T> Fail(int status, string code, string title, string detail, IDictionary<string, object?>? extensions = null)
        => new() { Succeeded = false, StatusCode = status, Code = code, Title = title, Detail = detail, Extensions = extensions ?? new Dictionary<string, object?>() };
}
