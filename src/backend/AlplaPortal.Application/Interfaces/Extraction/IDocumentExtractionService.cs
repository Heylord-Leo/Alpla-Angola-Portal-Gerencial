using AlplaPortal.Application.DTOs.Extraction;

namespace AlplaPortal.Application.Interfaces.Extraction;

public interface IDocumentExtractionService
{
    /// <summary>
    /// Extracts structured data from a document using the configured extraction provider.
    /// </summary>
    /// <param name="fileStream">The document stream.</param>
    /// <param name="fileName">Original filename for context.</param>
    /// <returns>A provider-agnostic extraction result.</returns>
    Task<ExtractionResultDto> ExtractAsync(Stream fileStream, string fileName, CancellationToken ct = default);
}
