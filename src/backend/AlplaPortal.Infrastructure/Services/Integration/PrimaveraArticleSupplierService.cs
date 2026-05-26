using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Primavera article-supplier relationship service — read-only, company-aware.
///
/// Uses PrimaveraConnectionFactory to resolve connections for the requested
/// company/database target. Every call requires an explicit PrimaveraCompany.
///
/// Source: Primavera table [ArtigoFornecedor] (25 columns; ~8 relationship fields exposed).
///   - Join key to articles:  ArtigoFornecedor.Artigo → Artigo.Artigo
///   - Join key to suppliers: ArtigoFornecedor.Fornecedor → Fornecedores.Fornecedor
///
/// Both ALPLAPLASTICO and ALPLASOPRO have identical ArtigoFornecedor schemas
/// (25 columns including CDU_CodBarrasEntidade). No adaptive detection needed.
///
/// Phase 4C scope:
/// - article → suppliers enriched with Fornecedores identity
/// - supplier → articles enriched with Artigo identity
/// - no pricing, discount, ranking, or sourcing logic
/// - no sync or writeback
/// </summary>
public class PrimaveraArticleSupplierService : IPrimaveraArticleSupplierService
{
    private readonly PrimaveraConnectionFactory _connectionFactory;
    private readonly ILogger<PrimaveraArticleSupplierService> _logger;

    private const int MaxRelationshipResults = 100;

    public PrimaveraArticleSupplierService(
        PrimaveraConnectionFactory connectionFactory,
        ILogger<PrimaveraArticleSupplierService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<PrimaveraArticleSuppliersDto?> GetSuppliersForArticleAsync(
        PrimaveraCompany company, string articleCode)
    {
        if (string.IsNullOrWhiteSpace(articleCode))
            return null;

        var trimmedCode = articleCode.Trim();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);

            // 1. Verify article exists and get description
            string? articleDescription;
            {
                await using var cmd = new SqlCommand(
                    "SELECT Descricao FROM Artigo WHERE Artigo = @code", connection);
                cmd.Parameters.AddWithValue("@code", trimmedCode);
                var result = await cmd.ExecuteScalarAsync();

                if (result == null)
                {
                    _logger.LogInformation(
                        "Article not found for supplier linkage. Code: {Code}, Company: {Company}",
                        trimmedCode, company);
                    return null;
                }

                articleDescription = result is DBNull ? null : result.ToString();
            }

            // 2. Query relationships with supplier enrichment
            var suppliers = new List<ArticleSupplierItemDto>();
            {
                const string sql = @"
                    SELECT TOP (@limit)
                        af.Artigo,
                        af.Fornecedor,
                        af.DescricaoFor,
                        af.ReferenciaFor,
                        af.UnidadeCompra,
                        af.PrazoEntrega,
                        af.Moeda,
                        af.DatUltEntrada,
                        f.Nome           AS SupplierName,
                        f.NumContrib     AS SupplierTaxId
                    FROM ArtigoFornecedor af
                    LEFT JOIN Fornecedores f ON af.Fornecedor = f.Fornecedor
                    WHERE af.Artigo = @code
                    ORDER BY af.Fornecedor";

                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@code", trimmedCode);
                cmd.Parameters.AddWithValue("@limit", MaxRelationshipResults);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    suppliers.Add(new ArticleSupplierItemDto
                    {
                        SupplierCode = reader["Fornecedor"]?.ToString() ?? string.Empty,
                        SupplierName = reader["SupplierName"] is DBNull ? null : reader["SupplierName"]?.ToString(),
                        SupplierTaxId = reader["SupplierTaxId"] is DBNull ? null : reader["SupplierTaxId"]?.ToString(),
                        SupplierDescription = reader["DescricaoFor"] is DBNull ? null : reader["DescricaoFor"]?.ToString(),
                        SupplierReference = reader["ReferenciaFor"] is DBNull ? null : reader["ReferenciaFor"]?.ToString(),
                        PurchaseUnit = reader["UnidadeCompra"] is DBNull ? null : reader["UnidadeCompra"]?.ToString(),
                        LeadTimeDays = reader["PrazoEntrega"] is DBNull ? null : Convert.ToInt16(reader["PrazoEntrega"]),
                        Currency = reader["Moeda"] is DBNull ? null : reader["Moeda"]?.ToString(),
                        LastEntryDate = reader["DatUltEntrada"] is DBNull ? null : Convert.ToDateTime(reader["DatUltEntrada"])
                    });
                }
            }

