# G2 Evidence — AI OCR Policy Controls

## Code Reference

### `AiOcrPolicyOptions` Configuration Model

**File**: [`DocumentExtractionOptions.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Configuration/DocumentExtractionOptions.cs)

```csharp
public class AiOcrPolicyOptions
{
    public bool RequireHumanConfirmation { get; set; } = true;
    public List<string> AllowedModules { get; set; } = new();
    public List<string> AllowedDocumentTypes { get; set; } = new();
    public List<string> AllowedRoles { get; set; } = new();
    public bool BlockHighRiskDocuments { get; set; } = true;
}
```

### Module Allowlist Enforcement

**File**: [`DocumentExtractionService.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionService.cs#L42-L55)

```csharp
// G2: Module allowlist enforcement
var policy = options.AiOcrPolicy;
if (!string.IsNullOrWhiteSpace(sourceContext) && policy.AllowedModules.Count > 0)
{
    var moduleAllowed = policy.AllowedModules.Any(m =>
        m.Equals(sourceContext, StringComparison.OrdinalIgnoreCase));
    if (!moduleAllowed)
    {
        _logger.LogWarning("Module '{Module}' is not in AllowedModules list.", sourceContext);
        await _adminLogWriter.WriteAsync("Warning", nameof(DocumentExtractionService),
            "OCR_MODULE_BLOCKED", ...);
        return new ExtractionResultDto { Success = false };
    }
}
```

### Document Type Allowlist Enforcement

**File**: [`DocumentExtractionService.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionService.cs#L57-L66)

```csharp
// G2: Document type allowlist enforcement
var extension = Path.GetExtension(fileName).ToLowerInvariant();
if (policy.AllowedDocumentTypes.Count > 0 && !policy.AllowedDocumentTypes.Contains(extension))
{
    _logger.LogWarning("Document type '{Extension}' is not in AllowedDocumentTypes.", extension);
    await _adminLogWriter.WriteAsync("Warning", nameof(DocumentExtractionService),
        "OCR_DOCUMENT_TYPE_BLOCKED", ...);
    return new ExtractionResultDto { Success = false };
}
```

### Global Feature Disable

**File**: [`DocumentExtractionService.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionService.cs#L32-L40)

```csharp
// G2: Global enable/disable check
if (!options.IsEnabled)
{
    await _adminLogWriter.WriteAsync("Warning", nameof(DocumentExtractionService),
        "OCR_FEATURE_DISABLED", ...);
    return new ExtractionResultDto { Success = false };
}
```

### Role Blocking Status

**Status**: Not implemented (deferred).
**Reason**: Existing RBAC via `[Authorize]` attributes on controllers already restricts API access by role. Per-role OCR blocking within the service layer was deemed redundant.
**Remaining Action**: If AI CoE requires explicit per-role OCR access control, add enforcement in `DocumentExtractionService` using `AllowedRoles` list (config key already exists).
**Location for future implementation**: [`DocumentExtractionService.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionService.cs) — after line 66.

### Human Confirmation Requirement

**Status**: Implemented (pre-existing).
**Evidence**: `RequireHumanConfirmation = true` in config. UI enforces confirm/edit/reject cycle:
- [`OcrFieldWrapper.tsx`](file:///c:/dev/alpla-portal/src/frontend/src/pages/Contracts/ocr/OcrFieldWrapper.tsx) — Confirmar/Limpar buttons
- [`OcrSuggestionChip.tsx`](file:///c:/dev/alpla-portal/src/frontend/src/pages/Contracts/ocr/OcrSuggestionChip.tsx) — Aplicar/Ignorar buttons
- [`ContractOcrExtractedField.cs`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/Contracts/ContractOcrExtractedField.cs) — `ConfirmedByUser`, `WasOverridden`, `DiscardedByUser` audit fields

### Configuration

See: [`ai-ocr-policy-redacted.md`](../configuration/ai-ocr-policy-redacted.md)

### Log Events

| Event Type | Trigger | Severity |
|:---|:---|:---|
| `OCR_FEATURE_DISABLED` | Global `IsEnabled=false` | Warning |
| `OCR_MODULE_BLOCKED` | Module not in `AllowedModules` | Warning |
| `OCR_DOCUMENT_TYPE_BLOCKED` | File extension not in `AllowedDocumentTypes` | Warning |

### Evidence Files

- Configuration: [`ai-ocr-policy-redacted.md`](../configuration/ai-ocr-policy-redacted.md)
- Log samples: [`OCR_MODULE_BLOCKED-sanitized.json`](../logs/OCR_MODULE_BLOCKED-sanitized.json), [`OCR_DOCUMENT_TYPE_BLOCKED-sanitized.json`](../logs/OCR_DOCUMENT_TYPE_BLOCKED-sanitized.json)
