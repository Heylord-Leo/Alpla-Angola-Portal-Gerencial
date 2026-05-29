# Guided Tour System

## 1. Purpose

The Guided Tour system helps users understand the Portal Gerencial interface through contextual tours. 

Tours can explain:
- the overall portal structure;
- module navigation;
- page-specific workflows;
- contextual overlays/drawers such as approval details.

## 2. Architecture Overview

The system architecture is designed in practical terms:
- **React Joyride** is used as the guided tour engine.
- Tours are defined as reusable tour definition files.
- Tours are registered in a central registry.
- Route-based tours are resolved by route prefixes.
- Contextual tours, such as drawer tours, are started manually because they only exist when a drawer or overlay is open.
- The provider is responsible for running Joyride, filtering/handling targets, and managing the active tour.

### Main Files and Responsibilities

- **`guidedTourTypes.ts`**: Defines the extensible type system (`TourId`, `TourLevel`, `TourDefinition`, `GuidedTourContextValue`).
- **`guidedTourRegistry.ts`**: The central registry mapping `TourId` to `TourDefinition`, and handling route resolution logic.
- **`useGuidedTour.ts`**: The core custom hook handling lifecycle logic, persistence, DOM filtering, Joyride callbacks, and dynamic scroll handling.
- **`GuidedTourProvider.tsx`**: React context provider rendering the Joyride overlay, Welcome Modal, and No-Steps Toast.
- **`GuidedTourButton.tsx`**: Topbar help button with a dropdown menu allowing users to choose Portal / Module / Page tours.
- **`GuidedTourContextButton.tsx`**: Inline page-header button for direct context tour launch.
- **`tours/`**: Folder containing individual tour definition files (e.g., `portalMainTour.ts`, `approvalDrawerAreaTour.ts`).

## 3. Tour Levels

The system organizes tours into hierarchical levels.

### Portal tours
Used for system-wide orientation, such as:
- `portal-main`

**Purpose**: Explain the general portal layout, topbar, sidebar, modules and help entry points. It auto-shows on first access.

### Module tours
Used for module-level guidance, such as:
- `module-purchasing-logistics`

**Purpose**: Explain the navigation, submenu and main sections of a module.

### Page tours
Used for screen-specific guidance, such as:
- `page-requests`
- `page-buyer-items`
- `page-receiving-workspace`
- `page-approvals-center`

**Purpose**: Explain how to use a specific screen.

### Drawer tours
Used for contextual overlays or drawers, such as:
- `drawer-approval-area`
- `drawer-approval-final`

**Purpose**: Explain content inside drawers or overlays that are only available after the user opens a specific item.

## 4. Tour Definition Structure

Tours are defined according to the `TourDefinition` shape.

### Page Tour Example

```ts
export const examplePageTour: TourDefinition = {
    id: 'page-example',
    level: 'page',
    label: 'Tour desta tela',
    routes: ['/example'],
    steps: [
        {
            target: '[data-tour="example-header"]',
            title: 'Page Header',
            content: 'This area identifies the page and its main purpose.',
            placement: 'bottom',
        },
    ],
};
```

### Drawer Tour Example

```ts
export const exampleDrawerTour: TourDefinition = {
    id: 'drawer-example',
    level: 'drawer',
    label: 'Tour do Drawer',
    routes: [], // Drawer tours are started manually, no route matching
    steps: [
        {
            target: '[data-tour="example-drawer-header"]',
            title: 'Drawer Header',
            content: 'This area summarizes the selected record.',
            placement: 'bottom',
        },
    ],
    scrollContainerSelector: '[data-tour-scroll-container="example-drawer"]',
};
```

## 5. Registry Rules

When creating a new tour, it must be registered in `guidedTourRegistry.ts`.

- Every new tour ID must be added to the `TourId` type.
- Route-based tours should include `routes` (route prefixes).
- Module, page, and portal tours are resolved by route prefix (`startsWith`).
- Drawer/contextual tours should be started manually (empty `routes` array).
- The registry should remain declarative; avoid hardcoded switch/case logic whenever possible.

