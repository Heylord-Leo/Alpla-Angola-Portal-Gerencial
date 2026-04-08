# Version

## Current Version

v2.39.7

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
- **2.12.1**: Resizable Approval Center Drawer. Implemented horizontal resizing with localStorage persistence and desktop-optimized reflow for decision insights.
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
