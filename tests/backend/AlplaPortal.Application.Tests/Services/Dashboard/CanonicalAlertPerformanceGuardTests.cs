using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B8.1 performance guard — the alert projection must NOT reintroduce a heavy sweep. It must not
/// instantiate FinanceObligationSummaryProjection, ReceivingQueueProjection or OperationalPipelineProjection,
/// and its Buyer query must be bounded by the near-deadline NeedBy cutoff (not the full Buyer population).
/// Structural guard (no injectable seam to spy on); narrow by design.
/// </summary>
public class CanonicalAlertPerformanceGuardTests
{
    private static string Source([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "backend")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "backend",
            "AlplaPortal.Infrastructure", "Services", "Dashboard", "CanonicalAlertProjection.cs"));
    }

    [Fact]
    public void Does_not_instantiate_the_heavy_projections()
    {
        var src = Source();
        Assert.DoesNotContain("new FinanceObligationSummaryProjection", src);
        Assert.DoesNotContain("new ReceivingQueueProjection", src);
        Assert.DoesNotContain("new OperationalPipelineProjection", src);
    }

    [Fact]
    public void Reuses_the_canonical_buyer_builder_over_a_bounded_near_deadline_candidate_query()
    {
        var src = Source();
        // Canonical actionability (no duplicated predicate copy).
        Assert.Matches(@"Proj\.Build\(BuyerQueueProjectionInputFactory\.FromRequest", src);
        Assert.Matches(@"NextBuyerActions\.Any\(a => a\.Actionable\)", src);
        // Bounded candidate query: NeedByDateUtc cutoff before hydrating.
        Assert.Matches(@"r\.NeedByDateUtc < candidateCutoff", src);
    }

    [Fact]
    public void Finance_alerts_use_one_flat_scheduled_payments_query_not_the_finance_projection()
    {
        var src = Source();
        Assert.Matches(@"from p in _context\.RequestPayments", src);
        Assert.Matches(@"p\.PaymentStatus == RequestPayment\.PaymentStatuses\.Scheduled", src);
        Assert.DoesNotContain(".Include(", GetFinanceRegion(src)); // no Include graph in the finance query
    }

    private static string GetFinanceRegion(string src)
    {
        // Anchor on the METHOD DEFINITION (not the earlier call site) so the Buyer method's Includes
        // are not swept into this region.
        var start = src.IndexOf("private async Task<List<DashboardV2AlertDto>> BuildFinanceAlertsAsync");
        return start < 0 ? src : src.Substring(start);
    }
}