**Expected Labels**:
- `"Tour inicial do Portal"` for portal tours
- `"Tour deste módulo"` for module tours
- `"Tour desta tela"` for page tours
- `"Tour da Aprovação"` (or similar contextual label) for drawer context

## 6. Naming Conventions

### Tour IDs
Tour IDs should follow this prefix format depending on their level:
- `portal-*` (e.g., `portal-main`)
- `module-*` (e.g., `module-purchasing-logistics`)
- `page-*` (e.g., `page-requests`, `page-approvals-center`)
- `drawer-*` (e.g., `drawer-approval-area`)

### Data-tour Anchors
`data-tour` anchors must be stable, descriptive and scoped to the page/component. 

**Do:**
- `requests-filter-button`
- `purchasing-kpi-cards`
- `approval-drawer-header`

**Don't** (avoid generic names):
- `header`
- `button`
- `card`
- `section`

## 7. Data-tour Anchors Behavior

All tour targets must use stable `data-tour` attributes.

**Rules**:
- Do not target dynamic text.
- Do not target request numbers, supplier names, status labels, values or translated text.
- Do not rely on generated class names.
- Prefer section containers, headers or stable action buttons.
- For dynamic cards/lists, target a stable wrapper or the first available card only if safe.
- For conditional sections, target the wrapper/header if it always exists.
- If a section may not exist, the tour must skip the step gracefully.

**Examples**:
```html
<section data-tour="requests-filter-button">...</section>
<div data-tour="approval-drawer-header">...</div>
```

## 8. Missing Target Behavior & Dynamic Steps

Tours must not break when a target does not exist. 

**Rules for Dynamic Steps:**
- Steps depending on real data, request cards, approvals, quotations or documents must be safe when data is missing.
- If the target does not exist, the step must be skipped gracefully (this is handled automatically by the `filterActiveSteps` function).
- If possible, target stable section headers instead of dynamic records.

**Examples of missing targets**:
- The user lacks access to a menu item.
- There are no requests in a list.
- There are no quotations.
- An approval drawer has no warning banners.
- A section is hidden by RBAC.
- A collapsible section is closed or not rendered.

**Expected behavior**:
- Invalid steps should be skipped gracefully.
- The remaining valid steps should still run.
- No target-not-found error should be visible to the end user.
- The tour should not force hidden elements to appear unless there is a safe page-specific preparation routine.

## 9. Scroll and Focus Behavior

The system supports two scroll modes depending on the tour level.

### Page scroll
- Used for portal, module and page tours.
- Compensates for sticky headers/topbar using window scroll.

### Drawer or container scroll
- Used for drawer-level tours.
- Must use a `scrollContainerSelector` when the target is inside a scrollable overlay.

**Drawer container attribute**:
```html
<div data-tour-scroll-container="approval-drawer">...</div>
```

**Behavior**:
- Targets must not be hidden behind sticky headers.
- Drawer targets must not be hidden behind drawer headers or sticky bottom action bars.
- The tooltip must remain readable.
- Drawer tours require custom scroll handling because browser window scrolling cannot scroll an overlay content. This is managed inside `useGuidedTour.ts` using the provided selector.

## 10. Creating a New Page Tour

1. Identify the route and page.
2. Add stable `data-tour` anchors to the UI components.
3. Create a new tour file in `src/frontend/src/features/guided-tour/tours/`.
4. Add the new tour ID to `TourId` in `guidedTourTypes.ts`.
5. Register the tour in `guidedTourRegistry.ts`.
6. Confirm the tour appears as "Tour desta tela" in the help menu.
7. Test with normal data, empty data, and restricted access profiles.
8. Run:
   ```bash
   npx tsc --noEmit
   npm run build
   ```

## 11. Creating a New Module Tour

1. Identify the module route prefixes.
2. Include sidebar/menu items when relevant.
3. Include main module dashboard/cockpit sections.
4. Add anchors to the module page and menu entries.
5. Create or update the module tour file.
6. Register it as a `module` tour in `guidedTourRegistry.ts`.
7. Confirm it appears as "Tour deste módulo".
8. Test with user profiles that have limited access.

## 12. Creating a Drawer Tour

