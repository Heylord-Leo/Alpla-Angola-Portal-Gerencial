# Version

## Current Version

v2.203.0

## [v2.203.0] - 2026-06-30

### Added — IT Module UI Alignment & Settings Migration (Phase 3D)

- **UI Realignment**: Synchronized the Catalog (`/it/catalogs`) and Equipment Type (`/it/types`) settings screens with the modern portal standards (`WizardLayout`, `StandardTable`, `KebabMenu`, `SearchFilterBar`). Replaced the legacy modal-based management.
- **Guided Tours**: Created dedicated page-level guided tours for the new IT Catalogs and IT Types screens. Added tab anchor hooks (`data-tour="it-module-tabs"`) to `ITLandingPage.tsx` to properly guide users across the module.
- **Bug Fixes**: Resolved dead code issues in `ModelWizardPage.tsx` and ensured strict TypeScript compilation (`tsc --noEmit`).

## [v2.202.0] - 2026-06-26

### Added — IT Equipment Purchase Traceability

- **Purchase Information**: Added support for tracking purchase value, date, and document reference (invoice number) for IT Equipment.
- **Unavailable State Handling**: Implemented explicit tracking of missing/legacy purchase data using `PurchaseInfoUnavailable` and `PurchaseInfoUnavailableReason` fields to maintain data integrity without blocking migrations.
- **PDF Responsibilities Term Update**: Restructured the "Termo de Responsabilidade" PDF table to support 10 columns using a compact 6.5pt font layout. The PDF now accurately displays equipment values, purchase dates, and purchase document numbers, explicitly rendering "Indisponível" for legacy records without values.
- **Form UI Update**: Added an always-visible "Compra / Rastreabilidade" section to the Equipment Form modal with validation for mandatory purchase fields or justified absence.

**Guided Tour impact: existing tour reviewed, no changes needed.**


- **HR Sync Logging & Robustness**: Refined the HR Employee Directory Synchronization to handle SQL connection timeouts gracefully without crashing the frontend. Added `EXTERNAL_DB_TIMEOUT` structured backend error. 
- **Shared Correlation ID**: The frontend now generates a shared `X-Correlation-ID` for the full synchronization operation (departments + employees), logged consistently via `AdminLogWriter` across events (`HR_SYNC_STARTED`, `HR_SYNC_SUCCESS`, `HR_SYNC_PARTIAL`, `HR_SYNC_FAILED`).
- **Partial Sync Handling**: Replaced hard crash on unprocessable records with skip-and-continue logic. Emits `HR_SYNC_PARTIAL` when some records are skipped, displaying the count in the UI.

## [v2.201.0] - 2026-06-25

- **Email Outbox + Background Processor**: Async email delivery via outbox pattern. Request creation no longer blocks on SMTP. Atomic `UPDATE...OUTPUT` claiming, crash recovery, 3-layer deduplication, exponential retry, dead-letter, AdminLog audit trail.
- **Badge Reprint Blank Fix**: Resolved CSS `visibility: hidden` conflict causing blank badge output during reprint. Aligned with existing `.hr-badge-print-area` print pattern.
- **Editable Card Number on Reprint**: Required "Número do Cartão" field in reprint modal with live preview. Per-event `CardNumberUsed` storage without modifying original history. `BADGE_REPRINT` AdminLog audit entry.

## [v2.200.0] - 2026-06-25

### Added — Buy2Pay Foundation & Purchasing Workflow Enhancements

- **Buy2Pay (B2P) Core**: Introduced reconciliation UI, payment tracking logic, and DB models (`RequestPayment`, `RequestReconciliation`).
- **OCR Module Configuration**: Migrated OCR whitelist from hardcoded appsettings to a secure-by-default DB table (`OcrModuleConfig`), including Admin API and Settings UI.
- **Buyer P.O. Creation Email**: Added a dedicated, idempotent email workflow for Buyers when a request enters the final approved stage, providing full operational data for PRIMAVERA P.O. creation.
- **P.O. Payment Condition Control**: Removed silent POST_PAID default; enforced explicit Buyer selection with OCR auto-detection. Persists detection source (`PaymentConditionSource`) for auditability.
- **Duplicate Document UX Safety**: Added a 5-second countdown safety delay to the confirmation button on duplicate document warning modals to prevent instinctive overrides.

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.199.0] - 2026-06-23

### Added — Accounts Payable Email Notification System

- **AP Notification Configuration**: Dedicated `AccountsPayableNotificationConfigs` table and Master Data UI panel for per-company AP email settings.
- **AP Notification Logging**: `AccountsPayableNotificationLogs` table with duplicate prevention.
- **Workflow Integration**: `PAYMENT_SCHEDULED` and `PAYMENT_COMPLETED` trigger AP emails with `CompanyId` routing.
- **CC Support**: Real CC handling via email service.
- **Non-Blocking Failures**: Email failures logged, do not block payment workflow.
- **Environment Policy**: `ApplyEnvironmentPolicy` clears both TO and CC in non-prod.

**Guided Tour impact: not applicable.**

## [v2.198.0] - 2026-06-22

### Added — User Onboarding Email Flow

- **Secure Password Setup**: Added an onboarding email flow for newly created users containing a secure token link instead of transmitting plain-text passwords.
- **Branding Correction**: Corrected email footer text to "ALPLA Angola".
- **UI Notifications**: Replaced browser `alert()` popups with consistent custom `toast` notifications in User Management.

**Guided Tour impact: not applicable.**

## [v2.197.0] - 2026-06-22

### Added — Request Field-Level Audit Trail & Edit Permissions

- **Field-Level Audit Trail**: Added automatic tracking of individual field changes during Request modifications. A new `Histórico do Pedido` section now displays old vs. new values for tracked fields (e.g., Description, Department, Need Level, Dates).
- **Edit Permissions Enforcement**: Restricted Request modification (Editing) strictly to the original Requester (Creator). Other roles can no longer edit a request, except for specific workflow transitions.

### Fixed — IVA Partial Save Bug & Global Layout Overlapping

- **IVA Partial Save Bug**: Fixed an issue where IVA percentages were not persisting correctly during partial request saves by ensuring backend entity propagation.
- **Global Layout Constraints**: Addressed overlapping UI elements globally.
  - Stabilized `--header-height` to `64px` and removed destructive `overflow: hidden` on Topbar to fix dropdown clipping.
  - Introduced `--env-banner-offset` to smartly adapt sticky headers across environments (TEST vs PROD), correcting sticky positioning on `RequestActionHeader`, `ApprovalCenter`, `BuyerItemsList`, `OperationsTransfersPage`, `MasterData`, and `CatalogItemsPanel`.

## [v2.196.0] - 2026-06-18

### Added — AI OCR Technical Hardening & Compliance Package

**Security & Compliance Hardening:**
- **Debug Logging Guard (G1):** Implemented dual-guard `IsDebugLoggingAllowed()` requiring both `IsDevelopment()` and explicit `DebugRawPayloadLogging` flag to prevent raw AI payload leakage to disk.
- **Policy Controls (G2):** Enforced module (`CONTRACTS`, `REQUESTS`) and document type allowlists to restrict AI extraction to authorized contexts.
- **Prompt Injection Defense (G3):** Injected security preamble to both invoice and contract system prompts to mitigate instruction overrides.
- **Retention Controls (G4):** Created `OcrCleanupService.cs` background service for managing data retention.
- **Malware Scanning (G5):** Added `IFileScanService` extension point and `NoOpFileScanService` placeholder.
- **Provider Readiness (G6):** Made `OpenAiDocumentExtractionProvider` endpoint configurable to support switching to Azure Document Intelligence.
- **System Logs Integration (G8):** Integrated 8 `OCR_*` events (`OCR_EXTRACTION_STARTED`, `OCR_MODULE_BLOCKED`, etc.) into the structured `AdminLogWriter` with `SafePayload` sanitization.

**Compliance Documentation & Evidence:**
- Updated 8 core compliance documents (v2.0) reflecting the post-hardening state.
- Generated a comprehensive 48-file evidence package under `docs/ai-ocr/evidence/` including redacted configurations, sanitized log samples, SQL verification queries, and build results.

**Guided Tour impact: not applicable.**

## [v2.195.2] - 2026-06-17

### Fixed — Global Frontend Responsive Audit & Layout Constraints

**Problem:** 
The Portal Gerencial frontend had structural layout issues that caused horizontal clipping on standard laptop resolutions (e.g. 1366x768 and 1440x900) at 100% browser zoom. Some pages expanded beyond the viewport, forcing users to manually zoom out, especially when the sidebar was expanded.

**Fixes Applied:**
- **Global Constraints:** Added `overflow-x: hidden` to the HTML tag and set global `max-width: 100%` rules for tables. Added media queries to automatically collapse the sidebar at viewport widths ≤1366px.
- **AppShell & PageContainer:** Added `overflowX: 'hidden'`, `maxWidth: '100%'`, and `minWidth: 0` to main content containers (`<motion.main>`, `PageContainer`) to prevent child elements from forcing a layout blowout.
- **Topbar & Header:** Refactored Topbar layout from fixed widths to flexible `min-width` and `flex-shrink` to prevent overlapping or clipping.
- **Specific Pages:** Modified `RequestsDashboard.tsx` root `div` to include `width: '100%'` and `minWidth: 0`, fixing a specific clipping bug where the "Tour da Tela" and action buttons were pushed off-screen at 1440x900 with an expanded sidebar.
- **Grid Auto-fit:** Adjusted CSS Grid columns in Dashboard, Finance, Settings, and Purchasing pages to use `repeat(auto-fit, minmax(...))` instead of fixed fractional tracks, ensuring cards wrap safely on smaller displays.

**Guided Tour impact: not applicable.**

## [v2.195.1] - 2026-06-17

### Added — IT Equipment Return Term Generation

**Auto-Generate Return Term:**
- When the last item of a Delivery Term is returned (status changes to `CLOSED`), the system automatically generates a branded Return Term PDF.
- The return document is linked to the original Delivery Term via a new `ReturnDocumentId` field.
- The PDF contains an electronic generation statement and an empty signature area for the user.
- An email is automatically dispatched to the IT Department with the Return Term PDF attached.

**Signed Return Document Upload:**
- Added the ability to upload a manually signed Return Document.
- Upload is available directly from the Delivery Terms page and the Equipment Quick-View drawer.
- Shows visual indicators for generated (blue) vs. signed (green) return documents.

**Quick-View Drawer UX:**
- Fixed a z-index issue that caused the drawer to appear behind the top header and TEST environment banner.
- Removed the direct "Atribuir" and "Devolver" buttons to enforce the Delivery Terms workflow.

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.194.0] - 2026-06-15

### Added — IT Equipment Refinements (Manufacture Date & MAC Split)

**Refinements:**
- Changed Laptop ShortCode from `NBK` to `LAP` for Asset Code generation.
- Added `ManufactureDate` field for lifecycle tracking.
- Split `MacAddress` field into `MacAddress` (Ethernet) and `WifiMacAddress` (Wi-Fi).
- Migration `20260615142951_ITEquipmentRefinements` applied.

## [v2.193.0] - 2026-06-15

### Added — IT Asset Code Auto-Generation, QR Code & Label Printing

**Automatic Asset Code Generation:**
- New `ITAssetCodeGeneratorService` generates unique asset codes on equipment creation using format: `{COMPANY_CODE}-{PLANT_CODE}-IT-{TYPE_SHORT_CODE}-{SEQUENCE:D6}` (e.g., `APA-AOVIA1-IT-NBK-000001`)
- Sequence counters are scoped per Company + Plant + Equipment Type (stored in `SystemCounters` table)
- Added `ShortCode` field to `ITEquipmentType` for compact asset code segments
- Added `CompanyCode` field to `Organization` for company identification
- Added `LegacyAssetCode` field to `ITEquipment` for manual/old patrimony codes
- `AssetTag` field repurposed as the official auto-generated Asset Code (read-only in frontend)
- Migration: `20260615104001_AddITAssetCodeAutoGeneration`

**QR Code & Deep Link Support:**
- Backend generates `QrCodeUrl` for each asset: `{FrontendBaseUrl}/it/equipment/{equipmentId}`
- Frontend route `/it/equipment/:id` auto-opens the equipment detail drawer
- Visual QR Code rendered in the equipment detail drawer using `qrcode.react` (QRCodeSVG)
- Action buttons: Open Equipment Page, Print Label, Copy Link
- Relative URL warning badge when `FrontendBaseUrl` is not configured

**Printable Asset Label:**
- Route `/it/equipment/:id/label` renders a 70mm×35mm printable label
- Layout: QR Code (left) + asset info (right): ALPLA ANGOLA, Asset Code, Type, S/N, Model, Plant, Company
- `@media print` CSS: hides app chrome, sets `@page` size to 70×35mm
- Includes `LegacyAssetCode` on label when present

**Authentication Flow — Return URL Preservation:**
- `ProtectedRoute` now captures the current URL before redirecting to login
- After successful authentication, the user is redirected back to the original URL
- Safety: only internal relative paths accepted as return URLs

**404 Not Found Page:**
- New `NotFoundPage` component for unmatched routes
- Catch-all `*` route in App.tsx

**Config Consolidation — `PortalBaseUrl` → `FrontendBaseUrl`:**
- Eliminated the separate `AppConfig:PortalBaseUrl` config key
- All services (QR Code generation, email CTA buttons, notification links) now use the existing `AppConfig:FrontendBaseUrl` key
- No additional config file changes needed on TEST or PROD servers

**Operational Script:**
- `scripts/maintenance/ResetITEquipmentData.sql` — controlled purge of IT asset operational data, preserving all master data/catalogs

**Guided Tour impact: existing tour reviewed, no changes needed.**

**Files Created:**
- `src/backend/.../Migrations/20260615104001_AddITAssetCodeAutoGeneration.cs` — Schema migration
- `src/backend/.../Migrations/20260615104001_AddITAssetCodeAutoGeneration.Designer.cs` — Snapshot
- `src/backend/.../Services/ITAssetCodeGeneratorService.cs` — Asset code generator
- `src/backend/scripts/maintenance/ResetITEquipmentData.sql` — Data reset script
- `src/frontend/src/pages/IT/ITEquipmentLabelPage.tsx` — Printable label page
- `src/frontend/src/pages/NotFoundPage.tsx` — 404 page

**Files Modified:**
- `src/backend/.../Controllers/ITEquipmentController.cs` — Calls asset code generator on create
- `src/backend/.../Controllers/ITDeliveryTermsController.cs` — Uses new field names
- `src/backend/.../Program.cs` — DI registration for asset code service
- `src/backend/.../Entities/ITEquipment.cs` — LegacyAssetCode property
- `src/backend/.../Entities/ITEquipmentType.cs` — ShortCode property
- `src/backend/.../Entities/Organization.cs` — CompanyCode property
- `src/backend/.../Data/ApplicationDbContext.cs` — Updated model config
- `src/backend/.../Services/WorkflowNotificationOrchestrator.cs` — PortalBaseUrl → FrontendBaseUrl
- `src/frontend/src/App.tsx` — New routes + ProtectedRoute returnUrl logic
- `src/frontend/src/features/auth/AuthContext.tsx` — Login redirect to return URL
- `src/frontend/src/components/it/EquipmentQuickViewDrawer.tsx` — QR code + action buttons
- `src/frontend/src/components/it/EquipmentFormModal.tsx` — AssetTag read-only, LegacyAssetCode field
- `src/frontend/src/components/it/EquipmentTable.tsx` — Display changes
- `src/frontend/src/pages/IT/ITEquipmentPage.tsx` — Equipment ID from route param
- `src/frontend/src/pages/IT/DeliveryTermsPage.tsx` — Updated field references
- `src/frontend/src/lib/itEquipmentApi.ts` — New API fields
- `src/frontend/src/types/itEquipment.ts` — New type fields
- `src/frontend/package.json` — Added qrcode.react dependency
- `src/frontend/src/config.ts` — APP_VERSION → v2.193.0
- `docs/VERSION.md` — v2.193.0
- `docs/CHANGELOG.md` — This entry

## [v2.192.0] - 2026-06-12

### Added — IT Equipment Module Improvements (Phase 1, 2, and 3)

- **Phase 1: IT Equipment Lifecycle Improvements**: Added dynamic equipment types with prefixes, implemented reversible retirement flow, and added a detailed audit timeline for equipment items.
- **Phase 2: Delivery Terms (Termos de Entrega)**: Created a new entity `ITEquipmentDeliveryTerm` to group multiple IT equipment assignments for a single employee into a single, signable PDF document.
- **Phase 3: Master Data and Catalogs**: Replaced free-text equipment fields (Manufacturer, Model, Processor, Memory) with admin-managed catalogs. Connected Delivery Terms to Master Data for Company, Plant, and Department, with cascading UI dropdowns. Implemented denormalized save strategy for backward compatibility.
- **Guided Tours**: Added a new guided tour for the IT Equipment module.

## [v2.191.1] - 2026-06-12

### Fixed — Quotation Submission Notification Emission

**Problem:** The `SUBMISSION_CONFIRMED` email was not being sent for Quotation requests because Quotation requests skip DRAFT and are created directly in `WAITING_QUOTATION`, bypassing the notification emission path.

**Fix:** Added notification emission directly in the `CreateRequestDraft` endpoint for Quotation requests.

### Improved — Submission Confirmation Email Content

- Email body now includes Request Title and Description with fallbacks.
- Applies to all request types (shared template).

### Improved — Buyer Queue Email CTA Button

- Added explicit "Abrir Pedido no Portal →" CTA button using `AppConfig:PortalBaseUrl`.

## [v2.191.0] - 2026-06-12

### Added — Quotation Email Notifications

Implemented three new email notification capabilities for the Quotation workflow:
- **Submission Confirmation**: Requesters now receive a "Confirmação de Submissão" email when a Quotation request is submitted (DRAFT → WAITING_QUOTATION).
- **Buyer Queue Alert**: Plant-scoped buyers now receive a `[AÇÃO NECESSÁRIA]` email when a new quotation request enters the queue, containing a rich summary of the request (Requester, Plant, Department, Value, Need-by date).
- **Assignment Confirmation**: When a buyer takes ownership of a quotation, the system now automatically emails both the buyer (confirming assignment) and the requester (informing them who their buyer is).

### Technical Changes
- Added two new constants in `WorkflowEventCodes.cs`: `QuotationAwaitingBuyer` and `BuyerAssigned`.
- Mapped `("SUBMIT", "WAITING_QUOTATION")` in `ResolveEventCode` to fix the previously silent transition.
- Implemented `AddPlantScopedBuyerRecipientsAsync` in `WorkflowNotificationOrchestrator` to replicate the safe plant-scoped routing used for Finance.
- Fired `_orchestrator.EmitAsync` natively inside the `/assign-buyer` endpoint.

## [v2.190.1] - 2026-06-11

### Fixed — Idempotent Migrations: Schema/History Desync Safe Handling

- Rewrote 3 recent migrations to use `IF NOT EXISTS` raw SQL guards
- Prevents `Column already exists` errors when migration history is out of sync with physical schema
- Safe for all environments: Development, TEST, PROD

## [v2.190.0] - 2026-06-11

### Added — Email Environment Identification

- Global email environment warning system (subject prefix, body banner, recipient redirect)
- Auto-applied in TEST/DEV environments; admin-configurable overrides
- New "Identificação de Ambiente de E-mail" section in Admin → Integrações → SMTP
- Migration `20260611114811_AddEmailEnvironmentIdentification` — 8 new columns on `SmtpSettings`

## [v2.189.5] - 2026-06-10

### Fixed — Quotation Save HTTP 500: Missing `ItemCatalogId` Column

- Added migration `20260610134920_AddItemCatalogToQuotationItems` to fix snapshot-vs-database desync
- Adds `ItemCatalogId` (nullable int FK) to `QuotationItems` table
- Resolves `SqlException: Invalid column name 'ItemCatalogId'` on quotation save

## [v2.189.4] - 2026-06-10

### Fixed — Migration Application: QUOTED_IDENTIFIER Session Option

Updated `scripts/db/apply-migrations.ps1` to fix Msg 1934 (`QUOTED_IDENTIFIER OFF`) when applying EF Core migrations via `sqlcmd`.

**Root cause:** `sqlcmd` defaults to `QUOTED_IDENTIFIER OFF`. Tables with filtered indexes, indexed views, or computed columns require `QUOTED_IDENTIFIER ON` for any DDL or DML operations.

**Changes:**
- All `sqlcmd` invocations now use the `-I` flag (`QUOTED_IDENTIFIER ON` at session level)
- Injected SET options header now also strips any `SET QUOTED_IDENTIFIER OFF` from EF-generated SQL
- Added preflight check: `SESSIONPROPERTY('QUOTED_IDENTIFIER')` must return `1` before proceeding
- Added diagnostic logging: first 20 lines of SQL, sqlcmd mode, QUOTED_IDENTIFIER scan results

**Files Changed:**
- `scripts/db/apply-migrations.ps1` — Robust sqlcmd session options

## [v2.189.3] - 2026-06-10

### Fixed — Missing Supplier Columns: Origin, SourceCompany, LastSyncedAtUtc

Created EF Core migration `20260610083347_AddSupplierSyncColumns` to add 3 columns to the `Suppliers` table that existed in the entity model and DbContext snapshot but were never added by any migration to existing databases.

**Root cause:** The `AddSupplierRegistrationFields` migration (20260425) was scaffolded after these entity properties were added, so the `Designer.cs` snapshot included them. However, the generated `Up()` method did not include `AddColumn` calls for `Origin`, `SourceCompany`, and `LastSyncedAtUtc`. The v2.156.3 fix corrected the `ConsolidatedBaseline` for clean installs but explicitly stated "No New Migration Required" — which left the TEST database missing these columns.

**Columns added:**
- `Origin` (nvarchar(max), NOT NULL, default `'MANUAL'`) — record origin traceability
- `SourceCompany` (nvarchar(max), nullable) — source Primavera company
- `LastSyncedAtUtc` (datetime2, nullable) — last synchronization timestamp

**SQL applied to Portal-Gerencial-Test:**
```sql
ALTER TABLE [Suppliers] ADD [Origin] nvarchar(max) NOT NULL DEFAULT N'MANUAL';
ALTER TABLE [Suppliers] ADD [SourceCompany] nvarchar(max) NULL;
ALTER TABLE [Suppliers] ADD [LastSyncedAtUtc] datetime2 NULL;
```

**Files Changed:**
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260610083347_AddSupplierSyncColumns.cs` — [NEW] Migration
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260610083347_AddSupplierSyncColumns.Designer.cs` — [NEW] Snapshot
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.3"
- `docs/VERSION.md` — v2.189.3
- `docs/CHANGELOG.md` — This entry

## [v2.189.2] - 2026-06-10


### Fixed — Supplier Creation: Unhandled Exception Safety Net

Added an outer try-catch around the entire `CreateSupplier` method body to prevent unhandled exceptions (from EF Core queries, `GetNextPortalCodeAsync`, or other non-`DbUpdateException` errors) from reaching the global `UseExceptionHandler` middleware and producing the generic "An error occurred while processing your request." message. The outer catch logs the actual exception type, message, and inner message, then returns a specific ProblemDetails response with the real error detail so the frontend can display meaningful diagnostic information instead of a generic error.

**Root Cause Analysis:** The v2.189.1 fix correctly handled `DbUpdateException` inside the save retry loop but left the Name/NIF pre-check queries, `GetNextPortalCodeAsync()`, and entity construction outside any catch block. Any non-`DbUpdateException` thrown by these operations (e.g., EF Core model mismatch, SQL connection timeout, transaction failure) would escape to the ASP.NET Core `UseExceptionHandler()` middleware, which returns a sanitized generic 500 ProblemDetails with no actionable detail.

**Files Changed:**
- `src/backend/AlplaPortal.Api/Controllers/LookupsController.cs` — Outer try-catch in `CreateSupplier`
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.2"
- `docs/VERSION.md` — v2.189.2
- `docs/CHANGELOG.md` — This entry

## [v2.189.1] - 2026-06-09


### Fixed — Supplier Creation 500 Error (Duplicate Name)

Fixed an unhandled `DbUpdateException` when creating a supplier with a name that already exists in the database. The `IX_Suppliers_Name` unique constraint violation was not caught by the `CreateSupplier` endpoint, causing a generic 500 error. Added Name uniqueness pre-check (case-insensitive) returning 409 Conflict with a clear ProblemDetails message. Improved `DbUpdateException` handling to detect `IX_Suppliers_Name`, TaxId, and `IX_Suppliers_PortalCode` constraint violations separately. Frontend `QuickSupplierModal` now handles 409 Conflict for both Name and NIF duplicates with field-level warning feedback.

