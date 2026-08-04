using AlplaPortal.Application.DTOs.Extraction;

namespace AlplaPortal.Application.Interfaces.Extraction;

public interface IDocumentExtractionService
{
    /// <summary>
    /// Extracts structured data from a document using the configured extraction provider.
    /// </summary>
    /// <param name="fileStream">The document stream.</param>
    /// <param name="fileName">Original filename for context.</param>
    /// <returns>
    /// A provider-agnostic extraction result. <b>Check <c>Success</c></b> — a blocked module, a
    /// disallowed extension, an unavailable provider and a provider failure all return
    /// <c>Success = false</c> rather than throwing.
    /// </returns>
    /// <param name="sourceContext">
    /// <b>An OCR module allowlist key, not a free-text hint.</b> When non-empty it is matched
    /// case-insensitively against <c>OcrModuleConfigs.ModuleKey</c>, and an unknown or disabled
    /// value makes the extraction return <c>Success = false</c> <b>before any provider is
    /// called</b> — no pages processed, no tokens spent, every field null. The configured modules
    /// are <c>REQUESTS</c> ("Requests &amp; Buy2Pay", which governs quotation and payment
    /// extraction) and <c>CONTRACTS</c>.
    ///
    /// <para>Passing <c>null</c> skips the allowlist entirely and always reaches the provider.</para>
    ///
    /// <para>Note that <see cref="IDocumentExtractionProvider"/> reads the <i>same</i> argument as a
    /// document-strategy hint (it recognises <c>quotation</c>, <c>payment_request</c> and
    /// <c>CONTRACT</c>). The two vocabularies are not the same set, so a value chosen for the
    /// strategy hint can silently fail the allowlist. Prefer a real module key.</para>
    /// </param>
    Task<ExtractionResultDto> ExtractAsync(Stream fileStream, string fileName, string? sourceContext = null, CancellationToken ct = default);
}
