using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Dashboard;

// ── B7: Dashboard V2 canonical Financial Summary (GERENCIAL, read-only, currency-safe). ──
// CURRENCY SAFETY (PD-B7-01): amounts are NEVER summed across currencies — every category carries one
// row per currency, with an explicit UNKNOWN bucket, and no FX conversion anywhere. Amounts are the
// available all-in total at each domain grain (quotation totals include IVA — PD-B7-13); this is monetary
// exposure, not actionability (that stays in B3). No paid-history (B7.3), no completed card (PD-B7-09).
// Help/label prose beyond the stable Label lives in the frontend.

public static class FinancialCategories
{
    public const string EmAprovacao = "EM_APROVACAO";
    public const string AguardandoPo = "AGUARDANDO_PO";
    public const string EmProcessamentoFinanceiro = "EM_PROCESSAMENTO_FINANCEIRO";
    public const string PagoAguardandoRecebimento = "PAGO_AGUARDANDO_RECEBIMENTO";
}

public static class FinancialEntityTypes
{
    public const string ApprovalBatch = "APPROVAL_BATCH";
    public const string PoGroup = "PO_GROUP";
}

/// <summary>Stable code for an unresolved/null currency. Frontend labels it "Moeda não identificada".</summary>
public static class FinancialCurrency
{
    public const string Unknown = "UNKNOWN";
}

/// <summary>One currency's aggregate within a category. <see cref="Amount"/> is a per-currency sum only —
/// it is never combined with any other currency. decimal, never floating point.</summary>
public sealed class CurrencyAmountDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int EntityCount { get; set; }   // entities of this category's grain contributing in this currency
    public int RequestCount { get; set; }  // distinct requests contributing in this currency
}

/// <summary>
/// One financial exposure category. <see cref="EntityCount"/>/<see cref="RequestCount"/> cover the whole
/// category population (its grain); <see cref="Currencies"/> carries the per-currency monetary rows.
/// <see cref="IsAuthoritative"/> is false when some entities in the population have no authoritative amount
/// (e.g. an approval batch with no decided winner snapshot) — those are counted but never given a fabricated
/// value (PD-B7-05/07).
/// </summary>
public sealed class FinancialCategoryDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityCount { get; set; }
    public int RequestCount { get; set; }
    public List<CurrencyAmountDto> Currencies { get; set; } = new();
    public bool IsAuthoritative { get; set; } = true;
}

public static class FinancialPeriods
{
    public const string Last30Days = "LAST_30_DAYS";
}

/// <summary>
/// B7.3 — secondary paid-history summary: confirmed payment evidence within a period, partitioned by
/// payment currency (never combined). Source = COMPLETED owed-money <c>RequestPayment</c> rows
/// (ADVANCE / FINAL_BALANCE / REGULARIZATION) with <c>PaidDateUtc</c> in [<see cref="FromUtc"/>,
/// <see cref="ToUtc"/>); amount = <c>ActualPaidAmount</c>. Refunds are NOT netted (PD-B7-12). This is
/// payment evidence, not accounting reconciliation.
/// </summary>
public sealed class PaidHistoryDto
{
    public string PeriodCode { get; set; } = FinancialPeriods.Last30Days;
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; } // half-open: PaidDateUtc >= FromUtc && < ToUtc
    /// <summary>One row per currency (EntityCount = payments in that currency). Never a combined total.</summary>
    public List<CurrencyAmountDto> Currencies { get; set; } = new();
    public int PaymentCount { get; set; }
    public int RequestCount { get; set; }
    /// <summary>False when some completed payments have a null ActualPaidAmount (counted, never fabricated).</summary>
    public bool IsAuthoritative { get; set; } = true;
}

public sealed class DashboardV2FinancialDto
{
    /// <summary>Null when the caller is not entitled (Finance / Local Manager / SysAdmin only, PD-B7-02);
    /// the frontend hides the section. Non-null (possibly empty) means entitled.</summary>
    public List<FinancialCategoryDto>? CurrentExposure { get; set; }
    /// <summary>Secondary paid history (B7.3); null when not entitled, alongside CurrentExposure.</summary>
    public PaidHistoryDto? PaidHistory { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
}
