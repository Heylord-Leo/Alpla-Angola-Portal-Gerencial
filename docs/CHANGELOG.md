# Changelog

All notable changes to the Alpla Angola - Portal Gerencial project will be documented in this file.

## [v2.79.0] - 2026-04-18 - Feature: Manual Badge Creation (Visitor Workflow)
### Added
- **Manual Badge Entry**: Added a new "Entrada Manual" toggle in the HR Employee Workspace. When activated, it seamlessly replaces the Primavera API search with a manual data entry form.
- **Visitor Badge Lifecycle**: The system can now issue badges for visitors or temporary staff without requiring them to be pre-registered in the Primavera ERP. These badges are logged into `BadgePrintHistory` utilizing a specialized `MANUAL-[timestamp]` employee code to maintain audit traceability.
- **Resilient Badge Rendering**: Upgraded the `BadgePreview` engine and `BadgeLayoutEditorV3` configurations to robustly handle multi-line text wrapping. Names that exceed container constraints now gracefully wrap to a newly allocated second text box, preventing truncation.
- **Contextual Form Validation**: Added visual validation logic blocking manual badge printing if requisite parameters (Name, Category, missing RFID card) aren't satisfied, ensuring blank or malformed visitor badges are not dispatched.

## [v2.78.0] - 2026-04-17 - Feature: Financial Snapshot & Payment Divergence Detection (Phase 1 — DEC-110)
### Added
- **Financial Snapshot at Approval**: `ApprovedTotalAmount`, `ApprovedCurrencyCode`, and `ApprovedAtUtc` are now captured immutably on the `Request` entity at the moment of final approval. QUOTATION flow sources the winning quotation total; PAYMENT flow sources `EstimatedTotalAmount`.
- **Mandatory Payment Amount Capture**: `ActualPaidAmount` and `ActualPaidAtUtc` are now required inputs when confirming payment via `MarkAsPaid`. The `FinanceActionModal` includes a new "Montante Efetivamente Pago" input field.
- **Payment Divergence Detection**: Automated comparison of `ActualPaidAmount` vs `ApprovedTotalAmount` using a 1% relative tolerance (with 1.00 absolute floor). When divergence exceeds tolerance, a `PAYMENT_DIVERGENCE_DETECTED` audit entry is created in `RequestStatusHistory` with detailed variance data.
- **Finance Status Guards**: `SchedulePayment` and `MarkAsPaid` endpoints now enforce explicit source-status validation. Actions from invalid workflow states return HTTP 400 with descriptive error messages listing allowed statuses.
- **Finance List Enrichment**: `FinanceListItemDto` now exposes `ApprovedTotalAmount`, `ActualPaidAmount`, `ApprovedCurrencyCode`, `ApprovedAtUtc`, `ActualPaidAtUtc`, and a computed `HasPaymentDivergence` flag.
### Documentation
- **WORKFLOW_ARCHITECTURE.md**: Added §6 — Financial Lifecycle covering value source by stage, snapshot rules, actor responsibilities, payment validation rules, divergence detection algorithm, and Phase 2 intent.
- **DECISIONS.md**: Added DEC-110 — Financial Snapshot & Payment Divergence Detection phased delivery rationale.

## [v2.77.3] - 2026-04-16 - Performance: Master Data Page Load Optimization (50s → 2s)
### Performance
- **GetUsers Cartesian Explosion Fix**: Replaced 4 eager-loading Include/ThenInclude chains with direct SQL projection in `UsersController.GetUsers`. The cartesian join was taking 25s+ for 10 users; now resolves in <200ms.
- **Frontend Sequential Loading**: Replaced `Promise.allSettled` (9 parallel API calls causing LocalDB connection pool contention at 44s) with sequential loading that completes in <1s.

## [v2.77.2] - 2026-04-16 - Backend: EF Core Decimal Precision Standardization
### Fixed
- **EF Core Decimal Precision**: Added explicit `HasColumnType` precision/scale for 16 decimal properties across 5 entities (`OcrExtractedItem`, `ReconciliationRecord`, `QuotationItem`, `RequestLineItem`, `Request`). Eliminates all model validation warnings at startup and prevents silent financial data truncation. Convention: money `decimal(18,2)`, percentages `decimal(9,4)`, quantities `decimal(18,4)`.
### Documentation
- **DECISIONS.md**: Added DEC-108 establishing mandatory decimal precision as a permanent backend architecture rule.

## [v2.77.0] - 2026-04-15 - Security: Dedicated HR Role & Scope Model
### Added
- **Dedicated HR Role**: Introduced `HR` as a standalone role (`RoleConstants.HR` / `ROLES.HR`) to decouple HR workspace access from the `Local Manager` privilege.
- **Backend Authorization**: `HRController` now enforces `[Authorize(Roles = "System Administrator,HR")]`. All other roles receive HTTP 403.
- **Login Scope Data**: `UserProfileDto` now includes `Plants` and `Departments` fields, populated directly from scope tables during login — eliminates the need for an extra `/api/v1/users/me` call.
- **Frontend Auth Context**: Added `hasHRAccess` derived boolean to `AuthContext`, combining `HR` role membership and `System Administrator` bypass.
- **User Management HR Warning**: When `HR` role is selected during user creation/editing, a contextual warning appears if no plants or departments are assigned, preventing creation of scopeless HR users.
### Changed
- **Navigation Visibility**: R.H. sidebar group and `Cadastro de Funcionários` submenu now require `ROLES.HR` or `ROLES.SYSTEM_ADMINISTRATOR` (previously required `ROLES.LOCAL_MANAGER`).
- **Route Protection**: `/hr/employees` route guard updated from `LOCAL_MANAGER` to `HR` role.
- **Manager Role Assignment**: Local Managers can now assign the `HR` role to users within their organizational scope.
### Breaking
- **`Local Manager` users no longer have implicit HR access.** Existing Local Managers who need continued access to the Employee Workspace must be explicitly assigned the `HR` role.
### Documentation
- **DECISIONS.md**: Added DEC-107 — Dedicated HR role architecture with explicit future-evolution constraints.
- **ACCESS_MODEL.md**: Updated to reflect HR role, scope model, and breaking change from Local Manager decoupling.

## [v2.76.1] - 2026-04-15 - Search Functionality Expansion
### Added
- **Global Search Scope**: Expanded the main Requests Dashboard search capabilities. The system now seamlessly searches by Requester Name (`Solicitante`) in addition to Request Number and Title.
- **Contextual Help Search Glossary**: Updated the contextual help (the "i" icon) in the "Explorador de Pedidos" modal to precisely reflect all actively indexable search parameters.

## [v2.76.0] - 2026-04-15 - UI: Buyer Portal Header Modernization
### Added
- **Buyer Portal Header Modernization**: Refactored the request card header into a clean 3-zone architecture (ID/Status, People/Approvers, Date/Actions) with improved visual hierarchy and scannability.
- **Action Kebab Menu (⋮)**: Replaced legacy "Cancelar" and "Detalhes" buttons with a unified, motion-animated dropdown menu to increase workspace density and reduce visual noise.
- **Teams Status Presence**: Integrated discrete Teams chat triggers next to requester, buyer, and approver names for immediate operational communication.
### Changed
- **Multi-Stage Approver Logic**: Updated the Area Approver visibility logic. The "Aprovador da Área" now remains visible throughout all initial stages (including Aguardando Cotação) until the request reaches "Aguardando Aprovação Final", providing consistent departmental context.
- **Card Layout Resilience**: Removed overflow constraints on quotation request cards to accommodate floating dropdown menus without clipping regressions.
- **Dark Mode Hospitality**: Standardized the new kebab menu components with semantic CSS variables (`var(--color-bg-surface)` and `var(--color-bg-neutral)`) for seamless theme switching.

## [v2.75.0] - 2026-04-15 - UX Feedback: Gestão de Cotações
### Added
- **Context-Aware Empty States**: Upgraded the "Gestão de Cotações" empty state to structurally depend on the active filter context. When the user has an active text search or status filter that yields zero results, contextual actionable buttons ("Limpar Busca" / "Limpar Filtros") are displayed directly in the empty state.
- **Structural Loading Skeletons**: Replaced the static "Carregando..." text with a custom `RequestGroupSkeleton` component utilizing pulsing CSS animations (mimicking the exact height and column metrics of the collapsed quotation groups) to eliminate layout shift post-data-fetch.
- **Localized Error Recovery**: Implemented localized boundaries for data fetching errors inside the Buyer Workspace. Failed fetches gracefully exit into an encapsulated "Falha ao Carregar" state containing an explicit and isolated "Tentar Novamente" recovery button mapped directly to the `loadData()` handler, preserving the shell structure visually.
- **Form Interactivity Preservation**: Extended all primary backend-bound mutation actions (Save Quotations, Re-assignments) to implicitly clear nested frontend list error contexts upon success, enhancing user resilience logic.

## [v2.74.0] - 2026-04-15 - Feature: Catalog Linkage in Manual Quotation Entry
### Added
- **Catalog Linkage in Manual Quotation Entry**: Integrated `CatalogItemAutocomplete` into the manual quotation entry mode within `BuyerItemsList.tsx`. This allows buyers to link manual entries directly to official Portal catalog items, ensuring data consistency for inventory and receiving.
- **Backend Catalog Traceability**: Updated `SavedQuotationItemDto` and `RequestsController` projections to persist and retrieve `ItemCatalogId` and `ItemCatalogCode` for quotation line items.

## [v2.73.0] - 2026-04-15 - Fix: Status Filter Pagination Architecture
### Fixed
- **Root cause**: Status is computed *after* matching Primavera items against the Portal catalog — it does not exist in the Primavera source system. The previous implementation paginated at the Primavera SQL level (`OFFSET/FETCH NEXT`), then applied the status filter post-match on each page. This caused pages to contain random numbers of matching items (e.g., page 2 = 0 items, page 4 = 13 items when filtering by "New").
- **Architecture change**: When `statusFilter` is active, the backend now fetches ALL Primavera records via `ListAllArticlesAsync` / `ListAllSuppliersAsync` (batched in pages of 200), performs matching on the full dataset, filters by status, then paginates the **filtered result** in-memory. This guarantees every page contains exactly `pageSize` items (except the last page).
- **Pagination accuracy**: `TotalPrimaveraRecords` now reflects the **filtered count** when a status filter is active, ensuring the frontend pagination ("Page X of Y") is correct for the filtered dataset.
- **Summary counts**: `NewCount`, `ExistsCount`, `ConflictCount` are now computed from the full matched dataset (not just the current Primavera page), providing accurate global counts regardless of which page is displayed.
- **Performance path preserved**: Without `statusFilter`, the original efficient per-page Primavera SQL pagination is still used — no behavioral change for unfiltered browsing.
### Added
- `ListAllArticlesAsync` on `IPrimaveraArticleService` / `PrimaveraArticleService` — sequential batch fetch (200 items/batch) for full dataset access.
- `ListAllSuppliersAsync` on `IPrimaveraSupplierService` / `PrimaveraSupplierService` — same pattern for suppliers.
### Changed (Frontend — v2.72.1)
- Status header filter drives `statusFilter` state (server query param) instead of client-side `columnFilters`.
- Header ↔ top dropdown are bidirectionally synced.
- "Limpar tudo" resets both client-side column filters and server-side status filter.

## [v2.72.0] - 2026-04-15 - Sync Workspace: Excel-Like Column Headers
### Added
- **Column Sorting**: Click any sortable column header to toggle ascending → descending → clear. Visual arrow indicators (▲/▼) show active sort direction.
- **Column Filtering**: Filter icon on each header opens a compact popover:
  - **Text columns** (Código, Descrição, Família, Unidade, Nome, NIF): case-insensitive, accent-insensitive "contains" search.
  - **Status column**: multi-select checkbox filter with localized labels (Novo, Existente, Conflito).
- **Reusable `SortableFilterHeader` component** (`components/shared/SortableFilterHeader.tsx`): shared by both Catalog and Supplier sync tables.
- **Page-scope banner**: Visible info banner when column filters/sort are active, explicitly stating: "Ordenação e filtros de coluna aplicados apenas aos itens visíveis nesta página." Shows active filter count, sort direction, and items visible vs total.
- **Global reset**: "Limpar tudo" button clears all column filters and sorting in one action.
- **Filter persistence**: Column filters and sort state persist across page changes and are reapplied to each newly loaded page.
- **Empty state**: Friendly message when column filters exclude all items on the current page.
### Technical Notes
- Client-side only: sorting and column filtering operate on the current page (up to 50 items) returned from the server. This is consistent with the existing post-match status filter behavior and does not mislead the user about cross-page effects.
- Stacking: top-level server controls (company, search, status) narrow the Primavera result; column header filters further refine the visible page; sort applies last.
- Columns supported — Catalog: Status, Código Primavera, Descrição (Primavera), Família, Unidade, Código Portal, Descrição (Portal). Supplier: Status, Código Primavera, Nome (Primavera), NIF (Primavera), Nome (Portal), NIF (Portal).

## [v2.71.0] - 2026-04-15 - Catalog Sync: Description-Based Matching (V2.1)
### Changed
- **Catalog Sync Matching Strategy (V2.1)**: Replaced PrimaveraCode-based matching with description-based comparison for the Item Catalog sync preview, with duplicate detection on both sides.
  - `Exists` → exact normalized description match between Primavera and Portal
  - `Conflict` → similar description (one contains the other, min 5 chars) — requires manual review
  - `Conflict` → duplicate description detected in Primavera result set (source ambiguity)
  - `Conflict` → duplicate description detected in Portal catalog (target ambiguity)
  - `New` → no relevant description match **and** no ambiguity on either side
- **Normalization Rules**: Descriptions are normalized before comparison: trim, uppercase, strip accents/diacritics, remove simple punctuation (`.` `,` `;` `:` `/` `-`), collapse repeated spaces.
- **Import Dedup Check**: Now uses normalized description matching instead of PrimaveraCode to prevent importing items that already exist in the Portal under a different code.
- **CatalogTable UI**: Added "Detalhe" column to display `conflictDetail` for Conflict-status catalog items.

### Fixed
- **Catalog Import 500 Error**: Added missing EF Core migration `AddItemCatalogSyncTraceabilityFields` for `SourceCompany` and `LastSyncedAtUtc` columns on `ItemCatalogItems` (and `Suppliers`). The entity model had these fields but the database schema did not, causing `SqlException: Invalid column name` on every import attempt.

