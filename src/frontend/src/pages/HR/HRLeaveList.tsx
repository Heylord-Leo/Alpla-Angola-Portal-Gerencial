import React, { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { Plus, X, Calendar as CalendarIcon, Clock, CheckCircle, XCircle, Ban, ChevronLeft, ChevronRight } from 'lucide-react';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { HRActionModal, HRActionType } from '../../components/modals/HRActionModal';
import { EmployeeAutocomplete } from '../../components/EmployeeAutocomplete';
import { DateInput } from '../../components/DateInput';

export default function HRLeaveList() {
    const [records, setRecords] = useState<any[]>([]);
    const [total, setTotal] = useState(0);
    const [loading, setLoading] = useState(true);
    const [page, setPage] = useState(1);
    const [pageSize] = useState(20);
    const [statusFilter, setStatusFilter] = useState('');
    
    // Create Drawer state
    const [isDrawerOpen, setIsDrawerOpen] = useState(false);
    const [leaveTypes, setLeaveTypes] = useState<any[]>([]);
    const [employees, setEmployees] = useState<any[]>([]);
    
    // Form state
    const [formEmployeeId, setFormEmployeeId] = useState('');
    const [formLeaveTypeId, setFormLeaveTypeId] = useState('');
    const [formStartDate, setFormStartDate] = useState('');
    const [formEndDate, setFormEndDate] = useState('');
    const [formNotes, setFormNotes] = useState('');
    const [submitting, setSubmitting] = useState(false);

    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'info', message: null });
    const [actionModal, setActionModal] = useState<{ show: boolean, action: HRActionType, recordId: string | null }>({ show: false, action: null, recordId: null });
    const [modalProcessing, setModalProcessing] = useState(false);
    const [modalFeedback, setModalFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'info', message: null });

    useEffect(() => {
        loadRecords();
    }, [page, statusFilter]);

    useEffect(() => {
        if (isDrawerOpen) {
            api.hrLeave.getTypes().then(setLeaveTypes).catch(console.error);
        }
    }, [isDrawerOpen]);

    const loadRecords = () => {
        setLoading(true);
        const params: any = { page, pageSize };
        if (statusFilter) params.status = statusFilter;

        api.hrLeave.getRecords(params).then(res => {
            setRecords(res.items);
            setTotal(res.total);
            setLoading(false);
        }).catch(err => {
            console.error(err);
            setLoading(false);
        });
    };

    const handleCreate = async (e: React.FormEvent) => {
        e.preventDefault();
        
        if (!formEmployeeId) {
            setFeedback({ type: 'error', message: 'Selecione um funcionário.' });
            return;
        }

        if (formStartDate && formEndDate && new Date(formStartDate) > new Date(formEndDate)) {
            setFeedback({ type: 'error', message: 'A data de fim deve ser igual ou posterior à data de início.' });
            return;
        }

        setSubmitting(true);
        try {
            const result = await api.hrLeave.createRecord({
                employeeId: formEmployeeId,
                leaveTypeId: Number(formLeaveTypeId),
                startDate: formStartDate,
                endDate: formEndDate,
                notes: formNotes
            });
            // Try to auto-submit for approval immediately to save clicks
            try {
                await api.hrLeave.updateRecordStatus(result.id, 'submit');
            } catch (submitErr) {
                console.error("Created but failed to submit:", submitErr);
            }
            setIsDrawerOpen(false);
            setFormEmployeeId('');
            setFormLeaveTypeId('');
            setFormStartDate('');
            setFormEndDate('');
            setFormNotes('');
            setFeedback({ type: 'success', message: 'Solicitação criada com sucesso.' });
            loadRecords();
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Erro ao criar solicitação.' });
            setIsDrawerOpen(false);
        } finally {
            setSubmitting(false);
        }
    };

    const triggerAction = (id: string, action: HRActionType) => {
        setActionModal({ show: true, action, recordId: id });
    };

    const handleConfirmAction = async (action: HRActionType, payload: { notes?: string }) => {
        if (!actionModal.recordId) return;
        setModalProcessing(true);
        setModalFeedback({ type: 'info', message: null });
        
        let requestPayload: any = {};
        if (action === 'REJECT') {
            requestPayload.reason = payload.notes;
        } else if (action === 'APPROVE') {
            requestPayload.comment = 'Aprovado via lista.';
        } else if (action === 'CANCEL') {
            requestPayload.comment = 'Cancelado via lista.';
        }

        const apiAction = action === 'APPROVE' ? 'approve' : action === 'REJECT' ? 'reject' : 'cancel';

        try {
            await api.hrLeave.updateRecordStatus(actionModal.recordId, apiAction, requestPayload);
            setFeedback({ type: 'success', message: `Pedido ${apiAction === 'approve' ? 'aprovado' : apiAction === 'reject' ? 'rejeitado' : 'cancelado'} com sucesso.` });
            loadRecords();
            setActionModal({ show: false, action: null, recordId: null });
        } catch (err: any) {
            console.error('Action error', err);
            setModalFeedback({ type: 'error', message: err.message || 'Erro ao processar ação.' });
        } finally {
            setModalProcessing(false);
        }
    };

    const getStatusConfig = (code: string) => {
        switch (code) {
            case 'DRAFT': return { label: 'Rascunho', color: '#64748b', bg: '#f1f5f9', icon: <Clock size={14} /> };
            case 'SUBMITTED': return { label: 'Pendente', color: '#d97706', bg: '#fef3c7', icon: <Clock size={14} /> };
            case 'APPROVED': return { label: 'Aprovado', color: '#16a34a', bg: '#dcfce3', icon: <CheckCircle size={14} /> };
            case 'REJECTED': return { label: 'Rejeitado', color: '#dc2626', bg: '#fee2e2', icon: <XCircle size={14} /> };
            case 'CANCELLED': return { label: 'Cancelado', color: '#475569', bg: '#e2e8f0', icon: <Ban size={14} /> };
            default: return { label: code, color: '#64748b', bg: '#f1f5f9', icon: <Clock size={14} /> };
        }
    };

    const formatDate = (dateStr: string) => {
        if (!dateStr) return '';
        return new Date(dateStr).toLocaleDateString('pt-AO');
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', position: 'relative' }}>
            <Feedback 
                type={feedback.type} 
                message={feedback.message} 
                onClose={() => setFeedback({ type: 'info', message: null })} 
            />
            <HRActionModal
                show={actionModal.show}
                action={actionModal.action}
                recordId={actionModal.recordId}
                onClose={() => {
                    setActionModal({ show: false, action: null, recordId: null });
                    setModalFeedback({ type: 'info', message: null });
                }}
                onConfirm={handleConfirmAction}
                processing={modalProcessing}
                feedback={modalFeedback}
                onCloseFeedback={() => setModalFeedback({ type: 'info', message: null })}
            />

            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
                <div>
                    <h2 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 800, color: '#0f172a' }}>Férias e Ausências</h2>
                    <p style={{ margin: '4px 0 0 0', color: '#64748b', fontWeight: 500 }}>
                        Gerencie registros de ausência e fluxo de aprovação.
                    </p>
                </div>
                <div style={{ display: 'flex', gap: '12px' }}>
                    <select 
                        value={statusFilter} 
                        onChange={e => setStatusFilter(e.target.value)}
                        style={{ padding: '8px 16px', borderRadius: '8px', border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text)', fontWeight: 600, outline: 'none' }}
                    >
                        <option value="">Todos os Estados</option>
                        <option value="SUBMITTED">Pendentes</option>
                        <option value="APPROVED">Aprovados</option>
                        <option value="REJECTED">Rejeitados</option>
                    </select>
                    <button 
                        onClick={() => setIsDrawerOpen(true)}
                        style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', backgroundColor: '#0ea5e9', color: '#fff', border: 'none', borderRadius: '8px', fontWeight: 600, cursor: 'pointer', boxShadow: '0 1px 3px rgba(0,0,0,0.1)', transition: 'background-color 0.2s' }}
                        onMouseOver={(e) => e.currentTarget.style.backgroundColor = '#0284c7'}
                        onMouseOut={(e) => e.currentTarget.style.backgroundColor = '#0ea5e9'}
                    >
                        <Plus size={18} /> Novo Registo
                    </button>
                </div>
            </div>

            {/* List */}
            <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)', overflow: 'hidden' }}>
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ width: '100%', minWidth: '900px', borderCollapse: 'collapse', textAlign: 'left' }}>
                        <thead>
                            <tr style={{ backgroundColor: '#f8fafc', borderBottom: '2px solid #e2e8f0' }}>
                                <th style={{ padding: '16px', fontWeight: 700, fontSize: '13px', color: '#475569', textTransform: 'uppercase' }}>Funcionário</th>
                                <th style={{ padding: '16px', fontWeight: 700, fontSize: '13px', color: '#475569', textTransform: 'uppercase' }}>Período</th>
                                <th style={{ padding: '16px', fontWeight: 700, fontSize: '13px', color: '#475569', textTransform: 'uppercase' }}>Tipo</th>
                                <th style={{ padding: '16px', fontWeight: 700, fontSize: '13px', color: '#475569', textTransform: 'uppercase' }}>Estado</th>
                                <th style={{ padding: '16px', fontWeight: 700, fontSize: '13px', color: '#475569', textTransform: 'uppercase', textAlign: 'right' }}>Ações</th>
                            </tr>
                        </thead>
                        <tbody>
                            {loading ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: '#64748b', fontWeight: 600 }}>Carregando dados...</td>
                                </tr>
                            ) : records.length === 0 ? (
                                <tr>
                                    <td colSpan={5} style={{ padding: '40px', textAlign: 'center', color: '#64748b', fontWeight: 600 }}>Nenhum registo encontrado.</td>
                                </tr>
                            ) : records.map(r => {
                                const status = getStatusConfig(r.statusCode);
                                return (
                                    <tr key={r.id} style={{ borderBottom: '1px solid #e2e8f0', transition: 'background-color 0.1s' }} onMouseOver={e => e.currentTarget.style.backgroundColor = '#f8fafc'} onMouseOut={e => e.currentTarget.style.backgroundColor = 'transparent'}>
                                        <td style={{ padding: '16px' }}>
                                            <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                <span style={{ fontWeight: 800, color: '#0f172a', fontSize: '14px' }}>{r.employeeName}</span>
                                                <span style={{ fontSize: '12px', color: '#64748b', fontWeight: 600 }}>Matrícula: {r.employeeCode}</span>
                                            </div>
                                        </td>
                                        <td style={{ padding: '16px' }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', color: '#334155', fontWeight: 600, fontSize: '14px' }}>
                                                <CalendarIcon size={16} color="#94a3b8" />
                                                {formatDate(r.startDate)} <span style={{ color: '#94a3b8' }}>&rarr;</span> {formatDate(r.endDate)}
                                                <span style={{ display: 'inline-block', marginLeft: 8, padding: '2px 6px', backgroundColor: '#f1f5f9', borderRadius: '4px', fontSize: '12px', color: '#475569' }}>
                                                    {r.totalDays} dia(s)
                                                </span>
                                            </div>
                                        </td>
                                        <td style={{ padding: '16px' }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                <div style={{ width: '8px', height: '8px', borderRadius: '50%', backgroundColor: r.leaveTypeColor || '#94a3b8' }} />
                                                <span style={{ fontWeight: 600, fontSize: '14px', color: '#334155' }}>{r.leaveType}</span>
                                            </div>
                                        </td>
                                        <td style={{ padding: '16px' }}>
                                            <div style={{ display: 'inline-flex', alignItems: 'center', gap: '6px', backgroundColor: status.bg, color: status.color, padding: '4px 10px', borderRadius: '999px', fontWeight: 700, fontSize: '12px' }}>
                                                {status.icon}
                                                {status.label}
                                            </div>
                                        </td>
                                        <td style={{ padding: '16px', textAlign: 'right' }}>
                                            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                                                {r.statusCode === 'SUBMITTED' && (
                                                    <>
                                                        <button 
                                                            onClick={() => triggerAction(r.id, 'APPROVE')}
                                                            title="Aprovar"
                                                            style={{ width: '32px', height: '32px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '6px', border: '1px solid #86efac', backgroundColor: '#dcfce3', color: '#16a34a', cursor: 'pointer' }}
                                                        >
                                                            <CheckCircle size={16} />
                                                        </button>
                                                        <button 
                                                            onClick={() => triggerAction(r.id, 'REJECT')}
                                                            title="Rejeitar"
                                                            style={{ width: '32px', height: '32px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '6px', border: '1px solid #fca5a5', backgroundColor: '#fee2e2', color: '#dc2626', cursor: 'pointer' }}
                                                        >
                                                            <XCircle size={16} />
                                                        </button>
                                                    </>
                                                )}
                                                {(r.statusCode === 'DRAFT' || r.statusCode === 'SUBMITTED' || r.statusCode === 'APPROVED') && (
                                                    <button 
                                                        onClick={() => triggerAction(r.id, 'CANCEL')}
                                                        title="Cancelar"
                                                        style={{ width: '32px', height: '32px', display: 'flex', alignItems: 'center', justifyContent: 'center', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: '#f8fafc', color: '#64748b', cursor: 'pointer' }}
                                                    >
                                                        <Ban size={16} />
                                                    </button>
                                                )}
                                            </div>
                                        </td>
                                    </tr>
                                )
                            })}
                        </tbody>
                    </table>
                </div>
                
                {/* Pagination */}
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '16px', borderTop: '1px solid #e2e8f0', backgroundColor: '#f8fafc' }}>
                    <span style={{ fontSize: '13px', fontWeight: 600, color: '#64748b' }}>
                        Mostrando {records.length} de {total} registos
                    </span>
                    <div style={{ display: 'flex', gap: '8px' }}>
                        <button 
                            disabled={page === 1}
                            onClick={() => setPage(page - 1)}
                            style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '32px', height: '32px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: page === 1 ? '#f1f5f9' : '#fff', color: page === 1 ? '#94a3b8' : '#0f172a', cursor: page === 1 ? 'not-allowed' : 'pointer' }}
                        >
                            <ChevronLeft size={18} />
                        </button>
                        <button 
                            disabled={page * pageSize >= total}
                            onClick={() => setPage(page + 1)}
                            style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '32px', height: '32px', borderRadius: '6px', border: '1px solid #cbd5e1', backgroundColor: page * pageSize >= total ? '#f1f5f9' : '#fff', color: page * pageSize >= total ? '#94a3b8' : '#0f172a', cursor: page * pageSize >= total ? 'not-allowed' : 'pointer' }}
                        >
                            <ChevronRight size={18} />
                        </button>
                    </div>
                </div>
            </div>

            {/* Slider Drawer Overlay for New Record */}
            {isDrawerOpen && (
                <div style={{ position: 'fixed', top: 0, left: 0, right: 0, bottom: 0, zIndex: 9999, display: 'flex', justifyContent: 'flex-end', backgroundColor: 'rgba(15, 23, 42, 0.4)', backdropFilter: 'blur(2px)' }}>
                    <div style={{ width: '100%', maxWidth: '450px', backgroundColor: '#fff', height: '100%', display: 'flex', flexDirection: 'column', boxSizing: 'border-box', boxShadow: '-4px 0 15px rgba(0,0,0,0.1)', animation: 'slideIn 0.3s ease-out' }}>
                        <div style={{ padding: '24px', borderBottom: '1px solid #e2e8f0', display: 'flex', justifyContent: 'space-between', alignItems: 'center', backgroundColor: '#f8fafc', boxSizing: 'border-box' }}>
                            <h3 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 800, color: '#0f172a' }}>Nova Solicitação</h3>
                            <button onClick={() => setIsDrawerOpen(false)} style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}>
                                <X size={24} />
                            </button>
                        </div>
                        
                        <div style={{ padding: '24px', flex: 1, overflowY: 'auto', overflowX: 'hidden', boxSizing: 'border-box' }}>
                            <form id="create-leave-form" onSubmit={handleCreate} style={{ display: 'flex', flexDirection: 'column', gap: '20px', width: '100%', boxSizing: 'border-box' }}>
                                
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                    <label style={{ fontSize: '13px', fontWeight: 700, color: '#475569' }}>Funcionário *</label>
                                    <EmployeeAutocomplete 
                                        onChange={(id) => setFormEmployeeId(id || '')}
                                    />
                                </div>

                                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '16px', opacity: formEmployeeId ? 1 : 0.5, pointerEvents: formEmployeeId ? 'auto' : 'none' }}>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', flex: '1 1 150px' }}>
                                        <label style={{ fontSize: '13px', fontWeight: 700, color: '#475569' }}>Data de Início *</label>
                                        <DateInput 
                                            required 
                                            value={formStartDate} 
                                            onChange={(v) => { setFormStartDate(v); if (formEndDate && new Date(v) > new Date(formEndDate)) setFormEndDate(v); }}
                                            style={{ padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1', outline: 'none', fontWeight: 500, color: '#0f172a' }}
                                        />
                                    </div>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', flex: '1 1 150px' }}>
                                        <label style={{ fontSize: '13px', fontWeight: 700, color: '#475569' }}>Data de Fim *</label>
                                        <DateInput 
                                            required 
                                            value={formEndDate} 
                                            onChange={(v) => setFormEndDate(v)}
                                            style={{ padding: '10px', borderRadius: '8px', border: formStartDate && formEndDate && new Date(formStartDate) > new Date(formEndDate) ? '1px solid #ef4444' : '1px solid #cbd5e1', outline: 'none', fontWeight: 500, color: '#0f172a' }}
                                        />
                                        {formStartDate && formEndDate && new Date(formStartDate) > new Date(formEndDate) && (
                                            <span style={{ fontSize: '11px', color: '#ef4444', fontWeight: 600 }}>A data de fim deve ser posterior ao início.</span>
                                        )}
                                    </div>
                                </div>

                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', opacity: (formStartDate && formEndDate && new Date(formStartDate) <= new Date(formEndDate)) ? 1 : 0.5, pointerEvents: (formStartDate && formEndDate && new Date(formStartDate) <= new Date(formEndDate)) ? 'auto' : 'none' }}>
                                    <label style={{ fontSize: '13px', fontWeight: 700, color: '#475569' }}>Tipo de Ausência *</label>
                                    <select 
                                        required 
                                        value={formLeaveTypeId} 
                                        onChange={e => setFormLeaveTypeId(e.target.value)}
                                        style={{ padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1', outline: 'none', fontWeight: 500, color: '#0f172a' }}
                                    >
                                        <option value="">Selecione o tipo...</option>
                                        {leaveTypes.map(t => (
                                            <option key={t.id} value={t.id}>{t.displayNamePt}</option>
                                        ))}
                                    </select>
                                </div>

                                <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', opacity: formLeaveTypeId ? 1 : 0.5, pointerEvents: formLeaveTypeId ? 'auto' : 'none' }}>
                                    <label style={{ fontSize: '13px', fontWeight: 700, color: '#475569' }}>Observações / Motivo</label>
                                    <textarea 
                                        rows={4}
                                        value={formNotes} 
                                        onChange={e => setFormNotes(e.target.value)}
                                        placeholder="Opcional. Adicione informações relevantes."
                                        style={{ padding: '10px', borderRadius: '8px', border: '1px solid #cbd5e1', outline: 'none', fontWeight: 500, color: '#0f172a', resize: 'vertical' }}
                                    />
                                </div>
                            </form>
                        </div>
                        
                        <div style={{ padding: '24px', borderTop: '1px solid #e2e8f0', display: 'flex', justifyContent: 'flex-end', flexWrap: 'wrap', gap: '12px', backgroundColor: '#f8fafc', boxSizing: 'border-box' }}>
                            <button 
                                type="button"
                                onClick={() => setIsDrawerOpen(false)}
                                style={{ padding: '10px 16px', borderRadius: '8px', border: '1px solid #cbd5e1', backgroundColor: '#fff', color: '#475569', fontWeight: 600, cursor: 'pointer' }}
                            >
                                Cancelar
                            </button>
                            <button 
                                form="create-leave-form"
                                type="submit"
                                disabled={submitting || !formEmployeeId || !formStartDate || !formEndDate || !formLeaveTypeId || new Date(formStartDate) > new Date(formEndDate)}
                                style={{ padding: '10px 24px', borderRadius: '8px', border: 'none', backgroundColor: (submitting || !formEmployeeId || !formStartDate || !formEndDate || !formLeaveTypeId || new Date(formStartDate) > new Date(formEndDate)) ? '#cbd5e1' : '#0ea5e9', color: '#fff', fontWeight: 700, cursor: (submitting || !formEmployeeId || !formStartDate || !formEndDate || !formLeaveTypeId || new Date(formStartDate) > new Date(formEndDate)) ? 'not-allowed' : 'pointer', transition: 'all 0.2s' }}
                            >
                                {submitting ? 'A submeter...' : 'Criar Solicitação'}
                            </button>
                        </div>
                    </div>
                </div>
            )}
            
            <style dangerouslySetInnerHTML={{__html: `
                @keyframes slideIn {
                    from { transform: translateX(100%); }
                    to { transform: translateX(0); }
                }
            `}} />
        </div>
    );
}
