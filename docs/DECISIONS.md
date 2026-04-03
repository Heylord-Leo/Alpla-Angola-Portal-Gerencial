# Decisions Log

Purpose: record important technical and process decisions so future work preserves context.

---

## DEC-091 — Official Move to Modern Corporate Design Language

- **Date:** 2026-04-03
- **Status:** Accepted
- **Context:** The project's visual direction needed to evolve from the initial "Industrial Brutalist" phase to a more professional, premium, and executive-focused design standard that aligns with ALPLA's global corporate identity.
- **Decision:** Officially transition the portal's UI/UX direction to **Modern Corporate**.
    - **New Default Standards**:
        - **Rounded Corners**: 8px or 12px as the new standard (replacing the 0px default).
        - **Soft Elevations**: Low-opacity, diffused shadows (replacing the "brutal" high-contrast shadows).
        - **Subtle Borders**: Lighter borders (Slate 200) for containers, avoiding heavy default blue borders.
        - **Blue as Accent**: Blue is strictly reserved for primary actions, indicators, and focus states.
    - **Retired Standards**:
        - **Industrial Brutalist** is no longer the official direction.
        - **0px radius** is deprecated as a default.
        - **Brutal/Heavy shadows** are no longer used for base components.
        - **Heavy blue borders** on all containers are discontinued.
    - **Migration Policy**: Existing screens will coexist with the new standards and be migrated in phases. All new or refactored screens must follow the Modern Corporate direction.
- **Alternatives considered:** Maintaining Industrial Brutalist (rejected as less suitable for a broad corporate audience).
- **Consequences:** Provides a more premium and accessible interface. Requires a phased refactor of core design tokens and shell components in the next implementation cycle.

---

## DEC-092 — Modern Corporate UI Refinement (Phase 2 Implementation)

- **Date:** 2026-04-03
- **Status:** Accepted
- **Context:** Following the establishment of the visual foundation (Phase 1), the system required a second phase of focused refinement on core operational screens (Dashboard, Requests List, Request Edit, Receiving Workspace) to ensure high-fidelity corporate standards and premium interactive quality.
- **Decision:** Implement specialized refinements across high-traffic operational areas:
    - **Premium Shadows**: Introduced `var(--shadow-premium)` (a multi-layered, ultra-soft indigo-tinted shadow) for primary entrance points like the Login card and Dashboard summary sections.
    - **Typography Density**: Standardized on `900` font-weight for all primary screen headers and section titles to create a strong, authoritative hierarchy.
    - **Header Consolidation**: Replaced fragmented header styles with a unified "Page Header" pattern featuring subtle bottom borders and consistent vertical rhythm.
    - **Operational List Refinement**: Transitioned the `RequestsList` from a high-contrast grid to a "Soft-Row" pattern—using `8px` rounded corners, lighter borders (`0.08` opacity), and subtle hover backgrounds.
    - **Interactive States**: Standardized hover/focus states to use soft tints of the corporate blue palette instead of the previous high-contrast brutalist borders.
- **Alternatives considered:** Full-page redesigns (rejected as too disruptive to existing user workflows).
- **Consequences:** Results in a significantly more mature and cohesive enterprise-grade interface. Maintains operational density while improving overall visual comfort and perceived quality.

---

## DEC-072 — Structural Root Grid for Shell Continuity

- **Date:** 2026-03-26
- **Status:** Accepted
- **Context:** Initial migration to Shell 2.0 resulted in a "floating card" sidebar and misaligned topbar on large screens due to max-width constraints on the outer shell.
- **Decision:** Transition to a full-viewport root grid in `AppShell`.
    1. Grid: `grid-template-columns: var(--sidebar-width) 1fr`.
    2. Sidebar: Direct grid child with `grid-row: 1 / -1`.
    3. Content: `maxWidth` moved to an inner wrapper strictly for the business area.
- **Alternatives considered:** Using `position: fixed` for all shell elements. Rejected as it creates brittle compensation logic for sidebar width changes.
- **Consequences:** Ensures the application frame always spans the full viewport while keeping business content readable and centered, resulting in a premium and stable shell look.

---

---

## DEC-071 — Shell 2.0 Collapsible Layout Strategy

- **Date:** 2026-03-26
- **Status:** Accepted
- **Context:** The move to Shell 2.0 required a collapsible sidebar that does not break the layout of existing business screens.
- **Decision:** Use a dynamic CSS Grid layout in `AppShell`. 
    1. Grid defined as: `grid-template-columns: var(--sidebar-width) minmax(0, 1fr)`.
    2. Sidebar width managed via a CSS variable `--sidebar-width` controlled by React state.
    3. Topbar uses `position: fixed` with a dynamic `left` offset matching the sidebar width.
- **Alternatives considered:** Switching to a pure Flexbox or Absolute positioning model. Rejected as too invasive for existing grid-dependent pages.
- **Consequences:** Achieves a modern "push" layout with 0.3s transitions while keeping legacy feature tables accessible and correctly rendered inside the dynamic grid container.

---

## DEC-070 — Frontend Modernization Foundation (v2.0)

- **Date:** 2026-03-26
- **Status:** Accepted
- **Context:** The Alpla Portal frontend required a modern foundation to support the Shell 2.0 migration, React 19 features, and the more performant Tailwind CSS 4 engine.
- **Decision:** Upgrade the core frontend stack:
  1. **React 19**: Migrate for long-term support and improved rendering performance.
  2. **React Router 7**: Adopt the latest routing standard while preserving declarative mode for stability.
  3. **Tailwind CSS 4**: Implement the CSS-first engine using the `@tailwindcss/vite` plugin to eliminate legacy JS-based configuration overhead.
  4. **Vite 6**: Standardize on Vite 6 to support the new Tailwind engine and React 19.
  5. **Motion/React**: Replace heavy `framer-motion` imports with the modular `motion/react` package.
- **Consequences:** Major leap in developer experience and build performance. Requires stricter TypeScript handling for `useRef` and `RefObject`. Foundation is now fully aligned with the Shell 2.0 technical requirements.

---


## DEC-069 — Alpla Shell 2.0 Migration Strategy

- **Date:** 2026-03-26
- **Status:** Accepted
- **Context:** Need to adopt the modern "Shell 2.0" UI redesign without disrupting production stability or losing complex business logic.
- **Decision:** Use a phased hybrid migration:
  1. Maintain `React Router` architecture.
  2. Integrate `TailwindCSS` mapped to existing `tokens.css`.
  3. Lead with `RequestsList` as the Pilot screen.
  4. Preserve all backend `api.ts` and RBAC logic exactly as-is.
- **Consequences:** Low-risk path to modernization, consistent visual language, and easier maintenance during the transition.

---

## DEC-001 — Adopt 3-layer DOE architecture

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Need reliable execution with AI orchestration and deterministic scripts
- **Decision:** Use Directive / Orchestration / Execution architecture
- **Consequences:** Better maintainability, clearer responsibilities, easier debugging

---

## DEC-002 — Use `.tmp/` for intermediates and keep deliverables in cloud services

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Local files are temporary processing artifacts and should not be treated as final outputs
- **Decision:** Store intermediates in `.tmp/`; keep user-facing deliverables in cloud services whenever applicable
- **Consequences:** Cleaner repository, easier regeneration, less confusion about final outputs

---

## DEC-003 — Request Line Items in V1

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Deciding whether V1 needs a separate line item entity or just a header-level total amount.
- **Decision:** Include a generic 1:N `RequestLineItem` entity for both Purchase and Payment requests. Keep workflow/approvals strictly at the header level.
- **Consequences:** Provides better detail out-of-the-gate and flexibility for future ERP sync, but avoids workflow complexity by restricting item-level partial approvals.

---

## DEC-004 — V1 Technical Stack (ASP.NET Core + React)

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Deciding the most stable and maintainable technologies for an internal portal deployed on an existing Windows Server + SQL Server on-premises infrastructure.
- **Decision:** Use ASP.NET Core 8 Web API for the backend, EF Core for the ORM, and React (Vite) for the frontend SPA.
- **Alternatives considered:** Node.js (NestJS) and Python (FastAPI/Django). Both were rejected due to the operational complexity of hosting them natively and reliably on Windows Server IIS without Docker.
- **Consequences:** Provides native IIS synergy, highly secure enterprise authorization middleware, and strict typing. Requires team familiarity with C#/.NET.

---

## DEC-005 — Physical Database PKs and Navigation Properties (EF Core)

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Deciding the physical implementation details for the first EF Core migration based on the V1 Data Model Draft.
- **Decision:** Use `Guid` for all primary keys on transactional tables (`Request`, `RequestLineItem`, `RequestStatusHistory`, `RequestAttachment`) to prevent ID-guessing in the API. Use `int` for static/administrative lookup tables (`Status`, `Priority`, `Currency`, `Department`, etc.) to keep foreign key payload sizes small.
- **Consequences:** Ensures excellent security for transactions while remaining performant and lightweight for simple Lookups.

---

## DEC-006 — EF Core Cascade Delete Constraints on Audit Fields

- **Date:** 2026-02-25
- **Status:** Accepted
- **Context:** Deciding how to handle SQL Server's "multiple cascade paths" error when a `Request` and its child entities (like `RequestAttachment` or `RequestStatusHistory`) both reference the same `User` table (e.g., `RequesterId` vs `UploadedByUserId`).
- **Decision:** Foreign keys representing audit traceability or metadata ownership (like `UploadedByUserId`, `CreatedByUserId`, `ActorUserId`) must explicitly implement `DeleteBehavior.NoAction` (or `Restrict`) in their EF Core configurations.
- **Consequences:** Prevents SQL Server deployment crashes and ensures biological audit history is preserved even if a `User` identity were to be hard-deleted from the directory, preventing accidental destruction of associated request timelines or file matrices.

---

## DEC-007 — Formalize Master Data Guidelines handling

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** The project requires a standard procedure for introducing reusable lookup/reference values (Units, Currencies, Cost Centers, etc.) to prevent technical debt from hardcoded UI/API enumerations.
- **Decision:** All Master Data must follow the steps defined in `MASTER_DATA_GUIDELINES.md`. This mandates DB-driven lookups, EF Core seeding, soft-deletion principles, and dynamic read-only REST API endpoints for UI hydration.
- **Consequences:** Slightly higher initial scaffolding overhead when adding a dropdown, but guarantees consistent UI states, database referential integrity, and simpler integration with external ERP systems later.

