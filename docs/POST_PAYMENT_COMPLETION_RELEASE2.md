# Post-Payment Completion — Release 2 (Document Classification) — Closing Record

> Release 1 is recorded separately in
> [POST_PAYMENT_COMPLETION_RELEASE1.md](./POST_PAYMENT_COMPLETION_RELEASE1.md).
> The design this release implements is
> [POST_PAYMENT_COMPLETION_PLAN_V7.md](./POST_PAYMENT_COMPLETION_PLAN_V7.md).

**Status: implemented, locally validated, not deployed.**
`PostPaymentCompletion.Enabled` is `false` in every committed configuration.

---

## 1. What Release 2 delivers

The identity of the document that originates a payable request, separated from the obligations it
creates.

- **Seven-value taxonomy** under Angola's *Regime Jurídico das Facturas* (Decreto Presidencial
  71/25): `ESTIMATE`, `PROFORMA`, `ADVANCE_INVOICE`, `INVOICE`, `INVOICE_RECEIPT`, `OTHER`,
  `UNCLASSIFIED`.
- **`DocumentObligationResolver`** — the single pure function translating identity into what remains
  owed, per usage context. No one-field shortcut survives.
- **OCR classification** with confidence and evidence, plus a labelled fallback when the provider
  returns no structured block.
- **Contradiction is a decision, not a warning**: a selection that disagrees with the reading is not
  applied until the user confirms it, and a high-risk contradiction requires a written reason.
- **Every contradiction is audited** in `DocumentClassificationOverrides` and in the request
  timeline, keyed so a repeated save writes nothing.

Nothing in Release 2 uploads, validates or reconciles a post-payment document. That is Release 3.

---

## 2. Local manual validation — PASSED

Performed by the product owner on the local environment
(`Portal-Gerencial-Dev-ProdClone` on `(localdb)\MSSQLLocalDB`), against commit `e36e785`
(v2.223.0), with the feature enabled only through the gitignored
`appsettings.Development.json`.

| # | Scenario | Result |
|---|---|---|
| 1 | Clean field layout — no inline block, no height change on selection | **PASS** |
| 2 | OCR suggestion icon and modal | **PASS** |
| 3 | Conflict modal opens automatically on a contradicting selection | **PASS** |
| 4 | Checkbox and 20-character justification enforced | **PASS** |
| 5 | Cancel (button, close, Escape) restores the previous value | **PASS** |
| 6 | Confirmed decision persists across save and reopen | **PASS** |
| 7 | Same behaviour in Quotation Management (create and reopen) | **PASS** |
| 8 | History written once — a repeated save does not duplicate it | **PASS** |
| 9 | Other contextual messages converted to icons (expired document, auto-filled company) | **PASS** |
| 10 | Keyboard navigation, dark mode, 1600×900 | **PASS** |
| 11 | General regression — Pedidos, Aprovações, Cotações, Recebimento, Finanças | **PASS** |

**Release 2 is approved locally.** No further Release 2 work is authorized unless a new defect is
reported.

### Automated validation at the same commit

- Backend build: 0 errors.
- Backend suite: **668 passed / 1 pre-existing baseline failure / 0 new failures.** The failure is
  `GroupBuilderServiceTests.BuildGroupsForRequestAsync_CreatesGroups_WhenLineItemsHaveSelectedQuotation`,
  which predates this work: the test asserts a total taken from `RequestLineItem.TotalAmount` while
  the service sums `QuotationItem.LineTotal`. Out of scope; recorded, not fixed.
- Frontend `tsc --noEmit`: clean. Vite build: clean.
- **No frontend test framework exists in this repository.** The UI behaviours above are covered by
  the manual validation and by unit tests of the extracted decision rules, not by automated UI
  tests. Adding a frontend test runner remains an open follow-up.

---

## 3. Local commit chain

| Commit | Version | Contents |
|---|---|---|
| `2586a97` | v2.219.0 | **Release 1** — domain foundation: dimensions, two-phase completion skeleton, idempotency keys, policy gate |
| `f026231` | — | Release 1 fix — effective-date configuration binding (a UTC+1 overflow 500-ing every `RequestsController` endpoint) |
| `f3e3475` | v2.220.0 | **Release 2, first attempt** — binary `PROFORMA \| FINAL_INVOICE` classification |
| `13fe038` | v2.221.0 | **Release 2 corrected** — seven-value Angolan taxonomy, identity split from obligations, OCR classification, conflict handling |
| `b24d25c` | v2.222.0 | Release 2 corrective — OCR classification surfaced (the draft mapper never carried it), fallback classifier, explanation moved to a modal |
| `e36e785` | v2.223.0 | Release 2 corrective — contextual messages become icons + modals; conflicts become a pending decision; override audit |

Base: `2e29f4c` on `Portal-Gerencial-rev1`. **Six commits ahead of `origin`; nothing pushed, no PR,
no merge, no TEST deployment.**

---

## 4. Schema added by Release 2

| Migration | Change |
|---|---|
| `20260731111543_CorrectDocumentTaxonomy` | Renames `BillingDocumentType` → `SourceDocumentType`, `FinalInvoiceStatus` → `OperationInvoiceStatus`; adds classification-evidence columns to `Requests` and `Quotations`. No data change — zero rows were classified when it ran. |
| `20260803130410_DocumentClassificationOverrideAudit` | Adds `DocumentClassificationOverrides` (one new table, unique `IdempotencyKey`, all relationships `NoAction`). Purely additive. |

Both applied to the local development clone through `execution/update_dev_database.ps1`. Neither has
been applied to TEST or PROD.

---

## 5. Rollback

Because the feature is disabled in every committed configuration, rolling back the **behaviour**
requires nothing: it is already inert. Rolling back the **code** is `git revert` of the commits in
§3, newest first.

The schema must not be rolled back casually. `CorrectDocumentTaxonomy` renames columns other code
now reads; `DocumentClassificationOverrideAudit` adds a table that may hold audit rows. If either
table already holds data, restore from backup rather than running the down script.

---

## 6. Carried into Release 3

1. **`INVOICE_RECEIPT` in the post-payment evidence context** discharges the operation-invoice and
   payment-evidence obligations together. Release 2 defines this in the resolver; nothing consumes
   it yet.
2. **`ADVANCE_INVOICE` remains a provisional rule** pending formal Finance confirmation — see the
   open questions in Plan v7 §9.
3. **`OTHER` routes to Finance review.** Release 2 records the requirement; the Finance queue that
   acts on it is Release 3.
4. **The Release 1 follow-ups still stand** — transaction-safe duplicate handling (now demonstrated
   in `SaveChangesWithClassificationAuditRetryAsync` and to be reused), the
   `WAITING_FISCAL_RECEIPT` priority collision, the missing `Request.CompletedAtUtc`, and the
   `ParentCompletionSweep`. See Release 1 §6.
5. **No frontend test framework.** Every UI guarantee in this release rests on manual validation.
