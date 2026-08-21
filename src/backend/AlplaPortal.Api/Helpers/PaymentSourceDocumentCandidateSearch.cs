using AlplaPortal.Domain.Services;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Helpers;

/// <summary>
/// One database row of the candidate search — primitive persisted fields only, materialized
/// before any normalization/scoring happens. Public so the translation regression tests can
/// compile the query with the real SQL Server provider.
/// </summary>
public sealed class PaymentSourceDocumentCandidateRow
{
    public Guid Id { get; init; }
    public int SequenceNumber { get; init; }
    public string? DocumentNumber { get; init; }
    public string? DocumentSeries { get; init; }
    public int? SupplierId { get; init; }
    public string? SupplierNameSnapshot { get; init; }
    public string? SupplierTaxIdSnapshot { get; init; }
    public string? SupplierMasterName { get; init; }
    public string? SupplierMasterTaxId { get; init; }
    public DateTime? DocumentDate { get; init; }
    public string? Currency { get; init; }
    public decimal? GrossAmount { get; init; }
    public int CompanyId { get; init; }
    public string? RequestNumber { get; init; }
    public Guid RequestId { get; init; }
}

/// <summary>What the candidate search needs to know about the incoming document.</summary>
public sealed record CandidateSearchInput
{
    /// <summary>The request being edited, when one exists. Null in the creation wizard.</summary>
    public Guid? CurrentRequestId { get; init; }
    /// <summary>Excluded so a document cannot be its own twin on update.</summary>
    public Guid? ExcludeDocumentId { get; init; }

    public int? CompanyId { get; init; }
    public int? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public string? SupplierTaxId { get; init; }
    public string? DocumentNumber { get; init; }
    public string? DocumentSeries { get; init; }
    public DateTime? DocumentDate { get; init; }
    public string? Currency { get; init; }
    public decimal? GrossAmount { get; init; }
    public string? ItemFingerprint { get; init; }
}

/// <summary>
/// Assembles the material for the LEVELS 2–4 business-duplicate evaluation: the candidate record
/// and every comparand sharing its normalized reference.
///
/// <para><b>The search anchors on the document number, never on the supplier.</b> Supplier
/// identity is evidence the hierarchy weighs — a NIF misread or an unresolved supplier must not
/// erase the agreement of number, date, currency and total. Comparands come from this request's
/// other active documents plus active documents of other LIVE requests; dead requests
/// (CANCELLED/REJECTED) and voided documents never participate.</para>
///
/// <para>Shared by the persistence guard and the review-time preflight — one assembly, one rule
/// engine, so what the UI predicts and what persistence enforces cannot drift.</para>
/// </summary>
public static class PaymentSourceDocumentCandidateSearch
{
    public static BusinessDuplicateCandidate BuildCandidate(CandidateSearchInput input) => new()
    {
        DocumentNumber = input.DocumentNumber,
        DocumentSeries = input.DocumentSeries,
        SupplierId = input.SupplierId,
        SupplierName = input.SupplierName,
        SupplierTaxId = input.SupplierTaxId,
        DocumentDate = input.DocumentDate,
        CompanyId = input.CompanyId,
        Currency = input.Currency,
        GrossAmount = input.GrossAmount,
        ItemFingerprint = input.ItemFingerprint
    };

    public static async Task<List<BusinessDuplicateComparand>> AssembleComparandsAsync(
        ApplicationDbContext context, CandidateSearchInput input)
    {
        var comparands = new List<BusinessDuplicateComparand>();

        var number = PaymentSourceDocumentFingerprint.NormalizeReference(input.DocumentNumber);
        if (number.Length == 0) return comparands;
        var series = PaymentSourceDocumentFingerprint.NormalizeReference(input.DocumentSeries);

        bool SameReference(string? n, string? s) =>
            string.Equals(PaymentSourceDocumentFingerprint.NormalizeReference(n), number, StringComparison.Ordinal) &&
            string.Equals(PaymentSourceDocumentFingerprint.NormalizeReference(s), series, StringComparison.Ordinal);

        // ── SQL side: the translatable query, projection as the FINAL operator ──
        // Normalization, fingerprints and every helper call happen strictly AFTER ToListAsync.
        var rows = await BuildCandidateRowsQuery(context, input).ToListAsync();

        // ── In-memory side: normalization filter, scope split, fingerprints ──
        foreach (var row in rows.Where(r => SameReference(r.DocumentNumber, r.DocumentSeries)))
        {
            var scope = input.CurrentRequestId != null && row.RequestId == input.CurrentRequestId
                ? BusinessDuplicateScope.SameRequest
                : BusinessDuplicateScope.OtherRequest;
            comparands.Add(await ToComparandAsync(context, row, scope));
        }

        return comparands;
    }

