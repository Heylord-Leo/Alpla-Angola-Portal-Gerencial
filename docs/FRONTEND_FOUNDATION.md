# Frontend Foundation Rules

## Request Identification and Navigation

1. **Unified Numbering Scheme**: All requests use a unified format: `REQ-DD/MM/YYYY-SequentialNumber` (e.g., `REQ-01/03/2026-001`). The sequential portion is monotonic and global, ensuring no number is ever reused even if the request is deleted.
2. **Identification Neutrality**: Frontend logic must never infer the business "Type" (Payment vs. Quotation) from the request number prefix. Always use the explicit `requestTypeCode` or `requestTypeId` supplied by the API.
3. **URL Safety**: Request identifiers containing slashes are used strictly for visual display and searching. Technical routing and API calls must utilize the immutable Guid `Id`.
4. **Modular Navigation Architecture**: The application uses a grouped navigation structure to organize features by business module (e.g., Compras).
    - **Groups**: Non-navigable containers that expand/collapse following an **Accordion (Single-Open)** model. Only one group can be expanded at a time in the main sidebar; expanding a new group automatically collapses the previous one. They still auto-expand if a child route is active.
    - **Active States**: Parent groups show a subtle "group active" highlight (light background) when a child is active, while the specific child link shows the "strong" highlight (primary color background + soft elevation).
    - **Configuration**: The sidebar is driven by a `MENU_ITEMS` configuration array in `Sidebar.tsx`, supporting `link`, `group`, and `action` types for easy scalability.
    - **Two-Zone Layout**: The sidebar is split into a **Navigation Zone** (top, scrollable) and a **System Actions Zone** (bottom, fixed). This ensures critical actions like "Sair" and "Configurações" are always reachable regardless of menu length.
    - **Independent Scrolling**: The navigation zone manages its own internal scrollbar. The sidebar container itself is full-height and its scrolling is decoupled from the main page content.

## Collapsible Sections (v1.1.48)

For workspaces and complex forms, the project uses a standardized `CollapsibleSection` component (`src/frontend/src/components/ui/CollapsibleSection.tsx`) to manage information density.

1. **Visual Pattern**: Sections feature a refined border (`border-1`), a soft elevation, and a clear header toggle.
2. **Controlled Animation**: Uses `motion/react` for smooth height transitions and `AnimatePresence` for clean dom mounting/unmounting.
3. **Search Behavior**: When integrated with search, sections should automatically expand if they contain filtered results, and restore to their default state when search is cleared.
4. **Default State**: Most workspaces should default to only the first or most relevant (e.g., "Pending") section being open. In the Request Edit form, the "ITENS DO PEDIDO" section defaults to **collapsed** on initial load to reduce vertical noise.
5. **Guided Attention**: For critical workflow stages (e.g., `WAITING_AREA_APPROVAL`), specific sections may be automatically expanded, scrolled into view (with sticky-header offset), and briefly highlighted with a soft pulse effect to guide the user's attention. This logic must only run once per record load.
6. **Migration Guidance**: Legacy screens may temporarily coexist with "Industrial Brutalist" styles (0px radius, heavy shadows). All new or refactored components must strictly follow the Modern Corporate standard (8px-12px radius, soft elevations).
  - **New Default Standards**:
    - **Rounded Corners**: 8px or 12px as the new standard (replacing the 0px default).
    - **Soft Elevations**: Low-opacity, diffused shadows (`var(--shadow-sm/md/lg)`).
    - **Action Floating Menus**: `KebabMenu` components must rely on graceful background hovers (`#f1f5f9`), avoiding uppercase commands.
    - **Subtle Borders**: Lighter borders (`var(--color-border)`) for containers, avoiding heavy default blue borders.
  - **Retired Standards**:
    - **Industrial Brutalist** is completely discontinued.
    - **Tailwind Utility Sprawl** is deprecated. Stick to object-mapped inline CSS properties utilizing token variables.
  - **Migration Policy**: All new or refactored screens must follow the Modern Corporate direction natively.

## Contextual Help and Accessibility (v2.24.0)

To assist users in complex administrative tasks, the project utilizes contextual help tooltips for sensitive fields like "Funções e Permissões".

1. **Centralized Definitions**: Role descriptions are maintained in a central repository (`src/frontend/src/constants/roles.ts`). This ensures that the same explanation is provided across all administrative screens (Create, Edit, and Profile).
2. **Accessible Tooltips**: The `Tooltip` component (`src/frontend/src/components/ui/Tooltip.tsx`) supports multi-modal triggers:
   - **Hover**: Standard desktop interaction for immediate feedback.
   - **Focus (Tab)**: Enables keyboard-only users and screen readers to access help text by tabbing into the `Info` icon.
3. **Visual Identity**: Help icons use the `Info` component from `lucide-react` at a standard `size={14}`, with a subtle `opacity: 0.5` to avoid visual clutter while remaining discoverable.
4. **Tooltips via Portal**: All tooltips must be rendered through the `DropdownPortal` pattern to ensure they are never clipped by parent containers with `overflow: hidden`.

## Resizable Drawer Pattern (v2.12.1)

To optimize information density on desktop screens, the Approval Center utilizes a **horizontally resizable side drawer**.

1. **Desktop-Only Capability**: Resizing is exclusively enabled for viewports > 768px. On mobile, the drawer defaults to a fixed 100% width.
2. **Persistence**: The user-selected width is stored in `localStorage` (`approvalDrawerWidth`) and restored upon page reload or drawer reopening.
3. **Constraints**: To maintain layout integrity, the width is clamped between **520px** and **90vw**.
4. **Resize UI**: A subtle 6px vertical handle on the drawer's left edge provides a high-hit-area interaction point without visual noise. It features a high-contrast highlighting of `var(--color-primary)` upon hover or drag.
5. **Fluid Reflow**: Internal components must utilize responsive grid templates (e.g., `repeat(auto-fill, minmax(200px, 1fr))`) to ensure content adapts naturally as the drawer expands, preventing excessive white space.

## Modernization Stack (v2.0.0)

The project has been modernized to support the Shell 2.0 architecture:

1. **React 19**: Utilizing the latest React features and stricter typing for refs and hooks.
2. **React Router 7**: Declarative routing is maintained, but the foundation is ready for future data-mode migration.
3. **Inline CSS Variables**: Strict deprecation of third-party utility class frameworks (like Tailwind). Styling must be implemented deterministically via `style={{ }}` bound to global `tokens.css`. 
4. **Motion**: Transitioned from `framer-motion` to the modular `motion/react` package.
5. **Sonner**: Introduced as the primary toast notification system, replacing ad-hoc feedback banners where appropriate.

## Global UI Layering and Z-Index Standardization (v2.13.0)

To prevent UI layering conflicts (e.g., modals appearing behind drawers or headers), the project enforces a centralized z-index hierarchy and root-rendering pattern.

1. **Centralized Scale**: All `zIndex` values must reference the `Z_INDEX` constants in `src/frontend/src/constants/ui.ts`. This ensures a single source of truth bridged between CSS-in-JS and `tokens.css`.
2. **Hierarchy Rules**:
   - `Dropdown/Popover` (1000) < `Drawer` (1100) < `Modal` (1200) < `Toast/Tooltip` (1300+).
   - This ensures that a confirmation modal triggered from within a drawer always renders above it.
