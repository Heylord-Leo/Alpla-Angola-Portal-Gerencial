# Version

## Current Version

v2.85.0

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