            _logger.LogInformation(
                "Article-supplier linkage completed. Article: {Code}, Company: {Company}, Suppliers: {Count}",
                trimmedCode, company, suppliers.Count);

            return new PrimaveraArticleSuppliersDto
            {
                ArticleCode = trimmedCode,
                ArticleDescription = articleDescription,
                SupplierCount = suppliers.Count,
                SourceCompany = company.ToString(),
                Source = "PRIMAVERA",
                Suppliers = suppliers
            };
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error during article-supplier linkage. Article: {Code}, Company: {Company}",
                trimmedCode, company);
            throw;
        }
    }

    public async Task<PrimaveraSupplierArticlesDto?> GetArticlesForSupplierAsync(
        PrimaveraCompany company, string supplierCode)
    {
        if (string.IsNullOrWhiteSpace(supplierCode))
            return null;

        var trimmedCode = supplierCode.Trim();

        try
        {
            await using var connection = await _connectionFactory.CreateConnectionAsync(company);

            // 1. Verify supplier exists and get name
            string? supplierName;
            {
                await using var cmd = new SqlCommand(
                    "SELECT Nome FROM Fornecedores WHERE Fornecedor = @code", connection);
                cmd.Parameters.AddWithValue("@code", trimmedCode);
                var result = await cmd.ExecuteScalarAsync();

                if (result == null)
                {
                    _logger.LogInformation(
                        "Supplier not found for article linkage. Code: {Code}, Company: {Company}",
                        trimmedCode, company);
                    return null;
                }

                supplierName = result is DBNull ? null : result.ToString();
            }

            // 2. Query relationships with article enrichment
            var articles = new List<SupplierArticleItemDto>();
            {
                const string sql = @"
                    SELECT TOP (@limit)
                        af.Artigo,
                        af.Fornecedor,
                        af.DescricaoFor,
                        af.ReferenciaFor,
                        af.UnidadeCompra,
                        af.PrazoEntrega,
                        af.Moeda,
                        af.DatUltEntrada,
                        a.Descricao      AS ArticleDescription,
                        a.UnidadeBase    AS BaseUnit
                    FROM ArtigoFornecedor af
                    LEFT JOIN Artigo a ON af.Artigo = a.Artigo
                    WHERE af.Fornecedor = @code
                    ORDER BY af.Artigo";

                await using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@code", trimmedCode);
                cmd.Parameters.AddWithValue("@limit", MaxRelationshipResults);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    articles.Add(new SupplierArticleItemDto
                    {
                        ArticleCode = reader["Artigo"]?.ToString() ?? string.Empty,
                        ArticleDescription = reader["ArticleDescription"] is DBNull ? null : reader["ArticleDescription"]?.ToString(),
                        BaseUnit = reader["BaseUnit"] is DBNull ? null : reader["BaseUnit"]?.ToString(),
                        SupplierDescription = reader["DescricaoFor"] is DBNull ? null : reader["DescricaoFor"]?.ToString(),
                        SupplierReference = reader["ReferenciaFor"] is DBNull ? null : reader["ReferenciaFor"]?.ToString(),
                        PurchaseUnit = reader["UnidadeCompra"] is DBNull ? null : reader["UnidadeCompra"]?.ToString(),
                        LeadTimeDays = reader["PrazoEntrega"] is DBNull ? null : Convert.ToInt16(reader["PrazoEntrega"]),
                        Currency = reader["Moeda"] is DBNull ? null : reader["Moeda"]?.ToString(),
                        LastEntryDate = reader["DatUltEntrada"] is DBNull ? null : Convert.ToDateTime(reader["DatUltEntrada"])
                    });
                }
            }

            _logger.LogInformation(
                "Supplier-article linkage completed. Supplier: {Code}, Company: {Company}, Articles: {Count}",
                trimmedCode, company, articles.Count);

            return new PrimaveraSupplierArticlesDto
            {
                SupplierCode = trimmedCode,
                SupplierName = supplierName,
                ArticleCount = articles.Count,
                SourceCompany = company.ToString(),
                Source = "PRIMAVERA",
                Articles = articles
            };
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error during supplier-article linkage. Supplier: {Code}, Company: {Company}",
                trimmedCode, company);
            throw;
        }
    }
}