3. **DropdownPortal Usage**: All overlays (modals, fixed drawers, dropdowns, tooltips) MUST be rendered through the `DropdownPortal` component or `createPortal` to the document body.
   - This escapes parent stacking contexts (like `AppShell` or `overflow: hidden` containers) and ensures predictable layering across the entire application.
4. **Stacking Context Protection**: The `AppShell` main content area must NOT define a numeric `zIndex` (unless strictly isolated), to avoid trapping fixed-position children in a lower stacking context than the shell's headers.

## Request & Plant Model (v1.1.25)

To support multi-plant allocation while maintaining institutional boundaries, the project uses a **Company-Level Request** model.

1. **Request-Level Company**: Every request belongs to exactly one legal `Company` (e.g., AlplaPLASTICO, AlplaSOPRO). This field is mandatory at creation and determines the pool of available plants.
2. **Item-Level Destination Plants**: Line items within a single request can be allocated to different `Plants` (Destination Plants).
3. **Validation Boundary**: All line items in a request MUST belong to plants owned by the same legal `Company`. The system enforces this strictly:
    - **Frontend**: Item-level plant selection is filtered by the selected request company.
    - **Backend**: Strict verification of company-plant ownership is performed during line item creation/update.
4. **Company Locking**: Once a request has at least one line item, the `Company` field becomes read-only in the header to prevent data inconsistency.

## Copy Request Flow (v2.14.0)

The "Copy Request" feature allows a user to create a new request using an existing one as a template. To maintain data integrity and prevent abandoned drafts, this flow follows specific business and routing rules.

1. **Routing**: The copy action initiates a navigation to `/requests/new?copyFrom={id}`. This is handled by `RequestCreate.tsx`, which serves as the "Copy Mode" owner.
2. **Business Data Prefilling**: Only header-level business fields that define the request structure are copied:
   - **Copied**: Title (original), Description, Request Type, Need Level, Department, Company, Plant, Buyer, Area Approver, Final Approver.
   - **NOT Copied**: Requester (sets to current user), Request ID/Number, Status, Currency, NeedByDate, Line Items, Values/Totals, Supplier, Attachments, Quotations, History.
3. **Title Composition**: The frontend automatically composes the new title as: `Cópia {SourceRequestNumber} {OriginalTitle}`.
4. **Description Warning**: A mandatory amber warning banner is displayed below the description: *"Atenção: Esta descrição foi copiada do pedido original. Revise o conteúdo antes de submeter..."*.
5. **NeedByDate Enforcement**: This field always comes blank. The user must explicitly enter a new date for the copy, subject to standard validation.
6. **Currency Logic**: Currency is NOT copied. It defaults to the system's internal default (AOA) and is not emphasized as a "copied" field.
7. **UX & Identity**:
   - **Breadcrumb**: Shows `Dashboard > Pedidos > Cópia de Pedido`.
   - **Page Title**: Updates to `Cópia de Pedido`.
   - **Secondary Action**: `CANCELAR` is replaced by `DESCARTAR CÓPIA` to clarify that no persisted record is being deleted.
   - **Navigation Protection**: Standard `beforeunload` protection applies if the form has been touched.

## Modern Dashboard Workspace (v2.40.0)

The Modern Dashboard is the unified entry point. It has been completely redesigned around the `ActionCarouselWidget` to maximize real-estate and guide immediate action focus.

### Operational Overview Segment

1. **Action Carousel ("Para Minha Ação")**:
    - A dynamic, fluidly scrolling horizontal container displaying high-urgency requests.
    - Clickable action cards using the Modern Corporate soft-elevation standard and integrated Kebab Menus.
2. **Status Grouping & Filter Dropdowns**:
    - Custom `<FilterDropdown />` wrappers now group isolated statuses into user-friendly semantic modules: `INICIAL`, `APROVAÇÃO & COTAÇÃO`, `FINANCEIRO & RECEBIMENTO`, and `FINALIZADOS`.
    - This eliminates cognitive overload when filtering standard queues.
3. **Floating UI & Pagination Centralization**:
    - "Total Filtrado" trackers and secondary actions are rendered as "Floating Widgets" (absolute/fixed position anchoring bottom-right).
    - **Pagination rules**: All table pagination controls must be strictly centered at the bottom of the table to prevent `z-index` clashing with the floating summary cards. 
4. **Attention Highlighting**:
    - Automatic Urgency categorization (`needByDateUtc`) maps into priority highlights utilizing `var(--color-status-amber)` or `var(--color-status-rose)`.

### Status and Action Badges

Use the global `.badge` class with one of the specialized semantic modifiers:

- **`.badge-success`**: Green background, white text. (Completed/Received)
- **`.badge-warning`**: Yellow background, dark text (`#111827`). (Pending/Attention)
- **`.badge-amber`**: Amber background, dark text (`#111827`). (Alternative Attention)
- **`.badge-danger`**: Red background, white text. (Action Required/Rejected)
- **`.badge-info`**: Blue/Indigo background, white text. (In Follow-up)
- **`.badge-neutral`**: Gray background, white text. (Draft/Process)

> [!IMPORTANT]
> To ensure WCAG AA compliance (4.5:1 ratio), always use dark text on yellow/amber/orange backgrounds. Avoid the previous "10% opacity" pattern for core status indicators as it often results in poor legibility.

### Winning Quotation Pivot (v1.1.38)

For `QUOTATION` requests, the `RequestEdit.tsx` component implements a **UI Pivot** when `selectedQuotationId` is present:
- **Header/Summary**: The Supplier Name and Estimated Total Amount are substituted with the winning quotation's commercial data (derived from the DTO).
- **Items Table**: The table body is swapped to render the items from the winning quotation instead of the preliminary request items.
- **Banner**: An informative banner is displayed to clarify that the user is viewing authoritative commercial data.
- **Locking**: Once selected and approved, the winning quotation becomes the immutable commercial definition of the request.
- **Identity Selection**: For COTAÇÃO requests, the "Winning Quotation" is the source of truth for the receiving operations.

---
### Interactive Workflow (Educational Segment)

A non-operational, instructional guide to the purchasing process.

1. **Main Path Dominance**: The workflow emphasizes the primary linear path (Rascunho to Execução) with clean connectors and explicit stage labels. Selected stages are clearly dominant, while non-selected stages maintain enough visual presence for readability.
2. **Role Identification**: Each stage includes a short, clean role label (`Solicitante`, `Comprador`, `Aprovador Área`, `Aprovador Final`, `Processo`) for immediate clarity on responsibility.
3. **Adjustment Exception Path**: The "Reajuste" branch is visually isolated (usually below the main path with dashed borders) to reinforce it as an exception/return zone. Explanatory text uses a legible, high-contrast style.
4. **Enhanced Details Panel**: Sequential, scannable panel with generous vertical breathing room between sections:
    - **Role & Objective**: Direct identification of the actor and the phase's goal.
    - **Activities**: Bulleted list of core operational tasks.
    - **Documentation**: Shown as modern chips/badges for high scannability.
    - **Next Step**: Prominent highlighted card (`var(--color-primary)`) indicating the immediate flow.

    - **Return/Adjustment**: Specialized amber-themed section (`var(--color-status-orange)`) detailing potential rework reasons and flows.
