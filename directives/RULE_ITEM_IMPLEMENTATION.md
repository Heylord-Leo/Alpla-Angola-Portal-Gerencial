# RULE - Request Item Implementation

## Objective

To implement the vertical slice for managing request line items (`RequestLineItem`) within the existing **Request Edit** flow.

The user must be able to:
1. Add items to the request.
2. View current items in a list or grid.
3. Edit items.
4. Remove items.
5. View the correctly updated request total.

## Expected Inputs

- Source code in `C:\dev\alpla-portal`.
- Project documentation: `DECISIONS.md`, `API_V1_DRAFT.md`, `FRONTEND_FOUNDATION.md`, etc.
- Current `RequestEdit` implementation.
- Existing backend infrastructure for `Request` and `RequestLineItem`.

## Expected Outputs

- **Items** section implemented within `RequestEdit`.
- Grid or table displaying current request items.
- Functional add, edit, and remove actions.
- Consistently updated request total.

## Functional Rules

### 1. Correct Model
- Follow the defined **1:N** relationship between `Request` and `RequestLineItem`.
- Reuse existing entities and endpoints where possible.

### 2. Expected UX
- Use a **grid/list-first** approach for items.
- Keep UX clean and consistent with the current portal.
- **Modern Corporate Style**: Use `TailwindCSS` mapped to `tokens.css` (8px-12px radius, soft elevations).
- Reuse project-standard feedback patterns.

### 3. Status-Based Editing
- Allow item mutation only when the request is in an editable state (e.g., `DRAFT`, rework states).
- Hide or disable mutation actions in non-editable states.

### 4. Totals
- The backend should be the source of truth for totals whenever possible.
- Update the list and totals after any add, edit, or delete operation.

## Technical Implementation

### Backend
- Ensure endpoints exist for: listing items by request, creating, editing, and removing items.
- Return the updated total or enough data to reload correctly.

### Frontend (RequestEdit.tsx)
- Add the **Items** section.
- Render items in a simple table or grid.
- Implement add/edit/remove with user confirmation for deletions.
- Update the UI without a full page reload.

## Definition of Done
The task is complete if:
1. A `PAYMENT` request can receive at least 2 items.
2. Items persist after a page refresh.
3. The total recalculates correctly after add, edit, and remove.
4. The UI handles invalid input gracefully.
5. Item actions respect status/permission rules.
