using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Logging;

/// <summary>
/// Dedicated service for writing admin-queryable operational log events.
/// This is NOT a generic ILoggerProvider. Only events explicitly written
/// by services via WriteAsync are persisted to AdminLogEntry.
/// 
/// Fail-safe: if the write fails, the exception is swallowed so that
/// log persistence never breaks the main request flow.
/// </summary>
public class AdminLogWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AdminLogWriter> _logger;

    public AdminLogWriter(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AdminLogWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Write an admin log entry. Best-effort: failure is logged to ILogger but
    /// does NOT propagate to the caller.
    /// </summary>
    public async Task WriteAsync(string level, string source, string eventType, string message,
        string? exceptionDetail = null, string? payload = null)
    {
        try
        {
            var correlationId = _httpContextAccessor.HttpContext?.Items
                .TryGetValue(CorrelationIdMiddleware.ContextKey, out var cid) == true
                ? cid?.ToString()
                : null;

            var userEmail = ResolveUserEmail();

            var entry = new AdminLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Level = level,
                Source = source,
                EventType = eventType,
                Message = SafePayload.Sanitize(message),
                CorrelationId = correlationId,
                UserEmail = userEmail,
                ExceptionDetail = exceptionDetail is null ? null : SafePayload.Sanitize(exceptionDetail),
                Payload = payload
            };

            // Resolve a fresh DbContext scope to avoid conflicts with the request scope.
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AdminLogEntries.Add(entry);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Fail-safe: absorb the error, log to ILogger only.
            _logger.LogWarning(ex, "AdminLogWriter failed to persist entry (EventType={EventType}). Main flow unaffected.", eventType);
        }
    }

    /// <summary>
    /// Resolve user email from server-side context only.
    /// Never trusts client-supplied values.
    /// </summary>
    private string? ResolveUserEmail()
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null) return null;

            // Try standard claims (for when real auth is introduced).
            var emailClaim = httpContext.User?.FindFirst("email")?.Value
                ?? httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            if (!string.IsNullOrWhiteSpace(emailClaim)) return emailClaim;

            // Fallback: check a trusted request header (dev scaffold only).
            // This header is validated server-side; no client payload is trusted.
            return httpContext.Request.Headers.TryGetValue("X-Dev-User", out var devUser)
                ? devUser.ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