**Files Changed:**
- `src/backend/AlplaPortal.Api/Controllers/LookupsController.cs` — Name uniqueness pre-check + improved DbUpdateException handling
- `src/frontend/src/components/Buyer/QuickSupplierModal.tsx` — 409 conflict handling for Name and NIF duplicates
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.1"
- `docs/VERSION.md` — v2.189.1
- `docs/CHANGELOG.md` — This entry

## [v2.189.0] - 2026-06-08

### Added — I.T Equipment Assignment: Availability Date & Visual Signature PDF

Exposed the `AssignedDate` field as "Data de disponibilização ao utilizador" in the assignment modal (required, defaults to today, user-changeable for historical assignments). No database migration — uses existing `ITEquipmentAssignment.AssignedDate` column.

**Assignment Agreement PDF enhancements:**
- Two separate date lines: `Data de disponibilização ao utilizador: DD/MM/YYYY` and `Data do documento: DD/MM/YYYY HH:mm UTC`
- Availability date added to the initial information table
- Visual cursive signature generated as transparent PNG using `System.Drawing.Common` (GDI+) with Segoe Script font
- Enhanced signature block: cursive PNG → signature line → printed name → role label
- Electronic generation statement with audit metadata (user, email, asset tag, responsible, timestamp)
- Statement uses "Documento gerado eletronicamente" (not "aceite") since no real user acceptance action exists yet

**Return Document PDF enhancements:**
- Same enhanced signature blocks and electronic generation statement applied for consistency

**Signature font fallback chain:** Segoe Script → Lucida Handwriting → Freestyle Script → Arial Italic (last resort)

**No new dependencies:** Uses existing `System.Drawing.Common` v10.0.5 reference in the Infrastructure project.

**Files Changed:**
- `src/frontend/src/components/it/AssignEquipmentModal.tsx` — Added availability date input field
- `src/backend/AlplaPortal.Infrastructure/Services/ITEquipmentPdfService.cs` — Signature PNG generation, enhanced blocks, date labels, electronic statement
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.0"
- `docs/VERSION.md` — v2.189.0
- `docs/CHANGELOG.md` — This entry

## [v2.188.0] - 2026-06-08

### Added — Supplier Ficha Primavera Import Enrichment

Extended the Primavera supplier import to automatically populate Address, Primary Contact, Banking, and Payment Terms in the Supplier Ficha, using data from the Primavera `Fornecedores` table.

**New Primavera fields mapped:**
- **Address**: Composite from `Morada`, `Morada1`, `Local`, `Cp`, `Pais` (joined with ", ")
- **Primary Contact**: `Tel` → ContactPhone1, `Email` → ContactEmail1
- **Banking**: `IBAN`, `Swift`, `NumCB` → BankIban, BankSwift, BankAccountNumber
- **Payment Terms**: `CondPag` → PaymentTerms, `ModoPag` → PaymentMethod

**Not available from Primavera** (remain empty, manually editable):
- Contact person Name and Role (ContactName1/2, ContactRole1/2)
- Secondary contact (ContactName2, ContactPhone2, ContactEmail2)

**Safe update rule**: When re-importing an existing supplier in DRAFT or PENDING_COMPLETION status, only empty Portal fields are filled from Primavera. Manually entered values are never overwritten. Suppliers in ACTIVE, PENDING_APPROVAL, or ADJUSTMENT_REQUESTED status are not modified.

**Safe column detection**: Uses a two-query approach — attempts extended columns (banking/payment) first. If any column is missing in the Primavera installation, falls back to the base column set automatically.

**Diagnostic logging**: Each supplier import/update logs a structured line showing which data groups were found/missing.

**Files Changed:**
- `PrimaveraSupplierDto.cs` — +5 properties (IBAN, Swift, BankAccountNumber, PaymentTerms, PaymentMethod)
- `PrimaveraSupplierService.cs` — Extended SQL columns + safe fallback + SafeReadString helper
- `SyncController.cs` — Import enrichment + safe update + BuildCompositeAddress + LogSupplierDiagnostic
- `docs/VERSION.md` — v2.188.0
- `docs/CHANGELOG.md` — This entry
- `docs/DECISIONS.md` — DEC-141: Safe Update Rule
- `src/frontend/src/config.ts` — APP_VERSION → "2.188.0"

## [v2.187.0] - 2026-06-05

### Security — HR Attendance API Access-Control Hardening (DEC-140)
- **Removed anonymous test endpoint**: Deleted `[AllowAnonymous]` diagnostic endpoint `GET /api/hr/attendance/test-verify/{innuxEmployeeId}/{date}` that exposed attendance data without authentication and leaked stack traces. This endpoint was a development artifact that should not have reached production.
- **Added HR module entitlement checks**: All production attendance endpoints (`GetCalendar`, `GetDayDetail`, `GetAbsenceCodes`, `GetWorkCodes`) now require `HasHRModuleAccess()` before execution. Previously, any authenticated user could call these endpoints (data was scoped, but the entitlement gate was missing). The entitlement check mirrors `HRLeaveController.HasHRModuleAccess()` exactly: System Administrator, HR, Local Manager, Department Manager, or self-calendar (email-matched HREmployee).
- **No sidebar changes**: "Gestão da Equipa" visibility for Viewer / Management users remains unchanged. This is by design and will be evaluated separately.

## [v2.186.1] - 2026-06-05

### Fixed
- **TEST Environment Banner Configuration (DEC-140)**: Created `scripts/server/configure-test-environment-banner.ps1` to set `AppEnvironment__*` IIS App Pool environment variables on the TEST server. Updated `GITHUB_ACTIONS_TEST_DEPLOYMENT.md` with the required variables and `appsettings.Test.json` template. No frontend changes.

## [v2.186.0] - 2026-06-05

### Added
- **Automatic Visual Environment Differentiation (DEC-140)**: Backend-driven environment detection with frontend visual indicators. TEST environment shows a fixed amber banner, sidebar badge, and browser title prefix. PROD remains visually clean. Single codebase, no separate builds. Layout offset via CSS variable `--env-banner-height`.

## [v2.185.10] - 2026-06-04

### Changed
- **Pending Approvals Notification**: Reverted sidebar-based approval highlighting in favor of a non-intrusive floating sticker (`PendingApprovalsSticker.tsx`).
- **Feedback Notification Style**: Adopted the bottom-right portal notification pattern to alert users of pending approvals. Includes sessionStorage persistence so the sticker remains hidden after manual dismissal.

## [v2.185.9] - 2026-06-04

### Changed
- **Preventive EF Core Migration Handling (DEC-137)**: Disabled automatic `Database.Migrate()` in non-Development environments. The API now detects pending migrations and crashes with a descriptive diagnostic listing each missing migration ID, instead of attempting DDL operations that fail with 500.30 under restricted IIS identity. GitHub Actions workflows now check for pending migrations before starting App Pools. New reusable migration comparison script: `scripts/db/check-pending-migrations.ps1`.

## [v2.185.8] - 2026-06-03

### Fixed
- **Supplier PortalCode D6 Standardization**: Fixed `IX_Suppliers_PortalCode` duplicate key collision when creating suppliers from the OCR/proforma flow. Root cause: `SyncController` generated D4 codes (`SUP-0003`) while `LookupsController` generated D6 codes (`SUP-000003`), and the self-healing parser required `Length == 10` which silently ignored D4 codes. Changes: (1) flexible parser in `GetNextPortalCodeAsync` handles any `SUP-XXXX` format, (2) `CreateSupplier` retries up to 3 times on PortalCode collision with sanitized error messages, (3) both `SupplierImport` and `SupplierImportReviewed` now use D6 format, (4) `SystemCounters` aligned after batch imports. See DEC-136.

## [v2.185.7] - 2026-06-03

### Fixed
- **Production Email Config Script — Schema Correction**: Regenerated `scripts/db/configure-production-email.sql` with validated AOVIA1VMS011 schema (correct table names, columns, FKs, schema guards).

## [v2.185.6] - 2026-06-03

### Added
- **Production Email Configuration Script**: Created `scripts/db/configure-production-email.sql` for safe SMTP settings migration from Test to Production.
- **Deployment Checklist Update**: Added email/SMTP configuration documentation to `DEPLOYMENT_CHECKLIST.md`.

## [v2.185.5] - 2026-06-03

### Fixed
- **SQL Express Backup Compatibility**: The `deploy-prod.yml` database backup step now detects SQL Server Edition at runtime via `SERVERPROPERTY('Edition')`. If Express Edition is detected, the backup runs without `COMPRESSION` (which Express does not support). Other editions continue to use `COMPRESSION` for faster, smaller backups.
- **Connection String Diagnostics**: Added pre-parse validation for `PROD_DB_CONNECTION_STRING` to detect common issues (newlines, leading/trailing whitespace, BOM characters) before attempting `SqlConnection`. When the error "Format of the initialization string does not conform to specification" occurs, the workflow now prints actionable guidance for correcting the GitHub secret.

## [v2.185.4] - 2026-06-03

### Fixed
- **Cascading IDE Lexer Errors**: Completely eliminated all VS Code PowerShell extension lexer false positives in `setup-production-environment.ps1` by rewriting log functions to pre-compute formatted strings in variables, replacing all `-f` operator calls, and switching the XML here-string from double quotes to single quotes.

## [v2.185.3] - 2026-06-02

### Fixed
- **IDE PowerShell Extension Lexer Error**: Refactored string interpolation in `setup-production-environment.ps1` to use the `-f` format operator.

## [v2.185.2] - 2026-06-02

### Fixed
- **PowerShell -WhatIf Failure**: Fixed an issue where `setup-production-environment.ps1 -WhatIf` failed during simulation mode because `Set-ItemProperty` and `New-WebBinding` were executing against App Pools and IIS Sites that were simulated but not actually created. Added `Test-Path` safety checks to skip configuration when resources are absent due to simulation.

## [v2.185.1] - 2026-06-02

### Fixed
- **PowerShell Parser Error**: Replaced UTF-8 em-dash characters with hyphens and enforced UTF-8 BOM encoding in server scripts.

## [v2.185.0] - 2026-06-02


### Added
- **Production Deployment Automation**: Complete CI/CD infrastructure for Production environment — workflow, bootstrap, validation, deployment guide, rollback procedures, and post-deployment checklist.

### Fixed
- **PowerShell `$pid` Collision**: Renamed `$pid` to `$procId` in server scripts.

## [v2.184.2] - 2026-06-02

### Fixed
- **Innux Integration Testing**: Replaced direct configuration reads with database-first cascade resolution via `IntegrationConfigResolver`, resolving false-negative "connection settings are incomplete" validation errors.

## [v2.184.1] - 2026-06-02

### Fixed
- **AlplaPROD Integration Testing**: Restored database configuration cascade priority, fixing false 'disabled' validation errors and synchronizing the factory logic.

## [2.184.0] - 2026-06-02

### Added — Integration: AlplaPROD 1.0 Multi-Plant Configuration

- Activated ALPLAPROD provider via EF migration (was previously planned/future-blocked).
- Added per-plant connection configuration (VIANA1, VIANA2, VIANA3) with independent server, database, username, password, and test connectivity.
- Added `TestPlantConnectionAsync` for per-plant connection testing via admin UI.
- Full UI for per-plant cards with configure, replace password, and test connection actions.

## [2.183.0] - 2026-06-02

### Fixed — Operations: Public Route

- Moved `OperationsLiveBoardPage` outside of `ProtectedRoute` and `AppShell`.
- This allows the Live Board to be displayed as a standalone page without requiring user login, while still preserving standard `apiFetch` behavior.

## [2.182.0] - 2026-06-02

### Changed — Operations: Anonymous Access for Live Board

**Scope:** Backend and Frontend access control for Operations Live Board.

- Made the Live Board route (`/operations/live-board/:plant`) accessible without login for TV/kiosk display usage.
- Added `[AllowAnonymous]` to the `GetLiveBoard` API endpoint while keeping all other Operations endpoints protected.
- Verified that no sensitive data (financials, usernames) is exposed in the Live Board DTOs.

## [2.181.0] - 2026-06-02

### Fixed — Operations: RBAC in User Management

**Scope:** Frontend User Management.

- Fixed an issue where the new `OPERATIONS` role was not visible in the User Management assignment screen due to a missing translation mapping in `roles.ts`.
- The role is now displayed correctly as `Operações`.

## [2.180.0] - 2026-06-02

### Added — Operations: RBAC for Live Board

**Scope:** Backend and Frontend access control for Operations Live Board.

- Added role-based access control for `/operations/live-board/:plant`
- Only users with `Operations` or `System Administrator` roles can access the TV Signage page.
- Added specific exception for public kiosk displays (to be implemented via secure token in next phase).

## [2.161.0] - 2026-05-29

### Added — Quotation Management Live Guide (v1.0.0)

New **Live Guide** for the Buyer's quotation management workspace (`/buyer/items`). Provides interactive, step-by-step guidance through the quotation management workflow — from page overview to adding quotations and completing the process.

**Guide Architecture:**
- Factory function pattern via `createQuotationManagementGuide(getState)` — receives a state getter to evaluate conditional steps without stale-closure risks.
- 11 assistive steps (all `requiredAction: 'none'`) covering: Introduction, Header, Search/Filters, Request Card, Expand Button, Assignment, Summary, Items, Documents/Quotations, Add Quotation (OCR/Manual), Complete Quotation.
- Conditional steps auto-skip when the target is not rendered (empty list, card not expanded, not assigned, etc.).
- Empty state handling: the intro step warns when no request groups are visible, and card-level steps are safely skipped.
- Assignment step adapts content dynamically: unassigned → explains "Atribuir a Mim"; assigned to current user → confirms ownership; assigned to other → explains limited actions.
- Rich JSX content for complex steps (Add Quotation explains OCR vs Manual entry with styled sections).
- `data-guide` attributes applied only to the first request group (index 0) to ensure unique Joyride targets.

**Files Changed**:
- `src/frontend/src/features/guided-tour/live-guide/liveGuideTypes.ts` — Extended `LiveGuideId` union with `'quotation-management-live-guide'`.
- `src/frontend/src/features/guided-tour/live-guide/liveGuideRegistry.ts` — Added registry entry for `/buyer/items`.
- `src/frontend/src/features/guided-tour/live-guide/guides/quotationManagement.liveGuide.tsx` — [NEW] Guide definition with factory function and 11 steps.
- `src/frontend/src/pages/Buyer/BuyerItemsList.tsx` — Added 10 `data-guide` attributes, `LiveGuideLauncher` in header, guide factory registration via `useLiveGuideRegistration`.
- `docs/VERSION.md` — Bumped to v2.161.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.161.0".
- `docs/GUIDED_TOUR_SYSTEM.md` — Added quotation management guide to Live Guides table.

## [2.160.0] - 2026-05-29

### Changed — Requests Page Guided Tour Points to Timeline Toggle Button

Updated the Requests page tour step 8 to target the new chevron expand/collapse button (`data-tour="request-timeline-toggle"`) instead of the generic `requests-explorer` row area. Title changed from "Clique na Linha para Expandir" to "Ver Timeline do Pedido". Content now instructs the user to click the button on the left side of the row. Placement changed from `top` to `right` for better alignment with the button.

The `data-tour` attribute is applied only to the first row's button to ensure a unique tour target. When no rows exist, `filterActiveSteps` automatically skips the step.

**Files Changed**:
- `src/frontend/src/features/guided-tour/tours/requestsPageTour.ts` — Updated step 8 target, title, content, placement.
- `src/frontend/src/pages/Requests/components/modern/RequestsTableWidget.tsx` — Added `data-tour="request-timeline-toggle"` to first row's chevron button, added `reqIndex` to map callback.
- `docs/VERSION.md` — Bumped to v2.160.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.160.0".

## [2.159.0] - 2026-05-29

### Added — Timeline Expand/Collapse Button on Requests Table

Added a visible expand/collapse chevron button as the first column of the Requests list table. Previously the timeline was only accessible by clicking the entire row, which had no visual affordance. The new button uses ChevronRight (closed) / ChevronDown (open) icons with Industrial Brutalist styling: clear border, strong hover/active states, and a filled primary-color background when expanded.

Accessibility: `aria-expanded`, `aria-controls` linking to a `role="region"` timeline panel, keyboard Enter/Space support, and Portuguese tooltip labels ("Ver timeline do pedido" / "Ocultar timeline do pedido").

**Files Changed**:
- `src/frontend/src/pages/Requests/components/modern/RequestsTableWidget.tsx` — Added expand column, button, a11y attributes, updated colSpan 8→9.
- `docs/VERSION.md` — Bumped to v2.159.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.159.0".

## [2.158.0] - 2026-05-29

### Fixed — Guided Tour on Mandatory Password Change + Department Scope Filtering

**Bug 1: Guided Tour suppressed on mandatory password change screen.**
The general guided tour welcome modal was incorrectly appearing on `/change-password` when a new user logs in with `mustChangePassword = true`. Fixed by adding route and `mustChangePassword` guards to the auto-trigger `useEffect` in `useGuidedTour.ts`. The tour remains pending (not marked as completed) and triggers normally once the user reaches the dashboard after changing their password.

**Bug 2: Department selector restricted to user-scoped departments.**
The department dropdown in Request Creation (`/requests/new`) was showing all active departments regardless of the user's authorization scope. Fixed by filtering the dropdown using `allowedDepartmentCodes` from `/api/v1/users/me`, mirroring the existing plant scope pattern. Auto-selection is applied when only one department is in scope. Backend validation added to `CreateRequest` and `UpdateRequestDraft` endpoints to reject out-of-scope department IDs (HTTP 403).

### Changed — Live Guide Copy Updates (v1.4.0)

- **Grau de Necessidade step**: Updated to rich JSX content explaining each urgency level (Crítico, Urgente, Normal, Baixo) with color-coded labels and descriptions.
- **Departamento step**: Updated to explain scope-based filtering — the list shows only departments within the user's access scope, with guidance to contact an administrator if needed.

**Files Changed**:
- `src/frontend/src/features/guided-tour/useGuidedTour.ts` — Added route + mustChangePassword guards.
- `src/frontend/src/pages/Requests/RequestCreate.tsx` — Added `allowedDepartmentCodes` state, filtering, auto-selection, diagnostic log.
- `src/backend/AlplaPortal.Api/Controllers/RequestsController.cs` — Department scope validation in CreateRequest + UpdateRequestDraft.
- `src/frontend/src/features/guided-tour/live-guide/guides/requestCreation.liveGuide.tsx` — Updated Grau de Necessidade + Departamento step copy; version → 1.4.0.
- `docs/VERSION.md` — Bumped to v2.158.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.158.0".

## [2.157.0] - 2026-05-28

### Added — Reusable Live Guide System & Request Creation Live Guide

Introduced a reusable **Live Guide** infrastructure as an extension of the existing Guided Tour system. Live Guides provide interactive, step-by-step task guidance that validates user input before allowing progression — unlike explanatory tours that are passive and informational.

**Live Guide Infrastructure** (`src/frontend/src/features/guided-tour/live-guide/`):
- `liveGuideTypes.ts` — Core types (`LiveGuideStep`, `LiveGuideDefinition`) with support for `string | ReactNode` content, step conditions, validation functions, and required actions.
- `useLiveGuide.ts` — React hook managing guide lifecycle (start, next, prev, skip, close, complete). Includes `findNextValidStep` for conditional step resolution and a **target-awaiting mechanism** that retries up to 550ms for DOM targets inside AnimatePresence animations.
- `LiveGuideProvider.tsx` — React context provider wrapping Joyride in controlled mode with a custom tooltip component. Uses a separate `TooltipDataContext` to bypass Joyride's internal memoization and ensure real-time validation state propagation.
- `LiveGuideLauncher.tsx` — Reusable button component for starting a registered Live Guide.
- `liveGuideStorage.ts` — localStorage persistence for guide completion/dismissal state.

**Request Creation Live Guide** (`guides/requestCreation.liveGuide.tsx`):
- Factory function pattern: `createRequestCreationGuide(getFormValues)` receives a form state getter to avoid tight coupling to React hooks.
- 12 steps covering: Introduction, Título, Descrição, Documentos de Apoio, Tipo de Pedido (rich JSX with bold Cotação/Pagamento sections and bullet examples), Itens Solicitados (Cotação-conditional), Input de Documento & Faturamento (Pagamento-conditional), Grau de Necessidade, Data Limite, Departamento, Empresa, Planta, Criar Rascunho.
- Conditional steps use DOM-first reading (`readRequestTypeFromDOM()`) with form state fallback to eliminate stale-closure risks.
- Validation blocks progression until required fields are filled (DOM value read + form state fallback).

**Design Decisions**:
- `data-guide` attributes are used for Live Guide targets, separate from `data-tour` (explanatory tours) to avoid namespace collisions.
- No auto-start in v1 — guide starts only from explicit user action ("Guia ao vivo" button).
- Guide definitions are factories registered via `useLiveGuideRegistration`, not hardcoded in page components.
- Custom Joyride tooltip renders `ReactNode` content directly and splits plain strings on `\n` for line breaks.

**Files Changed**:
- `src/frontend/src/features/guided-tour/live-guide/liveGuideTypes.ts` — [NEW] Core types.
- `src/frontend/src/features/guided-tour/live-guide/useLiveGuide.ts` — [NEW] Guide lifecycle hook.
- `src/frontend/src/features/guided-tour/live-guide/LiveGuideProvider.tsx` — [NEW] Context provider + custom tooltip.
- `src/frontend/src/features/guided-tour/live-guide/LiveGuideLauncher.tsx` — [NEW] Launcher button.
- `src/frontend/src/features/guided-tour/live-guide/liveGuideStorage.ts` — [NEW] Persistence utility.
- `src/frontend/src/features/guided-tour/live-guide/guides/requestCreation.liveGuide.tsx` — [NEW] Request creation guide definition.
- `src/frontend/src/features/guided-tour/GuidedTourProvider.tsx` — Integrated LiveGuideProvider wrapper.
- `src/frontend/src/features/guided-tour/GuidedTourButton.tsx` — Added LiveGuideLauncher import.
- `src/frontend/src/pages/Requests/RequestCreate.tsx` — Added `data-guide` attributes and LiveGuideLauncher + guide registration.
- `docs/GUIDED_TOUR_SYSTEM.md` — Added Live Guide architecture section.
- `docs/CHANGELOG.md` — This entry.
- `docs/VERSION.md` — Bumped to v2.157.0.
- `src/frontend/src/config.ts` — APP_VERSION → "2.157.0".

## [2.156.4] - 2026-05-28

### Fixed — Edge "Not Secure" Mixed Content Warning on TEST

The TEST portal at `https://portalgerencial-test.alpla.net/login` displayed a browser "Not secure" warning in Microsoft Edge despite having a valid SSL certificate. The browser reported: *"This site has a valid certificate, issued by a trusted authority. However, some parts of the site are not secure."*

**Root Cause**: Two issues combined to trigger the warning:

1. **ForwardedHeaders middleware disabled**: `UseForwardedHeaders()` was commented out in `Program.cs`. The IIS ARR reverse proxy forwards `HTTPS → HTTP` to Kestrel on `localhost:5001`. Without the middleware, `UseHttpsRedirection()` detected plain HTTP and could generate broken 307 redirects or HTTP-scheme URLs visible to the browser.

2. **No HTTP→HTTPS redirect**: The IIS Web site had both `:80` and `:443` bindings, but the frontend `web.config` had no redirect rule. The portal was accessible via plain `http://`, contributing to the insecure classification.

**Fixes Applied**:
- **Backend** (`Program.cs`): Enabled `ForwardedHeaders` with `XForwardedFor` and `XForwardedProto`. Added `app.UseForwardedHeaders()` as the first middleware call, before `UseHttpsRedirection()`. `KnownNetworks` and `KnownProxies` are cleared because IIS ARR runs on localhost in a single-server architecture.
- **Frontend** (`web.config`): Added HTTP→HTTPS permanent redirect (301) as the first IIS URL Rewrite rule, before the API reverse proxy and SPA fallback rules.

**Investigation Confirmed Safe**:
- No hardcoded insecure `http://` URLs in the frontend codebase.
- `API_BASE_URL` defaults to `''` (same-origin relative paths) — correct.
- Login page resources use relative paths — no external CDN/font/WebSocket URLs.
- Backend URL generation uses config-based `FrontendBaseUrl` (already HTTPS in `appsettings.Test.json`).

