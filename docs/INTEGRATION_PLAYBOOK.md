# Integration Playbook — Portal Gerencial

> **Status**: Phase 5A — Primavera request validation against master data (read-only, multi-database).
> **Last Updated**: 2026-04-14

This document serves as the integration implementation guide for the Portal Gerencial. It documents the generic provider foundation, explains how to add new providers, and establishes patterns for consistent future integration work.

---

## Architecture Overview

The integration system follows a **generic provider pattern** modeled after the Document Extraction subsystem. It is intentionally designed to support multiple external providers (Primavera, Innux, AlplaPROD, AlplaMART, future SQL/API systems) and multiple business domains per provider (employees, materials, suppliers, departments, cost centers, attendance, etc.).

### Key Design Principles

1. **Provider-agnostic foundation** — No entity, interface, or service is coupled to a specific provider or business domain.
2. **Capabilities as metadata** — Each provider declares its capabilities via a JSON array. This is a Phase 0 lightweight approach that may evolve to a relational structure if capability management becomes complex.
3. **Settings cascade** — Connection settings can come from `appsettings.json` (baseline) or be overridden by DB-persisted `IntegrationProviderSettings` records.
4. **Strict separation** — `IntegrationProviderSettings` is connection-oriented only. Business/domain-specific settings (sync rules, field mappings) belong in separate, dedicated entities.
5. **Read-only foundation** — The current foundation supports health checks and connection diagnostics only. No data sync, writes, or business operations are implemented yet.
6. **Activation requires configuration** — Having a provider implementation registered does not mean the provider is automatically active. Activation depends on explicit configuration (`Enabled = true` in appsettings.json) and valid connection settings. A provider can be implemented but not enabled.

### Layer Architecture

```
┌─────────────────────────────────────────────┐
│  Frontend: IntegrationHealth.tsx            │
│  (data-driven, renders from API)            │
├─────────────────────────────────────────────┤
│  API: IntegrationHealthController           │
│  GET  /api/admin/integrations/health        │
│  POST /api/admin/integrations/{code}/test   │
├─────────────────────────────────────────────┤
│  Service: IntegrationHealthService          │
│  (resolves providers, aggregates status)    │
├─────────────────────────────────────────────┤
│  Interface: IIntegrationProvider            │
│  (Code, Type, ConnectionType, TestConn)     │
├─────────────────────────────────────────────┤
│  Domain Entities:                           │
│  IntegrationProvider                        │
│  IntegrationConnectionStatus                │
│  IntegrationProviderSettings                │
├─────────────────────────────────────────────┤
│  Constants:                                 │
│  IntegrationStatusCodes                     │
│  IntegrationLogEventTypes                   │
└─────────────────────────────────────────────┘
```

---

## How to Add a New Provider

### Step 1: Register Provider in Database

Add a seed record in `ApplicationDbContext.OnModelCreating` or insert directly into `IntegrationProviders`:

```csharp
new IntegrationProvider
{
    Id = <next_id>,
    Code = "PROVIDER_CODE",           // Unique, uppercase
    Name = "Display Name",
    ProviderType = "ERP",             // ERP, BIOMETRIC, PRODUCTION, API, OTHER
    ConnectionType = "SQL",           // SQL, REST_API, SOAP, FILE
    Description = "...",
    Environment = "PRODUCTION",
    IsEnabled = false,                // Enable when implementation is ready
    IsPlanned = true,                 // Set to false when implementation exists
    DisplayOrder = <order>,
    Capabilities = "[\"EMPLOYEES\",\"MATERIALS\"]",
    CreatedAtUtc = DateTime.UtcNow
}
```

Also add a corresponding `IntegrationConnectionStatus` record.

### Step 2: Add Configuration (appsettings.json)

Add a section under `Integrations`:

```json
"Integrations": {
    "NewProvider": {
        "Enabled": false,
        "Server": "",
        "DatabaseName": "",
        "AuthenticationMode": "SQL",
        "TimeoutSeconds": 15
    }
}
```

### Step 3: Implement IIntegrationProvider

Create a concrete provider class:

```csharp
public class NewProviderIntegration : IIntegrationProvider
{
    public string Code => "PROVIDER_CODE";
    public string ProviderType => "ERP";
    public string ConnectionType => "SQL";

    public async Task<IntegrationConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        // Lightweight read-only connectivity probe
        // For SQL: open connection, execute diagnostic query, close
        // Recommended: SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName
        // For API: send a health/ping request
        // See PrimaveraIntegrationProvider.cs for reference implementation
    }
}
```