    /// <summary>
    /// The DATABASE half of the candidate search, exposed as an <see cref="IQueryable"/> so the
    /// translation regression tests can compile it with the real SQL Server provider
    /// (<c>ToQueryString()</c>) — the InMemory provider evaluates client-side and cannot catch
    /// "could not be translated" failures, which is exactly how one reached DEV.
    ///
    /// <para>Rules of this method: simple column/navigation predicates only (the dead-status
    /// filter rides the Request→Status navigation with a static string list — translatable), and
    /// the member-init projection is the FINAL operator. EF supports a projection only as the last
    /// operation; composing a <c>Where</c> over projected members is precisely the failure this
    /// replaced. No helper call (normalization, tolerance, fingerprint) may appear here.</para>
    /// </summary>
    public static IQueryable<PaymentSourceDocumentCandidateRow> BuildCandidateRowsQuery(
        ApplicationDbContext context, CandidateSearchInput input)
    {
        var currentId = input.CurrentRequestId;
        var excludeDocumentId = input.ExcludeDocumentId;

        return context.PaymentSourceDocuments
            .AsNoTracking()
            .Where(d => !d.IsVoided && d.DocumentNumber != null)
            .Where(d => excludeDocumentId == null || d.Id != excludeDocumentId)
            .Where(d =>
                // this request's other active documents…
                (currentId != null && d.RequestId == currentId) ||
                // …or active documents of other LIVE requests
                ((currentId == null || d.RequestId != currentId) &&
                 (d.Request!.Status == null ||
                  !PaymentSourceDocumentFileTwins.TerminalDeadRequestStatuses.Contains(d.Request.Status.Code))))
            .Select(d => new PaymentSourceDocumentCandidateRow
            {
                Id = d.Id,
                SequenceNumber = d.SequenceNumber,
                DocumentNumber = d.DocumentNumber,
                DocumentSeries = d.DocumentSeries,
                SupplierId = d.SupplierId,
                SupplierNameSnapshot = d.SupplierNameSnapshot,
                SupplierTaxIdSnapshot = d.SupplierTaxIdSnapshot,
                SupplierMasterName = d.Supplier != null ? d.Supplier.Name : null,
                SupplierMasterTaxId = d.Supplier != null ? d.Supplier.TaxId : null,
                DocumentDate = d.DocumentDate,
                Currency = d.Currency,
                GrossAmount = d.GrossAmount,
                CompanyId = d.Request!.CompanyId,
                RequestNumber = d.Request.RequestNumber,
                RequestId = d.RequestId
            });
    }

    private static async Task<BusinessDuplicateComparand> ToComparandAsync(
        ApplicationDbContext context, PaymentSourceDocumentCandidateRow row, BusinessDuplicateScope scope) => new()
    {
        Id = row.Id,
        SequenceNumber = row.SequenceNumber,
        DocumentNumber = row.DocumentNumber,
        DocumentSeries = row.DocumentSeries,
        SupplierId = row.SupplierId,
        // The documentary snapshot wins; the master row fills the gaps.
        SupplierName = row.SupplierNameSnapshot ?? row.SupplierMasterName,
        SupplierTaxId = row.SupplierTaxIdSnapshot ?? row.SupplierMasterTaxId,
        DocumentDate = row.DocumentDate,
        CompanyId = row.CompanyId,
        Currency = row.Currency,
        GrossAmount = row.GrossAmount,
        ItemFingerprint = await ComputeItemFingerprintAsync(context, row.Id),
        Scope = scope,
        RequestNumber = row.RequestNumber,
        RequestId = row.RequestId
    };

    /// <summary>
    /// Content fingerprint of a document's active items, or null when it has none — the hierarchy
    /// must see "no evidence", never a fabricated empty fingerprint.
    /// </summary>
    public static async Task<string?> ComputeItemFingerprintAsync(ApplicationDbContext context, Guid documentId)
    {
        var items = await context.RequestLineItems
            .AsNoTracking()
            .Where(i => i.PaymentSourceDocumentId == documentId && !i.IsDeleted)
            .Select(i => new { i.Description, i.Quantity, i.UnitPrice, i.TotalAmount })
            .ToListAsync();

        return PaymentSourceDocumentFingerprint.Compute(
            items.Select(i => new DuplicateFingerprintItem(
                i.Description, i.Quantity, i.UnitPrice, i.TotalAmount)));
    }
}
