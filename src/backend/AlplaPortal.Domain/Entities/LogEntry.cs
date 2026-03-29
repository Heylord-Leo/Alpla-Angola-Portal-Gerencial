using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Domain.Entities;

public class LogEntry
{
    public int Id { get; set; }
    
    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [MaxLength(20)]
    public string Level { get; set; } = "Information";
    
    [MaxLength(256)]
    public string? Source { get; set; }
    
    public string Message { get; set; } = string.Empty;
    
    [MaxLength(256)]
    public string? UserEmail { get; set; }
    
    [MaxLength(2048)]
    public string? Path { get; set; }
    
    [MaxLength(50)]
    public string? CorrelationId { get; set; }
    
    public string? Exception { get; set; }
    
    public string? Payload { get; set; } // Sanitized JSON or text
}
