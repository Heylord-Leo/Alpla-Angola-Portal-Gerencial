# Changelog

## [v2.10.2] - 2026-03-30

### Added
- **Conditional Need Date Restoration**: Re-introduced the "Data de Necessidade" field in the Request header, specifically for `QUOTATION` (Cotação) request types.
- **Backend Validation**: Implemented server-side enforcement in `RequestsController` to ensure the date is mandatory and valid for quotations.

### Changed
- **Form Layout Refinement**: Integrated the date field into the primary "Dados Gerais" grid, improving visual alignment and grid efficiency.
- **UX Animation**: Added smooth conditional transitions using `AnimatePresence` for a premium interface feel.

## [v2.10.1] - 2026-03-30

### Changed
- **Modernized Login Screen**: Replaced the legacy GIF with a high-resolution animation (`login-animation-960w.gif`) and removed the redundant "Portal Gerencial" heading for a cleaner look.
- **Request Creation Layout Refactor**: Moved the "Documentos de Apoio" attachment area from the bottom of the form to a contextual position directly below the "Descrição ou justificativa" field.
- **Attachment UI Modernization**: Implemented a lighter, inline/sub-label treatment for attachments, replacing the heavy section card with a compact dropzone and a responsive grid layout for files.

## [v2.9.17] - 2026-03-29

### Fixed
- **Quotation Workspace Filtering**: Implemented strict backend filtering for the "Gestão de Cotações" workspace. Only requests of type `QUOTATION` are now included in the list and KPI summaries, preventing data leakage from `PAYMENT` requests.
- **Centralized Constants**: Introduced `RequestConstants.cs` and `AttachmentConstants.cs` to eliminate hardcoded string literals throughout the backend controllers, improving maintainability.

### Added
- **Performance Optimization**: Added database indexes for `CreatedAtUtc` and `RequestId`/`IsDeleted` to optimize query performance for large request datasets.

## [v2.9.16] - 2026-03-29

### Added
- **Quotation Workflow Locking**: Implemented a mandatory read-only boundary for quotations once a request advances beyond the quotation/adjustment phase (WAITING_AREA_APPROVAL and later). This ensures commercial data integrity throughout the approval and operational lifecycle.

### Fixed
- **Status Guards Enforcement**: Applied centralized status-based mutation guards to all relevant backend endpoints (SaveQuotation, UpdateQuotation, DeleteQuotation, OcrExtract, and Proforma management).
- **Data Integrity Fix**: Added missing .Include(r => r.Status) to the SaveQuotation logic to ensure the request status is always available for validation, preventing runtime null references.

### Changed
- **Quotation Management UI**: Standardized "Gestão de Cotações" to hide (instead of disable) mutation actions when a request is in a locked state, providing a cleaner tracking interface for post-quotation workflows.


## [v2.9.15] - 2026-03-29

### Fixed
- **Quotation Completion Validation**: Resolved a persistent issue where the "Concluir cotação" action was blocked by a "zero items" error even when quotations with items were present. The validation logic now correctly calculates a union of request-level items and items stored within Quotation entities.
- **Workflow Signature Consistency**: Standardized the `completeQuotationAction` call across `BuyerItemsList.tsx` and `RequestEdit.tsx`.

## [v2.10.0] - 2026-03-29

### Added
- **Modernized Request Creation**: Reformulated the "Novo Pedido" screen for improved clarity and workflow alignment.
- **Support Documents Integration**: Re-introduced support for attaching technical files (photos, PDFs) directly during the draft creation stage.
- **Role-Based Participant Filtering**: Implemented dynamic filtering in the workflow participant selects (Comprador, Aprovador de Área, Aprovador Final) using real system users and real role checks.

### Changed
- **Draft Creation Flow**: Simplified the initial form by removing redundant fields (`Fornecedor`, `Necessário até`) that are now handled in the submission/buyer stages.
- **DTO Resilience**: Updated `CreateRequestDraftDto` to use nullable types for ID fields, improving model binding stability and validation feedback.

### Fixed
- **Generic Validation Error (400)**: Resolved the "One or more validation errors occurred" issue that blocked request creation by aligning frontend payload types with backend binder expectations.

### Fixed
- **Session Persistence**: Switched authentication storage from `localStorage` to `sessionStorage`. Sessions are now strictly tab/window scoped and do not survive browser closure, improving security for the administrative portal.

## [v2.9.13] - 2026-03-29

### Fixed
- **Modal Layering & Stacking Context**: Resolved a critical UI conflict where the sticky Topbar (z-index 100) obscured modal dialogs and side drawers. Implemented `DropdownPortal` in `UserManagement.tsx`, `ApprovalModal.tsx`, and `ReceivingModal.tsx` to ensure overlays render at the `document.body` level, bypassing core layout stacking constraints.
- **Import Resolution**: Fixed a naming collision in `UserManagement.tsx` between the `Link` icon (lucide-react) and the `Link` routing component (react-router-dom).

## [v2.9.12] - 2026-03-28

### Fixed
- **Line-Item Persistence Bug**: Fixed a regression in `RequestsController` where adding, updating, or deleting line items failed with an Entity Framework persistence error due to missing mandatory audit fields in `RequestStatusHistory`.

## [v2.9.11] - 2026-03-28

### Fixed
- **Buyer OCR Authentication**: Standardized the quotation document upload flow in `BuyerItemsList.tsx` to use the centralized `api.attachments.upload` utility, ensuring consistent JWT token propagation.
- **Admin Logs Authentication**: Refactored `SystemLogs.tsx` to use `apiFetch()`, securing administrative access to backend logs.

## [v2.9.10] - 2026-03-28

### Fixed
- **API Client Auth Consistency**: Resolved an issue where the Document Extraction Settings page (`/settings/document-extraction`) was failing with `401 Unauthorized` for valid `System Administrator` users. The frontend `api.ts` module was incorrectly using the browser's native `fetch()` for backend `admin` endpoints, bypassing the centralized credential injection that attaches the `Authorization: Bearer` JWT token. This has been corrected to use `apiFetch()`. The core OCR extraction engine in the business procurement screens was unaffected by this bug.

## [2.9.9] - 2026-03-28

### Fixed
- **Notification Database Migrations**: Added missing EF Core migration for `NotificationStatuses` and `InformationalNotifications`, and applied it to the active database.
- **Global Data Resilience**: Repaired a critical bug where failed global API calls for notifications threw uncaught exceptions that indirectly broke unrelated workflows like Document Extraction.

## [2.9.8] - 2026-03-28

### Removed
- **Payment Document-First Workflow (Rollback)**: Reverted the experimental "Document-First" creation flow for Payment Requests to restore workspace stability.
- **Workflow Reversion**: Removed the automatic redirect to the Proforma entry tab (`focus=proforma`) upon Payment creation.
- **UI Decoupling**: Unlinked the `QuotationEntry` component from the Payment Edit screen, returning it exclusively to the Buyer workspace.
- **Validation Restoration**: Restored mandatory `SupplierId` and `NeedByDateUtc` validation constraints for Payment Requests at both the frontend form and backend API levels.
- **Legacy Signature Fixes**: Reverted `completeQuotationAction` and `handleCompleteQuotation` to their legacy parameter signatures, resolving structural property mismatch errors.

## [2.9.7] - 2026-03-28

### Changed
- **Zero-Trust Authorization**: Transitioned from simulated `X-User-Mode` persona headers to a strictly claims-based authorization model.
- **Centralized Role Management**: Introduced `RoleConstants.cs` (Backend) and `roleConstants.ts` (Frontend) to eliminate raw role strings.
- **Backend Refactor**: Updated `BaseController`, `RequestsController`, and `LineItemsController` to use assigned user roles for workflow actions.
- **Frontend State Purge**: Completely removed `userMode` state, simulated persona logic, and the "Visualizar Como" UI dropdown from `RequestEdit.tsx`.
- **API Security**: Stripped all legacy `mode` parameters and `X-User-Mode` header injection from the API layer.

### Removed
- **"Visualizar Como" simulation mode**: Manual persona switching is no longer possible; the portal now always follows the real user context.
- **Simulated Role Inheritance**: Removed implicit operational bypass for `System Administrator`. Operational tasks (e.g. `Register PO`) now require explicit role assignment.

## [2.9.5] - 2026-03-28

### Fixed
- **Plant Data Collection Rule**: Corrected logic in the New Request screen that improperly disabled the Plant field. The field now appropriately remains enabled when users have multiple authorized plants, dynamically auto-selecting and locking only when contextually narrowed to a single option.

## [2.9.4] - 2026-03-28

### Added
- **Plant-Scope-Based Request Restrictions**: Implemented mandatory access control for the "Empresa" and "Planta" fields in the New Request flow.
- **Frontend Filtering**: Requests are now restricted to plants assigned to the user's profile, with auto-selection and field-locking for specific scenarios (single-plant, single-company).
- **Backend Enforcement**: Added `403 Forbidden` checks to ensure no request can be created or updated outside the user's authorized plant scope.
- **Data Consistency**: Added backend validation to ensure the submitted Company matches the selected Plant.
- **UX Guardrails**: Added a "No Plants Assigned" alert to prevent invalid form submissions for unconfigured profiles.

## [2.9.3] - 2026-03-28

### Added

- **Improved User Profile Transparency**: Transformed the "Meu Perfil" drawer into a comprehensive account management interface.
- **Dynamic Data Mapping**: Integrated plant and department lookups to map raw system codes to user-friendly labels (e.g., "PL01" -> "Planta Luanda").
- **Account Status Visibility**: Added explicit account state indicators (Ativo/Inativo) with high-contrast status badges.
- **Structured Role Management**: Refactored user roles into a dedicated, readable section with distinct permission badges and empty-state handling.
- **Direct Account Actions**: Integrated working "Alterar Palavra-passe" flow and "Contactar Suporte TI" deep links directly into the profile view.

## [2.9.2] - 2026-03-28

### Fixed

- Restricted "Configurações" (Settings) menu access to `System Administrator` role only.
- Implemented frontend route protection for Master Data and Document Extraction settings using `AdminRoute`.
- Secured backend endpoints in `DocumentExtractionSettingsController` and administrative methods in `LookupsController` with role-based authorization.
- Ensured unauthorized access attempts to restricted routes are redirected to `/dashboard`.

## [2.9.1] - 2026-03-28

### Added

- **Robust Notification Engine**: Implemented a new centralized notification service (`NotificationService`) that handles both operational sync and informational alerts.
- **Operational Workflow Synchronization**: Notifications for approvals, quotations, receiving, and payments now automatically reset to "Unread" if the underlying task count increases (`CurrentCount > LastReadCount`).
- **Notification Persistence**: Introduced `NotificationStatus` and `InformationalNotification` entities for per-user read-state tracking and dismissible alerts.
- **Management API**: Created `NotificationsController` with endpoints for listing, marking as read, marking all as read, and clearing informational notifications.
- **UI Management Actions**: Added "Marcar todas como lidas" and "Limpar lidas" functionality to the `NotificationBell` dropdown.
- **Visual Read-States**: Implemented distinct styling for unread items with high-contrast indicators and bold typography, strictly following standardized UI standards.

### Fixed

- **Notification Ordering**: Enforced stable ordering: Operational (Unread > Read) followed by Informational (Unread > Read).
- **Redundant API**: Removed legacy notification endpoint from `RequestsController` to de-duplicate logic.

## [2.9.0] - 2026-03-28

### Added

- **Compras & Logística Cockpit**: Implemented a new top-level management dashboard at `/purchasing` that aggregates the entire procurement flow.
- **Unified Purchasing API**: Created `GET /api/v1/requests/purchasing-summary` to provide cross-status operational counts and identified bottleneck alerts.
- **Operational Manual Drawer**: Developed an interactive right-side slide-in drawer for contextual help and operational guidance within the purchasing workspace.
- **Attention Panel**: Integrated a real-time alert system identifying overdue requests, pending approvals, and receiving bottlenecks.
- **Modular KPI Summary**: Refactored dashboard KPIs into a reusable component set under `src/frontend/src/components/common/dashboard/`.
- **Navigation Update**: Restructured the sidebar with a specialized "Compras & Logística" group and integrated landing page routing.

## [2.8.0] - 2026-03-27

### Added

- **Account Modernization**: Implemented a read-only "Meu Perfil" side drawer accessible from the topbar user dropdown.
- **Actionable Notifications Engine**: Developed a role-aware notification system that provides real-time counts and direct links to pending tasks (Approvals, Quotations, Receiving, Payments).
- **Integrated Password Flow**: Relocated and redesigned the "Alterar Palavra-passe" flow into the AppShell, ensuring it inherits the product layout and brand styles.
- **Backend Notification API**: Created a new `GET /api/v1/requests/notifications` endpoint in `RequestsController` with plant/department scoping and role-based logic.
- **Enhanced Dropdowns**: Updated `NotificationBell` and `UserDropdown` with Shell 2.0 aesthetics and improved interactivity.

### Fixed

- **Navigation Consistency**: Resolved visual raw/unintegrated appearance of the password change page by bringing it into the authenticated shell.
- **Lint Cleanup**: Eliminated unused imports and resolved TypeScript warnings in shell-level components.


## [2.7.0] - 2026-03-27

### Added

- **UI/UX Standardization Pass**: Re-aligned the `Login` page and `User Management` screen with the "Shell 2.0" standardized UI design.
- **Right-Side Sliding Drawer**: Converted the User Management edit/create modals to a persistent side drawer pattern for improved context preservation during complex data entry.
- **ALPLA Corporate Identity**: Restored brand colors (`#005596`, `#007AC3`) and typography (Montserrat/Inter) to the authentication flow.
- **Standardized Header Patterns**: Unified breadcrumbs and page titles across the administrative workspace.

## [2.6.0] - 2026-03-27

### Added

- **User Management Administration UI**: Integrated a high-density management table for users, roles, and plant/department scopes.
- **Permission Hardening**: Implemented strict subset-based scope checks for Local Managers (can only manage users within their own allowed plant/department boundaries).
- **Role Assignment Rules**: Restricted Local Managers to assigning only System Administrator-approved roles.
- **One-Time Password Flow**: Added a secure "Copy to Clipboard" modal for new user passwords and resets, ensuring passwords are never logged or stored in plain text.
- **Backend Administrative API**: Expanded `UsersController` with full CRUD, auditing via `AdminLogWriter`, and lookups for roles and scopes.
- **User Entity Navigation**: Updated the `User` domain entity with navigation properties for roles and scopes to enable robust Entity Framework querying.

## [2.5.1] - 2026-03-27

### Added

