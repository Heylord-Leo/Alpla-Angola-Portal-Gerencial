# Version

## Current Version

v2.34.0

## [2.29.0] - 2026-04-04

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

- **2.34.0**: Request Edit Workflow Optimizations. Refined the Request Edit UI for `PAYMENT` requests by suppressing non-applicable quotation sections and guided attention. Integrated `QuickSupplierModal` with an OCR prefill block to enable seamless supplier creation and auto-selection for unmatched invoice data.
- **2.33.2**: Approval Modal State Sync. Fixed a stale closure bug in the Approval Center drawer.
- **2.32.0**: Payment OCR Flow UX & Mapping Refinement. Resolved data mapping and UX bugs between the OCR upload and the persisted draft. Preserved OCR-derived final total and fixed supplier persistence. (Known Limitations: Discount handling is interim via description notes, and company prefill relies on deterministic scope fallback, not OCR tax ID matching).
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
