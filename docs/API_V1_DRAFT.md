# REST API Draft (V1)

## Document Info

- **Project:** Alpla Angola Internal Portal
- **Module:** Purchase Requests & Payments
- **Version:** 0.1.0 (Draft)
- **Status:** Draft
- **Related Specs:** `REQUIREMENTS_V1.md`, `ACCESS_MODEL.md`, `DATA_MODEL_DRAFT.md`, `STACK_DECISION.md`

---

## 1. Purpose

This document defines the draft REST API contract for the **V1 Purchase Requests & Payments** module. It serves as the synchronization point between the ASP.NET Core backend and the React frontend.

*Intentionally excluded from V1 Draft:* Final C# DTO names, detailed authentication middleware configuration (e.g., specific JWT algorithms), and external integration endpoints (AlplaPROD/Primavera).

---

## 2. API Design Principles (V1)

- **RESTful Resource Orientation:** Nouns represent entities (e.g., `/requests`), verbs represent HTTP methods (`GET`, `POST`), and actions represent specific state mutations (e.g., `/requests/{id}/approve`).
- **Server-Side Authorization:** The backend exclusively trusts its own policy checks. UI-provided roles are ignored.
- **Workflow-Aware Validation:** The API rejects mutations if the request's `StatusId` does not permit the action (enforcing `ACCESS_MODEL.md`).
- **Explicit Auditability:** Mutating workflow actions automatically generate `RequestStatusHistory` entries in the same database transaction.

---

## 3. Base Conventions

- **Base Route:** `/api/v1`
- **JSON Casing:** `camelCase` (standard ASP.NET Core default for JSON serialization).
- **Date/Time:** ISO 8601 UTC strings (e.g., `2026-02-25T12:00:00Z`).
- **Pagination:** Query parameters `?page=1&pageSize=20`. Response wrapped in a standard collection envelope (see below).
- **Filtering/Sorting:** Query parameters `?statusId=2&sortBy=requestedDate&sortDesc=true`.
- **Response Structure:** Direct payload for single items. Enveloped payload for lists to support pagination metadata.

```json
// Example List Envelope
{
  "data": [ { "id": 1, ... } ],
  "meta": {
    "totalCount": 150,
    "page": 1,
    "pageSize": 20,
    "totalPages": 8
  }
}
```

---

## 4. Authentication & Authorization (API Perspective)

- **V1 Auth Assumption:** The API expects an `Authorization: Bearer <token>` header containing standard JWT claims (or relies on ASP.NET Core Windows Authentication middleware if configured for pure Intranet).
- **Role Enforcement:** Endpoints use ASP.NET Core `[Authorize(Roles = "Solicitante, Aprovador")]` attributes for coarse filtering.
- **Status Validation:** Granular resource-based authorization occurs inside the Service/MediatR layer (e.g., returning HTTP 403 Forbidden if a Requester tries to edit a Pending Approval request).
- **Forbidden vs Unauthorized:**
  - `401 Unauthorized`: Missing or invalid token.
  - `403 Forbidden`: Valid user, but lacks role or request state is locked.

---

## 5. Core Resources (V1)

The V1 API provides routes for:

1. **Auth:** `/api/v1/auth/me` (Returns current user roles/claims for frontend bootstrap).
2. **Lookups:** `/api/v1/lookups/*` (Read-only static data).
3. **Requests:** `/api/v1/requests` (Core header and workflow routing).
4. **Line Items:** `/api/v1/requests/{id}/items` (Child entity management).
5. **Attachments:** `/api/v1/requests/{id}/attachments` (File handling).
6. **Audit Trail:** `/api/v1/requests/{id}/history` (Read-only timeline).

---

## 6. Request Endpoints (Detailed)

### `GET /api/v1/requests`

