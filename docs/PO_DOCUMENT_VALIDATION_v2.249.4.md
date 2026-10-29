# P.O. document validation, correction number, superseded files, cancelled-group obligations, Print View dates — v2.249.4

> Status: **implemented in the working tree, unreleased (2026-10-29)**. No commit, push, version bump, deploy, TEST/PROD access
> or data change was performed. This document is the implementation plan (SOP step 2) and the module reference for the nine
> findings of the manual TEST validation of v2.249.3 on `REQ-28/10/2026-628` (id `a0eba2c8-1155-425a-b5a6-d86f0f8fee04`), revised
> after the owner's review (server-held extraction evidence, date-only vs instant handling, legacy P.O. files, explicit number
> decision, identifiers, verification evidence). Companion documents: `docs/PO_GROUP_CANCELLATION.md` (phase 1 of the
> cancellation), the manual script `docs/PO_GROUP_CANCELLATION_TEST_SCRIPT_v2.249.0.md` (§3.9 = resume steps), DEC-152 in
> `docs/DECISIONS.md`.

## 1. Finding-by-finding matrix

| # | Finding (TEST, v2.249.3) | Classification | Root cause (confirmed in code) | Fix in this version |
|---|---|---|---|---|
| 1 | The correction wizard cannot correct the P.O. number; the quotation number survived the correction | **Confirmed defect** | `CorrectPoModal` has no number field and `lib/correctPoSubmission.buildRegisterPoPayload` never sent `purchaseOrderNumber`; `RegisterPo` only overwrites the number when one is sent, so the returned P.O.'s number was silently retained | Number block in the correction: current number and the number detected in the replacement document shown **separately**; when they differ the Buyer must **choose** ("Usar número detetado" / type another / "Manter número atual") before submitting — nothing is retained silently; `register-po` refuses a correction without a number (`400 PO_NUMBER_REQUIRED`) and treats a kept number that differs from what the server's own evidence read as an OCR divergence (confirmation + justification, audited); a changed number is audited (`PO_NUMBER_CORRECTED`: old → new, source, current and superseded files) and read everywhere from `RequestPoGroup.PurchaseOrderNumber` |
| 1b | "Corrigir P.O." rendered BOTH modals | **Confirmed defect** (found on the trace) | `RequestEdit.tsx` mounted `RegisterPoModal` on `poGroupIdForUpload` alone | Guard `!showCorrectPoModal` |
| 1c | The correction modal's "P.O Atual" card never found the current file | **Confirmed defect** (found on the trace) | It filtered on `requestPoGroupId`, which `RequestAttachmentDto` never carried | DTO carries `requestPoGroupId`, `supersededAtUtc`, `supersededByAttachmentId`, `isCurrentPo`; the modal uses the group's `currentPoAttachmentId` |
| 2 | Document identification in both P.O. wizards | **Confirmed gap** | Two components (`RegisterPoModal`, `CorrectPoModal`) on ONE endpoint (`register-po`) and ONE stateless, anonymous extraction (`direct-ocr`). The only identity signal was the ECF regex (`PrimaveraPoReference`, already longest-family-first with word boundaries — ECF99 was never read as ECF), used as a warning in the registration only | Shared pure rule `PurchaseOrderDocumentIdentifier`; a new request-scoped extraction endpoint that **persists** the server's reading (`PoDocumentExtraction`, bound to request + file hash); both wizards render one verdict block and one number block |
| 3 | A quotation was accepted as a P.O. | **Confirmed defect** (type verification was ABSENT, not bypassed: no override for the document type ever existed; the green "Documento Validado" compared total and supplier only) | `buildOcrMismatchResult` checks amount + supplier; `RegisterPo` had no document-type rule | Server-enforced on **server-held evidence** (§3): positively identified quotation / proforma / invoice → `400 DOCUMENT_NOT_PURCHASE_ORDER` (no override); UNCERTAIN or no evidence → `400 DOCUMENT_TYPE_CONFIRMATION_REQUIRED` unless `documentTypeConfirmed` + Comment (existing override policy, audited); extraction-independent guard: a P.O. file byte-identical to a QUOTATION/PROFORMA document of the request → `400 DOCUMENT_IS_QUOTATION_EVIDENCE` at upload and registration |
| 4 | Superseded P.O. documents ambiguous | **Confirmed defect** | No current pointer, no supersession state; consumers tested existence or took the newest by date | Additive migration `20261029140552_AddPoDocumentSupersessionAndExtraction`; `RegisterPo` stamps the current file and supersedes the earlier live files of the group; one reader rule `PoDocumentSelection` (group-bound); "Substituído" ≠ "Sem efeito" ≠ "P.O. atual"; legacy behaviour in §4 |
| 5 | Cancelled group still an active final-invoice obligation | **Confirmed defect** | `GET …/operation-invoice-obligations` never read the group status; the projector had no group-status input; short-close / invoice write endpoints had no CANCELLED guard | Projector reports a CANCELLED group as history (§6); write guards; UI history block; REQ-628 active total = 350,000 |
| 6 | Print View dates one day earlier | **Hypothesis resolved → confirmed defect** | `lib/utils.formatDate/formatDateTime/formatTime/getUrgencyStyle` parsed offsetless backend strings as browser-LOCAL time and then used UTC getters | Date-only business fields in the Print View use a pure **calendar** formatter (`formatCalendarDate`, string slicing, no timezone); the shared instant formatters read an offsetless string as UTC (`parseBackendInstant`) — traced per shape in §5 |
| 7 | Item counts | **Confirmed labelling defect** (not duplicated purchases) | Counts included NOT_QUOTED / IGNORED placeholders; a RESTART cancellation removes the live line links | `QuotedItemCount` / `NotQuotedItemCount`; `HistoricalLineItemCount` / `HistoricalLineNumbers` from the cancellation items |
| 8 | Pending confirmation showed the previous rejection ambiguously | **Confirmed defect** | `UpdateEcfDeclarationAsync` keeps the review columns (evidence) while the status returns to PENDING; the card rendered them unconditionally | Read-side chronology (`ResolveReviewChronology`): `financeReviewAppliesToCurrentDeclaration` + `previousFinanceReview` on the detail and the Finance queue; UI labels the previous review |
| 9a | `NotifyOnPoCancelled` not configurable | **Confirmed gap → fixed** | Column + orchestrator read existed; API GET/POST/PUT, form model and settings panel ignored it | Exposed on the supported path; default false; tests |
| 9b | Cancellation dialog has no evidence upload | **Optional enhancement** (owner: does not block) | D1–D16 never require evidence; docs call it optional | Not implemented |
| 9c | Requester e-mail on cancellation | **Intentional (D16)** | Code = decision; doc nuance: the requester is also notified on declaration updates and resends | Unchanged |
| 9d | Legacy "Cotar itens"; dashboard one entry per action | **Existing behaviour, out of scope** | Documented in `BuyerOpenQuotationWorkAdmissionTests` and CHANGELOG v2.249.2 | Unchanged |