## DEC-008 — Seamless Continuous Flow for Request Creation

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** Deciding how to allow users to add Line Items immediately after creating a new Request Draft without duplicating massive amounts of UI state in `RequestCreate.tsx`.
- **Decision:** Use a seamless local route transition (`navigate('/requests/{id}/edit', { replace: true })`) immediately upon successful POST of the Draft Header. The user's screen instantly updates to reveal the Line Items section without breaking context.
- **Superseded by:** DEC-045 (Consolidated Redirection)
- **Consequences:** Keeps `RequestCreate.tsx` simple and strictly focused on the Header POST. Eliminates the need for a complex "wizard" state machine, while providing the exact UX required (Header -> Items seamlessly).

---

## DEC-009 — Standardize Form Validation and Monetary Input Execution

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** Deciding how to gracefully intercept Backend API validation errors (HTTP 400 Bad Request) without bouncing users abruptly to generic error screens. And standardizing currency input formats visually to Angolan/European standards (`1.000,50`).
- **Decision:** Implement inline form validation as a project standard. Catch `ValidationProblemDetails` using `api.ts`, parse them into a dictionary (`Record<string, string[]>`), and render them conditionally below specific violating inputs `getInputStyle(PropName)` inside React state. Utilize a native `<CurrencyInput>` component bridging `Intl.NumberFormat` locally to physical raw string models dynamically.
- **Consequences:** Ensures excellent ergonomics for data-entry intensive screens, mitigating lost local edits while maintaining strict model coherence between the database decimal structures and localized frontend UI masks.

---

## DEC-014 — Unified Request Identification and Global Sequence

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Previous numbering strategy used type-specific prefixes (`PAG-`, `COM-`) and per-day sequences, which proved confusing. The requirement is for a unified, human-readable format that ensures monotonic sequence growth without reuse, even if records are deleted.
- **Decision:** Switch to format `REQ-DD/MM/YYYY-SequentialNumber` (where SequentialNumber is at least 3 digits). Centralize generation around a single `GLOBAL_REQUEST_COUNTER` key in the `SystemCounter` entity.
- **Consequences:** Provides a consistent, professional identifier across all request types. Prevents sequence "gaps" or "collisions" from affecting business predictability. Decouples the visual ID from technical key constraints or type-based logic.

---

## DEC-049 — Exceção de Edição de Fornecedor em Aguardando Cotação

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** Na etapa `WAITING_QUOTATION` (Aguardando Cotação), o pedido é tecnicamente bloqueado para edição de cabeçalho (para preservar a intenção original do solicitante). No entanto, a seleção do fornecedor e o upload da Proforma são requisitos obrigatórios para a conclusão desta etapa pelo Comprador.
- **Decision:** Implementar uma exceção de permissão cirúrgica para o campo **Fornecedor** durante a etapa de cotação.
  - **Frontend**: Introduzir a flag `canEditSupplier` que inclui `WAITING_QUOTATION`, permitindo que o `SupplierAutocomplete` permaneça editável.
  - **Backend**: Atualizar `UpdateRequestDraft` para permitir a alteração de `SupplierId` quando o pedido estiver em `WAITING_QUOTATION`, enquanto continua bloqueando alterações em outros campos do cabeçalho.
- **Consequences:** Melhora a fluidez do workflow de cotação. Garante que os dados necessários para a transição estejam disponíveis no sistema antes da conclusão. Mantém a segurança do cabeçalho contra alterações não autorizadas em outros campos críticos (Departamento, Planta, etc).

---

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Requests in `WAITING_QUOTATION` status were initially read-only. However, the Buyer needs to refine request data and insert final quotation values (prices, specific line items) before completion.
- **Decision:** Define `WAITING_QUOTATION` as an **active operational editing stage**. Hide the read-only banner and enable form/item mutations. Structural integrity is preserved by locking the `RequestType` field after the initial creation.
- **Consequences:** Provides the necessary flexibility for the Buyer to complete their procurement duties within the portal, while maintaining workflow invariants.

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to present API response statuses (successes and errors) to users on very tall, scrolling Request forms so they are never lost off-screen.
- **Decision:** Use a sticky `Feedback` component positioned in the top action bar. Re-map successes and errors via React `Location` state on navigations to ensure they persist across route hops.
- **Consequences:** Guarantees that users always see the result of their actions immediately, regardless of scroll position, and provides a unified "Feedback" language throughout the application.

---

## DEC-011 — Permanent Use of Portuguese for Status and History Visibility

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding on the language for user-facing audit logs and status names in the workflow.
- **Decision:** While internal status codes remain in stable English (e.g., `WAITING_AREA_APPROVAL`), all display names and history comments must be strictly in Portuguese for readability and business clarity.
- **Consequences:** Ensures the application feels native to the primary users in Angola while maintaining a predictable development environment.

---

## DEC-012 — Temporary Permission Model for Area Approval (v0.9.5)

- **Date:** 2026-03-01
- **Status:** Accepted (Temporary)
- **Context:** Implementing workflow actions before a full role-based access control (RBAC) / JWT authentication system is in place.
- **Decision:** For v0.9.5, approval actions (Approve, Reject, Request Adjustment) are visible to any user who can access the `RequestEdit` page for a request in the `WAITING_AREA_APPROVAL` status. Hardcode `dev@alpla.com` as the actor in history logs.
- **Consequences:** Allows testing and early use of the workflow logic, with the explicit understanding that proper actor/permission enforcement is a mandatory next phase.

---

## DEC-013 — AREA_ADJUSTMENT Semantic Grounding

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Reusing the `AREA_ADJUSTMENT` status code for the "Solicitar Reajuste" action.
- **Decision:** In the context of the Area Approval workflow for PAYMENT requests, the internal code `AREA_ADJUSTMENT` specifically represents the "Reajuste A.A" (Solicitor Rework) stage.
- **Consequences:** UI labels, badges, and history entries must always use the Portuguese term "Reajuste A.A" to maintain semantic clarity for the user, even if the backend code is more generic.
- **Decision:** Embed explicit, standardized text feedback banners natively inside `position: 'sticky'` layout containers anchoring to the top viewport edge. For cross-page transitions (like returning after draft creation), rely on `react-router-dom` `Location.state` payloads to render context cleanly on mount.
- **Consequences:** Provides massive UX clarity. Prevents users from wondering "did my save work?" when the form reloads.

---

## DEC-011 — Consolidating Urgency and Priority Fields

- **Date:** 2026-02-26
- **Status:** Accepted
- **Context:** The Request Header originally contained both `Prioridade` and `Grau de Necessidade`, creating semantic overlap and UX ambiguity. Additionally, line items lacked a way to express relative importance within a single Request.
- **Decision:** Drop the `Priority` Master Data entirely from the `Request` header. Rely exclusively on `NeedLevel` (rename UI label to "Grau de Necessidade do Pedido"). Introduce a separate, numeric `ItemPriority` (int) field onto `RequestLineItem` to allow users to rank items 1-N.
- **Consequences:** Eliminates confusion over what makes a request "Urgent" vs "Critical". Enables item-by-item triage for buyers. Requires a breaking EF Core schema migration (dropped Priority table).

---

## DEC-012 — Request Form Logical Sections & Validations

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The Request UI forms were vertically overwhelming and prone to submitting without fundamental identifiers like "Operation Type" or crucial "Workflow Participants".
- **Decision:** Break the Request form visually into semantic containers: General, Participants, and Financials. Force `Tipo de Pedido` and all Approvers to trigger explicit inline validations. Restrict manual editing of the `Estimated Total Amount` field so it inherently tracks Line Items mathematically.
- **Consequences:** Provides a significantly more guided, fail-proof experience. Mitigates accidental draft submissions without approvers.

---

## DEC-013 — URL-Driven List State Preservation

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to preserve complex list filter states (page size, search terms, status filters) when users navigate away to edit/view a Detail record and then navigate "back".
- **Decision:** Shift list state out of React local memory directly into the URL query string via `useSearchParams()`. When moving into a Detail/Edit view, append that query string inside `react-router-dom`'s `Location.state` (`{ fromList: location.search }`). Any "Cancel" or "Return" actions from the Detail view re-apply that exact payload onto the `/requests` routing destination.
- **Consequences:** Creates a robust, natively bookmarkable list implementation. Allows users to confidently deep-dive into items directly out of paginated sub-filters without fear of losing their exact scroll/search coordinates.

---

## DEC-014 — Department and Plant as Managed Master Data

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The Request header required two new organizational classification fields — Departamento (Department) and Planta (Plant). The initial temptation was to implement them as free-text inputs to ship faster.
- **Decision:** Implement both as proper Master Data entities (`Department`, `Plant`) with `Id`, `Code`, `Name`, `IsActive` following the standard defined in `MASTER_DATA_GUIDELINES.md`. Both are selectable in the Request form via managed dropdowns and fully maintainable in the `Dados Mestres` settings area.
- **Alternatives considered:** Free-text fields on the Request (rejected — no referential integrity, no filtering/aggregation support, no governance over valid values).
- **Consequences:** Requires a DB migration, new API endpoints, and UI management screens. Trade-off is worthwhile for data consistency, future reporting, and alignment with the enterprise data model.

---

## DEC-015 — Supplier as Managed Master Data

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deciding how to store Supplier information on the Request.
- **Decision:** Implement `Supplier` as a managed Master Data entity (`Id`, `Code`, `Name`, `TaxId`, `IsActive`). This facilitates data integrity, prevents typos in payment processing, and enables future ERP synchronization via `TaxId` (NIF).
- **Consequences:** Requires a dedicated management UI and DB table. Significantly improves financial auditing and consistency.

---

## DEC-016 — Request Type Standardization (QUOTATION/PAYMENT)

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** The business model requires exactly two flows: asking for a price (Quotation) vs processing a known invoice (Payment).
- **Decision:** Standardize on two core `RequestType` codes: `QUOTATION` and `PAYMENT`. Rename legacy "Purchase" label to "Quotation".
- **Stage 5 Workflow Correction**: Unified post-PO operational flow. Both `QUOTATION` and `PAYMENT` requests now follow the `PO_ISSUED -> PAYMENT_SCHEDULED -> PAYMENT_COMPLETED -> WAITING_RECEIPT` sequence. Removed direct bypass for Quotation requests.
- **Stage 5 Contract Alignment**: Final alignment pass completing DTO projections and removing fragile `any` mappings.
- **Consequences:** Simplifies UI conditional logic. `Supplier` is only required for `PAYMENT` requests.

