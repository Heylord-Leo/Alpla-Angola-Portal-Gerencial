using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Primavera article/material master-data lookup — read-only, company-aware.
///
/// Uses PrimaveraConnectionFactory to resolve connections for the requested
/// company/database target. Every call requires an explicit PrimaveraCompany.
///
/// Source: Primavera table [Artigo] with LEFT JOIN [Familias].
///
/// This service handles article/item-specific queries only.
/// Connection resolution, credential validation, and database targeting
/// are delegated to the shared PrimaveraConnectionFactory.
///
/// Phase 4A scope:
/// - lookup by code
/// - search by code or description fragment
/// - no stock, pricing, supplier-relationship, or sync logic
/// - SubFamilias enrichment deferred
/// - Remarks (Observacoes) deferred
///
/// CDU columns (CDU_codforneced, CDU_codinterno) are environment-specific custom
/// fields that exist only in ALPLAPLASTICO. This service detects their availability
/// at runtime and gracefully omits them when absent.
/// </summary>
public class PrimaveraArticleService : IPrimaveraArticleService
{
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly ILogger<PrimaveraArticleService> _logger;

    private const int MaxSearchResults = 50;
    private const int MinQueryLength = 2;

    public PrimaveraArticleService(
        PrimaveraConnectionFactory connectionFactory,
        ILogger<PrimaveraArticleService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<PrimaveraArticleDto?> GetArticleByCodeAsync(
        PrimaveraCompany company, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmedCode = code.Trim();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);

            var hasCdu = await HasCduColumnsAsync(connection);

            var query = BuildSelectQuery(hasCdu, whereClause: "a.Artigo = @code", orderClause: null, topClause: null);

            await using var cmd = new SqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@code", trimmedCode);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                _logger.LogInformation(
                    "Primavera article not found. Code: {Code}, Company: {Company}",
                    trimmedCode, company);
                return null;
            }

            var dto = MapArticle(reader, company, hasCdu);

            _logger.LogInformation(
                "Primavera article found. Code: {Code}, Description: {Description}, Company: {Company}",
                dto.Code, dto.Description, company);

            return dto;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error during Primavera article lookup. Code: {Code}, Company: {Company}",
                trimmedCode, company);
            throw;
        }
    }

    public async Task<List<PrimaveraArticleDto>> SearchArticlesAsync(
        PrimaveraCompany company, string query, int limit = 50)
    {
        var results = new List<PrimaveraArticleDto>();

        if (string.IsNullOrWhiteSpace(query))
            return results;

        var trimmedQuery = query.Trim();

        if (trimmedQuery.Length < MinQueryLength)
            return results;

        var boundedLimit = Math.Min(Math.Max(limit, 1), MaxSearchResults);

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);

            var hasCdu = await HasCduColumnsAsync(connection);

            var sql = BuildSelectQuery(hasCdu,
                whereClause: "a.Artigo LIKE @pattern OR a.Descricao LIKE @pattern",
                orderClause: "ORDER BY a.Artigo",
                topClause: "TOP (@limit)");

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@pattern", $"%{trimmedQuery}%");
            cmd.Parameters.AddWithValue("@limit", boundedLimit);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(MapArticle(reader, company, hasCdu));
            }

            _logger.LogInformation(
                "Primavera article search completed. Query: {Query}, Company: {Company}, Results: {Count}",
                trimmedQuery, company, results.Count);

            return results;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error during Primavera article search. Query: {Query}, Company: {Company}",
                trimmedQuery, company);
            throw;
        }
    }

    public async Task<(List<PrimaveraArticleDto> Items, int TotalCount)> ListArticlesAsync(
        PrimaveraCompany company, string? search = null, int page = 1, int pageSize = 50)
    {
        var results = new List<PrimaveraArticleDto>();
        var boundedPage = Math.Max(page, 1);
        var boundedPageSize = Math.Min(Math.Max(pageSize, 1), 200);
        var offset = (boundedPage - 1) * boundedPageSize;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);
            var hasCdu = await HasCduColumnsAsync(connection);

            // Build WHERE clause
            var whereConditions = new List<string> { "a.ArtigoAnulado = 0" };
            if (!string.IsNullOrWhiteSpace(search) && search.Trim().Length >= MinQueryLength)
            {
                whereConditions.Add("(a.Artigo LIKE @pattern OR a.Descricao LIKE @pattern)");
            }
            var whereClause = string.Join(" AND ", whereConditions);

            // Count total matching records
            var countSql = $"SELECT COUNT(*) FROM Artigo a LEFT JOIN Familias f ON f.Familia = a.Familia WHERE {whereClause}";
            await using var countCmd = new SqlCommand(countSql, connection);
            if (!string.IsNullOrWhiteSpace(search) && search.Trim().Length >= MinQueryLength)
                countCmd.Parameters.AddWithValue("@pattern", $"%{search.Trim()}%");
            var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            // Fetch page
            var selectSql = BuildSelectQuery(hasCdu, whereClause,
                orderClause: "ORDER BY a.Artigo OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                topClause: null);

            await using var cmd = new SqlCommand(selectSql, connection);
            if (!string.IsNullOrWhiteSpace(search) && search.Trim().Length >= MinQueryLength)
                cmd.Parameters.AddWithValue("@pattern", $"%{search.Trim()}%");
            cmd.Parameters.AddWithValue("@offset", offset);
            cmd.Parameters.AddWithValue("@pageSize", boundedPageSize);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(MapArticle(reader, company, hasCdu));
            }

            _logger.LogInformation(
                "Primavera article list completed. Company: {Company}, Search: {Search}, Page: {Page}, PageSize: {PageSize}, Total: {Total}, Returned: {Count}",
                company, search ?? "(all)", boundedPage, boundedPageSize, totalCount, results.Count);

            return (results, totalCount);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error during Primavera article list. Company: {Company}, Search: {Search}",
                company, search);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<PrimaveraArticleDto>> ListAllArticlesAsync(
        PrimaveraCompany company, string? search = null)
    {
        const int batchSize = 200;
        var allItems = new List<PrimaveraArticleDto>();
        int page = 1;
        int totalCount;

        do
        {
            var (items, total) = await ListArticlesAsync(company, search, page, batchSize);
            totalCount = total;
            allItems.AddRange(items);
            page++;
        } while (allItems.Count < totalCount);

        _logger.LogInformation(
            "Primavera article full fetch completed. Company: {Company}, Search: {Search}, Total: {Total}",
            company, search ?? "(all)", allItems.Count);

        return allItems;
    }

    // ─── SQL builder ───

    /// <summary>
    /// Builds the SELECT statement, conditionally including CDU custom columns
    /// only when they exist in the target database.
    /// </summary>
    private static string BuildSelectQuery(bool hasCdu, string whereClause, string? orderClause, string? topClause)
    {
        var top = topClause != null ? $" {topClause}" : "";
        var cduColumns = hasCdu
            ? @",
                    a.CDU_codforneced AS SupplierCode,
                    a.CDU_codinterno  AS InternalCode"
            : "";

        return $@"
            SELECT{top}
                a.Artigo          AS Code,
                a.Descricao       AS Description,
                a.UnidadeBase     AS BaseUnit,
                a.UnidadeCompra   AS PurchaseUnit,
                a.Familia         AS Family,
                f.Descricao       AS FamilyDescription,
                a.SubFamilia      AS SubFamily,
                a.TipoArtigo      AS ArticleType,
                a.Marca           AS Brand,
                a.ArtigoAnulado   AS IsCancelled{cduColumns}
            FROM Artigo a
            LEFT JOIN Familias f ON f.Familia = a.Familia
            WHERE {whereClause}
            {orderClause ?? ""}";
    }

    // ─── CDU column detection ───

    /// <summary>
    /// Checks whether the CDU_codforneced column exists in the Artigo table.
    /// These are environment-specific custom fields (not standard Primavera).
    /// Result indicates both CDU_codforneced and CDU_codinterno availability.
    /// </summary>
    private static async Task<bool> HasCduColumnsAsync(SqlConnection connection)
    {
        const string checkSql = @"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'Artigo' AND COLUMN_NAME = 'CDU_codforneced'";

        await using var cmd = new SqlCommand(checkSql, connection);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    // ─── Mapping helper ───

    private static PrimaveraArticleDto MapArticle(SqlDataReader reader, PrimaveraCompany company, bool hasCdu)
    {
        return new PrimaveraArticleDto
        {
            Code = reader["Code"]?.ToString() ?? string.Empty,
            Description = reader["Description"] is DBNull ? null : reader["Description"]?.ToString(),
            BaseUnit = reader["BaseUnit"] is DBNull ? null : reader["BaseUnit"]?.ToString(),
            PurchaseUnit = reader["PurchaseUnit"] is DBNull ? null : reader["PurchaseUnit"]?.ToString(),
            Family = reader["Family"] is DBNull ? null : reader["Family"]?.ToString(),
            FamilyDescription = reader["FamilyDescription"] is DBNull ? null : reader["FamilyDescription"]?.ToString(),
            SubFamily = reader["SubFamily"] is DBNull ? null : reader["SubFamily"]?.ToString(),
            ArticleType = reader["ArticleType"] is DBNull ? null : Convert.ToInt32(reader["ArticleType"]),
            Brand = reader["Brand"] is DBNull ? null : reader["Brand"]?.ToString(),
            IsCancelled = reader["IsCancelled"] is not DBNull && Convert.ToBoolean(reader["IsCancelled"]),
            SupplierCode = hasCdu && reader["SupplierCode"] is not DBNull ? reader["SupplierCode"]?.ToString() : null,
            InternalCode = hasCdu && reader["InternalCode"] is not DBNull ? reader["InternalCode"]?.ToString() : null,
            SourceCompany = company.ToString(),
            Source = "PRIMAVERA"
        };
    }
}