## 2. Root causes and files changed

### Backend
- `Domain/Services/PurchaseOrderDocumentIdentifier.cs` (NEW) — the shared verdict rule (§3).
- `Domain/Entities/PoDocumentExtraction.cs` (NEW) + `Infrastructure/Data/Configurations/PoDocumentExtractionConfiguration.cs` (NEW) + `DbSet PoDocumentExtractions` — the server-held evidence (§3).
- `Domain/Services/PoDocumentSelection.cs` (NEW) — the group-bound current-P.O. reader rule (§4).
- `Domain/Services/OperationInvoiceObligationProjector.cs`, `Domain/Constants/RequestConstants.cs` (`OperationInvoiceStatuses.Cancelled`, derived-only) — §6.
- `Domain/Entities/RequestAttachment.cs`, `RequestPoGroup.cs` — supersession fields / pointer; migration
  `Infrastructure/Data/Migrations/20261029140552_AddPoDocumentSupersessionAndExtraction` (§7).
- `Application/DTOs/Extraction/ExtractionResultDto.cs` (+`DocumentTitle`, `ReferencedDocumentNumbers`),
  `Infrastructure/Services/Extraction/OpenAiDocumentExtractionProvider.cs` (prompt + parse), `Api/Helpers/ExtractionMapper.cs`
  (verdict in `headerSuggestions.purchaseOrderIdentification`), `Application/DTOs/Requests/OcrExtractionResultDto.cs`.