## [v2.70.0] - 2026-04-15 - Authorization Hardening: Centralized Role Constants
### Fixed
- **SyncController 403 Regression**: Corrected `[Authorize(Roles = "Admin")]` to `[Authorize(Roles = RoleConstants.SystemAdministrator)]`. The `Admin` role does not exist in the system; the correct administrative role key is `System Administrator`.

### Changed
- **Backend Role Constant Enforcement**: Replaced all 7 hardcoded `"System Administrator"` string literals in `NotificationService.cs` with `RoleConstants.SystemAdministrator`.
- **Frontend Role Constant Enforcement**: Replaced all hardcoded role string literals with `ROLES.*` constants across:
  - `AuthContext.tsx` (central `isAdmin` and `isLocalManager` checks)
  - `ReceivingWorkspace.tsx` (scope filtering)
  - `QuickActions.tsx` (Purchasing — role-based action visibility)
  - `QuickActions.tsx` (Dashboard — role-based action visibility)
  - `UserManagement.tsx` (admin/manager role checks and role filtering)

### Documentation
- **ACCESS_MODEL.md**: Updated role label from "Admin" to "System Administrator" with explicit authorization key clarification warning.
- **DECISIONS.md**: Added DEC-105 — mandatory use of centralized role constants for all authorization checks.

## [v2.69.0] - 2026-04-14 - Phase 5A Primavera Request Validation Against Master Data (Read-Only)
### Added
- **PrimaveraRequestValidationDtos**: Input (`PrimaveraRequestValidationInputDto`) and result (`PrimaveraRequestValidationResultDto`) DTOs for structured validation:
  - Input: `Company`, `ArticleCode`, `SupplierCode` (optional)
  - Result: `ArticleExists`, `SupplierExists`, `RelationshipChecked`, `RelationshipExists`, `ValidationStatus`, `Messages[]`, enriched descriptions
- **IPrimaveraRequestValidationService / PrimaveraRequestValidationService**: Composition/validation layer above existing Primavera services:
  - Reuses `IPrimaveraArticleService` (article existence)
  - Reuses `IPrimaveraSupplierService` (supplier existence)
  - Reuses `IPrimaveraArticleSupplierService` (relationship existence)
  - No direct SQL — pure service composition
- **PrimaveraRequestValidationController**: Admin-internal API:
  - `POST /api/admin/integrations/primavera/request-validation/validate-line`
  - `POST /api/admin/integrations/primavera/request-validation/validate-batch` (max 50 lines)

### Validation Status Model
- **VALID**: article + supplier exist, relationship confirmed
- **WARNING**: article exists but no supplier provided (partial), or both exist but no relationship
- **INVALID**: article not found, or supplier provided but not found, or missing required inputs
- **ERROR**: technical failure (SQL timeout, provider misconfigured) — distinct from business INVALID

### Architecture Notes
- **Pure composition**: No new SQL queries — all validation done by calling existing read services
- **HTTP semantics**: Business validation results are always 200 (validation op succeeded); ValidationStatus field carries the business outcome
- **Batch support**: Up to 50 lines validated independently per call

### Decisions Recorded
- **DEC-117**: Validation is 200-based — business INVALID is in the response body, not 4xx HTTP status
- **DEC-118**: No-supplier = WARNING (partial validation), not INVALID
- **DEC-119**: No-relationship = WARNING (unlinked pair), not INVALID — allows new vendor relationships

## [v2.68.0] - 2026-04-14 - Phase 4C Primavera Article-Supplier Contextual Linkage (Read-Only)
### Added
- **PrimaveraArticleSupplierDtos**: Directional response DTOs for article-supplier relationships:
  - `PrimaveraArticleSuppliersDto` — wraps article identity with linked suppliers enriched from `Fornecedores`
  - `PrimaveraSupplierArticlesDto` — wraps supplier identity with linked articles enriched from `Artigo`
  - `ArticleSupplierItemDto` / `SupplierArticleItemDto` — enriched relationship items with identity + relationship fields
  - `PrimaveraArticleSupplierLinkDto` — raw relationship row DTO (8 fields from 25-column table)
- **IPrimaveraArticleSupplierService / PrimaveraArticleSupplierService**: Read-only, company-aware article-supplier relationship service. Two-step approach: verifies parent entity exists, then queries enriched relationships with LEFT JOIN to identity tables. Bounded to 100 results.
- **PrimaveraArticleSupplierController**: Admin-internal API:
  - `GET /api/admin/integrations/primavera/articles/{code}/suppliers?company=...`
  - `GET /api/admin/integrations/primavera/suppliers/{code}/articles?company=...`

### Architecture Notes
- **Source object**: `ArtigoFornecedor` table (25 columns; consistent across both companies)
- **Join keys**: `ArtigoFornecedor.Artigo → Artigo.Artigo`, `ArtigoFornecedor.Fornecedor → Fornecedores.Fornecedor`
- **Schema consistency**: Both ALPLAPLASTICO (319 rows) and ALPLASOPRO (256 rows) have identical column sets including `CDU_CodBarrasEntidade`
- **Scope**: Read-only, relationship visibility only. No pricing/ranking/sourcing logic.

### Decisions Recorded
- **DEC-114**: `ArtigoFornecedor` is the authoritative article-supplier relationship table
- **DEC-115**: Pricing fields (PrCustoUltimo, DescFor, PrecoUltEncomenda) excluded from first DTO — relationship visibility only
- **DEC-116**: Supplier enrichment via LEFT JOIN to Fornecedores (Nome, NumContrib); article enrichment via LEFT JOIN to Artigo (Descricao, UnidadeBase)

## [v2.67.0] - 2026-04-14 - Phase 4B Primavera Supplier Master Data Lookup (Read-Only)
### Added
- **PrimaveraSupplierDto**: ~15-field DTO for supplier master data. Source: Primavera `Fornecedores` table (116 columns; ~15 exposed). Fields: `Code`, `Name`, `FiscalName`, `TaxId`, `Email`, `Phone`, `Fax`, `Address`, `Address2`, `City`, `PostalCode`, `Country`, `SupplierType`, `IsCancelled`, `Currency`, `CreatedAt`, `SourceCompany`, `Source`.
- **IPrimaveraSupplierService / PrimaveraSupplierService**: Read-only, company-aware supplier lookup and search. Uses existing `PrimaveraConnectionFactory`. Search covers code, name, fiscal name, and tax ID (NIF).
- **PrimaveraSuppliersController**: Admin-internal API at `api/admin/integrations/primavera/suppliers`. Endpoints: `GET /{code}?company=...` (lookup) and `GET /search?company=...&q=...&limit=...` (search). Error semantics aligned with existing employee/article endpoints.

### Architecture Notes
- **Source object**: Primavera `Fornecedores` table (116 columns; consistent across both companies)
- **No CDU mismatch**: Unlike `Artigo`, both ALPLAPLASTICO and ALPLASOPRO have the same CDU columns for `Fornecedores` — no adaptive detection needed.
- **Search**: Bounded (max 50), min 2 chars, searches across `Fornecedor`, `Nome`, `NomeFiscal`, and `NumContrib`. Stable ordering by `Fornecedor` code.
- **Scope**: Read-only, multi-database aware. No financial/banking/credit/transactional fields.

### Decisions Recorded
- **DEC-111**: `Fornecedores` is the authoritative supplier source table. No joins needed for first version.
- **DEC-112**: Financial fields (CondPag, ModoPag, TotalDeb, LimiteCred, bank details) intentionally excluded — not relevant for initial lookup.
- **DEC-113**: Supplier-article relationship logic deferred to future phase.

## [v2.66.0] - 2026-04-14 - Phase 4A Primavera Article/Material Lookup (Read-Only)
### Added
- **PrimaveraArticleDto**: Focused ~13-field DTO for article/material master data. Source: Primavera `Artigo` table with `LEFT JOIN Familias`. Fields: `Code`, `Description`, `BaseUnit`, `PurchaseUnit`, `Family`, `FamilyDescription`, `SubFamily`, `ArticleType`, `Brand`, `IsCancelled`, `SupplierCode`, `InternalCode`, `SourceCompany`, `Source`.
- **IPrimaveraArticleService / PrimaveraArticleService**: Read-only, company-aware article lookup and search. Uses existing `PrimaveraConnectionFactory`. Adaptive CDU column detection — gracefully omits `CDU_codforneced` / `CDU_codinterno` when absent in target database (e.g., ALPLASOPRO).
- **PrimaveraArticlesController**: Admin-internal API at `api/admin/integrations/primavera/articles`. Endpoints: `GET /{code}?company=...` (lookup) and `GET /search?company=...&q=...&limit=...` (search). Error semantics aligned with existing Primavera employee endpoints.

### Architecture Notes
- **Source object**: Primavera `Artigo` table (154 columns; only ~13 exposed in DTO)
- **Enrichment join**: `LEFT JOIN Familias` for `FamilyDescription`
- **SubFamilias join**: Intentionally deferred
- **Remarks (Observacoes)**: Intentionally deferred from first DTO version
- **CDU fields**: `CDU_codforneced` (SupplierCode) and `CDU_codinterno` (InternalCode) are environment-specific custom fields. Present in ALPLAPLASTICO, absent in ALPLASOPRO. Detected at runtime via `INFORMATION_SCHEMA.COLUMNS`.
- **Search**: Bounded (max 50), minimum 2 chars, parameterized SQL, stable ordering by `Artigo` code
- **Scope**: Read-only, multi-database aware, no stock/pricing/sync/writeback

### Decisions Recorded
- **DEC-108**: CDU columns handled via runtime detection, not hardcoded per-company. Future-proof for schema changes.
- **DEC-109**: Remarks (Observacoes) deferred from first DTO — large ntext field, not essential for initial article lookup.
- **DEC-110**: SubFamilias enrichment deferred to keep first version simple and reduce composite-key assumptions.

## [v2.65.0] - 2026-04-14 - Phase 3B Unified Employee Profile (Read-Only)
### Added
- **UnifiedEmployeeProfileDto**: Cross-system profile composition with separated source sections (`PrimaveraProfileSection`, `InnuxProfileSection`). Match diagnostics at top level: `HasInnuxMatch`, `InnuxMatchStrategy`, `InnuxLookupStatus`, `InnuxLookupMessage`.
- **IUnifiedEmployeeProfileService / UnifiedEmployeeProfileService**: Read-only composition layer above both domain services. Primavera lookup first, then Innux enrichment by employee code. No direct SQL access.
- **UnifiedEmployeesController**: Admin-internal endpoint at `api/admin/integrations/employees/{code}?company=...`. Returns unified profile with both source sections.

### Architecture Notes
- **Match key**: `Primavera.Codigo` ↔ `Innux.Numero` (deterministic, code-based)
- **Source hierarchy**: Primavera is master; Innux is optional operational complement
- **Graceful degradation**: Innux failure does NOT fail the unified profile — Primavera profile is always returned if Primavera succeeds. Innux status is explicit via `InnuxLookupStatus` (`MATCHED`, `NOT_FOUND`, `ERROR`).
- **No flattening**: Source sections remain separated for clear provenance
- **Scope**: Lookup by code only. Unified search intentionally deferred.

### Decisions Recorded
- **DEC-106**: Innux failure degrades gracefully — Primavera profile returned with `InnuxLookupStatus = "ERROR"`, not HTTP error.
- **DEC-107**: Unified search deferred from Phase 3B to reduce composition complexity. Lookup by code is the first stable cross-system operation.

## [v2.64.0] - 2026-04-14 - Phase 3A Innux Employee Lookup (Read-Only)
### Added
- **InnuxConnectionFactory**: Shared, domain-neutral connection factory for Innux SQL connections. Single database (no company routing). Reusable by future Innux domain services (attendance, terminals, etc.).
- **IInnuxEmployeeService / InnuxEmployeeService**: Read-only Innux employee operational identity lookup. Queries `dbo.Funcionarios` with `LEFT JOIN dbo.Departamentos` for department enrichment. Supports lookup by employee number and search by name (bounded, max 50 results).
- **InnuxEmployeeDto**: Operational identity DTO with 17 mapped fields. Key naming: `IsActiveOperational` (explicitly Innux operational state, not unified cross-system status), `HasPhoto` (boolean presence indicator, never exposes blob data), `Source` (always `"INNUX"`).
- **InnuxEmployeesController**: Admin-internal API at `api/admin/integrations/innux/employees`. Error semantics aligned with Primavera controller (400/404/502/503/500).

### Changed
- **InnuxIntegrationProvider**: Refactored to delegate connection-string resolution to `InnuxConnectionFactory`, removing duplicated `BuildConnectionString()`. Single source of truth for Innux connection configuration.
- **DI Registration**: Added `InnuxConnectionFactory` and `IInnuxEmployeeService` registrations in `Program.cs`.

### Architecture Notes
- Innux has a single database target — no company parameter needed (unlike Primavera multi-database).
- Department JOIN key: `f.IDDepartamento = d.IDDepartamento` (runtime-validated assumption from schema discovery).
- Phase intentionally deferred: attendance events, terminal reads, biometric reconciliation, Primavera merge logic.

## [v2.63.0] - 2026-04-14 - Phase 2D Primavera Multi-Database Support
### Added
- **PrimaveraConnectionFactory**: Shared, domain-neutral connection factory that resolves SQL connections for any configured Primavera company/database target. Reusable by all future Primavera domain services (employees, materials, suppliers, departments, cost centers).
- **PrimaveraCompany enum**: Strongly typed selector for Primavera business databases (`ALPLAPLASTICO`, `ALPLASOPRO`). Stable internal codes — display labels resolved separately.
- **Multi-company configuration**: `Integrations:Primavera:Companies` section in appsettings supporting per-company `DatabaseName` and `Enabled` flags. Shared connection settings (Server, Instance, Auth, Timeout) remain at the Primavera level.
- **SourceCompany field**: `PrimaveraEmployeeDto.SourceCompany` identifies which Primavera database each record was read from.

### Changed
- **PrimaveraEmployeeService**: Refactored to accept `PrimaveraCompany` as explicit target. Delegates all connection resolution to `PrimaveraConnectionFactory`. Removed private `BuildConnectionString()`.
- **PrimaveraIntegrationProvider**: Refactored to use `PrimaveraConnectionFactory`. Health check uses Option A strategy: tests the first configured company (default target). Diagnostic logs include health target and configured companies list.
- **PrimaveraEmployeesController**: Employee lookup/search endpoints now require `company` query parameter (`?company=ALPLAPLASTICO`). Returns 400 for missing/invalid company, 503 for configuration errors, 502 for SQL failures.
- **DI Registration**: Added `PrimaveraConnectionFactory` registration in `Program.cs`.