### Step 4: Register in DI (Program.cs)

```csharp
builder.Services.AddScoped<IIntegrationProvider, NewProviderIntegration>();
```

### Step 5: Update Seed Data

Set `IsPlanned = false` for the provider. Do **not** automatically set `IsEnabled = true` — activation should depend on valid configuration, not on provider existence. Set `IsEnabled = false` and let environment-specific configuration control activation.

### Step 6: Create EF Migration

```bash
dotnet ef migrations add EnableNewProvider --project AlplaPortal.Infrastructure --startup-project AlplaPortal.Api
```

### Step 7: Verify

- Navigate to `/admin/health`
- Provider should appear as "Active" with a "Test Connection" button
- Test connection should execute and update status

---

## Where Settings Live

| Setting Type | Location | Entity |
|---|---|---|
| Provider registration | Database | `IntegrationProvider` |
| Connection config (baseline) | `appsettings.json` → `Integrations` section | N/A |
| Connection config (DB override) | Database | `IntegrationProviderSettings` |
| Runtime status | Database | `IntegrationConnectionStatus` |

Settings cascade: DB override → appsettings.json → defaults.

---

## How Health Checks Work

1. `IntegrationHealthService.GetHealthSummaryAsync()` is called by the controller
2. It reads all `IntegrationProvider` records from DB (with ConnectionStatus and Settings)
3. For each provider, it resolves a matching `IIntegrationProvider` implementation from DI
4. It assembles `IntegrationProviderStatusDto` with metadata + status
5. Planned/disabled providers get status without attempting connection
6. The `canTestConnection` flag is only `true` when: enabled + not planned + has settings + has implementation
7. Manual test triggers `TestProviderConnectionAsync()`, which calls the provider's `TestConnectionAsync()`, persists the result to `IntegrationConnectionStatus`, and logs the event via `AdminLogWriter`

---

## How Logging Works

All integration events use `AdminLogWriter` with standard event types from `IntegrationLogEventTypes`:

| Event Type | When |
|---|---|
| `INTEGRATION_CONNECTION_TEST_STARTED` | Manual test initiated |
| `INTEGRATION_CONNECTION_TEST_OK` | Test succeeded |
| `INTEGRATION_CONNECTION_TEST_FAILED` | Test failed |
| `INTEGRATION_HEALTH_RETRIEVED` | Health summary requested |
| `INTEGRATION_SETTINGS_UPDATED` | Provider settings changed |

All events use `LogSource = "IntegrationHealth"` and carry correlation IDs from the middleware.

New providers should reuse these event types. Do not create one-off event names.

---

## Future Integration Domains

The foundation is designed to support the following domains per provider:

### Primavera
- Employee profile lookup
- Article/material catalog
- Suppliers
- Departments
- Cost centers

### Innux
- Attendance/biometric status
- Terminal linkage
- Operational employee attendance

### Future
- AlplaPROD — production data
- AlplaMART — market/supply data
- API-based services

Future domain services should be layered as separate interfaces (e.g., `IPrimaveraEmployeeService`, `IPrimaveraArticleService`) on top of concrete provider implementations — not added to the `IIntegrationProvider` base contract.

---

## Status Codes Reference

| Code | Meaning | UI Label |
|---|---|---|
| `HEALTHY` | Connected and responsive | Operacional |
| `UNHEALTHY` | Configured but failing | Com Falhas |
| `UNREACHABLE` | Network/timeout failure | Inacessível |
| `NOT_CONFIGURED` | Settings incomplete | Não Configurado |
| `PLANNED` | Future/roadmap item | Prevista |

Backend uses stable code constants (`IntegrationStatusCodes`). Frontend maps to display labels separately.

---

## Phase 1A Reference: PrimaveraIntegrationProvider

> **Implemented**: 2026-04-14
> **Scope**: Connection health / diagnostics only. No business-domain queries.

### What it does

`PrimaveraIntegrationProvider` is the first concrete `IIntegrationProvider` implementation. It validates SQL connectivity to a Primavera ERP SQL Server instance and returns diagnostic identity information.

### Configuration flow

1. Reads `Integrations:Primavera` from `appsettings.json` (shared settings: Server, Instance, Auth, Timeout)
2. Reads per-company target from `Integrations:Primavera:Companies:{COMPANY}:DatabaseName`
3. Validates required fields: `Server`, `Enabled`, per-company `DatabaseName`
4. Supports both `SQL` and `WINDOWS` authentication modes
5. Builds `SqlConnection` via `PrimaveraConnectionFactory`
6. Executes: `SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName`
7. Returns server identity and response time in the test result