1. Identify the drawer component.
2. Identify the drawer scrollable container.
3. Add `data-tour-scroll-container` to the scrollable container.
4. Add stable `data-tour` anchors to drawer sections.
5. Create a drawer tour definition.
6. Add the drawer tour ID to the `TourId` type system.
7. Register the drawer tour without route matching (`routes: []`).
8. Add a contextual tour button inside the drawer (e.g. `GuidedTourContextButton`).
9. Start the correct tour manually using `startTour(tourId)` from the `useGuidedTourContext()` hook.
10. Ensure starting the tour does not close, reload, or reset the drawer.
11. Test drawer scroll behavior thoroughly.

*(Reference: `drawer-approval-area` and `drawer-approval-final`)*

## 13. Page Preparation Before Starting a Tour

When a tour needs the page to be prepared before running, it triggers a custom event `guided-tour:prepare`.

**Rule**:
- Prepare the page first by listening to `guided-tour:prepare`.
- The system waits for React to render the required DOM (built-in delay in `executeTourStart`).
- Then it filters valid steps.
- Then it starts Joyride.

This avoids reduced/incomplete tours when data exists but is not expanded yet.

**Example**:
On *Gestão de Cotações*, if there is at least one request in the list but none is expanded, the page should automatically expand the first available request before starting the complete page tour.

```tsx
useEffect(() => {
    const handlePrepareTour = (e: Event) => {
        const customEvent = e as CustomEvent<{ tourId: string }>;
        if (customEvent.detail.tourId === 'page-buyer-items') {
            // Logic to prepare the page, e.g., expand the first item
            if (items.length > 0 && !expandedItem) {
                handleExpand(items[0].id);
            }
        }
    };

    window.addEventListener('guided-tour:prepare', handlePrepareTour);
    return () => window.removeEventListener('guided-tour:prepare', handlePrepareTour);
}, [items, expandedItem, handleExpand]);
```

## 14. Copywriting Guidelines

- **CRITICAL RULE**: User-facing tour copy must *always* be in Portuguese, even though project documentation is written in English.
- Keep tooltip text concise.
- Use operational language.
- Explain what the user should understand or do.
- Avoid technical terms.
- **Do not** mention React, DOM, selectors, Joyride, or implementation details in user-facing tour text.

## 15. Access and RBAC Guidelines

Tours must strictly respect user access.

**Examples**:
- If a user cannot see a submenu, do not target it.
- If a user only has access to Area Approval, Final Approval-specific steps should not appear.
- If a section is hidden by permissions, continue with the remaining valid steps.
- Do not add tours for DEV/debug-only sections.

> [!WARNING]
> The DEV Seed/debug area must not be included in guided tours.

## 16. Verification Checklist

Use this standard checklist for any tour change:

- [ ] Confirm the tour appears in the correct context.
- [ ] Confirm each step highlights the expected area.
- [ ] Confirm scroll/focus is correct.
- [ ] Confirm the tour works with empty data.
- [ ] Confirm the tour works with restricted user permissions.
- [ ] Confirm missing dynamic sections do not break the tour.
- [ ] Confirm Portuguese copy is clear and concise.
- [ ] Confirm no DEV/debug-only section is included.
- [ ] Run: `npx tsc --noEmit`
- [ ] Run: `npm run build`
- [ ] Restart backend and frontend when manual verification requires it.

## 17. Current Known Tours

The active registered tours are:

- `portal-main`
- `module-purchasing-logistics`
- `page-requests`
- `page-buyer-items`
- `page-receiving-workspace`
- `page-approvals-center`
- `drawer-approval-area`
- `drawer-approval-final`

## 18. Maintenance Rules

- Every new tour should be documented.
- Every new tour ID must be typed.
- Every new `data-tour` anchor should be stable and meaningful.
- Every tour must be tested with missing targets.
- Drawer tours require drawer-aware scroll handling.
- Avoid targeting dynamic data.
- Avoid including DEV/debug-only UI.
- When a tour changes user-facing behavior, update `FRONTEND_FOUNDATION.md` only if it contains guided tour standards that need alignment.
- Keep `CHANGELOG.md` and `VERSION.md` aligned if a version bump is made.

---

## 19. Live Guide System (Extension)

### 19.1 Overview