### Decisions Recorded
- **DEC-103**: One Primavera provider, multiple business database targets. Database selection handled in domain services, not by duplicating providers.
- **DEC-104**: Option A health strategy — provider tests first configured company only. DEGRADED status deferred to future phase.
- **DEC-105**: Company selection via query parameter (`?company=<CODE>`). Route-segment style deferred.

## [v2.62.0] - 2026-04-14 - Phase 2B Innux Connection Health
### Added
- **Innux Integration**: Implemented `InnuxIntegrationProvider` mapping to `TIME_ATTENDANCE`, extending the generic integration framework to its second established real provider.
- **Provider Integrity Check**: Hooked runtime diagnostic test routines (`@@SERVERNAME`, `DB_NAME()`) to the Innux instance ensuring isolated SQL validations decoupled from existing biometric metadata pipelines. Enforced strict explicit credential validation reflecting real-world error constraints in the diagnostics UI.

## [v2.61.0] - 2026-04-14 - Phase 2A Primavera Employee Master Data
### Added
- **Primavera Integration**: Added read-only `PrimaveraEmployeeService` exposing the `IPrimaveraEmployeeService` contract to interact cleanly with `dbo.Funcionarios` joined with `dbo.Departamentos`.
- **Admin Endpoints**: Introduced `PrimaveraEmployeesController` (`/api/admin/integrations/primavera/employees`) to execute strict parameterized query matching.

### Changed
- Standardized cross-boundary response formats. DTO layers now map exact data states, allowing calling methods to interpret `TerminationDate` explicitly instead of risking implicit `IsActive` heuristics.

## [2.60.0] - 2026-04-14

### Changed
- **Primavera Integration Provider (Phase 1B)**: Stabilized runtime diagnostic connectivity to real Primavera infrastructure.
  - Replaced `Encrypt=false` (default) with `Encrypt=Optional` during SNI connection negotiation to gracefully downgrade on legacy SQL nodes without triggering 21-second timeouts.
  - Removed `ApplicationIntent.ReadOnly` flag as standalone environments drop the connection if no readable secondary is explicitly configured via routing.
  - Confirmed and applied **explicit SQL Authentication** as the canonical path. Desktop/session UI identity proxying is disallowed to prevent pipeline drops.
  - Provider connection response time verified at ~35ms.

### Decisions Recorded
- **DEC-102**: Explicit configured credentials required for real integrations; desktop session proxying disabled.

## [2.59.0] - 2026-04-14

### Added
- **Primavera Connection Health (Phase 1A)**: First concrete `IIntegrationProvider` implementation on the generic integration platform.
  - `PrimaveraIntegrationProvider`: validates SQL connectivity to Primavera ERP SQL Server. Uses diagnostic query (`SELECT @@SERVERNAME, DB_NAME()`) for identity confirmation. Read-only, no business-domain queries.
  - Supports both **SQL Server Authentication** and **Windows Authentication** modes — no default assumed.
  - Connection string built with `ApplicationIntent.ReadOnly` to enforce read-only at the transport level.
  - Integration logs emitted for connection test lifecycle (`STARTED`, `OK`, `FAILED`) via existing `IntegrationLogEventTypes`.
  - DI registration in `Program.cs`: `IIntegrationProvider → PrimaveraIntegrationProvider`.

### Changed
- **Primavera seed data**: Transitioned from `IsPlanned = true` (roadmap) to `IsPlanned = false` (real implementation exists). `IsEnabled` remains `false` — activation depends on explicit environment configuration, not provider existence.
- **Primavera connection status seed**: Updated from `PLANNED` to `NOT_CONFIGURED`.
- **Integration Playbook**: Added Phase 1A reference section, updated step 5 guidance (activation requires configuration), added activation lifecycle table.
- **appsettings.json**: Added `Username` and `Password` fields to Primavera config template for SQL auth mode support.

### Decisions Recorded
- **DEC-101**: Primavera provider activation lifecycle — implementation ≠ activation.

## [2.58.0] - 2026-04-14

### Added
- **Generic Integration Foundation (Phase 0)**: Implemented a provider-agnostic integration platform foundation supporting multiple external systems and business domains.
  - **Domain Entities**: `IntegrationProvider` (registry), `IntegrationConnectionStatus` (runtime health), `IntegrationProviderSettings` (connection config). All entities are generic — no coupling to specific providers or business domains.
  - **Application Layer**: `IIntegrationProvider` (minimal base contract: identity + connection test), `IIntegrationHealthService` (provider-agnostic aggregation), standardized DTOs (`IntegrationHealthSummaryDto`, `IntegrationProviderStatusDto`, `IntegrationConnectionTestResultDto`).
  - **Constants**: `IntegrationStatusCodes` (stable machine-readable: `HEALTHY`, `UNHEALTHY`, `UNREACHABLE`, `NOT_CONFIGURED`, `PLANNED`), `IntegrationLogEventTypes` (standardized `AdminLogWriter` event types).
  - **Infrastructure**: `IntegrationHealthService` — resolves providers from DI, aggregates status, executes connection tests, persists results, and logs events.
  - **API**: `IntegrationHealthController` — separate from `AdminDiagnosticsController`. `GET /api/admin/integrations/health` (summary), `POST /api/admin/integrations/{code}/test-connection` (manual test).
  - **Database**: EF Core migration `AddIntegrationFoundation` creating 3 tables with seed data for `PRIMAVERA` (ERP/SQL) and `INNUX` (Biometric/SQL) — both planned/disabled.
  - **Frontend**: Fully data-driven `IntegrationHealth.tsx` — renders provider cards dynamically from API. OCR explicitly separated as "internal service" from external providers. Test Connection button guarded (disabled for planned/unconfigured/unimplemented providers).
  - **Configuration**: `Integrations` section in `appsettings.json` with Primavera + Innux connection templates (disabled by default).
  - **Documentation**: Created `docs/INTEGRATION_PLAYBOOK.md` — comprehensive step-by-step guide for adding new providers.

### Decisions Recorded
- **DEC-100**: Generic integration foundation architecture — provider-agnostic, capabilities-as-metadata, strict settings separation.

## [2.57.0] - 2026-04-14

### Changed
- **RequestEdit Component Decomposition (DEC-096)**: Decomposed the monolithic `RequestEdit.tsx` (~1,274 lines) into a parent-child architecture (~660 lines in parent + 4 presentational children).
  - **Extracted Components**: `RequestGeneralDataSection`, `RequestFinancialSummary`, `RequestStatusActionPanels`, `RequestLineItemsSection` in `src/frontend/src/pages/Requests/components/`.
  - **Architecture**: Parent retains all state, handlers, permissions, and workflow logic. Children are strictly presentational, receiving data and callbacks via props.
- **CSS Module Migration**: Replaced five shared inline style helpers (`inputStyle`, `sectionTitleStyle`, `labelStyle`, `getInputStyle`, `renderFieldError`) with semantic CSS classes in `request-edit.module.css`.
- **Route-Level Code Splitting (DEC-099)**: Implemented `React.lazy()` + `Suspense` for ~20 page components in `App.tsx`. Eagerly loaded: `LoginPage`, `ResetPasswordPage`, `ChangePasswordPage`, `Dashboard`. Core JS bundle reduced from ~1,509 kB to ~446 kB (~70%).
- **LoadingSkeleton Fallback**: Introduced `LoadingSkeleton` component (`src/frontend/src/components/ui/LoadingSkeleton.tsx`) as the layout-aware fallback for lazy-loaded routes.
- **Dead Import Cleanup**: Removed 13 dead imports (10 Lucide icons, 3 components) from `RequestEdit.tsx` post-extraction.

### Decisions Recorded
- **DEC-096**: Incremental decomposition of `RequestEdit.tsx` with parent-orchestrator pattern.
- **DEC-097**: Explicit skip of generic `FormField` abstraction due to high field-type variation.
- **DEC-098**: Deferred accessibility/focus and motion-polish work to a future dedicated cycle.
- **DEC-099**: Route-level code splitting strategy with eager/lazy classification.

## [2.56.0] - 2026-04-13

### Added
- **PO Correction Forward Exit**: Completed the existing `WAITING_PO_CORRECTION` operational loop, which was previously a dead-end status.
  - **Backend**: `RegisterPo` endpoint now accepts `WAITING_PO_CORRECTION` as a valid source status, enabling the Buyer to re-register a corrected PO after Finance return.
  - **Conditional Action Codes**: Uses `REGISTER_PO` for initial registration (from `APPROVED`) and `REREGISTER_PO` for correction flow (from `WAITING_PO_CORRECTION`), preserving distinct audit history.
  - **Source-Status Guard (Finance Return)**: `ReturnForAdjustment` now validates that the request is in `PO_ISSUED` or `PAYMENT_SCHEDULED` before allowing a return. Returns from `PAYMENT_COMPLETED` or `WAITING_PO_CORRECTION` are blocked.
  - **Notification**: New `PO_CORRECTION_COMPLETED` event code notifies plant-scoped Finance users when a Buyer completes the correction.
  - **Frontend**: Added `CORRIGIR P.O` button (orange, visually distinct from `REGISTRAR P.O`) to the operational panel for `WAITING_PO_CORRECTION` status. Integrated `CorrectPoModal` in `RequestEdit.tsx`.
  - **Guidance**: Added `WAITING_PO_CORRECTION` to `getRequestGuidance()` — Responsible: Comprador, Action: Corrigir P.O devolvida por Finanças.
  - **Line Item Sync**: `WAITING_PO_CORRECTION` syncs items to `WAITING_ORDER` (idempotent — items are already in this state from prior `PO_ISSUED`).
  - **Business Decision**: Returning from `PAYMENT_SCHEDULED` intentionally invalidates the prior scheduling. After correction, Finance must re-evaluate from `PO_ISSUED`.

### Changed
- **WORKFLOW_ARCHITECTURE.md**: Added Section 6 (Finance Return / PO Correction Loop) and updated state machine tables, permission matrix, and attachment deletion rules.

## [2.55.0] - 2026-04-13

### Added
- **Financial Integrity Gate**: Implemented a server-side financial checkpoint at the quotation completion stage (`CompleteQuotation`).
  - Persists the OCR-extracted grand total (`OcrOriginalGrandTotal`) on the `Request` entity during OCR extraction as the integrity baseline.
  - Validates the completing quotation total against the OCR baseline using centralized tolerance (`max(1.0, 0.1% of original)`, configurable in `RequestConstants.FinancialIntegrity`).
  - Blocks progression (`409 Conflict`) with structured variance data when mismatch exceeds tolerance or unresolved reconciliation records exist.
  - Supports explicit buyer override with mandatory written justification.
  - Full audit trail: logs detection (`FINANCIAL_INTEGRITY_BLOCKED`), override acceptance (`FINANCIAL_INTEGRITY_OVERRIDE`) in `RequestStatusHistory` and `AdminLog`.
  - Frontend: Added Financial Integrity Modal in Quotation Management workspace (`BuyerItemsList.tsx`) with OCR vs Quotation comparison table, variance display, and override justification flow.
  - RequestEdit path surfaces integrity failures as modal feedback, directing buyers to the Quotation Management workspace for the override flow.

## [2.54.0] - 2026-04-13

### Added
- **Enriched Area Approver Notifications**: The `WorkflowNotificationOrchestrator` now intercepts payment events (`SCHEDULED`/`PAID`) and overrides the recipient footprint. Area Approvers now receive real-time financial transparency emails detailing the specific request amount, total monthly departmental spend, and percentage impact of the request on the department's monthly activity.
- **Approval Decision Guide**: Deployed a "Manual de Aprovação" helper to the `ApprovalDetailPanel.tsx` component, introducing an interactive, styled HTML modal containing step-by-step checklists, financial definition clarity, and guidance on required cost center bulk allocations.
- **Quotation Management UX**: Replaced the previous standalone `Link` page routing in `BuyerItemsList` with a frictionless, localized Quick View `RequestDrawerPresentation`, eliminating context loss during quote inspection.

## [2.53.0] - 2026-04-13

### Added
- **Workflow Notification Role-Casting**: Expanded the Orchestrator to dispatch role-specific email subjects, headlines, and contextual comments based cleanly on the actor's jurisdiction (Requester, Next Approver, or Buyer).
- **Self-Notification Lift**: Removed the `BypassSelfNotifyRule` suppression wall, allowing users to universally retain an email trail of requests they submitted onto their own governed departments. 
- **Admin System Logs Enhancement**: The `UsersController` backend now natively integrates `_adminLogWriter`, projecting explicit diagnostic telemetry trails on HTTP 400 violations caused by duplicate user/email registrations.

### Fixed
- **In-App Duplicate Handlers**: Removed obsolete `window.alert()` from identical user collisions inside `UserManagement.tsx`. Errors are now parsed and channeled into an organic top-bound red banner mapped with React local state.

## [2.52.0] - 2026-04-12

### Added
- **Server-Side Catalog Search & Pagination**: Improved performance of catalog items lookup by pushing load to the backend.
  - Re-engineered `CatalogItemsPanel.tsx` to utilize server-side search instead of client-side filtering.
  - Updated the backend `CatalogItemsController.cs` to accept optional `search` and `take` query parameters.
- **Autocomplete Optimization**: The item selection "pickup list" inside Request creation now correctly queries the server and strictly bounds outputs.
  - Limits returned UI outputs natively to a maximum of 10 items.
  - Added inline visibility of `Cod_Primavera` and `Cod_Fornecedor` inside the item autocomplete dropdown list options.

## [2.51.0] - 2026-04-11

### Added
- **Submission Confirmation Email**: Implemented an automated confirmation email sent directly to the requester immediately after submitting or resubmitting a request. The email includes a breakdown of line items and the estimated total value.
- **Approval Flow UX Redirection**: Transformed the standalone request view by replacing disconnected action buttons with a unified 'Pending Approval' banner.
- **Visual Attention Triggers**: Integrated front-end routing (`react-router-dom`) between `RequestEdit` and `ApprovalCenter`, implementing a custom `flash-red-row` CSS animation to instinctively guide approvers toward their assigned tasks upon redirection.
- **Dynamic SMTP Management**: Database-backed SMTP configuration with AES-256 encryption and real-time connectivity diagnostics.

## [2.50.0] - 2026-04-11