- **Aguardando Pagamento KPI Card**: Integrated a new operational summary card for payment-pending requests.

## [2.5.0] - 2026-03-27

### Added

- **Collapsible Sidebar**: Implemented a responsive navigation sidebar with expanded (260px) and collapsed (80px) states.
- **Dynamic Branding**: Integrated state-based logo switching (Full ↔ Compact) within the navigation header.
- **Layout Adaptivity**: Refactored `AppShell` with dynamic grid logic and smooth transitions for professional workspace management.
- **User Preference**: Sidebar state is persisted in `localStorage` across sessions.

## [2.4.1] - 2026-03-27

### Added

- **Synchronized Urgency Indicators**: Integrated overdue and due-soon visual logic into the Buyer / Gestão de Cotações workspace.
- **Urgency Visuals**: Added a 4px wide vertical color bar and tooltip to each request card in "Gestão de Cotações" for cross-module parity.
- **Refined Status Logic**: Updated `isFinalizedStatus` in `lib/utils.ts` to exclude `QUOTATION_COMPLETED`, `PO_ISSUED`, and financial statuses from urgency highlighting.

## [2.4.0] - 2026-03-27

### Added

- **KPI Dashboard & Summary Cards Standard**: Formalized the project-wide pattern for operational summary cards.
- **Normative Directives**: Created `directives/kpi_dashboard_standard.md` to govern future dashboard implementations (Directive Layer).
- **UI Design Reference**: Created `docs/ui/kpi_cards.md` defining the "Industrial Brutalist" visual and animation standards.

### Fixed

- **KPI Active State Synchronization**: Implemented robust subset-based status matching in `RequestsList.tsx`, ensuring consistent highlights for grouped status cards (e.g., "Finalizados", "Aguardando Aprovação").
- **Unified Identifier Mapping**: Synchronized KPI display labels with normalized filter keys across the Requests module.

## [2.1.0] - 2026-03-27

### Added

- **Management Dashboard KPIs**: Integrated interactive KPI summary cards at the top of the Requests page.
- **Unified Data Flow**: Refactored the backend to return a bundled `RequestListResponseDto`, ensuring 100% count consistency between the summary cards and the paginated list.
- **Animated Components**: Implemented `KPISummary` and `KPICard` using Framer Motion with "Industrial Brutalist" aesthetics (thick borders, high-contrast shadows).
- **Interactive Filtering**: KPI cards now act as quick-filter shortcuts that stay synchronized with the URL search parameters and list state.

### Fixed

- **Plant Data Persistence**: Validated and enforced full-stack persistence of the `PlantId` field from creation through to project-level list projection.
- **Strict Plant Filtering**: Corrected the Requests list filtering logic to use header-level `PlantId`, preventing incorrect matches from item-level associations.

## [2.0.6] - 2026-03-27

### Fixed

- **Requests List Plant Filtering**: Updated the backend logic in `RequestsController` to filter by strict Request Header `PlantId`. This ensures that older requests with `null` plants (displaying `---`) are correctly excluded when a specific plant filter is active, even if their line items match the filter.

## [2.0.5] - 2026-03-27

### Fixed

- **Backend Plant Persistence**: Resolved a critical issue where `PlantId` was not being persisted during request creation or updates.
- **DTO Synchronization**: Updated `CreateRequestDraftDto` and `UpdateRequestDraftDto` to include the mandatory `PlantId` field.
- **Controller Logic**: Fixed `RequestsController` to correctly map and persist `PlantId` from DTOs to the domain entity.
- **Buyer Workspace Projection**: Enhanced `LineItemDetailsDto` and `LineItemsController` to include header-level plant information, ensuring correct display in the Gestão de Cotações view.

## [2.0.4] - 2026-03-27

### Added

- **Requests Modernization (V3 High-Fidelity)**: Full structural and aesthetic rebuild of the Requests screen to "Golden Reference" standards.
- **Requester Avatars**: Integrated user initials and roles into the table rows for improved identity context.
- **ActionMenu Integration**: Unified row actions (Visualizar, Aprovar, Rejeitar) into a modern Shell 2.0 dropdown.
- **Functional Approval/Rejection**: Implemented direct API routing for Approvals and Rejections within the Requests list.
- **High-Density Pagination**: Modernized the footer with "DICA" info box and Shell 2.0 button styling.

## [2.0.3] - 2026-03-27

### Added

- **Requests Modernization (Finalized)**: Completed the deep refactor of `RequestsList.tsx` to align with Shell 2.0 aesthetics.
- **Design Tokens Integration**: Implemented full-bleed KPI cards, refined filter hub grid, and Shell 2.0 corporate table typography.
- **Technical Stabilization**: Resolved TypeScript DTO mappings (requesterName) and JSX nesting issues in the Filter Hub.

## [2.0.2] - 2026-03-26

### Added

- **Requests Modernization (Initial)**: Structural foundation for the Requests screen refactor.
- **Visual Harmonization**: Implemented card-based filter hub and refined header layout.
- **Improved UX**: Enhanced row expansion and timeline visibility.

## [2.0.1] - 2026-03-26

### Added

- **Sidebar Usability & Version Sync**:
  - Reorganized internal sidebar structure into **Header / Navigation / Footer** vertical zones.
  - Implemented **Viewport-Bounded Anchoring** in `AppShell` grid to ensure the sidebar footer is always visible.
  - Added **Independent Scrolling** to the sidebar navigation area for improved accessibility.
  - Synchronized project versioning across `CHANGELOG.md`, `VERSION.md`, `package.json`, and backend metadata.
  - Integrated **Real-time Backend Version Sourcing** via a new lightweight diagnostics endpoint.

### Changed

- **Sidebar Footer**: Now displays both Frontend (FE) and Backend (BE) versions with graceful fallback.
- **Documentation**: Synchronized and updated `VERSION.md` (canonical alignment).

## [2.0.0] - 2026-03-26

### Added

- **Modernization Foundation (Shell 2.0 Preparation)**:
  - Upgraded to **React 19** and **React DOM 19**.
  - Upgraded to **React Router 7** (declarative mode).
  - Migrated to **Tailwind CSS 4** (Vite-plugin / CSS-first engine).
  - Replaced `framer-motion` with **`motion/react`** for optimized modular animations.
  - Integrated **Sonner** for future notification systemic support.

### Changed

- **Build System**: Upgraded to **Vite 6** to support Tailwind 4 and React 19.
- **Global Styles**: Migrated `globals.css` to Tailwind 4 `@theme` block system.
- **Type Safety**: Resolved React 19 `RefObject` and `useRef` strictness in Autocomplete components.
- **Cleanup**: Removed obsolete `tailwind.config.js` and `postcss.config.js`.

## [1.3.0] - 2026-03-26

### Added

- **Shell/Layout Structure Migration (Phase 2)**:
  - Implemented **Collapsible Sidebar** with `isCollapsed` state and smooth CSS transitions.
  - Redesigned **Topbar** with backdrop-blur glassmorphism effect and fixed positioning.
  - Refactored **AppShell** to utilize a dynamic CSS Grid layout that responds to sidebar state.
  - Integrated **Lucide-React** for unified shell-level navigation icons.

### Fixed

- **Layout Overlap**: Added automatic content offset in `AppShell` to clear the fixed topbar.
- **CSS Order**: Resolved build warnings by reordering `@import` rules in `globals.css`.
- **Lint Cleanup**: Removed unused imports across shell components.

## [1.3.1] - 2026-03-26

### Added

- **Shell Correction Pass (Phase 2.1)**:
  - Transitioned `AppShell` to a **Structural Root Grid** enabling full-height sidebar and correctly spanned topbar.
  - Implemented **Inner Content Max-Width** strategy to preserve shell integrity on large viewports.

### Changed

- **Topbar**: Removed search box and transitioned to structural alignment.
- **Sidebar**: Refactored to act as a continuous structural column.

### Fixed

- **Shell Alignment**: Corrected left-side gaps and misalignment issues in the header area.
- **Scroll Behavior**: Ensured natural document scrolling is preserved across the new grid layout.

## [1.2.3] - 2026-03-26

### Added