### Phase 1B Reference: Runtime Configuration Stabilization

> **Implemented**: 2026-04-14
> **Scope**: Diagnostic success, authentication boundaries, and timeout resolution.

Current Phase: **Phase 2D (Primavera Multi-Database Support)**

## 🚀 Phase 2A: Primavera Employee Lookup (Read-Only)

> **Implemented**: 2026-04-14
> **Scope**: Internal API mappings for read-only `dbo.Funcionarios` joining `dbo.Departamentos`.
> **Rule**: No biometrics, no implicit states (exposing pure `TerminationDate` instead of computed `IsActive`), and constrained search.

---

## 🚀 Phase 2B: Innux Real Connection Health

> **Implemented**: 2026-04-14
> **Scope**: Connection health / diagnostics only for Innux Time and Attendance (`TIME_ATTENDANCE` provider designation). No business-domain queries (e.g. biometrics / employee lookups).

### Mandatory Rules for Phase 2B

1. **Explicit Credentials Only:** Like Primavera, the Innux integration strictly rejects implicit session usage unless `WINDOWS` mode is explicitly toggled via integration configuration. Fallback mechanisms are intentionally left empty, trapping unsafe execution logic. Error responses explicitly mirror real endpoint capability (`Login failed...`).
2. **Provider Separation:** Implemented via `InnuxIntegrationProvider`. Maintains abstraction integrity inside Phase 1 infrastructure patterns.
3. **Database Injection:** Seed records shifted from `BIOMETRICS` mapped to `IsPlanned = true` to the definitive `TIME_ATTENDANCE` mapping tracking `IsPlanned = false`, validating dynamic capabilities on `/admin/health` immediately.

Phase 2A introduces the first concrete business-domain read service, explicitly mapped directly against the verified Phase 1B integration runtime.

### Boundary Enforcement
1. **Primavera Only:** Innux data (such as biometric checks, attendance, or dynamic tracking) is strictly excluded in this phase to prevent early coupling.
2. **Read-Only DTOs:** Returned records map the exact dataset found in `dbo.Funcionarios` with a single join on `dbo.Departamentos`.
3. **No Inference:** Activity (`IsActive`) implies more complex logic (termination + suspension flags). The API surfaces exact values (`TerminationDate`, `IsTemporarilyInactive`) passing derivation logic back to caller semantics rather than hardcoding potentially fragile heuristics.
4. **Endpoint Constrains:** Employee Search endpoints limit queries to a minimum sequence length (>= 2 chars) matching up to 50 ordered results to restrict unbounded sequence table scans.

---

## 🚀 Phase 2D: Primavera Multi-Database Support

> **Implemented**: 2026-04-14
> **Scope**: Multi-database targeting for Primavera. One provider, multiple business database targets.

### Problem

Primavera is not a single business database. The environment has two productive master databases:

| Company | Primavera Database | Portal Company Entity |
|---|---|---|
| AlplaPLASTICO | `PRI297514001` | `Id=1` |
| AlplaSOPRO | `PRI297514003` | `Id=2` |

### Strategy: One Provider, Multiple Database Targets

Database targeting is a **domain-service concern**, not a provider concern:

- `IIntegrationProvider` (`PRIMAVERA`) stays unchanged — handles provider-level health only
- Domain services (employee lookup, future material/supplier reads) receive an explicit `PrimaveraCompany` target
- A shared `PrimaveraConnectionFactory` resolves connections for any configured company

### Key Components

| Component | Purpose |
|---|---|
| `PrimaveraCompany` enum | Compile-time safe company selector (`ALPLAPLASTICO`, `ALPLASOPRO`) |
| `PrimaveraConnectionFactory` | Shared, domain-neutral connection factory. Reads shared settings + per-company DatabaseName. Reusable by all future Primavera domain services |
| `IPrimaveraEmployeeService` | Company-aware interface — every call requires explicit `PrimaveraCompany` |
| `PrimaveraEmployeeDto.SourceCompany` | Identifies which database the record came from |

### Configuration Model

Shared connection settings at the Primavera level; per-company `DatabaseName` under `Companies`:

```json
"Integrations": {
    "Primavera": {
        "Enabled": true,
        "Server": "<server>",
        "InstanceName": "<instance>",
        "AuthenticationMode": "SQL",
        "Username": "<user>",
        "Password": "<password>",
        "TimeoutSeconds": 15,
        "Companies": {
            "ALPLAPLASTICO": {
                "DatabaseName": "<database_name>",
                "Enabled": true
            },
            "ALPLASOPRO": {
                "DatabaseName": "<database_name>",
                "Enabled": true
            }
        }
    }
}
```