## DEC-017: Conditional Post-Creation Navigation

**Date:** 2026-02-27  
**Status:** Approved  
**Context:** Creating a `QUOTATION` request is often an administrative step before a longer commercial process. Creating a `PAYMENT` request usually implies immediate entry of line items.  
**Decision:** We use a conditional redirect in `RequestCreate.tsx`:

- `QUOTATION` (Code-based check) -> Redirect to `/requests` (List).
- `PAYMENT` (Code-based check) -> Redirect to `/edit` (Item Entry).
- **Superseded by:** DEC-045 (Consolidated Redirection)
**Consequences:** Improved UX flow tailored to business necessity. Success message persistence is handled via React Router state and captured by the List component.

---

## DEC-065: Quotation Editor Mode Separation

**Context**: A regression caused new quotation flows to inherit the "Edit Mode" UI (badge, title, update button) if a previous edit session had been active for the same request.

**Decision**:

1. Explicitly reset `editingQuotationId`, `draftProformaFiles`, and `quotationDrafts` state for the specific `requestId` when starting a new manual or OCR flow.
2. Update `handleResetToSelect` (cancel/back logic) to also perform a full state cleanup for the request.
3. Differentiate the UI titles ("Registrar Nova Cotação" vs "Editar Cotação") to provide clear visual feedback to the buyer.

**Consequences**:

- Guaranteed clean state for every new quotation attempt.
- Prevents UI state "leaks" between different operational actions on the same Request.
- Improves scanability and reduces buyer confusion regarding the current operation.

---

## DEC-024 — Master Data Feedback and Inline Validation

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding how to standardize feedback and validation across Master Data screens to match the Request screens.
- **Decision:**
    1. Integrate the `Feedback.tsx` component into all Master Data screens for post-action messaging (success/error).
    2. Implement debounced inline uniqueness validation for critical fields (like Supplier Name and PrimaveraCode) to prevent submission failures.
    3. Standardize the "Brutalist" input styling for all Master Data form fields.
- **Consequences:** Provides a cohesive brand identity and UX patterns across the entire portal. Reduces user frustration by providing immediate validation feedback before form submission.

---

## DEC-025 — Searchable Dropdown Pattern (Combobox)

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** Large master data sets (like Suppliers) require a selection mechanism that scales beyond simple dropdowns but feels more integrated than detached autocompletes.
- **Decision:** Standardize on a "Searchable Dropdown" (Combobox) pattern for large lookups:
    1. **Immediate Feedback**: Open options on focus/click even if the query is empty.
    2. **Integrated Search**: Filter results live as the user types within the same dropdown container.
    3. **Visual Structure**: Use formatted results (e.g., `[Code] Name`) to ensure unique identification in the list.
    4. **Chevron Indicator**: Add a visual cue (Chevron) to signify dropdown behavior.
- **Consequences:** Provides a familiar "Dropdown" experience for users while maintaining the performance and scalability of an async search.

---

## DEC-018 — Persistent Request Numbering Strategy

- **Date:** 2026-02-27
- **Status:** Accepted
- **Context:** Deleting drafts caused the system to reuse sequential numbers because the previous logic depended on counting existing records. This resulted in duplicate request numbers.
- **Decision:** Use a dedicated `SystemCounters` table to store persistent, monotonically increasing counters. Every request type has its own counter key per day (e.g., `REQ_NO_QUOTATION_20260227`). The number allocation is decoupled from the `Requests` table content.
- **Consequences:** Ensures non-reusable and unique request numbers even after record deletion. Gaps in the sequence are expected and acceptable. Added a database-level unique index on `RequestNumber` as a final safeguard.

---

## DEC-019 — Single Currency Enforcement & Header Edit Lock

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** Mixed currencies within a single purchase request create accounting complexity and ambiguity in total calculations. Additionally, changing the request currency after items already exist would invalidate the consistency of those items.
- **Decision:**
    1. Enforce exactly one currency per request (defined at the header level).
    2. All line items must inherit this same currency; the item-level currency selector is removed.
    3. If a request has one or more line items, the request-level `CurrencyId` field becomes read-only to prevent inconsistencies.
- **Consequences:** Simplifies financial logic and reporting. Streamlines the item entry UX. Backend validates and normalizes item-level currency to match the header and rejects header currency changes if items exist.

---

## DEC-063 — Stage 8.5: Consolidação de Propriedade e Seleção de Cotação

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** O sistema continha regras híbridas de transição onde a responsabilidade pela seleção da cotação vencedora e edição de centros de custo estava ambígua entre Comprador e Aprovadores.
- **Decision:**
    1. **Quotation-First Model**: Para pedidos de COTAÇÃO, o fornecedor oficial é derivado da cotação selecionada. O `SupplierId` no cabeçalho do pedido é tratado como transitório e ignorado em favor da cotação vencedora.
    2. **Winner Selection**: A seleção da cotação vencedora é movida exclusivamente para o **Aprovador Final** na etapa `WAITING_FINAL_APPROVAL`. O Comprador não seleciona mais o vencedor no Workspace de Itens.
    3. **Cost Center Ownership**: A edição de centros de custo nos itens de linha é movida do Comprador para o **Aprovador de Área** na etapa `WAITING_AREA_APPROVAL`.
    4. **Role-Based detail view**: Introduzida a capacidade de alternar a visualização (`userMode`) no detalhe do pedido para permitir que o usuário atue em diferentes papéis se possuir as permissões necessárias (Simulado via `X-User-Mode`).
- **Consequences:** Garante separação de deveres (SoD) clara. Reduz o erro humano ao centralizar decisões financeiras (Vencedor, Centro de Custo) nos aprovadores, deixando a execução operacional com o comprador.

---

## DEC-066 — Winner Selection by Area Approver

- **Date:** 2026-03-24
- **Status:** Accepted
- **Context:** The original procurement logic required a winner selection for `QUOTATION` requests. Earlier decisions placed this at the Final Approval stage, but operational feedback indicated that Area Approvers (who often manage the requisitioning department) are better suited to make this technical/commercial selection.
- **Decision:** Move winner selection for `QUOTATION` requests to the `WAITING_AREA_APPROVAL` stage.
  - **Enforcement**: The `ApproveArea` action now requires a `SelectedQuotationId`.
  - **Backend**: `ProcessAreaApproval` validates that a winner is selected and belongs to the request.
  - **Frontend**: `RequestEdit` blocks the "APROVAR" action and shows an error if no winner is selected.
- **Consequences:** Earlier technical decision in the workflow. Final Approvers still review the selection but cannot change it.
- **Supersedes:** DEC-063 (Winner selection move)

---

## DEC-067 — Explicit Field Propagation Strategy for Document Extraction

- **Date:** 2026-03-24
- **Status:** Accepted
- **Context:** Recurring risk of losing extracted data in intermediate layers (e.g., `ExtractionMapper`, `OcrHeaderSuggestionsDto`) despite successful AI extraction.
- **Decision:** Adopt an "Explicit Propagation" strategy. Every new extraction field must be manually added to the provider mapping, internal DTO, legacy/compatibility DTO, and API mapper. Implicit or generic property bags are discouraged for core business fields to maintain strict typing and front-to-back contract visibility.
- **Consequences:** Increases initial development effort per field but guarantees reliability, discoverability, and testability across the entire pipeline.
- **Related:** [DOCUMENT_EXTRACTION_FIELD_PROPAGATION_STANDARD.md](DOCUMENT_EXTRACTION_FIELD_PROPAGATION_STANDARD.md)

---

## DEC-068 — Dedicated AdminLogWriter for Admin Observability (Step 2)

- **Date:** 2026-03-25
- **Status:** Accepted
- **Context:** There was a need for a queryable admin log store to surface OCR and integration failures in the Administrator Workspace. A generic `ILoggerProvider` writing all framework logs to the database was considered and rejected.
- **Decision:** Implement a dedicated `AdminLogWriter` service. Services explicitly call `WriteAsync(...)` to persist targeted `AdminLogEntry` records. The writer resolves its own `DbContext` scope, making it fail-safe (persistence errors are swallowed and redirected to `ILogger` only — they never propagate to the main request flow).
- **Alternatives considered:** `DbLoggerProvider` as an `ILoggerProvider` — rejected because it would persist all framework/runtime logs indiscriminately, creating noise, performance risk, and database growth that is hard to control.
- **Consequences:** Only explicitly instrumented events appear in **Logs do Sistema**, giving administrators a clean, actionable log feed. Adding new events requires intentional `WriteAsync` calls, keeping the admin log focused and scoped.
- **Related:** `docs/ARCHITECTURE.md` — Admin Observability Layer section.

---

## DEC-083 — Side-Panel Workspace Pattern for Approvals

- **Date:** 2026-04-01
- **Status:** Accepted
- **Context:** The previous stacked master-detail layout in the Approval Center felt disjointed and lacked visual focus. Users lost queue context when reviewing large requests, and the vertical stacking created a "long page" feel that hindered productivity.
- **Decision:** Implement a side-panel (drawer) workspace pattern.
    1. **Queue Visibility**: The main queue sections remain visible on the left, providing constant context.
    2. **Drawer Rendering**: Details load into a `640px` right-side drawer using `DropdownPortal` to ensure it renders above all other UI layers.
    3. **Strong Selection State**: Active rows in the queue use a `12px` accent border and unique background (`#eff6ff`) to clearly link the queue item to the open panel.
    4. **Auto-Selection**: After a successful approval action, the system automatically refreshes and opens the next pending item in the same queue to maximize throughput.
- **Alternatives considered:** Full-page navigation (rejected: loses queue context) or keeping the stacked layout (rejected: poor focus).
- **Consequences:** Significantly improves the "triage" experience for approvers. Reduces context switching and clicks. Requires careful management of panel state (`isPanelOpen`) and selection synchronization.

---

## DEC-084 — Role-Aware Intelligence Pattern for Approval Center

