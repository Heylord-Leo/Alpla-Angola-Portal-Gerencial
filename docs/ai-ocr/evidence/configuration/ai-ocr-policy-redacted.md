# Configuration Evidence — AI OCR Policy (G2)

> **Source**: `appsettings.json` lines 37–43
> **Config Key**: `DocumentExtraction:AiOcrPolicy`

## Configuration

```json
"AiOcrPolicy": {
  "RequireHumanConfirmation": true,
  "AllowedModules": [ "CONTRACTS", "REQUESTS" ],
  "AllowedDocumentTypes": [ ".pdf", ".jpg", ".jpeg", ".png" ],
  "AllowedRoles": [],
  "BlockHighRiskDocuments": true
}
```

## Field Descriptions

| Key | Value | Purpose |
|:---|:---|:---|
| `RequireHumanConfirmation` | `true` | No AI output saved without explicit user action |
| `AllowedModules` | `["CONTRACTS", "REQUESTS"]` | Only listed modules can invoke AI extraction |
| `AllowedDocumentTypes` | `[".pdf", ".jpg", ".jpeg", ".png"]` | Only listed file types are processed |
| `AllowedRoles` | `[]` (empty = all roles) | Reserved for future per-role restriction |
| `BlockHighRiskDocuments` | `true` | Reserved for document classification gate |

## Blocking Behavior

- **Module not in list** → Extraction blocked, `OCR_MODULE_BLOCKED` logged
- **Document type not in list** → Extraction blocked, `OCR_DOCUMENT_TYPE_BLOCKED` logged
- **Feature disabled** → All extraction blocked, `OCR_FEATURE_DISABLED` logged
- **Empty `AllowedModules`** → All modules allowed (no restriction)
- **Empty `AllowedDocumentTypes`** → All types allowed (no restriction)
