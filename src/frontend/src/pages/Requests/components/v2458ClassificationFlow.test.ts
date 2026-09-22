import { describe, it, expect } from 'vitest';
// v2.245.8 — structural guards for the drawer classification / final-invoice / completion-guidance
// wiring. Node-env vitest (no jsdom/RTL in this repository): behavior is unit-tested at the consumer
// boundary in src/lib/operationInvoiceView.test.ts and src/lib/operationInvoiceSubmit.test.ts; these
// guards prove the real components consume those units and the new endpoints.
import edit from '../RequestEdit.tsx?raw';
import section from './OperationInvoiceSection.tsx?raw';
import completion from './RequestCompletionSection.tsx?raw';
import panel from './RequestStatusActionPanels.tsx?raw';
import classificationModal from '../../../components/requests/OperationInvoiceClassificationModal.tsx?raw';
import registerModal from '../../../components/requests/OperationInvoiceRegisterModal.tsx?raw';
import apiSrc from '../../../lib/operationInvoiceApi.ts?raw';
import receivingOp from '../../Receiving/ReceivingOperation.tsx?raw';
import printModel from './print/requestPrintModel.ts?raw';

describe('drawer classification CTA (Fatura Final — Cobertura)', () => {
  it('the group card offers "Classificar Documento de Origem" only while classification is pending and only to deciders (Finance/SysAdmin)', () => {
    expect(section).toMatch(/const classificationPending = isClassificationPending\(obligation\)/);
    expect(section).toMatch(/\{classificationPending \? \(/);
    expect(section).toMatch(/\{canDecide \? \(/);
    expect(section).toMatch(/Classificar Documento de Origem/);
    expect(section).toMatch(/A classificação é efetuada pelo Financeiro ou pela Administração do Sistema\./);
    // canDecide = (Finance || Admin) && lifecycle open — the backend role gate mirrored
    expect(section).toMatch(/const canDecide = \(isFinance \|\| isAdmin\) && lifecycleOpen/);
  });

  it('the misleading "ativação controlada" hint no longer shows for an unclassified group (only for classified groups without expected total)', () => {
    expect(section).toMatch(/\) : !view\.hasExpected && \(/);
  });

  it('classification opens the dedicated modal and, on success, refreshes coverage and notifies the host', () => {
    expect(section).toMatch(/onClassify=\{\(\) => setClassifyGroup\(obligation\)\}/);
    expect(section).toMatch(/<OperationInvoiceClassificationModal/);
    expect(section).toMatch(/onClassified=\{\(\) => \{\s*setClassifyGroup\(null\);\s*void refresh\(\);\s*onObligationsChanged\?\.\(\);\s*\}\}/);
    expect(section).toMatch(/requestTypeCode=\{requestTypeCode\}/);
  });
});

describe('classification modal', () => {
  it('reuses the SAME document-type field and option set as the origin screens', () => {
    expect(classificationModal).toMatch(/import \{ SourceDocumentTypeField \} from '\.\/SourceDocumentTypeField'/);
    expect(classificationModal).toMatch(/<SourceDocumentTypeField\s*\n?\s*context=\{context\}/);
    expect(classificationModal).toMatch(/requestTypeCode === 'PAYMENT' \? 'PAYMENT_REQUEST' : 'QUOTATION_MANAGEMENT'/);
  });

  it('explains the operational consequence from the shared explanations and requires a meaningful justification', () => {
    expect(classificationModal).toMatch(/documentTypeExplanations\(context\)\.find\(e => e\.value === key\)/);
    expect(classificationModal).toMatch(/O que será exigido:/);
    expect(classificationModal).toMatch(/Valor esperado da fatura final:/);
    expect(classificationModal).toMatch(/justification\.trim\(\)\.length < RECONCILIATION_JUSTIFICATION_MIN_LENGTH/);
    expect(classificationModal).toMatch(/const canSubmit = !!normalizeDocumentType\(type\) && !justificationTooShort && !saving/);
    expect(classificationModal).toMatch(/disabled=\{!canSubmit\}/);
  });

  it('submits through the new endpoint and surfaces backend field errors / typed messages without closing', () => {
    expect(classificationModal).toMatch(/operationInvoiceApi\.classifyGroup\(requestId, obligation\.groupId, \{/);
    expect(classificationModal).toMatch(/if \(err instanceof ApiError && err\.fieldErrors\) setFieldErrors\(err\.fieldErrors\)/);
    expect(classificationModal).toMatch(/mapOperationInvoiceError\(err\)/);
    expect(classificationModal).toMatch(/onClassified\(\);/);
  });

  it('the API client targets the group-scoped classification route', () => {
    expect(apiSrc).toMatch(/\/api\/v1\/requests\/\$\{requestId\}\/po-groups\/\$\{groupId\}\/operation-invoice-classification/);
    expect(apiSrc).toMatch(/preflightCreate: async \(requestId: string\)/);
    expect(apiSrc).toMatch(/\$\{base\(requestId\)\}\/preflight/);
  });
});

describe('impossible invoice actions', () => {
  it('"Registrar Fatura Final" renders only with a classified, allocatable obligation; otherwise an explanatory blocker', () => {
    expect(section).toMatch(/const canRegisterInvoice = useMemo\(\(\) => hasRegistrableObligation\(relevantObligations\)/);
    expect(section).toMatch(/\{canWrite && canRegisterInvoice && \(/);
    expect(section).toMatch(/\{canWrite && !canRegisterInvoice && classificationPendingCount > 0 && \(/);
    expect(section).toMatch(/data-testid="register-blocked-by-classification"/);
    expect(section).toMatch(/\{REGISTER_BLOCKED_BY_CLASSIFICATION\}/);
  });
});

describe('orphan-upload prevention in the register modal', () => {
  it('the backend preflight is verified BEFORE the upload, through the serialized submitter', () => {
    expect(registerModal).toMatch(/const submitRef = useRef\(createInvoiceSubmitter\(\)\)/);
    expect(registerModal).toMatch(/preflight: mode === 'create'\s*\n?\s*\? \(\) => operationInvoiceApi\.preflightCreate\(requestId\)/);
    expect(registerModal).toMatch(/upload: uploadEvidence,/);
    // the only upload call site lives inside uploadEvidence, which the orchestrator invokes after the preflight
    expect(registerModal.match(/api\.attachments\.upload\(/g)?.length).toBe(1);
    const uploadIdx = registerModal.indexOf('const uploadEvidence');
    const submitIdx = registerModal.indexOf('await submitRef.current({');
    expect(uploadIdx).toBeGreaterThan(-1);
    expect(submitIdx).toBeGreaterThan(uploadIdx);
  });

  it('a create failure after upload retains the attachment id and the retry reuses it (no second upload)', () => {
    expect(registerModal).toMatch(/const retainedAttachmentRef = useRef<string \| null>\(null\)/);
    expect(registerModal).toMatch(/if \(outcome\.stage === 'create'\) \{[\s\S]{0,400}retainedAttachmentRef\.current = outcome\.attachmentId;/);
    expect(registerModal).toMatch(/\{ retainedAttachmentId: retainedAttachmentRef\.current \}/);
    // the restored own upload is announced as reused (never uploaded again)
    expect(registerModal).toMatch(/Ficheiro já carregado nesta sessão: \$\{own\.fileName\}[^`]*Será reutilizado — não é carregado novamente\./);
  });

  it('recovery is server-owned: on open the modal re-reads the unclaimed uploads; only THIS session\'s own upload is restored', () => {
    expect(registerModal).toMatch(/sortRecoverableCandidates\(await operationInvoiceApi\.listUnclaimedAttachments\(requestId\)\)/);
    expect(registerModal).toMatch(/const own = pickResumableAttachment\(list, retainedAttachmentRef\.current\)/);
    expect(registerModal).toMatch(/retainedAttachmentRef\.current = own\?\.attachmentId \?\? null/);
    expect(registerModal).toMatch(/setSelectedUpload\(own\)/);
    // discovered uploads become CANDIDATES (never selected automatically)
    expect(registerModal).toMatch(/setCandidates\(list\.filter\(c => c\.attachmentId !== own\?\.attachmentId\)\)/);
    expect(registerModal).toMatch(/data-testid="recoverable-uploads"/);
    expect(registerModal).toMatch(/Nenhum é usado automaticamente/);
    expect(registerModal).toMatch(/\{formatUtcTimestampDate\(c\.uploadedAtUtc\)\}\{c\.uploadedByName \? ` · \$\{c\.uploadedByName\}` : ''\}/);
    // explicit choice per candidate
    expect(registerModal).toMatch(/onClick=\{\(\) => reuseCandidate\(c\)\}/);
    expect(registerModal).toMatch(/onClick=\{\(\) => void releaseUpload\(c\.attachmentId\)\}/);
    // only a SELECTED upload satisfies the "file required" rule — a mere candidate never does
    expect(registerModal).toMatch(/if \(needsNewFile && !file && mode === 'create' && !retainedAttachmentRef\.current\)/);
    expect(registerModal).toMatch(/const reuseCandidate = \(candidate: OperationInvoiceUnclaimedAttachmentDto\) => \{\s*retainedAttachmentRef\.current = candidate\.attachmentId;/);
    expect(apiSrc).toMatch(/\$\{base\(requestId\)\}\/unclaimed-attachments/);
  });

  it('choosing another file (or "Descartar") EXPLICITLY releases the selected upload on the server — never merely forgets it', () => {
    expect(registerModal).toMatch(/onChange=\{e => \{[\s\S]{0,300}void handleLocalFileChosen\(e\.target\.files\?\.\[0\] \?\? null\);/);
    expect(registerModal).toMatch(/const handleLocalFileChosen = async \(chosen: File \| null\) => \{\s*const selected = retainedAttachmentRef\.current;\s*if \(selected\) \{\s*const outcome = await releaseUpload\(selected\);/);
    // an unresolved (failed) release refuses the new file instead of forgetting the upload
    expect(registerModal).toMatch(/if \(outcome === 'failed'\) \{[\s\S]{0,400}setFile\(null\);/);
    expect(registerModal).toMatch(/releasePreviousUpload\(\s*\(id\) => operationInvoiceApi\.releaseUnclaimedAttachment\(requestId, id\), attachmentId, isClaimedError\)/);
    expect(registerModal).toMatch(/mapOperationInvoiceError\(err\)\.code === 'OPERATION_INVOICE_ATTACHMENT_CLAIMED'/);
    // every release outcome refreshes server truth
    expect(registerModal).toMatch(/await refreshCandidates\(\);\s*return outcome;/);
    expect(registerModal).toMatch(/Descartar ficheiro carregado/);
    expect(apiSrc).toMatch(/\$\{base\(requestId\)\}\/attachments\/\$\{attachmentId\}\/release/);
  });

  it('a busy outcome (double click / race) is silently ignored and success clears the retained id', () => {
    expect(registerModal).toMatch(/if \(outcome\.stage === 'busy'\) return;/);
    expect(registerModal).toMatch(/if \(outcome\.ok\) \{\s*retainedAttachmentRef\.current = null;\s*setSelectedUpload\(null\);\s*onSaved\(\);/);
    // a create failure after upload refreshes server truth (restores the own upload, server-confirmed)
    expect(registerModal).toMatch(/if \(outcome\.stage === 'create'\) \{[\s\S]{0,600}retainedAttachmentRef\.current = outcome\.attachmentId;\s*await refreshCandidates\(\);/);
  });
});

describe('completion guidance and actions at WAITING_RECEIPT (legacy path)', () => {
  it('RequestEdit derives the legacy guidance from readiness facts and feeds header + panel through the same slot', () => {
    expect(edit).toMatch(/import \{ completionNextActionGuidance, legacyCompletionGuidance \} from '\.\.\/\.\.\/lib\/operationInvoiceView'/);
    expect(edit).toMatch(/const legacyGuidance = useMemo\(\s*\(\) => legacyCompletionGuidance\(completionReadiness, status\)/);
    expect(edit).toMatch(/const legacyFinalizeBlocked = !!legacyGuidance\?\.blocksLegacyFinalize/);
    expect(edit).toMatch(/const completionGuidance = release4Guidance\s*\n?\s*\?\? \(legacyGuidance \? \{ responsible: legacyGuidance\.responsible, nextAction: legacyGuidance\.nextAction \} : null\)/);
    expect(edit).toMatch(/release4Guidance: completionGuidance, scalarGuidance: getRequestGuidance/);
    expect(edit).toMatch(/suppressLegacyFinalize=\{release4LegacyFinalizeSuppressed \|\| legacyFinalizeBlocked\}/);
    expect(edit).toMatch(/completionGuidance=\{completionGuidance\}/);
  });

  it('the panel lets any WAITING_RECEIPT completion guidance win over the status-only wording (same precedence as the header)', () => {
    expect(panel).toMatch(/const guidance = \(status === 'WAITING_RECEIPT' && completionGuidance\)\s*\n?\s*\? completionGuidance/);
    // "Finalizar Pedido" stays behind suppressLegacyFinalize (now also true for classification pending)
    expect(panel).toMatch(/status === 'WAITING_RECEIPT' && !suppressLegacyFinalize/);
  });

  it('no independent frontend rulebook: guidance comes from readiness blocking codes, not from group fields', () => {
    expect(edit).not.toMatch(/lineItems\.every\(/);
    expect(edit).not.toMatch(/completionReadiness\.complete\b/);
  });
});

describe('refresh after classification', () => {
  it('the host bumps the readiness refresh key and reloads the request; sections keep a single fetch each', () => {
    expect(edit).toMatch(/const \[postPaymentRefreshKey, setPostPaymentRefreshKey\] = useState\(0\)/);
    expect(edit).toMatch(/const handleObligationsChanged = useCallback\(\(\) => \{\s*setPostPaymentRefreshKey\(k => k \+ 1\);\s*void loadData\(\);/);
    expect(edit).toMatch(/onObligationsChanged=\{handleObligationsChanged\}/);
    expect(edit).toMatch(/refreshKey=\{postPaymentRefreshKey\}/);
    expect(edit).toMatch(/requestTypeCode=\{requestTypeCode \|\| null\}/);
    expect(completion).toMatch(/useEffect\(\(\) => \{ void refresh\(\); \}, \[refresh, refreshKey\]\)/);
    expect(completion.match(/operationInvoiceApi\.getCompletionReadiness\(/g)?.length).toBe(1);
  });
});

describe('regressions kept from v2.245.5 – v2.245.7', () => {
  it('receiving correction, audit label and workflow-projection consumption are untouched', () => {
    expect(receivingOp).toMatch(/message: receiptSubmitSuccessMessage\(previousReceivedQty, receivedQty\)/);
    expect(receivingOp).toMatch(/canShowReopenReceiving\(group\.status, currentUserRoles, isReadOnly\)/);
    expect(printModel).toMatch(/RECEIVING_REOPENED: 'Recebimento reaberto para correção'/);
    expect(edit).toMatch(/loadWorkflowProjection\(api\.requests\.getWorkflowProjection, id, requestTypeCode, status,/);
    expect(edit).toMatch(/resolveProjectionGuidance\(projectionLoad, requestTypeCode, status\)/);
  });
});
