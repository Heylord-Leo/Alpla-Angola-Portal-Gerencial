# Changelog

All notable changes to this project will be documented in this file.

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

## [v2.13.1] - 2026-04-01

### Added
- **Detailed History Drill-down (Procurement)**: Implemented a high-density "sliding sub-panel" (drawer-within-a-drawer) that allows approvers to inspect historical purchase data for specific line items.
- **Normalized Matching Logic**: Backend support for matching items via normalized descriptions (levenshtein-like matching in SQL), providing a "Descrição Aproximada" context with a 1-year lookback.
- **Executive Drill-down UX**: Added a "Ver Detalhes" action to intelligence cards in the `DecisionInsightsPanel`. The sub-panel slides over the main drawer, maintaining approval context while opening deep historical data.
- **Price Variation Analysis**: The drill-down panel displays unit price comparisons against historical records with visual indicators for "Last Purchase" and price deviations.
- **Role-Aware Drill-down**: The entry point adapts its visual style based on the approver role (Area vs Final), ensuring a cohesive experience in the `DecisionInsightsPanel`.

### Changed
- **Drawer Header Redesign**: Replaced the heavy black header bar with a lighter, neutral surface with an 8px contextual left-accent border (blue for Area, green for Final). Improved prev/next navigation controls as 32px brutalist buttons.
- **History Panel Transition**: Replaced the "giant top-mounted slab" with a focused sliding sub-view using backdrop dimming and blur for maintained user focus.

## [v2.13.0] - 2026-04-01

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

## [v2.12.2] - 2026-04-01

### Added
- **Role-Aware Decision Intelligence (DEC-084)**: The `DecisionInsightsPanel` now adapts emphasis and content based on the current approval stage (`AREA` vs `FINAL`), providing contextually relevant decision support without duplicating the drawer or forking the component.
- **Context Banner**: Subtle role indicator at the top of the intelligence panel — "Foco: Legitimidade e Necessidade" (Area) or "Foco: Racionalidade Financeira" (Final).
- **Area Emphasis — Checklist de Legitimidade**: Compact informational checklist with ✅/⚠️ indicators for Centro de Custo, Justificativa, Fornecedor, and (for Quotation types) Cotação Formalizada. Purely visual — no new approval blockers.
- **Final Emphasis — Visão Financeira Comparativa**: KPI grid surfacing Year Accumulated Total (newly exposed), Historical Purchase Count, and Weighted Consolidated Variation with partial-coverage disclaimer when not all items have history.
- **Role-Based Section Reordering**: Shared intelligence blocks (Alerts, Department KPIs, Item Analysis) render in different priority order per approval stage — Area prioritizes alerts, Final prioritizes financial context.
- **Extracted Reusable Primitives**: Internal refactor of `SectionLabel`, `KpiCard`, and `ItemCard` sub-components for consistency and maintainability.

## [v2.12.1] - 2026-04-01

### Added
- **Resizable Approval Center Drawer**: Implemented horizontal resizing for the side drawer on desktop viewports (> 768px).
- **Persistent Width**: Support for saving and restoring drawer width via `localStorage` (`approvalDrawerWidth`).
- **Responsive Layout Reflow**: Optimized `DecisionInsightsPanel` (Visão Departamental and Análise por Item) to adapt naturally to varied drawer widths using responsive grid templates.
- **Interactive UI Handle**: Added a subtle, reactive resize handle on the left edge with high-hit-area hitboxes and hover indicators.
- **Intelligence Section Refinement**: Complete UI/UX refactor of the decision support panel in the Approval Center drawer.
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