### API Company Selection

Company is selected via query parameter:

```
GET /api/admin/integrations/primavera/employees/{code}?company=ALPLAPLASTICO
GET /api/admin/integrations/primavera/employees/search?company=ALPLASOPRO&q=Silva
```

### Health Check Strategy (Option A)

Provider health tests the **first configured company** (default target). Status is `HEALTHY` or `UNHEALTHY`. No `DEGRADED` status in this phase.

Diagnostic logs document which company/database the health check is testing against.

### Error Handling

| Scenario | HTTP | Error Message |
|---|---|---|
| Missing `company` parameter | 400 | `Company parameter is required. Valid values: ALPLAPLASTICO, ALPLASOPRO` |
| Invalid `company` value | 400 | `Invalid company. Valid values: ALPLAPLASTICO, ALPLASOPRO` |
| Company not configured/disabled | 503 | `Company 'X' is not configured/disabled in configuration` |
| Credentials missing | 503 | `Primavera credentials not configured` |
| SQL connection failure | 502 | `Failed to communicate with Primavera SQL backend for company X` |
| Employee not found | 404 | `Employee not found` |

### Decisions

- **DEC-103**: One Primavera provider, multiple business database targets. Database selection in domain services.
- **DEC-104**: Option A health — tests first configured company only. DEGRADED deferred.
- **DEC-105**: Company selection via query parameter. Route-segment style deferred.

---

The integration stack was stabilized against real Primavera infrastructure. The following operational rules apply permanently to all providers moving forward:

1. **Explicit Identity Required**: The provider must **never** rely on the ambient desktop/session user. Connections must execute exclusively using explicitly configured identities defined in the configuration layer (`appsettings.json` or Database).
2. **SQL Authentication is the Canonical Path**: For the current environment (`AOVIA1VMS012`), connection reliability depends on providing valid `sa` (or scoped SQL proxy) credentials with `AuthenticationMode: "SQL"`. Windows Authentication proxying from the Web server context is unsupported and discouraged due to "double-hop" constraints and pipeline drops.
3. **Optimized Link Negotiation**: Connection string generation enforces `Encrypt=Optional` to gracefully downgrade legacy connection handshakes without triggering 21-second timeouts due to missing PKI infrastructure on legacy SQL nodes.

### Activation lifecycle

| State | Seed | Config | Behavior |
|---|---|---|---|
| Implementation exists but not configured | `IsPlanned = false, IsEnabled = false` | `Enabled = false` | UI shows "Não Configurado", test button disabled |
| Configuration present but disabled | `IsPlanned = false, IsEnabled = false` | `Enabled = false`, Server/DB filled | UI shows "Não Configurado", test button disabled |
| Fully configured and enabled | `IsPlanned = false, IsEnabled = false` | `Enabled = true`, Server/DB filled | UI shows test button enabled, real health after test |

> **Note**: `IsEnabled` in the database seed is kept `false`. The provider only becomes testable when `appsettings.json` has `Enabled = true` and valid connection settings. This prevents "active but misconfigured" states.

### What it does NOT do (yet)

- Material/article queries
- Supplier/department/cost center reads
- Unified employee profile (Primavera + Innux merge)
- Attendance events / terminal reads
- Data synchronization
- Write operations
- Background health checks

Those will be layered via domain-specific services in future phases, reusing `PrimaveraConnectionFactory` and `InnuxConnectionFactory`.

---

## 🚀 Phase 3A: Innux Employee Lookup (Read-Only)

> **Scope**: First Innux domain-level read integration. Operational employee identity lookup only.
> No attendance events, no biometric reconciliation, no Primavera merge, no sync, no writes.

### Architecture

- **Single database**: Innux has one database (`Innux` on `AOVIA1VMS012\SQLINNUX`). No company routing needed.
- **InnuxConnectionFactory**: Shared, domain-neutral factory for Innux SQL connections. Simpler than `PrimaveraConnectionFactory` — no enum, no multi-target routing.
- **InnuxIntegrationProvider**: Refactored to delegate connection-string resolution to `InnuxConnectionFactory`. Single source of truth for Innux connection configuration.

### Tables Used

| Table | Purpose |
|---|---|
| `dbo.Funcionarios` | Operational employee identity (primary) |
| `dbo.Departamentos` | Department enrichment via LEFT JOIN |

### DTO Field Mapping (InnuxEmployeeDto)

