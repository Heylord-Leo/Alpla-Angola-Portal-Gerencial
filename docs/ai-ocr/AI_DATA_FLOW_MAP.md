# AI Data Flow Map — Portal Gerencial OCR Feature

> **Version**: 1.0 | **Date**: 2026-06-18 | **Status**: Technical Documentation

---

## 1. High-Level Architecture

```mermaid
graph TB
    subgraph Browser["🌐 Browser (Trust Boundary 1)"]
        UI["React Frontend"]
        UPLOAD["File Upload Component"]
        REVIEW["OCR Review UI"]
    end

    subgraph Backend["🔒 Portal Backend (Trust Boundary 2)"]
        API["ASP.NET Core API"]
        AUTH["JWT Authentication"]
        VALID["Upload Validation"]
        SVC["DocumentExtractionService"]
        PROV["OpenAiDocumentExtractionProvider"]
        NORM["ContractOcrNormalisationService"]
        LOG["AdminLogWriter + SafePayload"]
    end

    subgraph Storage["💾 Local Storage (Trust Boundary 3)"]
        FS["File System: data/attachments/"]
        DB["SQL Server Database"]
        DEBUG["debug/openai-json/ (DEV only)"]
    end

    subgraph External["☁️ External AI Provider (Trust Boundary 4)"]
        OPENAI["OpenAI API (api.openai.com)"]
    end

    subgraph Config["⚙️ Configuration"]
        APPSETTINGS["appsettings.json"]
        DBCONFIG["DocumentExtractionSettings (DB)"]
        ENVVAR["Environment Variables"]
    end

    UI -->|"HTTPS + JWT"| API
    UPLOAD -->|"multipart/form-data"| VALID
    VALID -->|"validated file"| FS
    API -->|"extraction request"| SVC
    SVC -->|"file stream + context"| PROV
    PROV -->|"HTTPS POST (base64 images/text)"| OPENAI
    OPENAI -->|"JSON response"| PROV
    PROV -->|"ExtractionResultDto"| SVC
    SVC -->|"audit records"| DB
    SVC -->|"events"| LOG
    LOG -->|"sanitized entries"| DB
    PROV -.->|"DEV only"| DEBUG
    NORM -->|"normalised fields"| DB
    DB -->|"extracted fields"| API
    API -->|"JSON response"| REVIEW
    REVIEW -->|"user actions"| API
    API -->|"confirmed values"| DB

    Config --> SVC
```

---

## 2. Data Flow Sequence — Contract OCR

```mermaid
sequenceDiagram
    actor User
    participant Browser
    participant API as Portal API
    participant Valid as Validation
    participant FS as File Storage
    participant SVC as ExtractionService
    participant OAIP as OpenAI Provider
    participant OpenAI as OpenAI API
    participant DB as Database
    participant Log as AdminLogWriter

    User->>Browser: Upload contract PDF
    Browser->>API: POST /api/v1/contracts/{id}/documents (multipart)
    API->>Valid: Validate extension, size, MIME
    Valid-->>API: ✅ Accepted
    API->>FS: Save file (GUID-based name)
    API->>DB: Create ContractDocument record
    API-->>Browser: 200 OK (document metadata)

    User->>Browser: Click "Extract with OCR"
    Browser->>API: POST /api/v1/contracts/{id}/ocr
    API->>DB: Create ContractOcrExtractionRecord (PENDING)
    API->>SVC: ExtractAsync(fileStream, fileName, "contract")
    SVC->>SVC: Resolve active provider from settings
    SVC->>OAIP: ExtractAsync(stream, name, context)

    OAIP->>OAIP: Detect PDF native text
    alt Native text found
        OAIP->>OAIP: Extract text (TextFirst strategy)
    else No native text
        OAIP->>OAIP: Rasterize PDF pages (PdfiumViewer)
    end

    OAIP->>OAIP: Build system prompt + user content
    OAIP->>OpenAI: POST /v1/chat/completions (images/text)
    OpenAI-->>OAIP: JSON response
    OAIP->>OAIP: Parse JSON → ExtractionContractDto

    Note over OAIP: DEV only: save raw JSON to debug/

    OAIP-->>SVC: ExtractionResultDto
    SVC->>DB: Update ContractOcrExtractionRecord (COMPLETED)
    SVC->>DB: Create ContractOcrExtractedField rows
    SVC->>DB: Store RawJsonResult (64KB max)
    SVC->>Log: Write extraction event
    SVC-->>API: ExtractionResultDto
    API-->>Browser: 200 OK (extracted fields)

    Browser->>Browser: Display fields with OCR badges/chips

    loop For each extracted field
        User->>Browser: Review field
        alt Confirm
            User->>Browser: Click "Confirmar"
            Browser->>API: POST /api/v1/contracts/{id}/ocr/{recordId}/confirm
            API->>DB: Set ConfirmedByUser=true, FinalSavedValue
        else Edit + Confirm
            User->>Browser: Edit value, click "Confirmar"
            Browser->>API: POST confirm (with edited value)
            API->>DB: Set WasOverridden=true, FinalSavedValue=editedValue
        else Reject
            User->>Browser: Click "Limpar" / "Ignorar"
            Browser->>API: POST /api/v1/contracts/{id}/ocr/{recordId}/discard
            API->>DB: Set DiscardedByUser=true
        end
    end

    User->>Browser: Save contract form
    Browser->>API: PUT /api/v1/contracts/{id}
    API->>DB: Persist only confirmed values to Contract entity
    API->>DB: Set Contract.OcrValidatedByUser=true
    API->>Log: Write confirmation event
```