5. **Responsive Adaptation**: The workflow utilizes a horizontally scrollable container with stable min-widths for stages and labels, ensuring a consistent experience on all screen sizes.
6. **Guide Aesthetic**: Uses explicit labels like "Guia Visual de Processo", lighter connectors (ChevronRight with strokeWidth 2), and restrained, elegant styling.

## Form Validation Standards

We use **Inline Form Validation** directly tied to the Backend responses as the project-wide standard. To ensure maximum productivity, users must never be navigated away from a form due to predictable field validations, and their partially-entered data must not be lost.

- **Required Field Enforcement**: Added HTML5 `required` attributes and backend `[Required]` annotations for `Grau de Necessidade` e `Necessário Até` fields, ensuring users cannot save a request without providing these values. Inline error messages highlight missing fields and preserve entered data for correction.
- **Conditional Visibility and Requirement**: Some fields (e.g., `Data de Necessidade`) are conditionally visible and required based on business rules (e.g., only for `QUOTATION` request types). These fields are excluded from validation when hidden and must be handled as `null` in the API payload if not applicable.

## Form Field and Placeholder Standards (v2.28.0)

To ensure maximum legibility and accessibility (WCAG AA Compliance), form fields and placeholders must follow these styling rules:

1. **Placeholder Contrast**: Never use opacity (e.g., `0.5`) for placeholder text. Instead, use the resolved semantic tokens:
   - `--color-placeholder`: Standard high-contrast color for inactive/unfilled fields.
   - `--color-placeholder-focus`: A distinct, even higher-contrast color when the field is active.
2. **Text Normalization**: Use `text-transform: none` for placeholder text to improve readability, especially in long-form fields or multiline textareas. Avoid global `uppercase` on placeholders.
3. **Empty States (Native Selects)**: Native `<select>` elements with an empty or "-- Selecione --" initial value must visually mimic a placeholder. Use the `:has(option[value=""]:checked)` selector pattern to apply `--color-placeholder`.
4. **Field States**:
   - **Disabled**: Use `var(--color-field-disabled-bg)` for background and `var(--color-text-field-disabled)` for text.
   - **Read-Only**: Use `var(--color-field-readonly-bg)` for background.
   - **Error**: Use `#EF4444` borders and `#FEF2F2` background (standard project error colors).
5. **Hierarchy**: Centralize these rules in `globals.css` and `tokens.css`. Component-level overrides must be used only sparingly and should still reference the global semantic tokens.

### Error Handling Paradigm

1. The ASP.NET Core API relies inherently on `[ApiController]` which synthesizes `ValidationProblemDetails` (HTTP 400).
2. The UI global `api.ts` interceptor catches HTTP 400 errors and serializes the `errors` dictionary into a custom localized `ApiError` class object.
3. Every mutating Form component (Create/Edit) must:
   - Instantiate a state structure: `const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});`
   - Reset it before sending logic: `setFieldErrors({});`
   - Handle exceptions logically: `if (err instanceof ApiError && err.fieldErrors) setFieldErrors(err.fieldErrors);`
4. Form UI must loop the mapped errors and generate contextual alerts (inline red text) using `!!fieldErrors[PropName]`.

### Custom Input Directives

**Money/Values**: The project enforces strictly masked text inputs using standard `Intl.NumberFormat` processing for monetary elements using the `CurrencyInput.tsx` custom React component.

- The `CurrencyInput` enforces visual masks strictly upon the screen (example: `100.000,50`) whilst resolving the backend state properties dynamically via pure API-Ready strings (`100000.50`). DO NOT use HTML `<input type="number">` on monetary variables, leverage `<CurrencyInput>`.
- **Currency Enforcement**: Purchase requests must use a single currency. Items automatically inherit the currency selected in the Request Header. The header currency becomes read-only once items are added to prevent calculation divergence.

### Date and Time Formatting

The project enforces a strictly standardized date and time display format to ensure consistency across all user-facing screens and industrial alignment.

1. **Shared Utilities**: All user-facing date and time rendering must use the shared utility functions from `src/frontend/src/lib/utils.ts`.
   - `formatDate(date)`: Enforces `DD/MM/YYYY` (UTC).
   - `formatTime(date)`: Enforces `HH:mm` (UTC).
   - `formatDateTime(date)`: Enforces `DD/MM/YYYY HH:mm` (UTC).
2. **Explicit Formatting & UTC Enforcement**: Developers must NOT rely on browser locale settings (`toLocaleDateString`, `toLocaleString`). The utilities explicitly use UTC methods (`getUTCDate`, `getUTCHours`, etc.) to ensure visual consistency regardless of the user's local timezone.
3. **Date Inputs**: For date entry in forms, use the shared `DateInput` component (`src/frontend/src/components/DateInput.tsx`). It enforces the `DD/MM/YYYY` visual standard while natively handling the `YYYY-MM-DD` string required by the API. It includes a built-in calendar picker (triggered by icon) while preserving manual typing with a strict mask.
4. **Safe Fallbacks**: Invalid or null dates must be handled gracefully by the utilities, typically returning `-` instead of "Invalid Date" or crashing.
5. **Display Only**: Formatting changes are strictly for the presentation layer. Backend communication and logic must preserve ISO 8601 UTC formats.

## Form Feedback Visibility

Success and error messages must remain conveniently visible while the user is working in long pages (e.g. Request details).

1. **Overlay Banners:** Page-level feedback banners (global successes or validation alerts) must be implemented as **Overlays** (fixed or absolute) so they do not occupy space in the document flow. This prevents dangerous layout shift (CLS) and accidental clicks during auto-dismissal.
2. **Standardization:** Utilize explicit text affirmations consistently:
   - *Creation:* "**Rascunho salvo com sucesso.**"
   - *Updates:* "**Rascunho salvo com sucesso.**"
   - *Items:* "Item salvo com sucesso."
3. **Cross-Route Context & Visual Cues:** To pass a success message after an action that requires a route hop (like saving a Draft and moving to Edit or returning to the List), inject the string via the `react-router-dom` `navigate(url, { state: { successMessage: '...' } })` payload and trap it via `useLocation` on the destination component.
   - *Conditional Navigation:* Upon creation, if the Request Type is `QUOTATION`, the user is returned to the List. If `PAYMENT`, they are guided to the Edit/Item screen.

### Guideline 3: Action Rendering

- **Backend: Item Requirement Enforcement**: Updated `SubmitRequest` to strictly require at least one line item for **PAYMENT** requests before entering the official workflow. **QUOTATION** requests are exempt from this rule on initial submission (Items handled by Buyer later).
- **Workflow: Financial Attachment Rule Alignment**: Unified `PAYMENT_SCHEDULE` and `PAYMENT_PROOF` rules for both `QUOTATION` and `PAYMENT` requests.
  - `PAYMENT_SCHEDULE` allowed in `PO_ISSUED`, `PAYMENT_SCHEDULED`.
  - `PAYMENT_PROOF` allowed in `PAYMENT_SCHEDULED`, `PAYMENT_COMPLETED`, `WAITING_RECEIPT`.