| DTO Field | Source Column | Notes |
|---|---|---|
| `EmployeeNumber` | `Numero` | Primary match key with Primavera `Codigo` |
| `InnuxEmployeeId` | `IDFuncionario` | Internal Innux technical PK |
| `Name` | `Nome` | Full name |
| `ShortName` | `NomeAbreviado` | Abbreviated display name |
| `Email` | `Email` | Contact |
| `Phone` | `Telefone` | Contact |
| `Mobile` | `Telemovel` | Contact |
| `DepartmentId` | `IDDepartamento` | FK to Departamentos |
| `DepartmentName` | JOIN `d.Descricao` | Enriched via LEFT JOIN |
| `CardNumber` | `Cartao` | Badge / biometric card |
| `IsActiveOperational` | `Activo` | Innux operational active state (NOT cross-system) |
| `LoginAd` | `LoginAD` | AD integration reference |
| `HireDate` | `DataAdmissao` | Admission date |
| `TerminationDate` | `DataDemissao` | Termination date |
| `Category` | `Categoria` | Employee category |
| `CostCenter` | `CentroCusto` | Cost center code |
| `HasPhoto` | `Fotografia IS NOT NULL` | Boolean presence only — never returns blob |
| `Source` | Hardcoded `"INNUX"` | Traceability |

### Department JOIN

- Key: `f.IDDepartamento = d.IDDepartamento`
- Status: **runtime-validated assumption** from schema discovery documentation
- If actual column name differs at runtime, the first SQL execution will surface it immediately

### API Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/admin/integrations/innux/employees/{number}` | Lookup by employee number |
| `GET` | `/api/admin/integrations/innux/employees/search?q=...&limit=50` | Search by name |

Auth: `System Administrator` role only.

Error semantics aligned with Primavera:
- `400` — invalid/too-short input
- `404` — employee not found
- `502` — SQL connectivity failure
- `503` — provider not configured/disabled
- `500` — unexpected error

### Naming Decisions

- **`IsActiveOperational`**: Named explicitly to indicate Innux operational active state, not the unified or authoritative cross-system employee status. This prevents ambiguity when Primavera + Innux unified profile work begins.
- **`HasPhoto`**: Boolean presence indicator only. Never exposes the actual image/blob payload.

### Intentionally Deferred to Later Phases

- Attendance events (`dbo.Ausencias`, punch records)
- Terminal assignments (`dbo.AtribuirTerminaisFuncionarios`)
- Biometric reconciliation
- Unified search (cross-system name search)
- Photo download endpoint
- Employee contracts (`dbo.FuncionariosContratos`)
- Employee attachments (`dbo.FuncionariosAnexos`)
- Sync / write operations

---

## 🚀 Phase 3B: Unified Employee Profile (Primavera + Innux, Read-Only)

> **Scope**: First cross-system employee composition. Lookup by Primavera code + company only.
> No unified search, no fuzzy matching, no attendance, no writes.

### Source Hierarchy

| Source | Role | Authority |
|---|---|---|
| **Primavera** | Master | Employee code, formal identity, HR status, department, company |
| **Innux** | Complement | Operational identity, card number, active state, login AD, photo presence |

**Primavera success is required for the profile. Innux is always optional.**

### Cross-System Match Key

```
Primavera.Codigo  ↔  Innux.Numero
```

Deterministic, code-based matching. No fuzzy name matching in this phase.

### Composition Flow

1. Lookup Primavera employee by `code` + `company`
2. If Primavera not found → `404`
3. Attempt Innux lookup using `Primavera.Code` as `Innux.Numero`
4. If Innux matched → populate `InnuxProfileSection`, `InnuxLookupStatus = "MATCHED"`
5. If Innux not found → `Innux = null`, `InnuxLookupStatus = "NOT_FOUND"`
6. If Innux error → `Innux = null`, `InnuxLookupStatus = "ERROR"`, safe message
7. Return unified profile (Primavera always present)

### Response Structure

```json
{
  "employeeCode": "21000073",
  "company": "ALPLAPLASTICO",
  "primavera": {
    "code": "...",
    "name": "...",
    "departmentName": "...",
    "sourceCompany": "ALPLAPLASTICO"
  },
  "innux": {
    "innuxEmployeeId": 1576,
    "employeeNumber": "21000073",
    "isActiveOperational": true,
    "source": "INNUX"
  },
  "hasInnuxMatch": true,
  "innuxMatchStrategy": "EMPLOYEE_CODE_TO_NUMBER",
  "innuxLookupStatus": "MATCHED",
  "innuxLookupMessage": null
}
```

