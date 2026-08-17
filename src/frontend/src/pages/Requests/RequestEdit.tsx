// @ts-nocheck
import { useRequestDetail } from './hooks/useRequestDetail';
import styles from './request-edit.module.css';
import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useNavigate, useParams, useLocation, useSearchParams } from 'react-router-dom';
import { 
    Save, 
    X, 
    ShieldCheck, 
    ShieldAlert, 
    Trash2, 
    Send, 
    ArrowLeft
} from 'lucide-react';
import { QuickSupplierModal } from '../../components/Buyer/QuickSupplierModal';
import { api, ApiError } from '../../lib/api';
import { useAuth } from '../../features/auth/AuthContext';
import { ROLES } from '../../constants/roles';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { formatCurrencyAO, getRequestGuidance, formatDateTimeAngola } from '../../lib/utils';
import { CurrencyDto, LookupDto, RequestStatusHistoryDto, RequestAttachmentDto, RequestLineItemDto, SavedQuotationDto } from '../../types';
import { RequestAttachments } from '../../components/RequestAttachments';
import { motion, AnimatePresence } from 'framer-motion';
import { completeQuotationAction } from '../../lib/workflow';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { RegisterPoModal } from '../../components/RegisterPoModal';
import { CorrectPoModal } from '../../components/CorrectPoModal';
import { ReconciliationModal } from '../../components/ui/ReconciliationModal';
import { CatalogItemReconciliationModal } from '../../components/CatalogItemReconciliationModal';
import { ReconciliationWarningDialog } from '../../components/ReconciliationWarningDialog';
import { FinalizeReceivingModal } from '../../components/modals/FinalizeReceivingModal';
import { RequestActionHeader, BreadcrumbItem, OperationalGuidance } from './components/RequestActionHeader';
import { RequestQuotations } from './components/RequestQuotations';
import { scrollToFirstError } from '../../lib/validation';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { RequestGeneralDataSection } from './components/RequestGeneralDataSection';
import { PaymentSourceDocumentsSection } from './components/PaymentSourceDocumentsSection';
import { PaymentSourceDocumentsSummaryDto } from '../../types/paymentSourceDocument';
import { RequestFinancialSummary } from './components/RequestFinancialSummary';
import { RequestStatusActionPanels } from './components/RequestStatusActionPanels';
import { RequestGroupDisplaySummary } from './components/RequestGroupDisplaySummary';
import { OperationInvoiceSection } from './components/OperationInvoiceSection';
import { RequestCompletionSection } from './components/RequestCompletionSection';
import { RequestLineItemsSection } from './components/RequestLineItemsSection';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { canCreateSupplierContextually } from '../../lib/supplierQuickCreate';
import { plantMismatches } from '../../lib/paymentSourceDocuments';

