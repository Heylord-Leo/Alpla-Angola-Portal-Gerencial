using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlplaPortal.Infrastructure.Logging;

/// <summary>
/// Produces safe, shaped payloads for persistence in AdminLogEntry.
/// Two-layer approach: known-field masking first, regex redaction as secondary safeguard.
/// Raw request/response bodies are never persisted.
/// </summary>
public static class SafePayload
{
    // Known sensitive field names — these are explicitly masked regardless of value content.
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "apikey", "api_key", "token", "accesstoken", "access_token", "secret",
        "clientsecret", "client_secret", "password", "connectionstring",
        "connection_string", "authorization", "bearertoken", "bearer_token"
    };

    // Secondary regex safeguard for any remaining sensitive patterns.
    private static readonly Regex[] RedactionPatterns =
    [
        new Regex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"(?:key|token|secret|password)\s*[:=]\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"(?:sk-|pk-)[A-Za-z0-9\-_]{10,}", RegexOptions.Compiled),
    ];

    /// <summary>
    /// Convert an anonymous object or DTO to a safe JSON payload string.
    /// Masks known sensitive fields and applies regex redaction.
    /// </summary>
    public static string? From(object? source)
    {
        if (source is null) return null;

        try
        {
            // Serialize to a dictionary so we can inspect keys.
            var json = JsonSerializer.Serialize(source, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Parse to a mutable dictionary and mask sensitive fields.
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (dict is null) return null;

            var masked = new Dictionary<string, object?>();
            foreach (var kv in dict)
            {
                masked[kv.Key] = SensitiveFields.Contains(kv.Key)
                    ? "[REDACTED]"
                    : (object?)kv.Value;
            }

            var maskedJson = JsonSerializer.Serialize(masked);

            // Apply regex redaction as final pass.
            return ApplyRegexRedaction(maskedJson);
        }
        catch
        {
            // If serialisation fails for any reason, return nothing rather than risking raw data.
            return "[PAYLOAD_UNAVAILABLE]";
        }
    }

    /// <summary>
    /// Apply regex redaction to an arbitrary string (e.g. exception messages).
    /// </summary>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        return ApplyRegexRedaction(input);
    }

    private static string ApplyRegexRedaction(string input)
    {
        foreach (var pattern in RedactionPatterns)
            input = pattern.Replace(input, "[REDACTED]");
        return input;
    }
}
