# Integration Management — Architecture Review

> **Phase A — Local Architecture Review (COMPLETE)**
> Version: v2.153.0
> Date: 2026-05-25
> Scope: Local codebase analysis only. No deployment. No production changes. All findings implemented in Phases B–D.

---

## 1. Current Configuration Sources

The Portal Gerencial backend uses a **layered configuration cascade** following .NET 8 conventions:

| Priority | Source | Scope | Notes |
|----------|--------|-------|-------|
| 1 (highest) | IIS AppPool Environment Variables | Per-environment | Used in Staging for `ConnectionStrings__DefaultConnection` |
| 2 | `appsettings.{Environment}.json` | Per-environment | `appsettings.Development.json` exists, no Staging/Production file |
| 3 | `appsettings.json` | Global defaults | Contains structure with empty placeholder values |
| 4 | Database rows | Runtime | `DocumentExtractionSettings`, `SmtpSettings`, `IntegrationProviderSettings` tables |

### Configuration Files

| File | Tracked in Git | Contains Secrets | Risk |
|------|----------------|------------------|------|
| [`appsettings.json`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Api/appsettings.json) | ✅ Yes | ⚠️ Dev JWT Secret (hardcoded) | Low — placeholder values |
| [`appsettings.Development.json`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Api/appsettings.Development.json) | ⛔ **YES — TRACKED** | ⛔ **Plaintext Primavera/Innux SQL credentials** | **CRITICAL** |
| `appsettings.Staging.json` | N/A | N/A | Does not exist |
| `appsettings.Production.json` | N/A | N/A | Does not exist |
| `.env` / `.env.production` | N/A | N/A | Not used by backend |

### Database-Backed Settings (Existing)

| Entity | Table | Has Encryption | Has Test | Has UI |
|--------|-------|----------------|----------|--------|
| [`DocumentExtractionSettings`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/DocumentExtractionSettings.cs) | `DocumentExtractionSettings` | ❌ No secrets stored | ✅ OpenAI test | ✅ Backend API |
| [`SmtpSettings`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/SmtpSettings.cs) | `SmtpSettings` | ✅ AES-256 (`EncryptedPassword`) | ✅ SMTP test | ✅ Backend API |
| [`IntegrationProvider`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/IntegrationProvider.cs) | `IntegrationProviders` | N/A (registry only) | ✅ Health check | ✅ Health cards |
| [`IntegrationProviderSettings`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/IntegrationProviderSettings.cs) | `IntegrationProviderSettings` | ✅ AES-256 (`EncryptedPassword`, `ApiKeyEncrypted`) | Via provider | ❌ **No management UI** |
| [`IntegrationConnectionStatus`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Domain/Entities/IntegrationConnectionStatus.cs) | `IntegrationConnectionStatuses` | N/A (status only) | N/A | ✅ Health cards |

---

## 2. Per-Provider Configuration Analysis

### OpenAI / ChatGPT API

