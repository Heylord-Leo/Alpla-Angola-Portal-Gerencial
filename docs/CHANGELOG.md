# Changelog

All notable changes to the Alpla Angola - Portal Gerencial project will be documented in this file.

## Current Version

v2.215.0

## [v2.215.0] - 2026-07-28

### Fixed — Final Approval Lot-Aware Totals & Approval Center Authoritative Amounts + Search

- **Final Approval lot-aware corrections**: a normalized `FinalApprovalLotViewDto` (built server-side by `FinalApprovalLotViewBuilder`) drives the Final Approval drawer. Requested-item totals now resolve from the **selected quotation item** (never the 0 request estimate for quotation requests); the financial summary shows **"Total Aprovado Neste Lote"** with the original estimate only as secondary context; "Cotações Salvas" separates **items considered in this lot** from **IGNORED / not-included** lines (kept for audit, never counted in the lot); the supplier is resolved from the current lot; the requesting plant ("Planta Solicitante") is disambiguated from the financial-allocation plant ("Planta (Alocação)"); and the misleading budget-impact metric was renamed to **monthly-accumulated participation** ("Part. Acum. Mensal") — a share of the department's monthly spend, not configured-budget consumption.
- **Approval Center authoritative amounts**: a centralized `ApprovalQueueAmountResolver` resolves one authoritative `ActionableAmount` per queue card by request type and approval stage — PAYMENT uses the payment amount; QUOTATION uses the **active batch lot total** (approved snapshot, else the sum of the batch's selected quotation items) for Area/Final, or the legacy selected-quotation total when there is no batch. Partial lots are isolated (pending items outside the lot never contaminate the amount). The queue-total KPI and the cards now use the **same** rule, so they can no longer diverge. REQ-17/07/2026-096 (waiting final approval, Lote #1) now shows AOA 79.572,00 instead of AOA 0,00.
- **Defensive missing-value behavior**: an unresolved amount is kept **null** and rendered as **"Valor ainda não definido"** (never a fabricated 0); a genuine zero stays a real 0 and remains distinguishable; a batch snapshot that disagrees with its item sum surfaces a warning instead of a false normal amount.
- **Approval Center search/filter improvements**: the search input is now properly wired (it was previously a no-op) and matches **accent- and case-insensitively** across request number, requester, department, type, status, supplier, plant, company and cost center (request numbers match with or without formatting). One deterministic **scope → search → chip filters → sort** pipeline is shared by both queue sections and the post-action refresh, so no filter silently resets another; value sort and the high-value filter use the authoritative actionable amount. Section counts reflect the filtered results, an **"X de Y pedidos exibidos"** indicator appears while filtering, and a dedicated **"Nenhum pedido encontrado para esta busca."** empty state replaces the ambiguous empty container. Top KPI cards remain global queue indicators.
- Backend unit tests added: `FinalApprovalLotViewBuilder` (10) and `ApprovalQueueAmountResolver` (10) — covering item-total resolution, IGNORED isolation, single/multi-supplier labels, snapshot/sum inconsistency, amount source by type/stage, partial-lot isolation, missing-vs-zero, and queue-total parity.
- **No migration and no database change.**
- **Known limitation (pending follow-up)**: a request with multiple simultaneously actionable `ApprovalBatch` records is still collapsed into a single request-level queue card, so the amount/lot shown on the card and the lot opened in the drawer can diverge (e.g. REQ-21/07/2026-132 with Lote #1 and Lote #2 both WAITING_AREA_APPROVAL). Redesigning the queue to one card per actionable batch is a separate, not-yet-started task.

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.214.0] - 2026-07-28

### Added & Fixed — Quotation Financial Reconciliation & Approval Monetary Corrections

- **OCR-original per-line baseline (immutable)**: each quotation line now captures its OCR-extracted quantity/unit-price/discount/IVA/unit/line-total at SaveQuotation and document-replacement time, and UpdateQuotation compares against — but never overwrites — that persisted baseline. Nulls mean "not extracted" (never treated as 0), so legacy/manual lines stay exempt.
- **Line-level financial-adjustment justification**: one consolidated free-text reason per line for material edits versus the OCR baseline, distinct from the reconciliation justification and the residual justification, enforced on both create and update.
- **Authoritative reconciliation preview + signed residual gate**: a read-only backend preview endpoint computes the document residual from the same shared calculator used by SaveQuotation/UpdateQuotation; a signed residual beyond tolerance blocks the save until explained. Reconciliation applicability keys on the OCR header total, not the source label.
- **EXTRA_ITEM / IGNORED handling & quick-text shortcuts**: buyer batch-composition INCLUDE/EXCLUDE decisions for genuine EXTRA_ITEM lines (shared service across batch creation and rework), a server-side legacy-unresolved-lines gate on Area Approval, and reconciliation-motive quick-text shortcuts (frontend text only, no codes/DB).
- **Partial-approval batch monetary standardization + dynamic financial summary**: winner and EXTRA_ITEM cards show a uniform Qtd / Preço s/IVA / IVA(rate%) / Total c/IVA structure; a live "Resumo financeiro do lote" sums only persisted `taxableBase`/`ivaAmount`/`lineTotal` (never re-derived), with excluded extras removed from the total and a missing-field guard that blocks confirmation instead of showing a false 0.
- **TaxableBase projection corrected across all read paths**: `SavedQuotationItemDto` now projects the net taxable base (`GrossSubtotal − DiscountAmount`) in RequestsController request-details, LineItemsController, and the SaveQuotation/UpdateQuotation responses — fixing "Subtotal sem IVA: 0,00" in the partial-approval modal.
- **Area Approval IVA-rate projection corrected**: request-details now projects the persisted `IvaRatePercent`, so the approval wizard shows "IVA (14%)" instead of a contradictory "IVA (0%)"; the label never renders a 0% rate against a positive IVA amount.
- Additive migration `20260727162615_AddQuotationItemOcrBaseline` (8 nullable columns on `QuotationItems`) — **not applied to any database as part of this release.**
- Backend and frontend regression tests added (reconciliation calculator, controller shapes, batch extra-item decisions, legacy-gate, justification validator).

**Guided Tour impact: not applicable.**

## [v2.213.0] - 2026-07-25

### Fixed — Quotation Wizard: Financial Integrity Override Restoration & Multi-Quotation Support

- **Financial Integrity override restored**: The Quotation Wizard's "Salvar Cotação" flow lost its override path during the July 14 rewrite — a divergence between the OCR-extracted total and the buyer's corrected total returned a hard HTTP 409 with no way to justify and retry. Restored as an inline panel (not a separate modal) showing OCR total, corrected total, variance, and tolerance, with a required justification textarea and an explicit "Salvar com Justificativa" retry — independent of the existing per-item `reconciliationJustification` mechanism.
- **OCR review fixes surfaced during restoration**: quantity/deletion edits in the "Documento e Extração" step were silently recalculating the running total to zero (a stale reconciliation-status filter left over from a different code path); "Total (Documento OCR)" in Revisão Final was bound to the wrong field, showing the buyer-corrected total instead of the immutable OCR original. Both corrected. Deleting an OCR-extracted item now requires explicit confirmation.
- **Ambiguous-save resilience**: a network error, HTTP 5xx, or timeout on quotation creation no longer guarantees a failed write — `SaveChangesAsync` commits atomically, and a client-side timeout can fire after the server already succeeded. The wizard now takes a fresh pre-attempt snapshot of the request's quotations, and on an ambiguous failure re-reads the request and looks for a new, matching quotation (exact `ProformaAttachmentId` match when available; conservative supplier+total/document-number corroboration otherwise) before reporting failure — closing the wizard and showing an interruption notice instead of a false "save failed" when the write actually succeeded.
- **Multiple quotations per supplier allowed**: removed the backend rule limiting a request to one quotation per supplier (`SaveQuotation`/`UpdateQuotation`) — a supplier may legitimately submit a revised, complementary, or alternative quotation. Each quotation remains independent by `QuotationId`; winner selection, batch eligibility, and coverage comparison were already `QuotationId`/`QuotationItemId`-scoped and required no changes. A non-blocking informational notice now appears in the wizard when the selected supplier already has other quotations on the request.
- **Duplicate-file detection reconnected**: the existing SHA-256 file-hash duplicate check (`computeFileHash` + `GET /attachments/check-duplicate`), already used elsewhere in the app, is now also checked before OCR upload in the Quotation Wizard, reusing the existing warning modal and its 5-second confirmation safety delay. The endpoint's response for a duplicate the current user cannot access no longer discloses the original request's ID, number, uploader, or timestamp.
- **Approval Batch Review cleanup**: the "other quotations" comparison area (already collapsed by default) now only lists quotations that actually have a matching item for that specific request line — quotations present on the request but never quoting that line no longer appear as "Não Cotado" placeholders. Updated labels and added an explicit consultation-only disclaimer.
- Removed the unfinished, unreachable legacy duplicate-supplier replacement modal (dead since the July 14 rewrite; its confirm action was a no-op).

**Guided Tour impact: existing tour reviewed, no changes needed.** Both the Buyer Items page tour and the Quotation Management live guide target stable containers (page header, search, request card, docs/quotations section) — none reference the wizard's internal steps, the Financial Integrity panel, the duplicate-file warning, or the Approval Batch Review comparison area, so nothing broke and nothing new required a tour anchor.

## [v2.212.0] - 2026-07-23

### Added — Finance: Group-Aware Payment Actions & Schedule Cancellation

- **Group-aware Finance actions**: single-group kebab menu and multi-group "Pagamentos por Fornecedor" cards now derive Schedule/Pay/Cancel eligibility and Portuguese labels from each `RequestPoGroup`'s own status (never a sibling's or the parent request's aggregated status), for both normal and advance groups. Normal: `PO_ISSUED`/`PAYMENT_REQUEST_SENT` → schedule or direct pay; `PAYMENT_SCHEDULED` → pay. Advance: `ADVANCE_PAYMENT_REQUIRED` → schedule or direct confirm; `ADVANCE_PAYMENT_SCHEDULED` → confirm.
- **Legacy PAYMENT PENDING-group fallback**: `IFinancePaymentEligibilityService.CanSchedule` now accepts `(requestTypeCode, requestStatusCode, groupStatus)`. For QUOTATION the group status remains sole authority (unchanged). For PAYMENT, a meaningful group status stays authoritative; only a null/empty/legacy `PENDING` group (never actively synced) falls back to the parent request status, and only against a narrow, explicitly justified set (`PO_ISSUED`, `PAYMENT_REQUEST_SENT`) — deliberately excluding `PAYMENT_SCHEDULED`/completed/rejected/cancelled states, since no reschedule flow exists.
- **Schedule cancellation** (`POST /api/v1/finance/{id}/cancel-schedule`): lets Finance correct a payment scheduled against the wrong request/group, before it is paid.
  - Normal: `PAYMENT_SCHEDULED → PO_ISSUED`; the scheduled `FINAL_BALANCE` `RequestPayment` row becomes `CANCELLED` (preserved for audit, `PaymentSequence`/`ScheduledDateUtc` retained). A later `SchedulePayment` call computes the next `PaymentSequence` dynamically rather than colliding with the cancelled row.
  - Advance: `ADVANCE_PAYMENT_SCHEDULED → ADVANCE_PAYMENT_REQUIRED`; the **same** `ADVANCE` `RequestPayment` row (originally created at PO registration) returns to `PLANNED` — `ScheduledDateUtc` cleared, `PlannedAmount`/`PlannedPercent` preserved — so it can be rescheduled through the existing `ScheduleAdvancePayment` flow without creating a new row.
  - Completed payments (`PAYMENT_COMPLETED`, `ADVANCE_PAYMENT_COMPLETED`, `WAITING_RECEIPT`, `COMPLETED`) are **not** cancellable through this workflow — reversal of a completed payment is out of scope, tracked as a separate future workflow.
  - A justification (≥20 trimmed characters) is required; the original scheduling history event is never modified — a new `PAYMENT_SCHEDULE_CANCELLED`/`ADVANCE_PAYMENT_SCHEDULE_CANCELLED` event is added alongside it.
  - The most recent active `PAYMENT_SCHEDULE` attachment for the group is marked **voided** (`RequestAttachment.VoidedAtUtc`/`VoidedByUserId`/`VoidReason`) — distinct from deletion: it remains stored, visible, and downloadable, shown with a "Sem efeito" badge and the recorded reason, and can no longer satisfy any "has schedule document" validation gate. Older/sibling-group attachments are never touched.
  - UI: `FinanceActionModal` gains a `CANCEL_SCHEDULE` mode with a read-only summary (supplier, amount, previously-scheduled date) and the required-reason field; `FinanceHistory.tsx` gets a "Cancelamentos" filter (server-side widened to include both normal and advance cancellation codes), CSV export labels, and a void indicator on the related upload's audit card.
- **Supplier/currency display fallback**: `FinanceGroupDisplayResolver` resolves a safe display supplier/currency (group snapshot → selected quotation → request-level `Supplier`/`Currency` → "---") for legacy `RequestPoGroup` rows whose own `SupplierNameSnapshot`/`CurrencyCode` were never actively synced — used by the Finance payments listing, the cancel-schedule modal, and both schedule/cancel structured history comments. Display-only; no backfill is written to the group record.
- **Business-date formatting fix**: `RequestPayment.ScheduledDateUtc` round-trips through SQL Server's `datetime2` (no offset metadata); the JSON payload for it therefore omits a `Z` suffix, and a naive `new Date(str)` on the frontend was parsing it as local time instead of UTC — silently shifting the displayed calendar day by the browser's UTC offset (reproduced: 24/07 stored, 23/07 displayed on a UTC+1 host). `formatBusinessDateOnly` extracts the calendar date directly from the ISO string, never constructing a `Date` object, so the cancel-schedule modal and the audit history now always agree.
- **Finance History attachment void indicator**: `DOCUMENTO ADICIONADO` audit cards for a since-cancelled `PAYMENT_SCHEDULE` upload now show "Sem efeito" and the void reason, via a best-effort filename match against the request's `PAYMENT_SCHEDULE` attachments (display-only; `RequestStatusHistory` has no attachment FK).
- **Migration**: `20260723175105_AddAttachmentVoidFields` — additive, nullable `VoidedAtUtc`/`VoidedByUserId`/`VoidReason` columns on `RequestAttachments`. Not applied to any database as part of this release; apply via the standard `execution/update_dev_database.ps1` / CI migration workflow.
- **Legacy compatibility**: no database backfill is performed anywhere in this release — QUOTATION requests remain fully group-status-driven; PAYMENT requests with a stale `PENDING` group use the parent status only as the narrow fallback described above; legacy supplier/currency fields are resolved for display only.
- See `docs/DECISIONS.md` (DEC-150) for the full architectural rationale, including why normal and advance cancellation intentionally mutate `RequestPayment` differently and why only the newest active attachment is voided.

**Guided Tour impact: not applicable for this release** — the Finance module (Payments list and History) has no guided tour registered (predates this task); creating one is deferred as a follow-up, not silently treated as reviewed/updated. See `docs/DECISIONS.md` (DEC-150).

## [v2.211.0] - 2026-07-22

### Fixed — Finance > Payments: Missing Actions on P.O. Emitida Requests + Search/Sort

- **Root cause**: `FinanceController.GetPayments` computed `AvailableFinanceActions` from the parent `Request.Status.Code` only, while separately filtering the `PoGroups` array returned in the DTO to statuses in `financeGroupStatuses`. 12 legacy `PAYMENT`-type `RequestPoGroup` rows (all created in the same historical backfill batch, `CreatedAtUtc = 2026-07-20 17:25:39`) are still at `Status = 'PENDING'` even though their parent `Request.Status.Code` already advanced to `PO_ISSUED`. Because `PENDING` is not a finance-pipeline group status, the API returned `PoGroups = []` for these rows, and `FinancePaymentsList.tsx`'s `poGroups.length > 0` gate hid "Agendar pagamento" / "Marcar como pago" even though the backend's own authorization rule for `MarkAsPaid` (parent-status-driven for `PAYMENT`-type requests) would have accepted the action.
- **Centralized eligibility**: extracted `IFinancePaymentEligibilityService` / `FinancePaymentEligibilityService` (`AlplaPortal.Application.Interfaces.Finance` / `AlplaPortal.Infrastructure.Services.Finance`) as the single source of truth for `SCHEDULE`/`PAY`/`RETURN`/`ADD_NOTE`/`ADD_PROOF` eligibility. `GetPayments` (listing) and `SchedulePayment`/`MarkAsPaid`/`ReturnForAdjustment` (execution) now share the exact same predicates — the list can no longer advertise an action the corresponding endpoint would reject, or hide one it would accept.
- **Frontend**: `FinancePaymentsList.tsx` renders actions from `availableFinanceActions` alone; a missing/unresolvable group id is now treated as an execution/input concern (see `resolveSingleGroupRowActions` in `lib/financePaymentsView.ts`), not folded into eligibility. The backend `PoGroups` projection no longer filters out legacy-status groups, so a resolvable group id is available whenever the row's only group needs one.
- **Search expanded**: the "Fornecedor"-only filter is now a general "Buscar" field (`Buscar por pedido ou fornecedor...`), matching by `Request.RequestNumber` OR the effective supplier name (`Quotation.SupplierNameSnapshot` / `Request.Supplier.Name`), server-side, case-insensitive, combinable with the existing status/currency filters. The legacy `searchSupplier` query param is still honored as a fallback so old bookmarked URLs keep working; the new `search` param takes precedence when both are present.
- **Sortable headers**: Identificação, Fornecedor, Vencimento, Status, and Valor are now sortable, following the same click-to-toggle UX, icons (`ArrowUp`/`ArrowDown`/`ArrowUpDown`), and server-side sort-key conventions already used on the Requests page — including sorting request numbers by `CreatedAtUtc.Date` (not the formatted string) and status by `RequestStatus.DisplayOrder` (workflow position, not alphabetical). Ações remains non-sortable.
- **Legacy data**: a PREVIEW/APPLY remediation script for the confirmed 12-row cohort was added but **not executed** — see `scripts/db/remediate-legacy-po-group-status-payment-po-issued.sql` and its companion rollback script. Running it (APPLY mode) requires separate, explicit authorization.
- **MarkAsPaid self-healing confirmed and covered**: a follow-up read-only review confirmed `MarkAsPaid` unconditionally advances `RequestPoGroup.Status` to the same paid status as `Request.Status` — the "impossible state" (`Request=PAYMENT_COMPLETED`/`RequestPoGroup=PENDING`) is unreachable. Documented in DEC-149 and covered end-to-end by a new controller-level transition test (`FinanceMarkAsPaidTransitionTests`), not just the eligibility predicate.
- **CANCELLED groups excluded from operational group count**: `PoGroups` intentionally still returns `CANCELLED` groups (historical display), but `financePaymentsView.ts` now centralizes a `resolveOperationalGroups`/`hasMultipleOperationalGroups` distinction so a `CANCELLED` group never inflates the multi-group determination, never gets auto-selected as the acting group, and never suppresses a valid single-group action. Legacy `PENDING` `PAYMENT` groups remain fully resolvable — this does not reintroduce the old broad finance-status filter (only `CANCELLED` is excluded, nothing else).

**Ficheiros alterados:**
- `src/backend/AlplaPortal.Application/Interfaces/Finance/IFinancePaymentEligibilityService.cs` (new)
- `src/backend/AlplaPortal.Infrastructure/Services/Finance/FinancePaymentEligibilityService.cs` (new)
- `src/backend/AlplaPortal.Api/Controllers/FinanceController.cs` — centralized eligibility, general search, server-side sorting
- `src/backend/AlplaPortal.Api/Program.cs` — DI registration
- `src/frontend/src/pages/Finance/FinancePaymentsList.tsx` — action rendering, search UI, sortable headers, operational-group-count helper usage
- `src/frontend/src/lib/financePaymentsView.ts` (new) — pure action-visibility / sort-toggle / operational-group-count helpers
- `src/frontend/src/lib/api.ts` — `getPayments` search/sort params
- `scripts/db/remediate-legacy-po-group-status-payment-po-issued.sql` (new, not executed)
- `scripts/db/rollback-legacy-po-group-status-payment-po-issued.sql` (new, not executed)
- Tests: `tests/backend/.../Services/Finance/FinancePaymentEligibilityServiceTests.cs`, `FinancePaymentsSearchAndSortQueryTests.cs`, `FinanceMarkAsPaidTransitionTests.cs` (new); `src/frontend/src/lib/financePaymentsView.test.mjs` (new)

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.210.1] - 2026-07-22

### Changed — Standardized Development Database to Portal-Gerencial-Dev-ProdClone

- **Canonical database**: `Portal-Gerencial-Dev-ProdClone` is now the only supported local Development database. `AlplaPortalV1` was backed up (27.43 MB, `RESTORE VERIFYONLY` passed) and dropped.
- **Pre-migration identity guard** (`Program.cs`, Development-only): queries `DB_NAME()` and `@@SERVERNAME` **before** `Database.Migrate()` and aborts with `InvalidOperationException` if the database is not the canonical clone. A post-migration re-verification confirms identity and logs the latest `MigrationId`. Does not affect TEST or PROD.
- **Canonical startup script** (`execution/restart_services.ps1`): validates `DB_NAME()` via direct SQL before launching backend/frontend. All alternative startup routes (`restart_services.py`, `scripts/start-all.ps1`) now delegate to this script.
- **Protected migration script** (`execution/update_dev_database.ps1`): preview-by-default with `DB_NAME()` preflight; application requires explicit `-Apply -Confirmation 'APPLY-MIGRATIONS-TO-DEV-CLONE'`.
- **Isolated integration tests**: shared `IntegrationTestDatabase` helper targets `Portal-Gerencial-IntegrationTests` with a forbidden-database guard that aborts if the resolved DB is the clone, `AlplaPortalV1`, or any production/test database. CI fail-loud mode via `CI_FAIL_ON_MISSING_DB`.
- **Fail-closed legacy tooling**: `DemoDataGenerator`, `run-sql.csx`, `apply-soft-delete-migration.csx`, and `apply-dec110-migration.ps1` exit immediately with deprecation messages.
- **Agent directive**: `directives/RULE_DEV_DATABASE.md` registered in AGENTS.md, CLAUDE.md, and GEMINI.md.

**Guided Tour impact: not applicable.**

## [v2.210.0] - 2026-07-22

### Added — Atualização de Dados PROD → Desenvolvimento Local (Export/Import)

- **Workflow `Export PROD Data for Dev`** (`export-prod-data-dev.yml`, self-hosted `AOVIA1VMS011`, environment `production`): executa somente `BACKUP DATABASE` em `Portal-Gerencial` (nunca escreve em PROD), calcula o checksum SHA-256 do `.bak` e publica ambos os arquivos (`.bak` + `.bak.sha256`) como artifact com retenção de 1 dia. Um step dedicado de limpeza (`if: always()`) remove os dois arquivos exatos do runner após o upload, falhando explicitamente se a remoção não puder ser confirmada. `permissions: contents: read` (least-privilege).
- **`scripts/db/import-prod-data-dev.ps1`** (executado manualmente, localmente, fora do GitHub Actions): restaura o backup em uma base LocalDB isolada e descartável (`Portal-Gerencial-Dev-ProdClone`), nunca `Portal-Gerencial`, `Portal-Gerencial-Test` ou `AlplaPortalV1`. Exige verificação de checksum SHA-256 bem-sucedida (`-ChecksumFilePath`/`-ExpectedSha256`) antes de qualquer conexão SQL quando `-Apply` é usado; modo Preview (padrão) é somente leitura.
- **`scripts/db/dev-safety-neutralization.sql`**: neutraliza e verifica (fail-closed) `EmailOutbox`, `SmtpSettings`, `IntegrationProviders`, `IntegrationProviderSettings` e `Users.PasswordResetToken`/`PasswordResetTokenExpiryUtc` após cada restore, usando checagens defensivas `OBJECT_ID`/`COL_LENGTH` e SQL dinâmico.
- **Sincronização de anexos** (`-AttachmentMode FullClone|Incremental|None`): sempre aditiva (`/E /XC /XN /XO`), nunca `/MIR`/`/PURGE`, nunca sobrescreve ou apaga arquivo local existente; caminho de destino validado contra raiz de disco, `Windows`/`Program Files`, raiz do repositório e caminhos de deploy PROD/TEST.
- **`docs/DEV_DATA_REFRESH.md`**: runbook completo (arquitetura, sensibilidade do artifact, sequência de comandos, procedimento de rollback, matriz de neutralização).
- **`scripts/db/validate-export-prod-data-dev-inputs.ps1`**: validação não-destrutiva de ref/confirmação/versão antes de qualquer operação no export.

**Guided Tour impact: not applicable.**

## [v2.209.0] - 2026-07-21

### Added — Approval Drawer Quick Actions

- **Rejeitar e Solicitar Reajuste restaurados**: botões de ação rápida no rodapé do drawer de Aprovação de Área, permitindo rejeição e solicitação de reajuste sem abrir o Wizard completo.
- **Guard G1 — Visibilidade condicional**: Reajuste e Rejeitar são exibidos apenas quando o escopo é determinístico — Cotação com lote ativo mostra os 3 botões; Pagamento mostra Rejeitar + Revisar Pedido; Cotação sem lote mostra apenas Revisar Pedido (fallback para o Wizard).
- **Guard G2 — Descrições batch-aware**: o modal de confirmação (ApprovalModal) exibe o número do lote e a contagem de itens nas descrições de rejeição e reajuste, informando claramente ao utilizador que a ação é aplicada apenas ao lote em revisão.
- **Guard G3 — Layout responsivo**: `flexWrap: 'wrap'` adicionado ao container do rodapé para segurança em larguras mínimas (520px).
- **Comportamento disabled**: todos os 3 botões de área incluem `disabled={approvalProcessing}` com feedback visual (opacity + cursor).
- **Tour atualizado**: `approvalDrawerAreaTour.ts` atualizado para refletir os novos botões de ação rápida.

**Ficheiros alterados:**
- `src/frontend/src/components/ApprovalModal.tsx` — props `batchNumber` e `batchItemCount` + descrições condicionais.
- `src/frontend/src/pages/Approvals/ApprovalDetailPanel.tsx` — rodapé de área com 3 botões guardados + props de batch no modal.
- `src/frontend/src/features/guided-tour/tours/approvalDrawerAreaTour.ts` — tour step e comentário atualizados.

**Guided Tour impact: existing tour updated.**

## [v2.208.4] - 2026-07-21

### Fixed — API: Rota Duplicada Causando HTTP 500 ao Salvar Cotação

- **Causa raiz**: `AuthorizeQuotationReuse` tinha dois atributos `[HttpPost]` empilhados — um resquício de copy-paste (`{id:guid}/quotations`, introduzido no mesmo commit `7e021d5` que criou o endpoint) e o atributo correto (`{id:guid}/quotations/{quotationId:guid}/authorize-reuse`). O ASP.NET Core registrava a rota `{id:guid}/quotations` tanto para `AuthorizeQuotationReuse` quanto para `SaveQuotation`, causando `AmbiguousMatchException` (HTTP 500, corpo vazio) em toda tentativa de salvar cotação desde o deploy daquele commit em TEST.
- **Correção**: removida apenas a linha de atributo duplicada e a linha em branco associada em `RequestsController.cs`. Nenhuma rota, autorização ou lógica de reuso de cotação foi alterada.
- **Frontend**: nenhuma alteração necessária — o único chamador (`api.ts`) já usava a rota correta `.../quotations/{quotationId}/authorize-reuse`.
- Verificado com build Release (0 erros) e teste de roteamento ao vivo local: ambas as rotas passam a resolver para uma única action cada (HTTP 401 por falta de autenticação, sem `AmbiguousMatchException`).

**Guided Tour impact: not applicable.**

## [v2.208.3] - 2026-07-19

### Fixed — CI: Guard "ResponsibleUserId" do Workflow "Apply TEST Migrations" Restrito Corretamente

- **Falso positivo corrigido**: o script incremental de 71→83 migrations (incluindo `20260716094419_PhaseCRemoveLegacyAreaApprovalConfig`, que remove a coluna legada `Departments.ResponsibleUserId` com backup de auditoria) estava sendo rejeitado por um guard que fazia busca de substring/regex genérica em todo o SQL gerado, sem distinguir operação de remoção legítima de recriação perigosa.
- **`Test-ResponsibleUserIdSafety`** (nova função pura em `scripts/db/migration-range.ps1`): combina duas camadas — (1) posição: qualquer referência de uma migration posicionada **depois** da que remove a coluna é sempre rejeitada; (2) padrão: mesmo antes/na migration de remoção, apenas formas genuinamente perigosas são rejeitadas (`ADD`, `CREATE INDEX`, `ADD CONSTRAINT`/FK, `ALTER COLUMN`, `UPDATE`/`INSERT` gravando na coluna) — leituras, `DROP COLUMN`/`INDEX`/`CONSTRAINT` e backups de auditoria em outra tabela continuam permitidos.
- **`Test-ModelSnapshotNoLegacyProperty`** (nova função pura): confirma, de forma estática e sem banco, que o modelo EF atual não define mais `Departments.ResponsibleUserId` — escopada ao bloco da entidade, evitando falso positivo com propriedades de nome parecido em outras entidades (ex.: `Request.CurrentResponsibleUserId`).
- **Nenhuma mudança na lógica de sincronização/migração em si**: `Test-MigrationPrefix`, `Get-MigrationRange`, `Get-MigrationIdsFromScript`, o comportamento de abortar antes do `sqlcmd`, backup, e demais proteções permanecem inalterados.
- 29 testes não-destrutivos adicionados e passando (`scripts/db/migration-range.Tests.ps1`), incluindo contra o script real gerado (71→83) e o `ApplicationDbContextModelSnapshot.cs` real do repositório.
- Confirmado por investigação: nenhum SQL foi aplicado ao TEST pela falha anterior nem por esta correção; `Portal-Gerencial-Test` permaneceu em 71 migrations aplicadas durante todo o processo.

**Guided Tour impact: not applicable.**

## [v2.208.2] - 2026-07-19

### Fixed — CI: Compatibilidade do Workflow "Sync PROD Data to TEST" com Runners sem Git no PATH

- **Correção do step "Resolve commit metadata"**: falhava no runner self-hosted com "The term 'git' is not recognized" porque `git` não está no `PATH` ali, e `actions/checkout@v4` caía para o fallback de download via API REST (documentado), que não deixa metadados `.git` utilizáveis.
- **Removida a dependência obrigatória de Git no runner**: o workflow não exige mais que `git` esteja instalado para rodar esta sincronização.
- **`GITHUB_SHA` validado e usado como SHA autoritativo**: fonte de verdade para o commit resolvido, fornecida diretamente pelo GitHub Actions para o dispatch — não depende de clone real do repositório.
- **Cross-check com `git rev-parse HEAD` somente quando Git e metadados `.git` estão disponíveis**: detectado via `Get-Command git` + verificação do diretório `.git`; nunca invocado incondicionalmente.
- **Suporte explícito ao checkout por fallback REST** do `actions/checkout@v4`; checkout continua sem `ref:` forçado (usa o ref selecionado no dispatch).
- **Validação de SHA hexadecimal completo de 40 caracteres** (`^[0-9a-fA-F]{40}$`) tanto no novo script de resolução de metadados quanto, como defesa em profundidade, em `validate-sync-prod-data-test-inputs.ps1`.
- **Falha explícita quando o Git local diverge de `GITHUB_SHA`**: o erro ocorre antes da validação de inputs/ref/versão e antes do script de sincronização — nunca depois.
- **Nenhuma alteração na lógica de sincronização de dados**: `scripts/db/sync-prod-data-test.ps1` permanece idêntico ao conteúdo já publicado e confiável (backup PROD/TEST, `SINGLE_USER`/`RESTORE ... WITH REPLACE`/`MULTI_USER`, remapeamento de login, neutralização de `EmailOutbox`/`SmtpSettings`/`IntegrationProviders`, espelhamento de anexos via Robocopy).
- **Warning do Node 20** (`actions/checkout@v4`) continua registrado apenas como nota informativa, sem relação com esta correção.
- Nenhuma alteração funcional na aplicação.

**Guided Tour impact: not applicable.**

## [v2.208.1] - 2026-07-19

### Fixed — CI: Endurecimento e Migração do Workflow "Sync PROD Data to TEST"

- **Workflow portado para `Portal-Gerencial-rev1`**: `.github/workflows/sync-prod-data-test.yml` e `scripts/db/sync-prod-data-test.ps1` existiam apenas em `main`, criados diretamente lá e nunca sincronizados com o branch oficial de desenvolvimento; agora são mantidos em `Portal-Gerencial-rev1` e chegam a `main` pelo processo normal de merge.
- **Removido o hardcode histórico `release_version == v2.205.0`**, que rejeitava qualquer versão publicada além de v2.205.0 (causa raiz da falha ao tentar dispatchar com v2.208.0).
- **Validação dinâmica contra `docs/VERSION.md`**: a versão exigida é lida da seção "Current Version" do repositório no ref selecionado, normalizada (trim), e comparada por igualdade exata com o input `release_version`.
- **Validação SemVer** (`^v\d+\.\d+\.\d+$`) aplicada tanto à versão do repositório quanto ao valor informado pelo usuário — rejeita formatos como `v2.208`, `2.208.0` (sem prefixo `v`) ou valor vazio.
- **Guard obrigatório de branch**: o workflow só prossegue quando `github.ref_name == 'Portal-Gerencial-rev1'`; qualquer outro ref (incluindo `main` ou branches de release) aborta antes de qualquer operação de banco, sem input de bypass.
- **As duas confirmações destrutivas preservadas**: `confirm_restore = RESTORE_PROD_TO_TEST` e `confirm_no_prod_changes = I_UNDERSTAND_PROD_WILL_NOT_BE_MODIFIED`.
- **Impressão segura de rastreabilidade** antes de qualquer operação destrutiva: ref selecionado, `github.ref_name`, SHA resolvido (`git rev-parse HEAD`, comparado com `github.sha`), versão do repositório, versão informada, status da confirmação, bancos e caminhos origem/destino — nenhuma senha, connection string ou segredo é impresso.
- **Script de sincronização preservado sem nenhuma mudança funcional**: `scripts/db/sync-prod-data-test.ps1` permanece byte-a-byte idêntico ao de `main` (backup PROD/TEST, `SINGLE_USER`/`RESTORE ... WITH REPLACE`/`MULTI_USER`, remapeamento de login, neutralização de `EmailOutbox`/`SmtpSettings`/`IntegrationProviders`, espelhamento de anexos via Robocopy com validação de exit code) — nenhum defeito foi encontrado nesta lógica.
- **Warning do Node 20** (`actions/checkout@v4`) registrado apenas como nota informativa, sem relação com a lógica de sincronização.
- Nova validação isolada e não-destrutiva (`scripts/db/validate-sync-prod-data-test-inputs.ps1`) — nunca abre conexão SQL, nunca cria backup, nunca invoca Robocopy — permitindo testar todos os guards sem tocar TEST ou PROD.
- Nenhuma alteração funcional na aplicação; nenhuma alteração na lógica de sincronização de dados.

**Guided Tour impact: not applicable.**

## [v2.208.0] - 2026-07-19

### Fixed — Integridade de Cotação e Correções do Wizard (Escopo A)

- **Linhas IGNORED permanecem no payload e no histórico de auditoria**: o wizard de Cotação passa a enviar as linhas explicitamente ignoradas (antes eram descartadas do payload, tornando cada linha ignorada uma falsa divergência exatamente igual ao seu valor com IVA); são persistidas em `QuotationItems` com `ReconciliationStatus=IGNORED` e justificativa.
- **Totais das linhas ignoradas excluídos do baseline comparável do OCR**: `QuotationIntegrityCalculator` calcula `excludedIgnoredTotal` (LineTotal com IVA) e `comparableDocumentTotal = totalOCR − ignoradas`; a variância e a tolerância (2,00) operam sobre escopos financeiros equivalentes. Divergências reais nas linhas consideradas continuam bloqueando (resposta 409 com campos de diagnóstico).
- **Linha ignorada com valor > 0 exige justificativa própria da reconciliação** (UI + gates de avanço/salvamento + validação backend 400 estruturada, sem stack trace); linha de valor zero é isenta.
- **Agregados da cotação (SaveQuotation/UpdateQuotation) somam apenas linhas consideradas** (MAPPED/SUBSTITUTE/EXTRA_ITEM) — a IGNORED fica para auditoria sem inflar o total.
- **Normalização de status de reconciliação no boundary da API** (trim + uppercase, vocabulário canônico `RequestConstants.ReconciliationStatuses`).
- **Ciclo de vida do `saveError` do wizard corrigido**: limpo ao abrir/fechar, iniciar nova cotação, trocar documento, navegar para trás, editar o draft (qualquer mutação), nova tentativa e sucesso — sem vazar entre sessões nem persistir após correção.
- Testes: `QuotationIntegrityCalculatorTests` (7) e `SaveQuotationIgnoredLineIntegrationTests` (3, controller real + SQL, incluindo variante de caixa normalizada).

### Added — Reuso Explícito de Cotações após Lotes Cancelados (Escopo B, Opção C)

- **Itens usados em lotes CANCELADOS são bloqueados por default**: a inelegibilidade é **derivada** dos vínculos históricos (`ApprovalBatchItems` × lote CANCELLED) — sem backfill destrutivo; requests históricos (ex.: REQ-13/07/2026-024) ficam corretos automaticamente.
- **Autorização explícita e por item do comprador** (`QuotationReuseAuthorization`): motivo obrigatório, **reuso parcial** suportado (a ação "reutilizar cotação" cria um registro por item), índice único filtrado impede duas autorizações ativas para o mesmo (item, lote de origem).
- **Revogação antes do consumo** (`.../quotation-reuse-authorizations/{id}/revoke`); autorização consumida não pode ser revogada (409 `REUSE_AUTHORIZATION_CONSUMED`); duplicada ativa → 409 `REUSE_ALREADY_AUTHORIZED`.
- **Consumo atômico**: ao usar o item num novo lote (CreateBatch/UpdateBatch), a autorização é consumida na mesma transação (`ConsumedByApprovalBatchId`/`ConsumedAtUtc`); falha do lote reverte o consumo; consumida nunca volta a ser elegível (novo ciclo exige novo cancelamento + nova autorização).
- **Backend rejeita seleção direta não autorizada**: `IQuotationItemEligibilityService` (fonte única) + 409 estruturado `QUOTATION_REUSE_NOT_AUTHORIZED` (com quotationItemId/lote de origem) em `CreateBatch`, `UpdateBatch`, `ResubmitBatch` e na aprovação individual de área — independentemente do frontend.
- **UI**: comprador vê badges "Usada em lote cancelado"/"Reuso requer confirmação" e modal de autorização (itens com checkbox, lote de origem, motivo, garantias de que o histórico não muda); wizard do aprovador oculta itens bloqueados (sem radio, fora do melhor-preço, do "Selecionar Todos" e do sumário, com mensagem "Lote #N cancelado — reuso não autorizado") e exibe proveniência "Reutilizado do Lote #N (cancelado)" nos autorizados, sem pré-seleção.
- **Auditoria**: `QUOTATION_REUSE_AUTHORIZED`, `QUOTATION_REUSE_REVOKED`, `QUOTATION_REUSED_IN_NEW_BATCH` no histórico do pedido (cotação, fornecedor, lote de origem/destino, itens, autor, motivo).
- **Lotes históricos e `ApprovalBatchItems` permanecem inalterados** (validado por teste e no ciclo da migration); `SelectedQuotationItemId` históricos preservados; migration `20260718205502_AddQuotationReuseAuthorizations` aditiva com Down completo (ciclo apply/rollback/reapply validado).
- Testes: `QuotationReuseAuthorizationIntegrationTests` (7, controllers reais + SQL, espelho do REQ-024).

**Notas operacionais (dev, registradas como débito técnico):** o HMR do Vite não detecta mudanças na árvore OneDrive/junction — reinicie o Vite após alterações de frontend; `dotnet run` pode falhar por política corporativa no apphost `.exe` — inicie o backend dev com `dotnet bin/Debug/net8.0/AlplaPortal.Api.dll`. Portas oficiais inalteradas (5000/5173).

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.207.3] - 2026-07-18

### Fixed — Aprovação de Área de pedidos PAYMENT (500 determinístico) e conflito de concorrência estruturado

- **Correção da aprovação de área de pedidos PAYMENT**: `POST /requests/{id}/area-approval/approve` retornava HTTP 500 (`DbUpdateConcurrencyException`, "expected to affect 1 row(s), but actually affected 0") em toda aprovação — reproduzido deterministicamente com um único POST.
- **Novas `RequestLineItemAllocation` registradas explicitamente como `Added`**: no bloco Multi-Allocation Propagation de `ProcessAreaApproval`, cada nova alocação agora é adicionada ao DbSet (`_context.RequestLineItemAllocations.Add(a)`) além da navigation collection, forçando INSERT — espelhando o padrão já correto do fluxo de lote (`ApprovalBatchController`, inalterado).
- **Prevenção de UPDATE indevido para Guid client-generated inexistente**: a causa raiz era o EF classificar como `Modified` (entidade "existente") uma alocação nova com PK Guid já preenchida, descoberta apenas via navegação — emitindo UPDATE numa linha que nunca existiu (0 rows → exceção). QUOTATION aprovado pelo fluxo individual usava o mesmo bloco e estava igualmente vulnerável; PAYMENT era o único caminho sem alternativa (o lote é bloqueado para PAYMENT).
- **Resposta 409 `APPROVAL_CONCURRENCY_CONFLICT` para conflitos reais**: `ApplyStatusChangeAndSyncItemsAsync` captura exclusivamente `DbUpdateConcurrencyException` (outras `DbUpdateException` não são tratadas como concorrência) e devolve ProblemDetails 409 com `code=APPROVAL_CONCURRENCY_CONFLICT`, título "Conflito de concorrência" e mensagem "O pedido foi alterado por outra operação. Atualize os dados e tente novamente." — sem stack trace; erro técnico logado no backend com correlation ID (TraceIdentifier); rollback preservado; sem retry.
- **Frontend (Central de Aprovações)**: o 409 estruturado fecha o wizard/modal, encerra o estado "Processando...", exibe mensagem amigável e recarrega a fila/pedido; sem reenvio automático e sem `alert()` técnico.
- **Testes de regressão SQL** (`AreaApprovalAllocationTrackingTests`, LocalDB): caminho feliz (novas alocações `Added` antes do SaveChanges → INSERTs persistidos, sem exceção), guard que reproduz o bug original (padrão navigation-only → `Modified` → `DbUpdateConcurrencyException`), e substituição com alocações pré-existentes (antigas `Deleted` e removidas, novas `Added` e inseridas, IDs disjuntos, ordem e percentagens preservadas).
- **Validação runtime (DEV)**: pedido PAYMENT real aprovado com um único POST → HTTP 200, 4 alocações inseridas, status `WAITING_FINAL_APPROVAL`, histórico `APPROVE` registrado, zero erros de concorrência.
- **Limitações registradas**: QUOTATION individual usa o mesmo bloco corrigido (cobertura estrutural + testes; sem runtime fim-a-fim por ausência de cenário pronto); o contrato 409 foi validado por código/testes, não por corrida física simultânea; `RowVersion` permanece melhoria futura separada (DEC-146).

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.207.2] - 2026-07-17

### Fixed — Geração incremental (FROM/TO) do SQL de migrations com validação de prefixo

- **Geração incremental por intervalo FROM/TO**: o SQL idempotente passa a ser gerado apenas para o intervalo pendente via `dotnet ef migrations script <FROM> <TO> --idempotent` (`FROM` = última migration aplicada, ou `0` para banco vazio; `TO` = última migration do filesystem), em vez de gerar desde a primeira migration.
- **Prevenção de recompilação de SQL histórico**: gerar desde o início reemitia o corpo de migrations já aplicadas; quando uma migration histórica referencia uma coluna removida por outra posterior (`Departments.ResponsibleUserId`), o SQL Server falhava ao **compilar** a referência inline dentro do bloco guardado `IF NOT EXISTS(...) BEGIN ... END` (erro 207 "Invalid column name") antes da avaliação do guard em runtime. O intervalo pendente exclui esses corpos históricos.
- **Validação estrita de prefixo do histórico** antes de qualquer backup/geração/aplicação: as migrations aplicadas devem ser um prefixo exato e contíguo da lista do filesystem (ordem canônica de `get-expected-migrations.ps1`). Bloqueia **gap, ordem divergente, migration desconhecida (aplicada ausente do filesystem), pendência intercalada, mais aplicadas que esperadas e duplicidade**, reportando índice/esperada/encontrada; o histórico nunca é corrigido automaticamente.
- **Validação exata dos MigrationIds presentes no script**: extração dos IDs inseridos em `__EFMigrationsHistory` (não busca textual genérica) exigindo conjunto == pendentes, nenhuma já-aplicada reinserida, nenhuma após o `TO` e contagem == pendentes; no cenário incremental, ausência de `ResponsibleUserId` e de `SET QUOTED_IDENTIFIER OFF`. Qualquer divergência aborta antes do `sqlcmd`.
- **Verificação read-only de estado parcial antes de nova aplicação** (`scripts/db/check-pre-migration-state.ps1`): confirma contagem aplicada, ausência das pendentes no histórico e ausência dos objetos que elas criam; e orientação pós-falha (snapshot read-only do `__EFMigrationsHistory` + conferência manual), sem auto-restore. `sqlcmd -b`, preflight `QUOTED_IDENTIFIER` e interrupção em exit≠0 preservados.
- Casos de **banco vazio** (`FROM = 0`, script completo esperado) e **sem pendências** (saída antecipada, sem backup/script/sqlcmd) tratados explicitamente. Testes unitários (`migration-range.Tests.ps1`, 18 asserts, sem DB).

**Guided Tour impact: not applicable.**

## [v2.207.1] - 2026-07-17

### Fixed — Estabilização do design-time do EF Core (pipeline de migrations)

- **`DesignTimeDbContextFactory`** (`AlplaPortal.Infrastructure/Data/`) fornece o `ApplicationDbContext` para os comandos `dotnet ef`, resolvendo a connection string por env `ConnectionStrings__DefaultConnection` → `appsettings(.Development).json` (opcional) → placeholder de design-time não-operacional (nunca LocalDB). Não resolve serviços da aplicação, não faz seed e não abre conexão.
- **`dotnet-ef` fixado em 8.0.11** via `.config/dotnet-tools.json` (ferramenta local).
- **Remoção da dependência da ferramenta global**: scripts e workflows passam a executar `dotnet tool restore` e a **validar** que `dotnet ef --version` é 8.0.11, falhando imediatamente caso contrário (nenhuma instalação/atualização/remoção de tool global no runner).
- **Geração idempotente em Release com `--no-build`**: uma única tentativa determinística reutilizando o build Release do workflow (removidas a tentativa Debug previsivelmente inválida e o fallback duplo); falha na geração aborta antes de aplicar qualquer SQL.
- **Infrastructure como startup project**: `--project` e `--startup-project` apontam para `AlplaPortal.Infrastructure` (self-contained, sem host), de modo que o `Program.cs` — e seu guard de connection-string — **nunca é executado** durante a geração do SQL. O guard de runtime permanece intacto.
- **Paridade dos workflows TEST e PROD** (`apply-migrations-test.yml` e `apply-migrations-prod.yml`) com a mesma estratégia técnica.

**Guided Tour impact: not applicable.**

## [v2.207.0] - 2026-07-17

### Added — Itens Obrigatórios, Workaround de Reconciliação, Fornecedor Contextual (OCR) e NIF de Empresa

- **Itens obrigatórios em novos pedidos**: Cotação valida ≥1 item válido (descrição, quantidade > 0, unidade) no **CreateRequest**; Pagamento valida no **Submit** (DRAFT sem itens continua permitido para OCR progressivo). Regra extraída para `IRequestLineItemSubmissionValidator` (reutilizável), com consistência financeira por linha no Pagamento. Sem migração destrutiva — pedidos históricos sem item permanecem acessíveis.
- **UX de campos obrigatórios**: em Empresas, conflito de NIF retorna contrato estruturado (`COMPANY_TAX_ID_CONFLICT` + nome/ID da empresa) e o frontend faz scroll, destaca o campo, mostra mensagem inline, foca e preserva os demais valores. Em novo pedido de Cotação, a seção de itens faz scroll, pulsa em vermelho ~5s (token reutilizável `.error-pulse`, respeita `prefers-reduced-motion`), depois mantém borda de erro discreta, com mensagens por seção e por campo e foco no primeiro campo corrigível.
- **Workaround de reconciliação (comprador)**: `POST /api/v1/requests/{requestId}/line-items/from-proforma` cria um item **solicitado** omitido a partir da proforma/OCR, semanticamente distinto de `EXTRA_ITEM`. `ILineItemFactory` centraliza criação/validação/histórico. Proveniência: `CreationOrigin=BUYER_RECONCILIATION`, `SourceProformaAttachmentId`, `CreationIdempotencyKey` (índice único filtrado) + evento `ITEM_ADDED_FROM_PROFORMA`. Idempotência de mesma operação (chave por linha) + detecção de duplicidade cross-session (RequestId + proforma + linha normalizada). `UnitPrice=0` — nunca copia o preço da proforma como valor solicitado.
- **Criação contextual de fornecedor (OCR de Pagamento)**: `POST /api/v1/lookups/suppliers/from-payment-ocr` cria fornecedor **apenas DRAFT** (Origin=PAYMENT_OCR; nunca PrimaveraCode/ativação/aprovação). `ISupplierCreationService` é a fonte única de matching autoritativo (NIF normalizado → nome normalizado → inativo → duplicidade provável) + geração de PortalCode concorrência-segura + auditoria. Endpoint geral de admin refatorado sobre o mesmo serviço, preservando o comportamento.
- **`Company.TaxId` (Dados Mestres → Empresas)**: NIF fiscal das empresas internas, normalizado, com índice único filtrado (migration `AddCompanyTaxId`; seed por `Code` — APA=5417567485, APS=5001760246). Tela de Empresas ganhou coluna e campo NIF com normalização, unicidade (409 estruturado) e validação inline.
- **Bloqueio autoritativo de NIF interno + fluxo de decisão do modal**: o serviço bloqueia qualquer NIF que pertença a uma empresa interna (`INTERNAL_COMPANY_TAX_ID`) no match e na criação. O modal contextual, ao receber o bloqueio, descarta o NIF interno, refaz o matching **apenas por nome** e apresenta um fluxo de decisão claro: **usar fornecedor cadastrado** (recomendado; card com estado ativo/inativo, portal/Primavera), **não é este** → alternativas (buscar outro / criar sem NIF / voltar / cancelar), e criação **sem NIF** apenas com confirmação explícita quando existe nome semelhante.
- **Endurecimento de auditoria (server-side)**: os metadados de proveniência enviados pelo cliente (NIF interno descartado, `RejectedSuggestedSupplierId`) são **validados/resolvidos no backend** — o NIF interno é confirmado contra `Company`, o fornecedor recusado é confirmado como candidato plausível do nome, e o histórico usa apenas nomes/IDs resolvidos do banco. Reivindicações falsas são ignoradas (sem auditoria fabricada).
- **Testes**: novos testes de unidade e integração (SQL Server) para matching/normalização, exclusão de NIF interno, `TaxIdNormalizer`, resolução de auditoria e validadores. Migrations aditivas validadas (apply/seed/índice/rollback/reapply).

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.206.0] - 2026-07-16

### Removed — Limpeza do Modelo Legado de Aprovação de Área (Fase C do Redesign)

- **`Department.ResponsibleUserId` removido** do modelo, DTOs, controllers e do banco (migration `PhaseCRemoveLegacyAreaApprovalConfig`: drop de FK/índice/coluna, com snapshot de auditoria `_PhaseC_DepartmentResponsibleBackup`). `DepartmentManagers` é a única configuração de responsabilidade de área.
- **Atribuições manuais da role "Area Approver" removidas** do banco (com snapshot `_PhaseC_AreaApproverManualAssignmentsBackup`; `Down()` restaura). A linha da role permanece em `Roles` — ela existe exclusivamente como **claim derivada** no login. A API **rejeita** (400 controlado) qualquer tentativa de atribuição manual, inclusive por System Administrator, e a role saiu das assignable-roles e do checkbox da UI.
- **HR migrado**: os 10 pontos de `HRLeaveController`/`HRAttendanceController`/`HRScheduleController` que identificavam o "Department Manager" por `ResponsibleUserId` (aprovação de férias, escalas, calendários) e o `managedDepartmentIds` do login agora derivam de `DepartmentManagers` (nível de departamento — planta específica ou global contam). Local Manager permanece 100% separado (administração de usuários).
- **Contratos migrados**: `TechnicalApproverId` resolvido no submit via `IApprovalRoutingService` (cascata dept+planta do contrato; primeiro manager do nível resolvido). Os gates de leitura de contratos continuam pela claim derivada — documentado que contratos usam claim genérica, não titularidade por pedido.
- **Cadastro de usuários**: nova seção somente leitura "Responsabilidades de Aprovação (derivadas)" no drawer (dept — planta/Global, ativo/inativo), com orientação "gerido em Dados Mestres → Departamentos"; hydration do formulário filtra o id da role derivada como defesa contra resíduos.
- **Dados Mestres → Departamentos**: campo "Responsável (Legado)" totalmente removido; a grade de managers é a única configuração; lista mostra a contagem de managers ativos (`managerCount`).
- **Compatibilidade legada explicitada**: cláusula de nomeado antigo isolada no método `IsLegacyNamedAreaApprover` (restrito a `WAITING_AREA_APPROVAL`/`WAITING_COST_CENTER`; pedidos pós-corte nunca se beneficiam, pois chegam à etapa com `AreaApproverId` null). O relatório de reconciliação ganhou a lista `LegacyPendingRequests` — quando vazia em PROD, a cláusula pode ser removida. Em Development (16/07/2026): **0 dependentes**.
- **`Request.AreaApproverId` intacto** (histórico/decisor); pedidos concluídos preservados (verificado: 25 registros históricos íntegros pós-migration em DEV).
- Pré-check registrado em DEV antes da migration: OK_DERIVADO 2 · PERDE_ACESSO 2 (Departamento Administracao, Manager Manual) · SO_CADASTRO 3 · 4 atribuições manuais removidas · 3 departamentos ativos ainda sem manager (Admin, Financeiro, Logística — submits bloqueiam até cadastro).

**Guided Tour impact: existing tour reviewed, no changes needed.**

### Changed — Corte Definitivo: Aprovação de Área por DepartmentManager (Fase B do Redesign)

- **DepartmentManager é a fonte única de verdade** para a aprovação de área — sem feature flag, sem fluxo paralelo. `Department.ResponsibleUserId` deixou de participar de qualquer pedido/evento novo (a coluna permanece até a Fase C; módulo HR e contratos não foram alterados).
- **Claim "Area Approver" derivada**: no login, a role é concedida exclusivamente a quem tem ≥1 linha ativa em `DepartmentManagers` (usuário ativo, planta ativa). A atribuição manual da role é ignorada na montagem das claims — deixa de dar fila, aprovação e e-mails. Mudanças de manager exigem novo login/renovação de token. O checkbox manual permanece na UI até a Fase C.
- **Submit**: não pré-nomeia aprovador. Exige ≥1 manager resolvível para (departamento, planta); sem manager → 400 com mensagem acionável + AdminLog `APPROVAL_ROUTING_NO_MANAGER`, sem alteração parcial de status. Mesmo guard na criação de lote de aprovação (entrada da cotação na etapa de área).
- **`Request.AreaApproverId` muda de semântica**: de "nomeado" para **"quem decidiu"** — null até a decisão; gravado com o ator ao aprovar/rejeitar/pedir reajuste (individual e em lote, somente após autorização).
- **Fila e contadores** (pendências, my-tasks, not-quoted): visibilidade por subquery EF em `DepartmentManagers` (manager da planta OU global vê; manager de outra planta não; role manual isolada não) + cláusula legada `AreaApproverId == usuário` para pedidos antigos em andamento + admin.
- **Autorização de decisão** (aprovar/rejeitar/reajuste, lote, seleção de vencedor, centro de custo, not-quoted): admin OU manager do departamento/planta (D1: específico ou global; outra planta nunca) OU nomeado legado. 403 claro: "Você não é responsável pelo departamento/planta deste pedido". Concorrência entre managers: segundo a decidir recebe **409** "Este pedido já foi decidido por {nome}".
- **E-mails**: `[AÇÃO NECESSÁRIA]` vai a **todos** os managers do nível resolvido pela cascata estrita (específicos da planta; senão globais; nunca o legado). Zero destinatários → AdminLog `APPROVAL_EMAIL_NO_RECIPIENT`. Informativos de pagamento e alertas de proforma corrigidos para departamento+planta (fim do vazamento entre plantas). Outbox/dedup/retry/DEAD_LETTER intactos.
- **Exibição**: pedidos pendentes sem decisor mostram "Pendente — N responsáveis: nomes" (campo novo `eligibleAreaManagerNames` no detalhe); após a decisão, o nome do decisor real. Pedidos antigos nomeados exibem o nome legado.
- **Frontend**: auto-fill e envio de `areaApproverId` removidos da criação/edição; campo removido dos DTOs de draft; Dados Mestres marca o campo "Responsável" como legado sem efeito.
- **Relatório de reconciliação**: resposta JSON ganhou `phaseNote` explicitando que `PERDE_ACESSO` já está sem acesso funcional a pedidos novos.
- **Compatibilidade preservada** apenas para pedidos antigos em andamento (nomeado vê/decide; histórico intacto; pedido sem planta resolve por managers globais).
- **Testes**: 26 testes na área de Approvals (cascata sem fallback legado, assimetria D1 e-mail×autorização, claim derivada via login real com Moq, D3, D2, planta inativa excluída).

**Guided Tour impact: existing tour reviewed, no changes needed.**

### Added — Department Managers por Planta (Fase A do Redesign de Aprovação de Área)

- **Nova tabela `DepartmentManagers`**: responsáveis de aprovação de área por Departamento + Planta (`PlantId NULL` = manager global). Unique composto por (departamento, planta, usuário) e índices para resolução e fila. Seed idempotente na migration: cada `Department.ResponsibleUserId` existente vira manager global — comportamento atual preservado.
- **`IApprovalRoutingService`** (`ApprovalRoutingService`): resolução em cascata (planta específica → global → responsável legado) com regra D1 — `ResolveAreaManagersAsync` (e-mail) estrito; `IsAreaManagerAsync` (autorização) inclusivo (planta OU global; outra planta nunca). **Ainda não conectado ao workflow** — submit, fila, aprovação e e-mails seguem no caminho legado até a Fase B.
- **CRUD de managers** em `Lookups → departments/{id}/managers` com regra D3: ao adicionar um manager, os escopos de visibilidade ausentes (`UserDepartmentScope` + `UserPlantScope`; todas as plantas ativas para manager global) são criados na mesma transação e devolvidos no response. Remoção/desativação nunca remove escopos.
- **Relatório de reconciliação** `GET /api/admin/reports/area-approver-reconciliation` (JSON/CSV): classifica usuários em `OK_DERIVADO`, `PERDE_ACESSO`, `SO_CADASTRO`, `INATIVO_COM_VINCULO`, `INCONSISTENTE` — pré-requisito da Fase C (D2).
- **Dados Mestres → Departamentos**: nova grade "Managers de Aprovação de Área (por Planta)" no editor do departamento, com aviso prévio dos escopos que serão auto-criados e confirmação posterior. Campo "Responsável" mantido e rotulado como **Legado**.
- **Testes**: 21 testes unitários novos (cascata, assimetria D1, filtros de inativo/sem e-mail, D3, reativação sem violar unique, classificações D2).
- **Sem mudança de comportamento em produção**: nenhum fluxo de aprovação, fila, submit ou e-mail foi alterado nesta fase.

**Guided Tour impact: existing tour reviewed, no changes needed.**

### Added — Prazo Mínimo por Grau de Necessidade (Criação de Pedido)

- **Regra de Prazo Mínimo**: O campo "Necessário até" (`NeedByDateUtc`) passa a respeitar um prazo mínimo derivado do "Grau de Necessidade" (`NeedLevelId`) na criação de pedidos de **Cotação**: `CRITICO` → hoje, `URGENTE` → hoje + 1 dia, `NORMAL` → hoje + 7 dias, `BAIXO` → hoje + 15 dias.
- **Preenchimento Automático**: Ao selecionar o grau, a data é preenchida automaticamente quando vazia e empurrada para a frente quando anterior ao mínimo (com aviso discreto). Datas posteriores ao mínimo são sempre preservadas.
- **Bloqueio no Date Picker**: O `DateInput` passa a propagar `min`/`max` para o seletor nativo de data, desabilitando datas anteriores ao prazo mínimo no calendário.
- **Validação Server-Side**: `POST /requests` rejeita (400) datas anteriores ao prazo mínimo do grau, impedindo bypass via API. Regra centralizada em `RequestConstants.NeedLevels` e espelhada no frontend em `lib/needByDate.ts`.
- **Escopo**: Pedidos de **Pagamento** são isentos — o mesmo campo carrega a data de vencimento da fatura do fornecedor, que legitimamente pode estar no passado. A edição de rascunhos existentes permanece inalterada (o mínimo é relativo a "hoje" e invalidaria retroativamente rascunhos antigos válidos).

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.205.0] - 2026-07-14

### Added — Approval Batch Wizard & CC Rateio

- **Approval Batch Wizard**: Implementação completa de assistente de aprovação em lote para pedidos de compra com etapas de overview, comparação de cotações, rateio por item, verificação orçamentária e adjudicação.
- **Rateio por Item**: Suporte para divisão percentual ou nominal de custos de itens individuais entre múltiplos Centros de Custo (`RequestLineItemAllocation`).
- **Agrupamento de P.O**: Agrupamento automático de pedidos aprovados sob a mesma P.O física (`RequestPoGroup`).
- **OCR Conciliação**: Persistência de dados OCR extraídos em faturas fiscais para auditoria (`OcrDataRaw`).
- **Visão Diária**: Adição da resolução diária ("Dias") no gráfico "Contexto Financeiro Visual" do drawer de aprovação.

### Added — IT Equipment Return & New Delivery Enforcement

- **Legacy Transfer Removal**: Remoção da ação de transferência rápida de equipamentos no drawer do comprador, forçando o fluxo completo de devolução e emissão de novo Termo.
- **Form Validation**: Validação inline contextual de e-mails corporativos no fluxo de termos.
- **PDF Layout**: Otimização do layout do PDF de Termo de Devolução para caber em página única.

**Guided Tour impact: existing tour reviewed, no changes needed.**
## [v2.204.1] - 2026-06-30

### Fixed — IT Equipment Path Resolution & Email Dispatch Resiliency

- **Robust Path Resolution**: Replaced fragile `..\..\..\` directory traversal in the IT Equipment module with `PathResolutionHelper`. Path references now utilize `appsettings.json` explicitly (falling back to IIS `ContentRootPath`), ensuring document generation and file saving work deterministically in deployed IIS environments.
- **Email Attachment Hardening**: `EmailService` and controllers handling Delivery Terms, Return Terms, and Assignments now specifically catch `FileNotFoundException`. Missing required PDFs gracefully abort workflows with a structured `ILogger` telemetry error, rather than silently sending blank emails and permitting assignments to persist without legal coverage.

**Guided Tour impact: not applicable.**

## [v2.204.0] - 2026-06-30

### Fixed — Advance Payment & Receiving Workflow 

- **Advance Payment Support**: Updated `FinancePaymentsList` to correctly display and filter requests with `ADVANCE_PAYMENT_REQUIRED` status. Unblocked the receiving pipeline (`LineItemsController`, `RequestsController`, `AttachmentsController`) to accept requests in `WAITING_SUPPLIER_DELIVERY` status, enabling payment proof upload and receiving continuation.
- **Receiving Workspace Layout**: Fixed horizontal misalignment in the Receiving Operation screen header by replacing `PageContainer` with a localized wrapper (`motion.div`) matching `RequestEdit.tsx` constraints.
- **Receipt Validation Modal**: Replaced the generic approval modal in `ReceivingOperation` and `RequestEdit` with a new, dedicated `FinalizeReceivingModal`. The modal strictly enforces receipt upload (or skip for services) before allowing a request to transition to `COMPLETED`.
- **Floating Badges Cleanup**: Standardized floating notification badges (`PendingApprovalsSticker`, `PendingReceivingSticker`) by moving them out of `AppShell`'s absolute positioning into a unified `StickerContainer` for proper stacking and z-index management.

**Guided Tour impact: not applicable.**

## [v2.203.1] - 2026-06-30

### Fixed — Finance List Component Crashes

- **DTO Alignment**: Updated `FinanceListItemDto` in the frontend to include `paymentCondition` and `advancePaymentPercent` to correctly match the C# API response, resolving TypeScript compilation errors.
- **Data Access Fix**: Replaced obsolete nested references (`i.request.id`, `i.request.status?.code`) with flat DTO property access (`i.id`, `i.statusCode`) in the Finance Payments list and action modal logic to prevent runtime reference errors. Corrected list path from `data?.items` to `data?.pagedResult?.items`.
- **Hotfix**: Resolved a duplicate `APP_VERSION` declaration in `config.ts` caused by a GitHub merge conflict.

**Guided Tour impact: not applicable.**

## [v2.203.0] - 2026-06-30

### Added — IT Module UI Alignment & Settings Migration (Phase 3D)

- **UI Realignment**: Synchronized the Catalog (`/it/catalogs`) and Equipment Type (`/it/types`) settings screens with the modern portal standards (`WizardLayout`, `StandardTable`, `KebabMenu`, `SearchFilterBar`). Replaced the legacy modal-based management.
- **Guided Tours**: Created dedicated page-level guided tours for the new IT Catalogs and IT Types screens. Added tab anchor hooks (`data-tour="it-module-tabs"`) to `ITLandingPage.tsx` to properly guide users across the module.
- **Bug Fixes**: Resolved dead code issues in `ModelWizardPage.tsx` and ensured strict TypeScript compilation (`tsc --noEmit`).

## [v2.202.0] - 2026-06-26

### Added — IT Equipment Purchase Traceability

- **Purchase Information**: Added support for tracking purchase value, date, and document reference (invoice number) for IT Equipment.
- **Unavailable State Handling**: Implemented explicit tracking of missing/legacy purchase data using `PurchaseInfoUnavailable` and `PurchaseInfoUnavailableReason` fields to maintain data integrity without blocking migrations.
- **PDF Responsibilities Term Update**: Restructured the "Termo de Responsabilidade" PDF table to support 10 columns using a compact 6.5pt font layout. The PDF now accurately displays equipment values, purchase dates, and purchase document numbers, explicitly rendering "Indisponível" for legacy records without values.
- **Form UI Update**: Added an always-visible "Compra / Rastreabilidade" section to the Equipment Form modal with validation for mandatory purchase fields or justified absence.

**Guided Tour impact: existing tour reviewed, no changes needed.**

## [v2.201.1] - 2026-06-26

### Fixed — HR Sync Logging & Robustness

- **Graceful Failure**: Refined the HR Employee Directory Synchronization to handle SQL connection timeouts gracefully without crashing the frontend. Added `EXTERNAL_DB_TIMEOUT` structured backend error. 
- **Shared Correlation ID**: The frontend now generates a shared `X-Correlation-ID` for the full synchronization operation (departments + employees), logged consistently via `AdminLogWriter` across events (`HR_SYNC_STARTED`, `HR_SYNC_SUCCESS`, `HR_SYNC_PARTIAL`, `HR_SYNC_FAILED`, `DEPT_SYNC_*`).
- **Partial Sync Handling**: Replaced hard crash on unprocessable records with skip-and-continue logic. Emits `HR_SYNC_PARTIAL` when some records are skipped, displaying the count in the UI. Skipped records log `EmployeeCode` and `Reason` via AdminLog.

**Guided Tour impact: not applicable.**

## [v2.201.0] - 2026-06-25

### Fixed — Request Creation Performance (Email Outbox)

- **Email Outbox + Background Processor**: Decoupled synchronous SMTP email sending from the request creation lifecycle. Emails are now queued to an `EmailOutbox` table and processed asynchronously by a `BackgroundService`, reducing `POST /api/v1/requests` response time from ~10 seconds to < 500ms.
- **Atomic Concurrency Safety**: Processor uses `UPDATE TOP(N)...OUTPUT INSERTED.*` SQL pattern for race-condition-proof row claiming, safe for multi-instance deployments.
- **Crash Recovery**: Auto-recovers entries stuck in `PROCESSING` status for >5 minutes after application restarts.
- **Deduplication**: Three-layer protection — insert-time code check, unique filtered DB index (`IX_EmailOutbox_Correlation_Recipient_Active`), and send-time verification.
- **Retry + Dead-Letter**: Exponential backoff retry with configurable max attempts. Failed entries marked `DEAD_LETTER` after exhaustion.
- **AdminLog Audit Trail**: Full lifecycle logging via `AdminLogWriter` — `QUEUED`, `SENT`, `RETRY_SCHEDULED`, `DEAD_LETTER`, `DEDUP`, `STUCK_RECOVERED`.

### Fixed — HR Badge Reprint Blank Output

- **Blank Reprint Fix**: Resolved CSS conflict where `body * { visibility: hidden !important }` from `employee-workspace.css` was overriding the reprint preview's `display: block`. Changed reprint preview to use `.hr-badge-print-area` class, aligning with the existing visibility-based print pattern.
- **Print CSS Rewrite**: Rewrote `badge-print-history.css` `@media print` rules to use `visibility`-based approach instead of conflicting `display: none` rules.

### Added — Editable Card Number Before Badge Reprint

- **Card Number Field**: Added required "Número do Cartão" editable field in the Reimprimir Crachá modal, pre-populated from the original print snapshot.
- **Live Preview**: Card number changes immediately reflected in the badge preview before printing.
- **Per-Event Storage**: New `BadgePrintEvent.CardNumberUsed` column stores the card number used in each specific reprint without modifying the original `BadgePrintHistory.CardNumber`.
- **AdminLog Audit Entry**: Each reprint writes a `BADGE_REPRINT` event to `AdminLogEntries` with full JSON payload (employee code/name, previous/new card number, reason, user, timestamp, status).

**Guided Tour impact: not applicable.**

**Files Created:**
- `src/backend/AlplaPortal.Domain/Entities/EmailOutboxEntry.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/EmailOutboxProcessor.cs`
- EF Migrations (`AddEmailOutbox`, `AddBadgePrintEventCardNumber`)


## [v2.200.0] - 2026-06-25

### Added — Buy2Pay Foundation & Purchasing Workflow Enhancements

- **Buy2Pay (B2P) Core**: Introduced reconciliation UI, payment tracking logic, and DB models (`RequestPayment`, `RequestReconciliation`).
- **OCR Module Configuration**: Migrated OCR whitelist from hardcoded appsettings to a secure-by-default DB table (`OcrModuleConfig`), including Admin API and Settings UI.
- **Buyer P.O. Creation Email**: Added a dedicated, idempotent email workflow for Buyers when a request enters the final approved stage, providing full operational data for PRIMAVERA P.O. creation.
- **P.O. Payment Condition Control**: Removed silent POST_PAID default; enforced explicit Buyer selection with OCR auto-detection. Persists detection source (`PaymentConditionSource`) for auditability.
- **Duplicate Document UX Safety**: Added a 5-second countdown safety delay to the confirmation button on duplicate document warning modals to prevent instinctive overrides.

**Guided Tour impact: existing tour reviewed, no changes needed.**

**Files Created:**
- `src/backend/AlplaPortal.Domain/Entities/OcrModuleConfig.cs`
- `src/backend/AlplaPortal.Domain/Entities/RequestPayment.cs`
- `src/backend/AlplaPortal.Domain/Entities/RequestReconciliation.cs`
- `src/frontend/src/components/ui/ReconciliationModal.tsx`
- EF Migrations (`AddB2PImplementation`, `AddOcrModuleConfig`, `AddPaymentConditionSource`)

## [v2.199.0] - 2026-06-23

### Added — Accounts Payable Email Notification System

- **AP Notification Configuration**: Created a dedicated `AccountsPayableNotificationConfigs` table and Master Data UI panel ("📧 E-mails Contas a Pagar") for managing per-company Accounts Payable email notification settings.
- **AP Notification Logging**: Created `AccountsPayableNotificationLogs` table with filtered unique index for duplicate prevention (`RequestId + EventCode + RecipientEmail WHERE Success=1 AND Skipped=0`).
- **Workflow Integration**: `PAYMENT_SCHEDULED` and `PAYMENT_COMPLETED` events now trigger automatic email notifications to the configured AP email address. `CompanyId` added to workflow event payloads for company-specific routing.
- **CC Support**: AP configurations support optional CC email addresses (semicolon-separated `CcEmails` field). CC is handled as real CC via the email service, not as separate emails.
- **Non-Blocking Failures**: AP email send failures do not block the payment workflow. Failures are logged in the notification log table.
- **Environment Policy**: `ApplyEnvironmentPolicy` now clears both `To` and `CC` recipients in non-production environments, preventing accidental emails to external AP mailboxes from TEST.
- **Frontend CRUD**: Full create/edit/delete interface in Master Data with validation, company dropdown, toggle controls for `NotifyOnScheduled` and `NotifyOnCompleted`, and inline CC email display.
- **Migration**: `20260623154314_AddAccountsPayableNotifications` — 2 tables, 5 indexes, 1 FK.

**Guided Tour impact: not applicable.**

**Files Created:**
- `src/backend/AlplaPortal.Api/Controllers/AccountsPayableConfigController.cs` — CRUD API
- `src/backend/AlplaPortal.Domain/Entities/AccountsPayableNotificationLog.cs` — Log entity
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260623154314_AddAccountsPayableNotifications.cs` — Migration
- `src/frontend/src/pages/Settings/ApNotificationsPanel.tsx` — AP config panel

**Files Modified:**
- `src/backend/AlplaPortal.Api/Controllers/FinanceController.cs` — CompanyId in workflow payloads
- `src/backend/AlplaPortal.Application/Interfaces/IEmailService.cs` — CC parameter
- `src/backend/AlplaPortal.Domain/Entities/Organization.cs` — AP config entity
- `src/backend/AlplaPortal.Domain/Events/WorkflowEvent.cs` — CompanyId property
- `src/backend/AlplaPortal.Infrastructure/Data/ApplicationDbContext.cs` — DbSets + model config
- `src/backend/AlplaPortal.Infrastructure/Services/EmailService.cs` — CC support + environment policy
- `src/backend/AlplaPortal.Infrastructure/Services/WorkflowNotificationOrchestrator.cs` — AP notification logic
- `src/frontend/src/lib/api.ts` — AP config API methods
- `src/frontend/src/pages/Settings/MasterData.tsx` — AP notifications tab
- `src/frontend/src/config.ts` — APP_VERSION → "v2.199.0"
- `docs/VERSION.md` — v2.199.0
- `docs/CHANGELOG.md` — This entry

## [v2.198.0] - 2026-06-22

### Added — User Onboarding Email Flow

- **Secure Password Setup**: Added an onboarding email flow for newly created users containing a secure token link instead of transmitting plain-text passwords.
- **Branding Correction**: Corrected email footer text to "ALPLA Angola".
- **UI Notifications**: Replaced browser `alert()` popups with consistent custom `toast` notifications in User Management.

**Guided Tour impact: not applicable.**

## [v2.197.0] - 2026-06-22

### Added — Request Field-Level Audit Trail & Edit Permissions

- **Field-Level Audit Trail**: Added automatic tracking of individual field changes during Request modifications. A new `Histórico do Pedido` section now displays old vs. new values for tracked fields (e.g., Description, Department, Need Level, Dates).
- **Edit Permissions Enforcement**: Restricted Request modification (Editing) strictly to the original Requester (Creator). Other roles can no longer edit a request, except for specific workflow transitions.

### Fixed — IVA Partial Save Bug & Global Layout Overlapping

- **IVA Partial Save Bug**: Fixed an issue where IVA percentages were not persisting correctly during partial request saves by ensuring backend entity propagation.
- **Global Layout Constraints**: Addressed overlapping UI elements globally.
  - Stabilized `--header-height` to `64px` and removed destructive `overflow: hidden` on Topbar to fix dropdown clipping.
  - Introduced `--env-banner-offset` to smartly adapt sticky headers across environments (TEST vs PROD), correcting sticky positioning on `RequestActionHeader`, `ApprovalCenter`, `BuyerItemsList`, `OperationsTransfersPage`, `MasterData`, and `CatalogItemsPanel`.

## [v2.196.0] - 2026-06-18
### Added — AI OCR Technical Hardening & Compliance Package

**Security & Compliance Hardening:**
- **Debug Logging Guard (G1):** Implemented dual-guard `IsDebugLoggingAllowed()` requiring both `IsDevelopment()` and explicit `DebugRawPayloadLogging` flag to prevent raw AI payload leakage to disk.
- **Policy Controls (G2):** Enforced module (`CONTRACTS`, `REQUESTS`) and document type allowlists to restrict AI extraction to authorized contexts.
- **Prompt Injection Defense (G3):** Injected security preamble to both invoice and contract system prompts to mitigate instruction overrides.
- **Retention Controls (G4):** Created `OcrCleanupService.cs` background service for managing data retention.
- **Malware Scanning (G5):** Added `IFileScanService` extension point and `NoOpFileScanService` placeholder.
- **Provider Readiness (G6):** Made `OpenAiDocumentExtractionProvider` endpoint configurable to support switching to Azure Document Intelligence.
- **System Logs Integration (G8):** Integrated 8 `OCR_*` events (`OCR_EXTRACTION_STARTED`, `OCR_MODULE_BLOCKED`, etc.) into the structured `AdminLogWriter` with `SafePayload` sanitization.

**Compliance Documentation & Evidence:**
- Updated 8 core compliance documents (v2.0) reflecting the post-hardening state.
- Generated a comprehensive 48-file evidence package under `docs/ai-ocr/evidence/` including redacted configurations, sanitized log samples, SQL verification queries, and build results.

**Guided Tour impact: not applicable.**

## [v2.195.2] - 2026-06-17

### Fixed — Global Frontend Responsive Audit & Layout Constraints

**Problem:** 
The Portal Gerencial frontend had structural layout issues that caused horizontal clipping on standard laptop resolutions (e.g. 1366x768 and 1440x900) at 100% browser zoom. Some pages expanded beyond the viewport, forcing users to manually zoom out, especially when the sidebar was expanded.

**Fixes Applied:**
- **Global Constraints:** Added `overflow-x: hidden` to the HTML tag and set global `max-width: 100%` rules for tables. Added media queries to automatically collapse the sidebar at viewport widths ≤1366px.
- **AppShell & PageContainer:** Added `overflowX: 'hidden'`, `maxWidth: '100%'`, and `minWidth: 0` to main content containers (`<motion.main>`, `PageContainer`) to prevent child elements from forcing a layout blowout.
- **Topbar & Header:** Refactored Topbar layout from fixed widths to flexible `min-width` and `flex-shrink` to prevent overlapping or clipping.
- **Specific Pages:** Modified `RequestsDashboard.tsx` root `div` to include `width: '100%'` and `minWidth: 0`, fixing a specific clipping bug where the "Tour da Tela" and action buttons were pushed off-screen at 1440x900 with an expanded sidebar.
- **Grid Auto-fit:** Adjusted CSS Grid columns in Dashboard, Finance, Settings, and Purchasing pages to use `repeat(auto-fit, minmax(...))` instead of fixed fractional tracks, ensuring cards wrap safely on smaller displays.

**Guided Tour impact: not applicable.**

## [v2.195.1] - 2026-06-17
### Fixed — UX Bugfixes & Responsive Layout Audit

**IT Equipment Fixes:**
- Fixed column overlapping in Delivery/Return PDF equipment tables and renamed "Asset Tag" to "Código do Ativo".
- Resolved a `401 Unauthorized` error when downloading Return Documents by updating the frontend to use an authenticated API call instead of a direct browser link.

**Global Responsive Layout Audit:**
- Replaced rigid grid structures (fixed fractions) with flexible `repeat(auto-fit)` configurations across all major pages (Dashboard, Requests, Finance, Purchasing, Settings).
- Added global structural flex constraints (`min-width: 0`, `overflow-x: hidden`) and global table containment to prevent horizontal view overflow.
- Implemented an auto-collapsing sidebar behavior for standard laptop resolutions (≤1366px viewport width) to maximize content space without requiring global zoom.

## [v2.195.0] - 2026-06-16

### Added — IT Equipment Return Term Generation

**Auto-Generate Return Term:**
- When the last item of a Delivery Term is returned (status changes to `CLOSED`), the system automatically generates a branded Return Term PDF.
- The return document is linked to the original Delivery Term via a new `ReturnDocumentId` field.
- The PDF contains an electronic generation statement and an empty signature area for the user.
- An email is automatically dispatched to the IT Department with the Return Term PDF attached.

**Signed Return Document Upload:**
- Added the ability to upload a manually signed Return Document.
- Upload is available directly from the Delivery Terms page and the Equipment Quick-View drawer.
- Shows visual indicators for generated (blue) vs. signed (green) return documents.

**Quick-View Drawer UX:**
- Fixed a z-index issue that caused the drawer to appear behind the top header and TEST environment banner.
- Removed the direct "Atribuir" and "Devolver" buttons to enforce the Delivery Terms workflow.

**Guided Tour impact: existing tour reviewed, no changes needed.**

**Files Changed:**
- `src/backend/.../Entities/ITEquipmentDeliveryTerm.cs`
- `src/backend/.../Constants/ITEquipmentConstants.cs`
- `src/backend/.../Services/ITEquipmentPdfService.cs`
- `src/backend/.../Controllers/ITDeliveryTermsController.cs`
- `src/backend/.../Data/Migrations/20260616123327_AddReturnDocumentToDeliveryTerm.cs`
- `src/frontend/src/types/itEquipment.ts`
- `src/frontend/src/lib/itEquipmentApi.ts`
- `src/frontend/src/pages/IT/DeliveryTermsPage.tsx`
- `src/frontend/src/components/it/EquipmentQuickViewDrawer.tsx`
- `src/frontend/src/config.ts` — APP_VERSION → v2.195.0
- `docs/VERSION.md` — v2.195.0
- `docs/CHANGELOG.md` — This entry

## [v2.194.0] - 2026-06-15

### Added — IT Equipment Refinements (Manufacture Date & MAC Split)

**Refinements:**
- Changed Laptop ShortCode from `NBK` to `LAP` for Asset Code generation
- Added `ManufactureDate` field for lifecycle tracking
- Split `MacAddress` field into `MacAddress` (Ethernet) and `WifiMacAddress` (Wi-Fi)
- Migration: `20260615142951_ITEquipmentRefinements` applied


## [v2.193.0] - 2026-06-15

### Added — IT Asset Code Auto-Generation, QR Code & Label Printing

**Automatic Asset Code Generation:**
- New `ITAssetCodeGeneratorService` generates unique asset codes on equipment creation using format: `{COMPANY_CODE}-{PLANT_CODE}-IT-{TYPE_SHORT_CODE}-{SEQUENCE:D6}`
- Example: `APA-AOVIA1-IT-NBK-000001`
- Sequence counters scoped per Company + Plant + Equipment Type via `SystemCounters` table
- New database fields: `ITEquipmentType.ShortCode`, `Organization.CompanyCode`, `ITEquipment.LegacyAssetCode`
- `AssetTag` repurposed as the official auto-generated Asset Code (read-only in UI, displayed as "Código do Ativo")
- Migration: `20260615104001_AddITAssetCodeAutoGeneration`

**Visual QR Code in Equipment Detail:**
- QR Code rendered in the equipment detail drawer using `qrcode.react` (`QRCodeSVG`)
- Shows asset code below QR and clickable URL link
- Action buttons: Abrir Ficha (open URL), Imprimir Etiqueta (print label), Copiar Link (copy URL)
- Relative URL warning badge when `FrontendBaseUrl` is not configured

**Printable Asset Label (70mm × 35mm):**
- New route `/it/equipment/:id/label` renders a print-ready label
- Layout: QR Code (left) + asset info (right): ALPLA ANGOLA, Asset Code, Type, S/N, Model, Plant, Company
- `@media print` CSS hides all app chrome, sets `@page` size to 70×35mm
- Screen preview with "Imprimir Etiqueta" button

**Deep Link & Authentication Flow:**
- New route `/it/equipment/:id` opens IT Equipment page with detail drawer auto-opened
- `ProtectedRoute` captures original URL before login redirect; after auth, user is returned to the original page
- Safety: only internal relative paths accepted as return URLs (no open redirects)
- New `NotFoundPage` with catch-all `*` route

**Config Consolidation — `PortalBaseUrl` eliminated:**
- Replaced `AppConfig:PortalBaseUrl` with the existing `AppConfig:FrontendBaseUrl` in `ITAssetCodeGeneratorService` and `WorkflowNotificationOrchestrator`
- QR Code URLs, email CTA buttons, and notification links now all use the same config key
- No additional config changes needed on TEST or PROD servers

**Operational Script:**
- `scripts/maintenance/ResetITEquipmentData.sql` — controlled purge of IT operational data preserving all master data

**Guided Tour impact: existing tour reviewed, no changes needed.**

**Files Created:**
- `src/backend/.../Migrations/20260615104001_AddITAssetCodeAutoGeneration.cs`
- `src/backend/.../Migrations/20260615104001_AddITAssetCodeAutoGeneration.Designer.cs`
- `src/backend/.../Services/ITAssetCodeGeneratorService.cs`
- `src/backend/scripts/maintenance/ResetITEquipmentData.sql`
- `src/frontend/src/pages/IT/ITEquipmentLabelPage.tsx`
- `src/frontend/src/pages/NotFoundPage.tsx`

**Files Modified:**
- `src/backend/.../Controllers/ITEquipmentController.cs`
- `src/backend/.../Controllers/ITDeliveryTermsController.cs`
- `src/backend/.../Program.cs`
- `src/backend/.../Entities/ITEquipment.cs`
- `src/backend/.../Entities/ITEquipmentType.cs`
- `src/backend/.../Entities/Organization.cs`
- `src/backend/.../Data/ApplicationDbContext.cs`
- `src/backend/.../Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `src/backend/.../Services/WorkflowNotificationOrchestrator.cs`
- `src/frontend/src/App.tsx`
- `src/frontend/src/features/auth/AuthContext.tsx`
- `src/frontend/src/components/it/EquipmentQuickViewDrawer.tsx`
- `src/frontend/src/components/it/EquipmentFormModal.tsx`
- `src/frontend/src/components/it/EquipmentTable.tsx`
- `src/frontend/src/pages/IT/ITEquipmentPage.tsx`
- `src/frontend/src/pages/IT/DeliveryTermsPage.tsx`
- `src/frontend/src/lib/itEquipmentApi.ts`
- `src/frontend/src/types/itEquipment.ts`
- `src/frontend/package.json`
- `src/frontend/src/config.ts` — APP_VERSION → v2.193.0
- `docs/VERSION.md` — v2.193.0
- `docs/CHANGELOG.md` — This entry

## [v2.192.0] - 2026-06-12

### Added — IT Equipment Module Improvements (Phase 1, 2, and 3)

- **Phase 1: IT Equipment Lifecycle Improvements**: Added dynamic equipment types with prefixes, implemented reversible retirement flow, and added a detailed audit timeline for equipment items.
- **Phase 2: Delivery Terms (Termos de Entrega)**: Created a new entity `ITEquipmentDeliveryTerm` to group multiple IT equipment assignments for a single employee into a single, signable PDF document.
- **Phase 3: Master Data and Catalogs**: Replaced free-text equipment fields (Manufacturer, Model, Processor, Memory) with admin-managed catalogs. Connected Delivery Terms to Master Data for Company, Plant, and Department, with cascading UI dropdowns. Implemented denormalized save strategy for backward compatibility.
- **Guided Tours**: Added a new guided tour for the IT Equipment module.

## [v2.191.1] - 2026-06-12

### Fixed — Quotation Submission Notification Emission

**Problem:** The `SUBMISSION_CONFIRMED` email was not being sent for Quotation requests. The v2.191.0 mapping in `ResolveEventCode("SUBMIT", "WAITING_QUOTATION")` was correct but unreachable because Quotation requests are created directly in `WAITING_QUOTATION` status (skipping DRAFT), so they never pass through `SubmitRequest` → `ApplyStatusChangeAndSyncItemsAsync` where notifications are emitted.

**Fix:** Added notification emission directly in the `CreateRequestDraft` endpoint for Quotation requests, replicating the dual-event pattern (primary `QUOTATION_AWAITING_BUYER` + secondary `SUBMISSION_CONFIRMED`).

### Improved — Submission Confirmation Email Content

- The `SUBMISSION_CONFIRMED` email body now includes **Request Title** and **Description** in a styled "Dados do Pedido" details card.
- Fallbacks applied: `Sem título` for empty title, `Não informado` for empty description.
- Applies to all request types (Payment and Quotation) since the template is shared.

### Improved — Buyer Queue Email CTA Button

- The `QUOTATION_AWAITING_BUYER` email now includes an explicit **"Abrir Pedido no Portal →"** CTA button using the environment-aware `AppConfig:PortalBaseUrl`.
- Button links directly to the request detail page (`/requests/{id}?mode=view`).
- Uses the ALPLA blue (#002D72) visual style consistent with existing portal email buttons.

## [v2.191.0] - 2026-06-12

### Added — Quotation Email Notifications

Implemented three new email notification capabilities for the Quotation workflow:
- **Submission Confirmation**: Requesters now receive a "Confirmação de Submissão" email when a Quotation request is submitted (DRAFT → WAITING_QUOTATION).
- **Buyer Queue Alert**: Plant-scoped buyers now receive a `[AÇÃO NECESSÁRIA]` email when a new quotation request enters the queue, containing a rich summary of the request (Requester, Plant, Department, Value, Need-by date).
- **Assignment Confirmation**: When a buyer takes ownership of a quotation, the system now automatically emails both the buyer (confirming assignment) and the requester (informing them who their buyer is).

### Technical Changes
- Added two new constants in `WorkflowEventCodes.cs`: `QuotationAwaitingBuyer` and `BuyerAssigned`.
- Mapped `("SUBMIT", "WAITING_QUOTATION")` in `ResolveEventCode` to fix the previously silent transition.
- Implemented `AddPlantScopedBuyerRecipientsAsync` in `WorkflowNotificationOrchestrator` to replicate the safe plant-scoped routing used for Finance.
- Fired `_orchestrator.EmitAsync` natively inside the `/assign-buyer` endpoint.

## [v2.190.1] - 2026-06-11

### Fixed — Idempotent Migrations: Schema/History Desync Safe Handling

**Problem:** Development backend crashed on startup with `Column name 'Origin' in table 'Suppliers' is specified more than once` when EF Core tried to apply `20260610083347_AddSupplierSyncColumns`. The columns already existed physically (created by `ConsolidatedBaseline`) but the migration was not registered in `__EFMigrationsHistory`.

**Root cause:** Three migrations used `migrationBuilder.AddColumn<T>()` which generates unconditional `ALTER TABLE ADD` — no `IF NOT EXISTS` guard. Databases created via ConsolidatedBaseline already had the columns, but the migration history only tracked up to migration #50 (pre-June 2026).

**Fix:** Rewrote all three recent migrations to use idempotent raw SQL with `IF NOT EXISTS` checks:

| Migration | Objects Guarded |
|---|---|
| `20260610083347_AddSupplierSyncColumns` | 3 columns (`Origin`, `SourceCompany`, `LastSyncedAtUtc`) |
| `20260610134920_AddItemCatalogToQuotationItems` | 1 column + 1 index + 1 FK |
| `20260611114811_AddEmailEnvironmentIdentification` | 8 columns |

All 3 migrations now safely skip existing objects and only create missing ones.

**Impact on TEST and PROD:**
- **TEST:** `AddSupplierSyncColumns` was already applied (migration #51). `AddItemCatalogToQuotationItems` was applied (migration #52). Only `AddEmailEnvironmentIdentification` is pending — now safe with `IF NOT EXISTS`.
- **PROD:** Same as TEST. The idempotent pattern ensures no failure even if schema partially exists.

**Files Changed:**
- `src/backend/.../Migrations/20260610083347_AddSupplierSyncColumns.cs` — Idempotent `IF NOT EXISTS` for 3 columns
- `src/backend/.../Migrations/20260610134920_AddItemCatalogToQuotationItems.cs` — Idempotent `IF NOT EXISTS` for column, index, FK
- `src/backend/.../Migrations/20260611114811_AddEmailEnvironmentIdentification.cs` — Idempotent `IF NOT EXISTS` for 8 columns
- `src/frontend/src/config.ts` — APP_VERSION → "v2.190.1"
- `docs/CHANGELOG.md` — This entry

## [v2.190.0] - 2026-06-11

### Added — Email Environment Identification

**Feature:** Global email environment warning system to prevent users from confusing TEST/DEV emails with production emails.

**Behavior:**
- **Non-production (TEST/DEV):** Subject prefix (`[TEST - IGNORE]` / `[DEV - IGNORE]`) and body warning banner are applied **automatically** to every outgoing email, regardless of admin configuration.
- **PROD:** Email modification is disabled by default. Subject prefix and body banner are only applied if the admin explicitly enables them.
- **SMTP Test Email:** Gets an environment-prefixed subject (`[TEST - SMTP TEST]`) but no body banner or redirect.
- **Recipient Redirection:** In non-production, all emails can be redirected to a configured test recipient. Original recipients are optionally shown in the email body.
- **Safety Override:** An explicit `AllowRealRecipientsInNonProduction` flag must be enabled to bypass redirection.
- **Audit Logging:** Every email logs original recipient, final recipient, subject, environment code, and timestamp via `AdminLogWriter`.

**Admin UI:** New collapsible "Identificação de Ambiente de E-mail" section in Admin → Integrações → SMTP configuration, with:
- Subject prefix toggle and custom text
- Body warning banner toggle and custom text
- Recipient redirect toggle with test email input
- Show original recipients toggle
- Safety override toggle (red warning)

**Migration:** `20260611114811_AddEmailEnvironmentIdentification` — adds 8 columns to `dbo.SmtpSettings`.

**Files Changed:**
- `src/backend/.../Domain/Entities/SmtpSettings.cs` — 8 new entity properties
- `src/backend/.../Application/DTOs/SmtpSettingsDto.cs` — 8 new DTO fields
- `src/backend/.../Application/DTOs/Integration/IntegrationSettingsDtos.cs` — 8 new fields in GET and PUT DTOs
- `src/backend/.../Application/Interfaces/ISmtpSettingsService.cs` — SmtpEffectiveSettings extended
- `src/backend/.../Infrastructure/Services/SmtpSettingsService.cs` — Map new fields + env-prefix SMTP test email
- `src/backend/.../Infrastructure/Services/Integration/IntegrationSettingsService.cs` — Read/write 8 fields for SMTP provider
- `src/backend/.../Infrastructure/Services/EmailService.cs` — **Rewritten** with centralized `ApplyEnvironmentPolicy`
- `src/backend/.../Migrations/20260611114811_AddEmailEnvironmentIdentification.cs` — Schema migration
- `src/frontend/src/types/index.ts` — 8 new fields in TS types
- `src/frontend/src/pages/Admin/IntegrationSettings.tsx` — New email environment UI section
- `src/frontend/src/config.ts` — APP_VERSION → "v2.190.0"
- `docs/CHANGELOG.md` — This entry

## [v2.189.5] - 2026-06-10

### Fixed — Quotation Save HTTP 500: Missing `ItemCatalogId` Column on `QuotationItems`

**Problem:** Saving a quotation via `POST api/v1/requests/{id}/quotations` returned HTTP 500 with `SqlException: Invalid column name 'ItemCatalogId'` on the `QuotationItems` table.

**Root cause:** The `QuotationItem` entity class and EF Core configuration both reference `ItemCatalogId` (nullable FK to `ItemCatalogItems`), and the model snapshot already included this column. However, **no migration ever added the column to the physical database table**. This is a snapshot-vs-database desync introduced when the `ItemCatalog` feature was added — the `AddItemCatalog` migration (20260412105910) only added `ItemCatalogId` to `RequestLineItems`, not to `QuotationItems`.

**Fix:** Created migration `20260610134920_AddItemCatalogToQuotationItems` that manually adds:
1. `ItemCatalogId` (nullable `int`) column to `QuotationItems`
2. Index `IX_QuotationItems_ItemCatalogId`
3. FK `FK_QuotationItems_ItemCatalogItems_ItemCatalogId` → `ItemCatalogItems.Id` (SetNull)

**Files Changed:**
- `src/backend/.../Migrations/20260610134920_AddItemCatalogToQuotationItems.cs` — Manual schema fix migration
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.5"
- `docs/VERSION.md` — v2.189.5
- `docs/CHANGELOG.md` — This entry

## [v2.189.4] - 2026-06-10

### Fixed — Migration Application: QUOTED_IDENTIFIER Session Option (Msg 1934)

**Problem:** Applying migration `20260610083347_AddSupplierSyncColumns` via `sqlcmd` failed with SQL Server Msg 1934: `UPDATE failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'`.

**Root cause:** `sqlcmd` defaults to `QUOTED_IDENTIFIER OFF` per ODBC specification. The `dbo.Suppliers` table (or related objects) requires `QUOTED_IDENTIFIER ON` for DDL/DML operations — this is enforced by SQL Server when tables have filtered indexes, indexed views, computed columns, or XML type methods.

The v2.189.3 fix injected SET options at the top of the generated SQL file, but `sqlcmd` itself starts the session with `QUOTED_IDENTIFIER OFF` before reading the file. Additionally, EF Core idempotent scripts may contain their own `SET QUOTED_IDENTIFIER OFF` statements that override the header.

**Fix:** Updated `scripts/db/apply-migrations.ps1`:
1. All `sqlcmd` invocations now include the `-I` flag (forces `QUOTED_IDENTIFIER ON` at session level)
2. Any `SET QUOTED_IDENTIFIER OFF` in EF-generated SQL is automatically replaced with `SET QUOTED_IDENTIFIER ON`
3. Added preflight check: `SELECT SESSIONPROPERTY('QUOTED_IDENTIFIER')` must return `1` before migration execution
4. Added diagnostic logging: first 20 lines of SQL file, sqlcmd authentication mode, QUOTED_IDENTIFIER OFF scan

**Files Changed:**
- `scripts/db/apply-migrations.ps1` — Robust sqlcmd session options
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.4"
- `docs/VERSION.md` — v2.189.4
- `docs/CHANGELOG.md` — This entry

## [v2.189.3] - 2026-06-10

### Fixed — Missing Supplier Columns: Origin, SourceCompany, LastSyncedAtUtc

**Problem:** The v2.189.2 diagnostic revealed the real backend exception: `Invalid column name 'LastSyncedAtUtc'. Invalid column name 'Origin'. Invalid column name 'SourceCompany'.` — EF Core was querying columns that did not exist in the TEST database.

**Root cause:** The `Supplier` entity model includes `Origin`, `SourceCompany`, and `LastSyncedAtUtc` properties. The `AddSupplierRegistrationFields` migration (20260425) was scaffolded after these properties were added to the model, so its `Designer.cs` snapshot includes them. However, the generated `Up()` method never included `AddColumn` calls for these 3 properties. The v2.156.3 release fixed the `ConsolidatedBaseline` for clean database installs but did not create a standalone migration for existing databases.

**Fix:** New EF Core migration `20260610083347_AddSupplierSyncColumns`:
- `Origin` — nvarchar(max), NOT NULL, DEFAULT `'MANUAL'` (safe for existing rows)
- `SourceCompany` — nvarchar(max), nullable
- `LastSyncedAtUtc` — datetime2, nullable

**Database:** Migration must be applied to `Portal-Gerencial-Test` via `Apply TEST Migrations` workflow before deploying.

**Files Changed:**
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260610083347_AddSupplierSyncColumns.cs` — [NEW] Migration
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260610083347_AddSupplierSyncColumns.Designer.cs` — [NEW] Snapshot
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.3"
- `docs/VERSION.md` — v2.189.3
- `docs/CHANGELOG.md` — This entry

## [v2.189.2] - 2026-06-10


### Fixed — Supplier Creation: Unhandled Exception Safety Net

**Problem:** Despite the v2.189.1 fix, the TEST environment still returned the generic "An error occurred while processing your request." (HTTP 500) when creating a supplier. This generic message comes from the ASP.NET Core `UseExceptionHandler()` middleware, indicating an **unhandled exception** escaping the controller.

**Root cause:** The v2.189.1 fix added a `DbUpdateException` catch block inside the save retry loop, but the Name/NIF pre-check queries, `GetNextPortalCodeAsync()`, and entity construction were **outside** any try-catch. Any non-`DbUpdateException` (e.g., EF Core model mismatch, SQL connection timeout, transaction error) would propagate unhandled to the global exception handler.

**Fix:** Wrapped the entire `CreateSupplier` method body in an outer `try-catch(Exception)` that:
- Logs the full exception type, message, and inner message via `_logger.LogError`
- Returns a ProblemDetails response with the actual error detail (`"Erro inesperado: {innerMsg}"`)
- Prevents the generic `UseExceptionHandler` middleware from swallowing the real error

This makes the actual error visible in the frontend Feedback component, enabling diagnosis without needing direct server log access.

**Files Changed:**
- `src/backend/AlplaPortal.Api/Controllers/LookupsController.cs` — Outer try-catch in `CreateSupplier`
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.2"
- `docs/VERSION.md` — v2.189.2
- `docs/CHANGELOG.md` — This entry

## [v2.189.1] - 2026-06-09


### Fixed — Supplier Creation 500 Error (Duplicate Name)

**Root cause:** The `POST api/v1/lookups/suppliers` endpoint validated NIF/TaxId uniqueness before saving, but did NOT validate Supplier Name uniqueness. The database has a unique index `IX_Suppliers_Name`, so when a supplier with the same name already existed, the `DbUpdateException` was either caught generically (returning a 500 with the controller's custom message) or escaped to the global exception handler (returning the default ASP.NET Core ProblemDetails 500 with no useful detail).

**Backend fix** (`LookupsController.CreateSupplier`):
- Added explicit Name uniqueness pre-check (case-insensitive `ToUpper()` comparison) before save, returning **409 Conflict** with ProblemDetails containing the existing supplier's PortalCode.
- Improved `DbUpdateException` catch block to detect specific constraint violations:
  - `IX_Suppliers_Name` → 409 Conflict (race condition safety net)
  - `IX_Suppliers_TaxId` → 409 Conflict (race condition safety net)
  - `IX_Suppliers_PortalCode` → 500 with clear retry message
  - Unknown → 500 with generic message
- Entity is properly detached on known duplicate errors to keep the change tracker clean.

**Frontend fix** (`QuickSupplierModal.tsx`):
- 409 Conflict handler now distinguishes Name vs NIF duplicates by inspecting the backend detail message.
- Name duplicates show an amber warning box below the Name field (matching the NIF duplicate UX pattern).
- NIF duplicates continue to show the existing amber warning box.
- Name field `onChange` now clears duplicate errors when the user edits, matching the NIF field behavior.

**No migration created.** The `IX_Suppliers_Name` unique index already exists in the database.

**Business rules preserved:**
- PortalCode generated automatically
- PrimaveraCode optional
- New supplier created as DRAFT
- Internal status codes stable (English/no accents)

**Files Changed:**
- `src/backend/AlplaPortal.Api/Controllers/LookupsController.cs` — Name uniqueness pre-check + improved DbUpdateException handling
- `src/frontend/src/components/Buyer/QuickSupplierModal.tsx` — 409 conflict handling for Name and NIF duplicates
- `src/frontend/src/config.ts` — APP_VERSION → "v2.189.1"
- `docs/VERSION.md` — v2.189.1
- `docs/CHANGELOG.md` — This entry

## [v2.189.0] - 2026-06-08

### Added — I.T Equipment Assignment: Availability Date & Visual Signature PDF

Exposed the existing `AssignedDate` field as "Data de disponibilização ao utilizador" in the assignment modal. The field is required, defaults to today's date, and can be changed for historical assignments. **No database migration needed** — reuses the existing `ITEquipmentAssignment.AssignedDate` column.

**Assignment Agreement PDF:**
- Two separate date lines: availability date (date only) and document generation date (date+time UTC)
- `Data de disponibilização` added to the info table
- Visual cursive signature: transparent PNG generated server-side using `System.Drawing.Common` with Segoe Script font (fallback: Lucida Handwriting → Freestyle Script → Arial Italic)
- Enhanced signature block layout: cursive PNG → signature line → printed name → role label
- Electronic generation statement: "Documento gerado eletronicamente no Portal Gerencial." with audit metadata (user, email, asset tag, responsible, timestamp)

**Return Document PDF:**
- Same enhanced signature blocks and electronic generation statement for consistency

**Design decisions:**
- Uses "gerado eletronicamente" (not "aceite") because no real user acceptance action exists yet
- PNG generation uses existing `System.Drawing.Common` v10.0.5 dependency — no new packages added
- Signature image rendered at 24pt in dark navy (#002D72) with transparent background

**Signature Behavior Corrections:**
- Assignment & Return PDFs: User signature block is now an empty area for manual signing (no generated cursive PNG).
- Assignment & Return PDFs: I.T Responsible signature block retains the generated cursive PNG.
- Electronic generation statement wording updated to "Documento gerado eletronicamente no Portal Gerencial pelo responsável de T.I."
- Availability date is now included in the audit metadata footer.

### Added — I.T Equipment Signed Term Upload

Added the ability to upload manually signed Assignment and Return Agreements.
- Added new document types: `SIGNED_ASSIGNMENT_AGREEMENT` and `SIGNED_RETURN_AGREEMENT`.
- Added new movement type: `SIGNED_TERM_UPLOADED`.
- Updated backend upload API to accept an `assignmentId` and restricted file extensions for signed terms to PDF, JPG, and PNG.
- Updated the "Atribuições" tab in `EquipmentQuickViewDrawer`:
  - Added visual indicators for generated terms (blue) and signed terms (green if uploaded, orange if pending).
  - Added action buttons to view generated terms, upload/replace signed terms, and view uploaded signed terms.

## [v2.188.0] - 2026-06-08

### Added — Supplier Ficha Primavera Import Enrichment (DEC-141)

Extended the Primavera supplier import to automatically populate Address, Primary Contact, Banking, and Payment Terms in the Supplier Ficha, using data from the Primavera `Fornecedores` table.

**New Primavera fields mapped:**
- **Address**: Composite from `Morada`, `Morada1`, `Local`, `Cp`, `Pais` (joined with ", ")
- **Primary Contact**: `Contacto` → ContactName1, `Cargo` → ContactRole1, `Telemovel` (fallback to `Tel`) → ContactPhone1, `Email` → ContactEmail1
- **Banking**: `IBAN`, `Swift`, `NumCB` → BankIban, BankSwift, BankAccountNumber
- **Payment Terms**: `CondPag` → PaymentTerms, `ModoPag` → PaymentMethod

**Not available from Primavera** (remain empty, manually editable):
- Secondary contact (ContactName2, ContactPhone2, ContactEmail2)

**Safe update rule**: When re-importing an existing supplier in DRAFT or PENDING_COMPLETION status, only empty Portal fields are filled from Primavera. Manually entered values are never overwritten. Suppliers in ACTIVE, PENDING_APPROVAL, or ADJUSTMENT_REQUESTED status are not modified.

**Safe column detection**: Uses a two-query approach — attempts extended columns (banking/payment/contact) first. If any column is missing in the Primavera installation, falls back to the base column set automatically.

**Diagnostic logging**: Each supplier import/update logs a structured line showing which data groups were found/missing.

### Fixed — I.T Equipment PDF Layout & Readability

Fixed a formatting issue in the automatically generated I.T Equipment Responsibility Term (PDF) where policy text and clause titles were stretched across the full page width with excessive spaces between words.

- Replaced `XParagraphAlignment.Justify` with `XParagraphAlignment.Left` to prevent text stretching on short lines.
- Improved the rendering of bulleted lists within the policy text by properly breaking inline bullet characters (`•`) into formatted, indented multi-line structures for better readability.
- Applied the same alignment correction to the Return Document (Termo de Devolução).

**Files Changed:**
- `src/backend/AlplaPortal.Application/DTOs/Integration/PrimaveraSupplierDto.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/PrimaveraSupplierService.cs`
- `src/backend/AlplaPortal.Api/Controllers/SyncController.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/ITEquipmentPdfService.cs`
- `src/frontend/src/pages/Settings/MasterData.tsx`
- `src/frontend/src/pages/Contracts/SupplierFichaList.tsx`
- `src/frontend/src/pages/Settings/SyncWorkspace.tsx`
- `docs/VERSION.md`
- `docs/CHANGELOG.md`
- `docs/DECISIONS.md`
- `src/frontend/src/config.ts`

## [v2.187.0] - 2026-06-05

### Security — HR Attendance API Access-Control Hardening (DEC-140)

**Scope:** Backend authorization hardening for `HRAttendanceController`. No frontend or sidebar changes.

**Critical fix — Anonymous test endpoint removed:**
- Deleted the `[AllowAnonymous]` diagnostic endpoint `GET /api/hr/attendance/test-verify/{innuxEmployeeId:int}/{date:datetime}`. This endpoint bypassed all authentication and exposed attendance data for any employee by Innux ID. It also leaked full exception stack traces on error. The endpoint was a development artifact that should not have reached production code.

**Entitlement alignment — `HasHRModuleAccess()` added:**
- Added explicit `HasHRModuleAccess()` entitlement checks to all production attendance endpoints: `GetCalendar`, `GetDayDetail`, `GetAbsenceCodes`, `GetWorkCodes`.
- Previously, these endpoints relied solely on `[Authorize]` (any authenticated user) plus data scoping via `GetScopedEmployeesQuery()`. While data was safely scoped, the missing entitlement gate was inconsistent with `HRLeaveController`, which has the same check.
- The `HasHRModuleAccess()` method mirrors `HRLeaveController.HasHRModuleAccess()` exactly: System Administrator, HR, Local Manager, Department Manager, or self-calendar (email-matched HREmployee).
- Diagnostic endpoints (`portal/resolve-schedule`, `portal/interpret-punches`, `portal/compare`, `portal/compare-range`) and the monthly report endpoint already had proper role-based restrictions and were not changed.

**No sidebar changes:**
- "Gestão da Equipa" visibility for Viewer / Management users remains unchanged. This is by design — the future HR permission matrix will be evaluated separately.

**Files Changed:**
- `src/backend/AlplaPortal.Api/Controllers/HRAttendanceController.cs` — Removed anonymous endpoint, added `HasHRModuleAccess()` method, added entitlement checks to 4 endpoints, updated XML doc.
- `src/frontend/src/config.ts` — APP_VERSION → "2.187.0".
- `docs/VERSION.md` — Bumped to v2.187.0.
- `docs/CHANGELOG.md` — This entry.
- `docs/DECISIONS.md` — DEC-140 HR access-control hardening decision.

## [v2.186.1] - 2026-06-05

### Fixed — TEST Environment Banner Not Appearing on Server (DEC-140)

**Root cause:** The TEST server's `appsettings.Test.json` did not contain the `AppEnvironment` section introduced in v2.186.0. Since the base `appsettings.json` defaults to PROD (`ShowBanner: false`), the TEST API endpoint `/api/app/environment` returned PROD values.

**Fix:**
- Created `scripts/server/configure-test-environment-banner.ps1` — sets `AppEnvironment__Code`, `AppEnvironment__Name`, and `AppEnvironment__ShowBanner` as IIS App Pool environment variables on `AlplaPortal-Test-Api-Pool`.
- Updated `GITHUB_ACTIONS_TEST_DEPLOYMENT.md` — added `AppEnvironment__*` variables to the IIS App Pool configuration section and updated the `appsettings.Test.json` template.
- No frontend changes — the frontend was already working correctly; only the backend configuration was missing.

**Configuration applied to TEST server:**
| Variable | Value |
|:---|:---|
| `AppEnvironment__Code` | `TEST` |
| `AppEnvironment__Name` | `Ambiente de Teste` |
| `AppEnvironment__ShowBanner` | `true` |

## [v2.186.0] - 2026-06-05

### Added — Automatic Visual Environment Differentiation (DEC-140)

**Scope:** Backend configuration + frontend visual indicators across all pages.

The application now automatically detects whether it is running in TEST or PRODUCTION and applies visual indicators accordingly. A single codebase serves both environments — no separate builds or environment variables at build time.

**Backend:**
- New `AppEnvironmentOptions` config model (`Code`, `Name`, `ShowBanner`).
- New anonymous endpoint `GET /api/app/environment` — returns environment config without authentication (required for login/public pages).
- Default configuration in `appsettings.json` = PROD (no banner). TEST overrides via IIS environment variables or `appsettings.Development.json` (gitignored).

**Frontend — TEST indicators:**
- **Fixed amber banner** at top of all pages: *"AMBIENTE DE TESTE — Use apenas para validações e simulações. Dados e ações deste ambiente não representam o ambiente produtivo."*
- **Sidebar TEST badge**: Amber pill badge near the system logo.
- **Browser title prefix**: `[TEST] Portal Gerencial`.
- **Fullscreen/LiveBoard**: Compact 24px inline amber strip (non-overlapping).
- **Print safety**: Banner hidden in `@media print`.

**Frontend — PROD behavior:**
- No banner, no badge, no title prefix. Layout completely unchanged.

**Layout mechanics:**
- CSS variable `--env-banner-height: 32px` offsets topbar, sidebar, and main content when banner is visible.
- `EnvironmentContext.tsx` fetches from API with URL-based fallback detection (`localhost`/`test` hostname → TEST).

**IIS Configuration (TEST deployment):**
```
AppEnvironment__Code = TEST
AppEnvironment__Name = Ambiente de Teste
AppEnvironment__ShowBanner = true
```
PROD requires no IIS changes.

**Files Created:**
- `src/backend/AlplaPortal.Application/Models/Configuration/AppEnvironmentOptions.cs` — Config model.
- `src/backend/AlplaPortal.Api/Controllers/AppController.cs` — Anonymous endpoint.
- `src/frontend/src/contexts/EnvironmentContext.tsx` — Context + provider + hook.
- `src/frontend/src/components/ui/EnvironmentBanner.tsx` — Fixed amber banner.

**Files Modified:**
- `src/backend/AlplaPortal.Api/Program.cs` — DI registration.
- `src/backend/AlplaPortal.Api/appsettings.json` — PROD default section.
- `src/frontend/src/App.tsx` — `EnvironmentProvider` wrapper.
- `src/frontend/src/styles/globals.css` — Banner CSS, badge CSS, print rules, CSS variables.
- `src/frontend/src/layouts/AppShell.tsx` — Banner integration + layout offset.
- `src/frontend/src/components/layout/Topbar.tsx` — Sticky offset.
- `src/frontend/src/components/layout/Sidebar.tsx` — TEST badge.
- `src/frontend/src/pages/LoginPage.tsx` — Banner on public page.
- `src/frontend/src/pages/ResetPasswordPage.tsx` — Banner on public page.
- `src/frontend/src/pages/Operations/OperationsLiveBoardPage.tsx` — Compact amber strip.
- `docs/DECISIONS.md` — DEC-140.
- `docs/CHANGELOG.md` — This entry.
- `docs/VERSION.md` — v2.186.0.
- `src/frontend/src/config.ts` — APP_VERSION → `2.186.0`.

## [v2.185.10] - 2026-06-04

### Changed
- **Pending Approvals Notification**: Reverted sidebar-based approval highlighting in favor of a non-intrusive floating sticker (`PendingApprovalsSticker.tsx`).
- **Feedback Notification Style**: Adopted the bottom-right portal notification pattern to alert users of pending approvals. Includes sessionStorage persistence so the sticker remains hidden after manual dismissal.

## [v2.185.9] - 2026-06-04

### Changed
- **Preventive EF Core Migration Handling (DEC-137)**: Disabled automatic `Database.Migrate()` execution in non-Development environments to prevent HTTP 500.30 startup failures caused by the IIS runtime identity lacking DDL permissions (v2.185.8 incident).

  **New Behavior**:
  - **Development**: `Database.Migrate()` still runs automatically on startup (unchanged).
  - **TEST / Staging / Production**: The application calls `GetPendingMigrations()` instead of `Migrate()`. If pending migrations are detected, it logs each missing migration ID (`[STARTUP] PENDING: <id>`) and crashes with a descriptive `InvalidOperationException` listing all missing IDs and remediation steps. It never attempts DDL operations.

  **GitHub Actions Workflow Changes**:
  - Both `deploy-test.yml` and `deploy-prod.yml` now include a "Check for pending EF Core migrations" step that runs **before** the App Pools are started.
  - The step reads the connection string from the preserved `appsettings.*.json`, queries `__EFMigrationsHistory`, and compares against the expected migration list.
  - If pending migrations are found, the deployment fails with `::error::` annotations listing each missing migration and remediation instructions.
  - The `deploy-prod.yml` step also includes a safety check that blocks deployment if the connection string resolves to `[Portal-Gerencial-Test]`.

  **New Script**: `scripts/db/check-pending-migrations.ps1` — reusable migration comparison script that can be run manually against any database. Reports applied, pending, and unknown migrations. Returns exit code 0 on pass, 1 on fail.

  **Documentation Updated**:
  - `docs/DEPLOYMENT_CHECKLIST.md` — Added mandatory "EF Core Migration Checklist" section with step-by-step procedure.
  - `docs/GITHUB_ACTIONS_TEST_DEPLOYMENT.md` — Updated Section 7 (Database Migrations) and Section 10.3 (Database Requirements).
  - `docs/GITHUB_ACTIONS_PROD_DEPLOYMENT.md` — Updated Section 7 (Database Migrations) and Section 10 (Troubleshooting).

  **Files Changed**:
  - `src/backend/AlplaPortal.Api/Program.cs` — Environment-aware migration handling.
  - `.github/workflows/deploy-test.yml` — Pre-start migration check step + updated summary.
  - `.github/workflows/deploy-prod.yml` — Pre-start migration check step + updated summary + header comment.
  - `scripts/db/check-pending-migrations.ps1` — New reusable migration comparison script.
  - `docs/DEPLOYMENT_CHECKLIST.md` — Mandatory migration checklist.
  - `docs/GITHUB_ACTIONS_TEST_DEPLOYMENT.md` — Updated migration sections.
  - `docs/GITHUB_ACTIONS_PROD_DEPLOYMENT.md` — Updated migration sections.
  - `docs/DECISIONS.md` — DEC-137.
  - `docs/CHANGELOG.md` — This entry.
  - `docs/VERSION.md` — v2.185.9.
  - `src/frontend/src/config.ts` — APP_VERSION → `v2.185.9`.

## [v2.185.8] - 2026-06-03

### Fixed
- **Supplier PortalCode D6 Standardization (DEC-136)**: Fixed `Cannot insert duplicate key row in object 'dbo.Suppliers' with unique index 'IX_Suppliers_PortalCode'` error when creating suppliers from the OCR/proforma flow (QuickSupplierModal).

  **Root Cause**: Two interacting bugs:
  1. `SyncController.SupplierImport` and `SupplierImportReviewed` generated PortalCodes in D4 format (`SUP-0003`, 8 chars), while `LookupsController.GetNextPortalCodeAsync` generated D6 format (`SUP-000003`, 10 chars).
  2. The self-healing parser in `GetNextPortalCodeAsync` required `maxCodeStr.Length == 10`, silently ignoring any D4 codes in the database. This caused the counter to regress to 0, generating `SUP-000001`/`SUP-000002` which collided with seed data.

  **Changes**:
  - **Flexible parser**: `ParsePortalCodeSequence()` static helper handles any `SUP-XXXX` numeric format (D4, D5, D6+). Materializes all codes client-side and finds the numeric max, avoiding SQL alphabetic ordering issues with mixed-length codes.
  - **Retry logic**: `CreateSupplier` retries up to 3 times on `IX_Suppliers_PortalCode` collision, detaching the failed entity and regenerating the code. Error messages are sanitized — raw SQL constraint names are never exposed to the frontend.
  - **D6 standardization**: `SupplierImport` and `SupplierImportReviewed` now use `$"SUP-{nextSeq:D6}"` instead of `D4`.
  - **SystemCounters alignment**: Both import endpoints now update the `SUPPLIER_PORTAL_CODE` counter after batch saves, keeping `GetNextPortalCodeAsync` synchronized.
  - **ILogger injection**: Added `ILogger<LookupsController>` to support structured warning/error logging in the retry flow.

  **Files Changed**:
  - `src/backend/AlplaPortal.Api/Controllers/LookupsController.cs` — Fixed `GetNextPortalCodeAsync`, added retry logic to `CreateSupplier`, added `ParsePortalCodeSequence` helper, injected `ILogger`.
  - `src/backend/AlplaPortal.Api/Controllers/SyncController.cs` — D4→D6 in `SupplierImport` and `SupplierImportReviewed`, fixed max-code parser, added SystemCounters alignment.
  - `src/frontend/src/config.ts` — APP_VERSION → `v2.185.8`.
  - `docs/VERSION.md` — v2.185.8.
  - `docs/CHANGELOG.md` — This entry.
  - `docs/DECISIONS.md` — DEC-136.

## [v2.185.7] - 2026-06-03

### Fixed
- **Production Email Config Script — Schema Correction**: Regenerated `scripts/db/configure-production-email.sql` from the validated AOVIA1VMS011 schema. Corrected table name `IntegrationConnectionStatuses` (plural, EF convention), replaced nonexistent columns (`LastTestedAtUtc`, `LastSuccessAtUtc`) with actual columns (`LastSuccessUtc`, `LastFailureUtc`, `LastResponseTimeMs`, `LastErrorMessage`, `ConsecutiveFailures`, `LastTestedByEmail`, `LastCheckedAtUtc`), standardized FK references to `IntegrationProviderId`, added schema guard checks that abort execution if required tables/columns are missing, and ensured final validation outputs counts/status only without exposing encrypted values.

## [v2.185.6] - 2026-06-03

### Added
- **Production Email Configuration Script**: Created `scripts/db/configure-production-email.sql` — a safe, idempotent SQL script that copies SMTP settings from the Test database to Production. Includes diagnostic comparison, pre-change backup (SQL Express compatible), masked sensitive output, and post-configuration validation.
- **Deployment Checklist Update**: Added "Email / SMTP Configuration (Production)" section to `docs/DEPLOYMENT_CHECKLIST.md` documenting where email settings are stored, how to initialize them from Test, encryption key prerequisites, and safe validation procedures.

## [v2.185.5] - 2026-06-03

### Fixed
- **SQL Express Backup Compatibility**: The `deploy-prod.yml` database backup step now detects SQL Server Edition at runtime via `SERVERPROPERTY('Edition')`. If Express Edition is detected, the backup runs without `COMPRESSION` (which Express does not support). Other editions continue to use `COMPRESSION` for faster, smaller backups.
- **Connection String Diagnostics**: Added pre-parse validation for `PROD_DB_CONNECTION_STRING` to detect common issues (newlines, leading/trailing whitespace, BOM characters) before attempting `SqlConnection`. When the error "Format of the initialization string does not conform to specification" occurs, the workflow now prints actionable guidance for correcting the GitHub secret.

## [v2.185.4] - 2026-06-03

### Fixed
- **Cascading IDE Lexer Errors**: Completely eliminated all VS Code PowerShell extension lexer false positives in `setup-production-environment.ps1` by rewriting log functions to pre-compute formatted strings in variables, replacing all `-f` operator calls, and switching the XML here-string from double quotes (`@"..."@`) to single quotes (`@'...'@`). This resolves spurious "Unexpected token" and "Missing closing '}'" errors reported by the IDE.

## [v2.185.3] - 2026-06-02

### Fixed
- **IDE PowerShell Extension Lexer Error**: Refactored string interpolation in `setup-production-environment.ps1` to use the `-f` format operator. This resolves a known issue where VS Code's PowerShell extension (and PSScriptAnalyzer) misinterprets nested subexpressions inside double quotes as an unterminated string, which incorrectly flags a "Missing closing '}' in statement block" error later in the file.

## [v2.185.2] - 2026-06-02

### Fixed
- **PowerShell -WhatIf Failure**: Fixed an issue where `setup-production-environment.ps1 -WhatIf` failed during simulation mode because `Set-ItemProperty` and `New-WebBinding` were executing against App Pools and IIS Sites that were simulated but not actually created. Added `Test-Path` safety checks to skip configuration when resources are absent due to simulation.

## [v2.185.1] - 2026-06-02

### Fixed
- **PowerShell Parser Error**: Replaced UTF-8 em-dash (`—`) characters with hyphens (`-`) in `setup-production-environment.ps1` and `validate-production-environment.ps1`, and enforced UTF-8 BOM encoding. This resolves an issue where Windows PowerShell 5.1 misinterpreted the characters under Windows-1252 encoding, leading to fatal string parsing errors (`The string is missing the terminator: "`).

## [v2.185.0] - 2026-06-02

### Added
- **Production Deployment Automation**: Complete CI/CD infrastructure for the Production environment on AOVIA1VMS011.
  - `deploy-prod.yml`: GitHub Actions workflow with `workflow_dispatch` trigger, database name validation (`[Portal-Gerencial]`), SQL backup, config file preservation, `web.config` preservation (port 5002), smoke test, and Test environment integrity check.
  - `setup-production-environment.ps1`: Idempotent bootstrap script — creates folder structure, IIS App Pools (`AlplaPortal-Prod-Api-Pool`, `AlplaPortal-Prod-Web-Pool`), IIS Sites, NTFS permissions, environment variables, HTTPS binding, and Production `web.config` (reverse proxy to `localhost:5002`).
  - `validate-production-environment.ps1`: Read-only validation script with 20+ checks covering folders, IIS, ports, certificates, config, and Test isolation.
  - `GITHUB_ACTIONS_PROD_DEPLOYMENT.md`: Comprehensive deployment guide (architecture, prerequisites, GitHub configuration, how-to, troubleshooting).
  - `POST_DEPLOYMENT_CHECKLIST_PROD.md`: Post-deployment validation checklist (automated and manual).
  - `ROLLBACK_PROCEDURE_PROD.md`: Step-by-step rollback procedures (application-only and full app + database).
  - `DEPLOYMENT_CHECKLIST.md`: Added Production Environment section with port, database, path, and IIS configuration reference.

### Fixed
- **PowerShell `$pid` Collision**: Renamed `$pid` to `$procId` in both server scripts to avoid conflict with PowerShell's readonly automatic variable.

## [v2.184.2] - 2026-06-02

### Fixed
- **Innux Integration Testing**: Replaced direct configuration reads with database-first cascade resolution via `IntegrationConfigResolver`, resolving false-negative "connection settings are incomplete" validation errors.

## [v2.184.1] - 2026-06-02

### Fixed
- **AlplaPROD Integration Testing**: Restored database configuration cascade priority, fixing false 'disabled' validation errors and synchronizing the factory logic.

## [v2.184.0] - 2026-06-02

### Added — Integration: AlplaPROD 1.0 Multi-Plant Configuration

**Scope:** Backend, Frontend, and Database migration for AlplaPROD multi-plant connection management.

**Root Cause:** AlplaPROD 1.0 was seeded as a planned/future provider with `IsPlanned=true`, `IsEnabled=false`, and `CurrentStatus=PLANNED`. This blocked all connection testing with the message "This provider is planned for a future phase."

**Changes:**

- **Migration (`ActivateAlplaProdProvider`):** Sets `IsPlanned=false`, `IsEnabled=true`, and `CurrentStatus=NOT_CONFIGURED` for the ALPLAPROD provider (Id=5). Follows the established activation pattern from Primavera and Innux providers.
- **HasData Seed (`ApplicationDbContext`):** Updated ALPLAPROD seed data to match the activated state, ensuring future database recreations start with the provider active.
- **Backend — DTOs:** Added `AlplaProdPlantSettingsDto`, `UpdateAlplaProdPlantDto`, `ReplaceAlplaProdPlantSecretDto`.
- **Backend — Service:** `UpdateAlplaProdPlantAsync()` and `ReplaceAlplaProdPlantSecretAsync()` methods with cascade credential fallback.
- **Backend — Controller:** `PUT ALPLAPROD/plant` and `POST ALPLAPROD/plant/secret` endpoints.
- **Backend — Provider:** Added `TestPlantConnectionAsync(plantKey)` for per-plant connection testing.
- **Backend — Health Service:** Per-plant routing for ALPLAPROD when `companyKey` is provided; bypass `IsEnabled` global check (same pattern as Primavera).
- **Frontend — Types:** 3 new TypeScript interfaces + `alplaProdPlants` on main DTO.
- **Frontend — API Client:** `updateAlplaProdPlant()` and `replaceAlplaProdPlantSecret()` methods.
- **Frontend — UI:** Per-plant cards in ProviderCard (server, database, username, password status, configure, replace password, test connection), `AlplaProdPlantConfigModal`, `AlplaProdPlantSecretModal`.

**Files Changed:**
- `src/backend/AlplaPortal.Infrastructure/Data/Migrations/20260602104846_ActivateAlplaProdProvider.cs` [NEW]
- `src/backend/AlplaPortal.Infrastructure/Data/ApplicationDbContext.cs` [MODIFIED]
- `src/backend/AlplaPortal.Application/DTOs/Integration/IntegrationSettingsDtos.cs` [MODIFIED]
- `src/backend/AlplaPortal.Application/Interfaces/Integration/IIntegrationSettingsService.cs` [MODIFIED]
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/IntegrationSettingsService.cs` [MODIFIED]
- `src/backend/AlplaPortal.Api/Controllers/Admin/IntegrationSettingsController.cs` [MODIFIED]
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/AlplaProdIntegrationProvider.cs` [MODIFIED]
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/IntegrationHealthService.cs` [MODIFIED]
- `src/frontend/src/types/index.ts` [MODIFIED]
- `src/frontend/src/lib/api.ts` [MODIFIED]
- `src/frontend/src/pages/Admin/IntegrationSettings.tsx` [MODIFIED]

## [v2.183.0] - 2026-06-02

### Fixed — Operations: Public Route

**Scope:** Frontend routing for Operations Live Board.

- **Frontend Issue:** The Live Board route (`/operations/live-board/:plant`) was nested inside the `ProtectedRoute` wrapper in `App.tsx`, which caused the page to require authentication regardless of API settings.
- **Resolution:** Moved the Live Board route outside of `ProtectedRoute` and `AppShell` entirely, rendering it as a standalone, public page. 

## [v2.182.0] - 2026-06-02

### Changed — Operations: Anonymous Access for Live Board

**Scope:** Backend and Frontend access control for Operations Live Board.

- Made the Live Board route (`/operations/live-board/:plant`) accessible without login for TV/kiosk display usage.
- Added `[AllowAnonymous]` to the `GetLiveBoard` API endpoint while keeping all other Operations endpoints protected.
- Verified that no sensitive data (financials, usernames) is exposed in the Live Board DTOs.

## [v2.181.0] - 2026-06-02

### Fixed — Operations: RBAC in User Management

- **User Management UI**: Fixed missing translation mapping in `roles.ts` which prevented `OPERATIONS` role from appearing in the user management role assignment list. Display name is now `Operações`.

## [v2.180.0] - 2026-06-02

### Added — Operations: RBAC for Live Board

- **Route access**: Added role-based access control for `/operations/live-board/:plant`.
- **Role requirement**: Only users with `Operations` or `System Administrator` roles can access the TV Signage page.
- **Kiosk display**: Added specific exception for public kiosk displays (to be implemented via secure token in next phase).
- **Backend security**: Added `[Authorize]` attributes with specific role enforcement to `OperationsController` endpoints (list, details, timeline), excluding the live board endpoint.
- **User Management**: Exposed the new `Operations` role in the UI for administrators to assign and remove.

## [v2.179.0] - 2026-06-01

### Changed — Operations Live Board: TV Signage UX Redesign

- **KPI Summary**: 4 large icon-driven cards (📥 Entradas, 🚚 Saídas, ⚠️ Atenção, ✅ Concluídos) replace old text-only footer.
- **Card redesign**: Compact layout — large PO# (24px), short route (V2→V1), single-line material, inline quantity, short attention message.
- **SVG timeline icons**: 5 distinct SVG icons per step (document/truck/inbox/half-circle/check-circle) with color states and active glow animation.
- **Auto-paging carousel**: Max 4 visible cards per column, 8s automatic page rotation, page dots, "+N em fila" overflow indicator. No scrollbars.
- **Attention visualization**: Stronger pulse animation (20px/6px glow), bold amber "⚠ 5h aguardando" short messages instead of long text.
- **Empty states**: Large centered SVG icon + message per column instead of italic text.
- **Typography**: Larger sizes (24px PO, 20px headers, 32px KPI values), uppercase headers with letter-spacing.
- **Background**: Deeper gradient (#0a0f1e → #111827) for higher TV contrast.
- **Bottom bar**: Clean version + query time footer.
- **No backend changes**. No barcode tracking. No ALL plant aggregation. No deployment changes.

## [v2.178.0] - 2026-06-01

### Added — Operations Phase Live 3: Frontend TV Page

- **Route**: `/operations/live-board/:plant` — TV-ready Live Transfer Board page.
- **Layout**: Two-column (Inbound/Outbound) dark-themed page with header, countdown bar, transfer cards, and summary footer.
- **Transfer cards**: PO number, stage badge, route (origin → destination), material name, quantity progress, mini-timeline, age indicator.
- **Mini-timeline**: 5-stage visual (done/active/pending) — backend is source of truth, no re-derivation.
- **Auto-refresh**: Server-driven interval (default 60s, clamp 30–300s) with animated countdown bar.
- **Fullscreen mode**: `?fullscreen=true` — fixed overlay, z-index 9999, larger fonts, hides AppShell chrome.
- **Stale data**: Green/amber/red freshness dot (thresholds: 5min warning, 15min error).
- **Error resilience**: Retains last known data on API failures, shows error banner.
- **Attention indicators**: Amber/red borders, subtle pulse animation, reason text for delayed transfers.
- **Types**: `OperationsLiveBoardResponse`, `OperationsLiveBoardSummary`, `OperationsLiveBoardTransfer`, `OperationsLiveBoardStep`.
- **API client**: `fetchOperationsLiveBoard()` added to `operationsApi.ts`.
- **Query params**: `refresh`, `maxInbound`, `maxOutbound`, `fullscreen`, `completedWindowHours`.
- **No backend changes**. No barcode tracking. No ALL plant aggregation. No deployment changes.

## [v2.177.0] - 2026-06-01

### Added — Operations Phase Live 2: Live Board Backend Endpoint

- **Scope**: Backend endpoint only — no frontend TV screen.
- **Endpoint**: `GET /api/operations/live-board?plant=VIANA1` — TV-ready response with pre-classified inbound/outbound transfer cards.
- **Query params**: `plant` (required), `refreshSeconds` (30–300), `maxInbound`/`maxOutbound` (1–12), `includeRecentlyCompleted` (bool), `completedWindowHours` (1–24).
- **Stage mapping**: 5 simplified stages: `ORDERED → SENT → RECEIVING → PARTIAL → COMPLETED` (+ `ERROR`).
- **Stage labels**: Portuguese (Pedido criado, Enviado, Aguardando recebimento, Parcialmente recebido, Concluído, Atenção).
- **Mini-timeline**: 5-step array per transfer with `done`/`active`/`pending` states.
- **Attention detection**: 4h receiving, 8h partial, 24h ordered, 48h critical thresholds.
- **Direction**: MVP heuristic based on known plant routes (VIANA2→VIANA1 inbound, VIANA1→VIANA3 outbound).
- **Received quantity**: Uses `T_WareneingangPlanungen.EntladeMenge` (consistent with Phase 7.1).
- **Summary counters**: Inbound/outbound totals, active counts, attention, completed.
- **Security**: `[Authorize]`, no financial values, no usernames, no raw SQL/traces.
- **Error handling**: 400/503/500 with Portuguese messages.
- **Files created**: `OperationsLiveBoardDtos.cs`, `IOperationsLiveBoardService.cs`, `OperationsLiveBoardQueryBuilder.cs`, `OperationsLiveBoardService.cs`.
- **Files modified**: `OperationsController.cs`, `Program.cs`, `config.ts`, `VERSION.md`, `CHANGELOG.md`.

## [v2.176.0] - 2026-06-01

### Added — Operations Phase 8: Live Transfer Board Design

- **Scope**: Design document only — no code implementation.
- **Document**: `docs/OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md` — TV/kiosk visual board for inter-plant material transfers.
- **Concept**: Two-column inbound/outbound layout, 5-stage simplified timeline, auto-refresh, dark mode, plant-contextual direction.
- **Endpoint**: `GET /api/operations/live-board?plant=VIANA1` (proposed, not implemented).
- **Files**: `OPERATIONS_LIVE_TRANSFER_BOARD_DESIGN.md` (new), `OPERATIONS_MODULE_TECHNICAL_DESIGN.md`, `config.ts`, `VERSION.md`, `CHANGELOG.md`.

## [v2.175.0] - 2026-06-01

### Fixed — Operations Phase 7.2: Partial Receipt Stage Derivation

- **Problem**: `GR_COMPLETED` always showed `Recebimento concluído`, even for partially delivered POs. A transfer can have completed receipt transactions but still be partially received at PO level.
- **Fix**: `deriveCurrentStage` now uses PO status + detail quantity data to distinguish partial from full receipt.
- **Rules**: PO `Parcialmente entregue` or `receivedQty < orderedQty` → `Parcialmente recebido`. Full receipt only when PO `Concluído` or `receivedQty >= orderedQty` or `openQty = 0`.
- **SummaryCard**: Now accepts optional `detailData` prop for quantity-aware stage derivation in drawer context.
- **References**: PO `#3429` (partial), `#3579` (completed), `#3581` (pending). PO `#3425` retired (finalized).
- **Files**: `OperationsTransfersPage.tsx`, `config.ts`, `VERSION.md`, `CHANGELOG.md`.

## [v2.174.0] - 2026-06-01

### Fixed — Operations Phase 7.1: Receipt Quantity Correction

- **Problem**: PO #3579 showed `Qtd. recebida: 0` despite completed receipt (`Nº recebimentos: 1`). Misleading zero worse than null.
- **Root cause**: `T_Wareneingaenge.IstMenge` is always `0` in AlplaPROD — column exists but not populated.
- **Fix**: Aggregate `SUM(T_WareneingangPlanungen.EntladeMenge)` instead of `SUM(T_Wareneingaenge.IstMenge)` in both Standard and Inhouse query pipelines.
- **Recalculation**: `openQuantity = orderedQuantity - receivedQuantity` (NULL-safe).
- **Files**: `OperationsTransferDetailQueryBuilder.cs`, `config.ts`, `VERSION.md`, `CHANGELOG.md`.

## [v2.173.0] - 2026-06-01

### Changed — Operations Phase 7: Drawer Runtime Visual Validation & UX Refinement

**Scope:** Runtime visual validation of Quick Viewer Drawer with real AlplaPROD data. Frontend-only changes.

**Issues found and fixed (from code review):**

1. **Missing "Informações do Pedido" card**
   - The `detailData.header` DTO was completely unused — no card rendered for order info (notes, created/updated by, dates, status).
   - Added `DetailHeaderCard` as the first detail card in the drawer sequence.

2. **`formatDateShort` → `DetailRow` inconsistency**
   - `formatDateShort(null)` returned `'—'` (string), but `DetailRow` checked `value === '—'` and hid those rows.
   - This caused null dates to silently vanish from detail cards instead of being handled properly.
   - Fixed: `formatDateShort` now returns `null` for null dates, `DetailRow` only checks `null`/`''`.

3. **Inaccurate "Paletes" label**
   - `BestellMengeVPK` is "packaging unit quantity" (VPK = Verpackung), not pallets.
   - Renamed to "Qtd. embalagem (VPK)".

4. **Loading card labels too generic**
   - "Status" → "Status carregamento" (avoids ambiguity with order status)
   - "Camião" → "Nº camião" (consistent with other Nº fields)
   - "Descrição" → "Descrição camião" (specifies what it describes)
   - "Nº guia" → "Nº guia de remessa" (full Portuguese term for delivery note)

5. **Removed deferred `Data entrega` row**
   - `deliveryDate` is always null (deferred from Phase 6.1) — `formatDateShort` returned `'—'` and `DetailRow` hid it.
   - Now explicitly removed from the loading card to avoid confusion.

6. **Technical IDs in body font**
   - Added `mono` prop to `DetailRow` for monospace rendering of IDs (`#1234`).
   - Applied to variant IDs, inhouse delivery IDs.

7. **Table cell null dates showing empty**
   - Table list cells for `createdDate`/`updatedDate` now show `'—'` consistently.

**Files Modified:**
- `src/frontend/src/pages/Operations/OperationsTransfersPage.tsx` — 7 UX refinements
- `src/frontend/src/config.ts` — APP_VERSION → `2.173.0`
- `docs/VERSION.md`, `docs/CHANGELOG.md`

**What is NOT included:**
- No barcode tracking
- No `ALL` plant aggregation
- No deployment changes
- No new endpoints or backend changes
- No write operations to AlplaPROD
- No new SQL inspection scripts

---

## [v2.172.0] - 2026-06-01

### Fixed — Operations Phase 6.1: Transfer Details Schema Correction

**Problem:**
Phase 6 assumed AlplaPROD column names that do not exist (`Farbe`, `PalettenMenge`, `LieferscheinNummer`, `LieferscheinDatum`, `Menge`). These were replaced with `NULL AS` placeholders during Phase 6, causing partially empty detail cards in the Quick Viewer Drawer.

**Research method:**
Cross-referenced existing SQL discovery files (`Viana{1,2,3}_02_column_search_german_labels.txt`, `Viana{1,2,3}_10_article_variant_trace.txt`) to find correct column names. No new SQL inspection script was needed — all answers were in existing Phase 1-2 discovery data.

**Resolved mappings (6 of 7):**

| DTO Field | Wrong Column | Correct Column | Table | Plants |
|-----------|-------------|----------------|-------|--------|
| `material.color` | `av.Farbe` | `av.Farbbezeichnung` | `T_Artikelvarianten` | All |
| `quantity.palletQuantity` | `bp.PalettenMenge` | `bp.BestellMengeVPK` | `T_Bestellpositionen` | All |
| `loading.truckNumber` | NULL placeholder | `la.LKWNummer` | `T_LadeAuftraege` | Viana 1/2 |
| `loading.truckDescription` | NULL placeholder | `la.LKWBezeichnung` | `T_LadeAuftraege` | Viana 1/2 |
| `loading.deliveryNumber` | `la.LieferscheinNummer` | `la.ExtLieferscheinNummer` | `T_LadeAuftraege` | Viana 1/2 |
| `quantity.receivedQuantity` | `w.Menge` | `SUM(w.IstMenge)` | `T_Wareneingaenge` | All |

**Still deferred (1 of 7):**
- `loading.deliveryDate` — No equivalent column in `T_LadeAuftraege`. Remains NULL.

**Key schema findings:**
- `T_Artikelvarianten` uses `Farbbezeichnung` (color name, nvarchar 100), not `Farbe`
- `T_Bestellpositionen` uses `BestellMengeVPK` (packaging unit qty, float), not `PalettenMenge`
- `T_LadeAuftraege` uses `ExtLieferscheinNummer` (external delivery note, nvarchar 50), not `LieferscheinNummer`
- `T_Wareneingaenge` uses `IstMenge` (actual received qty, float) and `SollMenge` (expected qty), not `Menge`
- `LKWNummer` and `LKWBezeichnung` were confirmed to exist but had been defensively set to NULL during Phase 6

**Files Modified:**
- `src/backend/.../OperationsTransferDetailQueryBuilder.cs` — 6 NULL→real column corrections (Standard + Inhouse queries)
- `src/frontend/src/config.ts` — APP_VERSION → `2.172.0`
- `docs/VERSION.md`, `docs/CHANGELOG.md`

**What is NOT included:**
- No barcode tracking
- No `ALL` plant aggregation
- No deployment changes
- No new screens or endpoints
- No write operations to AlplaPROD
- No new SQL inspection script (existing discovery was sufficient)

---

## [v2.171.0] - 2026-06-01

### Added — Operations Phase 6: Transfer Details in Quick Viewer Drawer

**Problem:**
The Quick Viewer Drawer only showed summary and timeline data. Users needed material information, quantities, loading/delivery details, and goods receipt status — all requiring a separate detail endpoint.

**Backend — New endpoint:**
`GET /api/operations/transfers/{plant}/{idBestellung}/details`

Returns a single `OperationsTransferDetail` DTO with 7 nested sections:
- `purchaseOrder` — PO date, status, journal reference
- `material` — Material name, article alias, color, type, classification, variant ID
- `quantity` — Ordered, received, open quantities, pallets, packaging
- `loading` — Standard: load date, truck, delivery number; Inhouse: delivery ID, production date
- `goodsReceipt` — Receipt status, count, dates, completion flag
- `technicalReferences` — All internal IDs for debugging

**Architecture:**
- `OperationsTransferDetailQueryBuilder` — Separate SQL for Standard (VIANA1/2) and Inhouse (VIANA3) pipelines. Uses `OUTER APPLY TOP 1` for representative material/position/loading rows.
- `OperationsTransferDetailService` — Orchestrates connection, query execution, row mapping with `ReadNullableX` DBNull helpers.
- Error hierarchy: 400 → 404 → 503 → 500 (Portuguese messages, no secrets exposed).

**Frontend — Parallel loading:**
- `handleSelectTransfer` now uses `Promise.allSettled([details, timeline])` — each loads independently with its own error handling.
- Detail and timeline spinners are independent.

**Frontend — 5 detail cards:**
1. **Material / Artigo** — Material name (highlighted), alias, color, type, classification, variant ID
2. **Quantidades** — Ordered (highlighted), received, open (amber warning), pallets, packaging
3. **Carregamento / Entrega** — Standard: load date, status, truck, delivery number; Inhouse: delivery ID, dates, journal
4. **Recebimento de Mercadoria** — Completion badge (green/amber), received quantity, receipt count, dates
5. **Referências Técnicas** — Collapsed by default, expandable with chevron animation, shows all internal IDs in mono font

**Drawer layout (top to bottom):**
Summary Card → Detail Cards → Timeline Section

**Files Created:**
- `src/backend/AlplaPortal.Application/DTOs/Operations/OperationsTransferDetailDto.cs`
- `src/backend/AlplaPortal.Application/Interfaces/Operations/IOperationsTransferDetailService.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/Operations/OperationsTransferDetailQueryBuilder.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/Operations/OperationsTransferDetailService.cs`

**Files Modified:**
- `src/backend/AlplaPortal.Api/Controllers/OperationsController.cs` — New details endpoint
- `src/backend/AlplaPortal.Api/Program.cs` — DI registration
- `src/frontend/src/types/operations.types.ts` — 6 detail interfaces
- `src/frontend/src/lib/operationsApi.ts` — `fetchOperationsTransferDetails()`
- `src/frontend/src/pages/Operations/OperationsTransfersPage.tsx` — Parallel loading, 5 detail cards
- `src/frontend/src/config.ts` — APP_VERSION → `2.171.0`
- `docs/VERSION.md`, `docs/CHANGELOG.md`

**What is NOT included:**
- No barcode tracking
- No `ALL` plant aggregation
- No deployment changes
- No write operations to AlplaPROD

---

## [v2.170.0] - 2026-06-01

### Changed — Operations List: Stage Column + Filter Extension

**Problem:**
The transfer list only showed the administrative PO status (e.g., `Submetido`) but not the operational situation. Users needed to open each drawer to understand the actual stage. Also, filter options lacked `Submetidos` and `Parcialmente entregues`.

**Frontend changes:**

1. **New column `Situação`**: Shows approximated operational stage per list row using `deriveListStage()`. Color-coded badge based on `mainStatus`:
   - `7, 8` → `Concluído` (green)
   - `5` → `Parcialmente entregue` (amber)
   - `2` → `Aguardando recebimento` (blue)
   - `6` → `Em processamento` (indigo)
   - `3` → `Cancelado` (red)
   - `1` → `Pendente` (gray)
   - default → `A verificar` (gray)

2. **Column rename**: `Status` → `Status PO`

3. **Filter label rename**: `Status` → `Status do pedido`

4. **New filter options**: `Submetidos` (SUBMITTED), `Parcialmente entregues` (PARTIALLY_DELIVERED)

5. **Eventos column simplified**: Shows count only (removed "Esperados:" prefix)

**Backend changes:**

Extended `OperationsTransferListQueryBuilder`:
- `SUBMITTED` → `Status IN (2)`
- `PARTIALLY_DELIVERED` → `Status IN (5)`
- `ACTIVE` unchanged: `Status IN (1, 2, 6)` for backward compatibility

**Important:** List-level `Situação` is an approximation. The drawer timeline remains the source of truth for exact operational stage.

**Files Modified:**
- `src/frontend/src/pages/Operations/OperationsTransfersPage.tsx` — Table, filters, `deriveListStage()`
- `src/frontend/src/config.ts` — APP_VERSION → `2.170.0`
- `src/backend/.../OperationsTransferListQueryBuilder.cs` — Filter mapping
- `docs/VERSION.md`, `docs/CHANGELOG.md`

**What is NOT included:**
- No transfer details endpoint
- No barcode tracking
- No `ALL` plant aggregation
- No deployment changes

---

## [v2.169.0] - 2026-06-01

### Changed — Operations Stage Derivation: Status-Aware Logic

**Problem:**
`Etapa atual` only considered completed events (`isCompleted === true`). This produced misleading results: a transfer with a pending `GR_CREATED` and completed `EDI_SYNCED` events showed `EDI sincronizado` instead of `Aguardando recebimento`.

**Root cause:**
The previous `deriveCurrentStage` function filtered events by `isCompleted` before checking priority. This ignored pending events that represent a more advanced operational stage.

**New logic:**
Stage derivation now considers ALL events in the timeline regardless of completion status. The most advanced event by priority determines the stage. The label is then resolved considering whether that event is completed or pending.

**Status-aware labels:**

| Event Code | Completed | Label |
|------------|-----------|-------|
| `GR_COMPLETED` | — | `Recebimento concluído` |
| `GR_CREATED` | ✅ | `Recebimento concluído` |
| `GR_CREATED` | ❌ | `Aguardando recebimento` |
| `INHOUSE_DELIVERY` | — | `Entrega interna criada` |
| `LOADING_ORDER` | ✅ | `Carregamento concluído` |
| `LOADING_ORDER` | ❌ | `Carregamento em andamento` |
| `LOADING_PLANNED` | — | `Carregamento planejado` |
| `CALLOFF_CREATED` | — | `Abruf criado` |
| `EDI_SYNCED` | — | `Enviado para planta solicitante` |
| `EDI_EXPORTED` | — | `EDI exportado` |
| `EDI_CREATED` | — | `Documento EDI criado` |
| `PO_REVISION` | — | `Pedido revisado` |
| `PO_CREATED` | — | `Pedido criado` |
| (no events) | — | `Sem eventos encontrados` |

Completion check uses: `isCompleted || mainStatus === 21 || statusMeaning === 'Concluído'`.

**Status do pedido remains unchanged** — still derived from `PO_CREATED.statusMeaning`.

**Files Modified:**
- `src/frontend/src/pages/Operations/OperationsTransfersPage.tsx` — `deriveCurrentStage`, `resolveStageLabel`, `isEventStatusCompleted` functions.
- `src/frontend/src/config.ts` — APP_VERSION → `2.169.0`.
- `docs/VERSION.md` — v2.169.0.
- `docs/CHANGELOG.md` — This entry.

**What is NOT included:**
- No backend endpoint changes
- No transfer details endpoint
- No barcode tracking
- No `ALL` plant aggregation
- No deployment changes

---

## [v2.168.0] - 2026-06-01

### Changed — Operations Summary Card: Business-Oriented UX

**Problem:**
The summary card showed `Eventos concluídos: 6 / 10 (60%)` with a progress bar, even for business-complete POs. This confused users into thinking the process was incomplete.

**Root cause:**
`ExpectedEventCount` represents the maximum possible timeline steps for the pipeline model, not a business completion denominator. Some steps may never occur depending on the operational path.

**Removed:**
- `Eventos concluídos: X / Y (%)` ratio field
- Progress bar based on `CompletedEventCount / ExpectedEventCount`
- `Eventos esperados` as a primary metadata field

**Added:**
- **Status do pedido** — Derived from the `PO_CREATED` event's `statusMeaning` field (backend-resolved from `T_Bestellungen.Status`). Shows business labels like `Concluído`, `Submetido`, `Parcialmente entregue`, `Cancelado`. Displayed as a color-coded badge using existing severity mapping.
- **Etapa atual** — Derived from the most advanced completed business event in the timeline. Uses a priority order from `PO_CREATED` (lowest) to `GR_COMPLETED` (highest). Shows Portuguese business labels like `Recebimento concluído`, `EDI sincronizado`.
- **Eventos encontrados** — Simple count of `events.length`. No misleading comparison to expected.
- **Etapas possíveis do modelo** — De-emphasized footnote (`opacity: 0.7`, small text, border-top separator) showing the pipeline model's maximum step count for technical reference only.

**Derivation logic (frontend-only):**

Status do pedido:
1. Find event with `eventCode === 'PO_CREATED'`
2. Use `event.statusMeaning` + `event.severity`
3. Fallback: `Desconhecido` / `info`

Etapa atual (priority, highest = most advanced):
1. `GR_COMPLETED` → Recebimento concluído
2. `GR_CREATED` → Recebimento criado
3. `INHOUSE_DELIVERY` → Entrega interna criada
4. `LOADING_ORDER` → Ordem de carregamento
5. `LOADING_PLANNED` → Carregamento planejado
6. `CALLOFF_CREATED` → Abruf criado
7. `EDI_SYNCED` → EDI sincronizado
8. `EDI_EXPORTED` → EDI exportado
9. `EDI_CREATED` → Documento EDI criado
10. `PO_REVISION` → Pedido revisado
11. `PO_CREATED` → Pedido criado
- No completed events → `Sem eventos encontrados`

**Files Modified:**
- `src/frontend/src/pages/Operations/OperationsTransfersPage.tsx` — SummaryCard refactored with business logic.
- `src/frontend/src/config.ts` — APP_VERSION → `2.168.0`.
- `docs/VERSION.md` — v2.168.0.
- `docs/CHANGELOG.md` — This entry.

**What is NOT included:**
- No backend endpoint changes
- No transfer details endpoint
- No barcode tracking
- No `ALL` plant aggregation
- No deployment changes

---

## [v2.167.0] - 2026-06-01

### Changed — Operations Module: Quick Viewer Drawer + Status 5 Fix

**UX Change — Quick Viewer Drawer:**
Timeline viewing moved from a below-list panel to a right-side Quick Viewer Drawer. Improvements:
- Clicking a transfer row opens a 600px slide-in drawer from the right (full-width on small screens).
- Backdrop overlay dims the page behind the drawer.
- Spring animation for smooth open/close transitions.
- Keyboard dismiss: `Escape` key closes the drawer.
- Body scroll lock while drawer is open.
- Drawer header: PO number, journal number, plant, pipeline badge (STANDARD/INHOUSE/PARTIAL), status badge.
- Drawer body: SummaryCard + TimelineSection (reused from Phase 3).
- Close button with hover state. Clicking backdrop also closes.
- Closing drawer clears selection.
- Selected row remains highlighted in the list.

**Bug Fix — Status 5 mapping (`T_Bestellungen.Status = 5`):**
- Previously displayed as `Desconhecido (5)`.
- Now correctly mapped to `Parcialmente entregue` with severity `warning`.
- `isCompleted = false` — the PO is not terminal in this status.
- Validated against PO 3425 in AlplaPURCHASE, where the order appears as partially delivered/fulfilled.

**Files Modified:**
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/Operations/OperationsStatusMapper.cs` — Added status 5 → `Parcialmente entregue` (warning).
- `src/frontend/src/pages/Operations/OperationsTransfersPage.tsx` — Replaced `TimelinePanel` with `QuickViewerDrawer`.
- `src/frontend/src/config.ts` — APP_VERSION → `2.167.0`.
- `docs/VERSION.md` — v2.167.0.
- `docs/CHANGELOG.md` — This entry.

**What is NOT included:**
- No transfer details endpoint.
- No barcode tracking.
- No `ALL` plant aggregation.
- No deployment changes.

---

## [v2.166.0] - 2026-06-01

### Added — Operations Module Phase 5: Frontend List Integration

Frontend list integration for the `/operations/transfers` page. Evolves the page from manual-lookup-only to a full filter → list → timeline experience.

**Page Layout (top to bottom):**
1. **Filter panel** — Plant dropdown (VIANA1/2/3), date range inputs (required, max 90 days), status filter (Todos/Ativos/Concluídos/Cancelados), PO/journal search, material search, page size selector.
2. **Paginated transfer list** — Table with pipeline badges (STANDARD/INHOUSE/PARTIAL), severity-colored status badges, dates, material name, quantity, expected event count.
3. **Timeline panel** — Loads when user clicks a transfer row. Uses existing `GET /api/operations/transfers/{plant}/{id}/timeline`.
4. **Manual lookup fallback** — Collapsible section retaining the Phase 3 manual lookup. Includes test hint: "VIANA1/26, VIANA2/26, VIANA3/5".

**API Client:**
- `fetchOperationsTransfers(filters)` — Builds query string from filters, calls `GET /api/operations/transfers`. Trims whitespace, omits empty optional params.

**TypeScript Types:**
- `OperationsTransferListItem` — 21-field DTO matching backend `OperationsTransferListItemDto`.
- `OperationsTransferListResponse` — Paginated wrapper with metadata.
- `OperationsTransferListFilters` — Filter form state.

**Behavior:**
- No auto-search on page load — user must click "Pesquisar transferências".
- Filter change resets page to 1.
- New search clears selected transfer and previous timeline.
- `completedEventCount = null` rendered as "Esperados: N" — no false progress bar.
- `packagingName = null` and `articleVariantType = null` rendered as `—`.
- Client-side validation: plant required, dates required, dateFrom ≤ dateTo, max 90 days.
- Error handling: 400 (API message), 503 (integration unavailable), 500 (generic). All Portuguese. No secrets/SQL/traces.

**Files Modified:**
- `src/frontend/src/types/operations.types.ts` — Added 3 list types.
- `src/frontend/src/lib/operationsApi.ts` — Added `fetchOperationsTransfers()`.
- `src/frontend/src/pages/Operations/OperationsTransfersPage.tsx` — Full rewrite.
- `src/frontend/src/config.ts` — APP_VERSION → `2.166.0`.
- `docs/VERSION.md` — v2.166.0.
- `docs/CHANGELOG.md` — This entry.

**What is NOT included:**
- No backend changes.
- No transfer details endpoint or page.
- No barcode tracking.
- No `ALL` plant aggregation.
- No deployment changes.

---

## [v2.165.0] - 2026-06-01

### Added — Operations Module Phase 4: Transfer List API

Backend endpoint for paginated, filterable listing of purchase orders/transfers from AlplaPROD.

**Endpoint:**
- `GET /api/operations/transfers?plant=VIANA1&dateFrom=2026-05-01&dateTo=2026-05-31&page=1&pageSize=25`

**Query Parameters:**
- `plant` (required): VIANA1, VIANA2, VIANA3
- `dateFrom` / `dateTo` (required): date range filter on `T_Bestellungen.Add_Date`, max 90 days
- `status` (optional): ACTIVE (1,2,6), COMPLETED (7,8), CANCELLED (3)
- `articleSearch` (optional): LIKE search on `T_Artikelvarianten.Bezeichnung` / `Alias`
- `poSearch` (optional): LIKE search on `IdBestellung` / `JournalNummer`
- `page` / `pageSize` (optional): pagination, default 1/25, max pageSize 100

**Files Created:**
- `src/backend/AlplaPortal.Application/DTOs/Operations/OperationsTransferListItemDto.cs`
- `src/backend/AlplaPortal.Application/DTOs/Operations/OperationsTransferListResponseDto.cs`
- `src/backend/AlplaPortal.Application/Interfaces/Operations/IOperationsTransferListService.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/Operations/OperationsTransferListQueryBuilder.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/Operations/OperationsTransferListService.cs`

**Files Modified:**
- `src/backend/AlplaPortal.Api/Controllers/OperationsController.cs` — Added `GetTransferList` action.
- `src/backend/AlplaPortal.Api/Program.cs` — Registered `IOperationsTransferListService`.
- `src/frontend/src/config.ts` — APP_VERSION → `2.165.0`.
- `docs/VERSION.md` — v2.165.0.
- `docs/CHANGELOG.md` — This entry.

**Design decisions (documented):**
- `CompletedEventCount` = null in list results — too expensive per-row; use timeline endpoint.
- `PackagingName` = null — `T_VpkVorschrift` join deferred until validated.
- `QuantityUnit` = null — `T_Bestellpositionen` has no unit column.
- `ALL` plant value deferred — requires multi-server aggregation.
- OUTER APPLY TOP 1 for `T_Bestellpositionen` — guarantees 1 row per PO.

**What is NOT included:**
- No frontend list UI.
- No transfer details endpoint.
- No barcode tracking.
- No `ALL` plant aggregation.
- No deployment changes.

---

## [v2.164.0] - 2026-05-31

### Added — Operations Module Phase 3: Frontend MVP — Transferências Logísticas

First user-facing Operations screen at `/operations/transfers`. Manual timeline lookup UI for querying transfer timelines from AlplaPROD.

**Page Features:**
- Search panel: plant dropdown (VIANA1/VIANA2/VIANA3) + IdBestellung input + search button.
- Summary card: plant info, pipeline model badge, event completion progress bar, query duration.
- Timeline: severity-colored event cards with completion icons, `Técnico` badges for technical events.
- Renders all events in API order — does not collapse events at same `sortOrder`.
- Client-side validation: plant required, IdBestellung must be positive integer.
- Error states: 400 (API message), 404 (not found), 503 (integration unavailable), 500 (generic). All in Portuguese.
- Loading and empty states with framer-motion animations.

**Files Created:**
- `src/frontend/src/types/operations.types.ts` — TypeScript DTOs.
- `src/frontend/src/lib/operationsApi.ts` — API client helper.
- `src/frontend/src/pages/Operations/OperationsTransfersPage.tsx` — Page component.

**Files Modified:**
- `src/frontend/src/constants/navigation.tsx` — Added `Operações > Transferências Logísticas`.
- `src/frontend/src/components/layout/Sidebar.tsx` — Added tour attribute.
- `src/frontend/src/App.tsx` — Lazy import + route with `AdminRoute` guard.
- `src/frontend/src/styles/globals.css` — Added `.spin-icon` utility.
- `src/frontend/src/config.ts` — APP_VERSION → `2.164.0`.
- `docs/VERSION.md` — v2.164.0.
- `docs/CHANGELOG.md` — This entry.

**Security Fix:**
- Added `[Authorize]` attribute to `OperationsController` — endpoint was unprotected. Now requires JWT authentication, matching all other controllers in the project.

**What is NOT included:**
- No transfer list endpoint.
- No transfer details endpoint.
- No barcode tracking.
- No backend business changes.
- No deployment changes.

---

## [v2.163.0] - 2026-05-31

### Added — Operations Module Phase 2: Timeline API

Backend API endpoint for querying transfer timelines from AlplaPROD production databases.

**Endpoint:**
- `GET /api/operations/transfers/{plant}/{idBestellung}/timeline`

**DTOs:**
- `OperationsTimelineEventDto` — normalized event with status mapping, severity, entity references.
- `OperationsTimelineResponseDto` — timeline wrapper with pipeline model, event counts, query metrics.

**Services:**
- `IOperationsTimelineService` / `OperationsTimelineService` — orchestrates connection, query, and mapping.
- `IOperationsPipelineDetector` / `OperationsPipelineDetector` — config-based pipeline model detection.
- `OperationsTimelineQueryBuilder` — parameterized SQL for Standard (10 events) and Inhouse (7 events).
- `OperationsStatusMapper` — Portuguese status labels + severity from confirmed Script 14 rules.

**Controller:**
- `OperationsController` — timeline endpoint with proper error handling (400/404/503/500).

**Architecture:**
- `AlplaProdPlant` and `AlplaProdPipelineModel` enums relocated to `AlplaPortal.Domain.Enums` for cross-layer accessibility.
- Infrastructure re-exports via `global using` for backward compatibility.

**Error handling:**
- Invalid plant → 400 Bad Request
- Integration disabled / plant disabled / missing credentials → 503 Service Unavailable
- Transfer not found → 404 Not Found
- SQL timeout / connection failure → 503 Service Unavailable
- All error messages in Portuguese. No secrets, connection strings, or stack traces in responses.

**What is NOT included:**
- No transfer list endpoint.
- No transfer details endpoint.
- No frontend screens.
- No deployment changes.
- AlplaPROD access remains strictly read-only (SELECT only).

---

## [v2.162.0] - 2026-05-31

### Added — Operations Module Phase 1: AlplaPROD Backend Foundation

Backend infrastructure for the future Operations module connecting to AlplaPROD 1.0 production databases (Viana 1, Viana 2, Viana 3).

**What is included:**
- `AlplaProdPlant` enum — `VIANA1`, `VIANA2`, `VIANA3` plant routing.
- `AlplaProdPipelineModel` enum — `STANDARD`, `INHOUSE`, `PARTIAL` pipeline models.
- `AlplaProdConnectionFactory` — Read-only, multi-server, multi-database connection factory with per-plant server/database configuration.
- `AlplaProdIntegrationProvider` — `IIntegrationProvider` health check that tests ALL enabled plants and aggregates results.
- `appsettings.json` — `Integrations:AlplaProd` section with placeholder configuration (no real credentials).
- Seed data — `IntegrationProvider` Id=5 (Code=`ALPLAPROD`, DisplayOrder=40, IsPlanned=true, IsEnabled=false).
- Seed data — `IntegrationConnectionStatus` Id=5 (Status=`PLANNED`).
- DI registration — `AlplaProdConnectionFactory` + `AlplaProdIntegrationProvider` registered in `Program.cs`.

**What is NOT included:**
- No timeline API, no transfer list API, no transfer details API.
- No OperationsController.
- No timeline SQL queries or query builders.
- No frontend screens.
- No deployment changes.
- AlplaPROD access remains strictly read-only.

**Health check diagnostic query (read-only):**
```sql
SELECT @@SERVERNAME, DB_NAME(), SYSTEM_USER, GETDATE();
```

**Files Created:**
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/AlplaProdPlant.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/AlplaProdPipelineModel.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/AlplaProdConnectionFactory.cs`
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/AlplaProdIntegrationProvider.cs`

**Files Modified:**
- `src/backend/AlplaPortal.Api/appsettings.json` — Added `Integrations:AlplaProd` section.
- `src/backend/AlplaPortal.Api/Program.cs` — DI registration.
- `src/backend/AlplaPortal.Infrastructure/Data/ApplicationDbContext.cs` — Seed data.
- `docs/OPERATIONS_MODULE_TECHNICAL_DESIGN.md` — Phase 1 status updated.
- `docs/VERSION.md` — Bumped to v2.162.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.162.0".

## [v2.161.0] - 2026-05-29

### Added — Quotation Management Live Guide (v1.0.0)
- New reusable Live Guide for the Buyer's quotation management workspace (`/buyer/items`).
- 11 assistive steps covering the full buyer workflow: Introduction, Header, Search/Filters, Request Card, Expand Button, Assignment, Request Summary, Items, Documents/Quotations, Add Quotation (OCR/Manual), Complete Quotation.
- Factory function pattern (`createQuotationManagementGuide`) with state getter to avoid stale-closure risks.
- All steps use `requiredAction: 'none'` — the guide is explanatory, not mandatory.
- Conditional steps auto-skip when targets are not visible (empty list, card not expanded, not assigned).
- Empty state handling: intro warns when no requests are visible; card-level steps are safely skipped.
- Assignment step adapts content: unassigned → "Atribuir a Mim" explanation; assigned to self → ownership confirmed; assigned to other → explains limited actions.
- Rich JSX content for Add Quotation step (OCR vs Manual entry).
- `data-guide` attributes applied only to the first request group to avoid duplicate Joyride targets.
- `LiveGuideLauncher` button added to page header next to existing Tour and Manual buttons.

**Files Changed**:
- `src/frontend/src/features/guided-tour/live-guide/liveGuideTypes.ts` — Extended `LiveGuideId` union.
- `src/frontend/src/features/guided-tour/live-guide/liveGuideRegistry.ts` — Added registry entry.
- `src/frontend/src/features/guided-tour/live-guide/guides/quotationManagement.liveGuide.tsx` — [NEW] Guide definition.
- `src/frontend/src/pages/Buyer/BuyerItemsList.tsx` — Added `data-guide` attributes, launcher, registration.
- `docs/VERSION.md` — Bumped to v2.161.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.161.0".
- `docs/GUIDED_TOUR_SYSTEM.md` — Updated Live Guides table.

## [v2.160.0] - 2026-05-29

### Changed — Requests Page Guided Tour Updated for Timeline Toggle Button
- Tour step 8 now targets `[data-tour="request-timeline-toggle"]` (the chevron button on the left of the first row).
- Old title "Clique na Linha para Expandir" replaced with "Ver Timeline do Pedido".
- Old content instructing to click the row replaced with instructions to click the left-side button.
- Placement changed from `top` to `right` for better visual alignment with the button.
- `data-tour="request-timeline-toggle"` added to the first row's chevron button only (avoids duplicate targets).
- Empty request list: step is automatically skipped by `filterActiveSteps`.

**Files Changed**:
- `src/frontend/src/features/guided-tour/tours/requestsPageTour.ts` — Step 8 target, title, content, placement.
- `src/frontend/src/pages/Requests/components/modern/RequestsTableWidget.tsx` — `data-tour` attribute + `reqIndex`.
- `docs/VERSION.md` — Bumped to v2.160.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.160.0".

## [v2.159.0] - 2026-05-29

### Added — Timeline Expand/Collapse Button on Requests Table
- Added a visible expand/collapse chevron button as the first column of the Requests list table.
- Closed state: ChevronRight icon, tooltip "Ver timeline do pedido".
- Open state: ChevronDown icon, tooltip "Ocultar timeline do pedido", filled primary-color background.
- Industrial Brutalist styling: clear 1.5px border, strong hover state (blue accent), focus ring.
- Accessibility: `aria-expanded`, `aria-controls` linking to `role="region"` timeline panel, keyboard Enter/Space.
- Row-click still works as secondary interaction.
- Updated colSpan from 8 to 9 for timeline and empty-state rows.

**Files Changed**:
- `src/frontend/src/pages/Requests/components/modern/RequestsTableWidget.tsx` — Expand column + button + a11y.
- `docs/VERSION.md` — Bumped to v2.159.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.159.0".

## [v2.158.0] - 2026-05-29

### Fixed — Guided Tour on Mandatory Password Change
- The general guided tour welcome modal was appearing during mandatory password change. Fixed by adding route (`/change-password`) and `mustChangePassword` guards to the auto-trigger `useEffect` in `useGuidedTour.ts`. Tour remains pending and triggers normally after password change.

### Fixed — Department Selector Restricted to User Scope
- The department dropdown in Request Creation showed all active departments. Now filtered using `allowedDepartmentCodes` from `/api/v1/users/me`, mirroring the existing plant scope pattern. Auto-selects when only one department is in scope. Backend validation added (HTTP 403) in `CreateRequest` and `UpdateRequestDraft`.

### Changed — Live Guide Copy Updates (v1.4.0)
- **Grau de Necessidade step**: Rich JSX explaining each urgency level (Crítico, Urgente, Normal, Baixo) with color-coded labels.
- **Departamento step**: Explains scope-based filtering and guides users to contact admin if needed.

**Files Changed**:
- `src/frontend/src/features/guided-tour/useGuidedTour.ts` — Route + mustChangePassword guards.
- `src/frontend/src/pages/Requests/RequestCreate.tsx` — `allowedDepartmentCodes` state, filtering, auto-selection.
- `src/backend/AlplaPortal.Api/Controllers/RequestsController.cs` — Department scope validation.
- `src/frontend/src/features/guided-tour/live-guide/guides/requestCreation.liveGuide.tsx` — Updated copy; version → 1.4.0.
- `docs/VERSION.md` — Bumped to v2.158.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.158.0".

## [v2.157.0] - 2026-05-28

### Added — Reusable Live Guide System & Request Creation Live Guide
- **Live Guide Infrastructure**: New reusable system under `src/frontend/src/features/guided-tour/live-guide/` for interactive, step-by-step task guidance with input validation. Extends the existing Guided Tour architecture using a separate `data-guide` attribute namespace.
- **Core Types**: `LiveGuideStep` and `LiveGuideDefinition` supporting `string | ReactNode` content, step conditions, validation functions, required actions (`input`, `select`, `upload`, `none`), and skip/fallback behaviors.
- **Guide Lifecycle Hook** (`useLiveGuide.ts`): Manages start, next, prev, skip, close, and complete actions. Includes `findNextValidStep` for conditional step resolution and a **target-awaiting mechanism** that retries up to 550ms for DOM targets rendered by AnimatePresence animations.
- **Provider & Custom Tooltip** (`LiveGuideProvider.tsx`): Wraps Joyride in controlled mode with a custom tooltip component. Uses a dedicated `TooltipDataContext` to bypass Joyride's memoization and propagate real-time validation state.
- **Launcher Button** (`LiveGuideLauncher.tsx`): Reusable component for explicit guide activation.
- **Persistence** (`liveGuideStorage.ts`): localStorage-backed completion/dismissal tracking.
- **Request Creation Live Guide** (`requestCreation.liveGuide.tsx`): Factory-based guide definition with 12 steps covering the full request creation flow. Includes rich JSX tooltips (bold Cotação/Pagamento labels with bullet examples), DOM-first conditional step conditions, and input validation that blocks progression until required fields are filled.
- **Conditional Steps**: Cotação → "Itens Solicitados" section; Pagamento → "Input de Documento & Faturamento" section. Steps appear only for the selected request type and wait for the animated DOM target to render.
- **No Auto-Start**: Guide starts only from explicit user action ("Guia ao vivo" button).

**Files Changed**:
- `src/frontend/src/features/guided-tour/live-guide/*.ts(x)` — [NEW] 6 files: types, hook, provider, launcher, storage, guide definition.
- `src/frontend/src/features/guided-tour/GuidedTourProvider.tsx` — Integrated LiveGuideProvider.
- `src/frontend/src/features/guided-tour/GuidedTourButton.tsx` — Added LiveGuideLauncher.
- `src/frontend/src/pages/Requests/RequestCreate.tsx` — Added `data-guide` attributes and guide registration.
- `docs/GUIDED_TOUR_SYSTEM.md` — Added Live Guide architecture section.
- `docs/VERSION.md` — Bumped to v2.157.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.157.0".

## [v2.156.4] - 2026-05-28

### Fixed — Edge "Not Secure" Mixed Content Warning on TEST
- **ForwardedHeaders Middleware Enabled**: Uncommented and configured `ForwardedHeaders` in `Program.cs` with `XForwardedFor` and `XForwardedProto`. Added `app.UseForwardedHeaders()` as the first middleware call. Without this, `UseHttpsRedirection()` saw plain HTTP from the IIS ARR reverse proxy and could generate broken 307 redirects to internal localhost URLs.
- **HTTP→HTTPS Redirect Rule**: Added a permanent (301) redirect rule as the first IIS URL Rewrite rule in `src/frontend/public/web.config`. The IIS site has both `:80` and `:443` bindings; without this rule, the portal was accessible over plain HTTP.
- **No Hardcoded Insecure URLs**: Confirmed zero `http://` browser-visible URLs in the frontend codebase. `API_BASE_URL` defaults to `''` (same-origin relative). All login page resources use relative paths.

## [v2.156.3] - 2026-05-28

### Fixed — Suppliers Baseline Schema Correction
- **Missing Columns in ConsolidatedBaseline**: The `Suppliers` table in the baseline migration was missing 3 columns (`Origin`, `SourceCompany`, `LastSyncedAtUtc`) that existed in the entity model and snapshot but were never created by any migration. Clean database installs caused runtime `SqlException: Invalid column name` errors in `ProformaDeadlineAlertService`.
- **Baseline Fix**: Added the 3 columns to the ConsolidatedBaseline `CreateTable` and `Designer.cs`. Updated seed data to include `Origin = "MANUAL"`.
- **Post-Install Validation**: Added the 3 Supplier columns to critical column checks.
- **No New Migration Required**: Snapshot was already correct; this fixes the baseline for clean installs only.

## [v2.156.2] - 2026-05-28

### Fixed — Primavera ERP Default SQL Server Instance
- **Connection String Builder**: `PrimaveraConnectionFactory.BuildConnectionString` now treats `MSSQLSERVER`, `DEFAULT`, empty, and whitespace instance names as the default SQL Server instance — producing `Server=host` instead of the invalid `Server=host\MSSQLSERVER` that caused "SQL Network Interfaces, error: 25 - Connection string is not valid."
- **Frontend Normalization**: Instance field value is trimmed and normalized to empty when saving `MSSQLSERVER` or `DEFAULT`, preventing bad data from being persisted to the database.
- **UI Improvement**: Instance field label now shows "(opcional)" with helper text: "Para a instância padrão do SQL Server, deixe este campo vazio."

## [v2.156.1] - 2026-05-27

### Improved — Deployment Tooling & Post-Install Validation
- **Admin User Seed Template**: Enhanced `docs/ADMIN_USER_SEED_TEMPLATE.sql` — now fully idempotent (creates new or updates existing users), assigns all 12 roles via safe `INSERT...WHERE NOT EXISTS`, assigns all active plants and departments dynamically.
- **Post-Install Validation**: Added `InformationalNotifications.Category` and `EventCorrelationId` to critical column checks (these were missing on TEST causing `/api/v1/notifications` 500). Added Step 5b: Admin User Bootstrap Validation — warns if no active System Administrator with plant/department scopes exists.
- **Password Hash Generator**: New `tools/PasswordHasher` — standalone .NET 8 console tool using `BCrypt.Net-Next 4.1.0` for generating admin seed password hashes. Referenced by `ADMIN_USER_SEED_TEMPLATE.sql`.

## [v2.156.0] - 2026-05-27

### Added — Migration Consolidation & Deployment Hardening
- **Consolidated Baseline Migration**: New `20260225000000_ConsolidatedBaseline` EF Core migration replacing 41 deleted migration files. Creates all 29 foundational tables, indexes, foreign keys, and seed data. Enables clean database installations via standard EF Core migration pipeline.
- **Startup Schema Validation**: `Program.cs` validates 14 critical tables after migration. In TEST/PRODUCTION, failure crashes the application. In Development, failures log a warning and continue.
- **Post-Install Validation Script**: New `docs/POST_INSTALL_DATABASE_VALIDATION.sql` — read-only SQL validating table existence, columns, seed data, FK integrity.
- **Deployment Checklist**: New `docs/DEPLOYMENT_CHECKLIST.md` — full deployment procedure including local development database setup.
- **Admin User Seed Template**: New `docs/ADMIN_USER_SEED_TEMPLATE.sql` — parameterized SQL template for first admin user creation.

### Changed
- Startup logging prefix changed from `[DEBUG]` to `[STARTUP]`.
- Added `/api/v1/lookups/request-types` and `/api/v1/iva-rates` to deployment validation checks.

### Database
- Migration: `20260225000000_ConsolidatedBaseline` required for all environments.
- Existing databases must register the baseline in `__EFMigrationsHistory` BEFORE deploying.
- Local development: recommended clean recreate via `dotnet ef database drop --force && dotnet ef database update`.

## [v2.155.2] - 2026-05-26

### Fixed — RequestCreate Scope Loading Resilience
- **Decoupled API Calls**: Separated `/me` (live user profile) from the 8 auxiliary lookups in `RequestCreate.tsx` into independent try/catch blocks. This prevents unrelated lookup failures from causing a false "ACESSO RESTRITO" error.
- **Detailed Error Banner**: Added a red "ERRO AO CARREGAR PERFIL" banner with Reload and Dashboard buttons when `/me` fails.
- **Auxiliary Lookups Warning**: Added an amber warning banner if auxiliary lookups fail, while preserving the valid user plant scope and keeping the creation form interactive.
- **Safe Diagnostic Logs**: Added three safe, non-sensitive `console.info` statements logging loaded plant counts, lookup status, and filter metrics for local debugging without exposing secrets.
- **Diagnostic Guide**: Created a comprehensive guide `docs/REQUEST_CREATE_ACCESS_RESTRICTED_DIAGNOSTIC.md` featuring DevTools instructions, network status lookup guides, and read-only database queries to troubleshoot access rules.

**Files Changed**:
- `src/frontend/src/pages/Requests/RequestCreate.tsx` — Decoupled `/me` loading, added errors and logs.
- `docs/REQUEST_CREATE_ACCESS_RESTRICTED_DIAGNOSTIC.md` — [NEW] Diagnostic checklist guide.
- `docs/VERSION.md` — Bumped to v2.155.2.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.155.2".

## [v2.155.1] - 2026-05-26

### Fixed — Post-Deployment TEST Environment Issues (CI/CD)
- **Blank Page Fix**: `Copy-Item` in `deploy-test.yml` flattened the Vite `dist/assets/` subdirectory during artifact staging. Replaced with `robocopy /E` to preserve the full directory tree. Added a validation step that fails the build if `assets/` is missing or empty.
- **API URL Duplication Fix**: Changed `API_BASE_URL` default in `api.ts` from `'/api'` to `''`. Every endpoint path already includes `/api/...`, so the old default produced double `/api/api/...` paths in production builds. Development (`VITE_API_BASE_URL=http://localhost:5000`) is unaffected.
- **Frontend web.config**: Created `src/frontend/public/web.config` with two IIS URL Rewrite rules: (1) reverse proxy from `/api/*` to `http://localhost:5001/api/*` (same-origin API routing), (2) SPA fallback to `index.html` for React Router. Requires IIS URL Rewrite Module + ARR on the server.
- **Documentation Update**: Expanded `docs/GITHUB_ACTIONS_TEST_DEPLOYMENT.md` with: reverse proxy architecture and ARR prerequisites, `ASPNETCORE_ENVIRONMENT=Test` configuration guide, `appsettings.Test.json` template and preservation strategy, API 500 diagnosis checklist, and post-deployment issue log table.

**Files Changed**:
- `.github/workflows/deploy-test.yml` — Replaced Copy-Item with robocopy + validation step.
- `src/frontend/src/lib/api.ts` — API_BASE_URL default changed from `'/api'` to `''`.
- `src/frontend/public/web.config` — [NEW] SPA fallback + reverse proxy rules.
- `docs/GITHUB_ACTIONS_TEST_DEPLOYMENT.md` — Expanded with 5 new sections.
- `docs/VERSION.md` — Bumped to v2.155.1.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION → "2.155.1".

## [v2.155.0] - 2026-05-26

### Added — GitHub Actions TEST Deployment Workflow (CI/CD)
- First automated CI/CD pipeline for deploying the Alpla Angola Portal Gerencial to the TEST environment on `AOVIA1VMS011`.
- Workflow: `.github/workflows/deploy-test.yml` — manual trigger (`workflow_dispatch`) with version input.
- Build job on `windows-latest`: .NET 8 restore → build → publish, Node.js 20 npm ci → tsc → vite build.
- Deploy job on self-hosted runner: timestamped backups, IIS App Pool stop/start, file deployment with config preservation, smoke test.
- Environment-specific `appsettings.*.json` files preserved on the server during deployment.
- Documentation: `docs/GITHUB_ACTIONS_TEST_DEPLOYMENT.md` — full deployment guide with prerequisites, IIS config, certificate info, rollback, and troubleshooting.
- No secrets committed, no production touched, port 5000 never used, EF migrations intentionally not automated.

## [v2.154.0] - 2026-05-25

### Added — Primavera ERP Connection Validation & Health Consistency Corrections

**Summary**: Resolved the connection testing validation messages and state discrepancies for the Primavera ERP integration in the **Gestão de Integrações (Integration Management)** module. Implemented 6 strict sequential validation checks in Portuguese before executing SQL connections, returning exact user-friendly diagnostics. Aligned the DTO enabled state directly with `provider.IsEnabled` in the database, resolving configuration button inconsistencies. Updated display status calculations to dynamically evaluate Primavera based on company-specific configurations (showing `"Inativo"` when disabled, `"Não Configurado"` if company database/credentials are missing, and fallback health states). Added a visual warning badge reading `"Senha não configurada."` next to the status badge on active company cards.

**Key Updates**:
- **Sequential Validation Pipeline**: Integrated 6 sequential connection validations in Portuguese (`PrimaveraIntegrationProvider.cs`) before attempting SQL connections. Returns exact diagnostic warnings (e.g. database missing, username missing, password missing) directly.
- **Enabled State Alignment**: Refactored DTO mappings (`IntegrationSettingsService.cs` and `IntegrationHealthService.cs`) to strictly check `provider.IsEnabled` from the database directly, ensuring toggles and health cards reflect true database states.
- **Dynamic Display Status**: Updated the health state calculator (`IntegrationHealthService.cs`) to dynamically check all active Primavera companies and their database/credential completeness, returning `"Inativo"` or `"Não Configurado"` as appropriate.
- **UI Warning Badge**: Added a warning badge reading `"Senha não configurada."` next to the status badge on active company rows in `IntegrationSettings.tsx` to highlight missing credentials.

**Files Changed**:
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/PrimaveraConnectionFactory.cs` — Exposed company configuration state safely via `GetCompanySettingsAsync`.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/PrimaveraIntegrationProvider.cs` — Integrated the 6 Portuguese validation checks.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/IntegrationHealthService.cs` — Aligned health statuses and bypassed early connection test exits for Primavera.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/IntegrationSettingsService.cs` — Removed enabled state configuration fallback in `MapToDto`.
- `src/frontend/src/pages/Admin/IntegrationSettings.tsx` — Rendered `"Senha não configurada."` warning badge on active company cards.

### Added — Security Incident Response & Unified SMTP Integration Consolidation (DEC-135)

**Summary**: Addressed the GitGuardian alert by staging a security incident report (`docs/SECURITY_INCIDENT_GITGUARDIAN_SMTP_SECRET_LEAK.md`), confirming that development configuration secrets are untracked and gitignored, and removing hardcoded passwords from scripts (`scripts/query_innux.ps1`). Consolidated the SMTP configuration strictly inside the **Gestão de Integrações (Integration Management)** module, completely removing it from the legacy **Dados Mestres (Master Data)** tab. Refactored the C# backend to route provider `"SMTP"` CRUD operations to the existing database-backed `SmtpSettings` table, preventing duplicate databases or configurations. Implemented scoped `IIntegrationProvider` health check providers (`SmtpIntegrationProvider.cs` and `OpenAiIntegrationProvider.cs`) and deleted the obsolete `SmtpSettingsController.cs`. Developed an administrative modal (`ConnectionConfigureModal`) in the frontend to securely modify non-secret connection parameters in real time.

**Key Updates**:
- **Immediate Security Incident Response**: Created the detailed incident report outlining historical exposure context, credential rotation requirements for Leonardo, and git history scrubbing procedures via `git-filter-repo`. Removed the plaintext database password `ad#56&Hfe` from the tracked `scripts/query_innux.ps1` script, resolving it dynamically from the `INNUX_DB_PASSWORD` environment variable. Confirmed active HEAD cleanliness, `.gitignore` rules, and absence of active hardcoded secrets.
- **Dados Mestres SMTP Removal**: Stripped the SMTP tab from the sidebar menu, state hooks, and `<SmtpSettingsPanel>` render block out of `MasterData.tsx` to prevent duplicate configuration areas.
- **Unified SMTP Configuration Routing**: Refactored `IntegrationSettingsService.cs` to map read/write and password rotation operations for provider `"SMTP"` securely to the legacy `SmtpSettings` database table, preserving database-backed single-row constraints and AES encryption keys.
- **New Integration Health Providers**: Created `SmtpIntegrationProvider.cs` and `OpenAiIntegrationProvider.cs` implementing the `IIntegrationProvider` connection test contract. Registered them in the DI pipeline (`Program.cs`) and deleted `SmtpSettingsController.cs`.
- **Administrative Configuration UI**: Implemented `ConnectionConfigureModal` inside `IntegrationSettings.tsx` to support editing non-secret connection parameters (Primavera, Innux, OpenAI, SMTP) in a clean, responsive modal form, keeping secret inputs securely masked.
- **Guided Tour & Type Verification**: Confirmed that guided tours are unaffected by the SMTP master data removal, and verified type-safety compilation on backend (`dotnet build`) and frontend (`npx tsc --noEmit`) with 0 errors.

**Files Changed**:
- `docs/SECURITY_INCIDENT_GITGUARDIAN_SMTP_SECRET_LEAK.md` — [NEW] Security incident report document.
- `scripts/query_innux.ps1` — Replaced hardcoded plaintext password with environment variable.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/SmtpIntegrationProvider.cs` — [NEW] Unified SMTP test connection provider.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/OpenAiIntegrationProvider.cs` — [NEW] Unified OpenAI test connection provider.
- `src/backend/AlplaPortal.Api/Controllers/Admin/SmtpSettingsController.cs` — [DELETE] Obsolete legacy controller.
- `src/backend/AlplaPortal.Api/Program.cs` — Registered new integration health providers.
- `src/backend/AlplaPortal.Application/DTOs/Integration/IntegrationSettingsDtos.cs` — Added SMTP transport properties.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/IntegrationSettingsService.cs` — Routed SMTP settings securely to `SmtpSettings` table.
- `src/frontend/src/pages/Settings/MasterData.tsx` — Stripped legacy SMTP settings UI panel.
- `src/frontend/src/pages/Admin/IntegrationSettings.tsx` — Added expandable SMTP cards and administrative configuration modal.
- `src/frontend/src/lib/api.ts` — Cleaned up old SMTP API definitions.
- `src/frontend/src/types/index.ts` — Updated types for integration parameters.
- `src/frontend/src/config.ts` — APP_VERSION → "2.154.0".
- `docs/VERSION.md` — Updated to v2.154.0.
- `docs/CHANGELOG.md` — This entry.
- `docs/DECISIONS.md` — DEC-135.

## [v2.153.0] - 2026-05-25

### Added — Integration Management Module: CRUD UI, Factory Refactoring & Frontend Type Safety (DEC-134)

**Summary**: Implemented a complete Integration Management module enabling System Administrators to view, configure, test, and manage all integration provider settings (Primavera, Innux, OpenAI, SMTP) from a unified admin UI. Refactored all runtime services to resolve configuration from database-backed `IntegrationProviderSettings` first, with `IConfiguration`/environment variable fallback, and a safe disabled state when neither source is available. Performed comprehensive frontend type safety cleanup eliminating all `any` types from the integration API layer.

**Phase A — Architecture Review**:
- Comprehensive analysis of the existing 4-layer configuration cascade (IIS env vars → appsettings → DB rows).
- Identified 3 services requiring refactoring: `PrimaveraConnectionFactory`, `InnuxConnectionFactory`, `DocumentExtractionSettingsService`.
- Documented security findings: plaintext credentials in `appsettings.Development.json`, hardcoded AES fallback key.
- Architecture review saved to `docs/INTEGRATION_MANAGEMENT_ARCHITECTURE_REVIEW.md`.

**Phase B — CRUD API & Frontend UI**:
- **New Controller**: `IntegrationSettingsController` under `/api/admin/integration-settings` with 7 endpoints (GET all, GET by code, PUT settings, POST secret, POST test, POST enable, POST disable).
- **New Service**: `IntegrationSettingsService` — full CRUD orchestration with AES encryption for secrets, admin log audit trail, and `IntegrationHealthService` delegation for test connections.
- **New DTOs**: `IntegrationSettingsDto`, `UpdateIntegrationSettingsRequest`, `ReplaceIntegrationSecretRequest` with `[JsonPropertyName]` serialization.
- **Database Migration**: `AddIntegrationManagementUI` — seeds OPENAI and SMTP providers + `IntegrationProviderSettings` rows with default config.
- **Frontend Page**: `IntegrationSettings.tsx` at `/admin/integrations` — expandable provider cards, inline field editing, masked secret management (`SecretManager` component), real-time connection testing, enable/disable toggle.
- **Admin Tile**: Added "Configurar Integrações" tile to `AdministratorWorkspace.tsx`.
- **Route**: Registered `/admin/integrations` in `App.tsx` with `System Administrator` role guard.

**Phase C — Factory Refactoring (DB-First Configuration)**:
- **New Service**: `IntegrationConfigResolver` — scoped DI service implementing the 3-tier cascade: DB (`IntegrationProviderSettings` with `AesEncryptionHelper` decryption) → `IConfiguration` fallback → Safe disabled state.
- **PrimaveraConnectionFactory**: Now resolves Server, InstanceName, Username, and Password from `IntegrationConfigResolver.ResolveAsync("PRIMAVERA")` before falling back to `Integrations:Primavera` config section.
- **InnuxConnectionFactory**: Same DB-first resolution via `IntegrationConfigResolver.ResolveAsync("INNUX")`.
- **OpenAiDocumentExtractionProvider**: API key now resolved via `IntegrationConfigResolver.ResolveApiKeyAsync("OPENAI")` with `OPENAI_API_KEY` environment variable fallback.
- **DocumentExtractionSettingsService**: Test connection method also uses the resolver cascade.
- **DI Registration**: `IntegrationConfigResolver` registered as scoped in `Program.cs`.

**Phase D — Frontend Type Safety Cleanup**:
- Moved inline `IntegrationSettingsDto` from `IntegrationSettings.tsx` to shared `types/index.ts`.
- Added `UpdateIntegrationSettingsDto`, `ReplaceIntegrationSecretDto`, `IntegrationConnectionTestResultDto` to shared types.
- Replaced all `Promise<any>` return types in `api.ts` integration methods with strongly-typed DTOs.
- Replaced all `catch (err: any)` with `catch (err: unknown)` and safe `instanceof Error` message extraction.
- **Bug Fix**: Test connection handler was reading `result.currentStatus` and `result.lastResponseTimeMs` (properties from `IntegrationProviderStatusDto`) instead of `result.success` and `result.responseTimeMs` (from `IntegrationConnectionTestResultDto`). This would have caused all test results to display as failures.
- Added `data-tour="integrations-configure-btn"` anchor to provider card header for guided tour integration.

**Files Changed**:
- `src/backend/AlplaPortal.Api/Controllers/Admin/IntegrationSettingsController.cs` — [NEW] CRUD + secret rotation + test + enable/disable.
- `src/backend/AlplaPortal.Application/DTOs/Integration/IntegrationSettingsDtos.cs` — [NEW] GET/PUT/POST DTOs.
- `src/backend/AlplaPortal.Application/Interfaces/IIntegrationSettingsService.cs` — [NEW] Service interface.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/IntegrationSettingsService.cs` — [NEW] Service implementation.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/IntegrationConfigResolver.cs` — [NEW] DB-first config resolver.
- `src/backend/AlplaPortal.Infrastructure/Persistence/Migrations/AddIntegrationManagementUI.cs` — [NEW] Migration.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/PrimaveraConnectionFactory.cs` — Refactored to use resolver.
- `src/backend/AlplaPortal.Infrastructure/Services/Integration/InnuxConnectionFactory.cs` — Refactored to use resolver.
- `src/backend/AlplaPortal.Infrastructure/Services/Extraction/OpenAiDocumentExtractionProvider.cs` — Refactored to use resolver.
- `src/backend/AlplaPortal.Api/Program.cs` — DI registration for `IntegrationSettingsService` and `IntegrationConfigResolver`.
- `src/frontend/src/pages/Admin/IntegrationSettings.tsx` — [NEW] Integration management UI.
- `src/frontend/src/pages/Admin/AdministratorWorkspace.tsx` — Added integration tile.
- `src/frontend/src/App.tsx` — Route registration.
- `src/frontend/src/types/index.ts` — Added 4 integration DTO types.
- `src/frontend/src/lib/api.ts` — Typed integration API methods.
- `src/frontend/src/config.ts` — APP_VERSION → "2.153.0".
- `docs/INTEGRATION_MANAGEMENT_ARCHITECTURE_REVIEW.md` — [NEW] Phase A architecture review.
- `docs/VERSION.md` — v2.153.0.
- `docs/CHANGELOG.md` — This entry.
- `docs/DECISIONS.md` — DEC-134.

## [v2.152.0] - 2026-05-25

### Fixed — AOVIA1VMS011 Staging IIS Connection String Mismatch & Hardening (DEC-133)

**Summary**: Resolved the staging login connection failure (`HTTP 500` error) by diagnosing a mismatch between the environment variable written by the secure configuration script (`ConnectionStrings__PortalDatabase`) and the configuration key expected by the .NET 8 backend API (`builder.Configuration.GetConnectionString("DefaultConnection")` in `Program.cs`). Patched the secure local PowerShell configuration script to map the correct `ConnectionStrings__DefaultConnection` variable in IIS using `Microsoft.Web.Administration` and recycle the target app pool `PortalGerencialTestApiPool` successfully. Documented the intentional double `/api` path prefix (`/api/api/auth/login`) arising from IIS virtual directories, and analyzed ephemeral in-memory DataProtection keys warnings with hardening recommendations for subsequent production releases.

**Key Updates**:
- **Staging Connection String Key Correction**: Patched the secure local PowerShell configuration script (`AOVIA1VMS011_PHASE3_SECURE_CONFIGURATION.ps1`) to set the `ConnectionStrings__DefaultConnection` environment variable on `PortalGerencialTestApiPool`, resolving the `System.InvalidOperationException: The ConnectionString property has not been initialized` exception.
- **IIS Secure Script Overwrite**: Transferred the updated configuration script over SMB to remote server temp path `\\AOVIA1VMS011\C$\temp\AOVIA1VMS011_PHASE3_SECURE_CONFIGURATION.ps1` for local execution.
- **IIS Virtual Path Routing Audit**: Documented the double `/api` prefix in request paths (IIS virtual path `/api` + controller routing prefix `/api/...`) showing it is intentional and functional under relative same-origin routing.
- **DataProtection Key Ring Analysis**: Analyzed IIS Event Viewer warnings regarding ephemeral in-memory key repository and provided actionable persistent DPAPI/registry key ring blueprints for future production security hardening.

### Fixed — SQL Login Password Mismatch (Error 18456)

**Summary**: Resolved SQL Server authentication failure (`Error 18456: Login failed for user 'usr_portalgerencial_test'`) caused by password mismatch between Phase 2 SQL login provisioning and Phase 3 IIS connection string configuration. Created and deployed a unified password reset script that atomically sets both the SQL login password and IIS AppPool environment variables.

**Key Updates**:
- **Root Cause Diagnosis**: Enabled ANCM stdout logging temporarily, triggered a login request, and captured the exact `SqlException` from the stdout log confirming `Error 18456, State 1, Class 14`.
- **Health Check Limitation Identified**: Documented that `/api/health` returns `Healthy` even with broken database authentication because `AddHealthChecks()` lacks `.AddSqlServer()`.
- **Unified Password Reset Script**: Created `AOVIA1VMS011_PHASE3_SQL_PASSWORD_RESET.ps1` to atomically `ALTER LOGIN` and update both `ConnectionStrings__DefaultConnection` and `ConnectionStrings__PortalDatabase` IIS environment variables in a single execution.
- **PowerShell Parser Fix**: Fixed Unicode em-dash (`U+2014`) corruption in PowerShell 5.1 by replacing with ASCII double-dashes and saving with UTF-8 BOM.

### Fixed — Stale Frontend Bundle Purge & Redeployment

**Summary**: Resolved stale v2.150.0 frontend assets being served despite v2.151.0 deployment by performing a full directory purge and clean redeployment of v2.152.0 bundle.

**Key Updates**:
- **Full Directory Purge**: Purged all files from `D:\PortalGerencial-Test\Frontend\*` before copying fresh v2.152.0 dist assets.
- **Bundle Verification**: Confirmed deployed `index.html` references `index-2C6NsQze.js` (v2.152.0), zero `localhost:5000` occurrences, zero stale chunk files.
- **Deployment Checklist**: Documented mandatory Vite deployment procedure requiring full directory purge before copy.
- **Browser Cache**: Identified browser-level cache as secondary cause; documented InPrivate/site data clear as validation procedure.

### Validated — Final Staging Operational State

- Login via `https://portal-gerencial-test.alpla.net/login` confirmed working.
- Frontend v2.152.0 bundle active (`index-2C6NsQze.js`).
- API calls route to same-origin `/api` (no `localhost:5000`).
- Production database `[Portal-Gerencial]` and frontend directory remain untouched.
- ANCM stdout logging restored to `false`.
- No secrets committed to source control.
- Ports 5000/5001 remain unused.

**Files Changed**:
- `docs/AOVIA1VMS011_PHASE3_STAGING_ADMIN_ACCESS_RECOVERY.md` — Updated with SQL login fix, health check limitation, and comprehensive issue resolution table.
- `docs/DECISIONS.md` — Updated DEC-133 choices for the `DefaultConnection` environment variable and DataProtection persistence.
- `docs/VERSION.md` — Bumped to v2.152.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — Updated `APP_VERSION` to "2.152.0".


## [v2.151.0] - 2026-05-25

### Added — AOVIA1VMS011 Phase 3 Staging Access Recovery & same-origin API Routing (DEC-133)

**Summary**: Successfully executed administrative staging access recovery on `AOVIA1VMS011` for target database `[Portal-Gerencial-Test]` using a dedicated compiled .NET 8 console utility `StagingAccessRecovery.exe` (which resolved Windows PowerShell .NET Core assembly loading blockers). Performed automated database schema sweeps to confirm `dbo.Users` and `dbo.UserRoleAssignments` structures and Role ID 1 mapping. Idempotently inserted/updated Leonardo's account state (`IsActive = 1`, `MustChangePassword = 1`, `AccessFailedCount = 0`, `LockoutEndUtc = NULL`) and assigned `System Administrator` role. Resolved frontend base URL connection blockers (`localhost:5000` failures) by refactoring Vite default API base path fallback in `api.ts` to same-origin relative `/api`, completely eliminating CORS and port binding complexities. Saved secure redacted logs on staging server, redeployed static frontend assets, and validated isolation from Production environment.

**Key Updates**:
- **Staging Access Recovery .NET 8 Utility**: Compiled a dedicated C# console utility and copied it over SMB to `C:\temp\StagingAccessRecovery\` to execute native BCrypt hashing and ADO.NET SQL updates locally on `AOVIA1VMS011`.
- **Database Schema Validation**: Automated column sweeps confirming expected properties on `dbo.Users` and role mappings on `dbo.UserRoleAssignments` and `dbo.Roles`.
- **Same-Origin Relative API Routing**: Refactored Vite API client base URL to relative `/api`, ensuring same-domain routing through IIS to bypass CORS preflights and remove direct Kestrel port 5000/5001 dependencies.
- **Frontend redeployment**: Built and redeployed clean static assets to `D:\PortalGerencial-Test\Frontend` with zero hardcoded `localhost:5000` occurrences in the dist bundle.
- **Strict Production Isolation**: Audited and confirmed that the Production database `[Portal-Gerencial]` and folders remain 100% clean and untouched.
- **Exposed temporary password remediation**: Safely recommended Leonardo rerun the recovery utility to reset his temporary credentials following screenshot exposure.

**Files Changed**:
- `docs/AOVIA1VMS011_PHASE3_STAGING_ADMIN_ACCESS_RECOVERY.md` — [NEW] Detailed staging recovery report.
- `docs/DECISIONS.md` — Updated DEC-133 to record recovery utility architecture and same-origin relative API path choice.
- `docs/VERSION.md` — Bumped to v2.151.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — Updated `APP_VERSION` to "2.151.0".
- `src/frontend/src/lib/api.ts` — Changed API client fallback URL to `/api`.

## [v2.150.0] - 2026-05-23

### Added — AOVIA1VMS011 Phase 3 Test/Staging Deployment Staged & Configured (DEC-133)

**Summary**: Packaged the Release backend API and Vite frontend static assets locally, transferred them over SMB to remote server `AOVIA1VMS011` staging directories (`D:\PortalGerencial-Test\Api` and `D:\PortalGerencial-Test\Frontend`), pre-placed Express backup scripts, created the secure IIS environment variable script `AOVIA1VMS011_PHASE3_SECURE_CONFIGURATION.ps1`, identified and documented connection string plaintext storage inside `C:\Windows\System32\inetsrv\config\applicationHost.config` (with Windows ACL protection) as a staging tradeoff, generated the idempotent SQL migrations script `migration.sql`, and established a controlled explicit database execution strategy against `[Portal-Gerencial-Test]` using `sqlcmd` with Windows Authentication, completely bypassing automatic health endpoint triggers.

**Key Updates**:
- **Controlled Binary Deployments**: Packaged Release backend API and Vite frontend static assets; copied them over SMB to remote server staging folders.
- **IIS Secure Configuration Script**: Configured secure environment variables configuration script utilizing interactive prompt and redacting all passwords in reports/logs.
- **IIS applicationHost.config Secret tradeoff defined**: Explicitly identified and documented connection string plaintext persistence in IIS applicationHost.config and recommended Windows Authentication for Phase 4 to eliminate passwords entirely.
- **Explicit migrations strategy**: Pre-placed idempotent migrations SQL script `migration.sql` and established explicit controlled database execution against `[Portal-Gerencial-Test]` using `sqlcmd` with Windows Authentication, bypassing automatic triggers.
- **Automated Express backups wrapper**: Staged PowerShell daily backup wrapper script and SQL scripts on remote server to bypass Express Edition SQL Agent limitations.

## [v2.149.0] - 2026-05-23

### Added — AOVIA1VMS011 SQL Portal Databases & Logins Provisioned (DEC-133)

**Summary**: Created and copied the local database provisioning PowerShell wrapper script `AOVIA1VMS011_PHASE2_CREATE_PORTAL_DATABASES_AND_LOGINS.ps1` to server `AOVIA1VMS011` over SMB. Provisioned dedicated databases `[Portal-Gerencial]` and `[Portal-Gerencial-Test]`, SQL Authentication logins (`adm_portalgerencial`, `usr_portalgerencial`, `usr_portalgerencial_test`), mapped roles and permissions (including temporary `db_owner` mappings to support EF migrations), verified cross-database isolation, and formulated the daily backup strategy using Windows Task Scheduler to address SQL Express Agent unavailability.

**Key Updates**:
- **Local provisioning wrapper created**: Created the PowerShell wrapper with secure dynamic in-memory password generation, zero password storage on disk, and copied it over SMB.
- **Portal databases provisioned**: Created dedicated databases `[Portal-Gerencial]` and `[Portal-Gerencial-Test]` using proper bracket notation.
- **SQL Server logins provisioned**: Created SQL Authentication logins `adm_portalgerencial` (DB Owner on both databases), `usr_portalgerencial` (DB Owner temporarily on production database), and `usr_portalgerencial_test` (DB Owner temporarily on test database).
- **Strict cross-database isolation verified**: Verified zero user mapping exposure in default system databases and complete isolation between Production and Test/Staging runtime sessions.
- **SQL Express backup blueprint**: Prepared the recommended automated daily backup strategy using Windows Task Scheduler and PowerShell/SQLCMD scripts.
- **Documentation and alignment**: Created `docs/AOVIA1VMS011_PHASE2_DATABASE_AND_LOGIN_CREATION_REPORT.md`, updated the database prep report, and bumped version to **v2.149.0**.

**Files Changed**:
- `docs/AOVIA1VMS011_PHASE2_DATABASE_AND_LOGIN_CREATION_REPORT.md` — [NEW] Detailed report detailing database/login creation, permissions, secure password handling, and backup strategies.
- `docs/AOVIA1VMS011_PHASE2_DATABASE_PREPARATION_REPORT.md` — Updated status and summary sections to document active provisioning outcomes.
- `docs/DECISIONS.md` — Updated DEC-133 to add database and login provisioning decisions (item 10).
- `docs/VERSION.md` — Bumped to v2.149.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — Updated `APP_VERSION` to "2.149.0".

## [v2.148.0] - 2026-05-23

### Added — AOVIA1VMS011 SQL Sysadmin Recovery Validation (DEC-133)

**Summary**: Verified and validated the controlled **SQL Server Single-User Mode Sysadmin Recovery** executed on default instance **`MSSQLSERVER`** locally on server `AOVIA1VMS011`. Analyzed the validation sweep report under Leonardo's administrative context (`ALPLA\adm_cintra01`) and confirmed that the administrative blocker has been completely resolved. The SQL Server instance has been successfully restored to **normal multi-user mode** and accepts normal Windows Authentication connections, Leonardo's account has been verified with **`sysadmin`** server role privileges, the instance remains completely clean, and no existing attendance databases were touched.

**Key Updates**:
- **Local validation script execution**: Authored and copied the read-only validation script `AOVIA1VMS011_PHASE2_SQL_SYSADMIN_RECOVERY_VALIDATION.ps1` to the server over SMB.
- **Service multi-user mode verified**: Checked default service status and verified that SQL Server `MSSQLSERVER` is running and is restored to normal multi-user mode with no active Single-User `/m` parameters.
- **Windows Login sysadmin validation**: Executed connection catalog queries showing that `ALPLA\adm_cintra01` successfully connects via local Windows Authentication and has full database catalog access (`IS_SRVROLEMEMBER('sysadmin') = 1`).
- **Pristine instance integrity verified**: Checked `sys.databases` and `sys.server_principals`, confirming that **no** Portal databases or SQL application logins have been created yet, and all system databases remain intact and healthy.
- **Operational safety sweeps**: Confirmed that `INNUX`, `INNUXTIME`, and `INUTIME` remain completely untouched, no secrets were stored, and no binaries have been deployed.
- **Documentation and alignment**: Created `docs/AOVIA1VMS011_PHASE2_SQL_SYSADMIN_RECOVERY_VALIDATION.md`, updated the database preparation report, and bumped version to **v2.148.0**.

**Files Changed**:
- `docs/AOVIA1VMS011_PHASE2_SQL_SYSADMIN_RECOVERY_VALIDATION.md` — [NEW] Comprehensive validation report detailing service state, sysadmin logins, catalog visibility, and integrity checks.
- `docs/AOVIA1VMS011_PHASE2_DATABASE_PREPARATION_REPORT.md` — Aligned status, resolved administrative blocker resolution, and linked to the new validation report.
- `docs/DECISIONS.md` — Updated DEC-133 item #2 to reflect successful recovery execution and validation.
- `docs/VERSION.md` — Bumped to v2.148.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — Updated `APP_VERSION` to "2.148.0".

## [v2.147.0] - 2026-05-23

### Added — AOVIA1VMS011 SQL Instance Reuse Assessment: Decommission Verified (DEC-133)

**Summary**: Successfully executed and completed the decommission and readiness validation of the local SQL Server default instance **`MSSQLSERVER`** on `AOVIA1VMS011`. Retargeted our Phase 2 SQL strategy based on Leonardo's confirmation that the previous workload has been successfully migrated to `AOVIA1VMS012`. Analyzed the verified local script execution report over SMB and physically confirmed that the default instance contains **zero user databases**, **zero active connections**, and is **100% safe to repurpose and reuse** for the Portal Gerencial databases. Prepared a detailed, controlled 9-step single-user mode recovery procedure to bypass the SQL sysadmin access blocker for `ALPLA\adm_cintra01`.

**Key Updates**:
- **Local service state audit**: Verified that default instance service `MSSQLSERVER` is running under virtual account `NT Service\MSSQLSERVER` with startup type `Automatic`.
- **Physical DATA directories scan**: Performed filesystem scanning on default DATA folder and recursively drive D:, verifying **0 user databases** exist on disk. All files represent standard clean system database templates.
- **Active network and process connections check**: Audited local port 1433 and netstat connections, confirming **0 active connections** exist on port 1433 or Named Pipes/Shared Memory for `MSSQLSERVER` process PID 3980.
- **SQL Agent validation**: Verified that SQL Server Agent `SQLSERVERAGENT` is stopped and disabled, and is functionally unavailable due to Express Edition limitations.
- **Controlled SQL sysadmin recovery blueprint**: Authored a step-by-step recovery plan using SQL Server Single-User Mode (`net start MSSQLSERVER /m"SQLCMD"`) to add `ALPLA\adm_cintra01` as a `sysadmin` login with zero risk of operational disruption.
- **Approved instance reuse recommendation**: Formally recommended keeping `MSSQLSERVER` installed, leaving system databases untouched, and provisioning only the new Portal databases (`[Portal-Gerencial]` and `[Portal-Gerencial-Test]`) and Portal logins (`adm_portalgerencial`, `usr_portalgerencial`, `usr_portalgerencial_test`).
- **Changelog & Documentation Alignment**: Created `docs/AOVIA1VMS011_SQL_INSTANCE_REUSE_ASSESSMENT.md` and integrated the decommission findings in `docs/AOVIA1VMS011_PHASE2_DATABASE_PREPARATION_REPORT.md` and amended `docs/DECISIONS.md` DEC-133.

**Files Changed**:
- `docs/AOVIA1VMS011_SQL_INSTANCE_REUSE_ASSESSMENT.md` — [NEW] Detailed SQL decommission, physical/network validation, Express feature analysis, and single-user recovery procedure.
- `docs/AOVIA1VMS011_PHASE2_DATABASE_PREPARATION_REPORT.md` — Updated Executive Summary to integrate decommission outcomes and link to the dedicated assessment report.
- `docs/DECISIONS.md` — Amended DEC-133 item #2 to record formal decommission validation and single-user recovery approvals.
- `docs/VERSION.md` — Bumped to v2.147.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION bumped to "2.147.0".

## [v2.146.0] - 2026-05-23

### Added — AOVIA1VMS011 Phase 2 Database Prep: AD & SQL Server Logins Discovery (DEC-133)

**Summary**: Successfully completed Phase 2 read-only Active Directory (AD) sweeps and local SQL Server logins discovery on server `AOVIA1VMS011`. Analyzed the generated discovery report over SMB Administrative Share (`\\AOVIA1VMS011\C$`) and uncovered a critical infrastructure finding concerning SQL Server Metadata Visibility Restrictions. Mapped the corporate AD group prefix standards (`SQ-`), audited local IT support group memberships, and formulated a robust least-privilege security recommendation to create a dedicated group before database provisioning in Phase 2.

**Key Updates**:
- **Active Directory sweeps & corporate standards**: Completed domain-wide group discovery and verified that SQL Server database administration groups at ALPLA are formatted with the **`SQ-`** prefix (e.g. `SQ-<ServerName>-<DatabaseName>_DBOwner`).
- **Leonardo Group membership audit**: Verified that Leonardo belongs to candidate IT support groups `ALPLA\SD-AOVIA1-IT-Systems` (local Viana IT support) and `ALPLA\SD-AO0001-IT-Systems` (Angola IT support).
- **SQL Logins discovery script execution**: Verified the successful execution of the pre-placed script `C:\temp\AOVIA1VMS011_PHASE2_DISCOVERY.ps1` locally under Leonardo's active administrative Windows context (`ALPLA\adm_cintra01`) on default instance `MSSQLSERVER`.
- **Metadata Visibility diagnosis**: Discovered that Leonardo's account successfully connected but queries returned 0 rows. Diagnosed this as an SQL Server Metadata Visibility Restriction, confirming that Leonardo's account `ALPLA\adm_cintra01` is NOT individually registered as an SQL login or member of the `sysadmin` role, and `BUILTIN\Administradores` is not configured as `sysadmin` on `MSSQLSERVER`.
- **SQL Portal DBAdmin group recommendation**: Formulated the official recommendation to request the Active Directory team to create a dedicated security group: **`ALPLA\SQ-AOVIA1VMS011-PortalGerencial-DBAdmins`** to align with corporate standards and least-privilege principles.
- **Critical database creation requirements**: Outlined that because `ALPLA\adm_cintra01` lacks SQL sysadmin rights, Leonardo must connect as a `sysadmin` (e.g., using `sa` or the default SQL Server service account) to create the Portal databases and assign logins in Phase 2.
- **Safe discovery policies**: Confirmed that no databases, logins, or users were created, no secrets were stored, and no changes were made to SQL security or Innux databases.

**Files Changed**:
- `docs/AOVIA1VMS011_PHASE2_DATABASE_PREPARATION_REPORT.md` — Updated Status to "SUCCESSFULLY COMPLETED", added verified script outputs, documented metadata visibility analysis, and detailed the official dedicated group recommendation.
- `docs/VERSION.md` — Bumped to v2.146.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION bumped to "2.146.0".

## [v2.145.0] - 2026-05-23

### Added — AOVIA1VMS011 Post-Remediation Validation: ANCM Blocker Resolved (DEC-133)

**Summary**: Conducted a highly successful post-remediation validation sweep on server `AOVIA1VMS011` following Leonardo's local RDP-based execution of the Hosting Bundle 8.0.8 Repair and `iisreset`. Confirmed that the ASP.NET Core IIS Module (ANCM) `aspnetcorev2.dll` is now present on the server, and `AspNetCoreModuleV2` is successfully registered in IIS global modules. Swept all sites, app pools, folders, permissions, cert SNI bindings, and closed ports to confirm absolute environment readiness for Phase 2.

**Key Updates**:
- **ANCM Blocker Resolved**: Remote sweep successfully verified that `aspnetcorev2.dll` is present in `C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll` and that the global module `AspNetCoreModuleV2` is registered and active in IIS.
- **IIS Sites & App Pools Verified**: Confirmed that the IIS Web Server service is running, both sites (`PortalGerencial.Production` and `PortalGerencial.Test`) exist, and all 4 app pools remain intact and properly configured.
- **Isolated Directory Layouts & Permissions**: Re-validated all 14 folders under drive `D:` and confirmed that NTFS ACL rules are mapped to dynamic App Pool identities (`IIS APPPOOL\PortalGerencialApiPool` and `IIS APPPOOL\PortalGerencialTestApiPool`).
- **HTTPS & Certificates Binding Integrity**: Verified that `CN=portal-gerencial.alpla.net` and `CN=portal-gerencial-test.alpla.net` SSL certificates are correctly bound to port 443 with SNI enabled.
- **Secure Port and Database Enforcements**: Verified that ports `5000` and `5001` remain completely closed and unused, no databases were created, and no application binaries have been deployed. No credentials or passwords were stored.

**Files Changed**:
- `docs/AOVIA1VMS011_PHASE1_SERVER_PREPARATION_REPORT.md` — Updated Status to "SUCCESSFULLY COMPLETED", marked the ANCM blocker as "RESOLVED ✅", and updated the Phase 2 roadmap next steps.
- `docs/VERSION.md` — Bumped to v2.145.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION bumped to "2.145.0".

## [v2.144.0] - 2026-05-23

### Added — AOVIA1VMS011 Backend Deployment Blocker Remediation: ANCM Repair Plan (DEC-133)

**Summary**: Prepared the concrete remediation steps to resolve the missing ASP.NET Core IIS Module (ANCM) `aspnetcorev2.dll` blocker before backend deployment on `AOVIA1VMS011`. Conducted thorough recursive searches on the server and workstation over SMB and confirmed that the `dotnet-hosting-8.0.8-win.exe` offline installer is not pre-cached on disk and proxy gateway rules block direct command-line downloads. Documented a clear, step-by-step remediation guide with secure CDN links for Leonardo to perform a local RDP Repair of the Hosting Bundle and run `iisreset`.

**Key Updates**:
- **Workstation & Server Installer Audit**: Verified that `dotnet-hosting-8.0.8-win.exe` is not available in the remote downloads, temp, or package cache folders, nor locally on the developer machine downloads.
- **Remediation Integration in Phase 1 Report**: Fully updated `docs/AOVIA1VMS011_PHASE1_SERVER_PREPARATION_REPORT.md` with step-by-step repair and verification guidelines.
- **Secure Installer Reference**: Documented the exact, secure Microsoft CDN download link for the offline installer `dotnet-hosting-8.0.8-win.exe`.
- **Validation After Remediation Checklist**: Detailed the post-repair checklists covering IIS status, SNI site integrity, URL Rewrite, closed ports 5000/5001, and zero databases/binaries deployed.

**Files Changed**:
- `docs/AOVIA1VMS011_PHASE1_SERVER_PREPARATION_REPORT.md` — Updated with local targeted search audits, secure download link, and RDP repair guidelines.
- `docs/VERSION.md` — Bumped to v2.144.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION bumped to "2.144.0".

## [v2.143.0] - 2026-05-23

### Added — AOVIA1VMS011 Phase 1 Server Preparation Completed (DEC-133)

**Summary**: Completed Phase 1 server provisioning and setup on Windows Server `AOVIA1VMS011` for a secure, isolated dual-environment deployment. Provisioned all folder hierarchies, copied certificates, set up local orchestration scripts, enabled IIS features, installed URL Rewrite module offline, securely bound PFX SSL certificates with SNI on HTTPS port 443, assigned NTFS folder security permissions, and opened firewall rules. Identified a critical Global IIS Module ANCM DLL blocker for Phase 2 backend deployment.

**Key Updates**:
- **Isolated Directory Layouts**: Created all 14 subfolders under drive `D:\PortalGerencial` and `D:\PortalGerencial-Test` remotely over SMB share.
- **IIS Enabled & URL Rewrite Installed**: Web Server role activated locally via PowerShell. URL Rewrite module successfully installed offline from the pre-placed `rewrite_amd64_en-US.msi`.
- **NTFS ACL Folder Permissions**: Mapped exact Modify/Read rules specifically for `IIS APPPOOL\PortalGerencialApiPool` and `IIS APPPOOL\PortalGerencialTestApiPool`.
- **Secure Certificate bindings**: Securely imported Production (`portal-gerencial.alpla.net`) and Test (`portal-gerencial-test.alpla.net`) SSL certificates (prompting passwords securely via SecureStrings) and bound them via SNI on HTTPS Port 443.
- **Firewall Exceptions**: Created HTTP 80 and HTTPS 443 inbound firewall rules. Confirmed ports 5000 and 5001 remain closed and unused.
- **ANCM Blocker Warning**: Discovered and validated that `aspnetcorev2.dll` is missing from registry and folders (due to Hosting Bundle pre-installation before IIS enablement). Documented this remaining blocker and recommended Repair in Phase 2.
- **SQL Instance Confirmed**: Instance `MSSQLSERVER` approved and documented. No databases created yet.

**Files Changed**:
- `docs/AOVIA1VMS011_PHASE1_SERVER_PREPARATION_REPORT.md` — [NEW] Detailed Phase 1 execution, validation, and readiness report.
- `docs/DECISIONS.md` — DEC-133 item #2 amended with approved `MSSQLSERVER` instance decision.
- `docs/VERSION.md` — Bumped to v2.143.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION bumped to "2.143.0".

## [v2.142.0] - 2026-05-22

### Added — AOVIA1VMS011 Dual-Environment Strategy: Test/Staging Environment (DEC-133)

**Summary**: Updated all AOVIA1VMS011 deployment documentation to include a completely isolated Test/Staging environment alongside Production. Each environment has its own database, folder tree, IIS site, application pools, SSL certificate, and configuration file. An integration write-safety classification and a Test→Promote→Production release workflow were added.

**Key Updates**:
- **Dual-Environment Architecture**: Both Production (`D:\PortalGerencial`) and Test/Staging (`D:\PortalGerencial-Test`) documented with complete isolation rules.
- **Separate SSL Certificates**: Production uses `82460ec13b4d0f90a349c960c5e45ac8.pfx`; Test/Staging uses `334ad6893b414f90a349c960c5e45af4.pfx`. Certificate passwords never stored.
- **Test Database**: `[Portal-Gerencial-Test]` with separate SQL login `usr_portalgerencial_test`. Bracket notation enforced.
- **Integration Write-Safety Matrix**: Primavera/Innux read-only in Test/Staging. Email disabled. Write-capable integrations blocked until approved.
- **Release Promotion Workflow**: Build → Deploy to Test → Validate → Promote to Production.
- **Temp Folders**: Added `Temp` subfolder to both environment folder structures.
- **DEC-133 Expanded**: Added items #8 (Dual-Environment Isolation) and #9 (Integration Write-Safety Policy).

**Files Changed**:
- `docs/AOVIA1VMS011_DEPLOYMENT_IMPLEMENTATION_PLAN.md` — Restructured for dual-environment with full Test/Staging IIS, SQL, config, and smoke test sections.
- `docs/SERVER_AOVIA1VMS011_READINESS_ANALYSIS.md` — Updated executive summary, architecture diagram, folder layout, and open decisions.
- `docs/DECISIONS.md` — DEC-133 amended with items #8 and #9.
- `docs/VERSION.md` — Bumped to v2.142.0.
- `docs/CHANGELOG.md` — This entry.
- `src/frontend/src/config.ts` — APP_VERSION bumped to "2.142.0".

## [v2.140.0] - 2026-05-22

### Changed — AOVIA1VMS011 Infrastructure Corrections: Database Rename & Port Restriction (DEC-133)

**Summary**: Applied two critical infrastructure corrections from Leonardo to the deployment documentation: (1) production database renamed from `AlplaPortal` to `[Portal-Gerencial]`, and (2) backend port 5000 restricted (reserved by another service), with IIS in-process hosting (`hostingModel="InProcess"`) now preferred to eliminate exposed Kestrel ports entirely.

**Key Updates**:
- **Database Renamed**: All references updated from `AlplaPortal` to `[Portal-Gerencial]` across readiness analysis, implementation plan, decisions log, SQL scripts, and connection strings. Bracket notation enforced due to hyphen.
- **Port 5000 Restricted**: Port 5000 marked as reserved/unavailable. Port 5001 also excluded. Backend must never bind to these ports.
- **IIS In-Process Hosting**: Deployment model changed from Kestrel-on-port reverse-proxy to ANCM in-process hosting. The .NET process runs inside `w3wp.exe` directly — no separate Kestrel port is exposed.
- **Folder Root Renamed**: Application root folder renamed from `D:\AlplaPortal` to `D:\PortalGerencial` across all documents and scripts.
- **IIS Pool/Site Names Renamed**: `AlplaPortalAppPool`/`AlplaPortalApiPool` → `PortalGerencialAppPool`/`PortalGerencialApiPool`. Site name → `PortalGerencial.Production`.
- **Validation Checklist Expanded**: Added smoke test step #8 verifying port 5000 is NOT bound on the server.

**Files Changed**:
- `docs/SERVER_AOVIA1VMS011_READINESS_ANALYSIS.md` — Database name, folder paths, pool names, port restrictions, hosting model.
- `docs/AOVIA1VMS011_DEPLOYMENT_IMPLEMENTATION_PLAN.md` — SQL scripts (bracket notation), connection strings, folder paths, pool names, IIS scripts, validation checklist.
- `docs/DECISIONS.md` — Amended DEC-133 with decisions #7 (port restriction) and updated #1, #2, #5, #6.
- `docs/VERSION.md` — Bumped version to `v2.140.0`.
- `docs/CHANGELOG.md` — This changelog entry.

---

## [v2.139.0] - 2026-05-22

### Added — AOVIA1VMS011 Deployment Architecture Updates & Implementation Plan (DEC-133)

**Summary**: Updated the technical assessment to reflect Leonardo's finalized deployment choices for server `AOVIA1VMS011` and authored a comprehensive, step-by-step deployment preparation roadmap (`docs/AOVIA1VMS011_DEPLOYMENT_IMPLEMENTATION_PLAN.md`).

**Key Updates**:
- **Local Database Strategy**: Formally accepted local isolation (Option A) with dedicated `AlplaPortal` database on general SQL Server instances (e.g. `MSSQLSERVER` / `MSSQLSERVER01`) on `AOVIA1VMS011`. Excluded any reuse or modification of existing Innux/Innuxtime databases.
- **HTTPS & SSL Certificate Binding**: Confirmed HTTPS planned from the start using local PFX file `C:\dev\alpla-portal\82460ec13b4d0f90a349c960c5e45ac8.pfx`. Mandated that certificate passwords must not be saved in any markdown, script, or configuration file.
- **Deployment Implementation Plan**: Authored `docs/AOVIA1VMS011_DEPLOYMENT_IMPLEMENTATION_PLAN.md` outlining controlled preparation steps across Phases A through G, covering controller path-traversal remediation, IIS URL Rewrite, SQL database creation, secure PFX import, production configuration templates, build publishing workflows, and a validation checklist.
- **Architectural Decision DEC-133**: Registered the finalized deployment choices, security rules, and code mitigation policies in the central `docs/DECISIONS.md` log.

**Files Changed**:
- `docs/SERVER_AOVIA1VMS011_READINESS_ANALYSIS.md` — Updated database strategy and decisions checklist.
- `docs/AOVIA1VMS011_DEPLOYMENT_IMPLEMENTATION_PLAN.md` — [NEW] Detailed multi-phase deployment roadmap.
- `docs/DECISIONS.md` — Registered `DEC-133` decision log.
- `docs/VERSION.md` — Bumped version to `v2.139.0`.
- `docs/CHANGELOG.md` — This changelog entry.

---

## [v2.138.0] - 2026-05-22

### Added — Server Deployment Readiness Analysis: AOVIA1VMS011 (DEC-133)

**Summary**: A comprehensive, read-only technical assessment and deployment readiness analysis of Windows Server `AOVIA1VMS011` for hosting the Portal Gerencial, culminating in a detailed 15-section markdown report under `docs/SERVER_AOVIA1VMS011_READINESS_ANALYSIS.md`.

**Key Assessment Points**:
- **Environment Inventory**: Running Windows Server 2022 Standard on `alpla.net`, with a dedicated system C: drive (61.98 GB free) and empty data D: drive (199.88 GB free). Hosts five local active SQL Server 2019 instances for Innux/InnuxTime employee attendance databases.
- **IIS Web Server Readiness**: Identified missing `Web-Server (IIS)` role and **IIS URL Rewrite Module v2.1** as critical blockers on the server.
- **Database Centralization Strategy**: Formally recommended hosting the portal database on ERP server `AOVIA1VMS012\SQLALPLA` (Option B) for optimal Primavera proximity and automatic backups.
- **Path Traversal Security Fix**: Audited `AttachmentsController.cs` and discovered a hardcoded relative path traversal vulnerability resolving to `C:\data\attachments`. Formulated a code correction loading from `appsettings.Production.json` to map to `D:\AlplaPortal\Attachments`.
- **Production Architecture**: Designed a single-site Unified Reverse Proxy configuration mapping Port 80/443 traffic to the static frontend and reverse-proxying `/api` requests back to the .NET Kestrel backend, bypassing all CORS issues.
- **Backup & Telemetry Recommendations**: Outlined database backups, incremental attachment logs, Serilog rotation on `D:\AlplaPortal\Logs`, and Event Viewer logging.

**Files Changed**:
- `docs/SERVER_AOVIA1VMS011_READINESS_ANALYSIS.md` — [NEW] detailed 15-section report
- `docs/VERSION.md` — v2.138.0
- `docs/CHANGELOG.md` — this entry

---

## [v2.137.0] - 2026-05-22

### Added — Guided Tour: Approval Drawer Tours (DEC-132)

**Summary**: Two new drawer-level guided tours for the Approval Quick Overview Drawer, with drawer-aware scroll handling and contextual tour selection based on approval stage.

**New Tours**:
- `drawer-approval-area` (8 steps): Operational validation focus — request need, allocation, CC/plant, items, quotation, alerts.
- `drawer-approval-final` (8 steps): Decision validation focus — financial impact, risks, documents, supplier choice, workflow history, final decision.

**Architecture**:
- New `'drawer'` tour level added to `TourLevel` type.
- New `scrollContainerSelector` property on `TourDefinition` — routes scroll handling to drawer container instead of window.
- `scrollTargetIntoView()` extended with drawer-aware branch: detects scroll container, compensates for sticky footer (72px), uses `container.scrollTo()`.
- Joyride config: `disableScrolling: true`, `scrollToFirstStep: false`, `overlayClickAction: false` for drawer tours.

**UI**:
- "Tour da Aprovação" button added to drawer action bar (next to "Manual de Aprovação").
- Auto-selects correct tour based on `approvalStage` (AREA → area tour, FINAL → final tour).

**Anchors Added**: `approval-drawer-header`, `approval-drawer-alerts`, `approval-drawer-request-info`, `approval-drawer-financial-allocation`, `approval-drawer-financial-context`, `approval-drawer-quotations`, `approval-drawer-documents`, `approval-drawer-items`, `approval-drawer-workflow`, `approval-drawer-actions`.

**Scroll Container**: `data-tour-scroll-container="approval-drawer"` on drawer scrollable div.

**Graceful Degradation**: All steps skipped when targets are absent (no alerts, no quotations, no documents, etc.).

**Files Changed**:
- `src/frontend/src/features/guided-tour/guidedTourTypes.ts` — drawer TourLevel, TourIds, scrollContainerSelector
- `src/frontend/src/features/guided-tour/tours/approvalDrawerAreaTour.ts` — [NEW] area tour definition
- `src/frontend/src/features/guided-tour/tours/approvalDrawerFinalTour.ts` — [NEW] final tour definition
- `src/frontend/src/features/guided-tour/guidedTourRegistry.ts` — registered both drawer tours
- `src/frontend/src/features/guided-tour/useGuidedTour.ts` — drawer-aware scroll, activeTourDef tracking
- `src/frontend/src/features/guided-tour/GuidedTourProvider.tsx` — Joyride config for drawer tours
- `src/frontend/src/pages/Approvals/ApprovalDetailPanel.tsx` — data-tour anchors, tour button
- `src/frontend/src/pages/Approvals/ApprovalCenter.tsx` — scroll container attribute
- `docs/VERSION.md` — v2.137.0
- `docs/CHANGELOG.md` — this entry

---

## [v2.136.0] - 2026-05-22

### Added — Guided Tour: Centro de Aprovações Page Tour (DEC-132)

**Summary**: New `page-approvals-center` tour added to the Guided Tour system, covering the full operational approval workflow with 7 conditional steps.

**Tour Steps**:
1. **Page Header** (`approvals-header`): Introduces the centralized approval workspace.
2. **KPI Cards** (`approvals-kpi-cards`): Explains pending counts, total value, urgency, and alerts.
3. **Filter Tabs** (`approvals-filter-tabs`): Covers triage controls for prioritizing the approval queue.
4. **Area Queue** (`approvals-area-queue`): Area-level approval decisions (conditional — area approvers only).
5. **Final Queue** (`approvals-final-queue`): Final-level approval decisions (conditional — final approvers only).
6. **Request Card** (`approvals-request-card`): Individual pending request card with key details (conditional — only if cards exist).
7. **Empty State** (`approvals-empty-state`): Shown when no approvals are pending (conditional — only when queues empty).

**Files Changed**:
- `[NEW] approvalsCenterTour.ts` — Tour step definitions.
- `[MODIFY] guidedTourTypes.ts` — Added `'page-approvals-center'` to `TourId`.
- `[MODIFY] guidedTourRegistry.ts` — Registered tour for `/approvals` route.
- `[MODIFY] ApprovalCenter.tsx` — Added `data-tour` anchors, `GuidedTourContextButton`, and `isFirstQueue` prop to `ApprovalQueueSection`.

**Notes**:
- DEV seed/debug area is explicitly excluded from the tour.
- All conditional steps leverage the existing `filterActiveSteps()` mechanism for graceful skipping.

## [v2.131.0] - 2026-05-22

### Improved — Guided Tour UX: Scroll Fix, Module & Page Tour Expansion (DEC-132)

**Summary**: Fixed Joyride scroll alignment issue where tour targets were hidden behind the 64px sticky topbar. Expanded module and page tour content with new targeted steps and data-tour anchor attributes.

**Scroll Fix** (`useGuidedTour.ts`, `GuidedTourProvider.tsx`):
- Joyride `scrollOffset: 80` (64px header + 16px breathing) and `scrollDuration: 350`
- `scrollToFirstStep` enabled
- `scrollTargetIntoView()` helper: compensates header on `STEP_BEFORE` using `requestAnimationFrame` + 80ms delay
- `HEADER_OFFSET_PX` reads CSS `--header-height` variable at load time

**Module Tour** (`purchasingLogisticsTour.ts`):
- Expanded from 5 → 9 steps: sidebar entry, cockpit overview, Pedidos, KPI cards, Pontos de Atenção, Ações Rápidas, Manual de Operação, Gestão de Cotações, Recebimento

**Page Tour** (`requestsPageTour.ts`):
- Expanded from 3 → 5 steps: action carousel + KPIs, filter tabs, filter button, table, row click/workflow

**Data Anchors**:
- `PurchasingLandingPage.tsx`: `purchasing-kpi-cards`, `purchasing-attention-points`, `purchasing-quick-actions`, `purchasing-operation-manual`
- `RequestsDashboard.tsx`: `requests-filter-button`, `requests-table`

## [v2.130.0] - 2026-05-22

### Added — Guided Tour Evolution: Multi-Tour Architecture (DEC-132)

**Summary**: Evolved the single "portal-main" guided tour into a registry-based multi-tier architecture supporting portal, module, and page-level tours. Initial target: Compras & Logística module with 3 page-level tours (Requests, Buyer Items, Receiving).

**Architecture**:
- `guidedTourRegistry.ts`: Central registry mapping `TourId` → `TourDefinition` with `getToursForRoute()` route-prefix resolution
- `guidedTourTypes.ts`: Expanded `TourId` union type and `GuidedTourContextValue` multi-tour API
- `useGuidedTour.ts`: Full refactor for registry-based tour selection and per-tour persistence

**New Tour Content** (`tours/` directory):
- `purchasingLogisticsTour.ts`: 5 steps (cockpit overview, pedidos, cotações, recebimento, module workflow)
- `requestsPageTour.ts`: 4 steps (header, action carousel, explorer, filter tabs)
- `buyerItemsPageTour.ts`: 3 steps (header, search bar, items list)
- `receivingWorkspaceTour.ts`: 3 steps (header, pending queue, completed section)

**UI Components**:
- `GuidedTourButton.tsx`: Transformed from single-click to dropdown menu with up to 3 contextual options (Portal / Module / Page)
- `GuidedTourContextButton.tsx`: Inline page-header button for direct page-level tour launch
- `GuidedTourProvider.tsx`: Updated context API + "no steps" toast notification

**Page Integration**:
- `PageHeader.tsx`: Added `data-tour` prop support
- `Sidebar.tsx`: Added `data-tour` attributes for sub-items (`buyer-items-menu`, `receiving-menu`)
- `PurchasingLandingPage.tsx`: data-tour + GuidedTourContextButton
- `RequestsDashboard.tsx`: data-tour on header, carousel, explorer, filter tabs + GuidedTourContextButton
- `BuyerItemsList.tsx`: data-tour on header, search bar, items list + GuidedTourContextButton
- `ReceivingWorkspace.tsx`: data-tour on header, pending, completed sections + GuidedTourContextButton

**Key Behaviors**:
- Separate localStorage persistence per tour (`guided-tour:{tourId}:v1:{userId}`)
- `filterActiveSteps()` prevents runtime errors from missing DOM targets
- Transient toast if no valid steps exist for a tour
- Existing `portal-main` tour preserved (backward compatible)

## [v2.129.0] - 2026-05-22

### Added — Guided Tour / Onboarding (DEC-131)

**Summary**: Implemented a guided onboarding tour using React Joyride v3 for first-time users. The tour introduces the portal's main structure (Topbar, Search, Notifications, Profile, Help, Sidebar modules) with RBAC-aware step filtering — modules the user cannot see are automatically skipped. Persistence via versioned localStorage keys scoped to user ID.

**New Feature Module**: `src/frontend/src/features/guided-tour/` (6 files: types, storage, steps, hook, provider, button).

**Layout Integration**:
- `AppShell.tsx` wrapped with `GuidedTourProvider`
- `Topbar.tsx`: `data-tour` attributes on topbar, search, notifications, profile + new `GuidedTourButton` (help icon)
- `Sidebar.tsx`: `data-tour` attributes on main menu and individual modules via `TOUR_ATTR_MAP` lookup

**Tour Steps (16 steps, PT content)**:
- Topbar → Search → Notifications → Profile → Help Button → Main Menu → Dashboard → Purchase Requests → Approvals → Compras & Logística → Finanças → Contratos → T.I. → R.H. → Configurações → Administração
- Each module (T.I., Configurações, Administração, Contratos) has its own dedicated step with distinct explanatory content
- Modules not visible to the user (due to RBAC) are silently skipped via DOM presence check

**Welcome Modal**: Animated overlay on first login ("Bem-vindo ao Portal Gerencial!") with "Iniciar Tour" / "Agora Não" options.
**Layout Readiness**: DOM polling (200ms intervals, 8s max) for authenticated user + topbar/menu presence instead of a fixed delay.
**Persistence**: `guided-tour:portal-main:v1:{userId}` — no anonymous keys.
**Help Button**: Permanent topbar button allows manual restart after completion/skip.
**Dependency**: `react-joyride` (~35KB gzipped).

## [v2.128.0] - 2026-05-22

### Changed — Remove LOCAL_OCR Provider, Consolidate OpenAI (DEC-130)

**Summary**: The local OCR provider (PaddleOCR/Tesseract) has been fully removed from the system. OpenAI Vision is now the sole active document extraction provider. The provider abstraction (`IDocumentExtractionProvider`) is preserved for future Azure Document Intelligence integration.

**Backend — Deleted Files**
- `AlplaPortal.Infrastructure/Services/Extraction/LocalOcrExtractionProvider.cs` — [DELETE] Provider implementation.
- `AlplaPortal.Infrastructure/Services/OcrService.cs` — [DELETE] Legacy dead-code service (referenced localhost:5005).
- `AlplaPortal.Application/Interfaces/IOcrService.cs` — [DELETE] Legacy dead-code interface (never registered in DI).

**Backend — Modified Files**
- `AlplaPortal.Api/Program.cs` — Removed `LocalOcrExtractionProvider` DI registration. OpenAI provider now registered unconditionally (removed Windows-only guard).
- `AlplaPortal.Api/appsettings.json` — `DefaultProvider` → `OPENAI`, removed `LocalOcr` config block, `OpenAi.Enabled` → `true`.
- `AlplaPortal.Application/Models/Configuration/DocumentExtractionOptions.cs` — Removed `LocalOcr` property, default → `OPENAI`.
- `AlplaPortal.Application/DTOs/Extraction/DocumentExtractionSettingsDto.cs` — Removed `LocalOcr*` fields, default → `OPENAI`.
- `AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionService.cs` — Removed LOCAL_OCR fallback and switch cases. Added explicit guard: legacy `LOCAL_OCR` DB value → warns and falls back to `OPENAI`.
- `AlplaPortal.Infrastructure/Services/Extraction/DocumentExtractionSettingsService.cs` — Removed all LOCAL_OCR logic, `TestLocalOcrConnectionAsync`, and LOCAL_OCR validation. Added LOCAL_OCR→OPENAI guard. LocalOcr DB fields cleared on save.
- `AlplaPortal.Api/Controllers/Admin/AdminDiagnosticsController.cs` — Removed `LocalOcr` from `ServiceHealthDto` and health check block.
- `AlplaPortal.Domain/Entities/DocumentExtractionSettings.cs` — `[Obsolete]` attributes on `LocalOcr*` fields.
- `AlplaPortal.Application/Interfaces/Extraction/IDocumentExtractionProvider.cs` — Updated `Name` doc comment.

**Frontend — Modified Files**
- `pages/Settings/DocumentExtractionSettings.tsx` — Removed LOCAL_OCR dropdown option and entire Local OCR config section. Removed `Cpu` icon import. Updated OpenAI label.
- `pages/Admin/ServiceDiagnosis.tsx` — Removed `localOcr` from `DiagnosisData` interface, removed "Serviço OCR" card, updated skeleton count and diagnostic notes.
- `pages/Admin/IntegrationHealth.tsx` — Updated OcrServiceCard description (removed "Local OCR e" text), updated status logic to check only OpenAI.
- `types/index.ts` — Removed `localOcr*` fields from `DocumentExtractionSettingsDto`.

**Documentation**
- `docs/DECISIONS.md` — Added DEC-130.
- `docs/VERSION.md` — Version bumped to v2.128.0.
- `docs/ARCHITECTURE.md` — Updated provider selection note.
- `docs/ui/ADMIN_MENUS_REFERENCE.md` — Updated code snippets.

## [v2.127.0] - 2026-05-21

### Changed — Dashboard Redesign: Operational Cockpit (DEC-129)

**Summary**: The Dashboard has been completely redesigned from a generic overview/training page into an operational management cockpit focused on action, priorities, exceptions, bottlenecks, and financial visibility.

**Backend — New Endpoint**
- **`GET /api/v1/requests/cockpit-summary`**: Dedicated Dashboard endpoint returning all cockpit data in a single call. Uses `GetScopedRequestsQuery()` for role-based filtering and the existing `myTasksCriteria` expression for "My Work Queue" counters. The existing `GET /api/v1/requests/summary` endpoint is unchanged.
- **`CockpitSummaryDto`**: Comprehensive DTO with my-work counters (pending, urgent, adjustment, overdue, near-deadline), pipeline KPIs (10 status counters), bottleneck analysis (stage distribution + oldest request age), financial aggregation (by status group, multi-currency aware), and severity-sorted attention alerts.

**Frontend — New Layout (7 sections)**
1. **Minha Fila de Trabalho** (`MyWorkQueue.tsx`): 5 role-contextual KPI cards. Cards with value=0 auto-hide (except the main "pending" card).
2. **Visão do Pipeline**: 10 compact status counter cards with click-through to filtered Requests lists. Color-coded accent bars and hover effects.
3. **Ações Rápidas** (`QuickActions.tsx`): Expanded from 3 to 6 role-aware actions (Novo Pedido, Ver Pedidos, Cotações, Aprovações, Pagamentos, Recebimentos).
4. **Atenção Requerida** (`AlertList.tsx`): Always visible. Severity-sorted alerts (CRITICAL → WARNING → INFO) with click-through. Professional empty state.
5. **Gargalos do Processo** (`BottleneckTable.tsx`): Visual distribution bars showing request concentration by workflow stage, with color-coded age badges.
6. **Resumo Financeiro** (`FinancialSummary.tsx`): Financial cards by status group. Multi-currency aware. No fake data — proper empty state.
7. **Como funciona o processo**: Workflow guide moved to collapsible `<details>` at bottom, collapsed by default.

### Files Changed
- `backend/AlplaPortal.Application/DTOs/Requests/CockpitSummaryDto.cs` — [NEW] DTO definitions.
- `backend/AlplaPortal.Api/Controllers/RequestsController.cs` — Added `GetCockpitSummary` endpoint.
- `frontend/src/types/index.ts` — Added cockpit TypeScript interfaces.
- `frontend/src/lib/api.ts` — Added `getCockpitSummary` API method.
- `frontend/src/pages/Dashboard/Dashboard.tsx` — Complete rewrite (cockpit layout).
- `frontend/src/pages/Dashboard/components/MyWorkQueue.tsx` — [NEW] My work queue component.
- `frontend/src/pages/Dashboard/components/AlertList.tsx` — [NEW] Attention alerts component.
- `frontend/src/pages/Dashboard/components/BottleneckTable.tsx` — [NEW] Bottleneck analysis component.
- `frontend/src/pages/Dashboard/components/FinancialSummary.tsx` — [NEW] Financial summary component.
- `frontend/src/pages/Dashboard/components/QuickActions.tsx` — Rewritten with role-aware actions.
- `docs/DECISIONS.md` — DEC-129.
- `docs/FRONTEND_FOUNDATION.md` — Updated Dashboard section.

---

## [v2.126.0] - 2026-05-21

### Fixed — HR Attendance: "Falta" Status Despite Valid Punches (DEC-128)

**Root Cause**: Days with valid raw terminal Entry + Exit punches still displayed as "Falta" (Absent) with H.Totais=00:00. Two issues contributed:

1. **Absence periods counted as worked**: `GetWorkedHoursAsync` included `AlteracoesPeriodos` rows with absence codes (e.g., F03 Falta Injustificada) in the BasicMinutes total. When the mixed-code portal interpreter cleared `AbsenceMinutes` to 0, the formula `Max(0, worked - absence)` no longer cancelled out, blocking PunchWithoutPeriod detection.

2. **No fallback status**: The monthly report had no mechanism to flag days where the Portal shows valid Entry/Exit but Innux has no processed work period.

**Backend — PunchWithoutPeriod Detection**
- **New check** in `BuildSingleDepartmentReportAsync`: After punch pairing, if Portal has valid Entry + Exit pair AND Innux status is "Absent"/"PortalInterpreted" AND `dayWorked.TotalMinutes == 0` AND span ≥ 60 minutes → set status to `PunchWithoutPeriod`.
- **Portal estimated time**: Calculates entry→exit span and includes it in the warning message: "Tempo estimado pelo Portal: HH:mm".
- **No H.Totais override**: H.Totais stays from Innux (00:00). Only the status display changes.
- **New DTO field**: `PortalEstimatedMinutes` on `AttendanceDailyRecordDto`.

**Backend — GetWorkedHoursAsync Fix**
- Added `AND ap.IDCodigoAusencia IS NULL` filter to exclude absence periods from worked hours calculation. Absence periods have time spans but represent scheduled absence, not actual work.

**Frontend — "Verificar" Status**
- Status column shows "Verificar" label with `AlertCircle` icon (orange/amber).
- Tooltip shows Portuguese warning message including Portal-estimated hours.
- Pulse animation on the warning icon for visual attention.
- Print-safe styles (no animation, visible text).

**Diagnostic Scan — May 2026**: 448 PunchWithoutPeriod day-records across 95 employees. This indicates a systemic Innux processing gap where raw terminal punches exist but Innux didn't generate work periods.

### Files Changed
- `backend/AlplaPortal.Infrastructure/Services/Integration/InnuxAttendanceService.cs` — `GetWorkedHoursAsync`: Added absence period filter (`IDCodigoAusencia IS NULL`).
- `backend/AlplaPortal.Application/DTOs/HR/AttendanceReportDtos.cs` — Added `PortalEstimatedMinutes`.
- `backend/AlplaPortal.Api/Controllers/HRAttendanceController.cs` — PunchWithoutPeriod detection in `BuildSingleDepartmentReportAsync`.
- `frontend/src/pages/HR/AttendanceMonthlyReport/HRAttendanceMonthlyReport.tsx` — "Verificar" label, icon, tooltip.
- `frontend/src/pages/HR/AttendanceMonthlyReport/hr-attendance-monthly-report.css` — Status badge and indicator styling.

---

## [v2.125.0] - 2026-05-21

### Fixed — HR Attendance: Punch Classification in Monthly Report

**Root Cause**: Innux biometric terminals can send mixed direction codes on the same day — e.g., Code `17` (alternate entry code) for the first punch and `EN` (standard entry code) for the second. Since `MapDirectionLabel` maps both `17` and `EN` to "Entrada", all punches ended up classified as entries. Exit punches (e.g., 17:32, 17:38) appeared in the wrong report column (ENT.2 instead of SAÍ.1). This affected both `GetRawPunchesAsync` (monthly report) and `GetPunchesAsync` (day-detail).

**Backend — Shared Interpretation Logic**
- **Extracted** `ApplyPortalPunchInterpretation` shared method in `InnuxAttendanceService.cs` — now applied to both `GetRawPunchesAsync` and `GetPunchesAsync` to ensure consistent direction interpretation.
- **Rule 4 (Mixed Codes)**: If all punches in a day resolve to the same `DirectionLabel` after `MapDirectionLabel` (regardless of raw code differences), the first chronological punch is classified as Entrada and the last as Saída. Single ambiguous punches are not inferred — they trigger a warning instead.
- **Tracking**: `IsPortalInterpreted` flag set on reinterpreted punches for audit transparency.

**Backend — Direction Warnings**
- **New DTO fields**: `HasDirectionWarning` and `DirectionWarningMessage` on `AttendanceDailyRecordDto`.
- **Controller detection**: Three warning scenarios detected: Portal-interpreted punches, single ambiguous punches, and multiple punches all with same ambiguous direction code.

**Frontend — Warning Indicators**
- **New indicator**: Compass icon (🧭) rendered next to the status column for days with direction warnings. Distinct from the existing anomaly triangle and Portal "P" badge.
- **Tooltip**: Hover shows the specific warning message in Portuguese.
- **CSS**: Print-compatible styling added.

### Files Changed
- `backend/AlplaPortal.Infrastructure/Services/Integration/InnuxAttendanceService.cs` — Extracted `ApplyPortalPunchInterpretation`, applied to both bulk and detail methods.
- `backend/AlplaPortal.Application/DTOs/HR/AttendanceReportDtos.cs` — Added `HasDirectionWarning`, `DirectionWarningMessage`.
- `backend/AlplaPortal.Api/Controllers/HRAttendanceController.cs` — Direction warning detection in `BuildSingleDepartmentReportAsync`.
- `frontend/src/pages/HR/AttendanceMonthlyReport/HRAttendanceMonthlyReport.tsx` — Direction warning rendering with Compass icon.
- `frontend/src/pages/HR/AttendanceMonthlyReport/hr-attendance-monthly-report.css` — Warning indicator styling (screen + print).

---

## [v2.124.0] - 2026-05-21

### Changed — I.T Equipment Documents: DOCX → PDF Migration with Branding

**Summary**: All official I.T Equipment documents (Termo de Responsabilidade / Entrega, Termo de Devolução) are now generated and emailed as branded PDF files instead of DOCX, using PdfSharpCore (MIT license).

**Backend — PDF Generation**
- **New Service**: `ITEquipmentPdfService` — generates branded A4 PDF documents via PdfSharpCore with: company logo in header (from `data/templates/branding/portal-logo.png`), two-column info table, policy text (from `data/templates/it-equipment/policy-text.txt`), signature lines, and automatic page-break management.
- **Logo Fallback**: If the logo file is missing, documents generate with a text-only header and a warning is logged — document generation does not fail.
- **Policy Text Required**: For Assignment Agreements, `policy-text.txt` is mandatory — generation fails with a clear Portuguese error message if missing. For Return Agreements, policy text is not needed.
- **MIME Detection**: Email attachments and download endpoint now auto-detect MIME type from file extension (`.pdf` / `.docx`), ensuring correct handling for both new PDFs and legacy DOCX files.
- **Legacy Compatibility**: Old DOCX documents remain downloadable. `ITEquipmentAgreementService` methods marked `[Obsolete]`.

**Frontend — UI Updates**
- ReturnEquipmentModal and ChangeEquipmentUserModal notice texts updated to mention "PDF" format.

**Affected Flows**: Assignment (Atribuir), Return (Devolver), and Change User (Trocar Utilizador) — all three now generate PDF.

### Files Changed
- `backend/AlplaPortal.Infrastructure/Services/ITEquipmentPdfService.cs` — [NEW] PDF generation service.
- `backend/AlplaPortal.Infrastructure/Services/ITEquipmentAgreementService.cs` — Marked `GenerateAsync` and `GenerateReturnDocumentAsync` as `[Obsolete]`.
- `backend/AlplaPortal.Infrastructure/Services/EmailService.cs` — Auto-detect MIME type for attachments.
- `backend/AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj` — Added PdfSharpCore package.
- `backend/AlplaPortal.Api/Program.cs` — Registered `ITEquipmentPdfService` in DI.
- `backend/AlplaPortal.Api/Controllers/ITEquipmentController.cs` — Inject `ITEquipmentPdfService`; route all document generation to PDF service.
- `backend/AlplaPortal.Api/Controllers/ITEquipmentDocumentsController.cs` — Auto-detect MIME type in download endpoint.
- `frontend/src/components/it/ReturnEquipmentModal.tsx` — Updated notice text to mention PDF.
- `frontend/src/components/it/ChangeEquipmentUserModal.tsx` — Updated notice text to mention PDF.
- `data/templates/it-equipment/policy-text.txt` — [NEW] Extracted equipment usage policy text.
- `data/templates/branding/portal-logo.png` — [NEW] Portal Gerencial logo for document branding.
- `docs/DECISIONS.md` — DEC-126.
- `docs/VERSION.md` — Bumped to v2.124.0.

---

## [v2.123.0] - 2026-05-20

### Added — I.T Equipment Inventory Management Module

**New Module**: Complete I.T equipment inventory management system. The IT department can now register, track, assign, return, repair, lose, reserve, and retire all company IT assets — with a full audit trail of every movement.

**Backend — Domain & API**
- **5 New Entities**: `ITEquipment`, `ITEquipmentAssignment`, `ITEquipmentMovementLog`, `ITEquipmentAcquisition`, `ITEquipmentDocument`.
- **Role-Based Access**: New `IT` role (seeded via migration). Only IT and System Administrator roles can access the module.
- **API Route**: `api/it/equipment` with endpoints for CRUD, lifecycle actions, and CSV import.
- **Equipment CRUD**: Create, update, list (search + 5 filters + sort + pagination), and detail (with assignments, movements, documents, acquisition).
- **Lifecycle Actions**: Assign → Return (OK/DAMAGED/NEEDS_REPAIR) → Send to Repair → Return from Repair (REPAIRED/NOT_REPAIRABLE) → Mark Lost → Reserve → Retire.
- **Movement Audit Log**: Every action creates an `ITEquipmentMovementLog` entry with previous/new status, owner changes, and operator notes.
- **CSV Import**: `POST /api/it/equipment/import` — multipart upload with flexible column mapping (supports English/Portuguese headers), duplicate detection (exact Asset Tag + conditional Hostname), and per-line error reporting. Empty status defaults to `UNKNOWN` (not `AVAILABLE`).
- **Document Management**: `ITEquipmentDocumentsController` — upload (SHA256-named), download, list, soft-delete. Document types: Invoice, Warranty, PO, Receipt, Delivery Note, Proforma, Payment Proof, Other.
- **Acquisition Tracking**: Optional 1:1 `ITEquipmentAcquisition` record per equipment (purchase order, invoice, payment, warranty dates/amounts). Future Primavera/Portal integration fields left nullable.

**Frontend — React SPA**
- **Navigation**: New "T.I" sidebar group (Monitor icon), visible only to IT/Admin roles. Lazy-loaded route at `/it/equipment`.
- **ITEquipmentPage**: KPI summary cards (8 status counters), global search, collapsible filter bar (Status, Type, Plant, Manufacturer), sortable table, pagination.
- **EquipmentQuickViewDrawer**: Slide-in detail view with 4 tabs (Informações, Atribuições, Movimentações, Documentos) and context-sensitive action buttons.
- **EquipmentFormModal**: Create/edit form with conditional acquisition section (shown when sourceType = ManualPurchase), field validation, and shared UI helpers.
- **Action Modals**: AssignEquipmentModal, ReturnEquipmentModal, RepairEquipmentModal (send/return), LostEquipmentModal, RetireEquipmentModal, ReserveEquipmentModal.
- **ImportEquipmentModal**: Drag-and-drop CSV upload with result preview (created/skipped/errors/duplicate hostnames).
- **Type System**: `itEquipment.ts` with status/type display configs, movement type labels, assignment status config, document type labels — all in Portuguese.

**Database Migration**: `AddITEquipmentModule`
- 5 tables: `ITEquipments`, `ITEquipmentAssignments`, `ITEquipmentMovementLogs`, `ITEquipmentAcquisitions`, `ITEquipmentDocuments`.
- Unique indexes on `AssetTag` and `SerialNumber` (conditional, non-null).
- FK cascade: `Restrict` on Equipment→Documents to avoid SQL Server multiple cascade path error.
- IT role seeded into `Roles` table.

### Files Changed
- `backend/AlplaPortal.Domain/Entities/ITEquipment*.cs` — [NEW] 5 entity files.
- `backend/AlplaPortal.Domain/Constants/ITEquipmentConstants.cs` — [NEW] Status/type/movement enums + CSV normalizers.
- `backend/AlplaPortal.Domain/Constants/RoleConstants.cs` — Added `IT` role.
- `backend/AlplaPortal.Infrastructure/Data/ApplicationDbContext.cs` — 5 DbSets + Fluent API configs + IT role seed.
- `backend/AlplaPortal.Infrastructure/Data/Migrations/AddITEquipmentModule.cs` — [NEW] Migration.
- `backend/AlplaPortal.Api/Controllers/ITEquipmentController.cs` — [NEW] Full equipment lifecycle API.
- `backend/AlplaPortal.Api/Controllers/ITEquipmentDocumentsController.cs` — [NEW] Document management API.
- `frontend/src/types/itEquipment.ts` — [NEW] TypeScript interfaces + display configs.
- `frontend/src/lib/itEquipmentApi.ts` — [NEW] API client module.
- `frontend/src/pages/IT/ITEquipmentPage.tsx` — [NEW] Main page.
- `frontend/src/components/it/EquipmentSummaryCards.tsx` — [NEW] KPI cards.
- `frontend/src/components/it/EquipmentTable.tsx` — [NEW] Sortable table.
- `frontend/src/components/it/EquipmentQuickViewDrawer.tsx` — [NEW] Detail drawer with 4 tabs.
- `frontend/src/components/it/EquipmentFormModal.tsx` — [NEW] Create/edit form + shared helpers.
- `frontend/src/components/it/AssignEquipmentModal.tsx` — [NEW] Assignment modal.
- `frontend/src/components/it/ReturnEquipmentModal.tsx` — [NEW] Return modal.
- `frontend/src/components/it/RepairEquipmentModal.tsx` — [NEW] Repair send/return modal.
- `frontend/src/components/it/LostEquipmentModal.tsx` — [NEW] Lost modal.
- `frontend/src/components/it/RetireEquipmentModal.tsx` — [NEW] Retire modal.
- `frontend/src/components/it/ReserveEquipmentModal.tsx` — [NEW] Reserve modal.
- `frontend/src/components/it/ImportEquipmentModal.tsx` — [NEW] CSV import modal.
- `frontend/src/constants/roles.ts` — Added IT role + description.
- `frontend/src/constants/navigation.tsx` — Added T.I sidebar group.
- `frontend/src/features/auth/AuthContext.tsx` — Added `hasITAccess`.
- `frontend/src/App.tsx` — Added `/it/equipment` route with ITRoute guard.
- `docs/VERSION.md` — Bumped to v2.123.0.

---

## [v2.122.0] - 2026-05-20

### Fixed — HR Monthly Attendance Report: Saldo (Balance) Always 00:00

**Problem**: The `Saldo` (balance) column in the HR Monthly Attendance Report always displayed `00:00`, even for employees with unjustified absences. Root cause: Innux stores balance as a `datetime-as-duration` value with base date `1900-01-01`. Negative balances (values before the base date) were silently truncated to 0 by `InnuxTimeHelper.ToMinutes()`.

**Solution**: Portal-computed balance replaces the Innux-sourced value.

| Column | Meaning | Source |
|---|---|---|
| H.Básicas | Planned/scheduled working hours | `AttendanceDaySummaryDto.ExpectedMinutes` |
| H.Falta | Unjustified absence hours | `AttendanceDaySummaryDto.AbsenceMinutes` (unchanged) |
| H.Totais | Positive counted hours (worked + justified) | Portal formula: `max(0, WorkedMinutes - AbsenceMinutes) + JustifiedMinutes` |
| Saldo | Time balance | Portal formula: `H.Totais - H.Básicas` |

**Exempt categories** (Vacation, Holiday, JustifiedAbsence): `H.Totais = H.Básicas`, `Saldo = 00:00`.

**Visual indicators**: Negative saldo in red/bold, positive saldo in green. Applied to daily records, monthly summaries, employee grand totals, and department totals — screen and print.

### Files Changed
- `backend/AlplaPortal.Api/Controllers/HRAttendanceController.cs` — Portal-computed `BasicMinutes`, `TotalMinutes`, `DailyBalance`; new `ComputePositiveCountedMinutes` helper; `AbsenceMinutes` accumulation fix.
- `frontend/src/pages/HR/AttendanceMonthlyReport/HRAttendanceMonthlyReport.tsx` — Balance color classes on all saldo display elements.
- `frontend/src/pages/HR/AttendanceMonthlyReport/hr-attendance-monthly-report.css` — `.balance-negative`, `.balance-positive` styles (screen + print).
- `docs/VERSION.md` — Bumped to v2.122.0.
- `docs/DECISIONS.md` — DEC-124.

---

## [v2.121.0] - 2026-05-20

### Added — HR Monthly Attendance Report: Consolidated & 30-Day Activity Filter
- **Consolidated Report ("Todos os Departamentos")**: Added a special option to the department selector to generate a single consolidated PDF report for all departments at once. The report groups employees by department, sorting departments and employees alphabetically.
- **30-Day Activity Filter**: Injected the same 30-day "real terminal punch" activity filter into the Monthly Report (for both single and consolidated flows). Employees without biometric punches in the last 30 days are automatically excluded to prevent ghost employees from polluting the report.
- **Segmented Filter UI**: Added a three-button segmented control (Com ponto recente, Sem ponto há +30 dias, Todos) to the Monthly Report UI.
- **Print Notices**: Added visual and print-only notices explaining the default filter behavior in the PDF header.

## [v2.120.1] - 2026-05-20

### Fixed — HR Attendance: 30-Day Activity Filter Using Wrong Data Source
- **Root Cause**: `GetLastAttendanceDatesAsync` queried `MAX(Data) FROM dbo.Alteracoes`, which includes pre-generated scheduled records (rest days, planned shifts). Innux auto-generates `Alteracoes` rows for employees with active work schedules, even after they leave the company. This caused `MAX(Data)` to return recent or future dates for inactive employees, making them incorrectly appear in the "Com ponto recente" default view.
- **Fix**: Changed the query to use `dbo.TerminaisMarcacoes` (real terminal clock punches) instead. This table only contains actual physical clock-in/clock-out events, providing an accurate signal for real employee attendance activity.
- **Employee Affected**: ABENECO MANUEL PEDRO (and similar former employees still scheduled in Innux) will now correctly appear only under "Sem ponto há +30 dias" or "Todos".
- **Diagnostic Logging**: Added temporary classification logging in `HRAttendanceController.GetCalendar` to trace employee activity filter decisions (ABENECO-specific debug logging included).
- **No Data Changes**: Read-only fix. No writes to Innux, Primavera, or Portal employee records.

## [v2.120.0] - 2026-05-20

### Added — HR Attendance: 30-Day Activity Filter
- **Inactive Employee Hiding**: Employees without any attendance/punch data for more than 30 days are now hidden by default from the HR Attendance Calendar. This prevents former employees (still active in Primavera) from polluting the attendance grid.
- **Backend Activity Detection**: New `GetLastAttendanceDatesAsync` method in `InnuxAttendanceService` queries `MAX(Data) FROM dbo.TerminaisMarcacoes` (real terminal punches) grouped by employee ID. The 30-day cutoff is calculated from today's date (not the viewed calendar month).
- **`attendanceActivity` API Parameter**: `GET /api/hr/attendance/calendar` accepts `attendanceActivity` (`active`|`noRecent`|`all`). Default: `active`. Backend filters employee IDs before querying the daily attendance grid (performance optimization).
- **Activity Summary**: API response includes `activitySummary` with `activeCount`, `noRecentCount`, and `totalCount`. Employees with `lastAttendanceDate == null` are categorized as `noRecent`.
- **`lastAttendanceDate` Field**: Each employee object in the response now includes `lastAttendanceDate` (nullable ISO string).
- **Segmented Filter UI**: Three-button segmented control above the existing filter bar: "Com ponto recente" (default), "Sem ponto há +30 dias", "Todos". Each button shows the employee count badge.
- **Explanatory Hint**: When in default "active" view, an informational message explains why employees are hidden: "Funcionários sem ponto há mais de 30 dias são ocultados por padrão, pois podem não ter sido desativados no Primavera."
- **"Último ponto" Display**: In "noRecent" view, each employee row shows their last attendance date in amber text, or "Não encontrado" if null.
- **Non-Destructive**: This is purely a UI visibility filter. No employee status changes, no writes to Primavera or Innux, no HR mapping changes.

## [v2.119.1] - 2026-05-20

### Fixed — HR Directory Sync: Missing EF Core Migration
- **Root Cause**: The v2.119.0 implementation added the `SuggestedPlantSource`, `SuggestedPlantReason`, `SuggestedPlantConfidence`, and `SuggestedPlantResolvedAtUtc` fields to the `HREmployee` domain entity, but the corresponding EF Core migration was not generated and applied to the database. This caused a runtime `SqlException: Invalid column name` when triggering the HR Directory synchronization.
- **Fix**: Created and applied the missing EF Core migration (`20260520092813_AddPlantSuggestionFields.cs`), successfully adding the nullable columns to the `HREmployees` table and restoring sync functionality.

## [v2.119.0] - 2026-05-20 - HR Directory: Primavera Plant Suggestion & Advanced Filters

### Added
- **Primavera Plant Suggestion Service** (`PrimaveraPlantSuggestionService`): New read-only advisory service that queries Primavera databases (ALPLASOPRO / ALPLAPLASTICO) to identify which Primavera company each unmapped HR employee belongs to. Does not write to Primavera.
  - **ALPLASOPRO → High Confidence**: Employee found in ALPLASOPRO maps to Viana 3 with High confidence. Pre-fills plant selection on accept.
  - **ALPLAPLASTICO → Ambiguous**: Employee found in ALPLAPLASTICO maps to Viana 1 or Viana 2. Requires manual selection.
  - **Not Found**: Employee not found in either database. Displayed as "Não encontrada".
  - **No Cost Center Logic**: Deliberately excluded per business decision — will be analyzed separately.
- **Suggestion Domain Fields**: Added `SuggestedPlantSource`, `SuggestedPlantReason`, `SuggestedPlantConfidence`, and `SuggestedPlantResolvedAtUtc` to `HREmployee` entity. EF Core migration applied.
- **Resolve Suggestions Endpoint**: `POST /api/hr/leave/employees/resolve-suggestions` triggers batch Primavera lookup for all active unmapped employees. Returns counts by confidence level (highConfidence, ambiguous, notFound).
- **Advanced Filtering (Backend)**: `GET /api/hr/leave/employees` now supports `mappingStatus` (mapped/unmapped), `missingField` (plant/department/manager), `hasSuggestion`, `plantId`, `departmentMasterId`, and `innuxDepartment` query parameters.
- **KPI Summary**: Backend returns a `summary` object with total, fullyMapped, unmapped, withoutPlant, withoutDepartment, withoutManager, and withSuggestion counts.
- **Frontend Filter Bar**: Collapsible filter panel with chip buttons (Todos, Mapeados, Não Mapeados, Sem Planta, Sem Departamento, Sem Responsável, Com Sugestão), plant dropdown, and Innux department text filter.
- **KPI Summary Cards**: Interactive clickable cards displaying mapping status metrics. Clicking a card applies the corresponding filter.
- **Suggestion Hints**: Unmapped employee rows display inline Primavera suggestion badges with confidence level, source database, and accept/map button.
- **Accept Suggestion Workflow**: "Aceitar Sugestão" button pre-fills Viana 3 for High-confidence suggestions; "Mapear Manualmente" opens edit mode for ambiguous suggestions.
- **Sync Integration**: The "Sincronizar" action now includes suggestion resolution as step 3 after department and employee sync, reporting suggestion counts in the success message.

### Security
- Primavera queries are strictly read-only SELECT operations. No writes to Primavera databases.
- No existing Portal mappings are overwritten — suggestions are advisory only.
- Suggestion resolution restricted to `System Administrator` and `HR` roles (inherits from `IsAdminOrHR`).

---



## [v2.118.2] - 2026-05-19 - HR Report Print Document Layout

### Fixed
- **Print Document Layout**: Replaced screen-capture-style print output with a proper official document layout. Report starts with "ALPLA Angola | Portal Gerencial / Relatório Mensal de Presenças" header and immediately shows employee data.
- **HR Module Chrome Hidden**: PageHeader ("RECURSOS HUMANOS") and navigation tabs (Visão Geral, Férias, Presenças, etc.) are now hidden during print via `screen-only` class in `HRLandingPage.tsx`.
- **Scoped Print CSS**: Complete rewrite of `@media print` in `hr-attendance-monthly-report.css` with document header styling, compact table layout, employee section `break-inside: avoid`, repeating `thead`, and A4 landscape 8mm margins.
- **Global Print CSS Simplified**: Reduced `globals.css` print rules to minimal AppShell override to avoid cross-page interference.

---

## [v2.118.1] - 2026-05-19 - HR Monthly Attendance Report Print Fix

### Fixed
- **Global Print CSS**: Added `@media print` rules to `globals.css` that hide the AppShell chrome (Topbar, Sidebar) and flatten the grid layout, resolving blank page output when printing the HR Monthly Attendance Report.
- **AppShell CSS Classes**: Added semantic class names (`app-shell`, `app-shell-grid`, `app-shell-sidebar`, `app-shell-main`) to `AppShell.tsx` for print-media targeting.
- **TypeScript Fix**: Removed unused `React` import in `HRAttendanceMonthlyReport.tsx` (TS6133).

---

## [v2.118.0] - 2026-05-19 - Catalog Sync Conflict Resolution

### Added
- **Catalog Sync Conflict Resolution Backend**: New `POST /api/v1/sync/catalog/resolve-conflict` endpoint with 4 resolution strategies: `UpdatePortal` (field-level selection for Description, Category, Unit, PrimaveraCode), `ConfirmAssociation` (link PrimaveraCode only), `CreateNew` (auto-generates ITM-NNNNN), and `AssociateManually` (link to a user-selected Portal item).
- **Data Integrity Enforcement**: PrimaveraCode validation (rejects null/empty/whitespace/"0"/all-zeros). Duplicate PrimaveraCode-to-Portal association prevention with descriptive error messages.
- **Conflict Resolver Modal**: New `CatalogConflictResolverModal.tsx` component with side-by-side Primavera vs Portal comparison, field-level checkbox selection for UpdatePortal strategy, manual item search for AssociateManually, and preview-before-confirm summary panel.
- **Table Integration**: "Resolver" action button in the catalog sync table for conflict-status rows. Immediate visual feedback and UI refresh on successful resolution.
- **Audit Logging**: All resolution actions logged via `AdminLogWriter` with action code `SYNC_CATALOG_RESOLVE_CONFLICT`, capturing strategy, codes, and timestamps.

---

## [v2.117.3] - 2026-05-19 - EF Core Warning Cleanup

### Fixed
- **Database Schema**: Explicitly configured `HasPrecision(18, 2)` for `AnnualBudget.TotalAmount` to resolve EF Core `Validation[30000]` silent truncation warning. Applied schema migration.

---

## [v2.117.2] - 2026-05-19 - Backend Warning Cleanup

### Fixed
- **Backend Refactor**: Resolved `CS1998` compiler warning in `MonthlyChangesOrchestrator` by removing unnecessary `async` modifier from `LogEventAsync` and returning `Task.CompletedTask`.

---

## [v2.117.1] - 2026-05-18 - HR Monthly Attendance Reporting Corrections

### Fixed
- **Backend Refactor**: Corrected `CS0103` build error by referencing `MapDirectionLabel` instead of `ClassifyDirection` in `InnuxAttendanceService`.
- **Access Control**: Hardened `/api/hr/attendance/reports/monthly-by-department` with `[Authorize(Roles = "System Administrator,HR")]`.
- **Punch Pairing**: Refactored logic to be direction-aware, prioritizing `DirectionLabel` over positional indices to handle anomalous codes `17` and `18`.
- **DTO Realignment**: Fixed frontend DTO property mapping to strictly match backend JSON keys (`employeeCode`, `employeeName`, etc.) and updated `EmployeeId` typing.
- **Frontend Refactor**: Replaced `<select>` with `DepartmentMasterAutocomplete` for scalable department picking, introduced an explicit read-only disclaimer, and improved warning UI.
- **Print Optimization**: Rewrote `hr-attendance-monthly-report.css` to force A4 landscape density, include dark-themed department grand totals, and explicitly show the Portal-Interpreted badge upon PDF print.

---

## [v2.117.0] - 2026-05-18 - HR Monthly Attendance Reporting

### Added
- **Backend API**: `GetMonthlyByDepartmentReport` generating aggregated, grouped daily attendance data from `TerminaisMarcacoes` and `Alteracoes`.
- **Frontend UI**: `HRAttendanceMonthlyReport` with print-ready styling matching Innux "Resultados mensais por departamento" layout.
- **Controls**: Department selection using `DepartmentMasterAutocomplete`, 62-day interval restriction, and "all/business/weekends" day filters.
- **Access Control**: Limited to `System Administrator` and `HR` roles, integrating safely into the HR workspace.

---

## [v2.116.0] - 2026-05-15 - Proforma Deadline Expiration Alerts

### Added
- **Proforma Deadline Alert Service** (`ProformaDeadlineAlertService`): New daily `BackgroundService` that scans PAYMENT requests in approval stages (`WAITING_AREA_APPROVAL`, `WAITING_FINAL_APPROVAL`) and sends Proforma expiration alerts to the responsible approver.
  - **Alert Levels**: `WARNING_3D` (3 days before), `WARNING_1D` (1 day before), `CRITICAL_0D` (same day), `EXPIRED` (past due).
  - **Deduplication**: Global composite unique index `(RequestId, AlertLevel, RecipientUserId)` — each recipient receives at most one alert per level per request. When a request moves to a new approval stage with a different approver, the new recipient can still receive the alert.
  - **Email Notifications**: Branded Portuguese email via `IEmailService.SendWorkflowNotificationAsync()` with urgency-colored details box, request context (number, requester, department, company/plant, supplier, total, deadline, days remaining), and CTA deep link.
  - **In-App Notifications**: Bell notification via `INotificationService.CreateNotificationAsync()` under category `PROFORMA_DEADLINE`.
  - **Approver Resolution**: Reuses `WorkflowNotificationOrchestrator` patterns — explicit `AreaApproverId`/`FinalApproverId` preferred, falls back to department-scoped fan-out for Area Approvers.
  - **Configuration** (`appsettings.json → AppConfig:ProformaDeadlineAlerts`): `Enabled`, `CheckIntervalHours` (default 24), `ThresholdDays` (default [3, 1, 0]), `CheckTimeUtcHour` (default 7 = 08:00 Angola).
  - **Audit Trail**: `ProformaDeadlineAlerts` table persists all sent alerts with email/in-app delivery status and error tracking. Admin log entry written per cycle via `AdminLogWriter`.
- **Database Migration**: `AddProformaDeadlineAlerts` — new `ProformaDeadlineAlerts` table with dedup index, recipient FK, and request FK.
- **Notification Category**: Added `PROFORMA_DEADLINE` to `NotificationConstants.NotificationCategories`.

---

## [v2.115.2] - 2026-05-15 - OCR Quotation Total Calculation Fix

### Fixed
- **OCR Quotation Total Missing VAT**: After OCR extraction with global VAT inference, the "Valor Total da Cotação" displayed the net subtotal (without VAT) instead of the final payable total. Root cause: `draft.totalAmount` was calculated in `useOcrProcessor.ts` **before** the Global VAT Inference pass applied `ivaRateId` to items. Added post-inference recalculation of both item totals and draft total when `globalVatInferred` is true.
- **Item Removal Total Inconsistency**: Replaced inline `reduce` in `handleRemoveQuotationItem` with `calculateDraftTotal()` for consistent total calculation including global discount and proportional IVA adjustment.

---

## [v2.115.1] - 2026-05-15 - Area Approval Rejection Fix

### Fixed
- **Area Approval Rejection Blocked by Allocation Validation**: Rejecting or requesting adjustment on a purchase request at the Area Approval stage was incorrectly blocked when items had missing Plant or Cost Center assignments. The frontend was sending `itemAssignments` with `null` int values during rejection, causing ASP.NET ModelState deserialization to fail before the controller logic ran.
  - **Frontend**: Stopped sending `itemAssignments` payload on `REJECT` and `REQUEST_ADJUSTMENT` actions (only sent on `APPROVE` where allocation validation is required).
  - **Backend**: Made `ItemApprovalAssignmentDto.PlantId` and `CostCenterId` nullable as defensive hardening. Updated `ProcessAreaApproval` validation to use nullable-safe comparisons.
  - **Error Handling**: Improved `ApprovalDetailPanel` error catch to extract detailed field-level validation messages from ASP.NET `ProblemDetails`, replacing the generic "One or more validation errors occurred" banner.

---

## [v2.115.0] - 2026-05-15 - OCR Global VAT Inference

### Added
- **OCR Global VAT Inference**: When a supplier quotation/proforma specifies VAT only at the document summary level (Subtotal + IVA + Total) but not per line item, the system now automatically infers the global VAT rate and applies it to all items.
  - **Inference Algorithm**: Calculates `(GrandTotal - Subtotal) / Subtotal` to derive the implied VAT percentage. Matches against active `ivaRates` with ±0.30 percentage point tolerance (e.g., 13.90% → 14%, but 13.00% ≠ 14%). Validates recalculated total within 2% of OCR grand total before applying.
  - **Priority Rule**: Explicit item-level VAT always takes priority. Global inference only triggers when **all** items have uncertain or missing VAT.
  - **Auditability Flags**: New `globalVatInferred` (draft-level), `inferredVatRatePercent` (draft-level), and `ivaGlobalInferred` (item-level) fields distinguish inferred VAT from OCR-extracted VAT.
  - **UI Feedback**: Green success banner ("IVA global de {rate}% identificado no resumo do documento e aplicado automaticamente a todos os itens.") replaces the red "IVA não identificado" warning when inference succeeds. Red warning preserved as fallback when inference fails.
  - **Manual Override**: Users can still manually change any item's VAT rate after inference.
  - **Dual Path**: Logic applied in both `useOcrProcessor.ts` (Buyer workspace) and `QuotationEntry.tsx` (legacy quotation entry).

---

## [v2.114.0] - 2026-05-14 - Approvals Intelligence & Notification Fixes

### Added
- **Approvals & Budget Insights**: Integrated budget health analytics into the approval flow.
  - Added `ApprovalIntelligenceService` to calculate utilization metrics (OK, WARNING, CRITICAL, EXCEEDED).
  - Added `DecisionInsightsPanel` and `DecisionQuotationCard` to `ApprovalDetailPanel`.

### Fixed
- **Backend Routing & Notification Remediation**: Remediated purchase request notification failures (as per `PURCHASE_REQUEST_NOTIFICATIONS_AUDIT.md`).
  - `FinanceController`: Propagated `DepartmentId` to `PAYMENT_SCHEDULED` and `PAYMENT_COMPLETED` events for correct fan-out.
  - `RequestsController`: Added `DepartmentId` to `QUOTATION_COMPLETED` and fixed `RESUBMIT` event mapping to route correctly to the Final Approver.
  - `WorkflowNotificationOrchestrator`: Updated `FinalApproved` logic to include Requester and Buyer.
- **TypeScript Linting Cleanup**: Addressed multiple `TS6133` (unused variables/imports) warnings across the codebase to ensure a clean `npm run build`. Affected components include `BuyerItemsList`, `ContractsAlerts`, `ActionCarouselWidget`, `FinanceHistory`, `GuideModal`, `HRLandingPage`, and `ModernRequestTimeline`.

---

## [v2.113.0] - 2026-05-14 - Dark Mode Contrast Fix### Fixed
- **Dark Mode Visibility**: Improved contrast for text and status indicators in `UserDropdown` and `NotificationBell` components when dark mode is active.
- **Semantic Tokens**: Introduced `--color-status-red-surface` in `tokens.css` to provide accessible, theme-aware tinted backgrounds for status elements.

---

## [v2.111.0] - 2026-05-14 - HR Team Calendar Scope Fix (Local Manager)

### Problem
Local Manager "Andre Vale" could not see any employees in the HR Team Calendar (`/hr/calendar`), despite having 10+ employees explicitly mapped to him via `ManagerUserId` (Responsável/Chefe). The calendar displayed "Nenhum funcionário mapeado no seu escopo de hierarquia."

### Root Cause
`GetScopedEmployeesQuery()` in `HRLeaveController.cs` — the Local Manager branch filtered employees using `PortalDepartmentId IN (user's dept scope)`. All employees assigned to Andre Vale had `PortalDepartmentId = NULL` (unmapped), causing the filter to return zero results. The `ManagerUserId` relationship was only checked in the Department Manager path, not in the Local Manager path.

### Solution

| Scope Case | Before (v2.110.0) | After (v2.111.0) |
|---|---|---|
| Plants + Depts | `PlantId AND PortalDeptId` | `ManagerUserId OR (PlantId AND PortalDeptId)` |
| Depts only | `PortalDeptId` | `ManagerUserId OR PortalDeptId` |
| Plants only | `PlantId` | `ManagerUserId OR PlantId` |
| No scopes | `WHERE false` (empty) | `ManagerUserId == userId` (manager-only) |

### Frontend UX
- Added info banner: "Não existem férias ou ausências registadas para a equipa neste período" when employees exist but no leave records match the selected period.
- Improved empty-state text with actionable guidance when no employees are in scope.

### Files Changed
- `backend/AlplaPortal.Api/Controllers/HRLeaveController.cs` — Added `e.ManagerUserId == userId` as OR condition in all Local Manager scope branches of `GetScopedEmployeesQuery()`.
- `frontend/src/pages/HR/HRTeamCalendar.tsx` — Added `cal-info-banner` component for the "no records" case; refined empty-state messaging.
- `frontend/src/pages/HR/hr-team-calendar.css` — Added `.cal-info-banner` styles.
- `docs/VERSION.md` — Bumped to v2.111.0.

### Security
No broadening of access. `ManagerUserId` is an admin-assigned FK — only HR/Admin can set it via the mapping endpoints. The change makes the Local Manager scope consistent with the existing Department Manager scope (line 183), which already uses `ManagerUserId`.

---

## [v2.110.0] - 2026-05-14 - HR Navigation UX Refinement

### Problem
After the v2.109.0 security fix, user "Andre Vale" (Local Manager) no longer sees HR administration screens. However, the sidebar still showed the group labeled "R.H." — misleadingly suggesting full HR module access. The page header inside `/hr/calendar` also showed "Recursos Humanos", reinforcing the confusion. Additionally, the tab bar inside the HR landing page still displayed admin-only tabs (Presenças, Escalas, Directório, Gestão de Crachás) to Local Managers even though route guards blocked access.

### Solution: Two Distinct Navigation Groups

| User Profile | Sidebar Group | Group Label | Visible Children |
|---|---|---|---|
| HR / System Administrator | `rh` | **R.H.** | Visão Geral, Calendário, Férias, Funcionários, Layouts, Histórico |
| Local Manager / Dept Manager / Viewer | `equipa` | **Gestão da Equipa** | Calendário da Equipa, Férias e Ausências |
| Requester only | *none* | — | — |

### HR Landing Page (Dynamic)

| Context | Page Title | Subtitle | Visible Tabs |
|---|---|---|---|
| HR / System Administrator | Recursos Humanos | Gestão de funcionários, férias, ausências e calendário da equipa | All tabs |
| Non-admin team user | Gestão da Equipa | Calendário da equipa, férias e ausências | Visão Geral, Férias, Calendário |

### Files Changed
- `constants/navigation.tsx` — Split `rh` group into two: `rh` (isHrAdmin) and `equipa` (isTeamModule). Added `CalendarCheck` icon. Updated `getNavigationConfig` signature to accept `hasHRAdminAccess`. Added `isHrAdmin` and `isTeamModule` to `NavItem` interface.
- `components/layout/Sidebar.tsx` — Passes `hasHRAdminAccess` to `getNavigationConfig`.
- `components/layout/GlobalSearch.tsx` — Passes `hasHRAdminAccess` to `getNavigationConfig`.
- `pages/HR/HRLandingPage.tsx` — Dynamic title/subtitle/icon based on `hasHRAdminAccess`. Tab filtering now uses `hasHRAdminAccess` instead of the previous `isViewerManagement`-only check, ensuring Local Managers see only team-level tabs.

## [v2.109.0] - 2026-05-14 - HR Module Access Control Fix

### Security Fix
- **HR Module Access Control**: User "Andre Vale" (Local Manager, Area Approver, Requester — no HR role) could see and access the full R.H. module in the sidebar, including employee badge administration at `/hr/badges/employees`. Root cause: `hasHRModuleAccess` in `AuthContext.tsx` included `isLocalManager`, granting all Local Managers access to the full HR module, including administration screens. The old `HRAdvancedRoute` guard only blocked `Viewer/Management` users — Local Managers passed through.

### Access Model (After Fix)
| HR Feature Category | Allowed Roles |
|---|---|
| **Administration** (badges, layouts, history, attendance, schedules, directory, monthly-changes) | `HR`, `System Administrator` |
| **Team-level** (overview, calendar, leave) | `HR`, `System Administrator`, `Local Manager`, `Department Manager`, `Viewer/Management` |
| **Backend write operations** (mapping, sync, bulk-update) | `HR`, `System Administrator` (unchanged — already enforced via `IsAdminOrHR`) |
| **Backend badge API** | `HR`, `System Administrator` (unchanged — `[Authorize(Roles = "System Administrator,HR")]`) |

### Files Changed
- `frontend/src/features/auth/AuthContext.tsx` — Added `hasHRAdminAccess` (HR + Admin only) alongside existing `hasHRModuleAccess` (team-level).
- `frontend/src/App.tsx` — Replaced `HRAdvancedRoute` with `HRAdminRoute` using `hasHRAdminAccess`; redirects unauthorized users to `/hr/calendar`.
- `frontend/src/constants/navigation.tsx` — Removed `LOCAL_MANAGER` from admin children (`rh-badges-employees`, `rh-badges-layouts`, `rh-badges-history`).

## [v2.108.0] - 2026-05-14 - Request Number Sort Fix & Column Sort Completeness

### Fixed
- **Request Number Column Sort (Backend)**: The "Número" column (`sortBy=requestNumber`) was sorted as a plain string via `ORDER BY RequestNumber`. Since the format is `REQ-DD/MM/YYYY-NNN`, lexicographic ordering compared the day portion first (`28` vs `14`), completely breaking chronological order. Now sorts by `CreatedAtUtc.Date` (primary) + `RequestNumber` (tiebreaker), producing correct date-then-sequence ordering.

### Added
- **Missing Backend Sort Cases**: The following frontend column keys were not handled in the backend sort switch and silently fell through to the `createdAtUtc` default:
  - `statusCode` → now sorts by `Status.DisplayOrder` (workflow-meaningful order, not alphabetical)
  - `requestTypeCode` → now sorts by `RequestType.Name`
  - `companyName` → now sorts by `Company.Name`
  - `needByDateUtc` → now sorts by `NeedByDateUtc`
  - `estimatedTotalAmount` → now sorts by the effective amount (selected quotation total or estimated amount)

### Files Changed
- `backend/AlplaPortal.Api/Controllers/RequestsController.cs` — Fixed `requestnumber` sort case; added 5 new sort cases.

## [v2.107.0] - 2026-05-14 - Persistent Table Preferences

### Added
- **`useTablePreferences` Hook** (`src/frontend/src/hooks/useTablePreferences.ts`): New reusable React hook that persists table filter, sort, and view state to `localStorage`, scoped by user ID and screen key. Features schema versioning, debounced writes (300ms), corrupt JSON resilience, and automatic empty-value cleanup.
  - Key format: `portal:prefs:{userId}:{screenKey}`
  - API: `preferences`, `setPreference`, `setPreferences`, `resetPreferences`, `isHydrated`
- **RequestsDashboard Persistence**: Search, filter type, status/company/plant/department filters, sort, page size, and advanced filter visibility now persist across navigation and refresh. "Limpar Filtros" resets UI + localStorage.
- **ApprovalCenter Persistence**: Sort mode and active triage filters now persist. New "Restaurar Padrão" button when non-default triage is active.
- **FinancePaymentsList Persistence** (URL-sync): Status codes, currency, and supplier search persist to localStorage and hydrate URL on mount. Deep-linking preserved. "Limpar" clears localStorage.
- **BuyerItemsList Persistence** (URL-sync): Search, item status, request status, and owner persist and URL-hydrate. Clear buttons also clear localStorage.

### Design Decisions
- URL-driven screens retain `useSearchParams` for deep-linking; localStorage hydrates URL only on mount when no URL params exist.
- `page` is never persisted (always starts at page 1).
- Unrelated localStorage keys (`approvalDrawerWidth`, `floatingMode.enabled`) are NOT migrated.

### Files Changed
- `frontend/src/hooks/useTablePreferences.ts` — [NEW] Core persistence hook.
- `frontend/src/pages/Requests/components/modern/RequestsDashboard.tsx` — Integrated preference persistence.
- `frontend/src/pages/Approvals/ApprovalCenter.tsx` — Integrated preference persistence + "Restaurar Padrão" button.
- `frontend/src/pages/Finance/FinancePaymentsList.tsx` — Integrated URL-sync preference persistence.
- `frontend/src/pages/Buyer/BuyerItemsList.tsx` — Integrated URL-sync preference persistence.

## [v2.106.0] - 2026-05-14 - Purchase Request Notification Priority Fixes

### Fixed
- **Finance Events — Missing DepartmentId**: `PAYMENT_SCHEDULED` and `PAYMENT_COMPLETED` events emitted from `FinanceController` were missing the `DepartmentId` field in the `WorkflowEvent` payload. This caused `HandlePaymentFanningOverridesAsync` (which resolves area approvers by department) to silently skip fan-out, resulting in area approvers never receiving payment lifecycle notifications for their departments.
  - **Fix**: Added `DepartmentId = r.DepartmentId` to both `WorkflowEvent` initializations in `SchedulePayment` and `MarkAsPaid` actions.
- **Quotation Completed — Missing DepartmentId**: `QUOTATION_COMPLETED` event in `RequestsController.CompleteQuotation` was missing `DepartmentId`, preventing correct department-scoped area approver resolution during the `HandlePendingAreaApprovalFanningAsync` fan-out.
  - **Fix**: Added `DepartmentId = r.DepartmentId` to the `WorkflowEvent` initialization.
- **FINAL_APPROVED Recipients Incomplete**: The `FINAL_APPROVED` event only notified the actor (Final Approver). The Requester and assigned Buyer — who need to know the request is ready for P.O. generation — were excluded from the notification.
  - **Fix**: Updated `ResolveRecipientsAsync` in `WorkflowNotificationOrchestrator` to resolve and include the Requester (`evt.RequesterId`) and the assigned Buyer (`Request.BuyerUserId`) as additional recipients.
- **RESUBMIT Routing to Wrong Approver**: When a request was resubmitted from `WAITING_FINAL_APPROVAL` status (after the Final Approver requested adjustments), `ResolveEventCode` incorrectly mapped the transition to `REQUEST_SUBMITTED`, which triggers area approver notification. The correct mapping is `AREA_APPROVED`, which triggers final approver notification.
  - **Fix**: Changed the `Resubmit` + `WaitingFinalApproval` case in `ResolveEventCode` from `WorkflowEventCodes.RequestSubmitted` to `WorkflowEventCodes.AreaApproved`.

### Documentation
- **`docs/PURCHASE_REQUEST_NOTIFICATIONS_AUDIT.md`**: Updated all 4 priority issues to "✅ FIXED in v2.106.0" status. Updated the notification matrix to reflect the corrected recipient lists.

### Files Changed
- `backend/AlplaPortal.Api/Controllers/FinanceController.cs` — Added `DepartmentId` to 2 `WorkflowEvent` initializations.
- `backend/AlplaPortal.Api/Controllers/RequestsController.cs` — Added `DepartmentId` to `QUOTATION_COMPLETED` event; fixed `ResolveEventCode` mapping for RESUBMIT.
- `backend/AlplaPortal.Infrastructure/Services/WorkflowNotificationOrchestrator.cs` — Added Requester + Buyer to `FINAL_APPROVED` recipients.
- `docs/PURCHASE_REQUEST_NOTIFICATIONS_AUDIT.md` — Marked fixes as applied.

## [v2.104.0] - 2026-05-14 - Buyer Requested Items Section

### Added
- **Buyer Requested Items Section ("Itens Solicitados no Pedido")**: Added a new read-only section within the Buyer Quotation Management expanded request view (`/buyer/items`). This section displays all items originally requested in the purchase request, giving buyers essential context before processing supplier quotations.
  - **Placement**: Appears between the request metadata (Plant, Department, Title, Description) and SEÇÃO A (Documentos e Cotações Registradas).
  - **Table Columns**: Line number (#), Description, Quantity, Unit, Estimated Unit Price, Estimated Total, Priority (Alta/Média/Baixa badges), and Type badge (✓ Catálogo / ✎ Manual).
  - **Catalog vs Manual Detection**: Items linked to the Portal item catalog (`ItemCatalogId`) display a green "Catálogo" badge. Free-text/manually entered items display an amber "Manual" badge.
  - **Cost Center Sub-detail**: When a cost center is assigned to an item, it is displayed as a secondary line below the description.
  - **Item Count Badge**: Header shows the total item count (e.g., "3 itens").
  - **Empty State**: Requests without detailed line items show an informational message: "Este pedido não possui itens detalhados."
  - **Backend**: Added `ItemCatalogId` to `LineItemDetailsDto` and `LineItemsController` query projection for catalog linkage detection.
  - **No disruption**: Section is purely informational. No edit/delete actions. Existing OCR import, manual quotation insertion, and quotation workflows remain unaffected.

## [v2.103.0] - 2026-05-14 - Requests Floating Mode Persistence

### Fixed
- **Requests Floating Mode Persistence**: The "Flutuante Ativo / Inativo" UI toggle on the Requests dashboard (`/requests`) now correctly saves its state to `localStorage`. The chosen view mode for the "New Request" button and summary footer persists across page navigation, browser reloads, and new sessions using the `alpla-portal.requests.floatingMode.enabled` key.

## [v2.102.0] - 2026-05-14 - Route-Level Access Control Hardening

### Security
- **Route-Level Access Control Hardening**: System-wide access-control audit conducted to eliminate gaps where UI-hidden resources remained accessible via direct URL manipulation.
  - Implemented `HRAdvancedRoute` to restrict HR diagnostic endpoints (attendance, schedules, directory, badges, monthly changes) strictly to users with `SYSTEM_ADMINISTRATOR` or `HR` roles, blocking `VIEWER/MANAGEMENT`.
  - Added `AdminRoute` guards to `/approvals` (AREA_APPROVER, FINAL_APPROVER), `/purchasing` and `/buyer/items` (BUYER), `/receiving` (RECEIVING, LOCAL_MANAGER), `/finance` (FINANCE), and `/contracts` (CONTRACTS, FINANCE).
- **Access Control Documentation**: Created `docs/ACCESS_CONTROL_AUDIT.md` mapping the security matrix and documenting required backend verification steps.

## [v2.101.0] - 2026-05-14 - HR Navigation: Finance-Style Tab Alignment

### Changed
- **HR Tab Navigation Pattern**: Replaced the pill-style horizontal tab bar (with `overflow-x: auto` causing horizontal scrolling) with the same inline bottom-border tab pattern used in `FinanceLandingPage`. Primary tabs are directly visible; secondary tabs are collapsed into a "Mais" dropdown. No horizontal scroll.
- **Primary Tabs (always visible)**: Visão Geral, Férias e Ausências, Calendário da Equipa, Presenças.
- **Secondary Tabs ("Mais" dropdown)**: Escalas & Horários, Directório & Mapeamento, Gestão de Crachás, Revisão de Presenças (Admin/HR only).
- **"Mais" active state**: The "Mais" button displays as an active tab when the current route belongs to a secondary tab.
- **Dropdown behavior**: Click-to-toggle, outside-click dismiss, Escape key dismiss, animated entry.
- **Role-aware visibility preserved**: Viewer/Management sees only primary subset (overview, calendar, leave) — no "Mais" button. Admin/HR sees all tabs including diagnostic.
- **Removed `framer-motion`** dependency from `HRLandingPage.tsx` (was used only for the old animated pill indicator).

### Files Changed
- `frontend/src/pages/HR/HRLandingPage.tsx` — Full rewrite to Finance-style NavLink pattern with "Mais" dropdown.
- `frontend/src/pages/HR/hr-landing.css` — Replaced pill styles with dropdown panel styles. Removed `overflow-x: auto`.

## [v2.100.0] - 2026-05-13 - HR Command Center: Scope-Enforced KPI Cards

### Changed
- **HR Command Center KPI Scope**: Dashboard endpoint now uses `GetTeamScopedEmployeesQuery()` (shared with calendar) instead of `GetScopedEmployeesQuery()`. KPI values (Ausentes Hoje, Em Férias, Aguardando Análise, Efetivo Ativo Mapeado) are calculated only over the user's scoped employee base.

### Added
- **`GetTeamScopedEmployeesQuery()`**: Refactored from `GetCalendarScopedEmployeesQuery()`. Shared by calendar and dashboard endpoints. Viewer/Management resolves to department-level; privileged roles delegate to standard scope.
- **Dashboard scope metadata**: `scopeType` and `scopeDescription` returned to frontend for contextual display.
- **Overview tab access**: Viewer/Management users now see the "Visão Geral" tab.
- **Role-filtered admin sections**: "Ação Necessária" and sync badge restricted to HR/System Administrator.
- **Scoped click targets**: KPI card navigation adapted per role (Viewer/Management → calendar instead of restricted employee pages).

### Security
- Leave management scope unchanged — self-only for Viewer/Management.
- KPI data remains aggregate-only — no employee-level PII exposed.
- Admin operational indicators hidden from non-admin users.

## [v2.99.9] - 2026-05-13 - HR Leave Notification System

### Added
- **HR Leave In-App Notifications**: Notification bar alerts for HR leave/absence request lifecycle events using existing `InformationalNotification` infrastructure.
  - **SUBMITTED → Approver notification**: Resolves via `HREmployee.ManagerUserId` → `Department.ResponsibleUserId` fallback. Warning type.
  - **APPROVED → Requester notification**: Success type.
  - **REJECTED → Requester notification**: Error type. No rejection reasons exposed.
  - **CANCELLED → Requester notification**: Only when cancelled by a different actor. Self-cancel is silent.
- **`NotificationCategories.HRLeave`**: New `"HR_LEAVE"` category in `NotificationConstants.cs`.
- **`INotificationService` injection**: `HRLeaveController` now receives notification service + logger for non-blocking dispatch.
- **Dedup via `LeaveStatusHistory.Id`**: Each notification uses the status history entry ID as `EventCorrelationId`, preventing duplicates from auto-submit or retries.

### Security
- Notifications contain only: employee name, leave type, date range.
- No notes, medical details, rejection reasons, approval comments, or attachments exposed.
- Self-notification suppressed: actors do not receive notifications for their own actions.

### Not Included
- No email notifications (deferred to future HR evaluation).
- No frontend changes (existing `NotificationBell.tsx` handles new notifications automatically).

## [v2.99.8] - 2026-05-13 - HR Calendar: Department Visibility for Viewer / Management

### Changed
- **HR Calendar Scope**: Viewer / Management users now see the team/department calendar instead of self-only. The backend resolves the user's linked HREmployee → `PortalDepartmentId` and returns all active employees from that department. Falls back to self-only if no department, or empty if unlinked/inactive.

### Added
- **`GetCalendarScopedEmployeesQuery()`**: Calendar-specific scope method in `HRLeaveController`. Only affects `GET /api/hr/leave/calendar`. Privileged roles delegate to unchanged `GetScopedEmployeesQuery()`. Leave management remains self-only.
- **`scopeType = "team"`**: New scope metadata for Viewer / Management with department access.
- **Frontend `"team"` scope**: `HRTeamCalendar.tsx` shows "Calendário da Equipa" with informational subtitle for team scope.

### Security
- `GetScopedEmployeesQuery()` unchanged — leave endpoints remain self-only for Viewer / Management.
- Calendar projection excludes sensitive fields (notes, reasons, attachments, medical details, approval comments).
- Only active employees returned (`IsActive == true`).

## [v2.99.7] - 2026-05-13 - HR Self-Service Leave Management for Viewer / Management

### Changed
- **HR Sidebar — "Férias e Ausências" Access**: Removed role restriction from sidebar item. Now visible to all HR-accessing roles including Viewer / Management. Backend `GetScopedEmployeesQuery()` remains the data scope boundary.
- **HR Tabs — Self-Service Extended**: `VIEWER_ONLY_TABS` expanded to `['calendar', 'leave']`. Viewer / Management users see both Calendar and Leave tabs.

### Added
- **Self-Service Leave UI (`HRLeaveList.tsx`)**: Role-aware `isSelfServiceOnly` mode:
  - Auto-resolves the user's linked HREmployee from the scoped backend API on mount.
  - New request drawer: read-only employee display replaces `EmployeeAutocomplete` for self-service.
  - Helper text: "Esta solicitação será registada automaticamente em seu nome."
  - Unlinked user warning with disabled creation when no HREmployee is linked.
  - Approve/Reject action buttons hidden for self-service users.
  - Cancel restricted to DRAFT/SUBMITTED for self-service (APPROVED requires HR intervention).
  - Context-appropriate subtitle for self-service mode.
- **Backend verified as secure** — no backend changes required:
  - `CreateLeaveRecord` validates `EmployeeId` via `GetScopedEmployeesQuery()`.
  - `ApproveLeaveRecord` / `RejectLeaveRecord` gated by `IsAdminOrHR`.
  - `CancelLeaveRecord` validates scope for non-admin users.

## [v2.99.6] - 2026-05-13 - HR Sidebar: Role-Aware Navigation for Viewer / Management

### Changed
- **HR Sidebar — Role-Aware Filtering**: Viewer / Management users now see only "Calendário da Equipa" in the sidebar R.H. group. Previously, all HR links (Visão Geral, Férias e Ausências, Funcionários, Layouts, Histórico de Impressão) were visible to any user with HR module access, regardless of their actual role permissions.
- **New sidebar item**: "Calendário da Equipa" (`/hr/calendar`) added to the HR sidebar navigation. Visible to all HR-accessing roles including Viewer / Management. Uses `CalendarDays` icon matching the HRLandingPage tab.
- **Restricted items**: Visão Geral, Férias e Ausências, Funcionários, Layouts, and Histórico de Impressão now require `HR`, `System Administrator`, or `Local Manager` roles.
- **No backend changes**. This is purely navigation cleanup — backend scope enforcement remains the source of truth.

## [v2.99.5] - 2026-05-13 - Self-Calendar Mapping Fix & Sync-Safety

### Fixed
- **Self-Calendar Mapping — Sync-Safety**: `HREmployeeSyncService.cs` now preserves manually-linked corporate emails when Innux provides NULL/empty. Previously, every sync cycle would erase `HREmployee.Email` because Innux has no email data, breaking self-calendar for Portal users.
- **Self-Calendar Empty-State Message**: Both `HRTeamCalendar.tsx` and `HRAttendanceCalendar.tsx` now display an actionable message when no employee is linked to the user, guiding them to contact HR for user-employee linking instead of the previous ambiguous "no records found" message.

### Changed
- **HREmployee.Email — Business Rule Documentation**: Clarified that `HREmployee.Email = NULL` is valid and expected for employees who do not use Portal self-service. Only employees with corporate email / Portal user accounts require this mapping. Non-self-service employees are managed by their direct manager, Local Manager, or authorized HR user.

### Data
- **Abel Domingos (EmployeeCode: 21000184)**: Set `HREmployee.Email = 'abel.domingos@alpla.com'` for self-service test user. Targeted correction — no other employee records were modified.

## [v2.99.4] - 2026-05-13 - UX Fix: HR Default Route for Viewer / Management

### Fixed
- **HR Default Route — Viewer / Management Redirect**: Viewer/Management users navigating to `/hr` are now redirected to `/hr/calendar` (the only tab they have access to) instead of `/hr/overview`. All other HR roles (System Administrator, HR, Local Manager, Department Manager) continue landing on `/hr/overview` as before.
- **Implementation**: New `HRIndexRedirect` component in `App.tsx` checks `isViewerManagement && !hasHRAccess` to determine the appropriate default route. This matches the existing tab-filtering logic in `HRLandingPage.tsx`.
- **No backend changes**. Sidebar role-filtering remains a documented future improvement.

## [v2.99.3] - 2026-05-13 - HR Module Access — Frontend Route Guard Alignment

### Changed
- **Frontend HR route guard**: Expanded `hasHRModuleAccess` in `AuthContext.tsx` to include `Local Manager` and `Viewer / Management` roles, matching the backend's `HasHRModuleAccess()` scope.
- **Role-aware tab visibility**: `HRLandingPage.tsx` now filters HR tabs by role. Viewer/Management sees only "Calendário da Equipa" (self-calendar). All other roles see the full tab set.
- **No backend changes**: Backend `GetScopedEmployeesQuery()` remains the source of truth for data scope.

## [v2.99.2] - 2026-05-12 - Diagnostic Review — Onboarding & Help UX

### Added
- **Help Drawer**: "Como usar esta tela?" button in the diagnostic banner opens a full guide drawer with page purpose, step-by-step instructions, field glossary, and severity level explanations. All in Portuguese. Follows existing `PurchasingHelpDrawer` pattern.
- **Severity Legend**: Inline legend strip above results table (Alta/Média/Baixa/Nenhuma with descriptions).
- **Column Tooltips**: Info icons on 6 key table headers with `ModernTooltip` explanations on hover.
- **Initial Guidance**: Improved empty-state before first search with structured guidance and hint to help button.
- **Scope**: Purely visual/UX. No backend or comparison logic changes. Page remains diagnostic/read-only.

## [v2.99.1] - 2026-05-12 - Diagnostic Review — Employee Search Autocomplete

### Changed
- **Employee Search Filter**: Replaced the technical "ID Funcionário (Innux)" number input with a name-based autocomplete in the `/hr/attendance-review` filter bar.
  - Debounced search (300ms) via `GET /api/hr/leave/employees?search=`. Dropdown shows employee name, department, and Innux ID.
  - Selected employee displayed as `Name (#InnuxID)` with clear button to revert to unfiltered view.
  - Keyboard navigation (↑/↓/Enter/Escape) and outside-click dismiss fully supported.
  - Backend: Added `InnuxEmployeeId` to `GetEmployees` projection (additive, backwards-compatible).

### Files Changed
- `backend/AlplaPortal.Api/Controllers/HRLeaveController.cs` — Added `InnuxEmployeeId` to employees projection.
- `frontend/src/pages/HR/HRAttendanceDiagnostics.tsx` — New `EmployeeAutocomplete` sub-component replacing numeric input.
- `frontend/src/pages/HR/hr-attendance-diagnostics.css` — Added autocomplete dropdown styles.
- `docs/VERSION.md` — Bumped to v2.99.1.
- `docs/CHANGELOG.md` — This entry.

---

## [v2.99.0] - 2026-05-12 - Portal Attendance Engine — Phase 4: Diagnostic Review UI

### Added
- **Attendance Diagnostic Review UI**: New read-only diagnostic page at `/hr/attendance-review` for HR/Admin users to visually inspect attendance discrepancies between Innux processed data and Portal raw-punch interpretation.
  - Filter bar with date range, employee ID, severity filter, and "Apenas divergências" toggle. Client-side 31-day validation.
  - Summary KPI cards: total days, severity breakdown (None/Low/Medium/High), execution time.
  - 13-column results table with severity badges, confidence indicators, and clickable rows.
  - Detail drawer with Innux vs Portal side-by-side comparison, discrepancy messages, warnings, recommended action, raw punches timeline, and punch pairs (on-demand from `interpret-punches`).
  - Diagnostic banner: "Esta tela é apenas diagnóstica. Nenhuma informação é gravada no Innux ou Primavera."
  - Access restricted to System Administrator and HR roles. Department Managers blocked by both tab visibility and page-level role guard.
  - Severity visual style: High=red, Medium=orange, Low=blue/informational, None=neutral gray.

### Files Changed
- `frontend/src/lib/api.ts` — Added `hrAttendanceDiagnostics` namespace with `compareRange` and `interpretPunches`.
- `frontend/src/pages/HR/HRAttendanceDiagnostics.tsx` — New diagnostic page component.
- `frontend/src/pages/HR/hr-attendance-diagnostics.css` — Scoped CSS for diagnostic UI.
- `frontend/src/pages/HR/HRLandingPage.tsx` — Added conditional "Revisão de Presenças" tab.
- `frontend/src/App.tsx` — Added lazy-loaded route with AdminRoute guard.
- `docs/VERSION.md` — Bumped to v2.99.0.
- `docs/CHANGELOG.md` — This entry.
- `docs/innux-operational-model.md` — Added Phase 4 documentation.

---

## [v2.98.1] - 2026-05-12 - Comparison Engine Hardening: Worked-Minutes Enrichment

### Fixed
- **Innux Worked-Minutes Enrichment**: Comparison engine now enriches `InnuxWorkedMinutes` from `AlteracoesPeriodos` (via `GetWorkedHoursAsync`) when the calendar summary returns 0 for a present employee. Eliminates false Medium discrepancies.
  - Triggered only when `InnuxWorkedMinutes == 0` and `InnuxStatus ∈ {Present, PortalInterpreted, Anomaly}`.
  - New DTO fields: `InnuxWorkedMinutesSource` (`CalendarSummary` | `DayDetail` | `NotAvailable`), `InnuxWorkedMinutesEnriched` (bool).
  - If enrichment yields `NotAvailable`, severity drops from Medium→Low with a clear Portuguese message.
  - `PortalInterpreted` status now included in the present-family check for worked-minutes comparison.

### Files Changed
- `backend/AlplaPortal.Application/DTOs/Integration/PortalAttendanceEngineDtos.cs` — Added `InnuxWorkedMinutesSource`, `InnuxWorkedMinutesEnriched` fields.
- `backend/AlplaPortal.Infrastructure/Services/Integration/AttendanceComparisonService.cs` — Enrichment step (1b), updated M2/L1 rules for `PortalInterpreted` + `NotAvailable` handling.
- `docs/VERSION.md` — Bumped to v2.98.1.
- `docs/CHANGELOG.md` — This entry.
- `docs/innux-operational-model.md` — Updated enrichment documentation.

---

## [v2.98.0] - 2026-05-12 - Portal Attendance Engine — Phase 3: Comparison Engine

### Added
- **Attendance Comparison Engine**: Backend-only diagnostic service that contrasts Innux processed attendance against Portal raw-punch interpretation. Does not replace the current HR Attendance Calendar behavior.
  - **AttendanceComparisonService**: Orchestrates `IInnuxAttendanceService`, `IPortalPunchInterpreter`, and `IPortalScheduleResolver` without new SQL queries. Compares presence status, entry/exit times, and worked minutes.
  - **Portal Status Derivation**: Derives attendance status from raw punches: `Present` (complete pairs, worked > 0), `NoPunches`, `Incomplete`, `DayOff` (rest day, no punches), `PresentOnRestDay`.
  - **Discrepancy Rules** (explicit, no ambiguous logic):
    - **HIGH**: Innux Absent/DayOff/Vacation/Holiday/JustifiedAbsence + Portal Present. Innux Present + Portal NoPunches.
    - **MEDIUM**: Both present but worked diff > 30min. Entry/exit time drift > 30min. Innux Present + Portal Incomplete. Duplicates detected.
    - **LOW**: Worked diff 1-30min. Schedule fallback via Alteracoes.IDHorario. Low Portal confidence.
  - **Portuguese Messages**: All `DiscrepancyMessages` and `RecommendedReviewAction` in Portuguese for HR users.
  - **Diagnostic Endpoints** (SystemAdministrator + HR roles):
    - `GET /api/hr/attendance/portal/compare/{innuxEmployeeId}/{date}` — single-day comparison.
    - `GET /api/hr/attendance/portal/compare-range?startDate=&endDate=&innuxEmployeeId=&departmentId=&onlyDiscrepancies=true` — range comparison (max 31 days).
  - **Range Safeguards**: 31-day maximum, clear 400 validation error, execution time logging.
  - **DTOs**: `AttendanceComparisonResultDto`, `DateRangeComparisonResultDto`. Replaces unused `AttendanceComparisonReadyDto` placeholder.
  - **Design Decision**: Schedule fallback (Alteracoes.IDHorario) is context only, NOT proof of attendance. Portal attendance evidence comes exclusively from raw punches.

### Files Changed
- `backend/AlplaPortal.Application/DTOs/Integration/PortalAttendanceEngineDtos.cs` — Phase 3 DTOs.
- `backend/AlplaPortal.Application/Interfaces/Integration/IAttendanceComparisonService.cs` — New interface.
- `backend/AlplaPortal.Infrastructure/Services/Integration/AttendanceComparisonService.cs` — Phase 3 implementation.
- `backend/AlplaPortal.Api/Controllers/HRAttendanceController.cs` — 2 new diagnostic comparison endpoints.
- `backend/AlplaPortal.Api/Program.cs` — DI registration for `IAttendanceComparisonService`.
- `docs/VERSION.md` — Bumped to v2.98.0.
- `docs/CHANGELOG.md` — This entry.

---

## [v2.97.0] - 2026-05-12 - Foundation: Portal-Side Attendance Interpretation Engine (Phases 1 & 2)

### Added
- **Portal-Side Attendance Interpretation Engine**: Backend-only, read-only foundation for an independent Portal-side attendance engine. This lays the groundwork for comparing Portal-computed attendance against Innux-processed results in a future Phase 3 (Comparison Engine).
  - **Phase 1 — Schedule Day Resolver (`PortalScheduleResolver`)**: Resolves the expected schedule for an employee on any date by computing cycle day offsets from `PlanosTrabalho`, mapping to `PlanosTrabalhoHorarios`, then hydrating from `HorariosPeriodos`. Handles overnight shift detection (entry > exit), expected entry/exit calculation, expected working minutes, and rest day identification.
  - **Phase 2 — Raw Punch Interpreter (`PortalPunchInterpreter`)**: Reads raw `TerminaisMarcacoes` records and performs full interpretation. Supports three direction inference strategies: standard EN/SA, alternate codes 17→Entry/18→Exit, and position-based inference for empty directions. Flags duplicate punches (`IsDuplicateCandidate`) without removing them. Builds Entry/Exit pairs, calculates worked minutes per pair and total, and assigns confidence scores (`High`/`Medium`/`Low`/`None`). Handles overnight shifts with schedule-bounded cutoffs for next-day punch collection.
  - **Diagnostic Endpoints**: Two new investigative-only endpoints in `HRAttendanceController`, restricted to `SystemAdministrator` and `HR` roles:
    - `GET /api/hr/attendance/portal/resolve-schedule/{innuxEmployeeId}/{date}` — Returns the resolved schedule with periods, expected times, and overnight flag.
    - `GET /api/hr/attendance/portal/interpret-punches/{innuxEmployeeId}/{date}` — Returns interpreted punches with direction, confidence, pairs, worked minutes, and warnings.
  - **DTOs**: `PortalAttendanceEngineDtos.cs` — `ResolvedScheduleDayDto`, `SchedulePeriodDto`, `PunchInterpretationResultDto`, `InterpretedPunchDto`, `PunchPairDto`.
  - **Interfaces**: `IPortalScheduleResolver`, `IPortalPunchInterpreter` — clean abstractions for testability.

### Architecture Notes
- All services use strictly read-only parameterized SQL queries (`SELECT` only). Zero writes to Innux or Primavera.
- Every interpretation decision is captured via `InterpretationReason` and `InterpretationRule` fields for full audit transparency.
- Duplicate punches are preserved and flagged, not deleted — HR can inspect them in the diagnostic output.
- Existing production HR Attendance Calendar remains unchanged. These endpoints are for diagnostic/investigative use only.

### Files Changed
- `backend/AlplaPortal.Application/DTOs/Integration/PortalAttendanceEngineDtos.cs` — New DTOs.
- `backend/AlplaPortal.Application/Interfaces/Integration/IPortalScheduleResolver.cs` — New interface.
- `backend/AlplaPortal.Application/Interfaces/Integration/IPortalPunchInterpreter.cs` — New interface.
- `backend/AlplaPortal.Infrastructure/Services/Integration/PortalScheduleResolver.cs` — Phase 1 implementation.
- `backend/AlplaPortal.Infrastructure/Services/Integration/PortalPunchInterpreter.cs` — Phase 2 implementation.
- `backend/AlplaPortal.Api/Controllers/HRAttendanceController.cs` — 2 new diagnostic endpoints.
- `backend/AlplaPortal.Api/Program.cs` — DI registrations for `IPortalScheduleResolver` and `IPortalPunchInterpreter`.

---

## [v2.96.3] - 2026-04-28 - Fix: HR Attendance — False Absences (F03) due to Code 17 Anomalies

### Fixed
- **Global Portal Override**: When the Portal detects a valid presence (e.g., multiple "Code 17" terminal punches spanning > 60 minutes) but Innux incorrectly classifies the day as a "Falta Injustificada" (F03), the Portal now overrides this display. It zeroes out the `absenceMinutes` and flags the period with a "PORTAL" work description.
- **Data Integrity**: This interpretation is strictly presentation-level within the Portal. No actual modification is made to the source Innux or Primavera databases.

### Files Changed
- `backend/AlplaPortal.Infrastructure/Services/Integration/InnuxAttendanceService.cs`
- `backend/AlplaPortal.Application/DTOs/Integration/InnuxAttendanceDtos.cs`
- `frontend/src/features/hr/components/HRAttendanceCalendar.tsx`

## [v2.96.2] - 2026-04-28 - Fix: HR Attendance — Innux Direction Codes 17/18 Mapping

### Fixed
- **Innux Direction Code 17 → Entrada**: Terminal punch records with direction code `17` are now correctly interpreted as entry punches instead of being labelled as "Código 17" (unknown).
- **Innux Direction Code 18 → Saída**: Terminal punch records with direction code `18` are now correctly interpreted as exit punches instead of being labelled as "Código 18" (unknown).
- **Anomaly False Positives**: Attendance days where employees punched using terminals emitting codes 17/18 are no longer incorrectly classified as anomalies solely due to the direction code being unrecognised. True anomalies (missing pair, shift conflict, duplicate punch, impossible sequence) continue to be flagged normally.

### Technical Details
- Centralised fix in `MapDirectionLabel()` — the single source of truth for Innux direction-code interpretation.
- DTO documentation updated to reflect the expanded set of known direction codes.
- No data was written to Innux or Primavera — this is a display/interpretation-only change.

### Files Changed
- `backend/AlplaPortal.Infrastructure/Services/Integration/InnuxAttendanceService.cs` — Added code 17/18 mappings to `MapDirectionLabel()`; updated anomaly comment.
- `backend/AlplaPortal.Application/DTOs/Integration/InnuxAttendanceDtos.cs` — Updated `Direction` and `DirectionLabel` xmldoc comments.

---

## [v2.96.1] - 2026-04-28 - Fix: Approval Price Analysis Banner — Remove Misleading "Preços Favoráveis"

### Changed
- **Approval Detail Panel — Price Analysis Banner**: Removed the misleading green "Preços Favoráveis" success banner that appeared when all items were within the historical average range. "Not above average" does not equal "favorable" — the previous presentation created a false sense of positive pricing when the system could only confirm prices were not above the mean.
- **Warning-Only Policy**: The price analysis banner now follows a warning-only policy:
  - **Items above average**: Amber warning banner with count of affected items (e.g., "2 itens deste pedido estão com preço acima da média histórica.").
  - **Items within/below average**: No banner displayed. The detailed per-item intelligence panel (already present in the "Inteligência para Decisão" section) provides granular price analysis with variation percentages.
- **Count Precision**: The warning banner now displays the exact count of items above average instead of a generic "um ou mais itens" message.

### Files Changed
- `frontend/src/pages/Approvals/ApprovalDetailPanel.tsx` — Replaced binary banner (warning vs. success) with conditional warning-only banner. Removed `hasHistoricalItems` guard; banner now gated exclusively on `hasItemAboveAvg`.

---

## [v2.96.0] - 2026-04-28 - Feature: OCR Catalog Item Auto-Match (DEC-123)

### Added
- **Backend Batch-Match Endpoint**: `POST /api/v1/catalog-items/batch-match` accepts an array of OCR-extracted item descriptions and returns index-keyed matches against the active item catalog. Uses in-memory normalized exact matching (trim, lowercase, diacritic removal, whitespace collapse, trailing punctuation strip) applied identically to both incoming descriptions and stored catalog records. Only 100% normalized exact matches are accepted — no fuzzy/partial matching.
- **Frontend Auto-Match Integration (Request Flow)**: `useOcrProcessor.ts` now calls the batch-match endpoint after OCR item mapping. Matched items are automatically linked to their catalog entry (`itemCatalogId`, `itemCatalogCode`) with `autoMatchStatus = 'AUTO_MATCHED'`. Unmatched items receive `autoMatchStatus = 'NEEDS_REVIEW'`.
- **Frontend Auto-Match Integration (Quotation Flow)**: `QuotationEntry.tsx` OCR processing includes the same batch-match step, ensuring consistent behavior across both Request and Quotation creation flows.
- **UX Badges**: Green "Correspondência automática" badge (with catalog code) for auto-matched items. Amber "Item não catalogado — verifique manualmente" badge for unmatched items. Badges render in both Request and Quotation item tables.
- **Manual Override Behavior**: When a user edits the item description or manually selects a catalog item via autocomplete, the `autoMatchStatus` is cleared to prevent misleading badge display. User intent always takes precedence over auto-match.
- **API Client**: Added `api.catalogItems.batchMatch()` method to the frontend API client.
- **Type Model**: Added `autoMatchStatus` field to both `OcrDraftItem` and `QuotationDraftItem` types.

### Design Decisions
- **Non-Blocking**: Auto-match failure (API error, timeout) does not block the OCR flow. Items remain in their default state and users can still manually link via the autocomplete.
- **No Auto-Creation**: Items that do not match the catalog are never automatically created. They stay as free-text descriptions for manual reconciliation.
- **Catalog Default Unit Inheritance**: When an auto-match is found and the OCR item has no resolved unit, the catalog item's default unit is automatically applied.

### Files Changed
- `backend/AlplaPortal.Api/Controllers/CatalogItemsController.cs` — New `BatchMatch` endpoint with `BatchMatchRequest`/`BatchMatchResponse` DTOs and `NormalizeDescription` helper.
- `frontend/src/lib/api.ts` — Added `batchMatch` method to `catalogItems` namespace.
- `frontend/src/types/index.ts` — Added `autoMatchStatus` to `OcrDraftItem`.
- `frontend/src/types/quotation.ts` — Added `autoMatchStatus` to `QuotationDraftItem`.
- `frontend/src/hooks/useOcrProcessor.ts` — Added catalog auto-match step (Part C) with diagnostic logging.
- `frontend/src/pages/Requests/RequestCreate.tsx` — Manual override logic in `handleUpdateOcrItem` and `handleCatalogSelectOcrItem`; UX badges in payment items table.
- `frontend/src/components/QuotationEntry.tsx` — Auto-match step in `_processUpload`; manual override in catalog select; UX badges in quotation items table.

---

## [v2.95.0] - 2026-04-27 - Workflow: Decouple Operational Receiving from Financial Receipt (DEC-122)

### Changed
- **Receiving Workspace — Semantic Decoupling**: The Receiving workspace no longer finalizes requests. The "FINALIZAR PEDIDO" button has been renamed to "CONFIRMAR RECEBIMENTO" and now calls a dedicated `confirmReceiving` API endpoint. This enforces the business distinction between physical item receiving (Receiving role) and financial receipt closure (Finance role).
- **ApprovalModal — New Action Type**: Added `CONFIRM_RECEIVING` action type with dedicated labels and descriptions. The Receiving workspace modal now uses this type instead of `FINALIZE`.
- **getRequestGuidance — Split Guidance**: `PAYMENT_COMPLETED` now shows "Mover para fase de recebimento operacional" (for Receiving role), while `WAITING_RECEIPT` shows "Anexar recibo do fornecedor e finalizar pedido" (for Finance role). Previously both shared the same generic guidance.
- **isFinalizedStatus — Lifecycle Fix**: Removed `PAYMENT_COMPLETED` from the finalized status list, as it is an active operational status requiring Receiving action.

### Added
- **Backend Endpoint**: `POST /api/v1/requests/{id}/operational/confirm-receiving` — exclusively for the Receiving role. Confirms physical item/service receipt. Transitions to `WAITING_RECEIPT` (all received) or `IN_FOLLOWUP` (partial). Never transitions to `COMPLETED`.
- **FinalizeRequest Guard**: Finance-only terminal action (`WAITING_RECEIPT` → `COMPLETED`). Requires `TYPE_RECEIPT` attachment. Receiving role is explicitly blocked.

### Documentation
- **WORKFLOW_ARCHITECTURE.md**: Updated status tables, state machine, and permission matrix to reflect the decoupled workflow.
- **DECISIONS.md**: Added DEC-122 — Decoupling Physical Item Receiving from Supplier Financial Receipt.
- **CHANGELOG.md**: This entry.

### Files Changed
- `backend/AlplaPortal.Api/Controllers/RequestsController.cs` — New `ConfirmReceiving` endpoint, refactored `FinalizeRequest` with Finance-only guard.
- `backend/AlplaPortal.Application/Helpers/RequestWorkflowHelper.cs` — `AreAllItemsReceived` status check, auto-completion prevention.
- `frontend/src/pages/Receiving/ReceivingOperation.tsx` — Button label, API call, modal type changes.
- `frontend/src/components/ApprovalModal.tsx` — Added `CONFIRM_RECEIVING` action type.
- `frontend/src/lib/utils.ts` — Split guidance, updated `isFinalizedStatus`.
- `docs/WORKFLOW_ARCHITECTURE.md` — Updated status and permission documentation.
- `docs/DECISIONS.md` — DEC-121 extended, DEC-122 added.

---

## [v2.94.0] - 2026-04-27 - Catalog Item Reconciliation Engine


### Fixed
- **Payment Request Autocomplete Bug**: Replaced plain `<input>` with `CatalogItemAutocomplete` in the Payment Request manual invoice flow. Items now searchable against the master catalog. (Phase 1)

### Added
- **Shared Reconciliation Hook (`useCatalogItemReconciliation`)**: Classifies items as MATCHED, UNMATCHED, CREATED_PENDING, LINKED_MANUALLY, or FREE_TEXT. Reusable across all item-entry flows.
- **Reconciliation Types**: Added `ReconcilableItem`, `ItemResolution`, `ClassifiedItem`, and `ReconciliationItemStatus` to shared types.
- **Backend Reconciliation-Create Endpoint**: `POST /api/v1/catalog-items/reconciliation-create` creates catalog items with `Origin = CREATED_PENDING_VALIDATION`. Includes duplicate detection.
- **Batch Reconciliation Modal (`CatalogItemReconciliationModal`)**: Shows all unresolved items in a single table with per-row actions: link to catalog, create new, or keep as free text.
- **Submission Warning Dialog (`ReconciliationWarningDialog`)**: Non-blocking guardrail shown before save/submit when unresolved catalog items exist. Offers review, override, or cancel.
- **QuotationEntry Integration**: Same reconciliation engine wired into quotation management flow.

### Files Changed
- `frontend/src/types/index.ts` — Added reconciliation types and `itemCatalogCode` to `OcrDraftItem`.
- `frontend/src/hooks/useCatalogItemReconciliation.ts` — New shared hook.
- `frontend/src/components/CatalogItemReconciliationModal.tsx` — New batch modal.
- `frontend/src/components/ReconciliationWarningDialog.tsx` — New warning dialog.
- `frontend/src/pages/Requests/RequestCreate.tsx` — Autocomplete fix + reconciliation integration.
- `frontend/src/components/QuotationEntry.tsx` — Reconciliation integration.
- `frontend/src/lib/api.ts` — Added `reconciliationCreate` method.
- `backend/AlplaPortal.Api/Controllers/CatalogItemsController.cs` — Added `ReconciliationCreate` endpoint and DTO.
- `docs/DECISIONS.md` — Logged DEC-120.

---

## [v2.93.5] - 2026-04-26 - Backend & Frontend: Hierarchical Budget Configuration

### Added
- **Hierarchical Budget Model**: Replaced flat department-based budget configuration with a granular hierarchy: Company → Plant → Department → (Optional) Cost Center.
- **Budget Matrix UI**: Created `FinanceBudgetConfig.tsx` to provide a filterable, editable matrix for managing hierarchical budgets, supporting real-time currency formatting and active/inactive toggles.
- **Backend Uniqueness Validation**: Enforced composite key validation `(FiscalYear, CompanyId, PlantId, DepartmentId, CostCenterId, CurrencyId)` during budget upserts instead of a database-level index to safely handle optional Cost Centers.
- **Granular Attribution**: Refactored `FinanceBudgetController` to calculate budget consumption at the line-item Cost Center level, gracefully falling back to general department budgets when no specific cost center is assigned.

### Changed
- **Database Schema**: Added `CompanyId`, `PlantId`, `CostCenterId`, and `IsActive` to the `AnnualBudget` entity. Dropped the unique index in favor of a covering index.
- **Data Reset**: Executed migration `AddHierarchicalBudgetScope`, applying a deliberate destructive reset of existing `AnnualBudget` records to allow manual reconfiguration using the new hierarchy.

### Files Changed
- `backend/AlplaPortal.Domain/Entities/AnnualBudget.cs` — Added hierarchical fields.
- `backend/AlplaPortal.Infrastructure/Data/ApplicationDbContext.cs` — Updated EF Core relationships and indexes.
- `backend/AlplaPortal.Infrastructure/Migrations/*_AddHierarchicalBudgetScope.cs` — Added schema migration and `DELETE` operation.
- `backend/AlplaPortal.Application/DTOs/Finance/BudgetDTOs.cs` — Updated DTOs.
- `backend/AlplaPortal.Api/Controllers/FinanceBudgetController.cs` — Refactored configuration and consumption logic.
- `frontend/src/pages/Finance/FinanceBudgetConfig.tsx` — Created new budget configuration UI.
- `docs/DECISIONS.md` — Logged DEC-118 detailing the shift to hierarchical budgets.

## [v2.93.4] - 2026-04-26 - UX: Budget Contextual Help for Comprometido/Pago

### Added
- **Budget Help Tooltips**: Added reusable contextual help icon (ℹ) to the Finance > Orçamento page explaining the difference between "Comprometido" and "Pago" for business users.
  - Help appears in two key locations: **Síntese Global** header and **Centros de Custo** section title.
  - Hover to reveal a rich tooltip with definitions, color-coded terms, and a practical example (508.906,26 Kz / 508.000,00 Kz).
  - Uses existing `ModernTooltip` component for consistency with Portal UX.
  - Reusable `BudgetHelpContent` and `BudgetHelpIcon` components for future use.

### Files Changed
- `frontend/src/pages/Finance/FinanceBudget.tsx` — Added `BudgetHelpContent`, `BudgetHelpIcon` components; placed help icons in KPI header and CC section title.

## [v2.93.3] - 2026-04-26 - Feature: Monthly Budget Evolution Chart by Cost Center

### Added
- **Monthly Budget Evolution Chart**: New stacked bar chart on the Finance > Orçamento page showing the monthly distribution of committed and paid values by cost center for the selected department and year.
  - Chart renders below the existing cost center summary in the right panel.
  - Segmented toggle control with three modes: **Comprometido**, **Pago**, **Ambos**.
  - In "Ambos" mode, uses grouped stacked bars (committed at full opacity, paid at 45% opacity) for clear visual separation.
  - Top 5 cost centers are named with distinct colors; remaining CCs are grouped as "Outros".
  - Empty state shows: "Sem movimentações orçamentais para os critérios selecionados."
  - Chart reacts to department selection, year change, and mode toggle.

### Technical Details
- **New Backend Endpoint**: `GET /api/v1/finance/budget/department/{departmentId}/monthly/{year}` — returns 12 months (Jan–Dec) with per-cost-center committed/paid breakdown.
- **Monthly Aggregation Logic**:
  - Comprometido: grouped by `CreatedAtUtc.Month` (consistent with existing yearly filter).
  - Pago: grouped by `ActualPaidAtUtc.Month` (fallback to `CreatedAtUtc.Month`).
- **New DTOs**: `BudgetMonthlyDataDto`, `BudgetMonthlyCostCenterDto`.
- **Frontend**: Uses existing `recharts` library (BarChart, stacked bars, responsive container).

### Files Changed
- `AlplaPortal.Application/DTOs/Finance/BudgetDTOs.cs` — 2 new DTO classes
- `AlplaPortal.Api/Controllers/FinanceBudgetController.cs` — New endpoint
- `frontend/src/lib/api.ts` — New API method `getMonthlyBreakdown`
- `frontend/src/pages/Finance/FinanceBudget.tsx` — Chart state, effects, toggle, and chart rendering

## [v2.93.2] - 2026-04-26 - Fix: Finance Workspace COMPLETED Status Visibility & Budget Status Include

### Fixed
- **Finance COMPLETED Status Visibility**: Requests reaching `COMPLETED` status (terminal state after all items received) were invisible in the Finance workspace — not appearing in Resumo Operacional, Pagamentos, or Orçamento. Root cause: `"COMPLETED"` was not defined in `RequestConstants.Statuses` and was absent from all Finance controller filter arrays.
  - Added `RequestConstants.Statuses.Completed = "COMPLETED"`.
  - Injected `Completed` into 3 `financeStatuses` arrays in `FinanceController` (summary, overview, payments).
  - Injected `Completed` into 2 `IsPaid` checks and the `completedThisMonth` filter in `FinanceController`.
  - Injected `Completed` into `CommittedStatuses` and 2 `IsPaid` checks in `FinanceBudgetController`.
- **Budget Committed/Paid Calculation Always Zero (Pre-existing Bug)**: Both budget overview and cost center detail queries in `FinanceBudgetController` were missing `.Include(r => r.Status)`. Without this, `req.Status?.Code` was always `null`, causing `CommittedStatuses.Contains(null)` to always be false. All budget committed/paid values returned 0 regardless of request status.

### Files Changed
- `AlplaPortal.Domain/Constants/RequestConstants.cs` — New constant
- `AlplaPortal.Api/Controllers/FinanceController.cs` — 6 filter locations updated
- `AlplaPortal.Api/Controllers/FinanceBudgetController.cs` — 5 locations updated + 2 Include fixes

### Verified
- Finance Summary: `completedThisMonth: 2`, `paidThisMonth: AOA 688,092.64` ✅
- Finance Budget: `committed: 688,998.90`, `paid: 688,998.90`, `usage: 3.44%` ✅
- Finance Payments: 2 items visible with COMPLETED status ✅
- Finance History: unchanged (11 entries) ✅

## [v2.93.1] - 2026-04-26 - Change: Payment Divergence — Zero-Tolerance Detection (DEC-110 Update)

### Changed
- **Payment Divergence Detection**: Removed the 1% relative tolerance (with 1.00 absolute floor) that previously suppressed small divergence warnings. Any non-zero difference between `ActualPaidAmount` and `ApprovedTotalAmount` (after standard 2-decimal currency rounding via `Math.Round(value, 2)`) now creates a `PAYMENT_DIVERGENCE_DETECTED` audit entry.
- **Directional Divergence Messages**: Audit entries now indicate whether the payment was "abaixo do valor aprovado" (below) or "acima do valor aprovado" (above), with absolute difference and percentage.
- **HasPaymentDivergence DTO Flag**: Updated to use `Math.Round` equality check instead of tolerance-gated comparison.

### Documentation
- **WORKFLOW_ARCHITECTURE.md**: Updated §6 Divergence Detection with zero-tolerance rule and revised scenario matrix.
- **DECISIONS.md**: Updated both DEC-110 instances to reflect the removal of the tolerance gate.
- **MANUAL_TEST_GUIDE.md**: Updated payment validation references to reflect zero-tolerance policy.

### Technical Notes
- OCR Financial Integrity tolerance (`RequestConstants.FinancialIntegrity`) is unchanged — it is a separate concern for OCR-vs-quotation comparison.
- No frontend changes required — divergence is computed server-side and recorded in audit history.

## [v2.93.0] - 2026-04-25 - Performance: Optimized Portal Backend Performance (Requests & Receiving)

### Changed
- **Backend N+1 Query Refactoring**: Optimized `RequestsController.GetRequests` and `ProjectToListItem`. Replaced inefficient per-row subqueries (`Quotations`, `StatusHistories`, `LineItems`) with a projection pattern using anonymous objects. This collapses multiple database trips into a single optimized query execution, significantly improving throughput for large datasets.
- **Database Indexing**: Identified and applied missing indexes required for high-frequency filtering operations in the `GetScopedRequestsQuery` logic. Added indexes to `Request` table for: `RequestTypeId`, `DepartmentId`, `PlantId`, `CompanyId`, `NeedLevelId`, and `SelectedQuotationId` via EF Core migration (`AddRequestPerformanceIndexes`).

### Technical Notes
- Applied EF Core migration to the LocalDB database to resolve index-related latency and timeouts observed with the seeded 50-request [DEMO] dataset.
- Loading times for the Requests list and Receiving workspace improved from timing out to under 2-3 seconds.

## [v2.90.0] - 2026-04-25 - Feature: Supplier Ficha Module, Approval Center Integration & P.O. Emission Guards

### Added
- **Supplier Ficha Module (Phase 2A)**: Full-stack delivery of the Supplier Registration (Ficha de Fornecedor) workflow within the Contracts module.
  - Backend: 11 new endpoints in `LookupsController` — CRUD, completeness engine, registration-check, submit-for-approval, DG-approve, DG-return, status history.
  - Frontend: `SupplierFichaList.tsx` (master list with search/filter), `SupplierFichaDetail.tsx` (editable detail page with document upload, completeness tracker, history timeline).
  - Status model: `DRAFT → PENDING_COMPLETION → PENDING_APPROVAL → ACTIVE / ADJUSTMENT_REQUESTED / SUSPENDED / BLOCKED`.
  - Single Final Approver workflow visible to users (DAF auto-stamped at submission for backend compatibility — invisible in UI).
  - Domain entities: Extended `Supplier` with 15+ registration fields, new `SupplierStatusHistory` audit entity, `SupplierConstants` status constants.
  - Two EF Core migrations: `AddSupplierRegistrationFields`, `AddSupplierApprovalWorkflow`.
- **Approval Center — Supplier Fichas Section (Phase 2B)**: Centralized supplier approval into the standard Approval Center drawer workflow.
  - New `SupplierApprovalPanel.tsx` drawer-content component matching the `ContractApprovalPanel` pattern (InfoCard grid, SectionBlocks, sticky action footer).
  - `ApprovalCenter.tsx` extended with supplier card queue (amber theme), click-to-open drawer, parallel data loading.
  - Supplier approval actions (Approve/Return) exclusively in the Approval Center — removed from `SupplierFichaDetail`.
  - Detail page approval tracker is now read-only: "Aguardando aprovação no Centro de Aprovações".
- **P.O. Emission Supplier Registration Guard**: `RegisterPoModal` calls `GET /suppliers/{id}/registration-check` on open.
  - ACTIVE: normal flow. PENDING_APPROVAL: amber warning banner. DRAFT/PENDING_COMPLETION/ADJUSTMENT_REQUESTED/SUSPENDED/BLOCKED: red blocking panel with disabled submit.
  - Supplier ID resolved from winning quotation (QUOTATION flow) or `formData.supplierId` (PAYMENT flow).
- **HR Attendance — Anomaly Detection Enhancement**: Days with raw terminal punches using unrecognised direction codes (e.g., code 17/18) while Innux reports "Falta Injustificada" are now classified as `Anomaly` instead of `Absent`. New `MapDirectionLabel` helper centralises direction code mapping.

### Changed
- **Approval Center Layout**: Supplier Fichas section uses compact card rows with selection highlighting, consistent with the contract approval queue visual pattern.
- **SupplierFichaDetail**: Stripped of all manual approval buttons (Aprovar, Solicitar Reajuste) and return modal — approval decisions are drawer-only in the Approval Center.

### Technical Notes
- Backend build: 0 errors. Frontend TypeScript: 0 new errors (25 pre-existing TS6133 in unrelated files).
- Architectural decision: All supplier approval actions centralized in Approval Center drawer (DEC-119).

## [v2.89.5] - 2026-04-25 - Feature: P.O. Emission Supplier Registration Guard (Phase 2A Completion)

### Added
- **P.O. Modal Registration Guard**: The `RegisterPoModal` now calls `GET /api/v1/lookups/suppliers/{id}/registration-check?operation=po` when opened, evaluating the supplier's registration status before allowing P.O. emission.
  - **ACTIVE**: No restrictions. Normal P.O. emission flow.
  - **PENDING_APPROVAL**: Amber warning banner displayed ("Fornecedor em Aprovação"), but P.O. emission is allowed. Users are advised to wait for approval.
  - **DRAFT / PENDING_COMPLETION / ADJUSTMENT_REQUESTED / SUSPENDED / BLOCKED**: Red blocking panel displayed ("Emissão de P.O Bloqueada") with supplier status badge. The "REGISTRAR P.O" button is disabled and grayed out.
- **Supplier ID Propagation**: `RequestEdit.tsx` now resolves the active `supplierId` from the winning quotation (QUOTATION flow) or from the request's `formData.supplierId` (PAYMENT flow) and passes it to `RegisterPoModal`.
- **Loading State**: A subtle spinner with "A verificar estado do fornecedor..." message appears while the registration check is in progress.

### Technical Notes
- Backend endpoint already existed from Phase 2A — this change is frontend-only.
- Guard resets cleanly on modal close/reopen.
- No impact on `CorrectPoModal` (PO correction flow uses same supplier, which has already been validated).

## [v2.89.4] - 2026-04-25 - Enhancement: HR Attendance — Absence-with-Raw-Punches Anomaly Detection

### Changed
- **Absence-with-Raw-Punches → Anomaly**: Days where Innux processed the official period as "Falta Injustificada" (F03) but raw terminal events exist with unrecognised direction codes (e.g., code 17) are now classified as `Anomaly` instead of `Absent`. This surfaces the contradiction for HR review without converting the day to "Present". Clean absences (no raw punches) remain classified as `Absent`.
- **Direction Label Mapping**: Raw punch direction codes now map to Portuguese labels (`EN` → "Entrada", `SA` → "Saída"). Numeric codes (e.g., "17") display as "Código 17" instead of the raw number, making it clear these are unrecognised terminal event types. Empty codes show "Sem direção".
- **Anomaly Description Broadened**: The Anomaly status description now covers both sub-types: (1) processed-without-raw-terminal-records, and (2) absence-declared-with-unrecognised-terminal-events. Updated across legend, guide modal, and drawer.

### Added
- **Absence-with-Raw-Punches Warning Banner**: New explanatory banner in the detail drawer for this specific anomaly type: "Existem marcações brutas no terminal, mas o Innux processou o período como Falta Injustificada. Verifique se as marcações foram feitas com código/direção não reconhecida ou se há necessidade de correção pelo R.H."
- **`MapDirectionLabel` Method**: New static helper in `InnuxAttendanceService` that centralises TipoProcessado → label mapping, replacing inline ternary logic.

### Technical Notes
- Classification rule 4b added to `ClassifyAttendance`: triggers when `absenceMinutes >= expectedMinutes AND rawPunchCount > 0 AND !hasEntry AND !hasExit AND punchCount == 0`.
- Investigated employee: APAULANTE DA CONCEIÇÃO FRANCISCO PAULO — 2026-04-06 (F03 / Falta Injustificada with 2 raw terminal events using code 17).
- Raw punch table column header changed from "Direcção" to "Direção / Código" for clarity.

## [v2.89.3] - 2026-04-25 - Fix: HR Attendance — Night Shift Cross-Midnight & Classification Accuracy

### Fixed
- **Detail Drawer Classification Mismatch**: The detail drawer computed attendance status before the actual raw punch count was known (using `rawPunchCount=-1` fallback), causing validated days to show "Desconhecido" even when entry/exit and raw punches existed. The drawer now re-classifies status after loading real raw punch data from `GetPunchesAsync`, ensuring calendar grid and drawer always agree.
- **Night Shift Cross-Midnight Punches**: `GetPunchesAsync` previously only fetched same-calendar-day punches (`tm.Data = @Date`), missing the exit punch for overnight shifts (e.g., 20:00–08:00+1). Now includes next-day early-morning punches (before 12:00) when `IsOvernightShift=true`.
- **Calendar `RawTerminalCount` for Night Shifts**: The correlated subquery in the calendar SQL now counts cross-midnight punches for overnight schedules, preventing false anomaly classification on night shift start days.
- **Validated Day with `Marcacao=0`**: For Escala-Intercalada patterns, Innux resets `Marcacao` to 0 after full validation. `ClassifyAttendance` now accepts `isValidated` and classifies validated days with entry/exit and raw punches as "Present" instead of "Unknown".

### Technical Notes
- `ClassifyAttendance` signature now includes `bool isValidated` — enables step 6c: validated + entry/exit + rawPunches > 0 = Present.
- `GetPunchesAsync` signature now includes `bool isOvernightShift` — triggers cross-midnight SQL date expansion.
- Anomaly preservation: processed days with zero raw terminal punches (e.g., Escala auto-processing without physical terminal records) remain classified as "Anomaly".
- Investigated employee: ANDERSON CLÁUDIO DOS SANTOS AZEVEDO (IDFuncionario 1626), Escala-Intercalada rotation (TN/FG/TM/FG cycle).
- Root cause confirmed as Portal query/classification issue, not Innux data inconsistency.


## [v2.89.2] - 2026-04-25 - Enhancement: HR Attendance — Icon-First Status System & Anomaly Reclassification
### Changed
- **Icon-First Status System**: Replaced all colored-dot status indicators in the calendar grid, legend, and guide modal with semantic Lucide icons. Each status now uses a meaningful icon (e.g., `CircleCheck` for Present, `CircleX` for Absent, `Palmtree` for Vacation, `ShieldAlert` for Anomaly). Legend and guide modal now render the exact same icon as the calendar cell — eliminating the previous mismatch where the legend showed colored dots but the calendar showed icons.
- **Anomaly Reclassification**: Days with processed Innux attendance (Alteracoes.Marcacao > 0) but zero raw terminal punches (TerminaisMarcacoes COUNT = 0) are now classified as `Anomaly` instead of `Present`. This surfaces Escala/rotation auto-processed days for HR review, since no physical terminal confirmation exists.
- **Unified Visual Config**: Created `STATUS_VISUAL_MAP` — a single source of truth for icon, label, CSS class, and description — shared across calendar cells, footer legend, and guide modal. Eliminated duplicated status→visual mappings.

### Added
- **Raw Punch Count in Calendar Query**: The calendar SQL query now includes a `RawTerminalCount` subquery (correlated `COUNT(*)` on `TerminaisMarcacoes`), enabling anomaly detection at the calendar level, not just in the detail drawer.
- **Anomaly Info Banner**: The detail drawer now shows a purple anomaly-themed banner (instead of the generic blue info banner) when a day has processed punches but no raw terminal records, with an explicit recommendation for HR review.

### Technical Notes
- The `ClassifyAttendance` method now accepts `rawPunchCount` as a parameter. "Present" status requires both `punchCount > 0` AND `rawPunchCount > 0`.
- New Lucide icons imported: `CircleCheck`, `CircleX`, `ShieldAlert`, `MinusCircle`.
- Old CSS classes (`att-status__dot`, `att-legend__swatch`) replaced with icon-based equivalents (`att-status__icon-wrap`, `att-legend__icon`).

## [v2.89.1] - 2026-04-25 - Fix: HR Attendance Calendar — Unified PunchCount Source
### Fixed
- **Calendar/Drawer Status Inconsistency**: Resolved a data source conflict where the calendar grid used a live `COUNT(TerminaisMarcacoes)` (raw terminal punches) while the detail drawer used `Alteracoes.Marcacao` (Innux-processed count). This divergence caused the same employee/date to show conflicting statuses (e.g., "?" on calendar vs "Presente" in drawer).
- **Unified PunchCount Source**: Both calendar grid and detail drawer now use `Alteracoes.Marcacao` as the canonical processed punch count for attendance status classification. The live `COUNT(TerminaisMarcacoes)` subquery has been removed from the calendar query.

### Added
- **Raw Punch Count Separation**: New `RawPunchCount` field on `AttendanceDaySummaryDto` exposing the live `COUNT(TerminaisMarcacoes)` as debug/audit data, separate from the official `PunchCount`.
- **Debug Metadata (Detail Drawer)**: The detail drawer now includes a collapsible "Dados Técnicos Innux" section exposing: `debugProcessedPunchCount`, `debugRawPunchCount`, `debugIsValidated`, `debugScheduleCode`, and `debugStatusSource` for HR/IT troubleshooting.
- **Informational Banner**: When `Alteracoes.Marcacao > 0` but zero raw terminal punches exist, the drawer displays a blue info banner explaining the discrepancy (manual validation, import, or purged records).

### Documentation
- **innux-operational-model.md**: Added formal implementation note under Assumptions §7 documenting the Portal's "processed-is-canonical" data source strategy.

## [v2.89.0] - 2026-04-25 - Feature: HR Attendance Calendar Modernization (Status, Metrics & Justifications)
### Fixed
- **Calendar Timezone Bug**: Resolved a -1 day rendering offset caused by `toISOString()` UTC conversion in the WAT (UTC+1) timezone. Replaced with local date component formatting (`YYYY-MM-DD`) across query parameters and React keys.

### Added
- **Vacation & Holiday Status Classification**: Extended the `ClassifyAttendance` engine to sub-classify justified absences into `Vacation` (🌴 "Gozo de Férias") and `Holiday` (⭐ "Feriado") statuses by parsing the Innux `Justificacao` text field. Added corresponding CSS styles, legend entries, and cell icons.
- **Worked Hours Metrics (Basic/Overtime)**: Implemented `GetWorkedHoursAsync` calculation engine that aggregates non-dispensed periods from `dbo.AlteracoesPeriodos` joined with `dbo.CodigosTrabalho`. Maps `Tipo = 'Normal'` → Basic and `Tipo LIKE 'Extra%'` → Overtime. Results merged into the calendar API response with graceful fallback on failure.
- **Drawer Metrics Display**: The employee day-detail drawer now shows "Básico", "Extra", and "Total Trab." metrics when worked hours data is available.
- **Justification Table (Structural)**: Created `HRAttendanceJustifications` database migration with FKs to `HREmployees` (Cascade) and `Users` (Restrict), indexes on `(HREmployeeId, Date)` and `Status`. Table supports future manager/employee justification workflow.

### Technical Notes
- Worked hours merge is non-blocking — if the calculation query fails, the calendar renders normally with zero values.
- Justification migration created but NOT yet applied. Entity class and DbSet registration pending Phase 4 functional work.

## [v2.88.0] - 2026-04-23 - Feature: HR Monthly Changes First Frontend Slice
### Added
- **HR Monthly Changes UI**: First frontend slice for the Innux-to-Primavera workflow.
  - Implemented `MonthlyChangesList` for viewing and creating processing runs.
  - Implemented `MonthlyChangesRunDetail` with tabs for Review Items, Anomalies, and Processing Logs.
  - Added support for filtering items by status and occurrence type.
  - Added visual anomaly flags and badging aligned with project design conventions.

## [v2.87.0] - 2026-04-23 - Hardening: HR Monthly Changes Detection Engine
### Fixed
- **Detection Overlap**: Resolved a potential overlapping logging defect in `OccurrenceDetectionEngine.cs` where both `UNJUSTIFIED_ABSENCE` and `LATENESS` could be generated for the exact same duration. Lateness is now evaluated first, and Unjustified Absence skips duplicated reporting.
- **Partial Justified Absences**: Fixed a bug where a day with both `AbsenceMinutes` and `JustifiedAbsenceMinutes` > 0 would fail to log the unjustified portion due to a strict `JustifiedAbsenceMinutes == 0` constraint in Rule 1.
- **Anomaly Escalation**: Improved the anomaly rule (Rule 4) to correctly upgrade all existing occurrences on a day to `NEEDS_REVIEW` with `IsAnomaly = true`, rather than only absence items.
### Added
- **Diagnostic Logging**: Added explicit occurrence type distribution counts and no-op snapshot counts to detection orchestrator logs to improve run quality tracking.
- **Data Validation Insight**: Confirmed via SQL analysis that `dbo.Alteracoes` (the Innux source) already pre-filters for exceptional attendance records (Falta, Ausencia, Anomalia). Thus, the 1147 synced snapshot rows legitimately produced 1147 actual occurrences, proving the 1:1 mapping was an expected behavior of the source table, rather than a detection overproduction bug.

## [v2.86.0] - 2026-04-23 - Foundation: HR Monthly Changes Middleware (Innux → Primavera)
### Added
- **HR Monthly Changes Middleware — Persistence Foundation**: 8 domain entities for the Innux-to-Primavera HR monthly export workflow:
  - `MCProcessingRun` — aggregate root, one per entity+month, full lifecycle state machine (DRAFT → SYNCING → NEEDS_REVIEW → READY_FOR_EXPORT → EXPORTED → CLOSED)
  - `MCAttendanceSnapshot` — immutable daily attendance data from Innux `Alteracoes` with unique constraint on (Run, Employee, Date)
  - `MCMonthlyChangeItem` — detected occurrence with lifecycle states (AUTO_CODED → APPROVED/ADJUSTED/EXCLUDED → EXPORTED)
  - `MCPrimaveraCodeMapping` — admin-configurable occurrence-to-Primavera-code rules with priority ranking
  - `MCDetectionThreshold` — admin-configurable lateness/detection thresholds per schedule/entity
  - `MCExportBatch` — export record with config audit snapshot (ConfigSnapshotJson + ConfigSnapshotHash per Amendment §5)
  - `MCExportRow` — denormalized export row mirroring Excel structure with source traceability
  - `MCProcessingLog` — pipeline diagnostic log entries for technical audit
- **EF Core Configuration**: Dedicated `IEntityTypeConfiguration<T>` classes with 15+ indexes, filtered anomaly index, unique snapshot constraint, and strict FK cascade policies (NoAction for Users, Restrict for audit-critical paths, Cascade for parent-child).
- **Migration**: `20260423143831_AddMonthlyChangesMiddleware` — creates all 8 tables. Stabilized ContractDocuments model snapshot drift from orphan migration.
### Technical Notes
- `CostCenter` is nullable across all entities (Amendment §4 — awaiting Primavera template confirmation)
- `TerminaisMarcacoes` (raw punches) NOT persisted in V1 — drill-down uses live Innux query (Amendment §3)
- `AUTO_CODED` items are NOT directly exportable — require explicit approval (Amendment §1)

## [v2.85.0] - 2026-04-22 - Feature: HR Attendance Calendar (Innux Integration)
### Added
- **HR Attendance Calendar Page**: New `HRAttendanceCalendar.tsx` component rendering an Innux-integrated attendance grid with employees in rows and days in columns. Cell colors and icons indicate attendance status (present, absent, rest day, overnight shift, anomaly).
- **Detail Drawer**: Clicking any attendance cell opens a slide-out drawer displaying full schedule details, punch times, balance minutes, justifications, and anomaly descriptions.
- **Backend Attendance API**: `HRAttendanceController` with endpoints for calendar data retrieval (`GetCalendar`), leveraging `IInnuxAttendanceService` and `IInnuxLookupService` for read-only Innux data access. Scope enforcement via `GetScopedEmployeesQuery()` ensures role-based data visibility.
- **Pagination**: 15 employees per page with navigation controls, displaying current page and total page count.
- **Alphabetical Sorting**: Employee list sorted alphabetically using locale-aware comparison (`pt-AO`).
- **Multi-Level Filters**: Dynamic dropdown filters for **Company**, **Plant**, and **Department** — derived from loaded data and applied client-side for instant responsiveness. Filters reset automatically on data refresh.
- **Employee Count Badge**: The "Funcionário" column header displays the total filtered employee count.
- **Month/Week View Toggle**: Segmented control for switching between full-month and ISO week-of-year calendar views.
- **Scroll-Contained Layout**: Flexbox-driven grid shell with native horizontal/vertical scrolling, sticky day header, and sticky first column — single scrollbar architecture with no mirror scrollbar.
### Backend
- **New Services**: `InnuxAttendanceService`, `InnuxLookupService`, `InnuxTimeHelper` — read-only Innux attendance data retrieval with schedule/department/shift lookups.
- **New DTOs**: `InnuxAttendanceDtos`, `InnuxLookupDtos` — typed contracts for attendance summary and lookup data.
- **Backend Data Projection**: `GetCalendar` endpoint exposes `plantName` and `companyName` through navigation properties to support frontend filtering.

## [v2.84.0] - 2026-04-22 - Feature: HR Team Calendar Modernization (Access Control + Week View)
### Added
- **Backend-Enforced Calendar Access Control**: `GetScopedEmployeesQuery()` now handles four distinct access tiers:
  - **System Admin**: full visibility.
  - **HR**: plant/department scope (OR logic — broad HR visibility).
  - **Local Manager**: plant/department scope (AND intersection logic — restrictive team visibility).
  - **Department Manager**: managed employees + managed department employees.
  - **Self-Calendar**: any authenticated user with a matching `HREmployee` record (email-based identity matching) sees only their own row.
- **`HasHRModuleAccess()` Broadened**: Now includes Local Manager role and self-calendar users. Safe because all HR endpoints also apply `GetScopedEmployeesQuery()` internally, limiting data to the user's scope.
- **Scope Metadata**: `GetCalendarData()` API response now includes a `scopeType` field (`all` | `hr` | `department` | `self`) for frontend header/mode adaptation.
- **Week View Mode**: ISO 8601 week-of-year visualization with week badge, 7-day horizontal navigation, and wider day columns.
- **Frozen Employee Column**: Sticky left column with scroll-aware shadow indicator and right-edge gradient hint.
- **Dedicated CSS**: `hr-team-calendar.css` using portal design tokens (`--color-*`, `--radius-*`, `--shadow-*`).
- **Scope-Aware UI**: Header adapts between "Meu Calendário" (self) and "Calendário da Equipa" (team). Legend footer shows context-appropriate scope description.
### Fixed
- **Local Manager Over-Broad Calendar Scope**: Fixed a critical scoping bug where the Local Manager branch used `OR` logic for plant/department filters, causing managers scoped to department TI to see all employees from all departments in their plant (Compras, Manutenção, Produção, etc.). Changed to `AND` intersection logic when both plant and department scopes exist. Also hardened the no-scope fallback to fail-safe (empty result) instead of broad visibility.
### Changed
- **View Mode Toggle**: Calendar now offers a segmented control (Mês / Semana) for switching between month and week views.
- **Navigation**: Replaced chevron arrows with standard ChevronLeft/ChevronRight icons and responsive prev/next labels.

## [v2.83.3] - 2026-04-21 - Feature: HR Employee Workspace Session Persistence
### Added
- **Session State Persistence**: The Funcionários (Employee Registration) screen now preserves its working state in `sessionStorage` when the user navigates to other submenus (Layouts, Histórico de Impressão) and restores it automatically upon return.
- **Persisted fields**: company, search query, search results, selected employee, unified profile, badge category, RFID card number, manual mode + manual fields, and selected layout.
- **Innux Photo Re-Fetch**: If the restored session had an Innux photo, it is automatically re-fetched from the server on mount (blob URLs are not restorable).
- **Local Upload Photo Handling**: Locally uploaded photos are explicitly NOT restored (blob URLs don't survive component unmount); the user simply re-uploads.
### Changed
- **Layout Restore**: The layout loading effect now checks for a previously-selected layout ID from the restored session and selects it instead of always defaulting to the first layout.
- **Reset Integration**: `handleCompanyChange`, `handleToggleManualMode` now clear the persisted session state in addition to clearing component state.

## [v2.83.2] - 2026-04-21 - Fix: HR Employee Search Reliability (Race Condition & State Management)
### Fixed
- **Race Condition in Employee Search**: Added `AbortController` to cancel in-flight search requests when a new search is triggered. A request sequence counter (`searchSeqRef`) ensures only the latest response updates the UI, preventing stale results from overwriting correct ones.
- **Silent Error Swallowing**: Added `res.ok` verification before reading search results. HTTP 502/503 backend errors previously bypassed the `catch` block and silently produced empty results; they now route through proper error handling with user-visible messages.
- **Stale State on Company Change**: Company dropdown (`onChange`) now invokes a dedicated `handleCompanyChange` handler that explicitly clears all search results, selected employee, loaded profile, photo, badge configuration, error state, and print results. Also cancels any in-flight search request to prevent cross-company data leakage.
### Added
- **Diagnostic Logging**: `HRController.SearchEmployees` now logs company, query, and result count at `Information` level for operational traceability.

## [v2.83.1] - 2026-04-20 - Refactor: UI Modernization — Legacy Brutalist → Modern Corporate (Final Pass)
### Changed
- **Full Brutalist Remediation (31 files)**: Systematic elimination of all remaining "Industrial Brutalist" design patterns. Zero occurrences of `var(--shadow-brutal)`, `4px/6px offset shadows`, `translate(-2px,-2px)` hover effects, or `2px/4px solid border-heavy` borders remain in the codebase.
  - **globals.css**: Removed `.btn-primary:active` translate/shadow offset; buttons now use `opacity:0.9` active state.
  - **Shared Components (12)**: `ApprovalModal`, `CorrectPoModal`, `RegisterPoModal`, `RequestLineItemForm`, `RequestAttachments`, `Feedback`, `Tooltip`, `CostCenterAutocomplete`, `DepartmentMasterAutocomplete`, `EmployeeAutocomplete`, `SupplierAutocomplete`, `QuotationEntry` — heavy borders and offset shadows replaced with `var(--shadow-sm/md)` and `1px solid var(--color-border)`.
  - **Layout / Modais (7)**: `UserProfileDrawer`, `UserDropdown`, `QuickSupplierModal`, `HRActionModal`, `ReceivingModal`, `FinanceActionModal`, `PurchasingHelpDrawer` — modal containers and action buttons fully aligned to Modern Corporate tokens.
  - **Páginas (10)**: `RequestCreate`, `RequestGeneralDataSection`, `RequestActionHeader`, `PurchasingLandingPage`, `Purchasing/QuickActions`, `BuyerItemsList`, `SystemLogs`, `FinanceHistory`, `ChangePasswordPage`, `AttentionList` — interactive hover states migrated from `translate(-2px,-2px) + 6px offset` to `translateY(-2px/3px) + var(--shadow-md/lg)`.
- **Token Standards established**: shadows → `var(--shadow-sm/md/lg)` · borders → `1px solid var(--color-border)` · interactive lift → `translateY(-Npx)` · radii → `var(--radius-md/lg)`.
- **Accepted exceptions** (2, consciously retained): `DecisionTimeline` `borderLeft: 4px` (semantic timeline indicator) · `RequestLineItemForm` spinner border-top (CSS loading circle).

## [v2.82.0] - 2026-04-20 - Feature: Payment Deadline Rules — Frontend & Documentation (DEC-117)
### Added
- **"Regras de Pagamento" Section in Contract Create/Edit**: Collapsible form section with progressive disclosure. Hidden by default; auto-opens when editing a contract with an existing rule. Driven by two new lookup endpoints (`/payment-term-types`, `/reference-event-types`).
- **Payment Term Type Selector**: Dropdown loads all supported rule types (`FIXED_DAYS_AFTER_REFERENCE`, `FIXED_DAY_OF_MONTH`, `NEXT_MONTH_FIXED_DAY`, `ON_RECEIPT`, `ADVANCE_PAYMENT`, `MANUAL`, `CUSTOM_TEXT`). Subsequent fields appear conditionally based on the selected type.
- **Reference Event Type Selector**: Appears only for rule types that require a reference date. Drives `InvoiceReceivedDate` visibility in the obligation form via `requiresInvoiceDate` flag.
- **Grace Period & Late Penalty/Interest Fields**: `GracePeriodDays`, `HasLatePenalty`, `LatePenaltyValue`, `LatePenaltyTypeCode`, `HasLateInterest`, `LateInterestValue`, `LateInterestTypeCode` — all wired to the save payload.
- **Manual Override Toggle**: `AllowsManualDueDateOverride` checkbox controlling per-obligation due date override permission.
- **Free-Text Rule Summary & Notes**: `PaymentRuleSummary`, `FinancialNotes`, `PenaltyNotes` — text areas available for any rule type.
- **Due Date Source Badge**: Obligation rows in the detail view now show `🔄 Auto (Contrato)` or `✏️ Manual` badges, color-coded to distinguish automatic calculation from manual override.
- **Obligation Deadline Metadata Panel**: Expandable sub-row under each obligation showing `ReferenceDateUtc`, `CalculatedDueDateUtc`, `GraceDateUtc`, `PenaltyStartDateUtc`. Visible when the contract has a payment rule and the obligation has a source badge.
- **Active Payment Rule Summary Panel**: New panel in ContractDetail "Geral" tab showing the summarized rule, financial notes, and penalty notes when a structured rule is configured.
- **Obligation Context Note**: Duration-aware guidance note in the obligation add/edit form. Prompts the user to supply `InvoiceReceivedDate` (or other required reference date) when the contract rule requires it.
- **Context-Aware Obligation Form Fields**: `InvoiceReceivedDate` field appears in obligation add/edit only when `ReferenceEventTypeCode` = `INVOICE_RECEIVED_DATE` (or similar user-supplied types).
### Documentation
- **CONTRACTS_WORKFLOW.md §11**: Added complete Payment Deadline Rules reference section covering all payment term types, reference event types, calculation logic, grace period formulas, manual override behavior, request generation impact, backward compatibility, and the UI source badge system.
- **DECISIONS.md DEC-117**: Added full decision record for structured payment deadline rules — context, all 9 sub-decisions, 4 alternatives considered, and consequences.

## [v2.81.1] - 2026-04-20 - Fix: Payment Request Generation Units

### Fixed
- **Contract Payment Generation**: Resolved an issue where payment requests generated from contract obligations were inappropriately adopting an inactive "EA" default unit by ensuring the `UnitId` drops to `null`.
- **Request Draft Default Unit Fallback**: Eliminated the legacy hardcoded "EA" UI fallback from `RequestsController` to allow items with undefined units to be properly surfaced without forced defaults.

## [v2.81.0] - 2026-04-19 - Feature: Contracts Management Module (First Vertical Slice)
### Added
- **Contract Domain Model**: Introduced 6 new entities — `Contract` (aggregate root), `ContractType`, `ContractDocument`, `ContractHistory`, `ContractAlert`, and `ContractPaymentObligation` — forming a complete contract lifecycle domain.
- **Contract Number Generation**: Automated sequential contract numbering via `SystemCounter` (`CTR-{year}-{sequence}`), following the same atomic counter pattern established for Request numbers.
- **Scoped Data Access**: `GetScopedContractsQuery` in `BaseController` derives company scope from user plant assignments, supports company-wide contracts (`PlantId = NULL`), and respects plant + department visibility rules.
- **Full REST API**: `ContractsController` with 18 endpoints covering list (filtered, paged, with summary KPIs), detail, create, update, 5 status transitions, obligation CRUD, payment request generation, document upload/download, alerts, and type lookups.
- **Generate Payment Request**: Core business action creating a `Request` (type=PAYMENT) from a `ContractPaymentObligation`, inheriting all organizational context and linking via unidirectional FKs (`Request.ContractId`, `Request.ContractPaymentObligationId`).
- **Contract Lifecycle State Machine**: 6 statuses (`DRAFT → UNDER_REVIEW → ACTIVE → SUSPENDED → TERMINATED → EXPIRED`) with enforced transition rules and full audit history.
- **Frontend Workspace**: New `/contracts` workspace with landing page shell, tabbed navigation, contracts list with summary cards, contract creation form (4 sections), and contract detail page with obligations, documents, history, and alerts tabs.
- **Obligation Management UI**: Inline obligation creation, status badges, and the "Gerar Pedido" action button directly in the obligations table for pending items on active contracts.
- **Sidebar Navigation**: `Contratos` group added with `FileSignature` icon, scoped to `Contracts`, `Finance`, and `System Administrator` roles.
- **Seed Data**: 4 contract types seeded — Service (`SERVICE`), Lease (`LEASE`), Supply (`SUPPLY`), Maintenance (`MAINTENANCE`).
### Changed
- **Proforma Validation UX**: Improved the Payment Request submission flow. When proforma validation fails, the system now automatically expands the attachments section and smooth-scrolls it into view to immediately guide the user's attention.
### Database
- **Migration**: `AddContractsModule` — creates 6 tables (`Contracts`, `ContractTypes`, `ContractDocuments`, `ContractHistories`, `ContractAlerts`, `ContractPaymentObligations`), adds `ContractId` and `ContractPaymentObligationId` nullable FKs to `Requests` table with `RESTRICT` delete behavior.
### Documentation
- **CONTRACTS_WORKFLOW.md**: Full BPM reference document with state machines, obligation lifecycle, payment request generation flow, scope rules, ERD, API endpoint catalog, history event types, and alert types.

## [v2.80.0] - 2026-04-18 - Feature: Finance Budget Tracking MVP (Phase 1)
### Added
- **Annual Budget Domain**: Introduced the `AnnualBudget` entity to manage distinct yearly budgets for departments based on a native currency, preventing duplicate budget definitions via `Year + DepartmentId + CurrencyId` constraints.
- **Budget Setup Interface**: Created `FinanceBudgetConfig.tsx` to enable users with `Finance` or `SystemAdministrator` roles to maintain annual departmental limits seamlessly.
- **Committed Spend Engine**: Configured the new `FinanceBudgetController` to calculate "Committed" vs "Paid" spend continuously in real-time, leveraging active request statuses while actively excluding any cancelled workloads.
- **Executive Overview Tracking**: Integrated an 'Acompanhamento Orçamental' panel into `FinanceOverview.tsx`. This view delivers a macro synthesis across currencies, highlights the top 5 departments at risk of breaching limits, and provides contextual drill-down into cost-center execution.

## [v2.79.0] - 2026-04-18 - Feature: Manual Badge Creation (Visitor Workflow)
### Added
- **Manual Badge Entry**: Added a new "Entrada Manual" toggle in the HR Employee Workspace. When activated, it seamlessly replaces the Primavera API search with a manual data entry form.
- **Visitor Badge Lifecycle**: The system can now issue badges for visitors or temporary staff without requiring them to be pre-registered in the Primavera ERP. These badges are logged into `BadgePrintHistory` utilizing a specialized `MANUAL-[timestamp]` employee code to maintain audit traceability.
- **Resilient Badge Rendering**: Upgraded the `BadgePreview` engine and `BadgeLayoutEditorV3` configurations to robustly handle multi-line text wrapping. Names that exceed container constraints now gracefully wrap to a newly allocated second text box, preventing truncation.
- **Contextual Form Validation**: Added visual validation logic blocking manual badge printing if requisite parameters (Name, Category, missing RFID card) aren't satisfied, ensuring blank or malformed visitor badges are not dispatched.

## [v2.78.0] - 2026-04-17 - Feature: Financial Snapshot & Payment Divergence Detection (Phase 1 — DEC-110)
### Added
- **Financial Snapshot at Approval**: `ApprovedTotalAmount`, `ApprovedCurrencyCode`, and `ApprovedAtUtc` are now captured immutably on the `Request` entity at the moment of final approval. QUOTATION flow sources the winning quotation total; PAYMENT flow sources `EstimatedTotalAmount`.
- **Mandatory Payment Amount Capture**: `ActualPaidAmount` and `ActualPaidAtUtc` are now required inputs when confirming payment via `MarkAsPaid`. The `FinanceActionModal` includes a new "Montante Efetivamente Pago" input field.
- **Payment Divergence Detection**: *(Superseded by v2.93.1 — tolerance removed, now zero-tolerance.)* Originally implemented automated comparison of `ActualPaidAmount` vs `ApprovedTotalAmount` using a 1% relative tolerance (with 1.00 absolute floor). When divergence exceeded tolerance, a `PAYMENT_DIVERGENCE_DETECTED` audit entry was created in `RequestStatusHistory` with detailed variance data.
- **Finance Status Guards**: `SchedulePayment` and `MarkAsPaid` endpoints now enforce explicit source-status validation. Actions from invalid workflow states return HTTP 400 with descriptive error messages listing allowed statuses.
- **Finance List Enrichment**: `FinanceListItemDto` now exposes `ApprovedTotalAmount`, `ActualPaidAmount`, `ApprovedCurrencyCode`, `ApprovedAtUtc`, `ActualPaidAtUtc`, and a computed `HasPaymentDivergence` flag.
### Documentation
- **WORKFLOW_ARCHITECTURE.md**: Added §6 — Financial Lifecycle covering value source by stage, snapshot rules, actor responsibilities, payment validation rules, divergence detection algorithm, and Phase 2 intent.
- **DECISIONS.md**: Added DEC-110 — Financial Snapshot & Payment Divergence Detection phased delivery rationale.

## [v2.77.3] - 2026-04-16 - Performance: Master Data Page Load Optimization (50s → 2s)
### Performance
- **GetUsers Cartesian Explosion Fix**: Replaced 4 eager-loading Include/ThenInclude chains with direct SQL projection in `UsersController.GetUsers`. The cartesian join was taking 25s+ for 10 users; now resolves in <200ms.
- **Frontend Sequential Loading**: Replaced `Promise.allSettled` (9 parallel API calls causing LocalDB connection pool contention at 44s) with sequential loading that completes in <1s.

## [v2.77.2] - 2026-04-16 - Backend: EF Core Decimal Precision Standardization
### Fixed
- **EF Core Decimal Precision**: Added explicit `HasColumnType` precision/scale for 16 decimal properties across 5 entities (`OcrExtractedItem`, `ReconciliationRecord`, `QuotationItem`, `RequestLineItem`, `Request`). Eliminates all model validation warnings at startup and prevents silent financial data truncation. Convention: money `decimal(18,2)`, percentages `decimal(9,4)`, quantities `decimal(18,4)`.
### Documentation
- **DECISIONS.md**: Added DEC-108 establishing mandatory decimal precision as a permanent backend architecture rule.

## [v2.77.0] - 2026-04-15 - Security: Dedicated HR Role & Scope Model
### Added
- **Dedicated HR Role**: Introduced `HR` as a standalone role (`RoleConstants.HR` / `ROLES.HR`) to decouple HR workspace access from the `Local Manager` privilege.
- **Backend Authorization**: `HRController` now enforces `[Authorize(Roles = "System Administrator,HR")]`. All other roles receive HTTP 403.
- **Login Scope Data**: `UserProfileDto` now includes `Plants` and `Departments` fields, populated directly from scope tables during login — eliminates the need for an extra `/api/v1/users/me` call.
- **Frontend Auth Context**: Added `hasHRAccess` derived boolean to `AuthContext`, combining `HR` role membership and `System Administrator` bypass.
- **User Management HR Warning**: When `HR` role is selected during user creation/editing, a contextual warning appears if no plants or departments are assigned, preventing creation of scopeless HR users.
### Changed
- **Navigation Visibility**: R.H. sidebar group and `Cadastro de Funcionários` submenu now require `ROLES.HR` or `ROLES.SYSTEM_ADMINISTRATOR` (previously required `ROLES.LOCAL_MANAGER`).
- **Route Protection**: `/hr/employees` route guard updated from `LOCAL_MANAGER` to `HR` role.
- **Manager Role Assignment**: Local Managers can now assign the `HR` role to users within their organizational scope.
### Breaking
- **`Local Manager` users no longer have implicit HR access.** Existing Local Managers who need continued access to the Employee Workspace must be explicitly assigned the `HR` role.
### Documentation
- **DECISIONS.md**: Added DEC-107 — Dedicated HR role architecture with explicit future-evolution constraints.
- **ACCESS_MODEL.md**: Updated to reflect HR role, scope model, and breaking change from Local Manager decoupling.

## [v2.76.1] - 2026-04-15 - Search Functionality Expansion
### Added
- **Global Search Scope**: Expanded the main Requests Dashboard search capabilities. The system now seamlessly searches by Requester Name (`Solicitante`) in addition to Request Number and Title.
- **Contextual Help Search Glossary**: Updated the contextual help (the "i" icon) in the "Explorador de Pedidos" modal to precisely reflect all actively indexable search parameters.

## [v2.76.0] - 2026-04-15 - UI: Buyer Portal Header Modernization
### Added
- **Buyer Portal Header Modernization**: Refactored the request card header into a clean 3-zone architecture (ID/Status, People/Approvers, Date/Actions) with improved visual hierarchy and scannability.
- **Action Kebab Menu (⋮)**: Replaced legacy "Cancelar" and "Detalhes" buttons with a unified, motion-animated dropdown menu to increase workspace density and reduce visual noise.
- **Teams Status Presence**: Integrated discrete Teams chat triggers next to requester, buyer, and approver names for immediate operational communication.
### Changed
- **Multi-Stage Approver Logic**: Updated the Area Approver visibility logic. The "Aprovador da Área" now remains visible throughout all initial stages (including Aguardando Cotação) until the request reaches "Aguardando Aprovação Final", providing consistent departmental context.
- **Card Layout Resilience**: Removed overflow constraints on quotation request cards to accommodate floating dropdown menus without clipping regressions.
- **Dark Mode Hospitality**: Standardized the new kebab menu components with semantic CSS variables (`var(--color-bg-surface)` and `var(--color-bg-neutral)`) for seamless theme switching.

## [v2.75.0] - 2026-04-15 - UX Feedback: Gestão de Cotações
### Added
- **Context-Aware Empty States**: Upgraded the "Gestão de Cotações" empty state to structurally depend on the active filter context. When the user has an active text search or status filter that yields zero results, contextual actionable buttons ("Limpar Busca" / "Limpar Filtros") are displayed directly in the empty state.
- **Structural Loading Skeletons**: Replaced the static "Carregando..." text with a custom `RequestGroupSkeleton` component utilizing pulsing CSS animations (mimicking the exact height and column metrics of the collapsed quotation groups) to eliminate layout shift post-data-fetch.
- **Localized Error Recovery**: Implemented localized boundaries for data fetching errors inside the Buyer Workspace. Failed fetches gracefully exit into an encapsulated "Falha ao Carregar" state containing an explicit and isolated "Tentar Novamente" recovery button mapped directly to the `loadData()` handler, preserving the shell structure visually.
- **Form Interactivity Preservation**: Extended all primary backend-bound mutation actions (Save Quotations, Re-assignments) to implicitly clear nested frontend list error contexts upon success, enhancing user resilience logic.

## [v2.74.0] - 2026-04-15 - Feature: Catalog Linkage in Manual Quotation Entry
### Added
- **Catalog Linkage in Manual Quotation Entry**: Integrated `CatalogItemAutocomplete` into the manual quotation entry mode within `BuyerItemsList.tsx`. This allows buyers to link manual entries directly to official Portal catalog items, ensuring data consistency for inventory and receiving.
- **Backend Catalog Traceability**: Updated `SavedQuotationItemDto` and `RequestsController` projections to persist and retrieve `ItemCatalogId` and `ItemCatalogCode` for quotation line items.

## [v2.73.0] - 2026-04-15 - Fix: Status Filter Pagination Architecture
### Fixed
- **Root cause**: Status is computed *after* matching Primavera items against the Portal catalog — it does not exist in the Primavera source system. The previous implementation paginated at the Primavera SQL level (`OFFSET/FETCH NEXT`), then applied the status filter post-match on each page. This caused pages to contain random numbers of matching items (e.g., page 2 = 0 items, page 4 = 13 items when filtering by "New").
- **Architecture change**: When `statusFilter` is active, the backend now fetches ALL Primavera records via `ListAllArticlesAsync` / `ListAllSuppliersAsync` (batched in pages of 200), performs matching on the full dataset, filters by status, then paginates the **filtered result** in-memory. This guarantees every page contains exactly `pageSize` items (except the last page).
- **Pagination accuracy**: `TotalPrimaveraRecords` now reflects the **filtered count** when a status filter is active, ensuring the frontend pagination ("Page X of Y") is correct for the filtered dataset.
- **Summary counts**: `NewCount`, `ExistsCount`, `ConflictCount` are now computed from the full matched dataset (not just the current Primavera page), providing accurate global counts regardless of which page is displayed.
- **Performance path preserved**: Without `statusFilter`, the original efficient per-page Primavera SQL pagination is still used — no behavioral change for unfiltered browsing.
### Added
- `ListAllArticlesAsync` on `IPrimaveraArticleService` / `PrimaveraArticleService` — sequential batch fetch (200 items/batch) for full dataset access.
- `ListAllSuppliersAsync` on `IPrimaveraSupplierService` / `PrimaveraSupplierService` — same pattern for suppliers.
### Changed (Frontend — v2.72.1)
- Status header filter drives `statusFilter` state (server query param) instead of client-side `columnFilters`.
- Header ↔ top dropdown are bidirectionally synced.
- "Limpar tudo" resets both client-side column filters and server-side status filter.

## [v2.72.0] - 2026-04-15 - Sync Workspace: Excel-Like Column Headers
### Added
- **Column Sorting**: Click any sortable column header to toggle ascending → descending → clear. Visual arrow indicators (▲/▼) show active sort direction.
- **Column Filtering**: Filter icon on each header opens a compact popover:
  - **Text columns** (Código, Descrição, Família, Unidade, Nome, NIF): case-insensitive, accent-insensitive "contains" search.
  - **Status column**: multi-select checkbox filter with localized labels (Novo, Existente, Conflito).
- **Reusable `SortableFilterHeader` component** (`components/shared/SortableFilterHeader.tsx`): shared by both Catalog and Supplier sync tables.
- **Page-scope banner**: Visible info banner when column filters/sort are active, explicitly stating: "Ordenação e filtros de coluna aplicados apenas aos itens visíveis nesta página." Shows active filter count, sort direction, and items visible vs total.
- **Global reset**: "Limpar tudo" button clears all column filters and sorting in one action.
- **Filter persistence**: Column filters and sort state persist across page changes and are reapplied to each newly loaded page.
- **Empty state**: Friendly message when column filters exclude all items on the current page.
### Technical Notes
- Client-side only: sorting and column filtering operate on the current page (up to 50 items) returned from the server. This is consistent with the existing post-match status filter behavior and does not mislead the user about cross-page effects.
- Stacking: top-level server controls (company, search, status) narrow the Primavera result; column header filters further refine the visible page; sort applies last.
- Columns supported — Catalog: Status, Código Primavera, Descrição (Primavera), Família, Unidade, Código Portal, Descrição (Portal). Supplier: Status, Código Primavera, Nome (Primavera), NIF (Primavera), Nome (Portal), NIF (Portal).

## [v2.71.0] - 2026-04-15 - Catalog Sync: Description-Based Matching (V2.1)
### Changed
- **Catalog Sync Matching Strategy (V2.1)**: Replaced PrimaveraCode-based matching with description-based comparison for the Item Catalog sync preview, with duplicate detection on both sides.
  - `Exists` → exact normalized description match between Primavera and Portal
  - `Conflict` → similar description (one contains the other, min 5 chars) — requires manual review
  - `Conflict` → duplicate description detected in Primavera result set (source ambiguity)
  - `Conflict` → duplicate description detected in Portal catalog (target ambiguity)
  - `New` → no relevant description match **and** no ambiguity on either side
- **Normalization Rules**: Descriptions are normalized before comparison: trim, uppercase, strip accents/diacritics, remove simple punctuation (`.` `,` `;` `:` `/` `-`), collapse repeated spaces.
- **Import Dedup Check**: Now uses normalized description matching instead of PrimaveraCode to prevent importing items that already exist in the Portal under a different code.
- **CatalogTable UI**: Added "Detalhe" column to display `conflictDetail` for Conflict-status catalog items.

### Fixed
- **Catalog Import 500 Error**: Added missing EF Core migration `AddItemCatalogSyncTraceabilityFields` for `SourceCompany` and `LastSyncedAtUtc` columns on `ItemCatalogItems` (and `Suppliers`). The entity model had these fields but the database schema did not, causing `SqlException: Invalid column name` on every import attempt.

## [v2.70.0] - 2026-04-15 - Authorization Hardening: Centralized Role Constants
### Fixed
- **SyncController 403 Regression**: Corrected `[Authorize(Roles = "Admin")]` to `[Authorize(Roles = RoleConstants.SystemAdministrator)]`. The `Admin` role does not exist in the system; the correct administrative role key is `System Administrator`.

### Changed
- **Backend Role Constant Enforcement**: Replaced all 7 hardcoded `"System Administrator"` string literals in `NotificationService.cs` with `RoleConstants.SystemAdministrator`.
- **Frontend Role Constant Enforcement**: Replaced all hardcoded role string literals with `ROLES.*` constants across:
  - `AuthContext.tsx` (central `isAdmin` and `isLocalManager` checks)
  - `ReceivingWorkspace.tsx` (scope filtering)
  - `QuickActions.tsx` (Purchasing — role-based action visibility)
  - `QuickActions.tsx` (Dashboard — role-based action visibility)
  - `UserManagement.tsx` (admin/manager role checks and role filtering)

### Documentation
- **ACCESS_MODEL.md**: Updated role label from "Admin" to "System Administrator" with explicit authorization key clarification warning.
- **DECISIONS.md**: Added DEC-105 — mandatory use of centralized role constants for all authorization checks.

## [v2.69.0] - 2026-04-14 - Phase 5A Primavera Request Validation Against Master Data (Read-Only)
### Added
- **PrimaveraRequestValidationDtos**: Input (`PrimaveraRequestValidationInputDto`) and result (`PrimaveraRequestValidationResultDto`) DTOs for structured validation:
  - Input: `Company`, `ArticleCode`, `SupplierCode` (optional)
  - Result: `ArticleExists`, `SupplierExists`, `RelationshipChecked`, `RelationshipExists`, `ValidationStatus`, `Messages[]`, enriched descriptions
- **IPrimaveraRequestValidationService / PrimaveraRequestValidationService**: Composition/validation layer above existing Primavera services:
  - Reuses `IPrimaveraArticleService` (article existence)
  - Reuses `IPrimaveraSupplierService` (supplier existence)
  - Reuses `IPrimaveraArticleSupplierService` (relationship existence)
  - No direct SQL — pure service composition
- **PrimaveraRequestValidationController**: Admin-internal API:
  - `POST /api/admin/integrations/primavera/request-validation/validate-line`
  - `POST /api/admin/integrations/primavera/request-validation/validate-batch` (max 50 lines)

### Validation Status Model
- **VALID**: article + supplier exist, relationship confirmed
- **WARNING**: article exists but no supplier provided (partial), or both exist but no relationship
- **INVALID**: article not found, or supplier provided but not found, or missing required inputs
- **ERROR**: technical failure (SQL timeout, provider misconfigured) — distinct from business INVALID

### Architecture Notes
- **Pure composition**: No new SQL queries — all validation done by calling existing read services
- **HTTP semantics**: Business validation results are always 200 (validation op succeeded); ValidationStatus field carries the business outcome
- **Batch support**: Up to 50 lines validated independently per call

### Decisions Recorded
- **DEC-117**: Validation is 200-based — business INVALID is in the response body, not 4xx HTTP status
- **DEC-118**: No-supplier = WARNING (partial validation), not INVALID
- **DEC-119**: No-relationship = WARNING (unlinked pair), not INVALID — allows new vendor relationships

## [v2.68.0] - 2026-04-14 - Phase 4C Primavera Article-Supplier Contextual Linkage (Read-Only)
### Added
- **PrimaveraArticleSupplierDtos**: Directional response DTOs for article-supplier relationships:
  - `PrimaveraArticleSuppliersDto` — wraps article identity with linked suppliers enriched from `Fornecedores`
  - `PrimaveraSupplierArticlesDto` — wraps supplier identity with linked articles enriched from `Artigo`
  - `ArticleSupplierItemDto` / `SupplierArticleItemDto` — enriched relationship items with identity + relationship fields
  - `PrimaveraArticleSupplierLinkDto` — raw relationship row DTO (8 fields from 25-column table)
- **IPrimaveraArticleSupplierService / PrimaveraArticleSupplierService**: Read-only, company-aware article-supplier relationship service. Two-step approach: verifies parent entity exists, then queries enriched relationships with LEFT JOIN to identity tables. Bounded to 100 results.
- **PrimaveraArticleSupplierController**: Admin-internal API:
  - `GET /api/admin/integrations/primavera/articles/{code}/suppliers?company=...`
  - `GET /api/admin/integrations/primavera/suppliers/{code}/articles?company=...`

### Architecture Notes
- **Source object**: `ArtigoFornecedor` table (25 columns; consistent across both companies)
- **Join keys**: `ArtigoFornecedor.Artigo → Artigo.Artigo`, `ArtigoFornecedor.Fornecedor → Fornecedores.Fornecedor`
- **Schema consistency**: Both ALPLAPLASTICO (319 rows) and ALPLASOPRO (256 rows) have identical column sets including `CDU_CodBarrasEntidade`
- **Scope**: Read-only, relationship visibility only. No pricing/ranking/sourcing logic.

### Decisions Recorded
- **DEC-114**: `ArtigoFornecedor` is the authoritative article-supplier relationship table
- **DEC-115**: Pricing fields (PrCustoUltimo, DescFor, PrecoUltEncomenda) excluded from first DTO — relationship visibility only
- **DEC-116**: Supplier enrichment via LEFT JOIN to Fornecedores (Nome, NumContrib); article enrichment via LEFT JOIN to Artigo (Descricao, UnidadeBase)

## [v2.67.0] - 2026-04-14 - Phase 4B Primavera Supplier Master Data Lookup (Read-Only)
### Added
- **PrimaveraSupplierDto**: ~15-field DTO for supplier master data. Source: Primavera `Fornecedores` table (116 columns; ~15 exposed). Fields: `Code`, `Name`, `FiscalName`, `TaxId`, `Email`, `Phone`, `Fax`, `Address`, `Address2`, `City`, `PostalCode`, `Country`, `SupplierType`, `IsCancelled`, `Currency`, `CreatedAt`, `SourceCompany`, `Source`.
- **IPrimaveraSupplierService / PrimaveraSupplierService**: Read-only, company-aware supplier lookup and search. Uses existing `PrimaveraConnectionFactory`. Search covers code, name, fiscal name, and tax ID (NIF).
- **PrimaveraSuppliersController**: Admin-internal API at `api/admin/integrations/primavera/suppliers`. Endpoints: `GET /{code}?company=...` (lookup) and `GET /search?company=...&q=...&limit=...` (search). Error semantics aligned with existing employee/article endpoints.

### Architecture Notes
- **Source object**: Primavera `Fornecedores` table (116 columns; consistent across both companies)
- **No CDU mismatch**: Unlike `Artigo`, both ALPLAPLASTICO and ALPLASOPRO have the same CDU columns for `Fornecedores` — no adaptive detection needed.
- **Search**: Bounded (max 50), min 2 chars, searches across `Fornecedor`, `Nome`, `NomeFiscal`, and `NumContrib`. Stable ordering by `Fornecedor` code.
- **Scope**: Read-only, multi-database aware. No financial/banking/credit/transactional fields.

### Decisions Recorded
- **DEC-111**: `Fornecedores` is the authoritative supplier source table. No joins needed for first version.
- **DEC-112**: Financial fields (CondPag, ModoPag, TotalDeb, LimiteCred, bank details) intentionally excluded — not relevant for initial lookup.
- **DEC-113**: Supplier-article relationship logic deferred to future phase.

## [v2.66.0] - 2026-04-14 - Phase 4A Primavera Article/Material Lookup (Read-Only)
### Added
- **PrimaveraArticleDto**: Focused ~13-field DTO for article/material master data. Source: Primavera `Artigo` table with `LEFT JOIN Familias`. Fields: `Code`, `Description`, `BaseUnit`, `PurchaseUnit`, `Family`, `FamilyDescription`, `SubFamily`, `ArticleType`, `Brand`, `IsCancelled`, `SupplierCode`, `InternalCode`, `SourceCompany`, `Source`.
- **IPrimaveraArticleService / PrimaveraArticleService**: Read-only, company-aware article lookup and search. Uses existing `PrimaveraConnectionFactory`. Adaptive CDU column detection — gracefully omits `CDU_codforneced` / `CDU_codinterno` when absent in target database (e.g., ALPLASOPRO).
- **PrimaveraArticlesController**: Admin-internal API at `api/admin/integrations/primavera/articles`. Endpoints: `GET /{code}?company=...` (lookup) and `GET /search?company=...&q=...&limit=...` (search). Error semantics aligned with existing Primavera employee endpoints.

### Architecture Notes
- **Source object**: Primavera `Artigo` table (154 columns; only ~13 exposed in DTO)
- **Enrichment join**: `LEFT JOIN Familias` for `FamilyDescription`
- **SubFamilias join**: Intentionally deferred
- **Remarks (Observacoes)**: Intentionally deferred from first DTO version
- **CDU fields**: `CDU_codforneced` (SupplierCode) and `CDU_codinterno` (InternalCode) are environment-specific custom fields. Present in ALPLAPLASTICO, absent in ALPLASOPRO. Detected at runtime via `INFORMATION_SCHEMA.COLUMNS`.
- **Search**: Bounded (max 50), minimum 2 chars, parameterized SQL, stable ordering by `Artigo` code
- **Scope**: Read-only, multi-database aware, no stock/pricing/sync/writeback

### Decisions Recorded
- **DEC-108**: CDU columns handled via runtime detection, not hardcoded per-company. Future-proof for schema changes.
- **DEC-109**: Remarks (Observacoes) deferred from first DTO — large ntext field, not essential for initial article lookup.
- **DEC-110**: SubFamilias enrichment deferred to keep first version simple and reduce composite-key assumptions.

## [v2.65.0] - 2026-04-14 - Phase 3B Unified Employee Profile (Read-Only)
### Added
- **UnifiedEmployeeProfileDto**: Cross-system profile composition with separated source sections (`PrimaveraProfileSection`, `InnuxProfileSection`). Match diagnostics at top level: `HasInnuxMatch`, `InnuxMatchStrategy`, `InnuxLookupStatus`, `InnuxLookupMessage`.
- **IUnifiedEmployeeProfileService / UnifiedEmployeeProfileService**: Read-only composition layer above both domain services. Primavera lookup first, then Innux enrichment by employee code. No direct SQL access.
- **UnifiedEmployeesController**: Admin-internal endpoint at `api/admin/integrations/employees/{code}?company=...`. Returns unified profile with both source sections.

### Architecture Notes
- **Match key**: `Primavera.Codigo` ↔ `Innux.Numero` (deterministic, code-based)
- **Source hierarchy**: Primavera is master; Innux is optional operational complement
- **Graceful degradation**: Innux failure does NOT fail the unified profile — Primavera profile is always returned if Primavera succeeds. Innux status is explicit via `InnuxLookupStatus` (`MATCHED`, `NOT_FOUND`, `ERROR`).
- **No flattening**: Source sections remain separated for clear provenance
- **Scope**: Lookup by code only. Unified search intentionally deferred.

### Decisions Recorded
- **DEC-106**: Innux failure degrades gracefully — Primavera profile returned with `InnuxLookupStatus = "ERROR"`, not HTTP error.
- **DEC-107**: Unified search deferred from Phase 3B to reduce composition complexity. Lookup by code is the first stable cross-system operation.

## [v2.64.0] - 2026-04-14 - Phase 3A Innux Employee Lookup (Read-Only)
### Added
- **InnuxConnectionFactory**: Shared, domain-neutral connection factory for Innux SQL connections. Single database (no company routing). Reusable by future Innux domain services (attendance, terminals, etc.).
- **IInnuxEmployeeService / InnuxEmployeeService**: Read-only Innux employee operational identity lookup. Queries `dbo.Funcionarios` with `LEFT JOIN dbo.Departamentos` for department enrichment. Supports lookup by employee number and search by name (bounded, max 50 results).
- **InnuxEmployeeDto**: Operational identity DTO with 17 mapped fields. Key naming: `IsActiveOperational` (explicitly Innux operational state, not unified cross-system status), `HasPhoto` (boolean presence indicator, never exposes blob data), `Source` (always `"INNUX"`).
- **InnuxEmployeesController**: Admin-internal API at `api/admin/integrations/innux/employees`. Error semantics aligned with Primavera controller (400/404/502/503/500).

### Changed
- **InnuxIntegrationProvider**: Refactored to delegate connection-string resolution to `InnuxConnectionFactory`, removing duplicated `BuildConnectionString()`. Single source of truth for Innux connection configuration.
- **DI Registration**: Added `InnuxConnectionFactory` and `IInnuxEmployeeService` registrations in `Program.cs`.

### Architecture Notes
- Innux has a single database target — no company parameter needed (unlike Primavera multi-database).
- Department JOIN key: `f.IDDepartamento = d.IDDepartamento` (runtime-validated assumption from schema discovery).
- Phase intentionally deferred: attendance events, terminal reads, biometric reconciliation, Primavera merge logic.

## [v2.63.0] - 2026-04-14 - Phase 2D Primavera Multi-Database Support
### Added
- **PrimaveraConnectionFactory**: Shared, domain-neutral connection factory that resolves SQL connections for any configured Primavera company/database target. Reusable by all future Primavera domain services (employees, materials, suppliers, departments, cost centers).
- **PrimaveraCompany enum**: Strongly typed selector for Primavera business databases (`ALPLAPLASTICO`, `ALPLASOPRO`). Stable internal codes — display labels resolved separately.
- **Multi-company configuration**: `Integrations:Primavera:Companies` section in appsettings supporting per-company `DatabaseName` and `Enabled` flags. Shared connection settings (Server, Instance, Auth, Timeout) remain at the Primavera level.
- **SourceCompany field**: `PrimaveraEmployeeDto.SourceCompany` identifies which Primavera database each record was read from.

### Changed
- **PrimaveraEmployeeService**: Refactored to accept `PrimaveraCompany` as explicit target. Delegates all connection resolution to `PrimaveraConnectionFactory`. Removed private `BuildConnectionString()`.
- **PrimaveraIntegrationProvider**: Refactored to use `PrimaveraConnectionFactory`. Health check uses Option A strategy: tests the first configured company (default target). Diagnostic logs include health target and configured companies list.
- **PrimaveraEmployeesController**: Employee lookup/search endpoints now require `company` query parameter (`?company=ALPLAPLASTICO`). Returns 400 for missing/invalid company, 503 for configuration errors, 502 for SQL failures.
- **DI Registration**: Added `PrimaveraConnectionFactory` registration in `Program.cs`.

### Decisions Recorded
- **DEC-103**: One Primavera provider, multiple business database targets. Database selection handled in domain services, not by duplicating providers.
- **DEC-104**: Option A health strategy — provider tests first configured company only. DEGRADED status deferred to future phase.
- **DEC-105**: Company selection via query parameter (`?company=<CODE>`). Route-segment style deferred.

## [v2.62.0] - 2026-04-14 - Phase 2B Innux Connection Health
### Added
- **Innux Integration**: Implemented `InnuxIntegrationProvider` mapping to `TIME_ATTENDANCE`, extending the generic integration framework to its second established real provider.
- **Provider Integrity Check**: Hooked runtime diagnostic test routines (`@@SERVERNAME`, `DB_NAME()`) to the Innux instance ensuring isolated SQL validations decoupled from existing biometric metadata pipelines. Enforced strict explicit credential validation reflecting real-world error constraints in the diagnostics UI.

## [v2.61.0] - 2026-04-14 - Phase 2A Primavera Employee Master Data
### Added
- **Primavera Integration**: Added read-only `PrimaveraEmployeeService` exposing the `IPrimaveraEmployeeService` contract to interact cleanly with `dbo.Funcionarios` joined with `dbo.Departamentos`.
- **Admin Endpoints**: Introduced `PrimaveraEmployeesController` (`/api/admin/integrations/primavera/employees`) to execute strict parameterized query matching.

### Changed
- Standardized cross-boundary response formats. DTO layers now map exact data states, allowing calling methods to interpret `TerminationDate` explicitly instead of risking implicit `IsActive` heuristics.

## [2.60.0] - 2026-04-14

### Changed
- **Primavera Integration Provider (Phase 1B)**: Stabilized runtime diagnostic connectivity to real Primavera infrastructure.
  - Replaced `Encrypt=false` (default) with `Encrypt=Optional` during SNI connection negotiation to gracefully downgrade on legacy SQL nodes without triggering 21-second timeouts.
  - Removed `ApplicationIntent.ReadOnly` flag as standalone environments drop the connection if no readable secondary is explicitly configured via routing.
  - Confirmed and applied **explicit SQL Authentication** as the canonical path. Desktop/session UI identity proxying is disallowed to prevent pipeline drops.
  - Provider connection response time verified at ~35ms.

### Decisions Recorded
- **DEC-102**: Explicit configured credentials required for real integrations; desktop session proxying disabled.

## [2.59.0] - 2026-04-14

### Added
- **Primavera Connection Health (Phase 1A)**: First concrete `IIntegrationProvider` implementation on the generic integration platform.
  - `PrimaveraIntegrationProvider`: validates SQL connectivity to Primavera ERP SQL Server. Uses diagnostic query (`SELECT @@SERVERNAME, DB_NAME()`) for identity confirmation. Read-only, no business-domain queries.
  - Supports both **SQL Server Authentication** and **Windows Authentication** modes — no default assumed.
  - Connection string built with `ApplicationIntent.ReadOnly` to enforce read-only at the transport level.
  - Integration logs emitted for connection test lifecycle (`STARTED`, `OK`, `FAILED`) via existing `IntegrationLogEventTypes`.
  - DI registration in `Program.cs`: `IIntegrationProvider → PrimaveraIntegrationProvider`.

### Changed
- **Primavera seed data**: Transitioned from `IsPlanned = true` (roadmap) to `IsPlanned = false` (real implementation exists). `IsEnabled` remains `false` — activation depends on explicit environment configuration, not provider existence.
- **Primavera connection status seed**: Updated from `PLANNED` to `NOT_CONFIGURED`.
- **Integration Playbook**: Added Phase 1A reference section, updated step 5 guidance (activation requires configuration), added activation lifecycle table.
- **appsettings.json**: Added `Username` and `Password` fields to Primavera config template for SQL auth mode support.

### Decisions Recorded
- **DEC-101**: Primavera provider activation lifecycle — implementation ≠ activation.

## [2.58.0] - 2026-04-14

### Added
- **Generic Integration Foundation (Phase 0)**: Implemented a provider-agnostic integration platform foundation supporting multiple external systems and business domains.
  - **Domain Entities**: `IntegrationProvider` (registry), `IntegrationConnectionStatus` (runtime health), `IntegrationProviderSettings` (connection config). All entities are generic — no coupling to specific providers or business domains.
  - **Application Layer**: `IIntegrationProvider` (minimal base contract: identity + connection test), `IIntegrationHealthService` (provider-agnostic aggregation), standardized DTOs (`IntegrationHealthSummaryDto`, `IntegrationProviderStatusDto`, `IntegrationConnectionTestResultDto`).
  - **Constants**: `IntegrationStatusCodes` (stable machine-readable: `HEALTHY`, `UNHEALTHY`, `UNREACHABLE`, `NOT_CONFIGURED`, `PLANNED`), `IntegrationLogEventTypes` (standardized `AdminLogWriter` event types).
  - **Infrastructure**: `IntegrationHealthService` — resolves providers from DI, aggregates status, executes connection tests, persists results, and logs events.
  - **API**: `IntegrationHealthController` — separate from `AdminDiagnosticsController`. `GET /api/admin/integrations/health` (summary), `POST /api/admin/integrations/{code}/test-connection` (manual test).
  - **Database**: EF Core migration `AddIntegrationFoundation` creating 3 tables with seed data for `PRIMAVERA` (ERP/SQL) and `INNUX` (Biometric/SQL) — both planned/disabled.
  - **Frontend**: Fully data-driven `IntegrationHealth.tsx` — renders provider cards dynamically from API. OCR explicitly separated as "internal service" from external providers. Test Connection button guarded (disabled for planned/unconfigured/unimplemented providers).
  - **Configuration**: `Integrations` section in `appsettings.json` with Primavera + Innux connection templates (disabled by default).
  - **Documentation**: Created `docs/INTEGRATION_PLAYBOOK.md` — comprehensive step-by-step guide for adding new providers.

### Decisions Recorded
- **DEC-100**: Generic integration foundation architecture — provider-agnostic, capabilities-as-metadata, strict settings separation.

## [2.57.0] - 2026-04-14

### Changed
- **RequestEdit Component Decomposition (DEC-096)**: Decomposed the monolithic `RequestEdit.tsx` (~1,274 lines) into a parent-child architecture (~660 lines in parent + 4 presentational children).
  - **Extracted Components**: `RequestGeneralDataSection`, `RequestFinancialSummary`, `RequestStatusActionPanels`, `RequestLineItemsSection` in `src/frontend/src/pages/Requests/components/`.
  - **Architecture**: Parent retains all state, handlers, permissions, and workflow logic. Children are strictly presentational, receiving data and callbacks via props.
- **CSS Module Migration**: Replaced five shared inline style helpers (`inputStyle`, `sectionTitleStyle`, `labelStyle`, `getInputStyle`, `renderFieldError`) with semantic CSS classes in `request-edit.module.css`.
- **Route-Level Code Splitting (DEC-099)**: Implemented `React.lazy()` + `Suspense` for ~20 page components in `App.tsx`. Eagerly loaded: `LoginPage`, `ResetPasswordPage`, `ChangePasswordPage`, `Dashboard`. Core JS bundle reduced from ~1,509 kB to ~446 kB (~70%).
- **LoadingSkeleton Fallback**: Introduced `LoadingSkeleton` component (`src/frontend/src/components/ui/LoadingSkeleton.tsx`) as the layout-aware fallback for lazy-loaded routes.
- **Dead Import Cleanup**: Removed 13 dead imports (10 Lucide icons, 3 components) from `RequestEdit.tsx` post-extraction.

### Decisions Recorded
- **DEC-096**: Incremental decomposition of `RequestEdit.tsx` with parent-orchestrator pattern.
- **DEC-097**: Explicit skip of generic `FormField` abstraction due to high field-type variation.
- **DEC-098**: Deferred accessibility/focus and motion-polish work to a future dedicated cycle.
- **DEC-099**: Route-level code splitting strategy with eager/lazy classification.

## [2.56.0] - 2026-04-13

### Added
- **PO Correction Forward Exit**: Completed the existing `WAITING_PO_CORRECTION` operational loop, which was previously a dead-end status.
  - **Backend**: `RegisterPo` endpoint now accepts `WAITING_PO_CORRECTION` as a valid source status, enabling the Buyer to re-register a corrected PO after Finance return.
  - **Conditional Action Codes**: Uses `REGISTER_PO` for initial registration (from `APPROVED`) and `REREGISTER_PO` for correction flow (from `WAITING_PO_CORRECTION`), preserving distinct audit history.
  - **Source-Status Guard (Finance Return)**: `ReturnForAdjustment` now validates that the request is in `PO_ISSUED` or `PAYMENT_SCHEDULED` before allowing a return. Returns from `PAYMENT_COMPLETED` or `WAITING_PO_CORRECTION` are blocked.
  - **Notification**: New `PO_CORRECTION_COMPLETED` event code notifies plant-scoped Finance users when a Buyer completes the correction.
  - **Frontend**: Added `CORRIGIR P.O` button (orange, visually distinct from `REGISTRAR P.O`) to the operational panel for `WAITING_PO_CORRECTION` status. Integrated `CorrectPoModal` in `RequestEdit.tsx`.
  - **Guidance**: Added `WAITING_PO_CORRECTION` to `getRequestGuidance()` — Responsible: Comprador, Action: Corrigir P.O devolvida por Finanças.
  - **Line Item Sync**: `WAITING_PO_CORRECTION` syncs items to `WAITING_ORDER` (idempotent — items are already in this state from prior `PO_ISSUED`).
  - **Business Decision**: Returning from `PAYMENT_SCHEDULED` intentionally invalidates the prior scheduling. After correction, Finance must re-evaluate from `PO_ISSUED`.

### Changed
- **WORKFLOW_ARCHITECTURE.md**: Added Section 6 (Finance Return / PO Correction Loop) and updated state machine tables, permission matrix, and attachment deletion rules.

## [2.55.0] - 2026-04-13

### Added
- **Financial Integrity Gate**: Implemented a server-side financial checkpoint at the quotation completion stage (`CompleteQuotation`).
  - Persists the OCR-extracted grand total (`OcrOriginalGrandTotal`) on the `Request` entity during OCR extraction as the integrity baseline.
  - Validates the completing quotation total against the OCR baseline using centralized tolerance (`max(1.0, 0.1% of original)`, configurable in `RequestConstants.FinancialIntegrity`).
  - Blocks progression (`409 Conflict`) with structured variance data when mismatch exceeds tolerance or unresolved reconciliation records exist.
  - Supports explicit buyer override with mandatory written justification.
  - Full audit trail: logs detection (`FINANCIAL_INTEGRITY_BLOCKED`), override acceptance (`FINANCIAL_INTEGRITY_OVERRIDE`) in `RequestStatusHistory` and `AdminLog`.
  - Frontend: Added Financial Integrity Modal in Quotation Management workspace (`BuyerItemsList.tsx`) with OCR vs Quotation comparison table, variance display, and override justification flow.
  - RequestEdit path surfaces integrity failures as modal feedback, directing buyers to the Quotation Management workspace for the override flow.

## [2.54.0] - 2026-04-13

### Added
- **Enriched Area Approver Notifications**: The `WorkflowNotificationOrchestrator` now intercepts payment events (`SCHEDULED`/`PAID`) and overrides the recipient footprint. Area Approvers now receive real-time financial transparency emails detailing the specific request amount, total monthly departmental spend, and percentage impact of the request on the department's monthly activity.
- **Approval Decision Guide**: Deployed a "Manual de Aprovação" helper to the `ApprovalDetailPanel.tsx` component, introducing an interactive, styled HTML modal containing step-by-step checklists, financial definition clarity, and guidance on required cost center bulk allocations.
- **Quotation Management UX**: Replaced the previous standalone `Link` page routing in `BuyerItemsList` with a frictionless, localized Quick View `RequestDrawerPresentation`, eliminating context loss during quote inspection.

## [2.53.0] - 2026-04-13

### Added
- **Workflow Notification Role-Casting**: Expanded the Orchestrator to dispatch role-specific email subjects, headlines, and contextual comments based cleanly on the actor's jurisdiction (Requester, Next Approver, or Buyer).
- **Self-Notification Lift**: Removed the `BypassSelfNotifyRule` suppression wall, allowing users to universally retain an email trail of requests they submitted onto their own governed departments. 
- **Admin System Logs Enhancement**: The `UsersController` backend now natively integrates `_adminLogWriter`, projecting explicit diagnostic telemetry trails on HTTP 400 violations caused by duplicate user/email registrations.

### Fixed
- **In-App Duplicate Handlers**: Removed obsolete `window.alert()` from identical user collisions inside `UserManagement.tsx`. Errors are now parsed and channeled into an organic top-bound red banner mapped with React local state.

## [2.52.0] - 2026-04-12

### Added
- **Server-Side Catalog Search & Pagination**: Improved performance of catalog items lookup by pushing load to the backend.
  - Re-engineered `CatalogItemsPanel.tsx` to utilize server-side search instead of client-side filtering.
  - Updated the backend `CatalogItemsController.cs` to accept optional `search` and `take` query parameters.
- **Autocomplete Optimization**: The item selection "pickup list" inside Request creation now correctly queries the server and strictly bounds outputs.
  - Limits returned UI outputs natively to a maximum of 10 items.
  - Added inline visibility of `Cod_Primavera` and `Cod_Fornecedor` inside the item autocomplete dropdown list options.

## [2.51.0] - 2026-04-11

### Added
- **Submission Confirmation Email**: Implemented an automated confirmation email sent directly to the requester immediately after submitting or resubmitting a request. The email includes a breakdown of line items and the estimated total value.
- **Approval Flow UX Redirection**: Transformed the standalone request view by replacing disconnected action buttons with a unified 'Pending Approval' banner.
- **Visual Attention Triggers**: Integrated front-end routing (`react-router-dom`) between `RequestEdit` and `ApprovalCenter`, implementing a custom `flash-red-row` CSS animation to instinctively guide approvers toward their assigned tasks upon redirection.
- **Dynamic SMTP Management**: Database-backed SMTP configuration with AES-256 encryption and real-time connectivity diagnostics.

## [2.50.0] - 2026-04-11

### Added
- **Password Recovery Workflow**: Implemented a complete self-service recovery flow including `Esqueceu a senha?` toggle on the login page, secure token generation with 15-minute expiry, and a dedicated `ResetPasswordPage`.
- **Bulletproof Email Logo (CID)**: Redesigned the transactional email engine to use **CID inline embedding** for the ALPLA logo. This ensures visual assets render correctly in all email clients without relying on a publicly accessible URL, solving "localhost" image breakage during development.
- **Robust Asset Resolution**: Implemented a multi-path fall-back strategy for locating physical assets on the server, with support for configuration overrides via `AppConfig:LogoPath`.
- **Environment Safety Guards**: Added strict backend validation to prevent the dispatch of transactional emails containing `localhost` or `127.0.0.1` links in non-development environments.

### Changed
- **Centralized URL Configuration**: Replaced dynamic `Request.Headers["Origin"]` resolution with a deterministic `AppConfig:FrontendBaseUrl` setting in `appsettings.json`, ensuring link reliability across staging and production.

## [2.49.2] - 2026-04-11

### Added
- **Intelligent Flow Notifications**: The system now issues explicit Informative Push Notifications to Requesters immediately when a Quotation completes, keeping the request authors directly in the loop.
- **Floating Area Navigation**: Warning banners for missing Selection decisions inside the Approval Drawer now support interactive smooth-scrolling, directly guiding Area Approvers to the specific form section using a 5-second red pulse animation.

### Fixed
- **Role-Based Visibility (RBAC)**: Stabilized Area Approver scopes inside `NotificationService.cs`, accurately surfacing Pending Approvals tailored strictly by role matching `Area Approver` rather than hardcoded IDs.
- **Restricted Access Paths**: The action buttons linking to "Gestão de Cotações" and "Recebimento" inside the Operational Hubs (`Dashboard` & `Purchasing`) now appropriately abide by RBAC filtering (Buyer and Receiving respectively), concealing pathways dynamically from unauthorized personas.
- **Approval Drawer Banners**: Repaired context leakage where Area Approvers were erroneously presented with blue re-routing banners designed for the "Final Approval" stage after their jurisdiction had already passed.

## [2.49.0] - 2026-04-11

### Added
- **Visual Checklists for Verification**: Implemented interactive row-highlighting checkboxes in the OCR and Request line item tables (Gestão de Cotações and Pedidos de Pagamento) to facilitate physical-to-digital document verification.
- **Dynamic OCR Discount Logic**: Implemented elastic proportion calculations for OCR discounts tracking `discountPercent`. This ensures that when buyers adjust line quantities, the discount scales mathematically and preserves logical unit subtotals.
- **Temporal Finance Graphing**: Expanded the projected cash-flow timeline on the Finance Dashboard to include configurable "1 Dia" (Default), "3 Dias", and "7 Dias" horizon toggles.
- **Contextual Request Terminology**: Requests Dashboard grid now natively translates the standard "Data Limite" column into "Recebido em" (for Completed Quotations) and "Pagamento Realizado em" (for Paid status), reducing timeline ambiguity.

### Fixed
- **Finance Modal Standardization**: Adapted the `FinanceActionModal` to the brutalist/premium corporate design standard, replacing legacy styling with CSS variables.

## [2.48.0] - 2026-04-11

### Added
- **Deep Linking Context Navigation**: The dashboard carousel now natively links urgent requests to their contextual workspaces (Purchasing: `WAITING_QUOTATION`, Receiving: `WAITING_RECEIPT` / `PAYMENT_COMPLETED`, Finance: `PO_ISSUED` / `PAYMENT_SCHEDULED`).
- **Visual Pulse Highlight**: Deep-linking now highlights the target element in the list with an animated red pulse, immediately driving the user's attention.
- **Drawer Integration (Finance)**: Finance obligations/payment lists now consume the standard `RequestQuickViewDrawer` for detailing objects, substituting intrusive new-tab spawning.

### Fixed
- **Roles Matrix Data Integrity**: Repaired an exclusion rule in `RequestsController.cs` that previously truncated actionable tasks for Buyers/Executors dependent on legacy timeline boundaries.
- **Layout Contraction Flaws**: Pushed `width: 100%` overrides on global Flex wrappers (`PageContainer.tsx`) to resolve aggressive collapsing behaviors in the `/finance/history` audit logs.
- Removed legacy UI links ("Modo Clássico") that persisted on modern component iterations.

## [2.47.0] - 2026-04-11

### Added
- **UI/UX Standarization (Phases 1-4)**: Completely standardized the visual alignment of all legacy operational and administrative workspaces to mirror the modern "Requests" baseline structure.
    - Decoupled brutalist grid/borders by injecting new `PageContainer`, `PageHeader`, `StandardTable`, and `SearchFilterBar` layout wrappers.
    - Refactored `Dashboard.tsx`, `FinanceLandingPage.tsx`, `PurchasingLandingPage.tsx`, `UserManagement.tsx`, `SystemLogs.tsx`, `MasterData.tsx`, `AdministratorWorkspace.tsx`, and all core inner modules to the new corporate design language.
    - Resolved widespread TypeScript constraints on Table wrappers, moving to native HTML tabular child declarations for superior component fluidity.

## [2.46.0] - 2026-04-11

### Added
- **Modern Requests Dashboard**: Completely visually overhauled the primary Requests workspace, migrating away from the legacy brutalist table patterns to a high-fidelity 'Modern Corporate' widget layout.
    - Added `ActionCarouselWidget` to surface urgent "Para Minha Ação" tasks with high-contrast motion cards.
    - Updated `RequestsTableWidget` to support native sticky scrolling, status chips, and integrated global Kebab menus.
- **Drawer Presentation Mode (Dual-Mode Architecture)**: Integrated `RequestDrawerPresentation`, allowing users to open and edit full requests via a slide-out right panel directly from the dashboard without navigating away or losing context. This leverages the existing `RequestEdit` business logic securely.

## [2.45.1] - 2026-04-10

### Fixed
- **Dark Mode UI Stabilization**: Conducted a systematic sweep of the entire frontend to eradicate hardcoded hex colors (`#fff`, `#f3f4f6`, `#e5e7eb`) from inline styles.
    - Updated 14+ core components and pages (User Management, Finance Overview, Dashboards, Autocompletes) to use theme-aware CSS variables.
    - Preserved document-viewer white-space integrity for PDFs and scanned images as per operational requirements.

## [2.45.0] - 2026-04-10

### Added
- **Dark Mode Support**: Implemented native theme switching (Light, Dark, System) with automatic FOUC (Flash of Unstyled Content) prevention.
    - Integrated a persistent `useTheme` hook with `localStorage` and system preference detection.
    - Overhauled `tokens.css` with high-contrast slate-based palettes and optimized "Congress Blue" for deep backgrounds.
    - Added an interactive theme switcher in the `UserDropdown` component.
- **Stacked Requests List**: Refactored the core Requests Management workspace into two distinct, vertically stacked sections:
    - **"Para Minha Ação"**: A filtered view dedicated to tasks requiring direct user intervention based on role-based responsibility logic (Requester adjustments, Area/Final approvals, Buyer quotations, Finance scheduling).
    - **"Explorador de Pedidos"**: A global view browsing all other accessible requests.
- **Reusable `RequestsGrid` Component**: Extracted list rendering, searching, filtering, and independent pagination into a modular component, enabling multi-list layouts without state collisions.

### Changed
- **Responsibility Filtering**: Enhanced the `RequestsController.GetRequests` endpoint with `myTasksOnly` and `excludeMyTasks` boolean flags, leveraging server-side LINQ expressions for advanced responsibility detection.

## [2.44.1] - 2026-04-10

### Fixed
- **Global Discount Persistence**: Resolved a regression where the "Desconto Comercial" was being overwritten by gross totals during the Payment Request submission. Recalculated totals now properly account for global discounts across all line-item mutation endpoints (Create, Update, Delete).
- **Payment Request Submission Payload**: Ensured `discountAmount` is included in the initial creation request to prevent zero-value defaults on the backend.

## [2.44.0] - 2026-04-10

### Added
- **Quotation Assignment Security**: Implemented strict ownership restrictions in the Quotation Management workflow. Unassigned or laterally assigned quotations are locked to read-only views, exposing a dynamic interface specifically for claiming/re-assigning ownership on-the-fly (`isAssignedToMe`).

### Changed
- **Quotation Discount Financial Model**: Refactored quotation draft properties from macro global elements down into explicit item-level discount declarations (`DiscountAmount` and `DiscountPercent`).
- **Orphan Attachment Cleanup Mitigation**: Integrated logic into the UI's draft lifecycle handlers effectively triggering backend deletion API operations exactly when an end-user abandons an actively loading OCR upload via the new "CANCELAR" interactive component.

## [2.43.1] - 2026-04-09

### Fixed
- **TotalAmount Persistence & Calculation**: Resolved a financial data loss issue where discount amounts and IVA rates were discarded during Request Line Item processing resulting in incorrect `TotalAmount` values (e.g., reverting to gross totals instead of net + IVA).
    - Appended `DiscountPercent` and `DiscountAmount` to the database schema and response payload to survive frontend auto-save loops.
    - Standardized `TotalAmount` calculation across all backend item generation points (Bulk Create, Clone, Add, Update) to explicitly include discounts and IVA variables.

## [2.43.0] - 2026-04-09

### Added
- **Context-Aware OCR Triage**: Implemented `sourceContext` propagation in the extraction pipeline. When documents are uploaded from Quotation or Payment flows, the system now enforces an "Invoice" classification, preventing catastrophic misclassification as "Contract" while still allowing manual override or scanned-file fallback.
- **Multi-Strategy Supplier Matching**: Overhauled the frontend supplier identification in `useOcrProcessor.ts`.
    1. **Normalization**: Automatically strips trailing punctuation (e.g., "S.A." vs "S.A"), collapses whitespace, and normalizes apostrophes.
    2. **NIF/TaxId Fallback**: Implemented a dedicated persistence-layer search by `TaxId` if name-based matching fails, significantly reducing duplicate supplier records.
- **Discount & IVA Reliability**: Enhanced the extraction prompt and mapping to distinguish Portuguese "Desc." (discount %) from "IVA" (tax %) columns. 
- **TotalPrice Anchor Validation**: Implemented a frontend safety net that reverse-engineers the real discount amount from the document's `totalPrice` if the AI confuses the discount and tax columns.

### Changed
- **OCR System Prompt**: Updated with bilingual (PT/DE) column mapping instructions and self-validation rules for line items.

## [2.42.1] - 2026-04-09

### Fixed
- **P.O. OCR Data Path Mismatch**: Corrected `RegisterPoModal` to read OCR data from the legacy envelope path (`integration.headerSuggestions.grandTotal.value`) instead of a non-existent flat path.
- **P.O. Grand Total Extraction**: Added `grandTotal` field to the GPT extraction schema to capture the final amount including IVA, resolving systematic mismatches against quotation totals.
- **P.O. Supplier Identification on Encomendas**: Added explicit prompt instructions for Purchase Order layouts from ERP Primavera.
- **Quotation Winner DTO Mapping**: Fixed `totalPrice`→`totalAmount` and `currencyId`→`currency` property mismatches in `RequestEdit.tsx`.
- **TextFirst Null Guard**: Hardened OCR early-return condition to reject empty strings via `!string.IsNullOrWhiteSpace`.

## [2.42.0] - 2026-04-09

### Added
- **P.O. Override Validation via OCR**: Implemented a protective soft-block flow in `RegisterPoModal`. The system evaluates similarity matching between the document's payload and the approved request parameters. Mismatches require an active acknowledgement (Override Confirmation) and a mandatory qualitative justification before generating the Purchase Order.
- **P.O. Dispute Audit Log**: The Backend endpoint (`RequestsController.RegisterPo`) now natively audits OCR mismatches and override comments directly into the `RequestStatusHistory` timeline ensuring financial traceability.

### Fixed
- **Quotation Workflow Item Desync**: Corrected a regression where selecting a winning quotation left the Area Approver with an empty grid. The system now performs a hard-sync operation (`RequestsController.SelectQuotation`), automatically wiping existing generic request line items and comprehensively replacing them with strict clones of the selected `QuotationItems`, preserving quantities, identical descriptions, and computed aggregates seamlessly.

## [2.41.0] - 2026-04-09

### Added
- **P.O. Workflow Visibility**: Implemented orange "Aguardando P.O" KPI card in the dashboard and a dedicated quick-chip in the requests grid mapped to the `APPROVED` status.
- **Optimized P.O. Registration UX**: Replaced fragmented workflow with a specialized `RegisterPoModal` that handles document upload and status transition to `PO_ISSUED` in one click.
- **Unit Master Data Integrity**: Filtered out deactivated units (`isActive: false`) from OCR auto-suggestions and manual selection menus in Quotation and Buyer Item lists.

### Fixed
- **RequestsController Build Error**: Corrected status constant usage from `Statuses.Approved` to `Statuses.FinalApproved`.
- **Frontend Build Stability**: Cleaned up imports in `DecisionFinancialTrendLine.tsx` and `ActionMenu.tsx`.

## [2.40.0] - 2026-04-09

### Added
- **OCR Line-Item Discount Extraction**: Extended the extraction pipeline to identify and extract per-item discount percentages and amounts from invoices (e.g., "Rabatt %" columns). Added `DiscountPercent` and `DiscountAmount` fields across the full backend DTO chain (`ExtractionLineItemDto`, `OcrLineItemSuggestionDto`, `ExtractionMapper`).
- **Discount Cross-Validation (Frontend)**: Implemented a safety net that recalculates `discountAmount` from `discountPercent` when the AI returns an incorrect per-unit value instead of the total line discount. Logs a `console.warn` when a correction is applied.
- **Editable Discount Column (UI)**: Added a new `DESC.` column to the Payment Request OCR items table, allowing users to view and manually correct extracted discount values. Includes a red "TOTAL ABATIMENTOS" summary row in the footer.
- **Company Auto-Identification (OCR)**: Implemented keyword-based company matching that identifies `AlplaPLASTICO` or `AlplaSOPRO` from OCR-extracted billing entity names, auto-filling the "Empresa" field with a visual confirmation message.
- **OCR Diagnostics**: Added `[OCR] Company Match Diagnostics` and `[OCR] Extraction & Calculation Diagnostics` console groups for real-time debugging. Enhanced Admin System Logs to show "Empresa Identificada (OCR)" with visual warning when extraction fails.

### Changed
- **AI Extraction Prompt**: Rewritten with explicit discount calculation rules and concrete examples to prevent the model from returning per-unit discounts instead of total line discounts.
- **Item Total Calculation**: All line-item and draft totals are now always recalculated from components (qty × unitPrice − discount + IVA) instead of trusting OCR-provided totals, eliminating silent zero-value errors caused by `??` operator semantics.
- **Quantity Column Width**: Increased QTD column from 60px to 80px to accommodate multi-digit quantities.

## [2.39.8] - 2026-04-09

### Changed
- **Requests List Performance Optimization**: Refactored the core EF Core LINQ projections in `RequestsController.GetRequests` to utilize `SelectMany().SumAsync()` left-joins, removing a crippling in-memory aggregation bottleneck and dropping server response times for the main workspace dataset from ~40s to ~230ms.

## [2.39.7] - 2026-04-08

### Added
- **OCR Execution Audit**: Integrated mandatory, persistent audit logging for all extraction pipeline executions (Success, Partial, Failure). Every run directly creates an immutable system log entry preserving user attribution, routing strategy, categorization (contract vs. invoice), and LLM token usage inside `AdminLogEntries` table.

## [2.39.6] - 2026-04-08

### Added
- **Contract Extraction Pipeline (Phase 3)**: Implemented a dedicated parsing strategy for long-text documents and contracts using sequential text chunking. Achieved a ~96% reduction in token usage for contracts (e.g., from ~111k to ~3.7k tokens) by bypassing unnecessary full-document Vision rasterization.
- **Smart Document Triage**: Developed a multi-factor classification heuristic analyzing text density and keyword signals within the first pages of PDFs to route documents definitively to either Invoice or Contract pipelines without causing schemas cross-contamination.

### Changed
- **Contract Metadata Exposure**: Exposed `ChunkCount`, `IsPartial`, and `ConflictsDetected` to track performance and data reliability of long-text ingestion paths without breaking existing presentation-layer mappings.

## [2.39.5] - 2026-04-08

### Changed
- **Adaptive OCR Routing (Phase 2)**: Introduced a Text-First extraction path using `PdfiumViewer` to preemptively extract text from native PDFs. Bypasses the heavy Vision payload generation for clean invoices, reducing extraction costs by ~98%. Scanned or insufficient documents automatically fall back to the Vision API rasterization.
- **Extraction Telemetry Enhancement**: Enriched `ExtractionResultDto.Metadata` with `RoutingStrategy`, `DetailMode`, and `NativeTextDetected` logic for seamless real-time consumption and token cost audits.

## [2.39.4] - 2026-04-08

### Added

- **Token & Cost Observability**: Extracted and mapped OpenAI token consumption (Prompt, Completion, Total) directly into `ExtractionResultDto.Metadata`. This telemetry is seamlessly exposed to the frontend payload, guaranteeing baseline observability for token consumption modeling.
- **Provider Payload Telemetry**: Added diagnostic logging in `OpenAiDocumentExtractionProvider` pointing exactly to the payload character size sent to the GPT Vision layer, further solidifying observability.

### Changed

- **Adaptive Document Rasterization Engine (Phase 1)**: Overhauled the OCR PDF rendering layer inside `OpenAiDocumentExtractionProvider` to decisively slash overarching token burn rates.
  - Transferred image projection output from lossless PNG to compressed `ImageFormat.Jpeg` (Quality: 85).
  - Reduced default rasterization limits from `300 DPI` to `150 DPI`, allowing smaller tile allocations in OpenAI's Vision model while preserving reading integrity.
  - Bounded initial analysis to `3 pages` max for financial invoices, averting runaway extraction over long procedural annexes.
  - Intercalated a `DocumentRenderProfile` structure to allow fluid, programmatic switching of DPI/quality policies pending future `Contract` analysis needs.

### Fixed

- **OpenAI Vision Payload Inflation**: Resolved a systemic token-burn issue where OpenAI billed ~37,000 prompt tokens per JPEG page due to `System.Text.Json` escaping base64 characters (e.g. `+` to `\u002B`). Injected `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` during payload construction to maintain standard Base64 integrity, allowing OpenAI to successfully process images as native tiles rather than enormous unstructured textual arrays.

## [2.39.3] - 2026-04-08

### Added

- **Approval Detail Financial Context**: Implemented a new `DecisionFinancialTrendLine` visual chart inside the Approval Center to contextualize request amounts alongside historical expenditure.
  - Generates real-time, comparative trend lines showing "Pending Payment" versus "Paid" items over configurable resolutions (Weeks or Months).
  - Integrates two scopes (`PLANT` and `DEPARTMENT`) to empower approvers with cross-sectional visibility of their spend throughput before making a decision.
  - Dynamically calculates item count and provides interactive tooltips indicating both monetary volume and request volume.
  - Upgraded internal timeline engine to safely aggregate and label ISO 8601 week mappings (`System.Globalization.ISOWeek`).
- **Development Tooling**: Enhanced the Dev Seeding engine to construct deep historical status lineages, seamlessly testing the decision intelligence pipeline without organic wait times.
- **Admin System Logs Analytics**: Upgraded the operational Logs panel into a rich observability dashboard.
  - **Deep Search**: Added full-text search capability into JSON `Payload` and `ExceptionDetail` stacks.
  - **Live Tail**: Introduced a toggleable auto-refresh worker (10s polling) to monitor systems in real-time.
  - **Activity Histogram**: Integrated Recharts to generate a temporal bar chart stacking errors, warnings, and infos for instant visual anomaly detection.
  - **Export CSV**: Added native `/export` endpoint and UI integration for bulk log extraction.
  - **JSON Clipboard**: Expanded log detail modal with one-click JSON payload copy capabilities.
- **Application Versioning**: Integrated global portal version tracking.
  - Centralized application settings payload onto standard `config.ts`.
  - Statically anchored the release version label (`v2.39.3`) to the Navigation Sidebar footer and the Public Login panel to aid deterministic bug reporting.
- **Finance History Observability**: Modernized the History & Audit interface for financial operators.
  - **Timeline Hybrid UI**: Replaced standard datagrid with a date-grouped visual timeline reflecting actions like Scheduled, Paid, Notes, and Return adjustments using color-coded nodes.
  - **Contextual Data Joins**: `FinanceHistoryItemDto` now aggregates associated metadata from the overarching Request (`RequestNumber`, `RequestTitle`), empowering instantaneous understanding of atomic historical events without secondary lookups.
  - **Export CSV Engine**: Delivered a backend-rendered, pre-formatted native export route decoupled from front-end table parsing limitations.
  - **Quick Searching & Filtering**: Injected global full-text evaluation for actor names, notes, and specific request numbers directly onto the timeline components.
- **Finance Dashboard Analytics**: Fully modernized the Finance Overview workspace into a data-driven cockpit.
  - **Recharts Integration**: Implemented Bar charts for 15-day Cash Flow Projection and Donut charts for Currency Liability Exposures (`AOA`, `USD`, `EUR`).
  - **Supplier Concentration**: Implemented a "Top 5" unpaginated live ranking to surface heavily centralized current debts.
  - **Operational Metrics**: Added a brutalist 'Aging' segment to track the latency (0-2, 3-5, 5+ days) of financial operations waiting for resolution.
  - **UX & Onboarding**: Deployed a dynamic "Help Glossary" Modal mapping out exactly how to read each KPI, matched with descriptive deep empty-states for sparse data environments.
- **Corporate Isolation Engine**: Re-engineered the Finance Pipeline to enforce dynamic company isolation.
  - Injected an interactive tab-view intercepting the `api.lookups.getCompanies()` Master Data to render available Active Entities (e.g., AlplaPLASTICO, AlplaSOPRO).
  - Wired `FinanceController.GetSummary` to accept an optional `companyId` constraint via `[FromQuery]`, enabling perfect segregation of debts, currency, and aging per CNPJ while maintaining the ability to scope `Global (Consolidated)`.

## [2.39.2] - 2026-04-08

### Added

- **Approval Allocation Interactivity**: The "Pendência de Alocação" warning on the Approval Detail Panel is now fully interactive. Clicking the warning triggers an automatic semantic scroll to the line items section, accompanied by a 5-second red pulse indicating exactly where the approver needs to operate.
- **Empty Field Highlighting**: Triggering the missing allocation warning now applies a persistent, high-contrast red border to any specific Plant or Cost Center dropdowns that are missing values, automatically dismissing once valid selections are made.
- **Bulk Apply Tooltips**: Added dark-mode hover tooltips to both layout variations of the "Aplicar aos X pendentes" buttons to explicitly clarify their function in filling unassigned selections.

## [2.39.1] - 2026-04-07

### Added

- **Finance Payment Scheduling Attachments**: Added optional file upload capabilities within the `FinanceActionModal` to support payment scheduling proofs using the backend `PAYMENT_SCHEDULE` document type.
- **Enhanced Payment Due Date Visibility**: The Finance payments grid now displays both the original due date ("Original") and the user-defined scheduled date ("Agendado") safely without conflation.

### Fixed

- **Payment Overdue Logic Refactor**: Refactored `FinanceController` overdue evaluations (`IsOverdue`, `IsDueSoon`) to prioritize the explicity-set `ScheduledDateUtc` over `NeedByDateUtc`. This eliminates false-positive overdue alerts for invoices that treasury deliberately rescheduled to future dates.

## [2.39.0] - 2026-04-07

### Added

- **Finance Workspace**: Implemented a comprehensive and compact operational cockpit for the treasury and accounts payable team under the "Finanças" navigation group.
  - **Overview Dashboard**: Added dynamic KPI cards tracking pending actions, scheduled volumes, overdue alerts, and completed monthly volume. Includes a curated "Immediate Attention" alert panel.
  - **Payments List**: A brutally efficient data grid aggregating all payment-pending requests. Hardened backend eligibility strictly requires a P.O. attachment before items enter the queue.
  - **Financial Action Handlers**: Direct UI actions allowing the finance team to *Schedule*, *Mark as Paid*, *Add Notes*, or *Return for Adjustment* per-request, utilizing a modern, Brutalist-compliant `<FinanceActionModal />` triggered from a standard `<KebabMenu />`.
  - **Dedicated Finance Return Flow**: Introduced new internal status `WAITING_PO_CORRECTION` ("Devolvido para Compras") to safely return invalid requests from Finance back to Purchasing without conflating states with general Approver rejections.
  - **Audit History**: Native tracking showing all actions taken by the finance team in a read-only audit log.
- **Backend Finance Services**: Segregated financial orchestration down into `FinanceController.cs` maximizing performance over the `RequestsController` and returning strictly typed DTOs mapping to the new namespace `AlplaPortal.Application.DTOs.Finance`.

## [2.38.2] - 2026-04-07

### Fixed

- **Quotation Attachment Anti-Duplication**: Resolved a functional regression where the SHA-256 duplicate warning modal failed to appear in the "Gestão de Cotações" workspace.
  - Fixed a React state-batching/closure bug in `QuotationEntry.tsx` that caused the loading state to flip prematurely.
  - Extended the pre-flight hash validation to the **OCR Import Flow** (`BuyerItemsList.tsx`), ensuring a consistent soft-block when uploading previously processed documents.
  - Implemented a standard file input reset pattern to ensure repeated selections of the same file correctly trigger the verification logic.
- **Supporting Document Visibility**: Surfaced non-quotation items (supporting attachments) in the Quotation Management header to provide buyers with full document context during analysis.


## [2.38.1] - 2026-04-07

### Added

- **Quotation Assignment Notifications**: Real-time notifications dispatched to the requester and the assigned buyer when a quotation is claimed or explicitly assigned.
- **Viewable Request Context**: Surfaced **Request Title** and **Description/Notes** in the Quotation Management Workspace to provide buyers with immediate context without needing to open the full request.

### Fixed

- **Buyer Assignment Resolution**: Safely mapping HTTP 204 responses on `assign-buyer` endpoints to prevent runtime parse failures.
- **UX Parity in Quotation Management**: Fixed a gap in API mapping in `LineItemsController` to correctly hydrate and display the assigned buyer's name.

## [2.38.0] - 2026-04-06

### Added

- **Request Document Anti-Duplication (Soft-Block)**: Implemented an intelligent pre-flight `SHA-256` hashing validation on the Frontend via Web Crypto APIs (`computeFileHash`) that converts physical documents to checksums regardless of format (PDF, PNG, Excel).
- **Server-Side File Verification**: Extended `AttachmentsController.cs` for physical duplicate monitoring. Prevents redundant document extraction and displays duplicate warnings in both Payment Request Creation (`RequestCreate.tsx`) and the Quotation Management Flow (`QuotationEntry.tsx`). Includes UX overrides for intentional duplication.

## [2.37.1] - 2026-04-06

### Fixed

- **Payment Request Date Validation Rejection**: Resolved the blocking 400 Bad Request error occurring during the creation or editing of requests with past dates.
  - Removed strict business constraints on the backend (`RequestsController.cs`) that rejected `NeedByDateUtc` values matching past dates.
  - Aligned backend flexibility with the updated frontend UX (which issues visual warnings via `AlertTriangle` rather than hard blockers).
  - Improved OCR flow fallback to transparently handle mapping of document dates into the unified `NeedByDateUtc` architecture.
- **OCR Unit Fallback Reliability**: Fixed an issue where the OCR extraction process would incorrectly save deactivated units (like 'EA') directly to item requests.
  - Re-engineered `useOcrProcessor.ts` to implement a rigid fallback sequence mapping unidentified or raw string aliases into standard system-ready `UN` identifiers.
  - Exposed a new interactive "UNID." selection field seamlessly bridging into the front-end OCR Item configuration table (`RequestCreate.tsx`).
- **Payment Request Table UI Cleanup**: Stripped contextually redundant columns ("Centro de Custo", "Vencimento", "Status") from the visual list space (`RequestEdit.tsx`) to declutter UX without mutating backend constraints.
- **Payment Request OCR & Manual UX Standardization**: Completely decoupled the OCR and Manual input modes from the legacy grey bounding box, adopting the "Gestão de Cotações" premium clean-card UI pattern.
  - Resolved nesting context logic in `RequestCreate.tsx` where manual inputs mistakenly operated visually as OCR success extracts.
  - Eliminated manual insertion friction by replacing "AAAA-MM-DD" text placeholders with native browser Date objects.
  - Integrated deterministic visual states parsing out the "DADOS EXTRAÍDOS COM SUCESSO" banner from an independent "INSCRIÇÃO MANUAL DA FATURA" banner.
  - Exposed full "Adicionar Item" lifecycle hooks nested inside and outside empty states to stabilize the UX for completely manual Payment Requests.
- **Approval Historical Price Intelligence**: Integrated an automated price-variance alert directly into the Approval Detailed Panel (`ApprovalDetailPanel.tsx`).
  - Visually flags requests that contain items priced above their historical averages (Yellow Warning).
  - Highlights favorably (Green Warning) when all historical item pricing stays aligned or below market average.
  - Decluttered redundant currency outputs in the hero header (`DecisionHeader.tsx`) and enlarged Request Number prominence.

## [2.37.0] - 2026-04-06

### Changed

- **Approval Workspace Overhaul**: Completely modernized the user interface for the Approval Detail panel. Replaced all legacy brutalist remnants (raw black borders, strong shadows) with the new 'Premium Corporate' aesthetic leveraging `--color-bg-surface`, `--shadow-sm`, and `--radius-lg`.
- **CSS Infrastructure Upgrade**: Migrated embedded nested panels (`DecisionInsightsPanel`, `DetailedHistoryPanel`, `DecisionTimeline`, `DecisionQuotationCard`) away from pure Tailwind utility dependency toward strict inline mapping to the internal `tokens.css` design system. This eliminates widespread rendering failures previously caused by global layout class collisions.
- **Adaptive UX Fallbacks**: Hardcoded deterministic overrides to fallback between grid 'Cards' and table 'List' structures automatically based on strict `> 5 item` thresholds inside the Approval viewer.

## [2.36.0] - 2026-04-06

### Added

- **Item-Level Cost Center Mapping (Area Approval)**: Transitioned Area Approval from request-level to item-level cost center assignment.
  - **Granular Allocation**: Approvers must now assign a cost center for each individual active line item in multi-item requests.
  - **Plant-Aware Filtering**: Cost center options dynamically filter themselves based on the authoritative `PlantId` belonging to the specific line item, preventing cross-plant financial misallocation.
  - **Safe Bulk-Fill Helper**: Introduced a "Repetir" helper that explicitly targets only unassigned items sharing the same plant.

### Changed

- **Decision Summary UX**: Upgraded the "Centro de Custo" card in the Decision Summary Panel to be highly reactive, introducing strong unassigned alert states (`Pendente X itens`) and clearly delineating uniform vs. mixed assignments.
- **DTO Migration**: Shifted `ApprovalActionDto` from a single `CostCenterId` to a `Dictionary<Guid, int>` representing `ItemCostCenters`.

## [2.35.0] - 2026-04-06

### Added

- **Reactive OCR Supplier Workflow (New Request)**: Relocated the unresolved supplier validation from Request Edit to the New Request screen for a proactive "pre-creation" experience.
  - **Visible Preservation**: OCR-extracted supplier names are now displayed even without a database match, marked with a clear "Unresolved" state.
  - **Quick-Create Integration**: In-place `QuickSupplierModal` access during OCR preview, with automatic state synchronization and selection upon successful creation.
- **Backend Portal Code Generation Hardening (DEC-098)**: Re-engineered the `Supplier.PortalCode` generation logic to be structurally robust and concurrency-safe.
  - **Auto-Sync / Self-Healing**: The system now automatically detects and fixes out-of-sync counters by querying the actual `Suppliers` table for the maximum existing suffix before incrementing.
  - **Concurrency Safety**: Implemented database-level `UPDLOCK, ROWLOCK` within a transaction to prevent duplicate code assignment during simultaneous creations.

### Fixed

- **Supplier Portal Code Collision**: Resolved the critical unique index violation (`IX_Suppliers_PortalCode`) where the system incorrectly reused `SUP-000001` after a transactional data reset.

### Changed

- **Maintenance Script Protection**: Updated `ResetTransactionalData.sql` to exclude Master Data counters (Suppliers) from blanket resets, ensuring sequence continuity across transactional wipes.

## [2.34.0] - 2026-04-06

### Added

- **Supplier Quick-Create (Request Edit)**: Integrated the ability to create new suppliers directly from the Request Edit screen.
  - Added a `+ NOVO FORNECEDOR` button for manual creation in authorized draft/quotation stages.
  - Implemented an **unmatched OCR supplier warning** that appears when a suggested name from an invoice does not match an existing database record, providing a "CRIAR AGORA" entry point.
  - Direct integration with `QuickSupplierModal` (reused from Buyer items) with auto-selection and validation clearing upon successful creation.

### Changed

- **PAYMENT Request Type Optimization**: Refined the Request Edit UI for payment-specific workflows, removing quotation-related bloat.
  - **Conditional UI Rendering**: The "Cotações Salvas" section is now suppressed for `PAYMENT` requests, ensuring a cleaner interface focused on financial tracking.
  - **Guided Attention Refinement**: auto-scroll and highlight logic (guided attention) now strictly targets `QUOTATION` requests, preventing jarring movements to non-existent sections in payment flows.

## [2.33.2] - 2026-04-06

### Fixed

- **Approval Modal State Sync**: Fixed a stale closure bug in the Approval Center drawer where action buttons and local states derived from previous requests bled into the next request when auto-navigating. 
  - Enforced a strict React render boundary by adding a `key` prop tied to the request ID on the `ApprovalDetailPanel`.
  - Added programmatic resets for drill-down overlays (`selectedDetailedItem`) on queue navigation handlers (`handleNext`, `handlePrev`, `handleActionCompleted`).

## [2.33.1] - 2026-04-06

### Fixed

- **Payment OCR Navigation White Screen**: Fixed a critical `Uncaught TypeError` caused by an invalid framer-motion `times: [0, 1, 0]` keyframe array in the OCR loading dots animation.
  - The Web Animations API (WAAPI) requires monotonically non-decreasing offsets. The invalid array crashed framer-motion's global animation engine, corrupting **all** `motion.*` components system-wide — causing a blank white content area on the edit page and a broken user profile menu.
  - Fixed by correcting the offsets to `times: [0, 0.5, 1]`.

### Added

- **ErrorBoundary Component**: Added a React Error Boundary wrapping route components (`RequestCreate`, `RequestEdit`) and the `AppShell.Outlet`. Future render crashes will display a visible red error panel instead of a silent white screen.
- **Loading Spinner CSS**: Added missing `@keyframes spin` to `globals.css`. The `RequestEdit` loading state now shows a visible animated spinner with inline styles instead of relying on the previously nonexistent `.spinner` class.
- **Defensive Data-Load Guard**: `RequestEdit` now shows a user-friendly error panel with retry/back actions if data fails to load, instead of rendering a white screen.

## [2.33.0] - 2026-04-05

### Fixed

- **Payment OCR Rendering Regression**: Resolved the "grey block" failure where the OCR success section was visually hidden after extraction.
  - Removed `overflow: hidden` and height-restricted animations from the parent container that were clipping the expanded result table.
  - Stabilized the success branch by transitioning from `motion.div` to a standard `div` with an explicit ID.
  - Hardened state transitions to ensure the UI paints immediately upon receiving the mapped draft.

## [2.32.0] - 2026-04-05

### Fixed

- **Payment OCR Flow UX & Mapping Refinement**: Resolved data mapping and UX bugs between the OCR upload and the persisted draft.
  - **Loading UX**: Implemented a responsive loading overlay during OCR processing and disabled premature form submission.
  - **Supplier Persistence**: Fixed a missing payload mapping, ensuring the OCR-resolved Supplier ID is correctly passed to the backend and preserved in the draft.
  - **Total Consistency**: Preserved the OCR-derived, IVA/discount-inclusive final total during Payment draft creation by passing it from the frontend and explicitly preventing backend sum-based reassignment for the `PAYMENT` request type.
  - **Currency Passthrough**: Correctly mapped the OCR-extracted currency alias to the draft payload.

### Known Limitations

- **Interim Discount Handling**: Surcharges and penalties are currently folded into the total by the OCR service and are not structurally extracted. Explicit discount values are temporarily appended to the Request `Description` as a traceability workaround. A dedicated `DiscountAmount` (and scalable financial adjustment architecture) is required for a final business-model solution.
- **Company Prefill**: The system currently employs a deterministic scope-based fallback (auto-selecting the only available company for restricted users). It does **not** employ true OCR-based company matching, as the `Company` entity lacks a `TaxId` necessary for correlation.

## [2.31.0] - 2026-04-05
### Fixed

- **Payment OCR Draft Persistence (DEC-097)**: Resolved a 500 error when creating Payment requests from OCR-extracted items.
  - Relaxed entity-level mandatory constraints for `CostCenterId` and `IvaRateId` on `RequestLineItem`, making them nullable in the database.
  - Implemented deterministic `LineNumber` assignment (incremental index + 1) during the draft creation payload and backend processing.
  - Deferred strict business validation to the `SubmitRequest` stage, ensuring a request cannot progress beyond `DRAFT` status if mandatory fields are missing.
  - Corrected calculation of `EstimatedTotalAmount` in the draft creation flow to reflect OCR-extracted item totals.

## [2.30.0] - 2026-04-05

### Added

- **Payment OCR Intake (DEC-096)**: Implemented automated document extraction for the "Payment" request type.
  - Contextual OCR dropzone in `RequestCreate.tsx` that appears when "Payment" is selected.
  - Automated mapping of invoice data (Number, Date, Currency, Items) to the request draft.
  - Interactive item review and editing before initial draft generation.
  - New backend endpoint `direct-ocr` in `RequestsController.cs` for ID-less document extraction.
- **Shared OCR Processing Hook**: Refactored quotation-specific OCR logic into a reusable `useOcrProcessor.ts` hook.
  - Decoupled normalization and field mapping from UI components.
  - Centralized calculation logic for IVA and totals.

### Changed

- **Quotation OCR Refactor**: Migrated `BuyerItemsList.tsx` to use the shared `useOcrProcessor` hook, ensuring logic parity across flows.

## [2.29.0] - 2026-04-04

### Added

- **Company Master Data Management**: Implemented a new "Empresas" tab in the Master Data UI to manage legal entities.
  - Supports full CRUD operations (Create, Read, Update, Toggle Active).
  - Integrated a User Lookup filtered by the `Final Approver` role for company-level assignment.
  - Implemented protection against renaming companies already associated with historical requests.
- **Backend CRUD for Companies**: Extended `LookupsController.cs` with robust endpoints for company management, including `FinalApproverUserId` support.

### Changed

- **System-Resolved Final Approver (DEC-093)**: Replaced manual requester-side actor selection with deterministic backend resolution based on the selected company.
- **Workflow Submission Gating**: Enhanced `RequestsController.cs` validation to strictly require a company-level Final Approver assignment before allowing submission.

## [2.28.0] - 2026-04-04

### Added

- **Placeholder & Field Legibility Design Tokens**: Introduced specific semantic tokens in `tokens.css` for form fields:
  - `--color-placeholder`: High-contrast grey for inactive placeholders.
  - `--color-placeholder-focus`: Increased contrast when the field is focused.
  - `--color-field-disabled-bg` & `--color-text-field-disabled`: Standardized colors for disabled inputs.
  - `--color-field-readonly-bg`: Visual distinction for read-only fields.

### Fixed

- **Project-Wide Accessibility Audit (Placeholders)**: Remediated low-contrast placeholder rendering by removing opacity-based transparency and relying on resolved high-contrast tokens.
- **Form Readability Normalization**: Removed global `uppercase` text-transform from placeholders and custom autocompletes to improve legibility for long-form examples.
- **Native Select Placeholder State**: Standardized the "-- Selecione --" (empty) state in native `<select>` elements using the `:has()` selector to mirror text input placeholder styling.
- **Autocomplete Component Standardization**: Updated `CostCenterAutocomplete` and `SupplierAutocomplete` to use new tokens and follow the global accessibility standard.

## [2.27.1] - 2026-04-04

### Fixed (2.27.1)
- **Drawer Layering Logic**: Resolved a systemic bug where `Z_INDEX` constants (strings) were being incremented in JavaScript, resulting in invalid CSS values. Replaced with valid `calc()` expressions in:
  - `UserProfileDrawer.tsx` (Fixed "Meu Perfil" visibility)
  - `ApprovalCenter.tsx` (Fixed resize handle visibility)
  - `PurchasingHelpDrawer.tsx` (Fixed "Manual de Operação" visibility)

## [2.27.0] - 2026-04-04

### Added
- **Scoped Admin Controls (DEC-095)**: Implemented role-based authority restrictions for Local Managers.
  - Empowered `Local Managers` to assign `Area Approver` and operational roles (`Requester`, `Receiving`, `Import`, `Viewer`).
  - Strictly restricted assignment of governance roles (`Buyer`, `Finance`, `Contracts`, etc.) to System Administrators.
  - Enforced scope-based filtering: Managers only see and assign plants/departments within their authorized boundary (e.g., `V1`, `V3`).
- **Receiving Workspace Scope Filtering**: Enforced plant/department data isolation in the Receiving module based on the logged-in user's profile.

### Fixed
- **Global Search Readability**: Scoped `::placeholder` styling to the header context (`.header-global-search`), ensuring high contrast on the dark topbar without leaking into light-surface search fields.
- **User Listing Visibility**: Harmonized Name/Code matching logic in User Management filtering, ensuring newly created scoped users are immediately visible to their managers.
- **Dynamic Master Data Loading**: Refactored admin forms to wait for `currentUser` profile initialization before populating filtered lookup options.

### Changed
- **Unified Navigation Governance**: Refactored navigation configuration to use role-based eligibility arrays, preventing unauthorized module discovery via group-level inheritance.

## [2.26.0] - 2026-04-04

### Changed

- **Instruction Layer Rebuild**: Consolidated agent governance into a lean, stable foundation.
- **Directives Consolidation**: Merged Documentation hygiene into `SOP_TASK_CLOSING.md` and unified status/stage rules into `RULE_WORKFLOW_PERMISSIONS.md`.
- **UI-Level Cleanup**: Removed legacy global workflows and redundant lifecycle rules from the Antigravity Customizations interface.
- **Legacy Migration**: Reorganized task-specific materials and reference SOPs into dedicated storage (`docs/rules/`).

## [2.25.1] - 2026-04-04

### Fixed

- **Tooltip Overflow**: Resolved UI clipping in the User Management drawer by implementing an explicit placement API in the shared `Tooltip` component and applying inward alignment for role-specific help text.

## [2.25.0] - 2026-04-04

### Added
- **Role Selection UX**: Implemented contextual help for the "Funções e Permissões" section in User Management.
  - Added a centralized `ROLE_DESCRIPTIONS` mapping for role definitions in `roles.ts`.
  - Integrated `Tooltip` and `Info` icons for every role option in the Create/Edit drawer.
- **Enhanced Role Constants**: Restored `ROLES` as the definitive shared source for role keys while safely augmenting it with descriptions.

### Fixed
- **White Screen Regression**: Restored the `ROLES` export in `src/frontend/src/constants/roles.ts`, resolving a critical build failure that prevented the application from rendering.
- **Table Header UX**: Standardized header contrast in `BuyerItemsList.tsx` and `MasterData.tsx` to align with the Modern Corporate design.


## [2.23.0] - 2026-04-03

### Added
- **Buyer Notifications for Updates**: Implemented automatic informational notifications for the assigned buyer when a requester modifies a request in `WAITING_QUOTATION` status. Includes a concise summary of changed fields.
- **Notification Service Extension**: Extended `INotificationService` with `CreateNotificationAsync` for discrete informational messaging.

### Fixed
- **Controlled Edit Persistence (Hotfix)**: Resolved a critical regression where requester edits in the "Aguardando Cotação" stage failed to persist.
- **Safe Comparison Logic**: Implemented `.Trim()` and null-coalescing in backend comparisons to ensure reliable change detection and history audit trails.
- **Backend Build Regression**: Corrected invalid `IsDeleted` property access on the `Quotation` entity across multiple controller methods.

### Changed
- **Requests List UX Restoration**:
  - Restored Request Number as a clickable link for direct navigation.
  - Optimized column widths: increased "Número" and reduced "Ações" for better content fit.
- **Smart Dirty Check**: Added a pre-modal check to `RequestEdit.tsx` to prevent unnecessary save confirmations when no header changes are detected.

## [2.22.0] - 2026-04-03

## [2.21.0] - 2026-04-03

### Added

- **Modern Corporate Visual Foundation (Phase 1)**:
  - New design tokens for border radii (`sm: 4px`, `md: 8px`, `lg: 12px`).
  - Standardized **Soft Elevation** tokens (`--shadow-sm`, `--shadow-md`, `--shadow-lg`).
  - Standardized **Border** tokens (`--color-border: rgba(0,0,0,0.1)`).
- **Core Component Refactor**:
  - `KPICard`: Executive-focused layout with soft elevation and neutral baselines.
  - `Sidebar`: Refined active states, thin borders, and modernized flyout menus.
  - `Topbar`: Reduced accent border and added subtle shadow.
  - `AppShell`: Replaced brutalist grid/borders with refined, rounded containers.

### Changed

- **Global Styling**:
  - Reduced button and form borders to `1px`.
  - Softened table borders and density while maintaining readability.
  - Removed brutalist hard-shadow offsets across the application.
- **Visual Alignment**:
  - Updated `collapsibleSection` and `badge` styles to follow the new radii standards.
- **v2.35.0**: Reactive OCR Supplier Workflow & Backend Portal Code Hardening (DEC-098). Relocated supplier validation to New Request screen and implemented robust, self-healing, concurrency-safe portal code generation.
- **v2.34.0**: Supplier Quick-Create in Request Edit & Payment Type Cleanup. Added supplier creation entry point for OCR mismatches and removed quotation-specific UI for payment requests.
- **v2.30.0**: Payment OCR Intake & Shared Hook (DEC-096). Implemented automated document extraction for Payment requests and refactored OCR logic into a shared hook.
- **v2.29.0**: Company Master Data & Final Approver Resolution. Implemented centralized company management and deterministic approval resolution, eliminating manual selection errors.
- **2.26.0**: Instruction Layer Cleanup & Baseline Rebuild. Consolidated fragmented permission and status rules into unified directives. Streamlined the process lifecycle and reorganized legacy documentation into reference storage.
- **2.25.1**: Tooltip Positioning Fix. Optimized the shared `Tooltip` component API to support explicit side-anchoring and alignment, resolving overflow regressions in the User Management drawer.
- **2.25.0**: Role Selection UX & UI Stability. Implemented contextual role tooltips for User Management and fixed a critical white screen regression by restoring the core `ROLES` constant. Standardized table header readability across operational modules.
- **2.24.0**: Brand Identity & Favicon Integration. Implemented a comprehensive favicon set based on the "A2 P-G" corporate logo, replacing the default Vite identity. Optimized for various devices (mobile, desktop, apple-touch). Also fixed a critical table header readability bug in the Master Data module.
- **2.23.0**: Request Edit Persistence & Buyer Notifications. Hotfixed the controlled-edit persistence regression and implemented automatic buyer notifications for requester updates in the quotation stage. Restored clickable request numbers and optimized list column widths.
- **2.22.0**: Modern Corporate UI Refinement (Phase 2). Significant interactive and visual elevation across high-traffic operational screens (Dashboard, Requests, Receiving). Replaced brutalist remnants with Soft Elevation and premium typography.
 Blue is now strictly reserved as an accent for actions and highlights.
- **v2.20.0**: Official Design Direction Transition. Formally transitioned the project from "Industrial Brutalist" to Modern Corporate. UI/UX Guidelines updated to emphasize soft elevations, refined borders, and rounded corners.

## [v2.19.0] - 2026-04-02

### Added

- **Approval Intelligence Tooltips**: Contextual hover explanations for all decision metrics (Monthly/Annual totals, Budget Impact, Purchase History, Variation) in the `DecisionInsightsPanel`.
- **Refined Cost Center Validation (DEC-090)**: Area Approval logic now differentiates between request types. PAYMENT requests with unified Cost Centers across items are automatically validated and read-only. Inconsistent or missing Cost Centers for PAYMENT, and all QUOTATION requests, still require explicit mandatory selection to ensure financial unicity (DEC-085).
- **Cost Center Propagation**: The selected Cost Center is automatically applied to all line items of the request upon Area Approval, ensuring data integrity for financial reporting.

## [v2.18.1] - 2026-04-02

### Changed

- **Sidebar Accordion Behavior**: Implementation of a single-open model for the expanded sidebar navigation. Opening one section automatically collapses others, reducing vertical bloat and improving navigation speed.
- **Auto-Expansion Refinement**: Optimized the route-awareness logic to ensure only the strictly relevant section expands upon navigation.

## [v2.18.0] - 2026-04-02

### Added

- **Navegação em Sidebar Recolhido (Hover Flyouts)**:
  - Implementação de painéis laterais (flyouts) que surgem ao passar o mouse sobre os ícones no modo recolhido.
  - Exibição do nome da seção e links de subseções com ícones.
  - Lógica de anti-flicker (150ms delay) e posicionamento inteligente (viewport-aware).
  - Fallback de clique: clicando no ícone no modo recolhido, o sistema agora navega para o primeiro link disponível daquele grupo.
- **Portal Rendering**: Integração com `DropdownPortal` para garantir que o menu lateral nunca seja cortado pelo shell da aplicação.

## [v2.17.0] - 2026-04-02

### Added

- **v2.18.0**: Sidebar Hover Flyouts (Navigation Overhaul)
- **v2.17.0**: Phase 2 Security Hardening (IP-based Rate Limiting)
- **Security Hardening (Phase 1)**: Implemented a robust security baseline focusing on attachment safety and authentication protection.
- **Attachment Upload Hardening**:
  - **Extension Whitelist**: Restricted uploads to a specific set of safe business extensions (`.pdf`, `.jpg`, `.jpeg`, `.png`, `.doc`, `.docx`, `.xls`, `.xlsx`).
  - **File Size Limits**: Enforced a strict 15MB limit per file on the backend.
  - **Filename Sanitization**: Implemented alphanumeric/hyphen/underscore sanitization for display filenames to prevent UI injection and path traversal.
  - **Storage Isolation**: Physical files are now stored using GUIDs, completely decoupling internal storage from user-provided names.
  - **MIME Verification**: Added basic Content-Type consistency checks as an additional security signal.
- **Login Brute-Force Protection**:
  - **Account Lockout Policy**: Implemented a temporary 15-minute lockout after 5 consecutive failed login attempts.
  - **Generic Error Messaging**: standardizing on a single unauthorized message to prevent user/account enumeration.
  - **Security Audit Logging**: Key security events (Lockouts, Blocked attempts) are now recorded in the `AdminLogEntries` table.

### Changed

- **User Entity Update**: Added `AccessFailedCount` and `LockoutEndUtc` fields to the core identity model.
- **Centralized Security Configuration**: Moved security parameters (lockout duration, attempt count, file limits, whitelists) to a dedicated `Security` section in `appsettings.json`.

### Added

- **Anti-Accumulative Copy Request Flow**: Implemented a robust "Copy Request" feature that uses a template-driven, frontend-first approach.
- **Strategic Data Exclusion**: The copy flow explicitly strips downstream operational data (items, currency, need-by date, attachments) to ensure the new request starts as a clean business need.
- **Title Composition**: Automatically generates the new title in the format `Cópia {SourceNumber} {OriginalTitle}`.
- **Ephemeral Draft State**: Copied requests exist only in the browser's memory until the user explicitly submits them, preventing database pollution from abandoned copies.
- **UX Safeguards**: Replaced "Cancelar" with "Descartar Cópia" in copy mode and added a mandatory warning banner for the copied description.
- **Navigation Protection**: Implemented `beforeunload` protection to prevent accidental loss of copied/edited data.

### Changed

- **Backend Template Mapping**: Updated `RequestsController.GetRequestTemplate` and `CreateRequestDraftDto` to support source request identification and title composition.

## [2.14.0] - 2026-04-02

### Added

- **Global UI Layering & Z-Index Standardization**: Unified the z-index hierarchy across the entire portal using centralized architectural tokens (`Z_INDEX` constants).
- **DropdownPortal Pattern**: Mandatory implementation of React Portals for all overlays (modals, drawers, tooltips, dropdowns) to ensure consistent rendering at the root level.

### Changed

- **AppShell Refactor**: Destroyed the global stacking context trap by removing `zIndex: 1` from the main content area, allowing fixed overlays to overlap the interface correctly.
- **Component Standardization**: Full refactor of `UserProfileDrawer`, `PurchasingHelpDrawer`, `Tooltip`, `Feedback`, `KebabMenu`, and `FilterDropdown` to adhere to the standardized positioning rules.

### Fixed

- **Layering Inconsistencies**: Resolved multiple bugs where confirmation modals appeared behind side drawers or top headers.

## [2.13.4] - 2026-04-02

### Fixed

- **Workflow**: Corrected a validation bug in the `Resubmeter Pedido` flow. High-level resubmission for requests in adjustment phases now correctly accounts for items contained within saved quotations, preventing false-positive "zero items" errors.

## [2.13.3] - 2026-04-02

### Fixed

- **Finance**: Corrected Quotation IVA calculation logic. IVA is now calculated on the net taxable amount (Gross - Discount) instead of the Gross subtotal.
- **Finance**: Populated `TotalTaxableBase` and `TotalDiscountAmount` in the Quotation entity and DTOs for consistent UI representation.
- **UI**: Updated `QuotationEntry` and `BuyerItemsList` to reflect the corrected calculation logic in the summary footers.

## [v2.13.2] - 2026-04-01

### Added

- **Alert Visibility — KPI Tooltip**: Added a hover tooltip to the "Com Alertas" KPI card in the Approval Center dashboard, explaining its meaning ("Pedidos com pontos de atenção na análise."). Reuses the portal-standard `Tooltip` component with structured content (label + definition).
- **Alert Visibility — Row Indicator**: Added a discreet `AlertCircle` icon (14px, rose) next to the request number in approval queue table rows for QUOTATION requests missing a selected winner. Includes a dark-variant tooltip on hover ("Requer atenção na análise"). No backend changes — reuses existing client-side alert condition.

## [2.13.1] - 2026-04-01

### Added

- **Detailed History Drill-down (Procurement)**: Implemented a high-density "sliding sub-panel" (drawer-within-a-drawer) that allows approvers to inspect historical purchase data for specific line items.
- **Normalized Matching Logic**: Backend support for matching items via normalized descriptions (levenshtein-like matching in SQL), providing a "Descrição Aproximada" context with a 1-year lookback.
- **Executive Drill-down UX**: Added a "Ver Detalhes" action to intelligence cards in the `DecisionInsightsPanel`. The sub-panel slides over the main drawer, maintaining approval context while opening deep historical data.
- **Price Variation Analysis**: The drill-down panel displays unit price comparisons against historical records with visual indicators for "Last Purchase" and price deviations.
- **Role-Aware Drill-down**: The entry point adapts its visual style based on the approver role (Area vs Final), ensuring a cohesive experience in the `DecisionInsightsPanel`.

### Changed

- **Drawer Header Redesign**: Replaced the heavy black header bar with a lighter, neutral surface with an 8px contextual left-accent border (blue for Area, green for Final). Improved prev/next navigation controls as 32px brutalist buttons.
- **History Panel Transition**: Replaced the "giant top-mounted slab" with a focused sliding sub-view using backdrop dimming and blur for maintained user focus.

## [2.13.0] - 2026-04-01

### Changed

- **Approval Center Visual Harmonization**: Full visual alignment of the `Centro de Aprovações` with the `Pedidos` (`RequestsList.tsx`) design standard.
- **Page Layout Refactor**: Transitioned to a full-width, flex-based layout with standardized gaps (`24px`) and margins.
- **Header Standardization**: Aligned typography, border tokens (`var(--color-primary)`), and hierarchy. Moved the header to the top of the workspace for consistent page rhythm.
- **KPI Card Integration**: Replaced custom card implementations in `QueueSummary.tsx` with the system-standard `KPICard` component for consistent motion and branding.
- **Filter Hub Modernization**: Updated sorting and filtering controls to use `--color-bg-surface`, `--color-primary` borders, and `var(--shadow-brutal)`.
- **Unified Filter Chips**: Standardized active-state behavior to use `var(--color-primary)` for all filter/sort chips, ensuring color consistency across the portal.
- **Queue Table Styling**: Normalized table wrappers and headers using global CSS tokens, replacing Tailwind-specific overrides with the system's "Brutalist" shadow and border patterns.
- **Loading & Empty States**: Standardized all feedback states with uppercase text patterns and design tokens.
- **Dev Tools Footprint**: Minimized the visual dominance of development tools to reduce UI noise.

## [2.12.2] - 2026-04-01

### Added

- **Role-Aware Decision Intelligence (DEC-084)**: The `DecisionInsightsPanel` now adapts emphasis and content based on the current approval stage (`AREA` vs `FINAL`), providing contextually relevant decision support without duplicating the drawer or forking the component.
- **Context Banner**: Subtle role indicator at the top of the intelligence panel — "Foco: Legitimidade e Necessidade" (Area) or "Foco: Racionalidade Financeira" (Final).
- **Area Emphasis — Checklist de Legitimidade**: Compact informational checklist with ✅/⚠️ indicators for Centro de Custo, Justificativa, Fornecedor, and (for Quotation types) Cotação Formalizada. Purely visual — no new approval blockers.
- **Final Emphasis — Visão Financeira Comparativa**: KPI grid surfacing Year Accumulated Total (newly exposed), Historical Purchase Count, and Weighted Consolidated Variation with partial-coverage disclaimer when not all items have history.
- **Role-Based Section Reordering**: Shared intelligence blocks (Alerts, Department KPIs, Item Analysis) render in different priority order per approval stage — Area prioritizes alerts, Final prioritizes financial context.
- **Extracted Reusable Primitives**: Internal refactor of `SectionLabel`, `KpiCard`, and `ItemCard` sub-components for consistency and maintainability.

## [2.12.1] - 2026-04-01

### Added

- **Resizable Approval Center Drawer**: Implemented horizontal resizing for the side drawer on desktop viewports (> 768px).
- **Persistent Width**: Support for saving and restoring drawer width via `localStorage` (`approvalDrawerWidth`).
- **Responsive Layout Reflow**: Optimized `DecisionInsightsPanel` (Visão Departamental and Análise por Item) to adapt naturally to varied drawer widths using responsive grid templates.
- **Interactive UI Handle**: Added a subtle, reactive resize handle on the left edge with high-hit-area hitboxes and hover indicators.
- **Consequences:** Provides a more premium and accessible interface. Requires a phased refactor of core design tokens and shell components in the next implementation cycle.

---

## DEC-096 — Shared OCR Processing & Direct Extraction

- **Date:** 2026-04-05
- **Status:** Accepted
- **Context:** The "Payment" request type requires automated data entry from invoices before the request is officially created. Existing OCR logic was tightly coupled to the Quotation workspace and required a Request ID.
- **Decision:** 
    - Abstract OCR normalization and mapping into a shared `useOcrProcessor` hook.
    - Implement a `direct-ocr` backend endpoint that skips request-specific validation for initial extraction.
    - Use an isolated "Payment Draft" state in `RequestCreate.tsx` to allow user review before persistence.
- **Consequences:** Ensures consistency between Quotation and Payment extraction logic. Reduces code duplication and enables a "fast-track" creation flow for payment-intensive workflows.

- **Date:** 2026-04-04
- **Status:** Accepted
- **Context:** Administrators adding or editing users often require clarity on the specific permissions associated with each system role to ensure correct assignment and internal compliance.
- **Decision:** Implement hover-activated tooltips for the role selection list in the User Management drawer.
    - **Implementation**:
        - Create a centralized `ROLE_DESCRIPTIONS` constant in `roles.ts` using `ROLES` keys as indices.
        - Utilize the system's `Tooltip` component and `Info` (circle-help) icons next to each role checkbox.
        - **Guardrail**: The structural `ROLES` constant MUST remain exported as the shared source of truth for logic to prevent architectural regressions (e.g., white screen).
- **Alternatives considered:** Static text under each role (rejected: adds too much vertical noise).
- **Consequences:** Improves administrative usability without cluttering the UI. Establishes a- **Tooltip**: Provides contextual help via standard information icons or trigger wrappers.
    - **API**: Supports `side` ('top', 'bottom', 'left', 'right') and `align` ('start', 'center', 'end') for precise placement control in restricted containers (e.g., Drawers).
    - **Default**: `side="top"`, `align="center"`.
**: Complete UI/UX refactor of the decision support panel in the Approval Center drawer.
- **Information Architecture**: Structured intelligence into three executive sections: `Destaques de Atenção`, `Visão Departamental`, and `Análise por Item`.
- **Item-Specific Intelligence Cards**: Implemented distinct, border-accented cards for each line item, featuring purchase frequency badges, price variation grids, and previous supplier highlights.
- **Executive Typography**: Applied `font-black` (900 weight) to metrics and `text-[9px]` uppercase tracking to labels for maximum readability and hierarchy.

### Fixed

- **DecisionInsightsPanel Types**: Expanded `DecisionAlertDto['level']` to include `'ERROR'` and `'DANGER'`, resolving TypeScript compilation errors and ensuring robust alert styling.
- **Alert System**: Compact row-based alerts with severity-specific icons and color-coded left borders.
- **Department Metrics**: Redesigned KPI grid focusing on monthly accumulation and relative budget impact.

## [v2.12.0] - 2026-04-01

### Changed

- **Approval Center UX Refinement**: Replaced the previous stacked master-detail layout with a high-efficiency right-side drawer/panel pattern.
- **Queue Context Preservation**: The approval queues remain visible on the left while the selected item detail opens in the side panel, allowing for better context during review.
- **Selection Visuals**: Implemented high-visibility selected states in the queue with a `12px` accent border and unique background highlight.
- **Workflow Automation**: Added "Auto-Select Next Item" logic that automatically loads the next pending item in the same queue after a successful approval action, significantly increasing throughput for approvers.
- **Responsive Drawer**: The detail panel adapts its width based on screen size (640px on desktop, 100% on mobile).

## [v2.11.5] - 2026-03-31

### Optimized

- **Quotation Management Performance**: Resolved Cartesian Explosion issue in `LineItemsController.GetLineItems` by applying `.AsSplitQuery()` to the related data hydration phase ($Attachments$, $StatusHistories$, $Quotations$).
- **Query Efficiency**: Improved screen load time from ~10s to <1s by eliminating redundant joins and suppressing `MultipleCollectionIncludeWarning`.

## [v2.11.4] - 2026-03-31

### Added

- **Quotation Save Confirmation**: Implemented a mandatory UX confirmation modal before saving or updating a quotation in the Buyer Workspace.
- **Contextual Messaging**: The confirmation message distinguishes between OCR-extracted data and manual entries to ensure user verification of automated results.
- **Frontend Foundation Alignment**: Reused the standard "Brutalist" `ApprovalModal` component to maintain UI/UX consistency across the portal.

## [2.10.4] - 2026-04-01

### Added

- **Guided UX Attention**: Novo padrão de atenção guiada para o Aprovador de Área. Ao carregar um pedido em status de aprovação, a seção de "Cotações Salvas" expande, rola e pulsa automaticamente para guiar o usuário.

### Changed

- **Default State**: A seção "ITENS DO PEDIDO" agora inicia colapsada por padrão para reduzir o ruído visual inicial.

### 6.2 Hooks

- **`useOcrProcessor`**: Shared hook for normalizing and mapping OCR results to request drafts. Used in both `RequestCreate.tsx` (Payment flow) and `BuyerItemsList.tsx` (Quotation flow).
- **`useFeedback`**: Centralized hook for managing top-level feedback messages (success, error, warning).

### Fixed

- **Regression Fix**: Corrigido erro onde o toggle de acordeão disparava o evento de sumit do formulário principal no `RequestEdit.tsx`.
- **Dirty State Precision**: O estado inicial do formulário agora considera auto-preenchimentos de sistema para evitar avisos falsos de "sem alterações".

## [2.10.3] - 2026-03-31

### Optimized

- **EF Core Query Audit**: Resolved non-deterministic First/FirstOrDefault warnings and Multiple Collection Include warnings (Cartesian Explosion) in core request and line item modules.
- **Deterministic Ordering**: Applied explicit `.OrderBy()` on all aggregate and lookup queries to ensure stable results across list views and dashboards.

## [v2.11.3] - 2026-03-31

### Optimized

- **EF Core Query Audit**: Resolved non-deterministic First/FirstOrDefault warnings and Multiple Collection Include warnings (Cartesian Explosion) in core request and line item modules.
- **Deterministic Ordering**: Applied explicit `.OrderBy()` on all aggregate and lookup queries to ensure stable results across list views and dashboards.
- **Query Splitting**: Integrated `.AsSplitQuery()` for high-complexity detail projections (Submit, Save, Delete, Cancel, Finalize) to eliminate performance degradation from broad Cartesian joins.

## [v2.11.2] - 2026-03-31

### Added

- **TOTAL FILTRADO KPI Trend (MoM)**: Implemented Month-over-Month (MoM) trend indicator comparing current Month-to-Date (MTD) vs. same period last month.
- **Trend Safety Logic**: Automatic neutral state ('Sem comparativo') when comparison is not meaningful.
- **Operational Data Dropdowns**: Simultaneously updated `RequestCreate` and `RequestEdit` line-item forms to explicitly restrict new dropdown selections to `Active` records only, while silently preserving and rendering historical IDs safely so old requests do not crash or blank out their deactivated units.
- **Request Form UX Refactoring**: Completely reorganized the Request Draft creation and editing forms (`RequestCreate.tsx` and `RequestEdit.tsx`) into three distinct semantic phases: General Details, Workflow Participants, and Financial Summary.
- **Sticky Layout Architecture (Fixed)**: Corrected critical CSS container bugs preventing sticky-scroll behavior natively. Resolved an invalid `calc(auto)` CSS token for the Sidebar, and stripped `transform: translateY()` attributes from `framer-motion` page wrappers that were inadvertently creating rigid containing blocks preventing the Form Action Bars from sticking to the viewport. Both the left Sidebar and top Action buttons now reliably dock during deep page scrolling.
- **Read-only Total Value**: The`Valor Total Estimado`field in the header is now strictly read-only, visually highlighted, and calculated directly from the line-item grid to prevent manual data-entry errors.
- **Strict Validations**: Integrated red inline semantic highlights enforcing required interaction for: `Tipo de Pedido`, `Comprador Atribuído`, `Aprovador de Área`, e `Aprovador Final`. Missing any triggers explicit anchor scrolling toward the error.
- **Required Field Enforcement**: Added HTML5`required` attributes and backend `[Required]` annotations for `Grau de Necessidade` and `Necessário Até`fields, preventing form submission without these values.

---

## [0.4.0] - 2026-02-26

### Changed

- **Urgency Consolidation**: Removed the semantically overlapping`Priority` concept from the workflow. The system now exclusively uses `NeedLevel`(Grau de Necessidade do Pedido) to express holistic Request urgency.
- **Item Level Triage**: Added a new numeric field `ItemPriority` (Prioridade do Item) to Line Items, allowing requisitioners to rank individual item fulfillment importance (e.g. 1 = highest).

- Dropped the `Priority` Master Data Entity, DbSets, EF Core configurations, and all DTO mappings from both Frontend and Backend Application scopes entirely to reduce cognitive load.

### Fixed

- **Master Data Active/Inactive Filtering**: Fixed a bug where deactivated/sunsetted Master Data records (e.g. Unit: 'CX') were still populating inside the React Select dropdowns during new operations. The Frontend API Service (`api.ts`) now successfully defaults to `includeInactive=false` for `getUnits`, `getCurrencies`, and `getNeedLevels`, guaranteeing that disabled records are definitively excluded from new interactions while remaining historically accurate on old saved requests.

---

## [0.3.1] - 2026-02-26

### Fixed

- **Line Item Validation Unmount Bug**: Fixed an issue in `RequestEdit.tsx` where attempting to save a line item with missing/invalid fields would trigger the generic page-level error state, completely unmounting the line item child form and masquerading as a page redirect. The inline validation now persists natively on the screen.
- **Validation Field Casing Bug**: Upgraded`renderFieldError`throughout the React forms (`RequestCreate` and `RequestEdit`) to be fully case-insensitive when parsing ASP.NET`ValidationProblemDetails`dictionaries. This ensures that validation highlights (red borders and text) correctly bind to inputs regardless of camelCase vs PascalCase properties returned by the server.
- **Request List Status Separation**: Refactored the generic "Estágio Atual" column in `RequestsList.tsx` into two distinct columns: "Status do Pedido" (displaying the formal Workflow DB status badge) and "Atribuição Atual" (displaying the current responsible actors, e.g. Comprador, Aprovador).

---

## [0.3.0] - 2026-02-26

### Added

- **Master Data UI Maintenance**: Enhanced `/settings/master-data` with table state badges ("Ativo"/"Inativo") and toggle action buttons ("Desativar"/"Ativar") enabling Soft Delete operations without touching the database via SQL.
- **Master Data Duplicate Prevention**: Enforced Unique Indexes natively inside`ApplicationDbContext` for `Unit`,`Currency`, and the new`NeedLevel` tables. The `LookupsController` catches `DbUpdateException` and strictly prevents exact code duplication, showing `409 Conflict`gracefully.
- **Need Level (Grau de Necessidade)**: Created `NeedLevel` DB entity and API endpoints. Connected to the `RequestCreate` and `RequestEdit` forms, injecting the selected state seamlessly into the parent request record footprint.
- **Continuous Creation Flow**: Modified the routing logic inside`RequestCreate.tsx`. Instead of redirecting to the requests list after Draft creation, the UI automatically transitions`navigate('/requests/{id}/edit')`, exposing the Line Items section immediately.

### Changed

- **List Endpoints Default Behavior**:`LookupsController` GET mappings natively filter out inactive records. To see all records (like inside the Master Data settings page), the UI now passes `?includeInactive=true`.
- **Delete Behavior**: Eliminated all physical `DELETE` statements from Master Data flows, replacing them entirely with a `toggle-active` payload logic.

### Fixed

- **Soft Delete Mapping Constraint**: Ensured legacy transactions bound to presently inactive enumerations continue rendering properly because the foreign key remains structurally intact.
- **Concurrent DB Exceptions on Line Items (Frontend)**: Fixed a nested DOM`<form>` overlap in `RequestEdit.tsx`that caused simultaneous save submissions, triggering EF Core errors.
- **UnitId Integer Mapping**: Corrected the logic in the Line Item modal to correctly map String Codes ("EA") back to Integer lookups (`Units.Id`) before pushing to the API.
- **DbUpdateConcurrencyException (Backend)**: Fixed an EF Core tracking issue in`RequestsController.AddLineItem` where ostensibly new Line Items with pre-assigned Guids were erroneously dispatched as `UPDATE` statements instead of `INSERT`statements, causing 0 rows affected exceptions.

---

## [0.1.0] - 2026-02-25

### Added

- Initial project documentation scaffold in`docs/`
- `PROJECT_OVERVIEW.md`
- `VERSION.md`
-`CHANGELOG.md`

### Changed

- N/A

### Fixed

- N/A

### Notes

- Initial baseline version for project documentation structure

