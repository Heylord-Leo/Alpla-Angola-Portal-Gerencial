using AlplaPortal.Application.DTOs.Extraction;

namespace AlplaPortal.Application.Interfaces.Extraction;

public interface IDocumentExtractionProvider
{
    /// <summary>
    /// Unique provider name (e.g., OPENAI, AZURE_DOCUMENT_INTELLIGENCE).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Extracts structured data from a document using the specific provider implementation.
    /// </summary>
    Task<ExtractionResultDto> ExtractAsync(Stream fileStream, string fileName, string? sourceContext = null, CancellationToken ct = default);
}
