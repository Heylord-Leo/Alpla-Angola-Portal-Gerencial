# Release 3 — Consumer audit of `Request.SupplierId`, `Request.PlantId`, `Request.SourceDocumentType`

> Mandated before any model change. Every backend consumer classified as
> **SAFE** (compatibility/display/routing — stays request-level) ·
> **GROUP** (must become group-scoped) ·
> **DOCUMENT** (must become source-document-scoped) ·
> **OBSOLETE**.

---

## 1. Governing rule

`Request.SupplierId`, `Request.PlantId` and `Request.SourceDocumentType` remain **authoritative for
authorization, approval routing and display**, and become **non-authoritative for obligations,
grouping and totals** on a multi-document PAYMENT request.

That split is what keeps the blast radius small. A request is still created *under* one plant and one
department — that is who approves it and who may see it. What changes is that the **documents**, not
the request header, decide what is being bought, from whom, for which plant, and what will be owed
afterwards.

`Request.SupplierId` / `PlantId` / `SourceDocumentType` are populated only while the request has
**exactly one active `PaymentSourceDocument`**; with several, `SupplierId` and `SourceDocumentType`
are set to `null` and the UI reports "vários". `PlantId` is **not** nulled — it remains the routing
plant (§3).

---

## 2. `Request.SourceDocumentType` — 7 consumers

| # | Site | Class | Action |
|---|---|---|---|
| 1 | `RequestsController:1124` — `RequestDetailsDto` projection | **SAFE** | Compatibility display. Keep; the DTO gains `SourceDocuments[]`. |
| 2 | `RequestsController:2092` — classification override audit (create) | **DOCUMENT** | Moves to per-document; key scope changes to `PaymentSourceDocumentId`. |
| 3 | `RequestsController:2376–2381` — update-draft classification | **DOCUMENT** | Moves to the source-document endpoint. |
| 4 | `RequestsController:2414` — classification override audit (update) | **DOCUMENT** | As #2. |
| 5 | `RequestsController:2676` — **submission gate**, `Resolve(...)` | **DOCUMENT** | Must validate **every** active document, not the header. |
| 6 | `RequestsController:5854` — **payment PO-group creation**, `Resolve(...)` | **DOCUMENT** + **GROUP** | Replaced by grouping over documents (§4). |
| 7 | `EntityConfigurations:27` — column mapping | **SAFE** | Column retained as compatibility. |

---

## 3. `Request.PlantId` — 57 sites, 5 needing change

### 3.1 SAFE — authorization, routing, filters, event payloads (48 sites)

Plant is the request's **access and routing scope** and stays request-level. No change.

| Category | Sites |
|---|---|
| **Authorization by plant** | `BaseController:55` · `NotificationService:382` · `RequestsController:651` |
| **Approval routing** | `RequestsController:97, 191, 556, 674, 684, 698, 704, 1332, 2614, 2618–2624` · `ApprovalBatchController:136, 308` · `AreaApproverReconciliationService:50` · `ProformaDeadlineAlertService:255` · `WorkflowNotificationOrchestrator:271, 385` |
| **Workflow event payloads** | `RequestsController:2140, 2163, 2928, 7253, 7492, 7714, 8052, 8076` |
| **Queues / filters / projections** | `RequestsController:960, 1108, 1595` · `FinanceController:475, 1184, 1431, 1529, 1717` · `LineItemsController:165` · `Program.cs:543` · `ApprovalQueueProjection:67` |
| **Header edit + index** | `RequestsController:2438, 2464` · `ApplicationDbContext:854` · `LookupsController:749` |
| **Approval intelligence** | `ApprovalIntelligenceService:371, 373, 569, 570` |

### 3.2 DOCUMENT — line defaults (2 sites)