- `Application/DTOs/Requests/RegisterPoActionDto.cs` — `PoAttachmentId`, `DocumentTypeConfirmed`, `PurchaseOrderNumberSource`;
  **no extraction fields** (the client has no channel for them).
- `Api/Controllers/RequestsController.cs` — `POST {id}/po-document-extract` (NEW); `RegisterPo`: file resolution, evidence lookup by
  hash, identification + number rules, `PO_NUMBER_REQUIRED`, `PO_NUMBER_CORRECTED` audit, supersession stamping; details DTO
  enrichment; obligations endpoint; finalize "unclassified" excludes cancelled.
- `Api/Controllers/AttachmentsController.cs` (quotation-evidence hash guard on P.O. upload), `OperationInvoiceShortClosesController.cs`,
  `OperationInvoicesController.cs`, `BuyerWorkspaceController.cs`, `AccountsPayableConfigController.cs`;
  `Infrastructure/Services/Purchasing/PoGroupCancellationService.cs` (`ResolveReviewChronology`); DTOs (`BuyerWorkspaceDtos`,
  `PoGroupCancellationDtos`, `OperationInvoiceObligationsDto`, `RequestDetailsDto`, `RequestPoGroupDto`, `QuotationDto`).

### Frontend
- `lib/utils.ts` — `formatCalendarDate` (date-only, string slicing) and `parseBackendInstant` (instants: offsetless = UTC).
- `lib/poDocumentIdentification.ts` (NEW: verdict reader, raw detected number, number field state machine with the explicit
  decision), `components/po/PoDocumentReviewBlocks.tsx` (NEW), `lib/registerPoErrors.ts`, `lib/correctPoSubmission.ts`,
  `components/CorrectPoModal.tsx`, `components/RegisterPoModal.tsx` (both call `api.requests.poDocumentExtract(requestId, file)`),
  `lib/api.ts`, `lib/logger.ts` (component key), `pages/Requests/RequestEdit.tsx`.
- `components/RequestAttachments.tsx`, `pages/Requests/components/print/requestPrintModel.ts`, `lib/operationInvoiceView.ts`,
  `pages/Requests/components/OperationInvoiceSection.tsx`, `pages/Buyer/BuyerRequestWorkspace.tsx`, `pages/Buyer/buyerWorkspaceView.ts`,
  `components/Finance/PoCancellationReviewPanel.tsx`, `lib/poGroupCancellation.ts`, `pages/Requests/components/RequestQuotations.tsx`,
  `pages/Buyer/BuyerItemsList.tsx`, `pages/Settings/apNotificationsForm.ts`, `pages/Settings/ApNotificationsPanel.tsx`, types.

## 3. Extraction, trusted evidence and fallback behaviour (both wizards)

**Where the evidence is born.** On file selection both wizards call `POST /api/v1/requests/{id}/po-document-extract`
(request scope first — out of scope answers 404 — then Buyer / SysAdmin). The server reads the bytes, computes their SHA-256
itself, runs the extraction provider, and persists `PoDocumentExtraction` (request, hash, file name, actor, time, success flag,
provider status, the identifier's verdict, and the facts: document number, heading, classification + confidence, body
references, supplier, tax id, currency, grand total). The response is the same envelope as `direct-ocr`, plus
`headerSuggestions.purchaseOrderIdentification`. A provider exception persists nothing (500 `PORTAL_ERROR`); an envelope with
`success=false` is persisted as a failed reading. `direct-ocr` itself is untouched (other callers).

