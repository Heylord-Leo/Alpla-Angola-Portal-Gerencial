using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Primavera supplier master-data lookup — read-only, company-aware.
///
/// Uses PrimaveraConnectionFactory to resolve connections for the requested
/// company/database target. Every call requires an explicit PrimaveraCompany.
///
/// Source: Primavera table [Fornecedores] (116 columns; ~15 exposed in DTO).
///
/// This service handles supplier-specific queries only.
/// Connection resolution, credential validation, and database targeting
/// are delegated to the shared PrimaveraConnectionFactory.
///
/// Phase 4B scope:
/// - lookup by supplier code
/// - search by name or code fragment
/// - no financial, banking, or transactional queries
/// - no supplier-article relationship logic
/// - no sync or writeback
///
/// Unlike the Artigo table, the Fornecedores table has consistent columns
/// across ALPLAPLASTICO and ALPLASOPRO (both 116 columns, same CDU set).
/// No adaptive column detection is needed for this table.
/// </summary>
public class PrimaveraSupplierService : IPrimaveraSupplierService
{
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly ILogger<PrimaveraSupplierService> _logger;

    private const int MaxSearchResults = 50;
    private const int MinQueryLength = 2;

    private const string SelectColumns = @"
        Fornecedor      AS Code,
        Nome            AS Name,
        NomeFiscal      AS FiscalName,
        NumContrib      AS TaxId,
        Email           AS Email,
        Tel             AS Phone,
        Fax             AS Fax,
        Morada          AS Address,
        Morada1         AS Address2,
        Local           AS City,
        Cp              AS PostalCode,
        Pais            AS Country,
        TipoFor         AS SupplierType,
        FornecedorAnulado AS IsCancelled,
        Moeda           AS Currency,
        DataCriacao     AS CreatedAt";

    public PrimaveraSupplierService(
        PrimaveraConnectionFactory connectionFactory,
        ILogger<PrimaveraSupplierService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<PrimaveraSupplierDto?> GetSupplierByCodeAsync(
        PrimaveraCompany company, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmedCode = code.Trim();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);

            var query = $@"
                SELECT
                    {SelectColumns}
                FROM Fornecedores
                WHERE Fornecedor = @code";

            await using var cmd = new SqlCommand(query, connection);
            cmd.Parameters.AddWithValue("@code", trimmedCode);

            await using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                _logger.LogInformation(
                    "Primavera supplier not found. Code: {Code}, Company: {Company}",
                    trimmedCode, company);
                return null;
            }

            var dto = MapSupplier(reader, company);

            _logger.LogInformation(
                "Primavera supplier found. Code: {Code}, Name: {Name}, Company: {Company}",
                dto.Code, dto.Name, company);

