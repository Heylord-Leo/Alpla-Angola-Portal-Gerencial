using System.Text;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Builds parameterized SQL queries for paginated transfer/purchase order listing.
///
/// Two query variants:
///   • Standard (Viana 1, Viana 2): T_Bestellungen + T_EAIJournal + T_Bestellpositionen + T_Artikelvarianten
///   • Inhouse  (Viana 3): Same base with INHOUSE pipeline model
///
/// Key design decisions:
///   • Uses OUTER APPLY (SELECT TOP 1 ...) for T_Bestellpositionen to ensure
///     exactly one row per IdBestellung — a PO may have multiple line items.
///   • Date filter uses Add_Date (PO creation date) — reliable and consistent.
///   • Status filter uses parameterized values (not dynamic SQL).
///   • Search uses parameterized LIKE with % wildcards added in service layer.
///   • Pagination uses OFFSET/FETCH NEXT.
///
/// All queries are READ-ONLY (SELECT only). No writes are ever performed.
///
/// Parameters:
///   @DateFrom    — Start date (inclusive)
///   @DateTo      — End date (exclusive — service adds +1 day)
///   @StatusList  — Comma-separated status values OR NULL for all
///   @ArticleSearch — LIKE pattern OR NULL
///   @PoSearch    — LIKE pattern OR NULL
///   @Offset      — Row offset for pagination
///   @PageSize    — Number of rows to return
///
/// Design reference: docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md §8
/// </summary>
public static class OperationsTransferListQueryBuilder
{
    // ─── Status filter mappings ───

    /// <summary>
    /// Maps status filter strings to T_Bestellungen.Status integer values.
    /// </summary>
    public static IReadOnlyList<int>? GetStatusValues(string? statusFilter) => statusFilter?.ToUpperInvariant() switch
    {
        "ACTIVE" => new[] { 1, 2, 6 },
        "SUBMITTED" => new[] { 2 },
        "PARTIALLY_DELIVERED" => new[] { 5 },
        "COMPLETED" => new[] { 7, 8 },
        "CANCELLED" => new[] { 3 },
        null => null,
        "" => null,
        _ => null, // unknown filter treated as no filter
    };

    /// <summary>
    /// Returns true if the status filter value is recognized.
    /// Returns true for null/empty (no filter = valid).
    /// </summary>
    public static bool IsValidStatusFilter(string? statusFilter)
    {
        if (string.IsNullOrWhiteSpace(statusFilter)) return true;
        return statusFilter.ToUpperInvariant() switch
        {
            "ACTIVE" or "SUBMITTED" or "PARTIALLY_DELIVERED" or "COMPLETED" or "CANCELLED" => true,
            _ => false,
        };
    }

    // ─── Query builders ───

    /// <summary>
    /// Builds the data query for Standard pipeline (VIANA1/VIANA2).
    /// Returns one row per IdBestellung with first position enrichment.
    /// </summary>
    public static string BuildStandardListDataQuery(IReadOnlyList<int>? statusValues)
    {
        return BuildListDataQuery(statusValues);
    }

    /// <summary>
    /// Builds the data query for Inhouse pipeline (VIANA3).
    /// Uses the same base query — the pipeline model difference is only in metadata.
    /// </summary>
    public static string BuildInhouseListDataQuery(IReadOnlyList<int>? statusValues)
    {
        // Same SQL structure — Inhouse differs only in response metadata (pipeline model, event count)
        return BuildListDataQuery(statusValues);
    }

    /// <summary>
    /// Builds the count query (total distinct POs matching filters).
    /// </summary>
    public static string BuildListCountQuery(IReadOnlyList<int>? statusValues)
    {
        var sb = new StringBuilder(512);

        sb.AppendLine("SELECT COUNT(DISTINCT b.IdBestellung)");
        sb.AppendLine("FROM [dbo].[T_Bestellungen] b");
        sb.AppendLine("LEFT JOIN [dbo].[T_EAIJournal] j ON j.IdJournal = b.IdJournal");

        // Same OUTER APPLY as data query — needed because WHERE clause references
        // pos.MaterialName and pos.ArticleAlias for article search
        sb.AppendLine("OUTER APPLY (");
        sb.AppendLine("    SELECT TOP 1");
        sb.AppendLine("        av.Bezeichnung       AS MaterialName,");
        sb.AppendLine("        av.Alias              AS ArticleAlias,");
        sb.AppendLine("        bp.IdArtikelVarianten");
        sb.AppendLine("    FROM [dbo].[T_Bestellpositionen] bp");
        sb.AppendLine("    LEFT JOIN [dbo].[T_Artikelvarianten] av");
        sb.AppendLine("        ON av.IdArtikelVarianten = bp.IdArtikelVarianten");
        sb.AppendLine("    WHERE bp.IdBestellung = b.IdBestellung");
        sb.AppendLine("    ORDER BY bp.IdBestellPosition ASC");
        sb.AppendLine(") pos");

        AppendWhereClause(sb, statusValues);

        return sb.ToString();
    }