---

## 3. Data Flow Sequence — Invoice/Proforma OCR

```mermaid
sequenceDiagram
    actor User
    participant Browser
    participant API as Portal API
    participant FS as File Storage
    participant SVC as ExtractionService
    participant OAIP as OpenAI Provider
    participant OpenAI as OpenAI API
    participant DB as Database

    User->>Browser: Upload proforma PDF
    Browser->>API: POST /api/v1/attachments/upload/{requestId}
    API->>API: Validate extension, size, MIME, workflow stage
    API->>FS: Save file (GUID name, SHA-256 hash)
    API->>DB: Create RequestAttachment record
    API-->>Browser: 200 OK

    User->>Browser: Trigger OCR extraction
    Browser->>API: POST /api/v1/requests/{id}/extract
    API->>SVC: ExtractAsync(fileStream, fileName, "quotation")
    SVC->>OAIP: ExtractAsync(stream, name, context)

    OAIP->>OAIP: Triage: classify as INVOICE
    OAIP->>OAIP: Process PDF (text/rasterize)
    OAIP->>OAIP: Build invoice prompt (71-line template)
    OAIP->>OpenAI: POST /v1/chat/completions
    OpenAI-->>OAIP: JSON (header + line items)
    OAIP-->>SVC: ExtractionResultDto

    SVC->>DB: Create OcrExtractedItem rows (immutable)
    SVC-->>API: ExtractionResultDto
    API-->>Browser: 200 OK (extracted data)

    Browser->>Browser: Populate quotation draft UI
    User->>Browser: Review, edit, confirm line items
    User->>Browser: Submit quotation
    Browser->>API: POST /api/v1/requests/{id}/quotation
    API->>DB: Save user-confirmed values
```

---

## 4. Trust Boundary Diagram

```mermaid
graph LR
    subgraph TB1["Trust Boundary 1: Browser"]
        direction TB
        B1["User Input"]
        B2["File Selection"]
        B3["OCR Review UI"]
    end

    subgraph TB2["Trust Boundary 2: Portal Backend"]
        direction TB
        B4["Authentication (JWT)"]
        B5["Authorization (RBAC)"]
        B6["Input Validation"]
        B7["Extraction Service"]
        B8["Audit Logging"]
    end

    subgraph TB3["Trust Boundary 3: Data Storage"]
        direction TB
        B9["SQL Server (encrypted at rest)"]
        B10["Local File Storage"]
    end

    subgraph TB4["Trust Boundary 4: External AI"]
        direction TB
        B11["OpenAI API"]
        B12["OpenAI Model (GPT-4 Turbo)"]
    end

    TB1 ==>|"HTTPS + JWT Token"| TB2
    TB2 ==>|"SQL Connection"| TB3
    TB2 ==>|"HTTPS + API Key"| TB4

    style TB1 fill:#e0f2fe,stroke:#0284c7
    style TB2 fill:#dcfce7,stroke:#16a34a
    style TB3 fill:#fef3c7,stroke:#d97706
    style TB4 fill:#fce7f3,stroke:#db2777
```

### Data Crossing Trust Boundaries