- **UI Migration Phase 1:** Global Styles and Tokens.
  - Updated `tokens.css` with Shell 2.0 colors (#003568), Montserrat/Inter fonts, and rounded corners (8px/12px).
  - Modernized `globals.css` with softer button styles, pill-shaped badges, and refined corporate tables.
  - Updated `UI_UX_GUIDELINES.md` to reflect the new Modern Corporate visual direction.

## [1.2.2] - 2026-03-26

Phase 4 Stabilization of the **Quotations Workspace**.

### Fixed

- **Structural Integrity**: Resolved JSX syntax regressions in `BuyerItemsList.tsx` to restore frontend compilation while preserving all business logic and OCR flows.
- **Documentation Hygiene**: Sanitized all absolute `file:///` links across `docs/`, `directives/`, and markdown artifacts, converting them to repository-relative paths.
- **Path Standards**: Ensured documentation integrity by removing local absolute paths from technical guides and artifacts.

### Changed

- **Build Stability**: Confirmed end-to-end frontend boot status without parser or build blockers.

---

## [1.2.1] - 2026-03-26

Phase 2 of the **Shell 2.0** redesign.

### Added

- **UI Modernization (Admin Foundation)**: Redesigned Master Data, Document Extraction Settings, Administrator Workspace, System Logs, Integration Health, and Service Diagnosis using the "Industrial Brutalist" layout.
- **Hybrid CSS Capability**: Maintained stable tokens.css propagation while deploying functional Tailwind markup onto administrative pages.

---

## [1.2.0] - 2026-03-26

Starting the official migration to the **Shell 2.0** redesign.

### Added

- **UI Modernization Strategy**: Defined the hybrid migration path using TailwindCSS while preserving React Router and business logic.
- **Shell 2.0 Design Tokens**: Integrated the new refined "Industrial Brutalist" palette and component standards into project documentation.
- **Migration Roadmap**: Established Phase 1-4 plan with `RequestsList` as the pilot screen.

---

## [1.1.67] - 2026-03-25

### Fixed

- **Status Badges (Requests List)**: Removed internal numeric prefix (`DisplayOrder`) from user-facing status badges. Badges now show only the human-readable label (e.g., "Rascunho" instead of "1. Rascunho").
- **Badge Compactness**: Added `.badge-sm` CSS utility for dense table contexts. Overrides `padding`, `font-weight`, `letter-spacing`, and `white-space` to reduce horizontal width pressure in the Requests list without affecting badge color semantics.
- **Feedback Layout Stabilization**: Moved ephemeral feedback messages in `RequestActionHeader` to a fixed-position viewport overlay (via React Portal), eliminating layout shift (CLS) during banner appearance/dismissal.

## [1.1.66] - 2026-03-25

### Fixed

- **Workflow Hardening**: Implemented mandatory content check for `QUOTATION` submissions (requires at least one item or attachment).
- **Validation Consistency**: Standardized document-related validation error titles to `Ação Bloqueada` across the workspace.

## [1.1.65] - 2026-03-25

### Added

- **Audit History**: Implemented history logging for substantive draft updates (`DADOS_ALTERADOS`) and quotation management actions (`COTACAO_ADICIONADA`, `COTACAO_SUBSTITUIDA`).

### Changed

- **Audit Hardening**: Ensure business-relevant edits are traceable in the request history list before submission and during the buyer stage.

## [1.1.64] - 2026-03-25

### Fixed

- **Timeline Mapping**: Corrected `GetQuotationStages` and `GetPaymentStages` to properly include `IN_FOLLOWUP` in the "Recebimento" stage.
- **API Connectivity**: Fixed incorrect argument signature in frontend `finalize` request for the Receiving flow.
- **User Interface**: Refined `ApprovalModal` wording for partial receipts to explicitly state the move to "Acompanhamento" operacional.

### Changed

- **Auditability**: Enhanced `FinalizeRequest` history registration to explicitly document pending items when partially finalized.

## [1.1.63] - 2026-03-25

### Changed

- **Supplier Synchronization Hardening**: Implemented header synchronization when a winning quotation is selected.
  - **Header Alignment**: Automatically syncs `SupplierId`, `EstimatedTotalAmount`, and `CurrencyId` to the request header from the winning quotation to ensure data consistency.
  - **Currency Lookup**: Added robust case-insensitive lookup for matching quotation currency codes to system `CurrencyId`, with fallback protection to avoid invalid data.
  - **Audit Traceability**: Integrated `AdminLogWriter` to document synchronization events and potential lookup failures in `AdminLogs`.

## [1.1.62] - 2026-03-25

### Changed

- **Administrative Workspace Stabilization**: Narrow structural and visual alignment of administrative pages (`SystemLogs`, `IntegrationHealth`, `ServiceDiagnosis`) with the portal's Brutalist header and design standards.
- **Naming Alignment**: Standardized integration naming from "SAP" to "AlplaPROD" / "Primavera ERP" across the Administrative Workspace to ensure consistent terminology.
- **SystemLogs Refactor**: UI alignment for the System Logs page, replacing hardcoded styles with system variables and standardizing typography.

## [1.1.61] - 2026-03-25

### Changed

- **RequestsController Workflow Hardening**: Implemented minimal protection rules to stabilize the backend request flow.
  - **SupplierId Lock**: For `QUOTATION` type requests, the header `SupplierId` is now read-only via the `UpdateRequestDraft` endpoint once the request leaves the `DRAFT` status. This prevents bypassing the Quotation-based model (where suppliers are managed via specialized winner selection).
  - **Controlled Process Logging**: Added targeted informational logging to `SyncLineItemStatusesAsync` to provide traceability of automated line item status synchronizations in `AdminLogs`.

## [1.1.60] - 2026-03-25

### Added

- **Sistema de Logs Administrativos (Step 2)**: Implemented structured, queryable admin audit log system.
  - **`AdminLogEntry`**: New domain entity for admin-queryable operational events (`Level`, `EventType`, `Source`, `CorrelationId`, `ExceptionDetail`, `Payload`).
  - **`AdminLogWriter`**: Dedicated best-effort persistence service (not a generic `ILoggerProvider`). Log writes never break the main request flow.
  - **`CorrelationIdMiddleware`**: Accepts or generates `X-Correlation-ID` per request, stores in context, returns in response header for client-side traceability.
  - **`SafePayload`**: Two-layer sanitization (known-field masking + regex redaction) to prevent persistence of secrets or sensitive data.
  - **`AdminLogsController`**: `GET /api/admin/logs` with server-side filtering (level, source, eventType, correlationId, date range, free text) and pagination. `GET /api/admin/logs/{id}` for full detail.
  - **OCR Audit Integration**: `DocumentExtractionSettingsService` now emits `OCR_SETTINGS_SAVED`, `OCR_SETTINGS_VALIDATION_FAILED` (Warning), `OCR_SETTINGS_SAVE_FAILED` (Error), `OCR_PROVIDER_TEST_OK`, and `OCR_PROVIDER_TEST_FAILED` events via `AdminLogWriter`.
  - **Logs do Sistema UI**: Replaced placeholder with functional data table featuring multi-criteria server-side filters, newest-first order, severity badges, and a detail panel showing `ExceptionDetail`, sanitized `Payload`, and an inline Correlation ID quick-filter shortcut.

### Added

- **Administrator Workspace (Step 1)**: Established the foundational structure for administrative management.
  - **Protected Hub**: Created `/admin/workspace` as a central entry point for administrators.
  - **Route Guarding**: Implemented `AdminRoute` wrapper and `isAdmin` scaffold to protect administrative sections from unauthorized access.
  - **Modular Navigation**: Integrated Administrator Workspace into the established modular menu pattern under the **"Administração"** group, ensuring UX consistency with other modules.
  - **Enhanced Access Scaffold**: Introduced `window.enableAdminScaffold(true/false)` global helper to facilitate manual developer validation across local browser sessions.
  - **Placeholder Sections**: Scaffolded `Logs do Sistema`, `Diagnóstico de Serviços`, and `Saúde das Integrações` as part of the modular hierarchy.

## [1.1.58] - 2026-03-25

### Added

- **Multi-page PDF Extraction**: The OpenAI extraction provider now rasterizes and processes up to 10 pages per PDF, improving extraction for long documents.
- **Unit Alias Matching**: Improved unit matching with alias support (e.g., `UNIDADE`, `UND`, `EA` -> `UN`). Supporting groups for `UN`, `KG`, `L`, `M`, and `CX`.
- **Debug Enhancements**: Multi-page PDFs now generate sequential debug images (e.g., `_page1.png`, `_page2.png`) for inspection.

## [1.1.57] - 2026-03-24

### Added

- **Quotation Extraction Currency Alias Matching**: Support for `AKZ`, `US$`, `€`, `Euro`, etc., mapping them to system codes (`AOA`, `USD`, `EUR`).
- **Quick Currency Creation**: New in-flow modal to register missing currencies directly from the quotation screen with auto-selection.
- **Dynamic Currencies**: Quotation currency dropdown now loads from live system master data.

## [v1.1.56] - 2026-03-24

### Fixed

- **Currency Fallback**: Fixed an issue where the quotation extraction flow defaulted to `USD` when the currency was missing or unmatched. The field now correctly remains empty, providing a suggestion hint when a value was extracted but not matched.

## [v1.1.55] - 2026-03-24

### Added

- **Item Tax Rate Extraction**: Added support for capturing and propagating the item-level tax percentage (IVA) to the `TAXA IVA` column.
- **Tax Rate Auto-Matching**: Implemented automatic IVA rule selection based on the extracted tax percentage.
- **Tax Suggestion Hints**: Added orange UI hints in the item table to show extracted rates that require manual rule selection.

## [v1.1.54] - 2026-03-24

### Added

- **Global Discount Extraction**: Added support for capturing and propagating the global quotation discount (absolute value) to the `VALOR DESC GLOBAL` field.

### Fixed

- **Duplicate Upload**: Resolved a bug where quotation documents were being duplicated during the OCR extraction flow. The system now correctly reuses the initial extraction upload.

## [v1.1.53] - 2026-03-24

### Added

- **Item Unit Extraction**: Added support for capturing and propagating the unit type (e.g. UN, KG) for each quotation item in the extraction flow.
- **Unit Auto-Matching**: Implemented conservative exact matching logic to automatically map extracted unit strings to master data unit IDs.
- **Extraction UI Hints**: Added visual feedback in the items list to show extracted unit suggestions for manual verification.

## [v1.1.52] - 2026-03-24

### Added

- **Document Extraction Field Propagation Standard**: Created `docs/DOCUMENT_EXTRACTION_FIELD_PROPAGATION_STANDARD.md` to define the mandatory flow for adding and validating new extraction fields.
- **Explicit Propagation Strategy**: Formalized the requirement to update all intermediate layers (Internal DTOs, Legacy Mappers, Frontend State) when adding new extraction capabilities.

### Fixed

- **Supplier Tax ID Propagation**: Fixed an issue where `supplierTaxId` was extracted by OpenAI but failed to reach the `QuickSupplierModal` due to missing mapping in the legacy OCR DTO and mapper.

## [v1.1.51] - 2026-03-24

### Added

- **Quotation Extraction UX Optimization**:
  - **Supplier Prefill**: The `QuickSupplierModal` now automatically prefills the supplier name field with the extracted OCR suggestion when opened via the "CRIAR AGORA" action.
  - **Automatic Matching**: Implemented proactive supplier matching in the OCR flow; if the extracted name exists in the database, it is automatically selected.
  - **Pre-OCR Persistence**: Quotation documents are now persisted to the `Attachments` API as `PROFORMA` before extraction, ensuring data integrity and eliminating the need for duplicate uploads.
  - **Unmatched Supplier Guidance**: Added a high-visibility warning and direct call-to-action ("CRIAR AGORA") when an extracted supplier is not found in the system.

## [v1.1.50] - 2026-03-24

### Added

- **Quotation Unit Integration**: Implemented unit-of-measure selection for individual quotation items, ensuring alignment with requested units.
- **Winner Selection Workflow**: Integrated winner selection into the Area Approval stage. Area Approvers must now select a winning quotation before approving `QUOTATION` type requests.
- **Backend Validation**: Implemented authoritative enforcement of winner selection during `ProcessAreaApproval` and role-based guards in `SelectQuotation`.
- **Document Extraction Architecture**: Decoupled backend from specific OCR implementation to support multiple providers (Step 1).
- **Extraction Configuration Refactor**: Implemented a strongly-typed, tiered configuration model for document extraction (Local OCR, OpenAI, Azure).
  - Established timeout precedence: Provider-specific > Global > Default (30s).
  - Introduced `DocumentExtractionSettings` database persistence for operational configurations.
  - Implemented `DocumentExtractionSettingsService` with prioritized resolution: Database -> Configuration -> Defaults.
  - Added global `IsEnabled` flag for document extraction.
  - Enhanced fallback logic for invalid, missing, or disabled providers.
- **Extraction Settings UI**: Implemented the administrative settings page for document extraction.
  - Added `DocumentExtractionSettings` page with Portuguese labels and industrial styling.
  - Integrated settings into the navigation sidebar under a new "Configurações" group.
  - Implemented real-time validation feedback and placeholder indicators for future AI providers.
  - Updated frontend API client and TypeScript types to support administrative settings management.
- **Connection Testing Capability**: Added "Test Connection" functionality to the extraction settings module.
  - Implemented `TestConnectionAsync` in the backend service with provider-specific logic (Local OCR ping).
  - Added a "Testar Conexão" button in the frontend settings UI with success/failure feedback and response time display.
  - Integrated `IHttpClientFactory` for lightweight connectivity checks.
- **OpenAI Document Extraction Provider (POC)**: Implemented OpenAI as a real backend provider.
  - Created `OpenAiDocumentExtractionProvider` implementation using OpenAI's **Chat Completion API** (Vision).
  - Registered the provider in the DI container for runtime resolution.
  - Implemented real-time connectivity testing for OpenAI in the settings module.
  - Aligned Document Extraction settings UI in the Portal with OpenAI backend capabilities.
  - Added persistent configuration for OpenAI model (gpt-4o-mini / gpt-4o).
  - Applied database migration to support unified extraction settings.
  - **PDF Support**: Integrated **PdfiumViewer** for server-side rasterization of PDF documents (POC: first page only).
  - **Validation**: Confirmed successful extraction from the real business PDF `C:\dev\FP TA2026 3753.pdf` with accurate mapping of Supplier, Totals, and Line Items.
  - Ensured secure, server-side-only API key resolution from environment variables.

### Fixed

- **Document Extraction Settings UI**: Resolved a "blank page" issue by refactoring the React component to ensure the layout and error feedback render even if settings are not yet loaded.
- **Document Extraction Settings API**: Fixed an HTTP 500 error ("Invalid column name 'CreatedAtUtc'") by creating and applying a missing database migration to sync the `DocumentExtractionSettings` table with the updated entity model.
- **Area Approval**: Fixed a 403 Forbidden error during Area Approval by ensuring the `X-User-Mode` header is correctly sent in the approval API request.
- **OCR Integration (Part 2)**: Resolved 400 Bad Request errors by implementing dynamic MIME type mapping (PDF/PNG/JPEG) in the backend and added frontend null-safety to prevent crashes on partial extraction data.
- **Frontend Guidance**: Updated `getRequestGuidance` to reflect the new Area Approval requirements for winner selection.

### Changed

- **Module Naming**: Standardized the procurement module label from "Itens do Pedido" to **"Gestão de Cotações"** across the UI and documentation.

## [v1.1.48] - 2026-03-23

### Added

- **Receiving Workspace Organization**: Implemented grouped, collapsible sections (Aguardando, Acompanhamento, Recebidos) to improve operational visibility.
- **CollapsibleSection Component**: New reusable UI component with `framer-motion` for smooth transitions and brutalist styling.
- **Search Auto-Expand**: Search functionality now automatically expands sections with matching results.
- **Extended Visibility**: `COMPLETED` requests are now visible in the Receiving workspace under the "Recebidos" section.

## [v1.1.45] - 2026-03-23

### Fixed

- **Status Badge Legibility**: Standardized status and action badges across the system to ensure WCAG AA contrast compliance.
  - Moved from "10% opacity" backgrounds to solid colors with high-contrast text.
  - Enforced dark text (`#111827`) on light backgrounds (Yellow/Amber/Orange), specifically fixing the unreadable `PENDENTE` badge in the Receiving workspace.
  - Centralized styling in `globals.css` with semantic classes (`.badge-success`, `.badge-warning`, etc.) to replace inconsistent local inline styles.

- **Status Mapping Correction**: Corrigido o mapeamento do status `IN_FOLLOWUP` (`Em Acompanhamento`) no cabeçalho do pedido. Agora exibe corretamente o responsável como `RECEBIMENTO` e a próxima ação como `AGUARDAR FINALIZACAO DE RECEBIMENTO`.

### Added

- **Messaging Discovery**: Realizado levantamento completo (inventory) de mensagens, banners e modais do frontend para futura inclusão do número do pedido (`REQ-ID`) em comunicações com o usuário.

## [v1.1.42] - 2026-03-22

### Added

- **Receiving UI**: Standardized "Receiving Operation" workspace following the project's Brutalist/Sharp design language.
- **Receiving Modal**: Adopted the standard project modal pattern for registering item reception.

### Fixed

- **Receiving Flow**: Resolved a Database Foreign Key constraint violation on `ActorUserId` in `RequestStatusHistories` by implementing a valid actor resolution logic in the backend.
- **Receiving UI**: Corrected duplicate request number prefixing ("REQ REQ-" -> "REQ-").
- **Backend Persistence**: Ensured `SyncRequestStatusAfterReceivingAsync` is correctly called for all item types, enabling automatic progression to `IN_FOLLOWUP` and `COMPLETED`.

### Changed

- **Styling**: Removed unprocessed Tailwind CSS utility classes from `ReceivingOperation` and `ReceivingModal`.
- **Backend Architecture**: Extracted a reusable `GetActorUserIdAsync` helper in `LineItemsController` to manage consistent user identification in status histories.

- Implemented mandatory state reset for `editingQuotationId`, `draftProformaFiles`, and `quotationDrafts` when starting new manual or OCR flows.
- Improved UI clarity by introducing specific titles for Create mode ("Registrar Nova Cotação") and Edit mode ("Editar Cotação").
- Enforced distinct primary button labeling and coloring (Green for Save, Orange for Update) based on the current mode.

## [v1.1.35] - 2026-03-21

### Fixed

- **Backend: Database Schema Stabilization**:
  - Resolved `Invalid column name 'ProformaAttachmentId'` SQL error by aligning the database schema with the `Quotation` entity model.
  - Implemented `FixQuotationSchema` migration to add missing `ProformaAttachmentId` and `DiscountAmount` columns to the `Quotations` table.
  - Verified stability of `LineItemsController` and `RequestsController` projections after the schema fix.

## [v1.1.34] - 2026-03-21

### Changed

- **UX: Refined Duplicate Supplier Modal**:
  - Refactored the duplicate-supplier warning from an inline card to a proper centered modal overlay with a dark backdrop.
  - Standardized the visual language using the project's "Brutalist" design tokens, aligned with `QuickSupplierModal`.
  - Improved readability by displaying a clear side-by-side context of existing vs. new quotation data.
  - Ensured safe "Cancel/Back" navigation that preserves the current quotation draft's integrity.
  - Implemented with `framer-motion` for smooth, viewport-stable transitions.

## [v1.1.33] - 2026-03-20

### Added

- **Supplier Integration in Quotation (Step 6)**:
  - Integrated proper supplier selection and creation into the quotation flow.
  - **Backend**: Updated `Quotation` entity with `SupplierId` and renamed `SupplierName` to `SupplierNameSnapshot`.
  - **API**: Added `POST /api/v1/lookups/suppliers` for quick creation and updated search endpoints.
  - **Frontend**: Created `QuickSupplierModal` for minimal, non-blocking supplier registration.
  - **UI**: Replaced plain text supplier input with `SupplierAutocomplete` in the quotation review area.
  - **Logic**: Ensured `SupplierNameSnapshot` is populated from the master data at save time, preventing stale data.
  - **OCR**: Integrated OCR-suggested names as initial search hints for the autocomplete.
- **Improved Quotation Visualization (Step 7)**:
  - Redesigned Section A of the Buyer Workspace to list all saved quotations.
  - Implemented expand/collapse detail view for each saved quotation, showing full item lists.
  - Added visual highlighting for the lowest total amount (MENOR VALOR) among quotations in the same currency.
  - Ensured all quotation item fields (Duration, Quantity, Unit Price, Line Total) are correctly persisted and displayed.
- **Manual Quotation Flow Correction (Step 8)**:
  - Fixed UX gap where "Inserir manualmente" failed to open an editable form.
  - Unified the editable draft concept across OCR and Manual modes.
  - Ensured manual mode initializes an empty, actionable draft with a selected supplier requirement.
  - Fixed a critical display bug where 'Total do Item' showed '0,00' in the saved quotation view.

### Added

- **Editable OCR Draft (Step 4)**:
  - Transformed read-only OCR extraction into a locally editable quotation draft.
  - Implemented `OcrDraftDto` and frontend draft state management in `BuyerItemsList.tsx`.
  - Added interactive review UI with editable header fields and line items.
  - Implemented auto-calculation for line totals (quantity * unit price) and grand totals.
  - Added "Add Row" and "Remove Row" capabilities to the OCR draft.
  - Implemented "Restaurar OCR" to revert local edits to original suggested values.
  - Preserved OCR quality score and origin context throughout the editing process.
- **Improved UX**: Removed unused loading states and modernized the extraction review area with better visual affordances.

## [v1.1.31] - 2026-03-20

### Added

- **OCR Service Integration (Step 3)**:
  - Real connection between Portal Gerencial and local OCR service.
  - Backend `IOcrService` and structured DTOs for extraction results.
  - New `POST /api/v1/requests/{id}/ocr-extract` endpoint.
  - Enhanced Buyer Workspace review area displaying header and item suggestions with quality scores.
  - Explicit error handling for service failures (unavailable, timeout, invalid file).

## [v1.1.30] - 2026-03-20

### Added

- **UX: Document Import Preparation (Step 2)**:
  - Structured "Document Import Panel" in Section B for quotation-specific file flow.
  - Implemented a multi-stage progression: select file -> continue -> review.
  - Added a reserved "Review Area Placeholder" to visually prepare for future OCR extraction results.
  - Implemented state isolation between Manual and Import modes.

## [v1.1.29] - 2026-03-20

### Changed

- **UX: Buyer Workspace Reorganization**:
  - Restructured the quotation area in `BuyerItemsList.tsx` into two distinct visual sections: **Section A (Existing)** for documents and items, and **Section B (Add New)** for entry.
  - Implemented explicit mode switching in Section B between **Upload Mode** (for files) and **Manual Entry** (for line items).
  - Added a "Back/Cancel" affordance in the Add New section for better navigation.
  - Improved the visual hierarchy of operational actions in the grouped workspace.
  - Enhanced the layout to support multiple quotations per request as distinct reviewable units.

## [v1.1.28] - 2026-03-19

### Changed

- **UI: Informational Request Header for Buyer Stages**:
  - Replaced actionable header with informational panel during buyer-handled stages (`WAITING_QUOTATION`).
  - Standardized informational labeling: "Responsável atual: Comprador" and "Próxima ação: Pedido em tratamento do comprador".
  - Corrected field editability: "Fornecedor" and Line Items are now read-only for requesters in `WAITING_QUOTATION`.
- **UX: Status-Aware Guidance**: Updated section labels (e.g., "Andamento do Pedido") and content to provide clear workflow guidance to requesters without enabling unauthorized actions.
- **UI: Responsável Atual / Próxima Ação Integration**: Directly integrated workflow guidance into the informational panel for improved transparency during buyer-owned steps.

## [v1.1.27] - 2026-03-19

### Fixed

- **Workflow: Buyer Workspace Item Filtering**: Resolved a critical issue where the "Destination Plant" dropdown was empty in the buyer quotation item form.
- **Backend: Lookup Projection**: Updated `LookupsController.GetPlants` to include `CompanyId` in the API response, enabling correct frontend filtering.
- **Frontend: Type-Safe Filtering**: Standardized `companyId` comparisons in `BuyerItemsList.tsx` and `RequestEdit.tsx` using `Number()` to ensure robust matching between requests and plants.
- **UX: Graceful Empty States**: Improved the "Destination Plant" dropdown in `RequestLineItemForm.tsx` to provide clear feedback when no plants are available for the selected company.

## [v1.1.25] - 2026-03-19

### Changed

- **Architecture: Request/Plant Model Refactor**: Replaced request-level Plant with request-level Company and item-level Destination Plants.
- **Refactor: Data Integrity**: Enforced strict company-plant ownership validation across backend and frontend.
- **UI: Item-Level Plant Allocation**: Line items now allow individual plant selection, while the overall request remains bound to a single legal company.
- **UI: Company Locking**: Prevented company changes on requests with existing line items to ensure data consistency.
- **UI: Requests List Update**: Updated the global requests list and dashboard attention list to display Company information instead of a single request-level plant.

## [v1.1.24] - 2026-03-19

### Added

- **UX: Mandatory Upload Highlight**: Implemented a temporary (5s) red box-shadow highlight for the document upload section/card when a required attachment is missing.
- **UX: Validation Scroll-and-Glow**: Combined the new highlight effect with existing auto-scroll behavior to better guide users to missing mandatory uploads in both Request Edit and Buyer Items workspaces.

## [v1.1.23] - 2026-03-19

### Added

- **Workflow: Request Creation Improvements**: Enhanced the "New Request" flow for better clarity and efficiency.
- **UX: Empty Request Type Default**: The "Tipo de Pedido" field now starts empty, requiring explicit selection and providing inline validation to prevent accidental submissions.
- **Workflow: Quotation Auto-Submission**: New "Cotação" requests are now automatically submitted upon creation, transitioning directly to the `WAITING_QUOTATION` status and bypassing the manual "Draft" step.
- **UI: Dynamic Action Button**: The primary action button in the creation screen now dynamically updates its label based on the selected request type ("CRIAR PEDIDO" for Quotations vs. "CRIAR RASCUNHO" for Payments).

## [v1.1.22] - 2026-03-19

### Changed

- **UI: Unified Request Header**: Refactored the request header into a shared, presentational `RequestActionHeader` component.
- **UI: Create/Edit Consistency**: Applied the new unified header to both `RequestCreate.tsx` and `RequestEdit.tsx`, ensuring identical layout, spacing, and functional alignment across the procurement flow.
- **UI: Visual Hierarchy Refinement**: Prioritized primary actions and request identification while maintaining discrete breadcrumbs and compact status/context badges.
- **UI: Operational Guidance Integration**: Integrated the "Responsável Atual" and "Próxima Ação" strip directly into the header for better workflow transparency.
- **Refactor: Component Decoupling**: Separated business logic from presentation by moving header rendering into a reusable component with explicit slots for status-specific actions.

## [v1.1.21] - 2026-03-19

### Fixed

- **Attachments: Document Duplication Fix**: Resolved a critical bug where uploading a single file resulted in duplicate entries in the UI.
- **Backend: Redundant Processing**: Fixed `AttachmentsController` to prioritize the `files` collection and only fallback to the singular `file` parameter if the collection is empty.
- **Frontend: Payload Optimization**: Updated the API client to stop redundantly appending the first file to both `file` and `files` fields in the FormData.

## [v1.1.20] - 2026-03-19

### Changed

- **Dashboard: Interactive Workflow Polish**: Applied a final visual polish pass to the "Workflow Interativo" section.
- **UI: Visual Hierarchy Refinement**: Increased the visual presence of non-selected stages for better overall readability without competing with the selected stage.
- **UI: Explanatory Text Improvement**: Enhanced the readability of the "Reajuste" explanatory text with improved contrast and sizing.
- **UI: Layout Breathing Room**: Added vertical spacing to the workflow details panel, improving the elegance and scanability of stage information.
- **UI: Connector Refinement**: Refined stage connectors with lighter markers for a more professional, balanced look.

## [v1.1.20] - 2026-03-24

### Added

- **Quotation Winner Selection**: Implemented the ability to select the winning commercial offer within a request.
  - **Database Integration**: Added tracking toggle (`IsSelected`) globally in `Quotation`, backed up by a structural script `AddQuotationIsSelected`.
  - **Single Selection Target**: A dedicated endpoint `/requests/{id}/quotations/{quotationId}/select` automatically overwrites historical selections, preserving a clean idempotent state.
  - **UI Indicators**: Reusing existing quotation components, `BuyerItemsList` enforces strict badges distinguishing `COTAÇÃO ESCOLHIDA` selectively against lowest-value hints.
- **Quotation Missing Proforma Integrity**:
  - Restored proactive tracking and conditional uploading logic for the `PROFORMA` attachment to trigger identically alongside OCR and MANUAL subroutines upon Quotation Save.
  - Secured `CONCLUIR COTAÇÃO` logic to disable strictly without collapsing adjacent auxiliary states.

## [v1.1.19] - 2026-03-19

### Changed

- **Dashboard: Interactive Workflow Refinement**: Improved the "Workflow Interativo" section to clearly distinguish between the main purchasing path and the return/adjustment flow.
- **Dashboard: Role Identification**: Added clear role labels (`Solicitante`, `Comprador`, `Aprovador Área`, `Aprovador Final`, `Processo`) to each workflow step.
- **Dashboard: Enhanced Details Panel**: Refined the details panel with chips for documentation, a prominent "Next Step" card, and specialized amber styling for return concepts.
- **Dashboard: Responsive Workflow**: Reorganized the workflow component to maintain readability and stability on all screen sizes via adaptive scrolling.

## [v1.1.18] - 2026-03-19

### Added

- **Dashboard: Attention List**: New section surfacing the Top 5 requests needing immediate action based on system urgency rules.
- **Urgency Integration**: The dashboard attention list strictly replicates the urgency highlighting (Yellow/Orange/Red) used in the main requests list.
- **Improved Dashboard Hierarchy**: Balanced layout between summary cards, quick actions, and the new attention-focused operational list.

## [v1.1.17] - 2026-03-19

### Added

- **Dashboard: Quick Actions**: New operational shortcut section featuring "Novo Pedido", "Ver Pedidos", and "Ver Itens do Pedido".
- **Dashboard: UI Tiles**: Implemented responsive action tiles with brutalist aesthetics and hover-elevated visual signals.

## [v1.1.16] - 2026-03-18

### Added

- **Dashboard: Actionable Summary Cards**: Operational cards on the dashboard are now clickable shortcuts that navigate to pre-filtered views.
- **Workflow: "Em Atenção" Filter**: Implemented a new system-wide urgency filter that identifies requests nearing their "Need By Date" (Overdue, Today, or within 3 days).
- **Backend: Urgency Filtering API**: Integrated `isAttention` support into the `RequestsController` for server-side urgency identification.

### Changed

- **UI: Hover Affordances**: Added elevation and shadow effects to dashboard cards to indicate clickability.
- **Filtering: Status Code Translation**: The Requests List now supports `statusCodes` URL parameters, automatically mapping them to internal database IDs for a seamless navigation-to-filter experience.

## [v1.1.15] - 2026-03-18

### Added

- **Dashboard de Compras**: New entry page for the Compras module at `/dashboard`.
- **Operational Summary**: Real-time cards for total requests, pending quotations, area/final approvals, reworks, and urgent items.
- **Interactive Workflow Guide**: Educational visualization of the purchasing process with detailed stage explanations and responsibility mapping.
- **Backend Summary API**: New `GET /api/v1/requests/summary` endpoint for efficient dashboard data aggregation.

### Fixed

- **Unused Imports**: Cleaned up minor lint warnings in new dashboard components.

## [v1.1.14] - 2026-03-18

### Refactor

- **UI: Sidebar Layout Refinement**: Converted the sidebar into a full-height independent container with its own internal scroll management.
- **UI: Two-Zone Sidebar**: Split the sidebar into a scrollable main navigation area and a fixed bottom area for system actions (Configurações, Sair).
- **UI: Logout Rename**: Renamed the logout button from "Sair Sistemicamente" to "Sair" for a cleaner interface.

## [v1.1.13] - 2026-03-18

### Added

- **UI: Modular Navigation Refactor**: Refactored the application sidebar into a modular structure. Features are now organized by business module (e.g., "Compras"), improving visual hierarchy and scalability.
- **UI: Collapsible Module Groups**: Introduced expandable/collapsible containers for modules. Groups auto-expand based on the current active route and support manual toggling.
- **UI: Differentiated Active States**: Implemented a two-tier highlighting system: parent groups show a subtle "group active" state when a child is active, while the specific child link receives the "strong" primary highlight and brutalist shadow.
- **Logic: Scalable Menu Configuration**: The navigation is now driven by a centralized `MENU_ITEMS` configuration supporting link, group, and action types.
- **Accessibility**: Added `aria-expanded` and standard keyboard navigation support for modular groups.

## [v1.1.12] - 2026-03-18

### Fixed

- **BuyerItemsList: Multi-file Proforma Display**: The buyer request-items screen now renders all uploaded Proforma files as a vertical list. Previously only the most recent file was shown. Each file has its own Download and Remove button.
- **BuyerItemsList: Native Confirm Dialog Removed**: Replaced `window.confirm` on file removal with a branded inline confirmation modal consistent with the project's design system. Shows the file name and requires explicit confirmation before deleting.
- **Backend: Full Proforma Attachment List**: The `LineItemsController` now returns a `proformaAttachments` array (all non-deleted Proforma files) alongside the legacy single `proformaId`/`proformaFileName` fields for backward compatibility.

## [v1.1.11] - 2026-03-18

### Changed

- **Global: Multi-file Document Upload**: All document upload fields now support selecting and uploading multiple files at once. Applies to `RequestAttachments` (request detail) and the Proforma upload in `BuyerItemsList`.
- **Global: Upload Scroll-Jump Fix**: Removed the `disabled` attribute from file inputs during upload, eliminating the browser focus-loss that caused unexpected scroll-to-top behavior. Upload is now blocked via an early return in the handler instead.
- **Backend: Aggregated Upload History**: When multiple files are uploaded in a single action, a single aggregated history entry is created (e.g., "3 documentos do tipo Proforma adicionados…") to keep the request history clean and uncluttered.
- **Backend: Backward-Compatible Multi-Upload**: The attachment upload endpoint now accepts both `files` (array) and `file` (singular) form parameters, ensuring full forward and backward compatibility.

## [v1.1.10] - 2026-03-18

### Added

- **Workflow: Adjustment Request Workflow**: Enhanced the request adjustment process so buyers can fully correct requests when returned by approvers.
- **Backend: Persistent Adjustment metadata**: The backend now strictly requires a justification when Area/Final approvers request an adjustment, securely storing the reason and actor details within `RequestStatusHistory`.
- **Frontend: Buyer Correction Capabilities**: When a request enters `REAJUSTE A.A` or `REAJUSTE A.F`, the `BuyerItemsList` screen now displays a prominent alert block showing the approver's justification, role, and date. It also unlocks full item editing and document deletion capabilities, while preserving existing business validations upon resubmission.

## [v1.1.9] - 2026-03-18

### Added

- Improved filtering UX on the Requests list with grouped multi-select dropdowns for Status, Type, Plant, and Department.
- Added quick workflow chips (Financeiro, Em Cotação, Em Aprovação, etc.) mapping directly to status filters.
- Implemented persistent URL query string synchronization for all filters.
- Added visual active filter tags with clear options and a dedicated empty state message.

## [v1.1.8] - 2026-03-18

### Changed

- **UX: Buyer Items List Improvements**: Addressed 3 UX issues when editing/deleting QUOTATION items: (1) Replaced browser-native `window.confirm` with project-standard `ApprovalModal` for item deletion; (2) Rebalanced table column widths and added explicit "AÇÕES" header so edit/delete buttons have stable placement; (3) Added scoped inline field validation (`fieldErrors`) to the manual edit item flow so missing fields highlight in red immediately.

## [v1.1.7] - 2026-03-18

### Added

- **Feature: Buyer Item Edit/Delete in BuyerItemsList**: QUOTATION requests in `WAITING_QUOTATION` now show per-item Edit (✏) and Delete (🗑) action buttons in BUYER mode. Clicking Edit opens the shared `RequestLineItemForm` inline pre-filled with the item's current values; Save calls `PUT /api/v1/requests/{id}/line-items/{itemId}` and Delete calls `DELETE`. PAYMENT-specific behavior unchanged.
- **Backend: Richer LineItemDetailsDto projection**: Added `ItemPriority`, `LineNumber`, and `UnitCode` to the `LineItemsController` list projection so the buyer edit form pre-fills correctly.

## [v1.1.6] - 2026-03-18

### Added

- **Fix: Duplicate Request Post-Launch Corrections**: Fixed 4 issues found after initial release: (1) request number now uses the correct `REQ-dd/MM/yyyy-xxx` project format instead of an alternate legacy format; (2) "Necessário até" date is intentionally cleared in duplicated requests so users must re-enter a new deadline; (3) copied QUOTATION request items no longer carry inherited `SupplierId`/`SupplierName` from the source, so cancellation rules and buyer workspace editability behave correctly; (4) verified PAYMENT request supplier inheritance is still preserved for those request types.
- **Feature: Duplicate Request**: Implement a new backend endpoint and frontend action to allow users to safely duplicate an existing request. This spawns a completely fresh `DRAFT` request mapping only core structural fields (e.g. description, items, quantities) while explicitly discarding legacy workflow state, participants, and timeline history.
- **Workflow: Controlled Request Cancellation**: Implemented a comprehensive request cancellation flow allowing users to cancel a request if no meaningful operational processing has occurred.
- **Backend: Strict Cancellation Rules**: Added `POST /api/v1/requests/{id}/cancel` endpoint that blocks `QUOTATION` cancellation if there are uploaded Proformas, Supplier assignments, or item processing. Blocks `PAYMENT` cancellation if operational docs exist (`PO`, `PAYMENT_SCHEDULE`, `PAYMENT_PROOF`).
- **UI: Cancel Request Integration**: Integrated a "Cancelar Pedido" action into the `RequestEdit` screen (for requesters) and the `BuyerItemsList` workspace (for buyers), rendering only when conditions allow.
- **Audit: Cancellation History**: Requires a mandatory cancellation reason from the user, logged cleanly into the Request History.

## [v1.1.5] - 2026-03-18

### Improved

- **DateInput**: Restored calendar picker functionality while maintaining the forced `DD/MM/YYYY` display format and manual typing support.

## [v1.1.4] - 2026-03-18

### Fixed

- **Timeline**: Stage completion logic for finalized requests. Predecessor stages are now correctly marked as completed.

### Added

- **UI**: A specialized "finish flag" icon for the final stage of the request timeline.

### Improved

- **UX**: Financial attachments (Payment Schedule and Proof) now remain visible for consultation in finalized requests (`COMPLETED` and `QUOTATION_COMPLETED`).

## [v1.1.3] - 2026-03-18

### Added

- **Workflow: Last-Item Receipt Auto-Completion**: Enhanced the Buyer Items workspace to automatically complete requests when the last pending item is marked as RECEIVED.
- **UI: Receipt Warning Modal**: Implemented a specialized warning in the `ApprovalModal` when marking the final item of a request, including a clear call-to-action: "OK, ENCERRAR PEDIDO, TODOS ITENS RECEBIDOS".
- **Backend: Authoritative Completion Logic**: Added `check-last-pending` endpoint and updated status change logic to ensure requests are closed only when no items are left in "pending" states (not RECEIVED and not CANCELLED).
- **Audit: Automatic Closure History**: The system now records a request-level history event when a request is auto-completed, providing a clear audit trail.

## [v1.1.2] - 2026-03-17

### Fixed

- **UI: Buyer Items Blank Screen**: Fixed a critical regression where the Buyer Items list (`/buyer/items`) would render a blank screen due to a missing `formatDate` import.
- **UI: Date Input Inconsistency**: Replaced native browser date inputs with a custom `DateInput` component to enforce the `DD/MM/YYYY` project standard across Request forms, resolving the `mm/dd/yyyy` placeholder inconsistency.
- **Refactoring: UTC Date Standardization**: Standardized `formatDateTime` and `formatTime` in `lib/utils.ts` to use UTC methods, ensuring visual consistency with `formatDate` and preventing timezone-related display shifts.
- **Logic: Consistent Urgency Calculation**: Updated `getUrgencyStyle` to use UTC-based day normalization for robust overdue/today calculations across different user timezones.
- **Workflow: Payment Proof Accessibility in PO_ISSUED**: Fixed a remaining inconsistency where `PAYMENT_PROOF` upload was blocked by backend validation in the `PO_ISSUED` status. Aligning both frontend and backend to allow same-day payment confirmation.

## [v1.1.1] - 2026-03-20

### Changed

- **UI: Date Format Standardization**: Standardized all user-facing date displays to `DD/MM/YYYY` format using centralized utility functions (`formatDate`, `formatTime`, `formatDateTime`).
- **Refactoring: Centralized Date Utils**: Created `src/frontend/src/lib/utils.ts` to host shared date formatting logic, replacing inconsistent `toLocaleDateString` and `toLocaleString` usage across the portal.
- **UI: Consistent Fallbacks**: Implemented safe fallbacks (`-`) for null, undefined, or invalid dates in all standardized fields.

### Fixed

- **UI: Inconsistent Date Rendering**: Resolved disparate date formatting in Buyer Items List, Request Timeline, Request History, and Attachments.

## [v1.1.0] - 2026-03-18

### Added

- **Workflow: Post-Payment Follow-up**: Implemented the buyer follow-up phase after payment is completed.
- **Request Status**: Added `IN_FOLLOWUP` ("Em Acompanhamento") status for requests that have started the post-payment delivery cycle.
- **Line Item Status**: Added `WAITING_ORDER` ("Aguardando Encomenda") as the initial post-payment status for line items.
- **Workflow: Mandatory Status Observations**: Enforced mandatory human-readable observations for line item status changes to `ORDERED`, `PARTIALLY_RECEIVED`, and `RECEIVED`.
- **UI: Standardized Approval Modal for Items**: Reused the `ApprovalModal` pattern to collect mandatory observations for item status updates in the Buyer workspace.
- **Backend: Item-Level Audit History**: Enhanced `RequestStatusHistory` to record fine-grained item status changes including item identification, status transitions, and mandatory buyer comments.
- **UI: Portal-Based Dropdowns**: Implemented React Portals for Supplier and Cost Center autocompletes to resolve layout breakage in scrollable table containers.
- **UI: Shared Dropdown Logic**: Created `useDropdownPosition` hook to handle fixed-position overlays with automatic scroll-parent tracking and viewport safety.

### Fixed

- **Stability**: Resolved database foreign key violation when recording item status history by implementing robust user resolution and adding missing seed data.

## [v1.0.4] - 2026-03-17

### Fixed

- **Buyer Workspace: Zero-Item Visibility**: Fixed a regression where `QUOTATION` requests with zero line items were not appearing in the Buyer Items workspace. The backend query now uses a Left Join to ensure all relevant requests are visible, even if they don't have items yet.
- **DTO: Nullable LineItemId**: Updated `LineItemDetailsDto` to make `LineItemId` nullable, allowing explicit representation of empty requests in the workspace.

## [v1.0.3] - 2026-03-17

### Fixed

- **Workflow: Conditional Item Validation**: Corrected the minimum line-item validation to allow **QUOTATION** requests to be submitted without items. **PAYMENT** requests continue to require at least one item as per business rules.

## [v1.0.2] - 2026-03-17

### Added

- **Buyer Workspace: Integrated Quotation Completion**: Added the "Concluir Cotação" operational action directly to the expanded request view in `BuyerItemsList.tsx`, allowing buyers to complete the step without navigating away.
- **Workflow: Shared Quotation Utility**: Created `src/frontend/src/lib/workflow.ts` to encapsulate identical validation and transition logic for `BuyerItemsList` and `RequestEdit`.
- **Backend: Request-Level Supplier Assignment**: Added a specialized `PATCH /api/v1/requests/{id}/supplier` endpoint to allow buyers to assign a supplier to the entire request directly from the items workspace.
- **Backend: Enhanced DTOs**: Updated `LineItemDetailsDto` to include parent request supplier metadata for better frontend context.

### Changed

- **Refactoring**: Updated `RequestEdit.tsx` to utilize the shared `completeQuotationAction` utility, ensuring consistent validation behavior across the portal.

## [v0.9.7] - 2026-03-17

### Fixed

- **Workflow:- Refined `QUOTATION` post-create redirection to list view.
- Implemented `PAYMENT` post-create redirection with `focus=items` signal (auto-scroll + pulse highlight).
- **Workflow: Mandatory Attachment Enforcement**: Implemented strict validation for documents on workflow transitions (Proforma, P.O, Payment Schedule, Payment Proof).
- **Audit History**: Added localized labels for document and item actions in the request timeline/history.
- Updated `LineItemDetailsDto` with `RequesterName` and `NeedByDateUtc` to provide better buyer context.
- Enhanced `BuyerItemsList` with a stronger visual hierarchy, separating quotation data from line items.
- Added visual section containers and refined expanded row headers in the Buyer Workspace.
- Fixed a regression in `RequestEdit.tsx` regarding duplicate items sections and cleaned up imports.
- Decoupled attachment management permission from line-item edit permission (consolidated fix).
 request reaches a terminal state (COMPLETED, REJECTED, CANCELLED).

## [v0.8.0] - 2026-03-06

### Added

- **Grouped Workspace UX**: `Itens do Pedido` now groups line items by Request for better context.
- **High-Visibility Action Badges**: New "AÇÃO NECESSÁRIA" badges in the line items grid to guide buyers.
- **Inline Proforma Management**: Buyers can now upload, view, and replace Proforma documents directly from the line items workspace.
  - **Auto-Calculation**: The `lineTotal` for each item and the "Grand Total" are automatically derived from `quantity * unitPrice`, ensuring mathematical consistency during human review.
  - **Draft Management**: Buyers can add new manual rows, remove suggested rows, or use the "Restaurar OCR" action to discard all edits and revert to the original extraction suggestions.
- **Inline Item Creation**: Ability to add items to an existing request directly from the line items grid.

### Changed

- **Server-Side Visibility Enforcement**: `GET /api/v1/line-items` now strictly filters by Request Status (WAITING_QUOTATION, AREA_ADJUSTMENT, FINAL_ADJUSTMENT, PAYMENT_COMPLETED).
- **Conditional Validation for Quotation**: `Cotação` (QUOTATION) type requests can now be saved as drafts without line items.
- **Payment Validation Preserved**: `Pagamento` (PAYMENT) type requests still require at least one line item before saving.
- **Redirection Logic**: Creating a `QUOTATION` now redirects to the list after saving the header, instead of forcing item creation immediately.

## [v1.0.1] - 2026-03-06

### Added

- **UI: Gestão de Cotações Grid**: Upgraded the dedicated "Gestão de Cotações" (formerly "Itens do Pedido") management screen (`/buyer/items`). It now displays a flat, data-rich grid of all line items rather than a list of requests.
- **Workflow / Permissions**: Added a temporary `X-User-Mode` role switch (Comprador vs. Aprovador de Área) to the Gestão de Cotações screen. Cost Centers are read-only for buyers, but editable for Approvers. Item Status and Supplier are editable for buyers, but read-only for Approvers.
- **UI: Centro de Custos**: Added the Cost Centers tab to the central `MasterData.tsx` screen, supporting full CRUD operations aligned with the project's brutalist aesthetic.
- **Backend: Line Items Grid Filtering**: The global grid (`/buyer/items`) is now server-side restricted. It only surfaces items associated with requests in `WAITING_QUOTATION`, `AREA_ADJUSTMENT`, `FINAL_ADJUSTMENT`, or `PAYMENT_COMPLETED` statuses to focus buyer and approver attention on actionable tasks.
- **Backend: Lookups API**: Added new lookup endpoints for `CostCenters` (GET search, PUT create/update, toggle active) and generated corresponding EF Core structure `AddCostCenterAndSupplierKeys`.
- **Backend: LineItems API**: Added `LineItemsController` facilitating paginated grid retrieval and role-gated PATCH operations for Status, Supplier, and Cost Center updates.

### Changed

- **Workflow: Centralized Line Item Auto-Sync**: Line item statuses now auto-sync automatically upon parent request transition (e.g. `APPROVED` -> items become `PENDING`). Explicitly preserves advanced statuses (`RECEIVED`, `PARTIALLY_RECEIVED`, `ORDERED`, `CANCELLED`) to prevent overwriting manual progress.
- **Workflow: Quotation Completion Logic**: `CompleteQuotation` now transitions directly to `WAITING_AREA_APPROVAL`. `QUOTATION_COMPLETED` has been deactivated as a persistent state in the database, streamlining the quotation workflow.
- **Workflow: Payment Enforcement Rules**: The Area and Final Approval stages now strictly enforce an Approve or Reject paradigm for `PAYMENT` requests. Backend and Frontend have been hardened to hide and block the "Solicitar Reajuste" action for `PAYMENT` types, ensuring they maintain a linear path to final approval.
- **UX: Restoration of Item Highlight**: Restored the auto-scroll and red highlight effect for `PAYMENT` requests when saved without items, improving guidance.
- **UX: Quotation Guidance**: Added informative success messages for `QUOTATION` creation and saving to orient users toward the new grouped item management workspace.

## [v1.0.0] - 2026-03-03

### Added

- **UI: Official Logo Integration**: Replaced the initial text-based branding with the official Alpla brand logo asset.
- **UX: Status-Aware Guidance Banner**: Replaced generic "non-editable" alerts with a persistent, context-aware guidance banner in `RequestEdit.tsx`.
- **Logic: Precision Editability Booleans**: Implemented `isDraftEditable`, `isQuotationPartiallyEditable`, and `isFullyReadOnly` to ensure UI affordances always match the stated request stage.
  - **Quotation Isolation**: Buyers now see clear guidance that Supplier and Items remain editable during `WAITING_QUOTATION`, while the rest of the header is locked.
- **Workflow: Consolidated Start of Process**: Unified the post-creation UX so that regardless of Request Type (Payment or Quotation), the user remains on the Edit screen to add items and attachments immediately.
- **Backend: Item Requirement Enforcement**: Updated SubmitRequest to strictly require at least one line item for both PAYMENT and QUOTATION types before entering the official workflow.
- **Workflow: Financial Attachment Rule Alignment**: Unified `PAYMENT_SCHEDULE` and `PAYMENT_PROOF` rules for both `QUOTATION` and `PAYMENT` requests, with status-specific enforcement.

### Fixed

- **UI: Misleading Messaging**: Removed the contradictory "Este pedido não pode mais ser editado" message that was appearing during the quotation stage.

### Improved

- **Stage 1**: Restored Requests List timeline rendering (missing `RequestTimelineInline` component).
- **Stage 2**: Aligned Operational Actions (REGISTER_PO, SCHEDULE_PAYMENT, COMPLETE_PAYMENT, MOVE_TO_RECEIPT, FINALIZE) between frontend and backend.
- **Stage 3**: Hardened Attachment Workflow enforcement based on Request Type and Status.
- **Stage 4**: Corrected Quotation-Stage Edit UX with status-aware guidance banners and precise locking.
- **Stage 5**: Aligned Frontend/Backend data contracts (itemPriority string union, ID consistency, missing DTO fields).
- **Backend: Item Audit History**: Implemented detailed status history recording for line item mutations (ITEM_ADICIONADO, ITEM_ALTERADO, ITEM_REMOVIDO) to ensure a full audit trail of procurement activities.

### Changed

- **Frontend: Quotation Stage Editability**: Refined access rules for the WAITING_QUOTATION stage. Header fields are now read-only to preserve the original requester's intent, while Line Items and Attachments remain fully editable for the Buyer.
- **UI: Action Button Standardization**: Renamed the primary action in the quotation stage to **CONCLUIR COTAÇÃO E ENVIAR PARA APROVAÇÃO** to clearly represent the workflow transition.

## [v0.9.21] - 2026-03-03

### Added

- **Workflow: Multi-Factor Attachment Enforcement**: Implemented strict gating for document uploads and deletions based on request type, status, and attachment type.
  - **Type Isolation (Corrected in Stage 5)**: Originally, sections for `PAYMENT_SCHEDULE` and `PAYMENT_PROOF` were hidden for `QUOTATION` requests. This was **overturned** to unify the financial flow.
  - **Stage Gating**: Upload and delete actions are conditionally disabled/hidden if the current workflow stage doesn't authorize that specific document type (e.g., `PROFORMA` is locked after quotation, and `P.O.` deletion is locked after issuance).
  - **Backend Hardening**: Updated `AttachmentsController` to reject invalid upload attempts with clear business-rule violation messages.

### Fixed

- **UI: Attachment UX**: Improved guidance by hiding misleading empty upload areas and restricted delete affordances in non-deletable stages.

## [v0.9.20] - 2026-03-03

### Fixed

- **Workflow: Operational Action Gating**: Aligned the operational action bar visibility in the frontend with backend business rules.
  - Implemented precision booleans (`canSchedulePayment`, `canMoveToReceipt`, etc.) for maintainable visibility logic.
  - **Payment flow (Corrected in Stage 5)**: Originally restricted `SchedulePayment` and `CompletePayment` actions strictly to `PAYMENT` type requests. This is now unified for all types.
  - **Quotation flow (Corrected in Stage 5)**: Originally enabled `MOVE_TO_RECEIPT` directly from `PO_ISSUED` for `QUOTATION` requests. This bypass was **removed** to ensure full financial traceability.

### Hardened

- **Backend: Permission Hardening**: Updated `RequestsController` to strictly enforce request type checks for operational payment transitions. `MoveToReceipt` now validates status correctly by request type.

## [v0.9.19] - 2026-03-03

### Changed

- **UI: Horizontal Visual Timeline**: Redesigned the request timeline into a high-fidelity horizontal visual workflow.
  - Implemented segmented connectors between stages.
  - Added visual distinction for Completed (green check), Current (pulsing blue), and Pending (neutral num) states.
  - Enhanced responsive behavior with horizontal overflow scrolling and fixed-width label containers.
  - Improved typography and hierarchy, keeping timestamps subordinate to status labels.

## [v0.9.18] - 2026-03-03

### Added

- **UI: Request Status Timeline**: Implemented an expandable row in the requests list that displays a horizontal timeline of status progression.
- **Backend: Timeline API**: Created a dedicated `GET /api/v1/requests/{id}/timeline` endpoint providing lightweight, stage-ordered data for the frontend timeline component.
- **Frontend: Lazy-Loading Timeline**: Added `RequestTimelineInline` component that fetches data only when a row is expanded, optimizing performance for large lists.

## [v0.9.17] - 2026-03-03

### Changed

- **Workflow: Quotation Supplier Editability**: Implemented a surgical permission exception for the **Fornecedor** (Supplier) field in the `WAITING_QUOTATION` stage. This allows Buyers to fulfill quotation requirements before completion while keeping the rest of the header locked to protect original requester intent.
- **Backend: Surgical Header Protection**: Updated `RequestsController` to restrict draft updates in the quotation stage strictly to the `SupplierId` field, rejecting unauthorized changes to other header metadata.

## [v0.9.16] - 2026-03-02

### Added

## [v0.9.15] - 2026-03-02

### Added

- **Workflow: Attachment Change Detection**: Implemented logic to ensure that document uploads, removals, and replacements are recognized as meaningful request modifications. This allows users to proceed through workflow stages (e.g., "Concluir Cotação" or "Reenviar Pedido") after only modifying attachments, while still maintaining "no-op" protection for completely unchanged requests.

### Changed

- **Backend: Metadata Synchronization**: Updated `AttachmentsController` to automatically refresh the parent request's `UpdatedAtUtc` and `UpdatedByUserId` whenever an attachment is modified, ensuring backend transition guards are satisfied.
- **Frontend: Status Persistence**: Enhanced `RequestEdit.tsx` to track attachment-driven changes, enabling workflow actions immediately after upload without requiring manual saves or field edits.

## [v0.9.14] - 2026-03-01

### Fixed

- **UI: Request Edit Structural Repair**: Corrected a major JSX corruption in`RequestEdit.tsx`where the main form was incorrectly nested inside the sticky header container. This was causing layout breaks and "detached" header behavior during scroll.
- **UI: Sticky Header Separation**: Refactored the header into a clean, standalone sticky unit containing only Banners, Breadcrumbs, Title, and Action Bars. The form guidance and data cards now scroll independently as standard content.

### Added

- **Workflow: Contextual Submission Wording**: Updated the submission confirmation modal to distinguish between new requests (**Confirmar Envio**) and requests returned for rework (**Confirmar Reenvio**).
- **Attachments: Metadata & Auditability**:
  - Enhanced the `RequestAttachments` UI to display uploader information ("Enviado por [Nome] em [Data/Hora]").
  - Implemented history recording for document deletions (**DOCUMENTO REMOVIDO**), complementing the existing upload history.
  - Verified and ensured robust file download support across the portal.
- **Logic: Flexible Proforma Rule**: Confirmed the implementation where`QUOTATION` requests no longer strictly require a Proforma for the *initial* submission, while maintaining the requirement for `PAYMENT` and `COMPLETE_QUOTATION`.

## [v0.9.13] - 2026-03-01

### Added

- **Workflow: Post-Approval Operational Stages (PAYMENT)**: Implemented the complete operational lifecycle for requests after final approval.
  - New transitions: `APPROVED` -> `PO_ISSUED` (Branching stage).
  - **Branching Stage (`PO_ISSUED`)**: Financeiro can now choose between direct payment (**PAGAR**) which moves to`PAYMENT_COMPLETED`, or scheduled payment (**AGENDAR PAGAMENTO**) which moves to`PAYMENT_SCHEDULED`.
  - **Unified Flow (Updated in Stage 5)**: This post-approval branch now applies to **both** `PAYMENT` and `QUOTATION` request types.
  - Simplified path: Removed the intermediate`PAYMENT_REQUEST_SENT`status.
  - Consistent convergence: All branches lead to `WAITING_RECEIPT` -> `COMPLETED`.
  - Updated`isFinalizedStatus` to treat `COMPLETED` as the end-state, maintaining visibility/urgency for `APPROVED`requests.
  - **Fixed: Unified Request Identification**: Refactored request numbering to a single format: `REQ-DD/MM/YYYY-SequentialNumber`. Replaced old `PAG-` and `COM-` prefixes.
  - **Feature: Global Monotonic Counter**: Implemented`GLOBAL_REQUEST_COUNTER`to ensure the sequential number is never reused, even if requests are deleted. Deletions no longer affect the next assigned number.
  - **Environment: Transactional Data Cleanup**: Safely deleted all existing request transactional data (`Requests`, `Items`, `History`, `Attachments`) to provide a clean state for the new numbering sequence.
  - **Fixed**: Added "real change" and **mandatory item presence** (at least one item required) to the **Concluir Cotação** action. Header-only changes are no longer sufficient to progress.
  - **Logic: Quotation Item Management**: Fixed a blockade that prevented adding, editing, or deleting line items in the `WAITING_QUOTATION` status. This status is now explicitly included in the allowed mutation guards alongside `DRAFT` and Rework statuses.
  - **UI/UX: Modal-Driven Feedback**: Implemented`modalFeedback` in `RequestEdit.tsx`to ensure validation errors from modal-driven actions (like approvals or quotation completion) are displayed directly within the modal context instead of hidden behind the overlay.
  - **Fixed: PO Action Visibility (Unified in Stage 5)**: Unified the "AÇÕES OPERACIONAIS" bar for both `PAYMENT` and `QUOTATION` post-approval. All financial and operational actions now allow both types in the correct status sequence.
  - **Guidance Update**: Made`getRequestGuidance` type-aware for the `PO_ISSUED`status, providing specific instructions for each request type.
- **Fixed: Quotation C  - **Actions**: Hide "Concluir Cotação" and all operational CTAs for the requester in these stages.
  - **Editability**: Ensure the "Fornecedor" field (and items) are read-only for the requester during these stages to reflect buyer ownership.
- **Consequences:** Provides a cleaner, safer UX for the requester. Prevents confusion about unauthorized actions or inconsistent field editability while maintaining workflow transparency. Ensures the requester knows the request is being handled by the procurement team.
esolved a 500 error during the "Concluir Cotação" action by seeding the missing `QUOTATION_COMPLETED` status in the database (now used as an internal/audit state if needed).
- **Improved Validation Feedback**: Added unique prefixes to backend validation messages for item operations to ensure clear traceability of business rule violations.
- **Workflow: Workflow-Driven Upload Area**: Implemented a robust document attachment system enforced by workflow transitions.
  - **Typed Attachments**: Added support for`PROFORMA`,`PO`,`PAYMENT_SCHEDULE`, and`PAYMENT_PROOF`.
  - **Transition Validation**: Requests are now blocked from progressing if the required documents for the current stage are missing (e.g., Proforma required for Submission and Quotation Completion).
  - **Unified History Logging**: Attachment events are logged in the request history using the existing UI pattern (`DOCUMENTO ADICIONADO`), preserving the current status and providing clear audit trails.
  - **Deletion Constraints**: Attachments can only be removed in editable stages (`DRAFT`, `AREA_ADJUSTMENT`, `FINAL_ADJUSTMENT`, `WAITING_QUOTATION`) to preserve workflow integrity.
  - **Frontend UI**: Integrated a new`RequestAttachments` component into `RequestEdit.tsx`for managing files, with automatic grouping by document type.

## [v0.9.12] - 2026-03-01

### Fixed

- **Navigation: Voltar Button Route**: Fixed a bug where the "Voltar" button navigated to an invalid URL with a leading space (`/ requests`).
- **Logic: Request List Sorting and Pagination**: Fixed an inconsistency where urgency-based sorting only applied to the current page. Moved the urgency ranking logic (Overdue > Today > Soon) to the backend API.
- **UI: Edge Layout Stability**: Fixed a critical cross-browser rendering issue in Microsoft Edge where the requests page appeared incorrectly scaled/oversized. Corrected by implementing`minmax(0, 1fr)` tracks in the main `AppShell` grid and applying `min-width: 0`constraints across the requests page layout to prevent uncontrolled track expansion.
- **UX Refinement: Requests Table Optimization**: Streamlined the requests list by removing the "Solicitante" and "Data de registro" columns. Reordered columns to place "Comando" (Action) in the first position for immediate accessibility.

- **UX Refinement: Guidance Banner Relocation**: Moved the "Responsável Atual / Próxima Ação" banner from the sticky top stack to a natural content area below the page title in `RequestEdit.tsx`.
- **UX Refinement: Navigation Label Standardization**: Standardized "CANCELAR" button to "VOLTAR" (with back icon) for existing requests to improve navigation clarity.
- **UX Refinement: Request Number Visibility**: Displayed the full Request Number prominently at the top of the header in `RequestEdit.tsx`.
- **UX Refinement: Urgency Highlighting & Sorting**: Implemented status-aware urgency highlighting (excluding finalized requests: APPROVED, REJECTED, CANCELLED).
- **UX Refinement: Critical Sorting**: Updated `RequestsList.tsx` to prioritize high-urgency requests at the top, using `createdAtUtc` as a robust secondary sort key.
- **Workflow Guidance**: Implemented a new guidance system across Request List and Details.
  - Added **Responsável Atual** and **Próxima Ação** fields to the Details page sticky header for better workflow transparency.
  - Added a new **Situação Atual** column to the Requests List identifying ownership and next steps.
- **Urgency Highlight**: Implemented row background highlighting in the Requests List based on the `Necessário Até` deadline.
  - **Light Red**: Overdue.
  - **Light Orange**: Due today.
  - **Light Yellow**: Due within 3 days.
- **Ownership Logic**: Centralized mapping logic in `utils.ts` for consistent role/action derivation across the UI.
  - **Special Rule**: For rework statuses, ownership follows the request type:`PAYMENT` -> Solicitante, `QUOTATION`-> Comprador.

## [v0.9.11] - 2026-03-01

### Standardized

- **Visual Standardization Pass**: Unified the entire request workflow UI (View, Edit, Rework) including banner padding (12px 24px), header button heights (44px/48px), uppercase breadcrumbs/titles, and centered brutalist modals.

## [v0.9.10] - 2026-03-01

### Standardized

- **Request Page UI Hierarchy**: Standardized top-of-page vertical stack (Status Banner > Context Banner > Breadcrumbs > Action Bar).
- **Layout Spacing**: Added 24px top padding to the sticky header container for better breathing room.
- **Branded Modals**: Replaced browser-native confirmation prompts (`window.confirm`) with styled modals for all request mutations and deletion actions.

## [v0.9.8] - 2026-03-01

### Added

- **Rework Change Detection**: Implemented robust detection of "real changes" before allowing resubmission of requests in rework status.
- **Backend Protection**: Server-side validation in `SubmitRequest` to block resubmission if `UpdatedAtUtc` hasn't moved past the last adjustment request.
- **Frontend Validation**:`isDirty` and `hasSavedChanges` tracking in `RequestEdit.tsx`to prevent no-op resubmissions with Portuguese feedback.

### Fixed

- **Rework UI**: Removed the incorrect "Modo de Visualização" (Blue Banner) from requests in rework status.
- **Breadcrumb & Title**: Updated UI titles to reflect "Reajustar Pedido" when in rework mode.

## [v0.9.7] - 2026-03-01

### Added

- **Request Rework Workflow**:
  - Requesters can now edit and resubmit requests returned for adjustment (`AREA_ADJUSTMENT`,`FINAL_ADJUSTMENT`).
  - Added Portuguese guidance banner for rework statuses in `RequestEdit.tsx`.
  - Balanced`SubmitRequest`logic (Backend) to handle resubmission paths.
  - Context-aware resubmission buttons and history logging.

## [v0.9.6] - 2026-03-01

### Final Approval Workflow (PAYMENT)

- **Workflow Actions**: Implemented explicit actions for requests in `WAITING_FINAL_APPROVAL` status:
  - **APROVAR**: Transitions to`APPROVED`.
  - **REJEITAR**: Transitions to `REJECTED` (requires comment).
  - **SOLICITAR REAJUSTE**: Transitions to`FINAL_ADJUSTMENT` / `Reajuste A.F`(requires comment).
- **Mutual Exclusion**: Ensured Area and Final approval action bars are never shown simultaneously.
- **Contextual Modals**: Updated`ApprovalModal`to provide stage-specific guidance for Final Approval.
- **Backend API**: Added `ApproveFinal`, `RejectFinal`, and `RequestAdjustmentFinal` endpoints logic with centralized `ProcessFinalApproval` helper.

### Item Form UX Correction

- **Field Defaults**: Mandatory fields `Prioridade` and `Unidade` now strictly start empty (`''`) for new items.
- **Visual Feedback**: Added explicit "Selecione..." placeholders to guide the user.
- **Strict Validation**: Client-side validation now blocks saving items if Priority or Unit are unselected.

## [v0.9.5] - 2026-03-01

### Area Approval Workflow (PAYMENT)

- **Workflow Actions**: Implemented explicit actions for requests in `WAITING_AREA_APPROVAL` status:
  - **APROVAR**: Moves request to`WAITING_FINAL_APPROVAL`.
  - **REJEITAR**: Moves request to `REJECTED` (requires comment).
  - **SOLICITAR REAJUSTE**: Moves request to`AREA_ADJUSTMENT` / `REAJUSTE A.A`(requires comment).
- **Approval UI**: Added a high-fidelity fixed action bar at the bottom of `RequestEdit.tsx` for authorized actions.
- **Interactive Modals**: Implemented confirmation modals with optional/required comments for approval actions.
- **Backend API**: Added new endpoints in `RequestsController` to process area-level decisions and log structured history.
- **Audit Consistency**: History logs and status badges now use standardized Portuguese terminology (e.g.,`Reajuste A.A`).

## [v0.9.4] - 2026-03-01

### View/Read-Only Mode

- **Unified Component**:`RequestEdit.tsx`is re-used for both editing and viewing to maintain layout consistency.
- **Conditional Rendering**: Non-DRAFT requests hide all mutation controls and show a blue `MODO DE VISUALIZAÇÃO` banner at the top.
- **History Timeline**: A dedicated section`Histórico do Pedido`displays chronological audit logs.
- **Navigation**: The primary action button for non-drafts is `VOLTAR`.

## [v0.9.3] - 2026-03-01

- **Feature: Conditional Submission UX & Rules** — Refined the submission workflow based on Request Type.
  - **Conditional Item Requirement**:`PAYMENT` requests now strictly require at least one line item for submission, while `QUOTATION`requests can be submitted with zero items.
  - **Validation surfacing**: Errors are shown inline and the screen auto-scrolls to the Item section if items are missing for PAYMENT requests.
  - **Action button standardization**:`SALVAR MUDANÇAS`,`SUBMETER PEDIDO`,`EXCLUIR RASCUNHO`.
  - **Portuguese UI Standardization**: Applied exact Portuguese strings for all primary actions in the Create/Edit flow:
    - Create Button: **SALVAR PEDIDO**
    - Edit Buttons: **SALVAR MUDANÇAS**, **SUBMETER PEDIDO**, **EXCLUIR RASCUNHO**, **CANCELAR**
  - **Standardized Feedback**:
    - Save Success: **Rascunho salvo com sucesso.**
    - Submit Success (PAYMENT): **Pedido enviado para aprovação da área com sucesso.**
    - Submit Success (QUOTATION): **Pedido enviado para cotação com sucesso.**
  - Header Validation: **Preencha os campos obrigatórios antes de submeter o pedido.**
  - **Improved Guidance**: Added helper text in the items section:
    - QUOTATION: "Itens podem ser adicionados, mas não são obrigatórios para pedidos de cotação."
    - PAYMENT (Empty): "Adicione pelo menos um item para continuar."

## [v0.9.1] - 2026-03-01

- **UI/UX Fix: Submission Flow Hardening** — Resolved critical edge cases in the submission process.
  - **Auto-Save before Submit**: The "Submeter Pedido" action now automatically persists all header changes as a draft before attempting submission, preventing "missing field" errors for unsaved inputs.
  - **Empty Items UX**: If submission is blocked due to no line items, the UI now automatically scrolls to the Items section and pulses a red highlight (5s) for clear visual guidance.
  - **Enhanced Validation Surfacing**: Replaced generic submission errors with detailed, specific validation messages returned from the backend (e.g., missing specific header fields or item constraints).
  - **Backend Validation Expansion**: `SubmitRequest` endpoint now strictly validates mandatory header fields (NeedLevel, Department, Plant, Date, Participants) in addition to item presence.
  - **Seeding Correction**: Fixed missing`SUBMITTED`status in database seeding, ensuring the workflow starts correctly on new environments.

## [v0.9.2] - 2026-02-28

- **Bug Fix (Frontend):** Resolved blank page when clicking "Visualizar" by adding missing`/requests/:id`route.
- **Business Rule (Backend):** Corrected post-submission status transitions:
  -`PAYMENT (PAG)` -> `AGUARDANDO APROVACAO AREA` (Stable code: `WAITING_AREA_APPROVAL`)
  - `QUOTATION (COM)` -> `AGUARDANDO COTACAO` (Stable code: `WAITING_QUOTATION`)
- **UX Improvement:** Updated`RequestEdit`to provide a clear read-only view mode for submitted requests, including contextual titles ("Visualizar Pedido") and hiding draft-specific controls.
- **Audit:** Submission history records now specify the initiated flow (Quotation vs Payment).

## [v0.9.0] - 2026-03-01

- **Feature: Request Submission Workflow** — Transitions requests from `DRAFT` to `SUBMITTED`, initiating the official approval flow.
  - **Backend Submission Logic**: New`POST /api/v1/requests/{id}/submit`endpoint with strict validation (Title, Description, Items, Positive Total).
  - **Conditional Supplier Rule**: Supplier is now strictly required for `PAYMENT` requests but remains optional for `QUOTATION` requests before submission.
  - **Audit & History**: Automatically sets`SubmittedAtUtc` and records a lifecycle event in `RequestStatusHistory`.
  - **Read-Only Enforcement**: All Mutation endpoints (`UpdateDraft`, `AddLineItem`, etc.) now definitively block changes if the request status is not `DRAFT`.
  - **Frontend UI**: Added interactive "Submeter Pedido" button with confirmation and validation.
  - **Post-Submission UX**: Successful submission triggers an automatic redirect to the Requests List with a visual success confirmation.
  - **UI Locking**: All form fields, item actions (Edit/Delete), and header buttons are automatically disabled or hidden when viewing a submitted request.

---

## [v0.8.6] - 2026-02-28

- **UI/UX Refinement: Design System Alignment** — Adjusted the Supplier dropdown's color palette to strictly follow the portal's design system.
  - **Color Correction**: Replaced harsh black/dark tones with the corporate blue (`--color-border-heavy`) for borders and shadows.
  - **Hover Improvements**: Changed result row hover state from black to a subtle gray (`#f3f4f6`), ensuring consistency with the site's overall hover language.
  - **Header Cleanup**: Adjusted the lookup table header to use a light gray background with muted text, removing the dark block aesthetic.
  - **Visual Ergonomics**: Increased cell padding and refined font treatments for better readability on high-density displays.

---

## [v0.8.5] - 2026-02-28

- **UI/UX Fix: Guaranteed Tabular Grid Layout & Hover Tracking** — Root cause of prior tabular rendering failure identified and resolved.
  - **Root Cause**: Tailwind arbitrary `grid-cols-[100px_100px_1fr]` classes were not being generated by the JIT compiler in development, causing the grid layout to collapse into stacked elements.
  - **Fix**: Replaced all Tailwind grid classes with explicit`display: grid` and `gridTemplateColumns` set in JavaScript `style`props, which are always applied regardless of CSS build configuration.
  - **Hover Feedback**: Added a controlled `hoveredId` React state with `onMouseEnter`/`onMouseLeave` handlers. Row background, column text colors, and borders now update immediately and correctly on hover.
  - **Full Row Selection**: Each result row is a`<div>` with `cursor: pointer` and `onMouseDown`for robust click handling.

---

## [v0.8.4] - 2026-02-28

- **UI/UX Correction: Tabular Supplier Dropdown & Selection Fix** — Resolved critical issues in the Supplier selection component.
  - **Tabular Layout**: Results are now rendered as a structured mini-table with headers (**Portal**, **Primavera**, **Descrição**) for better data alignment.
  - **Selection Fix**: Switched row selection event from`onClick` to `onMouseDown`to ensure selection completes before the dropdown is closed by external click listeners.
  - **Visual Affordance**: Enhanced row hover states and ensured full-row clickability.
  - **Empty Formatting**: Added`—` placeholder for missing `PrimaveraCode`to maintain grid alignment.

---

## [v0.8.3] - 2026-02-28

- **UI/UX Refinement: Skybow-style Supplier Dropdown** — Redesigned the Supplier field to match the visual language of existing form controls (like "Planta").
  - **Select-like Trigger**: The field now looks and feels like a standard dropdown, featuring the correct height, border, and brutalist shadow.
  - **Integrated Search**: Moved the search input *inside* the dropdown panel, providing a unified lookup experience.
  - **Auto-focus**: The internal search box automatically focuses upon opening the dropdown.
  - **Clean Selection**: Added a clear (x) button to the trigger for quick removal of the selected supplier.

---

## [v0.8.2] - 2026-02-28

- **Feature: Supplier Searchable Dropdown (Combobox)** — Completely refactored the Supplier selection UX to behave like a proper searchable dropdown.
  - **Immediate Open**: Dropdown now opens immediately on click/focus, loading the first 5 active suppliers.
  - **Live Search**: Filters results as the user types, with a 20-result limit for performance.
  - **Visual Structure**: Standardized on `[PortalCode] Name` format for both the selection field and result rows.
  - **Secondary Info**: PrimaveraCode is displayed as secondary info for enhanced disambiguation.
- **Backend: Search Endpoint Refinement** — Updated `LookupsController` to allow empty queries in `SearchSuppliers`, returning the top 5 records by default.

---

## [v0.8.1] - 2026-03-01

- **Feature: Supplier Master Data UX Standardization** — Integrated the `Feedback` component into `MasterData.tsx` for consistent success/error messaging across the portal.
- **Feature: Automated PortalCode Generation** — Suppliers now have their`PortalCode` (e.g., `SUP-000001`) automatically generated by the backend on creation. The field is read-only in the UI and shows "GERADO AUTOMÁTICO" during creation.
- **Feature: Inline Uniqueness Validation** — Implemented debounced (500ms) inline validation for Supplier `Name` and `PrimaveraCode`.
  - **Normalization**: Name uniqueness is enforced using trimmed, case-insensitive comparison.
  - **Conditional Uniqueness**: `PrimaveraCode` uniqueness is only enforced when a value is provided.
- **Improved: Supplier Search Results** — Redesigned the`SupplierAutocomplete`result list with a "Brutalist" high-contrast look.
  - Results now show the Supplier Name prominently with formatted Codes (`Portal` and `Primavera`) in a secondary row for better disambiguation.
  - Increased dropdown width and item padding for improved readability.
- **Backend**: Added `GET /api/v1/lookups/suppliers/check-uniqueness` endpoint to support frontend validation (including `excludeId` for edit mode).
- **Backend**: Updated`RequestsController` to populate `SupplierPortalCode`in DTOs, ensuring the frontend displays the correct identifier in all contexts.

---

## [v0.8.0] - 2026-02-28

- **Feature: Supplier Identification Enhancement** — Split supplier code into two distinct fields:`PortalCode` (Internal/Portal identifier, required) and `PrimaveraCode`(External/ERP identifier, optional). This prepares the system for future Primavera integration while maintaining local autonomy.
- **Feature: Async Supplier Search (Autocomplete)** — Replaced the standard supplier dropdown in Request forms with a high-performance `SupplierAutocomplete` component.
  - Implements async lookup via`/api/v1/lookups/suppliers/search`.
  - 300ms debounce and 3-character minimum constraint.
  - Professional UI with loading spinner and dual-code display (Portal/ERP).
  - Integrated into both `RequestCreate.tsx` and `RequestEdit.tsx`.
- **Backend**: New endpoint`GET /api/v1/lookups/suppliers/search` in `LookupsController`. Updated create/update DTOs to handle dual codes.
- **Migration**: EF Core migration `AddSupplierCodes` applied: renames existing `Code` to `PortalCode` (preserving data) and adds `PrimaveraCode` as a new nullable column with an index.

---

### [v0.6.3] - 2026-02-27

- **Fix: Request Number Collision on Retry** — The `SystemCounter` was being saved in the same `SaveChangesAsync` as the `Request`, so a failed request insert would roll back the counter. On the next attempt the counter reinitialized to 1 and collided with an existing number, producing a 500 error. Fixed by persisting the counter in a separate `SaveChangesAsync` call before inserting the request. The counter initialization is now also **self-healing**: it reads the max existing sequence for the day/type from `Requests` on first use, preventing any collision with pre-existing records.

### [v0.7.1] - 2026-02-28

- **Hotfix: Item Priority Payload Mismatch** — In `RequestEdit.tsx`, the `handleSaveItem` function was building the payload with `itemPriority: Number(itemForm.itemPriority) || 1`. This converted string codes like `'MEDIUM'` to `NaN`, then fell back to the integer `1`. The backend DTO (`CreateRequestLineItemDto` / `UpdateRequestLineItemDto`) expects a `string`, causing a JSON deserialization error: `"The JSON value could not be converted to System.String. Path: $.itemPriority"`. **Fix:** payload now sends the string code directly (`'HIGH'` / `'MEDIUM'` / `'LOW'`) with a safe fallback to `'MEDIUM'` for any unrecognized value. The `<select>` binding and backend validation were already correct; only the payload mapping was wrong.

### [Step 2] - 2026-03-25

- **Structured Backend Logging**: Implemented `DbLoggerProvider` and `LogEntry` entity for persistent, queryable system logs.
- **Correlation ID Tracking**: Added `LoggingMiddleware` to inject and track `X-Correlation-ID` across all requests.
- **Log Sanitization**: Implemented `LogSanitizer` to automatically redact sensitive information (API keys, tokens) from logs.
- **System Logs UI**: Created a new administrative page with advanced filtering (Level, Date, Source, Search, Correlation ID) and detailed log inspection.
- **OCR Integration Logs**: Added structured logging to the OCR settings and connection test flows.

### [v0.3.1] - 2026-03-23

#### Fixed

- **Quotation Flow:** Removed redundant timestamp-based "Robustness Check" in backend `CompleteQuotation` that incorrectly blocked completion when only quotations were updated.
- **Request Submission:** Removed redundant session-based "dirty check" in frontend `RequestEdit.tsx` and backend `SubmitRequest`, allowing resubmission of already-valid requests without dummy edits.
- **Receiving History:** Standardized quantity formatting in `Histórico ativo` to use `pt-AO` locale (`,` for decimal, `.` for thousands). Improved message clarity to `Acumulado: X de Y` and ensured correct unit display for quotation items.
- **Code Quality:** Removed unused state variables `hasSavedChanges` and `initialItemsJson` in `RequestEdit.tsx`.

### [v0.3.0] - 2026-03-23

- **Feature: Item Priority Redesign** — `ItemPriority` changed from a manual numeric field (`int`) to a business priority classification (`HIGH`/`MEDIUM`/`LOW`). Backend validates and normalizes the code; frontend replaces the numeric input with a select dropdown showing `Alta`/`Média`/`Baixa`. The item grid displays the priority as a colored badge. `LineNumber` remains the technical row order, clearly separate from business priority.
- **Feature: Line Item Status Foundation** — New`LineItemStatus`master data table with 7 statuses (`WAITING_QUOTATION`,`PENDING`,`UNDER_REVIEW`,`ORDERED`,`PARTIALLY_RECEIVED`,`RECEIVED`,`CANCELLED`). Initial item status is auto-assigned by the backend based on the parent request type:`QUOTATION` → `WAITING_QUOTATION`,`PAYMENT` → `PENDING`. Status is never accepted from client DTOs. Item status displayed as a read-only badge in the item grid for the requester. Buyer-controlled status editing is prepared as a future phase.
- **Backend**: New API endpoints `GET /api/v1/lookups/item-priorities` (static) and `GET /api/v1/lookups/line-item-statuses` (DB-driven).
- **Migration**: EF Core migration`AddLineItemStatusAndPriorityCode` with a safe, idempotent SQL data step for the `int` → `nvarchar` `ItemPriority`column conversion.

### [v0.7.0] - 2026-02-28

- **Feature: Item Priority Redesign** — `ItemPriority` changed from a manual numeric field (`int`) to a business priority classification (`HIGH`/`MEDIUM`/`LOW`). Backend validates and normalizes the code; frontend replaces the numeric input with a select dropdown showing `Alta`/`Média`/`Baixa`. The item grid displays the priority as a colored badge. `LineNumber` remains the technical row order, clearly separate from business priority.
- **Feature: Line Item Status Foundation** — New`LineItemStatus`master data table with 7 statuses (`WAITING_QUOTATION`,`PENDING`,`UNDER_REVIEW`,`ORDERED`,`PARTIALLY_RECEIVED`,`RECEIVED`,`CANCELLED`). Initial item status is auto-assigned by the backend based on the parent request type:`QUOTATION` → `WAITING_QUOTATION`,`PAYMENT` → `PENDING`. Status is never accepted from client DTOs. Item status displayed as a read-only badge in the item grid for the requester. Buyer-controlled status editing is prepared as a future phase.
- **Backend**: New API endpoints `GET /api/v1/lookups/item-priorities` (static) and `GET /api/v1/lookups/line-item-statuses` (DB-driven).
- **Migration**: EF Core migration`AddLineItemStatusAndPriorityCode` with a safe, idempotent SQL data step for the `int` → `nvarchar` `ItemPriority`column conversion.

### [v0.6.5] - 2026-02-28

- **Fix: Request Total Double-Counting (Root Cause)** —`AddLineItem`,`UpdateLineItem`, and`DeleteLineItem` were recalculating `EstimatedTotalAmount`by summing over the EF Core in-memory navigation collection (`request.LineItems`). Because`AddLineItem` also called `request.LineItems.Add(newItem)` before calling `SaveChangesAsync`, the new item was counted twice by the sum (once from the DB load, once from the in-memory add). All three endpoints now call`SaveChangesAsync` first, then recalculate the total via a direct `_context.RequestLineItems.Where(l => l.RequestId == requestId && !l.IsDeleted).SumAsync(l => l.TotalAmount)`query, which reads the clean committed state from the database. This guarantees an accurate, authoritative total after every item mutation.
- **Fix: Stuck "Salvando..." Button on Request Save** — `setSaving(false)` was only called in the `catch` block of `handleSubmit` in `RequestEdit.tsx`. On the success path, the button remained permanently in the loading state. Fixed by moving `setSaving(false)` into a `finally` block so it always resets regardless of success or failure.

### [v0.6.4] - 2026-02-28

- **Single Currency Enforcement**: Enforced exactly one currency per request. Header currency becomes read-only once items are added.
- **Fix: Request Total Inconsistency** — The`UpdateDraft` endpoint was previously overwriting the `EstimatedTotalAmount`with the value provided in the frontend DTO, ignoring the actual sum of line items. Fixed by ensuring the backend always recalculates the total from persisted items (excluding deleted ones) during every draft update and item mutation. The frontend no longer sends the total in the payload and refetches it from the server after every save to maintain a single source of truth.
- **Simplified Item UX**: Removed item-level currency selection; items now automatically inherit the request currency.

### [v0.6.3] - 2026-02-27

- **Persistent Request Numbering**: Replaced the record-count behavior with a dedicated `SystemCounters` table. Request numbers are now guaranteed unique and monotonic even if drafts are deleted.
- **Unique Request Number Index**: Added a database-level unique constraint on`RequestNumber`(treating NULLs separately) as a safeguard against concurrency collisions.
- **Draft Cleanup**: Standardized "Delete Draft" button in `RequestEdit.tsx` with improved event handling and navigation safety.

## [v0.6.1] - 2026-02-27

## [0.6.0] - 2026-02-27

### Added

- **Supplier Master Data**: Implemented `Supplier` entity (`Id`, `Code`, `Name`, `TaxId`, `IsActive`) with full CRUD + soft-delete management via `LookupsController`. Exposed in `GET|POST|PUT /api/v1/lookups/suppliers` and `PUT /api/v1/lookups/suppliers/{id}/toggle-active`.
- **Supplier on Request Header**: Added`SupplierId` (nullable FK) to the `Request`entity.
- **Supplier Validation (v0.6.0)**: Integrated Supplier master data into Request header with conditional validation (Required for PAYMENT, Optional for QUOTATION).
- **Backend Hardening**: Resolved nullable warnings in controllers and fixed build blocking errors.
- **Conditional Validation**: Implemented logic where `SupplierId` is **required** if `RequestType` is `PAYMENT`, and optional if `QUOTATION`. This is enforced both on the backend (`RequestsController`) and frontend (`RequestCreate.tsx`, `RequestEdit.tsx`).
- **Standardized Request Types**: Standardized business flow around two types:`QUOTATION` (COM) and `PAYMENT`(PAG). Updated DB seeds and UI labels accordingly.
- **Fornecedores Tab**: Added a new management tab to `MasterData.tsx` with dedicated fields for `taxId` (NIF).

### Changed

- Updated `RequestListItemDto` and `RequestDetailsDto` to include `supplierId` and `supplierName`.
- Frontend API client (`api.ts`) now includes`getSuppliers`,`createSupplier`,`updateSupplier`, and`toggleSupplier`.
- `LookupsController.cs` extended with full CRUD for Suppliers.

### Backend

- **EF Core Migration** `AddSupplierAndRequestTypeCodes` applied: creates `Suppliers` table, adds `SupplierId` to `Requests`, and adds `Code` column to `RequestTypes` for robust conditional logic.

---

## [0.5.0] - 2026-02-27

### Added

- **Department Master Data**: Implemented `Department` entity (`Id`, `Code`, `Name`, `IsActive`) with full CRUD + soft-delete management via `LookupsController`. Exposed in `GET|POST|PUT /api/v1/lookups/departments` and `PUT /api/v1/lookups/departments/{id}/toggle-active`.
- **Plant Master Data**: Implemented`Plant`entity (`Id`,`Code`,`Name`,`IsActive`) with full CRUD + soft-delete management. Exposed in`GET|POST|PUT /api/v1/lookups/plants` and `PUT /api/v1/lookups/plants/{id}/toggle-active`.
- **Department & Plant on Request Header**: Added `DepartmentId` and `PlantId` as required fields on the `Request` entity. Both fields are enforced at the backend DTO level (`CreateRequestDraftDto`, `UpdateRequestDraftDto`) and via frontend inline validation in `RequestCreate.tsx` and `RequestEdit.tsx`.
- **Master Data UI Tabs**: Extended`MasterData.tsx`(Settings page) with **Departamentos** and **Plantas** tabs, supporting the full create / edit / activate / deactivate lifecycle, matching the existing Units / Currencies / Need Levels pattern.
- **Request List Columns**: Added **Departamento** and **Planta** columns to the main `RequestsList.tsx` grid.
- **Default Seed Data**: Migration seeds one seeded Department (`Administração / ADM`) and one Plant (`Luanda / LUA`) to satisfy initial FK integrity when upgrading existing databases.

### Changed

-`RequestListItemDto` and `RequestDetailsDto` updated to include `departmentId`,`departmentName`,`plantId`,`plantName`.

- `api.ts` extended with `getDepartments`, `createDepartment`, `updateDepartment`, `toggleDepartment`, `getPlants`, `createPlant`, `updatePlant`, `togglePlant`.

### Backend

- **EF Core Migration** `AddDepartmentPlantAuditAndRequiredFields` applied: adds `IsActive` to `Departments` / `Plants` tables, seeds default records, and makes `DepartmentId` / `PlantId` non-nullable on `Requests`.

---

## [0.4.1] - 2026-02-27

### Changed

- **Item List UX Auto-Scroll & Inversion**: Dramatically improved the Request Item addition ergonomic flow. The `Novo Item` form now permanently visually renders *beneath* the existing Items table, keeping the table header as the primary section anchor. Upon creating a brand-new Request, the application now fluidly auto-scrolls down to the Items section and pulses a subtle 5-second red highlight, providing an unmistakable visual cue that adding line items is the immediate next action.
  - *Visual Guidance:* When guiding users to a specific section post-navigation (e.g. directing them to Item Creation after Draft Save), utilize a React`useRef` to trigger `.scrollIntoView({ behavior: 'smooth' })` against the target `div`, augmented by a subtle, temporary CSS`box-shadow`class representing a "glow" so the new work area is unmistakable.
- **Request Draft Post-Save Navigation**: Streamlined the Edit workflow. Clicking `Salvar Mudanças` on the main Request header now instantly redirects the user back to the centralized List View (`/requests`) upon network success, providing closure. As a direct safety measure, users are solidly blocked from saving the root Request if they have an active `Novo Item / Editar Item` child form currently unsaved underneath.
- **Request List Pagination**: Implemented dynamic subset slicing on the Primary List Data. Requests load gracefully capped at 20 items per page by default, drastically lowering database stress and increasing browser paint speed. The grid injects an interactive bottom bar affording custom limits (10/20/50/100) and structured Next/Previous iteration. This behaves symmetrically with active Filters resetting smartly to`Page 1`any time Search or Status constraints change.
- **Request List Search Filtering**: Implemented a responsive full-stack search bar against the Request DataList. It utilizes a 500ms debounce buffer on the frontend to protect server capacity while searching against both Request Number and Title criteria simultaneously on the backend.
- **Request List Status Filter**: Substituted the legacy Filter mock button with a native`<select>`dropdown powered by the live Reference Data dictionary. It dynamically executes a fast query intersection with the active text search, allowing deep cross-filtering (e.g., searching for a specific string explicitly inside "RASCUNHO" requests).

- **Sticky Contextual Feedback**: Completely reworked Page-level UX validation and success reporting for Request workflows. Success/error messages are now injected straight into the Top Action sticky block so they seamlessly follow users scrolling down massive item lists.
- **Cross-Component Navigation State**: Re-engineered `RequestCreate`'s success workflow to securely dispatch a `successMessage` pointer inside React Router's DOM History State. This perfectly populates `"Pedido criado com sucesso. Agora você pode adicionar os itens do pedido."` natively on the Edit view seamlessly without database hacking.
- **Master Data Separation**: Fixed a critical visibility flaw where deactivating a Master Data record (Unit, Currency, NeedLevel) erased it completely from the management list. The Maintenance Settings now explicitly fetch all records (`includeInactive=true`) to allow auditing and reactivation.
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