- **Date:** 2026-04-01
- **Status:** Accepted
- **Context:** The `DecisionInsightsPanel` rendered identical intelligence for both Area Approvers and Final Approvers. The decision context is fundamentally different: Area Approvers focus on legitimacy, necessity, and organizational fit, while Final Approvers focus on financial rationality and comparative history. A naive solution would fork the component or create two separate screens.
- **Decision:** Implement role-aware conditional rendering within a single shared `DecisionInsightsPanel` component.
    1. **Shared Foundation**: All three core blocks (Alerts, Department KPIs, Item Analysis) remain available to both roles.
    2. **Context Banner**: A subtle top-of-panel indicator reinforces the decision lens without dominating the UI.
    3. **Role-Specific Emphasis Blocks**: Each role receives a dedicated emphasis block — Area gets a "Checklist de Legitimidade" (informational, non-blocking), Final gets a "Visão Financeira Comparativa" (year totals, consolidated variation).
    4. **Section Reordering**: Shared blocks render in different priority order per role to surface the most relevant information first.
    5. **Lightweight Context Prop**: A `requestData` object passes minimal request context to the panel, avoiding coupling to the full `RequestDetailsDto`.
- **Alternatives considered:** Forking into two separate panel components (rejected: duplication, maintenance burden). Single panel with no differentiation (rejected: suboptimal decision support for each role).
- **Consequences:** Maintains a single component with shared visual language while providing contextually relevant decision support. Future role-specific enhancements can be added within the existing conditional structure without architectural changes.

---

## DEC-085 — Anti-Accumulative Copy Request Flow

- **Date:** 2026-04-02
- **Status:** Accepted
- **Context:** The previous "Copy" feature was broken, simply redirecting to a blank "New Request" screen. Furthermore, copying needs to be careful not to generate abandoned draft records or leak sensitive operational data (prices, items, status) from the source request.
- **Decision:** Implement a template-driven, frontend-first copy flow.
    1. **Frontend-Owned Mapping**: `RequestCreate.tsx` becomes the owner of the copy flow via `/requests/new?copyFrom={id}`. It fetches a "Template" from the backend and maps it into ephemeral form state.
    2. **Strategic Field Exclusion**: Only header-level structure is copied. Line items, currency, need-by date, and requester are explicitly excluded to ensure the resulting request is a fresh business need.
    3. **No Automatic Persistence**: Unlike a typical "New" flow that might create a draft ID immediately, the copy flow remains purely in the browser's memory until the user clicks "Submeter". This prevents database pollution with abandoned copies.
    4. **UX Safeguards**: Replaces standard "Cancel" with "Descartar Cópia" to clarify the ephemeral nature of the unsaved copy. Adds a mandatory warning banner for the copied description.
- **Alternatives considered:** Copying everything including items and creating a persisted Draft immediately (rejected: high risk of data leakage and DB bloat).
- **Consequences:** Ensures a clean, reliable duplication process. Reduces backend load by avoiding redundant draft creation. Requires users to re-enter items, which serves as a necessary validation of the new need.

---

## Template for New Decisions

## DEC-[NNN] — [Short title]

- **Date:** [YYYY-MM-DD]
- **Status:** Proposed / Accepted / Rejected / Superseded
- **Context:** Why this decision is needed
- **Decision:** What was chosen
- **Alternatives considered:** [Option A], [Option B]
- **Consequences:** Tradeoffs, risks, follow-up actions
- **Supersedes / Superseded by:** [DEC-XXX] (if applicable)

---


## DEC-020 — Item Priority as a Business Classification

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** The original `ItemPriority` field was a numeric manual input with no business meaning.
- **Decision:** Replace `int ItemPriority` with `string` storing one of: `HIGH`, `MEDIUM`, `LOW`. Backend validates/normalizes; frontend shows a select with labels `Alta`/`Média`/`Baixa`. `LineNumber` remains the technical row-order indicator, separate from business priority.
- **Alternatives considered:** C# enum, a DB lookup table.
- **Consequences:** Static codes are stable and simpler than a table. A migration with safe SQL data conversion handles existing int values. Priority displayed as a colored badge in the item grid.

---

## DEC-021 — Line Item Status as Master Data, Backend-Assigned

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** Each line item needs a lifecycle status. Status must not be freely set by the requester.
- **Decision:** `LineItemStatus` is a proper master data table (7 statuses). `AddLineItem` auto-assigns initial status based on parent `RequestType.Code`: `QUOTATION` → `WAITING_QUOTATION`, `PAYMENT` → `PENDING`. Status never accepted from client DTOs. `UpdateLineItem` preserves existing status. Requester sees status as a read-only badge.
- **Alternatives considered:** C# enum, plain string field.
- **Consequences:** Consistent with existing master data patterns. Supports future buyer-controlled editing (planned for next phase).

---

## DEC-050 — Inline Request Status Timeline

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** Users required quick visibility into request progression directly from the list view to reduce context switching.
- **Decision:** Implement an expandable table row in `RequestsList.tsx` that renders a `RequestTimelineInline` component.
  - **Logic**: Component fetches data from a dedicated `/timeline` endpoint which maps internal statuses to high-level workflow stages.
  - **UI (v0.9.19)**: Redesigned as a high-fidelity horizontal visual timeline with segmented connectors and state-aware markers (Completed/Current/Pending).
- **Consequences:** Provides immediate transparency. Uses lazy-loading and `stopPropagation` to maintain performance and prevent navigation conflicts. Horizontal layout handles long labels and many steps via responsive overflow.

---

## DEC-022 — Supplier Identification: PortalCode and PrimaveraCode

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** Preparation for future ERP (Primavera) integration while maintaining local autonomy.
- **Decision:** Split supplier code into two fields: `PortalCode` (internal, unique, required) and `PrimaveraCode` (external ERP code, optional).
- **Consequences:** Enables seamless future linkage with the ERP while allowing the Portal to manage local suppliers that might not yet exist in the ERP.

---

-3. **Table Wrapping**: Wide tables must always be wrapped in a `div` with `overflow-x: auto` and `width: 100%`. The table itself should have a stable `min-width` (e.g., `1200px`) to preserve readability.

## Document Attachments and Multi-File Validation

1. **Typed Interaction**: All file uploads must be categorized by a functional type (`PROFORMA`, `PO`, etc.). The UI must group files by these categories to guide the user naturally.
2. **Workflow Convergence**: Transition buttons are conditionally enabled or backend-validated against the presence of required file types. The rule is: *At least one valid, non-deleted attachment of the required type must exist*.
3. **Audit History Trail**: Every legal attachment addition must be mirrored as a `RequestStatusHistory` entry using the standard `DOCUMENTO ADICIONADO` action. These entries must inherit the request's current status to maintain a strictly linear time-based audit journey in the main history UI.
4. **Deletion Guard**: File deletion is a mutation that is strictly blocked once a request moves past the initial engagement phases (`DRAFT`, `WAITING_QUOTATION`, or Rework). This prevents "ghost" documents where a request appears approved without the required evidence.

---

## DEC-023 — Async Supplier Search Pattern (Autocomplete)

- **Date:** 2026-02-28
- **Status:** Accepted
- **Context:** As the supplier master grows (thousands of records), a standard dropdown becomes unusable and slow.
- **Decision:** Replace standard supplier dropdown with a dedicated `SupplierAutocomplete` component. It uses an async lookup endpoint (`/api/v1/lookups/suppliers/search`), implements a 300ms debounce, and requires at least 3 characters to trigger search. Results are limited to 20 for performance.
- **Consequences:** Provides a high-performance, scalable UX. Reduces initial page load weight by not fetching the entire supplier list upfront.

## DEC-026 — Formal Request Submission Pattern (Draft -> Submitted)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Requests must transition from a flexible "Draft" state to a stable "Submitted" state to enter the formal business workflow.
- **Decision:**
    1. Introduce a stable `SUBMITTED` status code in master data.
    2. Implement a dedicated `POST /requests/{id}/submit` endpoint for this transition.
    3. Backend enforces comprehensive completeness validation during submission (Header fields, Item existence, Positive Total).
    4. Transition is audited via `RequestStatusHistory` and marked with `SubmittedAtUtc`.
- **Consequences:** Provides a clear "point of no return" for the requester, ensuring data stability during the approval phases.

---

## DEC-027 — Conditional Supplier Requirement Rule

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding if a Supplier is mandatory at the point of submission.
- **Decision:**
  - For `QUOTATION` (COM) requests: Supplier is **optional**. The buyer is responsible for finding the supplier.
  - For `PAYMENT` (PAG) requests: Supplier is **mandatory**. The requester must define who is being paid.
- **Consequences:** Aligns with current Alpla Angola procurement practices. Frontend and backend both enforce this logic dynamically based on `RequestType.Code`.

---

## DEC-028 — Post-Submission Mutation Lock (Read-Only)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Once a request is submitted, it should not be modified by the requester to ensure approvers are reviewing a static, reliable document.
- **Decision:** All backend endpoints that mutate a Request or its Line Items (`PUT /requests/{id}/draft`, `POST /requests/{id}/line-items`, etc.) must return `409 Conflict` if the current status is not `DRAFT`. The frontend similarly disables all inputs and hides action buttons (Save, Delete, Add Item) for non-DRAFT requests.
- **Consequences:** Guarantees data integrity throughout the workflow lifecycle. Requires users to ask for a "Reject/Rework" (future V2) if changes are needed after submission.

---

## DEC-029 — Conditional Line Item Requirement for Submission

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** In the quotation flow, a buyer might be responsible for adding items later, while in a payment flow, the items must be defined upfront.
- **Decision:**
  - **PAYMENT** (PAG) requests strictly require at least one line item before submission.
  - **QUOTATION** (COM) requests allow submission with zero items.
- **Superseded by:** DEC-045 (Mandatory Items for All)
- **Consequences:** Improves flexibility for buyers in the Quotation flow while maintaining strict control for Payments. Frontend provides conditional helper text and blocks submission only for PAYMENT if empty.

---

## DEC-031 — Post-Submit Workflow Transitions (v0.9.4)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Requests must transition directly to business-relevant statuses after submission:
  - `PAYMENT` -> `WAITING_AREA_APPROVAL` ("Aguardando Aprovação da Área")
  - `QUOTATION` -> `WAITING_QUOTATION` ("Aguardando Cotação")
- **Decision:** Implement these direct transitions based on `RequestType.Code` immediately after a successful submission.
- **Consequences:** Streamlines the workflow, moving requests directly into the appropriate next business state without an intermediate, non-functional "SUBMITTED" display state.

---

## DEC-032 — Read-Only Enforcement for Non-Drafts (v0.9.4)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Once a request is no longer in `DRAFT` status, the UI must strictly enforce read-only mode, hiding all action buttons (Save, Submit, Add Item) and disabling all input fields.
- **Decision:** Implement this read-only enforcement via component re-use in `RequestEdit.tsx` and related forms. All mutation actions (buttons, input fields) are conditionally rendered or disabled if the request status is not `DRAFT`.
- **Consequences:** Prevents unauthorized modifications to requests that are already in a formal workflow, ensuring data integrity and consistency for approvers.