**How registration uses it.** `register-po` resolves the P.O. file (`poAttachmentId` from the upload response, validated as a live
P.O. file of the group; else the newest live file of the group), then loads the latest `PoDocumentExtraction` of **this request**
whose hash equals the attachment's **server-computed** `FileHash`. The identification is derived from that row alone through
`PurchaseOrderDocumentIdentifier`; the DTO has **no extraction fields** (removed), so nothing a caller submits can supply or alter
the identity. The business comparison (supplier / amount / currency) also prefers the evidence; the client-submitted values remain
only as the legacy fallback when no evidence exists.

| Situation | Evidence found? | Result |
|---|---|---|
| File extracted through the endpoint, then uploaded unchanged | yes (same bytes → same hash) | verdict of the server's own reading |
| Client "tampers" with or omits facts | n/a | nothing to tamper with: the DTO carries no facts |
| Another attachment's extraction (file A extracted, file B registered) | no (B's hash has no row) | UNCERTAIN, `evidence=NONE` |
| File changed after validation (same as above by construction) | no | UNCERTAIN |
| Same bytes extracted under another request | no (request-bound) | UNCERTAIN |
| Provider failed / empty reading | row with `Success=false` | UNCERTAIN, `evidence=FAILED` |
| Legacy client that never called the endpoint | no | UNCERTAIN, `evidence=NONE` |

UNCERTAIN always means: explicit `documentTypeConfirmed` + a mandatory Comment (the existing override policy), refused otherwise
(`400 DOCUMENT_TYPE_CONFIRMATION_REQUIRED` with `reasonCode` and `evidence`), and audited in the registration history
("Tipo de documento NÃO identificado automaticamente (REASON; extração xxxxxxxx de dd/MM HH:mm UTC | sem extração registada para
este ficheiro); confirmado manualmente pelo comprador. Justificativa: …"). IDENTIFIED_OTHER_TYPE (`400 DOCUMENT_NOT_PURCHASE_ORDER`)
has no override. The byte-identical-quotation guard (`DOCUMENT_IS_QUOTATION_EVIDENCE`) at upload and at registration does not depend
on any extraction. Tests: `RegisterPoDocumentIdentificationTests` (trust model section) and the endpoint tests in the same class.

**Identity rule (unchanged from the first delivery).** The document's identity is its own number and its heading. Only `ECF11 AAAA/NNN`
(Serviços), `ECF10 AAAA/NNN` (Materiais diversos) and `ECF AAAA/NNN` (Material de stock) are recognised, longest family first, with
word boundaries (`ECF99`, `ECF1`, `ECF12`, `ECF112026/421` are never generic `ECF`). Body references never identify. Verdicts:
IDENTIFIED_PO (one identity reference, no confident contrary classification); UNCERTAIN (several identities, identity contradicted by
a ≥ 0.70 quotation/invoice classification, references only in the body, unrecognised prefix, family word without a complete
reference, no identifier, no extraction); IDENTIFIED_OTHER_TYPE (no identity reference and a ≥ 0.50 quotation / proforma / invoice
classification or an unambiguous title).

**The number.** The server compares the submitted number with the document: an IDENTIFIED reference by canonical identity; for an
UNCERTAIN document, the raw number the evidence read (letter-bearing, non-fiscal, loose normalization) — a difference is an OCR
divergence (`400 OCR_MISMATCH` until `overrideConfirmed` + Comment). In the correction UI the current number and the detected number
are shown separately; when they differ the submission is blocked until the Buyer presses "Usar número detetado", types another
number, or presses "Manter número atual" (`numberDecisionPending`, `keepCurrentPoNumber`). Typing the current number back is not a
decision; a new file reopens it. The detected number is shown for review even when the series is not recognised ("Número lido no
documento: ECF99 2026/9003 — série não reconhecida …"). Exact REQ-628 case (current `TEST-MAQSIS-2026-9003`, replacement reads
`ECF99 2026/9003`): keeping the current number → server divergence requiring the explicit override; adopting the detected number →
registered with the `PO_NUMBER_CORRECTED` audit; the document remains UNCERTAIN (explicit confirmation + justification). Tests:
`REQ_628_*` in `RegisterPoDocumentIdentificationTests`; `poDocumentIdentification.test.ts` ("correction number decision").