### Added
- **Password Recovery Workflow**: Implemented a complete self-service recovery flow including `Esqueceu a senha?` toggle on the login page, secure token generation with 15-minute expiry, and a dedicated `ResetPasswordPage`.
- **Bulletproof Email Logo (CID)**: Redesigned the transactional email engine to use **CID inline embedding** for the ALPLA logo. This ensures visual assets render correctly in all email clients without relying on a publicly accessible URL, solving "localhost" image breakage during development.
- **Robust Asset Resolution**: Implemented a multi-path fall-back strategy for locating physical assets on the server, with support for configuration overrides via `AppConfig:LogoPath`.
- **Environment Safety Guards**: Added strict backend validation to prevent the dispatch of transactional emails containing `localhost` or `127.0.0.1` links in non-development environments.

### Changed
- **Centralized URL Configuration**: Replaced dynamic `Request.Headers["Origin"]` resolution with a deterministic `AppConfig:FrontendBaseUrl` setting in `appsettings.json`, ensuring link reliability across staging and production.

## [2.49.2] - 2026-04-11

### Added
- **Intelligent Flow Notifications**: The system now issues explicit Informative Push Notifications to Requesters immediately when a Quotation completes, keeping the request authors directly in the loop.
- **Floating Area Navigation**: Warning banners for missing Selection decisions inside the Approval Drawer now support interactive smooth-scrolling, directly guiding Area Approvers to the specific form section using a 5-second red pulse animation.

### Fixed
- **Role-Based Visibility (RBAC)**: Stabilized Area Approver scopes inside `NotificationService.cs`, accurately surfacing Pending Approvals tailored strictly by role matching `Area Approver` rather than hardcoded IDs.
- **Restricted Access Paths**: The action buttons linking to "Gestão de Cotações" and "Recebimento" inside the Operational Hubs (`Dashboard` & `Purchasing`) now appropriately abide by RBAC filtering (Buyer and Receiving respectively), concealing pathways dynamically from unauthorized personas.
- **Approval Drawer Banners**: Repaired context leakage where Area Approvers were erroneously presented with blue re-routing banners designed for the "Final Approval" stage after their jurisdiction had already passed.

## [2.49.0] - 2026-04-11

### Added
- **Visual Checklists for Verification**: Implemented interactive row-highlighting checkboxes in the OCR and Request line item tables (Gestão de Cotações and Pedidos de Pagamento) to facilitate physical-to-digital document verification.
- **Dynamic OCR Discount Logic**: Implemented elastic proportion calculations for OCR discounts tracking `discountPercent`. This ensures that when buyers adjust line quantities, the discount scales mathematically and preserves logical unit subtotals.
- **Temporal Finance Graphing**: Expanded the projected cash-flow timeline on the Finance Dashboard to include configurable "1 Dia" (Default), "3 Dias", and "7 Dias" horizon toggles.
- **Contextual Request Terminology**: Requests Dashboard grid now natively translates the standard "Data Limite" column into "Recebido em" (for Completed Quotations) and "Pagamento Realizado em" (for Paid status), reducing timeline ambiguity.

### Fixed
- **Finance Modal Standardization**: Adapted the `FinanceActionModal` to the brutalist/premium corporate design standard, replacing legacy styling with CSS variables.

## [2.48.0] - 2026-04-11

### Added
- **Deep Linking Context Navigation**: The dashboard carousel now natively links urgent requests to their contextual workspaces (Purchasing: `WAITING_QUOTATION`, Receiving: `WAITING_RECEIPT` / `PAYMENT_COMPLETED`, Finance: `PO_ISSUED` / `PAYMENT_SCHEDULED`).
- **Visual Pulse Highlight**: Deep-linking now highlights the target element in the list with an animated red pulse, immediately driving the user's attention.
- **Drawer Integration (Finance)**: Finance obligations/payment lists now consume the standard `RequestQuickViewDrawer` for detailing objects, substituting intrusive new-tab spawning.

### Fixed
- **Roles Matrix Data Integrity**: Repaired an exclusion rule in `RequestsController.cs` that previously truncated actionable tasks for Buyers/Executors dependent on legacy timeline boundaries.
- **Layout Contraction Flaws**: Pushed `width: 100%` overrides on global Flex wrappers (`PageContainer.tsx`) to resolve aggressive collapsing behaviors in the `/finance/history` audit logs.
- Removed legacy UI links ("Modo Clássico") that persisted on modern component iterations.

## [2.47.0] - 2026-04-11

### Added
- **UI/UX Standarization (Phases 1-4)**: Completely standardized the visual alignment of all legacy operational and administrative workspaces to mirror the modern "Requests" baseline structure.
    - Decoupled brutalist grid/borders by injecting new `PageContainer`, `PageHeader`, `StandardTable`, and `SearchFilterBar` layout wrappers.
    - Refactored `Dashboard.tsx`, `FinanceLandingPage.tsx`, `PurchasingLandingPage.tsx`, `UserManagement.tsx`, `SystemLogs.tsx`, `MasterData.tsx`, `AdministratorWorkspace.tsx`, and all core inner modules to the new corporate design language.
    - Resolved widespread TypeScript constraints on Table wrappers, moving to native HTML tabular child declarations for superior component fluidity.

## [2.46.0] - 2026-04-11

### Added
- **Modern Requests Dashboard**: Completely visually overhauled the primary Requests workspace, migrating away from the legacy brutalist table patterns to a high-fidelity 'Modern Corporate' widget layout.
    - Added `ActionCarouselWidget` to surface urgent "Para Minha Ação" tasks with high-contrast motion cards.
    - Updated `RequestsTableWidget` to support native sticky scrolling, status chips, and integrated global Kebab menus.
- **Drawer Presentation Mode (Dual-Mode Architecture)**: Integrated `RequestDrawerPresentation`, allowing users to open and edit full requests via a slide-out right panel directly from the dashboard without navigating away or losing context. This leverages the existing `RequestEdit` business logic securely.

## [2.45.1] - 2026-04-10

### Fixed
- **Dark Mode UI Stabilization**: Conducted a systematic sweep of the entire frontend to eradicate hardcoded hex colors (`#fff`, `#f3f4f6`, `#e5e7eb`) from inline styles.
    - Updated 14+ core components and pages (User Management, Finance Overview, Dashboards, Autocompletes) to use theme-aware CSS variables.
    - Preserved document-viewer white-space integrity for PDFs and scanned images as per operational requirements.

## [2.45.0] - 2026-04-10

### Added
- **Dark Mode Support**: Implemented native theme switching (Light, Dark, System) with automatic FOUC (Flash of Unstyled Content) prevention.
    - Integrated a persistent `useTheme` hook with `localStorage` and system preference detection.
    - Overhauled `tokens.css` with high-contrast slate-based palettes and optimized "Congress Blue" for deep backgrounds.
    - Added an interactive theme switcher in the `UserDropdown` component.
- **Stacked Requests List**: Refactored the core Requests Management workspace into two distinct, vertically stacked sections:
    - **"Para Minha Ação"**: A filtered view dedicated to tasks requiring direct user intervention based on role-based responsibility logic (Requester adjustments, Area/Final approvals, Buyer quotations, Finance scheduling).
    - **"Explorador de Pedidos"**: A global view browsing all other accessible requests.
- **Reusable `RequestsGrid` Component**: Extracted list rendering, searching, filtering, and independent pagination into a modular component, enabling multi-list layouts without state collisions.

### Changed
- **Responsibility Filtering**: Enhanced the `RequestsController.GetRequests` endpoint with `myTasksOnly` and `excludeMyTasks` boolean flags, leveraging server-side LINQ expressions for advanced responsibility detection.

## [2.44.1] - 2026-04-10

### Fixed
- **Global Discount Persistence**: Resolved a regression where the "Desconto Comercial" was being overwritten by gross totals during the Payment Request submission. Recalculated totals now properly account for global discounts across all line-item mutation endpoints (Create, Update, Delete).
- **Payment Request Submission Payload**: Ensured `discountAmount` is included in the initial creation request to prevent zero-value defaults on the backend.

## [2.44.0] - 2026-04-10

### Added
- **Quotation Assignment Security**: Implemented strict ownership restrictions in the Quotation Management workflow. Unassigned or laterally assigned quotations are locked to read-only views, exposing a dynamic interface specifically for claiming/re-assigning ownership on-the-fly (`isAssignedToMe`).

### Changed
- **Quotation Discount Financial Model**: Refactored quotation draft properties from macro global elements down into explicit item-level discount declarations (`DiscountAmount` and `DiscountPercent`).
- **Orphan Attachment Cleanup Mitigation**: Integrated logic into the UI's draft lifecycle handlers effectively triggering backend deletion API operations exactly when an end-user abandons an actively loading OCR upload via the new "CANCELAR" interactive component.

## [2.43.1] - 2026-04-09

### Fixed
- **TotalAmount Persistence & Calculation**: Resolved a financial data loss issue where discount amounts and IVA rates were discarded during Request Line Item processing resulting in incorrect `TotalAmount` values (e.g., reverting to gross totals instead of net + IVA).
    - Appended `DiscountPercent` and `DiscountAmount` to the database schema and response payload to survive frontend auto-save loops.
    - Standardized `TotalAmount` calculation across all backend item generation points (Bulk Create, Clone, Add, Update) to explicitly include discounts and IVA variables.

## [2.43.0] - 2026-04-09

### Added
- **Context-Aware OCR Triage**: Implemented `sourceContext` propagation in the extraction pipeline. When documents are uploaded from Quotation or Payment flows, the system now enforces an "Invoice" classification, preventing catastrophic misclassification as "Contract" while still allowing manual override or scanned-file fallback.
- **Multi-Strategy Supplier Matching**: Overhauled the frontend supplier identification in `useOcrProcessor.ts`.
    1. **Normalization**: Automatically strips trailing punctuation (e.g., "S.A." vs "S.A"), collapses whitespace, and normalizes apostrophes.
    2. **NIF/TaxId Fallback**: Implemented a dedicated persistence-layer search by `TaxId` if name-based matching fails, significantly reducing duplicate supplier records.
- **Discount & IVA Reliability**: Enhanced the extraction prompt and mapping to distinguish Portuguese "Desc." (discount %) from "IVA" (tax %) columns. 
- **TotalPrice Anchor Validation**: Implemented a frontend safety net that reverse-engineers the real discount amount from the document's `totalPrice` if the AI confuses the discount and tax columns.

### Changed
- **OCR System Prompt**: Updated with bilingual (PT/DE) column mapping instructions and self-validation rules for line items.

## [2.42.1] - 2026-04-09

### Fixed
- **P.O. OCR Data Path Mismatch**: Corrected `RegisterPoModal` to read OCR data from the legacy envelope path (`integration.headerSuggestions.grandTotal.value`) instead of a non-existent flat path.
- **P.O. Grand Total Extraction**: Added `grandTotal` field to the GPT extraction schema to capture the final amount including IVA, resolving systematic mismatches against quotation totals.
- **P.O. Supplier Identification on Encomendas**: Added explicit prompt instructions for Purchase Order layouts from ERP Primavera.
- **Quotation Winner DTO Mapping**: Fixed `totalPrice`→`totalAmount` and `currencyId`→`currency` property mismatches in `RequestEdit.tsx`.
- **TextFirst Null Guard**: Hardened OCR early-return condition to reject empty strings via `!string.IsNullOrWhiteSpace`.

## [2.42.0] - 2026-04-09

### Added
- **P.O. Override Validation via OCR**: Implemented a protective soft-block flow in `RegisterPoModal`. The system evaluates similarity matching between the document's payload and the approved request parameters. Mismatches require an active acknowledgement (Override Confirmation) and a mandatory qualitative justification before generating the Purchase Order.
- **P.O. Dispute Audit Log**: The Backend endpoint (`RequestsController.RegisterPo`) now natively audits OCR mismatches and override comments directly into the `RequestStatusHistory` timeline ensuring financial traceability.

### Fixed
- **Quotation Workflow Item Desync**: Corrected a regression where selecting a winning quotation left the Area Approver with an empty grid. The system now performs a hard-sync operation (`RequestsController.SelectQuotation`), automatically wiping existing generic request line items and comprehensively replacing them with strict clones of the selected `QuotationItems`, preserving quantities, identical descriptions, and computed aggregates seamlessly.

## [2.41.0] - 2026-04-09

### Added
- **P.O. Workflow Visibility**: Implemented orange "Aguardando P.O" KPI card in the dashboard and a dedicated quick-chip in the requests grid mapped to the `APPROVED` status.
- **Optimized P.O. Registration UX**: Replaced fragmented workflow with a specialized `RegisterPoModal` that handles document upload and status transition to `PO_ISSUED` in one click.
- **Unit Master Data Integrity**: Filtered out deactivated units (`isActive: false`) from OCR auto-suggestions and manual selection menus in Quotation and Buyer Item lists.

### Fixed
- **RequestsController Build Error**: Corrected status constant usage from `Statuses.Approved` to `Statuses.FinalApproved`.
- **Frontend Build Stability**: Cleaned up imports in `DecisionFinancialTrendLine.tsx` and `ActionMenu.tsx`.

## [2.40.0] - 2026-04-09

### Added
- **OCR Line-Item Discount Extraction**: Extended the extraction pipeline to identify and extract per-item discount percentages and amounts from invoices (e.g., "Rabatt %" columns). Added `DiscountPercent` and `DiscountAmount` fields across the full backend DTO chain (`ExtractionLineItemDto`, `OcrLineItemSuggestionDto`, `ExtractionMapper`).
- **Discount Cross-Validation (Frontend)**: Implemented a safety net that recalculates `discountAmount` from `discountPercent` when the AI returns an incorrect per-unit value instead of the total line discount. Logs a `console.warn` when a correction is applied.
- **Editable Discount Column (UI)**: Added a new `DESC.` column to the Payment Request OCR items table, allowing users to view and manually correct extracted discount values. Includes a red "TOTAL ABATIMENTOS" summary row in the footer.
- **Company Auto-Identification (OCR)**: Implemented keyword-based company matching that identifies `AlplaPLASTICO` or `AlplaSOPRO` from OCR-extracted billing entity names, auto-filling the "Empresa" field with a visual confirmation message.
- **OCR Diagnostics**: Added `[OCR] Company Match Diagnostics` and `[OCR] Extraction & Calculation Diagnostics` console groups for real-time debugging. Enhanced Admin System Logs to show "Empresa Identificada (OCR)" with visual warning when extraction fails.

