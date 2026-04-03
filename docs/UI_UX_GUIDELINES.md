# UI/UX Guidelines

## Overview

This document defines the visual foundation and user experience standards for the Alpla Angola internal web portal. The system is designed to be **desktop-first**, **productivity-oriented**, and built as a **modular system** utilizing forms, approvals, lists, and dashboards.

## A) Visual Direction

The visual style is a **Modern Corporate** identity, designed for premium internal efficiency while respecting the global ALPLA brand.

- **Corporate Brand Identity (ALPLA Shell 2.0):** Adopts the official refined corporate color palette (Deep Blue), high-end typographic hierarchy (Montserrat), and a professional, fluid tone.
- **Layout & Structure:** Adopts a clean, card-based structural containment with soft elevations, rounded corners (8px-12px), and a collapsible sidebar navigation for maximized workspace.
- **Migration Guidance:**
    - Legacy screens may temporarily coexist with older "Industrial Brutalist" styles.
    - All new or refactored screens must strictly follow the Modern Corporate standard.
    - Migration of legacy components will happen in phased cycles.

## B) Color Palette

*Note: Brand-specific ALPLA colors below are **Proposed (not officially confirmed)** based on public branding analysis.*

### Brand Colors

- **Primary:** `#003568` *(ALPLA Deep Blue)* — Used for primary actions, active states, and key highlights.
- **Secondary:** `#004C90` *(ALPLA Light Blue)* — Supporting color for hovers and secondary interactive elements.
- **Accent:** `#0054cb` *(ALPLA Accent)* — Functional highlight for specific UI call-outs.

### Neutral Colors (Office Context)

- **Background:** `#F8FAFC` (Slate 50) — Soft background to reduce eye strain during 8-hour usage.
- **Surface:** `#FFFFFF` (White) — For cards, modals, and content containers.
- **Border / Divider:** `#E2E8F0` (Slate 200) — Subtle structural definition.
- **Muted Text / Icons:** `#64748B` (Slate 500) — For secondary info, placeholders, and inactive states.
- **Primary Text:** `#1E293B` (Slate 800) — Dark, highly readable text (avoiding pure `#000000`).

### Status Colors

- **Pending (Warning):** `#F59E0B` (Amber) — Awaiting review or user action (e.g., waiting for manager approval).
- **Approved (Success):** `#10B981` (Emerald) — Successful completion or positive state.
- **Rejected (Danger/Error):** `#EF4444` (Rose) — Rejection, failure, or critical destructive actions.
- **Draft (Neutral):** `#94A3B8` (Slate 400) — Unsubmitted or saved items. User is still actively working on it but hasn't submitted yet.
- **Info:** `#3B82F6` (Blue) — Informational alerts, neutral system updates, or read-only states that don't imply "in-progress" like Drafts do.

*Clarification:* Use **Warning/Pending** (`#F59E0B`) to signal that a workflow is paused and requires intervention. Use **Accent** (`#FFB81C`) strictly as a UI highlight (like a "New Feature" badge or a specific call-to-action), not to indicate process status.

### Urgency Colors (Attention System)

Used for highlighting entire rows in list views to signal operational priority based on deadlines.

- **Overdue (Vencido):** `#fee2e2` (Light Red) — Deadline has passed.
- **Due Today (Hoje):** `#ffedd5` (Light Orange) — Deadline is within the current UTC day.
- **Due Soon (Próximo):** `#fef9c3` (Light Yellow) — Deadline is within the next 3 days.

*Logic:* Finalized requests (Completed, Rejected, Cancelled) are explicitly excluded from this highlighting to maintain focus on active work.

- **Primary Heading Font:** `Montserrat`, system-ui, sans-serif
- **Primary Body Font:** `Inter`, system-ui, sans-serif

### Typography Tokens

For the initial implementation on Windows desktop environments, fallback explicitly to web-safe sans-serif fonts to ensure consistent rendering without relying on custom font loads initially.

- **Headings (V1):** `'Trebuchet MS'`, `'Segoe UI'`, `Arial`, `sans-serif`
- **Body (V1):** `'Segoe UI'`, `'Helvetica Neue'`, `Arial`, `sans-serif`

### Sizing Scale (Desktop)