**Synthetic documents (TEST).** `ECF99` stays unrecognised on purpose (uncertain, confirmation + justification; the existing ECF99
history of REQ-628 is preserved). For future synthetic documents use the series that matches the **business meaning**: a material
such as the stretch film is **ECF10** (Materiais diversos, e.g. heading "Encomenda Mat Escritório/Diversos ECF10 2026/9004") or
**ECF** (Material de stock, e.g. "Encomenda a Fornecedor ECF 2026/9005"); **ECF11** only for a service. Keep quotation PDFs titled
"Cotação" / "Proposta" so the classifier identifies them as such.

## 4. Superseded vs current P.O. documents — compatibility with existing data

- **Authoritative file.** `RequestPoGroup.CurrentPoAttachmentId`, stamped by `RegisterPo` on every registration and correction with
  the file named by `poAttachmentId` (or the newest live file of the group); every other live P.O. file **of that group** is stamped
  `SupersededAtUtc / SupersededByAttachmentId / SupersededByUserId` in the same transaction. Earlier stamps are never rewritten
  (repeated corrections chain: F1 → superseded by F2, F2 → superseded by F3, pointer F3).
- **Readers** share `PoDocumentSelection.ResolveCurrentPoAttachmentId(groupId, pointer, attachments)`: candidates of other groups are
  filtered out inside the rule (no cross-group selection, whatever the caller passes); deleted and voided files are never current;
  the pointer wins when it still names a live file of the group; otherwise (groups registered before this version, dangling pointer)
  the newest live **non-superseded** file; otherwise the newest live file; otherwise null. Request details (`currentPoAttachmentId`,
  `isCurrentPo`), the correction modal and the Print View read that result.
- **Legacy group with several live files and no pointer** (REQ-628 after deployment): the newest live file is current
  (`PO_Ficticia_MAQSIS_9003_REQ628.pdf`), the older one is a live file without a stamp (no badge) until the next registration
  supersedes it. The first correction of such a group supersedes **every** earlier live file and sets the pointer.
- **Cancelled group.** The cancellation voids the group's P.O. files (existing behaviour), so the reader returns null even when the
  pointer is set; the obligations read model (§6) is status-driven and never consults documents — a cancelled group cannot regain an
  active obligation through any document fallback.
- **Evidence:** `RegisterPoDocumentIdentificationTests` (first correction of a legacy group with two live files, repeated corrections,
  cancellation after correction) and `PoDocumentSupersessionMigrationRehearsalTests` (§7: the real migration applied to a database at
  the previous migration with pre-migration rows).

## 5. Dates — date-only values vs instants

**Raw API values (representative).** Every `datetime2` column is serialized by System.Text.Json without an offset
(`DateTimeKind.Unspecified`): `needByDateUtc: "2026-11-04T00:00:00"`, `documentDate: "2026-10-29T00:00:00"`,
`uploadedAtUtc: "2026-10-29T14:30:00"`. Only the four properties decorated with `UtcDateTimeJsonConverter`
(`RequestStatusHistoryDto.CreatedAtUtc`, field-change `CreatedAtUtc`, `RequestDetailsDto.CreatedAtUtc`, `AdjustmentRequestedAtUtc`)
carry `+00:00`. Date inputs send `YYYY-MM-DD` converted to `…T00:00:00.000Z` (`useRequestDetail`, `quotationSaveLogic`), so storage is
UTC midnight — correct; the defect was presentation only.

**Two formatters, two meanings.**