### Changed
- **AI Extraction Prompt**: Rewritten with explicit discount calculation rules and concrete examples to prevent the model from returning per-unit discounts instead of total line discounts.
- **Item Total Calculation**: All line-item and draft totals are now always recalculated from components (qty × unitPrice − discount + IVA) instead of trusting OCR-provided totals, eliminating silent zero-value errors caused by `??` operator semantics.
- **Quantity Column Width**: Increased QTD column from 60px to 80px to accommodate multi-digit quantities.

## [2.39.8] - 2026-04-09

### Changed
- **Requests List Performance Optimization**: Refactored the core EF Core LINQ projections in `RequestsController.GetRequests` to utilize `SelectMany().SumAsync()` left-joins, removing a crippling in-memory aggregation bottleneck and dropping server response times for the main workspace dataset from ~40s to ~230ms.

## [2.39.7] - 2026-04-08

### Added
- **OCR Execution Audit**: Integrated mandatory, persistent audit logging for all extraction pipeline executions (Success, Partial, Failure). Every run directly creates an immutable system log entry preserving user attribution, routing strategy, categorization (contract vs. invoice), and LLM token usage inside `AdminLogEntries` table.

## [2.39.6] - 2026-04-08

### Added
- **Contract Extraction Pipeline (Phase 3)**: Implemented a dedicated parsing strategy for long-text documents and contracts using sequential text chunking. Achieved a ~96% reduction in token usage for contracts (e.g., from ~111k to ~3.7k tokens) by bypassing unnecessary full-document Vision rasterization.
- **Smart Document Triage**: Developed a multi-factor classification heuristic analyzing text density and keyword signals within the first pages of PDFs to route documents definitively to either Invoice or Contract pipelines without causing schemas cross-contamination.

### Changed
- **Contract Metadata Exposure**: Exposed `ChunkCount`, `IsPartial`, and `ConflictsDetected` to track performance and data reliability of long-text ingestion paths without breaking existing presentation-layer mappings.

## [2.39.5] - 2026-04-08

### Changed
- **Adaptive OCR Routing (Phase 2)**: Introduced a Text-First extraction path using `PdfiumViewer` to preemptively extract text from native PDFs. Bypasses the heavy Vision payload generation for clean invoices, reducing extraction costs by ~98%. Scanned or insufficient documents automatically fall back to the Vision API rasterization.
- **Extraction Telemetry Enhancement**: Enriched `ExtractionResultDto.Metadata` with `RoutingStrategy`, `DetailMode`, and `NativeTextDetected` logic for seamless real-time consumption and token cost audits.

## [2.39.4] - 2026-04-08

### Added

- **Token & Cost Observability**: Extracted and mapped OpenAI token consumption (Prompt, Completion, Total) directly into `ExtractionResultDto.Metadata`. This telemetry is seamlessly exposed to the frontend payload, guaranteeing baseline observability for token consumption modeling.
- **Provider Payload Telemetry**: Added diagnostic logging in `OpenAiDocumentExtractionProvider` pointing exactly to the payload character size sent to the GPT Vision layer, further solidifying observability.

### Changed

- **Adaptive Document Rasterization Engine (Phase 1)**: Overhauled the OCR PDF rendering layer inside `OpenAiDocumentExtractionProvider` to decisively slash overarching token burn rates.
  - Transferred image projection output from lossless PNG to compressed `ImageFormat.Jpeg` (Quality: 85).
  - Reduced default rasterization limits from `300 DPI` to `150 DPI`, allowing smaller tile allocations in OpenAI's Vision model while preserving reading integrity.
  - Bounded initial analysis to `3 pages` max for financial invoices, averting runaway extraction over long procedural annexes.
  - Intercalated a `DocumentRenderProfile` structure to allow fluid, programmatic switching of DPI/quality policies pending future `Contract` analysis needs.

### Fixed

- **OpenAI Vision Payload Inflation**: Resolved a systemic token-burn issue where OpenAI billed ~37,000 prompt tokens per JPEG page due to `System.Text.Json` escaping base64 characters (e.g. `+` to `\u002B`). Injected `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` during payload construction to maintain standard Base64 integrity, allowing OpenAI to successfully process images as native tiles rather than enormous unstructured textual arrays.

## [2.39.3] - 2026-04-08

### Added

- **Approval Detail Financial Context**: Implemented a new `DecisionFinancialTrendLine` visual chart inside the Approval Center to contextualize request amounts alongside historical expenditure.
  - Generates real-time, comparative trend lines showing "Pending Payment" versus "Paid" items over configurable resolutions (Weeks or Months).
  - Integrates two scopes (`PLANT` and `DEPARTMENT`) to empower approvers with cross-sectional visibility of their spend throughput before making a decision.
  - Dynamically calculates item count and provides interactive tooltips indicating both monetary volume and request volume.
  - Upgraded internal timeline engine to safely aggregate and label ISO 8601 week mappings (`System.Globalization.ISOWeek`).
- **Development Tooling**: Enhanced the Dev Seeding engine to construct deep historical status lineages, seamlessly testing the decision intelligence pipeline without organic wait times.
- **Admin System Logs Analytics**: Upgraded the operational Logs panel into a rich observability dashboard.
  - **Deep Search**: Added full-text search capability into JSON `Payload` and `ExceptionDetail` stacks.
  - **Live Tail**: Introduced a toggleable auto-refresh worker (10s polling) to monitor systems in real-time.
  - **Activity Histogram**: Integrated Recharts to generate a temporal bar chart stacking errors, warnings, and infos for instant visual anomaly detection.
  - **Export CSV**: Added native `/export` endpoint and UI integration for bulk log extraction.
  - **JSON Clipboard**: Expanded log detail modal with one-click JSON payload copy capabilities.
- **Application Versioning**: Integrated global portal version tracking.
  - Centralized application settings payload onto standard `config.ts`.
  - Statically anchored the release version label (`v2.39.3`) to the Navigation Sidebar footer and the Public Login panel to aid deterministic bug reporting.
- **Finance History Observability**: Modernized the History & Audit interface for financial operators.
  - **Timeline Hybrid UI**: Replaced standard datagrid with a date-grouped visual timeline reflecting actions like Scheduled, Paid, Notes, and Return adjustments using color-coded nodes.
  - **Contextual Data Joins**: `FinanceHistoryItemDto` now aggregates associated metadata from the overarching Request (`RequestNumber`, `RequestTitle`), empowering instantaneous understanding of atomic historical events without secondary lookups.
  - **Export CSV Engine**: Delivered a backend-rendered, pre-formatted native export route decoupled from front-end table parsing limitations.
  - **Quick Searching & Filtering**: Injected global full-text evaluation for actor names, notes, and specific request numbers directly onto the timeline components.
- **Finance Dashboard Analytics**: Fully modernized the Finance Overview workspace into a data-driven cockpit.
  - **Recharts Integration**: Implemented Bar charts for 15-day Cash Flow Projection and Donut charts for Currency Liability Exposures (`AOA`, `USD`, `EUR`).
  - **Supplier Concentration**: Implemented a "Top 5" unpaginated live ranking to surface heavily centralized current debts.
  - **Operational Metrics**: Added a brutalist 'Aging' segment to track the latency (0-2, 3-5, 5+ days) of financial operations waiting for resolution.
  - **UX & Onboarding**: Deployed a dynamic "Help Glossary" Modal mapping out exactly how to read each KPI, matched with descriptive deep empty-states for sparse data environments.
- **Corporate Isolation Engine**: Re-engineered the Finance Pipeline to enforce dynamic company isolation.
  - Injected an interactive tab-view intercepting the `api.lookups.getCompanies()` Master Data to render available Active Entities (e.g., AlplaPLASTICO, AlplaSOPRO).
  - Wired `FinanceController.GetSummary` to accept an optional `companyId` constraint via `[FromQuery]`, enabling perfect segregation of debts, currency, and aging per CNPJ while maintaining the ability to scope `Global (Consolidated)`.

## [2.39.2] - 2026-04-08

### Added

- **Approval Allocation Interactivity**: The "Pendência de Alocação" warning on the Approval Detail Panel is now fully interactive. Clicking the warning triggers an automatic semantic scroll to the line items section, accompanied by a 5-second red pulse indicating exactly where the approver needs to operate.
- **Empty Field Highlighting**: Triggering the missing allocation warning now applies a persistent, high-contrast red border to any specific Plant or Cost Center dropdowns that are missing values, automatically dismissing once valid selections are made.
- **Bulk Apply Tooltips**: Added dark-mode hover tooltips to both layout variations of the "Aplicar aos X pendentes" buttons to explicitly clarify their function in filling unassigned selections.

## [2.39.1] - 2026-04-07

### Added

- **Finance Payment Scheduling Attachments**: Added optional file upload capabilities within the `FinanceActionModal` to support payment scheduling proofs using the backend `PAYMENT_SCHEDULE` document type.
- **Enhanced Payment Due Date Visibility**: The Finance payments grid now displays both the original due date ("Original") and the user-defined scheduled date ("Agendado") safely without conflation.

### Fixed

- **Payment Overdue Logic Refactor**: Refactored `FinanceController` overdue evaluations (`IsOverdue`, `IsDueSoon`) to prioritize the explicity-set `ScheduledDateUtc` over `NeedByDateUtc`. This eliminates false-positive overdue alerts for invoices that treasury deliberately rescheduled to future dates.

## [2.39.0] - 2026-04-07

### Added

- **Finance Workspace**: Implemented a comprehensive and compact operational cockpit for the treasury and accounts payable team under the "Finanças" navigation group.
  - **Overview Dashboard**: Added dynamic KPI cards tracking pending actions, scheduled volumes, overdue alerts, and completed monthly volume. Includes a curated "Immediate Attention" alert panel.
  - **Payments List**: A brutally efficient data grid aggregating all payment-pending requests. Hardened backend eligibility strictly requires a P.O. attachment before items enter the queue.
  - **Financial Action Handlers**: Direct UI actions allowing the finance team to *Schedule*, *Mark as Paid*, *Add Notes*, or *Return for Adjustment* per-request, utilizing a modern, Brutalist-compliant `<FinanceActionModal />` triggered from a standard `<KebabMenu />`.
  - **Dedicated Finance Return Flow**: Introduced new internal status `WAITING_PO_CORRECTION` ("Devolvido para Compras") to safely return invalid requests from Finance back to Purchasing without conflating states with general Approver rejections.
  - **Audit History**: Native tracking showing all actions taken by the finance team in a read-only audit log.
- **Backend Finance Services**: Segregated financial orchestration down into `FinanceController.cs` maximizing performance over the `RequestsController` and returning strictly typed DTOs mapping to the new namespace `AlplaPortal.Application.DTOs.Finance`.

## [2.38.2] - 2026-04-07

### Fixed

- **Quotation Attachment Anti-Duplication**: Resolved a functional regression where the SHA-256 duplicate warning modal failed to appear in the "Gestão de Cotações" workspace.
  - Fixed a React state-batching/closure bug in `QuotationEntry.tsx` that caused the loading state to flip prematurely.
  - Extended the pre-flight hash validation to the **OCR Import Flow** (`BuyerItemsList.tsx`), ensuring a consistent soft-block when uploading previously processed documents.
  - Implemented a standard file input reset pattern to ensure repeated selections of the same file correctly trigger the verification logic.
- **Supporting Document Visibility**: Surfaced non-quotation items (supporting attachments) in the Quotation Management header to provide buyers with full document context during analysis.


## [2.38.1] - 2026-04-07

### Added

- **Quotation Assignment Notifications**: Real-time notifications dispatched to the requester and the assigned buyer when a quotation is claimed or explicitly assigned.
- **Viewable Request Context**: Surfaced **Request Title** and **Description/Notes** in the Quotation Management Workspace to provide buyers with immediate context without needing to open the full request.

### Fixed

- **Buyer Assignment Resolution**: Safely mapping HTTP 204 responses on `assign-buyer` endpoints to prevent runtime parse failures.
- **UX Parity in Quotation Management**: Fixed a gap in API mapping in `LineItemsController` to correctly hydrate and display the assigned buyer's name.

## [2.38.0] - 2026-04-06

### Added

- **Request Document Anti-Duplication (Soft-Block)**: Implemented an intelligent pre-flight `SHA-256` hashing validation on the Frontend via Web Crypto APIs (`computeFileHash`) that converts physical documents to checksums regardless of format (PDF, PNG, Excel).
- **Server-Side File Verification**: Extended `AttachmentsController.cs` for physical duplicate monitoring. Prevents redundant document extraction and displays duplicate warnings in both Payment Request Creation (`RequestCreate.tsx`) and the Quotation Management Flow (`QuotationEntry.tsx`). Includes UX overrides for intentional duplication.

## [2.37.1] - 2026-04-06

### Fixed

- **Payment Request Date Validation Rejection**: Resolved the blocking 400 Bad Request error occurring during the creation or editing of requests with past dates.
  - Removed strict business constraints on the backend (`RequestsController.cs`) that rejected `NeedByDateUtc` values matching past dates.
  - Aligned backend flexibility with the updated frontend UX (which issues visual warnings via `AlertTriangle` rather than hard blockers).
  - Improved OCR flow fallback to transparently handle mapping of document dates into the unified `NeedByDateUtc` architecture.
- **OCR Unit Fallback Reliability**: Fixed an issue where the OCR extraction process would incorrectly save deactivated units (like 'EA') directly to item requests.
  - Re-engineered `useOcrProcessor.ts` to implement a rigid fallback sequence mapping unidentified or raw string aliases into standard system-ready `UN` identifiers.
  - Exposed a new interactive "UNID." selection field seamlessly bridging into the front-end OCR Item configuration table (`RequestCreate.tsx`).
- **Payment Request Table UI Cleanup**: Stripped contextually redundant columns ("Centro de Custo", "Vencimento", "Status") from the visual list space (`RequestEdit.tsx`) to declutter UX without mutating backend constraints.
- **Payment Request OCR & Manual UX Standardization**: Completely decoupled the OCR and Manual input modes from the legacy grey bounding box, adopting the "Gestão de Cotações" premium clean-card UI pattern.
  - Resolved nesting context logic in `RequestCreate.tsx` where manual inputs mistakenly operated visually as OCR success extracts.
  - Eliminated manual insertion friction by replacing "AAAA-MM-DD" text placeholders with native browser Date objects.
  - Integrated deterministic visual states parsing out the "DADOS EXTRAÍDOS COM SUCESSO" banner from an independent "INSCRIÇÃO MANUAL DA FATURA" banner.
  - Exposed full "Adicionar Item" lifecycle hooks nested inside and outside empty states to stabilize the UX for completely manual Payment Requests.
