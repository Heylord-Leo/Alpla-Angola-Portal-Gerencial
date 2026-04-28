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
import { RequestDetailsDto } from '../../types';
import ReceivingModal from '../../components/modals/ReceivingModal';
import { FeedbackType } from '../../components/ui/Feedback';
import { RequestActionHeader } from '../Requests/components/RequestActionHeader';
import { ApprovalModal } from '../../components/ApprovalModal';
import { PageContainer } from '../../components/ui/PageContainer';
import { StandardTable } from '../../components/ui/StandardTable';

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

  const [finalizeModalOpen, setFinalizeModalOpen] = useState(false);
  const [finalizeComment, setFinalizeComment] = useState('');
  const [finalizeProcessing, setFinalizeProcessing] = useState(false);
  const [finalizeFeedback, setFinalizeFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });

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
      ? winningQuotation.items.map((qi: any) => ({
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
          type: 'QUOTATION_ITEM' as const
        }))
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
          type: 'LINE_ITEM' as const
        }));
  }, [request, winningQuotation]);

  const allReceived = operationalItems.every((item: any) => item.statusCode === 'RECEIVED');
  const isReadOnly = request?.statusCode === 'COMPLETED' || request?.statusCode === 'CANCELLED';

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
    } catch (err) {
      console.error('Error updating receiving:', err);
      setFeedback({ type: 'error', message: 'Falha ao atualizar o recebimento do item.' });
    }
  };

  const handleFinalizeClick = () => {
    setFinalizeModalOpen(true);
  };

  const handleConfirmFinalize = async () => {
    try {
        setFinalizeProcessing(true);
        await api.requests.confirmReceiving(request!.id, finalizeComment);
        setFinalizeModalOpen(false);
        navigate('/receiving/workspace', { state: { successMessage: 'Recebimento confirmado com sucesso.' } });
    } catch (err: any) {
        console.error('Error confirming receiving:', err);
        setFinalizeFeedback({ type: 'error', message: err.message || 'Falha ao confirmar recebimento.' });
    } finally {
        setFinalizeProcessing(false);
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
    <PageContainer padding="24px 32px">
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
            !isReadOnly && (
                <button
                    onClick={handleFinalizeClick}
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

      {selectedItem && (
        <ReceivingModal
          open={modalOpen}
          onClose={() => setModalOpen(false)}
          onConfirm={handleConfirmReceiving}
          itemDescription={selectedItem.description}
          authorizedQty={selectedItem.quantity}
          currentReceivedQty={selectedItem.receivedQty}
          unit={selectedItem.unit}
          readOnly={isReadOnly}
        />
      )}

      <ApprovalModal
        show={finalizeModalOpen}
        type="CONFIRM_RECEIVING"
        onClose={() => {
            setFinalizeModalOpen(false);
            setFinalizeFeedback({ type: 'success', message: null });
        }}
        onConfirm={handleConfirmFinalize}
        comment={finalizeComment}
        setComment={setFinalizeComment}
        processing={finalizeProcessing}
        feedback={finalizeFeedback}
        onCloseFeedback={() => setFinalizeFeedback({ ...finalizeFeedback, message: null })}
        isPartial={!allReceived}
      />
    </PageContainer>
  );
};

export default ReceivingOperation;