| Shape | `formatCalendarDate` (date-only) | `formatDate` | `formatDateTime` | `formatTime` |
|---|---|---|---|---|
| `2026-11-04` | 04/11/2026 | 04/11/2026 | 04/11/2026 00:00 | 00:00 |
| `2026-11-04T00:00:00` (date-only field) | 04/11/2026 | 04/11/2026 (was 03/11 on UTC+1) | 04/11/2026 00:00 | 00:00 |
| `2026-10-29T14:30:00` (offsetless instant) | 29/10/2026 | 29/10/2026 | 29/10/2026 14:30 (was 13:30 on UTC+1) | 14:30 |
| `2026-10-29T23:30:00Z` | 29/10/2026 | 29/10/2026 | 29/10/2026 23:30 (unchanged) | 23:30 |
| `2026-10-30T00:30:00+01:00` | 30/10/2026 (as written) | 29/10/2026 (UTC) | 29/10/2026 23:30 | 23:30 |

- Date-only business fields in the Print View (`documentDate`, `needByDateUtc`, `scheduledDateUtc`, `paidDateUtc`) now use
  `formatCalendarDate`: string slicing, no `Date` object, no timezone — the day printed is the day stored, on every browser. This is the
  convention already recorded in DECISIONS point 9 and used by `operationInvoiceView.formatDateOnly` / `financePaymentsView.formatBusinessDateOnly`.
- The three shared instant formatters keep the repository convention "display in UTC" (`FRONTEND_FOUNDATION.md`, UTC getters) and now
  read an offsetless string as UTC (`parseBackendInstant`), which is what the backend stores. Impact on existing callers (62 call
  sites): values carrying `Z` are unchanged; offsetless **instants** (attachment `uploadedAtUtc`, cancellation / review timestamps,
  queue dates) now display the stored UTC time instead of UTC minus the browser offset — on an Angola browser they move forward by one
  hour, which corrects them; offsetless **date-only** fields that still go through `formatDate` on other screens (`RequestQuotations`,
  `ApprovalDetailPanel`, `BuyerItemsList`, dashboard attention list) now show the stored calendar day. Angola-local display is a
  separate family (`formatDateAngola` …) and is not affected. No data was corrected.
- Tests: `lib/utils.dates.test.ts` (matrix above under `TZ=Africa/Luanda`, boundaries, the old behaviour reproduced and refuted),
  `requestPrintModel.test.ts` (REQ-628 shapes).

## 6. Cancelled groups and final-invoice obligations

- **Read model:** `OperationInvoiceObligationProjector` receives the group status; CANCELLED → `RequiresOperationInvoice=false`,
  `DerivedStatus=CANCELLED` (derived-only constant, neither blocking nor satisfied nor accepting uploads), expected / remaining null,
  `HistoricalExpectedAmount`, `HistoricalLineItemCount`, no drift, excluded from every rollup figure (`CancelledGroupCount` reported).
  REQ-628: active 350,000 = MULTIBIZ 120,000 + MAQSIS 230,000; TREVOTECH 250,000 appears only as history.
- **Writes refused:** short-close propose / approve / reject (`409 OI_GROUP_CANCELLED`, a pre-existing proposal is left untouched as
  evidence), invoice create admissibility, supplier-belongs, allocation PUT and allocation integrity, finalize "unclassified".
- **Deadline alerts, pending totals, completion blockers:** the dashboard / alert projections (`CanonicalAlertProjection`,
  `DashboardV2QueryService`) do not read invoice obligations at all; the Finance obligations list excludes cancelled groups
  (`FinanceObligationSummaryProjection` line 92, `activeGroups`); the completion engine skips cancelled groups
  (`RequestCompletionService` lines 131 and 352) and the completion readiness endpoint filters them (`RequestsController`
  `GetCompletionReadiness`, `relevant` groups) — so a cancelled group never blocks request completion nor contributes to pending
  totals; the only active-obligation surface that leaked was the obligations endpoint fixed here. The group's cached
  `OperationInvoiceStatus` / `ExpectedOperationInvoiceTotal` are **not** rewritten (financial history preserved).
- Tests: `CancelledGroupObligationTests`, `OperationInvoiceObligationProjectorTests`.

## 7. Migration

