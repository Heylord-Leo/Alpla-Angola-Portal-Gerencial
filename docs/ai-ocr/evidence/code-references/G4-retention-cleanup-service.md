# G4 Evidence — Retention and Cleanup Service

## Code Reference

### `OcrCleanupService` Implementation

**File**: [`OcrCleanupService.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/OcrCleanupService.cs) (134 lines)

**Type**: `BackgroundService` (runs daily)

**Behavior**:
1. Checks `AutoCleanupEnabled` flag — if `false`, skips entirely (default: `false`)
2. Iterates `debug/openai-json/` and `debug/openai-rasterized/` directories
3. Deletes files older than `DebugFileRetentionDays` (default: 7 days)
4. Logs `OCR_CLEANUP_EXECUTED` or `OCR_CLEANUP_FAILED` via `AdminLogWriter`
5. **Does NOT delete**: `AdminLogEntry` records, `ContractOcrExtractedField` records, or any official audit data

### Key Code Sections

```csharp
// Guard: cleanup only runs when explicitly enabled
if (!retention.AutoCleanupEnabled)
{
    _logger.LogDebug("[OcrCleanupService] AutoCleanupEnabled is false. Skipping cleanup.");
    return;
}
```

```csharp
// Only debug artifacts are cleaned — never official data
private static readonly string[] DebugFolders = new[]
{
    @"C:\dev\alpla-portal\debug\openai-json",
    @"C:\dev\alpla-portal\debug\openai-rasterized"
};
```

```csharp
// Audit trail of cleanup execution
var eventType = errors.Count > 0 ? "OCR_CLEANUP_FAILED" : "OCR_CLEANUP_EXECUTED";
await adminLogWriter.WriteAsync(level, nameof(OcrCleanupService), eventType,
    $"OCR cleanup completed. Files deleted: {totalFilesDeleted}. Errors: {errors.Count}.",
    payload: SafePayload.From(new { debugFilesDeleted, rawJsonRecordsCleaned = 0, ... }));
```

### `RetentionPolicyOptions` Configuration Model

**File**: [`DocumentExtractionOptions.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Configuration/DocumentExtractionOptions.cs)

```csharp
public class RetentionPolicyOptions
{
    public int DebugFileRetentionDays { get; set; } = 7;
    public int RawJsonResultRetentionDays { get; set; } = 90;
    public bool AutoCleanupEnabled { get; set; } = false;
}
```

### DI Registration

**File**: [`Program.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Api/Program.cs)

```csharp
builder.Services.AddHostedService<OcrCleanupService>();
```

### What Is NOT Cleaned

| Data | Cleaned? | Reason |
|:---|:---|:---|
| `AdminLogEntry` records | ❌ Never | Official audit trail |
| `ContractOcrExtractionRecord` | ❌ Never | Business audit data |
| `ContractOcrExtractedField` | ❌ Never | Field-level audit trail |
| `OcrExtractedItem` | ❌ Never | Invoice extraction audit |
| `debug/openai-json/` files | ✅ When enabled | Debug artifacts only |
| `debug/openai-rasterized/` files | ✅ When enabled | Debug artifacts only |
| `RawJsonResult` column | ❌ Not yet | Reserved for future (requires Legal confirmation) |

### Evidence Files

- Configuration: [`retention-policy-redacted.md`](../configuration/retention-policy-redacted.md)
- Test result: [`G4-cleanup-validation.md`](../test-results/G4-cleanup-validation.md)
