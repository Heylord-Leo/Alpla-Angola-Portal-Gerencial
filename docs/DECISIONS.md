# Decisions Log

Purpose: record important technical and process decisions so future work preserves context.

## DEC-143 — Accounts Payable Email Notification System

- **Date:** 2026-06-23
- **Status:** Accepted
- **Context:** The Portal Gerencial processes payment requests for two companies (Plastic and Sopro), each with a dedicated external Accounts Payable mailbox. When a payment request reaches scheduling or completion status, the AP team needs to be notified by email. The AP mailboxes are NOT portal users — they are external inbox addresses. The notification must be company-specific, configurable by administrators, and must not block the payment workflow if email delivery fails.
- **Decision:**
    1. **Dedicated Configuration Table (Option B):** Created `AccountsPayableNotificationConfigs` with fields: `CompanyId` (unique FK), `Email`, `CcEmails` (semicolon-separated), `Label`, `IsActive`, `NotifyOnScheduled`, `NotifyOnCompleted`, audit timestamps. Rejected the simpler Option A (adding a nullable field to `Companies` table) because it does not support CC, activation toggles, event-level control, or future extensibility.
    2. **Dedicated Log Table:** Created `AccountsPayableNotificationLogs` with a filtered unique index `IX_ApNotifLogs_Dedup` on `(RequestId, EventCode, RecipientEmail) WHERE Success=1 AND Skipped=0` for duplicate prevention.
    3. **CC Handling:** CC emails are sent as real CC on the email message, not as separate email sends. `IEmailService.SendEmailAsync` extended with an optional `ccRecipients` parameter. `ApplyEnvironmentPolicy` clears both `message.To` and `message.CC` in non-production environments.
    4. **Non-Blocking Integration:** `WorkflowNotificationOrchestrator.EmitAsync` wraps AP notification calls in a try-catch. Failures are logged but do not propagate — the payment status change always succeeds regardless of email delivery outcome.
    5. **Company Routing:** `CompanyId` added to `WorkflowEvent` payload. `FinanceController` endpoints now include `CompanyId` when emitting `PAYMENT_SCHEDULED` and `PAYMENT_COMPLETED` events. The orchestrator resolves the AP config by `CompanyId` from the event payload.
    6. **Master Data UI:** AP configs are managed via a new "📧 E-mails Contas a Pagar" tab in the existing Master Data page. No new routes, drawers, or modals. Administrators can create, edit, and delete configs per company. No auto-seeding — administrators manage all configs manually.
    7. **No Token/Auth for AP emails:** AP mailboxes are external recipients, not portal users. No user account creation or authentication is involved.