### API Endpoint

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/admin/integrations/employees/{code}?company=...` | Unified profile lookup |

Auth: `System Administrator` role only.

### Error Semantics

| Scenario | HTTP | Behavior |
|---|---|---|
| Missing/invalid company | `400` | Validation error |
| Empty employee code | `400` | Validation error |
| Primavera not found | `404` | `{error, code, company}` |
| Primavera SQL failure | `502` | SQL communication error |
| Primavera config failure | `503` | Provider misconfigured |
| Innux no-match | `200` | `HasInnuxMatch=false`, `InnuxLookupStatus="NOT_FOUND"` |
| Innux error | `200` | Primavera returned, `InnuxLookupStatus="ERROR"`, safe message |

### Key Decisions

- **DEC-106**: Innux failure degrades gracefully. Primavera profile always returned if Primavera succeeds.
- **DEC-107**: Unified search deferred. Lookup by code is the first stable cross-system operation.
- Source sections never flattened — provenance remains explicit.
- No raw exception details in API responses — safe diagnostic messages only.

### Intentionally Deferred

- Unified search (cross-system name search with N+1 enrichment)
- Fuzzy / manual reconciliation
- Attendance-event composition
- Write operations
- Reconciliation UI

---

## Phase 4A — Primavera Article/Material Lookup (Read-Only)

> First Primavera master-data read layer beyond employees. Company-aware, read-only.

### Source Schema

| Table | Purpose |
|---|---|
| `Artigo` | Primary article/material table (154 columns in ALPLAPLASTICO, 152 in ALPLASOPRO) |
| `Familias` | Family descriptions (joined via `Familia` code) |
| `SubFamilias` | Sub-family descriptions (deferred — composite key join not yet validated) |

### DTO Fields (PrimaveraArticleDto)

| Field | Source Column | Type | Notes |
|---|---|---|---|
| `Code` | `Artigo` | `nvarchar(48)` | Primary key |
| `Description` | `Descricao` | `nvarchar(100)` | Full article description |
| `BaseUnit` | `UnidadeBase` | `nvarchar(5)` | Base unit of measure |
| `PurchaseUnit` | `UnidadeCompra` | `nvarchar(5)` | Purchase unit |
| `Family` | `Familia` | `nvarchar(10)` | Family code |
| `FamilyDescription` | `Familias.Descricao` | `nvarchar` | From JOIN |
| `SubFamily` | `SubFamilia` | `nvarchar(10)` | Sub-family code (enrichment deferred) |
| `ArticleType` | `TipoArtigo` | `int` | Primavera article type code |
| `Brand` | `Marca` | `nvarchar(10)` | Brand/trademark |
| `IsCancelled` | `ArtigoAnulado` | `bit` | Whether the article is voided |
| `SupplierCode` | `CDU_codforneced` | `nvarchar(40)` | **Environment-specific** — ALPLAPLASTICO only |
| `InternalCode` | `CDU_codinterno` | `nvarchar(40)` | **Environment-specific** — ALPLAPLASTICO only |
| `SourceCompany` | computed | string | Which Primavera company |
| `Source` | constant | string | Always `"PRIMAVERA"` |

### API Surface

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/admin/integrations/primavera/articles/{code}?company=...` | Lookup by code |
| `GET` | `/api/admin/integrations/primavera/articles/search?company=...&q=...&limit=...` | Search by code or description |

Auth: `System Administrator`.

### Error Semantics

| Condition | HTTP | Details |
|---|---|---|
| Missing/invalid company | `400` | Required param |
| Empty/short query (< 2 chars) | `400` | Minimum length validation |
| Article not found | `404` | Code not in target database |
| SQL communication failure | `502` | Timeout or connection error |
| Provider misconfigured | `503` | Credentials missing, provider disabled |

### CDU Column Handling

The custom fields `CDU_codforneced` and `CDU_codinterno` exist only in ALPLAPLASTICO. The service detects their availability at runtime via `INFORMATION_SCHEMA.COLUMNS` and conditionally includes them in the SQL query. This design:

- Avoids hardcoding per-company schema assumptions
- Is future-proof if CDU columns are added to ALPLASOPRO later
- Returns `null` for SupplierCode/InternalCode when columns are absent

### Key Decisions

- **DEC-108**: CDU column detection via `INFORMATION_SCHEMA.COLUMNS` at query time. Not hardcoded per company.
- **DEC-109**: `Remarks` (`Observacoes`) deferred from first DTO — large ntext field, not essential for initial lookup.
- **DEC-110**: `SubFamilias` enrichment deferred — composite key (`Familia + SubFamilia`) not yet validated at scale.
- `DescricaoAbreviada` does **not exist** in this Primavera environment. Only `Descricao`.