export interface RequestEditProps { requestId?: string | null; onClose?: () => void; }
export function RequestEdit({ requestId: inputRequestId, onClose: onDrawerClose }: RequestEditProps = {}) {
    
    const {
        id,
        isCopyMode,
        copyFromId,
        loading,
        saving,
        submitting,
        feedback,
        setFeedback,
        fieldErrors,
        status,
        setStatus,
        statusFullName,
        statusBadgeColor,
        requestTypeCode,
        requestNumber,
        formData,
        setFormData,
        featureFlags,
        documentClassification,
        classificationConflict,
        setClassificationConflict,
        initialFormData,
        supplierName,
        setSupplierName,
        supplierPortalCode,
        setSupplierPortalCode,
        lineItems,
        setLineItems,
        itemForm,
        setItemForm,
        itemSaving,
        statusHistory,
        setStatusHistory,
        attachments,
        setAttachments,
        quotations,
        selectedQuotationId,
        units,
        currencies,
        needLevels,
        departments,
        companies,
        plants,
        costCenters,
        ivaRates,
        sectionsOpen,
        toggleSection,
        isItemsHighlighted,
        isAttachmentsHighlighted,
        isQuotationsHighlighted,
        itemsSectionRef,
        quotationsSectionRef,
        showApprovalModal,
        setShowApprovalModal,
        poGroupIdForUpload,
        setPoGroupIdForUpload,
        showCorrectPoModal,
        setShowCorrectPoModal,
        showReconciliationModal,
        setShowReconciliationModal,
        approvalComment,
        setApprovalComment,
        approvalProcessing,
        modalFeedback,
        setModalFeedback,
        quickSupplierModal,
        setQuickSupplierModal,
        isBuyer,
        isCreator,
        isAreaApprover,
        isFinalApprover,
        isFinance,
        isReceiving,
        isReworkStatus,
        isQuotationStage,
        isDraftEditable,
        isQuotationPartiallyEditable,
        isFullyReadOnly,
        hasSavedQuotations,
        canEditHeader,
        canEditSupplier,
        canEditItems,
        canManageAttachments,
        canExecuteOperationalAction,
        canEdit,
        canCancelRequest,
        handleChange,
        clearFieldError,
        handleSubmit,
        handleRequestAction,
        handleSubmitRequest,
        showCatalogReconciliationWarning,
        setShowCatalogReconciliationWarning,
        catalogReconciliation,
        catalogUnresolved,
        catalogDocumentLabels,
        catalogEquivalentIndexesOf,
        applyCatalogResolutions,
        handleSaveItem,
        handleDeleteItem,
        handleDeleteRequest,
        handleAttachmentRefresh,
        loadData,
        navigate,
        location,
        poGroups,
        setUsesMultiSourceDocuments
    } = useRequestDetail({ id: inputRequestId || undefined, onClose: onDrawerClose });

    const isDrawerMode = !!onDrawerClose;
    const { user } = useAuth();

    // Mirrors LookupsController.CanCreateSupplierContextuallyAsync. The scope half is proxied by the
    // lookup lists this screen loaded, which are themselves scoped to the user; the server remains
    // the authority and will still refuse if the proxy is ever generous.
    const canCreateSupplier = canCreateSupplierContextually(user?.roles, {
        hasPlantScope: plants.length > 0,
        hasDepartmentScope: departments.length > 0
    });

    // Release 3: the PAYMENT source-document collection. Held here rather than in the hook because
    // the summary is the authoritative source of totals and canSubmit, and the section owns it.
    const [sourceDocumentsSummary, setSourceDocumentsSummary] =
        useState<PaymentSourceDocumentsSummaryDto | null>(null);
    const [sourceDocsOpen, setSourceDocsOpen] = useState(true);

    /**
     * This request's documents are the authority, not its header.
     *
     * <p>From the persisted discriminator, never a row count or a date: a new multi-document draft
     * has zero documents until its first is saved and must still be treated as one.</p>
     *
     * <p>Declared AFTER the state it reads. `// @ts-nocheck` at the top of this file means the
     * compiler will not catch a use-before-declaration here, and a `const` read during render before
     * its `useState` has run is a temporal-dead-zone crash, not a warning.</p>
     */
    const isMultiDocumentPayment =
        featureFlags.paymentMultiDocumentEnabled &&
        requestTypeCode === 'PAYMENT' &&
        sourceDocumentsSummary?.usesMultiDocumentModel === true;
    const [documentEditingState, setDocumentEditingState] =
        useState<{ openSequence: number | null; unsavedSequences: number[] }>(
            { openSequence: null, unsavedSequences: [] });
    /** Why the request cannot be submitted yet. Shown instead of letting the backend refuse. */
    const [submitBlockers, setSubmitBlockers] = useState<string[] | null>(null);

    /**
     * Stable by construction. An inline arrow here is a new function every render, and the
     * collection used to fold that identity into its load effect — one render became one fetch,
     * and one fetch became one render.
     */
    const handleSourceDocumentsSummary = useCallback(
        (summary: PaymentSourceDocumentsSummaryDto | null) => {
            setSourceDocumentsSummary(summary);
            // The submit guards live in the hook and must stop asking for the header-level
            // classification the moment the documents own it.
            setUsesMultiSourceDocuments(summary?.usesMultiDocumentModel === true);
        }, [setUsesMultiSourceDocuments]);

    /**
     * §16 — submission preflight for a multi-document PAYMENT.
     *
     * <p>Two things must be true before an approver sees this request: nothing is still being typed,
     * and the backend's own summary says it may be submitted. The server re-checks everything and
     * stays authoritative; this exists so the user is told <b>which document</b> is the problem
     * rather than receiving one generic refusal.</p>
     */
    const guardedSubmitRequest = () => {
        const summary = sourceDocumentsSummary;
        if (!featureFlags.paymentMultiDocumentEnabled || !summary?.usesMultiDocumentModel) {
            void handleSubmitRequest();
            return;
        }

        const problems: string[] = [];

        for (const seq of documentEditingState.unsavedSequences) {
            problems.push(
                `Documento ${seq} tem alterações por guardar. Guarde o documento antes de gerar o pedido.`);
        }

        if (documentEditingState.openSequence != null &&
            !documentEditingState.unsavedSequences.includes(documentEditingState.openSequence)) {
            problems.push(
                `Documento ${documentEditingState.openSequence} ainda está em revisão. ` +
                'Confirme o documento antes de gerar o pedido.');
        }

        if (!summary.canSubmit) {
            problems.push(...summary.requestValidationMessages);
            for (const d of summary.documents) {
                for (const m of d.validationMessages) {
                    problems.push(`Documento ${d.sequenceNumber}: ${m}`);
                }
            }
        }

        if (problems.length > 0) { setSubmitBlockers(problems); return; }
        void handleSubmitRequest();
    };

    const getFieldErrors = (fieldName: string) => {
        if (!fieldErrors) return null;
        const normalizedField = fieldName.toLowerCase();
        const key = Object.keys(fieldErrors).find(k => {
            const normalizedKey = k.toLowerCase().replace(/^\$\./, '');
            return normalizedKey === normalizedField || normalizedKey.endsWith('.' + normalizedField);
        });
        return key ? fieldErrors[key] : null;
    };

    const renderFieldError = (fieldName: string) => {
        const errors = getFieldErrors(fieldName);
        if (!errors) return null;
        return (
            <div className={styles.fieldError}>
                {errors[0]}
            </div>
        );
    };

    const getInputClassName = (fieldName: string) =>
        `${styles.formInput} ${getFieldErrors(fieldName) ? styles.formInputError : ''}`;


    if (loading) {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', margin: '0 auto' }}>
                <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                <div style={{ padding: '20px', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', borderRadius: '8px', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-sm)' }}>
                    <div style={{ width: '40px', height: '40px', border: '3px solid var(--color-border)', borderTopColor: 'var(--color-primary)', borderRadius: '50%', animation: 'spin 1s linear infinite', margin: '0 auto 16px auto' }}></div>
                    <div style={{ fontWeight: 600, color: 'var(--color-text-main)' }}>Carregando detalhes do pedido...</div>
                </div>
            </div>
        );
    }

    // Defensive Guard: If data failed to load and we're not in copy mode, avoid white-screen crash
    // We check initialFormData as it's only populated after a successful loadData cycle.
    if (!loading && !initialFormData && !isCopyMode) {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1400px', margin: '100px auto', textAlign: 'center' }}>
                <Feedback type={feedback.type} message={feedback.message || 'Erro ao carregar o pedido.'} onClose={() => setFeedback(prev => ({ ...prev, message: null }))} />
                <div style={{ fontWeight: 600, color: 'var(--color-text-muted)', fontSize: '1.25rem' }}>O pedido solicitado não pôde ser encontrado ou carregado.</div>
                <div style={{ color: 'var(--color-text-muted)' }}>Isso pode ocorrer se o pedido foi excluído ou se há um problema de conexão.</div>
                <div style={{ display: 'flex', gap: '12px', justifyContent: 'center', marginTop: '24px' }}>
                    <button onClick={() => window.location.reload()} className="btn-primary" style={{ padding: '10px 24px' }}>Tentar Novamente</button>
                    <button onClick={() => navigate('/requests')} className="btn-secondary" style={{ padding: '10px 24px' }}>Voltar para a Lista</button>
                </div>
            </div>
        );
    }

    const headerProps = {
        breadcrumbs: [
            { label: 'Dashboard', to: '/' },
            { label: 'Pedidos', to: `/requests${location.state?.fromList || ''}` },
            { label: status === 'DRAFT' ? 'Editar' : isReworkStatus ? 'Reajustar' : 'Visualizar' }
        ] as BreadcrumbItem[],
        title: status === 'DRAFT' ? 'Editar Pedido' : isReworkStatus ? 'Reajustar Pedido' : 'Pedido',
        requestNumber,
        statusBadge: statusFullName && (
            <span className={`badge badge-sm badge-${
                statusBadgeColor === 'red' ? 'danger' :
                statusBadgeColor === 'yellow' ? 'warning' :
                statusBadgeColor === 'green' ? 'success' :
                statusBadgeColor || 'neutral'
            }`} style={{ marginLeft: '8px' }}>
                {statusFullName}
            </span>
        ),
        contextBadges: (
            <>
                {isFullyReadOnly && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: 'var(--color-bg-page)', color: '#64748b',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #e2e8f0',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldAlert size={12} /> MODO DE CONSULTA
                    </span>
                )}
                {isQuotationPartiallyEditable && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: '#e0f2fe', color: '#0369a1',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #bae6fd',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldCheck size={12} /> COTAÇÃO ATIVA
                    </span>
                )}
                {isReworkStatus && (
                    <span style={{
                        padding: '2px 8px', backgroundColor: '#fff7ed', color: '#ea580c',
                        borderRadius: '4px', fontSize: '0.65rem', fontWeight: 800,
                        textTransform: 'uppercase', border: '1px solid #ffedd5',
                        display: 'flex', alignItems: 'center', gap: '4px'
                    }}>
                        <ShieldAlert size={12} /> REAJUSTE NECESSÁRIO
                    </span>
                )}
            </>
        ),
        secondaryActions: (
            <>
                <button
                    type="button"
                    onClick={() => navigate(`/requests`)}
                    style={{
                        height: '36px', padding: '0 16px', borderRadius: 'var(--radius-md)', border: '1px solid var(--color-border)',
                        backgroundColor: 'var(--color-bg-surface)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                        fontWeight: 800, fontFamily: 'var(--font-family-display)', fontSize: '0.7rem', color: 'var(--color-text-main)',
                        boxShadow: 'var(--shadow-sm)', transition: 'all 0.2s'
                    }}
                >
                    {isCopyMode ? <><X size={14} /> DESCARTAR CÓPIA</> : (status === 'DRAFT' ? <><X size={14} /> CANCELAR</> : <><ArrowLeft size={14} /> VOLTAR</>)}
                </button>
            </>
        ),
        primaryActions: (
            <>
                {status === 'DRAFT' && !isCopyMode && (
                    <button
                        type="button"
                        onClick={() => handleDeleteRequest()}
                        disabled={saving}
                        style={{
                            height: '36px', padding: '0 12px', borderRadius: 'var(--radius-sm)', border: '2px solid #EF4444',
                            backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                            fontWeight: 800, fontFamily: 'var(--font-family-display)', color: '#EF4444', fontSize: '0.75rem'
                        }}
                    >
                        <Trash2 size={14} /> EXCLUIR
                    </button>
                )}
                {canCancelRequest && status !== 'DRAFT' && (
                    <button
                        type="button"
                        onClick={() => setShowApprovalModal({ show: true, type: 'CANCEL_REQUEST' })}
                        disabled={saving || submitting}
                        style={{
                            height: '36px', padding: '0 16px', borderRadius: 'var(--radius-md)', border: '1px solid #EF4444',
                            backgroundColor: 'white', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                            fontWeight: 800, fontFamily: 'var(--font-family-display)', color: '#EF4444', fontSize: '0.7rem',
                            boxShadow: 'var(--shadow-sm)', transition: 'all 0.2s'
                        }}
                    >
                        <X size={14} /> CANCELAR PEDIDO
                    </button>
                )}
                {(isDraftEditable || isQuotationPartiallyEditable) && !isCopyMode && (
                    <button
                        onClick={handleSubmit}
                        disabled={saving}
                        className="btn-primary"
                        style={{ height: '36px', padding: '0 16px', display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.75rem', borderRadius: 'var(--radius-sm)' }}
                    >
                        <Save size={14} /> {saving ? 'SALVANDO...' : 'SALVAR'}
                    </button>
                )}
                {isDraftEditable && (
                    <button
                        onClick={guardedSubmitRequest}
                        disabled={submitting || saving}
                        className="btn-primary"
                        style={{
                            height: '36px', padding: '0 16px', display: 'flex', alignItems: 'center', gap: '6px',
                            fontSize: '0.75rem', backgroundColor: 'var(--color-primary)', borderRadius: 'var(--radius-sm)'
                        }}
                    >
                        <Send size={14} /> {submitting ? 'PROCESSANDO...' : (isReworkStatus ? 'REENVIAR' : 'SUBMETER')}
                    </button>
                )}
            </>
        ),
        operationalGuidance: (status ? getRequestGuidance(status, requestTypeCode || '') : null) as OperationalGuidance | null,
        feedback,
        onCloseFeedback: () => setFeedback(prev => ({ ...prev, message: null })),
        isDrawerMode: !!onDrawerClose
    };

    return (

        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.4 }}
            style={{ display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', maxWidth: '1440px', margin: '0 auto', minWidth: 0 }}
        >

            {/* Sticky Header Unit - Feedback, Banners, and Main Action Header */}
            <RequestActionHeader {...headerProps}>
                <RequestStatusActionPanels
                    requestId={id}
                    status={status}
                    requestTypeCode={requestTypeCode}
                    isBuyer={isBuyer}
                    isAreaApprover={isAreaApprover}
                    isFinalApprover={isFinalApprover}
                    isFinance={isFinance}
                    isReceiving={isReceiving}
                    canExecuteOperationalAction={canExecuteOperationalAction}
                    isQuotationPartiallyEditable={isQuotationPartiallyEditable}
                    isCopyMode={isCopyMode}
                    poGroups={poGroups || []}
                    setPoGroupIdForUpload={setPoGroupIdForUpload}
                    setShowCorrectPoModal={setShowCorrectPoModal}
                    setShowReconciliationModal={setShowReconciliationModal}
                    setShowApprovalModal={setShowApprovalModal}
                    navigate={navigate}
                    onDrawerClose={onDrawerClose}
                    getRequestGuidance={getRequestGuidance}
                />
            </RequestActionHeader>

            {requestTypeCode === 'QUOTATION' && (
                <RequestGroupDisplaySummary poGroups={poGroups || []} fallbackStatusName={statusFullName || ''} />
            )}

            {/* Form Card */}
            <form onSubmit={handleSubmit} style={{
                display: 'flex',
                flexDirection: 'column',
                gap: '32px'
            }}>
                <RequestGeneralDataSection
                    isMultiDocumentPayment={isMultiDocumentPayment}
                    plantMismatches={isMultiDocumentPayment
                        ? plantMismatches(
                            formData.plantId ? Number(formData.plantId) : null,
                            sourceDocumentsSummary?.documents ?? [],
                            id => plants.find(p => p.id === id)?.name ?? null)
                        : []}
                    formData={formData}
                    setFormData={setFormData}
                    handleChange={handleChange}
                    clearFieldError={clearFieldError}
                    supplierName={supplierName}
                    setSupplierName={setSupplierName}
                    supplierPortalCode={supplierPortalCode}
                    setSupplierPortalCode={setSupplierPortalCode}
                    setQuickSupplierModal={setQuickSupplierModal}
                    needLevels={needLevels}
                    departments={departments}
                    companies={companies}
                    plants={plants}
                    canEditHeader={canEditHeader}
                    canEditSupplier={canEditSupplier}
                    isQuotationPartiallyEditable={isQuotationPartiallyEditable}
                    isQuotationStage={isQuotationStage}
                    hasSavedQuotations={hasSavedQuotations}
                    requestTypeCode={requestTypeCode}
                    requestNumber={requestNumber}
                    status={status}
                    lineItemsCount={lineItems.length}
                    featureFlags={featureFlags}
                    documentClassification={documentClassification}
                    classificationConflict={classificationConflict}
                    setClassificationConflict={setClassificationConflict}
                    sectionTitleClassName={styles.sectionTitle}
                    labelClassName={styles.formLabel}
                    getInputClassName={getInputClassName}
                    renderFieldError={renderFieldError}
                    getFieldErrors={getFieldErrors}
                />

                {/* Release 3: PAYMENT may carry several source documents, each with its own OCR,
                    classification and items. Renders nothing when the flag is off or when the
                    request has no source-document rows (the legacy single-document path). */}
                {id && (
                    <PaymentSourceDocumentsSection
                        requestId={id}
                        requestTypeCode={requestTypeCode}
                        statusCode={status}
                        multiDocumentEnabled={featureFlags.paymentMultiDocumentEnabled}
                        // The explicit discriminator, not a row count: a NEW multi-document draft has zero
                        // documents until its first is persisted, and must still show the collection.
                        hasSourceDocuments={
                            sourceDocumentsSummary?.usesMultiDocumentModel === true ||
                            (sourceDocumentsSummary?.documents.length ?? 0) > 0
                        }
                        documentCount={sourceDocumentsSummary?.documents.length ?? 0}
                        plants={plants}
                        currencies={currencies.map(c => ({ code: c.code, name: c.symbol || c.code }))}
                        isOpen={sourceDocsOpen}
                        onToggle={() => setSourceDocsOpen(o => !o)}
                        canCreateSupplier={canCreateSupplier}
                        onSummaryChange={handleSourceDocumentsSummary}
                        onEditingStateChange={setDocumentEditingState}
                    />
                )}

                {/* Release 4 Phase 3B: Final Invoice registration, allocation and coverage. Works
                    for PAYMENT and QUOTATION alike — the obligations read model is the abstraction.
                    Renders nothing while the coverage capability is off or nothing exists to show.
                    The completion lifecycle (Phase 4) is deliberately absent. */}
                {id && (
                    <OperationInvoiceSection
                        requestId={id}
                        coverageEnabled={featureFlags.postPaymentCompletionEnabled}
                        statusCode={status || null}
                        isFinance={isFinance}
                        isBuyer={isBuyer}
                        isAdmin={user?.roles?.includes('System Administrator') ?? false}
                        currentUserId={user?.id ?? null}
                    />
                )}

                {/* Release 4 Phase 4D: completion readiness — a faithful rendering of the backend
                    completion-readiness read model. Shows what is missing and who acts next; the
                    completion itself is automatic (no manual "Concluir Pedido"). Renders nothing
                    while the coverage capability is off or the request has no groups. */}
                {id && (
                    <RequestCompletionSection
                        requestId={id}
                        coverageEnabled={featureFlags.postPaymentCompletionEnabled}
                        lifecycleEnabled={featureFlags.completionLifecycleEnabled}
                        isFinance={isFinance}
                        isAdmin={user?.roles?.includes('System Administrator') ?? false}
                    />
                )}

                {/* One authoritative total. On a multi-document request the value IS the sum of the
                    active documents, and the source-document summary already states it — a second
                    editable copy of currency and discount would be a second, contradictory answer. */}
                {!isMultiDocumentPayment && (
                <RequestFinancialSummary
                    formData={formData}
                    setFormData={setFormData}
                    handleChange={handleChange}
                    clearFieldError={clearFieldError}
                    currencies={currencies}
                    canEditHeader={canEditHeader}
                    canEdit={canEdit}
                    lineItemsCount={lineItems.length}
                    isOpen={sectionsOpen.finance}
                    onToggle={() => toggleSection('finance')}
                    formatCurrencyAO={formatCurrencyAO}
                    labelClassName={styles.formLabel}
                    getInputClassName={getInputClassName}
                    renderFieldError={renderFieldError}
                />
                )}

            </form>

            <RequestLineItemsSection
                lineItems={lineItems}
                itemForm={itemForm}
                setItemForm={setItemForm}
                itemSaving={itemSaving}
                selectedQuotationId={selectedQuotationId}
                quotations={quotations}
                units={units}
                plants={plants}
                costCenters={costCenters}
                ivaRates={ivaRates}
                companyId={formData.companyId}
                requestTypeCode={requestTypeCode}
                supplierId={formData.supplierId ?? null}
                fieldErrors={fieldErrors}
                clearFieldError={clearFieldError}
                // Items belong to a document. They are changed inside the document composer, never
                // from a consolidated list where a line has no stated owner.
                canEditItems={canEditItems && !isMultiDocumentPayment}
                showSourceDocumentColumn={isMultiDocumentPayment}
                handleSaveItem={handleSaveItem}
                handleDeleteItem={handleDeleteItem}
                isOpen={sectionsOpen.items}
                onToggle={() => toggleSection('items')}
                isItemsHighlighted={isItemsHighlighted}
                itemsSectionRef={itemsSectionRef}
                formatCurrencyAO={formatCurrencyAO}
                sectionTitleClassName={styles.sectionTitle}
            />

            {/* NOTE: the NotQuotedDecisionPanel (accept/reject a buyer's not-quoted
                proposal) was removed here. Product decision: closing an item without
                quotation is now a final Buyer action ("Encerrar sem cotação" in the
                Buyer workspace, status CLOSED_NOT_QUOTED) — no Requester/Approver
                acceptance step. Legacy NOT_QUOTED_PROPOSED endpoints/components are
                kept dormant for old data only. */}

            {/* Section: Cotações Salvas */}
            {requestTypeCode === 'QUOTATION' && status !== 'CANCELLED' && status !== 'REJECTED' && (
                <CollapsibleSection
                    title="Cotações Salvas"
                    count={quotations.length}
                    isOpen={sectionsOpen.quotations}
                    onToggle={() => toggleSection('quotations')}
                >
                    <div 
                        ref={quotationsSectionRef}
                        style={{ 
                            padding: '32px',
                            transition: 'box-shadow 0.6s ease-in-out, border-color 0.6s ease-in-out',
                            boxShadow: isQuotationsHighlighted ? 'inset 0 0 0 4px rgba(239, 68, 68, 0.2), 0 0 20px rgba(239, 68, 68, 0.15)' : 'none',
                            border: isQuotationsHighlighted ? '2px solid #EF4444' : 'none',
                            borderRadius: 'inherit'
                        }}
                    >
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '24px' }}>
                            <h2 className={styles.sectionTitle} style={{ margin: 0 }}>
                                Cotações Recebidas
                            </h2>
                            {isFinalApprover && status === 'WAITING_FINAL_APPROVAL' && requestTypeCode === 'QUOTATION' && (
                                <span style={{ backgroundColor: '#4f46e5', color: '#fff', fontSize: '0.7rem', fontWeight: 900, padding: '4px 10px', borderRadius: '4px', textTransform: 'uppercase' }}>
                                    AÇÃO: SELECIONAR VENCEDORA
                                </span>
                            )}
                        </div>
                        
                        <RequestQuotations 
                            quotations={quotations}
                            isDrawerMode={isDrawerMode}
                            isFinalApproverMode={!!(isFinalApprover || isAreaApprover)}
                            isDecisionStage={status === 'WAITING_FINAL_APPROVAL' || status === 'WAITING_AREA_APPROVAL'}
                            onSelectWinner={async (qid) => {
                                try {
                                    await api.requests.selectQuotation(id!, qid);
                                    setFeedback({ type: 'success', message: 'Cotação vencedora selecionada!' });
                                    loadData();
                                } catch (err: any) {
                                    setFeedback({ type: 'error', message: err.message });
                                }
                            }}
                        />
                    </div>
                </CollapsibleSection>
            )}

            {/* Section: Pedido Anexos */}
            <CollapsibleSection
                title="Anexos do Pedido"
                count={attachments.length}
                isOpen={sectionsOpen.attachments}
                onToggle={() => toggleSection('attachments')}
            >
                <div style={{ padding: '32px' }}>
                    <RequestAttachments
                        id="attachments-section"
                        highlight={isAttachmentsHighlighted}
                        requestId={id || ''}
                        attachments={attachments}
                        canEdit={canManageAttachments}
                        onRefresh={handleAttachmentRefresh}
                        requestType={requestTypeCode || undefined}
                        status={status || undefined}
                        showSourceDocuments={isMultiDocumentPayment}
                        sourceDocuments={isMultiDocumentPayment
                            ? (sourceDocumentsSummary?.documents ?? []).map(d => ({
                                sequenceNumber: d.sequenceNumber,
                                attachmentId: d.attachmentId,
                                fileName: d.attachmentFileName ?? null,
                                documentNumber: d.documentNumber ?? null,
                                sourceDocumentType: (d.sourceDocumentType as string | null) ?? null,
                                supplierName: d.supplierNameSnapshot ?? null
                            }))
                            : []}
                    />
                </div>
            </CollapsibleSection>

            {/* Section D: Histórico do Pedido */}
            <CollapsibleSection
                title="Histórico do Pedido"
                count={statusHistory.length}
                isOpen={sectionsOpen.history}
                onToggle={() => toggleSection('history')}
            >
                <div style={{
                    padding: '32px'
                }}>
                    {statusHistory.length === 0 ? (
                        <div style={{ padding: '24px', textAlign: 'center', color: 'var(--color-text-muted)', backgroundColor: 'var(--color-bg-page)', border: '2px dashed var(--color-border)' }}>
                            Nenhum registro de histórico disponível para este pedido.
                        </div>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                            {statusHistory.map((entry, idx) => (
                                <div key={entry.id} style={{
                                    display: 'grid',
                                    gridTemplateColumns: 'minmax(150px, 1fr) 2fr 3fr',
                                    gap: '24px',
                                    padding: '20px',
                                    backgroundColor: idx % 2 === 0 ? 'var(--color-bg-surface)' : 'var(--color-bg-page)',
                                    borderLeft: '4px solid var(--color-primary)',
                                    position: 'relative'
                                }}>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Data / Hora</div>
                                        <div style={{ fontSize: '0.85rem', fontWeight: 600 }}>
                                            {formatDateTimeAngola(entry.createdAtUtc)}
                                        </div>
                                    </div>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Ação / Novo Status</div>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                                            <span style={{ fontSize: '0.9rem', fontWeight: 800, color: 'var(--color-primary)' }}>
                                                {entry.actionTaken === 'CREATE' ? 'CRIAÇÃO' :
                                                    entry.actionTaken === 'UPDATE' ? 'ATUALIZAÇÃO' :
                                                        entry.actionTaken === 'SUBMIT' ? 'SUBMISSÃO' : 
                                                            entry.actionTaken === 'DOCUMENTO ADICIONADO' ? 'DOC. ANEXADO' :
                                                                entry.actionTaken === 'DOCUMENTO REMOVIDO' ? 'DOC. REMOVIDO' :
                                                                    entry.actionTaken === 'ITEM_ADICIONADO' ? 'ITEM ADICIONADO' :
                                                                        entry.actionTaken === 'ITEM_ALTERADO' ? 'ITEM ALTERADO' :
                                                                            entry.actionTaken === 'ITEM_REMOVIDO' ? 'ITEM REMOVIDO' :
                                                                                entry.actionTaken}
                                            </span>
                                            <span style={{ fontSize: '0.8rem', fontWeight: 600, color: 'var(--color-text-main)' }}>
                                                ➡ {entry.newStatusName}
                                            </span>
                                        </div>
                                    </div>
                                    <div>
                                        <div style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '4px' }}>Responsável / Comentário</div>
                                        <div style={{ fontSize: '0.85rem' }}>
                                            <span style={{ fontWeight: 700, display: 'block', marginBottom: '4px' }}>{entry.actorName}</span>
                                            {entry.comment && (
                                                <span style={{ color: 'var(--color-text-muted)', fontStyle: 'italic', fontSize: '0.8rem', display: 'block', marginBottom: entry.fieldChanges && entry.fieldChanges.length > 0 ? '8px' : '0px' }}>"{entry.comment}"</span>
                                            )}
                                            {entry.fieldChanges && entry.fieldChanges.length > 0 && (
                                                <div style={{
                                                    marginTop: '8px',
                                                    padding: '10px 14px',
                                                    backgroundColor: 'var(--color-bg-page)',
                                                    borderRadius: 'var(--radius-md)',
                                                    border: '1px solid var(--color-border)',
                                                    fontSize: '0.75rem',
                                                    display: 'flex',
                                                    flexDirection: 'column',
                                                    gap: '6px'
                                                }}>
                                                    <div style={{ fontWeight: 800, color: 'var(--color-text-main)', fontSize: '0.7rem', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '2px' }}>
                                                        Alterações Detalhadas:
                                                    </div>
                                                    {entry.fieldChanges.map((change) => (
                                                        <div key={change.id} style={{ display: 'flex', flexWrap: 'wrap', gap: '6px', alignItems: 'center', lineHeight: '1.4' }}>
                                                            <span style={{ fontWeight: 700, color: 'var(--color-text-main)' }}>{change.fieldDisplayName}:</span>
                                                            <span style={{ color: 'var(--color-text-muted)', textDecoration: 'line-through', backgroundColor: 'rgba(239, 68, 68, 0.08)', padding: '1px 4px', borderRadius: '4px' }}>
                                                                {change.previousValue || 'vazio'}
                                                            </span>
                                                            <span style={{ color: 'var(--color-text-muted)' }}>➔</span>
                                                            <span style={{ fontWeight: 700, color: 'var(--color-primary)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.08)', padding: '1px 4px', borderRadius: '4px' }}>
                                                                {change.newValue || 'vazio'}
                                                            </span>
                                                        </div>
                                                    ))}
                                                </div>
                                            )}
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </CollapsibleSection>

            {/* Approval Modal */}
            {showApprovalModal.type === 'FINALIZE' ? (
                <FinalizeReceivingModal
                    requestId={id!}
                    requestNumber={requestNumber || ''}
                    attachments={attachments}
                    show={showApprovalModal.show}
                    onClose={() => {
                        setShowApprovalModal({ show: false, type: null });
                        setModalFeedback({ type: 'error', message: null });
                    }}
                    onSuccess={(msg) => {
                        setShowApprovalModal({ show: false, type: null });
                        setFeedback({ type: 'success', message: msg || 'Finalizado com sucesso.' });
                        loadData();
                    }}
                />
            ) : (
                <ApprovalModal
                    selectedQuotationName={quotations.find(q => q.isSelected)?.supplierNameSnapshot}
                    show={showApprovalModal.show}
                    type={showApprovalModal.type}
                    status={status}
                    isReworkStatus={isReworkStatus}
                    onClose={() => {
                        setShowApprovalModal({ show: false, type: null });
                        setApprovalComment('');
                        setModalFeedback({ type: 'error', message: null });
                    }}
                    onConfirm={(action) => handleRequestAction(action!)}
                    comment={approvalComment}
                    setComment={setApprovalComment}
                    processing={approvalProcessing || saving || submitting}
                    feedback={modalFeedback}
                    onCloseFeedback={() => setModalFeedback(prev => ({ ...prev, message: null }))}
                />
            )}

            {/* Register PO Modal */}
            {poGroupIdForUpload && (
                (() => {
                    const group = poGroups.find(g => g.id === poGroupIdForUpload);
                    if (!group) return null;

                    return (
                        <RegisterPoModal
                            show={!!poGroupIdForUpload}
                            requestId={id!}
                            poGroupId={poGroupIdForUpload}
                            supplierId={group.supplierId}
                            requestData={{
                                totalAmount: group.totalAmount,
                                supplierName: group.supplierNameSnapshot,
                                currencyCode: group.currencyCode || 'AOA'
                            }}
                            onClose={() => setPoGroupIdForUpload(null)}
                            onSuccess={async (msg) => {
                                setPoGroupIdForUpload(null);
                                setFeedback({ type: 'success', message: msg });
                                // Reload state
                                const data = await api.requests.get(id!);
                                setStatus(data.statusCode);
                                setStatusFullName(data.statusName);
                                setStatusBadgeColor(data.statusBadgeColor);
                                setStatusHistory(data.statusHistory || []);
                                setAttachments(data.attachments || []);
                            }}
                        />
                    );
                })()
            )}

            {/* Catalogue reconciliation — multi-document PAYMENT drafts.
                Same warning and same modal the creation flow uses; the only difference is that the
                answers are written straight to lines that already exist. */}
            <ReconciliationWarningDialog
                isOpen={showCatalogReconciliationWarning}
                unresolvedCount={catalogUnresolved.length}
                onReviewItems={() => {
                    setShowCatalogReconciliationWarning(false);
                    catalogReconciliation.openModal();
                }}
                onCancel={() => setShowCatalogReconciliationWarning(false)}
            />

            <CatalogItemReconciliationModal
                isOpen={catalogReconciliation.isModalOpen}
                onClose={catalogReconciliation.closeModal}
                classifiedItems={catalogReconciliation.classifiedItems}
                documentLabels={catalogDocumentLabels}
                equivalentIndexesOf={catalogEquivalentIndexesOf}
                onResolveAll={(resolutions) => { void applyCatalogResolutions(resolutions); }}
            />

            {/* Reconciliation Modal */}
            <ReconciliationModal
                show={showReconciliationModal}
                requestId={id!}
                onClose={() => setShowReconciliationModal(false)}
                onSuccess={(message) => {
                    setFeedback({ type: 'success', message });
                    loadData();
                }}
            />

            {/* Correct PO Modal — only for WAITING_PO_CORRECTION correction flow (isolated from initial registration) */}
            {poGroupIdForUpload && showCorrectPoModal && (
                <CorrectPoModal
                    show={showCorrectPoModal}
                    requestId={id!}
                    poGroupId={poGroupIdForUpload}
                    onClose={() => {
                        setShowCorrectPoModal(false);
                        setPoGroupIdForUpload(null);
                    }}
                    onSuccess={async (msg) => {
                        setShowCorrectPoModal(false);
                        setPoGroupIdForUpload(null);
                    setFeedback({ type: 'success', message: msg });
                    const data = await api.requests.get(id);
                    setStatus(data.statusCode);
                    setStatusFullName(data.statusName);
                    setStatusBadgeColor(data.statusBadgeColor);
                    setStatusHistory(data.statusHistory || []);
                    setAttachments(data.attachments || []);
                }}
            />
            )}

            <QuickSupplierModal 
                isOpen={quickSupplierModal.show}
                onClose={() => setQuickSupplierModal({ show: false, initialName: '', initialTaxId: '' })}
                onSuccess={(supplier: { id: number; name: string; taxId?: string }) => {
                    setFormData(prev => ({ ...prev, supplierId: String(supplier.id) }));
                    setSupplierName(supplier.name);
                    setSupplierPortalCode('');
                    clearFieldError('SupplierId');
                }}
                initialName={quickSupplierModal.initialName}
                initialTaxId={quickSupplierModal.initialTaxId}
            />

            {/* §16 — every remaining document-level issue, named by document. */}
            {submitBlockers && (
                <ConfirmationDialog
                    title="O pedido ainda não pode ser gerado"
                    message={
                        <ul style={{ margin: 0, paddingLeft: '18px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                            {submitBlockers.map(b => <li key={b}>{b}</li>)}
                        </ul>
                    }
                    confirmText="Rever documentos"
                    cancelText="Fechar"
                    variant="warning"
                    onConfirm={() => { setSubmitBlockers(null); setSourceDocsOpen(true); }}
                    onCancel={() => setSubmitBlockers(null)}
                />
            )}

        </motion.div >
    );
}