`20261029140552_AddPoDocumentSupersessionAndExtraction` (replaces the first-delivery `20261029124306`, removed before any database saw it):

- `RequestPoGroups.CurrentPoAttachmentId` uniqueidentifier NULL; `RequestAttachments.SupersededAtUtc` datetime2 NULL,
  `SupersededByAttachmentId`, `SupersededByUserId` uniqueidentifier NULL;
- table `PoDocumentExtractions` (FK to `Requests`, cascade) + index `IX_PoDocumentExtractions_Request_Hash_Created`.

Exactly 6 operations (4 `AddColumn` all nullable, 1 `CreateTable`, 1 `CreateIndex`), no data statements, no backfill — asserted by
`PoDocumentSupersessionMigrationRehearsalTests.The_migration_contains_only_the_intended_additive_operations`. Existing migration
files are untouched (only `ApplicationDbContextModelSnapshot.cs` changed). The rehearsal test builds a disposable LocalDB database in
the exact pre-migration state (schema at `20261028082044` with `__EFMigrationsHistory` recording every migration up to it), inserts
pre-migration rows (a group with two live P.O. files and no pointer, a voided file, another group's file, a cancelled group), applies
the **idempotent SQL that `dotnet ef` generates for previous → target** (the same artefact the Apply-Migrations workflows run; no
`DROP` in the forward script), applies it a second time (idempotent, no change), and verifies rows intact with NULL new columns, the
history row, and the reader rule over the migrated model. Finding recorded on the way: the migration chain is **not replayable from
an empty database** (`20260603152331_AddItemCatalogSourceCompanyFix` re-adds the `SourceCompany` and `LastSyncedAtUtc` columns that
`20260603151258_AddItemCatalogSourceCompany` already added; `Migrate()` from zero fails there with SQL error 2705) — a pre-existing
condition, irrelevant to the environments (never built from zero), recorded in `docs/BACKLOG-MIGRATION-CHAIN-REPLAY.md` and left
unchanged. TEST: **Apply TEST Migrations**
before **Deploy to TEST**. Never run `Down`.

## 8. Test evidence and remaining limitations

Backend: `PurchaseOrderDocumentIdentifierTests`, `RegisterPoDocumentIdentificationTests` (trust model, numbers, REQ-628 cases,
supersession on legacy data, evidence-hash guard, extraction endpoint), `PoDocumentSupersessionMigrationRehearsalTests` (LocalDB),
`CancelledGroupObligationTests`, extensions in `PoGroupCancellationServiceTests` and `AccountsPayableConfigControllerTests`; the four
pre-existing register-po test classes now pass the explicit type confirmation (no evidence in their seeds). Frontend:
`utils.dates.test.ts`, `poDocumentIdentification.test.ts`, extensions of `correctPoSubmission.test.ts`, `registerPoErrors.test.ts`,
`operationInvoiceView.test.ts`, `requestPrintModel.test.ts`, `buyerWorkspaceView.test.ts`, `poGroupCancellation.test.ts`,
`poGroupCancellationWiring.test.ts`, `apNotificationsForm.test.ts`. Counts are in the delivery message. **No browser verification was
performed.**

Remaining limitations:
- The evidence is produced by the wizard's extraction call; a Buyer who never extracts a file can still register it after the explicit
  confirmation + justification (by design, audited). Server-side OCR at registration time would remove even that path and is a
  separate design.
- The OpenAI prompt gained two fields (`documentTitle`, `referencedDocumentNumbers`); older provider answers degrade gracefully.
- The shared instant formatters now display offsetless instants as UTC on every screen (a correction, but visible: +1 h on Angola
  browsers for timestamps that were never suffixed).
- The cancelled-group fix is read-side + guards; cached invoice columns stay as historical evidence.

## 9. Exact manual retest steps

See `docs/PO_GROUP_CANCELLATION_TEST_SCRIPT_v2.249.0.md` **§3.9** (Portuguese): it checks the current state first (MAQSIS already
"Devolvido para Compras" after the second Finance return — no further return), then resumes with the existing PDFs.
