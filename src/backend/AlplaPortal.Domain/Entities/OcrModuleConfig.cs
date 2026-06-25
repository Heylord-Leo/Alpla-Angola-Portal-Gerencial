using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlplaPortal.Domain.Entities;

public class OcrModuleConfig
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string ModuleKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    [MaxLength(200)]
    public string? AllowedExtensions { get; set; }

    public int? MaxFileSizeMb { get; set; }

    [MaxLength(50)]
    public string? ProviderOverride { get; set; }

    [MaxLength(100)]
    public string? ModelOverride { get; set; }

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
