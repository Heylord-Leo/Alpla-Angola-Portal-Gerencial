using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace AlplaPortal.Application.Tests.Services.Dashboard;

/// <summary>
/// B7 low-risk perf hotfix — one FinancialSummaryProjection build must execute the canonical finance
/// obligation projection ONLY ONCE (both Em processamento and Pago derive from the single materialized
/// result). FinanceObligationSummaryProjection is constructed with `new` (no injected seam to spy on), so
/// this is a focused structural guard over the source — deliberately narrow, per the task's guidance to
/// avoid a disproportionate abstraction. The values-unchanged proof lives in the B7 behaviour/integration
/// tests.
/// </summary>
public class FinancialSummaryReuseTests
{
    private static string Source([CallerFilePath] string thisFile = "")
    {
        // Compile-time path of THIS test file, so it works regardless of runtime cwd (build output may be
        // redirected out of the repo tree). Walk up to the repo root (the folder containing src/backend).
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "backend")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "backend",
            "AlplaPortal.Infrastructure", "Services", "Dashboard", "FinancialSummaryProjection.cs"));
    }

    [Fact]
    public void FinanceObligationProjection_is_constructed_exactly_once_per_build()
    {
        var src = Source();
        var count = Regex.Matches(src, @"new FinanceObligationSummaryProjection\(").Count;
        Assert.Equal(1, count);
    }

    [Fact]
    public void The_two_finance_categories_derive_from_a_shared_obligations_result()
    {
        var src = Source();
        // Both derivations take the pre-materialized obligations, not a fresh sweep.
        Assert.Matches(@"BuildFinanceProcessing\(financeObligations\)", src);
        Assert.Matches(@"BuildPaidAsync\(financeObligations\)", src);
        // The old per-category duplicate-sweep helper is gone.
        Assert.DoesNotContain("BuildFinanceProcessingAndPaidAsync", src);
    }
}