### Intentionally Deferred

- SubFamilias enrichment join
- Remarks (Observacoes) field
- Stock balances / pricing / purchase history
- Supplier-article relationship logic
- OCR item reconciliation
- Portal catalog sync
- Write/update operations
- Unidades (unit descriptions) enrichment

---

## Phase 4B — Primavera Supplier Master Data Lookup (Read-Only)

> First Primavera supplier-side master-data read layer. Company-aware, read-only.

### Source Schema

| Table | Purpose |
|---|---|
| `Fornecedores` | Primary supplier master table (116 columns in both companies) |

Unlike `Artigo`, the `Fornecedores` table has **consistent columns across both companies** (ALPLAPLASTICO and ALPLASOPRO). No adaptive CDU column detection is needed.

### DTO Fields (PrimaveraSupplierDto)

| Field | Source Column | Type | Notes |
|---|---|---|---|
| `Code` | `Fornecedor` | `nvarchar(12)` | Primary key |
| `Name` | `Nome` | `nvarchar(100)` | Full supplier name |
| `FiscalName` | `NomeFiscal` | `nvarchar(150)` | Formal/fiscal name |
| `TaxId` | `NumContrib` | `nvarchar(20)` | Tax identification / NIF |
| `Email` | `Email` | `nvarchar(100)` | Contact email |
| `Phone` | `Tel` | `nvarchar(20)` | Telephone |
| `Fax` | `Fax` | `nvarchar(20)` | Fax |
| `Address` | `Morada` | `nvarchar(50)` | Primary address |
| `Address2` | `Morada1` | `nvarchar(50)` | Secondary address line |
| `City` | `Local` | `nvarchar(50)` | City/locality |
| `PostalCode` | `Cp` | `nvarchar(15)` | Postal code |
| `Country` | `Pais` | `nvarchar(2)` | ISO country code |
| `SupplierType` | `TipoFor` | `nvarchar(1)` | Supplier type code |
| `IsCancelled` | `FornecedorAnulado` | `bit` | Whether supplier is voided |
| `Currency` | `Moeda` | `nvarchar(3)` | Currency code |
| `CreatedAt` | `DataCriacao` | `datetime` | Record creation date |
| `SourceCompany` | computed | string | Which Primavera company |
| `Source` | constant | string | Always `"PRIMAVERA"` |

### API Surface

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/admin/integrations/primavera/suppliers/{code}?company=...` | Lookup by supplier code |
| `GET` | `/api/admin/integrations/primavera/suppliers/search?company=...&q=...&limit=...` | Search by code, name, fiscal name, or NIF |

Auth: `System Administrator`.

### Error Semantics

| Condition | HTTP | Details |
|---|---|---|
| Missing/invalid company | `400` | Required param |
| Empty/short query (< 2 chars) | `400` | Minimum length validation |
| Supplier not found | `404` | Code not in target database |
| SQL communication failure | `502` | Timeout or connection error |
| Provider misconfigured | `503` | Credentials missing, provider disabled |

### Key Decisions

- **DEC-111**: `Fornecedores` is the authoritative supplier source table. No joins needed for first version.
- **DEC-112**: Financial fields (payment terms, credit limits, balances, bank details) intentionally excluded from first DTO.
- **DEC-113**: Supplier-article relationship logic deferred — no `ArtigoFornecedor` join yet.

### Intentionally Deferred

- Supplier-article relationship (ArtigoFornecedor table)
- Financial/banking fields (CondPag, ModoPag, TotalDeb, LimiteCred)
- Notas (ntext remarks) field
- B2B integration fields
- Retention/withholding tax fields
- Supplier ranking / scoring
- Purchase history by supplier
- Portal supplier sync
- Write/update operations
- OCR vendor reconciliation

---

## Phase 4C — Primavera Article-Supplier Contextual Linkage (Read-Only)

> First article-supplier relationship layer. Company-aware, read-only.

### Source Schema

| Table | Purpose |
|---|---|
| `ArtigoFornecedor` | Article-supplier relationship table (25 columns in both companies) |

Join keys:
- `ArtigoFornecedor.Artigo` → `Artigo.Artigo` (article identity)
- `ArtigoFornecedor.Fornecedor` → `Fornecedores.Fornecedor` (supplier identity)

**Schema consistency**: Both ALPLAPLASTICO (319 rows) and ALPLASOPRO (256 rows) have identical 25-column sets including `CDU_CodBarrasEntidade`.

### Relationship Fields Exposed

| Field | Source Column | Type | Notes |
|---|---|---|---|
| `SupplierDescription` | `DescricaoFor` | `nvarchar(200)` | Supplier’s own description |
| `SupplierReference` | `ReferenciaFor` | `nvarchar(48)` | Supplier reference code |
| `PurchaseUnit` | `UnidadeCompra` | `nvarchar(5)` | Unit for purchasing |
| `PurchaseConversionFactor` | `FactorConvCompra` | `float` | Conversion factor |
| `LeadTimeDays` | `PrazoEntrega` | `smallint` | Delivery lead time |
| `Currency` | `Moeda` | `nvarchar(3)` | Currency code |
| `LastEntryDate` | `DatUltEntrada` | `datetime` | Last goods receipt |

### Enrichment Joins

| Direction | Join | Enriched Fields |
|---|---|---|
| Article → Suppliers | LEFT JOIN `Fornecedores` | `Nome`, `NumContrib` |
| Supplier → Articles | LEFT JOIN `Artigo` | `Descricao`, `UnidadeBase` |

### API Surface

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/admin/integrations/primavera/articles/{code}/suppliers?company=...` | Suppliers for an article |
| `GET` | `/api/admin/integrations/primavera/suppliers/{code}/articles?company=...` | Articles for a supplier |