**Files Changed**:
- `src/backend/AlplaPortal.Api/Program.cs` — Enabled ForwardedHeaders middleware.
- `src/frontend/public/web.config` — Added HTTP→HTTPS redirect rule.
- `src/frontend/src/config.ts` — Bumped to v2.156.4.
- `docs/VERSION.md` — This entry.
- `docs/CHANGELOG.md` — v2.156.4 entry.
- `docs/GITHUB_ACTIONS_TEST_DEPLOYMENT.md` — Documented ForwardedHeaders and HTTPS redirect requirements.

## [2.156.3] - 2026-05-28

### Fixed — Suppliers Baseline Schema Correction
- **Missing Columns in ConsolidatedBaseline**: The `Suppliers` table in `20260225000000_ConsolidatedBaseline` was missing 3 columns that existed in the entity model and `ApplicationDbContextModelSnapshot` but were never added by any migration: `Origin` (nvarchar, NOT NULL, default `'MANUAL'`), `SourceCompany` (nvarchar, nullable), `LastSyncedAtUtc` (datetime2, nullable).
- **Root Cause**: The entity properties were added to `Supplier.cs` and the snapshot was updated during migration scaffolding, but the generated `Up()` method in `AddSupplierRegistrationFields` did not include `AddColumn` calls for these 3 properties. Clean database installs created the Suppliers table without them, causing runtime `SqlException: Invalid column name` errors when `ProformaDeadlineAlertService` queried requests with `.Include(r => r.Supplier)`.
- **Baseline Fix**: Added the 3 columns to the ConsolidatedBaseline `CreateTable` and `Designer.cs` snapshot. Updated seed data to include `Origin = "MANUAL"`.
- **Post-Install Validation**: Added `Suppliers.Origin`, `Suppliers.SourceCompany`, and `Suppliers.LastSyncedAtUtc` to the critical column checks in `POST_INSTALL_DATABASE_VALIDATION.sql`.
- **No New Migration Required**: The `ApplicationDbContextModelSnapshot` already contained these properties. This fix corrects the baseline for clean installs only.

### Database
- **Migration**: No new EF migration. Existing ConsolidatedBaseline updated.
- **Existing databases**: Validate schema with `POST_INSTALL_DATABASE_VALIDATION.sql`. For TEST, prefer clean recreation when there is no important data. For Production, prepare a reviewed migration or repair plan before any database action.
- **Local development**: Recommended clean recreate via `dotnet ef database drop --force && dotnet ef database update`.

**Files Changed**:
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260225000000_ConsolidatedBaseline.cs` — Added 3 Supplier columns + seed data.
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260225000000_ConsolidatedBaseline.Designer.cs` — Added 3 properties + seed Origin.
- `docs/POST_INSTALL_DATABASE_VALIDATION.sql` — Added 3 Supplier column checks.
- `docs/VERSION.md` — Bumped to v2.156.3.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.156.3".

## [2.156.2] - 2026-05-28

### Fixed — Primavera ERP Default SQL Server Instance
- **Connection String Builder**: `BuildConnectionString` in `PrimaveraConnectionFactory.cs` now correctly handles the default SQL Server instance. Values `MSSQLSERVER`, `DEFAULT`, empty, or whitespace are treated as the default instance — producing `Server=host` instead of the invalid `Server=host\MSSQLSERVER`.
- **Frontend Normalization**: `IntegrationSettings.tsx` trims and normalizes `MSSQLSERVER`/`DEFAULT` to empty before saving, preventing bad data from being persisted.
- **UI Improvement**: Instance field label now shows "(opcional)" with helper text: "Para a instância padrão do SQL Server, deixe este campo vazio."