The Live Guide system extends the Guided Tour architecture to support **interactive task guidance** — step-by-step walkthroughs that help users complete real tasks (e.g., filling forms) while validating each step before allowing progression.

| Concept | Guided Tour | Live Guide |
|---------|-------------|------------|
| Purpose | Explain UI elements | Guide task execution |
| Validation | None | Per-step, blocking |
| Attribute | `data-tour` | `data-guide` |
| Control | Joyride auto-progression | Manual step control |
| ID Type | `TourId` | `LiveGuideId` |
| Start | Auto (welcome) or manual | Always manual |
| Screen Impact | Read-only overlay | User interacts with real form |

### 19.2 Architecture

```
LiveGuideProvider (context + controlled Joyride)
  └── useLiveGuide (hook — lifecycle, validation, step control)
       ├── liveGuideTypes.ts (type definitions)
       ├── liveGuideRegistry.ts (metadata registry + route matching)
       ├── liveGuideStorage.ts (localStorage persistence)
       └── guides/
            └── requestCreation.liveGuide.ts (factory function)
```

- **LiveGuideProvider** is nested inside `GuidedTourProvider`, sharing the same provider tree.
- **Joyride** is used in controlled mode (`continuous={false}`, `stepIndex` controlled) exclusively for spotlight, overlay, and positioning.
- The **custom tooltip** (via `tooltipComponent`) handles all UI: step content, validation indicators, navigation buttons.
- Step transitions are managed by the `useLiveGuide` hook, not by Joyride's internal state.

### 19.3 Type System

```typescript
type LiveGuideId = 'request-creation-live-guide' | 'quotation-management-live-guide';
type RequiredAction = 'input' | 'select' | 'upload' | 'click' | 'none';
type LiveGuideStatus = 'completed' | 'cancelled' | 'not-started';
```

### 19.4 Data Attributes

Live Guides use `data-guide` attributes (never `data-tour`):

| Attribute | Target |
|-----------|--------|
| `data-guide="request-form"` | The `<form>` element |
| `data-guide="request-title"` | Title input label |
| `data-guide="request-description"` | Description textarea label |
| `data-guide="request-documents"` | Documents section |
| `data-guide="request-type"` | Request type select |
| `data-guide="request-need-level"` | Need level select |
| `data-guide="request-department"` | Department select |
| `data-guide="request-company"` | Company select |
| `data-guide="request-plant"` | Plant select |
| `data-guide="request-submit"` | Submit button |

### 19.5 Step Validation

Each step can define:
- `requiredAction` — what type of interaction is needed.
- `validate()` — returns `true` when the step's condition is satisfied.
- `validationMessage` — Portuguese message shown when blocked.
- `allowSkip` — if `true`, the user may bypass validation.

Validation is evaluated every 300ms while the guide is active.

### 19.6 Guide Factory Pattern

Guide definitions are created via factory functions that receive a `getFormValues()` getter, avoiding tight coupling between the guide system and page component state.

```typescript
const guide = createRequestCreationGuide(() => ({
    title: formData.title,
    description: formData.description,
    // ...
}));
```

Pages register their factory via `useLiveGuideRegistration()` and unregister on unmount.

### 19.7 Entry Points

Live guides can be started from:
1. **Inline button** — `LiveGuideLauncher` component placed in the page header.
2. **Topbar help dropdown** — `GuidedTourButton` automatically shows live guides available for the current route under a "Guias Interativos" section.

### 19.8 Registered Live Guides

| ID | Module | Route | Description |
|----|--------|-------|-------------|
| `request-creation-live-guide` | requests | `/requests/new` | 12-step guide for creating a new request draft |
| `quotation-management-live-guide` | buyer | `/buyer/items` | 11-step assistive guide for the quotation management workspace |

### 19.9 Adding a New Live Guide

1. Create a new file in `live-guide/guides/` with a factory function.
2. Add the `LiveGuideId` to the union type in `liveGuideTypes.ts`.
3. Add a metadata entry in `liveGuideRegistry.ts`.
4. In the target page, register the factory via `useLiveGuideRegistration()`.
5. Add `data-guide` attributes to the target elements.
6. Optionally add a `LiveGuideLauncher` button.
7. Document the new guide in this file.
