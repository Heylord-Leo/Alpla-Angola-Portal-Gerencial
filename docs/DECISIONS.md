# Decisions Log

Purpose: record important technical and process decisions so future work preserves context.

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
