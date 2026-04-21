using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Interfaces.Contracts;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AlplaPortal.Infrastructure.Services.Contracts;

/// <summary>
/// Converts raw OCR string output (dates, value, currency, supplier name)
/// into typed, portal-matched values for Phase 1 Contract OCR staging.
/// </summary>
public class ContractOcrNormalisationService : IContractOcrNormalisationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ContractOcrNormalisationService> _logger;

    // Confidence threshold below which we skip auto-fill (field goes to null)
    private const decimal AutoFillThreshold = 0.70m;

    // Supplier fuzzy match: minimum Jaro-Winkler score to propose a match
    private const double SupplierFuzzyThreshold = 0.82;

    // Portuguese month name→number map for date parsing
    private static readonly Dictionary<string, int> PtMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["janeiro"] = 1, ["fevereiro"] = 2, ["março"] = 3,  ["marco"] = 3,
        ["abril"]   = 4, ["maio"]      = 5, ["junho"]  = 6,
        ["julho"]   = 7, ["agosto"]    = 8, ["setembro"] = 9,
        ["outubro"] = 10, ["novembro"] = 11, ["dezembro"] = 12,
        // Abbreviated
        ["jan"] = 1, ["fev"] = 2, ["mar"] = 3, ["abr"] = 4,
        ["mai"] = 5, ["jun"] = 6, ["jul"] = 7, ["ago"] = 8,
        ["set"] = 9, ["out"] = 10, ["nov"] = 11, ["dez"] = 12,
    };

    // Currency alias → standard ISO code for portal matching
    private static readonly Dictionary<string, string> CurrencyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kwanza"]  = "AOA", ["aoa"] = "AOA", ["kz"] = "AOA",
        ["dólar"]   = "USD", ["dollar"] = "USD", ["$"] = "USD", ["usd"] = "USD",
        ["euro"]    = "EUR", ["eur"] = "EUR", ["€"] = "EUR",
        ["pound"]   = "GBP", ["gbp"] = "GBP", ["£"] = "GBP",
        ["rand"]    = "ZAR", ["zar"] = "ZAR",
    };

    public ContractOcrNormalisationService(
        ApplicationDbContext db,
        ILogger<ContractOcrNormalisationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ContractOcrNormalisedResult> NormaliseAsync(
        ExtractionContractDto raw,
        CancellationToken ct = default)
    {
        // ── 1. Load portal master data needed for matching ────────────────
        var currencies = await _db.Currencies
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Code, c.Symbol })
            .ToListAsync(ct);

        var suppliers = await _db.Suppliers
            .Where(s => s.IsActive)
            .Select(s => new { s.Id, s.Name, s.TaxId })
            .ToListAsync(ct);

        // ── 2. Normalise dates ────────────────────────────────────────────
        var (effDate, effConf)  = NormaliseDate(raw.EffectiveDate, raw.EffectiveDateConfidence);
        var (endDate, endConf)  = NormaliseDate(raw.EndDate, raw.EndDateConfidence);
        var (sigDate, sigConf)  = NormaliseDate(raw.SignatureDate, raw.SignatureDateConfidence);

        // ── 3. Normalise total contract value ─────────────────────────────
        var (value, valueConf) = NormaliseDecimalValue(raw.TotalContractValue, raw.TotalContractValueConfidence);

        // ── 4. Match currency ─────────────────────────────────────────────
        int?    matchedCurrencyId  = null;
        string? currencyRawKept    = raw.CurrencyRaw;
        decimal? currencyConf      = raw.CurrencyConfidence;

        if (!string.IsNullOrWhiteSpace(raw.CurrencyRaw))
        {
            var isoCode = NormaliseToIsoCode(raw.CurrencyRaw);
            var matched = currencies.FirstOrDefault(c =>
                string.Equals(c.Code, isoCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Symbol, raw.CurrencyRaw.Trim(), StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                matchedCurrencyId = matched.Id;
                _logger.LogDebug("Currency '{Raw}' matched to portal code '{Code}' (Id={Id})",
                    raw.CurrencyRaw, matched.Code, matched.Id);
            }
            else
            {
                // No exact match → currency stays as suggestion-only (no auto-fill)
                matchedCurrencyId = null;
                _logger.LogInformation("Currency '{Raw}' had no portal match — will not auto-fill.", raw.CurrencyRaw);
            }
        }

        // ── 5. Supplier fuzzy match ───────────────────────────────────────
        SupplierMatchResult? supplierMatch = null;
        if (!string.IsNullOrWhiteSpace(raw.SupplierName))
        {
            // First try exact TaxId match
            if (!string.IsNullOrWhiteSpace(raw.SupplierTaxId))
            {
                var taxMatch = suppliers.FirstOrDefault(s =>
                    s.TaxId != null &&
                    NormaliseTaxId(s.TaxId) == NormaliseTaxId(raw.SupplierTaxId));

                if (taxMatch != null)
                {
                    supplierMatch = new SupplierMatchResult(taxMatch.Id, taxMatch.Name, 1.0);
                    _logger.LogDebug("Supplier matched by TaxId '{TaxId}' → '{Name}'", raw.SupplierTaxId, taxMatch.Name);
                }
            }

            // Fallback to fuzzy name match
            if (supplierMatch == null)
            {
                var bestScore  = 0.0;
                int bestId     = 0;
                string bestName = string.Empty;

                foreach (var sup in suppliers)
                {
                    var score = JaroWinkler(Normalise(raw.SupplierName), Normalise(sup.Name));
                    if (score > bestScore) { bestScore = score; bestId = sup.Id; bestName = sup.Name; }
                }

                if (bestScore >= SupplierFuzzyThreshold)
                {
                    supplierMatch = new SupplierMatchResult(bestId, bestName, bestScore);
                    _logger.LogDebug("Supplier '{Raw}' fuzzy-matched to '{Name}' (score={Score:F3})",
                        raw.SupplierName, bestName, bestScore);
                }
                else
                {
                    _logger.LogInformation("Supplier '{Raw}' had no fuzzy match above threshold (best={Score:F3})",
                        raw.SupplierName, bestScore);
                }
            }
        }

        // ── 6. Build suggestion-only fields ──────────────────────────────
        var suggestedTitle        = TruncateSuggestion(raw.Parties, 200);
        var suggestedCounterparty = TruncateSuggestion(raw.SupplierName, 200);
        var suggestedGoverningLaw = TruncateSuggestion(raw.GoverningLaw, 300);
        var suggestedPaymentTerms = TruncateSuggestion(raw.PaymentTerms, 300);
        // SuggestedDescription: operational scope summary from the LLM object/scope clause.
        // Suppressed when the LLM returns null — TruncateSuggestion returns null on empty input.
        var suggestedDescription  = TruncateSuggestion(raw.ContractScope, 300);

        // ── 6b. Phase 1.1 Rule B — financial inconsistency penalty ───────
        // If the LLM flagged a numeric/written-word mismatch, clamp direct
        // TotalContractValue confidence to 0.20 so it falls below AutoFillThreshold
        // and is treated as SUGGESTION, not AUTO_FILL.
        bool financialInconsistency = raw.WrittenAmountInconsistencyDetected;
        if (financialInconsistency && valueConf.HasValue && valueConf.Value > 0.20m)
        {
            _logger.LogWarning(
                "[OCR-Norm] Financial inconsistency flag received — clamping TotalContractValue " +
                "confidence from {Original:F2} to 0.20. Value will be SUGGESTION-only, not AUTO_FILL.",
                valueConf.Value);
            valueConf = 0.20m;
            value = null; // Prevent auto-fill at the result gate below
        }

        // ── 6c. Phase 1.1 Rule A — derived total (monthly × months) ─────
        // Only derive when the explicit total is absent, low-confidence, or
        // suppressed by the inconsistency penalty above.
        decimal?  parsedMonthly    = null;
        decimal?  derivedTotal     = null;
        decimal?  derivedTotalConf = null;
        string?   monthlyRaw       = TruncateSuggestion(raw.RecurringMonthlyAmount, 100);
        int?      durationMonths   = raw.ContractDurationMonths;

        if (!string.IsNullOrWhiteSpace(raw.RecurringMonthlyAmount))
        {
            var (parsed, _) = NormaliseDecimalValue(raw.RecurringMonthlyAmount,
                raw.RecurringMonthlyAmountConfidence);
            parsedMonthly = parsed;
        }

        bool explicitTotalMissing = !value.HasValue && (valueConf ?? 0) < AutoFillThreshold;
        bool explicitConfLow      = value.HasValue && (valueConf ?? 0) < AutoFillThreshold;

        if (parsedMonthly.HasValue && parsedMonthly.Value > 0
            && durationMonths.HasValue && durationMonths.Value > 0
            && (explicitTotalMissing || explicitConfLow || financialInconsistency))
        {
            derivedTotal     = parsedMonthly.Value * durationMonths.Value;
            derivedTotalConf = 0.60m; // Fixed — this is a calculation, not an extraction
            _logger.LogInformation(
                "[OCR-Norm] Derived SuggestedTotalContractValue: {Monthly} × {Months} = {Total} (conf=0.60).",
                parsedMonthly.Value, durationMonths.Value, derivedTotal.Value);
        }

        // ── 7. Assemble result ────────────────────────────────────────────
        bool hasAutoFill = (effDate.HasValue && effConf >= AutoFillThreshold)
                        || (endDate.HasValue && endConf >= AutoFillThreshold)
                        || (sigDate.HasValue && sigConf >= AutoFillThreshold)
                        || (value.HasValue   && valueConf >= AutoFillThreshold)
                        || matchedCurrencyId.HasValue;

        bool hasSuggestion = !string.IsNullOrWhiteSpace(suggestedTitle)
                          || !string.IsNullOrWhiteSpace(suggestedCounterparty)
                          || !string.IsNullOrWhiteSpace(suggestedGoverningLaw)
                          || !string.IsNullOrWhiteSpace(suggestedPaymentTerms)
                          || !string.IsNullOrWhiteSpace(suggestedDescription)
                          || supplierMatch != null
                          || derivedTotal.HasValue;

        return new ContractOcrNormalisedResult
        {
            EffectiveDate          = effConf >= AutoFillThreshold  ? effDate  : null,
            EffectiveDateConf      = effConf,
            ExpirationDate         = endConf >= AutoFillThreshold  ? endDate  : null,
            ExpirationDateConf     = endConf,
            SignatureDate          = sigConf >= AutoFillThreshold  ? sigDate  : null,
            SignatureDateConf      = sigConf,
            TotalContractValue     = valueConf >= AutoFillThreshold ? value   : null,
            TotalContractValueConf = valueConf,
            CurrencyId             = matchedCurrencyId,
            CurrencyRaw            = currencyRawKept,
            CurrencyConf           = currencyConf,
            SuggestedTitle         = suggestedTitle,
            SuggestedCounterparty  = suggestedCounterparty,
            SuggestedGoverningLaw  = suggestedGoverningLaw,
            SuggestedPaymentTerms  = suggestedPaymentTerms,
            SuggestedDescription   = suggestedDescription,
            SupplierMatch          = supplierMatch,
            // Phase 1.1 financial derivation
            SuggestedTotalContractValue     = derivedTotal,
            SuggestedTotalContractValueConf = derivedTotalConf,
            RecurringMonthlyAmountRaw       = monthlyRaw,
            RecurringMonthlyAmountParsed    = parsedMonthly,
            ContractDurationMonths          = durationMonths,
            FinancialInconsistencyDetected  = financialInconsistency,
            QualityScore           = raw.EffectiveDateConfidence ?? raw.EndDateConfidence ?? raw.TotalContractValueConfidence ?? 0.5m,
            HasAnyAutoFillField    = hasAutoFill,
            HasAnySuggestionField  = hasSuggestion,
        };
    }


    // ── Date normalisation ───────────────────────────────────────────────────

    private (DateOnly? date, decimal? conf) NormaliseDate(string? raw, decimal? conf)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        // 1. ISO-8601 (YYYY-MM-DD) — preferred from prompt instructions
        if (DateOnly.TryParse(raw, out var iso))
            return (iso, conf);

        // 2. DD/MM/YYYY or DD-MM-YYYY (Angolan/Portuguese convention)
        if (TryParseDmy(raw, out var dmy))
            return (dmy, conf);

        // 3. "10 de Janeiro de 2024" / "10 Janeiro 2024"
        if (TryParsePortugueseLong(raw, out var pt))
            return (pt, conf);

        _logger.LogDebug("Could not parse date string '{Raw}' — skipping", raw);
        return (null, null);
    }

    private static bool TryParseDmy(string s, out DateOnly result)
    {
        result = default;
        var formats = new[]
        {
            "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy",
            "dd.MM.yyyy", "d.M.yyyy",
        };
        foreach (var fmt in formats)
            if (DateOnly.TryParseExact(s.Trim(), fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;
        return false;
    }

    private static bool TryParsePortugueseLong(string s, out DateOnly result)
    {
        result = default;
        // Pattern: "10 de Janeiro de 2024" or "10 Janeiro 2024"
        var m = Regex.Match(s.Trim(),
            @"(\d{1,2})\s+(?:de\s+)?([A-Za-záéíóúãõçÁÉÍÓÚÃÕÇ]+)\.?\s+(?:de\s+)?(\d{4})",
            RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        if (!int.TryParse(m.Groups[1].Value, out int day)) return false;
        if (!PtMonths.TryGetValue(m.Groups[2].Value.TrimEnd('.'), out int month)) return false;
        if (!int.TryParse(m.Groups[3].Value, out int year)) return false;

        try { result = new DateOnly(year, month, day); return true; }
        catch { return false; }
    }

    // ── Decimal value normalisation ──────────────────────────────────────────

    private (decimal? value, decimal? conf) NormaliseDecimalValue(string? raw, decimal? conf)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        // Strip currency symbols and common noise
        var cleaned = Regex.Replace(raw, @"[^\d.,\-]", " ").Trim();

        // Handle European format: 1.234.567,89 → 1234567.89
        if (Regex.IsMatch(cleaned, @"\d{1,3}(\.\d{3})+,\d{2}$"))
            cleaned = cleaned.Replace(".", "").Replace(",", ".");

        // Handle US/DOT format: 1,234,567.89 → strip commas
        else if (Regex.IsMatch(cleaned, @"\d{1,3}(,\d{3})+\.\d{2}$"))
            cleaned = cleaned.Replace(",", "");

        // Remove any remaining commas (single comma used as thousands separator)
        else
            cleaned = cleaned.Replace(",", "");

        // Take the largest number found (the total value, not a sub-clause reference)
        var numbers = Regex.Matches(cleaned, @"[\d]+(?:\.\d+)?")
            .Select(n => decimal.TryParse(n.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0)
            .Where(d => d > 0)
            .OrderByDescending(d => d)
            .ToList();

        if (!numbers.Any()) return (null, null);
        return (numbers.First(), conf);
    }

    // ── Currency helpers ─────────────────────────────────────────────────────

    private static string? NormaliseToIsoCode(string rawCurrency)
    {
        var trimmed = rawCurrency.Trim();
        if (CurrencyAliases.TryGetValue(trimmed, out var iso)) return iso;
        // Already looks like a 3-letter ISO code
        if (trimmed.Length == 3 && trimmed.All(char.IsLetter)) return trimmed.ToUpperInvariant();
        return null;
    }

    // ── Supplier helpers ─────────────────────────────────────────────────────

    private static string NormaliseTaxId(string s) =>
        Regex.Replace(s, @"[^\d]", "");

    private static string Normalise(string s) =>
        Regex.Replace(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD),
            @"\p{Mn}", "").Trim();

    private static string? TruncateSuggestion(string? s, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Length <= maxLen ? s : s[..maxLen] + "…";
    }

    // ── Jaro-Winkler string similarity ──────────────────────────────────────

    private static double JaroWinkler(string s1, string s2)
    {
        if (s1 == s2) return 1.0;
        if (s1.Length == 0 || s2.Length == 0) return 0.0;

        int matchWindow = Math.Max(s1.Length, s2.Length) / 2 - 1;
        if (matchWindow < 0) matchWindow = 0;

        var s1Matches = new bool[s1.Length];
        var s2Matches = new bool[s2.Length];
        int matches = 0, transpositions = 0;

        for (int i = 0; i < s1.Length; i++)
        {
            int start = Math.Max(0, i - matchWindow);
            int end   = Math.Min(i + matchWindow + 1, s2.Length);
            for (int j = start; j < end; j++)
            {
                if (s2Matches[j] || s1[i] != s2[j]) continue;
                s1Matches[i] = s2Matches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0;

        int k = 0;
        for (int i = 0; i < s1.Length; i++)
        {
            if (!s1Matches[i]) continue;
            while (!s2Matches[k]) k++;
            if (s1[i] != s2[k]) transpositions++;
            k++;
        }

        double jaro = (matches / (double)s1.Length
                     + matches / (double)s2.Length
                     + (matches - transpositions / 2.0) / matches) / 3.0;

        // Winkler prefix bonus (up to 4 chars)
        int prefix = 0;
        for (int i = 0; i < Math.Min(4, Math.Min(s1.Length, s2.Length)); i++)
        {
            if (s1[i] == s2[i]) prefix++;
            else break;
        }

        return jaro + prefix * 0.1 * (1 - jaro);
    }
}
