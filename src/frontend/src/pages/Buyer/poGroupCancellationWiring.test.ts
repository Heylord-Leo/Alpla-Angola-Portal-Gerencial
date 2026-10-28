import { describe, it, expect } from 'vitest';
import workspaceSrc from './BuyerRequestWorkspace.tsx?raw';
import dialogSrc from '../../components/Buyer/CancelPoGroupDialog.tsx?raw';
import financeListSrc from '../Finance/FinancePaymentsList.tsx?raw';
import financeModalSrc from '../../components/modals/FinanceActionModal.tsx?raw';
import reviewPanelSrc from '../../components/Finance/PoCancellationReviewPanel.tsx?raw';
import registerPoSrc from '../../components/RegisterPoModal.tsx?raw';
import correctPoSrc from '../../components/CorrectPoModal.tsx?raw';
import apiSrc from '../../lib/api.ts?raw';
import groupSummarySrc from '../Requests/components/RequestGroupDisplaySummary.tsx?raw';
import requestEditSrc from '../Requests/RequestEdit.tsx?raw';
import printModelSrc from '../Requests/components/print/requestPrintModel.ts?raw';
import financeHistorySrc from '../Finance/FinanceHistory.tsx?raw';

/**
 * Phase 1 P.O. group cancellation — source-level wiring (no DOM runtime in this suite). Proves the surfaces are
 * connected to the backend contract (docs/PO_GROUP_CANCELLATION.md): routes, row-version concurrency fields, the
 * ECF declaration fields, the Finance return classification and the two register-po error codes.
 */
