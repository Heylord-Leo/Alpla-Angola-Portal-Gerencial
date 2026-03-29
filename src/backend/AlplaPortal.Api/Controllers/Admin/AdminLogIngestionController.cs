using AlplaPortal.Infrastructure.Logging;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Api.Controllers.Admin;

public class FrontendLogIngestionDto
{
    [Required, MaxLength(20)]
    public string Level { get; set; } = "Information";

    [Required, MaxLength(64)]
    public string EventType { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Controlled component key from client: Global | OcrSettings | AdminApi
    /// </summary>
    [Required, MaxLength(50)]
    public string ComponentKey { get; set; } = "Global";

    [MaxLength(256)]
    public string? Route { get; set; }

    [MaxLength(512)]
    public string? Endpoint { get; set; }

    public int? StatusCode { get; set; }

    [MaxLength(50)]
    public string? CorrelationId { get; set; }

    [MaxLength(50)]
    public string? ClientEventId { get; set; }

    [MaxLength(512)]
    public string? UserAgent { get; set; }

    public string? ExceptionDetail { get; set; }
}

[ApiController]
[Route("api/admin/logs/ingest")]
public class AdminLogIngestionController : ControllerBase
{
    private readonly AdminLogWriter _logWriter;

    public AdminLogIngestionController(AdminLogWriter logWriter)
    {
        _logWriter = logWriter;
    }

    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] FrontendLogIngestionDto dto)
    {
        // Normalize level and event type
        var level = NormalizeLevel(dto.Level);
        var eventType = NormalizeEventType(dto.EventType);
        
        // Build controlled source
        var source = dto.ComponentKey switch
        {
            "OcrSettings" => "Frontend:OcrSettings",
            "AdminApi" => "Frontend:AdminApi",
            _ => "Frontend:Global"
        };

        // Construct message with context
        var contextMessage = $"[Route: {dto.Route ?? "-"}] {dto.Message}";
        if (dto.StatusCode.HasValue || !string.IsNullOrEmpty(dto.Endpoint))
        {
            contextMessage += $" ({dto.Endpoint ?? "-"} -> {dto.StatusCode?.ToString() ?? "-"})";
        }

        // Build payload safely
        var payloadObj = new
        {
            dto.ClientEventId,
            dto.UserAgent,
            dto.Route,
            dto.Endpoint,
            dto.StatusCode
        };
        var payloadJson = SafePayload.From(payloadObj);

        await _logWriter.WriteAsync(
            level: level,
            source: source,
            eventType: eventType,
            message: SafePayload.Sanitize(contextMessage),
            exceptionDetail: string.IsNullOrEmpty(dto.ExceptionDetail) ? null : SafePayload.Sanitize(dto.ExceptionDetail),
            payload: payloadJson
        );

        return Ok();
    }

    private static string NormalizeLevel(string input)
    {
        return input switch
        {
            "Error" => "Error",
            "Warning" => "Warning",
            _ => "Information"
        };
    }

    private static string NormalizeEventType(string input)
    {
        return input switch
        {
            "RUNTIME_ERROR" => "RUNTIME_ERROR",
            "UNHANDLED_REJECTION" => "UNHANDLED_REJECTION",
            "API_REQUEST_FAILED" => "API_REQUEST_FAILED",
            "OCR_SETTINGS_UI_ERROR" => "OCR_SETTINGS_UI_ERROR",
            _ => "FRONTEND_EVENT"
        };
    }
}