            return dto;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error during Primavera supplier lookup. Code: {Code}, Company: {Company}",
                trimmedCode, company);
            throw;
        }
    }

    public async Task<List<PrimaveraSupplierDto>> SearchSuppliersAsync(
        PrimaveraCompany company, string query, int limit = 50)
    {
        var results = new List<PrimaveraSupplierDto>();

        if (string.IsNullOrWhiteSpace(query))
            return results;

        var trimmedQuery = query.Trim();

        if (trimmedQuery.Length < MinQueryLength)
            return results;

        var boundedLimit = Math.Min(Math.Max(limit, 1), MaxSearchResults);

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);

            var sql = $@"
                SELECT TOP (@limit)
                    {SelectColumns}
                FROM Fornecedores
                WHERE Fornecedor LIKE @pattern
                   OR Nome LIKE @pattern
                   OR NomeFiscal LIKE @pattern
                   OR NumContrib LIKE @pattern
                ORDER BY Fornecedor";

            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@pattern", $"%{trimmedQuery}%");
            cmd.Parameters.AddWithValue("@limit", boundedLimit);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(MapSupplier(reader, company));
            }

            _logger.LogInformation(
                "Primavera supplier search completed. Query: {Query}, Company: {Company}, Results: {Count}",
                trimmedQuery, company, results.Count);

            return results;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error during Primavera supplier search. Query: {Query}, Company: {Company}",
                trimmedQuery, company);
            throw;
        }
    }

    public async Task<(List<PrimaveraSupplierDto> Items, int TotalCount)> ListSuppliersAsync(
        PrimaveraCompany company, string? search = null, int page = 1, int pageSize = 50)
    {
        var results = new List<PrimaveraSupplierDto>();
        var boundedPage = Math.Max(page, 1);
        var boundedPageSize = Math.Min(Math.Max(pageSize, 1), 200);
        var offset = (boundedPage - 1) * boundedPageSize;

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);

            // Build WHERE clause
            var whereConditions = new List<string> { "FornecedorAnulado = 0" };
            if (!string.IsNullOrWhiteSpace(search) && search.Trim().Length >= MinQueryLength)
            {
                whereConditions.Add("(Fornecedor LIKE @pattern OR Nome LIKE @pattern OR NomeFiscal LIKE @pattern OR NumContrib LIKE @pattern)");
            }
            var whereClause = string.Join(" AND ", whereConditions);

            // Count total matching records
            var countSql = $"SELECT COUNT(*) FROM Fornecedores WHERE {whereClause}";
            await using var countCmd = new SqlCommand(countSql, connection);
            if (!string.IsNullOrWhiteSpace(search) && search.Trim().Length >= MinQueryLength)
                countCmd.Parameters.AddWithValue("@pattern", $"%{search.Trim()}%");
            var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            // Fetch page
            var sql = $@"
                SELECT
                    {SelectColumns}
                FROM Fornecedores
                WHERE {whereClause}
                ORDER BY Fornecedor
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            await using var cmd = new SqlCommand(sql, connection);
            if (!string.IsNullOrWhiteSpace(search) && search.Trim().Length >= MinQueryLength)
                cmd.Parameters.AddWithValue("@pattern", $"%{search.Trim()}%");
            cmd.Parameters.AddWithValue("@offset", offset);
            cmd.Parameters.AddWithValue("@pageSize", boundedPageSize);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(MapSupplier(reader, company));
            }

            _logger.LogInformation(
                "Primavera supplier list completed. Company: {Company}, Search: {Search}, Page: {Page}, PageSize: {PageSize}, Total: {Total}, Returned: {Count}",
                company, search ?? "(all)", boundedPage, boundedPageSize, totalCount, results.Count);

            return (results, totalCount);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error during Primavera supplier list. Company: {Company}, Search: {Search}",
                company, search);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<PrimaveraSupplierDto>> ListAllSuppliersAsync(
        PrimaveraCompany company, string? search = null)
    {
        const int batchSize = 200;
        var allItems = new List<PrimaveraSupplierDto>();
        int page = 1;
        int totalCount;

        do
        {
            var (items, total) = await ListSuppliersAsync(company, search, page, batchSize);
            totalCount = total;
            allItems.AddRange(items);
            page++;
        } while (allItems.Count < totalCount);

        _logger.LogInformation(
            "Primavera supplier full fetch completed. Company: {Company}, Search: {Search}, Total: {Total}",
            company, search ?? "(all)", allItems.Count);

        return allItems;
    }

    // ─── Mapping helper ───

    private static PrimaveraSupplierDto MapSupplier(SqlDataReader reader, PrimaveraCompany company)
    {
        return new PrimaveraSupplierDto
        {
            Code = reader["Code"]?.ToString() ?? string.Empty,
            Name = reader["Name"] is DBNull ? null : reader["Name"]?.ToString(),
            FiscalName = reader["FiscalName"] is DBNull ? null : reader["FiscalName"]?.ToString(),
            TaxId = reader["TaxId"] is DBNull ? null : reader["TaxId"]?.ToString(),
            Email = reader["Email"] is DBNull ? null : reader["Email"]?.ToString(),
            Phone = reader["Phone"] is DBNull ? null : reader["Phone"]?.ToString(),
            Fax = reader["Fax"] is DBNull ? null : reader["Fax"]?.ToString(),
            Address = reader["Address"] is DBNull ? null : reader["Address"]?.ToString(),
            Address2 = reader["Address2"] is DBNull ? null : reader["Address2"]?.ToString(),
            City = reader["City"] is DBNull ? null : reader["City"]?.ToString(),
            PostalCode = reader["PostalCode"] is DBNull ? null : reader["PostalCode"]?.ToString(),
            Country = reader["Country"] is DBNull ? null : reader["Country"]?.ToString(),
            SupplierType = reader["SupplierType"] is DBNull ? null : reader["SupplierType"]?.ToString(),
            IsCancelled = reader["IsCancelled"] is not DBNull && Convert.ToBoolean(reader["IsCancelled"]),
            Currency = reader["Currency"] is DBNull ? null : reader["Currency"]?.ToString(),
            CreatedAt = reader["CreatedAt"] is DBNull ? null : Convert.ToDateTime(reader["CreatedAt"]),
            SourceCompany = company.ToString(),
            Source = "PRIMAVERA"
        };
    }
}