---

## DEC-033 — Technical Role of "SUBMITTED" Status (v0.9.4)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Clarifying the purpose and visibility of the `SUBMITTED` status.
- **Decision:** The `SUBMITTED` status exists only as an internal/technical audit step in the `RequestStatusHistory`. It must never be the visible functional state in the workflow UI. After submission, the request immediately transitions to a business-relevant status (e.g., `WAITING_AREA_APPROVAL` or `WAITING_QUOTATION`).
- **Consequences:** Simplifies the user's understanding of the request's current state by presenting only actionable business statuses. Maintains a clear audit trail of the submission event.

---

## DEC-030 — Standardized Portuguese UI Text for Request Workflow

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Need for a unified and professional Portuguese terminology across all primary request actions and feedback.
- **Decision:** Enforce the following exact strings:
  - **SALVAR PEDIDO** (Create Action)
  - **SALVAR MUDANÇAS**, **SUBMETER PEDIDO**, **EXCLUIR RASCUNHO**, **CANCELAR** (Edit Actions)
  - **Rascunho salvo com sucesso.** (Save Feedback)
  - **Preencha os campos obrigatórios antes de submeter o pedido.** (Header Validation)
  - **Para submeter, o pedido deve conter pelo menos um item.** (Item Validation)
- **Consequences:** Ensures consistency and a professional feel for the portal's user interface.

---

## DEC-034 — Semantic Grounding of FINAL_ADJUSTMENT (v0.9.6)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding how to present the `FINAL_ADJUSTMENT` state to the user in the Final Approval workflow.
- **Decision:** The internal status `FINAL_ADJUSTMENT` is semantically grounded as **"Reajuste A.F"** (Aprovação Final) in the UI.
- **Consequences:** UI labels, badges, and history logs must consistently use the label "Reajuste A.F". It represents a request for rework initiated by the Final Approver.

---

## DEC-035 — Item Form Mandatory Field UX (v0.9.6)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Ensuring data quality for mandatory fields in the Item insertion/edit form.
- **Decision:**
    1. Mandatory selects (`Prioridade`, `Unidade`) must start empty (`''`) for new items.
    2. They must display an explicit empty placeholder (e.g., "Selecione a unidade...").
    3. Both client-side and server-side validation must block submission if these are not explicitly selected by the user.

---

## DEC-036 — Request Rework / Adjustment Workflow (v0.9.7)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Requests returned for adjustment (`AREA_ADJUSTMENT` or `FINAL_ADJUSTMENT`) must be editable by the requester so they can fix issues and resubmit.
- **Decision:**
    1. **Editability**: Mutation guards (Backend) and UI input states (Frontend) are relaxed to allow edits if the request is in an adjustment status (AA or AF).
    2. **Resubmission Path**: `SubmitRequest` logic is updated to return the request to the *corresponding* approval stage rather than restarting from the beginning.
    3. **Guidance Banner**: A Portuguese notification banner is shown at the top of the form during rework statuses to guide the user.
    4. **Audit Logging**: Resubmissions are logged as `RESUBMIT` in history with context-aware comments.

---

## DEC-037 — Rework Resubmission Real-Change Requirement (v0.9.8)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** To prevent premature resubmission, requests in rework status must be genuinely modified before they can be sent back to approval.
- **Decision:**
    1. **Change Tracking**: The backend `RequestsController.UpdateDraft` now compares incoming values with persisted ones and only updates `UpdatedAtUtc` if a field actually changed.
    2. **Authoritative Validation**: The `SubmitRequest` endpoint verifies that `request.UpdatedAtUtc` is strictly greater than the `CreatedAtUtc` of the latest `REQUEST_ADJUSTMENT` action in the status history.
    3. **Frontend Guard**: `RequestEdit.tsx` tracks `isDirty` and `hasSavedChanges` state to block the "REENVIAR PEDIDO" button if no session edits occurred, providing immediate Portuguese feedback: "É necessário realizar pelo menos uma alteração antes de reenviar o pedido."
- **Consequences:** Prevents "no-op" resubmissions and ensures the rework cycle contains actual progress.

---

---

## DEC-040 — Visual Standardization of Request Workflow (v0.9.11)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Ensuring a consistent "brutalist" design system and professional UX across all request screens (View, Edit, Rework, Approval).
- **Decision:**
    1. **Unified Banner System**: Status and Context banners now share the same padding (12px 24px), icon alignment, and border styles while preserving semantic differentiation via background colors and specific icons (ShieldCheck vs ShieldAlert).
    2. **Standardized Header Action Bar**: Unified buttons to 44px height (secondary) and 48px height (primary/approval) with uppercase labels and consistent font weights (800).
    3. **Page & Section Hierarchy**: Rigidly enforced levels (Page Title: 1.8rem, Section Title: 1.1rem) across all screens.
    4. **Confirmation Modal UX**: Standardized modal titles to uppercase and added "brutalist" input styling for approval comments. Centered action buttons for clearer focus.
- **Consequences:** Provides a highly professional, integrated feel for the application's most critical workflow. Enhances scannability and reduces cognitive load by establishing stable UI patterns.

## DEC-041: Request Workflow UI/UX Refinements (v0.9.12)

- **Date:** 2026-03-01
- **Status:** Approved
- **Context:** Need to improve the clarity of ownership and prioritization in the request workflow.
- **Decision:**
    1. **Banner Relocation**: Moved the responsibility/next-action banner below the header to allow natural scrolling.
    2. **Label Standardization**: Changed "CANCELAR" to "VOLTAR" for existing requests.
    3. **Visibility Enhancement**: Displayed Request Number prominently in the header.
    4. **Smart Priority Sorting**: Implemented status-aware sorting and urgency highlighting, excluding finalized requests (`REJECTED`, `CANCELLED`).
- **Consequences:** Reduces visual noise in the sticky header and helps users focus on active, high-urgency tasks. Ensures secondary sorting is robust by using `createdAtUtc`.

---

## DEC-042: Post-Approval Operational Pipeline Normalization (v0.9.13)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** The post-approval workflow for PAYMENT requests requires a clear operational sequence (PO Emission -> Scheduling -> Payment -> Receipt).
- **Decision:**
    1. **Semantic Sequence**: Normalized the transition flow to ensure the business meaning is unambiguous: `PAYMENT_COMPLETED` (Backend confirmed) -> `WAITING_RECEIPT` (Operational verification) -> `COMPLETED` (Finalized).
    2. **Operational Action Bar**: Introduced a standalone, sticky **AÇÕES OPERACIONAIS** section in `RequestEdit.tsx`. This bar is strictly isolated from approval/rework actions to prevent role confusion and visual clutter.
    3. **Status Definition Update**: Redefined `APPROVED` as an active "Working" status rather than a "Finalized" status. Urgency highlights now persist through the operational stages until the request reaches the definitive `COMPLETED` state.
- **Consequences:** Provides a robust, multi-step operational tracking system for finance and procurement teams. Ensures the portal remains a source of truth for the physical execution of approved payments.
- **Supersedes**: DEC-011 and DEC-041 regarding "Finalized" status definitions.

---

## DEC-043: Quotation Lifecycle and Financeiro Branching (v1.0.0+)

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** Deciding on the lifecycle for Quotation requests and the operational branching for Payments.
- **Decision:**
    1. **Quotation Lifecycle**: Quotation completion no longer leads to a terminal state. Instead, it advances to `WAITING_AREA_APPROVAL`. This ensures stakeholder review before finalization.
    2. **Financeiro Branching isolation**: In the `PO_ISSUED` status, the Financeiro role can choose between **PAGAR** (direct to `PAYMENT_COMPLETED`) or **AGENDAR PAGAMENTO** (to `PAYMENT_SCHEDULED`).
  - **Superseded by DEC-051**: This flow is now unified for both `PAYMENT` and `QUOTATION` types to ensure full financial traceability.
    1. **Finalized Status Behavior**: Finalized statuses (`COMPLETED`, `REJECTED`, `CANCELLED`) are excluded from urgency highlights. `QUOTATION_COMPLETED` is downgraded to an internal audit state and is no longer part of the standard finalizing path.
- **Consequências:** Ensina a um fluxo de negócio mais robusto para cotações enquanto mantém ramificações claras para todos os pedidos após aprovação.

---

## DEC-044: Workflow-Driven Typed Attachments

- **Date:** 2026-03-01
- **Status:** Accepted
- **Context:** The portal requires mandatory documentation for specific workflow stages (e.g., Proforma for submission, PO for registration). Generic attachments were insufficient for enforcement.
- **Decision:**
    1. **Typed Attachments**: Every attachment must have an `AttachmentTypeCode` (PROFORMA, PO, PAYMENT_SCHEDULE, PAYMENT_PROOF).
    2. **Multi-file Validation**: A stage is valid if at least one active, non-deleted file of the required type exists.
    3. **History Integration**: Uploads are logged in the existing request history using the same UI layout as status changes (`DOCUMENTO ADICIONADO`), preserving the current status for audit consistency.
    4. **Structural Deletion Lock**: Deletion is only permitted in editable stages. Once a document is "locked" in a confirmed workflow step, it cannot be removed.
- **Consequences:** Ensures compliance with fiscal and procurement rules. Maintains a clean, unified audit trail.

---

## DEC-045 — Consolidated Redirection and Mandatory Items for All

- **Date:** 2026-03-02
- **Status:** Accepted
- **Context:** To simplify the "Start of Process" and ensure data quality, we decided to unify the creation UX and submission requirements regardless of the request type.
- **Decision:**
    1. **Redirection**: All new requests (Quotation or Payment) now stay on the "Edit" screen after creation to allow immediate item/attachment entry.
    2. **Item Requirement**: At least one line item is now strictly required for BOTH "QUOTATION" and "PAYMENT" types upon initial submission.
- **Supersedes:** DEC-008, DEC-017, DEC-029
- **Consequences:** Provides a more consistent and predictable entry point for all users. Ensures all submitted requests have a baseline financial structure.

---

## DEC-046 — Locked Header during Quotation Gathering