- **Approval Historical Price Intelligence**: Integrated an automated price-variance alert directly into the Approval Detailed Panel (`ApprovalDetailPanel.tsx`).
  - Visually flags requests that contain items priced above their historical averages (Yellow Warning).
  - Highlights favorably (Green Warning) when all historical item pricing stays aligned or below market average.
  - Decluttered redundant currency outputs in the hero header (`DecisionHeader.tsx`) and enlarged Request Number prominence.

## [2.37.0] - 2026-04-06

### Changed

- **Approval Workspace Overhaul**: Completely modernized the user interface for the Approval Detail panel. Replaced all legacy brutalist remnants (raw black borders, strong shadows) with the new 'Premium Corporate' aesthetic leveraging `--color-bg-surface`, `--shadow-sm`, and `--radius-lg`.
- **CSS Infrastructure Upgrade**: Migrated embedded nested panels (`DecisionInsightsPanel`, `DetailedHistoryPanel`, `DecisionTimeline`, `DecisionQuotationCard`) away from pure Tailwind utility dependency toward strict inline mapping to the internal `tokens.css` design system. This eliminates widespread rendering failures previously caused by global layout class collisions.
- **Adaptive UX Fallbacks**: Hardcoded deterministic overrides to fallback between grid 'Cards' and table 'List' structures automatically based on strict `> 5 item` thresholds inside the Approval viewer.

## [2.36.0] - 2026-04-06

### Added

- **Item-Level Cost Center Mapping (Area Approval)**: Transitioned Area Approval from request-level to item-level cost center assignment.
  - **Granular Allocation**: Approvers must now assign a cost center for each individual active line item in multi-item requests.
  - **Plant-Aware Filtering**: Cost center options dynamically filter themselves based on the authoritative `PlantId` belonging to the specific line item, preventing cross-plant financial misallocation.
  - **Safe Bulk-Fill Helper**: Introduced a "Repetir" helper that explicitly targets only unassigned items sharing the same plant.

### Changed

- **Decision Summary UX**: Upgraded the "Centro de Custo" card in the Decision Summary Panel to be highly reactive, introducing strong unassigned alert states (`Pendente X itens`) and clearly delineating uniform vs. mixed assignments.
- **DTO Migration**: Shifted `ApprovalActionDto` from a single `CostCenterId` to a `Dictionary<Guid, int>` representing `ItemCostCenters`.

## [2.35.0] - 2026-04-06

### Added

- **Reactive OCR Supplier Workflow (New Request)**: Relocated the unresolved supplier validation from Request Edit to the New Request screen for a proactive "pre-creation" experience.
  - **Visible Preservation**: OCR-extracted supplier names are now displayed even without a database match, marked with a clear "Unresolved" state.
  - **Quick-Create Integration**: In-place `QuickSupplierModal` access during OCR preview, with automatic state synchronization and selection upon successful creation.
- **Backend Portal Code Generation Hardening (DEC-098)**: Re-engineered the `Supplier.PortalCode` generation logic to be structurally robust and concurrency-safe.
  - **Auto-Sync / Self-Healing**: The system now automatically detects and fixes out-of-sync counters by querying the actual `Suppliers` table for the maximum existing suffix before incrementing.
  - **Concurrency Safety**: Implemented database-level `UPDLOCK, ROWLOCK` within a transaction to prevent duplicate code assignment during simultaneous creations.

### Fixed

- **Supplier Portal Code Collision**: Resolved the critical unique index violation (`IX_Suppliers_PortalCode`) where the system incorrectly reused `SUP-000001` after a transactional data reset.

### Changed

- **Maintenance Script Protection**: Updated `ResetTransactionalData.sql` to exclude Master Data counters (Suppliers) from blanket resets, ensuring sequence continuity across transactional wipes.

## [2.34.0] - 2026-04-06

### Added

- **Supplier Quick-Create (Request Edit)**: Integrated the ability to create new suppliers directly from the Request Edit screen.
  - Added a `+ NOVO FORNECEDOR` button for manual creation in authorized draft/quotation stages.
  - Implemented an **unmatched OCR supplier warning** that appears when a suggested name from an invoice does not match an existing database record, providing a "CRIAR AGORA" entry point.
  - Direct integration with `QuickSupplierModal` (reused from Buyer items) with auto-selection and validation clearing upon successful creation.

### Changed

- **PAYMENT Request Type Optimization**: Refined the Request Edit UI for payment-specific workflows, removing quotation-related bloat.
  - **Conditional UI Rendering**: The "Cotações Salvas" section is now suppressed for `PAYMENT` requests, ensuring a cleaner interface focused on financial tracking.
  - **Guided Attention Refinement**: auto-scroll and highlight logic (guided attention) now strictly targets `QUOTATION` requests, preventing jarring movements to non-existent sections in payment flows.

## [2.33.2] - 2026-04-06

### Fixed

- **Approval Modal State Sync**: Fixed a stale closure bug in the Approval Center drawer where action buttons and local states derived from previous requests bled into the next request when auto-navigating. 
  - Enforced a strict React render boundary by adding a `key` prop tied to the request ID on the `ApprovalDetailPanel`.
  - Added programmatic resets for drill-down overlays (`selectedDetailedItem`) on queue navigation handlers (`handleNext`, `handlePrev`, `handleActionCompleted`).

## [2.33.1] - 2026-04-06

### Fixed

- **Payment OCR Navigation White Screen**: Fixed a critical `Uncaught TypeError` caused by an invalid framer-motion `times: [0, 1, 0]` keyframe array in the OCR loading dots animation.
  - The Web Animations API (WAAPI) requires monotonically non-decreasing offsets. The invalid array crashed framer-motion's global animation engine, corrupting **all** `motion.*` components system-wide — causing a blank white content area on the edit page and a broken user profile menu.
  - Fixed by correcting the offsets to `times: [0, 0.5, 1]`.

### Added

- **ErrorBoundary Component**: Added a React Error Boundary wrapping route components (`RequestCreate`, `RequestEdit`) and the `AppShell.Outlet`. Future render crashes will display a visible red error panel instead of a silent white screen.
- **Loading Spinner CSS**: Added missing `@keyframes spin` to `globals.css`. The `RequestEdit` loading state now shows a visible animated spinner with inline styles instead of relying on the previously nonexistent `.spinner` class.
- **Defensive Data-Load Guard**: `RequestEdit` now shows a user-friendly error panel with retry/back actions if data fails to load, instead of rendering a white screen.

## [2.33.0] - 2026-04-05

### Fixed

- **Payment OCR Rendering Regression**: Resolved the "grey block" failure where the OCR success section was visually hidden after extraction.
  - Removed `overflow: hidden` and height-restricted animations from the parent container that were clipping the expanded result table.
  - Stabilized the success branch by transitioning from `motion.div` to a standard `div` with an explicit ID.
  - Hardened state transitions to ensure the UI paints immediately upon receiving the mapped draft.

## [2.32.0] - 2026-04-05

### Fixed

- **Payment OCR Flow UX & Mapping Refinement**: Resolved data mapping and UX bugs between the OCR upload and the persisted draft.
  - **Loading UX**: Implemented a responsive loading overlay during OCR processing and disabled premature form submission.
  - **Supplier Persistence**: Fixed a missing payload mapping, ensuring the OCR-resolved Supplier ID is correctly passed to the backend and preserved in the draft.
  - **Total Consistency**: Preserved the OCR-derived, IVA/discount-inclusive final total during Payment draft creation by passing it from the frontend and explicitly preventing backend sum-based reassignment for the `PAYMENT` request type.
  - **Currency Passthrough**: Correctly mapped the OCR-extracted currency alias to the draft payload.

### Known Limitations

- **Interim Discount Handling**: Surcharges and penalties are currently folded into the total by the OCR service and are not structurally extracted. Explicit discount values are temporarily appended to the Request `Description` as a traceability workaround. A dedicated `DiscountAmount` (and scalable financial adjustment architecture) is required for a final business-model solution.
- **Company Prefill**: The system currently employs a deterministic scope-based fallback (auto-selecting the only available company for restricted users). It does **not** employ true OCR-based company matching, as the `Company` entity lacks a `TaxId` necessary for correlation.

## [2.31.0] - 2026-04-05
### Fixed

- **Payment OCR Draft Persistence (DEC-097)**: Resolved a 500 error when creating Payment requests from OCR-extracted items.
  - Relaxed entity-level mandatory constraints for `CostCenterId` and `IvaRateId` on `RequestLineItem`, making them nullable in the database.
  - Implemented deterministic `LineNumber` assignment (incremental index + 1) during the draft creation payload and backend processing.
  - Deferred strict business validation to the `SubmitRequest` stage, ensuring a request cannot progress beyond `DRAFT` status if mandatory fields are missing.
  - Corrected calculation of `EstimatedTotalAmount` in the draft creation flow to reflect OCR-extracted item totals.

## [2.30.0] - 2026-04-05

### Added

- **Payment OCR Intake (DEC-096)**: Implemented automated document extraction for the "Payment" request type.
  - Contextual OCR dropzone in `RequestCreate.tsx` that appears when "Payment" is selected.
  - Automated mapping of invoice data (Number, Date, Currency, Items) to the request draft.
  - Interactive item review and editing before initial draft generation.
  - New backend endpoint `direct-ocr` in `RequestsController.cs` for ID-less document extraction.
- **Shared OCR Processing Hook**: Refactored quotation-specific OCR logic into a reusable `useOcrProcessor.ts` hook.
  - Decoupled normalization and field mapping from UI components.
  - Centralized calculation logic for IVA and totals.

### Changed

- **Quotation OCR Refactor**: Migrated `BuyerItemsList.tsx` to use the shared `useOcrProcessor` hook, ensuring logic parity across flows.

## [2.29.0] - 2026-04-04

### Added

- **Company Master Data Management**: Implemented a new "Empresas" tab in the Master Data UI to manage legal entities.
  - Supports full CRUD operations (Create, Read, Update, Toggle Active).
  - Integrated a User Lookup filtered by the `Final Approver` role for company-level assignment.
  - Implemented protection against renaming companies already associated with historical requests.
- **Backend CRUD for Companies**: Extended `LookupsController.cs` with robust endpoints for company management, including `FinalApproverUserId` support.

### Changed

- **System-Resolved Final Approver (DEC-093)**: Replaced manual requester-side actor selection with deterministic backend resolution based on the selected company.
- **Workflow Submission Gating**: Enhanced `RequestsController.cs` validation to strictly require a company-level Final Approver assignment before allowing submission.

## [2.28.0] - 2026-04-04

### Added

- **Placeholder & Field Legibility Design Tokens**: Introduced specific semantic tokens in `tokens.css` for form fields:
  - `--color-placeholder`: High-contrast grey for inactive placeholders.
  - `--color-placeholder-focus`: Increased contrast when the field is focused.
  - `--color-field-disabled-bg` & `--color-text-field-disabled`: Standardized colors for disabled inputs.
  - `--color-field-readonly-bg`: Visual distinction for read-only fields.

### Fixed

- **Project-Wide Accessibility Audit (Placeholders)**: Remediated low-contrast placeholder rendering by removing opacity-based transparency and relying on resolved high-contrast tokens.
- **Form Readability Normalization**: Removed global `uppercase` text-transform from placeholders and custom autocompletes to improve legibility for long-form examples.
- **Native Select Placeholder State**: Standardized the "-- Selecione --" (empty) state in native `<select>` elements using the `:has()` selector to mirror text input placeholder styling.
- **Autocomplete Component Standardization**: Updated `CostCenterAutocomplete` and `SupplierAutocomplete` to use new tokens and follow the global accessibility standard.

## [2.27.1] - 2026-04-04

### Fixed (2.27.1)
- **Drawer Layering Logic**: Resolved a systemic bug where `Z_INDEX` constants (strings) were being incremented in JavaScript, resulting in invalid CSS values. Replaced with valid `calc()` expressions in:
  - `UserProfileDrawer.tsx` (Fixed "Meu Perfil" visibility)
  - `ApprovalCenter.tsx` (Fixed resize handle visibility)
  - `PurchasingHelpDrawer.tsx` (Fixed "Manual de Operação" visibility)

## [2.27.0] - 2026-04-04

### Added
- **Scoped Admin Controls (DEC-095)**: Implemented role-based authority restrictions for Local Managers.
  - Empowered `Local Managers` to assign `Area Approver` and operational roles (`Requester`, `Receiving`, `Import`, `Viewer`).
  - Strictly restricted assignment of governance roles (`Buyer`, `Finance`, `Contracts`, etc.) to System Administrators.
  - Enforced scope-based filtering: Managers only see and assign plants/departments within their authorized boundary (e.g., `V1`, `V3`).
- **Receiving Workspace Scope Filtering**: Enforced plant/department data isolation in the Receiving module based on the logged-in user's profile.

### Fixed
- **Global Search Readability**: Scoped `::placeholder` styling to the header context (`.header-global-search`), ensuring high contrast on the dark topbar without leaking into light-surface search fields.
- **User Listing Visibility**: Harmonized Name/Code matching logic in User Management filtering, ensuring newly created scoped users are immediately visible to their managers.
- **Dynamic Master Data Loading**: Refactored admin forms to wait for `currentUser` profile initialization before populating filtered lookup options.

### Changed
- **Unified Navigation Governance**: Refactored navigation configuration to use role-based eligibility arrays, preventing unauthorized module discovery via group-level inheritance.

## [2.26.0] - 2026-04-04

### Changed

- **Instruction Layer Rebuild**: Consolidated agent governance into a lean, stable foundation.
- **Directives Consolidation**: Merged Documentation hygiene into `SOP_TASK_CLOSING.md` and unified status/stage rules into `RULE_WORKFLOW_PERMISSIONS.md`.
- **UI-Level Cleanup**: Removed legacy global workflows and redundant lifecycle rules from the Antigravity Customizations interface.
- **Legacy Migration**: Reorganized task-specific materials and reference SOPs into dedicated storage (`docs/rules/`).

## [2.25.1] - 2026-04-04

### Fixed

- **Tooltip Overflow**: Resolved UI clipping in the User Management drawer by implementing an explicit placement API in the shared `Tooltip` component and applying inward alignment for role-specific help text.

## [2.25.0] - 2026-04-04

### Added
- **Role Selection UX**: Implemented contextual help for the "Funções e Permissões" section in User Management.
  - Added a centralized `ROLE_DESCRIPTIONS` mapping for role definitions in `roles.ts`.
  - Integrated `Tooltip` and `Info` icons for every role option in the Create/Edit drawer.
