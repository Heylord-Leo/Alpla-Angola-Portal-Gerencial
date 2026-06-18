# SCR-30 — Provider Settings (Masked)

> **Status**: Placeholder — requires manual capture

## Screenshot Details

| Field | Value |
|:---|:---|
| **Screenshot ID** | SCR-30 |
| **Description** | Document Extraction Settings page showing provider config |
| **URL/Page** | Portal → Settings → Document Extraction |
| **Role Required** | Admin |
| **Evidence Type** | UI Screenshot |

## Capture Steps

1. Log in as Admin
2. Navigate to Settings → Document Extraction (or Integrations)
3. Screenshot the provider configuration panel showing:
   - Provider name (OPENAI)
   - Model selection
   - Enabled/disabled toggle
   - Endpoint field (if visible)
4. **DO NOT screenshot** the API key field — mask or collapse it

## Expected Visible Result

- Provider: OPENAI
- Model: gpt-4-turbo
- Enabled: Yes
- Connection test button visible

## Masking Requirements

- **API key must be fully masked** or not visible
- Mask any real endpoint URLs if they contain internal domain names

## Alternative Evidence

- Configuration excerpt: [`provider-endpoint-redacted.md`](../configuration/provider-endpoint-redacted.md)
- Code reference: [`G6-provider-switch-readiness.md`](../code-references/G6-provider-switch-readiness.md)
