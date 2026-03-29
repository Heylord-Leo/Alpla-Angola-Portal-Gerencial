# Architecture

## Overview

High-level description of the system architecture and major components.

## Architecture Model

This project follows the 3-layer DOE architecture:

1. **Directive Layer** (`directives/`) — SOPs / what to do
2. **Orchestration Layer** (AI agent) — routing, sequencing, recovery, documentation
3. **Execution Layer** (`execution/`) — deterministic scripts

## Components

### Directives

- `directives/[name].md` — [Purpose]

### Execution Scripts

- `execution/[name].py` — [Purpose]
- `execution/[name].py` — [Purpose]

### Data / Storage

- Local temp files: `.tmp/`
- Deliverables: [Google Sheets / Slides / DB / SharePoint / etc.]
- Logs: `.tmp/logs/` (if applicable)

## Data Flow

### Master Data Strategy

All reusable reference data (e.g., Units, Currencies, Plants) must follow the project's Master Data standardization process. Hardcoding is strictly discouraged. Instead, reference lists are driven by database entities seeded for local-dev stability, exposed via read-only `/api/v1/lookups` endpoints, and constrained by soft-delete paradigms. See `MASTER_DATA_GUIDELINES.md` for the comprehensive 8-step operating procedure.

## Integrations

### Document Extraction Architecture

The portal uses a provider-agnostic abstraction for extracting structured data from documents (Quotation import flow).

- **Abstraction**: `IDocumentExtractionService` (Orchestrator) and `IDocumentExtractionProvider` (Engine).
- **Internal Model**: `ExtractionResultDto` (Stable canonical model).
- **Field Propagation**: All extracted fields must follow the [Document Extraction Field Propagation Standard](DOCUMENT_EXTRACTION_FIELD_PROPAGATION_STANDARD.md) to ensure front-to-back integrity.
- **Provider Selection**: Driven by configuration key `DocumentExtraction:Provider` (e.g., `LOCAL_OCR`). Defaults to `LOCAL_OCR` if missing or invalid.
- **Legacy Support**: `RequestsController` utilizes a dedicated mapper to bridge the internal stable model to the legacy JSON shape expected by the current frontend.

## Authentication / Credentials

Describe where credentials live and how they are used.

- `.env` for environment variables
- `credentials.json` / `token.json` for Google OAuth (gitignored)
- [Other credential strategy]

## Admin Observability Layer

### Admin Audit Log (`AdminLogWriter`)

The portal uses a **dedicated admin audit log** separate from the main application logging pipeline (`ILogger` → console/file).

- **Entity**: `AdminLogEntry` — persisted in the SQL Server database via `ApplicationDbContext`.
- **Writer**: `AdminLogWriter` (Scoped DI service). Services explicitly call `WriteAsync(...)` to persist targeted operational events. This is **not** an interceptor of all framework logs.
- **Fail-safe**: `WriteAsync` catches all persistence exceptions and logs them to the standard `ILogger`. Failed log writes never propagate to the caller or break the main request flow.
- **Correlation ID**: `CorrelationIdMiddleware` runs first in the pipeline. It accepts `X-Correlation-ID` from incoming headers or generates a short GUID. The ID is stored in `HttpContext.Items["CorrelationId"]`, returned in the response `X-Correlation-ID` header, and persisted in each `AdminLogEntry`. Allows tracing all log entries related to a single API call from **Logs do Sistema**.
- **Sanitization**: `SafePayload` applies two-layer sanitization to all persisted payloads: (1) explicit known-field masking (`ApiKey`, `Token`, `Password`, etc.) and (2) regex redaction as a secondary safeguard.

**Current instrumented events** (`DocumentExtractionSettingsService`):

| EventType | Level |
|---|---|
| `OCR_SETTINGS_SAVED` | Information |
| `OCR_SETTINGS_VALIDATION_FAILED` | Warning |
| `OCR_SETTINGS_SAVE_FAILED` | Error |
| `OCR_PROVIDER_TEST_OK` | Information |
| `OCR_PROVIDER_TEST_FAILED` | Warning / Error |


## Versioning Strategy

- Semantic Versioning (MAJOR.MINOR.PATCH) / [Other]
- Changelog location: `docs/CHANGELOG.md`
- Version file: `docs/VERSION.md`

## Open Technical Questions

- [Question]
- [Question]

## Future Improvements

- [Improvement]
- [Improvement]
