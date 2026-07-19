using System.Collections.Generic;
using AlplaPortal.Application.Validation;
using AlplaPortal.Domain.Entities;
using Xunit;

namespace AlplaPortal.Application.Tests.Validation;

/// <summary>
/// Financial Integrity Gate math (SaveQuotation). The OCR baseline covers the WHOLE document;
/// lines explicitly reconciled as IGNORED are subtracted from it so both sides of the comparison
/// share the same scope. Real divergences on considered lines must still trip the gate.
/// </summary>
public class QuotationIntegrityCalculatorTests
{
    private static QuotationItem Item(string status, decimal gross, decimal discount = 0m, decimal iva = 0m, decimal? lineTotal = null) => new()
    {
        Description = "x",
        ReconciliationStatus = status,
        GrossSubtotal = gross,
        DiscountAmount = discount,
        IvaAmount = iva,
        LineTotal = lineTotal ?? (gross - discount + iva)
    };

    private static decimal NetOf(IEnumerable<QuotationItem> items)
    {
        decimal net = 0;
        foreach (var i in items) net += i.GrossSubtotal - i.DiscountAmount;
        return net;
    }

    [Fact] // Cenários 1–8: caso reproduzido — linha ignorada deixa de ser falsa divergência
    public void IgnoredLine_IsExcludedFromBaseline_VarianceZero()
    {
        var items = new List<QuotationItem>
        {
            Item("MAPPED", gross: 49875m),
            Item("IGNORED", gross: 36936m)
        };

        var r = QuotationIntegrityCalculator.Compute(
            ocrBaseline: 86811m, items, globalDiscount: 0m, netAfterItemDiscounts: NetOf(items));

        Assert.Equal(86811m, r.OcrOriginalTotal);
        Assert.Equal(36936m, r.ExcludedIgnoredTotal);
        Assert.Equal(49875m, r.ComparableDocumentTotal);   // 86.811 − 36.936
        Assert.Equal(49875m, r.QuotationConsideredTotal);
        Assert.Equal(0m, r.VarianceAmount);                 // salvamento permitido sem justificativa genérica
        Assert.True(r.VarianceAmount <= QuotationIntegrityCalculator.ToleranceAmount);
    }

    [Fact] // Cenários 9–10: linha ignorada + divergência REAL nas linhas consideradas → gate ainda bloqueia
    public void IgnoredLine_DoesNotMask_RealDivergenceOnConsideredLines()
    {
        var items = new List<QuotationItem>
        {
            Item("MAPPED", gross: 48000m),   // documento dizia 49.875 para esta linha
            Item("IGNORED", gross: 36936m)
        };

        var r = QuotationIntegrityCalculator.Compute(86811m, items, 0m, NetOf(items));

        Assert.Equal(49875m, r.ComparableDocumentTotal);
        Assert.Equal(48000m, r.QuotationConsideredTotal);
        Assert.Equal(1875m, r.VarianceAmount);              // divergência real preservada
        Assert.True(r.VarianceAmount > QuotationIntegrityCalculator.ToleranceAmount);
        Assert.Equal(3.76m, r.VariancePercent);             // sobre o comparável (1875/49875)
    }

    [Fact] // Cenários 11–12: duas linhas ignoradas → excludedIgnoredTotal é a soma correta
    public void TwoIgnoredLines_ExcludedTotal_IsTheSum()
    {
        var items = new List<QuotationItem>
        {
            Item("MAPPED", gross: 40000m),
            Item("IGNORED", gross: 36936m),
            Item("IGNORED", gross: 1000m)
        };

        var r = QuotationIntegrityCalculator.Compute(77936m, items, 0m, NetOf(items));

        Assert.Equal(37936m, r.ExcludedIgnoredTotal);
        Assert.Equal(40000m, r.ComparableDocumentTotal);
        Assert.Equal(0m, r.VarianceAmount);
    }

    [Fact] // Cenários 13–14: linha ignorada de valor zero não altera o total comparável
    public void ZeroValueIgnoredLine_DoesNotChangeComparableTotal()
    {
        var items = new List<QuotationItem>
        {
            Item("MAPPED", gross: 49875m),
            Item("IGNORED", gross: 0m)
        };

        var r = QuotationIntegrityCalculator.Compute(49875m, items, 0m, NetOf(items));

        Assert.Equal(0m, r.ExcludedIgnoredTotal);
        Assert.Equal(49875m, r.ComparableDocumentTotal);
        Assert.Equal(0m, r.VarianceAmount);
    }

    [Fact] // Cenários 15–16: desconto (item + global) e IVA — tolerância opera sobre o comparável
    public void DiscountIvaAndRounding_ToleranceAppliesOnComparableTotal()
    {
        // MAPPED: 50.000 − 1.000 desc = 49.000 + IVA 14% (6.860) = 55.860
        // IGNORED (documento): 10.000 → baseline 65.861,50 (documento com 1,50 de arredondamento)
        var items = new List<QuotationItem>
        {
            Item("MAPPED", gross: 50000m, discount: 1000m, iva: 6860m),
            Item("IGNORED", gross: 10000m, lineTotal: 10000m)
        };

        var r = QuotationIntegrityCalculator.Compute(
            ocrBaseline: 65861.50m, items, globalDiscount: 0m, netAfterItemDiscounts: NetOf(items));

        Assert.Equal(10000m, r.ExcludedIgnoredTotal);
        Assert.Equal(55861.50m, r.ComparableDocumentTotal);
        Assert.Equal(55860m, r.QuotationConsideredTotal);   // desconto + IVA aplicados
        Assert.Equal(1.50m, r.VarianceAmount);              // dentro da tolerância de 2,00
        Assert.True(r.VarianceAmount <= QuotationIntegrityCalculator.ToleranceAmount);
    }

    [Fact] // NOT_QUOTED não é linha do documento: não entra em nenhum dos lados
    public void NotQuotedEntries_AffectNeitherSide()
    {
        var items = new List<QuotationItem>
        {
            Item("MAPPED", gross: 49875m),
            Item("NOT_QUOTED", gross: 0m),
            Item("IGNORED", gross: 36936m)
        };

        var r = QuotationIntegrityCalculator.Compute(86811m, items, 0m, NetOf(items));

        Assert.Equal(36936m, r.ExcludedIgnoredTotal);       // só a IGNORED sai do baseline
        Assert.Equal(0m, r.VarianceAmount);
    }

    [Fact] // Desconto global é rateado apenas sobre as linhas consideradas (comportamento preservado)
    public void GlobalDiscount_IsProportionallyAppliedToConsideredLines()
    {
        var items = new List<QuotationItem>
        {
            Item("MAPPED", gross: 50000m),
            Item("IGNORED", gross: 50000m)
        };
        // net de TODAS as linhas = 100.000; considered net = 50.000 → metade do desconto global (1.000) = 500
        var r = QuotationIntegrityCalculator.Compute(
            ocrBaseline: 99500m, items, globalDiscount: 1000m, netAfterItemDiscounts: 100000m);

        Assert.Equal(49500m, r.QuotationConsideredTotal);   // 50.000 − 500
        Assert.Equal(49500m, r.ComparableDocumentTotal);    // 99.500 − 50.000
        Assert.Equal(0m, r.VarianceAmount);
    }
}