- **H1 (Page Title):** 24px / 1.5rem, Bold (Futura)
- **H2 (Section Heading):** 20px / 1.25rem, Semi-Bold (Futura)
- **H3 (Card/Modal Title):** 16px / 1rem, Medium (Futura)
- **Body / Primary Text:** 14px / 0.875rem, Regular (Helvetica)
- **Dense Table Text:** 13px / 0.8125rem, Regular (Helvetica) - *For compact views*
- **Small / Metadata Text:** 12px / 0.75rem, Regular (Helvetica)

## D) Design Tokens

Implementation-ready tokens to maintain strict consistency.

- **Spacing Scale:** `4px`, `8px`, `12px`, `16px`, `24px`, `32px`, `48px`. Ensure all paddings and margins use these increments.
- **Border Radius:**
  - `4px` (Small utility elements)
  - `8px` (Main buttons, inputs, components)
  - `12px` (Cards, Modals, content containers)
  - `9999px` (Pills, Badges, Avatars)
- **Shadows (Soft Elevation):**
  - `sm`: `0 1px 2px 0 rgb(0 0 0 / 0.05)` — Interactive cards and surface containers.
  - `md`: `0 4px 6px -1px rgb(0 0 0 / 0.1)` — Dropdowns, popovers, and elevated states.
  - `lg`: `0 10px 15px -3px rgb(0 0 0 / 0.1)` — Modals, Drawers, and high-level overlays.
- **Focus Ring:** `2px solid #004C90` with `2px` offset. Mandatory for accessibility.
- **Z-index Scale (Standardized Hierarchy):**
  - `Base`: 1
  - `Overlay`: 10 (Sticky elements, small overlays)
  - `Sidebar/Topbar`: 100-110 (Main shell)
  - `Dropdown/Popover`: 1000
  - `Drawer`: 1100
  - `Modal`: 1200
  - `Tooltip`: 1300
  - `Toast/Notification`: 1400

*Implementation Rule:* Use the `Z_INDEX` constants from `src/frontend/src/constants/ui.ts` bridged with `tokens.css` variables.

## E) Layout Standards (Desktop-First)

Optimized for wide monitors and data-heavy operations.

- **Sidebar Width:** `260px` (Expanded) / `80px` (Collapsed/Icon-only).
- **Topbar Height:** `64px`. Contains breadcrumbs, global search, and profile.
- **Content Max-Width:** Fluid (`100%`) by default for tables, but bounded to `1440px` for forms to prevent excessively wide text lines.
- **Page Header Pattern:**
  - Top-Left: Breadcrumbs (above) + H1 Page Title (below).
  - Top-Right: Primary Page Actions (e.g., "Add New").
- **Filter/Action Bar:** Located immediately below the Page Header. Contains search, dropdown filters, and table view toggles.
- **Sticky Table Header:** Mandatory for operational lists so headers remain visible during vertical scrolling.
- **Form Layout:** Sectioned into Cards. Maximum of 2 columns for inputs to maintain readability.

## F) Component Standards (Enterprise Focus)

- **Buttons:**
  - *Primary:* Solid `#004C90` background, white text. Main action only (max 1 per form).
  - *Secondary:* Outlined or light gray background. For "Cancel", "Back", etc.
  - *Danger:* Solid `#EF4444`. For destructive actions (e.g., "Delete Request"). Requires confirmation modal.
- **Inputs:** `4px` border radius, `#E2E8F0` border. Must show a `#004C90` focus ring. Floating labels or top-aligned labels (preferred for scanning).
- **Validation:** Error states must turn borders/text `#EF4444` and include explicit descriptive helper text below the input.
- **Tables:** Zebra-striping or row-borders. Includes a *Dense Mode* toggle for high-volume data scanning. Row-level actions (Edit, Approve, View) placed in the far-right sticky column.
- **Cards:** White surface with standard `sm` shadow and `8px` radius. Used to group related form fields or dashboard metrics.
- **Modals / Drawers:** Modals for quick confirmations/small edits. Side-drawers (sliding from right) for complex filtering or quick-viewing an item without losing list context.
- **Tabs:** Underline style, highlighting the active tab with the primary blue `#004C90`.
- **Badges:** Small pills with status colors (Background: `10%` opacity of status color, Text: `100%` status color) for immediate visual recognition.
- **Notifications / Toasts:** Slide in from bottom-right or top-right. Auto-dismiss after 5s (unless error).
- **Empty & Loading States:** Empty tables must show a friendly "No data found" illustration/icon. Data fetches must use skeleton loaders matching the table/card structure, avoiding full-page spinners where possible.
- **Attachments:**
  - *Upload Area:* Drag-and-drop zone with a clear file browser button. Provide immediate feedback on file type/size validation errors.
  - *List Row Pattern:* Display attachment `Name`, `Size`, `Uploaded By`, and `Upload Date`.
  - *Actions:* Row actions must include "Preview" (if applicable), "Download", and "Remove" (if permitted).
  - *Errors:* Inline red error text below the specific failed upload, clearly stating the reason (e.g., "File exceeds 10MB limit").