describe('Buyer Workspace — "Grupos P.O." tab + header "Cancelar P.O."', () => {
    it('registers the groups tab and renders TabGroups from ws.poGroups with the tour anchor', () => {
        expect(workspaceSrc).toMatch(/\{tab === GROUPS_TAB && \(\s*<TabGroups/);
        expect(workspaceSrc).toContain('data-tour="buyer-workspace-po-groups"');
        expect(workspaceSrc).toMatch(/const groups = ws\.poGroups \?\? \[\];/);
    });
    it('header "Cancelar P.O." is gated by the server next-action (CANCEL_PO_GROUP) through resolveCancelPoGroupTarget and sits inside the action group', () => {
        const groupStart = workspaceSrc.indexOf('data-tour="buyer-workspace-actions"');
        const groupEnd = workspaceSrc.indexOf('{/* Meta grid */}');
        const group = workspaceSrc.slice(groupStart, groupEnd);
        expect(group).toContain('data-tour="buyer-workspace-cancel-po"');
        expect(group).toMatch(/cancelTarget\.kind !== 'NONE' &&/);
        expect(group).toMatch(/if \(single\) openCancelForGroup\(single\); else setTab\(GROUPS_TAB\);/);
        expect(workspaceSrc).toMatch(/resolveCancelPoGroupTarget\(ws\.nextActions, poGroups\)/);
    });
    it('group cards use the server decisions only (canCorrectPo / canCancel / cancelBlockMessage / priorEcfConfirmation / cancellation.actions)', () => {
        expect(workspaceSrc).toMatch(/\{g\.canCorrectPo && \(/);
        expect(workspaceSrc).toMatch(/disabled=\{busy \|\| !g\.canCancel\}/);
        expect(workspaceSrc).toMatch(/g\.cancelBlockMessage \|\| 'Cancelamento indisponível para este grupo'/);
        expect(workspaceSrc).toMatch(/priorEcfBlockMessage\(g\.priorEcfConfirmation\)/);
        expect(workspaceSrc).toMatch(/lastFinanceReturnLabel\(g\.lastFinanceReturn\)/);
        expect(workspaceSrc).toMatch(/c\.actions\.canUpdateDeclaration &&/);
        expect(workspaceSrc).toMatch(/c\.actions\.canResendNotification &&/);
        // v2.249.2 — registration and correction both run on the request page (per-group REGISTRAR / CORRIGIR actions);
        // no detour through the Para Minha Ação list any more.
        expect(workspaceSrc).not.toMatch(/buildPoCorrectionDeepLink/);
        expect(workspaceSrc).toMatch(/onRegister=\{\(\) => navigate\(`\/requests\/\$\{ws\.requestId\}`/);
        expect(workspaceSrc).toMatch(/onCorrect=\{\(\) => navigate\(`\/requests\/\$\{ws\.requestId\}`/);
        // Declaration vs Finance confirmation are separate blocks with the approved headings.
        expect(workspaceSrc).toContain('Declaração do comprador sobre a ECF');
        expect(workspaceSrc).toContain('Confirmação de Finanças');
        expect(workspaceSrc).toContain('financeRecipientsResolvedCount');
        expect(workspaceSrc).toMatch(/c\.notificationWarning &&/);
    });
    it('D13: the cancelled-group card renders the Finance e-mail policy outcome and the per-recipient delivery list with status chips; resend is prominent when attention is needed', () => {
        expect(workspaceSrc).toMatch(/const policy = describeFinanceEmailPolicy\(c\);/);
        expect(workspaceSrc).toContain('data-tour="buyer-workspace-po-group-notification"');
        expect(workspaceSrc).toMatch(/const delivery = c\.delivery \?\? \[\];/);
        expect(workspaceSrc).toContain('data-tour="buyer-workspace-po-group-delivery"');
        expect(workspaceSrc).toMatch(/delivery\.map\(\(d, i\) => \{\s*const chip = deliveryChip\(d\.status\);/);
        expect(workspaceSrc).toMatch(/d\.status === 'FAILED' && <span[^>]*>tentativa \{d\.retryCount\}/);
        expect(workspaceSrc).toMatch(/\(d\.status === 'DEAD_LETTER' \|\| d\.status === 'EXPIRED'\) && d\.lastError/);
        expect(workspaceSrc).toMatch(/c\.buyerEmailQueued \? ' E-mail ao comprador enfileirado\.' : ''/);
        expect(workspaceSrc).toMatch(/c\.actions\.canResendNotification && \(resendNeedsAttention\(c\) \? \(/);
        expect((workspaceSrc.match(/data-tour="buyer-workspace-po-group-resend"/g) || []).length).toBe(2);
        // Finance row shows the same policy outcome.
        expect(reviewPanelSrc).toMatch(/item\.financeEmailPolicyOutcome && \(\(\) => \{\s*const policy = describeFinanceEmailPolicy\(item\);/);
        expect(reviewPanelSrc).toContain('data-tour="finance-po-cancellation-email-policy"');
    });
    it('mounts CancelPoGroupDialog in both modes and the resend confirmation; success goes through the silent re-read (afterMutation → refreshAfterMutation)', () => {
        expect(workspaceSrc).toMatch(/<CancelPoGroupDialog\s+mode=\{cancelDialog\.mode\}/);
        expect(workspaceSrc).toMatch(/setCancelDialog\(\{ mode: 'cancel', group \}\)/);
        expect(workspaceSrc).toMatch(/setCancelDialog\(\{ mode: 'declaration', group: g \}\)/);
        expect(workspaceSrc).toMatch(/api\.requests\.resendPoGroupCancellationNotification\(ws\.requestId, g\.id\)/);
        expect(workspaceSrc).toMatch(/title="Reenviar notificação de cancelamento"/);
        expect(workspaceSrc).toMatch(/afterMutation\(r\.notificationWarning \? `\$\{r\.message\} \$\{r\.notificationWarning\}` : r\.message\)/);
        expect(workspaceSrc).toMatch(/const afterMutation = \(msg: string\) => \{ setActiveHost\(null\); flash\(msg\); void refreshAfterMutation\(\); \};/);
    });
    it('batches tab shows released items and per-currency totals; header meta shows active/historical approved totals', () => {
        expect(workspaceSrc).toMatch(/batchReleasedItemsLabel\(b\)/);
        expect(workspaceSrc).toMatch(/batchTotalsLines\(b\)\.map/);
        expect(workspaceSrc).toContain('label="Aprovado ativo"');
        expect(workspaceSrc).toContain('label="Aprovado histórico"');
        expect(workspaceSrc).toMatch(/headerApprovedTotals\(ws\.activeApprovedTotals\)/);
    });
});

describe('CancelPoGroupDialog — payload contract', () => {
    it('cancel mode posts kind/reasonCode/reason + the ECF declaration fields + expectedGroupRowVersion = group.rowVersion', () => {
        expect(dialogSrc).toMatch(/api\.requests\.cancelPoGroup\(requestId, group\.id, \{/);
        expect(dialogSrc).toMatch(/kind: form\.kind,/);
        expect(dialogSrc).toMatch(/reasonCode: form\.reasonCode,/);
        expect(dialogSrc).toMatch(/reason: form\.reason\.trim\(\),/);
        expect(dialogSrc).toMatch(/ecfStatus: ecfRequired \? form\.ecfStatus : undefined,/);
        expect(dialogSrc).toMatch(/ecfReference: ecfRequired && form\.ecfStatus === 'DECLARED_CANCELLED' \? form\.ecfReference\.trim\(\) : undefined,/);
        expect(dialogSrc).toMatch(/ecfDeclarationNote: ecfRequired && form\.ecfDeclarationNote\.trim\(\) \? form\.ecfDeclarationNote\.trim\(\) : undefined,/);
        expect(dialogSrc).toMatch(/expectedGroupRowVersion: group\.rowVersion \?\? undefined,/);
    });
    it('declaration mode PUTs ecf-declaration with expectedRowVersion = cancellation.rowVersion', () => {
        expect(dialogSrc).toMatch(/api\.requests\.updatePoGroupEcfDeclaration\(requestId, group\.id, \{/);
        expect(dialogSrc).toMatch(/expectedRowVersion: cancellation\?\.rowVersion \?\? undefined,/);
    });
    it('ECF section only when financeConfirmationWouldBeRequired, with the three declarable options, the declaration wording and the consequences list', () => {
        expect(dialogSrc).toMatch(/const ecfRequired = mode === 'cancel' \? group\.financeConfirmationWouldBeRequired : true;/);
        expect(dialogSrc).toMatch(/\{ecfRequired && \(/);
        expect(dialogSrc).toContain("['PENDING_EXTERNAL', 'DECLARED_CANCELLED', 'UNKNOWN']");
        expect(dialogSrc).toContain('sua declaração');
        expect(dialogSrc).toContain('não verifica');
        expect(dialogSrc).toContain('Finanças confirma separadamente');
        expect(dialogSrc).toMatch(/describeCancellationConsequences\(form, \{/);
        expect(dialogSrc).toMatch(/validateCancelPoGroupForm\(form, \{ financeConfirmationWouldBeRequired: ecfRequired \}\)/);
        // Server detail is shown; the dialog never claims the Portal cancelled the ECF.
        expect(dialogSrc).toMatch(/err\?\.details\?\.detail \|\| err\?\.message/);
        expect(dialogSrc).not.toMatch(/o Portal (anulou|cancelou|verificou)/i);
    });
});

describe('Finance — return classification + ECF confirmation panel', () => {
    it('FinanceActionModal RETURN has the "Tipo de devolução" radio (PO_CORRECTION default) and the optional reason select, forwarded in the payload', () => {
        expect(financeModalSrc).toMatch(/useState<FinanceReturnKindOption>\('PO_CORRECTION'\)/);
        expect(financeModalSrc).toContain('Tipo de devolução');
        expect(financeModalSrc).toContain("'Corrigir P.O.'");
        expect(financeModalSrc).toContain("'Pedido cancelado — não corrigir'");
        expect(financeModalSrc).toMatch(/FINANCE_RETURN_REASON_CODES\.map/);
        expect(financeModalSrc).toMatch(/\? \{ date, notes, file, amount, returnKind, returnReasonCode: returnReasonCode \|\| undefined \}/);
    });
    it('FinancePaymentsList forwards returnKind/returnReasonCode to api.finance.returnForAdjustment and renders PoCancellationReviewPanel above the obligations', () => {
        expect(financeListSrc).toMatch(/api\.finance\.returnForAdjustment\(actionModal\.requestId, payload\.notes, actionModal\.groupId \?\? undefined, payload\.returnKind, payload\.returnReasonCode\)/);
        const panelAt = financeListSrc.indexOf('<PoCancellationReviewPanel');
        const obligationsAt = financeListSrc.indexOf('{/* ── Obligation containers ── */}');
        expect(panelAt).toBeGreaterThan(-1);
        expect(panelAt).toBeLessThan(obligationsAt);
        expect(financeListSrc).toMatch(/import \{ PoCancellationReviewPanel \} from '\.\.\/\.\.\/components\/Finance\/PoCancellationReviewPanel'/);
    });
    it('the review panel fetches status=OPEN, badges pending+rejected, separates the Buyer declaration, gates on canReview and warns on zero recipients', () => {
        expect(reviewPanelSrc).toMatch(/api\.finance\.listPoCancellations\(\{ status: 'OPEN', plantId \}\)/);
        expect(reviewPanelSrc).toMatch(/data\.pendingCount \+ data\.rejectedCount/);
        expect(reviewPanelSrc).toContain('Cancelamentos a confirmar');
        expect(reviewPanelSrc).toContain('Declaração do comprador');
        expect(reviewPanelSrc).toContain('Confirmar anulação');
        expect(reviewPanelSrc).toContain('Rejeitar declaração');
        expect(reviewPanelSrc).toMatch(/\{item\.canReview \? \(/);
        expect(reviewPanelSrc).toMatch(/item\.reviewBlockReason \|\|/);
        expect(reviewPanelSrc).toMatch(/item\.financeRecipientsResolvedCount === 0 &&/);
        expect(reviewPanelSrc).toMatch(/api\.finance\.confirmPoCancellation\(item\.cancellationId, \{ note: note\.trim\(\) \|\| undefined, expectedRowVersion: item\.rowVersion \?\? undefined \}\)/);
        expect(reviewPanelSrc).toMatch(/api\.finance\.rejectPoCancellation\(item\.cancellationId, \{ note: note\.trim\(\), expectedRowVersion: item\.rowVersion \?\? undefined \}\)/);
        expect(reviewPanelSrc).toMatch(/validateFinanceRejectionNote\(note\)/);
    });
    it('api client exposes every route of the contract', () => {
        expect(apiSrc).toContain('/po-groups/${groupId}/cancel`');
        expect(apiSrc).toContain('/po-groups/${groupId}/cancellation`');
        expect(apiSrc).toContain('/po-groups/${groupId}/cancellation/eligibility`');
        expect(apiSrc).toContain('/po-groups/${groupId}/cancellation/ecf-declaration`');
        expect(apiSrc).toContain('/po-groups/${groupId}/cancellation/resend-notification`');
        expect(apiSrc).toContain('/api/v1/finance/po-cancellations${qs ? `?${qs}` : \'\'}`');
        expect(apiSrc).toContain('/api/v1/finance/po-cancellations/${cancellationId}/confirm`');
        expect(apiSrc).toContain('/api/v1/finance/po-cancellations/${cancellationId}/reject`');
        expect(apiSrc).toMatch(/returnForAdjustment: async \(id: string, notes: string, requestPoGroupId\?: string, returnKind\?: string, returnReasonCode\?: string\)/);
        expect(apiSrc).toMatch(/previousPoNumberReuseConfirmed\?: boolean, previousPoNumberReuseComment\?: string/);
    });
});

describe('Register / correct P.O. — PRIOR_ECF_CONFIRMATION_PENDING and PREVIOUS_PO_NUMBER_REUSED', () => {
    it('RegisterPoModal classifies errors through the shared helper, blocks on prior ECF (no retry) and resubmits the same payload with the reuse fields', () => {
        expect(registerPoSrc).toMatch(/const decision = classifyRegisterPoError\(err\);/);
        expect(registerPoSrc).toMatch(/if \(decision\.kind === 'PRIOR_ECF_CONFIRMATION_PENDING'\) \{\s*setPriorEcfBlock\(decision\.message\);\s*return;/);
        expect(registerPoSrc).toMatch(/if \(decision\.kind === 'PREVIOUS_PO_NUMBER_REUSED'\) \{\s*setPreviousPoReuse\(\{ message: decision\.message \}\);\s*return;/);
        expect(registerPoSrc).toMatch(/if \(priorEcfBlock\) return;/);
        expect(registerPoSrc).toMatch(/validatePreviousPoReuse\(reuseConfirmed, reuseComment\)/);
        expect(registerPoSrc).toMatch(/previousPoReuse \? withPreviousPoReuse\(basePayload, reuseComment\) : basePayload/);
        expect(registerPoSrc).toContain('data-tour="register-po-prior-ecf-block"');
        expect(registerPoSrc).toContain('data-tour="register-po-previous-number-reuse"');
        expect(registerPoSrc).toContain('REGISTER_PO_ERROR_MESSAGES.reuseCheckbox');
        expect(registerPoSrc).toMatch(/disabled=\{isBlocked \|\| !!priorEcfBlock \|\| processing/);
        // Legacy branches preserved (now read through the classifier).
        expect(registerPoSrc).toMatch(/decision\.kind === 'DUPLICATE_PO'/);
        expect(registerPoSrc).toMatch(/decision\.kind === 'OCR_MISMATCH'/);
        // Upload happens once (the refused attempt already uploaded the same file).
        expect(registerPoSrc).toMatch(/if \(!\(previousPoReuse && uploadedFileRef\.current === file\)\) \{/);
    });
    it('CorrectPoModal does the same via lib/correctPoSubmission (previousPoNumberReuseComment + skipUpload)', () => {
        expect(correctPoSrc).toMatch(/const decision = classifyRegisterPoError\(err\);/);
        expect(correctPoSrc).toMatch(/if \(decision\.kind === 'PRIOR_ECF_CONFIRMATION_PENDING'\) setPriorEcfBlock\(decision\.message\);/);
        expect(correctPoSrc).toMatch(/else if \(decision\.kind === 'PREVIOUS_PO_NUMBER_REUSED'\) \{[^]*?uploadedFileRef\.current = file;[^]*?setPreviousPoReuse\(\{ message: decision\.message \}\);/);
        expect(correctPoSrc).toMatch(/previousPoNumberReuseComment: previousPoReuse \? reuseComment : undefined,/);
        expect(correctPoSrc).toMatch(/skipUpload: !!previousPoReuse && uploadedFileRef\.current === file,/);
        expect(correctPoSrc).toContain('data-tour="correct-po-prior-ecf-block"');
        expect(correctPoSrc).toContain('data-tour="correct-po-previous-number-reuse"');
        expect(correctPoSrc).toMatch(/disabled=\{!!priorEcfBlock \|\| processing/);
    });
});

describe('Request page + history labels', () => {
    it('CANCELLED groups render the "P.O. cancelada" chip and a WAITING_PO group blocked by a prior ECF shows the notice', () => {
        expect(groupSummarySrc).toMatch(/export function RequestGroupCancellationChips/);
        expect(groupSummarySrc).toMatch(/filter\(g => g\.status === 'CANCELLED'\)/);
        expect(groupSummarySrc).toMatch(/g\.status === 'WAITING_PO' && g\.priorEcfConfirmation\?\.blocked/);
        expect(groupSummarySrc).toContain('CANCELLED_GROUP_CHIP_LABEL');
        expect(requestEditSrc).toMatch(/<RequestGroupCancellationChips poGroups=\{poGroups \|\| \[\]\} \/>/);
    });
    it('history label maps know the eight new actions', () => {
        for (const code of ['PO_GROUP_CANCELLED', 'PO_GROUP_ITEMS_RELEASED', 'PO_GROUP_ITEMS_CLOSED', 'PO_CANCELLATION_ECF_DECLARED', 'PO_CANCELLATION_CONFIRMED', 'PO_CANCELLATION_REJECTED', 'PO_CANCELLATION_NOTIFICATION_RESENT', 'PO_NUMBER_REUSED_AFTER_CANCELLATION']) {
            expect(printModelSrc).toContain(`${code}:`);
            expect(financeHistorySrc).toContain(`case '${code}':`);
        }
        expect(printModelSrc).toContain("PO_GROUP_CANCELLED: 'P.O. cancelada'");
        expect(financeHistorySrc).toContain("label: 'Anulação da ECF confirmada por Finanças'");
    });
});

// v2.249.2 — Workspace "Grupos P.O." tab: first registration vs correction, kind-aware guidance, delivery refresh.
describe('Workspace group cards — v2.249.2 (register vs correct, return-kind guidance, delivery refresh)', () => {
    it('"Registrar P.O." is driven by canRegisterPo (WAITING_PO) and "Corrigir P.O." by canCorrectPo (WAITING_PO_CORRECTION) — separate flags', () => {
        expect(workspaceSrc).toMatch(/\{g\.canRegisterPo && \(/);
        expect(workspaceSrc).toMatch(/data-testid="group-register-po"/);
        expect(workspaceSrc).toMatch(/<FileText size=\{12\} \/> Registrar P\.O\./);
        expect(workspaceSrc).toMatch(/\{g\.canCorrectPo && \(/);
        expect(workspaceSrc).toMatch(/<Edit2 size=\{12\} \/> Corrigir P\.O\./);
    });
    it('a blocked replacement P.O. disables "Registrar P.O." and shows the reason as readable text (not a transient message)', () => {
        expect(workspaceSrc).toMatch(/disabled=\{busy \|\| !!priorBlock\} onClick=\{\(\) => !priorBlock && onRegister\(g\)\}/);
        expect(workspaceSrc).toMatch(/data-testid="group-register-blocked"/);
        expect(workspaceSrc).toContain('Registo da P.O. indisponível: {priorBlock}');
    });
    it('the Finance-return banner adds guidance from the STORED kind (financeReturnGuidance), only for live groups', () => {
        expect(workspaceSrc).toMatch(/\{!cancelled && <div data-testid="finance-return-guidance"[^>]*>\{financeReturnGuidance\(g\.lastFinanceReturn\?\.kind\)\}<\/div>\}/);
    });
    it('the delivery list carries a read timestamp, the background-processing note and an "Atualizar estado" re-read that never re-sends', () => {
        expect(workspaceSrc).toMatch(/data-testid="delivery-refresh"/);
        expect(workspaceSrc).toContain('o envio é processado em segundo plano (≈ 10 s por ciclo); “Pendente” logo após o cancelamento é normal.');
        expect(workspaceSrc).toMatch(/onClick=\{onRefresh\}[^>]*title="Voltar a ler o estado de entrega no servidor \(não reenvia e-mails\)"/);
        expect(workspaceSrc).toMatch(/onRefresh=\{\(\) => void refreshAfterMutation\(\)\}/);
        expect(workspaceSrc).toMatch(/readAt=\{wsReadAt\}/);
        expect(workspaceSrc).toMatch(/setWs\(w\); setWsReadAt\(new Date\(\)\);/);
    });
    it('"Cotações & Documentos" flags quotations whose items need an explicit reuse authorization (nothing reused silently)', () => {
        expect(workspaceSrc).toMatch(/data-testid="quotation-reuse-blocked"/);
        expect(workspaceSrc).toContain('Reutilização requer autorização ({q.reuseBlockedItemCount})');
        expect(workspaceSrc).toMatch(/data-testid="quotation-reuse-authorized"/);
    });
    it('Editar/Excluir visibility uses the SERVER quotation window (scalar OR item-level), not only the scalar mirror', () => {
        expect(workspaceSrc).toMatch(/canManageWorkspaceQuotations\(ws\?\.requestStatusCode, user\?\.roles, ROLES\.BUYER, ws\?\.buyerId, user\?\.id, ws\?\.quotationWindowOpen\)/);
    });
});