- **Date:** 2026-03-02
- **Status:** Accepted
- **Context:** Deciding the degree of editability for Buyers during the "WAITING_QUOTATION" stage to prevent unintentional changes to the original requester's intent.
- **Decision:** In the "WAITING_QUOTATION" stage, the **Request Header is locked** (read-only). Buyers can only add/edit/delete **Line Items** and manage **Attachments** (including mandatory Proforma).
- **Consequences:** Protects the integrity of the original request's metadata (Need Level, Date, Participants) while allowing the Buyer to perform all necessary quotation tasks. Requires differentiated "canEdit" logic in the frontend.

---

## DEC-020 — Operational Action Gating by Type and Status

- **Date:** 2026-03-03
- **Status:** Superseded by DEC-051
- **Context:** Deciding how to manage the operational workflow transitions for different request types (Payment vs. Quotation) after final approval.
- **Decision (Superseded):** The original decision to allow `QUOTATION` requests to bypass scheduling/payment steps was **overturned in Stage 5**.
- **Current Rule (DEC-051)**: Both `QUOTATION` and `PAYMENT` requests follow the same unified financial sequence after `PO_ISSUED`.
- **Consequences:** Eliminates user-facing "BadRequest" errors during the operational phase and ensures backend data integrity for unified lifecycle transitions.

---

## DEC-021 — Multi-Factor Attachment Gating

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** Ensuring documents are uploaded only at the correct business stage to prevent workflow failures and user confusion.
- **Decision:** Implement a validation matrix (TypeCode × StatusCode) for both frontend visibility and backend processing.
  - **Frontend**: Sections for Payment Proofs are now visible for both `QUOTATION` and `PAYMENT` requests once a P.O is issued (PO_ISSUED status and beyond).
  - **Backend**: Update `Upload` endpoint to reject invalid stage combinations with `BadRequest`.
- **Consequences:** Provides a cleaner and guided UX, ensures audit trail integrity by strictly controlling when documents enter the system across the unified financial flow.

---

## DEC-022 — Status-Aware Request Edit Guidance

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** The previous UX showed a generic "Pedido Bloqueado" alert as soon as a request left the `DRAFT` status. However, in the `WAITING_QUOTATION` stage, certain fields (Supplier, Items) remain editable by the Buyer, leading to a contradictory and confusing experience.
- **Decision:** Implement a persistent, status-aware guidance banner in `RequestEdit.tsx` that explicitly distinguishes between full editability, partial editability (Quotation), and read-only modes.
  - **Implementation**: Replaced the generic alert with a context-aware header banner driven by precision booleans (`isDraftEditable`, `isQuotationPartiallyEditable`, `isFullyReadOnly`).
- **Consequences:** Resolves the misleading "non-editable" communication during the quotation phase, properly guides the buyer on what can still be changed, and improves the overall professional feel of the workflow transitions.

---

### DEC-023: Strict Contract Alignment & Nullability

Status: Accepted

We standardized on the `number | null` pattern for numeric IDs in the frontend to match backend `int?` types, replacing inconsistent usage of optional props and empty strings. We also promoted backend business codes (e.g., `'HIGH' | 'MEDIUM' | 'LOW'` for `itemPriority`) as the single source of truth for the frontend types, ensuring type safety and reducing mapping complexity.

---

## DEC-051 — Unified Post-PO Operational Workflow

- **Date:** 2026-03-03
- **Status:** Accepted
- **Context:** Originally, Quotation (COM) requests were designed to move directly from `PO_ISSUED` to `WAITING_RECEIPT`. However, the business process requires that both Quotation and Payment requests go through a financial flow (Scheduling -> Completion) once a PO exists.
- **Decision:** Unify the operational lifecycle after the `PO_ISSUED` status for both `QUOTATION` and `PAYMENT` types. Both must now undergo payment scheduling and proof of payment upload before moving to the receipt phase.## DEC-052 — Temporary Permission Gating for Gestão de Cotações (`X-User-Mode`)

- **Context:** The new `/buyer/items` "Gestão de Cotações" (formerly "Gestão de Itens") screen introduces inline editing of line items. Different fields require different role permissions (e.g., Cost Center can only be edited by an Area Approver, while Supplier is chosen by the Buyer). However, the enterprise SSO (JWT Roles) integration is not yet complete.
- **Decision:** Implement a temporary UI toggle that injects an `X-User-Mode` HTTP header into requests targeting `/api/v1/line-items`. The backend `LineItemsController` inspects this header to simulate role-based access control, throwing a `403 Forbidden` if a Buyer attempts to edit the `CostCenterId`.
- **Consequences:**

---

## DEC-054 — Modular Navigation Architecture

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** As the platform grows, a flat list of navigation entries becomes difficult to manage and visually overwhelming. There is a need for a scalable, modular structure that groups features by business area.
- **Decision:** Implement a grouped navigation structure in the `Sidebar.tsx`.
  - **Configuration-Driven**: Use a `MENU_ITEMS` array supporting `link`, `group`, and `action` types.
  - **Expandable Groups**: Modules like "Compras" are non-navigable containers that expand to show children.
  - **Auto-Expansion**: Groups automatically expand if one of their children's routes is active, ensuring the user always has context.
  - **Two-Tier Highlighting**: Parent groups use a subtle highlight when a child is active, while the active child receives the primary "strong" highlight.
- **Consequences:** Provides a clean visual hierarchy and makes the sidebar ready for future modules (RH, Financeiro, etc.) without further architectural changes. Improves accessibility with `aria-expanded` and consistent keyboard navigation.

---

## DEC-055 — Sidebar Layout: Independent Scrolling and Two-Zone Structure

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** The previous sidebar layout was dependent on the main page content scroll and became difficult to use when many navigation items were present. System actions like "Sair" were pushed off-screen.
- **Decision:** Adopt a full-height, two-zone sidebar layout.
  - **Independent Scroll**: The sidebar navigation zone is isolated with its own `overflow-y: auto`, decoupling it from the main workspace scroll.
  - **Fixed Bottom Actions**: System-level actions (Configurações, Sair) are pinned to the bottom of the sidebar container, ensuring they remain always visible.
  - **AppShell Integration**: Reconfigured the layout grid in `AppShell.tsx` to support a full-height sidebar track.
- **Consequences:** Dramatically improves ergonomics for power users. Ensures critical system controls are always accessible. Resolves the "scroll-overlap" issue between the menu and main content.

---

## DEC-053 — Post-Payment Delivery Follow-up and Mandatory Observations

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** Once a request reaches `PAYMENT_COMPLETED`, it enters an operational follow-up phase where the buyer manages the delivery of items with the supplier. This phase requires fine-grained tracking of item statuses and mandatory commentary for auditability during transitions to `ORDERED` or `RECEIVED` states.
- **Decision:**
    1. Introduce `IN_FOLLOWUP` as a new request-level status for the delivery cycle.
    2. Introduce `WAITING_ORDER` as the initial item status post-payment.
    3. Enforce mandatory observations for line item status changes to `ORDERED`, `PARTIALLY_RECEIVED`, or `RECEIVED` using the standard `ApprovalModal`.
    4. Record these item-level mutations in `RequestStatusHistory` as distinct audit events with human-readable descriptions.
- **Consequences:** Provides full delivery lifecycle traceability within the portal. Reuses existing modal patterns for consistent UX. Ensures that deliveries are documented with buyer comments, improving accountability and troubleshooting.

---

## DEC-056 — Dashboard de Compras Aggregation Logic

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** The new Dashboard de Compras requires aggregating multiple status codes into human-readable operational cards. Specifically, the "Aguardando Aprovação Final" and "Em Atenção" cards needed a precise business definition.
- **Decision:**
    1. **Aprovação Final Aggregation**: We decided to include `WAITING_COST_CENTER` (Inserir C.C) within the "Aguardando Aprovação Final" count. Although technically a separate status, it conceptually belongs to the final validation stage before a P.O is issued, aligning with user expectations for "Final Approval" oversight.
    2. **Attention Logic Alignment**: The "Em Atenção" card uses the same 4-day window (Today + 3) and terminal state exclusion as the primary `RequestsList` sorting logic. This ensures that the dashboard reflects the same operational priorities used in the daily workspace.
    3. **Educational vs Operational**: The interactive workflow guide is strictly educational. We decoupled it from real request data to prevent users from mistaking it for a control panel, using a "Guide/Manual" design language.
- **Consequences:** Ensures consistency between the dashboard overview and the daily operational lists. Provides clear guidance for new users without risking accidental data mutation.

---

## DEC-057 — Dashboard-to-List Filter Translation Pattern

- **Date:** 2026-03-18
- **Status:** Accepted
- **Context:** Deciding how to implement navigation from dashboard summary cards to specific filtered views in the Requests List without hardcoding numeric database IDs in the Dashboard component.
- **Decision:** Use semantic status codes (e.g., `WAITING_AREA_APPROVAL`) in the URL query string (`?statusCodes=...`).
  - **Frontend Translation**: The `RequestsList` component is responsible for translating these codes into physical database IDs by cross-referencing with the loaded status lookup data.
  - **Fallback**: If codes are provided but lookup data isn't loaded yet, the component waits for the lookup fetch to complete before parsing and applying the filters.
- **Consequences:** Decouples the Dashboard from specific DB IDs, making it more portable and less prone to breaking if IDs change between environments. Centralizes filter logic in the target list components. Improves URL readability for end users.

---

## DEC-058 — Informational Request Header for Requesters in Buyer Stages

- **Date:** 2026-03-19
- **Status:** Accepted
- **Context:** With the introduction of the dedicated Buyer Workspace, requesters should no longer see or interact with operational actions related to the quotation process. However, they still need clear visibility into the request's status and the buyer's responsibility.
- **Decision:** When a request is in a buyer-handled stage (like `WAITING_QUOTATION`), replace the actionable header area with a read-only informational panel.
  - **Content**: Display "Responsável atual: Comprador" and "Próxima ação: Pedido em tratamento do comprador".
  - **Labels**: Use informational section titles like "Andamento do Pedido" or "Status do Fluxo".
  - **Actions**: Hide "Concluir Cotação" and all operational CTAs for the requester in these stages.
  - **Editability**: Ensure the "Fornecedor" field (and items) are read-only for the requester during these stages to reflect buyer ownership.
- **Consequences:** Provides a cleaner, safer UX for the requester. Prevents confusion about unauthorized actions or inconsistent field editability while maintaining workflow transparency. Ensures the requester knows the request is being handled by the procurement team.

---

