---
description: Official task finalization, logging, and commit/push
---

> [!IMPORTANT]
> This workflow follows the standard operating procedure defined in:
> 👉 **[directives/SOP_TASK_CLOSING.md](file:///C:/dev/alpla-portal/directives/SOP_TASK_CLOSING.md)**
>
> This is the **ONLY** authorized workflow for `git commit` and `git push`.

1. **Validation**
   - Ensure all steps from `/task-review` are satisfied and the work is stable.

2. **Guided Tour Impact Check**
   - Check if the task added, removed, renamed, moved or changed screens, menus, submenus, routes, drawers, modals, quick overview panels, workflow functions, buttons, cards, action menus, or guided UI sections.
   - If a task introduces a new user-facing screen, route, module, menu, submenu, drawer, modal, quick overview panel, workflow function, or important action area, and no guided tour exists for it, the agent must create the appropriate tour before publishing.
   - Examples:
     - New page added → create a page tour.
     - New module added → create a module tour.
     - New drawer/quick overview added → create a contextual drawer tour.
     - New important workflow action added to an existing page → update the existing page tour.
     - New important section added to an existing module dashboard → update the module/page tour.
   - If a change was made, inspect and update the affected tour definitions under `src/frontend/src/features/guided-tour/tours/`.
   - Check and update `src/frontend/src/features/guided-tour/guidedTourRegistry.ts` and `src/frontend/src/features/guided-tour/guidedTourTypes.ts` as needed.
   - Remove or update obsolete tour steps that point to removed UI elements.
   - Add new stable `data-tour` anchors when the new UI element is important for user orientation or workflow understanding.
   - Verify missing-target behavior so tours do not break when RBAC hides a section or when data is empty.
   - Follow: 👉 **[docs/GUIDED_TOUR_SYSTEM.md](file:///C:/dev/alpla-portal/docs/GUIDED_TOUR_SYSTEM.md)**
   
   **Guided Tour checklist**:
   - [ ] Did this task add/remove/rename a route, page, menu or submenu?
   - [ ] Did this task add/remove/change a drawer, modal or quick overview panel?
   - [ ] Did this task add/remove/change important buttons, cards, filters, action menus or workflow actions?
   - [ ] Are existing tour steps still pointing to valid data-tour targets?
   - [ ] Are new data-tour anchors needed?
   - [ ] Do tours still work with empty data and restricted user access?
   - [ ] Was docs/GUIDED_TOUR_SYSTEM.md followed?
   - [ ] Was the Guided Tour impact result documented in the publish walkthrough?

   > [!IMPORTANT]
   > You MUST NOT publish until the Guided Tour impact is fully handled and explicitly documented in your walkthrough with one of the following exact phrases:
   > - "Guided Tour impact: not applicable."
   > - "Guided Tour impact: existing tour reviewed, no changes needed."
   > - "Guided Tour impact: existing tour updated."
   > - "Guided Tour impact: new tour created and registered."

3. **Persistence**
   - Finalize documentation (`CHANGELOG.md`, `VERSION.md`), update the UI Version in frontend `config.ts`, and perform Git commit/push.
   - If the Guided Tour changed user-facing behavior, ensure related documentation is updated if needed.

4. **Convention**
   - Use **Conventional Commits** (`feat:`, `fix:`, `docs:`, etc.) for the message.

5. **Safety**
   - Only push to `origin/main` when the task is fully coherent and verified.