| # | Site | Action |
|---|---|---|
| 8 | `RequestsController:2033` — `PlantId = itemDto.PlantId ?? request.PlantId` | Becomes `itemDto.PlantId ?? sourceDocument.PlantId ?? request.PlantId`. |
| 9 | `RequestsController:5265` — item created from proforma | Same fallback chain when the item belongs to a source document. |

### 3.3 GROUP — budget allocation (2 sites) — **a latent defect this feature exposes**

| # | Site | Finding |
|---|---|---|
| 10 | `BudgetPreviewController:211` | `int plantId = request.PlantId ?? 0;` — the line's **own** `PlantId` is never consulted, even though `RequestLineItem.PlantId` already exists and can already differ. |
| 11 | `BudgetCalculationHelper:227` | Identical fallback in the authoritative calculation. |

With multi-plant payment requests this misallocates budget to the header plant. **Fix: prefer
`li.PlantId ?? request.PlantId`.** This is more correct today as well, not only after the change —
it is a pre-existing bug that multi-document would have turned into a visible one.

### 3.4 FALSE POSITIVES — a different `request` variable (10 sites)

`HRLeaveController:470, 476, 505, 507` · `ITEquipmentController:269, 287, 297, 309, 314` — DTO
parameters named `request`, unrelated to the `Request` entity. No action.

---

## 4. `Request.SupplierId` — 20 sites, 4 needing change

### 4.1 SAFE (13 sites)

| Category | Sites |
|---|---|
| Projections / display | `RequestsController:964, 1115` · `LineItemsController:172` · `Program.cs:550` |
| Quotation-driven header sync (QUOTATION only) | `RequestsController:1436, 7775, 7794` |
| Header edit (single-document path) | `RequestsController:2494, 2516` |
| Buyer-processing heuristic | `RequestsController:4908` |
| Supplier link/unlink (single-document path) | `RequestsController:7541, 7545, 7643` |

### 4.2 DOCUMENT (4 sites)

| # | Site | Action |
|---|---|---|
| 12 | `LineItemFactory:69` — `SupplierId = typeCode == "PAYMENT" ? request.SupplierId : null` | Take the supplier from the item's `PaymentSourceDocument` when one is set. |
| 13 | `RequestsController:5470` — `item.SupplierId = request.SupplierId` on PAYMENT edit | Same. |
| 14 | `RequestsController:5818–5832` — **payment PO-group creation** | Replaced by grouping over documents (§5). |
| 15 | `RequestsController:5470` context — `SupplierName = null` | Follows #13. |

---

## 5. Payment PO-group creation — the single largest change

**Today** (`ProcessFinalApproval`, `RequestsController:5808–5864`): exactly **one** group per payment
request, from `request.SupplierId` / `CurrencyId` / `EstimatedTotalAmount`.

**Release 3:** group the request's active line items by

```
Supplier + Currency + PaymentCondition + Plant + SourceDocumentType
```

taking supplier / plant / type from each item's `PaymentSourceDocument`, and the total from the sum
of the item totals in each group. `RequestPoGroup` gains `PlantId`.

The quotation path (`GroupBuilderService`, key `Supplier + Currency + PaymentCondition`) is **not
touched**.

---

## 6. Summary

| Class | Count | Meaning |
|---|---|---|
| **SAFE** | 61 | Authorization, routing, event payloads, queues, projections, single-document header edits |
| **DOCUMENT** | 9 | Classification, submission gate, line defaults, supplier propagation |
| **GROUP** | 3 | Payment PO-group creation, budget allocation ×2 |
| **OBSOLETE** | 0 | Nothing removed; the header fields survive as compatibility |
| **FALSE POSITIVE** | 10 | Different `request` variable |

**Known limitation, recorded deliberately:** budget scope, approval routing and plant authorization
all continue to use the **request-level** plant. A payment request spanning two plants is therefore
approved and access-scoped by the plant it was created under, while its *groups and obligations*
follow the documents. Making routing itself multi-plant is a workflow change well beyond Release 3
and is not attempted here.