Auth: `System Administrator`. Both endpoints return 404 if the parent entity does not exist.

### Key Decisions

- **DEC-114**: `ArtigoFornecedor` is the authoritative article-supplier relationship table.
- **DEC-115**: Pricing fields (`PrCustoUltimo`, `DescFor`, `PrecoUltEncomenda`) excluded — relationship visibility only.
- **DEC-116**: Enrichment uses LEFT JOIN so relationships still appear even if identity lookup fails partially.

### Intentionally Deferred

- Pricing / last cost / discount fields
- VAT / tax code fields
- Document reference fields (TipoDoc, NumDoc, SerieDoc)
- Supplier ranking / Classificacao
- Article availability / stock checks
- Sourcing recommendation logic
- Request validation against relationships
- Portal procurement workflow integration
- Write/update operations

---

## Phase 5A — Primavera Request Validation Against Master Data (Read-Only)

> First Portal-side validation layer built above Primavera master-data services. Pure composition.

### Architecture

This phase introduces **no new SQL**. It is a composition/validation layer above:

| Reused Service | Purpose |
|---|---|
| `IPrimaveraArticleService` | Article existence check |
| `IPrimaveraSupplierService` | Supplier existence check |
| `IPrimaveraArticleSupplierService` | Relationship existence check |

### Validation Status Model

| Status | Meaning |
|---|---|
| `VALID` | Article exists, supplier exists (if provided), relationship confirmed |
| `WARNING` | Article exists, but: supplier not provided (partial) OR both exist but no relationship (unlinked) |
| `INVALID` | Article not found, supplier not found, or missing required inputs |
| `ERROR` | Technical failure (SQL timeout, misconfiguration) — distinct from business INVALID |

### Validation Rules (Deterministic)

| Input State | Result |
|---|---|
| Missing/invalid company | `INVALID` |
| Missing article code | `INVALID` |
| Article not found | `INVALID` |
| Article found, no supplier provided | `WARNING` (partial) |
| Article found, supplier not found | `INVALID` |
| Article + supplier found, relationship exists | `VALID` |
| Article + supplier found, no relationship | `WARNING` (unlinked) |
| Technical failure | `ERROR` |

### API Surface

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/admin/integrations/primavera/request-validation/validate-line` | Single-line validation |
| `POST` | `/api/admin/integrations/primavera/request-validation/validate-batch` | Batch validation (max 50 lines) |

Auth: `System Administrator`. Business-level results are always HTTP 200.

### HTTP Semantics

Business validation (VALID/WARNING/INVALID) is returned as HTTP 200 because the validation operation itself succeeded. The `ValidationStatus` field carries the business outcome. HTTP 4xx/5xx are reserved for request-level errors (bad payload, server failure).

### Key Decisions

- **DEC-117**: Business INVALID is in the response body, not 4xx HTTP status.
- **DEC-118**: No-supplier = WARNING (partial), not INVALID.
- **DEC-119**: No-relationship = WARNING (unlinked pair), not INVALID — allows new vendor relationships.

### Intentionally Deferred

- Request-form UI integration
- Fuzzy/approximate matching
- Supplier ranking or recommendation
- Price comparison / availability checks
- OCR reconciliation
- Portal sync/writeback
- Automatic suggestion engines
- Procurement workflow integration