| Aspect | Current State |
|--------|---------------|
| API Key source | `_configuration["OPENAI_API_KEY"]` — environment variable only ([DocumentExtractionSettingsService.cs:271](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionSettingsService.cs#L271)) |
| Non-secret config | DB-backed via `DocumentExtractionSettings` (model, timeouts, enabled flags) |
| Fallback | `appsettings.json` → `DocumentExtraction:OpenAi` section |
| Test connection | ✅ Calls `https://api.openai.com/v1/models` with Bearer token |
| UI management | ✅ `DocumentExtractionSettingsController` — get/update/test |
| Secret storage | ❌ Environment variable only — no DB-backed API key management |
| Gap | No UI to set/rotate `OPENAI_API_KEY` from the Portal |

### Primavera ERP

| Aspect | Current State |
|--------|---------------|
| Config source | `_configuration.GetSection("Integrations:Primavera")` — reads from appsettings only ([PrimaveraConnectionFactory.cs:46](file:///c:/dev/alpla-portal/src/backend/AlplaPortal/Infrastructure/Services/Integration/PrimaveraConnectionFactory.cs#L46)) |
| Credentials | `Username` + `Password` from `Integrations:Primavera` config section |
| Companies | Per-company `DatabaseName` from `Integrations:Primavera:Companies:{key}:DatabaseName` |
| Test connection | ✅ Via `PrimaveraIntegrationProvider` → `IIntegrationProvider.TestConnectionAsync()` |
| DB settings entity | ✅ `IntegrationProviderSettings` exists (with `Server`, `InstanceName`, `Username`, `EncryptedPassword`) |
| Gap | **DB settings entity is seeded but never read by `PrimaveraConnectionFactory`** — config reads appsettings only |
| Gap | ⛔ **Plaintext `sa` / `P@ssw0rd` in `appsettings.Development.json` tracked in Git** |
| Gap | No UI to manage Primavera connection settings |

### Innux Time & Attendance

| Aspect | Current State |
|--------|---------------|
| Config source | `_configuration.GetSection("Integrations:Innux")` — reads from appsettings only ([InnuxConnectionFactory.cs:45](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Integration/InnuxConnectionFactory.cs#L45)) |
| Credentials | `Username` + `Password` from `Integrations:Innux` config section |
| Test connection | ✅ Via `InnuxIntegrationProvider` → `IIntegrationProvider.TestConnectionAsync()` |
| DB settings entity | ✅ `IntegrationProviderSettings` exists but **never read** by `InnuxConnectionFactory` |
| Gap | ⛔ **Plaintext `sa` / credentials in `appsettings.Development.json` tracked in Git** |
| Gap | No UI to manage Innux connection settings |

### Email / SMTP

| Aspect | Current State |
|--------|---------------|
| Config source | Dual: DB-backed `SmtpSettings` table + `appsettings.json` → `SmtpSettings` section |
| Credentials | AES-256 encrypted via [`AesEncryptionHelper`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Security/AesEncryptionHelper.cs) |
| Test connection | ✅ Via `SmtpSettingsService.TestConnectionAsync()` |
| UI management | ✅ `SmtpSettingsController` — get/update/test |
| Fallback | appsettings → DB override (DB takes priority when present) |
| Gap | ✅ **Already properly implemented** — DB-backed with encryption, fallback, test, and API |

---

## 3. Security Findings

### ⛔ CRITICAL — Plaintext Credentials in Git

**File**: `src/backend/AlplaPortal.Api/appsettings.Development.json`
**Status**: **Tracked in Git** (not in `.gitignore`)

Contains plaintext:
- Primavera SQL `Username: "sa"` / `Password: "P@ssw0rd"`
- Innux SQL `Username: "sa"` / `Password` (actual credentials)
- SMTP password placeholder `[USE_ENVIRONMENT_OR_USER_SECRETS]`
- JWT secret key

**Recommendation**: Add to `.gitignore`, remove from tracking, rotate credentials, use `dotnet user-secrets` for local development.

### ⚠️ AES Encryption Key Material

The [`AesEncryptionHelper`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Security/AesEncryptionHelper.cs) uses a hardcoded fallback key material (`AlplaPortal_SmtpSettings_AES256_Key_2026`). This should be overridden via `AppConfig:EncryptionKey` in environment variables for production.

### ⚠️ IntegrationProviderSettings Entity Unused

The `IntegrationProviderSettings` table and entity are fully designed with `EncryptedPassword` and `ApiKeyEncrypted` columns, but **neither `PrimaveraConnectionFactory` nor `InnuxConnectionFactory` reads from it**. All configuration comes from `IConfiguration` (appsettings).

---

## 4. Which Settings Are Local-Only

| Setting | Local-Only | Environment Variable | DB-Backed |
|---------|------------|---------------------|-----------|
| Primavera Server/Auth/Password | ✅ appsettings.Development.json | ❌ | ❌ (entity exists, not read) |
| Innux Server/Auth/Password | ✅ appsettings.Development.json | ❌ | ❌ (entity exists, not read) |
| OpenAI API Key | ❌ | ✅ `OPENAI_API_KEY` | ❌ |
| OpenAI Model/Timeouts | ❌ | ❌ | ✅ `DocumentExtractionSettings` |
| SMTP Server/Port/Sender | ✅ appsettings.Development.json | ❌ | ✅ `SmtpSettings` |
| SMTP Password | ✅ placeholder only | ❌ | ✅ AES-encrypted |
| JWT Secret | ✅ appsettings (hardcoded) | Should be env var | ❌ |
| DefaultConnection | ✅ appsettings.Development.json | ✅ Staging: IIS env var | ❌ |

---

## 5. Services That Need Refactoring

### Must Refactor (Phase C)

| Service | Current Source | Target | Effort |
|---------|---------------|--------|--------|
| [`PrimaveraConnectionFactory`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Integration/PrimaveraConnectionFactory.cs) | `IConfiguration` only | DB settings → appsettings fallback | Medium |
| [`InnuxConnectionFactory`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Integration/InnuxConnectionFactory.cs) | `IConfiguration` only | DB settings → appsettings fallback | Medium |
| [`DocumentExtractionSettingsService`](file:///c:/dev/alpla-portal/src/backend/AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionSettingsService.cs) | `OPENAI_API_KEY` from env var only | DB-backed encrypted API key → env var fallback | Low |

### Already Complete (No Refactoring Needed)

| Service | Status |
|---------|--------|
| `SmtpSettingsService` | ✅ Full DB-backed + AES encryption + fallback + test |
| `IntegrationHealthService` | ✅ Reads both DB and config for enabled/settings status |

---

## 6. Existing Foundation to Reuse

The codebase already provides a **significant foundation** that the Integration Management module can build upon:

### Database Schema (Already Migrated)

- `IntegrationProviders` table — PRIMAVERA and INNUX are seeded (migration `20260414131442`)
- `IntegrationProviderSettings` table — FK to providers, has `EncryptedPassword`, `ApiKeyEncrypted`, `Server`, `DatabaseName`, `InstanceName`, `Username`, `ApiBaseUrl`, `TimeoutSeconds`, `AdditionalConfig`
- `IntegrationConnectionStatuses` table — persisted health check results

### Backend Services

- `IntegrationHealthService` — provider-agnostic health check orchestrator
- `IntegrationHealthController` — `GET /api/admin/integrations/health`, `POST /{providerCode}/test-connection`
- `PrimaveraIntegrationProvider` / `InnuxIntegrationProvider` — `IIntegrationProvider` implementations
- `AesEncryptionHelper` — AES-256 encryption for secrets

### Frontend

- `IntegrationHealth.tsx` — 528-line integration health dashboard at `/admin/health`
- `api.admin.integrations.getHealth()` / `testConnection()` — API client methods
- `AdministratorWorkspace.tsx` — admin tile grid with existing "Saúde das Integrações" tile

---

## 7. Recommended Target Architecture

### Configuration Resolution Order

```
1. Database (IntegrationProviderSettings) → highest priority
   ↓ if null/empty
2. Environment Variables / appsettings → fallback
   ↓ if null/empty
3. Safe disabled state → never crash, return "not configured"
```

### New Components Required

| Layer | Component | Purpose |
|-------|-----------|---------|
| **Backend Controller** | `IntegrationSettingsController` | CRUD + secret rotation + test for provider settings |
| **Backend Service** | `IIntegrationSettingsService` | Orchestrates DB read/write with AES encryption for secrets |
| **Backend Refactor** | `PrimaveraConnectionFactory` | Add DB settings resolution before `IConfiguration` fallback |
| **Backend Refactor** | `InnuxConnectionFactory` | Add DB settings resolution before `IConfiguration` fallback |
| **Backend Refactor** | `DocumentExtractionSettingsService` | Add DB-backed encrypted API key before env var fallback |
| **Frontend Page** | `IntegrationSettings.tsx` | Full integration management UI at `/admin/integrations` |
| **Frontend API** | `api.admin.integrationSettings.*` | API client for settings CRUD |
| **Migration** | `AddIntegrationManagementUI` | Seed OPENAI and SMTP providers if not already present |

### Security Model

| Principle | Implementation |
|-----------|---------------|
| Encryption at rest | `AesEncryptionHelper` (existing) with configurable key material |
| Secret masking | API GET never returns decrypted secrets; returns `hasPassword: true/false` |
| Secret rotation | Dedicated `POST /secret` endpoint for password/key replacement |
| Audit trail | `AdminLogWriter` for all settings changes (existing pattern) |
| Authorization | `[Authorize(Roles = "System Administrator")]` (existing pattern) |
| Read-only mode | `IsReadOnly` flag on provider for Primavera/Innux in Production |