- **Purpose:** List requests. Powers "My Requests" and "Pending Approvals" based on query scopes.
- **Allowed Roles:** All (scoping applied internally based on user's identity).
- **List View Support (Crucial):** Includes `allowedActions: ["edit", "cancel", "submit"]` in each returned object so the React table can render dropdown actions without redefining state logic.

### `GET /api/v1/requests/{id}`

- **Purpose:** Retrieve full detail comprising header, line items, and current status.
- **Payload:** Includes `allowedActions` array for the Detail Page Action Panel.

### `POST /api/v1/requests`

- **Purpose:** Create a new Draft.
- **Payload:** Header data (Type, Title, Context, SupplierId).
- **Validation:** If `requestTypeId` maps to `PAYMENT`, `supplierId` is mandatory.
- **Side Effect:** Automatically sets `Status = Draft` and `RequesterId = CurrentUser`.

### `PUT /api/v1/requests/{id}`

- **Purpose:** Update header fields.
- **Validation:** Fails if `Status != Draft` or `Rejected` (Rework).

### `POST /api/ v1/requests/{id}/submit`

- **Purpose:** Move a Request from `DRAFT` state to the official `SUBMITTED` workflow state.
- **Validation Rules:**
  - Request must have a `Title` and `Description`.
  - Request must have at least one non-deleted `LineItem`.
  - `EstimatedTotalAmount` must be greater than zero.
  - **Conditional Supplier Rule:** Mandatory for `PAYMENT` type, optional for `QUOTATION` type.
  - Current status must be `DRAFT`.
- **Side Effects:**
  - Updates `StatusId` to `SUBMITTED`.
  - Sets `SubmittedAtUtc` to current server time.
  - Inserts a transition record into `RequestStatusHistory`.
  - Locks the request and all child items from further mutation.
- **Response:** `200 OK` with confirmation message and target status code.

### `POST /api/v1/requests/{id}/approve`

- **Purpose:** Approve a pending request.
- **Payload:** `{ "comment": "Optional note" }`
- **Allowed Roles:** Aprovador de Área (assigned to this request).
- **Side Effect:** Status -> `Approved`. Audit trail written.

### `POST /api/v1/requests/{id}/reject`

- **Purpose:** Reject back to Requester/Purchaser for rework.
- **Payload:** `{ "comment": "Reason for rework" }` *(Comment is mandatory)*.
- **Side Effect:** Status -> `Rejected`. Audit trail written.

### `POST /api/v1/requests/{id}/cancel`

- **Purpose:** Terminate the request. Can be performed by Requester (if Draft) or Admin (override).

---

## 7. Request Line Item Endpoints

*Managed strictly under the context of a parent Request.*

- **`GET /api/v1/requests/{id}/line-items`** (Pending future implementation if detached from request details)
- **`POST /api/v1/requests/{id}/line-items`**: Adds a new line item, recalculates request total.
- **`PUT /api/v1/requests/{reqId}/line-items/{itemId}`**: Updates item details, recalculates request total.
- **`DELETE /api/v1/requests/{reqId}/line-items/{itemId}`**: Soft deletes item, recalculates request total.

## Gestão de Cotações (Buyer Workspace)

The `/buyer/items` route provides a dedicated workspace for managing individual line items and processing supplier quotations across all active requests via a grouped data grid.

- **Grouped Architecture**: Unlike the Request List, this grid groups items by their parent Request, providing critical context (Request Number, Status) and centralized actions (Proforma, Add Quotation).
- **Inline Editing (Skybow pattern)**: Users edit fields directly within the grid cell. Changes are auto-saved to the backend (`PATCH /api/v1/line-items/{id}`) and provide immediate visual feedback (green checkmark).
- **Temporary Role Gating (`mode`)**: To facilitate testing before enterprise SSO/RBAC is implemented, the UI features a temporary toggle between "Comprador" and "Aprovador de Área" on the Gestão de Cotações screen.
  - **Buyer Mode**: Can edit `Status` and `Supplier`. `Cost Center` is strictly read-only.
  - **Area Approver Mode**: Can edit `Cost Center`. `Status` and `Supplier` are strictly read-only.

### Action Badge Rules

High-visibility badges are displayed on each Request group:

- `WAITING_QUOTATION`: **AÇÃO NECESSÁRIA: COTAR** (Red)
- `AREA_ADJUSTMENT` / `FINAL_ADJUSTMENT`: **AÇÃO NECESSÁRIA: REAJUSTAR** (Orange)
- `PAYMENT_COMPLETED`: **PAGAMENTO REALIZADO** (Green)
  - **Filtering**: Supports additional filtering (`query`, `itemStatus`, `requestStatus` - intersected with allowed set, `plant`, `department`).
- **`PATCH /api/v1/line-items/{id}`**: Updates specific line item properties (e.g. `lineItemStatusId`, `supplierId`, `costCenterId`).
  - **Role Gating `X-User-Mode`**: When updating `costCenterId`, the endpoint enforces a strict security policy. If the `X-User-Mode` HTTP Header is not set to `AREA_APPROVER`, the API will return a `403 Forbidden` response.

**Validation:**

- None of these mutating endpoints (`POST`, `PUT`, `DELETE`) will succeed unless the parent Request is in an editable state (`Draft` or `Rejected/Rework`).
- `POST` and `PUT` will return `409 Conflict` if the specified `Unit` has `AllowsDecimalQuantity == false` but the `Quantity` value contains a fractional/decimal amount.
- **Currency Normalization**: Line item `CurrencyId` is automatically normalized to the parent Request's `CurrencyId`. Any divergent currency ID provided in the payload is ignored.
- **Header Lock**: `PUT /api/v1/requests/{id}` will reject `CurrencyId` changes if the request already contains line items.

---

## 8. Attachment Endpoints

*Managed physically via file system, logically via database.*

- **`GET /api/v1/requests/{id}/attachments`**: List metadata (Name, Size, Uploader).
- **`POST /api/v1/requests/{id}/attachments`**:
  - **Payload:** `multipart/form-data`.
  - **Validation:** Max size 10MB (`application/pdf`, `image/jpeg`, `image/png`, `application/vnd.openxmlformats-officedocument.*`). Fails if Request is locked.
- **`GET /api/v1/requests/{reqId}/attachments/{attachId}/download`**: Returns file stream based on StorageReference.
- **`DELETE /api/v1/requests/{reqId}/attachments/{attachId}`**: Soft-deletes the attachment record (`IsDeleted = true`). Allowed by Uploader if Request is in Draft state.

---

## 9. Lookup Endpoints (Read-Only)

Returns simple arrays to populate React Select dropdowns. Standard structure is `{ id, code, label/name, isActive }`. The `units` endpoint also returns `allowsDecimalQuantity: boolean`.

- `GET /api/v1/lookups/request-types`
- `GET /api/v1/lookups/statuses`

**Maintained via UI CRUD (Soft Delete supported via `toggle-active`):**

- `GET|POST|PUT /api/v1/lookups/currencies`
- `PUT /api/v1/lookups/currencies/{id}/toggle-active`
- `GET|POST|PUT /api/v1/lookups/units`
- `PUT /api/v1/lookups/units/{id}/toggle-active`
- `GET|POST|PUT /api/v1/lookups/need-levels`
- `PUT /api/v1/lookups/need-levels/{id}/toggle-active`
- `GET|POST|PUT /api/v1/lookups/departments`
- `PUT /api/v1/lookups/departments/{id}/toggle-active`
- `GET|POST|PUT /api/v1/lookups/plants`
- `PUT /api/v1/lookups/plants/{id}/toggle-active`
- `GET|POST|PUT /api/v1/lookups/suppliers`
- `PUT /api/v1/lookups/suppliers/{id}/toggle-active`
- `GET|POST|PUT /api/v1/lookups/cost-centers`
- `GET /api/v1/lookups/cost-centers/search?q={query}`
- `PUT /api/v1/lookups/cost-centers/{id}/toggle-active`

---

## 10. Error Model (V1)

Implementation maps to standard ASP.NET `ProblemDetails` (RFC 7807), ensuring React can predictably parse UI errors.

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Error",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": {
    "EstimatedAmount": [
      "The Estimated Amount must be greater than zero."
    ],
    "StatusId": [
      "Cannot 'Approve' a request that is already 'Approved'."
    ]
  }
}
```

- `400 Bad Request`: Validation failure (missing fields).
- `403 Forbidden`: Valid user, but invalid state or role ownership.
- `404 Not Found`: Record does not exist.
- `409 Conflict`: Stale update (e.g., someone else already approved it).

---

## 11. Audit Trail Expectations

Every mutating workflow action (`submit`, `approve`, `reject`, `cancel`) MUST be wrapped in an Entity Framework Database Transaction that simultaneously updates the Request's `StatusId` and inserts a new immutable record into `RequestStatusHistory`. Failure to insert the history must roll back the status change.

---

## 12. Open Questions & Assumptions

- **Assumption:** Attachment payload uses `multipart/form-data`. An alternative is Base64 JSON, but `multipart` is heavily preferred in ASP.NET Core for memory efficiency on large files.
- **Assumption:** Line Items are saved piecemeal via individual API calls (or batch `PUT`), rather than demanding the entire Request aggregate be saved in one massive JSON blob.
- **OQ-300:** Do we need a hard `PATCH` endpoint for minor updates, or is a full `PUT` on the header sufficient for V1 forms?

---

## 13. Implementation Mapping (Next Step)

To translate this into C# code:

1. `AlplaPortal.Api/Controllers`: Map the routes listed above using Attribute Routing `[HttpPost("{id}/submit")]`.
2. `AlplaPortal.Core/DTOs`: Create Request/Response record types matching the expected JSON.
3. `AlplaPortal.Core/Services`: Implement the business logic (Status transitions) completely decoupled from HTTP concerns.
4. `AlplaPortal.Core/Authorization`: Implement ASP.NET Core generic Handlers to evaluate record ownership cleanly.

---

## 14. Suggested Build Order (Backend API)

1. **Phase 1: Foundation.** Lookups API + Initial DB Seed.
2. **Phase 2: Core Records.** `GET/POST/PUT` for Requests and Line Items (Drafts only, ignoring workflow).
3. **Phase 3: The State Machine.** The workflow action endpoints (`submit`, `approve`, `reject`) + Audit Trail writes.
4. **Phase 4: Attachments.** `multipart` handling and file IO.
5. **Phase 5: Security.** Hooking up ASP.NET Policies to enforce the `ACCESS_MODEL.md` rules globally.
