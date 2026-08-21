using System;
using AlplaPortal.Api.Helpers;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Requests;

/// <summary>
/// Query-TRANSLATION regression pins for the candidate search, compiled with the REAL SQL Server
/// provider via <c>ToQueryString()</c> — which forces the full LINQ-to-SQL pipeline without
/// touching a database.
///
/// <para>The DEV regression this pins: the candidate search composed a <c>Where</c> on top of a
/// parameterized-record projection (legal only as the final operator), and every InMemory-based
/// test passed anyway, because the InMemory provider evaluates LINQ client-side and never
/// translates anything. <c>ToQueryString()</c> throws the exact "could not be translated"
/// exception the user saw, so an untranslatable shape can never again reach DEV silently.</para>
/// </summary>
public class CandidateSearchTranslationTests
{
    /// <summary>SQL Server provider services only — ToQueryString never opens a connection.</summary>
    private static ApplicationDbContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(unused);Database=unused;Integrated Security=true;TrustServerCertificate=true")
            .Options);

    [Fact]
    public void The_preflight_shape_translates_to_sql_server()
    {
        using var ctx = SqlServerContext();

        // The wizard shape: no current request, nothing to exclude.
        var sql = PaymentSourceDocumentCandidateSearch.BuildCandidateRowsQuery(ctx,
            new CandidateSearchInput { DocumentNumber = "ONP_18910_v3" }).ToQueryString();

        Assert.Contains("PaymentSourceDocuments", sql);
        Assert.Contains("IsVoided", sql);
    }

    [Fact]
    public void The_persistence_guard_shape_translates_to_sql_server()
    {
        using var ctx = SqlServerContext();

        // The Create/Update shape: current request set, the edited document excluded — the exact
        // query "GERAR PEDIDO" executes.
        var sql = PaymentSourceDocumentCandidateSearch.BuildCandidateRowsQuery(ctx,
            new CandidateSearchInput
            {
                CurrentRequestId = Guid.NewGuid(),
                ExcludeDocumentId = Guid.NewGuid(),
                DocumentNumber = "ONP_18910_v3",
                DocumentSeries = "A"
            }).ToQueryString();

        Assert.Contains("PaymentSourceDocuments", sql);
        Assert.Contains("Requests", sql);          // the live-request status filter joined in SQL
    }
}
