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
import { formatCurrencyAO, getRequestGuidance, formatDateTime } from '../../lib/utils';
import { CurrencyDto, LookupDto, RequestStatusHistoryDto, RequestAttachmentDto, RequestLineItemDto, SavedQuotationDto } from '../../types';
import { RequestAttachments } from '../../components/RequestAttachments';
import { motion, AnimatePresence } from 'framer-motion';
import { completeQuotationAction } from '../../lib/workflow';
import { ApprovalModal, ApprovalActionType } from '../../components/ApprovalModal';
import { RegisterPoModal } from '../../components/RegisterPoModal';
import { CorrectPoModal } from '../../components/CorrectPoModal';
import { RequestActionHeader, BreadcrumbItem, OperationalGuidance } from './components/RequestActionHeader';
import { RequestQuotations } from './components/RequestQuotations';
import { scrollToFirstError } from '../../lib/validation';
import { CollapsibleSection } from '../../components/ui/CollapsibleSection';
import { RequestGeneralDataSection } from './components/RequestGeneralDataSection';
import { RequestFinancialSummary } from './components/RequestFinancialSummary';
import { RequestStatusActionPanels } from './components/RequestStatusActionPanels';
import { RequestLineItemsSection } from './components/RequestLineItemsSection';

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
        showRegisterPoModal,
        setShowRegisterPoModal,
        showCorrectPoModal,
        setShowCorrectPoModal,
        approvalComment,
        setApprovalComment,
        approvalProcessing,
        modalFeedback,
        setModalFeedback,
        quickSupplierModal,
        setQuickSupplierModal,
        isBuyer,
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
        handleSaveItem,
        handleDeleteItem,
        handleDeleteRequest,
        handleAttachmentRefresh,
        loadData,
        navigate,
        location
    } = useRequestDetail({ id: inputRequestId || undefined, onClose: onDrawerClose });

    const isDrawerMode = !!onDrawerClose;

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
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1400px', margin: '0 auto' }}>
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
                        onClick={handleSubmitRequest}
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
                    setShowRegisterPoModal={setShowRegisterPoModal}
                    setShowCorrectPoModal={setShowCorrectPoModal}
                    setShowApprovalModal={setShowApprovalModal}
                    navigate={navigate}
                    onDrawerClose={onDrawerClose}
                    getRequestGuidance={getRequestGuidance}
                />
            </RequestActionHeader>

            {/* Form Card */}
            <form onSubmit={handleSubmit} style={{
                display: 'flex',
                flexDirection: 'column',
                gap: '32px'
            }}>
                <RequestGeneralDataSection
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
                    sectionTitleClassName={styles.sectionTitle}
                    labelClassName={styles.formLabel}
                    getInputClassName={getInputClassName}
                    renderFieldError={renderFieldError}
                    getFieldErrors={getFieldErrors}
                />



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
                canEditItems={canEditItems}
                handleSaveItem={handleSaveItem}
                handleDeleteItem={handleDeleteItem}
                isOpen={sectionsOpen.items}
                onToggle={() => toggleSection('items')}
                isItemsHighlighted={isItemsHighlighted}
                itemsSectionRef={itemsSectionRef}
                formatCurrencyAO={formatCurrencyAO}
                sectionTitleClassName={styles.sectionTitle}
            />

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
                                            {formatDateTime(entry.createdAtUtc)}
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
                                                <span style={{ color: 'var(--color-text-muted)', fontStyle: 'italic', fontSize: '0.8rem' }}>"{entry.comment}"</span>
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

            {id && (
                (() => {
                    let activeTotalAmount = Number(formData.estimatedTotalAmount) || 0;
                    let activeSupplierName = supplierName;
                    let activeCurrencyCode = currencies.find(c => c.id === Number(formData.currencyId))?.code || '';
                    let activeSupplierId: number | null = formData.supplierId ? Number(formData.supplierId) : null;

                    if (requestTypeCode === 'QUOTATION' && quotations.some(q => q.isSelected)) {
                       const winner = quotations.find(q => q.isSelected)!;
                       activeTotalAmount = winner.totalAmount;
                       activeSupplierName = winner.supplierNameSnapshot;
                       activeCurrencyCode = winner.currency || activeCurrencyCode;
                       if (winner.supplierId) activeSupplierId = winner.supplierId;
                    }

                    return (
                        <RegisterPoModal
                            show={showRegisterPoModal}
                            requestId={id}
                            supplierId={activeSupplierId}
                            requestData={{
                                totalAmount: activeTotalAmount,
                                supplierName: activeSupplierName,
                                currencyCode: activeCurrencyCode
                            }}
                            onClose={() => setShowRegisterPoModal(false)}
                            onSuccess={async (msg) => {
                                setShowRegisterPoModal(false);
                                setFeedback({ type: 'success', message: msg });
                                // Reload state
                                const data = await api.requests.get(id);
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

            {/* Correct PO Modal — only for WAITING_PO_CORRECTION correction flow (isolated from initial registration) */}
            <CorrectPoModal
                show={showCorrectPoModal}
                requestId={id}
                onClose={() => setShowCorrectPoModal(false)}
                onSuccess={async (msg) => {
                    setShowCorrectPoModal(false);
                    setFeedback({ type: 'success', message: msg });
                    const data = await api.requests.get(id);
                    setStatus(data.statusCode);
                    setStatusFullName(data.statusName);
                    setStatusBadgeColor(data.statusBadgeColor);
                    setStatusHistory(data.statusHistory || []);
                    setAttachments(data.attachments || []);
                }}
            />

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

        </motion.div >
    );
}
