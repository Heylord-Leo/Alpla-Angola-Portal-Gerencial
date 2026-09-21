import React, { useEffect, useState, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { 
  Package, 
  CheckCircle, 
  AlertTriangle, 
  FileText, 
  History,
  Info,
  ArrowLeft
} from 'lucide-react';
import { api } from '../../lib/api';
import { logger } from '../../lib/logger';
import { RequestDetailsDto } from '../../types';
import ReceivingModal from '../../components/modals/ReceivingModal';
import { FeedbackType } from '../../components/ui/Feedback';
import { RequestActionHeader } from '../Requests/components/RequestActionHeader';
import { RequestAttachments } from '../../components/RequestAttachments';
import { FinalizeReceivingModal } from '../../components/modals/FinalizeReceivingModal';
import { StandardTable } from '../../components/ui/StandardTable';
import { isReceivingActionableGroupStatus, RECEIVING_PHASE_BLOCKER, canConfirmReceiving, isReceivingConfirmed } from '../../lib/receivingEligibility';
import { motion } from 'framer-motion';

const highlightStyles = `
@keyframes sectionHighlight {
  0% { outline: 2px solid transparent; background-color: transparent; }
  15% { outline: 3px solid #ef4444; background-color: #fef2f2; }
  100% { outline: 2px solid transparent; background-color: transparent; }
}
.section-attention-highlight {
  animation: sectionHighlight 5s ease-out;
}
`;

const ReceivingOperation: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [request, setRequest] = useState<RequestDetailsDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
  
  const [modalOpen, setModalOpen] = useState(false);
  const [selectedItem, setSelectedItem] = useState<{
    id: string;
    description: string;
    quantity: number;
    receivedQty: number;
    unit: string;
    type: 'LINE_ITEM' | 'QUOTATION_ITEM';
  } | null>(null);

  const [finalizeModalState, setFinalizeModalState] = useState<{ show: boolean, groupId: string | null, groupName?: string }>({ show: false, groupId: null });

  const fetchRequest = async () => {
    if (!id) return;
    try {
      setLoading(true);
      const data = await api.requests.get(id);
      setRequest(data);
    } catch (err) {
      console.error('Error fetching request:', err);
      setFeedback({ type: 'error', message: 'Falha ao carregar dados do pedido.' });
    } finally {
      setLoading(false);
    }
  };

  const [isHighlighted, setIsHighlighted] = useState(false);

  useEffect(() => {
    fetchRequest();
    const params = new URLSearchParams(window.location.search);
    if (params.get('highlightRequestId') === id) {
        setIsHighlighted(true);
    }
  }, [id]);

  const isQuotationFlow = useMemo(() => 
    request?.requestTypeCode === 'QUOTATION' && !!request?.selectedQuotationId
  , [request]);

  const winningQuotation = useMemo(() => 
    isQuotationFlow 
      ? request?.quotations?.find((q: any) => q.id === request.selectedQuotationId)
      : null
  , [isQuotationFlow, request]);

  const operationalItems = useMemo(() => {
    if (!request) return [];
    
    return winningQuotation 
      ? winningQuotation.items.map((qi: any) => {
          // v2.245.0: resolve the item's PO group. Priority: explicit selectedQuotationItemId link;
          // then the canonical, unambiguous LineNumber match (LineNumber is unique within a request's
          // line items and within a quotation). Never guess when the line number is ambiguous.
          const byLink = request.lineItems?.find((li: any) => li.selectedQuotationItemId === qi.id);
          const byNumber = (request.lineItems ?? []).filter((li: any) => li.lineNumber === qi.lineNumber);
          const correspondingLineItem = byLink ?? (byNumber.length === 1 ? byNumber[0] : undefined);
          const requestPoGroupId = correspondingLineItem?.requestPoGroupId;
          return {
            id: qi.id,
            lineNumber: qi.lineNumber,
            description: qi.description,
            quantity: qi.quantity,
            receivedQty: qi.receivedQuantity || 0,
            unit: request.lineItems[0]?.unit || 'UN',
            statusName: qi.lineItemStatusName || 'Pendente',
            statusCode: qi.lineItemStatusCode || 'PENDING',
            statusColor: qi.lineItemStatusBadgeColor || '#EAB308',
            notes: qi.divergenceNotes,
            type: 'QUOTATION_ITEM' as const,
            requestPoGroupId
          };
        })
      : request.lineItems.map((li: any) => ({
          id: li.id,
          lineNumber: li.lineNumber,
          description: li.description,
          quantity: li.quantity,
          receivedQty: li.receivedQuantity || 0,
          unit: li.unit,
          statusName: li.lineItemStatusName || 'Pendente',
          statusCode: li.lineItemStatusCode || 'PENDING',
          statusColor: li.lineItemStatusBadgeColor || '#EAB308',
          notes: li.divergenceNotes,
          type: 'LINE_ITEM' as const,
          requestPoGroupId: li.requestPoGroupId
        }));
  }, [request, winningQuotation]);

  const allReceived = operationalItems.every((item: any) => item.statusCode === 'RECEIVED');
  const isReadOnly = request?.statusCode === 'COMPLETED' || request?.statusCode === 'CANCELLED';

  // v2.245.4: items that belong to NO active group of this request (RequestPoGroupId null or pointing at an
  // unknown/cancelled group). Such items can never be received against a group, so the page must fail
  // closed with a visible remediation blocker instead of silently rendering nothing.
  const unlinkedItems = useMemo(() => {
    const groupIds = new Set((request?.poGroups ?? []).map((g: any) => g.id));
    return operationalItems.filter((i: any) => !i.requestPoGroupId || !groupIds.has(i.requestPoGroupId));
  }, [request, operationalItems]);

  const handleOpenModal = (item: any) => {
    setSelectedItem(item);
    setModalOpen(true);
  };

  const handleConfirmReceiving = async (receivedQty: number, notes: string) => {
    if (!selectedItem) return;
    
    try {
      if (selectedItem.type === 'QUOTATION_ITEM') {
        await api.requests.updateItemReceiving(selectedItem.id, receivedQty, notes);
      } else {
        await api.lineItems.updateReceiving(selectedItem.id, receivedQty, notes);
      }
      setFeedback({ type: 'success', message: 'Recebimento registrado com sucesso.' });
      fetchRequest();
    } catch (err: any) {
      console.error('Error updating receiving:', err);
      const errorMessage = err instanceof Error ? err.message : (err?.response?.data?.message || 'Falha ao atualizar o recebimento do item.');
      
      logger.error(`Erro ao atualizar recebimento do item ${selectedItem.id} no pedido ${request?.requestNumber}: ${errorMessage}`, err, 'Global');
      setFeedback({ type: 'error', message: errorMessage });
    }
  };

  // v2.245.0: never open confirm-receiving without a real RequestPoGroupId. Group-based receiving
  // always needs a concrete group id — an empty id would POST { requestPoGroupId: '' } and the backend
  // (correctly) returns "Grupo P.O. não encontrado."
  const handleFinalizeClick = (groupId: string, groupName: string) => {
    if (!groupId) {
      setFeedback({ type: 'error', message: 'Não foi possível identificar o grupo de recebimento. Atualize a página e tente novamente.' });
      return;
    }
    setFinalizeModalState({ show: true, groupId, groupName });
  };

  // v2.245.0: the top-level action must resolve a real group — never guess with an empty id.
  // Exactly one resolvable receiving group → use it; zero → blocker; multiple → require the
  // group-specific confirm buttons (no first-group guess).
  const handleTopLevelFinalize = () => {
    const resolvable = (request?.poGroups ?? []).filter((g: any) =>
      operationalItems.some((i: any) => i.requestPoGroupId === g.id));
    if (resolvable.length === 1) {
      handleFinalizeClick(resolvable[0].id, resolvable[0].supplierNameSnapshot ?? '');
    } else if (resolvable.length === 0) {
      setFeedback({ type: 'error', message: 'Não foi possível identificar o grupo de recebimento. Atualize a página e tente novamente.' });
    } else {
      setFeedback({ type: 'error', message: 'Este pedido possui múltiplos grupos. Confirme o recebimento em cada grupo individualmente.' });
    }
  };



  if (loading) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '100px' }}>
        <div style={{ fontWeight: 800, color: 'var(--color-primary)', fontSize: '1.2rem', textTransform: 'uppercase' }}>Carregando Operação...</div>
      </div>
    );
  }

  if (!request) return null;

  const cardStyle = {
    backgroundColor: 'var(--color-bg-surface)',
    border: '1px solid var(--color-border)',
    boxShadow: 'var(--shadow-sm)',
    borderRadius: 'var(--radius-md)',
    overflow: 'hidden' as const
  };

  const sectionHeaderStyle = {
    padding: '16px 24px',
    borderBottom: '1px solid var(--color-border)',
    backgroundColor: 'var(--color-bg-page)',
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center'
  };

  return (
    <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ duration: 0.4 }}
        style={{ display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', maxWidth: '1440px', margin: '0 auto', minWidth: 0 }}
    >
      <style>{highlightStyles}</style>
      <RequestActionHeader
        title="Operação de Recebimento"
        requestNumber={request.requestNumber?.startsWith('REQ') ? request.requestNumber : `REQ ${request.requestNumber}`}
        breadcrumbs={[
            { label: 'Workspace de Recebimento', to: '/receiving/workspace' },
            { label: request.requestNumber?.startsWith('REQ') ? request.requestNumber : `REQ ${request.requestNumber}` }
        ]}
        feedback={feedback}
        onCloseFeedback={() => setFeedback({ ...feedback, message: null })}
        statusBadge={
            <div className="badge" style={{ color: 'var(--color-primary)', backgroundColor: 'rgba(var(--color-primary-rgb), 0.05)', border: '1px solid var(--color-primary)' }}>
                {request.statusName}
            </div>
        }
        primaryActions={
            !isReadOnly && (!request.poGroups || request.poGroups.length === 0)
              && canConfirmReceiving(request.statusCode, allReceived) && (
                <button
                    onClick={handleTopLevelFinalize}
                    className="btn-primary"
                    style={{ display: 'flex', alignItems: 'center', gap: '8px' }}
                >
                    <CheckCircle size={18} />
                    CONFIRMAR RECEBIMENTO
                </button>
            )
        }
        secondaryActions={
            <button
                onClick={() => navigate('/receiving/workspace')}
                className="btn-secondary"
                style={{ display: 'flex', alignItems: 'center', gap: '8px' }}
            >
                <ArrowLeft size={18} />
                VOLTAR
            </button>
        }
      />

      {/* Banner de Contexto (Identity Block) */}
      <div style={{ ...cardStyle, padding: '24px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '20px' }}>
          <div>
              <div style={{ fontSize: '0.7rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.1em', marginBottom: '8px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                  <Package size={14} /> Dados do Suprimento
              </div>
              <h2 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase' }}>
                  {request.title}
              </h2>
              <div style={{ marginTop: '12px', display: 'flex', gap: '24px', fontSize: '0.8rem', fontWeight: 700, textTransform: 'uppercase' }}>
                  <div>FORNECEDOR: <span style={{ color: 'var(--color-text-main)' }}>{request.supplierName}</span></div>
                  <div style={{ color: 'var(--color-border)' }}>|</div>
                  <div>TIPO: <span style={{ color: 'var(--color-text-main)' }}>{request.requestTypeName}</span></div>
              </div>
          </div>
          <div style={{ backgroundColor: 'var(--color-bg-page)', padding: '16px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)', textAlign: 'right' }}>
              <div style={{ fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '4px' }}>Unidade de Negócio</div>
              <div style={{ fontSize: '0.9rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase' }}>
                  {request.companyName} / {request.plantName || 'N/A'}
              </div>
          </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 320px', gap: '24px' }}>
        {/* Main Items Block */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
          {(request.poGroups && request.poGroups.length > 0) ? (
            request.poGroups.map((group: any) => {
              const groupItems = operationalItems.filter((i: any) => i.requestPoGroupId === group.id);
              if (groupItems.length === 0) {
                // A group with no items is only silent when every item of the request belongs to some
                // other active group. If the request holds UNLINKED items, this group is the victim of a
                // linkage defect: render a read-only blocker — no REGISTRAR, no CONFIRMAR, nothing that
                // could bypass the missing linkage. Progress stays 0/N, consistent with the blocked state.
                if (unlinkedItems.length === 0) return null;
                return (
                  <div key={group.id} style={cardStyle} data-testid="linkage-blocker">
                    <div style={sectionHeaderStyle}>
                      <h3 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 900, textTransform: 'uppercase', display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <FileText size={18} color="var(--color-primary)" />
                        Conferência: {group.supplierNameSnapshot}
                      </h3>
                      <div style={{ fontSize: '0.7rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-primary)', display: 'flex', alignItems: 'center', gap: '6px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.1)', padding: '4px 8px', border: '1px solid var(--color-primary)' }}>
                        <Info size={12} /> Status: {group.statusName || group.status}
                      </div>
                    </div>
                    <div style={{ margin: '16px 24px 24px', padding: '12px 16px', backgroundColor: '#FFFBEB', border: '1px solid #FCD34D', borderRadius: 'var(--radius-sm)', display: 'flex', alignItems: 'flex-start', gap: '10px' }}>
                      <AlertTriangle size={16} style={{ color: '#B45309', flexShrink: 0, marginTop: '2px' }} />
                      <div style={{ fontSize: '0.8rem', fontWeight: 700, color: '#92400E' }}>
                        Este pedido contém {unlinkedItems.length} item(ns) não vinculado(s) ao grupo de recebimento.
                        <span style={{ display: 'block', fontWeight: 600, marginTop: '2px' }}>
                          O recebimento não pode ser registado nem confirmado até que a vinculação seja corrigida por remediação administrativa controlada.
                        </span>
                      </div>
                    </div>
                  </div>
                );
              }
              
              // v2.245.0 §16: single canonical eligibility rule (mirror of the backend evaluator). A group
              // whose GROUP status is not a valid receiving phase (e.g. the PAYMENT PENDING drift) is
              // read-only — the backend would reject any receiving action on it.
              const groupActionable = isReceivingActionableGroupStatus(group.status);
              const isGroupReadOnly = isReadOnly || !groupActionable;
              // v2.245.0 §15: a group that is not yet in a valid receiving phase (and whose request is not
              // terminal) gets an explicit read-only blocker instead of a silently dead card.
              const showPhaseBlocker = !isReadOnly && !groupActionable;
              // v2.245.0 duplicate-confirm fix (REQ-06/07/2026-023): the CONFIRM action is a one-time
              // attestation — offered only from a PRE-confirmation status AND once every item is received,
              // and NEVER once the group is already confirmed (WAITING_RECEIPT). Distinct from item-receipt
              // access (isGroupReadOnly).
              const groupAllReceived = groupItems.length > 0 && groupItems.every((i: any) => i.statusCode === 'RECEIVED');
              const receivedCount = groupItems.filter((i: any) => i.statusCode === 'RECEIVED').length;
              const groupConfirmed = isReceivingConfirmed(group.status);
              const showConfirmButton = !isReadOnly && canConfirmReceiving(group.status, groupAllReceived);
              // Three canonical states: (1) confirmed → next guidance is the fiscal receipt/finalization;
              // (2) all received but not yet confirmed → prompt to confirm; (3) still receiving → progress.
              const groupHint = groupConfirmed
                ? 'Recebimento confirmado. Anexar recibo do fornecedor e finalizar pedido.'
                : groupAllReceived
                ? 'Recebimento completo — confirme o recebimento.'
                : `${receivedCount} de ${groupItems.length} itens recebidos. Existem quantidades pendentes.`;

              return (
                <div key={group.id} className={isHighlighted ? 'section-attention-highlight' : ''} style={cardStyle}>
                  <div style={sectionHeaderStyle}>
                    <h3 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 900, textTransform: 'uppercase', display: 'flex', alignItems: 'center', gap: '10px' }}>
                      <FileText size={18} color="var(--color-primary)" />
                      Conferência: {group.supplierNameSnapshot}
                    </h3>
                    <div style={{ display: 'flex', gap: '8px' }}>
                      {isQuotationFlow && (
                        <div style={{ fontSize: '0.7rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-primary)', display: 'flex', alignItems: 'center', gap: '6px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.1)', padding: '4px 8px', border: '1px solid var(--color-primary)' }}>
                           <Info size={12} /> Cotação Vencedora
                        </div>
                      )}
                      <div style={{ fontSize: '0.7rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-primary)', display: 'flex', alignItems: 'center', gap: '6px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.1)', padding: '4px 8px', border: '1px solid var(--color-primary)' }}>
                         <Info size={12} /> Status: {group.statusName || group.status}
                      </div>
                      {!isGroupReadOnly && (
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                          <span style={{ fontSize: '0.68rem', fontWeight: 700, color: groupConfirmed ? 'var(--color-primary)' : groupAllReceived ? 'var(--color-status-green, #16a34a)' : 'var(--color-text-muted)' }}>
                            {groupHint}
                          </span>
                          {showConfirmButton && (
                            <button
                                onClick={() => handleFinalizeClick(group.id, group.supplierNameSnapshot)}
                                className="btn-primary"
                                style={{ display: 'flex', alignItems: 'center', gap: '6px', height: '28px', padding: '0 12px', fontSize: '0.7rem' }}
                            >
                                <CheckCircle size={14} />
                                CONFIRMAR RECEBIMENTO
                            </button>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                  {showPhaseBlocker && (
                    <div style={{ margin: '16px 24px 0', padding: '12px 16px', backgroundColor: '#FFFBEB', border: '1px solid #FCD34D', borderRadius: 'var(--radius-sm)', display: 'flex', alignItems: 'flex-start', gap: '10px' }}>
                      <AlertTriangle size={16} style={{ color: '#B45309', flexShrink: 0, marginTop: '2px' }} />
                      <div style={{ fontSize: '0.8rem', fontWeight: 700, color: '#92400E' }}>
                        {RECEIVING_PHASE_BLOCKER}
                        <span style={{ display: 'block', fontWeight: 600, marginTop: '2px' }}>
                          Status atual do grupo: {group.statusName || group.status}. Não é possível registar recebimento nesta fase.
                        </span>
                      </div>
                    </div>
                  )}
                  <div style={{ overflowX: 'auto' }}>
                    <StandardTable>
                      <thead>
                        <tr style={{ backgroundColor: '#FAFAFA', borderBottom: '1px solid var(--color-border)' }}>
                          <th style={{ width: '50px', textAlign: 'center', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em' }}>#</th>
                          <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Descrição</th>
                          <th style={{ width: '150px', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Qtd Autorizada</th>
                          <th style={{ width: '150px', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Qtd Recebida</th>
                          <th style={{ width: '160px', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Status</th>
                          <th style={{ width: '140px', textAlign: 'center', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em' }}>Ação</th>
                        </tr>
                      </thead>
                      <tbody>
                        {groupItems.map((item: any) => (
                          <tr key={item.id || item.lineNumber}>
                            <td style={{ textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 800, padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>{item.lineNumber}</td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                              <div style={{ fontWeight: 800, textTransform: 'uppercase', fontSize: '0.85rem' }}>{item.description}</div>
                              {item.notes && (
                                 <div style={{ marginTop: '8px', padding: '8px', backgroundColor: '#fff7ed', border: '1px solid #fed7aa', color: '#9a3412', fontSize: '0.75rem', fontWeight: 700, display: 'flex', gap: '6px', borderRadius: '4px' }}>
                                   <AlertTriangle size={14} style={{ flexShrink: 0 }} />
                                   <span>OBS: {item.notes}</span>
                                 </div>
                              )}
                            </td>
                            <td style={{ fontWeight: 700, color: 'var(--color-text-muted)', padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                              {item.quantity} {item.unit}
                            </td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                              <span style={{ fontWeight: 900, color: item.receivedQty > 0 ? 'var(--color-primary)' : 'var(--color-text-muted)' }}>
                                {item.receivedQty} {item.unit}
                              </span>
                            </td>
                            <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                              <div 
                                className={`badge ${item.statusCode === 'RECEIVED' ? 'badge-success' : 'badge-warning'}`}
                                style={{ width: '100%', justifyContent: 'center', fontSize: '0.6rem', padding: '2px 8px' }}
                              >
                                {item.statusName}
                              </div>
                            </td>
                            <td style={{ textAlign: 'center', padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                              <button
                                onClick={() => handleOpenModal(item)}
                                className="btn-secondary"
                                style={{ padding: '6px 12px', fontSize: '0.7rem', width: '100%', opacity: isGroupReadOnly ? 0.7 : 1, borderRadius: '6px' }}
                              >
                                {isGroupReadOnly ? 'VER DETALHES' : 'REGISTRAR'}
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </StandardTable>
                  </div>
                </div>
              );
            })
          ) : (
            <div className={isHighlighted ? 'section-attention-highlight' : ''} style={cardStyle}>
              <div style={sectionHeaderStyle}>
                <h3 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 900, textTransform: 'uppercase', display: 'flex', alignItems: 'center', gap: '10px' }}>
                  <FileText size={18} color="var(--color-primary)" />
                  Conferência de Itens Autorizados
                </h3>
                {isQuotationFlow && (
                  <div style={{ fontSize: '0.7rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-primary)', display: 'flex', alignItems: 'center', gap: '6px', backgroundColor: 'rgba(var(--color-primary-rgb), 0.1)', padding: '4px 8px', border: '1px solid var(--color-primary)' }}>
                     <Info size={12} /> Baseado na Cotação Vencedora
                  </div>
                )}
              </div>
  
              <div style={{ overflowX: 'auto' }}>
                <StandardTable>
                  <thead>
                    <tr style={{ backgroundColor: '#FAFAFA', borderBottom: '1px solid var(--color-border)' }}>
                      <th style={{ width: '50px', textAlign: 'center', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em' }}>#</th>
                      <th style={{ padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Descrição</th>
                      <th style={{ width: '150px', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Qtd Autorizada</th>
                      <th style={{ width: '150px', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Qtd Recebida</th>
                      <th style={{ width: '160px', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em', textAlign: 'left' }}>Status</th>
                      <th style={{ width: '140px', textAlign: 'center', padding: '14px 20px', fontSize: '0.65rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.08em' }}>Ação</th>
                    </tr>
                  </thead>
                  <tbody>
                    {operationalItems.map((item: any) => (
                      <tr key={item.id || item.lineNumber}>
                        <td style={{ textAlign: 'center', color: 'var(--color-text-muted)', fontWeight: 800, padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>{item.lineNumber}</td>
                        <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                          <div style={{ fontWeight: 800, textTransform: 'uppercase', fontSize: '0.85rem' }}>{item.description}</div>
                          {item.notes && (
                             <div style={{ marginTop: '8px', padding: '8px', backgroundColor: '#fff7ed', border: '1px solid #fed7aa', color: '#9a3412', fontSize: '0.75rem', fontWeight: 700, display: 'flex', gap: '6px', borderRadius: '4px' }}>
                               <AlertTriangle size={14} style={{ flexShrink: 0 }} />
                               <span>OBS: {item.notes}</span>
                             </div>
                          )}
                        </td>
                        <td style={{ fontWeight: 700, color: 'var(--color-text-muted)', padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                          {item.quantity} {item.unit}
                        </td>
                        <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                          <span style={{ fontWeight: 900, color: item.receivedQty > 0 ? 'var(--color-primary)' : 'var(--color-text-muted)' }}>
                            {item.receivedQty} {item.unit}
                          </span>
                        </td>
                        <td style={{ padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                          <div 
                            className={`badge ${item.statusCode === 'RECEIVED' ? 'badge-success' : 'badge-warning'}`}
                            style={{ width: '100%', justifyContent: 'center', fontSize: '0.6rem', padding: '2px 8px' }}
                          >
                            {item.statusName}
                          </div>
                        </td>
                        <td style={{ textAlign: 'center', padding: '12px 20px', borderBottom: '1px solid var(--color-border)' }}>
                          <button
                            onClick={() => handleOpenModal(item)}
                            className="btn-secondary"
                            style={{ padding: '6px 12px', fontSize: '0.7rem', width: '100%', opacity: isReadOnly ? 0.7 : 1, borderRadius: '6px' }}
                          >
                            {isReadOnly ? 'VER DETALHES' : 'REGISTRAR'}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </StandardTable>
              </div>
            </div>
          )}

          <div style={{ marginTop: '24px' }}>
            <RequestAttachments
              requestId={request.id}
              attachments={request.attachments || []}
              canEdit={!isReadOnly}
              onRefresh={fetchRequest}
              requestType={request.requestTypeCode}
              status={request.statusCode}
            />
          </div>
        </div>

        {/* Sidebar */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
          {/* Progress Card */}
          <div style={cardStyle}>
            <div style={{ ...sectionHeaderStyle, padding: '12px 16px' }}>
              <h3 style={{ margin: 0, fontSize: '0.8rem', fontWeight: 900, textTransform: 'uppercase' }}>Progresso</h3>
            </div>
            <div style={{ padding: '20px' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.7rem', fontWeight: 900, color: 'var(--color-text-muted)', textTransform: 'uppercase', marginBottom: '12px' }}>
                  <span>Conferência</span>
                  <span>{operationalItems.filter((i: any) => i.statusCode === 'RECEIVED').length} / {operationalItems.length} ITENS</span>
              </div>
              <div style={{ width: '100%', backgroundColor: 'var(--color-bg-page)', height: '10px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', display: 'flex', padding: '2px', overflow: 'hidden' }}>
                  {operationalItems.map((item: any) => (
                      <div 
                          key={item.id} 
                          style={{ 
                              height: '100%', 
                              backgroundColor: item.statusCode === 'RECEIVED' ? 'var(--color-primary)' : 'transparent',
                              borderRight: '1px solid var(--color-bg-page)',
                              flex: 1
                          }}
                      />
                  ))}
              </div>
            </div>
          </div>

          {/* History Card */}
          <div style={cardStyle}>
            <div style={{ ...sectionHeaderStyle, padding: '12px 16px' }}>
              <h3 style={{ margin: 0, fontSize: '0.8rem', fontWeight: 900, textTransform: 'uppercase', display: 'flex', alignItems: 'center', gap: '8px' }}>
                <History size={16} /> Histórico Ativo
              </h3>
            </div>
            <div style={{ padding: '20px', maxHeight: '500px', overflowY: 'auto' }}>
              {request.statusHistory?.filter((h: any) => 
                h.actionTaken === 'RECEIVING_PROGRESS' || 
                h.actionTaken === 'ITEM_STATUS_CHANGE' || 
                h.actionTaken === 'ITEM_RECEIVING_REGISTRATION' ||
                h.actionTaken === 'FINALIZE'
              ).length > 0 ? (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', position: 'relative' }}>
                    <div style={{ position: 'absolute', left: '7px', top: '0', bottom: '0', width: '2px', backgroundColor: 'var(--color-border)' }} />
                    {request.statusHistory
                      ?.filter((h: any) => 
                        h.actionTaken === 'RECEIVING_PROGRESS' || 
                        h.actionTaken === 'ITEM_STATUS_CHANGE' || 
                        h.actionTaken === 'ITEM_RECEIVING_REGISTRATION' ||
                        h.actionTaken === 'FINALIZE'
                      )
                      .map((h: any, idx: number) => (
                          <div key={idx} style={{ position: 'relative', paddingLeft: '28px' }}>
                             <div style={{ position: 'absolute', left: '0', top: '4px', width: '16px', height: '16px', borderRadius: '50%', backgroundColor: 'var(--color-bg-surface)', border: '3px solid var(--color-primary)', zIndex: 1 }} />
                             <div style={{ fontSize: '0.65rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase', marginBottom: '4px' }}>
                                 {new Date(h.createdAtUtc).toLocaleString('pt-AO')}
                             </div>
                             <div style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-main)', marginBottom: '4px', lineHeight: '1.4' }}>
                                 {h.comment}
                             </div>
                             <div style={{ fontSize: '0.65rem', color: 'var(--color-text-muted)', fontWeight: 800, textTransform: 'uppercase' }}>
                                 RESP: {h.actorName}
                             </div>
                          </div>
                      ))}
                  </div>
              ) : (
                <div style={{ textAlign: 'center', padding: '40px 0' }}>
                  <History size={32} style={{ color: 'var(--color-border)', marginBottom: '12px', opacity: 0.5 }} />
                  <p style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontWeight: 800, textTransform: 'uppercase', margin: 0 }}>
                      Nenhum registro de conferência.
                  </p>
                </div>
              )}
            </div>
          </div>
        </div>
      </div>

      {selectedItem && (() => {
        // v2.245.0 §15: never allow the item-receipt action for a group whose status is not a valid
        // receiving phase — the backend rejects it. Groups exist ⇒ gate on the item's own group status;
        // the legacy no-group branch keeps request-level read-only only.
        const selectedGroupId = operationalItems.find((i: any) => i.id === selectedItem.id)?.requestPoGroupId;
        const selectedGroup = selectedGroupId ? request.poGroups?.find((g: any) => g.id === selectedGroupId) : undefined;
        const selectedItemReadOnly = isReadOnly || (!!selectedGroup && !isReceivingActionableGroupStatus(selectedGroup.status));
        return (
          <ReceivingModal
            open={modalOpen}
            onClose={() => setModalOpen(false)}
            onConfirm={handleConfirmReceiving}
            itemDescription={selectedItem.description}
            authorizedQty={selectedItem.quantity}
            currentReceivedQty={selectedItem.receivedQty}
            unit={selectedItem.unit}
            readOnly={selectedItemReadOnly}
          />
        );
      })()}

      <FinalizeReceivingModal
        requestId={request.id}
        requestNumber={request.requestNumber || ''}
        groupId={finalizeModalState.groupId || ''}
        groupName={finalizeModalState.groupName}
        attachments={request.attachments || []}
        show={finalizeModalState.show}
        onClose={() => setFinalizeModalState({ show: false, groupId: null })}
        onSuccess={(msg) => {
            setFinalizeModalState({ show: false, groupId: null });
            if (msg) {
                navigate('/receiving/workspace', { state: { successMessage: msg } });
            }
        }}
        isPartial={finalizeModalState.groupId ? operationalItems.filter(i => i.requestPoGroupId === finalizeModalState.groupId).some(i => i.statusCode !== 'RECEIVED') : !allReceived}
      />
    </motion.div>
  );
};

export default ReceivingOperation;
