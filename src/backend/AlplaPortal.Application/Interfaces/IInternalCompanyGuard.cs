using AlplaPortal.Domain.Services;

namespace AlplaPortal.Application.Interfaces;

/// <summary>
/// The one place that asks the database "is this counterparty an internal ALPLA entity?".
/// </summary>
///
/// <remarks>
/// <para>Wraps <see cref="InternalCompanyPolicy"/> — which is pure and knows nothing about storage —
/// with the single query that loads the authoritative <c>Companies</c> rows. Every caller that has
/// to make this decision goes through here: OCR supplier matching, quick supplier creation, the
/// supplier pickers, source-document persistence and request submission. One implementation, so the
/// answer cannot differ depending on which door the data came in through.</para>
///
/// <para>There is no bypass argument on any method, and none should be added. This is financial
/// integrity, not authorization.</para>
/// </remarks>
public interface IInternalCompanyGuard
{
    /// <summary>The internal ALPLA legal entities, from the <c>Companies</c> table.</summary>
    Task<IReadOnlyList<InternalCompanyRef>> GetInternalCompaniesAsync(CancellationToken ct = default);

    /// <summary>
    /// The internal company a name/NIF pair resolves to, or null when it is a genuine third party.
    /// </summary>
    Task<InternalCompanyRef?> ResolveAsync(string? name, string? taxId, CancellationToken ct = default);

    /// <summary>
    /// The internal company an existing supplier row turns out to be, or null.
    /// </summary>
    ///
    /// <remarks>
    /// Needed because internal entities legitimately exist in the supplier master — ALPLA ANGOLA
    /// SOPRO arrives there from the Primavera sync on its own and would return after any deletion.
    /// They are excluded at the point of <b>use</b>, never removed.
    /// </remarks>
    Task<InternalCompanyRef?> ResolveSupplierAsync(int? supplierId, CancellationToken ct = default);
}