- **Workflow: Payment Flow Strictness**: For `PAYMENT` requests, approval stages strictly permit only `APPROVE` or `REJECT` actions. The `REQUEST_ADJUSTMENT` action is explicitly hidden in the UI and blocked by backend logic.
- **UX: Buyer Navigation Focus**: The system supports deep-linking via query parameters. The `/requests/:id?focus=items` parameter from the "Itens do Pedido" list will automatically scroll the user's viewport to the items section and pulse a highlight to orient the buyer immediately.
- **UX: Attachment Management Lifecycle**: Attachment management (Upload/Delete) is decoupled from header and item editing. It remains active throughout the entire request lifecycle until the request is finalized, governed by status-specific gating for each document type.

### Guideline 4: Contract Alignment

- **IDs**: Use `number | null` for numeric IDs from the backend.
- **Business Codes**: Use string unions (e.g., `'HIGH' | 'MEDIUM' | 'LOW'`) as the source of truth for status and priority fields.

## Buyer Workspace (Grouped Layout)

The Buyer Workspace (`BuyerItemsList.tsx`) is the central hub for managing procurement actions across multiple requests.

### Core Patterns

- **Grouping by Request**: Line items are never shown in isolation. They are always nested under their parent Request card.
- **Action Badges**: High-visibility status indicators guide the buyer:
  - `WAITING_QUOTATION` -> **AÇÃO NECESSÁRIA: COTAR** (Red)
  - `AREA_ADJUSTMENT` / `FINAL_ADJUSTMENT` -> **AÇÃO NECESSÁRIA: REAJUSTAR** (Orange)
  - `PAYMENT_COMPLETED` -> **PAGAMENTO REALIZADO** (Green)
  - `WAITING_RECEIPT` -> **AGUARDANDO RECEBIMENTO** (Amber)
  - `IN_FOLLOWUP` -> **EM ACOMPANHAMENTO / RECEBIMENTO PARCIAL** (Cyan)
- **Mandatory Observations**: For specific line item status changes (`ORDERED`, `PARTIALLY_RECEIVED`, `RECEIVED`), the system enforces a mandatory observation collected through the project-standard `ApprovalModal`.

## [v1.1.42] - 2026-03-22

### Added

- **Stage 10.5: Refactor da UI de Recebimento**:
  - **Standardized Header**: Integrado o componente `RequestActionHeader` na tela de Operação de Recebimento, unificando breadcrumbs, títulos e feedbacks.
  - **Modern Corporate Design Alignment**: Visual update to follow the refined corporate standard (soft elevations, uppercase buttons, clean borders).
  - **Identidade Preservada**: Alinhamento com o layout shell do portal sem perder a identidade funcional da operação de recebimento.
- **Correção de Prefixo**: Corrigido bug visual de prefixo duplo "REQ REQ-" nos números de pedido.

## Shell 2.0 Redesign Architecture (v2.0.0)

Beginning in v2.0.0, the portal adopted the **Shell 2.0 (Modern Industrial)** layout, featuring a collapsible navigation system and an optimized workspace area.

### Core Shell Components
    - **Transitions**: Smooth CSS transitions (`0.3s ease`) applied to grid columns and internal offsets.
4. **Sidebar Hover Flyouts (v2.18.0)**:
    - **Contextual Navigation**: When collapsed, hovering over a navigation icon triggers a lateral flyout panel.
    - **Content**: Displays the section name as a header and all available child links (subsections) with their respective icons.
    - **Stability**: Implements a 150ms "anti-flicker" delay. The flyout remains open while the pointer is over either the icon or the flyout itself.
    - **Viewport Awareness**: The flyout automatically calculates its vertical position to stay within the viewport, shifting upwards if necessary for items at the bottom of the sidebar.
    - **Click Fallback**: Clicking a collapsed icon provides a robust fallback by navigating to the item's direct link or its first available child, ensuring navigation is possible even if hover interactions are restricted.
    - **Portal Rendering**: Uses `DropdownPortal` to render overlays at the body level, escaping the sidebar's `overflow: hidden` constraint.

### Component Preservation Rule
1. **Black Box Data**: Redesigned UI components (e.g., `MasterData.tsx`, `DocumentExtractionSettings.tsx`) must completely preserve underlying API queries, mutation hooks, custom debounced inline-validations, RBAC evaluations, and localized error handling behavior. 
2. **Presentation Only**: The redesign acts solely as a presentation layer overhaul (DOM structure, grid-management, and styling). It is strictly forbidden to alter backend payloads, DTO models, structural request tracking, or lookup cascades simply to satisfy an aesthetic layout.
3. **Styling Propagation**: New visual components use explicit inline CSS mapped tightly to `tokens.css`. Legacy elements and modernized structures successfully compute against the exact same color spacing and depth tokens.
4. **Independent Scoping**: Changes to isolated settings tabs or admin diagnostics must not bubble up side-effects into the `AppShell` or globally mutate navigation bounds un-prompted.

### Content Spacing
- The main content area utilizes a combination of `marginTop` (to clear the fixed topbar) and generous internal padding (`padding: 2rem 3rem`) to provide breathing room for complex feature tables and forms without causing horizontal overflow.

### Fixed

- **Stage 10.4: Alinhamento de Schema de Banco**:
  - Correção de erro SQL por colunas faltantes (`ReceivedQuantity`, `DivergenceNotes`, `LineItemStatusId`) nas tabelas `RequestLineItems` e `QuotationItems`.
  - Configuração de precisão decimal (`18,4`) para quantidades recebidas.

### Operational Receiving Workspace (v1.1.42)

The Receiving Workspace (`ReceivingWorkspace.tsx`) and Operation (`ReceivingOperation.tsx`) provide a dedicated environment for the post-payment phase.

1. **Workspace List**: Filtered view for requests in `PAYMENT_COMPLETED` or `WAITING_RECEIPT` status. Users can jump into the operational details via the "RECEBER" action.
2. **Authoritative Context**: The operation page dynamically identifies the item source:
   - `PAYMENT` Request -> Uses `RequestLineItems`.
   - `QUOTATION` Request -> Uses items from the **Winning Quotation**.
3. **Item Conference**: A specialized grid allows recording `ReceivedQuantity` and `DivergenceNotes` for each item.
4. **Layout Standardization**: The page utilizes the unified `RequestActionHeader` to provide a consistent header, breadcrumb, and action experience, following the portal's standard shell and Modern Corporate design language.
5. **Status Sync**: Updating an item automatically recalculates the overall request status:
   - If any item is `PARTIALLY_RECEIVED` or `PENDING` while others are finished, the request moves to `IN_FOLLOWUP`.
   - Once all items are `RECEIVED`, the "Finalizar Pedido" action becomes available to move the request to `COMPLETED`.

### Quotation Management (2-Section Layout)

To improve focus and operational clarity, the quotation area within the expanded request card is split into two distinct visual sections:

