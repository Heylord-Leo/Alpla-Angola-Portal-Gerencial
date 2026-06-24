import React, { useEffect, useState, useCallback } from 'react';
import { api } from '../../lib/api';
import { FeedbackType } from '../../components/ui/Feedback';
import { KebabMenu } from '../../components/ui/KebabMenu';
import { Edit2, Power, PowerOff } from 'lucide-react';

interface ApConfig {
    id: number;
    companyId: number;
    companyName: string;
    email: string;
    ccEmails: string | null;
    label: string | null;
    isActive: boolean;
    notifyOnScheduled: boolean;
    notifyOnCompleted: boolean;
    createdAtUtc: string;
    updatedAtUtc: string;
}

interface ApNotificationsPanelProps {
    feedback: { message: string; type: FeedbackType } | null;
    setFeedback: (f: { message: string; type: FeedbackType } | null) => void;
    companies: { id: number; name: string; isActive?: boolean }[];
}

export function ApNotificationsPanel({ feedback: _feedback, setFeedback, companies }: ApNotificationsPanelProps) {
    const [configs, setConfigs] = useState<ApConfig[]>([]);
    const [loading, setLoading] = useState(true);
    const [editId, setEditId] = useState<number | null>(null);
    const [formData, setFormData] = useState({
        companyId: 0,
        email: '',
        ccEmails: '',
        label: '',
        notifyOnScheduled: true,
        notifyOnCompleted: true
    });

    const loadConfigs = useCallback(async () => {
        try {
            setLoading(true);
            const data = await api.apNotificationConfigs.list();
            setConfigs(data);
        } catch (err: any) {
            setFeedback({ message: err.message || 'Falha ao carregar configurações.', type: 'error' });
        } finally {
            setLoading(false);
        }
    }, [setFeedback]);

    useEffect(() => {
        loadConfigs();
    }, [loadConfigs]);

    const handleEdit = (config: ApConfig) => {
        setEditId(config.id);
        setFormData({
            companyId: config.companyId,
            email: config.email,
            ccEmails: config.ccEmails || '',
            label: config.label || '',
            notifyOnScheduled: config.notifyOnScheduled,
            notifyOnCompleted: config.notifyOnCompleted
        });
    };

    const handleCancel = () => {
        setEditId(null);
        setFormData({
            companyId: 0,
            email: '',
            ccEmails: '',
            label: '',
            notifyOnScheduled: true,
            notifyOnCompleted: true
        });
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFeedback(null);

        // Frontend validation
        if (!formData.email.trim()) {
            setFeedback({ message: 'O e-mail principal é obrigatório.', type: 'error' });
            return;
        }
        if (!formData.email.includes('@')) {
            setFeedback({ message: 'O e-mail principal não tem um formato válido.', type: 'error' });
            return;
        }

        if (formData.ccEmails.trim()) {
            const ccList = formData.ccEmails.split(/[;,]/).map(s => s.trim()).filter(Boolean);
            if (ccList.length > 10) {
                setFeedback({ message: 'O número máximo de e-mails CC é 10.', type: 'error' });
                return;
            }
            for (const cc of ccList) {
                if (!cc.includes('@')) {
                    setFeedback({ message: `O endereço CC '${cc}' não é um e-mail válido.`, type: 'error' });
                    return;
                }
            }
        }

        try {
            if (editId) {
                await api.apNotificationConfigs.update(editId, {
                    email: formData.email.trim(),
                    ccEmails: formData.ccEmails.trim() || null,
                    label: formData.label.trim() || null,
                    notifyOnScheduled: formData.notifyOnScheduled,
                    notifyOnCompleted: formData.notifyOnCompleted
                });
                setFeedback({ message: 'Configuração atualizada com sucesso.', type: 'success' });
            } else {
                if (!formData.companyId) {
                    setFeedback({ message: 'Selecione uma empresa.', type: 'error' });
                    return;
                }
                await api.apNotificationConfigs.create({
                    companyId: formData.companyId,
                    email: formData.email.trim(),
                    ccEmails: formData.ccEmails.trim() || null,
                    label: formData.label.trim() || null,
                    notifyOnScheduled: formData.notifyOnScheduled,
                    notifyOnCompleted: formData.notifyOnCompleted
                });
                setFeedback({ message: 'Configuração criada com sucesso.', type: 'success' });
            }
            handleCancel();
            await loadConfigs();
        } catch (err: any) {
            setFeedback({ message: err.message || 'Erro ao salvar configuração.', type: 'error' });
        }
    };

    const handleToggle = async (id: number) => {
        try {
            setFeedback(null);
            await api.apNotificationConfigs.toggleActive(id);
            await loadConfigs();
            setFeedback({ message: 'Estado alterado com sucesso.', type: 'success' });
        } catch (err: any) {
            setFeedback({ message: err.message || 'Erro ao alterar estado.', type: 'error' });
        }
    };

    // Companies that already have a config (filter out for create mode)
    const configuredCompanyIds = configs.map(c => c.companyId);
    const availableCompanies = editId
        ? companies
        : companies.filter(c => !configuredCompanyIds.includes(c.id));

    const inputStyle: React.CSSProperties = {
        width: '100%',
        padding: '12px',
        backgroundColor: 'white',
        border: '2px solid var(--color-border)',
        fontSize: '0.85rem',
        fontWeight: 600,
        outline: 'none'
    };

    const labelStyle: React.CSSProperties = {
        display: 'block',
        fontSize: '0.75rem',
        fontWeight: 800,
        color: 'var(--color-text-main)',
        textTransform: 'uppercase',
        marginBottom: '6px'
    };

    return (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', gap: '32px', alignItems: 'start' }}>

            {/* Form Column */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', position: 'sticky', top: 'calc(var(--header-height) + 1rem + var(--env-banner-offset, 0px))' }}>
                <div style={{
                    backgroundColor: 'var(--color-bg-surface)',
                    padding: '32px',
                    borderRadius: 'var(--radius-lg)',
                    border: '1px solid var(--color-border)'
                }}>
                    <h2 style={{
                        marginTop: 0, marginBottom: '24px', fontSize: '1.25rem', fontWeight: 900,
                        textTransform: 'uppercase', color: 'var(--color-primary)',
                        borderBottom: '2px solid var(--color-border)', paddingBottom: '12px'
                    }}>
                        {editId ? '✏️ Editar Configuração' : '➕ Nova Configuração'}
                    </h2>
                    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>

                        {/* Company Select */}
                        <div>
                            <label style={labelStyle}>Empresa <span style={{ color: 'red' }}>*</span></label>
                            <select
                                required
                                disabled={!!editId}
                                style={{ ...inputStyle, cursor: editId ? 'not-allowed' : 'pointer', backgroundColor: editId ? 'var(--color-bg-page)' : 'white' }}
                                value={formData.companyId}
                                onChange={e => setFormData({ ...formData, companyId: parseInt(e.target.value) })}
                            >
                                <option value={0}>Selecione a Empresa...</option>
                                {availableCompanies.map(c => (
                                    <option key={c.id} value={c.id}>{c.name}</option>
                                ))}
                            </select>
                        </div>

                        {/* Email */}
                        <div>
                            <label style={labelStyle}>E-mail Principal (To:) <span style={{ color: 'red' }}>*</span></label>
                            <input
                                required
                                type="email"
                                style={inputStyle}
                                value={formData.email}
                                onChange={e => setFormData({ ...formData, email: e.target.value })}
                                placeholder="accounts@alpla.com"
                            />
                        </div>

                        {/* CC Emails */}
                        <div>
                            <label style={labelStyle}>E-mails CC (Opcional)</label>
                            <input
                                type="text"
                                style={inputStyle}
                                value={formData.ccEmails}
                                onChange={e => setFormData({ ...formData, ccEmails: e.target.value })}
                                placeholder="cc1@alpla.com; cc2@alpla.com"
                            />
                            <p style={{ marginTop: '4px', fontSize: '0.65rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>Separe múltiplos e-mails por ponto-e-vírgula (;) ou vírgula (,). Máx: 10.</p>
                        </div>

                        {/* Label */}
                        <div>
                            <label style={labelStyle}>Descrição (Opcional)</label>
                            <input
                                type="text"
                                style={inputStyle}
                                value={formData.label}
                                onChange={e => setFormData({ ...formData, label: e.target.value })}
                                placeholder="Ex: AOVIA1-alpla-plasticos-accounts"
                            />
                        </div>

                        {/* Event Toggles */}
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                            <label style={labelStyle}>Notificar nos Eventos</label>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                <input
                                    id="notifyScheduled"
                                    type="checkbox"
                                    style={{ width: '18px', height: '18px', cursor: 'pointer' }}
                                    checked={formData.notifyOnScheduled}
                                    onChange={e => setFormData({ ...formData, notifyOnScheduled: e.target.checked })}
                                />
                                <label htmlFor="notifyScheduled" style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)', cursor: 'pointer' }}>
                                    Agendar Pagamento (PAYMENT_SCHEDULED)
                                </label>
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                <input
                                    id="notifyCompleted"
                                    type="checkbox"
                                    style={{ width: '18px', height: '18px', cursor: 'pointer' }}
                                    checked={formData.notifyOnCompleted}
                                    onChange={e => setFormData({ ...formData, notifyOnCompleted: e.target.checked })}
                                />
                                <label htmlFor="notifyCompleted" style={{ fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)', cursor: 'pointer' }}>
                                    Confirmar Pagamento (PAYMENT_COMPLETED)
                                </label>
                            </div>
                        </div>

                        {/* Buttons */}
                        <div style={{ display: 'flex', gap: '12px', marginTop: '8px' }}>
                            <button type="submit" style={{
                                flex: 1, padding: '14px', backgroundColor: 'var(--color-primary)',
                                color: '#fff', border: 'none', fontSize: '0.85rem', fontWeight: 900,
                                textTransform: 'uppercase', cursor: 'pointer', borderRadius: 'var(--radius-md)'
                            }}>
                                {editId ? 'Atualizar' : 'Criar'}
                            </button>
                            {editId && (
                                <button type="button" onClick={handleCancel} style={{
                                    padding: '14px 24px', backgroundColor: 'transparent',
                                    color: 'var(--color-text-muted)', border: '2px solid var(--color-border)',
                                    fontSize: '0.85rem', fontWeight: 900, textTransform: 'uppercase',
                                    cursor: 'pointer', borderRadius: 'var(--radius-md)'
                                }}>
                                    Cancelar
                                </button>
                            )}
                        </div>
                    </form>
                </div>
            </div>

            {/* Table Column */}
            <div style={{
                backgroundColor: 'var(--color-bg-surface)',
                padding: '32px',
                borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)'
            }}>
                <h2 style={{
                    marginTop: 0, marginBottom: '24px', fontSize: '1.25rem', fontWeight: 900,
                    textTransform: 'uppercase', color: 'var(--color-primary)',
                    borderBottom: '2px solid var(--color-border)', paddingBottom: '12px'
                }}>
                    📧 Configurações Ativas
                </h2>

                {loading ? (
                    <p style={{ color: 'var(--color-text-muted)' }}>A carregar...</p>
                ) : configs.length === 0 ? (
                    <p style={{ color: 'var(--color-text-muted)', fontStyle: 'italic' }}>Nenhuma configuração de Contas a Pagar registada.</p>
                ) : (
                    <div style={{ overflowX: 'auto' }}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
                            <thead>
                                <tr style={{ borderBottom: '2px solid var(--color-border)' }}>
                                    <th style={{ textAlign: 'left', padding: '10px 8px', fontWeight: 800, fontSize: '0.7rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Empresa</th>
                                    <th style={{ textAlign: 'left', padding: '10px 8px', fontWeight: 800, fontSize: '0.7rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>E-mail</th>
                                    <th style={{ textAlign: 'left', padding: '10px 8px', fontWeight: 800, fontSize: '0.7rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>CC</th>
                                    <th style={{ textAlign: 'center', padding: '10px 8px', fontWeight: 800, fontSize: '0.7rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Agendar</th>
                                    <th style={{ textAlign: 'center', padding: '10px 8px', fontWeight: 800, fontSize: '0.7rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Pago</th>
                                    <th style={{ textAlign: 'center', padding: '10px 8px', fontWeight: 800, fontSize: '0.7rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Estado</th>
                                    <th style={{ textAlign: 'right', padding: '10px 8px', fontWeight: 800, fontSize: '0.7rem', textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Ações</th>
                                </tr>
                            </thead>
                            <tbody>
                                {configs.map(config => (
                                    <tr key={config.id} style={{ borderBottom: '1px solid var(--color-border)', opacity: config.isActive ? 1 : 0.5 }}>
                                        <td style={{ padding: '12px 8px', fontWeight: 700 }}>{config.companyName}</td>
                                        <td style={{ padding: '12px 8px', fontFamily: 'monospace', fontSize: '0.8rem' }}>{config.email}</td>
                                        <td style={{ padding: '12px 8px', fontFamily: 'monospace', fontSize: '0.75rem', color: 'var(--color-text-muted)', maxWidth: '150px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                            {config.ccEmails || '—'}
                                        </td>
                                        <td style={{ padding: '12px 8px', textAlign: 'center' }}>
                                            {config.notifyOnScheduled ? '✅' : '—'}
                                        </td>
                                        <td style={{ padding: '12px 8px', textAlign: 'center' }}>
                                            {config.notifyOnCompleted ? '✅' : '—'}
                                        </td>
                                        <td style={{ padding: '12px 8px', textAlign: 'center' }}>
                                            <span style={{
                                                display: 'inline-block', padding: '4px 10px', borderRadius: '12px',
                                                fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase',
                                                backgroundColor: config.isActive ? '#dcfce7' : '#fef2f2',
                                                color: config.isActive ? '#166534' : '#991b1b'
                                            }}>
                                                {config.isActive ? 'Ativo' : 'Inativo'}
                                            </span>
                                        </td>
                                        <td style={{ padding: '12px 8px', textAlign: 'right' }}>
                                            <KebabMenu
                                                options={[
                                                    { icon: <Edit2 size={14} />, label: 'Editar', onClick: () => handleEdit(config) },
                                                    config.isActive
                                                        ? { icon: <PowerOff size={14} />, label: 'Desativar', onClick: () => handleToggle(config.id) }
                                                        : { icon: <Power size={14} />, label: 'Ativar', onClick: () => handleToggle(config.id) }
                                                ]}
                                            />
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
            </div>
        </div>
    );
}
