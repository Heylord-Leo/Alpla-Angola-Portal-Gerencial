# Changelog

All notable changes to this project will be documented in this file.

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
