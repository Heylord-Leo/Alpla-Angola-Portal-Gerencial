using Microsoft.AspNetCore.Http;

namespace AlplaPortal.Infrastructure.Logging;

/// <summary>
/// Middleware that manages the X-Correlation-ID header for end-to-end request tracing.
/// - Accepts an existing X-Correlation-ID from the incoming request if present.
/// - Generates a new GUID if none is provided.
/// - Stores it in HttpContext.Items["CorrelationId"] for use by services.
/// - Returns it in the response header so clients can reference it for support/debugging.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ContextKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N")[..12]; // Short 12-char ID for readability

        context.Items[ContextKey] = correlationId;

        // Append to response so the client can see it in DevTools / error messages.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
