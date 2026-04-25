import React, { useEffect, useState, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { api } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import './SupplierFicha.css';

const STATUS_LABELS: Record<string, string> = {
  DRAFT: 'Rascunho',
  PENDING_COMPLETION: 'Pendente de Preenchimento',
  PENDING_APPROVAL: 'Pendente de Aprovação',
  ADJUSTMENT_REQUESTED: 'Reajuste Solicitado',
  ACTIVE: 'Ativo',
  SUSPENDED: 'Suspenso',
  BLOCKED: 'Bloqueado',
};

const DOC_TYPE_LABELS: Record<string, string> = {
  CONTRIBUINTE: 'Contribuinte',
  CERTIDAO_COMERCIAL: 'Certidão Comercial',
  DIARIO_REPUBLICA: 'Diário da República',
  ALVARA: 'Alvará',
};

const EVENT_LABELS: Record<string, string> = {
  CREATED: 'Criado',
  FIELD_UPDATED: 'Campo Atualizado',
  STATUS_CHANGED: 'Estado Alterado',
  DOCUMENT_UPLOADED: 'Documento Enviado',
  DOCUMENT_REMOVED: 'Documento Removido',
  SUBMITTED_FOR_APPROVAL: 'Submetido para Aprovação',
  DAF_APPROVED: 'Transição Interna do Sistema',
  DG_APPROVED: 'Aprovação Final',
  ADJUSTMENT_REQUESTED: 'Reajuste Solicitado',
  ACTIVATED: 'Ativado',
  SUSPENDED: 'Suspenso',
  BLOCKED: 'Bloqueado',
  REACTIVATED: 'Reativado',
};

const REQUIRED_DOC_TYPES = ['CONTRIBUINTE', 'CERTIDAO_COMERCIAL', 'DIARIO_REPUBLICA', 'ALVARA'];

interface SupplierFichaData {
  id: number;
  portalCode: string;
  primaveraCode: string | null;
  name: string;
  taxId: string | null;
  address: string | null;
  registrationStatus: string;
  origin: string | null;
  isActive: boolean;
  contactName1: string | null;
  contactRole1: string | null;
  contactPhone1: string | null;
  contactEmail1: string | null;
  contactName2: string | null;
  contactRole2: string | null;
  contactPhone2: string | null;
  contactEmail2: string | null;
  bankAccountNumber: string | null;
  bankIban: string | null;
  bankSwift: string | null;
  paymentTerms: string | null;
  paymentMethod: string | null;
  createdAtUtc: string;
  createdByUserId: string | null;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  notes: string | null;
  documents: Array<{
    id: string;
    documentType: string;
    fileName: string;
    contentType: string | null;
    fileSizeBytes: number;
    uploadedAtUtc: string;
    uploadedByUserName: string;
  }>;
  requiredDocTypes: string[];
  uploadedDocTypes: string[];
  // Phase 2 approval fields
  dafApproverId: string | null;
  dafApprovedAtUtc: string | null;
  dgApproverId: string | null;
  dgApprovedAtUtc: string | null;
  submittedByUserId: string | null;
  submittedAtUtc: string | null;
  adjustmentComment: string | null;
}

interface CompletenessReport {
  totalChecks: number;
  completedChecks: number;
  completionPercentage: number;
  isComplete: boolean;
  missingItems: string[];
  categories: Array<{
    category: string;
    label: string;
    totalItems: number;
    completedItems: number;
    isComplete: boolean;
    missingItems: string[];
  }>;
  hasAlvara: boolean;
  alvaraNote: string | null;
}

interface HistoryEntry {
  id: string;
  eventType: string;
  fromStatusCode: string | null;
  toStatusCode: string | null;
  comment: string;
  occurredAtUtc: string;
  actorUserId: string;
  actorName: string;
}

const SupplierFichaDetail: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [ficha, setFicha] = useState<SupplierFichaData | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [editMode, setEditMode] = useState(false);
  const [formData, setFormData] = useState<any>({});
  const [feedback, setFeedback] = useState<{ message: string | null; type: FeedbackType }>({ message: null, type: 'success' });
  const [uploadingDoc, setUploadingDoc] = useState<string | null>(null);
  const fileInputRefs = useRef<Record<string, HTMLInputElement | null>>({});
  const [completeness, setCompleteness] = useState<CompletenessReport | null>(null);
  const [history, setHistory] = useState<HistoryEntry[]>([]);
  const [showHistory, setShowHistory] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [confirmModal, setConfirmModal] = useState<{
    title: string;
    body: string;
    confirmLabel: string;
    confirmVariant: 'primary' | 'success' | 'warning' | 'danger';
    onConfirm: () => void;
  } | null>(null);

  const fetchFicha = async () => {
    if (!id) return;
    setLoading(true);
    try {
      const data = await api.lookups.getSupplierFicha(Number(id));
      setFicha(data);
      setFormData({
        name: data.name || '',
        taxId: data.taxId || '',
        primaveraCode: data.primaveraCode || '',
        address: data.address || '',
        contactName1: data.contactName1 || '',
        contactRole1: data.contactRole1 || '',
        contactPhone1: data.contactPhone1 || '',
        contactEmail1: data.contactEmail1 || '',
        contactName2: data.contactName2 || '',
        contactRole2: data.contactRole2 || '',
        contactPhone2: data.contactPhone2 || '',
        contactEmail2: data.contactEmail2 || '',
        bankAccountNumber: data.bankAccountNumber || '',
        bankIban: data.bankIban || '',
        bankSwift: data.bankSwift || '',
        paymentTerms: data.paymentTerms || '',
        paymentMethod: data.paymentMethod || '',
        notes: data.notes || '',
      });
    } catch (err) {
      console.error('Error loading ficha:', err);
      showToast('Erro ao carregar ficha do fornecedor.', 'error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchFicha();
  }, [id]);

  useEffect(() => {
    if (ficha) fetchCompleteness();
  }, [ficha?.id]);

  const fetchCompleteness = async () => {
    if (!id) return;
    try {
      const data = await api.lookups.getSupplierCompleteness(Number(id));
      setCompleteness(data);
    } catch { /* silent */ }
  };

  const fetchHistory = async () => {
    if (!id) return;
    try {
      const data = await api.lookups.getSupplierHistory(Number(id));
      setHistory(data);
    } catch { /* silent */ }
  };

  const showToast = (message: string, type: FeedbackType) => {
    setFeedback({ message, type });
  };

  const handleSubmitForApproval = async () => {
    if (!ficha) return;
    setConfirmModal({
      title: 'Submeter Ficha para Aprovação',
      body: 'A ficha será submetida ao Aprovador Final. Depois de submetida, não poderá ser editada até à decisão.',
      confirmLabel: 'Submeter para Aprovação',
      confirmVariant: 'primary',
      onConfirm: async () => {
        setSubmitting(true);
        try {
          await api.lookups.submitSupplierForApproval(ficha.id);
          showToast('Ficha submetida para aprovação.', 'success');
          await fetchFicha();
          fetchCompleteness();
        } catch (err: any) {
          const detail = err?.response?.data?.detail || err?.message || 'Erro ao submeter.';
          showToast(detail, 'error');
        } finally {
          setSubmitting(false);
        }
      },
    });
  };



  const handleInputChange = (field: string, value: string) => {
    setFormData((prev: any) => ({ ...prev, [field]: value }));
  };

  const handleSave = async () => {
    if (!ficha) return;
    setSaving(true);
    try {
      await api.lookups.updateSupplierFicha(ficha.id, formData);
      showToast('Ficha atualizada com sucesso.', 'success');
      setEditMode(false);
      await fetchFicha();
      fetchCompleteness();
    } catch (err: any) {
      showToast(err?.message || 'Erro ao salvar ficha.', 'error');
    } finally {
      setSaving(false);
    }
  };

  const handleStatusChange = async (newStatus: string) => {
    if (!ficha) return;
    const variantMap: Record<string, 'warning' | 'danger' | 'success'> = {
      SUSPENDED: 'warning', BLOCKED: 'danger', ACTIVE: 'success',
    };
    setConfirmModal({
      title: `${STATUS_LABELS[newStatus] || newStatus}`,
      body: `Deseja alterar o estado do fornecedor "${ficha.name}" para "${STATUS_LABELS[newStatus]}"?`,
      confirmLabel: `Confirmar ${STATUS_LABELS[newStatus]}`,
      confirmVariant: variantMap[newStatus] || 'primary',
      onConfirm: async () => {
        try {
          await api.lookups.updateSupplierStatus(ficha.id, newStatus);
          showToast(`Estado alterado para ${STATUS_LABELS[newStatus]}.`, 'success');
          await fetchFicha();
          fetchCompleteness();
        } catch (err: any) {
          showToast(err?.message || 'Erro ao alterar estado.', 'error');
        }
      },
    });
  };

  const handleFileUpload = async (docType: string, file: File) => {
    if (!ficha) return;
    setUploadingDoc(docType);
    try {
      await api.lookups.uploadSupplierDocument(ficha.id, file, docType);
      showToast(`Documento "${DOC_TYPE_LABELS[docType]}" enviado com sucesso.`, 'success');
      await fetchFicha();
      fetchCompleteness();
    } catch (err: any) {
      showToast(err?.message || 'Erro ao enviar documento.', 'error');
    } finally {
      setUploadingDoc(null);
    }
  };

  const handleFileDownload = async (doc: SupplierFichaData['documents'][0]) => {
    try {
      const blob = await api.lookups.downloadSupplierDocument(doc.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = doc.fileName;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      showToast('Erro ao baixar documento.', 'error');
    }
  };

  const handleFileDelete = async (docId: string, docType: string) => {
    setConfirmModal({
      title: 'Remover Documento',
      body: `Confirma a remoção do documento "${DOC_TYPE_LABELS[docType]}"? Esta ação não pode ser desfeita.`,
      confirmLabel: 'Confirmar Remoção',
      confirmVariant: 'danger',
      onConfirm: async () => {
        try {
          await api.lookups.deleteSupplierDocument(docId);
          showToast('Documento removido.', 'success');
          await fetchFicha();
          fetchCompleteness();
        } catch {
          showToast('Erro ao remover documento.', 'error');
        }
      },
    });
  };

  const getStatusActions = () => {
    if (!ficha) return [];
    const status = ficha.registrationStatus;
    const actions: Array<{ label: string; status: string; variant: string; handler?: () => void }> = [];

    switch (status) {
      case 'DRAFT':
      case 'PENDING_COMPLETION':
        // No status-change actions — user fills data and submits for approval
        break;
      case 'ADJUSTMENT_REQUESTED':
        // Resubmit handled by submit-approval button in checklist panel
        break;
      case 'ACTIVE':
        actions.push({ label: 'Suspender', status: 'SUSPENDED', variant: 'warning' });
        actions.push({ label: 'Bloquear', status: 'BLOCKED', variant: 'danger' });
        break;
      case 'SUSPENDED':
        actions.push({ label: 'Reativar', status: 'ACTIVE', variant: 'success' });
        actions.push({ label: 'Bloquear', status: 'BLOCKED', variant: 'danger' });
        break;
      case 'BLOCKED':
        actions.push({ label: 'Reativar', status: 'ACTIVE', variant: 'success' });
        break;
    }
    return actions;
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  if (loading) {
    return (
      <div className="ficha-detail-loading">
        <div className="ficha-loading-spinner" />
        <p>Carregando ficha...</p>
      </div>
    );
  }

  if (!ficha) {
    return (
      <div className="ficha-detail-loading">
        <p>Fornecedor não encontrado.</p>
        <button className="ficha-btn ficha-btn--secondary" onClick={() => navigate('/contracts/fichas')}>Voltar</button>
      </div>
    );
  }

  const docCompleteness = REQUIRED_DOC_TYPES.filter(t => ficha.uploadedDocTypes.includes(t)).length;

  return (
    <div className="ficha-detail-container">
      {/* Feedback */}
      <Feedback
        type={feedback.type}
        message={feedback.message}
        onClose={() => setFeedback(prev => ({ ...prev, message: null }))}
        isFixed
      />

      {/* Header */}
      <div className="ficha-detail-header">
        <button className="ficha-back-btn" onClick={() => navigate('/contracts/fichas')}>
          ← Voltar
        </button>
        <div className="ficha-detail-header-info">
          <div className="ficha-detail-title-row">
            <h1>{ficha.name}</h1>
            <span
              className="ficha-status-badge ficha-status-badge--lg"
              style={{ '--badge-color': getStatusColor(ficha.registrationStatus) } as React.CSSProperties}
            >
              {STATUS_LABELS[ficha.registrationStatus]}
            </span>
          </div>
          <div className="ficha-detail-meta">
            <span>{ficha.portalCode}</span>
            {ficha.primaveraCode && <span>Primavera: {ficha.primaveraCode}</span>}
            {ficha.origin && <span>Origem: {ficha.origin}</span>}
          </div>
        </div>
        <div className="ficha-detail-header-actions">
          {!editMode ? (
            <button className="ficha-btn ficha-btn--primary" onClick={() => setEditMode(true)}>
              ✏️ Editar
            </button>
          ) : (
            <>
              <button className="ficha-btn ficha-btn--secondary" onClick={() => { setEditMode(false); fetchFicha(); }}>
                Cancelar
              </button>
              <button className="ficha-btn ficha-btn--success" onClick={handleSave} disabled={saving}>
                {saving ? 'Salvando...' : '💾 Salvar'}
              </button>
            </>
          )}
        </div>
      </div>

      {/* Status Actions */}
      {getStatusActions().length > 0 && (
        <div className="ficha-status-actions">
          <span className="ficha-status-actions-label">Ações de Estado:</span>
          {getStatusActions().map((action) => (
            <button
              key={action.status}
              className={`ficha-btn ficha-btn--${action.variant}`}
              onClick={() => handleStatusChange(action.status)}
            >
              {action.label}
            </button>
          ))}
        </div>
      )}

      {/* Adjustment Comment Alert */}
      {ficha.registrationStatus === 'ADJUSTMENT_REQUESTED' && ficha.adjustmentComment && (
        <div className="ficha-adjustment-alert">
          <div className="ficha-adjustment-alert-icon">⚠️</div>
          <div className="ficha-adjustment-alert-content">
            <strong>Reajuste Solicitado</strong>
            <p>{ficha.adjustmentComment}</p>
          </div>
        </div>
      )}

      {/* Completeness Checklist Panel */}
      {completeness && !['ACTIVE', 'SUSPENDED', 'BLOCKED'].includes(ficha.registrationStatus) && (
        <div className="ficha-completeness-panel">
          <div className="ficha-completeness-header">
            <h3>Checklist de Preenchimento</h3>
            <div className="ficha-completeness-bar-wrap">
              <div className="ficha-completeness-bar" style={{ width: `${completeness.completionPercentage}%` }} />
            </div>
            <span className="ficha-completeness-pct">{completeness.completionPercentage}%</span>
          </div>
          <div className="ficha-completeness-categories">
            {completeness.categories.map(cat => (
              <div key={cat.category} className={`ficha-completeness-cat ${cat.isComplete ? 'ficha-completeness-cat--ok' : ''}`}>
                <div className="ficha-completeness-cat-header">
                  <span className={`ficha-completeness-icon ${cat.isComplete ? 'ok' : 'pending'}`}>
                    {cat.isComplete ? '✓' : '○'}
                  </span>
                  <span className="ficha-completeness-cat-label">{cat.label}</span>
                  <span className="ficha-completeness-cat-count">{cat.completedItems}/{cat.totalItems}</span>
                </div>
                {!cat.isComplete && cat.missingItems.length > 0 && (
                  <ul className="ficha-completeness-missing">
                    {cat.missingItems.map(item => <li key={item}>{item}</li>)}
                  </ul>
                )}
              </div>
            ))}
          </div>
          {completeness.alvaraNote && (
            <div className="ficha-completeness-alvara-note">ℹ️ {completeness.alvaraNote}</div>
          )}
          {/* Submit for Approval Button */}
          {['DRAFT', 'PENDING_COMPLETION', 'ADJUSTMENT_REQUESTED'].includes(ficha.registrationStatus) && (
            <button
              className="ficha-btn ficha-btn--approval"
              onClick={handleSubmitForApproval}
              disabled={submitting || !completeness.isComplete}
              title={!completeness.isComplete ? 'Preencha todos os campos obrigatórios antes de submeter.' : ''}
            >
              {submitting ? 'Submetendo...' : '🚀 Submeter para Aprovação'}
            </button>
          )}
        </div>
      )}

      {/* Approval status info (read-only, no action buttons) */}
      {ficha.registrationStatus === 'PENDING_APPROVAL' && (
        <div className="ficha-approval-tracker">
          <h3>Aprovação em Curso</h3>
          <div className="ficha-approval-steps">
            <div className={`ficha-approval-step ${ficha.dgApprovedAtUtc ? 'ficha-approval-step--done' : 'ficha-approval-step--pending'}`}>
              <div className="ficha-approval-step-icon">{ficha.dgApprovedAtUtc ? '✅' : '⏳'}</div>
              <div className="ficha-approval-step-info">
                <strong>Aprovador Final</strong>
                {ficha.dgApprovedAtUtc
                  ? <span className="ficha-approval-step-date">Aprovado em {new Date(ficha.dgApprovedAtUtc).toLocaleString('pt-PT')}</span>
                  : <span className="ficha-approval-step-pending">Aguardando aprovação no Centro de Aprovações</span>
                }
              </div>
            </div>
          </div>
        </div>
      )}

      {/* History Toggle */}
      <div className="ficha-history-toggle">
        <button className="ficha-btn ficha-btn--secondary ficha-btn--sm" onClick={() => { setShowHistory(!showHistory); if (!showHistory) fetchHistory(); }}>
          {showHistory ? '▾ Ocultar Histórico' : '▸ Ver Histórico'}
        </button>
      </div>
      {showHistory && history.length > 0 && (
        <div className="ficha-history-timeline">
          {history.filter(h => h.eventType !== 'DAF_APPROVED').map(h => (
            <div key={h.id} className="ficha-history-entry">
              <div className="ficha-history-dot" />
              <div className="ficha-history-content">
                <div className="ficha-history-header">
                  <span className="ficha-history-event">{EVENT_LABELS[h.eventType] || h.eventType}</span>
                  <span className="ficha-history-date">{new Date(h.occurredAtUtc).toLocaleString('pt-PT')}</span>
                </div>
                {h.fromStatusCode && h.toStatusCode && h.fromStatusCode !== h.toStatusCode && (
                  <span className="ficha-history-transition">
                    {STATUS_LABELS[h.fromStatusCode] || h.fromStatusCode} → {STATUS_LABELS[h.toStatusCode] || h.toStatusCode}
                  </span>
                )}
                {h.comment && <p className="ficha-history-comment">{h.comment}</p>}
                <span className="ficha-history-actor">por {h.actorName}</span>
              </div>
            </div>
          ))}
        </div>
      )}



      {/* Confirmation Modal */}
      {confirmModal && (
        <div className="ficha-modal-overlay" onClick={() => setConfirmModal(null)}>
          <div className="ficha-modal" onClick={e => e.stopPropagation()}>
            <h3>{confirmModal.title}</h3>
            <p>{confirmModal.body}</p>
            <div className="ficha-modal-actions">
              <button className="ficha-btn ficha-btn--secondary" onClick={() => setConfirmModal(null)}>Cancelar</button>
              <button className={`ficha-btn ficha-btn--${confirmModal.confirmVariant}`} onClick={() => { confirmModal.onConfirm(); setConfirmModal(null); }}>
                {confirmModal.confirmLabel}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Content Grid */}
      <div className="ficha-detail-grid">
        {/* Identification Section */}
        <div className="ficha-section">
          <h2 className="ficha-section-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
            Identificação
          </h2>
          <div className="ficha-field-grid">
            <FieldRow label="Denominação Social" field="name" value={formData.name} editMode={editMode} onChange={handleInputChange} required />
            <FieldRow label="NIF / Contribuinte N.º" field="taxId" value={formData.taxId} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="Código Primavera" field="primaveraCode" value={formData.primaveraCode} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="Morada" field="address" value={formData.address} editMode={editMode} onChange={handleInputChange} />
          </div>
        </div>

        {/* Contact 1 */}
        <div className="ficha-section">
          <h2 className="ficha-section-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72"/></svg>
            Contacto Principal
          </h2>
          <div className="ficha-field-grid">
            <FieldRow label="Nome" field="contactName1" value={formData.contactName1} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="Cargo" field="contactRole1" value={formData.contactRole1} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="Telemóvel" field="contactPhone1" value={formData.contactPhone1} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="E-mail" field="contactEmail1" value={formData.contactEmail1} editMode={editMode} onChange={handleInputChange} type="email" />
          </div>
        </div>

        {/* Contact 2 */}
        <div className="ficha-section">
          <h2 className="ficha-section-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72"/></svg>
            Contacto Secundário
          </h2>
          <div className="ficha-field-grid">
            <FieldRow label="Nome" field="contactName2" value={formData.contactName2} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="Cargo" field="contactRole2" value={formData.contactRole2} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="Telemóvel" field="contactPhone2" value={formData.contactPhone2} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="E-mail" field="contactEmail2" value={formData.contactEmail2} editMode={editMode} onChange={handleInputChange} type="email" />
          </div>
        </div>

        {/* Banking */}
        <div className="ficha-section">
          <h2 className="ficha-section-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>
            Dados Bancários
          </h2>
          <div className="ficha-field-grid">
            <FieldRow label="Conta N.º" field="bankAccountNumber" value={formData.bankAccountNumber} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="IBAN" field="bankIban" value={formData.bankIban} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="SWIFT" field="bankSwift" value={formData.bankSwift} editMode={editMode} onChange={handleInputChange} />
          </div>
        </div>

        {/* Commercial Terms */}
        <div className="ficha-section">
          <h2 className="ficha-section-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
            Condições Comerciais
          </h2>
          <div className="ficha-field-grid">
            <FieldRow label="Condições de Pagamento" field="paymentTerms" value={formData.paymentTerms} editMode={editMode} onChange={handleInputChange} />
            <FieldRow label="Forma de Pagamento" field="paymentMethod" value={formData.paymentMethod} editMode={editMode} onChange={handleInputChange} />
          </div>
        </div>

        {/* Notes */}
        <div className="ficha-section ficha-section--full">
          <h2 className="ficha-section-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
            Observações
          </h2>
          {editMode ? (
            <textarea
              className="ficha-textarea"
              value={formData.notes}
              onChange={(e) => handleInputChange('notes', e.target.value)}
              rows={4}
              placeholder="Observações internas sobre o fornecedor..."
            />
          ) : (
            <p className="ficha-notes-text">{ficha.notes || 'Sem observações.'}</p>
          )}
        </div>

        {/* Documents Section */}
        <div className="ficha-section ficha-section--full ficha-section--docs">
          <h2 className="ficha-section-title">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
            Documentos Obrigatórios
            <span className="ficha-doc-completeness">
              {docCompleteness}/{REQUIRED_DOC_TYPES.length} completo
            </span>
          </h2>
          <div className="ficha-doc-grid">
            {REQUIRED_DOC_TYPES.map((docType) => {
              const existingDoc = ficha.documents.find(d => d.documentType === docType);
              const isUploading = uploadingDoc === docType;

              return (
                <div key={docType} className={`ficha-doc-card ${existingDoc ? 'ficha-doc-card--uploaded' : 'ficha-doc-card--missing'}`}>
                  <div className="ficha-doc-card-header">
                    <span className="ficha-doc-type-label">{DOC_TYPE_LABELS[docType]}</span>
                    {existingDoc ? (
                      <span className="ficha-doc-status ficha-doc-status--ok">✓</span>
                    ) : (
                      <span className="ficha-doc-status ficha-doc-status--missing">Pendente</span>
                    )}
                  </div>

                  {existingDoc ? (
                    <div className="ficha-doc-card-body">
                      <p className="ficha-doc-filename">{existingDoc.fileName}</p>
                      <p className="ficha-doc-meta">
                        {formatFileSize(existingDoc.fileSizeBytes)} · {existingDoc.uploadedByUserName} · {new Date(existingDoc.uploadedAtUtc).toLocaleDateString('pt-PT')}
                      </p>
                      <div className="ficha-doc-actions">
                        <button className="ficha-doc-btn ficha-doc-btn--download" onClick={() => handleFileDownload(existingDoc)}>
                          ⬇ Download
                        </button>
                        <button className="ficha-doc-btn ficha-doc-btn--delete" onClick={() => handleFileDelete(existingDoc.id, docType)}>
                          🗑 Remover
                        </button>
                        <button
                          className="ficha-doc-btn ficha-doc-btn--replace"
                          onClick={() => fileInputRefs.current[docType]?.click()}
                        >
                          🔄 Substituir
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="ficha-doc-card-body ficha-doc-card-body--empty">
                      {isUploading ? (
                        <div className="ficha-doc-uploading">
                          <div className="ficha-loading-spinner ficha-loading-spinner--sm" />
                          <span>Enviando...</span>
                        </div>
                      ) : (
                        <button
                          className="ficha-doc-upload-btn"
                          onClick={() => fileInputRefs.current[docType]?.click()}
                        >
                          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                            <polyline points="17 8 12 3 7 8"/>
                            <line x1="12" y1="3" x2="12" y2="15"/>
                          </svg>
                          Carregar Documento
                        </button>
                      )}
                    </div>
                  )}

                  <input
                    type="file"
                    ref={el => { fileInputRefs.current[docType] = el; }}
                    style={{ display: 'none' }}
                    accept=".pdf,.jpg,.jpeg,.png,.doc,.docx"
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      if (file) handleFileUpload(docType, file);
                      e.target.value = '';
                    }}
                  />
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {/* Audit Footer */}
      <div className="ficha-audit-footer">
        <span>Criado em: {new Date(ficha.createdAtUtc).toLocaleString('pt-PT')}</span>
        {ficha.updatedAtUtc && (
          <span>Última atualização: {new Date(ficha.updatedAtUtc).toLocaleString('pt-PT')}</span>
        )}
      </div>
    </div>
  );
};

// Helper component for form field rows
const FieldRow: React.FC<{
  label: string;
  field: string;
  value: string;
  editMode: boolean;
  onChange: (field: string, value: string) => void;
  required?: boolean;
  type?: string;
}> = ({ label, field, value, editMode, onChange, required, type = 'text' }) => (
  <div className="ficha-field-row">
    <label className="ficha-field-label">
      {label}
      {required && <span className="ficha-required">*</span>}
    </label>
    {editMode ? (
      <input
        type={type}
        className="ficha-field-input"
        value={value}
        onChange={(e) => onChange(field, e.target.value)}
        required={required}
      />
    ) : (
      <span className="ficha-field-value">{value || '—'}</span>
    )}
  </div>
);

function getStatusColor(status: string): string {
  const map: Record<string, string> = {
    DRAFT: '#6b7280',
    PENDING_COMPLETION: '#f59e0b',
    PENDING_APPROVAL: '#6366f1',
    ADJUSTMENT_REQUESTED: '#f97316',
    ACTIVE: '#10b981',
    SUSPENDED: '#f97316',
    BLOCKED: '#ef4444',
  };
  return map[status] || '#6b7280';
}

export default SupplierFichaDetail;