1. **Section A: Quotations & Documents**: Displays previously registered quotation items and uploaded documents.
2. **Section B: Add New Quotation**: A dedicated area for introducing new data. It supports two distinct modes:
   - **Upload Mode**: For attaching digital documents (e.g., Proformas).
   - **Manual Mode**: For direct entry of quotation line items using the project-standard item form.
- **Mode Switching**: The UI provides explicit buttons ("Upload documento" and "Inserir manualmente") to toggle between modes, with a "Back/Cancel" action to return to the neutral choice state.
- **Document Import Flow**: When importing a document, the UI follows a structured progression:
  1. **Selection**: File identification and validation.

  2. **Extraction**: A "Continuar com documento" action that calls the OCR service.

  3. **Review & Edit (Step 4)**: The extraction result is converted into a **locally editable draft** (`OcrDraftDto`).

     - **Header Suggestions**: Supplier, Document Number, Date, and Currency become interactive input fields.
     - **Item Suggestions**: Line items are rendered in an editable table.
     - **Auto-Calculation**: The `lineTotal` for each item and the "Grand Total" are automatically derived from `quantity * unitPrice`, ensuring mathematical consistency during human review.
     - **Draft Management**: Buyers can add new manual rows, remove suggested rows, or use the "Restaurar OCR" action to discard all edits and revert to the original extraction suggestions.
- **Review Notice**: A mandatory `AlertCircle` notice is displayed above the draft to remind the buyer that the data is an unverified suggestion and requires human review before persistence.
- **Context Preservation**: The OCR quality score and the original "extracted" status of fields remain visible even while the draft is being edited.
- **Operational Actions**: Workflow transitions (e.g., "Concluir Cotação") are grouped in an action bar below the quotation data for clear progression.
- **Quotation Financials (v1.1.26)**: The system enforces a simplified quotation model. Item-level operations track `GrossSubtotal` and `IvaAmount` strictly based on unit prices, quantities, and selected `IvaRateId` from Master Data. Discounts are NEVER applied per item; instead, a single `discountAmount` acts as a manual quotation-level scalar applied to the calculated global total in the footer.

