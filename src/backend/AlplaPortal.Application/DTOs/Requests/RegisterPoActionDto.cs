namespace AlplaPortal.Application.DTOs.Requests;

public class RegisterPoActionDto
{
    public string? Comment { get; set; }
    
    // OCR Validation Payload
    public bool HasMismatches { get; set; }
    public bool OverrideConfirmed { get; set; }
    public string? MismatchDetails { get; set; }
}