    // ─── Private helpers ───

    private static string BuildListDataQuery(IReadOnlyList<int>? statusValues)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("SELECT");
        sb.AppendLine("    b.IdBestellung,");
        sb.AppendLine("    b.IdJournal,");
        sb.AppendLine("    j.JournalNummer,");
        sb.AppendLine("    b.[Status]          AS MainStatus,");
        sb.AppendLine("    b.Add_Date          AS CreatedDate,");
        sb.AppendLine("    b.Upd_Date          AS UpdatedDate,");
        sb.AppendLine("    pos.MaterialName,");
        sb.AppendLine("    pos.ArticleAlias,");
        sb.AppendLine("    pos.Quantity,");
        sb.AppendLine("    CAST(b.IdBestellung AS NVARCHAR(50)) AS ReferenceNumber,");
        sb.AppendLine("    b.Bemerkung         AS Notes");
        sb.AppendLine("FROM [dbo].[T_Bestellungen] b");
        sb.AppendLine("LEFT JOIN [dbo].[T_EAIJournal] j ON j.IdJournal = b.IdJournal");

        // OUTER APPLY ensures exactly one row per PO even with multiple positions
        sb.AppendLine("OUTER APPLY (");
        sb.AppendLine("    SELECT TOP 1");
        sb.AppendLine("        av.Bezeichnung       AS MaterialName,");
        sb.AppendLine("        av.Alias              AS ArticleAlias,");
        sb.AppendLine("        bp.BestellMenge       AS Quantity,");
        sb.AppendLine("        bp.IdArtikelVarianten");
        sb.AppendLine("    FROM [dbo].[T_Bestellpositionen] bp");
        sb.AppendLine("    LEFT JOIN [dbo].[T_Artikelvarianten] av");
        sb.AppendLine("        ON av.IdArtikelVarianten = bp.IdArtikelVarianten");
        // NOTE: T_ArtikelvariantenTyp join removed — IdArtikelvariantenTyp column
        // does not exist in T_Artikelvarianten on AlplaPROD. ArticleVariantType
        // will be null in results. Can be re-added when schema is confirmed.
        sb.AppendLine("    WHERE bp.IdBestellung = b.IdBestellung");
        sb.AppendLine("    ORDER BY bp.IdBestellPosition ASC");
        sb.AppendLine(") pos");

        AppendWhereClause(sb, statusValues);

        sb.AppendLine("ORDER BY b.Add_Date DESC");
        sb.AppendLine("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;");

        return sb.ToString();
    }

    private static void AppendWhereClause(StringBuilder sb, IReadOnlyList<int>? statusValues)
    {
        sb.AppendLine("WHERE b.Add_Date >= @DateFrom");
        sb.AppendLine("  AND b.Add_Date < @DateTo");

        // Status filter — parameterized IN clause
        if (statusValues != null && statusValues.Count > 0)
        {
            // Build parameterized placeholders: @S0, @S1, @S2
            var placeholders = new StringBuilder();
            for (int i = 0; i < statusValues.Count; i++)
            {
                if (i > 0) placeholders.Append(", ");
                placeholders.Append($"@S{i}");
            }
            sb.AppendLine($"  AND b.[Status] IN ({placeholders})");
        }

        // Article search — LIKE on Bezeichnung or Alias
        sb.AppendLine("  AND (@ArticleSearch IS NULL OR pos.MaterialName LIKE @ArticleSearch OR pos.ArticleAlias LIKE @ArticleSearch)");

        // PO search — LIKE on IdBestellung (cast) or JournalNummer
        sb.AppendLine("  AND (@PoSearch IS NULL OR CAST(b.IdBestellung AS NVARCHAR(50)) LIKE @PoSearch OR j.JournalNummer LIKE @PoSearch)");
    }
}