- **Duplicate Supplier Prevention (v1.1.34)**: If a buyer attempts to save a quotation for a supplier that already has a quotation in the same request, the system triggers a **Duplicate Supplier Resolution Modal**.
  - **Overlay Pattern**: The modal renders as a centered overlay with a dark backdrop and high z-index (2000), ensuring it is never clipped or hidden behind footer elements.
  - **Corporate UI**: Uses the same visual language as `QuickSupplierModal` (refined borders, soft elevations, uppercase headers).
  - **Conflict Resolution**: Displays a comparison between the current draft and the existing quotation (Doc #, Date, Value) to help the buyer decide whether to replace or cancel.
  - **Atomic Replacement**: Replacing an existing quotation is an atomic backend operation that ensures the old quotation and its proforma are removed only after the new one is successfully persisted.

## Gestão### Independent Line Item Grid Endpoints (`/api/v1/line-items`)

The V1 API provides a `LineItemsController` to manage line items globally across all requests (powers the "Gestão de Cotações" grouped workspace).

### `GET /api/v1/line-items`

- **Purpose**: Global search and management of items.
- **Enforced Filter**: Server-side logic strictly filters results to only include Requests with status: `WAITING_QUOTATION`, `AREA_ADJUSTMENT`, `FINAL_ADJUSTMENT`, or `PAYMENT_COMPLETED`.
- **Visibility**: To support business rules, `QUOTATION` requests appear in the workspace even with zero line items, allowing buyers to add them during the quotation stage.
- **Bypass Prevention**: Client-provided filters are intersected with this allowed set.

- **Flat Architecture**: Unlike the Request List, this grid displays one row per *Line Item*, allowing bulk review and updates without navigating into individual requests.
- **Inline Editing (Skybow pattern)**: Users edit fields directly within the grid cell. Changes are auto-saved to the backend (`PATCH /api/v1/line-items/{id}`) and provide immediate visual feedback (green checkmark).
- **Temporary Role Gating (`X-User-Mode`)**: To facilitate testing before enterprise SSO/RBAC is implemented, the UI features a temporary toggle between "Comprador", "Aprovador de Área", "Financeiro" and "Recebimento". This sets the `X-User-Mode` HTTP header and controls UI visibility.
  - **Buyer Mode**: Can edit `Status` and `Supplier`. `Cost Center` is strictly read-only.
  - **Area Approver Mode**: Can edit `Cost Center`. `Status` and `Supplier` are strictly read-only.
  - **Financeiro / Recebimento Modes**: Are restricted to operational workflow actions and specific attachment management.
- **Future State**: The `X-User-Mode` dropdown is a *temporary artifact*. It will be removed once standard JWT-based Role-Based Access Control (RBAC) is implemented project-wide.

## Page Layout and Hierarchy (Header Architecture)

The project utilizes a unified, presentational `RequestActionHeader` component to ensure a consistent top-of-page experience across all request-related screens (Create, Edit, View). 

1. **Standard Workspace Width (v2.10.3)**: Primary request forms use a responsive layout with `width: 100%` and a balanced `maxWidth: 1440px`. This prevents "cramped UI" syndrome on standard laptops and desktops while ensuring focused readability on ultrawide monitors.
2. **AppShell Protection**: Containers must use `min-width: 0` to prevent horizontal overflow in the `AppShell`'s grid layout.

### RequestActionHeader Units

1. **Breadcrumbs (Row 1, Left)**: Visual navigation path showing current location (e.g., DASHBOARD / PEDIDOS / EDITAR). Uses uppercase, 700 weight, and subtle colors.
2. **Context Badges (Row 1, Right)**: Dynamic semantic indicators (e.g., "MODO DE CONSULTA", "REAJUSTE NECESSÁRIO") using `lucide-react` icons (ShieldAlert, ShieldCheck) and distinct background colors.
3. **Primary Row (Row 2)**:

## Action Bar and Button Standards

All primary action buttons in the sticky header and modals must follow these standards:

- **Heights**: 44px for secondary/utility buttons (Cancel, Save); 48px for primary workflow transitions and approval actions.
- **Typography**: `800` font weight, `UPPERCASE` labels, and `var(--font-family-display)`.
- **Emphasis**: `var(--color-primary)` for standard actions; `var(--color-status-red)` for destructive actions (Delete, Reject).

## Branded Confirmation Modals

User actions that result in permanent changes or status transitions must use project-standard "Brutalist" modals instead of browser-native `window.confirm()`.

  - **Corporate Modals**: Textareas and inputs must use soft elevations and consistent padding.
- **Implementation**: Utilize the standardized `showApprovalModal` pattern in components.

### Workflow Utilities (`src/frontend/src/lib/workflow.ts`)

To avoid duplicating complex transition logic, shared validations and backend calls are extracted into a central utility:

- **`completeQuotationAction`**: Standardizes the "Concluir Cotação" requirements:
  - Must have at least one line item.
  - Must have a `PROFORMA` attachment.
  - Must have a `Supplier` assigned to the request.
- **Usage**: Used by both `BuyerItemsList.tsx` (grouped view) and `RequestEdit.tsx` (detail view).

#### Post-Creation UX Patterns

- **Conditional Redirection**: Requests redirect based on type:
  - `QUOTATION`: To list view (items optional).
  - `PAYMENT`: To edit view with `?focus=items` (items required).
- **Interactive Signals**: The `?focus=items` parameter triggers a smooth scroll and a `pulse-highlight` animation on the targeted section.

#### Workflow Enforcement Patterns

- **Transition Validation**: Mandatory documents are enforced both on the client (within action modals) and the backend (during state transitions).
- **History Audit**: Document and line-item actions are recorded and displayed in the request timeline with localized labels for full auditability.

#### Buyer Workspace Data Pattern

- **Flat Detail Projections**: The `LineItemDetailsDto` is used to flatten request-level context (Requester, Deadline) into line-item rows for the Buyer Items List, reducing the need for nested API calls.

### Component Design

## Search and Filtering Patterns

1. **Standard Input Debouncing:** When implementing live search text fields that hit backend APIs directly, utilize a standard `useEffect` timeout (commonly 500ms) linking a fast local `searchInput` state to a delayed global `searchTerm` dependency. Never trigger sequential heavy dataset API calls on every keystroke natively without a buffer.
2. **Backend Delegation:** Filtering should execute as a `.Where()` clause on the backend via standard `?search=` parameters rather than executing raw client-side `.filter()` against gigantic cached memory arrays whenever feasible, enhancing enterprise list scaling.
3. **Multi-Dimensional Query Intersect:** When combining disparate filter modalities (like a text Search Box alongside a Status Dropdown), treat their parameters independently (e.g. `api.list(searchTerm, statusId)`). The backend is strictly responsible for running stacked AND (`.Where()`) queries processing all active filters collectively, never independently resetting lists.

## High-Cardinality Selection (Skybow Tabular Dropdown)

For fields requiring selection from large datasets (e.g., Suppliers, Materials), the **Skybow Tabular Searchable Dropdown** pattern is the project standard:

1. **Visual Consistency**: The closed state (trigger) must look identical to standard form selects (height, border, soft elevation).
2. **Tabular Results**: Results are displayed in a structured mini-table with explicit headers (e.g., `Portal`, `Primavera`, `Descrição`) and aligned columns.
3. **Selection Logic**: Use `onMouseDown` for row selection to avoid race conditions with blur/click-outside events. The entire row must be selectable.
4. **Integrated Search**: The search box is located *inside* the dropdown panel, not as the trigger itself. This box must `autoFocus` on open.
5. **Immediate Context**: Opening the dropdown immediately displays initial results (top 5).
6. **Clean Interaction**: Provide a clear indicator (Chevron) and a quick-clear (x) mechanism within the trigger area.
7. **Portal Overlay (Required)**: To ensure the dropdown is never clipped by scroll containers or parents with `overflow: hidden`, use **React Portals** via `ReactDOM.createPortal` (or the shared `DropdownPortal.tsx` component).
   - The panel must use `position: fixed` with dynamic coordinate calculation.
   - Use the `useDropdownPosition` hook to automatically track scroll-parent events and ensure viewport safety (automatic flipping and horizontal containment).

## List State Preservation

To ensure users never lose their place within deeply paginated or filtered datasets when navigating away to Detail or Edit screens, all primary Lists (like the Requests List) must enforce the following pattern:

1. **URL-Driven State:** Store current list coordinates (Search terms, Dropdown IDs, Pages, and Page Size sizes) structurally inside `react-router-dom`'s `useSearchParams` hook, mapping them natively out of `<input>` values upon change.
2. **Forwarding Location:** When rendering outbound `<Link>` components or routing away from a List, inject the pure, raw query URL onto the destination React Router `state` payload: `<Link to="/target" state={{ fromList: location.search }}>`.
3. **Retrieval and Restitution:** In the target component (Edit, Create, or sidebar Links), extract the injected location search parameters natively `<Link to={"/list" + (location.search?.fromList || "")}>` so that upon "Cancel" or "Return", the precise URL parameter state injects backwards verbatim, perfectly restoring the initial DOM render dynamically.
4. **Hardened State Consumption (Ref-Guarded Pattern) (v2.32.0):** To prevent race conditions, infinite re-render loops, or "white screen" hydration failures when consuming `location.state` (e.g., `successMessage`, `highlightItems`):
   - **One-Time Consumption:** Use a `useRef(false)` (e.g. `hasConsentedState`) to ensure the state-processing logic runs **exactly once** per component mount.
   - **Delayed Cleanup:** When clearing the transient state from the browser history via `navigate(replace)`, wrap the call in a `setTimeout(..., 0)`. This defers the navigation to the next event loop tick, ensuring the initial render cycle and any entry animations (e.g., Framer Motion) are stable before the history stack is modified.
   - **Structural Guards:** Always include a defensive "Data Not Found" block after the loading state to prevent UI crashes if background data fetching is interrupted or returns null during a rapid navigation transition.

### Expandable Table Row Pattern (v0.9.18+)

For displaying secondary but contextual information in list views (like status timelines) without navigating away:

1. **State Management**: Use `expandedRequestId` state to track the currently open row. Only one row should be expanded at a time to maintain focus.
2. **Toggle Logic**: Clicking anywhere on the main row (except for interactive elements) triggers the expansion.
3. **Event Shielding**: All interactive elements (Links, Buttons) inside the row MUST implement `onClick={(e) => e.stopPropagation()}` to prevent accidental expansion/collapse.
4. **Lazy Loading**: Content for the expanded area must be fetched only when the row is expanded to keep initial list load performance high.
5. **Visual Feedback**: The expanded state should be visually distinct (e.g., using a thicker border-left or a subtle background shift).
6. **Horizontal Workflow Pattern (v0.9.19)**:
   - **Visualization**: Use a horizontal progression with segmented connectors.
   - **Responsive**: Implement `overflow-x-auto` with a stable `min-width` (e.g., `850px`) to handle multiple steps/long labels on narrow screens.
   - **Markers**: Use explicit icons (Check) for completed steps and pulsing effects for the current step.
   - **Labels**: Constrain label width (e.g., `140px`) to prevent layout squashing and handle multiline Portuguese status names.

## Workflow Guidance and Urgency Visualization (v0.9.12)

The system provides visual cues to help users prioritize and process requests effectively.

### Responsibility Banner

- **Location**: In the `RequestEdit` page, a banner is displayed below the main header.
- **Content**: Shows the "Responsável Atual" (Current Owner) and "Próxima Ação" (Next Action).
- **Behavior**: Scrolls naturally with the content page.

### Urgency Highlighting

Requests in the list and details view are highlighted based on their `needByDateUtc` (Necessário Até).

- **Overdue**: Red highlight (Priority 3).
- **Due Today**: Orange highlight (Priority 2).
- **Due within 3 days**: Yellow highlight (Priority 1).
- **Normal**: No highlight (Priority 0).

### Post-Approval Operational Actions (v0.9.13+)

For **PAYMENT** requests, the workflow continues after final approval through several operational stages (e.g., PO Emission, Payment Scheduling).

1. **Operational Action Bar**: A dedicated sticky bar labeled **AÇÕES OPERACIONAIS** appears in `RequestEdit.tsx` for requests in `APPROVED` or subsequent operational stages.
2. **Context-Aware Actions**: This bar conditionally renders the required action(s) (e.g., "REGISTRAR P.O", "PAGAR") based on **both** current status and `requestTypeCode`.
3. **Stage-Based Gating (v0.9.20)**:
    - **Unified Logic**: Both `QUOTATION` and `PAYMENT` requests follow the sequence: `PO_ISSUED` -> `PAYMENT_SCHEDULED` -> `PAYMENT_COMPLETED` -> `WAITING_RECEIPT`.
    - **Manual Verification**: The bypass for Quotation requests originally proposed in v0.9.13 was **removed in Stage 5** to ensure full financial traceability.
    - **Implementation**: Visibility logic in `RequestEdit.tsx` uses precision booleans. `canMoveToReceipt` strictly enforces `PAYMENT_COMPLETED` for all request types, with ownership assigned to the `RECEIVING` role.
4. **Quotation Isolation**: Quotation requests have their own isolated action bar (e.g., "CONCLUIR COTAÇÃO") in the `WAITING_QUOTATION` stage.
5. **Hardened Backend Transition**: Backend endpoints (e.g., `move-to-receipt`) implement type-aware status validation, ensuring all types follow the payment completion rule.
6. **Attachment Workflow Enforcement (v0.9.21)**:
    - **Multi-Factor Gating**: Uploads are restricted by `statusCode` and `attachmentTypeCode`.
    - **Visibility Rules**:
        - `PAYMENT_SCHEDULE` and `PAYMENT_PROOF` sections are visible for both `QUOTATION` and `PAYMENT` requests during the operational phase once a P.O is issued.
        - `PAYMENT_SCHEDULE` is limited to `PO_ISSUED` and `PAYMENT_SCHEDULED`.
        - `PAYMENT_PROOF` is allowed in `PO_ISSUED`, `PAYMENT_SCHEDULED`, `PAYMENT_COMPLETED`, and `WAITING_RECEIPT`.
        - `ADICIONAR` and `REMOVER` (Trash) buttons are conditionally shown/hidden based on whether the current status allows that specific action.
    - **Persistence**: Historical attachments remain visible and downloadable even if the upload/delete actions are deactivated for the current status.
    - **Backend Validation**: The `AttachmentsController` strictly validates every upload attempt, returning a business-rule violation if the combination is invalid.
7. **Modal Feedback Consistency**: For all workflow actions triggered via confirmation modals, validation feedback must be presented inside the modal using the `modalFeedback` state to prevent overlay clipping.
8. **Status-Aware Guidance (v1.0.0)**:
    - **Messaging Refinement**: Generic alerts are replaced by a persistent context-aware guidance banner in `RequestEdit.tsx`.
    - **Categorization**:
        - **Draft/Rework**: Full editability is implied by the presence of all controls.
        - **Quotation**: "Edição Parcial" - Header locked, Supplier/Items editable.
        - **Locked**: "Modo de Visualização" - Request fully read-only.
        - **Operational Handover**: Once `PAYMENT_COMPLETED`, `WAITING_RECEIPT` or `IN_FOLLOWUP`, responsibility shifts to the **Recebimento** (RECEIVING) role.
    - **Implementation**: Uses precision booleans (`isDraftEditable`, `isQuotationPartiallyEditable`, `isFullyReadOnly`) to ensure UI affordances match the banner message.

> [!IMPORTANT]
> **Finalized Status Definition**: Requests are considered "Finalized" when they reach:
>
> - `COMPLETED`: For **PAYMENT** requests.
> - `REJECTED` or `CANCELLED`: For all types.
> Finalized requests are excluded from urgency highlighting and do not appear in "Attention Needed" filters.

### Document Attachment Standards (v0.9.15+)

All request-related documents must be managed via the `RequestAttachments.tsx` unified component, ensuring full auditability and metadata visibility.

1. **Metadata Display**: Every attachment must display its uploader and timestamp: "*Enviado por [Nome] em [Data/Hora]*".
2. **Workflow Validation**: Mandatory document presence (like Proforma) must be validated both on the frontend (client-side block) and backend (API block) before allowed transitions.
3. **Modification Tracking**: Document uploads and removals are considered **meaningful changes to the request**.
   - **Backend**: Every attachment action must update the parent request's `UpdatedAtUtc` timestamp to satisfy backend transition guards.
   - **Frontend**: Attachment actions must trigger a refresh call that sets the `hasSavedChanges` state to `true`, allowing the user to proceed with workflow actions without needing to edit other fields or items.
4. **Audit History**: Every document upload and removal must be recorded in the Request History using the standard pattern:
   - ACTION: `DOCUMENTO ADICIONADO` or `DOCUMENTO REMOVIDO`.
   - NEW STATUS: Preserve current status code.
   - COMMENT: For single-file uploads, specify filename and type. For batch uploads, use the aggregated format: *"N documentos do tipo [TypeLabel] adicionados ao pedido por [User]."*
   - **Aggregation Rule**: Multi-file uploads must produce exactly **one** history entry per upload action, not one per file. This keeps history clean and scannable.
5. **Immediate Feedback**: Actions within the attachment component must trigger an immediate `onRefresh` (wrapped to track changes) of the parent request details to synchronize the UI and history tracking.
6. **Multi-file Upload (v1.1.11)**: All file inputs in the project support `multiple` file selection. The user can select several files at once; all are uploaded as a single batch action.
7. **No Scroll Jump (v1.1.11)**: File inputs must **never** use the `disabled` attribute to block re-upload during an active upload. Instead, guard with an early return in the `onChange` handler. This prevents browser focus loss that would otherwise scroll the page to the top.
8. **Input Reset (v1.1.11)**: After every upload (success or failure), the file input value must be cleared (`e.target.value = ''`) so the user can re-select the same file if needed.

### Request List Sorting

The request list is sorted to prioritize active work:

1. **Urgency Priority**: Higher urgency (Overdue > Today > Soon) comes first.
2. **Attention Needed**: Requests requiring action from the current user or specific roles are prioritized.
3. **Creation Date**: Within the same urgency level, requests are sorted by `createdAtUtc` descending.

This visualization ensures that high-priority/overdue requests are immediately noticeable without adding visual noise or disrupting readability.

### Cross-Browser Layout Stability

To ensure consistent rendering between Chrome and Microsoft Edge (especially in complex grid layouts):

1. **Grid Track Sizing**: Avoid using simple `1fr` for columns that contain potentially wide children (like tables). Always use `minmax(0, 1fr)` to ensure the track can shrink below the children's `min-content` size, allowing internal scrollbars to function correctly.
2. **Flex Constraints**: Apply `min-width: 0` to flex and grid children that contain internal scrolling areas or large text. This breaks the default `min-content` width behavior that often leads to layout stretching in Edge.
3. **Table Wrapping**: Wide tables must always be wrapped in a `div` with `overflow-x: auto` and `width: 100%`. The table itself should have a stable `min-width` (e.g., `1200px`) to preserve readability.

### 15.4 Quotation Choice and Winners
- A workflow cycle may produce multitudes of Quotations per Request (`Quotation` entities).
- UI actions trigger `POST /api/v1/requests/{requestId}/quotations/{quotationId}/select` to enforce single-selection on the Backend.
- **Display priorities in `BuyerItemsList` segregate explicit selected wins (`COTAÇÃO ESCOLHIDA`) from raw heuristics (`Menor Valor`) to ensure unambiguous commercial decisions.**

## Administrator Workspace (Step 1 - v1.1.59)

The Administrator Workspace provides a dedicated area for technical management and system health monitoring, restricted to users with the `Administrador do Sistema` role.

### Access Control & Scaffolding

1. **Temporary Auth Scaffold**: During the initial transition to RBAC, the project uses a temporary `isAdmin()` helper in `src/frontend/src/lib/auth.ts`.
   - **Mechanism**: Checks `localStorage.getItem('dev_admin_scaffold') === 'true'`.
   - **Manual Activation**: Developers can toggle admin access in the console using `window.enableAdminScaffold(true)` or `window.enableAdminScaffold(false)`. This automatically reloads the page.
   - **Transition Rule**: This is a development-only artifact. It MUST be replaced by standard JWT-based Role-Based Access Control before production release.
2. **Modular Navigation Pattern**: Administrator features are integrated into the sidebar as a modular group named **"Administração"**.
   - **Group Identification**: `id: 'administracao'`.
   - **Consistency**: Follows the standard expand/collapse and active-state tracking used by "Compras" and "Operacional".
3. **Route Protection**: All administrative routes (`/admin/*`) are wrapped in an `AdminRoute` guard component.
   - **Behavior**: Unauthorized users attempting to access these routes directly by URL are automatically redirected to the `/dashboard`.
3. **Menu Visibility**: The "Workspace do Administrador" sidebar entry utilizes the same `isAdmin()` check to remain hidden from non-administrative users, preventing UI noise and accidental access attempts.

### Layout & Consistency

- **Header Patterns**: Admin pages follow the project's standard header hierarchy (Breadcrumbs > Icon + Title > Subtitle).
- **Navigation Tiles**: The main Admin Hub utilizes high-contrast tiles with soft elevations and upward hover transitions to maintain visual alignment with the Dashboard and Workspace patterns.
- **Section Isolation**: Administrative functionality is isolated within the `/admin` path to maintain a clean separation between business operations and system management.

---

## Operational Security Patterns

---

## Role-Aware Intelligence Pattern (DEC-084)

The `DecisionInsightsPanel` in the Approval Center uses **conditional rendering** based on the `approvalStage` prop (`'AREA' | 'FINAL'`) to adapt its emphasis without component duplication.

### Architecture
- **Single Component**: `DecisionInsightsPanel` receives `approvalStage` and an optional `requestData` context object.
- **Shared Foundation**: Alerts, Department KPIs, and Item Analysis blocks render for both roles.
- **Role Emphasis Blocks**: Area gets `AreaEmphasisBlock` (Checklist de Legitimidade), Final gets `FinalEmphasisBlock` (Visão Financeira Comparativa).
- **Section Reordering**: Shared blocks appear in different priority order per role.

### Extension Rules
- New role-specific sections should be added as conditional blocks within the existing component, not by forking.
- The `requestData` prop should remain lightweight — pass only what the emphasis blocks need, never the full `RequestDetailsDto`.
- Emphasis blocks are informational only — they must not introduce new approval blockers without a separate DEC entry.


---

## System-Resolved Workflow Actors (v2.29.0)

To minimize cognitive load on Requesters and ensure strict adherence to approval hierarchies, the frontend no longer provides manual actor selection:

1. **Area Approvers & Final Approvers**: These are strictly resolved by backend logic during submission based on the selected Department and Company, preventing bypasses.
2. **Buyer Self-Assignment**: Buyers are no longer manually picked by the Requester. New requests are created unbound (unassigned). 
3. **Unassigned Queue**: The BuyerItemsList.tsx Workspace groups unbound requests under a 'Não Atribuídos' tab. Active buyers monitor this queue and use the 'Atribuir a Mim' action to claim operational ownership of the request.

## Company Master Data (v2.30.0)

To support the system-resolved workflow actors, the "Dados Mestres" area now includes a dedicated "Empresas" tab.

1. **Company Listing**: Displays all companies with their ID, Name, and assigned Final Approver.
2. **Final Approver Assignment**:
    - **Filtered Selection**: The Final Approver field within the company form is a searchable dropdown that only displays users with the **Final Approver** role (`ROLES.FINAL_APPROVER`).
    - **Persistence**: This assignment is the authoritative source for the `FinalApproverUserId` used during request submission.
3. **CRUD Behavior**:
    - **Name Locking**: For existing companies, the `Nome` field is read-only if the company is already referenced by historical requests, preserving data integrity.
    - **Active/Inactive**: Companies can be toggled via the standard `handleToggleActive` pattern.
4. **Integration**: The `Companies` master data powers the plant-filtering logic and the header-level company selection in the Request Create/Edit forms.

## Finance Workspace (v2.39.0)

The Finance Workspace (`FinanceLandingPage.tsx`) acts as the dedicated operational cockpit for the treasury and accounts payable team, decoupled from the standard Buyer or Approver flows.

### Core Architecture

1. **Standalone API Segregation**: Data is sourced exclusively from `FinanceController.cs` returning targeted DTOs (`FinanceSummaryDto`, `FinanceListItemDto`), avoiding over-fetching from the generic Requests endpoints.
2. **Brutalist Presentation**: The UI strictly inherits the project's "Brutalist / Modern Corporate" standards, relying on direct `style={{ ... }}` implementations leveraging CSS variables (`var(--color-primary)`, `var(--shadow-brutal)`) rather than arbitrary Tailwind utility classes.
3. **Modular Sub-Navigation**: Features a left-aligned local navigation column bridging three primary modules:
    - **Visão Geral**: High-density KPI cards and an "Immediate Attention" panel.
    - **Pagamentos**: A dense, grid-based list of actionable payment requests.
    - **Histórico**: A read-only audit log of finance-authored actions.

### Technical & Business Rules

1. **Document Awareness (`Faltam Docs`)**: The workspace dynamically computes missing documentation requirements based strictly on workflow stage and request type.
    - *Proforma*: Flagged for quotations missing initial documentation.
    - *P.O.*: Flagged for requests missing formal purchase orders.
    - *Comprovativo*: Flagged for requests marked as paid but lacking a physical receipt upload.
2. **Action Handlers**: Permitted actions (Agendar, Pagar, Notas, Devolver) are accessible directly from the payment list via the portal standard `<KebabMenu />` component, optimizing bulk processing velocity. 
3. **Contextual Modals**: All explicit UI actions trigger the strictly native `<FinanceActionModal />` (`DropdownPortal`). No `window.prompt` or `window.confirm` dialogues are ever used in the business system. "Mark as Paid" executes a full (not partial) payment workflow transition.
