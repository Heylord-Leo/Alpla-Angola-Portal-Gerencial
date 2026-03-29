namespace AlplaPortal.Application.DTOs.Extraction;

public class ConnectionTestResultDto
{
    public bool Success { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public long? ResponseTimeMs { get; set; }
}
