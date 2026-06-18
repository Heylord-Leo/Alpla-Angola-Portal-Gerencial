# G4 — Cleanup Service Validation

> **Status**: Code-verified — not executed live (AutoCleanupEnabled=false by default)

## What Was Validated

| Check | Method | Result |
|:---|:---|:---|
| `OcrCleanupService` compiles | `dotnet build` | ✅ Pass |
| Service is registered as `BackgroundService` | Code inspection of `Program.cs` | ✅ Verified |
| `AutoCleanupEnabled=false` by default | `appsettings.json` inspection | ✅ Verified |
| Guard prevents cleanup when disabled | Code inspection of `RunCleanupAsync()` | ✅ Verified |
| Only debug folders are targeted | Code inspection (`DebugFolders` array) | ✅ Verified |
| Official audit data is never deleted | Code inspection — no AdminLogEntry/field deletes | ✅ Verified |
| Cleanup logs `OCR_CLEANUP_EXECUTED` | Code inspection of event emission | ✅ Verified |
| Cleanup logs `OCR_CLEANUP_FAILED` on errors | Code inspection of error path | ✅ Verified |

## Live Execution Status

> [!WARNING]
> **Not executed live.** Cleanup is disabled by default (`AutoCleanupEnabled=false`). It must remain disabled until Legal/AI CoE confirms retention requirements.

## Manual Validation Instructions

To test cleanup manually:

1. Set `DocumentExtraction:Retention:AutoCleanupEnabled` to `true` in `appsettings.Development.json`
2. Create test files older than 7 days in `debug/openai-json/`
3. Restart the application
4. Wait for the cleanup cycle (24 hours) or reduce `Interval` temporarily for testing
5. Verify test files were deleted
6. Query `AdminLogEntries` for `OCR_CLEANUP_EXECUTED` event
7. **Restore** `AutoCleanupEnabled=false` after testing

## Conclusion

Cleanup service is implemented, registered, and code-verified. Live execution is intentionally deferred pending Legal retention approval.
