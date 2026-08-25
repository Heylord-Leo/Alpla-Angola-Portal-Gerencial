# Buyer Workspace — Supplier Sheet Capability Model, Drawer & Classic Dependency Status

> Final-state reference for the Buyer Queue + Workspace campaign (Phases 3A–3E). Companion to
> [`BUYER_QUEUE_CANONICAL_MODEL.md`](BUYER_QUEUE_CANONICAL_MODEL.md) (queue + Workspace projection),
> [`BUYER_WIZARD_HOST_EXTRACTION_PLAN.md`](BUYER_WIZARD_HOST_EXTRACTION_PLAN.md) (shared Quotation Wizard
> controller) and [`SUPPLIER_INTELLIGENCE_MODEL.md`](SUPPLIER_INTELLIGENCE_MODEL.md) (carousel metrics).

## 1. Supplier Sheet capability model (backend-authoritative)

The Supplier Sheet ("ficha") is a **single source of truth**: one form, one set of APIs, reused by the
Contracts route (`/contracts/fichas/:id`) and the Buyer Workspace drawer. Authorization is decided
**only on the backend** and returned to the UI as a `capabilities` object on `GET /lookups/suppliers/{id}/ficha`.
The frontend never maps roles to permissions — it consumes the flags to hide/read-only controls.

- **Model:** `SupplierSheetCapabilities` + `ISupplierCapabilityEvaluator`
  (`AlplaPortal.Application/Interfaces/ISupplierCapabilityEvaluator.cs`), implemented by
  `SupplierCapabilityEvaluator` (`AlplaPortal.Infrastructure/Services/Suppliers/`).
- **Request-scoped Buyer access:** a Buyer may act on a supplier only when it is involved
  (`Request.SupplierId` | `Quotations` | `LineItems` | `PoGroups`) in a request the Buyer is authorized to
  access under the **canonical** request scope — `RequestAccessScope.ScopedRequestsAsync`
  (`AlplaPortal.Infrastructure/Data/`), the same policy behind `/buyer/requests/{id}` (plant/department,
  **not** ownership). `BaseController.GetScopedRequestsQuery` delegates to it (single source).
- **Field-level writes:** `PUT /ficha` applies only the field groups the caller may edit; an attempt to
  change a forbidden group is rejected with **403** (`SupplierFichaFieldGuard`), never silently ignored.
- **Status defect fix:** governance lifecycle (`PUT /status`) removed Buyer **and** Local Manager from the
  role gate; a `CanChangeStatus` assertion backs it up.

### Capability matrix

| Capability | Buyer (in-scope) | Contracts | Finance | Local Manager | System Admin |
|---|:--:|:--:|:--:|:--:|:--:|
| View | ✅ | ✅ | ✅ | ✅ | ✅ |
| Edit Contacts / Address / Observations | ✅ | ✅ | ✅ | ✅ | ✅ |
| Upload Documents | ✅ | ✅ | ✅ | ✅ | ✅ |
| Delete Documents | ❌ | ✅ | ✅ | ✅ | ✅ |
| Edit Identity/Tax | ❌ | ✅ | ✅ | ✅ | ✅ |
| Edit Banking | ❌ | ✅ | ✅ | ✅ | ✅ |
| Edit Commercial | ❌ | ✅ | ✅ | ✅ | ✅ |
| Submit for Approval | ❌ | ✅ | ✅ | ✅ | ✅ |
| Change Status | ❌ | ✅ | ✅ | ❌ | ✅ |
| Approve / Reject | ❌ | ❌ | ❌ | ❌ | ✅ |

Approve/Reject are the DAF/DG endpoints (Area/Final Approver), not part of the Supplier Sheet surface.

## 2. Supplier Sheet content & drawer

- **`SupplierFichaDetailContent`** (`src/frontend/src/pages/Contracts/`) — the single reusable form.
  Props: `supplierId`, `hostMode: 'page' | 'drawer'`, `onClose?`, `onSaved?`, `onDirtyChange?`. It never
  reads `useParams`.
- **Page host** `SupplierFichaDetail` — thin route wrapper (`hostMode="page"`).
- **Drawer host** `BuyerSupplierFichaDrawer` (`src/frontend/src/pages/Buyer/`) — right-side drawer opened
  imperatively from the Workspace supplier carousel's **"Ver Perfil Completo"**. Owns only chrome +
  open/close + dirty and supplier-switch guards (pure decisions in `supplierDrawerGuard.ts`). A dirty close
  or switch raises the standard confirmation (Descartar alterações / Continuar editando); never auto-saves.
  Successful save keeps the drawer open, clears dirty, and silently refreshes the Workspace projection
  (carousel card) while preserving route/tab/carousel index.

## 3. Classic dependency status (`/buyer/items/classic`)

The Workspace covers the core loop; the classic workbench (`BuyerItemsList`) is retained for the actions
not yet migrated, reachable via **"Abrir ferramentas clássicas"** (Workspace) and **"Tela clássica"**
(queue). **Do not remove classic** until these land in the Workspace.

| Action | In Workspace | Classic only |
|---|:--:|:--:|
| Add quotation (new) — OCR + manual | ✅ (shared Wizard controller) | |
| Complete quotations (partial coverage) | ✅ | |
| Submit approval batch | ✅ (BuyerApprovalBatchHost) | |
| Rework batch | ✅ (BuyerBatchReworkHost) | |
| Supplier profile (Ficha drawer) | ✅ (new) | |
| Edit existing quotation | | ✅ (controller supports it; Workspace wiring deferred) |
| Delete quotation | | ✅ |
| Remove proforma / document | | ✅ |
| Close item "não cotado" | | ✅ |
| Reutilizar cotação (reuse authorization) | | ✅ |
| Cancel batch | | ✅ |
| Assign-to-me / claim request | | ✅ |
| Cancel request | | ✅ |

## 4. DEV harness

Buyer synthetic fixtures (`ZZTEST-BUY-*`) via `BuyerDevFixtureController`, documented in
[`BUYER_DEV_REGRESSION_HARNESS.md`](BUYER_DEV_REGRESSION_HARNESS.md). Supplier Sheet scope/capability
paths are exercised with a synthetic supplier involved in a `ZZTEST-BUY` request; reset fixtures after use.
