using AlplaPortal.Application.DTOs.Requests;

namespace AlplaPortal.Application.Interfaces;

public interface IOcrService
{
    /// <summary>
    /// Processes a document file and returns structured extraction suggestions.
    /// </summary>
    /// <param name="fileStream">The document stream.</param>
    /// <param name="fileName">Original filename for context.</param>
    /// <returns>Structured extraction result.</returns>
    Task<OcrExtractionResultDto> ExtractAsync(Stream fileStream, string fileName);
}