- **Enhanced Role Constants**: Restored `ROLES` as the definitive shared source for role keys while safely augmenting it with descriptions.

### Fixed
- **White Screen Regression**: Restored the `ROLES` export in `src/frontend/src/constants/roles.ts`, resolving a critical build failure that prevented the application from rendering.
- **Table Header UX**: Standardized header contrast in `BuyerItemsList.tsx` and `MasterData.tsx` to align with the Modern Corporate design.


## [2.23.0] - 2026-04-03

### Added
- **Buyer Notifications for Updates**: Implemented automatic informational notifications for the assigned buyer when a requester modifies a request in `WAITING_QUOTATION` status. Includes a concise summary of changed fields.
- **Notification Service Extension**: Extended `INotificationService` with `CreateNotificationAsync` for discrete informational messaging.

### Fixed
- **Controlled Edit Persistence (Hotfix)**: Resolved a critical regression where requester edits in the "Aguardando Cotação" stage failed to persist.
- **Safe Comparison Logic**: Implemented `.Trim()` and null-coalescing in backend comparisons to ensure reliable change detection and history audit trails.
- **Backend Build Regression**: Corrected invalid `IsDeleted` property access on the `Quotation` entity across multiple controller methods.

### Changed
- **Requests List UX Restoration**:
  - Restored Request Number as a clickable link for direct navigation.
  - Optimized column widths: increased "Número" and reduced "Ações" for better content fit.
- **Smart Dirty Check**: Added a pre-modal check to `RequestEdit.tsx` to prevent unnecessary save confirmations when no header changes are detected.

## [2.22.0] - 2026-04-03

## [2.21.0] - 2026-04-03

### Added

- **Modern Corporate Visual Foundation (Phase 1)**:
  - New design tokens for border radii (`sm: 4px`, `md: 8px`, `lg: 12px`).
  - Standardized **Soft Elevation** tokens (`--shadow-sm`, `--shadow-md`, `--shadow-lg`).
  - Standardized **Border** tokens (`--color-border: rgba(0,0,0,0.1)`).
- **Core Component Refactor**:
  - `KPICard`: Executive-focused layout with soft elevation and neutral baselines.
  - `Sidebar`: Refined active states, thin borders, and modernized flyout menus.
  - `Topbar`: Reduced accent border and added subtle shadow.
  - `AppShell`: Replaced brutalist grid/borders with refined, rounded containers.

### Changed

- **Global Styling**:
  - Reduced button and form borders to `1px`.
  - Softened table borders and density while maintaining readability.
  - Removed brutalist hard-shadow offsets across the application.
- **Visual Alignment**:
  - Updated `collapsibleSection` and `badge` styles to follow the new radii standards.
- **v2.35.0**: Reactive OCR Supplier Workflow & Backend Portal Code Hardening (DEC-098). Relocated supplier validation to New Request screen and implemented robust, self-healing, concurrency-safe portal code generation.
- **v2.34.0**: Supplier Quick-Create in Request Edit & Payment Type Cleanup. Added supplier creation entry point for OCR mismatches and removed quotation-specific UI for payment requests.
- **v2.30.0**: Payment OCR Intake & Shared Hook (DEC-096). Implemented automated document extraction for Payment requests and refactored OCR logic into a shared hook.
- **v2.29.0**: Company Master Data & Final Approver Resolution. Implemented centralized company management and deterministic approval resolution, eliminating manual selection errors.
- **2.26.0**: Instruction Layer Cleanup & Baseline Rebuild. Consolidated fragmented permission and status rules into unified directives. Streamlined the process lifecycle and reorganized legacy documentation into reference storage.
- **2.25.1**: Tooltip Positioning Fix. Optimized the shared `Tooltip` component API to support explicit side-anchoring and alignment, resolving overflow regressions in the User Management drawer.
- **2.25.0**: Role Selection UX & UI Stability. Implemented contextual role tooltips for User Management and fixed a critical white screen regression by restoring the core `ROLES` constant. Standardized table header readability across operational modules.
- **2.24.0**: Brand Identity & Favicon Integration. Implemented a comprehensive favicon set based on the "A2 P-G" corporate logo, replacing the default Vite identity. Optimized for various devices (mobile, desktop, apple-touch). Also fixed a critical table header readability bug in the Master Data module.
- **2.23.0**: Request Edit Persistence & Buyer Notifications. Hotfixed the controlled-edit persistence regression and implemented automatic buyer notifications for requester updates in the quotation stage. Restored clickable request numbers and optimized list column widths.
- **2.22.0**: Modern Corporate UI Refinement (Phase 2). Significant interactive and visual elevation across high-traffic operational screens (Dashboard, Requests, Receiving). Replaced brutalist remnants with Soft Elevation and premium typography.
 Blue is now strictly reserved as an accent for actions and highlights.
- **v2.20.0**: Official Design Direction Transition. Formally transitioned the project from "Industrial Brutalist" to Modern Corporate. UI/UX Guidelines updated to emphasize soft elevations, refined borders, and rounded corners.

## [v2.19.0] - 2026-04-02

### Added

- **Approval Intelligence Tooltips**: Contextual hover explanations for all decision metrics (Monthly/Annual totals, Budget Impact, Purchase History, Variation) in the `DecisionInsightsPanel`.
- **Refined Cost Center Validation (DEC-090)**: Area Approval logic now differentiates between request types. PAYMENT requests with unified Cost Centers across items are automatically validated and read-only. Inconsistent or missing Cost Centers for PAYMENT, and all QUOTATION requests, still require explicit mandatory selection to ensure financial unicity (DEC-085).
- **Cost Center Propagation**: The selected Cost Center is automatically applied to all line items of the request upon Area Approval, ensuring data integrity for financial reporting.

## [v2.18.1] - 2026-04-02

### Changed

- **Sidebar Accordion Behavior**: Implementation of a single-open model for the expanded sidebar navigation. Opening one section automatically collapses others, reducing vertical bloat and improving navigation speed.
- **Auto-Expansion Refinement**: Optimized the route-awareness logic to ensure only the strictly relevant section expands upon navigation.

## [v2.18.0] - 2026-04-02

### Added

- **Navegação em Sidebar Recolhido (Hover Flyouts)**:
  - Implementação de painéis laterais (flyouts) que surgem ao passar o mouse sobre os ícones no modo recolhido.
  - Exibição do nome da seção e links de subseções com ícones.
  - Lógica de anti-flicker (150ms delay) e posicionamento inteligente (viewport-aware).
  - Fallback de clique: clicando no ícone no modo recolhido, o sistema agora navega para o primeiro link disponível daquele grupo.
- **Portal Rendering**: Integração com `DropdownPortal` para garantir que o menu lateral nunca seja cortado pelo shell da aplicação.

## [v2.17.0] - 2026-04-02

### Added

- **v2.18.0**: Sidebar Hover Flyouts (Navigation Overhaul)
- **v2.17.0**: Phase 2 Security Hardening (IP-based Rate Limiting)
- **Security Hardening (Phase 1)**: Implemented a robust security baseline focusing on attachment safety and authentication protection.
- **Attachment Upload Hardening**:
  - **Extension Whitelist**: Restricted uploads to a specific set of safe business extensions (`.pdf`, `.jpg`, `.jpeg`, `.png`, `.doc`, `.docx`, `.xls`, `.xlsx`).
  - **File Size Limits**: Enforced a strict 15MB limit per file on the backend.
  - **Filename Sanitization**: Implemented alphanumeric/hyphen/underscore sanitization for display filenames to prevent UI injection and path traversal.
  - **Storage Isolation**: Physical files are now stored using GUIDs, completely decoupling internal storage from user-provided names.
  - **MIME Verification**: Added basic Content-Type consistency checks as an additional security signal.
- **Login Brute-Force Protection**:
  - **Account Lockout Policy**: Implemented a temporary 15-minute lockout after 5 consecutive failed login attempts.
  - **Generic Error Messaging**: standardizing on a single unauthorized message to prevent user/account enumeration.
  - **Security Audit Logging**: Key security events (Lockouts, Blocked attempts) are now recorded in the `AdminLogEntries` table.

### Changed

- **User Entity Update**: Added `AccessFailedCount` and `LockoutEndUtc` fields to the core identity model.
- **Centralized Security Configuration**: Moved security parameters (lockout duration, attempt count, file limits, whitelists) to a dedicated `Security` section in `appsettings.json`.

### Added

- **Anti-Accumulative Copy Request Flow**: Implemented a robust "Copy Request" feature that uses a template-driven, frontend-first approach.
- **Strategic Data Exclusion**: The copy flow explicitly strips downstream operational data (items, currency, need-by date, attachments) to ensure the new request starts as a clean business need.
- **Title Composition**: Automatically generates the new title in the format `Cópia {SourceNumber} {OriginalTitle}`.
- **Ephemeral Draft State**: Copied requests exist only in the browser's memory until the user explicitly submits them, preventing database pollution from abandoned copies.
- **UX Safeguards**: Replaced "Cancelar" with "Descartar Cópia" in copy mode and added a mandatory warning banner for the copied description.
- **Navigation Protection**: Implemented `beforeunload` protection to prevent accidental loss of copied/edited data.

### Changed

- **Backend Template Mapping**: Updated `RequestsController.GetRequestTemplate` and `CreateRequestDraftDto` to support source request identification and title composition.

## [2.14.0] - 2026-04-02

### Added

- **Global UI Layering & Z-Index Standardization**: Unified the z-index hierarchy across the entire portal using centralized architectural tokens (`Z_INDEX` constants).
- **DropdownPortal Pattern**: Mandatory implementation of React Portals for all overlays (modals, drawers, tooltips, dropdowns) to ensure consistent rendering at the root level.

### Changed

- **AppShell Refactor**: Destroyed the global stacking context trap by removing `zIndex: 1` from the main content area, allowing fixed overlays to overlap the interface correctly.
- **Component Standardization**: Full refactor of `UserProfileDrawer`, `PurchasingHelpDrawer`, `Tooltip`, `Feedback`, `KebabMenu`, and `FilterDropdown` to adhere to the standardized positioning rules.

### Fixed

- **Layering Inconsistencies**: Resolved multiple bugs where confirmation modals appeared behind side drawers or top headers.

## [2.13.4] - 2026-04-02

### Fixed

- **Workflow**: Corrected a validation bug in the `Resubmeter Pedido` flow. High-level resubmission for requests in adjustment phases now correctly accounts for items contained within saved quotations, preventing false-positive "zero items" errors.

## [2.13.3] - 2026-04-02

### Fixed

- **Finance**: Corrected Quotation IVA calculation logic. IVA is now calculated on the net taxable amount (Gross - Discount) instead of the Gross subtotal.
- **Finance**: Populated `TotalTaxableBase` and `TotalDiscountAmount` in the Quotation entity and DTOs for consistent UI representation.
- **UI**: Updated `QuotationEntry` and `BuyerItemsList` to reflect the corrected calculation logic in the summary footers.

## [v2.13.2] - 2026-04-01

### Added

- **Alert Visibility — KPI Tooltip**: Added a hover tooltip to the "Com Alertas" KPI card in the Approval Center dashboard, explaining its meaning ("Pedidos com pontos de atenção na análise."). Reuses the portal-standard `Tooltip` component with structured content (label + definition).
- **Alert Visibility — Row Indicator**: Added a discreet `AlertCircle` icon (14px, rose) next to the request number in approval queue table rows for QUOTATION requests missing a selected winner. Includes a dark-variant tooltip on hover ("Requer atenção na análise"). No backend changes — reuses existing client-side alert condition.

## [2.13.1] - 2026-04-01

### Added

- **Detailed History Drill-down (Procurement)**: Implemented a high-density "sliding sub-panel" (drawer-within-a-drawer) that allows approvers to inspect historical purchase data for specific line items.
- **Normalized Matching Logic**: Backend support for matching items via normalized descriptions (levenshtein-like matching in SQL), providing a "Descrição Aproximada" context with a 1-year lookback.
- **Executive Drill-down UX**: Added a "Ver Detalhes" action to intelligence cards in the `DecisionInsightsPanel`. The sub-panel slides over the main drawer, maintaining approval context while opening deep historical data.
- **Price Variation Analysis**: The drill-down panel displays unit price comparisons against historical records with visual indicators for "Last Purchase" and price deviations.
- **Role-Aware Drill-down**: The entry point adapts its visual style based on the approver role (Area vs Final), ensuring a cohesive experience in the `DecisionInsightsPanel`.

### Changed

- **Drawer Header Redesign**: Replaced the heavy black header bar with a lighter, neutral surface with an 8px contextual left-accent border (blue for Area, green for Final). Improved prev/next navigation controls as 32px brutalist buttons.
- **History Panel Transition**: Replaced the "giant top-mounted slab" with a focused sliding sub-view using backdrop dimming and blur for maintained user focus.

## [2.13.0] - 2026-04-01

### Changed

- **Approval Center Visual Harmonization**: Full visual alignment of the `Centro de Aprovações` with the `Pedidos` (`RequestsList.tsx`) design standard.
- **Page Layout Refactor**: Transitioned to a full-width, flex-based layout with standardized gaps (`24px`) and margins.
- **Header Standardization**: Aligned typography, border tokens (`var(--color-primary)`), and hierarchy. Moved the header to the top of the workspace for consistent page rhythm.
- **KPI Card Integration**: Replaced custom card implementations in `QueueSummary.tsx` with the system-standard `KPICard` component for consistent motion and branding.
- **Filter Hub Modernization**: Updated sorting and filtering controls to use `--color-bg-surface`, `--color-primary` borders, and `var(--shadow-brutal)`.
- **Unified Filter Chips**: Standardized active-state behavior to use `var(--color-primary)` for all filter/sort chips, ensuring color consistency across the portal.
- **Queue Table Styling**: Normalized table wrappers and headers using global CSS tokens, replacing Tailwind-specific overrides with the system's "Brutalist" shadow and border patterns.
- **Loading & Empty States**: Standardized all feedback states with uppercase text patterns and design tokens.
- **Dev Tools Footprint**: Minimized the visual dominance of development tools to reduce UI noise.

## [2.12.2] - 2026-04-01

### Added

