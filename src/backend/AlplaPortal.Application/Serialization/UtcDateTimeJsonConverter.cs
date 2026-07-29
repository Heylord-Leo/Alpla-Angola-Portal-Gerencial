using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlplaPortal.Application.Serialization;

/// <summary>
/// JSON converter for <see cref="DateTime"/> properties that store UTC instants
/// but arrive from EF Core with <see cref="DateTimeKind.Unspecified"/>.
/// 
/// Writes: always appends "+00:00" so JavaScript can parse the value as UTC.
/// Reads:  accepts ISO 8601 with or without offset and normalizes to UTC.
/// 
/// Apply via [JsonConverter(typeof(UtcDateTimeJsonConverter))] on DTO properties
/// that represent database CreatedAtUtc / UpdatedAtUtc columns.
/// This is NOT a global converter — it is scoped to individual properties.
/// </summary>
public class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Accept any ISO 8601 string and normalize to UTC
        if (reader.TryGetDateTimeOffset(out var dto))
            return dto.UtcDateTime;

        if (reader.TryGetDateTime(out var dt))
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        throw new JsonException($"Unable to parse DateTime from: {reader.GetString()}");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Force UTC kind so the ISO 8601 output includes +00:00
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var dto = new DateTimeOffset(utc);
        writer.WriteStringValue(dto.ToString("O")); // e.g. "2026-07-28T09:43:49.1337619+00:00"
    }
}