**Files Changed**:
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/PrimaveraConnectionFactory.cs` — Default instance normalization.
- `src/frontend/src/pages/Admin/IntegrationSettings.tsx` — Frontend normalization + UI helper text.
- `docs/VERSION.md` — Bumped to v2.156.2.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.156.2".

## [2.156.1] - 2026-05-27

### Improved — Deployment Tooling & Post-Install Validation
- **Admin User Seed Template**: Enhanced `docs/ADMIN_USER_SEED_TEMPLATE.sql` to be fully idempotent (works for both new and existing users). Now assigns all 12 administrative roles using safe `INSERT...WHERE NOT EXISTS` patterns. Plant and department scopes assigned dynamically via `WHERE IsActive = 1`.
- **Post-Install Validation**: Added `InformationalNotifications.Category` and `InformationalNotifications.EventCorrelationId` to critical column checks. Added Step 5b: Admin User Bootstrap Validation — verifies at least one active System Administrator exists with plant scopes and department scopes.
- **Password Hash Generator**: New `tools/PasswordHasher` — standalone .NET 8 console tool using `BCrypt.Net-Next 4.1.0` (same as application) for generating admin seed password hashes safely. Referenced by `ADMIN_USER_SEED_TEMPLATE.sql`.

**Files Changed**:
- `docs/ADMIN_USER_SEED_TEMPLATE.sql` — Idempotent, all roles, dynamic scopes.
- `docs/POST_INSTALL_DATABASE_VALIDATION.sql` — Added notification columns + admin bootstrap check.
- `tools/PasswordHasher/PasswordHasher.csproj` — [NEW] BCrypt hash generator project.
- `tools/PasswordHasher/Program.cs` — [NEW] BCrypt hash generator code.
- `docs/VERSION.md` — Bumped to v2.156.1.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.156.1".

## [2.156.0] - 2026-05-27

### Added — Migration Consolidation & Deployment Hardening
- **Consolidated Baseline Migration**: New `20260225000000_ConsolidatedBaseline` EF Core migration replacing 41 deleted migration files. Creates all 29 foundational tables with complete schema, indexes, foreign keys, and seed data. Enables clean database installations to work through the standard EF Core migration pipeline.
- **Startup Schema Validation**: `Program.cs` now validates 14 critical tables exist after migration. In TEST/PRODUCTION, migration or schema failure **crashes the application** to prevent operation with a broken database. In Development, failures log a warning but allow continued local iteration.
- **Post-Install Validation Script**: New `docs/POST_INSTALL_DATABASE_VALIDATION.sql` — read-only SQL script that validates table existence, critical columns, seed data counts, migration history, user status, and FK integrity. Run after every deployment.
- **Deployment Checklist**: New `docs/DEPLOYMENT_CHECKLIST.md` covering pre-deployment, deployment steps, post-deployment validation, clean install, upgrade, emergency rollback, and **local development database** setup (Option A: clean recreate, Option B: preserve existing data).
- **Admin User Seed Template**: New `docs/ADMIN_USER_SEED_TEMPLATE.sql` — parameterized SQL template for creating the first administrator user on clean databases, with placeholder validation and forced password change on first login.

### Changed
- **Startup Logging**: All migration messages now use `[STARTUP]` prefix instead of `[DEBUG]` for production visibility.
- **Post-Deployment Endpoint Checks**: Added `/api/v1/lookups/request-types` and `/api/v1/iva-rates` to the deployment validation checklist (these were the root cause of the original AOVIA1VMS011 issue).

### Database
- **Migration**: `20260225000000_ConsolidatedBaseline` — required for all environments.
- **Existing databases**: Must register the baseline in `__EFMigrationsHistory` BEFORE deploying the new build.
- **Local development**: Recommended clean recreate via `dotnet ef database drop --force && dotnet ef database update`.

**Files Changed**:
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260225000000_ConsolidatedBaseline.cs` — [NEW] Consolidated baseline migration.
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260225000000_ConsolidatedBaseline.Designer.cs` — [NEW] Model snapshot for baseline.
- `src/backend/AlplaPortal.Api/Program.cs` — Crash-on-failure startup + schema validation.
- `docs/POST_INSTALL_DATABASE_VALIDATION.sql` — [NEW] Read-only schema health check.
- `docs/DEPLOYMENT_CHECKLIST.md` — [NEW] Full deployment procedure + local dev instructions.
- `docs/ADMIN_USER_SEED_TEMPLATE.sql` — [NEW] Admin user seed template.
- `docs/VERSION.md` — Bumped to v2.156.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.156.0".

## [2.155.2] - 2026-05-26

### Fixed — RequestCreate Scope Loading Resilience
- **Decoupled API Calls**: Separated `/me` (live user profile) from the 8 auxiliary lookups in `RequestCreate.tsx` into independent try/catch blocks. This prevents unrelated lookup failures from causing a false "ACESSO RESTRITO" error.
- **Detailed Error Banner**: Added a red "ERRO AO CARREGAR PERFIL" banner with Reload and Dashboard buttons when `/me` fails.
- **Auxiliary Lookups Warning**: Added an amber warning banner if auxiliary lookups fail, while preserving the valid user plant scope and keeping the creation form interactive.
- **Safe Diagnostic Logs**: Added three safe, non-sensitive `console.info` statements logging loaded plant counts, lookup status, and filter metrics for local debugging without exposing secrets.
- **Diagnostic Guide**: Created a comprehensive guide `docs/REQUEST_CREATE_ACCESS_RESTRICTED_DIAGNOSTIC.md` featuring DevTools instructions, network status lookup guides, and read-only database queries to troubleshoot access rules.

## [2.155.1] - 2026-05-26

### Fixed — Post-Deployment TEST Environment Issues
- **Blank Page Fix**: Replaced `Copy-Item` with `robocopy /E` in `deploy-test.yml` to preserve Vite `dist/assets/` subdirectory structure. Added validation step to verify `assets/` directory and JS/CSS files exist before artifact upload.
- **API URL Duplication Fix**: Changed `API_BASE_URL` default from `'/api'` to `''` in `api.ts`. Endpoints already include `/api/` prefix — the old default caused double `/api/api/` paths in production builds.
- **Frontend web.config**: Created `src/frontend/public/web.config` with IIS URL Rewrite rules for reverse proxy (`/api/*` → `http://localhost:5001/api/*`) and SPA fallback (`index.html` for React Router).
- **Documentation**: Expanded `GITHUB_ACTIONS_TEST_DEPLOYMENT.md` with reverse proxy prerequisites (ARR), `ASPNETCORE_ENVIRONMENT` configuration, `appsettings.Test.json` requirements, API 500 diagnosis checklist, and post-deployment issue log.

## [2.155.0] - 2026-05-26

### Added — GitHub Actions TEST Deployment Workflow (CI/CD)
- **First CI/CD Pipeline**: Created `.github/workflows/deploy-test.yml` — the first GitHub Actions workflow for automated deployment to the TEST environment on `AOVIA1VMS011`.
- **Workflow Dispatch**: Manual trigger via `workflow_dispatch` with a required `version` input (e.g., `v2.154.0`).
- **Build Job**: Compiles backend (.NET 8 Release) and frontend (React/Vite with TypeScript type check) on `windows-latest`, uploads artifacts.
- **Deploy Job**: Runs on self-hosted runner (`[self-hosted, Windows, X64, iis, test, alpla-portal-test]`) with GitHub Environment `test`. Downloads artifacts, creates timestamped backups, stops IIS App Pools, deploys files, starts IIS App Pools, runs smoke test.
- **Config Preservation**: Environment-specific `appsettings.*.json` files on the server are preserved during deployment. Frontend server-side `web.config` (SPA rewrite rules) conditionally preserved.
- **Safety**: No secrets committed, no production touched, port 5000 never used (API uses port 5001), EF migrations intentionally not automated.
- **Documentation**: Created `docs/GITHUB_ACTIONS_TEST_DEPLOYMENT.md` — comprehensive deployment guide covering prerequisites, IIS configuration, runner labels, environment variables, certificate info, backup/rollback, migration policy, post-deployment checklist, and troubleshooting.

## [2.154.0] - 2026-05-25

### Improved — Primavera ERP Connection Validation & Health Consistency Corrections
- **Sequential Validation Pipeline**: `TestCompanyConnectionAsync` in `PrimaveraIntegrationProvider.cs` now runs 6 strict sequential validation checks in Portuguese before executing any SQL connection: (1) provider disabled, (2) company disabled, (3) server missing, (4) database missing, (5) username missing, (6) password missing — each returning a specific, actionable diagnostic message.
- **Enabled State Alignment**: `IntegrationSettingsService.MapToDto` and `IntegrationHealthService.GetHealthSummaryAsync` now derive `isEnabled` strictly from `provider.IsEnabled` in the database, eliminating the `|| configEnabled` fallback that caused mismatch between toggle buttons and backend connection test behavior.
- **Dynamic Display Status**: `DetermineDisplayStatus` in `IntegrationHealthService.cs` now dynamically evaluates all active Primavera companies, returning `Inactive` when the provider is disabled and `NotConfigured` when any enabled company lacks database, username, or password.
- **Primavera Bypass Gate**: `TestProviderConnectionAsync` bypasses the global `isEnabled` early-exit for `PRIMAVERA` so the provider-level validation can report the specific Portuguese diagnostic message instead of a generic English error.
- **UI Warning Badge**: `IntegrationSettings.tsx` now renders `"⚠ Senha não configurada."` on active company cards where the password has not been set.
- **Zero-Error Verification**: Backend build (`dotnet build`) and frontend type check (`npx tsc --noEmit`) passed with 0 warnings and 0 errors.

### Added — Security Incident Response & Unified SMTP Integration Consolidation (DEC-135)
- **GitGuardian Security Incident Remediation**: Created the official security incident report (`docs/SECURITY_INCIDENT_GITGUARDIAN_SMTP_SECRET_LEAK.md`) outlining date, repository context, and a git history scrubbing procedure using `git-filter-repo`. Removed the hardcoded plaintext database password from the tracked script `scripts/query_innux.ps1`, replacing it with a dynamic environment variable `INNUX_DB_PASSWORD`. Confirmed `appsettings.Development.json` is untracked and safely gitignored.
- **Dados Mestres SMTP Removal**: Completely stripped the legacy SMTP tab, panel rendering (`<SmtpSettingsPanel>`), import statements, and related component state variables from `MasterData.tsx`, ensuring that SMTP is no longer managed under Master Data.
- **Unified SMTP Configuration**: Modified `IntegrationSettingsService.cs` and `IntegrationSettingsDtos.cs` to route the `"SMTP"` provider operations securely to the existing database-backed single-row `SmtpSettings` table, preventing duplicate model creation and preserving historical record encryption.
- **Integration Settings UI Enhancements**: Extended `IntegrationSettings.tsx` to display SMTP connection fields (Host, Port, SSL, Sender Email, Sender Name, Username) inside the cards, and implemented `ConnectionConfigureModal` allowing administrators to configure connection details for Primavera, Innux, OpenAI, and SMTP.
- **New Integration Health Providers**: Created `SmtpIntegrationProvider.cs` and `OpenAiIntegrationProvider.cs` implementing `IIntegrationProvider` to enable real-time connection testing under the unified `IntegrationSettingsController`. Deleted the obsolete `SmtpSettingsController.cs`.
- **Guided Tour Audit**: Verified that no active tours target the legacy SMTP master data panels.
- **Zero-Error Verification**: Confirmed zero compilation errors (`dotnet build`) and zero type safety issues (`npx tsc --noEmit`).

## [2.153.0] - 2026-05-25

### Added — Integration Management Module: CRUD UI, Factory Refactoring & Frontend Type Safety (DEC-134)
- **Integration Settings CRUD API**: New `IntegrationSettingsController` with GET (all/by-code), PUT (non-secret update), POST (secret rotation via AES encryption), POST (test connection), POST (enable/disable) endpoints under `[Authorize(Roles = "System Administrator")]`.
- **Integration Settings Management UI**: New `IntegrationSettings.tsx` page at `/admin/integrations` with expandable provider cards, inline field editing, masked secret management, real-time connection testing, and enable/disable controls. Added to `AdministratorWorkspace.tsx` admin tile grid.
- **Factory Refactoring — DB-First Configuration**: Created `IntegrationConfigResolver` (scoped service) implementing the DB → `IConfiguration` → Safe Disabled cascade. Refactored `PrimaveraConnectionFactory`, `InnuxConnectionFactory`, and `OpenAiDocumentExtractionProvider` to consume DB-backed settings with environment variable fallback.
- **Frontend Type Safety**: Moved inline `IntegrationSettingsDto` to shared `types/index.ts`. Added `UpdateIntegrationSettingsDto`, `ReplaceIntegrationSecretDto`, and `IntegrationConnectionTestResultDto` types. Eliminated all `Promise<any>` returns in `api.ts` integration methods. Replaced all `catch (err: any)` with `catch (err: unknown)` and safe `instanceof Error` checks.
- **Bug Fix — Test Connection Result Mapping**: Fixed test connection handler reading `result.currentStatus` (wrong DTO shape) instead of `result.success` from `IntegrationConnectionTestResultDto`. This would have caused test results to always show failure.
- **Tour Anchors**: Added `data-tour="integrations-configure-btn"` anchor to the provider card header for guided tour integration.
- **Database Migration**: `AddIntegrationManagementUI` migration seeds OPENAI and SMTP providers and `IntegrationProviderSettings` rows.

## [2.152.0] - 2026-05-25

### Fixed — AOVIA1VMS011 Staging IIS Connection String Mismatch & Hardening (DEC-133)
- **Staging Connection String Key Correction:** Resolved the staging backend login `HTTP 500` error by patching the secure IIS configuration script to write the correct **`ConnectionStrings__DefaultConnection`** environment variable key expected by EF Core (`Program.cs`), resolving the `System.InvalidOperationException: The ConnectionString property has not been initialized` blocker.
- **Same-Origin Virtual Directory Audit:** Documented the intentional double `/api` virtual path structure (`/api/api/auth/login`) arising from IIS virtual directories combined with backend controller route prefixes.
- **DataProtection Key Ring Hardening Analysis:** Identified and analyzed ephemeral in-memory DataProtection keys warnings from IIS AppPool and outlined persistent key ring mitigation strategies for production.

## [2.151.0] - 2026-05-25

### Added — AOVIA1VMS011 Phase 3 Staging Access Recovery & same-origin API Routing (DEC-133)
- **Staging Access Recovery Utility:** Compiled and deployed a dedicated .NET 8 console utility `StagingAccessRecovery.exe` to `C:\temp\StagingAccessRecovery\` on `AOVIA1VMS011` to resolve legacy Windows PowerShell .NET Core loading blockers.
- **BCrypt Hashing Validation:** Automated schema mapping verification (`dbo.Users` columns, `dbo.Roles` admin seed, `dbo.UserRoleAssignments`) and mapped Leonardo's user account securely using standard `BCrypt.Net-Next` assembly.
- **Relative API base path routing:** Refactored frontend API client default base URL in `api.ts` from hardcoded `localhost:5000` to relative same-origin `/api` path, eliminating CORS preflights and direct Kestrel dependency in staging/production builds.
- **Secure Redacted Logging:** Confirmed zero plaintext secrets or hashes saved to documentation, repository, or remote audit logs.

## [2.150.0] - 2026-05-23

### Added — AOVIA1VMS011 Phase 3 Test/Staging Deployment Staged & Configured (DEC-133)
- **Controlled Binary Deployments:** Packaged backend API in Release mode and compiled Vite frontend static assets; successfully transferred all assets over SMB to remote server.
- **IIS Secure Configuration Blueprint:** Configured a secure local PowerShell deployment configuration script `AOVIA1VMS011_PHASE3_SECURE_CONFIGURATION.ps1` utilizing interactive credential prompt and redacting all passwords in report.
- **IIS applicationHost.config tradeoff defined:** Explicitly identified and documented connection string plaintext persistence in IIS applicationHost.config and recommended Windows Authentication for Phase 4.
- **Explicit migrations strategy:** Pre-placed idempotent migrations SQL script `migration.sql` and established explicit controlled database execution against `[Portal-Gerencial-Test]` using `sqlcmd` with Windows Authentication, rather than automatic health check triggers.
- **Automated Express backups wrapper:** Staged PowerShell daily backup wrapper script and idempotent SQL scripts on remote server to bypass Express Edition SQL Agent limitations.

## [2.149.0] - 2026-05-23

### Added — AOVIA1VMS011 SQL Portal Databases & Logins Provisioned (DEC-133)
- **Local Provisioning Wrapper:** Created PowerShell provisioning wrapper `AOVIA1VMS011_PHASE2_CREATE_PORTAL_DATABASES_AND_LOGINS.ps1` with secure in-memory random password generation and copied it over SMB.
- **Portal Databases Created:** Provisioned dedicated relational databases `[Portal-Gerencial]` and `[Portal-Gerencial-Test]` using bracket notation.
- **SQL Application Logins Created:** Created SQL Authentication logins `adm_portalgerencial`, `usr_portalgerencial`, and `usr_portalgerencial_test`.
- **Database Mappings & Permissions Mapped:** Configured `adm_portalgerencial` as `db_owner` on both databases, and mapped runtime logins as `db_owner` temporarily to support Entity Framework Core DDL migrations.
- **Cross-Database Isolation Verified:** Verified zero mapping exposure in system databases and confirmed complete production/test isolation.
- **SQL Express Backup Strategy:** Prepared a robust daily backup strategy using Windows Task Scheduler + sqlcmd scripts to handle SQL Agent unavailability.
- **Documentation Updated:** Created `docs/AOVIA1VMS011_PHASE2_DATABASE_AND_LOGIN_CREATION_REPORT.md` and updated database prep report and decisions log.

## [2.148.0] - 2026-05-23

### Added — AOVIA1VMS011 SQL Sysadmin Recovery Validation (DEC-133)
- **Local Validation Execution:** Authored the validation script `AOVIA1VMS011_PHASE2_SQL_SYSADMIN_RECOVERY_VALIDATION.ps1` and copied it to the remote server over SMB.
- **Service Multi-User Audit:** Validated that service `MSSQLSERVER` is running and is restored to normal multi-user mode with no Single-User `/m` parameter active.
- **Windows Login sysadmin validation:** Confirmed that `ALPLA\adm_cintra01` successfully connects via local Windows Authentication and has full administrative rights (`IS_SRVROLEMEMBER('sysadmin') = 1`).
- **Catalog Integrity Checked:** Physically and logically verified that no Portal databases or logins have been created yet, and all existing databases are intact and untouched.
- **Operational Isolation Confirmed:** Confirmed that no Innux or attendance databases were impacted, no secrets were stored, and no binaries were deployed.
- **Documentation Updated:** Created `docs/AOVIA1VMS011_PHASE2_SQL_SYSADMIN_RECOVERY_VALIDATION.md` and updated the database preparation report.

## [2.147.0] - 2026-05-23

### Added — AOVIA1VMS011 SQL Instance Reuse Assessment: Decommission Verified (DEC-133)
- **Local Service Audit:** Checked SQL services locally on `AOVIA1VMS011`, confirming the default instance `MSSQLSERVER` is running and service account is `NT Service\MSSQLSERVER`.
- **Physical DATA Audit:** Performed direct scanning of SQL default DATA folder and recursively drive D:, verifying **0 user databases** exist on disk (only system databases present).
- **Active Connections Audit:** Ran network and process netstat scans, confirming **0 active connections** on port 1433 or local pipes.
- **SQL Agent Status:** Audited service launcher, confirming SQL Server Agent is stopped and unavailable due to Express Edition restrictions.
- **Controlled SQL Access Recovery Blueprint:** Prepared a step-by-step single-user mode (`/m"SQLCMD"`) recovery plan to resolve the sysadmin access blocker for account `ALPLA\adm_cintra01` with zero operational risk.
- **Decommission Verdict:** Officially confirmed the instance is no longer used, and is 100% safe to repurpose for Portal databases `[Portal-Gerencial]` and `[Portal-Gerencial-Test]`.
- **Changelog & Documentation Alignment:** Created `AOVIA1VMS011_SQL_INSTANCE_REUSE_ASSESSMENT.md` and aligned system to v2.147.0.

## [2.146.0] - 2026-05-23

### Added — AOVIA1VMS011 Phase 2 Database Prep: AD & SQL Server Logins Discovery (DEC-133)
- **Active Directory Sweeps:** Completed domain-wide group discovery and mapped the ALPLA corporate AD group naming convention prefix standard (`SQ-`).
- **Leonardo Group memberships audit:** Checked Leonardo's active domain group memberships via `whoami /groups` and identified candidate IT support groups `ALPLA\SD-AOVIA1-IT-Systems` and `ALPLA\SD-AO0001-IT-Systems`.
- **SQL Server Logins discovery script:** Pre-placed and executed the read-only SQL Server discovery script `C:\temp\AOVIA1VMS011_PHASE2_DISCOVERY.ps1` under Leonardo's administrative Windows context (`ALPLA\adm_cintra01`) on default instance `MSSQLSERVER`.
- **Metadata Visibility restrictions analysis:** Documented the critical infrastructure finding where Leonardo's admin account connects successfully but suffers from metadata visibility restrictions (returning 0 rows), confirming it is not individually configured as a `sysadmin` login or mapped to a `sysadmin` group.
- **SQL Portal DBAdmin group recommendation:** Formulated the official recommendation to request the Active Directory team to create a dedicated least-privilege security group: `ALPLA\SQ-AOVIA1VMS011-PortalGerencial-DBAdmins`.
- **Safe Database Prep Policies:** Confirmed that no databases, logins, or users were created, no secrets were stored, and no changes were made to SQL security or Innux databases.

## [2.145.0] - 2026-05-23

### Added — AOVIA1VMS011 Post-Remediation Validation: ANCM Blocker Resolved (DEC-133)
- **ANCM Blocker Resolved:** Verified that the ASP.NET Core IIS Module (ANCM) `aspnetcorev2.dll` is now present and successfully registered as an active global module.
- **IIS Post-Setup Verification:** Confirmed that the IIS Web Server service is running, and that all Production and Test/Staging folders, application pools, and site settings remain completely intact.
- **HTTPS Bindings Secured:** Confirmed that the SSL certificate SNI bindings remain intact on Port 443 with 0 plain-text credentials stored.
- **Secure Port Enforcements:** Verified that ports `5000` and `5001` remain closed and unused.
- **Database Isolation Verification:** Audited and confirmed that no databases have been created yet, and that all Innux operational attendance databases remain untouched.

## [2.144.0] - 2026-05-23

### Added — AOVIA1VMS011 Backend Deployment Blocker Remediation: ANCM Repair Plan (DEC-133)
- **ANCM Blocker Diagnosis:** Documented the full cause analysis and post-setup findings for the missing `aspnetcorev2.dll` and unconfigured global handler.
- **Detailed Remediation Blueprint:** Prepared and documented clear, step-by-step instructions for Leonardo to download the correct `dotnet-hosting-8.0.8-win.exe` offline installer, copy it to `C:\temp\`, and execute an administrative **Repair**.
- **Post-Setup Validation Checks:** Documented IIS service verification, HTTPS cert bindings, URL Rewrite checks, and `iisreset` verification metrics to guarantee deployment readiness.

## [2.143.0] - 2026-05-23

### Added — AOVIA1VMS011 Phase 1 Server Preparation Completed (DEC-133)
- **IIS Server Role Provisioned:** Successfully enabled IIS Web Server features and Management Tools locally on `AOVIA1VMS011` using the idempotent setup script.
- **Offline URL Rewrite Installation:** Installed IIS URL Rewrite module offline from `C:\temp\rewrite_amd64_en-US.msi`. Checked `rewrite.dll` binary presence.
- **Isolated Directory Layouts:** Provisioned the complete isolated 14 folder structures over SMB on drive `D:\PortalGerencial` and `D:\PortalGerencial-Test`.
- **NTFS ACL Permissions Applied:** Assigned Modify/Read rights specifically to the dynamial App Pool Identities (`IIS APPPOOL\PortalGerencialApiPool` and `IIS APPPOOL\PortalGerencialTestApiPool`).
- **Interactive SSL Certificate Binding:** Securely imported Production and Test/Staging SSL certificates (prompting securely for passwords via SecureStrings) and configured HTTPS port 443 bindings with SNI.
- **Firewall Exceptions:** Added TCP 80/443 inbound firewall rules. Verified ports 5000/5001 remain closed and unused.
- **ANCM Blocker Logged:** Identified and verified the ASP.NET Core IIS Module (ANCM) `aspnetcorev2.dll` missing registration warning as the primary remaining blocker before backend deployment. Recommended Hosting Bundle repair in Phase 2.
- **SQL Instance Confirmed:** Officially recorded instance `MSSQLSERVER` as the approved target for the Portal databases.

## [2.142.0] - 2026-05-22

### Added — AOVIA1VMS011 Dual-Environment Strategy: Test/Staging Environment (DEC-133)
- **Dual-Environment Architecture**: Deployment documentation restructured to support both Production and Test/Staging on `AOVIA1VMS011` with complete isolation between environments.
- **Test/Staging Resources**: Dedicated database `[Portal-Gerencial-Test]`, folder root `D:\PortalGerencial-Test` (with Frontend, Api, Logs, Attachments, Backups, Packages, Temp), IIS site `PortalGerencial.Test`, app pools `PortalGerencialTestAppPool`/`PortalGerencialTestApiPool`.
- **Separate SSL Certificate**: Test/Staging uses its own PFX certificate (`334ad6893b414f90a349c960c5e45af4.pfx`), separate from the Production certificate.
- **Integration Write-Safety Matrix**: Classified all integrations by write capability. Primavera/Innux enabled as read-only in Test/Staging. Email notifications disabled by default. Write-capable integrations blocked until explicitly approved.
- **Release Promotion Workflow**: New Build→Test→Validate→Promote→Production deployment flow documented. Direct production deployment without test validation is prohibited.
- **Temp Folders**: Added `Temp` subfolder to both Production and Test/Staging directory structures.
- **DEC-133 Amended**: Added decision items #8 (Dual-Environment Isolation) and #9 (Integration Write-Safety Policy).

## [2.140.0] - 2026-05-22

### Changed — AOVIA1VMS011 Infrastructure Corrections: Database Rename & Port Restriction (DEC-133)
- **Database Renamed**: Production database renamed from `AlplaPortal` to `[Portal-Gerencial]`. All SQL scripts, connection strings, and documentation now use bracket notation due to the hyphen in the name.
- **Port 5000 Restriction**: Port 5000 is reserved/unavailable on AOVIA1VMS011 (used intermittently by another service). Port 5001 also excluded. Backend must never bind to ports 5000 or 5001.
- **IIS In-Process Hosting**: Deployment model changed from Kestrel-on-port to IIS in-process hosting (`hostingModel="InProcess"` via ANCM). No separate Kestrel port is exposed externally. All user traffic flows through IIS HTTPS on port 443.
- **Folder Root Renamed**: Application root folder renamed from `D:\AlplaPortal` to `D:\PortalGerencial`. All subfolder references (Frontend, Api, Logs, Attachments, Backups, Packages) updated.
- **IIS Pool Names Renamed**: Application pools renamed from `AlplaPortalAppPool`/`AlplaPortalApiPool` to `PortalGerencialAppPool`/`PortalGerencialApiPool`.
- **IIS Site Name Renamed**: From `AlplaPortal.Production` to `PortalGerencial.Production`.
- **Smoke Test Expanded**: Added validation step #8 (Port 5000 NOT bound) to the deployment validation checklist.
- **DEC-133 Amended**: Added decision items #7 (Backend Port Restriction) and updated items #1, #2, #5, #6 with new naming.

## [2.139.0] - 2026-05-22

### Added — AOVIA1VMS011 Deployment Architecture Updates & Implementation Plan (DEC-133)

## [2.138.0] - 2026-05-22

### Added — Server Deployment Readiness Analysis: AOVIA1VMS011 (DEC-133)
- **Comprehensive Deployment Assessment**: Generated a detailed, read-only 15-section readiness report under `docs/SERVER_AOVIA1VMS011_READINESS_ANALYSIS.md` for Windows Server `AOVIA1VMS011` to host the Portal Gerencial (.NET 8 + React Vite + SQL Server).
- **Environment Inventory**: Documented OS (Windows Server 2022 Standard), dedicated system drive C: (61.98 GB free), empty data drive D: (199.88 GB free), and shared status (hosts Innux database instances).
- **IIS Web Server Gap Analysis**: Identified missing `Web-Server (IIS)` role and **IIS URL Rewrite Module v2.1** as critical blockers on the server.
- **SQL Server Strategy Recommendation**: Documented local SQL Server 2019 instances (`MSSQLSERVER`, `MSSQLSERVER01`, `INNUX`, `INUTIME`, `INNUXTIME`) and formally recommended Option B: host the database on the ERP database server `AOVIA1VMS012\SQLALPLA` next to Primavera databases.
- **Path Traversal Security Fix**: Audited `AttachmentsController.cs` and discovered a hardcoded relative path traversal vulnerability resolving to `C:\data\attachments`. Recommended refactoring it to load from `appsettings.Production.json` and map to `D:\AlplaPortal\Attachments`.
- **Integration Configuration Audit**: Cataloged development mappings to Primavera (`AOVIA1VMS012\SQLALPLA`) and Innux (`np:\\AOVIA1VMS012\pipe\MSSQL$SQLINNUX\sql\query`) for production enablement guidance.
- **Production Architecture Design**: Detailed a single-site Unified Reverse Proxy architecture mapping Port 80/443 traffic to static frontend on `D:` and reverse-proxying `/api` requests back to .NET Kestrel backend, bypassing all CORS issues.
- **Backup & Telemetry Recommendations**: Outlined daily database backups, incremental attachment logs, Serilog daily rotation on `D:\AlplaPortal\Logs`, and Event Viewer logging.

## [2.137.0] - 2026-05-22

### Added — Guided Tour: Approval Drawer Tours (DEC-132)
- **Two New Drawer Tours**: `drawer-approval-area` (8 steps, operational validation focus) and `drawer-approval-final` (8 steps, decision validation focus) for the Approval Quick Overview Drawer.
- **New Tour Level**: Added `'drawer'` to `TourLevel` type — distinct from page tours, drawer tours scroll inside a panel instead of the window.
- **Drawer-Aware Scroll Handling**: Extended `scrollTargetIntoView()` in `useGuidedTour.ts` to detect a `scrollContainerSelector` on the active tour definition. When set, scrolls the drawer container using `container.scrollTo()` with sticky footer compensation (72px). Falls back to standard window scroll for page tours.
- **`scrollContainerSelector` Property**: Added optional `scrollContainerSelector?: string` to `TourDefinition`. Drawer tours registered with `scrollContainerSelector: '[data-tour-scroll-container="approval-drawer"]'`.
- **Tour Button in Drawer**: Added "Tour da Aprovação" button inside `ApprovalDetailPanel.tsx` next to the existing "Manual de Aprovação" button. Auto-selects the correct tour based on `approvalStage` (`AREA` → `drawer-approval-area`, `FINAL` → `drawer-approval-final`).
- **Joyride Drawer Config**: For drawer-level tours, `scrollToFirstStep` is disabled, `disableScrolling` is enabled (custom scroll handler takes over), and `overlayClickAction` is set to `'none'` to prevent accidental drawer dismissal.
- **data-tour Anchors**: Added 11 anchors to `ApprovalDetailPanel.tsx`: `approval-drawer-header`, `approval-drawer-tour-button`, `approval-drawer-manual-button`, `approval-drawer-alerts`, `approval-drawer-request-info`, `approval-drawer-financial-allocation`, `approval-drawer-financial-context`, `approval-drawer-quotations`, `approval-drawer-documents`, `approval-drawer-items`, `approval-drawer-workflow`, `approval-drawer-actions`.
- **Scroll Container Attribute**: Added `data-tour-scroll-container="approval-drawer"` to the drawer's scrollable `div` in `ApprovalCenter.tsx`.
- **Graceful Missing Targets**: Alert step skipped when no validation warnings exist. Financial context, quotations, documents, workflow, and items steps skipped when sections are absent. Existing `filterActiveSteps()` handles all cases.
- **Area Tour Focus**: Request need, allocation status, cost center, plant, items, winning quotation, validation alerts.
- **Final Tour Focus**: Financial impact, risks, supplier/quotation choice, documents, workflow history, final decision.

## [2.136.0] - 2026-05-22

### Added — Guided Tour: Centro de Aprovações Page Tour (DEC-132)
- **New Tour Created**: `page-approvals-center` tour with 7 steps covering the full approval workflow: Page Header, KPI Cards, Filter Tabs, Area Queue, Final Queue, Request Card, and Empty State.
- **New Tour File**: `approvalsCenterTour.ts` — tour definition with conditional steps for role-based queues and data-dependent cards.
- **New TourId**: Added `'page-approvals-center'` to the `TourId` union type.
- **Registry Entry**: Registered in `guidedTourRegistry.ts` matching route `/approvals`.
- **Tour Button**: Added `GuidedTourContextButton` to the PageHeader of `ApprovalCenter.tsx`.
- **data-tour Anchors**: Added `approvals-header`, `approvals-kpi-cards`, `approvals-filter-tabs`, `approvals-area-queue`, `approvals-final-queue`, `approvals-request-card` (first card only), and `approvals-empty-state`.
- **Conditional Step Behavior**: Area/Final queue steps appear only if the user has the corresponding approver role. Request card step appears only if at least one card is in the DOM. Empty state step appears only when both queues are empty.
- **DEV Seed Area Excluded**: No `data-tour` attributes were added to the DEV tools section; it is completely ignored by the tour.

## [2.135.0] - 2026-05-22

### Improved — Guided Tour: Workspace de Recebimento Tour Expansion (DEC-132)
- **Tour Expanded (3→6 steps)**: Receiving Workspace page tour (`page-receiving-workspace`) expanded from 3 generic steps to 6 targeted steps: Page Header, Info Banner, Search, Pedidos Pendentes, Pedidos em Acompanhamento (new), Pedidos Recebidos.
- **New data-tour Attributes**: Added `data-tour="receiving-info"` (info banner), `data-tour="receiving-search"` (search bar), and `data-tour="receiving-in-progress"` (followup/in-progress section) to `ReceivingWorkspace.tsx`.
- **In-Progress Section Coverage**: The "Pedidos em acompanhamento de recebimento" section now has a dedicated tour step explaining partial receipts and pending inspections.
- **Graceful Degradation**: All section tour anchors wrap the `CollapsibleSection` component (always rendered even with 0 items), so steps appear regardless of record count. `filterActiveSteps` handles any conditional rendering.

## [2.134.0] - 2026-05-22

### Fixed — Guided Tour: Auto-Expand First Request on Gestão de Cotações Tour (DEC-132)
- **Pre-Tour Preparation Event**: `useGuidedTour.executeTourStart()` now dispatches a `guided-tour:prepare` CustomEvent before filtering steps. This allows page components to perform preparation (e.g., expanding a collapsed section) before the tour checks which DOM targets are available.
- **Auto-Expand on BuyerItemsList**: When the `page-buyer-items` tour starts with no request expanded, the component listens for the preparation event and automatically expands the first available request. This ensures the full 6-step tour (header → search → list → opened request → items → quotations) runs without requiring manual preparation.
- **350ms Render Delay**: After dispatching the preparation event, the tour system waits 350ms + one animation frame for React to process the state update and paint the expanded content before filtering steps.
- **Graceful Fallback**: If no requests exist, the event handler does nothing and the tour runs in reduced mode (header → search → empty state).

## [2.133.0] - 2026-05-22

### Improved — Guided Tour: Gestão de Cotações Page Tour Expansion (DEC-132)
- **Tour Expanded (3→7 steps)**: Buyer Items page tour (`page-buyer-items`) expanded from 3 generic steps to 7 targeted steps: Page Header, Search & Filters, Request List, Opened Request Details, Requested Items, Quotations & Documents, and Empty State.
- **New data-tour Attributes**: Added `data-tour="buyer-open-request"` (expanded request container), `data-tour="buyer-open-request-items"` (requested items section), `data-tour="buyer-open-request-quotations"` (quotations/documents section), and `data-tour="buyer-items-empty-state"` (empty state placeholder) to `BuyerItemsList.tsx`.
- **Conditional Step Behavior**: Steps 4–6 (opened request, items, quotations) only appear when a request is expanded. Step 7 (empty state) only appears when no requests exist. All handled via existing `filterActiveSteps()` — no custom logic needed.
- **Graceful Degradation**: Tour runs with 3 steps (header, search, list + empty state) when no requests exist; runs with up to 6 steps (header, search, list, opened request, items, quotations) when a request is expanded.

## [2.132.0] - 2026-05-22

### Fixed — Guided Tour: Pedidos Submenu, Floating Buttons, Kebab Menu & Step Order (DEC-132)
- **Pedidos Submenu Fix (Correction 1)**: The module tour step for "Pedidos" was targeting a wrapper `div` via the top-level `TOUR_ATTR_MAP` instead of the actual submenu `NavLink`. Fixed by: (a) removing `pedidos` from the top-level map, (b) adding `'pedidos': 'purchase-requests-menu'` to the `CHILD_TOUR_MAP` inside `SidebarGroup`, and (c) updating tour targets in both `purchasingLogisticsTour.ts` and `portalMainTour.ts`. The step now correctly highlights the clickable "Pedidos" link inside the expanded "Compras & Logística" group.
- **Floating Buttons Tour (Correction 2)**: Added `data-tour="requests-floating-total"` to both inline and floating total value footers. Added `data-tour="requests-floating-toggle"` to the floating mode toggle button. New tour steps explain: (a) the total reflects the sum of values on the current page of filtered results, (b) the toggle switches between floating and inline summary modes.
- **Kebab Menu Tour (Correction 3)**: Added `data-tour="requests-card-kebab-menu"` to the kebab wrapper in `CarouselCard` (ActionCarouselWidget). New tour step explains contextual actions (Vis. Rápida, Duplicar). Step is skipped gracefully if no action cards are visible.
- **Step Reordering (Correction 4)**: Requests page tour reordered to: Action Carousel → Kebab Menu → Floating Total → Floating Toggle → Quick Filters → Advanced Filters → Table → Row Click/Workflow. Total: 8 steps (up from 5).
- **Graceful Missing Targets (Correction 5)**: Verified existing `filterActiveSteps()` logic correctly handles all conditional scenarios: RBAC-hidden menus, empty action queues, hidden floating buttons, and collapsed sidebar groups. No code changes needed.

## [2.131.0] - 2026-05-22

### Improved — Guided Tour UX: Scroll Fix, Module & Page Tour Expansion (DEC-132)
- **Scroll Alignment Fix**: Set Joyride `scrollOffset: 80` and `scrollDuration: 350` to compensate for the 64px sticky topbar. Added `scrollToFirstStep` prop. Implemented manual `scrollTargetIntoView` helper that runs on `STEP_BEFORE` events — uses `requestAnimationFrame` + delayed check to ensure targets are not hidden behind the header.
- **Header Offset Detection**: `HEADER_OFFSET_PX` constant reads CSS variable `--header-height` at module load, with a 16px breathing room, falling back to 80px if unavailable.
- **Module Tour Expansion (Compras & Logística)**: Tour expanded from 5 generic steps to 9 targeted steps covering: sidebar menu entry, cockpit overview, Pedidos menu, KPI cards, Pontos de Atenção, Ações Rápidas, Manual de Operação, Gestão de Cotações (buyer items), and Recebimento.
- **Page Tour Expansion (Requests)**: Tour expanded from 3 generic steps to 5 focused steps: Fila de Ação & Indicadores, Filtros Rápidos (tabs), Pesquisa & Filtros Avançados, Tabela de Pedidos, and Row click/Workflow explanation.
- **New data-tour Attributes**: Added `data-tour="purchasing-kpi-cards"`, `data-tour="purchasing-attention-points"`, `data-tour="purchasing-quick-actions"`, `data-tour="purchasing-operation-manual"` to `PurchasingLandingPage.tsx`. Added `data-tour="requests-filter-button"`, `data-tour="requests-table"` to `RequestsDashboard.tsx`.
- **Graceful Missing Targets**: All tour steps auto-filter via `filterActiveSteps` — RBAC-hidden elements are silently skipped.

## [2.130.0] - 2026-05-22

### Added — Guided Tour Evolution: Registry-Based Multi-Tour Architecture (DEC-132)
- **Tour Registry Architecture**: Introduced `guidedTourRegistry.ts` with `getToursForRoute()` to resolve portal, module, and page-level tours based on the current route.
- **Multi-Tour Type System**: Expanded `TourId` union to support `portal-main`, `module-purchasing-logistics`, `page-requests`, `page-buyer-items`, and `page-receiving-workspace`.
- **Module Tour — Compras & Logística**: 5 steps covering cockpit overview, sidebar sub-modules (Pedidos, Gestão de Cotações, Recebimento), and module workflow.
- **Page Tours**: 3 page-level tours with contextual steps for Requests Dashboard (header, action carousel, explorer, filter tabs), Buyer Items / Gestão de Cotações (header, search bar, items list), and Receiving Workspace (header, pending queue, completed section).
- **GuidedTourButton Dropdown**: Help button (❓) in Topbar now opens a dropdown menu showing up to 3 contextual tour options (Portal, Module, Page) based on the current route.
- **GuidedTourContextButton**: New inline button component placed in page headers for direct page-level tour launch.
- **Route-to-Module Resolution**: Routes `/purchasing`, `/requests`, `/buyer/items`, `/receiving/workspace` all resolve to the Compras & Logística module tour.
- **Separate Persistence**: Each tour has its own `guided-tour:{tourId}:v1:{userId}` localStorage key. Completing a page tour does not affect the module or portal tour status.
- **No-Steps Toast**: If a requested tour has no valid DOM targets (e.g., RBAC-hidden elements), a transient informational toast appears instead of crashing.
- **Backward Compatible**: Existing `portal-main` tour preserved — auto-shows on first access, separate persistence key, welcome modal behavior unchanged.
- **data-tour Attributes**: Added tour anchor attributes to Sidebar sub-items, PageHeader components, and key sections of PurchasingLandingPage, RequestsDashboard, BuyerItemsList, and ReceivingWorkspace.
- **PageHeader Enhancement**: `PageHeader` component now supports `data-tour` prop for direct tour targeting.
- Decision: DEC-132.

## [2.129.0] - 2026-05-22

### Added — Guided Tour / Onboarding (DEC-131)
- Guided onboarding tour using React Joyride v3 for first-time users.
- 16 tour steps covering Topbar, Search, Notifications, Profile, Help, Main Menu, Dashboard, Purchase Requests, Approvals, Compras & Logística, Finanças, Contratos, T.I., R.H., Configurações, Administração.
- Each sidebar module (T.I., Configurações, Administração, Contratos) has its own dedicated tour step with distinct data-tour attribute and Portuguese explanatory content.
- RBAC-aware step filtering: modules not visible to the user are silently skipped via DOM presence check.
- Welcome modal on first login with "Iniciar Tour" / "Agora Não" options.
- Layout readiness via DOM polling (not fixed delay).
- Persistence: `guided-tour:portal-main:v1:{userId}` in localStorage.
- Permanent help button (❓) in Topbar for manual restart.
- Decision: DEC-131.

## [2.128.0] - 2026-05-22

### Changed — Remove LOCAL_OCR Provider, Consolidate OpenAI (DEC-130)
- LOCAL_OCR (PaddleOCR/Tesseract) provider removed from the system. OpenAI is now the sole active document extraction provider.
- Deleted: `LocalOcrExtractionProvider.cs`, legacy `OcrService.cs`, legacy `IOcrService.cs`.
- Configuration: `appsettings.json` default changed to `OPENAI`, `LocalOcr` section removed, OpenAI enabled by default.
- Backend: LOCAL_OCR fallback guard — if database still has `DefaultProvider = "LOCAL_OCR"`, system warns and falls back to OPENAI.
- Database: `LocalOcr*` columns marked `[Obsolete]`, retained for EF Core compatibility, cleared on save.
- Frontend: LOCAL_OCR option removed from provider dropdown, Local OCR settings section removed, diagnostics cards updated.
- Decision: DEC-130.

## [2.127.0] - 2026-05-21

### Changed — Dashboard Redesign: Operational Cockpit (DEC-129)
- Dashboard transformed from generic overview page to operational management cockpit.
- New `GET /api/v1/requests/cockpit-summary` endpoint (CockpitSummaryDto) — scope-aware, single call for all dashboard data.
- 7-section frontend layout: My Work Queue (5 KPI cards), Pipeline Vision (10 status cards), Quick Actions (6 role-aware), Attention Alerts (severity-sorted), Process Bottlenecks (visual distribution bars + age badges), Financial Summary (multi-currency aware), Workflow Guide (collapsible at bottom).
- New components: MyWorkQueue.tsx, AlertList.tsx, BottleneckTable.tsx, FinancialSummary.tsx.
- QuickActions rewritten with role-based visibility. Fixed Novo Pedido route from `/requests/create` to `/requests/new`.
- Decision: DEC-129.

## [2.126.0] - 2026-05-21

### Fixed — HR Attendance: "Falta" Status Despite Valid Punches (DEC-128)
- PunchWithoutPeriod detection: When Portal-interpreted punches show valid Entry + Exit but Innux has no processed work period, status changes from "Falta" to "Verificar" with estimated Portal hours tooltip.
- GetWorkedHoursAsync fix: Excluded `AlteracoesPeriodos` rows with absence codes (`IDCodigoAusencia IS NULL`) from worked hours calculation. Absence periods were incorrectly counted as basic worked time.
- Frontend: "Verificar" label with AlertCircle icon, pulse animation, and print-safe CSS.
- Diagnostic scan May 2026: 448 PunchWithoutPeriod day-records across 95 employees.
- Decision: DEC-128.

## [2.125.0] - 2026-05-21

### Fixed — HR Attendance: Punch Classification in Monthly Report
- Unified `ApplyPortalPunchInterpretation` shared method extracted in `InnuxAttendanceService.cs` — applied to both `GetRawPunchesAsync` (bulk/monthly report) and `GetPunchesAsync` (day-detail).
- Root cause: `GetRawPunchesAsync` did not apply the "Double Code 17" interpretation, causing exit punches to appear under ENT.2 instead of SAÍ.1.
- Added `HasDirectionWarning` and `DirectionWarningMessage` to `AttendanceDailyRecordDto` for audit visibility.
- Frontend: Compass icon (🧭) indicator for days with Portal-interpreted punch direction. Distinct from anomaly triangle and Portal "P" badge.
- Print-compatible CSS styling for direction warning indicator.
- Decision: DEC-127.

## [2.124.0] - 2026-05-21

### Changed — I.T Equipment Documents: DOCX → PDF Migration with Branding
- Official I.T Equipment documents (Termo de Responsabilidade, Termo de Devolução) now generated as branded PDF files using PdfSharpCore (MIT license).
- New `ITEquipmentPdfService` — branded A4 PDF with logo header, two-column data table, equipment usage policy text, signature lines, and automatic page breaks.
- Policy text extracted to `data/templates/it-equipment/policy-text.txt` (required for Assignment Agreements, optional for Return).
- Logo loaded from `data/templates/branding/portal-logo.png` — graceful fallback to text-only header if missing.
- Email attachment and download endpoint MIME type auto-detection (PDF/DOCX).
- Legacy DOCX documents remain downloadable. `ITEquipmentAgreementService` marked `[Obsolete]`.
- Decision: DEC-126.

## [2.123.0] - 2026-05-20

### Added — I.T Equipment Inventory Management Module
- **New Module**: Complete I.T equipment inventory management system for tracking, assigning, and auditing all company IT assets.
- **Domain Entities**: `ITEquipment`, `ITEquipmentAssignment`, `ITEquipmentMovementLog`, `ITEquipmentAcquisition`, `ITEquipmentDocument` with full lifecycle support.
- **Role-Based Access**: New `IT` role restricts module access to IT staff and System Administrators. Seed migration auto-creates the role.
- **Equipment Lifecycle**: Full status machine (AVAILABLE → IN_USE → RETURNED / IN_REPAIR / LOST / RESERVED / RETIRED / DAMAGED / UNKNOWN) with movement log audit trail.
- **CSV Import**: Multipart upload endpoint (`POST /api/it/equipment/import`) with flexible column mapping, duplicate detection (Asset Tag exact + Hostname conditional), and detailed error/skip reporting. Legacy records imported with UNKNOWN status when source status is empty.
- **Equipment CRUD**: Create, update, list (with search, multi-filter, sorting, pagination), and detail endpoints.
- **Action Endpoints**: Assign, Return (with condition: OK/DAMAGED/NEEDS_REPAIR), Send to Repair, Return from Repair, Mark Lost, Reserve, Retire — each with movement log and assignment status updates.
- **Document Management**: Upload, download, list, delete equipment documents (invoices, warranties, POs) via `ITEquipmentDocumentsController`.
- **Acquisition Tracking**: Optional 1:1 acquisition record per equipment with purchase order, invoice, payment, and warranty fields. Future integration fields (Primavera, Portal Request) are nullable.
- **Frontend**: Full React SPA module at `/it/equipment` with KPI summary cards, sortable/filterable table, search, quick-view drawer (4 tabs: Info, Assignments, Movements, Documents), create/edit form modal, and dedicated action modals for each lifecycle transition.
- **Import UI**: Upload modal with drag-and-drop, validation result preview (created/skipped/errors/duplicate hostnames).
- **Navigation**: New "T.I" sidebar group with Monitor icon, visible only to IT/Admin roles.
- **Migration**: `AddITEquipmentModule` — creates 5 tables with unique indexes, FK constraints (Restrict for Documents to avoid cascade cycles), and IT role seed.

## [2.122.0] - 2026-05-20

### Fixed — HR Monthly Attendance Report: Saldo (Balance) Always 00:00
- **Root Cause**: The `Saldo` (balance) column was sourced from Innux's `BalanceMinutes` field, which is a datetime-as-duration column. `InnuxTimeHelper.ToMinutes()` returns 0 for any value ≤ the 1900-01-01 base date, silently truncating all negative balances to zero. This was a known limitation documented in the codebase but never addressed in the report output.
- **Fix — Portal-Computed Balance (DEC-124)**: The monthly attendance report now computes `Saldo` independently using `H.Totais - H.Básicas` instead of relying on the unreliable Innux Saldo column.
  - **H.Básicas** = Planned/scheduled working hours (`ExpectedMinutes`) — the expected workload for the day, shown even if the employee did not work.
  - **H.Falta** = Unjustified absence hours (unchanged — sourced from Innux `Falta` column).
  - **H.Totais** = Positive counted hours: real worked hours + justified/approved absence hours. Unjustified absence hours are NOT counted as positive time.
  - **Saldo** = `H.Totais - H.Básicas`. Negative on unjustified absence days, zero on fully worked or justified/exempt days, positive on overtime days.
- **Exempt Categories**: Vacation, Holiday, and Justified Absence days are balance-neutral (`H.Totais = H.Básicas`, `Saldo = 00:00`). Rest days remain all-zero.
- **Visual Indicators**: Negative balance values render in **red/bold**, positive in **green**. Zero balance remains neutral. Styling applies to daily records, monthly summaries, employee grand totals, and department totals — both on screen and in print/PDF.
- **Monthly/Grand Totals**: Now accumulate the corrected daily balance values, showing the real positive/negative time balance across the period.
- **AbsenceMinutes Accumulation**: Monthly and grand total DTOs now accumulate `AbsenceMinutes` (previously the field existed but was never summed).
- **No Data Changes**: Read-only computation change. No writes to Innux, Primavera, or Portal databases. Only affects the monthly report output.

## [2.121.0] - 2026-05-20

### Added — HR Monthly Attendance Report: Consolidated & 30-Day Activity Filter
- **Consolidated Report ("Todos os Departamentos")**: Added a special option to the department selector to generate a single consolidated PDF report for all departments at once. The report groups employees by department, sorting departments and employees alphabetically.
- **30-Day Activity Filter**: Injected the same 30-day "real terminal punch" activity filter into the Monthly Report (for both single and consolidated flows). Employees without biometric punches in the last 30 days are automatically excluded to prevent ghost employees from polluting the report.
- **Segmented Filter UI**: Added a three-button segmented control (Com ponto recente, Sem ponto há +30 dias, Todos) to the Monthly Report UI.
- **Print Notices**: Added visual and print-only notices explaining the default filter behavior in the PDF header.

## [2.120.1] - 2026-05-20

### Fixed — HR Attendance: 30-Day Activity Filter Using Wrong Data Source
- **Root Cause**: `GetLastAttendanceDatesAsync` queried `MAX(Data) FROM dbo.Alteracoes`, which includes pre-generated scheduled records (rest days, planned shifts). Innux auto-generates `Alteracoes` rows for employees with active work schedules, even after they leave the company. This caused `MAX(Data)` to return recent or future dates for inactive employees, making them incorrectly appear in the "Com ponto recente" default view.
- **Fix**: Changed the query to use `dbo.TerminaisMarcacoes` (real terminal clock punches) instead. This table only contains actual physical clock-in/clock-out events, providing an accurate signal for real employee attendance activity.
- **Employee Affected**: ABENECO MANUEL PEDRO (and similar former employees still scheduled in Innux) will now correctly appear only under "Sem ponto há +30 dias" or "Todos".
- **Diagnostic Logging**: Added temporary classification logging in `HRAttendanceController.GetCalendar` to trace employee activity filter decisions (ABENECO-specific debug logging included).
- **No Data Changes**: Read-only fix. No writes to Innux, Primavera, or Portal employee records.

## [2.120.0] - 2026-05-20

### Added — HR Attendance: 30-Day Activity Filter
- **Inactive Employee Hiding**: Employees without any attendance/punch data for more than 30 days are now hidden by default from the HR Attendance Calendar. This prevents former employees (still active in Primavera) from polluting the attendance grid.
- **Backend Activity Detection**: New `GetLastAttendanceDatesAsync` method in `InnuxAttendanceService` queries `MAX(Data) FROM dbo.TerminaisMarcacoes` (real terminal punches) grouped by employee ID. The 30-day cutoff is calculated from today's date (not the viewed calendar month).
- **`attendanceActivity` API Parameter**: `GET /api/hr/attendance/calendar` accepts `attendanceActivity` (`active`|`noRecent`|`all`). Default: `active`. Backend filters employee IDs before querying the daily attendance grid (performance optimization).
- **Activity Summary**: API response includes `activitySummary` with `activeCount`, `noRecentCount`, and `totalCount`. Employees with `lastAttendanceDate == null` are categorized as `noRecent`.
- **`lastAttendanceDate` Field**: Each employee object in the response now includes `lastAttendanceDate` (nullable ISO string).
- **Segmented Filter UI**: Three-button segmented control above the existing filter bar: "Com ponto recente" (default), "Sem ponto há +30 dias", "Todos". Each button shows the employee count badge.
- **Explanatory Hint**: When in default "active" view, an informational message explains why employees are hidden: "Funcionários sem ponto há mais de 30 dias são ocultados por padrão, pois podem não ter sido desativados no Primavera."
- **"Último ponto" Display**: In "noRecent" view, each employee row shows their last attendance date in amber text, or "Não encontrado" if null.
- **Non-Destructive**: This is purely a UI visibility filter. No employee status changes, no writes to Primavera or Innux, no HR mapping changes.

## [2.119.1] - 2026-05-20

### Fixed — HR Directory Sync: Missing EF Core Migration
- **Root Cause**: The v2.119.0 implementation added the `SuggestedPlantSource`, `SuggestedPlantReason`, `SuggestedPlantConfidence`, and `SuggestedPlantResolvedAtUtc` fields to the `HREmployee` domain entity, but the corresponding EF Core migration was not generated and applied to the database. This caused a runtime `SqlException: Invalid column name` when triggering the HR Directory synchronization.
- **Fix**: Created and applied the missing EF Core migration (`20260520092813_AddPlantSuggestionFields.cs`), successfully adding the nullable columns to the `HREmployees` table and restoring sync functionality.

## [2.119.0] - 2026-05-20

### Added — HR Directory: Primavera Plant Suggestion & Advanced Filters
- **Primavera Plant Suggestion Service**: Read-only advisory service that queries Primavera databases (ALPLASOPRO / ALPLAPLASTICO) to suggest plant mappings for unmapped HR employees. ALPLASOPRO → Viana 3 (High confidence), ALPLAPLASTICO → Viana 1/2 (Ambiguous). No Primavera writes.
- **Suggestion Domain Fields**: `SuggestedPlantSource`, `SuggestedPlantReason`, `SuggestedPlantConfidence`, `SuggestedPlantResolvedAtUtc` on `HREmployee` entity.
- **Resolve Suggestions Endpoint**: `POST /api/hr/leave/employees/resolve-suggestions` — batch Primavera lookup.
- **Advanced Filtering**: `mappingStatus`, `missingField`, `hasSuggestion`, `plantId`, `departmentMasterId`, `innuxDepartment` parameters on `GET /api/hr/leave/employees`.
- **KPI Summary**: Backend returns mapping status counts. Frontend renders interactive summary cards.
- **Frontend**: Collapsible filter bar with chips, suggestion hints on unmapped rows, accept/map workflow, integrated into sync action.

## [2.118.2] - 2026-05-19

### Fixed — HR Monthly Attendance Report Print Document Layout
- **Print Document Layout**: Replaced screen-capture-style print output with a proper official document layout. The printed report now starts with a dedicated document header ("ALPLA Angola | Portal Gerencial / Relatório Mensal de Presenças") and immediately shows employee data — no wasted pages.
- **HR Module Chrome Hidden**: Added `screen-only` class to `HRLandingPage.tsx` PageHeader and tab navigation, ensuring "RECURSOS HUMANOS" title and navigation tabs (Visão Geral, Férias, Presenças, etc.) are completely hidden during print.
- **Scoped Print CSS**: Rewrote the `@media print` block in `hr-attendance-monthly-report.css` with 300+ lines of report-specific print rules covering document header, compact tables, employee sections with `break-inside: avoid`, repeating table headers, and A4 landscape with 8mm margins.
- **Global Print CSS Simplified**: Reduced `globals.css` print rules to a minimal AppShell override (hide sidebar/topbar, flatten grid, visibility utilities), avoiding over-specific selectors that could affect other pages.

## [2.118.1] - 2026-05-19

### Fixed — HR Monthly Attendance Report Print Blank Page
- **Global Print CSS**: Added `@media print` rules to `globals.css` that hide the AppShell chrome (Topbar, Sidebar) and flatten the grid layout. The report content now fills the full printed page instead of being crushed by the fixed-width sidebar grid.
- **AppShell CSS Classes**: Added semantic class names (`app-shell`, `app-shell-grid`, `app-shell-sidebar`, `app-shell-main`) to `AppShell.tsx` layout elements, enabling CSS-based print targeting alongside existing inline styles.
- **TypeScript Fix**: Removed unused `React` import in `HRAttendanceMonthlyReport.tsx` (TS6133), ensuring a clean `npx tsc --noEmit` pass.

## [2.118.0] - 2026-05-19

### Added — Catalog Sync Conflict Resolution
- **Backend**: New `POST /api/v1/sync/catalog/resolve-conflict` endpoint supporting 4 resolution strategies: `UpdatePortal` (field-level selection for Description, Category, Unit, PrimaveraCode), `ConfirmAssociation`, `CreateNew` (canonical ITM-NNNNN), and `AssociateManually`.
- **Data Integrity**: Strict PrimaveraCode validation (rejects null, empty, whitespace, "0", and all-zeros). Duplicate PrimaveraCode-to-Portal item association prevention.
- **Frontend UI**: New `CatalogConflictResolverModal` with side-by-side Primavera vs Portal comparison, field-level checkbox selection for UpdatePortal, manual search for AssociateManually, and preview-before-confirm summary.
- **Integration**: "Resolver" action button in the catalog sync table for conflict-status rows. Immediate UI refresh on successful resolution.
- **Audit Trail**: All resolution actions logged via `AdminLogWriter` with action code `SYNC_CATALOG_RESOLVE_CONFLICT`.

## [2.117.3] - 2026-05-19

### Fixed — EF Core Warning Cleanup
- **Database Schema**: Explicitly configured `HasPrecision(18, 2)` for `AnnualBudget.TotalAmount` to resolve EF Core `Validation[30000]` silent truncation warning.

## [2.117.2] - 2026-05-19

### Fixed — Backend Warning Cleanup
- **Backend Refactor**: Resolved `CS1998` compiler warning in `MonthlyChangesOrchestrator` by removing unnecessary `async` modifier from `LogEventAsync` and returning `Task.CompletedTask`.

## [2.117.1] - 2026-05-18

### Fixed — HR Monthly Attendance Reporting Corrections
- **Backend Refactor**: Corrected `CS0103` build error by referencing `MapDirectionLabel` instead of `ClassifyDirection` in `InnuxAttendanceService`.
- **Access Control**: Hardened `/api/hr/attendance/reports/monthly-by-department` with `[Authorize(Roles = "System Administrator,HR")]`.
- **Punch Pairing**: Refactored logic to be direction-aware, prioritizing `DirectionLabel` over positional indices to handle anomalous codes `17` and `18`.
- **DTO Realignment**: Fixed frontend DTO property mapping to strictly match backend JSON keys (`employeeCode`, `employeeName`, etc.) and updated `EmployeeId` typing.
- **Frontend Refactor**: Replaced `<select>` with `DepartmentMasterAutocomplete` for scalable department picking, introduced an explicit read-only disclaimer, and improved warning UI.
- **Print Optimization**: Rewrote `hr-attendance-monthly-report.css` to force A4 landscape density, include dark-themed department grand totals, and explicitly show the Portal-Interpreted badge upon PDF print.

## [2.117.0] - 2026-05-18

### Added — HR Monthly Attendance Reporting
- **Backend API**: `GetMonthlyByDepartmentReport` generating aggregated, grouped daily attendance data from `TerminaisMarcacoes` and `Alteracoes`.
- **Frontend UI**: `HRAttendanceMonthlyReport` with print-ready styling matching Innux "Resultados mensais por departamento" layout.
- **Controls**: Department selection using `DepartmentMasterAutocomplete`, 62-day interval restriction, and "all/business/weekends" day filters.
- **Access Control**: Limited to `System Administrator` and `HR` roles, integrating safely into the HR workspace.

## [2.116.0] - 2026-05-15

### Added — Proforma Deadline Expiration Alerts
- **Background Service**: `ProformaDeadlineAlertService` scans PAYMENT requests in approval stages daily and alerts approvers when Proforma deadlines approach or expire.
- **Alert Levels**: `WARNING_3D`, `WARNING_1D`, `CRITICAL_0D`, `EXPIRED` — configurable thresholds.
- **Deduplication**: Composite unique index `(RequestId, AlertLevel, RecipientUserId)` — each alert sent at most once per recipient.
- **Dual-Channel**: Branded Portuguese email + in-app bell notification (`PROFORMA_DEADLINE` category).
- **Approver Resolution**: Mirrors `WorkflowNotificationOrchestrator` patterns (explicit + department fan-out fallback).
- **Audit Trail**: `ProformaDeadlineAlerts` table with delivery status tracking. Admin log per cycle.
- **Configuration**: `AppConfig:ProformaDeadlineAlerts` in `appsettings.json`.
- **Migration**: `AddProformaDeadlineAlerts`.
- **Decision**: DEC-123.

## [2.115.2] - 2026-05-15

### Fixed — OCR Quotation Total Missing VAT
- **Root Cause**: `draft.totalAmount` was calculated before Global VAT Inference applied `ivaRateId` to items, so the displayed total excluded VAT.
- **Fix**: Added post-inference recalculation of item totals and draft total when `globalVatInferred` is true.
- **Consistency**: Replaced inline `reduce` in `handleRemoveQuotationItem` with `calculateDraftTotal()`.

## [2.115.1] - 2026-05-15

### Fixed — Area Approval Rejection Blocked by Allocation Validation
- **Root Cause**: Frontend sent `itemAssignments` with `null` int values during rejection, causing ASP.NET ModelState deserialization failure before controller logic ran.
- **Frontend Fix**: Stopped sending `itemAssignments` on `REJECT` and `REQUEST_ADJUSTMENT` actions. Only sent on `APPROVE`.
- **Backend Hardening**: Made `ItemApprovalAssignmentDto.PlantId` and `CostCenterId` nullable. Updated `ProcessAreaApproval` validation to use nullable-safe comparisons.
- **Error Handling**: Improved `ApprovalDetailPanel` error catch to extract detailed field-level validation messages from ASP.NET `ProblemDetails`.

## [2.115.0] - 2026-05-15

### Added — OCR Global VAT Inference
- **Global VAT Detection**: When a supplier document specifies VAT only at the summary level (Subtotal + IVA + Total), the OCR pipeline now automatically infers the implied VAT rate and applies it to all items.
- **Inference Algorithm**: `(GrandTotal - Subtotal) / Subtotal` → match against active `ivaRates` with ±0.30pp tolerance → validate total within 2% before applying.
- **Priority Rule**: Explicit item-level VAT always takes priority. Inference only triggers when all items have uncertain/missing VAT.
- **Auditability**: New `globalVatInferred`, `inferredVatRatePercent` (draft-level), and `ivaGlobalInferred` (item-level) flags for traceability.
- **UI**: Green success banner replaces "IVA não identificado" warning when inference succeeds. Manual override preserved.

## [2.114.0] - 2026-05-14

### Feature — Approvals & Budget Insights
- **Budget Health Analytics**: Integrated budget utilization metrics into the approval flow using `ApprovalIntelligenceService`.
- **Decision Support UI**: Added `DecisionInsightsPanel` and `DecisionQuotationCard` to provide comprehensive budget context during purchase request approvals.

### Bug Fix — Backend Routing & Notifications
- **Missing Department Context**: Fixed `FinanceController` and `RequestsController` missing `DepartmentId` in status emits, enabling correct fan-out routing to department roles.
- **Resubmit Event Mapping**: Fixed `RESUBMIT` event mapping in `RequestsController` to properly route to the Final Approver.
- **Workflow Orchestration**: Expanded `FinalApproved` logic to include both Requester and Buyer.

### Chore — Code Quality & Linting
- **TypeScript Cleanup**: Resolved remaining `TS6133` unused variable and import warnings across the frontend codebase, ensuring a clean `npm run build`.

## [2.113.0] - 2026-05-14

### UI/UX Fix
- **Dark Mode Contrast**: Improved contrast in `UserDropdown` and `NotificationBell` for dark mode visibility. Added `--color-status-red-surface` semantic token.

## [2.112.0] - 2026-05-14

### Bug Fix — HR Team Calendar Scope for Local Manager
- **Root Cause**: `GetScopedEmployeesQuery()` Local Manager branch filtered by `PortalDepartmentId` (which was NULL for all mapped employees), ignoring the `ManagerUserId` relationship. This caused zero employees to appear in the Team Calendar, Dashboard, and Leave views.
- **Fix**: Added `e.ManagerUserId == userId` as an OR condition in all Local Manager scope branches — consistent with the existing Department Manager scope pattern. Directly assigned employees (Responsável/Chefe) are now always visible regardless of `PortalDepartmentId` status.
- **Frontend UX**: Added info banner distinguishing "no employees in scope" from "no leave records for the team this period." Improved empty-state messaging with actionable guidance.
- **Security**: No broadening of access. `ManagerUserId` is an admin-assigned field; only HR Admins can set it. Write operations use the same scope, maintaining intended access boundaries.

## [2.110.0] - 2026-05-14

### UX Refinement
- **HR Navigation Split**: Non-HR users (Local Manager, Department Manager, Viewer/Management) no longer see the sidebar group labeled "R.H." — they now see "Gestão da Equipa" with only team-level children (Calendário da Equipa, Férias e Ausências). The full "R.H." group with all children (including admin screens) is visible only to HR and System Administrator roles.
- **HR Page Title**: When a non-HR user accesses `/hr/calendar` or `/hr/leave`, the page header now shows "Gestão da Equipa" instead of "Recursos Humanos", with a team-appropriate subtitle and icon.
- **HR Tab Filtering**: Non-admin users inside the HR landing page now only see 3 tabs (Visão Geral, Férias e Ausências, Calendário da Equipa). Previously, Local Managers saw all tabs including Presenças, Escalas, Directório, and Gestão de Crachás even though route guards blocked access.

### Changed
- **Navigation Config**: `getNavigationConfig()` now accepts `hasHRAdminAccess` as a 3rd parameter. Added `isHrAdmin` and `isTeamModule` flags to `NavItem` interface.
- **GlobalSearch**: Passes `hasHRAdminAccess` to navigation config — team-level users only find team features in global search results.

## [2.109.0] - 2026-05-14

### Security Fix
- **HR Module Access Control**: Local Manager and Area Approver roles no longer grant access to HR administration screens (Funcionários/badges, Layouts, Histórico de Impressão, Attendance, Schedules, Directory, Monthly Changes). Only `HR` and `System Administrator` roles can access admin screens. Team-level features (Visão Geral, Calendário da Equipa, Férias e Ausências) remain accessible to Local Managers, Department Managers, and Viewer/Management.

### Changed
- **AuthContext**: Split `hasHRModuleAccess` into two tiers: `hasHRModuleAccess` (team features) and `hasHRAdminAccess` (administration — HR/Admin only).
- **Route Guard**: Replaced `HRAdvancedRoute` (which only blocked Viewer/Management) with `HRAdminRoute` using `hasHRAdminAccess`.
- **Sidebar Navigation**: Admin HR children (`rh-badges-employees`, `rh-badges-layouts`, `rh-badges-history`) now require `[HR, System Administrator]` roles — removed `LOCAL_MANAGER` from allowed roles.

## [2.108.0] - 2026-05-14

### Fixed
- **Request Number Column Sort**: Sorting by "Número" column now uses chronological date order (`CreatedAtUtc.Date`) with request number as tiebreaker, instead of treating `REQ-DD/MM/YYYY-NNN` as a plain string (which sorted `DD` lexicographically, breaking date chronology).

### Added
- **Missing Column Sort Cases**: Backend now handles all frontend column sort keys (`statusCode`, `requestTypeCode`, `companyName`, `needByDateUtc`, `estimatedTotalAmount`) — previously these silently fell through to `createdAtUtc` default.

## [2.107.0] - 2026-05-14

### Added
- **Persistent Table Preferences**: Reusable `useTablePreferences` hook persists filter, sort, and view state to `localStorage` scoped by user ID. Integrated into RequestsDashboard, ApprovalCenter, FinancePaymentsList, and BuyerItemsList. URL-driven screens use URL-sync pattern preserving deep-linking.

## [2.106.0] - 2026-05-14

### Fixed
- **Purchase Request Notification Priority Fixes**: Remediated 4 high-confidence notification routing issues identified in the Purchase Request notifications audit (`docs/PURCHASE_REQUEST_NOTIFICATIONS_AUDIT.md`).
  - **Finance Events — Missing DepartmentId**: `PAYMENT_SCHEDULED` and `PAYMENT_COMPLETED` events in `FinanceController` now correctly populate `DepartmentId` from the request entity, enabling area approver fan-out via `HandlePaymentFanningOverridesAsync`.
  - **Quotation Events — Missing DepartmentId**: `QUOTATION_COMPLETED` event in `RequestsController` now includes `DepartmentId`, ensuring correct department-scoped area approver resolution.
  - **FINAL_APPROVED Recipients**: Updated `ResolveRecipientsAsync` in `WorkflowNotificationOrchestrator` to include the Requester and the assigned Buyer in `FINAL_APPROVED` notifications, ensuring all operational stakeholders are notified when a request is ready for P.O. generation.
  - **RESUBMIT Routing Fix**: Corrected `ResolveEventCode` mapping for `RESUBMIT` from `WAITING_FINAL_APPROVAL` — was incorrectly mapped to `REQUEST_SUBMITTED` (triggering area approver notifications), now correctly maps to `AREA_APPROVED` (triggering final approver notification).

## [2.104.0] - 2026-05-14

### Added
- **Buyer Requested Items Section ("Itens Solicitados no Pedido")**: New read-only section in the Buyer Quotation Management expanded view displaying all items from the original purchase request. Includes table with line number, description, quantity, unit, estimated prices, priority badges, and catalog/manual type detection via `ItemCatalogId`. Backend: added `ItemCatalogId` to `LineItemDetailsDto` and `LineItemsController` projection.

## [2.102.0] - 2026-05-14

### Security
- **Route-Level Access Control Hardening**: System-wide access-control audit conducted to eliminate gaps where UI-hidden resources remained accessible via direct URL manipulation.
  - Implemented `HRAdvancedRoute` to restrict HR diagnostic endpoints (attendance, schedules, directory, badges, monthly changes) strictly to users with `SYSTEM_ADMINISTRATOR` or `HR` roles, blocking `VIEWER/MANAGEMENT`.
  - Added `AdminRoute` guards to `/approvals` (AREA_APPROVER, FINAL_APPROVER), `/purchasing` and `/buyer/items` (BUYER), `/receiving` (RECEIVING, LOCAL_MANAGER), `/finance` (FINANCE), and `/contracts` (CONTRACTS, FINANCE).
- **Access Control Documentation**: Created `docs/ACCESS_CONTROL_AUDIT.md` mapping the security matrix and documenting required backend verification steps.

## [2.100.0] - 2026-05-13

### Changed
- **HR Command Center — Scope-Enforced KPI Cards**: Dashboard KPI cards (Ausentes Hoje, Em Férias, Aguardando Análise, Efetivo Ativo Mapeado) now respect the logged-in user's role and scope.
  - **System Administrator**: Broad/global data with full action section and sync badge.
  - **HR**: Data scoped by plant/department assignments. Full action section.
  - **Local Manager / Department Manager**: Data scoped to managed employees. Action section hidden.
  - **Viewer / Management (linked with department)**: Data scoped to the user's own department/team. Informational only — no admin actions.
  - **Viewer / Management (no linked employee)**: Safe empty values with clear "not linked" message.

### Added
- **`GetTeamScopedEmployeesQuery()`**: Shared team-scope method (refactored from `GetCalendarScopedEmployeesQuery`). Used by both calendar and dashboard endpoints. Privileged roles delegate to `GetScopedEmployeesQuery()`; Viewer/Management resolves to department-level via linked HREmployee.
- **Dashboard scope metadata**: `scopeType` and `scopeDescription` added to the dashboard API response. Frontend displays contextual Portuguese scope description.
- **Overview tab for Viewer/Management**: `VIEWER_ONLY_TABS` now includes `overview`, `calendar`, and `leave`.
- **Role-aware KPI click targets**: Viewer/Management clicking "Efetivo Ativo Mapeado" navigates to `/hr/calendar` instead of restricted `/hr/badges/employees`.

### Security
- `GetScopedEmployeesQuery()` unchanged — leave create/list/approve/reject/cancel remain self-only for Viewer/Management.
- Dashboard KPIs remain aggregate-only — no employee names, absence reasons, medical details, or PII exposed.
- "Ação Necessária" section (missing mappings, stale requests, sync issues) restricted to HR/System Administrator.
- Sync badge hidden for non-admin users.

## [2.99.9] - 2026-05-13

### Added
- **HR Leave Notification System**: In-app notification bar alerts for HR leave/absence request lifecycle events.
  - **Submit notification**: When a leave record transitions to SUBMITTED, the resolved approver (via `HREmployee.ManagerUserId` → `Department.ResponsibleUserId` fallback) receives a warning notification.
  - **Approve notification**: When a leave record is APPROVED, the request creator receives a success notification.
  - **Reject notification**: When a leave record is REJECTED, the request creator receives an error notification.
  - **Cancel notification**: When a leave record is CANCELLED by someone other than the request creator, the creator receives a warning notification. Self-cancellation does not generate notifications.
  - **Dedup-safe**: Uses `LeaveStatusHistory.Id` as `EventCorrelationId` with `CreateNotificationWithDedupAsync` to prevent duplicate notifications on retries or race conditions.
  - **Non-blocking**: Notification failures are caught and logged as warnings; they never corrupt the leave workflow transaction.
  - **Privacy**: Notifications contain only employee name, leave type, and date range. No notes, medical details, rejection reasons, or approval comments are exposed.
  - **New category**: `NotificationCategories.HRLeave = "HR_LEAVE"` separates HR leave notifications from general categories.
  - **No email**: In-app only. Email notifications deferred to future evaluation.
  - **No frontend changes**: Existing `NotificationBell.tsx` automatically renders the new notifications with click-to-navigate to `/hr/leave`.

## [2.99.8] - 2026-05-13

### Changed
- **HR Calendar — Department Visibility for Viewer / Management**: Broadened calendar scope from self-only to department-level for Viewer / Management users. The user's linked HREmployee → PortalDepartmentId determines which department's employees are visible. Leave management remains self-only.

### Added
- **`GetCalendarScopedEmployeesQuery()` in HRLeaveController**: Calendar-specific scope method. Privileged roles delegate to the unchanged `GetScopedEmployeesQuery()`. Viewer / Management users see active employees from their linked employee's department. Falls back to self-only if no department, or empty if unlinked.
- **`scopeType = "team"`**: New scope type returned by the calendar endpoint for Viewer / Management with department access. Frontend displays contextual header and informational note.
- **Frontend `HRTeamCalendar.tsx`**: Added `"team"` scope handling with appropriate Portuguese text for title, subtitle, and scope description.

### Security
- `GetScopedEmployeesQuery()` unchanged — leave create/list/approve/reject/cancel remain self-only for Viewer / Management.
- Calendar projection confirmed: no notes, reasons, attachments, medical details, or approval comments exposed.
- Only active employees returned (`e.IsActive == true`).

## [2.99.7] - 2026-05-13

### Changed
- **HR Sidebar — "Férias e Ausências" Visible to All HR Roles**: Removed role restriction from the sidebar item. Viewer / Management self-service users now see "Férias e Ausências" alongside "Calendário da Equipa". Backend scope enforcement remains the data access boundary.
- **HR Tabs — Self-Service Access Extended**: `VIEWER_ONLY_TABS` in `HRLandingPage.tsx` now includes `calendar` and `leave`. Viewer / Management users see both tabs; all other HR features remain hidden.

### Added
- **Self-Service Leave UI in HRLeaveList**: Role-aware `isSelfServiceOnly` mode for Viewer / Management users:
  - Auto-resolves the user's own HREmployee via scoped backend API on page load.
  - Drawer shows read-only employee display (no `EmployeeAutocomplete` selector).
  - Helper text: "Esta solicitação será registada automaticamente em seu nome."
  - Unlinked user warning: "A sua conta não está vinculada a um registo de funcionário. Contacte o RH." with disabled creation button.
  - Approve/Reject action buttons hidden for self-service users.
  - Cancel button restricted to DRAFT and SUBMITTED statuses (not APPROVED) for self-service.
  - Subtitle changes to "Visualize e solicite as suas férias e ausências."
- **No backend changes**. Backend scope enforcement via `GetScopedEmployeesQuery()` and `IsAdminOrHR` guards were verified as already secure.

## [2.99.6] - 2026-05-13

### Changed
- **HR Sidebar — Role-Aware Filtering**: Viewer / Management users now see only "Calendário da Equipa" in the sidebar R.H. group. Admin-level HR links restricted to HR, System Administrator, and Local Manager roles. New `rh-calendar` sidebar item added for all HR-accessing roles.

## [2.99.5] - 2026-05-13

### Fixed
- **Self-Calendar Mapping — Sync-Safety**: `HREmployeeSyncService.cs` preserves manually-linked corporate emails when Innux provides NULL/empty. Prevents sync from erasing Portal User ↔ HREmployee email mapping.
- **Self-Calendar Empty-State Message**: `HRTeamCalendar.tsx` and `HRAttendanceCalendar.tsx` display an actionable message guiding users to contact HR for user-employee linking when no matching HREmployee is found.

### Data
- **Abel Domingos (EmployeeCode: 21000184)**: Targeted data correction — set `HREmployee.Email = 'abel.domingos@alpla.com'` for self-service test user.

## [2.99.4] - 2026-05-13

### Fixed
- **HR Default Route — Viewer / Management Redirect**: `/hr` index route now redirects Viewer/Management users to `/hr/calendar` (self-calendar) instead of `/hr/overview`. Other HR roles retain the `/hr/overview` default. New `HRIndexRedirect` component in `App.tsx`. No backend changes. Sidebar role-filtering remains a future UX improvement.

## [2.99.3] - 2026-05-13

### Changed
- **HR Module Access — Frontend Route Guard Alignment**: Expanded the frontend `HRRoute` guard to match the backend's `HasHRModuleAccess()` scope, allowing `Local Manager` and `Viewer / Management` users to access `/hr` routes.
  - `Local Manager`: Can now access the HR workspace and Team Calendar through the UI. Sees employees within their assigned plant/department scope (enforced by backend).
  - `Viewer / Management`: Can now access the HR workspace but sees only the "Calendário da Equipa" tab (self-calendar). Other HR tabs are hidden. Backend returns only the user's own HREmployee record via email match.
  - `AuthContext.tsx`: Added `isViewerManagement` flag. Expanded `hasHRModuleAccess` to include `isLocalManager` and `isViewerManagement`.
  - `HRLandingPage.tsx`: Role-aware tab filtering. Viewer/Management users see only the calendar tab. All other roles see the full tab set.
  - No backend changes. Backend scope enforcement remains the source of truth.

## [2.99.2] - 2026-05-12

### Added
- **Diagnostic Review — Onboarding & Help UX**: Lightweight guidance layer for the `/hr/attendance-review` page to improve first-time HR user experience.
  - **Help Drawer**: "Como usar esta tela?" button in the diagnostic banner opens a slide-in drawer (following the existing `PurchasingHelpDrawer` pattern) with four sections: page purpose, step-by-step usage guide, field glossary table, and severity level explanations. All content in Portuguese.
  - **Severity Legend**: Compact inline legend strip above the results table showing all four severity levels (Alta/Média/Baixa/Nenhuma) with descriptions, using the existing badge visual style.
  - **Column Tooltips**: Info icons (ℹ) on 6 table headers (Status Innux, Status Portal, Severidade, Confiança, Min. Innux, Min. Portal) using the existing `ModernTooltip` component. Short Portuguese explanations on hover.
  - **Initial Guidance**: Improved empty-state before first search with structured guidance text and a hint pointing to the help button.
  - **Design**: Purely visual/UX. No backend changes. No comparison logic changes. Page remains strictly diagnostic and read-only.

## [2.99.1] - 2026-05-12

### Changed
- **Diagnostic Review — Employee Search Autocomplete**: Replaced the technical "ID Funcionário (Innux)" numeric input in the `/hr/attendance-review` filter bar with an intuitive employee name search autocomplete.
  - **Autocomplete UX**: Debounced search (300ms) against the existing `GET /api/hr/leave/employees?search=` endpoint. Dropdown shows employee name, Innux department, and Innux ID for diagnostic transparency. Keyboard navigation (↑/↓/Enter/Escape) fully supported.
  - **Selection Display**: Selected employee shown as `Name (#InnuxID)` with a clear button (×) that reverts the filter to "Todos" (all employees).
  - **Backend Change**: Added `InnuxEmployeeId` to the `GetEmployees` projection in `HRLeaveController.cs` (one field, additive, backwards-compatible).
  - **Design**: Strictly diagnostic, read-only. No changes to the comparison engine, HR Attendance Calendar, Innux, or Primavera.

## [2.99.0] - 2026-05-12

### Added
- **Portal Attendance Engine — Phase 4: Diagnostic Review UI**: New HR-only diagnostic page at `/hr/attendance-review` for visually inspecting attendance discrepancies between Innux processed data and Portal raw-punch interpretation.
  - **Route & Access**: New tab "Revisão de Presenças" in the HR workspace, visible only to System Administrator and HR roles. Department Managers cannot access via tab or direct URL. Page-level role guard enforced independently from route guard.
  - **Filter Bar**: Date range (with client-side 31-day validation), Innux Employee ID, severity filter (Todos/Alta/Média/Baixa/Nenhuma), and "Apenas divergências" toggle.
  - **Summary KPI Cards**: Total days analyzed, severity breakdown (None/Low/Medium/High), execution time.
  - **Results Table**: 13-column table with severity badges, confidence indicators, and clickable rows for drill-down.
  - **Detail Drawer**: Slide-in panel showing Innux vs Portal side-by-side comparison, discrepancy messages, portal warnings, recommended review action, schedule resolution source, Innux worked-minutes enrichment metadata, raw punch timeline, and punch pairs — fetched on-demand from `interpret-punches` endpoint.
  - **Severity Visual Style**: High (red), Medium (orange), Low (blue/informational), None (neutral gray). Low severity styled as informational, not success.
  - **Diagnostic Banner**: Persistent informational banner: "Esta tela é apenas diagnóstica. Nenhuma informação é gravada no Innux ou Primavera."
  - **Design**: Strictly diagnostic, read-only. No approve/reject/correct/write-back actions. Does not change the existing HR Attendance Calendar behavior. Consumes existing `compare-range` and `interpret-punches` backend endpoints. No backend changes.

## [2.98.1] - 2026-05-12

### Fixed
- **Portal Attendance Engine — Innux Worked-Minutes Enrichment**: The comparison engine now enriches `InnuxWorkedMinutes` from `AlteracoesPeriodos` (via `GetWorkedHoursAsync`) when the calendar summary returns 0 for a present employee. This eliminates false Medium discrepancies caused by the calendar grid query not merging worked-hour detail.
  - **Enrichment logic**: Triggered only when `InnuxWorkedMinutes == 0` and `InnuxStatus` is `Present`, `PortalInterpreted`, or `Anomaly`. Uses `GetWorkedHoursAsync` (existing read-only service, no new SQL).
  - **New DTO fields**: `InnuxWorkedMinutesSource` (`CalendarSummary` | `DayDetail` | `NotAvailable`), `InnuxWorkedMinutesEnriched` (bool). Both fields are additive — backwards compatible.
  - **False positive prevention**: If enrichment yields `NotAvailable` (no `AlteracoesPeriodos` records), the discrepancy is downgraded from Medium to Low with the message: "Atenção: minutos trabalhados do Innux não estavam disponíveis no resumo diário; comparação baseada em dados incompletos."
  - **PortalInterpreted status handling**: The `PortalInterpreted` status (used by the Portal-Override path for Code 17/18 re-classified employees) is now included in the present-family check for worked-minutes comparison, preventing comparison bypass.
  - **Validation**: Confirmed enrichment path works correctly. In the current Innux deployment, `AlteracoesPeriodos` is consistently empty, so enrichment yields `NotAvailable` and severity drops from Medium→Low as designed.

## [2.98.0] - 2026-05-12

### Added
- **Portal Attendance Engine — Phase 3: Comparison Engine (Diagnostic)**: Backend-only comparison engine that contrasts Innux processed attendance results against Portal raw-punch interpretation for diagnostic purposes. No changes to the production HR Attendance Calendar UI.
  - **AttendanceComparisonService**: Orchestrates existing services (`IInnuxAttendanceService`, `IPortalPunchInterpreter`, `IPortalScheduleResolver`) without new SQL queries. Compares status, entry/exit times, and worked minutes. Assigns discrepancy severity (None/Low/Medium/High) using explicit rules.
  - **Portal Status Derivation**: Derives `PortalStatus` from raw punches — `Present` (complete pairs, worked > 0), `NoPunches`, `Incomplete`, `DayOff` (rest day, no punches), `PresentOnRestDay`.
  - **Discrepancy Rules**: HIGH = Innux absent but Portal present, or vice versa. MEDIUM = worked minutes drift > 30min, entry/exit drift > 30min, incomplete pairs, duplicates. LOW = minor drift 1-30min, Alteracoes fallback, low confidence.
  - **Portuguese Messages**: All `DiscrepancyMessages` and `RecommendedReviewAction` fields are in Portuguese for HR users.
  - **Diagnostic Endpoints**: Two new endpoints restricted to SystemAdministrator and HR roles:
    - `GET /api/hr/attendance/portal/compare/{innuxEmployeeId}/{date}` — single-day comparison
    - `GET /api/hr/attendance/portal/compare-range?startDate=&endDate=&innuxEmployeeId=&departmentId=&onlyDiscrepancies=true` — range comparison (max 31 days)
  - **Batch Statistics**: Range endpoint returns `DateRangeComparisonResultDto` with severity counts, execution time, and total employee-days processed.
  - **DTOs**: `AttendanceComparisonResultDto` (per-day result), `DateRangeComparisonResultDto` (batch wrapper). Replaces the unused `AttendanceComparisonReadyDto` placeholder from v2.97.0.
  - **Design**: Strictly diagnostic, read-only. Does not replace the current Innux-based calendar. Schedule fallback from Alteracoes.IDHorario is context only, NOT proof of attendance.

## [2.97.1] - 2026-05-12

### Fixed
- **Portal Attendance Engine — Code 17/18 Interpretation (F-PCH-01)**: Codes 17 and 18 are no longer mapped to fixed Entry/Exit directions. Production validation confirmed that terminals send Code 17 for both entry and exit punches. Both codes are now treated as direction-ambiguous and resolved via position-based inference (first punch = Entry, last punch = Exit). Applied rule changed from `Code17Entry`/`Code18Exit` to `Code17_18Ambiguous` + `InferredFirstEntry`/`InferredLastExit`.
- **Portal Attendance Engine — Escala Schedule Fallback (F-SCH-01)**: Added a fallback resolution path for Escala-type work plans (`CycleDays == 0`) where `PlanosTrabalhoHorarios` has no cycle mappings. The resolver now queries `Alteracoes.IDHorario` to source the schedule from Innux's daily assignment record. New `ScheduleResolutionSource` DTO field tracks whether the schedule was resolved via the primary path or the fallback.
- **Portal Attendance Engine — Duplicate Detection Enhancement (F-PCH-02)**: Duplicate detection window expanded from 2 to 15 minutes and no longer requires same-terminal matching. Consecutive same-direction punches within the threshold are flagged as duplicates regardless of which terminal was used. Added cascade prevention to avoid flagging chains of duplicates.

## [2.97.0] - 2026-05-12

### Added
- **Portal-Side Attendance Interpretation Engine — Phases 1 & 2 (Diagnostic Foundation)**: Backend-only, read-only foundation for an independent Portal-side attendance interpretation engine. No changes to the existing HR Attendance Calendar UI or production workflows.
  - **Phase 1 — Schedule Day Resolver (`PortalScheduleResolver`)**: Resolves the expected schedule for an employee on a given date using `PlanosTrabalho`, `PlanosTrabalhoHorarios`, and `HorariosPeriodos`. Computes cycle indices, handles overnight shift detection, and calculates expected working minutes.
  - **Phase 2 — Raw Punch Interpreter (`PortalPunchInterpreter`)**: Reads raw `TerminaisMarcacoes` data, infers Entry/Exit directions (EN/SA, codes 17/18, position-based), flags duplicate punches without removing them (`IsDuplicateCandidate`), builds punch pairs, calculates worked minutes, and assigns confidence scores (High/Medium/Low/None).
  - **Diagnostic Endpoints**: Two new diagnostic-only endpoints restricted to SystemAdministrator and HR roles:
    - `GET /api/hr/attendance/portal/resolve-schedule/{innuxEmployeeId}/{date}`
    - `GET /api/hr/attendance/portal/interpret-punches/{innuxEmployeeId}/{date}`
  - **DTOs**: `PortalAttendanceEngineDtos.cs` with `ResolvedScheduleDayDto`, `SchedulePeriodDto`, `PunchInterpretationResultDto`, `InterpretedPunchDto`, and `PunchPairDto`.
  - **Design**: All services are strictly read-only (SELECT-only SQL). No writes to Innux or Primavera. All interpretation decisions and rules are captured in the diagnostic DTOs for full transparency.

## [2.96.3] - 2026-04-28

### Fixed
- **HR Attendance — False Absences (F03) due to Code 17 Anomalies**: Implemented a global Portal-side override. When the Portal detects valid presence (multiple Code 17 punches) but Innux incorrectly reports an unjustified absence (F03), the Portal now clears the absence data (`absenceMinutes` = 0), removes the "Falta Injustificada" label, and flags the period with a "PORTAL" work description. This corrects the UI projection without altering source data in Innux or Primavera.

## [2.96.2] - 2026-04-28

### Fixed
- **HR Attendance — Innux Direction Codes 17/18**: Terminal codes `17` and `18` now correctly map to Entrada and Saída respectively. No longer treated as unknown direction codes.

## [2.96.1] - 2026-04-28

### Changed
- **Approval Detail Panel — Price Analysis Banner**: Removed the misleading green "Preços Favoráveis" success banner.
- **Warning-Only Policy**: The price analysis banner now follows a warning-only policy for items above average.

## [2.93.4] - 2026-04-26

### Added
- **Budget Help Tooltips**: Contextual help icons on Finance > Orçamento explaining Comprometido vs Pago for business users.
- **Reusable Components**: `BudgetHelpContent`, `BudgetHelpIcon` using existing `ModernTooltip`.

## [2.93.3] - 2026-04-26

### Added
- **Monthly Budget Evolution Chart**: Stacked bar chart on Finance > Orçamento showing monthly committed/paid breakdown by cost center.
- **New Endpoint**: `GET /api/v1/finance/budget/department/{departmentId}/monthly/{year}` — 12-month CC breakdown.
- **New DTOs**: `BudgetMonthlyDataDto`, `BudgetMonthlyCostCenterDto`.
- **Toggle Modes**: Comprometido, Pago, Ambos with grouped stacked bars.

## [2.93.2] - 2026-04-26

### Fixed
- **Finance COMPLETED Status Visibility**: Requests with `COMPLETED` status were invisible in the Finance workspace (Resumo Operacional, Pagamentos, Orçamento) because the status was not defined in `RequestConstants.Statuses` and was excluded from all Finance controller filter arrays.
  - Added `RequestConstants.Statuses.Completed = "COMPLETED"`.
  - Injected `Completed` into 3 `financeStatuses` arrays, 2 `IsPaid` checks, and the `completedThisMonth` filter in `FinanceController`.
  - Injected `Completed` into `CommittedStatuses` and 2 `IsPaid` checks in `FinanceBudgetController`.
- **Budget Calculation Always Zero (Pre-existing Bug)**: Both budget overview and cost center detail queries in `FinanceBudgetController` were missing `.Include(r => r.Status)`, causing `req.Status?.Code` to always be `null`. Committed/Paid aggregation returned 0 for all departments regardless of status.

## [2.93.1] - 2026-04-26

### Changed
- **Payment Divergence Detection**: Removed the 1% relative tolerance for payment divergence warnings. Any non-zero difference between `ActualPaidAmount` and `ApprovedTotalAmount` now creates a `PAYMENT_DIVERGENCE_DETECTED` audit entry.

## [2.93.0] - 2026-04-25

### Changed
- **Backend Performance Optimization (Requests & Receiving)**: Eliminated N+1 query patterns in `/api/v1/requests` list and the Receiving workspace.
  - Refactored `GetRequests` and `ProjectToListItem` in `RequestsController` to use explicit anonymous projections (`let`-like logic), fetching related Quotations, Cost Centers, and Status Histories in a single database operation before projecting into DTOs.
  - Applied new performance indexes via EF Core Migration (`AddRequestPerformanceIndexes`) for critical high-frequency filtering fields (`RequestTypeId`, `DepartmentId`, `PlantId`, `CompanyId`, `NeedLevelId`, `SelectedQuotationId`).
  - Request list load time significantly reduced (from timeout to 2-3s) for large datasets.

## [2.92.0] - 2026-04-25

### Changed
- **Supplier Ficha UX/UI Standardization (Phases 1–4)**: Complete visual alignment of the "Ficha de Fornecedor" module with the Portal Gerencial "Modern Corporate" design system.
  - **Phase 1 — Token Alignment**: Removed parallel `:root` CSS variables from `SupplierFicha.css`, migrated all hardcoded status/surface/text/border colors to canonical `tokens.css` design tokens, and unified modal `z-index` to `var(--z-modal)`.
  - **Phase 2 — List Page Standardization**: Replaced custom CSS classes with Portal-compliant components. KPI cards redesigned with Lucide icons, hover transitions, `aria-pressed` states, and token-based coloring. Table standardized with framer-motion row stagger. Pagination arrows replaced with Lucide icons. Shimmer-based loading skeleton and Feedback error component integrated.
  - **Phase 3 — Detail Page Polish**: Replaced all emojis with Lucide SVG icons. Aligned approval tracker accent from purple (#6366f1) to Portal primary (`var(--color-primary)`). Added `role="dialog"`, `aria-modal`, and `aria-label` to confirmation modals. Aligned `getStatusColor()` to design token variables with CSS custom property fallbacks.
  - **Phase 4 — Loading/Error States**: Integrated standard loading skeleton and Feedback component for API errors on the list page.

## [2.91.0] - 2026-04-25

### Added
- **Supplier Import Review Modal**: Added a pre-import review step when importing new suppliers from Primavera. Users can now review and edit supplier data (Name, NIF) before committing the import, preventing dirty data from entering the Portal. New `POST /api/v1/sync/suppliers/import-reviewed` endpoint preserves the existing import contract.

## [2.90.0] - 2026-04-25

### Added
- **Supplier Ficha Approval Workflow (Phase 2B)**: Centralized supplier approval actions into the Approval Center quick-view drawer. Removed obsolete approval UI from the detail page to enforce submission-only model.

## [2.89.0] - 2026-04-25

### Fixed
- **Calendar Timezone Bug**: Resolved -1 day rendering offset caused by UTC conversion in WAT timezone. Replaced `toISOString()` with local date formatting.

### Added
- **Vacation & Holiday Status Classification**: Extended `ClassifyAttendance` to sub-classify justified absences into Vacation (🌴) and Holiday (⭐) statuses via Innux justification text parsing.
- **Worked Hours Metrics (Basic/Overtime)**: New `GetWorkedHoursAsync` engine aggregating non-dispensed periods from `dbo.AlteracoesPeriodos` with `CodigosTrabalho` type mapping. Graceful fallback on failure.
- **Justification Table (Structural)**: `HRAttendanceJustifications` migration with FKs, indexes, and status lifecycle. Not yet applied — awaiting Phase 4 functional work.

## [2.88.0] - 2026-04-23

### Added
- **HR Monthly Changes UI**: First frontend slice for the Innux-to-Primavera workflow. Implemented `MonthlyChangesList` for viewing and creating processing runs, and `MonthlyChangesRunDetail` with tabs for Review Items, Anomalies, and Processing Logs. Added support for filtering items by status and occurrence type.

## [2.87.0] - 2026-04-23

### Fixed
- **HR Monthly Changes Detection Engine Hardening**: Resolved lateness/absence overlapping logging, fixed missing Unjustified Absence creation on days with partial Justified Absence, and improved anomaly escalation logic. Added diagnostic logging. Verified that the Innux source table `dbo.Alteracoes` pre-filters standard days, confirming that a 1:1 snapshot-to-occurrence mapping is the correct domain behavior.

## [2.86.0] - 2026-04-23

### Added
- **HR Monthly Changes Middleware — Persistence Foundation**: 8 domain entities (MCProcessingRun, MCAttendanceSnapshot, MCMonthlyChangeItem, MCPrimaveraCodeMapping, MCDetectionThreshold, MCExportBatch, MCExportRow, MCProcessingLog), EF Core fluent configurations, and applied migration for the Innux → Portal → Primavera HR monthly export workflow. Backend-only, no UI.

## [2.85.0] - 2026-04-22

### Added
- **HR Attendance Calendar (Innux Integration)**: Full-stack delivery of the Innux-integrated attendance calendar with paginated grid (15/page), alphabetical sorting (pt-AO locale), multi-level filtering (Company, Plant, Department), scroll-contained layout architecture, cell-click detail drawer, and backend attendance API with scope-enforced data access.

## [2.84.0] - 2026-04-22

### Added
- **HR Team Calendar Modernization**: Backend-enforced access control (System Admin / HR / Local Manager / Department Manager / Self-Calendar), Week-of-Year view mode with ISO week badge, frozen sticky employee column, scope-aware UI headers, and dedicated CSS with portal design tokens.

### Fixed
- **Local Manager Calendar Scope**: Corrected OR→AND intersection logic for plant/department filters. Local Managers scoped to a specific department now only see employees from that department, not all employees from the entire plant.

## [2.83.1] - 2026-04-20

### Changed
- **UI Modernization — Legacy Brutalist → Modern Corporate (Final Pass)**: Systematic elimination of all remaining "Industrial Brutalist" design patterns across 31 frontend files. Zero occurrences of `var(--shadow-brutal)`, `4px/6px offset shadows`, `translate(-2px,-2px)` hover effects, or `2px/4px solid border-heavy` heavy borders remain in the codebase.
  - **Shared Components**: `ApprovalModal`, `CorrectPoModal`, `RegisterPoModal`, `RequestLineItemForm`, `RequestAttachments`, `Feedback`, `Tooltip`, `CostCenterAutocomplete`, `DepartmentMasterAutocomplete`, `EmployeeAutocomplete`, `SupplierAutocomplete`, `QuotationEntry`.
  - **Layout / Modais**: `UserProfileDrawer`, `UserDropdown`, `QuickSupplierModal`, `HRActionModal`, `ReceivingModal`, `FinanceActionModal`, `PurchasingHelpDrawer`.
  - **Páginas**: `RequestCreate`, `RequestGeneralDataSection`, `RequestActionHeader`, `PurchasingLandingPage`, `Purchasing/QuickActions`, `BuyerItemsList`, `SystemLogs`, `FinanceHistory`, `ChangePasswordPage`, `AttentionList`.
  - **globals.css**: Removed `.btn-primary:active` translate offset.
  - **Token Standards**: All shadows → `var(--shadow-sm/md/lg)`. All borders → `1px solid var(--color-border)`. All interactive lifts → `translateY(-2px/3px)`. All radii → `var(--radius-md/lg)`.

## [2.83.0] - 2026-04-20

### Added
- **Contract-Driven Cash Flow Projection (Phase 1 — DEC-118)**: Full-stack delivery of the contractual projection feature in the Finance module.
  - New `ContractProjectionSection.tsx` component with 6 KPI cards (Compromissos do Mês, Próximos 90 Dias, Em Pipeline, Confirmado, Vencidos s/ Pedido, Risco de Penalidade), bar chart (6 meses por bucket), and paginated detail table with filters por bucket e risco.
  - New backend endpoints `GET /api/v1/finance/contract-projections/summary` e `/contract-projections` in `FinanceController.cs`.
  - New `ContractProjectionDtos.cs` with typed DTOs: Summary, MonthySeries, Item, PagedResult.
  - Forecast buckets (`PROJECTED`, `OVERDUE_NO_REQUEST`, `PIPELINE`, `CONFIRMED`, `REALIZED`) and risk levels (`HIGH`, `MEDIUM`, `LOW`) derived at query time from active contract obligations — no duplication of financial state.
  - Integrated lazily (React.lazy + Suspense) at the bottom of `FinanceOverview.tsx`.
  - Filterable by `companyId`, `bucket`, `onlyAtRisk`, and date range. Paginated (15 items/page).

### Changed
- **Tailwind CSS Remediation — 100% Complete (Phases 1–3)**: Systematic removal of all Tailwind utility classes from 31 frontend files (~167 occurrences). Replaced with inline styles using `var(--color-*)` CSS tokens and native CSS pseudo-classes. Tailwind is no longer a dependency.
  - Phase 1 (Critical): `QuotationEntry.tsx` (89 classes), `QuickCurrencyModal.tsx` (17 classes) — full JSX rewrite with inline styles.
  - Phase 2 (High): `UserProfileDrawer`, `DecisionInsightsPanel`, `DecisionQuotationCard`, `ApprovalDetailPanel`, `EmployeeWorkspace`, `QuickSupplierModal`, `DetailedHistoryPanel`, `BuyerItemsList` — surgical conversion.
  - Phase 3 (Medium/Low): Remaining 16 files with 1–3 occurrences, including `MasterData`, `ApprovalCenter`, `UserManagement` (with custom `slideInFromRight` keyframe added to `globals.css`).
- **Finance Budget Config**: Restored missing styles and colors on the `FinanceBudgetConfig` page, aligning it with the design system.


## [2.82.0] - 2026-04-20

### Added
- **Payment Deadline Rules — Frontend & Documentation (DEC-117)**: Completed full-stack delivery of the payment deadline rules feature introduced in v2.81.0.
  - "Regras de Pagamento" collapsible section in ContractCreate/Edit with progressive disclosure and conditional field rendering based on selected payment term type and reference event type.
  - Due date source badge (`🔄 Auto` / `✏️ Manual`) on each obligation in ContractDetail.
  - Expandable obligation deadline metadata panel showing `ReferenceDateUtc`, `CalculatedDueDateUtc`, `GraceDateUtc`, `PenaltyStartDateUtc`.
  - Active payment rule summary panel in the contract detail "Geral" tab.
  - Context-aware obligation form guidance and `InvoiceReceivedDate` field when required by the active rule.
  - Full documentation in `CONTRACTS_WORKFLOW.md §11` and `DECISIONS.md DEC-117`.

## [2.81.0] - 2026-04-19

### Added
- **Contracts Management Module (First Vertical Slice)**: Full vertical delivery including domain model (6 entities), REST API (18 endpoints), and frontend workspace (5 pages). Supports contract lifecycle state machine (DRAFT → ACTIVE → TERMINATED), payment obligation management, and manual Payment Request generation from obligations. Scoped data access via plant/department visibility rules. Documented in `CONTRACTS_WORKFLOW.md` and `DEC-111`.


## [2.80.0] - 2026-04-18

### Added
- **Annual Budget Domain**: Introduced the `AnnualBudget` entity to manage distinct yearly budgets for departments based on a native currency, preventing duplicate budget definitions via `Year + DepartmentId + CurrencyId` constraints.
- **Budget Setup Interface**: Created `FinanceBudgetConfig.tsx` to enable users with `Finance` or `SystemAdministrator` roles to maintain annual departmental limits seamlessly.
- **Committed Spend Engine**: Configured the new `FinanceBudgetController` to calculate "Committed" vs "Paid" spend continuously in real-time, leveraging active request statuses while actively excluding any cancelled workloads.
- **Executive Overview Tracking**: Integrated an 'Acompanhamento Orçamental' panel into `FinanceOverview.tsx`. This view delivers a macro synthesis across currencies, highlights the top 5 departments at risk of breaching limits, and provides contextual drill-down into cost-center execution.

## [2.79.0] - 2026-04-18

### Added
- **Manual Badge Creation**: Added a new "Entrada Manual" toggle in the HR Employee Workspace. This allows issuing badges manually avoiding prerequisite Primavera registration schemas logic.
- **Resilient Multi-line Layouts**: Upgraded `BadgePreview` capabilities, granting robust text-wrapping functionality for long strings like multi-line complete names over constrained areas.

## [2.78.0] - 2026-04-17

### Added
- **Financial Snapshot & Payment Divergence Detection (Phase 1 — DEC-110)**: Automated comparisons across requested vs actually paid amounts via `ActualPaidAmount` capturing procedures.
- **Workflow Overrides**: Hardened endpoints like `SchedulePayment` with active status checks.

## [2.77.3] - 2026-04-16

### Performance
- **Master Data Page Load Optimization (50s → 2s)**: Diagnosed and resolved two compounding performance issues causing the Master Data page to take ~50 seconds to load:
  1. **Backend: GetUsers Cartesian Explosion** — The `/api/v1/users` endpoint used 4 eager-loading Include chains (Department, Roles, Plants, Departments) that produced a cartesian join taking 25s+ for just 10 users. Replaced with direct `Select` projection in SQL, reducing the endpoint to <200ms.
  2. **Frontend: Parallel API Contention** — `Promise.allSettled` fired 9 API calls simultaneously, overwhelming LocalDB's connection pool. Switched to sequential loading, which completes all 9 requests in <1s vs the previous 44s under contention.

## [2.77.2] - 2026-04-16

### Fixed
- **EF Core Decimal Precision Standardization**: Added explicit `HasColumnType` precision/scale for all decimal properties across `OcrExtractedItem`, `ReconciliationRecord`, `QuotationItem`, `RequestLineItem`, and `Request` entities. Eliminates all "No store type was specified for the decimal property" model validation warnings and prevents potential silent data truncation. Convention: money `(18,2)`, percentages `(9,4)`, quantities `(18,4)`.

### Documentation
- **DECISIONS.md**: Added DEC-108 — Mandatory Explicit Decimal Precision rule as a permanent backend convention.

## [2.77.1] - 2026-04-16

### Fixed
- **Primavera Department Sync Integration**: Fixed a silent synchronization failure by removing the invalid `Inactivo` column mapping constraint. The backend now cascades a comprehensive `207 Multi-Status` detailing Created, Updated, Processed, and Errors metrics directly to the user interface.
- **Department Master Mapping Quality**: Reconstructed the HR department lookup DTO, resolving blank text regressions during rendering.

### Added
- **Intelligent Origem Reconciliation**: Activated an automated normalization and suggestions engine for HR mapping logic. The drop-down actively scores the source Innux department against available HR records resulting in safe, isolated top-level `✨ PROVÁVEL` / `SUGESTÃO` selections.
- **Isolated Plant Topology**: Configured UX boundaries around Company/Plant. The Department Master autocomplete immediately unlinks state data and actively issues an isolated invalidation toaster if users navigate their form across organizational Plant constraints (e.g. AlplaPLASTICOS to AlplaSOPRO).

## [2.77.0] - 2026-04-16

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

## [2.76.1] - 2026-04-15

### Added
- **Global Search Scope**: Expanded the main Requests Dashboard search capabilities. The system now seamlessly searches by Requester Name (`Solicitante`) in addition to Request Number and Title.
- **Contextual Help Search Glossary**: Updated the contextual help (the "i" icon) in the "Explorador de Pedidos" modal to precisely reflect all actively indexable search parameters.

## [2.76.0] - 2026-04-15

### Added
- **Buyer Portal Header Modernization**: Refactored the request card header into a clean 3-zone architecture (ID/Status, People/Approvers, Date/Actions) with improved visual hierarchy and scannability.
- **Action Kebab Menu (⋮)**: Replaced legacy "Cancelar" and "Detalhes" buttons with a unified, motion-animated dropdown menu to increase workspace density and reduce visual noise.
- **Teams Status Presence**: Integrated discrete Teams chat triggers next to requester, buyer, and approver names for immediate operational communication.

### Changed
- **Multi-Stage Approver Logic**: Updated the Area Approver visibility logic. The "Aprovador da Área" now remains visible throughout all initial stages (including Aguardando Cotação) until the request reaches "Aguardando Aprovação Final", providing consistent departmental context.
- **Card Layout Resilience**: Removed overflow constraints on quotation request cards to accommodate floating dropdown menus without clipping regressions.
- **Dark Mode Hospitality**: Standardized the new kebab menu components with semantic CSS variables (`var(--color-bg-surface)` and `var(--color-bg-neutral)`) for seamless theme switching.

## [2.75.0] - 2026-04-15

### Added
- **Context-Aware Empty States**: Upgraded the "Gestão de Cotações" empty state to structurally depend on the active filter context. When the user has an active text search or status filter that yields zero results, contextual actionable buttons ("Limpar Busca" / "Limpar Filtros") are displayed directly in the empty state.
- **Structural Loading Skeletons**: Replaced the static "Carregando..." text with a custom `RequestGroupSkeleton` component utilizing pulsing CSS animations (mimicking the exact height and column metrics of the collapsed quotation groups) to eliminate layout shift post-data-fetch.
- **Localized Error Recovery**: Implemented localized boundaries for data fetching errors inside the Buyer Workspace. Failed fetches gracefully exit into an encapsulated "Falha ao Carregar" state containing an explicit and isolated "Tentar Novamente" recovery button mapped directly to the `loadData()` handler, preserving the shell structure visually.
- **Form Interactivity Preservation**: Extended all primary backend-bound mutation actions (Save Quotations, Re-assignments) to implicitly clear nested frontend list error contexts upon success, enhancing user resilience logic.

## [2.74.0] - 2026-04-15

### Added
- **Catalog Linkage in Manual Quotation Entry**: Integrated `CatalogItemAutocomplete` into the manual quotation entry mode within `BuyerItemsList.tsx`. This allows buyers to link manual entries directly to official Portal catalog items, ensuring data consistency for inventory and receiving.
- **Backend Catalog Traceability**: Updated `SavedQuotationItemDto` and `RequestsController` projections to persist and retrieve `ItemCatalogId` and `ItemCatalogCode` for quotation line items.

## [2.73.0] - 2026-04-15

### Changed
- **RequestEdit Modernization Cycle**: Decomposed `RequestEdit.tsx` into parent-orchestrator + 4 presentational children. Introduced `request-edit.module.css` for local style management. Implemented route-level code splitting via `React.lazy()` reducing core bundle by ~70%. Created `LoadingSkeleton` fallback. Cleaned dead imports.

## [2.55.0] - 2026-04-13

### Added
- **Financial Integrity Gate**: Server-side checkpoint at quotation completion validating OCR-extracted totals vs system-calculated totals. Centralized tolerance constants. Full audit trail for detection, blocking, and justified override. Frontend integrity modal for the Buyer workspace.

## [2.53.0] - 2026-04-13

### Added
- **Workflow Notification Role-Casting**: Expanded the Orchestrator to dispatch role-specific email subjects, headlines, and contextual comments based cleanly on the actor's jurisdiction (Requester, Next Approver, or Buyer).
- **Self-Notification Lift**: Removed the `BypassSelfNotifyRule` suppression wall, allowing users to universally retain an email trail of requests they submitted onto their own governed departments. 
- **Admin System Logs Enhancement**: The `UsersController` backend now natively integrates `_adminLogWriter` on `USER_CREATION_FAILED`.

### Fixed
- **In-App Duplicate Handlers**: Removed obsolete `window.alert()` from identical user collisions inside `UserManagement.tsx`, channeling errors into an organic local-state UI banner.

## [2.52.0] - 2026-04-12

### Added
- **Server-Side Catalog Search & Pagination**: Improved performance of catalog items lookup by pushing load to the backend (`take=10`).
- **Autocomplete Optimization**: Improved the UX of the "pickup list" inside Request creation with max limits and extra visibility (`Cod_Primavera`, `Cod_Fornecedor`).

## [2.51.0] - 2026-04-11

### Added
- **Dynamic SMTP Management**: Database-backed SMTP configuration with AES-256 encryption and real-time connectivity diagnostics.

## [2.50.0] - 2026-04-11

### Added
- **Password Recovery Workflow**: Secure self-service password reset flow with token-based authentication and CID-embedded email branding.
- **Environment Safety**: Centralized URL config and strict guards against localhost leaks.

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
- **UI/UX Standarization & Context Linking**: Enhanced the modern requests dashboard with cross-workspace deep linking functionality and persistent layout bugfixes.

## [2.47.0] - 2026-04-11

### Added
- **UI/UX Standarization (Phases 1-4)**: Completely standardized the visual alignment of all legacy operational and administrative workspaces to mirror the modern "Requests" baseline structure.

## [2.46.0] - 2026-04-11

### Added
- **Modern Requests Dashboard**: Visually overhauled the primary Requests workspace away from the legacy table patterns to a high-fidelity 'Modern Corporate' widget layout (`ActionCarouselWidget`, `RequestsTableWidget`).
- **Drawer Presentation Mode (Dual-Mode Architecture)**: Integrated `RequestDrawerPresentation`, allowing users to open and edit full requests via a slide-out right panel directly from the dashboard without navigating away.

## [2.45.1] - 2026-04-10

### Fixed
- **Dark Mode UI Stabilization**: System-wide eradication of hardcoded hex colors to prevent visibility issues in themed environments.

### Added
- **Dark Mode Support**: Native theme switching (Light, Dark, System) with FOUC prevention and high-contrast slate-based palettes.
- **Stacked Requests List**: Refactored workspace with "Para Minha Ação" and "Explorador de Pedidos" sections.
- **`RequestsGrid` Component**: Modular request list rendering with isolated state management.

### Changed
- **Responsibility Filtering**: Server-side LINQ expressions for advanced role-based task identification.

## [2.44.0] - 2026-04-10

### Added
- **Quotation Assignment Security**: Implemented strict ownership restrictions in the Quotation Management workflow. Unassigned or laterally assigned quotations are locked to read-only views, exposing a dynamic interface specifically for claiming/re-assigning ownership on-the-fly (`isAssignedToMe`).

### Changed
- **Quotation Discount Financial Model**: Refactored quotation draft properties from macro global elements down into explicit item-level discount declarations (`DiscountAmount` and `DiscountPercent`).
- **Orphan Attachment Cleanup Mitigation**: Integrated logic into the UI's draft lifecycle handlers effectively triggering backend deletion API operations exactly when an end-user abandons an actively loading OCR upload via the new "CANCELAR" interactive component.

## [2.43.1] - 2026-04-09

### Fixed
- **TotalAmount Persistence & Calculation**: Resolved a financial data loss issue where discount amounts and IVA rates were discarded during Request Line Item processing resulting in incorrect `TotalAmount` values (reverting to gross totals instead of net + IVA).
    - Added `DiscountPercent` and `DiscountAmount` to the database schema.
    - Included discount fields in the `GetRequest` response to survive frontend auto-save round-trips.
    - Unified the `TotalAmount` calculation across 4 backend generation points (Bulk Create, Clone, Add, Update) to fully incorporate discounts and IVA.

## [2.43.0] - 2026-04-09

### Added
- **Context-Aware OCR Triage**: Implemented `sourceContext` propagation in the extraction pipeline. When documents are uploaded from Quotation or Payment flows, the system now enforces an "Invoice" classification, preventing catastrophic misclassification as "Contract".
- **Multi-Strategy Supplier Matching**: Overhauled the frontend supplier identification in `useOcrProcessor.ts`, including punctuation-insensitive normalization and NIF/TaxId fallback search to eliminate duplicate records.
- **Discount & IVA Reliability**: Improved the OCR prompt and frontend mapping to accurately distinguish Portuguese "Desc." (discount) from "IVA" (tax) columns.
- **TotalPrice Anchor Validation**: Implemented a frontend safety net that reverse-engineers the correct discount amount from the document's total price if AI classification fails.

### Changed
- **OCR System Prompt**: Updated with bilingual (PT/DE) column mapping instructions and self-validation rules for line items.

## [2.42.1] - 2026-04-09

### Fixed
- **P.O. OCR Data Path Mismatch**: The `RegisterPoModal` was reading OCR results from a non-existent flat path (`header.totalAmount`). Corrected to read from the legacy envelope (`integration.headerSuggestions.grandTotal.value`).
- **P.O. Grand Total Extraction**: The GPT extraction prompt only requested the subtotal before tax (`totalAmount`), causing a systematic mismatch against the quotation's grand total. Added a new `grandTotal` field to the extraction schema capturing the final amount including IVA, with a safe fallback chain (`GrandTotal ?? TotalAmount`).
- **P.O. Supplier Identification on Encomendas**: Added explicit prompt instructions for Purchase Order layouts where ALPLA appears as the header entity and the actual supplier is listed under `Exmo.(s) Sr.(s)`.
- **Quotation Winner DTO Mapping**: Fixed `totalPrice` → `totalAmount` and `currencyId` → `currency` property mismatches when reading the winning quotation in `RequestEdit.tsx`.
- **TextFirst Null Guard**: Hardened the TextFirst early-return condition to reject empty strings (`!string.IsNullOrWhiteSpace`) instead of only checking for `null`.

## [2.42.0] - 2026-04-09

### Added
- **P.O. Override Validation via OCR**: Implemented a protective soft-block flow in `RegisterPoModal`. The system evaluates similarity matching between the document's payload and the approved request parameters. Mismatches require an active acknowledgement (Override Confirmation) and a mandatory qualitative justification before generating the Purchase Order.
- **P.O. Dispute Audit Log**: The Backend endpoint (`RequestsController.RegisterPo`) now natively audits OCR mismatches and override comments directly into the `RequestStatusHistory` timeline ensuring financial traceability.

### Fixed
- **Quotation Workflow Item Desync**: Corrected a regression where selecting a winning quotation left the Area Approver with an empty grid. The system now performs a hard-sync operation (`RequestsController.SelectQuotation`), automatically wiping existing generic request line items and comprehensively replacing them with strict clones of the selected `QuotationItems`, preserving quantities, identical descriptions, and computed aggregates seamlessly.

## [2.41.0] - 2026-04-09

### Added
- **P.O. Workflow Visibility**: Added "Awaiting P.O" metric to dashboard KPIs and a high-visibility quick-chip in the requests grid.
- **Atomic P.O. Registration**: Created a dedicated `RegisterPoModal` to handle PDF upload and status transition in a single, frictionless action.
- **Unit Master Data Integrity**: Implemented `isActive` filtering for units of measure across OCR extraction and manual input menus.

### Fixed
- **Status Constant Regressions**: Fixed backend build failure caused by incorrect status constant naming (`Approved` vs `FinalApproved`).

## [2.40.0] - 2026-04-09

### Added
- **OCR Line-Item Discount Extraction**: Full pipeline support for per-item discount percentages and amounts, with cross-validation safety net and editable UI column.
- **Company Auto-Identification (OCR)**: Keyword-based matching for automatic company field population from invoice billing entity.
- **OCR Diagnostics**: Console-level extraction and matching diagnostics for debugging.

### Changed
- **AI Extraction Prompt**: Rewritten with explicit discount calculation rules and concrete examples.
- **Item Total Calculation**: Always recalculated from components, eliminating silent zero-value errors.

## [2.39.8] - 2026-04-09

### Changed
- **Requests List Performance Optimization**: Refactored the core EF Core LINQ projections in `RequestsController.GetRequests` to utilize `SelectMany().SumAsync()` left-joins, removing a crippling in-memory aggregation bottleneck and dropping server response times for the main workspace dataset from ~40s to ~230ms.

## [2.39.7] - 2026-04-08

### Added
- **OCR Execution Audit**: Integrated mandatory, persistent audit logging for all extraction pipeline executions.

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
- **Token & Cost Observability**: Extracted and mapped OpenAI token consumption (Prompt, Completion, Total) directly into `ExtractionResultDto.Metadata`.

### Changed
- **Adaptive Document Rasterization Engine (Phase 1)**: Overhauled the OCR PDF rendering behavior inside `OpenAiDocumentExtractionProvider` (JPEG at 150 DPI, max 3 pages).

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
- **Finance Overview Dashboard**: Added dynamic KPI cards tracking pending actions, scheduled volumes, and overdue alerts.
- **Finance Payments List**: Added a dedicated data grid utilizing the standardized `KebabMenu` framework and `<FinanceActionModal />` popups for all request interactions. Hardened backend P.O. enforcement eligibility for queue generation.
- **Dedicated Return Workflow**: Added internal logical status `WAITING_PO_CORRECTION` to allow native context-safe return loops between Finance and Purchasing layers.
- **Backend Finance Services**: Implemented `FinanceController.cs` to segregate financial orchestration and introduced typed `FinanceDtos`.

## [2.38.2] - 2026-04-07

### Fixed

- **Quotation Attachment Anti-Duplication**: Resolved functional regression in SHA-256 duplicate warning modal. Extended pre-flight hash validation to the OCR Import Flow in Buyer Workspace.
- **Supporting Document Visibility**: Surfaced non-quotation items in the Quotation Management header.

## [2.38.1] - 2026-04-07

### Added

- **Quotation Assignment Notifications**: Real-time notifications dispatched to the requester and the assigned buyer when a quotation is claimed or explicitly assigned.
- **Viewable Request Context**: Surfaced **Request Title** and **Description/Notes** in the Quotation Management Workspace to provide buyers with immediate context without needing to open the full request.

### Fixed

- **Buyer Assignment Resolution**: Safely mapping HTTP 204 responses on `assign-buyer` endpoints to prevent runtime parse failures.
- **UX Parity in Quotation Management**: Fixed a gap in API mapping in `LineItemsController` to correctly hydrate and display the assigned buyer's name.

## [2.38.0] - 2026-04-06

### Added

- **Request Document Anti-Duplication (Soft-Block)**: Implemented an intelligent pre-flight `SHA-256` hashing validation on the Frontend via Web Crypto APIs.
- **Server-Side File Verification**: Extended `AttachmentsController.cs` for physical duplicate monitoring. Prevents redundant document extraction and saves user intake errors for Payment and Quotations flows.

## [2.37.1] - 2026-04-06

### Fixed

- **Payment Request Date Validation Rejection**: Resolved the blocking 400 Bad Request error occurring during the creation or editing of requests with past dates. Removed strict business constraints on the backend (`RequestsController.cs`) that rejected `NeedByDateUtc` values matching past dates, aligning it with the new frontend visual warning implementation.
- **Payment Request OCR & Manual UX Standardization**: Restructured the frontend Payment Request form to permanently decouple OCR vs Manual selection from the legacy grey UI bounds. Applied the deterministic "Gestão de Cotações" premium clean-card pattern with separated manual parsing boundaries, integrated native Date picker inputs, and exposed inline grid interactions to resolve friction during non-OCR manual invoicing.
- **Approval Historical Price Intelligence**: Integrated an automated price-variance alert directly into the Approval Detailed Panel. It visually flags requests that contain items priced above their historical averages (Yellow Warning), or conversely highlights them favorably (Green) when all historical item pricing stays aligned or below market average. Also decluttered redundant currency outputs in the hero header.

## [2.37.0] - 2026-04-06

### Changed

- **Approval Workspace Overhaul**: Completely modernized the user interface for the Approval Detail panel. Replaced all legacy brutalist remnants (raw black borders, strong shadows) with the new 'Premium Corporate' aesthetic leveraging `--color-bg-surface`, `--shadow-sm`, and `--radius-lg`.
- **CSS Infrastructure Upgrade**: Migrated embedded nested panels (`DecisionInsightsPanel`, `DetailedHistoryPanel`, `DecisionTimeline`, `DecisionQuotationCard`) away from pure Tailwind utility dependency toward strict inline mapping to the internal `tokens.css` design system, terminating rendering failures caused by clashing layout strategies.

## [2.36.0] - 2026-04-06

### Added

- **Item-Level Cost Center Mapping (Area Approval)**: Transitioned Area Approval from request-level to item-level cost center assignment to support complex, multi-plant operations.
- **Plant-Aware Assignment**: The UI now strictly uses the Line Item's `PlantId` to filter cost centers, rather than assuming all items belong to the Request Header's plant.
- **Safe Bulk-Fill**: Added a specialized action to replicate a selected cost center to other *unassigned* items on the *same plant*.

### Changed

- **DTO Migration**: Refactored `ApprovalActionDto.CostCenterId` to `ApprovalActionDto.ItemCostCenters` (Dictionary) in the C# backend.

## [2.35.0] - 2026-04-06

### Added

- **Company Master Data Management**: Implemented a new "Empresas" tab in the Master Data UI to manage legal entities and their workflow assignments.
- **Backend CRUD for Companies**: Extended `LookupsController.cs` with robust endpoints for company management, including `FinalApproverUserId` support and name-lock protection.

### Changed

- **System-Resolved Actor Model (DEC-093)**: Replaced manual requester-side selection of `Area Approver` and `Final Approver` with deterministic resolution based on `Department` and `Company` master data.

## [2.28.0] - 2026-04-04

### Added

- **Placeholder & Field Legibility tokens**: New semantic design tokens for accessible contrast (`--color-placeholder`, `--color-placeholder-focus`, etc.).

### Fixed

- **Project-Wide Legibility Audit**: Resolved systemic low-contrast placeholder issues by removing opacity-based styling and normalizing `text-transform` across all input types, including custom autocompletes, native selects, and error states.

## [2.27.1] - 2026-04-04

### Fixed (2.27.1)

- **Drawer Layering Logic**: Resolved a systemic bug where `Z_INDEX` constants (strings) were being incremented in JavaScript, resulting in invalid CSS values. Replaced with valid `calc()` expressions in:
  - `UserProfileDrawer.tsx` (Fixed "Meu Perfil" visibility)
  - `ApprovalCenter.tsx` (Fixed resize handle visibility)
  - `PurchasingHelpDrawer.tsx` (Fixed "Manual de Operação" visibility)

## [2.27.0] - 2026-04-04

## Version History

- **2.37.0**: Approval Workspace UI Engine Overhaul. Replaced complete UI styling stack for the Approval Detail panel with internal CSS tokens.
- **2.36.0**: Area Approval Item-Level Cost Centers. Transitioned cost center assignment from request-level to granular item-level tracking, strictly enforcing plant compatibility.
- **2.35.0**: Reactive OCR Supplier Workflow & Backend Portal Code Hardening (DEC-098). Relocated supplier validation to New Request screen and implemented robust, self-healing, concurrency-safe portal code generation.
- **2.34.0**: Request Edit Workflow Optimizations. Refined the Request Edit UI for `PAYMENT` requests by suppressing non-applicable quotation sections and guided attention. Integrated `QuickSupplierModal` with an OCR prefill block to enable seamless supplier creation and auto-selection for unmatched invoice data.
- **2.33.2**: Approval Modal State Sync. Fixed a stale closure bug in the Approval Center drawer.
- **2.33.1**: Payment OCR Navigation Hotfix. Resolved animation keyframe crash causing white screen.
- **2.31.0**: Payment OCR Persistence Fix (DEC-097). Relaxed DB constraints for Cost Center and IVA on draft line items, deferring strict validation to the submission stage.
- **2.30.0**: Payment OCR Intake & Shared Hook (DEC-096). Implemented automated document extraction for Payment requests and refactored OCR logic into a shared hook.
- **2.29.0**: Company Master Data & Entity Governance (DEC-093). Implemented full CRUD support for legal entities with integrated Final Approver role-based assignment. Simplified the request creation flow by automating actor resolution for Area and Final Approvers.
- **2.28.0**: Placeholder & Field Legibility Audit. Implemented a project-wide remediation for form field contrast. Replaced opaque placeholders with resolved high-contrast tokens, normalized text-transform for readability, and standardized native select placeholder states.
- **2.27.1**: Z-Index Layering Hotfix. Corrected a systemic bug in z-index calculation that caused drawer content to fall behind backdrops in User Profile, Approval Center, and Purchasing Help panels.
- **2.27.0**: Scoped Admin Controls & Global Search Refinement. Implemented a restricted role-assignment matrix for Local Managers, allowing they to assign Area Approver within their scope. Enforced strict plant/department data filtering in User Management and Receiving. Fixed high-contrast search placeholder styling.
- **2.26.0**: Instruction Layer Cleanup & Baseline Rebuild. Consolidated fragmented permission and status rules into unified directives. Streamlined the process lifecycle and reorganized legacy documentation into reference storage.
- **2.25.1**: Tooltip Positioning Fix. Optimized the shared `Tooltip` component API to support explicit side-anchoring and alignment, resolving overflow regressions in the User Management drawer.
- **2.25.0**: Role Selection UX & UI Stability. Implemented contextual role tooltips for User Management and fixed a critical white screen regression by restoring the core `ROLES` constant. Standardized table header readability across operational modules.
- **2.24.0**: Brand Identity & Favicon Integration. Implemented a comprehensive favicon set based on the "A2 P-G" corporate logo, replacing the default Vite identity. Optimized for various devices (mobile, desktop, apple-touch). Also fixed a critical table header readability bug in the Master Data module.
- **2.23.0**: Request Edit Persistence & Buyer Notifications. Hotfixed the controlled-edit persistence regression and implemented automatic buyer notifications for requester updates in the quotation stage. Restored clickable request numbers and optimized list column widths.
- **2.22.0**: Modern Corporate UI Refinement (Phase 2). Significant interactive and visual elevation across high-traffic operational screens (Dashboard, Requests, Receiving). Replaced brutalist remnants with Soft Elevation and premium typography.
- **2.21.0**: Modern Corporate Visual Foundation (Phase 1). Transitioned the core design system to Soft Elevation, rounded corners (8px/12px), and refined border tokens. Initial implementation of the premium corporate aesthetic.
- **2.19.0**: Refined Cost Center Validation (DEC-090). Area Approval logic now differentiates between request types. PAYMENT requests with unified Cost Centers across items are automatically validated and read-only. Inconsistent or missing Cost Centers for PAYMENT, and all QUOTATION requests, still require explicit mandatory selection.
- **2.18.1**: Sidebar Accordion Refinement. Implemented a single-open model for expanded navigation and optimized route-awareness logic to reduce vertical bloat and improve navigation speed.
- **2.18.0**: Sidebar Hover Flyouts (Navigation Overhaul). Interactive side panels (flyouts) for collapsed navigation with anti-flicker delay, portal rendering, and intelligent positioning.
- **2.17.0**: Phase 2 Security Hardening & Copy Flow. Implemented IP-based rate limiting, brute-force lockout policy, and strict attachment whitelisting. Includes the Anti-Accumulative Copy Request Flow (template-driven duplication).
- **2.15.0**: Anti-Accumulative Copy Request Flow. Implemented a template-driven, frontend-first duplication process that excludes downstream operational data to ensure fresh entries. Includes UX safeguards like "Descartar Cópia" and navigation protection.
- **2.14.0**: Global UI Layering & Z-Index Standardization. Unified z-index hierarchy across the portal, eliminated stacking context traps in AppShell, and standardized all overlays (Drawers, Modals, Popovers, Tooltips) using centralized Z_INDEX constants and DropdownPortal.
- **2.13.4**: Corrected a validation bug in the `Resubmeter Pedido` flow. High-level resubmission for requests in adjustment phases now correctly accounts for items contained within saved quotations, preventing false-positive "zero items" errors.
- **2.12.2**: Role-Aware Decision Intelligence (DEC-084). Adapted DecisionInsightsPanel to provide contextually different emphasis for Area Approvers (Checklist de Legitimidade) and Final Approvers (Visão Financeira Comparativa). Role-based section reordering. No backend changes.
- **2.106.0**: Purchase Request Notification Priority Fixes. Fixed missing DepartmentId in Finance/Quotation events, added Requester+Buyer to FINAL_APPROVED recipients, corrected RESUBMIT routing from REQUEST_SUBMITTED to AREA_APPROVED.
- **2.104.0**: Buyer Requested Items Section. Added "Itens Solicitados no Pedido" read-only section to the Buyer Quotation Management view with catalog/manual type badges, priority indicators, and item count.
- **2.103.0**: Requests Floating Mode Persistence. The "Flutuante Ativo / Inativo" UI toggle on the Requests dashboard now correctly saves its state to `localStorage`.
- **2.12.1**: Resizable Approval Center Drawer. Implemented horizontal resizing with localStorage persistence and desktop-optimized reflow for decision insights.
- **2.12.2**: Fixed AlplaPROD integration testing. Restored database configuration cascade priority, fixing false 'disabled' validation errors and synchronizing the factory logic.
- **2.12.0**: Approval Center UX Refinement. Replaced stacked layout with a high-efficiency right-side drawer/panel workspace. Implemented auto-selection of next pending items and distinct queue-linked selection visual cues.
- **2.11.5**: Fixed Cartesian Explosion in Quotation Management. Optimized backend hydration via `.AsSplitQuery()` to improve load times and resolve EF Core warnings.
- **2.11.4**: Quotation Save Confirmation. Implemented mandatory UX confirmation prior to saving/updating quotations, with contextual messaging for OCR vs. Manual entries.
- **2.11.3**: EF Core Query Optimization. Resolved non-deterministic warnings and Cartesian Explosion issues via explicit Ordering and `.AsSplitQuery()` in core request modules.

- **2.11.2**: TOTAL FILTRADO KPI Trend (MoM). Added MTD vs PMTD comparison with multi-currency safety and subtle Layout Option A indicator.

- **2.11.1**: TOTAL FILTRADO KPI Card. Replaced 'Finalizados' with monetary aggregate of filtered requests. Implemented multi-currency protection.

- **2.11.0**: Automating Departmental Area Approvers (DEC-082). Integrated Department Responsible as default Area Approver. Added Responsible field to Department master data with role-based filtering and backend schema extension.
- **2.10.9**: Proper Master Data UI Standardization. Switched to the system-standard `KebabMenu` component in all Master Data sections for full parity with the Requests list UI. 
- **2.10.8**: Initial Master Data Row Action Migration.

- **2.10.7**: Real Cost Centers & Plant-Based Filtering. Replaced test CCs with 5 operational Cost Centers (Viana 1/2/3). Added PlantId FK, mandatory plant selector in Master Data, and per-item CC/Plant validation in AddLineItem and UpdateLineItem.
- **2.10.6**: Requester Hover UX. Added a contextual tooltip to the Request Number in the list to display the requester's name.
- **2.10.5**: Payment Submission Fix & Line Item Refactor. Fixed 400 validation errors on submission for Payment requests and integrated mandatory Cost Center/IVA at the line item level.
- **2.10.4**: Request Form Layout Optimization. Increased the horizontal space for "Create" and "Edit/View" request screens to 1440px, aligning with the improved Requests List page for better desktop usability.
- **2.10.4**: Conditional Need Date Restoration. Re-introduced the "Data de Necessidade" field with conditional visibility and mandatory validation for Quotation requests. Refined grid layout and added smooth transitions.
- **2.10.1**: Modernized Login & Request Creation UX. Updated login animation and removed redundant text. Relocated Request Creation attachments to a contextual inline area below the justification field for improved flow.
- **2.9.17**: Strict Quotation Workspace Filtering. Enforced backend-level request type filtering to prevent Payment requests from appearing in the quotation management area. Added performance indexes.
- **2.9.16**: Quotation Workflow Locking. Implemented strict read-only boundaries for quotations after the quotation phase ends, including backend guards and frontend action hiding.
- **2.9.15**: Quotation Completion Validation Fix. Resolved a false-positive "zero items" error by correctly summing request-level and quotation-level line items.
- **2.9.14**: Session Security Fix. Migrated auth storage to `sessionStorage` to enforce tab-scoped access and logout on browser close.
- **2.9.13**: Modal Layering Fix. Standardized z-index stacking for User Management, Approval, and Receiving modals using React Portals.
- **2.9.12**: Line-Item Persistence Bug. Fixed audit log constraint violations during line-item updates.
- **2.9.11**: Buyer OCR & Admin Logs Auth standardization.
- **2.9.10**: Auth Connectivity Fix. Restored API credential injection for Admin settings.

- **2.9.7**: Zero-Trust Authorization & Legacy Persona Purge. Complete removal of "Visualizar Como" (User Mode) simulation. Implemented centralized Role Constants and claims-based authorization across all module controllers and the frontend API layer.
- **2.9.6**: Fixed visual display of auto-selected Plant when Company is changed.
- **2.9.5**: Corrected Plant/Company scope logic to ensure the Plant field remains interactive for users with multiple scopes, dynamically auto-selecting only when logically necessary.

- **2.9.4**: Plant-Scope-Based Request Restrictions. Implemented mandatory access control for the "Empresa" and "Planta" fields in the New Request flow, including frontend filtering, backend `403 Forbidden` enforcement, and data consistency validation.

- **2.9.3**: Improved User Profile Transparency. Transformed the "Meu Perfil" area into a professional account management interface with dynamic mapping for plants/departments, explicit status indicators, and integrated account actions.
- **2.9.2**: Restricted "Configurações" (Settings) menu access to `System Administrator` role only.
- **2.9.1**: Robust Notification Read-State UX. Implemented a role-aware notification engine with persistent read-states, operational workflow synchronization, and "Mark All as Read" / "Clear Read" management for optimized actor efficiency.
- **2.9.0**: Compras & Logística Management Cockpit. Implemented the new top-level workspace with a centralized dashboard (KPIs, Attention Panel, Quick Actions) and an interactive "Manual de Operação" side drawer for operational guidance.
- **2.8.0**: Account & Notification Modernization. Implemented a read-only Profile side drawer, integrated the "Alterar Palavra-passe" flow into the AppShell, and launched a real-time actionable notification engine for operational actors (Buyers, Approvers, Finance, Receiving).
- **2.7.0**: UI/UX Standardization Pass. Brought Login and User Management screens to the standardized UI design. Converted User Management editing to a Drawer-based pattern.
- **2.6.0**: User Management Administration UI. Integrated a high-density management table for users, roles, and plant/department scopes with stricter Local Manager subset logic.
- **2.4.1**: Synchronized Urgency Indicators. Integrated overdue and due-soon visual logic into the Buyer / Gestão de Cotações workspace with refined finalized status exclusions.
- **2.4.0**: Formalized KPI Dashboard & Summary Cards Standard. Added normative directives and UI standards for robust dashboard patterns.
- **2.1.0**: Management Dashboard KPIs. Integrated interactive KPI summary cards with a unified backend data flow and standardized design.
- **2.0.6**: Improved Requests List Filtering. Updated `RequestsController` to filter by strict Request Header `PlantId`.
- **2.0.5**: Fixed Backend Plant Persistence. Corrected DTOs and RequestsController to ensure `PlantId` is saved and projected correctly across the request lifecycle.

- **0.4.0**: Consolidated Urgency Fields. Removed redundant 'Priority' Master Data in favor of a definitive 'Grau de Necessidade do Pedido' at the Request Header level. Added numeric 'Prioridade do Item' scaling to Line Items for granular purchasing triage.
- **0.3.1**: Fixed Line Item validation bug where ASP.NET 400 errors caused the `RequestEdit` line item modal to unmount. Separated formal Request Status from the current Stage Actor in the main requests list table for UI clarity.
- **0.3.0**: Added Project-wide UX Standardization (Inline Form Validation and Reusable Currency Input Masking).

Semantic Versioning (MAJOR.MINOR.PATCH)

- **PATCH**: correções, pequenos ajustes seguros, melhorias sem impacto funcional relevante
- **MINOR**: novas funcionalidades, novos fluxos, novos scripts, mudanças compatíveis
- **MAJOR**: mudanças que quebram compatibilidade, redesign relevante, alteração de contrato/schema

## Current Status

Active Development

## Version Notes

- Manutenção de Auditoria e Performance de Banco de Dados.
- Redução de Avisos de Query Não-Determinística (EF Core).
- Prevenção de Explosão Cartesiana em Projeções Complexas.
