# Master Data Guidelines

## 1. Goal

This document establishes a standard procedure for handling Master Data (also known as lookup or reference data). Whenever a new reusable list of values is needed, this implementation must follow a consistent pattern rather than being hardcoded ad-hoc.

## 2. What is Master Data?

Master Data comprises the core entities of the enterprise that describe the objects around which business is conducted. In the context of this project, it represents reference values that populate UI dropdowns and enforce data integrity.

Examples of Master Data include, but are not limited to:

- Units of Measure (EA, KG, L, etc.) ✅ Implemented
- Currencies (AOA, USD, EUR) ✅ Implemented
- Need Levels / Graus de Necessidade (Baixo, Normal, Urgente, Crítico) ✅ Implemented
- Departments (Departamentos) ✅ Implemented (v0.5.0)
- Plants / Fábricas (Plantas) ✅ Implemented (v0.5.0)
- Suppliers (Fornecedores) ✅ Implemented (v0.6.0)
- Request Types (Quotation, Payment) ✅ Seeded (read-only)
- Categories / Classifications (CAPEX, OPEX) ✅ Seeded (read-only)
- Cost Centers (Centros de Custo) — Planned

## 3. Standard Procedure for Master Data

Whenever a new set of reference values is introduced, developers **must** adhere to the following checklist:

### Step 1: Evaluate Reusability

Assess whether the new value list is specific to a single component or reusable across multiple modules. If it is purely structural logic (like a simple boolean state), it might not be Master Data. If it represents a business noun or categorization, treat it as Master Data.

### Step 2: Avoid Hardcoding

**Rule:** Do not hardcode reusable lookup lists directly into the Frontend (React components) or Backend (C# Enums or conditionals), unless the domain logic strictly mandates it (and even then, prefer DB-driven lookups). All enumerations presented to the user should be fetched from the API.

### Step 3: Define Entity / Table Strategy

Define whether the Master Data requires an explicit SQL table (`Lookups` schema or dedicated table):

- **Simple Lookups:** Entities that only need an `Id`, `Code`, and `Name`/`Description` (e.g., Request Types, Priorities). Usually derived from a base Lookup entity class.
- **Complex Lookups:** Entities that hold relationships or specific business metadata (e.g., Currencies might hold conversion rates; Plants might link to Cost Centers; Units might hold validation flags like `AllowsDecimalQuantity`).

### Step 4: Define Seed Data for Local Development

Ensure the application can boot cleanly on any developer's machine:

- Implement Entity Framework Core `HasData()` configurations or a dedicated `DbSeeder` class.
- Seed the essential, base system values (e.g., `1 = Normal`, `2 = Urgente`) so the database is never empty upon migration.

### Step 5: Define API Access (Read-Only vs. CRUD)

Determine the exposure level:

- **Default (Read-Only):** Expose a simple `GET /api/v1/lookups/{resource}` endpoint that returns `{ id, code, name }` for React dropdowns.
- **Full Maintenance (CRUD):** If business users need to add/edit options (e.g., adding a new Cost Center), implement an administrative CRUD controller (`POST`, `PUT`, `DELETE`).

### Step 5b: Active vs Inactive Filtering Rule

**Rule:** Inactive Master Data records must remain available for historical/reference purposes in the database and API responses retrieving exact entities. However, they **must not** appear in selection lists (dropdowns) for new operations. All backend lookup endpoints used for React Select binds must default to active records only (`includeInactive = false`).

### Step 6: Define Validation & Usage Rules

Specify how consuming modules should use this data:

- Backend: Expose Foreign Keys (`CurrencyId`). Enforce referential integrity in EF Core.
- Frontend: Bind React Select inputs to the lookup arrays. Handle loading/error states for dropdowns gracefully.

### Step 7: Documentation Updates

Whenever new Master Data is added:

- Update `DATA_MODEL_DRAFT.md` with the new table/schema.
- Update `API_V1_DRAFT.md` with the new Lookup endpoint.
- Register any complex decision logic in `DECISIONS.md`.

### Step 8: Keep Future Expansion in Mind (Soft Delete is Mandatory)

**Rules for physical deletion prevention:**

- Never delete Master Data records physically via `DELETE` statements or EF Core `Remove()`.
- Always design reference tables with standard audit fields (e.g., `public bool IsActive { get; set; } = true;` and `CreatedAt`).
- Implement UI toggles (`PUT /toggle-active`) instead of delete buttons.
- Filter out inactive records by default in dropdown `GET` endpoints (`?includeInactive=false` by default).
- This allows legacy transactions to retain historical associations without breaking older views, even if the user cannot select that lookup value in new forms.

## 9. Appendix: Core Workflow Request Statuses

The enterprise sets the following standard 18 milestones for the Request Workflow. Any system implementation *must* seed these technical internal `Code` names, mapped to their `DisplayOrder` and UI `BadgeColor`.  The standard database seeding mechanism applies these constants at boot:

1. `DRAFT` (Rascunho) - gray
2. `WAITING_QUOTATION` (Aguardando Cotação) - blue
3. `WAITING_AREA_APPROVAL` (Aguardando Aprovação Área) - indigo
4. `AREA_ADJUSTMENT` (Reajuste A.A) - orange
5. `WAITING_FINAL_APPROVAL` (Aguardando Aprovação Final) - purple
6. `FINAL_ADJUSTMENT` (Reajuste A.F) - teal
7. `REJECTED` (Rejeitado) - red
8. `WAITING_COST_CENTER` (Inserir C.C) - yellow
9. `APPROVED` (Aprovado) - green
10. `PROFORMA_INVOICE_INSERTED` (Fatura Proforma Inserida) - slate
11. `PO_REQUESTED` (Solicitado P.O) - cyan
12. `PO_ISSUED` (P.O Emitida) - sky
13. `PAYMENT_REQUEST_SENT` (Solicitação Pagamento Enviada) - rose
14. `PAYMENT_SCHEDULED` (Pagamento Agendado) - violet
15. `PAYMENT_COMPLETED` (Pagamento Realizado) - fuchsia
16. `WAITING_RECEIPT` (Aguardando Recibo) - stone
17. `COMPLETED` (Finalizado) - emerald
18. `CANCELLED` (Cancelado) - amber

---

## 10. Scaling Large Masters (Autocomplete Pattern)

...

## 11. Internal Role Simulation (Transitional)

To support multi-stakeholder workflows before a formal RBAC system is implemented, the project utilize a **Transitional Role Simulation** model.

### Available Functional Roles

- **BUYER** (Comprador): Owns the quotation and P.O. registration phases.
- **AREA_APPROVER** (Aprovador de Área): Responsible for technical and cost center validation.
- **FINAL_APPROVER** (Aprovador Final): Responsible for commercial winner selection and final approval.
- **FINANCE** (Financeiro): Owns the payment scheduling and completion phases.
- **RECEIVING** (Recebimento): Owns the post-payment receiving and closure phases (v1.1.41+).

### Implementation

- **Frontend**: The active role is stored in `localStorage` under `user_mode` and passed via the `X-User-Mode` header.
- **Simulated Context**: Each role has its own workspace (e.g., Buyer Workspace, Receiving Workspace) that automatically enforces the required mode.
- **Action Gating**: UI controls and operational buttons are conditionally rendered based on the active `userMode`.
