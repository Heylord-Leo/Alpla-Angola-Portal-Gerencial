using System;

namespace AlplaPortal.Application.DTOs.Extraction;

public class OcrModuleConfigDto
{
    public int Id { get; set; }
    public string ModuleKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? AllowedExtensions { get; set; }
    public int? MaxFileSizeMb { get; set; }
    public string? ProviderOverride { get; set; }
    public string? ModelOverride { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
