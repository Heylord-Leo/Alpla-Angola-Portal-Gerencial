using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers.Admin;

public class ServiceHealthDto
{
    public ServiceStatusItem Backend { get; set; } = new();
    public ServiceStatusItem Database { get; set; } = new();
    public ServiceStatusItem LocalOcr { get; set; } = new();
    public ServiceStatusItem OpenAi { get; set; } = new();
}

public class ServiceStatusItem
{
    /// <summary>Healthy | Unhealthy | Configured | NotConfigured | Unreachable</summary>
    public string Status { get; set; } = "Unknown";
    public bool Configured { get; set; }
    public bool Reachable { get; set; }
    public bool Healthy { get; set; }
    public string? Message { get; set; }
}

[ApiController]
[Route("api/admin/diagnostics")]
public class AdminDiagnosticsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IDocumentExtractionSettingsService _extractionSettingsService;

    public AdminDiagnosticsController(
        ApplicationDbContext db,
        IDocumentExtractionSettingsService extractionSettingsService)
    {
        _db = db;
        _extractionSettingsService = extractionSettingsService;
    }

    [HttpGet("health")]
    public async Task<ActionResult<ServiceHealthDto>> GetHealth(CancellationToken ct)
    {
        var health = new ServiceHealthDto();

        // 1. Backend (If we are here, it's reachable and healthy)
        health.Backend = new ServiceStatusItem 
        { 
            Status = "Healthy", 
            Configured = true, 
            Reachable = true, 
            Healthy = true 
        };

        // 2. Database
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            health.Database = new ServiceStatusItem
            {
                Status = canConnect ? "Healthy" : "Unhealthy",
                Configured = true,
                Reachable = canConnect,
                Healthy = canConnect,
                Message = canConnect ? null : "Falha na ligação à base de dados."
            };
        }
        catch (Exception ex)
        {
            health.Database = new ServiceStatusItem { Status = "Unhealthy", Configured = true, Healthy = false, Message = ex.Message };
        }

        // 3. Extraction Settings (for LocalOcr and OpenAI status)
        var settings = await _extractionSettingsService.GetSettingsAsync(ct);

        // Local OCR
        health.LocalOcr = new ServiceStatusItem
        {
            Configured = !string.IsNullOrEmpty(settings.LocalOcrBaseUrl),
            Status = string.IsNullOrEmpty(settings.LocalOcrBaseUrl) ? "NotConfigured" : "Configured"
        };
        
        // If configured, check reachability via the test connection (simplified here)
        if (health.LocalOcr.Configured && settings.DefaultProvider == "LOCAL_OCR")
        {
            var test = await _extractionSettingsService.TestConnectionAsync(ct);
            health.LocalOcr.Reachable = test.Success;
            health.LocalOcr.Healthy = test.Success;
            health.LocalOcr.Status = test.Success ? "Healthy" : "Unreachable";
            health.LocalOcr.Message = test.Message;
        }

        // OpenAI
        health.OpenAi = new ServiceStatusItem
        {
            Configured = settings.OpenAiEnabled && !string.IsNullOrEmpty(settings.OpenAiModel),
            Status = (settings.OpenAiEnabled && !string.IsNullOrEmpty(settings.OpenAiModel)) ? "Configured" : "NotConfigured"
        };
        
        if (health.OpenAi.Configured && settings.DefaultProvider == "OPENAI")
        {
             var test = await _extractionSettingsService.TestConnectionAsync(ct);
             health.OpenAi.Reachable = test.Success;
             health.OpenAi.Healthy = test.Success;
             health.OpenAi.Status = test.Success ? "Healthy" : "Unreachable";
             health.OpenAi.Message = test.Message;
        }

        return Ok(health);
    }

    [HttpGet("version")]
    public ActionResult<object> GetVersion()
    {
        return Ok(new { version = "2.0.1" });
    }
}