## G) Screen Patterns

Reusable layout templates tailored for Alpla Angola workflows:

1. **List Page:** Page Header -> Filter Bar -> Sticky Table -> Pagination.
2. **Request Form Page:** Page Header -> Two-column Card(s) for input -> Bottom Sticky Action Bar (Save Draft, Submit).
3. **Detail / Approval Page:** Header (with Status Badge) -> Summary Card (Left) + Interaction/Action Panel (Right) -> History/Comments Timeline (Bottom).
4. **Dashboard Page:** Header -> Top Row: KPI Summary Cards (4 cols) -> Middle Row: Main Charts (e.g., 2 cols) -> Bottom Row: Operational "Action Needed" Table.

### Approval Timeline / Audit Trail Pattern

Must be present on Detail/Approval pages for full transparency.

- **Required Fields per Entry:** Date/Time, User Name, Action Taken (e.g., "Submitted", "Approved"), Comment (if provided), Resulting Status.
- **Visual Structure:** A vertical timeline (connected line with dots) or a structured list below the main content.
- **Long Content:** Truncate comments longer than 3 lines with a "Read More" expander. Attached files tied to a specific timeline event should appear as small, clickable pills directly below the comment.

### Permission-Aware UI Behavior

- **Action Visibility:** Hide actions (like "Approve" or "Delete") completely if the user's role *never* has permission for them.
- **Disabled Actions:** If a user *has* the role permission, but the action is invalid for the current state (e.g., trying to approve a Draft), show the button as **Disabled** and provide helper text or a tooltip explaining *why* (e.g., "Only pending requests can be approved").
- **Consistency:** The logic for action availability must be identical across List views (row actions), Detail views (action panels), and Forms.

## H) Language and Content Style

- **Primary Interface Language:** Portuguese (PT) for V1 rollout.
- **Terminology Consistency:**
  - "Approve" -> `Aprovar`
  - "Reject" -> `Rejeitar`
  - "Submit" -> `Enviar` / `Submeter`
  - "Draft" -> `Rascunho`
  - "Pending" -> `Pendente`
- **Tone:** Direct, professional, and clear. Avoid overly conversational text.
- **Error Messages:** Must explain *what* went wrong and *how* to fix it (e.g., "O campo 'Data' é obrigatório", not just "Erro.").

## I) Accessibility & Office Readability

For users staring at the screen for 8 hours a day:

1. **Contrast:** Strict adherence to WCAG AA (4.5:1 ratio for standard text).
2. **Focus Management:** Absolute visibility for keyboard nav (`2px` focus rings).
3. **Click Targets:** Minimum `40x40px` for interactive icons/buttons.
4. **Color Independence:** Never use *only* color for meaning. "Rejected" elements need both red color and an icon (e.g., ✖) or text label.
5. **Breathing Room:** Do not artificially condense data unless the user explicitly toggles "Dense Mode." Default spacing prevents misclicks and eye strain.

## J) Tailwind CSS Integration (Shell 2.0)

As of the **Shell 2.0** redesign phases, Tailwind CSS v4 is used as a functional styling extension mapped to existing CSS variables (`tokens.css`) via the `@tailwindcss/postcss` plugin.

1. **No-Prefix Strategy**: Tailwind utilities are applied natively without a prefix (e.g., `flex`, `p-4`, `bg-white`).
2. **Variable Mapping**: Base colors, spacing, typography, and soft elevation tokens from `tokens.css` are configured as themes in `tailwind.config.js` (e.g., `theme: { colors: { 'portal-primary': 'var(--color-primary)' } }`).
3. **Restricted Usage**: Tailwind is strictly confined to new `V2` components or modernized redesign bounds (e.g., `AdministratorWorkspace`, `MasterData`, `SystemLogs`). Do not inject Tailwind into legacy core structural wrappers or globally redefine fundamental elements in `index.css`.