## DEC-059 — 2-Section Quotation Area in Buyer Workspace

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** The previous Buyer Workspace mixed manual item entry, document uploads, and operational actions in a single area, leading to confusion as requests often involve multiple quotations.
- **Decision:** Restructure the quotation management area into two distinct visual sections:
  - **Section A (Existing)**: Displays registered quotations and uploaded documents, providing clear visibility of what has already been processed.
  - **Section B (Add New)**: A dedicated entry zone with explicit **Mode Switching** (Upload vs. Manual).
- **Consequences:** Improves operational focus by separating "work done" from "work to be done". Provides a cleaner path for adding multiple quotations per request. Standardizes the entry flow with a "Back/Cancel" mechanism to prevent accidental state locks.

---

## DEC-060 — Reserved Review Area for Future OCR Integration

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** Preparing the system for a future OCR-based quotation extraction flow without performing real processing yet.
- **Decision:** Transition the "Reserved Review Area" from a placeholder to a real integration point. Connect the UI to the local OCR service through the Portal backend. Render actual suggestions (Supplier, Total, Items) using a structured field-and-table layout.

---

## DEC-061 — Local State for OCR Drafts (Step 4)

---

## DEC-068 — Unified Operational Flow and FINANCE Role (Stage 9.1)

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** A workflow inconsistency was identified where `QUOTATION` requests were bypassing the payment phase, while the backend correctly required `PAYMENT_COMPLETED` for receipt. Additionally, role responsibility for post-P.O. actions needed formalization.
- **Decision:**
    1. **Workflow Unification**: Both `QUOTATION` and `PAYMENT` requests now follow the same unified operational lifecycle: `PO_ISSUED` -> `PAYMENT_SCHEDULED` -> `PAYMENT_COMPLETED` -> `WAITING_RECEIPT`.
    2. **FINANCE Role Introduction**: Introduced a simulated `FINANCE` user mode to manage payment-related actions (`SCHEDULE_PAYMENT`, `COMPLETE_PAYMENT`).
    3. **Role Handover**: Finance is responsible for the payment phase, while the Buyer remains responsible for `APPROVED` (P.O. Registration) and `PAYMENT_COMPLETED` onwards (Receipt and Finalization).
- **Consequences:** Ensures full financial traceability for all request types. Clarifies actor responsibilities in the post-P.O. phase. Resolves the "Allowed statuses: PAYMENT_COMPLETED" conflict by aligning the UI guidance with backend validation.

---

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** Deciding how to manage edits to OCR extraction suggestions before they are persisted as official Quotation records.
- **Decision:** Use a dedicated local React state (`ocrDrafts`) to store the editable version of OCR results. Changes to header fields or line items are managed entirely on the frontend until the user explicitly triggers an "Apply" or "Conclude" action.
- **Consequences:** Provides a safe, isolated "sandbox" for buyers to review and correct extraction data without polluting the database with unverified or partial data. Decouples the OCR service logic from the main Quotation entity persistence, allowing for explicit "Restore to OCR Suggestions" functionality.

---

## DEC-062 — Improved Quotation Visualization & Comparison (Step 7)

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** Buyers need to quickly compare multiple quotations for the same request. A flat list of header totals was insufficient for detailed analysis.
- **Decision:** Implement a structured list in Section A of the Buyer Workspace with expand/collapse capabilities.
  - **Quotation Summary**: Shows Supplier, Doc Number, Date, and Grand Total.
  - **Item Details**: Expands to show a table of all quotation items (Description, Qty, Unit Price, Line Total).
  - **Visual Highlighting**: Automatically highlight the lowest total amount (MENOR VALOR) as a primary visual cue when multiple quotations share the same currency.
- **Consequences:** Significantly improves the comparison experience. Provides transparency into the breakdown of each quotation without cluttering the main view.

---

## DEC-063 — Unified Quotation Draft and Persistence Fixes (Step 8)

- **Date:** 2026-03-20
- **Status:** Accepted
- **Context:** The manual quotation mode had a UX gap where the form wouldn't open, and a technical bug caused item totals to display as '0,00' in the saved view.
- **Decision:**
  - **Unified Draft Logic**: Consolidate the "Editable Draft" state (`quotationDrafts`) for both OCR and Manual modes. "Inserir manualmente" now simply initializes an empty draft and triggers the same review/edit UI used by OCR.
  - **Explicit Persistence**: Ensure `handleSaveQuotation` explicitly maps and sends `lineTotal` for each item to the backend, preventing reliance on backend defaults or recalculations during the critical save path.
  - **API Response Enrichment**: Update the `SaveQuotation` endpoint to return the full itemized record, ensuring the frontend has immediate access to the persisted state.
- **Consequences:** Resolves the manual mode UX failure. Guarantees data integrity for saved quotations. Simplifies frontend logic by using a single path for all quotation entries.

---

## DEC-064 — Backend Schema Stabilization for Quotations

- **Date:** 2026-03-21
- **Status:** Accepted
- **Context:** Following the extension of the `Quotation` entity with `ProformaAttachmentId` and `DiscountAmount` for improved document tracking and financial management, a SQL Server mismatch occurred (`Invalid column name 'ProformaAttachmentId'`). This was due to the entity model advancing beyond the applied database migrations.
- **Decision:** Generate and apply a cumulative stabilization migration (`FixQuotationSchema`).
- **Consequences:** Resolves runtime SQL errors in `LineItemsController` and `RequestsController`. Ensures that all future quotation-related data (proforma links and header-level discounts) are correctly persisted and queryable. Reinforces the requirement for manual schema synchronization when modifying core transactional entities.

---

## DEC-065 — Transitional Authorization Model Enforcement

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** During Stage 9, a blocking error ("No authentication handlers are registered") emerged when the Final Approver attempted to select a winning quotation. This was caused by the use of ASP.NET Core's `Forbid()` result, which implicitly demands active authentication middleware (e.g., `AddAuthentication()`) to format the response. Because the project operates on a transitional dev simulation without real RBAC/auth infrastructure, the middleware crashed.
- **Decision:**
  - Strictly prohibit the use of `[Authorize]`, `Forbid()`, and `Unauthorized()` in the current project phase.
  - Enforce role-based access control by returning explicit `StatusCode(403, "Message")` when `GetUserMode()` or status rules fail.
  - Ensure this pattern is applied uniformly across all controllers (`RequestsController`, `LineItemsController`, etc.).
- **Consequences:** Restores full functionality to the quotation and item-editing workflows. Prevents developers from prematurely wiring empty auth middleware to silence errors. Keeps the codebase compatible with future real RBAC adoption (where these explicit 403s can simply be swapped for `[Authorize]` policies once the infrastructure exists).

---

## DEC-066 — Authoritative Quotation Selection (Stage 9)

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** Transitioning from a request-level attribute model to a quotation-first model where the winner defines the final commercial parameters.
- **Decision:**
    1. **Persistence**: The `SelectedQuotationId` is stored in the `Request` table.
    2. **Approval Blocking**: In `WAITING_FINAL_APPROVAL`, the final approver **must** select a winner before the "APROVAR" button becomes active. Backend validation enforces this with a 400 BadRequest.
    3. **Commercial Source**: Once selected (or approved), the items, total amount, and currency of the selected quotation become the authoritative source for the request.
    4. **Legacy Compatibility**: Request-level `SupplierId` and items remain for `PAYMENT` requests and as snapshots/history for `QUOTATION` requests, but they are visually superseded by the winner data in post-selection views.

---

## DEC-067 — Commercial Locking vs. Operational Continuation

- **Date:** 2026-03-22
- **Status:** Accepted
- **Context:** Avoiding workflow paralysis after a commercial decision is finalized.
- **Decision:**
    1. **Commercial Locking**: After `APPROVED`, the authoritative quotation selection and the vendor choice are fully locked.
    2. **Operational Continuation**: The operational phase (P.O. issuance, payment, receipt) remains fully active.
    3. **UI Integration**: Action buttons for the operational flow (e.g., "REGISTRAR P.O") are integrated into the persistent **Status Guidance Panels** of `RequestEdit.tsx`, ensuring the Buyer can always see and execute the next step regardless of the commercial data locking state.
- **Consequences:** Ensures full financial control over the commitment (winner selection) while allowing the business process to move forward smoothly.

---

## DEC-066 — Introducing the Receiving Workspace and RECEIVING Role

- **Date:** 2026-03-24
- **Status:** Accepted
- **Context:** The post-payment phase (RECEIPT) requires a dedicated workspace and functional role to separate operational delivery tasks from procurement (Buyer) and financial (Finance) tasks.
- **Decision:**
    1. Introduce a new functional role: `RECEIVING` (Recebimento).
    2. Create a dedicated **Receiving Workspace** on the frontend, filtered for `PAYMENT_COMPLETED` and `WAITING_RECEIPT` statuses.
    3. Update `getRequestGuidance` and role-switching logic to assign post-payment responsibility exclusively to the `RECEIVING` role.
    4. Implement a dedicated route `/receiving/workspace` and include it in a new "Operacional" sidebar group.
- **Consequences:** Provides clear ownership for the final stage of the workflow. Prevents "workflow pollution" in the Buyer and Finance workspaces. Establishes a modular foundation for future logistics/warehouse roles.

---

## DEC-070 — Status Badge Contrast Standardization

- **Date:** 2026-03-23
- **Status:** Accepted
- **Context:** The previous "10% opacity" badge styling resulted in insufficient contrast for light status colors like Amber, Yellow, and Cyan, violating WCAG AA accessibility standards.
- **Decision:** Switch to solid background colors for all status badges, with dark text (`#111827`) enforced for light backgrounds (Yellow/Amber/Orange) to ensure a minimum 4.5:1 contrast ratio.
- **Consequences:** Significantly improves legibility for critical statuses like `PENDENTE`. Centralizes badge styling in `globals.css` via semantic classes, eliminating inconsistent inline-style implementations.

---

## DEC-073 — Quotation Workflow Locking and Mutability Boundary

- **Date:** 2026-03-29
- **Status:** Accepted
- **Context:** Quotation data integrity was at risk if buyers could modify, replace, or delete quotations after a request had been approved or advanced to final operational stages.
- **Decision:** Implement a strict 'Read-Only' boundary for quotations once a request advances beyond the quotation/adjustment phase.
    1. **Centralized Rule**: Create RequestWorkflowHelper.CanMutateQuotation(statusCode) as the single source of truth.
    2. **Backend Enforcement**: Apply the guard to all mutation endpoints (Save, Update, Delete, OCR, Proforma Management).
    3. **Frontend Hardening**: Hide mutation actions in the Buyer Workspace for post-quotation requests.
