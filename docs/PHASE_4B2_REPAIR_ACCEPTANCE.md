# Phase 4B.2 — Human DEV Acceptance: PAYMENT PO-group repair

Exercises the SysAdmin repair (dry-run C, execute D, idempotency E, unsafe F) against **synthetic
DEV-only** fixtures. Nothing here touches PROD or any historical request.

## 0. Enable the DEV harness (once)

The fixture endpoints are triple-gated (DEBUG build + Development env + explicit opt-in). In your
local, gitignored `appsettings.Development.json` add:

```json
"DevFixtures": { "PaymentPoRepairEnabled": true }
```

Run the API in **Development / Debug**. Base URL below is your DEV API host, e.g.
`https://localhost:5001` → shown as `{API}`.

## 1. Seed the fixtures

```
POST {API}/api/v1/dev/payment-po-repair-fixtures/seed
```

Response (note the two returned `id`s — the request **numbers** are fixed):

```json
{
  "message": "Fixtures ZZTEST-PAY-REPAIR criadas.",
  "safe":            { "id": "<SAFE_ID>",   "requestNumber": "ZZTEST-PAY-REPAIR-SAFE",   "expectedClassification": "SAFE_TO_REPAIR" },
  "unsafeCandidate": { "id": "<UNSAFE_ID>", "requestNumber": "ZZTEST-PAY-REPAIR-UNSAFE", "expectedClassification": "MANUAL_REVIEW" }
}
```

- **SAFE** = PAYMENT, APPROVED, one PaymentSourceDocument + one linked line item, **zero** PO groups, no downstream evidence.
- **UNSAFE** = PAYMENT, APPROVED, **zero** PO groups, but carries a P.O. attachment (downstream evidence).

Get a SysAdmin token to call the repair endpoints (or just log in to the DEV portal as a SysAdmin and use the browser session):

```
GET {API}/api/v1/dev/payment-po-repair-fixtures/token   →   { "token": "<JWT>", ... }
```

All repair calls below need header: `Authorization: Bearer <JWT>`.

## 2. (C) Dry-run — SAFE

```
GET {API}/api/v1/requests/admin/payment-po-repair/candidates?requestIds=<SAFE_ID>&requestIds=<UNSAFE_ID>
```

Expected for the SAFE row: `classification = "SAFE_TO_REPAIR"`, `model = "MultiDocument"`,
`existingGroupCount = 0`, `expectedGroupCount = 1`, `hasPoEvidence = false`,
`hasDownstreamEvidence = false`. **Zero writes** — re-run `.../state` or the dry-run and the group
count stays 0.

## 3. (D) Execute — SAFE only

```
POST {API}/api/v1/requests/admin/payment-po-repair/execute
Content-Type: application/json

{ "requestIds": ["<SAFE_ID>"] }
```

Expected: `[{ "outcome": "REPAIRED", "groupsCreated": 1, "scalarStatusCode": "APPROVED", ... }]`.

Then open the SAFE request in the **normal** portal UI and verify:
- Scalar/audit history still records **APPROVED** (unchanged).
- Status badge (list row **and** detail header) now reads **"Aguardando P.O."**.
- As a Buyer, the **Registrar P.O** action appears.
- A `WAITING_PO` PO group now exists; the line item is linked to it.

(Quick check without the UI: `GET {API}/api/v1/dev/payment-po-repair-fixtures/state` → SAFE row shows `poGroupCount = 1`, `status = "APPROVED"`.)

## 4. (E) Idempotency — execute SAFE again

```
POST {API}/api/v1/requests/admin/payment-po-repair/execute
{ "requestIds": ["<SAFE_ID>"] }
```

Expected: `[{ "outcome": "SKIPPED", ... }]` (already has groups). Verify **no** duplicate group
(`poGroupCount` still 1), no new item→group links, status still APPROVED, no destructive writes.

## 5. (F) Unsafe — dry-run and (attempted) execute

```
GET  {API}/api/v1/requests/admin/payment-po-repair/candidates?requestIds=<UNSAFE_ID>
POST {API}/api/v1/requests/admin/payment-po-repair/execute   { "requestIds": ["<UNSAFE_ID>"] }
```

Expected: dry-run `classification = "MANUAL_REVIEW"` (`hasDownstreamEvidence = true`); execute
`outcome = "MANUAL_REVIEW"` and **no** group is written (`poGroupCount` stays 0).

## 6. Reset (delete the DEV fixtures)

```
POST {API}/api/v1/dev/payment-po-repair-fixtures/reset
```

Removes every `ZZTEST-PAY-REPAIR*` request and its children (line items, source documents, groups,
attachments, history). Re-run `seed` to start over.

---
*Not product functionality. The fixture controller is `#if DEBUG` (a stub in Release) and returns 404
unless the Development env + `DevFixtures:PaymentPoRepairEnabled` opt-in are both set.*
