# AI OCR Compliance Evidence Package — README

> **Version**: 2.0 | **Date**: 2026-06-18

## Overview

This directory contains the complete evidence package for the AI-assisted OCR/document extraction feature in the Portal Gerencial.

## Quick Access

| Document | Purpose |
|:---|:---|
| [EVIDENCE_INDEX.md](EVIDENCE_INDEX.md) | **Master traceability matrix** — start here |
| [README.md](README.md) | This file |

## Directory Structure

```
evidence/
├── EVIDENCE_INDEX.md          ← Master traceability matrix
├── README.md                  ← This file
├── api/                       ← Sanitized API response samples
│   ├── extraction-response-sample-redacted.json
│   └── upload-rejection-response.json
├── build/                     ← Build validation results
│   ├── backend-build-result.md
│   └── frontend-build-result.md
├── code-references/           ← Source code evidence per hardening gap
│   ├── G1-debug-logging-guard.md
│   ├── G2-ai-ocr-policy-controls.md
│   ├── G3-prompt-injection-defense.md
│   ├── G4-retention-cleanup-service.md
│   ├── G5-malware-scan-extension.md
│   ├── G6-provider-switch-readiness.md
│   └── G8-system-logs-integration.md
├── configuration/             ← Redacted configuration evidence
│   ├── document-extraction-settings-redacted.md
│   ├── debug-raw-payload-logging-redacted.md
│   ├── ai-ocr-policy-redacted.md
│   ├── retention-policy-redacted.md
│   └── provider-endpoint-redacted.md
├── logs/                      ← Sanitized log samples (not live data)
│   ├── OCR_EXTRACTION_STARTED-sanitized.json
│   ├── OCR_EXTRACTION_COMPLETED-sanitized.json
│   ├── OCR_EXTRACTION_FAILED-sanitized.json
│   ├── OCR_FEATURE_DISABLED-sanitized.json
│   ├── OCR_MODULE_BLOCKED-sanitized.json
│   ├── OCR_DOCUMENT_TYPE_BLOCKED-sanitized.json
│   ├── OCR_CLEANUP_EXECUTED-sanitized.json
│   ├── G1-metadata-only-log-sample.json
│   ├── G3-prompt-version-log-sample.json
│   └── G5-noop-file-scan-warning-sample.json
├── screenshots/               ← Screenshot placeholders with capture instructions
│   ├── README.md
│   ├── SCR-28-debug-logging-disabled.md
│   ├── SCR-29-ai-ocr-policy-config.md
│   ├── SCR-30-provider-settings-masked.md
│   ├── SCR-31-system-logs-ocr-filter.md
│   └── SCR-32-ocr-log-detail-safe-payload.md
├── sql/                       ← Evidence SQL queries (execute on TEST)
│   ├── ocr_system_log_events.sql
│   ├── ocr_extraction_records.sql
│   ├── ocr_field_review_evidence.sql
│   ├── ocr_settings_evidence.sql
│   └── ocr_cleanup_evidence.sql
├── test-results/              ← Test execution evidence
│   ├── poc-test-execution-status.md
│   ├── G3-prompt-injection-test-result.md
│   ├── G4-cleanup-validation.md
│   ├── G5-file-scan-placeholder-validation.md
│   └── G6-provider-endpoint-validation.md
└── test-samples/              ← Test input files
    └── prompt_injection_sample.txt
```

## Security Note

> [!CAUTION]
> - All files in this directory are sanitized. No real API keys, secrets, or live data are included.
> - Log samples use placeholder UUIDs and masked emails.
> - SQL queries include masking for sensitive fields.
> - Screenshots must be captured with appropriate redactions per the capture guide.