| Boundary Crossing | Data Transferred | Protection | Risk |
|:---|:---|:---|:---|
| TB1 → TB2 (Browser → Backend) | JWT token, uploaded file, user actions | HTTPS, JWT validation, input validation | File contains untrusted content |
| TB2 → TB3 (Backend → Storage) | Extraction records, file bytes, audit entries | SQL connection string, file system ACLs | Data at rest protection |
| TB2 → TB4 (Backend → OpenAI) | Base64 images, extracted text, system prompt | HTTPS, API key in Authorization header | **Data leaves corporate boundary** |
| TB4 → TB2 (OpenAI → Backend) | JSON response with extracted fields | HTTPS, response validation | Untrusted AI output |

---

## 5. Data Sources and Sinks

| # | Data Element | Source | Processing | Sink | Sensitivity |
|:---|:---|:---|:---|:---|:---|
| 1 | Uploaded document (PDF/image) | User upload via browser | Validated, stored, sent to AI | File system + AI provider | Business documents — varies |
| 2 | Rasterized page images | PDF → PdfiumViewer | Converted to base64 | AI provider (in-memory only) | Same as source document |
| 3 | Extracted text (native PDF) | PDF text layer | Sent in prompt | AI provider (in-memory only) | Same as source document |
| 4 | System prompt | Hardcoded in provider | Prepended to user content | AI provider | Non-sensitive |
| 5 | AI JSON response | AI provider | Parsed, normalised | Database (RawJsonResult, fields) | Contains extracted document data |
| 6 | Normalised field values | AI response + normalisation | Type-converted, validated | Database (ContractOcrExtractedField) | Business data |
| 7 | User confirmation actions | User clicks in browser | Recorded with userId/timestamp | Database (confirmation audit fields) | Non-sensitive |
| 8 | Final saved values | User-confirmed data | Persisted to business entity | Database (Contract, Request) | Business data |
| 9 | Audit log entries | Service events | Sanitized via SafePayload | Database (AdminLogEntry) | Operational data |
| 10 | Debug JSON files | AI response (DEV only) | Raw JSON to disk | `debug/openai-json/` | ⚠️ Contains extracted data |

---

## 6. Data That MUST NOT Be Sent to AI Provider

> [!CAUTION]
> The following data categories must never be included in documents processed by the AI provider, unless explicitly approved by Legal, Information Security, and the AI CoE:

| # | Data Category | Reason | Current Control |
|:---|:---|:---|:---|
| 1 | Passwords, tokens, API keys | Credential exposure | Not expected in business documents |
| 2 | Employee payroll data | Personal data / GDPR | OCR not used in HR module |
| 3 | Employee health/medical data | Special category data / GDPR | OCR not used in HR module |
| 4 | Secret/confidential legal strategy | Legal privilege | 🟣 Requires document classification policy |
| 5 | Security-relevant source code | Intellectual property | Not expected in business documents |
| 6 | Customer personal data without consent | GDPR / data protection | 🟣 Requires assessment per document type |
| 7 | Supplier confidential pricing (non-disclosure) | Contractual obligation | 🟣 Depends on NDA terms |
| 8 | Government-classified documents | Regulatory | Not applicable currently |
| 9 | Biometric or identification data | Special category data | Not expected in business documents |
| 10 | Financial account numbers / IBAN | Financial data exposure | May appear in invoices — assess risk |

### Recommended Mitigations

1. **Document classification policy**: Define which document types are approved for AI processing
2. **Pre-processing PII detection**: Consider scanning for sensitive patterns before AI submission
3. **User training**: Inform users about document types that should NOT be processed via OCR
4. **Admin controls**: Allow administrators to restrict OCR to specific document type codes

---

## Evidence Package References

| Area | Evidence Files |
|:---|:---|
| Debug logging guard (G1) | `evidence/code-references/G1-debug-logging-guard.md`, `evidence/configuration/debug-raw-payload-logging-redacted.md` |
| Policy controls (G2) | `evidence/code-references/G2-ai-ocr-policy-controls.md`, `evidence/configuration/ai-ocr-policy-redacted.md` |
| Provider endpoint (G6) | `evidence/code-references/G6-provider-switch-readiness.md`, `evidence/configuration/provider-endpoint-redacted.md` |
| System Logs (G8) | `evidence/code-references/G8-system-logs-integration.md` |
| API samples | `evidence/api/extraction-response-sample-redacted.json`, `evidence/api/upload-rejection-response.json` |

> 👉 Full evidence index: [`evidence/EVIDENCE_INDEX.md`](evidence/EVIDENCE_INDEX.md)