- **Alternatives considered:** (1) Adding a single `AccountsPayableEmail` column to the `Companies` table (Option A — rejected: no CC support, no activation toggle, no event-level control, no logging). (2) Sending CC as a separate email call (rejected: not real CC, would appear as a separate email in the recipient's inbox). (3) Auto-seeding config rows for known companies (rejected: user requested administrator-managed configs via UI).
- **Consequences:** AP email notifications are fully configurable per company. The system is extensible for future companies. Email delivery failures are tracked in the log table. Non-production environments are protected from accidental external emails. The dedup index prevents duplicate notifications on retries.

---

## DEC-142 — IT Equipment Module Master Data & Catalogs

- **Date:** 2026-06-12
- **Status:** Accepted
- **Context:** The IT Equipment module relied on free-text fields for Manufacturer, Model, Processor, Memory, and Plant. Delivery Terms also used free-text fields for Department and Plant. This caused data inconsistency, typo-driven duplication, and poor reporting capability. Furthermore, Company/Plant dependencies were not enforced.
- **Decision:**
    1. **Master Data Integration:** `ITEquipmentDeliveryTerm` now links directly to Master Data via `CompanyId`, `PlantId`, and `DepartmentId` foreign keys.
    2. **Admin Catalogs:** Created `ITEquipmentManufacturer`, `ITEquipmentModel`, `ITEquipmentProcessor`, and `ITEquipmentMemoryOption` lookup tables. These are manageable by IT admins via a new "Gerir Catálogos" modal.
    3. **Denormalized Persistence Strategy:** To preserve backward compatibility and avoid a massive data migration in this phase, the new UI dropdowns resolve the selected catalog item names and save them as flat strings into the existing `ITEquipment` text columns (`Manufacturer`, `Model`, etc.).
    4. **Cascading Dropdowns:** UI forms now enforce logical cascades: Company must be selected before Plant; Plant is filtered by Company; Manufacturer must be selected before Model; changing Manufacturer clears the Model.
    5. **Guided Tour:** A dedicated IT Equipment Guided Tour was added to onboard users to the new catalog features.
- **Consequences:** Data entry is now standardized and typo-proof. Existing records remain intact without requiring destructive schema changes. Admin users have self-service control over available equipment options. The technical debt of denormalized fields in `ITEquipment` remains but is now fed by clean, structured inputs.

---

## DEC-141 — Supplier Ficha Primavera Import Enrichment & Safe Update Rule

- **Date:** 2026-06-08
- **Status:** Accepted
- **Context:** Suppliers imported from Primavera via the Sync module were created with only Name, NIF, and PrimaveraCode. The Supplier Ficha detail screen already had UI sections for Address, Primary/Secondary Contact, Banking, and Payment Terms, but these were always empty after import. Investigation of the Primavera `Fornecedores` table confirmed that address (`Morada`, `Morada1`, `Local`, `Cp`, `Pais`), contact (`Tel`, `Email`), banking (`IBAN`, `Swift`, `NumCB`), and payment (`CondPag`, `ModoPag`) columns are available. Contact person name/role and secondary contact are NOT available from Primavera.
- **Decision:**
    1. **Extended DTO & SQL**: `PrimaveraSupplierDto` extended with 5 banking/payment properties. `PrimaveraSupplierService` SQL query extended with 12 additional columns (address, contact, banking, payment). A safe column detection mechanism attempts extended columns first; on `SqlException 207` (invalid column name), it falls back to the base column set and caches the result for the service lifetime.
    2. **Composite address**: Address is built by joining non-empty parts of Morada, Morada1, Local, Cp, Pais with ", " separator. Stored as a single string in `Supplier.Address`.
    3. **Import enrichment**: Both `/suppliers/import` and `/suppliers/import-reviewed` endpoints now populate all available enrichment fields when creating new suppliers. The reviewed import re-fetches the full Primavera record to access enrichment data that was not part of the review DTO.
    4. **Safe update rule**: When re-importing a supplier whose PrimaveraCode already exists in the Portal:
       - If status is `DRAFT` or `PENDING_COMPLETION`: only empty/null Portal fields are filled from Primavera. Manually entered values are never overwritten.
       - If status is `ACTIVE`, `PENDING_APPROVAL`, or `ADJUSTMENT_REQUESTED`: the supplier is skipped entirely (no modifications).
       - Each safe-update logs which fields were filled and which were skipped (already had manual data).
    5. **Unavailable fields**: Contact person name (`ContactName1/2`), contact role (`ContactRole1/2`), and secondary contact (`ContactName2`, `ContactPhone2`, `ContactEmail2`) are documented as unavailable from Primavera and remain empty for manual entry.
    6. **Diagnostic logging**: Each import/update logs a structured `[SYNC]` line showing data group availability (Address yes/no, Phone yes/no, IBAN yes/no, etc.).
    7. **No frontend changes**: The existing Supplier Ficha detail screen already binds to all enrichment entity properties.
- **Alternatives considered:** (1) Adding new Primavera tables/views to get contact person name (rejected: not available in the standard Fornecedores scope; would require Primavera customization). (2) Overwriting all fields on re-import (rejected: would destroy manually entered data). (3) Creating a separate "enrich" endpoint (rejected: unnecessary — the safe-update logic integrates cleanly into the existing import flow).
- **Consequences:** Newly imported suppliers will have richer data from day one. Existing DRAFT/PENDING_COMPLETION suppliers can be re-imported to fill gaps without losing manual work. The safe column detection ensures compatibility with different Primavera installations that may not have all columns.

---

## DEC-140 — HR Attendance API Access-Control Hardening

- **Date:** 2026-06-05
- **Status:** Accepted
- **Context:** The DEC-140 investigation report (HR Module Access Control) identified a critical security finding: `HRAttendanceController` had an `[AllowAnonymous]` diagnostic endpoint (`GET /api/hr/attendance/test-verify/{id}/{date}`) that exposed attendance data for any employee without authentication and leaked full stack traces. Additionally, all production attendance endpoints (`GetCalendar`, `GetDayDetail`, lookup endpoints) lacked explicit HR module entitlement checks — they relied only on `[Authorize]` (any authenticated user) plus data scoping, which was inconsistent with `HRLeaveController` that has `HasHRModuleAccess()` gates.
- **Decision:**
    1. **Remove the anonymous test endpoint entirely.** It was a development artifact that should not exist in production code. The production `GetDayDetail` endpoint provides the same data with proper authentication and scope validation.
    2. **Add `HasHRModuleAccess()` to HRAttendanceController**, mirroring the exact logic from `HRLeaveController`: System Administrator, HR, Local Manager, Department Manager, or self-calendar (email-matched HREmployee record). Applied as the first check in `GetCalendar`, `GetDayDetail`, `GetAbsenceCodes`, and `GetWorkCodes`.
    3. **Do not change sidebar behavior.** The "Gestão da Equipa" menu visibility for `Viewer / Management` users is intentional and will be evaluated as a separate decision. The backend correctly scopes data to self-only for these users.
    4. **Do not change diagnostic endpoints.** The Portal-side diagnostic endpoints (`portal/resolve-schedule`, `portal/interpret-punches`, `portal/compare`, `portal/compare-range`) already have proper inline `IsAdminOrHR` role checks. The monthly report endpoint already has `[Authorize(Roles = "System Administrator,HR")]`.
- **Alternatives considered:** (1) Restrict the test endpoint to System Administrator only instead of removing it (rejected: the production `GetDayDetail` endpoint serves the same purpose with proper auth and scope). (2) Add `[Authorize(Roles = "System Administrator,HR,...")]` at controller level instead of per-endpoint `HasHRModuleAccess()` checks (rejected: too coarse — would block self-calendar users who need `GetCalendar` and `GetDayDetail`). (3) Create a shared base class or middleware for `HasHRModuleAccess()` (deferred: keeping it as a private method in each controller mirrors the existing pattern and avoids architectural changes in a security-focused patch).
- **Consequences:** Unauthenticated users can no longer access any HR attendance endpoint. Authenticated users without HR module entitlement receive 403 Forbidden. Existing behavior for System Administrator, HR, Local Manager, Department Manager, and Viewer/Management (self-calendar) users is unchanged. No frontend changes, no sidebar changes, no database changes.

---

## DEC-140 — Automatic Visual Environment Differentiation (TEST vs PROD)

- **Date:** 2026-06-05
- **Status:** Accepted
- **Context:** The same application codebase is deployed to both TEST (`portalgerencial-test.alpla.net`) and PRODUCTION (`portal.alpla.com`) environments. Users — especially administrators and testers — had no visual indication of which environment they were using, creating a risk of performing real operations (approvals, payments, data modifications) in PRODUCTION while believing they were in TEST. The system needed an automatic, non-invasive visual differentiation mechanism without duplicating frontend logic or creating separate builds.
- **Decision:**
    1. **Backend-driven configuration:** A new `AppEnvironment` section in `appsettings.json` defines `Code` (PROD/TEST), `Name` (display label), and `ShowBanner` (boolean). PROD defaults are: `Code = "PROD"`, `ShowBanner = false`. TEST overrides are applied via IIS environment variables (`AppEnvironment__Code = TEST`, `AppEnvironment__ShowBanner = true`) or `appsettings.Development.json` (gitignored).
    2. **Anonymous API endpoint:** `GET /api/app/environment` (in `AppController.cs`) returns the environment configuration without requiring authentication, enabling the login and password-reset pages to display the banner before user login.
    3. **Frontend URL fallback:** `EnvironmentContext.tsx` first fetches from the API. If the API is unreachable (e.g., during initial load), it falls back to URL-based detection (`localhost` or `test` in hostname → TEST). This ensures the banner appears even if the backend is slow to respond.
    4. **Single rendering path:** `EnvironmentBanner.tsx` is a fixed-position amber banner at the top of the viewport. It is rendered once per context: `AppShell.tsx` for authenticated pages, and directly in `LoginPage.tsx` / `ResetPasswordPage.tsx` for public pages. No duplication.
    5. **Layout offset via CSS variable:** `--env-banner-height: 32px` offsets the topbar, sidebar, and main content area when the banner is visible. PROD layout is completely unchanged (variable set to `0`).
    6. **Sidebar TEST badge:** An amber pill badge with "TEST" text appears in the sidebar header area.
    7. **Browser title prefix:** `[TEST] Portal Gerencial` is set via `useEffect` in the context provider.
    8. **Fullscreen/LiveBoard support:** `OperationsLiveBoardPage.tsx` renders a compact 24px amber inline strip instead of the full fixed banner.
    9. **Print safety:** The banner is hidden in `@media print` rules.
    10. **PROD is always the default:** If no `AppEnvironment` section exists in configuration, the system behaves as PROD (no banner, no badge, no title prefix).
- **Alternatives considered:** (1) Build-time environment variables (`VITE_ENV=TEST`) injected during CI/CD (rejected: requires separate builds per environment, violates single-codebase principle). (2) URL-only detection without backend (rejected: fragile — URL patterns can change, and the backend is the authoritative source). (3) A watermark overlay instead of a banner (rejected: less visible, harder to read, and may interfere with screenshots/reports).
- **Consequences:** Users immediately see which environment they are in upon page load. PROD remains visually clean. The IIS deployment team must set 3 environment variables for TEST; PROD requires no changes. The banner and badge are purely informational — they do not affect application logic or data flow.

---

## DEC-137 — Disable Automatic Database.Migrate() in Non-Development Environments

- **Date:** 2026-06-04
- **Status:** Accepted
- **Context:** In v2.185.8, both TEST and PRODUCTION on AOVIA1VMS011 failed with `HTTP Error 500.30 — ASP.NET Core app failed to start`. Root cause: `Program.cs` calls `context.Database.Migrate()` on startup, but the IIS runtime identity (`usr_portalgerencial_test` / `usr_portalgerencial`) does not have DDL/db_owner permissions on the target database. The migration attempt crashed the ASP.NET Core process before Kestrel could start, producing an opaque 500.30 error with no diagnostic output (stdout logging was disabled in `web.config`). The issue was compounded by the lack of pre-deployment migration validation in the GitHub Actions workflows.
- **Decision:**
    1. **Environment-aware migration handling in `Program.cs`**: In Development, keep `Database.Migrate()` for local iteration. In all other environments (Test, Staging, Production), do NOT call `Database.Migrate()`. Instead, call `context.Database.GetPendingMigrations()` to detect unapplied migrations. If pending migrations exist, log each missing migration ID and crash with a descriptive `InvalidOperationException` that includes remediation instructions. The application never attempts DDL operations outside Development.
    2. **GitHub Actions pre-start migration check**: Both `deploy-test.yml` and `deploy-prod.yml` now include a "Check for pending EF Core migrations" step that runs **before** starting the IIS App Pools. The step reads the connection string from the preserved `appsettings.*.json`, queries `__EFMigrationsHistory`, compares against the expected migration list, and fails the deployment with `::error::` annotations if any migrations are pending. The production workflow also includes a safety check that blocks deployment if the connection string resolves to `[Portal-Gerencial-Test]`.
    3. **Reusable migration comparison script**: `scripts/db/check-pending-migrations.ps1` compares expected migrations (hardcoded list maintained in sync with the Migrations folder) against the database's `__EFMigrationsHistory`. Reports applied, pending, and unknown migrations. Returns exit code 0/1.
    4. **No DDL permissions for IIS runtime**: The IIS runtime identity only needs `db_datareader` and `db_datawriter`. All schema changes are applied manually using a DBA-level account via SSMS or `sqlcmd` before deployment.
    5. **Manual migration workflow**: Developers must (a) run `check-pending-migrations.ps1` to identify pending migrations, (b) generate an idempotent SQL script via `dotnet ef migrations script`, (c) review and apply via SSMS/sqlcmd, (d) verify `__EFMigrationsHistory`, (e) deploy via GitHub Actions. The workflow validates the result before starting the App Pool.
- **Alternatives considered:** (1) Granting db_owner/DDL to the IIS runtime identity (rejected: violates least-privilege principle and creates security risk). (2) Running migrations in the GitHub Actions workflow with a separate DBA connection string (rejected: adds complexity, requires new secrets management, and the team prefers manual review of SQL scripts before execution). (3) Warn-and-continue behavior (rejected: an API running against a stale schema would produce unpredictable data errors that are harder to diagnose than a clear startup crash).
- **Consequences:** The API will never start with a stale schema. Deployment failures caused by missing migrations now produce clear, actionable diagnostics instead of opaque 500.30 errors. The tradeoff is that every release with schema changes requires a manual migration step before deployment. The deployment workflow provides a safety net to catch missed migrations.

---

## DEC-138 — Missing .Designer.cs Files for Two EF Core Migrations (Technical Debt)

- **Date:** 2026-06-04
- **Status:** Accepted (Technical Debt)
- **Context:** During the v2.185.9 TEST migration application, `dotnet ef migrations script --idempotent` generated a script with only 50 of 52 expected migration INSERTs. Investigation revealed that two migration files are missing their corresponding `.Designer.cs` partial class files:
    - `20260421155149_AddContractDocumentSoftDelete.cs` — missing `20260421155149_AddContractDocumentSoftDelete.Designer.cs`
    - `20260425101500_AddAttendanceJustifications.cs` — missing `20260425101500_AddAttendanceJustifications.Designer.cs`
    
    Without the Designer file (which contains the model snapshot at that point in the migration chain), EF Core tooling cannot include these migrations in auto-generated scripts. The likely cause is that these migrations were manually created or their Designer files were accidentally deleted/not committed.
- **Decision:** Manually craft idempotent SQL scripts derived directly from the migration `Up()` methods for environments that need these migrations applied. Do NOT regenerate the Designer files or consolidate migrations at this time to avoid disrupting the migration chain. Document as technical debt for future resolution.
- **Impact:**
    - `dotnet ef migrations script --idempotent` will skip these two migrations until resolved.
    - Future full-idempotent script generation will always produce 50/52 INSERTs unless Designer files are restored.
    - Manual migration scripts (`scripts/db/apply-test-missing-migrations-v2-185-9.sql`) are the workaround.
- **Future resolution options:** (1) Regenerate Designer files for both migrations (requires careful snapshot alignment). (2) Include both migrations' DDL in a future consolidation migration. (3) Accept the current state since the manual scripts work correctly.
- **Related files:**
    - `scripts/db/apply-test-missing-migrations-v2-185-9.sql` — manually crafted idempotent SQL
    - `scripts/db/apply-test-v2-185-9-server.ps1` — server-side automation script

---

## DEC-139 — Automated EF Core Migration Workflows via GitHub Actions

- **Date:** 2026-06-04
- **Status:** Accepted
- **Context:** After DEC-137 disabled `Database.Migrate()` in non-Development environments, every release with schema changes required manual RDP access to AOVIA1VMS011, manual SQL script crafting, and manual `sqlcmd` execution. This process was error-prone (as demonstrated by COMPRESSION and RAISERROR syntax issues during v2.185.9 TEST/PROD migration) and slow. Additionally, the expected migration list was hardcoded in three separate files (`check-pending-migrations.ps1`, `deploy-test.yml`, `deploy-prod.yml`), requiring manual synchronization for every new migration.
- **Decision:**
    1. **Auto-generated migration list (eliminates hardcoded arrays):** A new script `scripts/db/get-expected-migrations.ps1` scans the EF Core migrations folder (`src/backend/AlplaPortal.Infrastructure/Data/Migrations/`) and derives the expected migration IDs from filenames. It excludes `*.Designer.cs` and `*Snapshot.cs` files. The build step in `deploy-test.yml` and `deploy-prod.yml` now generates an `expected-migrations.txt` file packaged with the API artifact. The deploy jobs read from this file instead of inline arrays. `check-pending-migrations.ps1` also calls the auto-generation script.
    2. **Apply TEST Migrations workflow (`apply-migrations-test.yml`):** Manual-dispatch GitHub Actions workflow. Checks out the repo, builds the backend, detects pending migrations, backs up `Portal-Gerencial-Test`, generates idempotent SQL via `dotnet ef`, validates the SQL covers all pending migrations (DEC-138 protection), applies via `sqlcmd`, and verifies `__EFMigrationsHistory`. Exits successfully with a notice if no migrations are pending.
    3. **Apply PRODUCTION Migrations workflow (`apply-migrations-prod.yml`):** Same logic as TEST but with additional safety: requires `YES-PROD` confirmation input, validates the target database is `Portal-Gerencial` (blocks if it detects `Portal-Gerencial-Test`), and uses the GitHub `production` environment with approval gates.
    4. **Reusable `apply-migrations.ps1` script:** Shared by both workflows. Accepts environment, connection string, backup directory, and expected database name as parameters. Implements the full lifecycle: detect → backup → generate → validate → apply → verify.
    5. **DEC-138 protection:** Before applying any SQL, the workflow validates that the generated idempotent script references all pending migration IDs. If any are missing (e.g., due to missing `.Designer.cs` files), the workflow fails with a clear error pointing to DEC-138.
    6. **Deploy workflows remain safety nets:** The existing pending migration checks in `deploy-test.yml` and `deploy-prod.yml` remain active. They block deployment if any migrations are pending, regardless of whether the Apply Migrations workflow was used.
- **Invariants preserved (DEC-137):**
    - `Database.Migrate()` remains disabled in non-Development environments.
    - IIS runtime identities remain `db_datareader` + `db_datawriter` only.
    - Deploy workflows still block on pending migrations.
    - Production requires explicit `YES-PROD` confirmation and database identity validation.
- **Alternatives considered:** (1) Re-enabling `Database.Migrate()` with a DBA connection string at startup (rejected: violates DEC-137 principle of no auto-migration). (2) Using GitHub Actions secrets for inline SQL execution (rejected: less auditable than `dotnet ef` generated scripts). (3) Keeping hardcoded migration lists (rejected: error-prone, already caused maintenance burden).
- **Consequences:** Migration application is now a one-click GitHub Actions workflow instead of a multi-step RDP process. New migrations only require adding the file to the Migrations folder — no manual list updates needed. The DEC-138 validation gate prevents silent failures from missing Designer files.
- **Related files:**
    - `scripts/db/get-expected-migrations.ps1` — auto-generates migration list from filesystem
    - `scripts/db/apply-migrations.ps1` — reusable migration application script
    - `.github/workflows/apply-migrations-test.yml` — TEST migration workflow
    - `.github/workflows/apply-migrations-prod.yml` — PRODUCTION migration workflow

---

## DEC-136 — Supplier PortalCode D6 Standardization

- **Date:** 2026-06-03
- **Status:** Accepted
- **Context:** Creating a supplier from the OCR/proforma flow (QuickSupplierModal) failed with `Cannot insert duplicate key row in object 'dbo.Suppliers' with unique index 'IX_Suppliers_PortalCode'`. Investigation revealed two interacting bugs: (1) `SyncController` import endpoints generated PortalCodes in D4 format (`SUP-0003`, 8 chars) while `LookupsController.GetNextPortalCodeAsync` generated D6 format (`SUP-000003`, 10 chars), and (2) the self-healing parser in `GetNextPortalCodeAsync` required `maxCodeStr.Length == 10`, silently ignoring any D4 codes already in the database. This caused the SystemCounters to regress and produce codes that collided with seed data.
- **Decision:**
    1. **D6 as canonical standard**: All PortalCode generation paths now use `$"SUP-{seq:D6}"` — producing 10-character codes like `SUP-000001`. This applies to `LookupsController.GetNextPortalCodeAsync`, `SyncController.SupplierImport`, and `SyncController.SupplierImportReviewed`.
    2. **Flexible parser**: A new `ParsePortalCodeSequence()` helper handles any `SUP-XXXX` numeric suffix format (D4, D5, D6+) by extracting `Substring(4)` and parsing numerically. The previous rigid `Length == 10` check was the root cause of the silent failure.
    3. **Client-side max resolution**: The self-healing query now materializes all `SUP-` codes and finds the numeric max on the client side, avoiding SQL alphabetic ordering issues with mixed-length strings (e.g., `SUP-0003` sorts higher than `SUP-000002` alphabetically but is numerically lower).
    4. **Retry-on-collision**: `CreateSupplier` wraps the save in a retry loop (max 3 attempts) that catches `IX_Suppliers_PortalCode` collisions, detaches the failed entity, and regenerates the code. This handles rare race conditions.
    5. **Sanitized error messages**: `DbUpdateException` messages are logged server-side but never exposed to the frontend. The UI receives a generic Portuguese error message.
    6. **SystemCounters alignment**: Both `SupplierImport` and `SupplierImportReviewed` now update the `SUPPLIER_PORTAL_CODE` SystemCounter after batch saves, ensuring subsequent calls to `GetNextPortalCodeAsync` don't regress.
- **Alternatives considered:** (1) Creating a data migration to normalize existing D4 codes in development to D6 (rejected: only dev has D4 data, TEST and PRODUCTION are empty). (2) Extracting code generation to a shared service class (rejected: the scope is limited to two controllers, and the SystemCounters self-healing pattern is already robust enough after the parser fix).
- **Consequences:** PortalCode generation is now safe against format inconsistencies and race conditions. The D6 format is the project standard. Existing D4 codes in development environments remain valid and are correctly parsed.

---

## DEC-135 — Security Incident Response & Unified SMTP Integration Consolidation

- **Date:** 2026-05-25
- **Status:** Accepted
- **Context:** A GitGuardian alert indicated that SMTP credentials were exposed historically in the repository's Git history (originating from a previously tracked `appsettings.Development.json` file). Immediate remediation was required to secure the active repository HEAD, establish rotation checklists, and prepare a history cleanup plan before proceeding to staging replication. Concurrently, a usability review of the new Integration Management Module (DEC-134) revealed a duplication in SMTP management interface: a legacy "SMTP" tab existed under "Dados Mestres" (Master Data) in `MasterData.tsx`, while a new "Email / SMTP Service" card was introduced under "Gestão de Integrações" (Integration Management). The two settings areas needed consolidation, completely removing SMTP from Master Data and hosting it solely under Integration Management, without introducing duplicate database entities or breaking existing email service encryption flows.
- **Decision:**
    1. **Immediate Security Incident Response:**
        - Created the official security incident report at `docs/SECURITY_INCIDENT_GITGUARDIAN_SMTP_SECRET_LEAK.md` detailing the alert, exposure analysis, mandatory credential rotation list, and history purge steps using `git-filter-repo`.
        - Purged the plaintext database password (`ad#56&Hfe`) from the tracked `scripts/query_innux.ps1` script, modifying it to dynamically resolve from the `INNUX_DB_PASSWORD` environment variable.
        - Confirmed that `appsettings.Development.json` is successfully untracked in HEAD and robustly ignored in `.gitignore` along with `secrets.json`, guaranteeing no plaintext credentials are tracked in active source control.
    2. **Master Data Cleanup:** Completely removed the SMTP tab, associated state hooks, panels, and import dependencies from the master settings page `MasterData.tsx`, ensuring that SMTP settings are no longer accessible under Master Data.
    3. **Unified Backend Configuration Routing:** Extended the unified integration service (`IntegrationSettingsService.cs`) to route the `"SMTP"` provider operations directly to the existing database-backed single-row `SmtpSettings` table, reusing the pre-existing AES encryption and connection mechanisms without creating duplicate entities or schemas.
    4. **Health Check Provider Abstraction:** Created scoped `IIntegrationProvider` implementations (`SmtpIntegrationProvider` and `OpenAiIntegrationProvider`) to handle connection health tests dynamically under the unified integrations endpoint. Deleted the legacy obsolete `SmtpSettingsController.cs`.
    5. **Administrative Connection Editing UI:** Introduced `ConnectionConfigureModal` inside `IntegrationSettings.tsx` to allow administrators to edit non-secret connection parameters (Host, Port, SSL, Sender Email, Sender Name, Usernames, URLs) in a clean modal form, while secret parameters remain strictly masked and manageable only via secure rotation fields.
    6. **Guided Onboarding Tour Impact:** Audited Joyride tour files and verified that the removal of legacy SMTP tab elements has zero impact on active guided onboarding flows.
- **Alternatives considered:**
    - Creating a new `SMTP` mapping model and schema under `IntegrationProviderSettings` (rejected: introduces database duplication and breaks backward compatibility with existing encrypted `SmtpSettings` table and mail transport flows).
    - Executing the Git history rewrite immediately (rejected: history purge requires repository lock and team coordination, so we staged the plan for explicit approval before execution).
- **Consequences:** The GitGuardian security leak is mitigated in the active HEAD, and the historical purge procedure is fully defined. SMTP configuration is consolidated into a single technical settings area under Integration Management, eliminating confusion and ensuring administrative settings are securely encrypted at rest, masked in transit, and validated in real time.

---

## DEC-134 — Integration Management Module: CRUD UI, Factory Refactoring & Frontend Type Safety

- **Date:** 2026-05-25
- **Status:** Accepted
- **Context:** The Portal Gerencial backend had full database schema support for `IntegrationProviderSettings` (with AES-encrypted secrets) since migration `20260414131442`, but no runtime service or UI consumed those rows. `PrimaveraConnectionFactory`, `InnuxConnectionFactory`, and `OpenAiDocumentExtractionProvider` all read configuration exclusively from `IConfiguration` (appsettings files / environment variables), creating a gap where the existing `IntegrationProviderSettings` entity was seeded but inert. Additionally, `appsettings.Development.json` tracked plaintext SQL credentials in Git — a known security finding. The frontend API layer used `Promise<any>` for all integration methods, suppressing type-safety checks at compile time.
- **Decision:** Implement a 4-phase Integration Management module:
    1. **Phase A — Architecture Review:** Conducted a full analysis of the 4-layer configuration cascade and documented findings in `docs/INTEGRATION_MANAGEMENT_ARCHITECTURE_REVIEW.md`. Identified 3 services requiring DB-first refactoring and 2 critical security findings (plaintext credentials in Git, hardcoded AES fallback key).
    2. **Phase B — CRUD API & Frontend UI:** New `IntegrationSettingsController` (7 endpoints), `IntegrationSettingsService`, and `IntegrationSettings.tsx` page at `/admin/integrations`. Settings are projected to the frontend via `IntegrationSettingsDto` which uses `hasPassword`/`hasApiKey` boolean flags — **decrypted secrets are NEVER transmitted to the frontend**. Secret rotation uses a dedicated `POST /secret` endpoint that accepts the new value and encrypts it via `AesEncryptionHelper` before persisting. Test connection delegates to the existing `IntegrationHealthService.TestProviderConnectionAsync()`.
    3. **Phase C — Factory Refactoring (DB-First Configuration):** Created `IntegrationConfigResolver` as a scoped DI service implementing a standardized 3-tier cascade: (a) Query `IntegrationProviderSettings` by provider code, decrypt secrets with `AesEncryptionHelper`, (b) Fall back to `IConfiguration.GetSection("Integrations:{code}")`, (c) Return a safe "not configured" result that prevents crashes. All 3 consumer services (`PrimaveraConnectionFactory`, `InnuxConnectionFactory`, `OpenAiDocumentExtractionProvider`) refactored to use the resolver.
    4. **Phase D — Frontend Type Safety:** Eliminated all `any` types from the integration API surface. Moved inline `IntegrationSettingsDto` to shared `types/index.ts`, added 3 additional DTO types, replaced all `Promise<any>` with typed returns, and replaced all `catch (err: any)` with `catch (err: unknown)` using safe `instanceof Error` checks. **Fixed a critical bug** where the test connection handler read `result.currentStatus` (from `IntegrationProviderStatusDto`) instead of `result.success` (from `IntegrationConnectionTestResultDto`), which would have caused all test results to display as failures.
    5. **Security Model:** Encryption at rest via `AesEncryptionHelper` (configurable key material via `AppConfig:EncryptionKey`). API GET never returns decrypted secrets. Dedicated `POST /secret` for password/key replacement. `AdminLogWriter` audit trail for all changes. `[Authorize(Roles = "System Administrator")]` on all endpoints. `IsReadOnly` flag supported for production lock-down.
    6. **Backward Compatibility:** Existing `appsettings.Development.json` values continue to work as fallback. No migration required for local development. Services that have no DB settings row simply fall back to `IConfiguration` as before.
- **Alternatives considered:** (1) Hardcoding a per-factory DB query (rejected: code duplication across 3+ factories). (2) Using `IOptionsMonitor` with DB-backed provider (rejected: unnecessary complexity — the resolver pattern is simpler and allows async DB access with encryption). (3) Implementing a full user-secrets migration in this phase (rejected: deferred — plaintext credentials in `appsettings.Development.json` are a known security finding documented for a future remediation phase).
- **Consequences:** System Administrators can now configure, test, and manage all integration providers from the Portal UI without direct database or server access. Runtime services automatically pick up DB-backed settings when available, while preserving backward-compatible fallback to appsettings. The AES encryption key should be overridden via environment variable in staging/production — the hardcoded fallback key is development-only.

---

## DEC-133 — AOVIA1VMS011 Deployment Architecture & Security Decisions

- **Date:** 2026-05-22
- **Status:** Accepted
- **Context:** Deploying the Alpla Angola Portal Gerencial (React + Vite SPA frontend, .NET 8 API backend, SQL Server relational database) to the shared VM server `AOVIA1VMS011` required aligning on critical infrastructure choices: database centralization vs local isolation, SSL/HTTPS bindings, security policy, and path-traversal remediation.
- **Decision:**
    1. **Local Database Isolation (Option A Accepted):** The Portal Gerencial production database will remain locally on `AOVIA1VMS011` as a dedicated database named `[Portal-Gerencial]`. Centralizing on `AOVIA1VMS012\SQLALPLA` is officially rejected. Because the name contains a hyphen, all SQL scripts and connection strings must reference it using bracket notation: `[Portal-Gerencial]`.
    2. **Strict Innux Database Segregation & Approved SQL Instance Reuse:** Under no circumstances will any existing `Innux`, `Innuxtime`, or `INUTIME` attendance databases on `AOVIA1VMS011` be touched, modified, or reused. The local databases `[Portal-Gerencial]` and `[Portal-Gerencial-Test]` must co-exist on the default general-purpose SQL Server 2019 instance (`MSSQLSERVER`). Following physical decommission validation which verified that the instance has **zero user databases** and **zero active connections**, the reuse of `MSSQLSERVER` is approved. Furthermore, the **Single-User Mode SQL Administrative Recovery** has been successfully executed and validated, promoting Leonardo's account `ALPLA\adm_cintra01` to the `sysadmin` server role, successfully restoring normal multi-user operation, and completely resolving the database administrative blocker.
    3. **SSL / HTTPS Provisioning:** To ensure robust communication security, HTTPS is planned from day one. A valid SSL certificate file is pre-deployed and locally accessible at `C:\dev\alpla-portal\82460ec13b4d0f90a349c960c5e45ac8.pfx`.
    4. **Secure Password Handling Policy:** The PFX certificate password must never be documented, stored in scripts, configuration files, or committed to source control. Import procedures (GUI Wizard or secure memory parameters in PowerShell using `Read-Host -AsSecureString`) are mandated.
    5. **Path Traversal Remediation:** To prevent file uploads from default-writing to `C:\data\attachments` and potentially exhausting system drive storage when deployed under IIS, the hardcoded path resolution in `AttachmentsController.cs` must be refactored to read from configuration key `AppConfig:UploadStoragePath`, targeting `D:\PortalGerencial\Attachments` in production.
    6. **Unified Single-Site Architecture:** IIS will host the static frontend on the root `D:\PortalGerencial\Frontend` and serve all `/api/*` traffic via the ASP.NET Core Module V2 (`hostingModel="InProcess"`) as a backend sub-application (`D:\PortalGerencial\Api`). No separate Kestrel port is exposed.
    7. **Backend Port Restriction (Port 5000 Unavailable):** Port 5000 is reserved/used intermittently by another service on `AOVIA1VMS011`. The backend must **never** bind to port 5000 or 5001 in either environment. The preferred hosting model is **IIS in-process** (ANCM), eliminating the need for an externally visible Kestrel port. If a standalone Kestrel port is technically needed, it must bind only to `localhost`/`127.0.0.1` on a confirmed free port (candidates: 5100, 5101, 8081) and must never be opened in Windows Firewall.
    8. **Dual-Environment Test/Staging Isolation:** A separate Test/Staging environment will be provisioned on `AOVIA1VMS011` alongside Production, using completely isolated resources: database `[Portal-Gerencial-Test]`, folder root `D:\PortalGerencial-Test`, IIS site `PortalGerencial.Test`, app pools `PortalGerencialTestAppPool`/`PortalGerencialTestApiPool`, and a dedicated SSL certificate (`C:\dev\alpla-portal\334ad6893b414f90a349c960c5e45af4.pfx`). Test and Production must never share databases, attachment folders, log directories, temp folders, or connection strings. Both folder structures include: Frontend, Api, Logs, Attachments, Backups, Packages, and Temp.
    9. **Integration Write-Safety Policy (Test/Staging):** Test/Staging may connect to OCR (OpenAI), Primavera, and Innux. However, only **read-only** access is initially enabled for Primavera and Innux. Email notifications are **disabled** in Test/Staging by default. Any integration with write capability (ERP write-back, Innux modifications, webhooks, external triggers) must remain disabled, sandboxed, or in dry-run mode until explicitly approved by Leonardo as a separate decision. Certificate passwords for both environments must never be stored in documentation, scripts, logs, or source control.
    10. **Database & Login Provisioning Strategy:** Provision separate, dedicated SQL Authentication logins to restrict database operations to minimal requirements: `adm_portalgerencial` (db_owner on both databases for schema management), `usr_portalgerencial` (db_owner temporarily on [Portal-Gerencial] for EF Core DDL migrations), and `usr_portalgerencial_test` (db_owner temporarily on [Portal-Gerencial-Test] for EF Core DDL migrations). Runtime credentials must be generated securely in-memory using cryptographically secure random pools with zero plaintext persistence. An SQL Express backup strategy using Windows Task Scheduler + sqlcmd scripts is approved to bypass SQL Server Agent unavailability.
    11. **Phase 3 Test/Staging Controlled Deployment and Verification:** Establish the staging deployment baseline for release v2.150.0. The SQL connection string password is securely inputted and persisted via IIS environment variables, with a known tradeoff that the connection string is stored in plaintext inside `C:\Windows\System32\inetsrv\config\applicationHost.config` (restricted strictly by OS ACLs to local Administrators and SYSTEM). To resolve this permanently, we mandate moving to Windows Authentication (trusted connection via AppPoolIdentity mapped directly to SQL) in Phase 4. To ensure full visibility and troubleshooting integrity, EF Core database migrations are explicitly and deliberately executed against `[Portal-Gerencial-Test]` using the pre-placed idempotent migrations SQL script `migration.sql` via local `sqlcmd` with Windows Authentication, rather than being triggered automatically via HTTP health check endpoints.
    12. **Phase 3 Staging Admin Access Recovery & Same-Origin Relative API Base Path:** To recover staging access, a dedicated .NET 8 console utility `StagingAccessRecovery.exe` is compiled and executed locally on `AOVIA1VMS011` utilizing dynamic BCrypt hashing to resolve the legacy Windows PowerShell .NET Core assembly loading blocker. Plaintext passwords are cryptographically generated or inputted securely and are never stored in logs or source control. Furthermore, to resolve frontend network connection blockers (`localhost:5000` failures), the client API base path is corrected from a hardcoded absolute address to a same-origin relative path `/api` which resolves same-domain routing through IIS and completely eliminates CORS complexities in both Staging and Production builds.
- **Consequences:** The deployment topology is fully defined as a dual-environment architecture documented in the readiness assessment and implementation plan. Security parameters, folder permissions, database boundaries, port restrictions, integration safety classifications, and environment isolation rules are established. Staging deployment verification constraints are corrected to mandate explicit `sqlcmd` migration execution and document the `applicationHost.config` storage tradeoff. Staging admin access recovery is successfully executed via a compiled .NET 8 utility bypassing runtime version blockers, and client API configurations are consolidated on same-origin relative `/api` routes through IIS, removing Kestrel direct port bindings and CORS complexities.

---

## DEC-132 — Guided Tour Evolution: Registry-Based Multi-Tour Architecture

- **Date:** 2026-05-22
- **Status:** Accepted
- **Context:** The initial Guided Tour (DEC-131) was a single "portal-main" tour covering the overall system structure. As users become familiar with the top-level navigation, they need deeper contextual guidance for specific modules and pages. The Compras & Logística module was identified as the priority target due to its workflow complexity (Pedidos → Cotações → Recebimento).
- **Decision:** Evolve the guided tour system from a single flat tour to a registry-based multi-tier architecture supporting portal, module, and page-level tours.
    1. **Tour Registry**: Central `guidedTourRegistry.ts` maps `TourId` → `TourDefinition` and resolves available tours by route prefix.
    2. **Tour Hierarchy**: Three tiers — `portal-*` (system-wide), `module-*` (feature area navigation), `page-*` (screen-specific usage).
    3. **Route Resolution**: `getToursForRoute(pathname)` returns `{ portal, module?, page? }` — module and page are optional. Multiple routes can resolve to the same module tour.
    4. **Help Dropdown**: GuidedTourButton transformed from single-click restart to a dropdown listing available tours for the current context. Always shows "Tour inicial do Portal"; conditionally shows module/page tours.
    5. **Inline Tour Button**: `GuidedTourContextButton` placed in page headers for direct page-level tour launch.
    6. **Separate Persistence**: Each tour uses `guided-tour:{tourId}:v1:{userId}` — completing one tour does not affect others.
    7. **No-Steps Safety**: `filterActiveSteps` + toast message if no DOM targets found — prevents runtime errors from RBAC-hidden UI.
    8. **Backward Compatible**: `portal-main` tour preserved exactly as before — auto-show on first access, welcome modal, same persistence key.
- **Alternatives considered:** (1) Single monolithic tour with module "chapters" (rejected: too long, can't match context). (2) Router-level automatic tour start per page (rejected: intrusive). (3) Third-party tour library switch (rejected: React Joyride v3 is sufficient).
- **Consequences:** New tours can be added by creating a tour file and registering it. Module teams can independently define their tour content without modifying core guided tour infrastructure.

---

## DEC-131 — Guided Tour / Onboarding System

- **Date:** 2026-05-22
- **Status:** Accepted
- **Context:** First-time users of the Portal Gerencial had no structured introduction to the system's layout, navigation, or key features. Users relied on word-of-mouth or trial-and-error to discover functionality.
- **Decision:** Implement a guided onboarding tour using React Joyride v3 with the following characteristics:
    1. **16 tour steps** covering Topbar, Search, Notifications, Profile, Help, Main Menu, Dashboard, and all sidebar modules.
    2. **Welcome modal** on first login with "Iniciar Tour" / "Agora Não" options.
    3. **Layout readiness** via DOM polling (not fixed delay).
    4. **RBAC-aware step filtering**: modules not visible to the user are silently skipped via DOM presence check.
    5. **Persistence**: `guided-tour:portal-main:v1:{userId}` in localStorage.
    6. **Help button** (❓) in Topbar for manual restart.
- **Consequences:** Users have a structured introduction to the system. The tour is non-intrusive (skippable, dismissable) and respects role-based access boundaries.

---

## DEC-130 — Remove LOCAL_OCR Provider, Consolidate on OpenAI

- **Date:** 2026-05-22
- **Status:** Accepted
- **Context:** The local OCR provider (PaddleOCR/Tesseract, deployed as a Python service on `localhost:5005`) was not meeting business needs for document extraction accuracy. The OpenAI Vision provider consistently delivered better structured extraction results for invoices, quotations, and contracts. Maintaining two providers added operational complexity (Docker container, health checks, configuration surface) without proportional value.
- **Decision:** Remove LOCAL_OCR as a supported extraction provider and consolidate on OpenAI as the sole active provider.
    1. **Deleted files:** `LocalOcrExtractionProvider.cs` (provider implementation), `OcrService.cs` (legacy dead-code service), `IOcrService.cs` (legacy dead-code interface).
    2. **Configuration:** `appsettings.json` default changed from `LOCAL_OCR` to `OPENAI`. The `LocalOcr` config section was removed. OpenAI is now enabled by default.
    3. **Backward compatibility:** If the database still contains `DefaultProvider = "LOCAL_OCR"`, the system logs a warning and falls back to `OPENAI`. The application does not crash.
    4. **Database columns:** `LocalOcrEnabled`, `LocalOcrBaseUrl`, `LocalOcrTimeoutSeconds` columns in `DocumentExtractionSettings` are retained (no migration) but marked `[Obsolete]`. They are cleared to `false/null` on every settings save.
    5. **Provider abstractions preserved:** `IDocumentExtractionProvider`, `ProviderSettings`, and the multi-provider DI pattern remain intact for future Azure Document Intelligence integration.
    6. **Frontend:** LOCAL_OCR option removed from provider dropdown. The entire "Local OCR" settings section removed. OpenAI label changed from "Experimental" to primary.
    7. **Diagnostics:** "Serviço OCR" card removed from Service Diagnosis. Integration Health card updated to reference only OpenAI. Admin Diagnostics health endpoint no longer returns `localOcr` status.
- **Alternatives considered:** (1) Fixing PaddleOCR accuracy (rejected: fundamental accuracy limitations with Portuguese/Angolan document formats). (2) Keeping LOCAL_OCR as a dormant fallback (rejected: dead code and UI complexity for zero value). (3) Migrating to Azure Document Intelligence immediately (rejected: not ready — kept as future placeholder).
- **Consequences:** The system no longer requires or attempts to connect to `http://localhost:5005`. Docker deployments can remove the Python OCR container. Future providers can be added via the existing `IDocumentExtractionProvider` abstraction.

---

## DEC-129 — Dashboard Redesign: Operational Cockpit

- **Date:** 2026-05-21
- **Status:** Accepted
- **Context:** The Dashboard page mixed generic KPI cards, a weak "Atenção Requerida" section (which returned `null` when empty), and a large Workflow Interactive guide that dominated ~50% of the viewport. It felt more like a presentation/training page than an operational tool.
- **Decision:** Redesign the Dashboard as an operational cockpit focused on action, priorities, exceptions, bottlenecks, and financial visibility.
    1. **New dedicated endpoint:** `GET /api/v1/requests/cockpit-summary` returns all data for the Dashboard in a single call. The existing `GET /api/v1/requests/summary` is untouched — it is used by the Requests page.
    2. **"Minha Fila de Trabalho" section:** 5 role-contextual cards showing: `Aguardando minha ação` (myTasksCriteria-based), `Urgentes` (today/tomorrow), `Em Reajuste`, `Atrasados`, `Próximos da data`. Only cards with items (+ the main "pending" card) are shown.
    3. **Pipeline KPI cards:** 10 compact status counters (Activos, Ag. Cotação, Aprov. Área, Aprov. Final, Reajuste, Ag. PO, Ag. Pagamento, Pago, Recebimento, Concluídos) with click-through to filtered request lists.
    4. **Quick Actions:** Expanded from 3 to 6 role-aware actions (Novo Pedido, Ver Pedidos, Cotações, Centro de Aprovações, Pagamentos, Recebimentos). Each visible only if the user has the required role.
    5. **"Atenção Requerida":** Always visible. Empty state shows "Nenhuma atenção crítica no momento." Alerts are severity-sorted (CRITICAL → WARNING → INFO) and include overdue, near-deadline, and adjustment items.
    6. **Bottlenecks table:** Visual distribution bars showing which workflow stages have the most requests stuck, with age indicators (color-coded by urgency).
    7. **Financial summary:** Aggregated totals by status group (Em Aprovação, Aprovado, Pendente Pagamento, Pago). Multi-currency aware. Shows only reliable data — no fake metrics.
    8. **Workflow guide:** Moved to a collapsible `<details>` at the bottom, collapsed by default. Available for onboarding but not dominating operational space.
    9. **No global filters in V1:** Dashboard relies on existing role-based and scope-based filtering from `GetScopedRequestsQuery()`. The endpoint and frontend state are structured to support Empresa/Planta/Período filters in V2.
- **Alternatives considered:** (1) Modifying the existing `/summary` endpoint (rejected: risk of breaking the Requests page). (2) Implementing global filters in V1 (rejected: scope creep — the role scoping already provides meaningful personalization). (3) Removing the Workflow guide entirely (rejected: still valuable for onboarding).
- **Consequences:** The Dashboard becomes an actionable operational tool. The old `AttentionList` component is superseded by the new `AlertList`. The old generic KPI cards are replaced by the `MyWorkQueue` and Pipeline sections. The Workflow guide remains accessible but de-emphasized.

---

## DEC-128 — HR Attendance: PunchWithoutPeriod Status Detection

- **Date:** 2026-05-21
- **Status:** Accepted
- **Context:** After fixing punch column placement (DEC-127), the monthly report still showed days as "Falta" with H.Totais=00:00 even when Portal-interpreted punches displayed a valid Entry + Exit pair. Two root causes: (1) `GetWorkedHoursAsync` counted absence period minutes (F03) as basic worked time because `AlteracoesPeriodos` rows with `IDCodigoAusencia` were not filtered out, and (2) no status existed to represent the discrepancy between Portal-visible punches and Innux-absent work periods.
- **Decision:** Implement a conservative "Verificar" status without overriding Innux official hours:
    1. **GetWorkedHoursAsync Fix:** Added `AND ap.IDCodigoAusencia IS NULL` to the SQL query. Absence periods have time spans (e.g., 08:00-17:30) but represent scheduled absence, not actual work. This filter is safe because `ComputePositiveCountedMinutes` already handles justified/unjustified absence via separate `AbsenceMinutes`/`JustifiedAbsenceMinutes` fields.
    2. **PunchWithoutPeriod Detection:** After punch pairing in the monthly report builder, if (a) Portal has valid Entry + Exit, (b) Innux status is "Absent" or "PortalInterpreted", (c) `dayWorked.TotalMinutes == 0`, and (d) entry→exit span ≥ 60 minutes, set status to `PunchWithoutPeriod`.
    3. **H.Totais Unchanged:** The Portal does NOT override official Innux worked hours. H.Totais remains 00:00. Only the status display changes from "Falta" to "Verificar" to alert HR that Innux processing may need review.
    4. **Portal Estimated Time:** Calculated from entry→exit span (e.g., 07:47→17:32 = 09:45). Shown in tooltip for HR reference, NOT as official hours.
    5. **Frontend Display:** "Verificar" badge (orange/amber) with AlertCircle icon and pulse animation. Portuguese tooltip includes estimated hours.
- **Alternatives considered:** (1) Override H.Totais with Portal-calculated hours (rejected: violates Innux-as-source-of-truth principle). (2) Show "Presente" based on Portal punches alone (rejected: Innux may have valid reasons for the absence classification). (3) Filter absence periods in the SQL JOIN instead of WHERE (rejected: LEFT JOIN filter in WHERE is equivalent and simpler).
- **Consequences:** 448 day-records across 95 employees in May 2026 now show "Verificar" instead of "Falta", alerting HR to systematic Innux processing gaps. No data is modified.
- **Diagnostic Impact:** The high volume (95/137 employees) suggests a systemic Innux configuration issue — raw terminal punches exist but Innux is not generating `AlteracoesPeriodos` work periods. This is an Innux-side issue, not a Portal defect.

---
## DEC-127 — HR Attendance: Unified Punch Direction Interpretation

- **Date:** 2026-05-21
- **Status:** Accepted
- **Context:** The HR Attendance Monthly Report showed exit punches in the wrong column (e.g., ENT.2 instead of SAÍ.1) for employees whose terminal sends mixed direction codes. Real-world data showed a single day could have Code `17` (alternate entry code) on the first punch and Code `EN` (standard entry code) on the second punch. Since `MapDirectionLabel` maps both `17` and `EN` to "Entrada", both punches ended up in entry columns, regardless of clock time.
- **Decision:** Extract the Portal punch interpretation logic into a shared method (`ApplyPortalPunchInterpretation`) and apply it consistently to both bulk and detail flows:
    1. **Shared Method:** `ApplyPortalPunchInterpretation` in `InnuxAttendanceService.cs` groups punches by employee+day. Applies code-specific rules first (all Code 17, all Code 18), then a fallback Rule 4: if all punches in a day have the same `DirectionLabel` after `MapDirectionLabel` (e.g., Code 17→"Entrada" + EN→"Entrada"), the first chronological punch is classified as "Entrada" and the last as "Saída". Single ambiguous punches are not inferred.
    2. **Rule 4 (Mixed Codes):** Handles the critical scenario where the terminal uses different raw codes that all map to the same direction. Without this rule, both punches would appear in entry columns. Example: 07:47 (Code 17→Entrada) + 17:32 (EN→Entrada) → Rule 4 infers 07:47=Entrada, 17:32=Saída.
    3. **Audit Transparency:** Reinterpreted punches are flagged with `IsPortalInterpreted = true` for traceability.
    4. **Direction Warnings:** Three warning scenarios propagated to the frontend: Portal-interpreted days, single ambiguous punch days, and multiple same-direction ambiguous punch days. Displayed via a compass icon (🧭) with Portuguese tooltip.
    5. **Hour Calculations Unaffected:** Worked hours (H.Básicas, H.Totais, Saldo) come from `AlteracoesPeriodos` (Innux processed data), which is decoupled from the column-classification logic. The fix only affects visual punch column assignment — no numerical impact.
- **Alternatives considered:** (1) Fix only `GetRawPunchesAsync` with duplicate logic (rejected: leads to future drift). (2) Trust terminal direction codes blindly (rejected: production data proves Code 17 and EN can both appear as entry on the same day). (3) Remap Code 17 to a different direction (rejected: Code 17 genuinely represents entry on days where both 17 and 18 are used).
- **Consequences:** Entry/Exit punch columns in the monthly report now match chronological reality. Direction warnings provide audit visibility without blocking workflows. Rule 4 handles mixed-code terminals transparently.

---
## DEC-126 — I.T Equipment Documents: DOCX → PDF Migration with Branding

- **Date:** 2026-05-21
- **Status:** Accepted
- **Context:** Official I.T Equipment documents (Termo de Responsabilidade / Entrega, Termo de Devolução) were generated as DOCX files using `DocumentFormat.OpenXml`. The company required these official documents to be generated, stored, and emailed as PDF files, and to include the Portal Gerencial system logo in the document header for brand consistency.
- **Decision:** Migrate all I.T Equipment document generation from DOCX to direct PDF output using PdfSharpCore:
    1. **PDF Library:** PdfSharpCore 1.3.67 (MIT license — no revenue restrictions, no licensing risk for large companies like Alpla). QuestPDF was considered but rejected due to its paid license requirement for companies with ≥ $1M annual revenue.
    2. **New Service:** `ITEquipmentPdfService` replaces `ITEquipmentAgreementService` for all new document generation. Uses `PdfSharpCore.Drawing.XGraphics` for layout with `XTextFormatter` for text wrapping and page-break management.
    3. **Branded Header:** Every PDF includes a branded header with: Portal Gerencial logo (from `data/templates/branding/portal-logo.png`), company name, and module identifier. If the logo file is missing, the document is generated with a text-only header and a warning is logged — document generation does not fail.
    4. **Policy Text:** The equipment usage policy text is stored externally at `data/templates/it-equipment/policy-text.txt`, editable without code changes. For Assignment Agreements, this file is **required** — if missing, generation fails with a clear error message. For Return Agreements, the policy text is not needed (uses internal declaration text only).
    5. **Document Naming:** `Termo_Responsabilidade_[AssetTag]_[yyyyMMddHHmm].pdf` and `Termo_Devolucao_[AssetTag]_[yyyyMMddHHmm].pdf`.
    6. **Legacy Compatibility:** Existing DOCX documents in the database remain downloadable. The download endpoint auto-detects MIME type from file extension (`.pdf` → `application/pdf`, `.docx` → `application/vnd.openxmlformats...`). The old `ITEquipmentAgreementService` is marked `[Obsolete]` but not removed.
    7. **Email Attachments:** MIME type for email attachments is now auto-detected from file extension, ensuring email clients correctly recognize PDF and DOCX attachments.
    8. **Affected Flows:** Assignment (assign), Return (return), and Change User (change-user) — all three flows now generate PDF documents.
- **Alternatives considered:** (1) QuestPDF for fluent PDF layout (rejected: paid license ≥ $1M revenue). (2) DOCX-to-PDF conversion using LibreOffice/PdfiumViewer (rejected: user specified direct PDF generation, no external converter dependencies). (3) Hardcoding policy text in code (rejected: extracting to file makes it editable without deployments).
- **Consequences:** All new I.T Equipment documents are PDF with branding. PDF files open inline in browsers via the download endpoint. The policy text can be updated by editing a text file without code changes. Legacy DOCX documents remain accessible.

---
## DEC-125 — I.T Equipment Inventory Management Module

- **Date:** 2026-05-20
- **Status:** Accepted
- **Context:** The company manages a fleet of IT equipment (laptops, desktops, monitors, printers, servers, networking gear, peripherals, mobile devices) currently tracked in a CSV spreadsheet. The IT team needed a structured system within the Portal to manage the full lifecycle of every asset — from registration through assignment, repair, loss, and retirement — with audit-grade traceability.
- **Decision:** Implement a new I.T Module with the following architectural decisions:
    1. **Dedicated IT Role:** A new `IT` role (seeded via migration) controls module access. Only `IT` and `System Administrator` roles can access the module. This follows the pattern established by DEC-107 (HR role) — dedicated role per functional domain.
    2. **Equipment Status Machine:** Equipment follows a defined lifecycle: `AVAILABLE → IN_USE → RETURNED / IN_REPAIR / LOST / RESERVED / RETIRED / DAMAGED / UNKNOWN`. The `UNKNOWN` status is reserved for legacy imported records where the original status was empty/null — per user requirement, empty status must NOT default to `AVAILABLE` since that incorrectly implies readiness for assignment.
    3. **Movement Audit Log Pattern:** Every status change creates an `ITEquipmentMovementLog` entry recording: previous status → new status, previous/new assigned user, operator, timestamp, and notes. This provides a complete, immutable audit trail. The `PerformedByUserId` always records the authenticated user who triggered the action.
    4. **Assignment Model:** `ITEquipmentAssignment` tracks current and historical user assignments with `Status` (ACTIVE/RETURNED/TRANSFERRED), assignment/return dates, return condition (OK/DAMAGED/NEEDS_REPAIR), and responsible approver. An equipment can only have one ACTIVE assignment at a time — the assign endpoint validates this.
    5. **CSV Import Strategy:** Import is a controlled user action via multipart upload, NOT automatic on startup. The backend receives the CSV through the API endpoint, normalizes column headers (supporting both English and Portuguese names), and processes each row independently. Duplicate detection: exact match on `AssetTag`, conditional match on `Hostname` (only when both source and target are non-empty). Duplicates are reported but do not fail the entire import — each row succeeds or fails independently.
    6. **Document FK Cascade Restriction:** `ITEquipmentDocument` has FKs to both `ITEquipment` and `ITEquipmentAcquisition`. SQL Server prohibits multiple cascade delete paths to the same target table. Solution: `Equipment→Document` uses `DeleteBehavior.Restrict` while `Acquisition→Document` uses `DeleteBehavior.Cascade`. This follows the same pattern used for other multi-FK entities in the system.
    7. **Acquisition Tracking (Phase 1):** A 1:1 optional `ITEquipmentAcquisition` record per equipment stores purchase order, invoice, payment, and warranty data. Integration fields (`PrimaveraDocumentReference`, `PortalRequestId`) are nullable for future connection to the existing Purchase Request workflow.
    8. **Hostname Conditional Uniqueness:** Hostname uniqueness is enforced only when the hostname is non-empty. Equipment without hostnames (e.g., peripherals, monitors) should not trigger uniqueness violations. Implemented via backend validation during create/update/import operations.
- **Alternatives considered:** (1) Auto-importing CSV on first startup (rejected: user wants explicit control). (2) Defaulting empty status to AVAILABLE (rejected: misleading for unverified legacy data). (3) Using Cascade delete for all Document FKs (rejected: SQL Server cascade path limitation). (4) Embedding acquisition fields directly in ITEquipment (rejected: acquisition is a distinct concern with its own lifecycle and future integration potential).
- **Consequences:** The IT team has a structured, auditable system replacing the CSV spreadsheet. Every equipment movement is logged. The module is isolated by role, preventing accidental access from other functional areas. The acquisition model is ready for future Primavera/Portal Purchase Request integration. Legacy data imports can be repeated safely due to duplicate detection.

---

## DEC-124 — Portal-Computed Attendance Report Balance (Saldo)

- **Date:** 2026-05-20
- **Status:** Accepted
- **Context:** The HR Monthly Attendance Report `Saldo` (balance) column always displayed `00:00`, even for employees with unjustified absences. Root cause: Innux stores the `Saldo` value as a `datetime-as-duration` with base date `1900-01-01`. `InnuxTimeHelper.ToMinutes()` returns 0 for any value at or before the base date, silently truncating all negative balances to zero. This was already documented as a known limitation in `InnuxAttendanceDtos.cs` and `InnuxTimeHelper.cs` but was never addressed in the report output.
- **Decision:** The monthly attendance report now computes `DailyBalance` independently in `HRAttendanceController.BuildSingleDepartmentReportAsync`, replacing the Innux-sourced `BalanceMinutes`.
    1. **Column Semantics Clarification:**
        - `H.Básicas` (`BasicMinutes`) = **Planned/scheduled** working hours (`AttendanceDaySummaryDto.ExpectedMinutes`), shown even if the employee did not work.
        - `H.Falta` (`AbsenceMinutes`) = Unjustified absence hours (unchanged — Innux `Falta` column).
        - `H.Totais` (`TotalMinutes`) = **Positive counted hours**: real worked hours + justified/approved absence hours. Unjustified absence is NOT counted.
        - `Saldo` (`DailyBalance`) = `H.Totais - H.Básicas`. Portal-computed.
    2. **Positive Counted Minutes Formula:** `max(0, WorkedTotalMinutes - AbsenceMinutes) + JustifiedAbsenceMinutes`. Innux may record scheduled periods in `AlteracoesPeriodos` even on absence days, so the unjustified `AbsenceMinutes` is subtracted to derive real worked time.
    3. **Exempt Categories:** Vacation, Holiday, and JustifiedAbsence status days return `PositiveCountedMinutes = ExpectedMinutes`, making them balance-neutral (`Saldo = 00:00`). Rest days return 0 (no expected work).
    4. **Visual Indicators:** Negative balance in red/bold, positive in green. Applied across daily records, monthly summaries, employee grand totals, and department totals — both screen and print.
    5. **Scope:** Read-only computation change. No writes to Innux, Primavera, or Portal databases. Only affects the monthly report output. The HR Attendance Calendar is NOT affected.
- **Alternatives considered:** (1) Fixing `InnuxTimeHelper.ToMinutes()` to detect and decode negative balances from Innux datetime values (rejected: Innux's negative encoding scheme is unconfirmed; would require reverse-engineering the vendor's internal convention). (2) Adding a `BalanceMinutesRaw` column to `AttendanceDaySummaryDto` with signed integer conversion (rejected: same unknown encoding problem — the issue is at the Innux source, not the Portal layer).
- **Consequences:** Balance values now correctly reflect positive and negative time balances. Monthly/grand totals accumulate real balances. The report becomes actionable for HR to identify employees with time deficits. If Innux's negative encoding is ever documented, the Portal formula could be replaced with the Innux value, but the current formula is correct and self-consistent.

---

## DEC-123 — Proforma Deadline Expiration Alerts

- **Date:** 2026-05-15
- **Status:** Accepted
- **Context:** PAYMENT requests with Proforma documents often have strict payment deadlines (`NeedByDateUtc`). When a request is stalled in approval stages (`WAITING_AREA_APPROVAL` or `WAITING_FINAL_APPROVAL`), the responsible approver may not be aware that the Proforma is about to expire or has already expired, risking financial penalties and supplier relationship damage.
- **Decision:** Implement an automated daily `BackgroundService` (`ProformaDeadlineAlertService`) to monitor and alert approvers:
    1. **Scope**: Only PAYMENT requests with a non-null `NeedByDateUtc` in active approval stages are monitored.
    2. **Alert Levels**: Four graduated levels — `WARNING_3D` (3 days before), `WARNING_1D` (1 day before), `CRITICAL_0D` (same day), `EXPIRED` (past due). Configurable via `ThresholdDays` in `appsettings.json`.
    3. **Deduplication Strategy**: Global composite unique index `(RequestId, AlertLevel, RecipientUserId)`. Once an alert is sent, it is never repeated — no daily re-sends for the same alert level. When a request transitions to a new approval stage with a different approver, the new recipient can still receive the relevant alert.
    4. **Recipient Resolution**: Mirrors `WorkflowNotificationOrchestrator` patterns — explicit `Request.AreaApproverId`/`FinalApproverId` preferred, with department-scoped fan-out as fallback for Area Approvers (active users with `Area Approver` role in the same department).
    5. **Notification Channels**: Dual-channel — branded Portuguese email via `IEmailService` and in-app bell notification via `INotificationService` (category: `PROFORMA_DEADLINE`).
    6. **Audit Trail**: `ProformaDeadlineAlert` entity persists every dispatched alert with email/notification delivery status, timestamps, and error details.
    7. **Scheduling**: Runs at a configurable UTC hour (default 07:00 = 08:00 Angola time) with a 24-hour interval.
    8. **EXPIRED Alert**: Sent only once per request per recipient. No daily repetition of overdue alerts.
- **Alternatives considered:** (1) Real-time alerts triggered on approval-stage entry (rejected: does not handle requests already in the pipeline). (2) Same-day-only deduplication (rejected: a global unique index is simpler and prevents cumulative alert fatigue). (3) Including QUOTATION requests (rejected: deferred — they have different deadline semantics).
- **Consequences:** Approvers are proactively warned before Proforma deadlines lapse. The system avoids alert fatigue through strict deduplication. The audit trail enables operational reporting on alert effectiveness. Configuration is externalized for easy tuning without code changes.

---

## DEC-121 — Portal Attendance Engine: Phase 3 Comparison Engine

- **Date:** 2026-05-12
- **Status:** Accepted
- **Context:** With Phases 1 (Schedule Resolver) and 2 (Punch Interpreter) validated, a comparison engine was needed to systematically contrast Innux processed attendance against Portal raw-punch interpretation. The goal is diagnostic: identify discrepancies so HR can review cases where Innux and Portal disagree, without replacing the current production calendar.
- **Decision:** Implement `AttendanceComparisonService` as a pure orchestrator — no new SQL queries. The service calls existing `IInnuxAttendanceService`, `IPortalPunchInterpreter`, and `IPortalScheduleResolver`, then applies explicit discrepancy rules:
    1. **Portal Status Derivation:** Derived deterministically from punch interpretation: `Present` (complete pairs, worked > 0), `NoPunches`, `Incomplete`, `DayOff` (rest day, no punches), `PresentOnRestDay`.
    2. **Discrepancy Severity Rules (explicit mappings):** HIGH = Innux absent-family + Portal Present, or Innux Present + Portal NoPunches. MEDIUM = both present but worked diff > 30min, time drift > 30min, incomplete pairs, duplicates. LOW = minor drift, Alteracoes fallback, low confidence.
    3. **Schedule Fallback Clarification:** Alteracoes.IDHorario is used strictly as schedule context for Escala plans. It is NOT treated as proof of attendance.
    4. **Range Safeguards:** Maximum 31 days, execution time logging, batch processing for department scans.
    5. **Portuguese Messages:** All `DiscrepancyMessages` and `RecommendedReviewAction` in Portuguese for HR consumption.
- **Alternatives considered:** (1) Running the comparison at query time inside the existing calendar endpoint (rejected: mixes diagnostic/production concerns). (2) Building the comparison with new SQL joins (rejected: unnecessary complexity, existing services already have the data). (3) Making the comparison the default calendar source (rejected: premature — needs validation first).
- **Consequences:** HR and IT now have a systematic tool to audit Innux reliability per employee-day. The comparison results can inform a future decision to promote Portal interpretation as the primary attendance source. The architecture remains decoupled — comparison logic depends only on service interfaces, not SQL schemas.

## DEC-120 — Portal-Side Attendance Interpretation Engine (Phases 1 & 2)

- **Date:** 2026-05-12
- **Status:** Accepted
- **Context:** The existing HR Attendance Calendar relies entirely on Innux-processed `Alteracoes` data, which has exhibited persistent issues: duplicated entries, false absence classifications, contradictory vacation/shift data, and opaque interpretation logic. The Portal cannot correct or write to Innux or Primavera databases. A separate, read-only "shadow" interpretation engine was needed to diagnose these issues transparently and lay the foundation for a future Portal-managed attendance model.
- **Decision:** Implement a backend-only, diagnostic-purpose interpretation engine in two phases:
    1. **Phase 1 — Schedule Day Resolver (`PortalScheduleResolver`):** Reads `PlanosTrabalho`, `PlanosTrabalhoHorarios`, and `HorariosPeriodos` to resolve the expected schedule for any employee on any date. Computes cycle day indices, detects overnight shifts (entry time > exit time), calculates expected working minutes, and identifies rest days. Uses strictly parameterized, SELECT-only SQL queries.
    2. **Phase 2 — Raw Punch Interpreter (`PortalPunchInterpreter`):** Reads raw `TerminaisMarcacoes` records and interprets them independently of Innux's `TipoProcessado` output. Supports three direction inference strategies: standard EN/SA, alternate codes 17→Entry/18→Exit, and position-based inference for empty directions. **Transparency principle:** duplicate punches are flagged (`IsDuplicateCandidate`) but never removed — all raw data is preserved for audit. Builds Entry/Exit pairs, calculates worked minutes, and assigns confidence scores (`High`/`Medium`/`Low`/`None`). Every interpretation decision is captured via `InterpretationReason` and `InterpretationRule` fields.
    3. **Diagnostic Endpoints:** Two new diagnostic-only endpoints in `HRAttendanceController`, restricted to `SystemAdministrator` and `HR` roles. These endpoints are NOT consumed by the production calendar UI.
    4. **Strict Constraints:** No writes to Innux. No writes to Primavera. No changes to the existing HR Attendance Calendar behavior. No changes to existing API responses used by the current calendar.
- **Alternatives considered:** (1) Fixing Innux directly (rejected: external system, no write permissions). (2) Adding interpretation logic into the existing `InnuxAttendanceService` (rejected: mixes concerns, risks regression in production calendar). (3) Building a full replacement engine immediately (rejected: premature — need diagnostic validation first).
- **Consequences:** Provides a reliable, transparent diagnostic tool for HR/IT to analyze attendance discrepancies. The engine operates as a parallel "shadow" system with zero impact on production workflows. Future Phase 3 (Comparison Engine) can leverage these services to automatically detect and flag discrepancies between Innux-processed and Portal-interpreted results. The architecture supports a gradual transition from Innux-dependent to Portal-managed attendance interpretation once confidence is established.

## DEC-119 — HR Attendance: Portal-Side Override for False Absences (F03)

- **Date:** 2026-04-28
- **Status:** Accepted
- **Context:** Due to anomalies in Innux terminal processing (specifically involving "Code 17" events), the Innux database was correctly capturing punches but incorrectly concluding the day as a "Falta Injustificada" (F03) with `Marcacao = 0`. This caused the Portal to display employees as absent despite having valid entry and exit punches on record. Modifying the Innux database or Primavera integration directly was out of scope due to external constraints.
- **Decision:** Implement a global "Portal-Side Override" presentation rule.
    1. **Interpretation:** If the Portal detects valid presence (multiple Code 17 punches spanning > 60 minutes) but the day is flagged as an unjustified absence (F03), the Portal intercepts this state.
    2. **Correction:** The `absenceMinutes` for the day are reset to 0, preventing the absence from bubbling up to summary aggregations.
    3. **Presentation:** The `Falta Injustificada` label is cleared, and the period is assigned a `PORTAL` work description.
- **Alternatives considered:** Writing correction scripts directly into the Innux database (rejected due to safety and vendor compliance). Suppressing the period entirely (rejected as it would hide the processed record).
- **Consequences:** The UI correctly reflects actual employee presence. Crucially, the underlying Innux data and Primavera exports remain untouched, confining the fix entirely to the presentation layer until the root cause in the Innux processing engine is resolved.

## DEC-118 — Hierarchical Budget Configuration

- **Date:** 2026-04-26
- **Status:** Accepted
- **Context:** The previous budget configuration was limited to a simple department-level association. However, budget control required a more granular allocation strategy that could reflect the actual organizational hierarchy, including Company, Plant, Department, and optionally, Cost Center.
- **Decision:** Restructure the `AnnualBudget` entity and update related finance calculations to use a composite hierarchical structure.
    1. **Granular Hierarchy:** Add `CompanyId`, `PlantId`, and `CostCenterId` to the budget domain model.
    2. **Flexible Cost Center (General Budgets):** `CostCenterId` is optional. If null, it represents a "General Department" budget. If specified, it represents a specific cost center budget.
    3. **Composite Unique Key:** Instead of a strict EF Core unique index (due to nullable `CostCenterId`), uniqueness is enforced via backend validation during the save operation based on `(FiscalYear, CompanyId, PlantId, DepartmentId, CostCenterId, CurrencyId)`.
    4. **Active/Inactive State:** Add an `IsActive` flag to support disabling outdated configurations without deleting them, maintaining historical referential integrity.
    5. **Granular Consumption Tracking:** Update `FinanceBudgetController` to allocate consumed values based on `RequestLineItem.CostCenterId`. If an item lacks a cost center, it falls back to the general department budget. Header-level Request values are proportionally distributed across line items.
- **Alternatives considered:** Strict cost center requirement (rejected: general budgets are still needed). Single database-level unique index (rejected: EF Core handles multiple nulls differently across providers, making it brittle; explicit backend validation was preferred).
- **Consequences:** Provides accurate, fine-grained financial tracking reflecting true operational structure. A breaking data migration was applied to clear old `AnnualBudget` records, requiring reconfiguration in the new UI.

## DEC-117 — Structured Payment Deadline Rules for Contracts

- **Date:** 2026-04-20
- **Status:** Accepted
- **Context:** Contract payment due dates were previously tracked only via a free-text `PaymentTerms` field with no calculation logic. This caused manual due date entry per obligation and no systematic tracking of grace periods, penalty start dates, or calculation audit trails. The goal was to model real-world contract payment rules accurately while maintaining backward compatibility.
- **Decision:** Add structured payment rule fields to `Contract` and calculated tracking fields to `ContractPaymentObligation`, backed by a domain-level calculation service (`ContractDueDateCalculator`).
    1. **Structured rule at contract level**: `PaymentTermTypeCode` (7 types), `ReferenceEventTypeCode` (6 events), `PaymentTermDays`, `PaymentFixedDay`, `AllowsManualDueDateOverride`, `GracePeriodDays`, `HasLatePenalty`, `LatePenaltyValue/TypeCode`, `HasLateInterest`, `LateInterestValue/TypeCode`, `PaymentRuleSummary`, `FinancialNotes`, `PenaltyNotes`.
    2. **Calculated fields at obligation level**: `CalculatedDueDateUtc` (always computed), `DueDateSourceCode` (`AUTO_FROM_CONTRACT` | `MANUAL_OVERRIDE`), `ReferenceDateUtc`, `GraceDateUtc`, `PenaltyStartDateUtc`.
    3. **Calculation service**: `ContractDueDateCalculator` is a static domain service. It resolves reference dates from `ReferenceEventTypeCode`, calculates due dates per rule, and computes grace/penalty dates. It **fails fast** (`BadRequest`) when required user-supplied reference data is missing — it never defaults silently.
    4. **No monetary calculation**: `LatePenaltyValue` and `LateInterestValue` are stored for contract terms only. No penalty calculation engine is implemented in this phase.
    5. **Backward compatibility preserved**: The existing `PaymentTerms` free-text field is kept. Contracts without a `PaymentTermTypeCode` operate normally with manual due dates per obligation.
    6. **Manual override control**: `AllowsManualDueDateOverride` gates per-obligation due date overrides at the contract level. Without it, auto-calculated rules reject manual override attempts.
    7. **Auto-open in UI**: The "Regras de Pagamento" section auto-opens in the contract edit form when a rule is already configured.
    8. **Obligation context note**: The obligation add/edit form displays a context-aware note explaining what the selected rule requires from the user (invoice date, manual ref date, or nothing).
    9. **Due date source badge**: Each obligation in the detail view shows a `🔄 Auto` or `✏️ Manual` badge with an expandable deadline metadata panel (reference date, calculated date, grace date, penalty start date).
- **Alternatives considered:**
    1. Keeping free-text only (rejected: no calculation support, no grace/penalty tracking).
    2. Putting calculation logic in the controller (rejected: domain rules belong in the domain layer).
    3. Defaulting to `DateTime.UtcNow` when reference date is missing (rejected: silent defaults cause wrong due dates in production data — fail-fast is safer).
    4. Auto-creating grace dates even for contracts without a grace period rule (rejected: only computed when `GracePeriodDays > 0` or when a rule is active).
- **Consequences:** Obligations now carry full due-date provenance. `Request.NeedByDateUtc` is sourced from the final (auto or overridden) obligation `DueDateUtc`. The urgency heuristic (`NeedLevelCode`) applies correctly for contract-sourced requests. Monetary penalty calculation remains deferred until a future dedicated engine phase. All new lookup endpoints (`/payment-term-types`, `/reference-event-types`) are documented in `CONTRACTS_WORKFLOW.md §11`. EF Core decimal precision follows the convention established in DEC-108.

## DEC-111 — Contracts Management Module Architecture


- **Date:** 2026-04-19
- **Status:** Accepted
- **Context:** The portal needed a Contracts module to manage external agreements (service contracts, leases, supply, maintenance) and connect them to the existing payment request workflow. Key challenge: linking contracts to payment requests without introducing circular dependencies or breaking existing cascade delete paths.
- **Decision:** Implement a first vertical slice with the following architectural decisions:
    1. **Unidirectional FK Strategy:** `Request` references `Contract` and `ContractPaymentObligation` via nullable FKs — NOT the reverse. The `Contract` entity has no direct navigation collection to `Request`. This eliminates circular cascade path conflicts with EF Core's existing `Company → Department → Request` chain.
    2. **RESTRICT Delete Behavior:** Both FKs (`Request.ContractId`, `Request.ContractPaymentObligationId`) use `DeleteBehavior.Restrict` to prevent accidental deletion of contracts that have linked payment requests.
    3. **Manual Payment Request Generation:** Payment requests are NEVER auto-created. A user must explicitly trigger `POST /contracts/{id}/obligations/{oblId}/generate-request` on a PENDING obligation of an ACTIVE contract. This preserves the human-in-the-loop audit principle established in the existing workflow.
    4. **Contract Number Atomicity:** Uses the existing `SystemCounters` table with a new `CONTRACT_COUNTER` key, following the same `FindAsync + SaveChanges` atomic pattern as `GLOBAL_REQUEST_COUNTER`. Format: `CTR-{year}-{sequence:00000}`.
    5. **Company-Aware Scope Derivation:** `GetScopedContractsQuery()` derives company scope from user plant assignments (`UserPlants → Plant.CompanyId`), supporting both plant-specific and company-wide contracts (`PlantId = NULL`). This mirrors the `Requests` scope model.
    6. **Contract Types as Lookup Entity:** `ContractType` is a seeded reference table (SERVICE, LEASE, SUPPLY, MAINTENANCE) — not an enum — to allow future additions without code changes.
- **Alternatives considered:** (1) Bidirectional FK with `Contract.Requests` collection (rejected: creates cascade cycle with `Company → Department → Request` path). (2) Auto-generating Payment Requests on obligation creation (rejected: violates audit principle, user must decide when to trigger payment). (3) Using the `Request.RequestTypeId` as the only link (rejected: insufficient — need `ContractId` + `ObligationId` for traceability and lifecycle sync).
- **Consequences:** The `Request` entity gains two nullable FK columns. Existing requests with `NULL` contract fields are unaffected. The obligation lifecycle (`PENDING → REQUEST_CREATED → PAID → CANCELLED`) enables future auto-sync when the linked Request reaches a terminal payment state. Full workflow documented in `docs/CONTRACTS_WORKFLOW.md`.

## DEC-110 — Financial Snapshot & Payment Divergence Detection (Phase 1)

- **Date:** 2026-04-17
- **Status:** Accepted
- **Context:** The system had no mechanism to preserve approved financial values or compare them against actual payments. Once approved, financial values were frozen by convention but not by enforcement. Commercial conditions (total amount, VAT, currency impact, supplier terms) could change between approval and payment with no structured detection or audit trail.
- **Decision:** Implement a phased delivery for post-approval commercial change handling.
    1. **Phase 1 (this implementation):**
        - Add `ApprovedTotalAmount`, `ApprovedCurrencyCode`, `ApprovedAtUtc` to `Request` entity — immutable snapshot captured at final approval.
        - Add `ActualPaidAmount`, `ActualPaidAtUtc` to `Request` entity — mandatory input when confirming payment via `MarkAsPaid`.
        - Add status guards to `SchedulePayment` and `MarkAsPaid` in `FinanceController` — only allowed from valid source statuses.
        - Implement divergence detection: if `Math.Round(ActualPaidAmount, 2) ≠ Math.Round(ApprovedTotalAmount, 2)`, create a `PAYMENT_DIVERGENCE_DETECTED` audit entry. No tolerance threshold — any difference is reported. *(Updated 2026-04-26: removed 1% tolerance gate per business decision.)*
        - Phase 1 divergence is **informational** — payment proceeds, divergence is logged and visible. Message indicates direction (abaixo/acima do valor aprovado).
        - All new fields are nullable for backward compatibility with legacy requests.
    2. **Phase 2 (documented, not implemented):**
        - New exception workflow statuses: `COMMERCIAL_CHANGE_REVIEW`, `REAPPROVAL_REQUIRED`, `POST_PAYMENT_REGULARIZATION`.
        - Complementary payment mechanism with cumulative tracking.
        - Pre-payment commercial revalidation endpoint.
- **Alternatives considered:** (1) Making divergence blocking in Phase 1 (rejected: requires new statuses, approval center changes, and sidebar updates — too much scope). (2) Making `ActualPaidAmount` optional (rejected: defeats the purpose of divergence detection).
- **Consequences:** Establishes the data foundation for full commercial change handling. `ActualPaidAmount` is mandatory when paying. Legacy requests with null snapshot fields skip divergence detection gracefully. Finance payments list shows divergence badge for affected requests. Full documentation in `WORKFLOW_ARCHITECTURE.md §6`.

## DEC-108 — Mandatory Explicit Decimal Precision in EF Core Configurations

- **Date:** 2026-04-16
- **Status:** Accepted
- **Context:** Multiple EF Core decimal properties across the domain model (`OcrExtractedItem`, `ReconciliationRecord`, `QuotationItem`, `RequestLineItem`, `Request`) were relying on SQL Server provider defaults for precision/scale. This generated startup warnings (`No store type was specified for the decimal property...`) and risked silent data truncation — especially critical for financial amounts, OCR-extracted values, percentages, and reconciliation scores.
- **Decision:** Establish a **mandatory convention** for all backend decimal fields. Every new or modified `decimal` property must define explicit database precision/scale in EF Core Fluent API configuration. Provider defaults must never be relied upon.
    1. **Convention Table:**
        | Domain Category | EF Core Configuration | Examples |
        |---|---|---|
        | Money / totals / prices / amounts | `HasColumnType("decimal(18,2)")` | `UnitPrice`, `TotalAmount`, `DiscountAmount`, `LineTotal`, `OcrOriginalGrandTotal` |
        | Percentages / rates / confidence scores | `HasColumnType("decimal(9,4)")` | `DiscountPercent`, `TaxRate`, `IvaRatePercent`, `MatchConfidence`, `QualityScore` |
        | Quantities / fractional quantities / divergence | `HasColumnType("decimal(18,4)")` | `Quantity`, `ReceivedQuantity`, `QuantityDivergence` |
        | File sizes / metadata | `HasPrecision(10, 3)` | `FileSizeMBytes` |
    2. **Configuration location:** All precision must be declared in the entity's `IEntityTypeConfiguration<T>` class inside `EntityConfigurations.cs`, or in the `OnModelCreating` block in `ApplicationDbContext.cs` if the entity doesn't have a dedicated configuration class.
    3. **Enforcement:** Any PR introducing a new `decimal` property without explicit precision must be rejected during code review.
- **Alternatives considered:** A global model convention using `modelBuilder.Properties<decimal>().HavePrecision(18,2)` (rejected: masks domain intent — percentages, quantities, and scores require different precision than monetary values).
- **Consequences:** Eliminates all EF Core decimal warnings at startup. Prevents silent truncation. Establishes a clear, auditable standard for data-layer integrity. Requires all existing decimal fields to be audited and configured (completed in this change).

## DEC-107 — Dedicated HR Role for V1 Employee Workspace Security

- **Date:** 2026-04-15
- **Status:** Accepted
- **Context:** The HR Employee Workspace (`Cadastro de Funcionários`) was initially protected only by the `Local Manager` role, which piggy-backed HR access onto user management privileges. This was inadequate because: (1) not all Local Managers need HR access, (2) some users need HR access without management privileges, and (3) future HR submenus (Vacation, Time Attendance, Badge Layout) will require independent feature-level authorization.
- **Decision:** Introduce a dedicated `HR` role for the V1 Employee Workspace.
    1. **New role:** `HR` (backend: `RoleConstants.HR`, frontend: `ROLES.HR`). Added to the domain role table.
    2. **Authorization:** `HRController` now uses `[Authorize(Roles = "System Administrator,HR")]`. `System Administrator` retains unrestricted access.
    3. **Breaking change:** `Local Manager` no longer has implicit HR access. Existing Local Managers must be explicitly assigned the `HR` role to retain access.
    4. **Scope enforcement:** HR users are subject to plant/department scope filtering via `UserPlantScopes` and `UserDepartmentScopes`, consistent with the existing `BaseController` pattern.
    5. **User Management:** Local Managers can assign the `HR` role to users within their scope. A validation warning appears when the `HR` role is selected but no plants/departments are assigned.
    6. **Login response:** `UserProfileDto` now includes `Plants` and `Departments` fields, populated from scope tables during login.
- **Future evolution (explicitly incomplete):**
    - This PR secures only the current V1 HR Employee Workspace.
    - Parent HR menu visibility is currently tied to the single `HR` role.
    - Future HR submenus (Vacation Management, Time Attendance, Badge Layout Management) will require an additional authorization evolution — likely per-submenu feature flags or a hierarchical permission model — where `Local Manager` may access some HR submenus while being blocked from others.
- **Alternatives considered:** Reusing `Local Manager` with a feature flag (rejected: conflates management privilege with HR access). Creating per-submenu roles immediately (rejected: premature — only one HR screen exists today).
- **Consequences:** Clean separation of HR access from management privileges. Scalable foundation for future HR submodule authorization. Requires explicit role assignment for existing Local Managers who need continued HR access.


## DEC-106 — Catalog Sync Uses Description-Based Matching (V2.1)

- **Date:** 2026-04-15
- **Status:** Accepted
- **Context:** The original PrimaveraCode-based catalog sync matching produced false positives because Portal items imported from SharePoint had `PrimaveraCode` values that were not real Primavera article codes. Additionally, the Primavera source itself contains duplicate descriptions (e.g., "BALL-BEARING ROLLER", "WASHER") that make automatic imports unsafe without manual review.
- **Decision:** Catalog sync matching is based **exclusively on normalized description comparison**, with ambiguity detection on both sides.
    1. **Exists:** Exact match after normalization (trim, uppercase, strip accents, remove `.`,`,`,`;`,`:`,`/`,`-`, collapse spaces).
    2. **Conflict (similar):** Similar description (one contains the other, minimum 5 chars). Requires manual review.
    3. **Conflict (source dup):** Multiple Primavera items share the same normalized description. Source-side ambiguity cannot be safely auto-imported.
    4. **Conflict (target dup):** Multiple Portal items share the same normalized description. Target-side ambiguity requires manual review.
    5. **New:** No relevant match **and** no duplicates on either side. Safe for import.
    6. **Import dedup:** Uses the same normalized description check to prevent duplicates.
- **Rationale:** Description is the most reliable human-verifiable field. Ambiguity in either source or target makes automatic classification unsafe. Conservative approach: any doubt becomes Conflict, never New or Exists.

## DEC-105 — Authorization Must Use Centralized Role Constants

- **Date:** 2026-04-15
- **Status:** Accepted
- **Context:** A sync feature was implemented with `[Authorize(Roles = "Admin")]`, but the system's administrative role has always been `"System Administrator"` (not `"Admin"`). The `"Admin"` role does not exist in the database, seed data, or anywhere in the role model. This caused all sync endpoints to return HTTP 403 for every user. The `ACCESS_MODEL.md` documentation used "Admin" as an informal shorthand, which may have contributed to the confusion.
- **Decision:** All authorization checks must reference centralized role constants, never hardcoded string literals.
    1. **Backend:** Use `RoleConstants.SystemAdministrator` (from `AlplaPortal.Domain.Constants`) in `[Authorize]` attributes and inline role checks.
    2. **Frontend:** Use `ROLES.SYSTEM_ADMINISTRATOR` (from `constants/roles.ts`) in route guards, menu visibility, and permission checks.
    3. **No aliases:** The string `"Admin"` is NOT a valid authorization role key. It must not appear in any `[Authorize]`, `roles.Contains()`, or `roles.includes()` call.
    4. **Documentation:** Where `ACCESS_MODEL.md` uses "Admin" as a display label, it must explicitly note that the real authorization key is `System Administrator`.



## DEC-104 — Innux Configuration Strategy (Phase 2B)

- **Date:** 2026-04-14
- **Status:** Accepted
- **Context:** Integration with internal systems demands strict authentication tracking. During implementation of Phase 2B, establishing connection capabilities to the existing Biometrics database (Innux) demanded parity with the Primavera standards (preventing fallback session reliance). 
- **Decision:** The Innux configuration must remain strictly explicitly driven.
    1. **Identity Isolation:** Innux exclusively evaluates explicitly provided appsettings configuration overrides without falling back to host execution states.
    2. **Graceful Failures:** Failures correctly propagate via `NOT_CONFIGURED` or real payload responses (e.g., login failure constraints), avoiding silent fallbacks or implicit data exposure.
    3. **Runtime Scope:** `InnuxIntegrationProvider` inherits a `TIME_ATTENDANCE` category natively, superseding hardcoded metadata arrays inside Phase 1 schema definitions.


## DEC-102 — Explicit Primavera Provider Connectivity Configuration (Phase 1B)

- **Date:** 2026-04-14
- **Status:** Accepted
- **Context:** During the stabilization of Phase 1A (diagnostic connection path), the underlying ADO.NET stack timed out parsing TLS SNI handshakes. Furthermore, relying on desktop-session Windows Authentication proxies from the web server context proved unreliable due to constraint boundaries.
- **Decision:** The integration provider must enforce explicitly configured identity resolution.
    1. **Identity Isolation:** Providers must exclusively use credentials explicitly declared in configuration files or DB override settings. They must completely ignore the active context of the user driving the UI.
    2. **Authentication Method:** SQL Authentication is enforced as the canonical operation mode for environments bridging legacy domain boundaries. Windows Authentication proxying is abandoned to prevent "double hop" drops.
    3. **Connection Fallback Policy:** The SQL string builder forces `Encrypt=Optional` to guarantee fallback to Named Pipes / Unencrypted TCP, averting 21-second drop cycles. 
    4. **Read-Only App Intent Removed:** Removed `ApplicationIntent.ReadOnly` flag as standalone, non-availability-group clustered resources drop connections demanding a strictly readable secondary.

## DEC-101 — Primavera Provider Activation Lifecycle (Phase 1A)

- **Date:** 2026-04-14
- **Status:** Accepted
- **Context:** Phase 1A introduces the first concrete `IIntegrationProvider` (Primavera). A decision was needed on whether the provider should be automatically enabled in all environments upon migration, or whether activation should require explicit configuration.
- **Decision:** Provider implementation does **not** imply automatic activation.
    1. **Seed state**: `IsPlanned = false, IsEnabled = false` — the provider is no longer a roadmap item, but it is not auto-enabled.
    2. **Activation depends on configuration**: the provider only becomes testable when `appsettings.json` has `Integrations:Primavera:Enabled = true` and valid connection settings (`Server`, `DatabaseName`).
    3. **Authentication flexibility**: Both `SQL` and `WINDOWS` authentication modes are supported. No default is assumed — the mode must be explicitly configured per environment.
    4. **Diagnostic query**: Uses `SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName` instead of `SELECT 1` for richer diagnostics without business-domain coupling.
    5. **Read-only enforcement**: Connection string includes `ApplicationIntent.ReadOnly` to prevent accidental writes at the transport level.
    6. **Multi-database awareness**: The appsettings configuration is an initial connection template, not a permanent binding to a single database. Primavera has multiple productive databases (PRI297514001, PRI297514003). Phase 1A does not assume a single-database strategy.
- **Alternatives considered:** Auto-enabling Primavera via seed (rejected: creates "active but misconfigured" states in environments without valid config). Using `SELECT 1` only (rejected: less diagnostic value). Defaulting to Windows Auth (rejected: not universally applicable — depends on app pool identity and service account).
- **Consequences:** Safer deployments — no unexpected "active but misconfigured" providers. Each environment controls its own activation. Future providers (Innux, etc.) should follow the same lifecycle pattern.

---

## DEC-100 — Generic Integration Foundation (Phase 0)

- **Date:** 2026-04-14
- **Status:** Accepted
- **Context:** The Portal requires a foundation for integrating with external systems (Primavera ERP, Innux Biometric, and future SQL/API providers) across multiple business domains (employees, materials, suppliers, departments, cost centers, attendance). An "employee-only integration framework" or provider-specific coupling was explicitly rejected.
- **Decision:** Implement a **generic, provider-oriented integration platform foundation**:
    1. **Provider Registry**: `IntegrationProvider` entity with JSON-based capabilities. Future domains are metadata, not schema changes.
    2. **Minimal Base Contract**: `IIntegrationProvider` defines only identity and connectivity. Business-domain services (e.g., `IPrimaveraEmployeeService`) will be layered on top in future phases.
    3. **Settings Separation**: `IntegrationProviderSettings` is strictly connection-oriented (server, auth, timeout). Business/domain settings are explicitly prohibited here and belong in dedicated entities.
    4. **Separate Controllers**: `IntegrationHealthController` (external providers) vs `AdminDiagnosticsController` (internal services like OCR). Intentionally not merged.
    5. **Status Code Contract**: Backend uses stable constants (`IntegrationStatusCodes`). Frontend maps to display labels independently.
    6. **Guards**: Connection testing is disabled for planned, unconfigured, or unimplemented providers. Credentials are encrypted via `AesEncryptionHelper` and never exposed to frontend.
    7. **Phase 0 Scope**: Foundation only — no data sync, no writes, no business operations, no background jobs. Only Primavera and Innux seeded.
- **Alternatives considered:** Employee-specific integration framework (rejected: not extensible). Merging with AdminDiagnosticsController (rejected: different architectural categories). Relational capabilities table (rejected: premature for Phase 0).
- **Consequences:** Future providers simply implement `IIntegrationProvider`, register in DI, seed the database, and appear automatically in the UI. Domain-specific services can be layered without refactoring the foundation. See `docs/INTEGRATION_PLAYBOOK.md` for the step-by-step guide.

---

## DEC-099 — Route-Level Code Splitting Strategy

- **Date:** 2026-04-13
- **Status:** Accepted
- **Context:** The React SPA loaded all page components in a single monolithic bundle (~1,509 kB), causing slow initial page loads even for users who only need the login screen or dashboard.
- **Decision:** Implement route-level code splitting using `React.lazy()` and `Suspense` in `App.tsx`.
    1. **Eagerly Loaded**: Only the authentication critical path (`LoginPage`, `ResetPasswordPage`, `ChangePasswordPage`) and `Dashboard` remain in the main bundle.
    2. **Lazy Loaded**: All other ~20 page components are converted to lazy imports, each generating its own chunk.
    3. **Fallback**: A shared `LoadingSkeleton` component provides a layout-aware shimmer animation during chunk retrieval.
    4. **Auth Guard Ordering**: `ProtectedRoute` wrappers are placed outside the `Suspense` boundary to ensure authentication checks execute before any lazy chunk is downloaded.
- **Alternatives considered:** Component-level splitting (rejected: too granular, marginal gains for high complexity). No splitting (rejected: unacceptable initial load time for a production portal).
- **Consequences:** Core JS bundle reduced from ~1,509 kB to ~446 kB (~70% reduction). All new pages must follow the lazy-loading pattern unless they are part of the authentication critical path.

---

## DEC-098 — Deferred Accessibility/Focus and Motion Polish

- **Date:** 2026-04-13
- **Status:** Accepted
- **Context:** During the `RequestEdit.tsx` modernization planning, accessibility improvements (focus management, trap focus, keyboard navigation) and motion polish (`AnimatePresence` transitions) were identified as valuable but orthogonal to the structural decomposition goal.
- **Decision:** Explicitly defer accessibility/focus management and motion-polish work to a future dedicated cycle. These improvements should not be mixed into structural refactoring phases because they require distinct validation criteria, testing approaches, and user-facing verification.
- **Alternatives considered:** Including accessibility fixes within the modernization cycle (rejected: mixes concerns, increases risk, and complicates manual verification of structural changes).
- **Consequences:** The current codebase has no regressions in accessibility or motion relative to its pre-modernization state, but also no improvements. A future dedicated cycle is recommended when accessibility becomes a priority.

---

## DEC-097 — Skip Generic FormField Abstraction

- **Date:** 2026-04-13
- **Status:** Accepted
- **Context:** During the CSS Module migration (Phase 4) of the `RequestEdit.tsx` modernization, a generic `<FormField>` wrapper was considered to unify label + input + error rendering across all form fields.
- **Decision:** Do not introduce a generic `FormField` abstraction. The variation across field types is too high to justify a single wrapper:
    - Native `<input>`, `<select>`, `<textarea>` each have different DOM structures.
    - `DateInput` applies `className` to a container div, not the inner input.
    - `SupplierAutocomplete` is a fully self-contained composite component.
    - Some fields have contextual helper text, quick-action buttons, or conditional warnings.
- **Alternatives considered:** A thin `FormField` wrapper handling only label + error (rejected: insufficient value for the abstraction cost; most fields already follow a recognizable pattern with CSS Module classes).
- **Consequences:** Form fields continue to use direct CSS Module class names (`formLabel`, `formInput`, `fieldError`) applied individually. This is more verbose but avoids a leaky abstraction.

---

## DEC-096 — Incremental Decomposition of RequestEdit.tsx

- **Date:** 2026-04-13
- **Status:** Accepted
- **Context:** `RequestEdit.tsx` had grown to ~1,274 lines, combining UI rendering for general data, financial summary, status/action panels, and line items with complex workflow logic, making the file difficult to maintain and review.
- **Decision:** Decompose `RequestEdit.tsx` into a parent-child architecture using an incremental, phased approach.
    1. **Parent as Orchestrator**: `RequestEdit.tsx` retains all state management (`useRequestDetail`), event handlers, permission booleans, and workflow conditional logic. It is not a thin wrapper.
    2. **Presentational Children**: Four child components (`RequestGeneralDataSection`, `RequestFinancialSummary`, `RequestStatusActionPanels`, `RequestLineItemsSection`) receive all data and handlers via props. They do not call hooks or manage workflow state.
    3. **Phased Delivery**: Each section was extracted as an independent phase with manual `npm run build` verification between steps to prevent regressions.
    4. **Local CSS Module**: Shared inline style helpers were migrated to `request-edit.module.css`, scoped exclusively to `RequestEdit` and its children.
- **Alternatives considered:** Full rewrite of RequestEdit as a multi-page wizard (rejected: too risky, would break existing deep-linking and URL state patterns). Single-pass extraction of all sections (rejected: higher regression risk without phase-level verification checkpoints).
- **Consequences:** `RequestEdit.tsx` reduced from ~1,274 to ~660 lines (~48% reduction). Future maintenance of individual sections can be done in focused files without navigating the full workflow logic. The parent remains the only place where workflow decisions are made.

---

## DEC-095 — Dynamic SMTP Management (AES-256 Encryption)

- **Date:** 2026-04-11
- **Status:** Accepted
- **Context:** Legacy SMTP configuration was hardcoded in `appsettings.json` and required application restarts to change. Passwords were stored in plaintext within configuration files, violating security best practices for sensitive credentials.
- **Decision:** Implement a secure, database-driven SMTP management system.
    1. **Persistence Strategy**: Store SMTP settings in a dedicated `SmtpSettings` table, following the pattern established for Document Extraction settings. Database settings take precedence over file-based configuration.
    2. **Encryption at Rest**: Implement a dedicated `AesEncryptionHelper` (AES-256-CBC with HMAC-SHA256) to encrypt and verify SMTP passwords in the database. Encryption keys are securely managed via environment variables.
    3. **Resolution Fallback**: Refactor `EmailService` to resolve effective credentials via a provider chain: `Database (Encrypted)` → `appsettings.json` → `Hardcoded Defaults`.
    4. **Write-Only Security**: The API `GET` endpoint returns only a `hasPassword` boolean. The `PUT` endpoint treats the password as write-only (update only if non-blank).
    5. **On-Demand Diagnostics**: Expose a non-destructive SMTP handshake test (`TestConnectionAsync`) in the UI to verify configuration validity before saving.
- **Alternatives considered:** Plaintext database storage (rejected for security). Azure KeyVault (rejected due to on-premises deployment constraints).
- **Consequences:** Provides agility for administrators to rotate credentials without engineering intervention. Hardens security for sensitive SMTP tokens. Standardizes the "Encrypted Settings" pattern for future integrations.

---

## DEC-094 — Password Recovery Infrastructure (CID Email & Config URL)

- **Date:** 2026-04-11
- **Status:** Accepted
- **Context:** Implementing password recovery emails revealed issues with asset rendering and link reliability.
    1. **Asset Visibility**: Remote URLs for logos often fail in development (localhost not accessible from webmail) or get blocked by strict corporate filters.
    2. **Link Fragility**: Relying on `Origin` headers or auto-detection resulted in broken links during mixed environment testing.
- **Decision:** 
    1. **CID Logo Strategy**: Use `LinkedResource` to embed the ALPLA logo directly into the email body as a `cid:alpla-logo` attachment. Implementation includes a multi-path resolution helper (`ResolveLogoPath`) to find assets across dev and production layouts.
    2. **Centralized Base URL**: Centralize the destination URL for all transactional links into `AppConfig:FrontendBaseUrl` within `appsettings.json`.
    3. **Production Safety**: Throw `InvalidOperationException` in `EmailService` if a link containing `localhost` is generated while `ASPNETCORE_ENVIRONMENT != Development`.
- **Alternatives considered:** Absolute remote URLs (rejected: fragile in dev/internal networks). External CDN storage for logos (rejected: adds external dependency and CORS complexity).
- **Consequences:** Ensures branding is visible even offline or behind firewalls. Guarantees link stability across deployments. Adds a hard failure mode to prevent shipping emails with developer-local links.

---

## DEC-093 — Company-Level Final Approver Resolution

- **Date:** 2026-04-04
- **Status:** Accepted
- **Context:** Workflow participants were previously manually selected by the requester, leading to errors and inconsistent business logic. Specifically, the "Final Approver" role was broad, and the system lacked a way to resolve a specific user responsible for a given company.
- **Decision:** Implement **System-Resolved Actor Model** for the Final Approver.
    1. **Source of Truth**: The `Company` entity now holds a `FinalApproverUserId` field.
    2. **UI Management**: Extended the "Dados Mestres" UI with an "Empresas" tab, allowing administrators to manage companies and assign their respective Final Approvers.
    3. **Enforcement**: Workflow resolution in `RequestsController` is now strictly bound to this company-level mapping. Submission fails safely if no approver is assigned to the company.
    4. **Role Filtering**: The user selection for the company approver is restricted to users with the `Final Approver` role.
- **Alternatives considered:** Falling back to "any user with Final Approver role" (rejected: lacks accountability and predictability). Manual selection (rejected: prone to user error).
- **Consequences:** Ensures deterministic workflow resolution. Eliminates "missing participant" errors at submission time for properly configured companies. Centralizes governance of approval authority within the Master Data administrative workspace.

---

## DEC-091 — Official Move to Modern Corporate Design Language

- **Date:** 2026-04-03
- **Status:** Accepted
- **Context:** The "Industrial Brutalist" aesthetic served the MVP well but lacks the premium feel and information density required for the next phase of the Portal.
- **Decision:** Explicitly transition to a **Modern Corporate** design system.
    - **New Default Standards**:
        - **Rounded Corners**: 8px or 12px as the new standard (replacing the 0px default).
        - **Soft Elevations**: Low-opacity, diffused shadows (replacing the "brutal" high-contrast shadows).
        - **Subtle Borders**: Lighter borders (Slate 200) for containers, avoiding heavy default blue borders.
        - **Blue as Accent**: Blue is strictly reserved for primary actions, indicators, and focus states.
    - **Retired Standards**:
        - **Industrial Brutalist** is no longer the official direction.
        - **0px radius** is deprecated as a default.
        - **Brutal/Heavy shadows** are no longer used for base components.
        - **Heavy blue borders** on all containers are discontinued.
    - **Migration Policy**: Existing screens will coexist with the new standards and be migrated in phases. All new or refactored screens must follow the Modern Corporate direction.
- **Alternatives considered:** Maintaining Industrial Brutalist (rejected as less suitable for a broad corporate audience).
- **Consequences:** Provides a more premium and accessible interface. Requires a phased refactor of core design tokens and shell components in the next implementation cycle.

---

## DEC-092 — Modern Corporate UI Refinement (Phase 2 Implementation)

- **Date:** 2026-04-03
- **Status:** Accepted
- **Context:** Following the establishment of the visual foundation (Phase 1), the system required a second phase of focused refinement on core operational screens (Dashboard, Requests List, Request Edit, Receiving Workspace) to ensure high-fidelity corporate standards and premium interactive quality.
- **Decision:** Implement specialized refinements across high-traffic operational areas:
    - **Premium Shadows**: Introduced `var(--shadow-premium)` (a multi-layered, ultra-soft indigo-tinted shadow) for primary entrance points like the Login card and Dashboard summary sections.
    - **Typography Density**: Standardized on `900` font-weight for all primary screen headers and section titles to create a strong, authoritative hierarchy.
    - **Header Consolidation**: Replaced fragmented header styles with a unified "Page Header" pattern featuring subtle bottom borders and consistent vertical rhythm.
    - **Operational List Refinement**: Transitioned the `RequestsList` from a high-contrast grid to a "Soft-Row" pattern—using `8px` rounded corners, lighter borders (`0.08` opacity), and subtle hover backgrounds.
    - **Interactive States**: Standardized hover/focus states to use soft tints of the corporate blue palette instead of the previous high-contrast brutalist borders.
- **Alternatives considered:** Full-page redesigns (rejected as too disruptive to existing user workflows).
- **Consequences:** Results in a significantly more mature and cohesive enterprise-grade interface. Maintains operational density while improving overall visual comfort and perceived quality.

---

## DEC-072 — Structural Root Grid for Shell Continuity

- **Date:** 2026-03-26
- **Status:** Accepted
- **Context:** Initial migration to Shell 2.0 resulted in a "floating card" sidebar and misaligned topbar on large screens due to max-width constraints on the outer shell.
- **Decision:** Transition to a full-viewport root grid in `AppShell`.
    1. Grid: `grid-template-columns: var(--sidebar-width) 1fr`.
    2. Sidebar: Direct grid child with `grid-row: 1 / -1`.
    3. Content: `maxWidth` moved to an inner wrapper strictly for the business area.
- **Alternatives considered:** Using `position: fixed` for all shell elements. Rejected as it creates brittle compensation logic for sidebar width changes.
- **Consequences:** Ensures the application frame always spans the full viewport while keeping business content readable and centered, resulting in a premium and stable shell look.

---

---

## DEC-071 — Shell 2.0 Collapsible Layout Strategy

- **Date:** 2026-03-26
- **Status:** Accepted
- **Context:** The move to Shell 2.0 required a collapsible sidebar that does not break the layout of existing business screens.
- **Decision:** Use a dynamic CSS Grid layout in `AppShell`. 
    1. Grid defined as: `grid-template-columns: var(--sidebar-width) minmax(0, 1fr)`.
    2. Sidebar width managed via a CSS variable `--sidebar-width` controlled by React state.
    3. Topbar uses `position: fixed` with a dynamic `left` offset matching the sidebar width.
- **Alternatives considered:** Switching to a pure Flexbox or Absolute positioning model. Rejected as too invasive for existing grid-dependent pages.
- **Consequences:** Achieves a modern "push" layout with 0.3s transitions while keeping legacy feature tables accessible and correctly rendered inside the dynamic grid container.

---

## DEC-070 — Frontend Modernization Foundation (v2.0)

- **Date:** 2026-03-26
- **Status:** Accepted
- **Context:** The Alpla Portal frontend required a modern foundation to support the Shell 2.0 migration, React 19 features, and the more performant Tailwind CSS 4 engine.
- **Decision:** Upgrade the core frontend stack:
  1. **React 19**: Migrate for long-term support and improved rendering performance.
  2. **React Router 7**: Adopt the latest routing standard while preserving declarative mode for stability.
  3. **Tailwind CSS 4**: Implement the CSS-first engine using the `@tailwindcss/vite` plugin to eliminate legacy JS-based configuration overhead.
  4. **Vite 6**: Standardize on Vite 6 to support the new Tailwind engine and React 19.
  5. **Motion/React**: Replace heavy `framer-motion` imports with the modular `motion/react` package.
- **Consequences:** Major leap in developer experience and build performance. Requires stricter TypeScript handling for `useRef` and `RefObject`. Foundation is now fully aligned with the Shell 2.0 technical requirements.

---


## DEC-069 — Alpla Shell 2.0 Migration Strategy

- **Date:** 2026-03-26
- **Status:** Accepted
- **Context:** Need to adopt the modern "Shell 2.0" UI redesign without disrupting production stability or losing complex business logic.
- **Decision:** Use a phased hybrid migration:
  1. Maintain `React Router` architecture.
  2. Integrate `TailwindCSS` mapped to existing `tokens.css`.
  3. Lead with `RequestsList` as the Pilot screen.
  4. Preserve all backend `api.ts` and RBAC logic exactly as-is.
- **Consequences:** Low-risk path to modernization, consistent visual language, and easier maintenance during the transition.

---

## DEC-001 — Adopt 3-layer DOE architecture

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Need reliable execution with AI orchestration and deterministic scripts
- **Decision:** Use Directive / Orchestration / Execution architecture
- **Consequences:** Better maintainability, clearer responsibilities, easier debugging

---

## DEC-002 — Use `.tmp/` for intermediates and keep deliverables in cloud services

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Local files are temporary processing artifacts and should not be treated as final outputs
- **Decision:** Store intermediates in `.tmp/`; keep user-facing deliverables in cloud services whenever applicable
- **Consequences:** Cleaner repository, easier regeneration, less confusion about final outputs

---

## DEC-003 — Request Line Items in V1

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Deciding whether V1 needs a separate line item entity or just a header-level total amount.
- **Decision:** Include a generic 1:N `RequestLineItem` entity for both Purchase and Payment requests. Keep workflow/approvals strictly at the header level.
- **Consequences:** Provides better detail out-of-the-gate and flexibility for future ERP sync, but avoids workflow complexity by restricting item-level partial approvals.

---

## DEC-004 — V1 Technical Stack (ASP.NET Core + React)

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Deciding the most stable and maintainable technologies for an internal portal deployed on an existing Windows Server + SQL Server on-premises infrastructure.
- **Decision:** Use ASP.NET Core 8 Web API for the backend, EF Core for the ORM, and React (Vite) for the frontend SPA.
- **Alternatives considered:** Node.js (NestJS) and Python (FastAPI/Django). Both were rejected due to the operational complexity of hosting them natively and reliably on Windows Server IIS without Docker.
- **Consequences:** Provides native IIS synergy, highly secure enterprise authorization middleware, and strict typing. Requires team familiarity with C#/.NET.

---

## DEC-005 — Physical Database PKs and Navigation Properties (EF Core)

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Deciding the physical implementation details for the first EF Core migration based on the V1 Data Model Draft.
- **Decision:** Use `Guid` for all primary keys on transactional tables (`Request`, `RequestLineItem`, `RequestStatusHistory`, `RequestAttachment`) to prevent ID-guessing in the API. Use `int` for static/administrative lookup tables (`Status`, `Priority`, `Currency`, `Department`, etc.) to keep foreign key payload sizes small.
- **Consequences:** Ensures excellent security for transactions while remaining performant and lightweight for simple Lookups.

---

## DEC-006 — EF Core Cascade Delete Constraints on Audit Fields

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Deciding how to handle SQL Server's "multiple cascade paths" error when a `Request` and its child entities (like `RequestAttachment` or `RequestStatusHistory`) both reference the same `User` table (e.g., `RequesterId` vs `UploadedByUserId`).
- **Decision:** Foreign keys representing audit traceability or metadata ownership (like `UploadedByUserId`, `CreatedByUserId`, `ActorUserId`) must explicitly implement `DeleteBehavior.NoAction` (or `Restrict`) in their EF Core configurations.
- **Consequences:** Prevents SQL Server deployment crashes and ensures biological audit history is preserved even if a `User` identity were to be hard-deleted from the directory, preventing accidental destruction of associated request timelines or file matrices.

---

## DEC-007 — Formalize Master Data Guidelines handling

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** The project requires a standard procedure for introducing reusable lookup/reference values (Units, Currencies, Cost Centers, etc.) to prevent technical debt from hardcoded UI/API enumerations.
- **Decision:** All Master Data must follow the steps defined in `MASTER_DATA_GUIDELINES.md`. This mandates DB-driven lookups, EF Core seeding, soft-deletion principles, and dynamic read-only REST API endpoints for UI hydration.
- **Consequences:** Slightly higher initial scaffolding overhead when adding a dropdown, but guarantees consistent UI states, database referential integrity, and simpler integration with external ERP systems later.

## DEC-008 — Seamless Continuous Flow for Request Creation

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** Deciding how to allow users to add Line Items immediately after creating a new Request Draft without duplicating massive amounts of UI state in `RequestCreate.tsx`.
- **Decision:** Use a seamless local route transition (`navigate('/requests/{id}/edit', { replace: true })`) immediately upon successful POST of the Draft Header. The user's screen instantly updates to reveal the Line Items section without breaking context.
- **Superseded by:** DEC-045 (Consolidated Redirection)
- **Consequences:** Keeps `RequestCreate.tsx` simple and strictly focused on the Header POST. Eliminates the need for a complex "wizard" state machine, while providing the exact UX required (Header -> Items seamlessly).

---

## DEC-009 — Standardize Form Validation and Monetary Input Execution

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** Deciding how to gracefully intercept Backend API validation errors (HTTP 400 Bad Request) without bouncing users abruptly to generic error screens. And standardizing currency input formats visually to Angolan/European standards (`1.000,50`).
- **Decision:** Implement inline form validation as a project standard. Catch `ValidationProblemDetails` using `api.ts`, parse them into a dictionary (`Record<string, string[]>`), and render them conditionally below specific violating inputs `getInputStyle(PropName)` inside React state. Utilize a native `<CurrencyInput>` component bridging `Intl.NumberFormat` locally to physical raw string models dynamically.
- **Consequences:** Ensures excellent ergonomics for data-entry intensive screens, mitigating lost local edits while maintaining strict model coherence between the database decimal structures and localized frontend UI masks.

---

## DEC-014 — Unified Request Identification and Global Sequence

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Previous numbering strategy used type-specific prefixes (`PAG-`, `COM-`) and per-day sequences, which proved confusing. The requirement is for a unified, human-readable format that ensures monotonic sequence growth without reuse, even if records are deleted.
- **Decision:** Switch to format `REQ-DD/MM/YYYY-SequentialNumber` (where SequentialNumber is at least 3 digits). Centralize generation around a single `GLOBAL_REQUEST_COUNTER` key in the `SystemCounter` entity.
- **Consequences:** Provides a consistent, professional identifier across all request types. Prevents sequence "gaps" or "collisions" from affecting business predictability. Decouples the visual ID from technical key constraints or type-based logic.

---

## DEC-049 — Exceção de Edição de Fornecedor em Aguardando Cotação

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** Na etapa `WAITING_QUOTATION` (Aguardando Cotação), o pedido é tecnicamente bloqueado para edição de cabeçalho (para preservar a intenção original do solicitante). No entanto, a seleção do fornecedor e o upload da Proforma são requisitos obrigatórios para a conclusão desta etapa pelo Comprador.
- **Decision:** Implementar uma exceção de permissão cirúrgica para o campo **Fornecedor** durante a etapa de cotação.
  - **Frontend**: Introduzir a flag `canEditSupplier` que inclui `WAITING_QUOTATION`, permitindo que o `SupplierAutocomplete` permaneça editável.
  - **Backend**: Atualizar `UpdateRequestDraft` para permitir a alteração de `SupplierId` quando o pedido estiver em `WAITING_QUOTATION`, enquanto continua bloqueando alterações em outros campos do cabeçalho.
- **Consequences:** Melhora a fluidez do workflow de cotação. Garante que os dados necessários para a transição estejam disponíveis no sistema antes da conclusão. Mantém a segurança do cabeçalho contra alterações não autorizadas em outros campos críticos (Departamento, Planta, etc).

---

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Requests in `WAITING_QUOTATION` status were initially read-only. However, the Buyer needs to refine request data and insert final quotation values (prices, specific line items) before completion.
- **Decision:** Define `WAITING_QUOTATION` as an **active operational editing stage**. Hide the read-only banner and enable form/item mutations. Structural integrity is preserved by locking the `RequestType` field after the initial creation.
- **Consequences:** Provides the necessary flexibility for the Buyer to complete their procurement duties within the portal, while maintaining workflow invariants.

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to present API response statuses (successes and errors) to users on very tall, scrolling Request forms so they are never lost off-screen.
- **Decision:** Use a sticky `Feedback` component positioned in the top action bar. Re-map successes and errors via React `Location` state on navigations to ensure they persist across route hops.
- **Consequences:** Guarantees that users always see the result of their actions immediately, regardless of scroll position, and provides a unified "Feedback" language throughout the application.

---

## DEC-011 — Permanent Use of Portuguese for Status and History Visibility

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding on the language for user-facing audit logs and status names in the workflow.
- **Decision:** While internal status codes remain in stable English (e.g., `WAITING_AREA_APPROVAL`), all display names and history comments must be strictly in Portuguese for readability and business clarity.
- **Consequences:** Ensures the application feels native to the primary users in Angola while maintaining a predictable development environment.

---

## DEC-012 — Temporary Permission Model for Area Approval (v0.9.5)

- **Date:** 2026-03-01
- **Status:** Accepted (Temporary)
- **Context:** Implementing workflow actions before a full role-based access control (RBAC) / JWT authentication system is in place.
- **Decision:** For v0.9.5, approval actions (Approve, Reject, Request Adjustment) are visible to any user who can access the `RequestEdit` page for a request in the `WAITING_AREA_APPROVAL` status. Hardcode `dev@alpla.com` as the actor in history logs.
- **Consequences:** Allows testing and early use of the workflow logic, with the explicit understanding that proper actor/permission enforcement is a mandatory next phase.

---

## DEC-013 — AREA_ADJUSTMENT Semantic Grounding

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Reusing the `AREA_ADJUSTMENT` status code for the "Solicitar Reajuste" action.
- **Decision:** In the context of the Area Approval workflow for PAYMENT requests, the internal code `AREA_ADJUSTMENT` specifically represents the "Reajuste A.A" (Solicitor Rework) stage.
- **Consequences:** UI labels, badges, and history entries must always use the Portuguese term "Reajuste A.A" to maintain semantic clarity for the user, even if the backend code is more generic.
- **Decision:** Embed explicit, standardized text feedback banners natively inside `position: 'sticky'` layout containers anchoring to the top viewport edge. For cross-page transitions (like returning after draft creation), rely on `react-router-dom` `Location.state` payloads to render context cleanly on mount.
- **Consequences:** Provides massive UX clarity. Prevents users from wondering "did my save work?" when the form reloads.

---

## DEC-011 — Consolidating Urgency and Priority Fields

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** The Request Header originally contained both `Prioridade` and `Grau de Necessidade`, creating semantic overlap and UX ambiguity. Additionally, line items lacked a way to express relative importance within a single Request.
- **Decision:** Drop the `Priority` Master Data entirely from the `Request` header. Rely exclusively on `NeedLevel` (rename UI label to "Grau de Necessidade do Pedido"). Introduce a separate, numeric `ItemPriority` (int) field onto `RequestLineItem` to allow users to rank items 1-N.
- **Consequences:** Eliminates confusion over what makes a request "Urgent" vs "Critical". Enables item-by-item triage for buyers. Requires a breaking EF Core schema migration (dropped Priority table).

---

## DEC-012 — Request Form Logical Sections & Validations

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The Request UI forms were vertically overwhelming and prone to submitting without fundamental identifiers like "Operation Type" or crucial "Workflow Participants".
- **Decision:** Break the Request form visually into semantic containers: General, Participants, and Financials. Force `Tipo de Pedido` and all Approvers to trigger explicit inline validations. Restrict manual editing of the `Estimated Total Amount` field so it inherently tracks Line Items mathematically.
- **Consequences:** Provides a significantly more guided, fail-proof experience. Mitigates accidental draft submissions without approvers.

---

## DEC-013 — URL-Driven List State Preservation

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to preserve complex list filter states (page size, search terms, status filters) when users navigate away to edit/view a Detail record and then navigate "back".
- **Decision:** Shift list state out of React local memory directly into the URL query string via `useSearchParams()`. When moving into a Detail/Edit view, append that query string inside `react-router-dom`'s `Location.state` (`{ fromList: location.search }`). Any "Cancel" or "Return" actions from the Detail view re-apply that exact payload onto the `/requests` routing destination.
- **Consequences:** Creates a robust, natively bookmarkable list implementation. Allows users to confidently deep-dive into items directly out of paginated sub-filters without fear of losing their exact scroll/search coordinates.

---

## DEC-014 — Department and Plant as Managed Master Data

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The Request header required two new organizational classification fields — Departamento (Department) and Planta (Plant). The initial temptation was to implement them as free-text inputs to ship faster.
- **Decision:** Implement both as proper Master Data entities (`Department`, `Plant`) with `Id`, `Code`, `Name`, `IsActive` following the standard defined in `MASTER_DATA_GUIDELINES.md`. Both are selectable in the Request form via managed dropdowns and fully maintainable in the `Dados Mestres` settings area.
- **Alternatives considered:** Free-text fields on the Request (rejected — no referential integrity, no filtering/aggregation support, no governance over valid values).
- **Consequences:** Requires a DB migration, new API endpoints, and UI management screens. Trade-off is worthwhile for data consistency, future reporting, and alignment with the enterprise data model.

---

## DEC-015 — Supplier as Managed Master Data

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to store Supplier information on the Request.
- **Decision:** Implement `Supplier` as a managed Master Data entity (`Id`, `Code`, `Name`, `TaxId`, `IsActive`). This facilitates data integrity, prevents typos in payment processing, and enables future ERP synchronization via `TaxId` (NIF).
- **Consequences:** Requires a dedicated management UI and DB table. Significantly improves financial auditing and consistency.

---

## DEC-016 — Request Type Standardization (QUOTATION/PAYMENT)

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The business model requires exactly two flows: asking for a price (Quotation) vs processing a known invoice (Payment).
- **Decision:** Standardize on two core `RequestType` codes: `QUOTATION` and `PAYMENT`. Rename legacy "Purchase" label to "Quotation".
- **Stage 5 Workflow Correction**: Unified post-PO operational flow. Both `QUOTATION` and `PAYMENT` requests now follow the `PO_ISSUED -> PAYMENT_SCHEDULED -> PAYMENT_COMPLETED -> WAITING_RECEIPT` sequence. Removed direct bypass for Quotation requests.
- **Stage 5 Contract Alignment**: Final alignment pass completing DTO projections and removing fragile `any` mappings.
- **Consequences:** Simplifies UI conditional logic. `Supplier` is only required for `PAYMENT` requests.

## DEC-017: Conditional Post-Creation Navigation

**Date:** 2026-02-27  
**Status:** Approved  
**Context:** Creating a `QUOTATION` request is often an administrative step before a longer commercial process. Creating a `PAYMENT` request usually implies immediate entry of line items.  
**Decision:** We use a conditional redirect in `RequestCreate.tsx`:

- `QUOTATION` (Code-based check) -> Redirect to `/requests` (List).
- `PAYMENT` (Code-based check) -> Redirect to `/edit` (Item Entry).
- **Superseded by:** DEC-045 (Consolidated Redirection)
**Consequences:** Improved UX flow tailored to business necessity. Success message persistence is handled via React Router state and captured by the List component.

---

## DEC-065: Quotation Editor Mode Separation

**Context**: A regression caused new quotation flows to inherit the "Edit Mode" UI (badge, title, update button) if a previous edit session had been active for the same request.

**Decision**:

1. Explicitly reset `editingQuotationId`, `draftProformaFiles`, and `quotationDrafts` state for the specific `requestId` when starting a new manual or OCR flow.
2. Update `handleResetToSelect` (cancel/back logic) to also perform a full state cleanup for the request.
3. Differentiate the UI titles ("Registrar Nova Cotação" vs "Editar Cotação") to provide clear visual feedback to the buyer.

**Consequences**:

- Guaranteed clean state for every new quotation attempt.
- Prevents UI state "leaks" between different operational actions on the same Request.
- Improves scanability and reduces buyer confusion regarding the current operation.

---

## DEC-024 — Master Data Feedback and Inline Validation

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding how to standardize feedback and validation across Master Data screens to match the Request screens.
- **Decision:**
    1. Integrate the `Feedback.tsx` component into all Master Data screens for post-action messaging (success/error).
    2. Implement debounced inline uniqueness validation for critical fields (like Supplier Name and PrimaveraCode) to prevent submission failures.
    3. Standardize the "Brutalist" input styling for all Master Data form fields.
- **Consequences:** Provides a cohesive brand identity and UX patterns across the entire portal. Reduces user frustration by providing immediate validation feedback before form submission.

---

## DEC-025 — Searchable Dropdown Pattern (Combobox)

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** Large master data sets (like Suppliers) require a selection mechanism that scales beyond simple dropdowns but feels more integrated than detached autocompletes.
- **Decision:** Standardize on a "Searchable Dropdown" (Combobox) pattern for large lookups:
    1. **Immediate Feedback**: Open options on focus/click even if the query is empty.
    2. **Integrated Search**: Filter results live as the user types within the same dropdown container.
    3. **Visual Structure**: Use formatted results (e.g., `[Code] Name`) to ensure unique identification in the list.
    4. **Chevron Indicator**: Add a visual cue (Chevron) to signify dropdown behavior.
- **Consequences:** Provides a familiar "Dropdown" experience for users while maintaining the performance and scalability of an async search.

---

## DEC-018 — Persistent Request Numbering Strategy

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deleting drafts caused the system to reuse sequential numbers because the previous logic depended on counting existing records. This resulted in duplicate request numbers.
- **Decision:** Use a dedicated `SystemCounters` table to store persistent, monotonically increasing counters. Every request type has its own counter key per day (e.g., `REQ_NO_QUOTATION_20260227`). The number allocation is decoupled from the `Requests` table content.
- **Consequences:** Ensures non-reusable and unique request numbers even after record deletion. Gaps in the sequence are expected and acceptable. Added a database-level unique index on `RequestNumber` as a final safeguard.

---

## DEC-019 — Single Currency Enforcement & Header Edit Lock

- **Date:** 2026-02-28
- **Status:** Accepted
---

## DEC-049 — Exceção de Edição de Fornecedor em Aguardando Cotação

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** Na etapa `WAITING_QUOTATION` (Aguardando Cotação), o pedido é tecnicamente bloqueado para edição de cabeçalho (para preservar a intenção original do solicitante). No entanto, a seleção do fornecedor e o upload da Proforma são requisitos obrigatórios para a conclusão desta etapa pelo Comprador.
- **Decision:** Implementar uma exceção de permissão cirúrgica para o campo **Fornecedor** durante a etapa de cotação.
  - **Frontend**: Introduzir a flag `canEditSupplier` que inclui `WAITING_QUOTATION`, permitindo que o `SupplierAutocomplete` permaneça editável.
  - **Backend**: Atualizar `UpdateRequestDraft` para permitir a alteração de `SupplierId` quando o pedido estiver em `WAITING_QUOTATION`, enquanto continua bloqueando alterações em outros campos do cabeçalho.
- **Consequences:** Melhora a fluidez do workflow de cotação. Garante que os dados necessários para a transição estejam disponíveis no sistema antes da conclusão. Mantém a segurança do cabeçalho contra alterações não autorizadas em outros campos críticos (Departamento, Planta, etc).

---

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Requests in `WAITING_QUOTATION` status were initially read-only. However, the Buyer needs to refine request data and insert final quotation values (prices, specific line items) before completion.
- **Decision:** Define `WAITING_QUOTATION` as an **active operational editing stage**. Hide the read-only banner and enable form/item mutations. Structural integrity is preserved by locking the `RequestType` field after the initial creation.
- **Consequences:** Provides the necessary flexibility for the Buyer to complete their procurement duties within the portal, while maintaining workflow invariants.

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to present API response statuses (successes and errors) to users on very tall, scrolling Request forms so they are never lost off-screen.
- **Decision:** Use a sticky `Feedback` component positioned in the top action bar. Re-map successes and errors via React `Location` state on navigations to ensure they persist across route hops.
- **Consequences:** Guarantees that users always see the result of their actions immediately, regardless of scroll position, and provides a unified "Feedback" language throughout the application.

---

## DEC-011 — Permanent Use of Portuguese for Status and History Visibility

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding on the language for user-facing audit logs and status names in the workflow.
- **Decision:** While internal status codes remain in stable English (e.g., `WAITING_AREA_APPROVAL`), all display names and history comments must be strictly in Portuguese for readability and business clarity.
- **Consequences:** Ensures the application feels native to the primary users in Angola while maintaining a predictable development environment.

---

## DEC-012 — Temporary Permission Model for Area Approval (v0.9.5)

- **Date:** 2026-03-01
- **Status:** Accepted (Temporary)
- **Context:** Implementing workflow actions before a full role-based access control (RBAC) / JWT authentication system is in place.
- **Decision:** For v0.9.5, approval actions (Approve, Reject, Request Adjustment) are visible to any user who can access the `RequestEdit` page for a request in the `WAITING_AREA_APPROVAL` status. Hardcode `dev@alpla.com` as the actor in history logs.
- **Consequences:** Allows testing and early use of the workflow logic, with the explicit understanding that proper actor/permission enforcement is a mandatory next phase.

---

## DEC-013 — AREA_ADJUSTMENT Semantic Grounding

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Reusing the `AREA_ADJUSTMENT` status code for the "Solicitar Reajuste" action.
- **Decision:** In the context of the Area Approval workflow for PAYMENT requests, the internal code `AREA_ADJUSTMENT` specifically represents the "Reajuste A.A" (Solicitor Rework) stage.
- **Consequences:** UI labels, badges, and history entries must always use the Portuguese term "Reajuste A.A" to maintain semantic clarity for the user, even if the backend code is more generic.
- **Decision:** Embed explicit, standardized text feedback banners natively inside `position: 'sticky'` layout containers anchoring to the top viewport edge. For cross-page transitions (like returning after draft creation), rely on `react-router-dom` `Location.state` payloads to render context cleanly on mount.
- **Consequences:** Provides massive UX clarity. Prevents users from wondering "did my save work?" when the form reloads.

---

## DEC-011 — Consolidating Urgency and Priority Fields

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** The Request Header originally contained both `Prioridade` and `Grau de Necessidade`, creating semantic overlap and UX ambiguity. Additionally, line items lacked a way to express relative importance within a single Request.
- **Decision:** Drop the `Priority` Master Data entirely from the `Request` header. Rely exclusively on `NeedLevel` (rename UI label to "Grau de Necessidade do Pedido"). Introduce a separate, numeric `ItemPriority` (int) field onto `RequestLineItem` to allow users to rank items 1-N.
- **Consequences:** Eliminates confusion over what makes a request "Urgent" vs "Critical". Enables item-by-item triage for buyers. Requires a breaking EF Core schema migration (dropped Priority table).

---

## DEC-012 — Request Form Logical Sections & Validations

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The Request UI forms were vertically overwhelming and prone to submitting without fundamental identifiers like "Operation Type" or crucial "Workflow Participants".
- **Decision:** Break the Request form visually into semantic containers: General, Participants, and Financials. Force `Tipo de Pedido` and all Approvers to trigger explicit inline validations. Restrict manual editing of the `Estimated Total Amount` field so it inherently tracks Line Items mathematically.
- **Consequences:** Provides a significantly more guided, fail-proof experience. Mitigates accidental draft submissions without approvers.

---

## DEC-013 — URL-Driven List State Preservation

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to preserve complex list filter states (page size, search terms, status filters) when users navigate away to edit/view a Detail record and then navigate "back".
- **Decision:** Shift list state out of React local memory directly into the URL query string via `useSearchParams()`. When moving into a Detail/Edit view, append that query string inside `react-router-dom`'s `Location.state` (`{ fromList: location.search }`). Any "Cancel" or "Return" actions from the Detail view re-apply that exact payload onto the `/requests` routing destination.
- **Consequences:** Creates a robust, natively bookmarkable list implementation. Allows users to confidently deep-dive into items directly out of paginated sub-filters without fear of losing their exact scroll/search coordinates.

---

## DEC-014 — Department and Plant as Managed Master Data

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The Request header required two new organizational classification fields — Departamento (Department) and Planta (Plant). The initial temptation was to implement them as free-text inputs to ship faster.
- **Decision:** Implement both as proper Master Data entities (`Department`, `Plant`) with `Id`, `Code`, `Name`, `IsActive` following the standard defined in `MASTER_DATA_GUIDELINES.md`. Both are selectable in the Request form via managed dropdowns and fully maintainable in the `Dados Mestres` settings area.
- **Alternatives considered:** Free-text fields on the Request (rejected — no referential integrity, no filtering/aggregation support, no governance over valid values).
- **Consequences:** Requires a DB migration, new API endpoints, and UI management screens. Trade-off is worthwhile for data consistency, future reporting, and alignment with the enterprise data model.

---

## DEC-015 — Supplier as Managed Master Data

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to store Supplier information on the Request.
- **Decision:** Implement `Supplier` as a managed Master Data entity (`Id`, `Code`, `Name`, `TaxId`, `IsActive`). This facilitates data integrity, prevents typos in payment processing, and enables future ERP synchronization via `TaxId` (NIF).
- **Consequences:** Requires a dedicated management UI and DB table. Significantly improves financial auditing and consistency.

---

## DEC-016 — Request Type Standardization (QUOTATION/PAYMENT)

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The business model requires exactly two flows: asking for a price (Quotation) vs processing a known invoice (Payment).
- **Decision:** Standardize on two core `RequestType` codes: `QUOTATION` and `PAYMENT`. Rename legacy "Purchase" label to "Quotation".
- **Stage 5 Workflow Correction**: Unified post-PO operational flow. Both `QUOTATION` and `PAYMENT` requests now follow the `PO_ISSUED -> PAYMENT_SCHEDULED -> PAYMENT_COMPLETED -> WAITING_RECEIPT` sequence. Removed direct bypass for Quotation requests.
- **Stage 5 Contract Alignment**: Final alignment pass completing DTO projections and removing fragile `any` mappings.
- **Consequences:** Simplifies UI conditional logic. `Supplier` is only required for `PAYMENT` requests.

## DEC-017: Conditional Post-Creation Navigation

**Date:** 2026-02-27  
**Status:** Approved  
**Context:** Creating a `QUOTATION` request is often an administrative step before a longer commercial process. Creating a `PAYMENT` request usually implies immediate entry of line items.  
**Decision:** We use a conditional redirect in `RequestCreate.tsx`:

- `QUOTATION` (Code-based check) -> Redirect to `/requests` (List).
- `PAYMENT` (Code-based check) -> Redirect to `/edit` (Item Entry).
- **Superseded by:** DEC-045 (Consolidated Redirection)
**Consequences:** Improved UX flow tailored to business necessity. Success message persistence is handled via React Router state and captured by the List component.

---

## DEC-065: Quotation Editor Mode Separation

**Context**: A regression caused new quotation flows to inherit the "Edit Mode" UI (badge, title, update button) if a previous edit session had been active for the same request.

**Decision**:

1. Explicitly reset `editingQuotationId`, `draftProformaFiles`, and `quotationDrafts` state for the specific `requestId` when starting a new manual or OCR flow.
2. Update `handleResetToSelect` (cancel/back logic) to also perform a full state cleanup for the request.
3. Differentiate the UI titles ("Registrar Nova Cotação" vs "Editar Cotação") to provide clear visual feedback to the buyer.

**Consequences**:

- Guaranteed clean state for every new quotation attempt.
- Prevents UI state "leaks" between different operational actions on the same Request.
- Improves scanability and reduces buyer confusion regarding the current operation.

---

## DEC-024 — Master Data Feedback and Inline Validation

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding how to standardize feedback and validation across Master Data screens to match the Request screens.
- **Decision:**
    1. Integrate the `Feedback.tsx` component into all Master Data screens for post-action messaging (success/error).
    2. Implement debounced inline uniqueness validation for critical fields (like Supplier Name and PrimaveraCode) to prevent submission failures.
    3. Standardize the "Brutalist" input styling for all Master Data form fields.
- **Consequences:** Provides a cohesive brand identity and UX patterns across the entire portal. Reduces user frustration by providing immediate validation feedback before form submission.

---

## DEC-025 — Searchable Dropdown Pattern (Combobox)

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** Large master data sets (like Suppliers) require a selection mechanism that scales beyond simple dropdowns but feels more integrated than detached autocompletes.
- **Decision:** Standardize on a "Searchable Dropdown" (Combobox) pattern for large lookups:
    1. **Immediate Feedback**: Open options on focus/click even if the query is empty.
    2. **Integrated Search**: Filter results live as the user types within the same dropdown container.
    3. **Visual Structure**: Use formatted results (e.g., `[Code] Name`) to ensure unique identification in the list.
    4. **Chevron Indicator**: Add a visual cue (Chevron) to signify dropdown behavior.
- **Consequences:** Provides a familiar "Dropdown" experience for users while maintaining the performance and scalability of an async search.

---

## DEC-018 — Persistent Request Numbering Strategy

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deleting drafts caused the system to reuse sequential numbers because the previous logic depended on counting existing records. This resulted in duplicate request numbers.
- **Decision:** Use a dedicated `SystemCounters` table to store persistent, monotonically increasing counters. Every request type has its own counter key per day (e.g., `REQ_NO_QUOTATION_20260227`). The number allocation is decoupled from the `Requests` table content.
- **Consequences:** Ensures non-reusable and unique request numbers even after record deletion. Gaps in the sequence are expected and acceptable. Added a database-level unique index on `RequestNumber` as a final safeguard.

---

## DEC-019 — Single Currency Enforcement & Header Edit Lock

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** Mixed currencies within a single purchase request create accounting complexity and ambiguity in total calculations. Additionally, changing the request currency after items already exist would invalidate the consistency of those items.
- **Decision:**
    1. Enforce exactly one currency per request (defined at the header level).
    2. All line items must inherit this same currency; the item-level currency selector is removed.
    3. If a request has one or more line items, the request-level `CurrencyId` field becomes read-only to prevent inconsistencies.
- **Consequences:** Simplifies financial logic and reporting. Streamlines the item entry UX. Backend validates and normalizes item-level currency to match the header and rejects header currency changes if items exist.

---

## DEC-063 — Stage 8.5: Ownership Consolidation and Quotation Selection

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** The system contained hybrid transition rules where responsibility for selecting the winning quotation and editing cost centers was ambiguous between the Buyer and Approvers.
- **Decision:**
    1. **Quotation-First Model**: For QUOTATION requests, the official supplier is derived from the selected quotation. The `SupplierId` in the request header is treated as transient and ignored in favor of the winning quotation.
    2. **Winner Selection**: Winning quotation selection is moved exclusively to the **Final Approver** at the `WAITING_FINAL_APPROVAL` stage. The Buyer no longer selects the winner in the Items Workspace.
    3. **Cost Center Ownership**: Line item cost center editing is moved from the Buyer to the **Area Approver** at the `WAITING_AREA_APPROVAL` stage.
    4. **Role-Based detail view**: Introduced the ability to toggle the view (`userMode`) in the request detail to allow users to act in different roles if they have the necessary permissions (Simulated via `X-User-Mode`).
- **Consequences:** Ensures clear Separation of Duties (SoD). Reduces human error by centralizing financial decisions (Winner, Cost Center) with approvers, leaving operational execution to the buyer.

---

## DEC-066 — Winner Selection by Area Approver

- **Date:** 2026-03-24
- **Status:** Accepted
- **Context:** The original procurement logic required a winner selection for `QUOTATION` requests. Earlier decisions placed this at the Final Approval stage, but operational feedback indicated that Area Approvers (who often manage the requisitioning department) are better suited to make this technical/commercial selection.
- **Decision:** Move winner selection for `QUOTATION` requests to the `WAITING_AREA_APPROVAL` stage.
  - **Enforcement**: The `ApproveArea` action now requires a `SelectedQuotationId`.
  - **Backend**: `ProcessAreaApproval` validates that a winner is selected and belongs to the request.
  - **Frontend**: `RequestEdit` blocks the "APROVAR" action and shows an error if no winner is selected.
- **Consequences:** Earlier technical decision in the workflow. Final Approvers still review the selection but cannot change it.
- **Supersedes:** DEC-063 (Winner selection move)

---

## DEC-067 — Explicit Field Propagation Strategy for Document Extraction

- **Date:** 2026-03-24
- **Status:** Accepted
- **Context:** Recurring risk of losing extracted data in intermediate layers (e.g., `ExtractionMapper`, `OcrHeaderSuggestionsDto`) despite successful AI extraction.
- **Decision:** Adopt an "Explicit Propagation" strategy. Every new extraction field must be manually added to the provider mapping, internal DTO, legacy/compatibility DTO, and API mapper. Implicit or generic property bags are discouraged for core business fields to maintain strict typing and front-to-back contract visibility.
- **Consequences:** Increases initial development effort per field but guarantees reliability, discoverability, and testability across the entire pipeline.
- **Related:** [DOCUMENT_EXTRACTION_FIELD_PROPAGATION_STANDARD.md](DOCUMENT_EXTRACTION_FIELD_PROPAGATION_STANDARD.md)

---

## DEC-068 — Dedicated AdminLogWriter for Admin Observability (Step 2)

- **Date:** 2026-03-25
- **Status:** Accepted
- **Context:** There was a need for a queryable admin log store to surface OCR and integration failures in the Administrator Workspace. A generic `ILoggerProvider` writing all framework logs to the database was considered and rejected.
- **Decision:** Implement a dedicated `AdminLogWriter` service. Services explicitly call `WriteAsync(...)` to persist targeted `AdminLogEntry` records. The writer resolves its own `DbContext` scope, making it fail-safe (persistence errors are swallowed and redirected to `ILogger` only — they never propagate to the main request flow).
- **Alternatives considered:** `DbLoggerProvider` as an `ILoggerProvider` — rejected because it would persist all framework/runtime logs indiscriminately, creating noise, performance risk, and database growth that is hard to control.
- **Consequences:** Only explicitly instrumented events appear in **Logs do Sistema**, giving administrators a clean, actionable log feed. Adding new events requires intentional `WriteAsync` calls, keeping the admin log focused and scoped.
- **Related:** `docs/ARCHITECTURE.md` — Admin Observability Layer section.

---

## DEC-083 — Side-Panel Workspace Pattern for Approvals

- **Date:** 2026-04-01
- **Status:** Accepted
- **Context:** The previous stacked master-detail layout in the Approval Center felt disjointed and lacked visual focus. Users lost queue context when reviewing large requests, and the vertical stacking created a "long page" feel that hindered productivity.
- **Decision:** Implement a side-panel (drawer) workspace pattern.
    1. **Queue Visibility**: The main queue sections remain visible on the left, providing constant context.
    2. **Drawer Rendering**: Details load into a `640px` right-side drawer using `DropdownPortal` to ensure it renders above all other UI layers.
    3. **Strong Selection State**: Active rows in the queue use a `12px` accent border and unique background (`#eff6ff`) to clearly link the queue item to the open panel.
    4. **Auto-Selection**: After a successful approval action, the system automatically refreshes and opens the next pending item in the same queue to maximize throughput.
- **Alternatives considered:** Full-page navigation (rejected: loses queue context) or keeping the stacked layout (rejected: poor focus).
- **Consequences:** Significantly improves the "triage" experience for approvers. Reduces context switching and clicks. Requires careful management of panel state (`isPanelOpen`) and selection synchronization.

---

## DEC-084 — Role-Aware Intelligence Pattern for Approval Center

- **Date:** 2026-04-01
- **Status:** Accepted
- **Context:** The `DecisionInsightsPanel` rendered identical intelligence for both Area Approvers and Final Approvers. The decision context is fundamentally different: Area Approvers focus on legitimacy, necessity, and organizational fit, while Final Approvers focus on financial rationality and comparative history. A naive solution would fork the component or create two separate screens.
- **Decision:** Implement role-aware conditional rendering within a single shared `DecisionInsightsPanel` component.
    1. **Shared Foundation**: All three core blocks (Alerts, Department KPIs, Item Analysis) remain available to both roles.
    2. **Context Banner**: A subtle top-of-panel indicator reinforces the decision lens without dominating the UI.
    3. **Role-Specific Emphasis Blocks**: Each role receives a dedicated emphasis block — Area gets a "Checklist de Legitimidade" (informational, non-blocking), Final gets a "Visão Financeira Comparativa" (year totals, consolidated variation).
    4. **Section Reordering**: Shared blocks render in different priority order per role to surface the most relevant information first.
    5. **Lightweight Context Prop**: A `requestData` object passes minimal request context to the panel, avoiding coupling to the full `RequestDetailsDto`.
- **Alternatives considered:** Forking into two separate panel components (rejected: duplication, maintenance burden). Single panel with no differentiation (rejected: suboptimal decision support for each role).
- **Consequences:** Maintains a single component with shared visual language while providing contextually relevant decision support. Future role-specific enhancements can be added within the existing conditional structure without architectural changes.

---

## DEC-085 — Anti-Accumulative Copy Request Flow

- **Date:** 2026-04-02
- **Status:** Accepted
- **Context:** The previous "Copy" feature was broken, simply redirecting to a blank "New Request" screen. Furthermore, copying needs to be careful not to generate abandoned draft records or leak sensitive operational data (prices, items, status) from the source request.
- **Decision:** Implement a template-driven, frontend-first copy flow.
    1. **Frontend-Owned Mapping**: `RequestCreate.tsx` becomes the owner of the copy flow via `/requests/new?copyFrom={id}`. It fetches a "Template" from the backend and maps it into ephemeral form state.
    2. **Strategic Field Exclusion**: Only header-level structure is copied. Line items, currency, need-by date, and requester are explicitly excluded to ensure the resulting request is a fresh business need.
    3. **No Automatic Persistence**: Unlike a typical "New" flow that might create a draft ID immediately, the copy flow remains purely in the browser's memory until the user clicks "Submeter". This prevents database pollution with abandoned copies.
    4. **UX Safeguards**: Replaces standard "Cancel" with "Descartar Cópia" to clarify the ephemeral nature of the unsaved copy. Adds a mandatory warning banner for the copied description.
- **Alternatives considered:** Copying everything including items and creating a persisted Draft immediately (rejected: high risk of data leakage and DB bloat).
- **Consequences:** Ensures a clean, reliable duplication process. Reduces backend load by avoiding redundant draft creation. Requires users to re-enter items, which serves as a necessary validation of the new need.

---

## Template for New Decisions

## DEC-[NNN] — [Short title]

- **Date:** [YYYY-MM-DD]
- **Status:** Proposed / Accepted / Rejected / Superseded
- **Context:** Why this decision is needed
- **Decision:** What was chosen
- **Alternatives considered:** [Option A], [Option B]
- **Consequences:** Tradeoffs, risks, follow-up actions
- **Supersedes / Superseded by:** [DEC-XXX] (if applicable)

---


- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** The portal requires mandatory documentation for specific workflow stages (e.g., Proforma for submission, PO for registration). Generic attachments were insufficient for enforcement.
- **Decision:**
    1. **Typed Attachments**: Every attachment must have an `AttachmentTypeCode` (PROFORMA, PO, PAYMENT_SCHEDULE, PAYMENT_PROOF).
    2. **Multi-file Validation**: A stage is valid if at least one active, non-deleted file of the required type exists.
    3. **History Integration**: Uploads are logged in the existing request history using the same UI layout as status changes (`DOCUMENTO ADICIONADO`), preserving the current status for audit consistency.
    4. **Structural Deletion Lock**: Deletion is only permitted in editable stages. Once a document is "locked" in a confirmed workflow step, it cannot be removed.
- **Consequences:** Ensures compliance with fiscal and procurement rules. Maintains a clean, unified audit trail.

---

## DEC-045 — Consolidated Redirection and Mandatory Items for All

- **Date:** 2026-03-02
- **Status:** Accepted
- **Context:** To simplify the "Start of Process" and ensure data quality, we decided to unify the creation UX and submission requirements regardless of the request type.
- **Decision:**
    1. **Redirection**: All new requests (Quotation or Payment) now stay on the "Edit" screen after creation to allow immediate item/attachment entry.
    2. **Item Requirement**: At least one line item is now strictly required for BOTH "QUOTATION" and "PAYMENT" types upon initial submission.
- **Supersedes:** DEC-008, DEC-017, DEC-029
- **Consequences:** Provides a more consistent and predictable entry point for all users. Ensures all submitted requests have a baseline financial structure.

---

## DEC-046 — Locked Header during Quotation Gathering

- **Date:** 2026-03-02
- **Status:** Accepted
- **Context:** Deciding the degree of editability for Buyers during the "WAITING_QUOTATION" stage to prevent unintentional changes to the original requester's intent.
- **Decision:** In the "WAITING_QUOTATION" stage, the **Request Header is locked** (read-only). Buyers can only add/edit/delete **Line Items** and manage **Attachments** (including mandatory Proforma).
- **Consequences:** Protects the integrity of the original request's metadata (Need Level, Date, Participants) while allowing the Buyer to perform all necessary quotation tasks. Requires differentiated "canEdit" logic in the frontend.

---

## DEC-020 — Operational Action Gating by Type and Status

- **Date:** 2026-03-03
- **Status:** Superseded by DEC-051
- **Context:** Deciding how to manage the operational workflow transitions for different request types (Payment vs. Quotation) after final approval.
- **Decision (Superseded):** The original decision to allow `QUOTATION` requests to bypass scheduling/payment steps was **overturned in Stage 5**.
- **Current Rule (DEC-051)**: Both `QUOTATION` and `PAYMENT` requests follow the same unified financial sequence after `PO_ISSUED`.
- **Consequences:** Eliminates user-facing "BadRequest" errors during the operational phase and ensures backend data integrity for unified lifecycle transitions.

---

## DEC-021 — Multi-Factor Attachment Gating

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** Ensuring documents are uploaded only at the correct business stage to prevent workflow failures and user confusion.
- **Decision:** Implement a validation matrix (TypeCode × StatusCode) for both frontend visibility and backend processing.
  - **Frontend**: Sections for Payment Proofs are now visible for both `QUOTATION` and `PAYMENT` requests once a P.O is issued (PO_ISSUED status and beyond).
  - **Backend**: Update `Upload` endpoint to reject invalid stage combinations with `BadRequest`.
- **Consequences:** Provides a cleaner and guided UX, ensures audit trail integrity by strictly controlling when documents enter the system across the unified financial flow.

---

## DEC-022 — Status-Aware Request Edit Guidance

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** The previous UX showed a generic "Pedido Bloqueado" alert as soon as a request left the `DRAFT` status. However, in the `WAITING_QUOTATION` stage, certain fields (Supplier, Items) remain editable by the Buyer, leading to a contradictory and confusing experience.
- **Decision:** Implement a persistent, status-aware guidance banner in `RequestEdit.tsx` that explicitly distinguishes between full editability, partial editability (Quotation), and read-only modes.
  - **Implementation**: Replaced the generic alert with a context-aware header banner driven by precision booleans (`isDraftEditable`, `isQuotationPartiallyEditable`, `isFullyReadOnly`).
- **Consequences:** Resolves the misleading "non-editable" communication during the quotation phase, properly guides the buyer on what can still be changed, and improves the overall professional feel of the workflow transitions.

---

### DEC-023: Strict Contract Alignment & Nullability

Status: Accepted

We standardized on the `number | null` pattern for numeric IDs in the frontend to match backend `int?` types, replacing inconsistent usage of optional props and empty strings. We also promoted backend business codes (e.g., `'HIGH' | 'MEDIUM' | 'LOW'` for `itemPriority`) as the single source of truth for the frontend types, ensuring type safety and reducing mapping complexity.

---

## DEC-051 — Unified Post-PO Operational Workflow

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** Originally, Quotation (COM) requests were designed to move directly from `PO_ISSUED` to `WAITING_RECEIPT`. However, the business process requires that both Quotation and Payment requests go through a financial flow (Scheduling -> Completion) once a PO exists.
- **Decision:** Unify the operational lifecycle after the `PO_ISSUED` status for both `QUOTATION` and `PAYMENT` types. Both must now undergo payment scheduling and proof of payment upload before moving to the receipt phase.## DEC-052 — Temporary Permission Gating for Gestão de Cotações (`X-User-Mode`)

- **Context:** The new `/buyer/items` "Gestão de Cotações" (formerly "Gestão de Itens") screen introduces inline editing of line items. Different fields require different role permissions (e.g., Cost Center can only be edited by an Area Approver, while Supplier is chosen by the Buyer). However, the enterprise SSO (JWT Roles) integration is not yet complete.
- **Decision:** Implement a temporary UI toggle that injects an `X-User-Mode` HTTP header into requests targeting `/api/v1/line-items`. The backend `LineItemsController` inspects this header to simulate role-based access control, throwing a `403 Forbidden` if a Buyer attempts to edit the `CostCenterId`.
- **Consequences:**

---

## DEC-054 — Modular Navigation Architecture

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** As the platform grows, a flat list of navigation entries becomes difficult to manage and visually overwhelming. There is a need for a scalable, modular structure that groups features by business area.
- **Decision:** Implement a grouped navigation structure in the `Sidebar.tsx`.
  - **Configuration-Driven**: Use a `MENU_ITEMS` array supporting `link`, `group`, and `action` types.
  - **Expandable Groups**: Modules like "Compras" are non-navigable containers that expand to show children.
  - **Auto-Expansion**: Groups automatically expand if one of their children's routes is active, ensuring the user always has context.
  - **Two-Tier Highlighting**: Parent groups use a subtle highlight when a child is active, while the active child receives the primary "strong" highlight.
- **Consequences:** Provides a clean visual hierarchy and makes the sidebar ready for future modules (RH, Financeiro, etc.) without further architectural changes. Improves accessibility with `aria-expanded` and consistent keyboard navigation.

---

## DEC-055 — Sidebar Layout: Independent Scrolling and Two-Zone Structure

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** The previous sidebar layout was dependent on the main page content scroll and became difficult to use when many navigation items were present. System actions like "Sair" were pushed off-screen.
- **Decision:** Adopt a full-height, two-zone sidebar layout.
  - **Independent Scroll**: The sidebar navigation zone is isolated with its own `overflow-y: auto`, decoupling it from the main workspace scroll.
  - **Fixed Bottom Actions**: System-level actions (Configurações, Sair) are pinned to the bottom of the sidebar container, ensuring they remain always visible.
  - **AppShell Integration**: Reconfigured the layout grid in `AppShell.tsx` to support a full-height sidebar track.
- **Consequences:** Dramatically improves ergonomics for power users. Ensures critical system controls are always accessible. Resolves the "scroll-overlap" issue between the menu and main content.

---

## DEC-053 — Post-Payment Delivery Follow-up and Mandatory Observations

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** Once a request reaches `PAYMENT_COMPLETED`, it enters an operational follow-up phase where the buyer manages the delivery of items with the supplier. This phase requires fine-grained tracking of item statuses and mandatory commentary for auditability during transitions to `ORDERED` or `RECEIVED` states.
- **Decision:**
    1. Introduce `IN_FOLLOWUP` as a new request-level status for the delivery cycle.
    2. Introduce `WAITING_ORDER` as the initial item status post-payment.
    3. Enforce mandatory observations for line item status changes to `ORDERED`, `PARTIALLY_RECEIVED`, or `RECEIVED` using the standard `ApprovalModal`.
    4. Record these item-level mutations in `RequestStatusHistory` as distinct audit events with human-readable descriptions.
- **Consequences:** Provides full delivery lifecycle traceability within the portal. Reuses existing modal patterns for consistent UX. Ensures that deliveries are documented with buyer comments, improving accountability and troubleshooting.

---

## DEC-056 — Dashboard de Compras Aggregation Logic

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** The new Dashboard de Compras requires aggregating multiple status codes into human-readable operational cards. Specifically, the "Aguardando Aprovação Final" and "Em Atenção" cards needed a precise business definition.
- **Decision:**
    1. **Aprovação Final Aggregation**: We decided to include `WAITING_COST_CENTER` (Inserir C.C) within the "Aguardando Aprovação Final" count. Although technically a separate status, it conceptually belongs to the final validation stage before a P.O is issued, aligning with user expectations for "Final Approval" oversight.
    2. **Attention Logic Alignment**: The "Em Atenção" card uses the same 4-day window (Today + 3) and terminal state exclusion as the primary `RequestsList` sorting logic. This ensures that the dashboard reflects the same operational priorities used in the daily workspace.
    3. **Educational vs Operational**: The interactive workflow guide is strictly educational. We decoupled it from real request data to prevent users from mistaking it for a control panel, using a "Guide/Manual" design language.
- **Consequences:** Ensures consistency between the dashboard overview and the daily operational lists. Provides clear guidance for new users without risking accidental data mutation.

---

## DEC-057 — Dashboard-to-List Filter Translation Pattern

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** Deciding how to implement navigation from dashboard summary cards to specific filtered views in the Requests List without hardcoding numeric database IDs in the Dashboard component.
- **Decision:** Use semantic status codes (e.g., `WAITING_AREA_APPROVAL`) in the URL query string (`?statusCodes=...`).
  - **Frontend Translation**: The `RequestsList` component is responsible for translating these codes into physical database IDs by cross-referencing with the loaded status lookup data.
  - **Fallback**: If codes are provided but lookup data isn't loaded yet, the component waits for the lookup fetch to complete before parsing and applying the filters.
- **Consequences:** Decouples the Dashboard from specific DB IDs, making it more portable and less prone to breaking if IDs change between environments. Centralizes filter logic in the target list components. Improves URL readability for end users.

---

## DEC-058 — Informational Request Header for Requesters in Buyer Stages

- **Date:** 2026-03-19
- **Status:** Accepted
- **Context:** With the introduction of the dedicated Buyer Workspace, requesters should no longer see or interact with operational actions related to the quotation process. However, they still need clear visibility into the request's status and the buyer's responsibility.
- **Decision:** When a request is in a buyer-handled stage (like `WAITING_QUOTATION`), replace the actionable header area with a read-only informational panel.
  - **Content**: Display "Responsável atual: Comprador" and "Próxima ação: Pedido em tratamento do comprador".
  - **Labels**: Use informational section titles like "Andamento do Pedido" or "Status do Fluxo".
  - **Actions**: Hide "Concluir Cotação" and all operational CTAs for the requester in these stages.
  - **Editability**: Ensure the "Fornecedor" field (and items) are read-only for the requester during these stages to reflect buyer ownership.
- **Consequences:** Provides a cleaner, safer UX for the requester. Prevents confusion about unauthorized actions or inconsistent field editability while maintaining workflow transparency. Ensures the requester knows the request is being handled by the procurement team.

---

## DEC-059 — 2-Section Quotation Area in Buyer Workspace

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** The previous Buyer Workspace mixed manual item entry, document uploads, and operational actions in a single area, leading to confusion as requests often involve multiple quotations.
- **Decision:** Restructure the quotation management area into two distinct visual sections:
  - **Section A (Existing)**: Displays registered quotations and uploaded documents, providing clear visibility of what has already been processed.
  - **Section B (Add New)**: A dedicated entry zone with explicit **Mode Switching** (Upload vs. Manual).
- **Consequences:** Improves operational focus by separating "work done" from "work to be done". Provides a cleaner path for adding multiple quotations per request. Standardizes the entry flow with a "Back/Cancel" mechanism to prevent accidental state locks.

---

## DEC-060 — Reserved Review Area for Future OCR Integration

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** Preparing the system for a future OCR-based quotation extraction flow without performing real processing yet.
- **Decision:** Transition the "Reserved Review Area" from a placeholder to a real integration point. Connect the UI to the local OCR service through the Portal backend. Render actual suggestions (Supplier, Total, Items) using a structured field-and-table layout.

---

## DEC-061 — Local State for OCR Drafts (Step 4)

---

## DEC-068 — Unified Operational Flow and FINANCE Role (Stage 9.1)

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** A workflow inconsistency was identified where `QUOTATION` requests were bypassing the payment phase, while the backend correctly required `PAYMENT_COMPLETED` for receipt. Additionally, role responsibility for post-P.O. actions needed formalization.
- **Decision:**
    1. **Workflow Unification**: Both `QUOTATION` and `PAYMENT` requests now follow the same unified operational lifecycle: `PO_ISSUED` -> `PAYMENT_SCHEDULED` -> `PAYMENT_COMPLETED` -> `WAITING_RECEIPT`.
    2. **FINANCE Role Introduction**: Introduced a simulated `FINANCE` user mode to manage payment-related actions (`SCHEDULE_PAYMENT`, `COMPLETE_PAYMENT`).
    3. **Role Handover**: Finance is responsible for the payment phase, while the Buyer remains responsible for `APPROVED` (P.O. Registration) and `PAYMENT_COMPLETED` onwards (Receipt and Finalization).
- **Consequences:** Ensures full financial traceability for all request types. Clarifies actor responsibilities in the post-P.O. phase. Resolves the "Allowed statuses: PAYMENT_COMPLETED" conflict by aligning the UI guidance with backend validation.

---

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** Deciding how to manage edits to OCR extraction suggestions before they are persisted as official Quotation records.
- **Decision:** Use a dedicated local React state (`ocrDrafts`) to store the editable version of OCR results. Changes to header fields or line items are managed entirely on the frontend until the user explicitly triggers an "Apply" or "Conclude" action.
- **Consequences:** Provides a safe, isolated "sandbox" for buyers to review and correct extraction data without polluting the database with unverified or partial data. Decouples the OCR service logic from the main Quotation entity persistence, allowing for explicit "Restore to OCR Suggestions" functionality.

---

## DEC-062 — Improved Quotation Visualization & Comparison (Step 7)

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** Buyers need to quickly compare multiple quotations for the same request. A flat list of header totals was insufficient for detailed analysis.
- **Decision:** Implement a structured list in Section A of the Buyer Workspace with expand/collapse capabilities.
  - **Quotation Summary**: Shows Supplier, Doc Number, Date, and Grand Total.
  - **Item Details**: Expands to show a table of all quotation items (Description, Qty, Unit Price, Line Total).
  - **Visual Highlighting**: Automatically highlight the lowest total amount (MENOR VALOR) as a primary visual cue when multiple quotations share the same currency.
- **Consequences:** Significantly improves the comparison experience. Provides transparency into the breakdown of each quotation without cluttering the main view.

---

## DEC-063 — Unified Quotation Draft and Persistence Fixes (Step 8)

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** The manual quotation mode had a UX gap where the form wouldn't open, and a technical bug caused item totals to display as '0,00' in the saved view.
- **Decision:**
  - **Unified Draft Logic**: Consolidate the "Editable Draft" state (`quotationDrafts`) for both OCR and Manual modes. "Inserir manualmente" now simply initializes an empty draft and triggers the same review/edit UI used by OCR.
  - **Explicit Persistence**: Ensure `handleSaveQuotation` explicitly maps and sends `lineTotal` for each item to the backend, preventing reliance on backend defaults or recalculations during the critical save path.
  - **API Response Enrichment**: Update the `SaveQuotation` endpoint to return the full itemized record, ensuring the frontend has immediate access to the persisted state.
- **Consequences:** Resolves the manual mode UX failure. Guarantees data integrity for saved quotations. Simplifies frontend logic by using a single path for all quotation entries.

---

## DEC-064 — Backend Schema Stabilization for Quotations

- **Date:** 2026-03-21
- **Status:** Accepted
- **Context:** Following the extension of the `Quotation` entity with `ProformaAttachmentId` and `DiscountAmount` for improved document tracking and financial management, a SQL Server mismatch occurred (`Invalid column name 'ProformaAttachmentId'`). This was due to the entity model advancing beyond the applied database migrations.
- **Decision:** Generate and apply a cumulative stabilization migration (`FixQuotationSchema`).
- **Consequences:** Resolves runtime SQL errors in `LineItemsController` and `RequestsController`. Ensures that all future quotation-related data (proforma links and header-level discounts) are correctly persisted and queryable. Reinforces the requirement for manual schema synchronization when modifying core transactional entities.

---

## DEC-065 — Transitional Authorization Model Enforcement

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** During Stage 9, a blocking error ("No authentication handlers are registered") emerged when the Final Approver attempted to select a winning quotation. This was caused by the use of ASP.NET Core's `Forbid()` result, which implicitly demands active authentication middleware (e.g., `AddAuthentication()`) to format the response. Because the project operates on a transitional dev simulation without real RBAC/auth infrastructure, the middleware crashed.
- **Decision:**
  - Strictly prohibit the use of `[Authorize]`, `Forbid()`, and `Unauthorized()` in the current project phase.
  - Enforce role-based access control by returning explicit `StatusCode(403, "Message")` when `GetUserMode()` or status rules fail.
  - Ensure this pattern is applied uniformly across all controllers (`RequestsController`, `LineItemsController`, etc.).
- **Consequences:** Restores full functionality to the quotation and item-editing workflows. Prevents developers from prematurely wiring empty auth middleware to silence errors. Keeps the codebase compatible with future real RBAC adoption (where these explicit 403s can simply be swapped for `[Authorize]` policies once the infrastructure exists).

---

## DEC-066 — Authoritative Quotation Selection (Stage 9)

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** Transitioning from a request-level attribute model to a quotation-first model where the winner defines the final commercial parameters.
- **Decision:**
    1. **Persistence**: The `SelectedQuotationId` is stored in the `Request` table.
    2. **Approval Blocking**: In `WAITING_FINAL_APPROVAL`, the final approver **must** select a winner before the "APROVAR" button becomes active. Backend validation enforces this with a 400 BadRequest.
    3. **Commercial Source**: Once selected (or approved), the items, total amount, and currency of the selected quotation become the authoritative source for the request.
    4. **Legacy Compatibility**: Request-level `SupplierId` and items remain for `PAYMENT` requests and as snapshots/history for `QUOTATION` requests, but they are visually superseded by the winner data in post-selection views.

---

## DEC-067 — Commercial Locking vs. Operational Continuation

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** Avoiding workflow paralysis after a commercial decision is finalized.
- **Decision:**
    1. **Commercial Locking**: After `APPROVED`, the authoritative quotation selection and the vendor choice are fully locked.
    2. **Operational Continuation**: The operational phase (P.O. issuance, payment, receipt) remains fully active.
    3. **UI Integration**: Action buttons for the operational flow (e.g., "REGISTRAR P.O") are integrated into the persistent **Status Guidance Panels** of `RequestEdit.tsx`, ensuring the Buyer can always see and execute the next step regardless of the commercial data locking state.
- **Consequences:** Ensures full financial control over the commitment (winner selection) while allowing the business process to move forward smoothly.

---

## DEC-066 — Introducing the Receiving Workspace and RECEIVING Role

- **Date:** 2026-03-24
- **Status:** Accepted
- **Context:** The post-payment phase (RECEIPT) requires a dedicated workspace and functional role to separate operational delivery tasks from procurement (Buyer) and financial (Finance) tasks.
- **Decision:**
    1. Introduce a new functional role: `RECEIVING` (Recebimento).
    2. Create a dedicated **Receiving Workspace** on the frontend, filtered for `PAYMENT_COMPLETED` and `WAITING_RECEIPT` statuses.
    3. Update `getRequestGuidance` and role-switching logic to assign post-payment responsibility exclusively to the `RECEIVING` role.
    4. Implement a dedicated route `/receiving/workspace` and include it in a new "Operacional" sidebar group.
- **Consequences:** Provides clear ownership for the final stage of the workflow. Prevents "workflow pollution" in the Buyer and Finance workspaces. Establishes a modular foundation for future logistics/warehouse roles.

---

## DEC-070 — Status Badge Contrast Standardization

- **Date:** 2026-03-23
- **Status:** Accepted
- **Context:** The previous "10% opacity" badge styling resulted in insufficient contrast for light status colors like Amber, Yellow, and Cyan, violating WCAG AA accessibility standards.
- **Decision:** Switch to solid background colors for all status badges, with dark text (`#111827`) enforced for light backgrounds (Yellow/Amber/Orange) to ensure a minimum 4.5:1 contrast ratio.
- **Consequences:** Significantly improves legibility for critical statuses like `PENDENTE`. Centralizes badge styling in `globals.css` via semantic classes, eliminating inconsistent inline-style implementations.

---

## DEC-073 — Quotation Workflow Locking and Mutability Boundary

- **Date:** 2026-03-29
- **Status:** Accepted
- **Context:** Quotation data integrity was at risk if buyers could modify, replace, or delete quotations after a request had been approved or advanced to final operational stages.
- **Decision:** Implement a strict 'Read-Only' boundary for quotations once a request advances beyond the quotation/adjustment phase.
    1. **Centralized Rule**: Create RequestWorkflowHelper.CanMutateQuotation(statusCode) as the single source of truth.
    2. **Backend Enforcement**: Apply the guard to all mutation endpoints (Save, Update, Delete, OCR, Proforma Management).
    3. **Frontend Hardening**: Hide mutation actions in the Buyer Workspace for post-quotation requests.
- **Consequences:** Guarantees commercial data consistency throughout the approval and operational lifecycle. Prevents accidental or unauthorized changes once stakeholders have begun the approval process. Improves auditability.

---

## DEC-074 — Contextual Attachment Placement in Request Creation

- **Date:** 2026-03-30
- **Status:** Accepted
- **Context:** The "Documentos de Apoio" section in the Request Draft Creation form was located at the very bottom, creating a fragmented UX where users had to scroll away from the justification field to attach supporting files.
- **Decision:** Move the attachment area directly below the "Descrição ou justificativa" field in the "Dados Gerais do Pedido" section.
    1. **Integrated UI**: Removed the separate heavy section block.
    2. **Inline Treatment**: Used a lighter inline/sub-label header ("Documentos de Apoio") instead of a full section header.
    3. **Compact Dropzone**: Reduced the padding and visual weight of the upload dropzone to align with the justification field.
- **Alternatives considered:** Keeping the separate section but moving it up. Rejected as it still felt like a disconnected block.
- **Consequences:** Improves the natural flow of request explanation, reducing the likelihood of users forgetting to attach mandatory or supporting files. Makes the form feel more cohesive and modern.

---
---

## DEC-074 — Restauração Condicional da Data de Necessidade

- **Date:** 2026-03-30
- **Status:** Accepted
- **Context:** O campo "Data de Necessidade" (`NeedByDateUtc`) foi removido anteriormente mas precisava ser restaurado para pedidos de Cotação, seguindo uma regra de visibilidade condicional.
- **Decision:** Restaurar o campo de forma integrada (reutilizando a estrutura de DTO/Entidade existente) com lógica condicional no frontend.
    1. **Visibilidade**: Visível apenas quando `RequestTypeId` (ou Code) é `QUOTATION`.
    2. **Obrigatoriedade**: Obrigatório no frontend e backend apenas para `QUOTATION`.
    3. **UX**: Utilização de `AnimatePresence` para transição suave e posicionamento dentro do grid de "Dados Gerais".
    4. **Payload**: Enviar `null` explicitamente quando o tipo não for `QUOTATION` ou o campo estiver oculto.
- **Alternatives considered:** Criar um novo campo separado ou manter persistência mesmo quando oculto. Rejeitado para evitar inconsistência de dados.
- **Consequences:** Garante que pedidos de Cotação tenham datas limite claras enquanto mantém o formulário simplificado para Pedidos de Pagamento.

---

## [2026-03-30] DEC-075: Request Form Layout Optimization

- **Context:** The "Request Draft Creation" and "Request Edit/Details" screens were using a rigid, legacy `maxWidth: 1000px` shell. On modern desktop displays, this created excessive empty margins and an unnecessarily cramped experience for complex line item tables and multi-column forms.
- **Decision:** Increased the effective width of the primary request form container to **1440px** and established a standard for responsive, wider form layouts in the portal.
- **Implementation Detail:**
    1.  Updated `RequestCreate.tsx` and `RequestEdit.tsx` to use `width: 100%, maxWidth: '1440px'`.
    2.  Added `minWidth: 0` to top-level page containers to ensure flex/grid child stability.
    3.  Maintained `margin: '0 auto'` for center-alignment on ultrawide displays.
- **Alternatives considered:** Making the form fully fluid (100% width). Rejected because single-column fields or long descriptions become difficult to read when stretched beyond 1500px.
- **Consequences:** More breathing room for the "Itens do Pedido" table, better utilization of the AppShell workspace, and improved visual consistency with the Requests List screen.

---

## [2026-03-30] DEC-076: Relaxing Validation for Payment Requests (PAG)

- **Context:** In the "Payment Request" workflow, the "Fornecedor" (Supplier) and "Planta de destino" (Item Destination Plant) fields were strictly enforced at the draft and submission stages. This created a rigid experience for users who only had partial information at the start of the extraction or manual entry process.
- **Decision:** Relaxed the validation for `SupplierId` (Request level) and `PlantId` (Line Item level) specifically for **Payment** request types.
- **Implementation Detail:**
    1.  Removed the `[Required]` attribute from `PlantId` in `CreateRequestLineItemDto` and `UpdateRequestLineItemDto`.
    2.  Implemented manual, conditional `PlantId` validation in `RequestsController.cs` for non-Payment types.
    3.  Removed hard-coded `SupplierId` requirement for Payment requests in `UpdateDraft` and `SubmitRequest` controller methods.
    4.  Updated frontend components (`RequestEdit.tsx` and `RequestLineItemForm.tsx`) to make the visual mandatory indicators (red asterisks) and input `required` attributes conditional on the request type code.
- **Alternatives considered:** Keeping the backend fields mandatory while only relaxing the frontend. Rejected because it would cause 400 Bad Request errors when saving valid partial drafts.
- **Consequences:** More flexible "Draft-to-Submission" workflow for Payments, allowing for incomplete supplier or plant data when capturing from documents (OCR).

---

## DEC-101 — Guided UX Attention Patterns

- **Date:** 2026-04-01
- **Status:** Accepted
- **Context:** Complex forms like `RequestEdit` have multiple collapsible sections. In specific workflow stages (e.g., Area Approval), users need to be quickly guided to newly available or critical information (like Saved Quotations) without manual exploration.
- **Decision:** Implement a standardized "Guided Attention" pattern for critical workflow transitions:
    1. **Auto-Expand**: Automatically expand relevant `CollapsibleSection` components.
    2. **Auto-Scroll**: Smoothly scroll the targeted section into the viewport with a fixed offset (e.g., `-220px`) to account for sticky headers and action bars.
    3. **Visual Highlight**: Apply a temporary "pulse" or high-contrast border/shadow effect (e.g., Red glow) for 3-5 seconds.
    4. **One-Time Execution**: Ensure the effect triggers only once per initial load of a specific record (using Ref-based flags) to avoid annoying re-triggers on local state updates.
- **Alternatives considered:** Permanent banners or blocking pop-ups. Rejected as too invasive and disruptive to the industrial UI feel.
- **Consequences:** Significantly improves task completion speed for approvers and buyers. Establishes a predictable pattern for guiding users through complex state-dependent forms.

---

## DEC-086 — Security Hardening Phase 1: Uploads & Login Protection

- **Date:** 2026-04-02
- **Status:** Accepted
- **Context:** Following a security audit, two critical gaps were identified: the lack of file upload restrictions and the absence of brute-force protection during authentication.
- **Decision:** Implement a "Minimum Safe" baseline for Phase 1:
    1.  **Attachment Uploads**:
        *   **Whitelist-only**: Only `.pdf`, `.jpg`, `.jpeg`, `.png`, `.doc`, `.docx`, `.xls`, `.xlsx` are allowed.
        *   **Size Limit**: Enforced at **15MB** per file.
        *   **Filename Sanitization**: Original filenames are sanitized (removing non-alphanumeric/hyphen/underscore) before storage in the DB to prevent UI injection and path traversal risks.
        *   **Physical Storage**: Files are stored using GUIDs, completely decoupling the physical filename from user input.
        *   **MIME Signal**: Basic `ContentType` consistency check is implemented as a secondary signal, not a hard blocking rule.
    2.  **Login Protection**:
        *   **Lockout Policy**: Accounts are temporarily locked for **15 minutes** after **5 failed attempts**.
        *   **Generic Error Messages**: Failed logins return a generic unauthorized message to prevent user enumeration.
        *   **Audit Logging**: Lockout events and blocked attempts are logged to the `AdminLogEntries` for SOC review.
- **Consequences:** Legacy files remains accessible. User experience is slightly impacted by lockout, but generic messaging maintains high privacy. Future hardening (IP-based throttling, deep MIME inspection) is planned for Phase 2.

---

## DEC-087 — Security Hardening Phase 2: IP-Based Rate Limiting

- **Date:** 2026-04-02
- **Status:** Accepted
- **Context:** While Phase 1 addressed user-specific lockouts, it left the system vulnerable to distributed brute-force attacks against non-existent users and resource exhaustion at the API edge.
- **Decision:** Implement IP-based rate limiting using ASP.NET Core built-in middleware.
    1.  **Strict Policy (LoginPolicy)**: Applied exclusively to `AuthController.Login`.
    2.  **Thresholds**: 10 permits per 1-minute fixed window per Remote IP.
    3.  **Localhost Configuration**: Rate limiting is configurable for localhost (disabled by default in Dev, but enabled via `Security:RateLimiting:EnableForLocalhost`) to allow local validation.
    4.  **Generic Rejection**: Returns `429 Too Many Requests` with a generic message: "Muitas tentativas. Tente novamente em breve."
    5.  **Throttled Audit Logging**: Logs `IP_RATE_LIMITED` to `AdminLogEntries` once per minute per IP to prevent audit log flooding.
    6.  **Safe IP Resolution**: Relies on `RemoteIpAddress`. Forwarded headers are only trusted if `ForwardedHeadersOptions` are explicitly configured for a trusted proxy.
- **Consequences:** Provides a high-performance first line of defense. Reduces database and CPU load during brute-force campaigns. Minimal impact on legitimate users behind shared NATs due to the generous 10/min threshold.

---

## DEC-090 — Cost Center Refinement: PAYMENT vs QUOTATION Behavior

- **Date:** 2026-04-02
- **Status:** Accepted
- **Context:** DEC-085 introduced mandatory Cost Center selection for Area Approvers. However, for **PAYMENT** requests, Cost Centers are often pre-defined during the requisition/item phase. Forcing a re-selection for already valid data is redundant. Conversely, **QUOTATION** requests often lack a definitive CC until the approval stage.
- **Decision:** Implement a conditional validation and display logic in the Approval Center:
    1. **Unified PAYMENT**: If all line items in a PAYMENT request share the same non-null Cost Center, the field is rendered as **Read-Only** with a green "Validado" badge. Approval is not blocked.
    2. **Inconsistent PAYMENT**: If a PAYMENT request has items with different Cost Centers, it is treated as a data conflict. The UI shows a red **"Conflito: Unificação Obrigatória"** warning and forces the approver to select one, which then propagates to all items (consistent with DEC-085's unification goal).
    3. **Missing/QUOTATION**: If data is missing or it is a QUOTATION request, the standard mandatory dropdown behavior remains.
- **Consequences:** Reduces friction for the most common PAYMENT scenarios while maintaining strict financial integrity for edge cases and quotations. Ensures all approved requests leave the Area stage with a unified, valid Cost Center.

---

## DEC-096 — Payment OCR Intake & Shared Hook

- **Date:** 2026-04-05
- **Status:** Accepted
- **Context:** Automated document extraction was needed for the "Payment" request type before the request exists in the database. Existing OCR logic was coupled to the Quotation workspace and required a RequestId.
- **Decision:**
    1. **Direct OCR Endpoint**: Created `POST /api/requests/direct-ocr` for document extraction without a RequestId.
    2. **Shared Hook (`useOcrProcessor`)**: Refactored extraction logic into a reusable hook for both Quotation and Payment flows.
    3. **Interactive Review**: Implemented a "Payment Draft" review area in `RequestCreate.tsx` to allow users to verify extracted data before persistence.
- **Consequences:** Provides a high-efficiency entry point for invoice-based payments while reusing stable extraction logic.

---

## DEC-097 — Relaxed Persistence for Payment OCR Drafts

- **Date:** 2026-04-05
- **Status:** Accepted
- **Context:** Payment OCR often extracts items without all business-mandatory data (Cost Center, IVA Rate). Forcing these fields at the initial draft creation step caused 500 errors and prevented saving progress.
- **Decision:**
    1. **Relaxed Entity Constraints**: Removed `[Required]` from `CostCenterId` and `IvaRateId` in `RequestLineItem`.
    2. **Nullable Database Columns**: Implemented migration `RelaxLineItemOptionalFieldsForDrafts` to make these columns nullable.
    3. **Deterministic Sequencing**: Enforced `LineNumber` assignment (incremental) during creation to ensure structural integrity.
    4. **Submission Gating**: Added strict server-side validation in `SubmitRequest` to ensure all line items have mandatory business fields before the request enters the workflow.
- **Consequences:** Enables a "Save Now, Complete Later" UX for complex OCR extractions. Prevents persistence crashes while maintaining strict financial governance at the submission boundary.

---

## DEC-098 — Adaptive OCR Routing & Token Optimization (Phase 2)

- **Date:** 2026-04-08
- **Status:** Accepted
- **Context:** The previous naive Vision-based OpenAI OCR flow was processing clean, digital PDFs as rasterized images, leading to excessive token consumption (e.g., >37k tokens per invoice, ~111k for larger files) and high associated costs, due to OpenAI's tokenization logic for `high` detail images.
- **Decision:** Implement a triage-first Adaptive OCR strategy.
    1. **Native Text Detection**: The system uses `PdfiumViewer` to extract text from the first up to 5 pages of a document to determine if it's a native digital PDF.
    2. **Text-First Routing**: If text is viable (>100 characters and >2 lines), the flow routes to a text-only OpenAI payload avoiding Vision API completely. This applies to standard invoices.
    3. **Vision Fallback**: If extraction via text fails quality checks, or if it's a scanned document/contract, it seamlessly falls back to the original Rasterize-to-Vision flow.
    4. **Telemetry**: Introduced explicit telemetry fields (`RoutingStrategy`, `DetailMode`, `NativeTextDetected`) in `ExtractionMetadataDto` to measure the cost-effectiveness in production admin logs without impacting legacy API consumers.
- **Consequences:** Dramatically reduces token usage by approximately 98% for native PDFs (e.g., 618 tokens instead of >37,000 for standard invoices). Preserves fallback capability for scanned documents, preventing brittle failures. Sets the foundation for adaptive token optimization rules.

---

## DEC-099 — Context-Aware Document Triage & Multi-Strategy Matching

- **Date:** 2026-04-09
- **Status:** Accepted
- **Context:** OCR extraction suffered from two critical failures: 1) Invoices misclassified as Contracts due to overlapping keywords in footers (referencing payment terms), and 2) Duplicate suppliers created due to minor naming variations (e.g., "ITA, SA." vs "ITA SA").
- **Decision:** 
    1. **Source-Context Hint**: The extraction pipeline now accepts a `sourceContext` param (e.g. `quotation`). If present, the triage engine automatically favors the `Invoice` strategy unless the document is definitively a multi-page legal contract (dominant scoring).
    2. **Aggressive Keyword Weighting**: Strong invoice keywords (e.g. "Factura", "Proforma") now carry 3x weight and act as "vetos" against contract classification.
    3. **Multi-Step Matching**: Frontend supplier matching now follows a three-step sequence: Normalized Name Match -> NIF/TaxId API Search -> Fuzzy Name Match. 
    4. **Normalization Standards**: Standardized stripping of common punctuation and corporate suffixes during the search phase to bridge the gap between extraction strings and Master Data records.
- **Consequences:** Dramatically increases extraction reliability for first-time uploads. Eliminates the most common cause of "Partial" extraction states for invoices. Provides a significantly cleaner Supplier master dataset by preventing "punctuation-based" duplicates.

---

## DEC-110 — Financial Snapshot & Payment Divergence Detection (Phase 1)

- **Date:** 2026-04-17
- **Status:** Accepted
- **Context:** The system had no mechanism to preserve approved financial values or compare them against actual payments. Once approved, financial values were frozen by convention but not by enforcement. Commercial conditions (total amount, VAT, currency impact, supplier terms) could change between approval and payment with no structured detection or audit trail.
- **Decision:** Implement a phased delivery for post-approval commercial change handling.
    1. **Phase 1 (this implementation):**
        - Add `ApprovedTotalAmount`, `ApprovedCurrencyCode`, `ApprovedAtUtc` to `Request` entity — immutable snapshot captured at final approval.
        - Add `ActualPaidAmount`, `ActualPaidAtUtc` to `Request` entity — mandatory input when confirming payment via `MarkAsPaid`.
        - Add status guards to `SchedulePayment` and `MarkAsPaid` in `FinanceController` — only allowed from valid source statuses.
        - Implement divergence detection: if `Math.Round(ActualPaidAmount, 2) ≠ Math.Round(ApprovedTotalAmount, 2)`, create a `PAYMENT_DIVERGENCE_DETECTED` audit entry. No tolerance threshold — any difference is reported. *(Updated 2026-04-26: removed 1% tolerance gate per business decision.)*
        - Phase 1 divergence is **informational** — payment proceeds, divergence is logged and visible. Message indicates direction (abaixo/acima do valor aprovado).
        - All new fields are nullable for backward compatibility with legacy requests.
    2. **Phase 2 (documented, not implemented):**
        - New exception workflow statuses: `COMMERCIAL_CHANGE_REVIEW`, `REAPPROVAL_REQUIRED`, `POST_PAYMENT_REGULARIZATION`.
        - Complementary payment mechanism with cumulative tracking.
        - Pre-payment commercial revalidation endpoint.
- **Alternatives considered:** (1) Making divergence blocking in Phase 1 (rejected: requires new statuses, approval center changes, and sidebar updates — too much scope). (2) Making `ActualPaidAmount` optional (rejected: defeats the purpose of divergence detection).
- **Consequences:** Establishes the data foundation for full commercial change handling. `ActualPaidAmount` is mandatory when paying. Legacy requests with null snapshot fields skip divergence detection gracefully. Finance payments list shows divergence badge for affected requests. Full documentation in `WORKFLOW_ARCHITECTURE.md §6`.

---

## DEC-119 — Centralized Supplier Approval (Drawer-Only Model)

- **Date:** 2026-04-25
- **Status:** Accepted
- **Context:** The Supplier Ficha (Phase 2A) initially included inline approve/return buttons directly on the `SupplierFichaDetail` page. This created two parallel paths for the same approval action — one on the detail page and one (planned) in the Approval Center. The contract approval workflow had already been standardized on a drawer-only model in the Approval Center (DEC-083).
- **Decision:** Centralize all supplier ficha approval decisions exclusively in the Approval Center drawer.
    1. **Single Decision Point**: Approve and Return actions are only available via `SupplierApprovalPanel` rendered inside the Approval Center's quick-view drawer.
    2. **Detail Page Read-Only**: `SupplierFichaDetail` retains only the "Submeter para Aprovação" action (for the ficha creator) and a read-only tracker showing "Aguardando aprovação no Centro de Aprovações".
    3. **Visual Consistency**: The supplier drawer uses the same `AnimatePresence > DropdownPortal > overlay + sliding panel` pattern as the contract drawer, with an amber accent to differentiate it from the blue/purple contract stages.
    4. **Data Loading**: Supplier pending fichas are loaded in parallel with requests and contracts during `loadQueue()`.
- **Alternatives considered:** Keeping dual approval paths (detail page + Approval Center). Rejected: creates confusion, duplicates logic, and violates the single-responsibility principle for approval orchestration.
- **Consequences:** All three approval types (Requests, Contracts, Suppliers) now follow the identical drawer-based workflow in the Approval Center. Approvers have a single, consistent workspace for all pending decisions. The detail page is strictly for data entry and review.

---

## DEC-120 — Catalog Item Reconciliation Engine (Unified Cross-Flow)

- **Date:** 2026-04-27
- **Status:** Accepted
- **Context:** Items entered across multiple flows (Payment Request OCR, Payment Request manual, Quotation Management) may not link to the master catalog. The Payment Request flow was using a plain `<input>` for item descriptions instead of the `CatalogItemAutocomplete` component, preventing catalog matching. There was no unified mechanism to detect and resolve unmatched items before submission.
- **Decision:** Implement a shared Catalog Item Reconciliation Engine that works consistently across all item-entry flows.
    1. **Phase 1 — Autocomplete Bug Fix**: Replace plain `<input>` in `RequestCreate.tsx` with `CatalogItemAutocomplete` (same as `QuotationEntry.tsx`).
    2. **Shared Hook (`useCatalogItemReconciliation`)**: Classifies items as MATCHED, UNMATCHED, LOW_CONFIDENCE, CREATED_PENDING, LINKED_MANUALLY, or FREE_TEXT. Tracks resolutions in a `Map<number, ItemResolution>`.
    3. **Backend Endpoint (`POST /api/v1/catalog-items/reconciliation-create`)**: Creates catalog items with `Origin = CREATED_PENDING_VALIDATION` and `IsActive = true`. Performs duplicate detection on normalized descriptions. Returns existing item if a match is found.
    4. **Submission Guardrail (Warning-with-Override)**: When unresolved items exist, a `ReconciliationWarningDialog` intercepts submission. The user can: (a) open the batch reconciliation modal, (b) continue anyway (items recorded as free text), or (c) cancel.
    5. **Batch Reconciliation Modal**: Shows ALL unresolved items in a single table. Per-row actions: link to existing catalog item, create new item (CREATED_PENDING_VALIDATION), or keep as free text with optional justification.
    6. **Mandatory Coverage**: RequestCreate (Payment + Requester items) and QuotationEntry.
- **Alternatives considered:** Blocking submission entirely for unmatched items. Rejected: too rigid for the current operational context. Per-item inline-only resolution. Rejected: does not allow batch review.
- **Consequences:** All item-entry flows share the same reconciliation engine via `useCatalogItemReconciliation`. New catalog items created through this flow are flagged for admin validation. Free-text items are tracked with optional justification for audit purposes.

---

### DEC-121: Mandatory Receipt Upload for Finance
- **Date:** 2026-04-27
- **Context:** To ensure the portal aligns with the physical operational processes, a payment request cannot be fully considered 'Completed' until Finance has uploaded the official receipt document.
- **Decision:** Added a mandatory guardrail in the operational completion flow. The FinalizeRequest backend endpoint explicitly blocks requests in the WAITING_RECEIPT status from finalizing if a TYPE_RECEIPT ('Recibo') document is not attached. 
- **Implementation:** Added TYPE_RECEIPT attachment type. In the frontend, the UI action 'FINALIZAR PEDIDO' in WAITING_RECEIPT state is now exclusively visible to the Finance role. The receipt attachment component logic restricts this upload to Finance/System Admin users.
- **Extended by:** DEC-122

---

### DEC-122: Decoupling Physical Item Receiving from Supplier Financial Receipt
- **Date:** 2026-04-27
- **Status:** Accepted
- **Context:** A fundamental business process misunderstanding caused the Receiving workspace to act as both the physical goods receiving confirmation AND the financial receipt document closure. In reality, "Recebimento" (Receiving) is the physical/operational confirmation of goods/services arrival, while "Recibo" (Receipt) is the financial document issued by the supplier confirming they received payment. These are two distinct business concepts with different responsible roles.
- **Decision:** Strictly decouple the two workflows:
    1. **New Backend Endpoint**: `POST /api/v1/requests/{id}/operational/confirm-receiving` — exclusively for the Receiving role. Confirms physical item/service receipt. Transitions request to `WAITING_RECEIPT` (all received) or `IN_FOLLOWUP` (partial). Never transitions to `COMPLETED`.
    2. **Refactored Finalization**: `FinalizeRequest` is now strictly a Finance-only terminal action (`WAITING_RECEIPT` → `COMPLETED`). Requires `TYPE_RECEIPT` attachment. Receiving role is explicitly blocked from triggering finalization.
    3. **Receiving UI Update**: Renamed button from "FINALIZAR PEDIDO" to "CONFIRMAR RECEBIMENTO". Modal type changed from `FINALIZE` to `CONFIRM_RECEIVING`. API call changed from `finalize` to `confirmReceiving`.
    4. **Guidance Labels**: Split `getRequestGuidance` for `PAYMENT_COMPLETED` (Recebimento: "Mover para fase de recebimento") vs `WAITING_RECEIPT` (Financeiro: "Anexar recibo do fornecedor e finalizar").
    5. **Status Lifecycle Fix**: Removed `PAYMENT_COMPLETED` from `isFinalizedStatus` since it's an active operational status requiring Receiving action.
- **Alternatives considered:** Keeping a single "finalize" action with different behavior per role. Rejected: creates semantic confusion and violates Separation of Duties.
- **Consequences:** Clear role boundaries between Receiving (physical) and Finance (financial). Prevents premature request completion. Ensures every completed request has both physical receipt confirmation and supplier financial receipt document.

---

## DEC-133 — AOVIA1VMS011 Phase 3 Staging Access Recovery & same-origin API Routing

- **Date:** 2026-05-25
- **Status:** Accepted
- **Context:** Staging access was blocked due to forgotten passwords, requiring administrative recovery. Attempting to run a PowerShell recovery script loading modern backend BCrypt assemblies failed because the legacy PowerShell console runs on .NET Framework 4.8, which cannot load .NET 8 / .NET Core assemblies. Furthermore, the compiled frontend static bundle fell back to calling a direct Kestrel port `http://localhost:5000` because the Vite build-time API URL environment variable was omitted, causing CORS blocks in the browser.
- **Decision:** Address access blocks with a compiled console recovery tool and same-origin relative fallback paths:
    1. **StagingAccessRecovery Console Tool**: Created a native .NET 8 console application `StagingAccessRecovery.exe` using standard `BCrypt.Net-Next` and `Microsoft.Data.SqlClient` packages, and ran it locally on `AOVIA1VMS011` to securely create/update `Leonardo.Cintra@alpla.com` as a System Administrator in `[Portal-Gerencial-Test]`.
    2. **Same-Origin Relative Base Path**: Changed frontend default API client base path fallback in `api.ts` from `http://localhost:5000` to same-origin relative `/api`. Static assets compiled and deployed under same domain (`https://portal-gerencial-test.alpla.net`) will map cleanly to IIS `/api` sub-application, completely eliminating CORS complexities.
- **Alternatives considered:** (1) Hardcoding staging domain in the frontend bundle (rejected: introduces environment-specific dependencies in compiled bundles). (2) Writing raw/mock hashes directly to the SQL database (rejected: dangerous and breaks security validation).
- **Consequences:** Restored full administrative control to Leonardo on staging. Cleaned up Vite frontend bundles to refer to same-origin `/api` path, making deployments environment-agnostic.

---

## DEC-134 — AOVIA1VMS011 Staging Connection String Key Correction & DataProtection Hardening

- **Date:** 2026-05-25
- **Status:** Accepted
- **Context:** Following the relative base path fix, the login request successfully reached the backend, but returned `HTTP 500`. The Event Viewer flagged `System.InvalidOperationException: The ConnectionString property has not been initialized` during AuthController.Login because the secure configuration script mapped the SQL credentials to the environment variable key `ConnectionStrings__PortalDatabase`, whereas `Program.cs` strictly expects the key `DefaultConnection` via `builder.Configuration.GetConnectionString("DefaultConnection")`. Event Viewer also logged warnings regarding ephemeral in-memory DataProtection keys because write permissions were unavailable on w3wp key storage repositories.
- **Decision:** Align backend connection string configuration and document system hardening:
    1. **IIS Configuration Patch**: Patched the local secure IIS AppPool configuration script `AOVIA1VMS011_PHASE3_SECURE_CONFIGURATION.ps1` to write the correct **`ConnectionStrings__DefaultConnection`** environment variable key to `applicationHost.config` on `PortalGerencialTestApiPool`, and recycle the AppPool.
    2. **IIS Virtual Directory Routing Audit**: Confirmed the double `/api` virtual path prefix (`/api/api/auth/login`) is caused by combining the IIS `/api` sub-application directory with the backend controller's prefix, and verified same-origin routing maps this structure successfully.
    3. **DataProtection Hardening Analysis**: Audited key ring warnings and recommended registry-based persistent keyring storage for the production launch.
- **Alternatives considered:** Changing `Program.cs` to read `PortalDatabase` key (rejected: requires redeploying backend binaries, which increases regression risk compared to a clean environment variable patch).
- **Consequences:** Resolved the HTTP 500 login failure by initializing ApplicationDbContext with a valid connection string. Staged staging credentials securely. Placed a path for production DPAPI key persistence.

---

## DEC-135 — Integration Management Module Architecture & Security Remediation

- **Date:** 2026-05-25
- **Status:** Accepted
- **Context:** After deploying the system to Test/Staging (v2.152.0), it became clear that integration settings for Primavera, Innux, OpenAI, and SMTP were either hardcoded in appsettings or available only through IIS environment variables. No admin-facing UI existed for managing integration credentials, and `appsettings.Development.json` was tracked in Git with plaintext SQL credentials (sa / P@ssw0rd). The existing `IntegrationProviderSettings` entity (from migration 20260414131442) was never read by any connection factory.
- **Decision:**
    1. **Security Remediation**: Remove `appsettings.Development.json` from Git tracking (`git rm --cached`), add to `.gitignore`, document migration to `dotnet user-secrets` for local development. All exposed Primavera/Innux SQL passwords must be rotated.
    2. **Schema Reuse**: Reuse the existing `IntegrationProviders`, `IntegrationProviderSettings`, and `IntegrationConnectionStatuses` tables instead of creating a duplicate `SystemIntegrationSettings` table.
    3. **Schema Extension**: Add `IsReadOnly`, `SecretVersion`, and `UpdatedByUserId` columns to `IntegrationProviderSettings`.
    4. **Provider Unification**: Seed OPENAI and SMTP as new `IntegrationProvider` rows (Id=3, Id=4) alongside existing PRIMAVERA and INNUX.
    5. **Runtime Configuration Cascade**: DB-backed `IntegrationProviderSettings` (highest priority) → `IConfiguration` / appsettings / env vars (fallback) → safe disabled state (never crash).
    6. **Factory Refactoring**: Modify `PrimaveraConnectionFactory`, `InnuxConnectionFactory`, and `DocumentExtractionSettingsService` to read DB settings first with config fallback, preserving current local dev behavior.
    7. **Secret Handling**: DTOs never return `EncryptedPassword` or `ApiKeyEncrypted`. API responses expose only `HasPassword: true/false` and `HasApiKey: true/false`. Secret rotation uses a dedicated `POST /{code}/secret` endpoint.
    8. **Encryption Key Hardening**: The current `AesEncryptionHelper` hardcoded fallback key is tolerated for local development ONLY. Staging and Production environments MUST provide `AppConfig__EncryptionKey` as an IIS environment variable. This is a mandatory hardening item before production deployment.
    9. **Frontend**: New `IntegrationSettings.tsx` page at `/admin/integrations` with provider cards, configure modal, masked secret display, replace-secret workflow, and test-connection workflow.
    10. **Read-Only Mode**: Primavera and Innux must remain read-only when configured for Staging/Production unless explicitly approved.
- **Alternatives considered:** Creating a new `SystemIntegrationSettings` entity (rejected: duplicates existing schema). Using DataProtection API instead of AesEncryptionHelper (rejected: not yet persistent in Production, see DEC-134).
- **Consequences:** Eliminates plaintext credentials from source control. Provides admin-managed integration configuration through the Portal UI. Enables self-service deployment to new environments without requiring manual appsettings editing. Maintains backward compatibility with existing config-driven behavior.
- **Related:** [INTEGRATION_MANAGEMENT_ARCHITECTURE_REVIEW.md](INTEGRATION_MANAGEMENT_ARCHITECTURE_REVIEW.md)

---

## DEC-136 — Live Guide System (Interactive Task Guidance)

- **Date:** 2026-05-28
- **Status:** Accepted
- **Context:** The existing Guided Tour system (DEC-132) provides explanatory walkthroughs of UI elements. Users requested interactive step-by-step guidance for complex tasks like creating a new purchase request — helping them fill the form while validating each field before progression. This is fundamentally different from "explaining the screen" and requires per-step validation, controlled state, and real form interaction during the guide.
- **Decision:**
    1. **Reusable Extension**: Implement the Live Guide as a reusable extension of the existing Guided Tour architecture, not as a one-off solution inside any specific page.
    2. **Separate Namespace**: Use `data-guide` attributes for Live Guide targets, keeping them separate from `data-tour` (Guided Tours) to avoid namespace collisions.
    3. **Controlled Joyride**: Use Joyride in controlled mode (`continuous={false}`, manual `stepIndex`) exclusively for spotlight, overlay, and positioning. All step transitions are managed by a custom `useLiveGuide` hook.
    4. **Custom Tooltip**: Use Joyride's `tooltipComponent` prop for a fully custom tooltip with validation indicators, blocked progression, skip buttons, and Portuguese copy.
    5. **Factory Pattern**: Guide definitions are created via factory functions that receive form state getters, decoupling the guide system from page component internals.
    6. **Manual Start Only**: Live Guides start only from explicit user action (no auto-start). Auto-start may be evaluated after UX validation.
    7. **Provider Nesting**: `LiveGuideProvider` is nested inside `GuidedTourProvider` so both systems coexist in the same provider tree.
    8. **Topbar Integration**: The help dropdown (`GuidedTourButton`) automatically discovers live guides available for the current route via the `liveGuideRegistry`.
- **Alternatives considered:** Converting the form into a wizard (rejected: user must see and use the full form), building a separate tooltip system without Joyride (rejected: duplicates spotlight/overlay/positioning logic), using `data-tour` for both systems (rejected: namespace collision risk).
- **Consequences:** Establishes a scalable pattern for adding interactive task guides to any page. First implementation: Request Creation (10-step guide with field validation). The system can be extended to other complex forms (contracts, items, etc.) by adding new guide definitions.
- **Related:** [GUIDED_TOUR_SYSTEM.md](GUIDED_TOUR_SYSTEM.md) § 19

---

## DEC-137 — Supplier Import Relocation (Dados Mestres → Fichas de Fornecedor)

- **Date:** 2026-06-08
- **Status:** Accepted
- **Context:** The Primavera supplier import was located under Configurações → Dados Mestres → Fornecedores. The business process requires imported suppliers to enter as DRAFT fichas for the Contracts team to review and complete registration. Hosting the import entry point in a settings/maintenance screen did not align with this workflow.
- **Decision:**
    1. **Remove** the "Sincronizar com Primavera" entry point from `MasterData.tsx` (Dados Mestres → Fornecedores tab).
    2. **Add** an "Importar do Primavera" button to `SupplierFichaList.tsx` (Contratos → Fichas de Fornecedor).
    3. **Add** a new route `/contracts/sync/:entityType` in `App.tsx`, guarded by `ROLES.CONTRACTS`, reusing the existing `SyncWorkspace` component.
    4. **Make** `SyncWorkspace.tsx` back-navigation context-aware: when accessed from `/contracts/`, the back button navigates to `/contracts/fichas` with label "Fichas de Fornecedor"; from `/settings/`, it navigates to `/settings/master-data` with label "Dados Mestres".
    5. **Preserve** the `/settings/sync/:entityType` route for catalog sync (Dados Mestres → Catálogo de Itens).
    6. **No backend changes**: The existing API already enforces `RegistrationStatus = DRAFT` for synced suppliers, enriches fields from Primavera, and performs duplicate detection by PrimaveraCode and NIF.
- **Alternatives considered:** Creating a new dedicated sync component inside the Contracts module (rejected: unnecessary duplication; the existing SyncWorkspace is generic and reusable via route parameterization).
- **Consequences:** The supplier import flow now aligns with the business workflow where Contracts reviews and completes supplier fichas. Dados Mestres retains its CRUD maintenance focus without sync operations for suppliers. Catalog sync remains unaffected.