- **Consequences:** Guarantees commercial data consistency throughout the approval and operational lifecycle. Prevents accidental or unauthorized changes once stakeholders have begun the approval process. Improves auditability.

---

## DEC-074 — Contextual Attachment Placement in Request Creation

- **Date:** 2026-03-30
- **Status:** Accepted
- **Context:** The "Documentos de Apoio" section in the Request Draft Creation form was located at the very bottom, creating a fragmented UX where users had to scroll away from the justification field to attach supporting files.
- **Decision:** Move the attachment area directly below the "Descrição ou justificativa" field in the "Dados Gerais do Pedido" section.
    1. **Integrated UI**: Removed the separate heavy section block.
    2. **Inline Treatment**: Used a lighter inline/sub-label header ("Documentos de Apoio") instead of a full section header.
    3. **Compact Dropzone**: Reduced the padding and visual weight of the upload dropzone to align with the justification field.
- **Alternatives considered:** Keeping the separate section but moving it up. Rejected as it still felt like a disconnected block.
- **Consequences:** Improves the natural flow of request explanation, reducing the likelihood of users forgetting to attach mandatory or supporting files. Makes the form feel more cohesive and modern.

---
---

## DEC-074 — Restauração Condicional da Data de Necessidade

- **Date:** 2026-03-30
- **Status:** Accepted
- **Context:** O campo "Data de Necessidade" (`NeedByDateUtc`) foi removido anteriormente mas precisava ser restaurado para pedidos de Cotação, seguindo uma regra de visibilidade condicional.
- **Decision:** Restaurar o campo de forma integrada (reutilizando a estrutura de DTO/Entidade existente) com lógica condicional no frontend.
    1. **Visibilidade**: Visível apenas quando `RequestTypeId` (ou Code) é `QUOTATION`.
    2. **Obrigatoriedade**: Obrigatório no frontend e backend apenas para `QUOTATION`.
    3. **UX**: Utilização de `AnimatePresence` para transição suave e posicionamento dentro do grid de "Dados Gerais".
    4. **Payload**: Enviar `null` explicitamente quando o tipo não for `QUOTATION` ou o campo estiver oculto.
- **Alternatives considered:** Criar um novo campo separado ou manter persistência mesmo quando oculto. Rejeitado para evitar inconsistência de dados.
- **Consequences:** Garante que pedidos de Cotação tenham datas limite claras enquanto mantém o formulário simplificado para Pedidos de Pagamento.

---

## [2026-03-30] DEC-075: Request Form Layout Optimization

- **Context:** The "Request Draft Creation" and "Request Edit/Details" screens were using a rigid, legacy `maxWidth: 1000px` shell. On modern desktop displays, this created excessive empty margins and an unnecessarily cramped experience for complex line item tables and multi-column forms.
- **Decision:** Increased the effective width of the primary request form container to **1440px** and established a standard for responsive, wider form layouts in the portal.
- **Implementation Detail:**
    1.  Updated `RequestCreate.tsx` and `RequestEdit.tsx` to use `width: 100%, maxWidth: '1440px'`.
    2.  Added `minWidth: 0` to top-level page containers to ensure flex/grid child stability.
    3.  Maintained `margin: '0 auto'` for center-alignment on ultrawide displays.
- **Alternatives considered:** Making the form fully fluid (100% width). Rejected because single-column fields or long descriptions become difficult to read when stretched beyond 1500px.
- **Consequences:** More breathing room for the "Itens do Pedido" table, better utilization of the AppShell workspace, and improved visual consistency with the Requests List screen.

---

## [2026-03-30] DEC-076: Relaxing Validation for Payment Requests (PAG)

- **Context:** In the "Payment Request" workflow, the "Fornecedor" (Supplier) and "Planta de destino" (Item Destination Plant) fields were strictly enforced at the draft and submission stages. This created a rigid experience for users who only had partial information at the start of the extraction or manual entry process.
- **Decision:** Relaxed the validation for `SupplierId` (Request level) and `PlantId` (Line Item level) specifically for **Payment** request types.
- **Implementation Detail:**
    1.  Removed the `[Required]` attribute from `PlantId` in `CreateRequestLineItemDto` and `UpdateRequestLineItemDto`.
    2.  Implemented manual, conditional `PlantId` validation in `RequestsController.cs` for non-Payment types.
    3.  Removed hard-coded `SupplierId` requirement for Payment requests in `UpdateDraft` and `SubmitRequest` controller methods.
    4.  Updated frontend components (`RequestEdit.tsx` and `RequestLineItemForm.tsx`) to make the visual mandatory indicators (red asterisks) and input `required` attributes conditional on the request type code.
- **Alternatives considered:** Keeping the backend fields mandatory while only relaxing the frontend. Rejected because it would cause 400 Bad Request errors when saving valid partial drafts.
- **Consequences:** More flexible "Draft-to-Submission" workflow for Payments, allowing for incomplete supplier or plant data when capturing from documents (OCR).

---

## DEC-101 — Guided UX Attention Patterns

- **Date:** 2026-04-01
- **Status:** Accepted
- **Context:** Complex forms like `RequestEdit` have multiple collapsible sections. In specific workflow stages (e.g., Area Approval), users need to be quickly guided to newly available or critical information (like Saved Quotations) without manual exploration.
- **Decision:** Implement a standardized "Guided Attention" pattern for critical workflow transitions:
    1. **Auto-Expand**: Automatically expand relevant `CollapsibleSection` components.
    2. **Auto-Scroll**: Smoothly scroll the targeted section into the viewport with a fixed offset (e.g., `-220px`) to account for sticky headers and action bars.
    3. **Visual Highlight**: Apply a temporary "pulse" or high-contrast border/shadow effect (e.g., Red glow) for 3-5 seconds.
    4. **One-Time Execution**: Ensure the effect triggers only once per initial load of a specific record (using Ref-based flags) to avoid annoying re-triggers on local state updates.
- **Alternatives considered:** Permanent banners or blocking pop-ups. Rejected as too invasive and disruptive to the industrial UI feel.
- **Consequences:** Significantly improves task completion speed for approvers and buyers. Establishes a predictable pattern for guiding users through complex state-dependent forms.

---

## DEC-086 — Security Hardening Phase 1: Uploads & Login Protection

- **Date:** 2026-04-02
- **Status:** Accepted
- **Context:** Following a security audit, two critical gaps were identified: the lack of file upload restrictions and the absence of brute-force protection during authentication.
- **Decision:** Implement a "Minimum Safe" baseline for Phase 1:
    1.  **Attachment Uploads**:
        *   **Whitelist-only**: Only `.pdf`, `.jpg`, `.jpeg`, `.png`, `.doc`, `.docx`, `.xls`, `.xlsx` are allowed.
        *   **Size Limit**: Enforced at **15MB** per file.
        *   **Filename Sanitization**: Original filenames are sanitized (removing non-alphanumeric/hyphen/underscore) before storage in the DB to prevent UI injection and path traversal risks.
        *   **Physical Storage**: Files are stored using GUIDs, completely decoupling the physical filename from user input.
        *   **MIME Signal**: Basic `ContentType` consistency check is implemented as a secondary signal, not a hard blocking rule.
    2.  **Login Protection**:
        *   **Lockout Policy**: Accounts are temporarily locked for **15 minutes** after **5 failed attempts**.
        *   **Generic Error Messages**: Failed logins return a generic unauthorized message to prevent user enumeration.
        *   **Audit Logging**: Lockout events and blocked attempts are logged to the `AdminLogEntries` for SOC review.
- **Consequences:** Legacy files remains accessible. User experience is slightly impacted by lockout, but generic messaging maintains high privacy. Future hardening (IP-based throttling, deep MIME inspection) is planned for Phase 2.

---

## DEC-087 — Security Hardening Phase 2: IP-Based Rate Limiting

- **Date:** 2026-04-02
- **Status:** Accepted
- **Context:** While Phase 1 addressed user-specific lockouts, it left the system vulnerable to distributed brute-force attacks against non-existent users and resource exhaustion at the API edge.
- **Decision:** Implement IP-based rate limiting using ASP.NET Core built-in middleware.
    1.  **Strict Policy (LoginPolicy)**: Applied exclusively to `AuthController.Login`.
    2.  **Thresholds**: 10 permits per 1-minute fixed window per Remote IP.
    3.  **Localhost Configuration**: Rate limiting is configurable for localhost (disabled by default in Dev, but enabled via `Security:RateLimiting:EnableForLocalhost`) to allow local validation.
    4.  **Generic Rejection**: Returns `429 Too Many Requests` with a generic message: "Muitas tentativas. Tente novamente em breve."
    5.  **Throttled Audit Logging**: Logs `IP_RATE_LIMITED` to `AdminLogEntries` once per minute per IP to prevent audit log flooding.
    6.  **Safe IP Resolution**: Relies on `RemoteIpAddress`. Forwarded headers are only trusted if `ForwardedHeadersOptions` are explicitly configured for a trusted proxy.
- **Consequences:** Provides a high-performance first line of defense. Reduces database and CPU load during brute-force campaigns. Minimal impact on legitimate users behind shared NATs due to the generous 10/min threshold.

---

## DEC-090 — Cost Center Refinement: PAYMENT vs QUOTATION Behavior

- **Date:** 2026-04-02
- **Status:** Accepted
- **Context:** DEC-085 introduced mandatory Cost Center selection for Area Approvers. However, for **PAYMENT** requests, Cost Centers are often pre-defined during the requisition/item phase. Forcing a re-selection for already valid data is redundant. Conversely, **QUOTATION** requests often lack a definitive CC until the approval stage.
- **Decision:** Implement a conditional validation and display logic in the Approval Center:
    1. **Unified PAYMENT**: If all line items in a PAYMENT request share the same non-null Cost Center, the field is rendered as **Read-Only** with a green "Validado" badge. Approval is not blocked.
    2. **Inconsistent PAYMENT**: If a PAYMENT request has items with different Cost Centers, it is treated as a data conflict. The UI shows a red **"Conflito: Unificação Obrigatória"** warning and forces the approver to select one, which then propagates to all items (consistent with DEC-085's unification goal).
    3. **Missing/QUOTATION**: If data is missing or it is a QUOTATION request, the standard mandatory dropdown behavior remains.
- **Consequences:** Reduces friction for the most common PAYMENT scenarios while maintaining strict financial integrity for edge cases and quotations. Ensures all approved requests leave the Area stage with a unified, valid Cost Center.