- **Role-Aware Decision Intelligence (DEC-084)**: The `DecisionInsightsPanel` now adapts emphasis and content based on the current approval stage (`AREA` vs `FINAL`), providing contextually relevant decision support without duplicating the drawer or forking the component.
- **Context Banner**: Subtle role indicator at the top of the intelligence panel — "Foco: Legitimidade e Necessidade" (Area) or "Foco: Racionalidade Financeira" (Final).
- **Area Emphasis — Checklist de Legitimidade**: Compact informational checklist with ✅/⚠️ indicators for Centro de Custo, Justificativa, Fornecedor, and (for Quotation types) Cotação Formalizada. Purely visual — no new approval blockers.
- **Final Emphasis — Visão Financeira Comparativa**: KPI grid surfacing Year Accumulated Total (newly exposed), Historical Purchase Count, and Weighted Consolidated Variation with partial-coverage disclaimer when not all items have history.
- **Role-Based Section Reordering**: Shared intelligence blocks (Alerts, Department KPIs, Item Analysis) render in different priority order per approval stage — Area prioritizes alerts, Final prioritizes financial context.
- **Extracted Reusable Primitives**: Internal refactor of `SectionLabel`, `KpiCard`, and `ItemCard` sub-components for consistency and maintainability.

## [2.12.1] - 2026-04-01

### Added

- **Resizable Approval Center Drawer**: Implemented horizontal resizing for the side drawer on desktop viewports (> 768px).
- **Persistent Width**: Support for saving and restoring drawer width via `localStorage` (`approvalDrawerWidth`).
- **Responsive Layout Reflow**: Optimized `DecisionInsightsPanel` (Visão Departamental and Análise por Item) to adapt naturally to varied drawer widths using responsive grid templates.
- **Interactive UI Handle**: Added a subtle, reactive resize handle on the left edge with high-hit-area hitboxes and hover indicators.
- **Consequences:** Provides a more premium and accessible interface. Requires a phased refactor of core design tokens and shell components in the next implementation cycle.

---

## DEC-096 — Shared OCR Processing & Direct Extraction

- **Date:** 2026-04-05
- **Status:** Accepted
- **Context:** The "Payment" request type requires automated data entry from invoices before the request is officially created. Existing OCR logic was tightly coupled to the Quotation workspace and required a Request ID.
- **Decision:** 
    - Abstract OCR normalization and mapping into a shared `useOcrProcessor` hook.
    - Implement a `direct-ocr` backend endpoint that skips request-specific validation for initial extraction.
    - Use an isolated "Payment Draft" state in `RequestCreate.tsx` to allow user review before persistence.
- **Consequences:** Ensures consistency between Quotation and Payment extraction logic. Reduces code duplication and enables a "fast-track" creation flow for payment-intensive workflows.

- **Date:** 2026-04-04
- **Status:** Accepted
- **Context:** Administrators adding or editing users often require clarity on the specific permissions associated with each system role to ensure correct assignment and internal compliance.
- **Decision:** Implement hover-activated tooltips for the role selection list in the User Management drawer.
    - **Implementation**:
        - Create a centralized `ROLE_DESCRIPTIONS` constant in `roles.ts` using `ROLES` keys as indices.
        - Utilize the system's `Tooltip` component and `Info` (circle-help) icons next to each role checkbox.
        - **Guardrail**: The structural `ROLES` constant MUST remain exported as the shared source of truth for logic to prevent architectural regressions (e.g., white screen).
- **Alternatives considered:** Static text under each role (rejected: adds too much vertical noise).
- **Consequences:** Improves administrative usability without cluttering the UI. Establishes a- **Tooltip**: Provides contextual help via standard information icons or trigger wrappers.
    - **API**: Supports `side` ('top', 'bottom', 'left', 'right') and `align` ('start', 'center', 'end') for precise placement control in restricted containers (e.g., Drawers).
    - **Default**: `side="top"`, `align="center"`.
**: Complete UI/UX refactor of the decision support panel in the Approval Center drawer.
- **Information Architecture**: Structured intelligence into three executive sections: `Destaques de Atenção`, `Visão Departamental`, and `Análise por Item`.
- **Item-Specific Intelligence Cards**: Implemented distinct, border-accented cards for each line item, featuring purchase frequency badges, price variation grids, and previous supplier highlights.
- **Executive Typography**: Applied `font-black` (900 weight) to metrics and `text-[9px]` uppercase tracking to labels for maximum readability and hierarchy.

### Fixed

- **DecisionInsightsPanel Types**: Expanded `DecisionAlertDto['level']` to include `'ERROR'` and `'DANGER'`, resolving TypeScript compilation errors and ensuring robust alert styling.
- **Alert System**: Compact row-based alerts with severity-specific icons and color-coded left borders.
- **Department Metrics**: Redesigned KPI grid focusing on monthly accumulation and relative budget impact.

## [v2.12.0] - 2026-04-01

### Changed

- **Approval Center UX Refinement**: Replaced the previous stacked master-detail layout with a high-efficiency right-side drawer/panel pattern.
- **Queue Context Preservation**: The approval queues remain visible on the left while the selected item detail opens in the side panel, allowing for better context during review.
- **Selection Visuals**: Implemented high-visibility selected states in the queue with a `12px` accent border and unique background highlight.
- **Workflow Automation**: Added "Auto-Select Next Item" logic that automatically loads the next pending item in the same queue after a successful approval action, significantly increasing throughput for approvers.
- **Responsive Drawer**: The detail panel adapts its width based on screen size (640px on desktop, 100% on mobile).

## [v2.11.5] - 2026-03-31

### Optimized

- **Quotation Management Performance**: Resolved Cartesian Explosion issue in `LineItemsController.GetLineItems` by applying `.AsSplitQuery()` to the related data hydration phase ($Attachments$, $StatusHistories$, $Quotations$).
- **Query Efficiency**: Improved screen load time from ~10s to <1s by eliminating redundant joins and suppressing `MultipleCollectionIncludeWarning`.

## [v2.11.4] - 2026-03-31

### Added

- **Quotation Save Confirmation**: Implemented a mandatory UX confirmation modal before saving or updating a quotation in the Buyer Workspace.
- **Contextual Messaging**: The confirmation message distinguishes between OCR-extracted data and manual entries to ensure user verification of automated results.
- **Frontend Foundation Alignment**: Reused the standard "Brutalist" `ApprovalModal` component to maintain UI/UX consistency across the portal.

## [2.10.4] - 2026-04-01

### Added

- **Guided UX Attention**: Novo padrão de atenção guiada para o Aprovador de Área. Ao carregar um pedido em status de aprovação, a seção de "Cotações Salvas" expande, rola e pulsa automaticamente para guiar o usuário.

### Changed

- **Default State**: A seção "ITENS DO PEDIDO" agora inicia colapsada por padrão para reduzir o ruído visual inicial.

### 6.2 Hooks

- **`useOcrProcessor`**: Shared hook for normalizing and mapping OCR results to request drafts. Used in both `RequestCreate.tsx` (Payment flow) and `BuyerItemsList.tsx` (Quotation flow).
- **`useFeedback`**: Centralized hook for managing top-level feedback messages (success, error, warning).

### Fixed

- **Regression Fix**: Corrigido erro onde o toggle de acordeão disparava o evento de sumit do formulário principal no `RequestEdit.tsx`.
- **Dirty State Precision**: O estado inicial do formulário agora considera auto-preenchimentos de sistema para evitar avisos falsos de "sem alterações".

## [2.10.3] - 2026-03-31

### Optimized

- **EF Core Query Audit**: Resolved non-deterministic First/FirstOrDefault warnings and Multiple Collection Include warnings (Cartesian Explosion) in core request and line item modules.
- **Deterministic Ordering**: Applied explicit `.OrderBy()` on all aggregate and lookup queries to ensure stable results across list views and dashboards.

## [v2.11.3] - 2026-03-31

### Optimized

- **EF Core Query Audit**: Resolved non-deterministic First/FirstOrDefault warnings and Multiple Collection Include warnings (Cartesian Explosion) in core request and line item modules.
- **Deterministic Ordering**: Applied explicit `.OrderBy()` on all aggregate and lookup queries to ensure stable results across list views and dashboards.
- **Query Splitting**: Integrated `.AsSplitQuery()` for high-complexity detail projections (Submit, Save, Delete, Cancel, Finalize) to eliminate performance degradation from broad Cartesian joins.

## [v2.11.2] - 2026-03-31

### Added

- **TOTAL FILTRADO KPI Trend (MoM)**: Implemented Month-over-Month (MoM) trend indicator comparing current Month-to-Date (MTD) vs. same period last month.
- **Trend Safety Logic**: Automatic neutral state ('Sem comparativo') when comparison is not meaningful.
- **Operational Data Dropdowns**: Simultaneously updated `RequestCreate` and `RequestEdit` line-item forms to explicitly restrict new dropdown selections to `Active` records only, while silently preserving and rendering historical IDs safely so old requests do not crash or blank out their deactivated units.
- **Request Form UX Refactoring**: Completely reorganized the Request Draft creation and editing forms (`RequestCreate.tsx` and `RequestEdit.tsx`) into three distinct semantic phases: General Details, Workflow Participants, and Financial Summary.
- **Sticky Layout Architecture (Fixed)**: Corrected critical CSS container bugs preventing sticky-scroll behavior natively. Resolved an invalid `calc(auto)` CSS token for the Sidebar, and stripped `transform: translateY()` attributes from `framer-motion` page wrappers that were inadvertently creating rigid containing blocks preventing the Form Action Bars from sticking to the viewport. Both the left Sidebar and top Action buttons now reliably dock during deep page scrolling.
- **Read-only Total Value**: The`Valor Total Estimado`field in the header is now strictly read-only, visually highlighted, and calculated directly from the line-item grid to prevent manual data-entry errors.
- **Strict Validations**: Integrated red inline semantic highlights enforcing required interaction for: `Tipo de Pedido`, `Comprador Atribuído`, `Aprovador de Área`, e `Aprovador Final`. Missing any triggers explicit anchor scrolling toward the error.
- **Required Field Enforcement**: Added HTML5`required` attributes and backend `[Required]` annotations for `Grau de Necessidade` and `Necessário Até`fields, preventing form submission without these values.

---

## [0.4.0] - 2026-02-26

### Changed

- **Urgency Consolidation**: Removed the semantically overlapping`Priority` concept from the workflow. The system now exclusively uses `NeedLevel`(Grau de Necessidade do Pedido) to express holistic Request urgency.
- **Item Level Triage**: Added a new numeric field `ItemPriority` (Prioridade do Item) to Line Items, allowing requisitioners to rank individual item fulfillment importance (e.g. 1 = highest).

- Dropped the `Priority` Master Data Entity, DbSets, EF Core configurations, and all DTO mappings from both Frontend and Backend Application scopes entirely to reduce cognitive load.

### Fixed

- **Master Data Active/Inactive Filtering**: Fixed a bug where deactivated/sunsetted Master Data records (e.g. Unit: 'CX') were still populating inside the React Select dropdowns during new operations. The Frontend API Service (`api.ts`) now successfully defaults to `includeInactive=false` for `getUnits`, `getCurrencies`, and `getNeedLevels`, guaranteeing that disabled records are definitively excluded from new interactions while remaining historically accurate on old saved requests.

---

## [0.3.1] - 2026-02-26

### Fixed

- **Line Item Validation Unmount Bug**: Fixed an issue in `RequestEdit.tsx` where attempting to save a line item with missing/invalid fields would trigger the generic page-level error state, completely unmounting the line item child form and masquerading as a page redirect. The inline validation now persists natively on the screen.
- **Validation Field Casing Bug**: Upgraded`renderFieldError`throughout the React forms (`RequestCreate` and `RequestEdit`) to be fully case-insensitive when parsing ASP.NET`ValidationProblemDetails`dictionaries. This ensures that validation highlights (red borders and text) correctly bind to inputs regardless of camelCase vs PascalCase properties returned by the server.
- **Request List Status Separation**: Refactored the generic "Estágio Atual" column in `RequestsList.tsx` into two distinct columns: "Status do Pedido" (displaying the formal Workflow DB status badge) and "Atribuição Atual" (displaying the current responsible actors, e.g. Comprador, Aprovador).

---

## [0.3.0] - 2026-02-26

### Added

- **Master Data UI Maintenance**: Enhanced `/settings/master-data` with table state badges ("Ativo"/"Inativo") and toggle action buttons ("Desativar"/"Ativar") enabling Soft Delete operations without touching the database via SQL.
- **Master Data Duplicate Prevention**: Enforced Unique Indexes natively inside`ApplicationDbContext` for `Unit`,`Currency`, and the new`NeedLevel` tables. The `LookupsController` catches `DbUpdateException` and strictly prevents exact code duplication, showing `409 Conflict`gracefully.
- **Need Level (Grau de Necessidade)**: Created `NeedLevel` DB entity and API endpoints. Connected to the `RequestCreate` and `RequestEdit` forms, injecting the selected state seamlessly into the parent request record footprint.
- **Continuous Creation Flow**: Modified the routing logic inside`RequestCreate.tsx`. Instead of redirecting to the requests list after Draft creation, the UI automatically transitions`navigate('/requests/{id}/edit')`, exposing the Line Items section immediately.

### Changed

- **List Endpoints Default Behavior**:`LookupsController` GET mappings natively filter out inactive records. To see all records (like inside the Master Data settings page), the UI now passes `?includeInactive=true`.
- **Delete Behavior**: Eliminated all physical `DELETE` statements from Master Data flows, replacing them entirely with a `toggle-active` payload logic.

### Fixed

- **Soft Delete Mapping Constraint**: Ensured legacy transactions bound to presently inactive enumerations continue rendering properly because the foreign key remains structurally intact.
- **Concurrent DB Exceptions on Line Items (Frontend)**: Fixed a nested DOM`<form>` overlap in `RequestEdit.tsx`that caused simultaneous save submissions, triggering EF Core errors.
- **UnitId Integer Mapping**: Corrected the logic in the Line Item modal to correctly map String Codes ("EA") back to Integer lookups (`Units.Id`) before pushing to the API.
- **DbUpdateConcurrencyException (Backend)**: Fixed an EF Core tracking issue in`RequestsController.AddLineItem` where ostensibly new Line Items with pre-assigned Guids were erroneously dispatched as `UPDATE` statements instead of `INSERT`statements, causing 0 rows affected exceptions.

---

## [0.1.0] - 2026-02-25

### Added

- Initial project documentation scaffold in`docs/`
- `PROJECT_OVERVIEW.md`
- `VERSION.md`
-`CHANGELOG.md`

### Changed

- N/A

### Fixed

- N/A

### Notes

- Initial baseline version for project documentation structure
