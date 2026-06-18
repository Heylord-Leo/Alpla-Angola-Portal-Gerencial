# SCR-29 — AI OCR Policy Configuration Evidence

> **Status**: Placeholder — requires manual capture

## Screenshot Details

| Field | Value |
|:---|:---|
| **Screenshot ID** | SCR-29 |
| **Description** | AI OCR policy configuration showing feature flags |
| **URL/Page** | `appsettings.json` or Admin Settings UI |
| **Role Required** | Admin / Developer |
| **Evidence Type** | Configuration |

## Capture Steps

1. Open `appsettings.json` and navigate to `DocumentExtraction:AiOcrPolicy` section
2. Screenshot showing `AllowedModules`, `AllowedDocumentTypes`, `RequireHumanConfirmation`
3. Alternatively: Navigate to Settings → Document Extraction in the portal UI

## Expected Visible Result

- `RequireHumanConfirmation: true`
- `AllowedModules: ["CONTRACTS", "REQUESTS"]`
- `AllowedDocumentTypes: [".pdf", ".jpg", ".jpeg", ".png"]`

## Alternative Evidence

- Configuration excerpt: [`ai-ocr-policy-redacted.md`](../configuration/ai-ocr-policy-redacted.md)
- Code reference: [`G2-ai-ocr-policy-controls.md`](../code-references/G2-ai-ocr-policy-controls.md)
