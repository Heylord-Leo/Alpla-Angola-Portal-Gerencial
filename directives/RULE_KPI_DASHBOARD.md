# Directive: KPI Dashboard and Summary Cards Standard

This directive establishes the mandatory pattern for implementing KPI and summary cards within the Alpla Portal. It ensures visual consistency, interaction parity, and data integrity across all dashboard-style interfaces.

## 1. Operational Purpose

- **Meaningful Summaries**: Cards must represent actionable operational metrics.
- **Interactive Shortcuts**: Cards should provide click-to-filter functionality for the main entity list on the page.
- **Management Value**: Avoid purely decorative cards; every card must serve a clear monitoring or navigation purpose.

## 2. Data Integrity & Source of Truth

- **Unified Source**: KPI counts MUST be derived from the same base query or API contract as the main list/table to prevent divergence.
- **Server-Side Bundling**: Prefer returning summary data alongside the paged list results in a single response DTO (e.g., `RequestListResponseDto`).
- **Consistency Guarantee**: If a separate endpoint is used, it must apply the exact same filtering logic as the list endpoint.

## 3. Interaction & State Management

- **Key Separation**: The **Display Label** (e.g., "Aguardando Aprovação") must be treated as a separate concept from the **Normalized Filter Key** (e.g., "Em Aprovação").
- **Centralized Mapping**: For cards representing a group of statuses (e.g., "Finalizados"), the mapping of codes -> group labels must be centralized in the component configuration.
- **Active State Drive**: The active visual state must be driven by current URL/state filter parameters, not internal component state.
- **URL Restoration**: Clicking a card must update the URL. On page load, the card highlight must be restored by parsing the current URL parameters.
- **Single Selection**: Only one summary card should be "active" at a time in standard list views.

## 4. Selection Logic (Handling Groups)

- **Robust Matching**: Use a subset-based matching logic: a card/chip is active if ALL currently selected status IDs map to codes that belong to that card's group.
- **Implementation Pattern**:

```tsx
if (chip.activeCodes.length > 0 && selectedCodes.every(c => chip.activeCodes.includes(c))) {
    return chip.label;
}
```

## 5. Modern Corporate Tone
 
- Use the **Shell 2.0** layout (collapsible navigation).
- **Presentation Layer**: Static grids of raw data are deprecated. Operational dashboards must utilize dynamic `ActionCarouselWidget` containers for urgent requests, featuring interactive sliding items and `framer-motion` implementations.
- **Cards & Floating Actions**: `var(--radius-lg)` (8px-12px rounded corners), soft elevations (`var(--shadow-sm)` and `var(--shadow-lg)` on hover). Kebab Menus must use elegant regular font weights and light blue generic hover patterns (`#f1f5f9`), completely abandoning brutalist thick borders or uppercase typography.
- **Emphasis**:
    - **Active Elements**: May use blue (`var(--color-primary)`) for active indicators.
    - **Status Grouping**: Dropdowns must use grouped contextual sets (e.g. INICIAL, FINANCEIRO) rather than raw unorganized IDs.
- **Typography**: Refined hierarchy (sans-serif fallbacks with normalized case tracking, explicitly avoiding `textTransform: uppercase` in standard data menus).
- **Styling Architecture**: Rely cleanly on mapped inline CSS attributes tied to system variables (e.g., `backgroundColor: 'var(--color-bg-surface)'`), officially severing required layout dependency on Tailwind CSS utility sprawls in root operational layouts.

## 6. Architecture

- **Component Reuse**: Use the project-standard `KPICard` and `KPISummary` components.
- **Config-Driven**: Define card sets as arrays of objects with `id`, `label`, `filterKey`, and `icon`.
- **Responsive Grid**: Use CSS Grid with `repeat(auto-fit, minmax(240px, 1fr))` to ensure layout stability across screen sizes.
