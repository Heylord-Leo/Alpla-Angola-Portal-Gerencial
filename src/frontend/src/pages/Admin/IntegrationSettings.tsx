import { useState, useEffect, useCallback } from 'react';
import {
    Database, Globe, Mail, Cpu, RefreshCw, Shield,
    Eye, EyeOff, CheckCircle2, XCircle, AlertCircle, HelpCircle,
    Clock, Loader2, Lock, Unlock, Key, Server, ChevronDown, ChevronUp
} from 'lucide-react';
import { api } from '../../lib/api';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import type { IntegrationSettingsDto } from '../../types';

/*
 * ──────────────────────────────────────────────────────
 *  Display helpers
 * ──────────────────────────────────────────────────────
 */

const PROVIDER_ICONS: Record<string, React.ReactNode> = {
    PRIMAVERA: <Database size={24} />,
    INNUX: <Clock size={24} />,
    OPENAI: <Cpu size={24} />,
    SMTP: <Mail size={24} />,
};

const STATUS_DISPLAY: Record<string, { label: string; color: string; bg: string; icon: React.ReactNode }> = {
    HEALTHY:        { label: 'Operacional',     color: 'var(--color-status-green)',  bg: 'color-mix(in srgb, var(--color-status-green) 15%, transparent)', icon: <CheckCircle2 size={14} /> },
    UNHEALTHY:      { label: 'Erro',            color: 'var(--color-status-red)',    bg: 'color-mix(in srgb, var(--color-status-red) 15%, transparent)',   icon: <XCircle size={14} /> },
    UNREACHABLE:    { label: 'Erro',            color: 'var(--color-status-red)',    bg: 'color-mix(in srgb, var(--color-status-red) 15%, transparent)',   icon: <AlertCircle size={14} /> },
    NOT_CONFIGURED: { label: 'Não Configurado', color: 'color-mix(in srgb, var(--color-text-main) 70%, transparent)', bg: 'color-mix(in srgb, var(--color-text-muted) 12%, transparent)', icon: <HelpCircle size={14} /> },
    PLANNED:        { label: 'Prevista',        color: 'var(--color-status-blue)',   bg: 'color-mix(in srgb, var(--color-status-blue) 15%, transparent)',  icon: <Clock size={14} /> },
    INACTIVE:       { label: 'Inativo',         color: 'var(--color-text-muted)',    bg: 'color-mix(in srgb, var(--color-text-muted) 15%, transparent)',   icon: <XCircle size={14} /> },
    PENDING_TEST:   { label: 'Pendente de Teste', color: '#d97706',                  bg: 'rgba(217, 119, 6, 0.12)',                                        icon: <Clock size={14} /> }
};

const DESCRIPTION_TRANSLATIONS: Record<string, string> = {
    "Enterprise Resource Planning — master data source for employees, articles, suppliers, departments, and cost centers.":
        "Sistema ERP utilizado como fonte de dados mestre para colaboradores, artigos, fornecedores, departamentos e centros de custo.",
    "Biometric time and attendance system — complementary employee/attendance data source.":
        "Sistema de assiduidade e marcação de ponto utilizado como fonte complementar de dados de colaboradores e presenças.",
    "AI-powered document extraction and analysis — OCR processing for proformas, invoices, and contracts.":
        "Serviço de IA para extração e análise de documentos, utilizado no OCR de proformas, faturas e contratos.",
    "Email notification service — sends workflow alerts, password resets, and proforma deadline reminders.":
        "Serviço de e-mail utilizado para envio de alertas de workflow, redefinição de senha e lembretes de prazos de proformas."
};

function StatusBadge({ status }: { status?: string }) {
    const display = STATUS_DISPLAY[status || 'NOT_CONFIGURED'] || STATUS_DISPLAY.NOT_CONFIGURED;
    return (
        <span style={{
            display: 'inline-flex', alignItems: 'center', gap: '0.375rem',
            padding: '0.25rem 0.75rem', borderRadius: '9999px',
            fontSize: '0.75rem', fontWeight: 600,
            color: display.color, backgroundColor: display.bg
        }}>
            {display.icon} {display.label}
        </span>
    );
}

function SecretIndicator({ hasSecret, version }: { hasSecret: boolean; version: number }) {
    return (
        <span style={{
            display: 'inline-flex', alignItems: 'center', gap: '0.375rem',
            fontSize: '0.8125rem', color: hasSecret ? 'var(--color-status-green)' : 'var(--color-text-muted)'
        }}>
            {hasSecret ? <Key size={14} /> : <Lock size={14} />}
            {hasSecret ? `Configurado (v${version})` : 'Não configurado'}
        </span>
    );
}

/*
 * ──────────────────────────────────────────────────────
 *  Secret Replace Modal
 * ──────────────────────────────────────────────────────
 */

function SecretReplaceModal({ provider, secretType, onClose, onSuccess }: {
    provider: IntegrationSettingsDto;
    secretType: 'PASSWORD' | 'API_KEY';
    onClose: () => void;
    onSuccess: () => void;
}) {
    const [value, setValue] = useState('');
    const [confirm, setConfirm] = useState('');
    const [showValue, setShowValue] = useState(false);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');

    const label = secretType === 'PASSWORD' ? 'Senha' : 'Chave API';
    const canSave = value.length > 0 && value === confirm;

    async function handleSave() {
        if (!canSave) return;
        setSaving(true);
        setError('');
        try {
            await api.admin.integrationSettings.replaceSecret(provider.code, secretType, value);
            onSuccess();
        } catch (err: unknown) {
            setError(err instanceof Error ? err.message : 'Falha ao atualizar segredo.');
        } finally {
            setSaving(false);
        }
    }

    return (
        <div style={{
            position: 'fixed', inset: 0, zIndex: 1000,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            backgroundColor: 'rgba(0,0,0,0.5)', backdropFilter: 'blur(4px)'
        }}>
            <div style={{
                backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)', padding: '2rem',
                width: '100%', maxWidth: '480px', boxShadow: '0 24px 48px rgba(0,0,0,0.2)'
            }}>
                <h3 style={{ margin: '0 0 0.5rem 0', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <Key size={20} /> Substituir {label}
                </h3>
                <p style={{ margin: '0 0 1.5rem 0', fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                    Provedor: <strong>{provider.name}</strong> ({provider.code})
                </p>

                {error && (
                    <div style={{
                        padding: '0.75rem', marginBottom: '1rem', borderRadius: 'var(--radius-sm)',
                        backgroundColor: 'color-mix(in srgb, var(--color-status-red) 10%, transparent)',
                        color: 'var(--color-status-red)', fontSize: '0.875rem'
                    }}>
                        {error}
                    </div>
                )}

                <div style={{ marginBottom: '1rem' }}>
                    <label style={{ display: 'block', marginBottom: '0.375rem', fontSize: '0.8125rem', fontWeight: 600 }}>
                        Novo {label}
                    </label>
                    <div style={{ position: 'relative' }}>
                        <input
                            type={showValue ? 'text' : 'password'}
                            value={value}
                            onChange={e => setValue(e.target.value)}
                            placeholder={`Digite o novo ${label.toLowerCase()}...`}
                            autoComplete="new-password"
                            style={{
                                width: '100%', padding: '0.625rem 2.5rem 0.625rem 0.75rem',
                                border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)',
                                backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
                                fontSize: '0.875rem', boxSizing: 'border-box'
                            }}
                        />
                        <button
                            type="button"
                            onClick={() => setShowValue(!showValue)}
                            style={{
                                position: 'absolute', right: '0.5rem', top: '50%', transform: 'translateY(-50%)',
                                background: 'none', border: 'none', cursor: 'pointer',
                                color: 'var(--color-text-muted)', padding: '0.25rem'
                            }}
                        >
                            {showValue ? <EyeOff size={16} /> : <Eye size={16} />}
                        </button>
                    </div>
                </div>

                <div style={{ marginBottom: '1.5rem' }}>
                    <label style={{ display: 'block', marginBottom: '0.375rem', fontSize: '0.8125rem', fontWeight: 600 }}>
                        Confirmar {label}
                    </label>
                    <input
                        type="password"
                        value={confirm}
                        onChange={e => setConfirm(e.target.value)}
                        placeholder={`Confirme o novo ${label.toLowerCase()}...`}
                        autoComplete="new-password"
                        style={{
                            width: '100%', padding: '0.625rem 0.75rem',
                            border: `1px solid ${confirm && confirm !== value ? 'var(--color-status-red)' : 'var(--color-border)'}`,
                            borderRadius: 'var(--radius-sm)',
                            backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
                            fontSize: '0.875rem', boxSizing: 'border-box'
                        }}
                    />
                    {confirm && confirm !== value && (
                        <p style={{ margin: '0.25rem 0 0', fontSize: '0.75rem', color: 'var(--color-status-red)' }}>
                            Os valores não coincidem.
                        </p>
                    )}
                </div>

                <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'flex-end' }}>
                    <button onClick={onClose} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                        color: 'var(--color-text-main)', cursor: 'pointer', fontSize: '0.875rem'
                    }}>
                        Cancelar
                    </button>
                    <button onClick={handleSave} disabled={!canSave || saving} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: 'none', backgroundColor: canSave ? 'var(--color-primary)' : 'var(--color-border)',
                        color: 'white', cursor: canSave ? 'pointer' : 'not-allowed',
                        fontSize: '0.875rem', fontWeight: 600,
                        display: 'flex', alignItems: 'center', gap: '0.5rem'
                    }}>
                        {saving && <Loader2 size={14} className="spin" />}
                        Substituir
                    </button>
                </div>
            </div>
        </div>
    );
}

/*
 * ──────────────────────────────────────────────────────
 *  Primavera Company Secret Modal (New)
 * ──────────────────────────────────────────────────────
 */

function PrimaveraCompanySecretModal({ companyKey, companyName, onClose, onSuccess }: {
    companyKey: 'ALPLAPLASTICO' | 'ALPLASOPRO';
    companyName: string;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const [value, setValue] = useState('');
    const [confirm, setConfirm] = useState('');
    const [showValue, setShowValue] = useState(false);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');

    const canSave = value.length > 0 && value === confirm;

    async function handleSave() {
        if (!canSave) return;
        setSaving(true);
        setError('');
        try {
            await api.admin.integrationSettings.replacePrimaveraCompanySecret({
                companyKey,
                newPassword: value
            });
            onSuccess();
        } catch (err: unknown) {
            setError(err instanceof Error ? err.message : 'Falha ao atualizar senha.');
        } finally {
            setSaving(false);
        }
    }

    return (
        <div style={{
            position: 'fixed', inset: 0, zIndex: 1000,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            backgroundColor: 'rgba(0,0,0,0.5)', backdropFilter: 'blur(4px)'
        }}>
            <div style={{
                backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)', padding: '2rem',
                width: '100%', maxWidth: '480px', boxShadow: '0 24px 48px rgba(0,0,0,0.2)'
            }}>
                <h3 style={{ margin: '0 0 0.5rem 0', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <Key size={20} /> Substituir Senha
                </h3>
                <p style={{ margin: '0 0 1.5rem 0', fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                    Empresa: <strong>{companyName}</strong> (Primavera ERP)
                </p>

                {error && (
                    <div style={{
                        padding: '0.75rem', marginBottom: '1rem', borderRadius: 'var(--radius-sm)',
                        backgroundColor: 'color-mix(in srgb, var(--color-status-red) 10%, transparent)',
                        color: 'var(--color-status-red)', fontSize: '0.875rem'
                    }}>
                        {error}
                    </div>
                )}

                <div style={{ marginBottom: '1rem' }}>
                    <label style={{ display: 'block', marginBottom: '0.375rem', fontSize: '0.8125rem', fontWeight: 600 }}>
                        Nova Senha
                    </label>
                    <div style={{ position: 'relative' }}>
                        <input
                            type={showValue ? 'text' : 'password'}
                            value={value}
                            onChange={e => setValue(e.target.value)}
                            placeholder="Digite a nova senha..."
                            autoComplete="new-password"
                            style={{
                                width: '100%', padding: '0.625rem 2.5rem 0.625rem 0.75rem',
                                border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)',
                                backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
                                fontSize: '0.875rem', boxSizing: 'border-box'
                            }}
                        />
                        <button
                            type="button"
                            onClick={() => setShowValue(!showValue)}
                            style={{
                                position: 'absolute', right: '0.5rem', top: '50%', transform: 'translateY(-50%)',
                                background: 'none', border: 'none', cursor: 'pointer',
                                color: 'var(--color-text-muted)', padding: '0.25rem'
                            }}
                        >
                            {showValue ? <EyeOff size={16} /> : <Eye size={16} />}
                        </button>
                    </div>
                </div>

                <div style={{ marginBottom: '1.5rem' }}>
                    <label style={{ display: 'block', marginBottom: '0.375rem', fontSize: '0.8125rem', fontWeight: 600 }}>
                        Confirmar Senha
                    </label>
                    <input
                        type="password"
                        value={confirm}
                        onChange={e => setConfirm(e.target.value)}
                        placeholder="Confirme a nova senha..."
                        autoComplete="new-password"
                        style={{
                            width: '100%', padding: '0.625rem 0.75rem',
                            border: `1px solid ${confirm && confirm !== value ? 'var(--color-status-red)' : 'var(--color-border)'}`,
                            borderRadius: 'var(--radius-sm)',
                            backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
                            fontSize: '0.875rem', boxSizing: 'border-box'
                        }}
                    />
                    {confirm && confirm !== value && (
                        <p style={{ margin: '0.25rem 0 0', fontSize: '0.75rem', color: 'var(--color-status-red)' }}>
                            Os valores não coincidem.
                        </p>
                    )}
                </div>

                <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'flex-end' }}>
                    <button onClick={onClose} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                        color: 'var(--color-text-main)', cursor: 'pointer', fontSize: '0.875rem'
                    }}>
                        Cancelar
                    </button>
                    <button onClick={handleSave} disabled={!canSave || saving} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: 'none', backgroundColor: canSave ? 'var(--color-primary)' : 'var(--color-border)',
                        color: 'white', cursor: canSave ? 'pointer' : 'not-allowed',
                        fontSize: '0.875rem', fontWeight: 600,
                        display: 'flex', alignItems: 'center', gap: '0.5rem'
                    }}>
                        {saving && <Loader2 size={14} className="spin" />}
                        Substituir
                    </button>
                </div>
            </div>
        </div>
    );
}


/*
 * ──────────────────────────────────────────────────────
 *  AlplaPROD Plant Secret Modal
 * ──────────────────────────────────────────────────────
 */

function AlplaProdPlantSecretModal({ plantKey, plantName, onClose, onSuccess }: {
    plantKey: string;
    plantName: string;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const [value, setValue] = useState('');
    const [confirm, setConfirm] = useState('');
    const [showValue, setShowValue] = useState(false);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');

    const canSave = value.length > 0 && value === confirm;

    async function handleSave() {
        if (!canSave) return;
        setSaving(true);
        setError('');
        try {
            await api.admin.integrationSettings.replaceAlplaProdPlantSecret({
                plantKey,
                newPassword: value
            });
            onSuccess();
        } catch (err: unknown) {
            setError(err instanceof Error ? err.message : 'Falha ao atualizar senha.');
        } finally {
            setSaving(false);
        }
    }

    return (
        <div style={{
            position: 'fixed', inset: 0, zIndex: 1000,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            backgroundColor: 'rgba(0,0,0,0.5)', backdropFilter: 'blur(4px)'
        }}>
            <div style={{
                backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)', padding: '2rem',
                width: '100%', maxWidth: '480px', boxShadow: '0 24px 48px rgba(0,0,0,0.2)'
            }}>
                <h3 style={{ margin: '0 0 0.5rem 0', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <Key size={20} /> Substituir Senha
                </h3>
                <p style={{ margin: '0 0 1.5rem 0', fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                    Planta: <strong>{plantName}</strong> (AlplaPROD 1.0)
                </p>

                {error && (
                    <div style={{
                        padding: '0.75rem', marginBottom: '1rem', borderRadius: 'var(--radius-sm)',
                        backgroundColor: 'color-mix(in srgb, var(--color-status-red) 10%, transparent)',
                        color: 'var(--color-status-red)', fontSize: '0.875rem'
                    }}>
                        {error}
                    </div>
                )}

                <div style={{ marginBottom: '1rem' }}>
                    <label style={{ display: 'block', marginBottom: '0.375rem', fontSize: '0.8125rem', fontWeight: 600 }}>
                        Nova Senha
                    </label>
                    <div style={{ position: 'relative' }}>
                        <input
                            type={showValue ? 'text' : 'password'}
                            value={value}
                            onChange={e => setValue(e.target.value)}
                            placeholder="Digite a nova senha..."
                            autoComplete="new-password"
                            style={{
                                width: '100%', padding: '0.625rem 2.5rem 0.625rem 0.75rem',
                                border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)',
                                backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
                                fontSize: '0.875rem', boxSizing: 'border-box'
                            }}
                        />
                        <button
                            type="button"
                            onClick={() => setShowValue(!showValue)}
                            style={{
                                position: 'absolute', right: '0.5rem', top: '50%', transform: 'translateY(-50%)',
                                background: 'none', border: 'none', cursor: 'pointer',
                                color: 'var(--color-text-muted)', padding: '0.25rem'
                            }}
                        >
                            {showValue ? <EyeOff size={16} /> : <Eye size={16} />}
                        </button>
                    </div>
                </div>

                <div style={{ marginBottom: '1.5rem' }}>
                    <label style={{ display: 'block', marginBottom: '0.375rem', fontSize: '0.8125rem', fontWeight: 600 }}>
                        Confirmar Senha
                    </label>
                    <input
                        type="password"
                        value={confirm}
                        onChange={e => setConfirm(e.target.value)}
                        placeholder="Confirme a nova senha..."
                        autoComplete="new-password"
                        style={{
                            width: '100%', padding: '0.625rem 0.75rem',
                            border: `1px solid ${confirm && confirm !== value ? 'var(--color-status-red)' : 'var(--color-border)'}`,
                            borderRadius: 'var(--radius-sm)',
                            backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
                            fontSize: '0.875rem', boxSizing: 'border-box'
                        }}
                    />
                    {confirm && confirm !== value && (
                        <p style={{ margin: '0.25rem 0 0', fontSize: '0.75rem', color: 'var(--color-status-red)' }}>
                            Os valores não coincidem.
                        </p>
                    )}
                </div>

                <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'flex-end' }}>
                    <button onClick={onClose} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                        color: 'var(--color-text-main)', cursor: 'pointer', fontSize: '0.875rem'
                    }}>
                        Cancelar
                    </button>
                    <button onClick={handleSave} disabled={!canSave || saving} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: 'none', backgroundColor: canSave ? 'var(--color-primary)' : 'var(--color-border)',
                        color: 'white', cursor: canSave ? 'pointer' : 'not-allowed',
                        fontSize: '0.875rem', fontWeight: 600,
                        display: 'flex', alignItems: 'center', gap: '0.5rem'
                    }}>
                        {saving && <Loader2 size={14} className="spin" />}
                        Substituir
                    </button>
                </div>
            </div>
        </div>
    );
}

/*
 * ──────────────────────────────────────────────────────
 *  AlplaPROD Plant Configure Modal
 * ──────────────────────────────────────────────────────
 */

const PLANT_DISPLAY_NAMES: Record<string, string> = {
    VIANA1: 'Viana 1',
    VIANA2: 'Viana 2',
    VIANA3: 'Viana 3'
};

function AlplaProdPlantConfigModal({ plantKey, provider, onClose, onSuccess }: {
    plantKey: string;
    provider: IntegrationSettingsDto;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const plant = provider.alplaProdPlants?.find(p => p.plantKey === plantKey);
    const [server, setServer] = useState(plant?.server || '');
    const [databaseName, setDatabaseName] = useState(plant?.databaseName || '');
    const [username, setUsername] = useState(plant?.username || '');
    const [enabled, setEnabled] = useState(plant?.enabled !== false);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');

    async function handleSave() {
        setSaving(true);
        setError('');
        try {
            await api.admin.integrationSettings.updateAlplaProdPlant({
                plantKey,
                server: server || undefined,
                databaseName: databaseName || undefined,
                username: username || undefined,
                enabled
            });
            onSuccess();
        } catch (err: unknown) {
            setError(err instanceof Error ? err.message : 'Falha ao salvar configurações.');
        } finally {
            setSaving(false);
        }
    }

    const inputStyle: React.CSSProperties = {
        width: '100%', padding: '0.625rem 0.75rem',
        border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)',
        backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
        fontSize: '0.875rem', boxSizing: 'border-box'
    };

    const labelStyle: React.CSSProperties = {
        display: 'block', marginBottom: '0.375rem', fontSize: '0.8125rem', fontWeight: 600
    };

    return (
        <div style={{
            position: 'fixed', inset: 0, zIndex: 1000,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            backgroundColor: 'rgba(0,0,0,0.5)', backdropFilter: 'blur(4px)'
        }}>
            <div style={{
                backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)', padding: '2rem',
                width: '100%', maxWidth: '540px', maxHeight: '90vh', overflowY: 'auto',
                boxShadow: '0 24px 48px rgba(0,0,0,0.2)'
            }}>
                <h3 style={{ margin: '0 0 0.5rem 0', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <Server size={20} /> Configurar Planta
                </h3>
                <p style={{ margin: '0 0 1.5rem 0', fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                    Planta: <strong>{PLANT_DISPLAY_NAMES[plantKey] || plantKey}</strong> (AlplaPROD 1.0)
                </p>

                {error && (
                    <div style={{
                        padding: '0.75rem', marginBottom: '1rem', borderRadius: 'var(--radius-sm)',
                        backgroundColor: 'color-mix(in srgb, var(--color-status-red) 10%, transparent)',
                        color: 'var(--color-status-red)', fontSize: '0.875rem'
                    }}>
                        {error}
                    </div>
                )}

                <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', marginBottom: '1.5rem' }}>
                    <div>
                        <label style={labelStyle}>Servidor (IP ou Hostname)</label>
                        <input type="text" value={server} onChange={e => setServer(e.target.value)} placeholder="ex: 192.168.1.100" style={inputStyle} />
                        <p style={{ margin: '0.25rem 0 0', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                            Cada planta pode ter o seu próprio servidor SQL.
                        </p>
                    </div>
                    <div>
                        <label style={labelStyle}>Base de Dados</label>
                        <input type="text" value={databaseName} onChange={e => setDatabaseName(e.target.value)} placeholder="ex: ALPLAPROD_VIANA1" style={inputStyle} />
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: '1rem', alignItems: 'end' }}>
                        <div>
                            <label style={labelStyle}>Utilizador <span style={{ fontWeight: 400, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>(deixe vazio para usar credencial global)</span></label>
                            <input type="text" value={username} onChange={e => setUsername(e.target.value)} placeholder="ex: alplaProdUser" style={inputStyle} />
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem', height: '38px', justifyContent: 'center' }}>
                            <span style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', textAlign: 'center' }}>Ativar</span>
                            <input
                                type="checkbox"
                                checked={enabled}
                                onChange={e => setEnabled(e.target.checked)}
                                style={{ width: '18px', height: '18px', margin: '0 auto' }}
                            />
                        </div>
                    </div>

                    {plant?.pipelineModel && (
                        <div style={{
                            padding: '0.75rem',
                            borderRadius: 'var(--radius-sm)',
                            backgroundColor: 'color-mix(in srgb, var(--color-text-muted) 5%, transparent)',
                            border: '1px solid var(--color-border)'
                        }}>
                            <div style={{ fontSize: '0.6875rem', fontWeight: 600, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '0.25rem' }}>
                                PIPELINE MODEL (somente leitura)
                            </div>
                            <div style={{ fontSize: '0.875rem', fontFamily: 'monospace', color: 'var(--color-text-main)' }}>
                                {plant.pipelineModel}
                            </div>
                        </div>
                    )}
                </div>

                <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'flex-end' }}>
                    <button onClick={onClose} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                        color: 'var(--color-text-main)', cursor: 'pointer', fontSize: '0.875rem'
                    }}>
                        Cancelar
                    </button>
                    <button onClick={handleSave} disabled={saving} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: 'none', backgroundColor: 'var(--color-primary)',
                        color: 'white', cursor: 'pointer',
                        fontSize: '0.875rem', fontWeight: 600,
                        display: 'flex', alignItems: 'center', gap: '0.5rem'
                    }}>
                        {saving && <Loader2 size={14} className="spin" />}
                        Salvar Configurações
                    </button>
                </div>
            </div>
        </div>
    );
}


/*
 * ──────────────────────────────────────────────────────
 *  Connection Configure Modal
 * ──────────────────────────────────────────────────────
 */

function ConnectionConfigureModal({ provider, onClose, onSuccess }: {
    provider: IntegrationSettingsDto;
    onClose: () => void;
    onSuccess: () => void;
}) {
    const isSQL = provider.connectionType === 'SQL';
    const isAPI = provider.connectionType === 'REST_API';
    const isSMTP = provider.code === 'SMTP';
    const isPrimavera = provider.code === 'PRIMAVERA';

    const [server, setServer] = useState(provider.server || '');
    const [databaseName, setDatabaseName] = useState(provider.databaseName || '');
    const [instanceName, setInstanceName] = useState(provider.instanceName || '');
    const [authenticationMode, setAuthenticationMode] = useState(provider.authenticationMode || 'SQL');
    const [username, setUsername] = useState(provider.username || '');
    const [apiBaseUrl, setApiBaseUrl] = useState(provider.apiBaseUrl || '');
    const [timeoutSeconds, setTimeoutSeconds] = useState(provider.timeoutSeconds || 15);
    const [additionalConfig, setAdditionalConfig] = useState(provider.additionalConfig || '');

    // Company DB state for Primavera
    const [alplaPlasticoDb, setAlplaPlasticoDb] = useState('');
    const [alplaPlasticoEnabled, setAlplaPlasticoEnabled] = useState(true);
    const [alplaPlasticoUsername, setAlplaPlasticoUsername] = useState('');
    const [alplaSoproDb, setAlplaSoproDb] = useState('');
    const [alplaSoproEnabled, setAlplaSoproEnabled] = useState(true);
    const [alplaSoproUsername, setAlplaSoproUsername] = useState('');

    useEffect(() => {
        if (isPrimavera && provider.primaveraCompanies) {
            const plastico = provider.primaveraCompanies.find(c => c.companyKey === 'ALPLAPLASTICO');
            if (plastico) {
                setAlplaPlasticoDb(plastico.databaseName || '');
                setAlplaPlasticoEnabled(plastico.enabled !== false);
                setAlplaPlasticoUsername(plastico.username || '');
            }
            const sopro = provider.primaveraCompanies.find(c => c.companyKey === 'ALPLASOPRO');
            if (sopro) {
                setAlplaSoproDb(sopro.databaseName || '');
                setAlplaSoproEnabled(sopro.enabled !== false);
                setAlplaSoproUsername(sopro.username || '');
            }
        }
    }, [isPrimavera, provider.primaveraCompanies]);

    // SMTP fields
    const [port, setPort] = useState(provider.port || 587);
    const [enableSsl, setEnableSsl] = useState(provider.enableSsl !== false);
    const [senderEmail, setSenderEmail] = useState(provider.senderEmail || '');
    const [senderName, setSenderName] = useState(provider.senderName || '');

    // SMTP Email Environment Identification
    const [enableSubjectPrefix, setEnableSubjectPrefix] = useState(provider.enableSubjectPrefix ?? false);
    const [subjectPrefixText, setSubjectPrefixText] = useState(provider.subjectPrefixText || '');
    const [enableBodyWarningBanner, setEnableBodyWarningBanner] = useState(provider.enableBodyWarningBanner ?? false);
    const [warningBannerText, setWarningBannerText] = useState(provider.warningBannerText || '');
    const [redirectAllToTestRecipient, setRedirectAllToTestRecipient] = useState(provider.redirectAllToTestRecipient ?? false);
    const [testRecipientEmail, setTestRecipientEmail] = useState(provider.testRecipientEmail || '');
    const [showOriginalRecipientsInBody, setShowOriginalRecipientsInBody] = useState(provider.showOriginalRecipientsInBody ?? false);
    const [allowRealRecipientsInNonProduction, setAllowRealRecipientsInNonProduction] = useState(provider.allowRealRecipientsInNonProduction ?? false);
    const [envSectionExpanded, setEnvSectionExpanded] = useState(false);

    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');

    async function handleSave() {
        setSaving(true);
        setError('');
        try {
            const payload: any = {};
            if (isSQL) {
                payload.server = server;
                payload.databaseName = databaseName;
                // Normalize default SQL Server instance names to empty
                const trimmedInstance = instanceName.trim();
                const isDefault = !trimmedInstance
                    || trimmedInstance.toUpperCase() === 'MSSQLSERVER'
                    || trimmedInstance.toUpperCase() === 'DEFAULT';
                payload.instanceName = isDefault ? '' : trimmedInstance;
                payload.authenticationMode = authenticationMode;
                payload.username = username;
                payload.timeoutSeconds = Number(timeoutSeconds) || undefined;
                payload.additionalConfig = additionalConfig;
            } else if (isAPI) {
                payload.apiBaseUrl = apiBaseUrl;
                payload.timeoutSeconds = Number(timeoutSeconds) || undefined;
            } else if (isSMTP) {
                payload.server = server;
                payload.port = Number(port);
                payload.enableSsl = enableSsl;
                payload.senderEmail = senderEmail;
                payload.senderName = senderName;
                // Email Environment Identification
                payload.enableSubjectPrefix = enableSubjectPrefix;
                payload.subjectPrefixText = subjectPrefixText || undefined;
                payload.enableBodyWarningBanner = enableBodyWarningBanner;
                payload.warningBannerText = warningBannerText || undefined;
                payload.redirectAllToTestRecipient = redirectAllToTestRecipient;
                payload.testRecipientEmail = testRecipientEmail || undefined;
                payload.showOriginalRecipientsInBody = showOriginalRecipientsInBody;
                payload.allowRealRecipientsInNonProduction = allowRealRecipientsInNonProduction;
            }

            await api.admin.integrationSettings.update(provider.code, payload);

            if (isPrimavera) {
                await Promise.all([
                    api.admin.integrationSettings.updatePrimaveraCompany({
                        companyKey: 'ALPLAPLASTICO',
                        databaseName: alplaPlasticoDb,
                        enabled: alplaPlasticoEnabled,
                        username: alplaPlasticoUsername
                    }),
                    api.admin.integrationSettings.updatePrimaveraCompany({
                        companyKey: 'ALPLASOPRO',
                        databaseName: alplaSoproDb,
                        enabled: alplaSoproEnabled,
                        username: alplaSoproUsername
                    })
                ]);
            }

            onSuccess();
        } catch (err: unknown) {
            setError(err instanceof Error ? err.message : 'Falha ao salvar configurações.');
        } finally {
            setSaving(false);
        }
    }

    const inputStyle: React.CSSProperties = {
        width: '100%', padding: '0.625rem 0.75rem',
        border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)',
        backgroundColor: 'var(--color-bg-surface)', color: 'var(--color-text-main)',
        fontSize: '0.875rem', boxSizing: 'border-box'
    };

    const labelStyle: React.CSSProperties = {
        display: 'block', marginBottom: '0.375rem', fontSize: '0.8125rem', fontWeight: 600
    };

    return (
        <div style={{
            position: 'fixed', inset: 0, zIndex: 1000,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            backgroundColor: 'rgba(0,0,0,0.5)', backdropFilter: 'blur(4px)'
        }}>
            <div style={{
                backgroundColor: 'var(--color-bg-surface)', borderRadius: 'var(--radius-lg)',
                border: '1px solid var(--color-border)', padding: '2rem',
                width: '100%', maxWidth: '540px', maxHeight: '90vh', overflowY: 'auto',
                boxShadow: '0 24px 48px rgba(0,0,0,0.2)'
            }}>
                <h3 style={{ margin: '0 0 0.5rem 0', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <Server size={20} /> Configurar Conexão
                </h3>
                <p style={{ margin: '0 0 1.5rem 0', fontSize: '0.875rem', color: 'var(--color-text-muted)' }}>
                    Provedor: <strong>{provider.name}</strong> ({provider.code})
                </p>

                {error && (
                    <div style={{
                        padding: '0.75rem', marginBottom: '1rem', borderRadius: 'var(--radius-sm)',
                        backgroundColor: 'color-mix(in srgb, var(--color-status-red) 10%, transparent)',
                        color: 'var(--color-status-red)', fontSize: '0.875rem'
                    }}>
                        {error}
                    </div>
                )}

                <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', marginBottom: '1.5rem' }}>
                    {isSQL && (
                        <>
                            <div>
                                <label style={labelStyle}>Servidor (IP ou Hostname)</label>
                                <input type="text" value={server} onChange={e => setServer(e.target.value)} placeholder="ex: 192.168.1.100" style={inputStyle} />
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: isPrimavera ? '1fr' : '1fr 1fr', gap: '1rem' }}>
                                {!isPrimavera && (
                                    <div>
                                        <label style={labelStyle}>Base de Dados</label>
                                        <input type="text" value={databaseName} onChange={e => setDatabaseName(e.target.value)} placeholder="ex: PRIANGOLA" style={inputStyle} />
                                    </div>
                                )}
                                <div>
                                    <label style={labelStyle}>Instância SQL <span style={{ fontWeight: 400, color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>(opcional)</span></label>
                                    <input type="text" value={instanceName} onChange={e => setInstanceName(e.target.value)} placeholder="ex: SQLEXPRESS" style={inputStyle} />
                                    <p style={{ margin: '0.25rem 0 0', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                        Para a instância padrão do SQL Server, deixe este campo vazio.
                                    </p>
                                </div>
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
                                <div>
                                    <label style={labelStyle}>Modo de Autenticação</label>
                                    <select value={authenticationMode} onChange={e => setAuthenticationMode(e.target.value)} style={inputStyle}>
                                        <option value="SQL">SQL Server Authentication</option>
                                        <option value="WINDOWS">Windows Authentication</option>
                                    </select>
                                </div>
                                {authenticationMode === 'SQL' && !isPrimavera && (
                                    <div>
                                        <label style={labelStyle}>Utilizador Geral</label>
                                        <input type="text" value={username} onChange={e => setUsername(e.target.value)} placeholder="ex: sa" style={inputStyle} />
                                    </div>
                                )}
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: '1rem' }}>
                                <div>
                                    <label style={labelStyle}>Timeout (segundos)</label>
                                    <input type="number" value={timeoutSeconds} onChange={e => setTimeoutSeconds(Number(e.target.value))} style={inputStyle} />
                                </div>
                            </div>
                            {!isPrimavera && (
                                <div>
                                    <label style={labelStyle}>Configurações Adicionais de Conexão (ex: TrustServerCertificate=True)</label>
                                    <textarea value={additionalConfig} onChange={e => setAdditionalConfig(e.target.value)} placeholder="Adicione chaves extras de connection string" style={{ ...inputStyle, minHeight: '60px', fontFamily: 'monospace' }} />
                                </div>
                            )}

                            {isPrimavera && (
                                <div style={{
                                    marginTop: '1rem',
                                    paddingTop: '1rem',
                                    borderTop: '2px solid var(--color-border)'
                                }}>
                                    <h4 style={{
                                        margin: '0 0 1rem 0',
                                        fontSize: '0.875rem',
                                        fontWeight: 800,
                                        textTransform: 'uppercase',
                                        color: 'var(--color-text-primary)',
                                        display: 'flex',
                                        alignItems: 'center',
                                        gap: '0.5rem'
                                    }}>
                                        <Database size={16} /> Bases de Dados por Empresa
                                    </h4>
                                    
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                                        {/* Alpla Plástico */}
                                        <div style={{
                                            display: 'grid',
                                            gridTemplateColumns: '2fr 2fr auto',
                                            alignItems: 'end',
                                            gap: '1rem',
                                            padding: '0.75rem',
                                            backgroundColor: 'color-mix(in srgb, var(--color-text-muted) 5%, transparent)',
                                            borderRadius: 'var(--radius-sm)',
                                            border: '1px solid var(--color-border)'
                                        }}>
                                            <div>
                                                <label style={labelStyle}>Alpla Plástico (Base de Dados)</label>
                                                <input
                                                    type="text"
                                                    value={alplaPlasticoDb}
                                                    onChange={e => setAlplaPlasticoDb(e.target.value)}
                                                    placeholder="ex: PRI297514001"
                                                    style={inputStyle}
                                                />
                                            </div>
                                            <div>
                                                <label style={labelStyle}>Utilizador</label>
                                                <input
                                                    type="text"
                                                    value={alplaPlasticoUsername}
                                                    onChange={e => setAlplaPlasticoUsername(e.target.value)}
                                                    placeholder="ex: usuario_plastico"
                                                    style={inputStyle}
                                                />
                                            </div>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem', height: '38px', justifyContent: 'center' }}>
                                                <span style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', textAlign: 'center' }}>Ativar</span>
                                                <input
                                                    type="checkbox"
                                                    checked={alplaPlasticoEnabled}
                                                    onChange={e => setAlplaPlasticoEnabled(e.target.checked)}
                                                    style={{ width: '18px', height: '18px', margin: '0 auto' }}
                                                />
                                            </div>
                                        </div>

                                        {/* Alpla Sopro */}
                                        <div style={{
                                            display: 'grid',
                                            gridTemplateColumns: '2fr 2fr auto',
                                            alignItems: 'end',
                                            gap: '1rem',
                                            padding: '0.75rem',
                                            backgroundColor: 'color-mix(in srgb, var(--color-text-muted) 5%, transparent)',
                                            borderRadius: 'var(--radius-sm)',
                                            border: '1px solid var(--color-border)'
                                        }}>
                                            <div>
                                                <label style={labelStyle}>Alpla Sopro (Base de Dados)</label>
                                                <input
                                                    type="text"
                                                    value={alplaSoproDb}
                                                    onChange={e => setAlplaSoproDb(e.target.value)}
                                                    placeholder="ex: PRI297514003"
                                                    style={inputStyle}
                                                />
                                            </div>
                                            <div>
                                                <label style={labelStyle}>Utilizador</label>
                                                <input
                                                    type="text"
                                                    value={alplaSoproUsername}
                                                    onChange={e => setAlplaSoproUsername(e.target.value)}
                                                    placeholder="ex: usuario_sopro"
                                                    style={inputStyle}
                                                />
                                            </div>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.25rem', height: '38px', justifyContent: 'center' }}>
                                                <span style={{ fontSize: '0.7rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', textAlign: 'center' }}>Ativar</span>
                                                <input
                                                    type="checkbox"
                                                    checked={alplaSoproEnabled}
                                                    onChange={e => setAlplaSoproEnabled(e.target.checked)}
                                                    style={{ width: '18px', height: '18px', margin: '0 auto' }}
                                                />
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            )}
                        </>
                    )}

                    {isAPI && (
                        <>
                            <div>
                                <label style={labelStyle}>URL Base da API</label>
                                <input type="text" value={apiBaseUrl} onChange={e => setApiBaseUrl(e.target.value)} placeholder="https://api.openai.com/v1" style={inputStyle} />
                            </div>
                            <div>
                                <label style={labelStyle}>Timeout (segundos)</label>
                                <input type="number" value={timeoutSeconds} onChange={e => setTimeoutSeconds(Number(e.target.value))} style={inputStyle} />
                            </div>
                        </>
                    )}

                    {isSMTP && (
                        <>
                            <div>
                                <label style={labelStyle}>Servidor SMTP (Host)</label>
                                <input type="text" value={server} onChange={e => setServer(e.target.value)} placeholder="smtp.office365.com" style={inputStyle} />
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
                                <div>
                                    <label style={labelStyle}>Porta</label>
                                    <input type="number" value={port} onChange={e => setPort(Number(e.target.value))} style={inputStyle} />
                                </div>
                                <div>
                                    <label style={labelStyle}>Segurança</label>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', height: '38px' }}>
                                        <input type="checkbox" checked={enableSsl} onChange={e => setEnableSsl(e.target.checked)} style={{ width: '16px', height: '16px' }} />
                                        <span style={{ fontSize: '0.875rem' }}>Habilitar SSL/TLS</span>
                                    </div>
                                </div>
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
                                <div>
                                    <label style={labelStyle}>E-mail Remetente</label>
                                    <input type="email" value={senderEmail} onChange={e => setSenderEmail(e.target.value)} placeholder="portal@empresa.com" style={inputStyle} />
                                </div>
                                <div>
                                    <label style={labelStyle}>Nome Remetente</label>
                                    <input type="text" value={senderName} onChange={e => setSenderName(e.target.value)} placeholder="ALPLA Portal" style={inputStyle} />
                                </div>
                            </div>
                            {/* ── Email Environment Identification ── */}
                            <div style={{
                                marginTop: '1.5rem',
                                borderTop: '2px solid color-mix(in srgb, #FFC107 40%, var(--color-border))',
                                paddingTop: '1rem'
                            }}>
                                <button
                                    type="button"
                                    onClick={() => setEnvSectionExpanded(!envSectionExpanded)}
                                    style={{
                                        background: 'none', border: 'none', cursor: 'pointer',
                                        display: 'flex', alignItems: 'center', gap: '0.5rem',
                                        width: '100%', padding: '0.5rem 0',
                                        fontSize: '0.875rem', fontWeight: 800,
                                        textTransform: 'uppercase' as const,
                                        color: '#856404'
                                    }}
                                >
                                    <span style={{ fontSize: '1.1rem' }}>⚠️</span>
                                    Identificação de Ambiente de E-mail
                                    <span style={{ marginLeft: 'auto', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                        {envSectionExpanded ? '▲' : '▼'}
                                    </span>
                                </button>
                                <p style={{ margin: '0 0 0.75rem 0', fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                    Em ambientes não-produção (TEST/DEV), avisos são aplicados automaticamente.
                                    Estas configurações permitem personalizar o comportamento.
                                </p>

                                {envSectionExpanded && (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                                        {/* Subject Prefix */}
                                        <div style={{
                                            padding: '1rem',
                                            backgroundColor: 'color-mix(in srgb, #FFC107 8%, transparent)',
                                            borderRadius: 'var(--radius-sm)',
                                            border: '1px solid color-mix(in srgb, #FFC107 30%, var(--color-border))'
                                        }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.75rem' }}>
                                                <input
                                                    type="checkbox"
                                                    checked={enableSubjectPrefix}
                                                    onChange={e => setEnableSubjectPrefix(e.target.checked)}
                                                    style={{ width: '16px', height: '16px' }}
                                                    id="smtp-env-subject-prefix"
                                                />
                                                <label htmlFor="smtp-env-subject-prefix" style={{ fontSize: '0.8125rem', fontWeight: 600, cursor: 'pointer' }}>
                                                    Ativar prefixo personalizado no assunto
                                                </label>
                                            </div>
                                            <input
                                                type="text"
                                                value={subjectPrefixText}
                                                onChange={e => setSubjectPrefixText(e.target.value)}
                                                placeholder="[TEST - IGNORE]"
                                                style={{ ...inputStyle, opacity: enableSubjectPrefix ? 1 : 0.5 }}
                                                disabled={!enableSubjectPrefix}
                                            />
                                            <p style={{ margin: '0.25rem 0 0', fontSize: '0.7rem', color: '#856404' }}>
                                                Deixe vazio para usar o padrão automático: [AMBIENTE - IGNORE]
                                            </p>
                                        </div>

                                        {/* Body Warning Banner */}
                                        <div style={{
                                            padding: '1rem',
                                            backgroundColor: 'color-mix(in srgb, #FFC107 8%, transparent)',
                                            borderRadius: 'var(--radius-sm)',
                                            border: '1px solid color-mix(in srgb, #FFC107 30%, var(--color-border))'
                                        }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.75rem' }}>
                                                <input
                                                    type="checkbox"
                                                    checked={enableBodyWarningBanner}
                                                    onChange={e => setEnableBodyWarningBanner(e.target.checked)}
                                                    style={{ width: '16px', height: '16px' }}
                                                    id="smtp-env-body-banner"
                                                />
                                                <label htmlFor="smtp-env-body-banner" style={{ fontSize: '0.8125rem', fontWeight: 600, cursor: 'pointer' }}>
                                                    Ativar banner de aviso personalizado no corpo
                                                </label>
                                            </div>
                                            <textarea
                                                value={warningBannerText}
                                                onChange={e => setWarningBannerText(e.target.value)}
                                                placeholder="Esta mensagem foi gerada pelo ambiente TEST do ALPLA Portal. Não representa um pedido real e nenhuma ação é necessária."
                                                style={{ ...inputStyle, minHeight: '60px', fontFamily: 'inherit', opacity: enableBodyWarningBanner ? 1 : 0.5 }}
                                                disabled={!enableBodyWarningBanner}
                                            />
                                            <p style={{ margin: '0.25rem 0 0', fontSize: '0.7rem', color: '#856404' }}>
                                                Deixe vazio para usar o texto de aviso padrão do sistema.
                                            </p>
                                        </div>

                                        {/* Redirect Section */}
                                        <div style={{
                                            padding: '1rem',
                                            backgroundColor: 'color-mix(in srgb, #1565C0 6%, transparent)',
                                            borderRadius: 'var(--radius-sm)',
                                            border: '1px solid color-mix(in srgb, #1565C0 25%, var(--color-border))'
                                        }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.75rem' }}>
                                                <input
                                                    type="checkbox"
                                                    checked={redirectAllToTestRecipient}
                                                    onChange={e => setRedirectAllToTestRecipient(e.target.checked)}
                                                    style={{ width: '16px', height: '16px' }}
                                                    id="smtp-env-redirect"
                                                />
                                                <label htmlFor="smtp-env-redirect" style={{ fontSize: '0.8125rem', fontWeight: 600, cursor: 'pointer' }}>
                                                    Redirecionar todos os e-mails para destinatário de teste
                                                </label>
                                            </div>
                                            <input
                                                type="email"
                                                value={testRecipientEmail}
                                                onChange={e => setTestRecipientEmail(e.target.value)}
                                                placeholder="teste@empresa.com"
                                                style={{ ...inputStyle, marginBottom: '0.75rem', opacity: redirectAllToTestRecipient ? 1 : 0.5 }}
                                                disabled={!redirectAllToTestRecipient}
                                            />
                                            {redirectAllToTestRecipient && (
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                                    <input
                                                        type="checkbox"
                                                        checked={showOriginalRecipientsInBody}
                                                        onChange={e => setShowOriginalRecipientsInBody(e.target.checked)}
                                                        style={{ width: '16px', height: '16px' }}
                                                        id="smtp-env-show-original"
                                                    />
                                                    <label htmlFor="smtp-env-show-original" style={{ fontSize: '0.8125rem', cursor: 'pointer' }}>
                                                        Mostrar destinatários originais no corpo do e-mail
                                                    </label>
                                                </div>
                                            )}
                                        </div>

                                        {/* Safety Override */}
                                        <div style={{
                                            padding: '1rem',
                                            backgroundColor: 'color-mix(in srgb, var(--color-status-red) 6%, transparent)',
                                            borderRadius: 'var(--radius-sm)',
                                            border: '1px solid color-mix(in srgb, var(--color-status-red) 25%, var(--color-border))'
                                        }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                                <input
                                                    type="checkbox"
                                                    checked={allowRealRecipientsInNonProduction}
                                                    onChange={e => setAllowRealRecipientsInNonProduction(e.target.checked)}
                                                    style={{ width: '16px', height: '16px' }}
                                                    id="smtp-env-allow-real"
                                                />
                                                <label htmlFor="smtp-env-allow-real" style={{ fontSize: '0.8125rem', fontWeight: 600, cursor: 'pointer', color: 'var(--color-status-red)' }}>
                                                    Permitir envio para destinatários reais em ambiente de teste
                                                </label>
                                            </div>
                                            <p style={{ margin: '0.5rem 0 0 1.5rem', fontSize: '0.7rem', color: 'var(--color-status-red)' }}>
                                                ⚠️ ATENÇÃO: Ao ativar, os e-mails serão enviados para os destinatários reais mesmo em TEST/DEV.
                                            </p>
                                        </div>
                                    </div>
                                )}
                            </div>
                        </>
                    )}
                </div>

                <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'flex-end' }}>
                    <button onClick={onClose} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                        color: 'var(--color-text-main)', cursor: 'pointer', fontSize: '0.875rem'
                    }}>
                        Cancelar
                    </button>
                    <button onClick={handleSave} disabled={saving} style={{
                        padding: '0.5rem 1.25rem', borderRadius: 'var(--radius-sm)',
                        border: 'none', backgroundColor: 'var(--color-primary)',
                        color: 'white', cursor: 'pointer',
                        fontSize: '0.875rem', fontWeight: 600,
                        display: 'flex', alignItems: 'center', gap: '0.5rem'
                    }}>
                        {saving && <Loader2 size={14} className="spin" />}
                        Salvar Configurações
                    </button>
                </div>
            </div>
        </div>
    );
}

/*
 * ──────────────────────────────────────────────────────
 *  Provider Card Component
 * ──────────────────────────────────────────────────────
 */

function ProviderCard({ provider, onRefresh }: {
    provider: IntegrationSettingsDto;
    onRefresh: () => void;
}) {
    const [expanded, setExpanded] = useState(false);
    const [testing, setTesting] = useState(false);
    const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
    const [toggling, setToggling] = useState(false);
    const [secretModal, setSecretModal] = useState<'PASSWORD' | 'API_KEY' | null>(null);
    const [primaveraSecretModal, setPrimaveraSecretModal] = useState<{ key: 'ALPLAPLASTICO' | 'ALPLASOPRO'; name: string } | null>(null);
    const [alplaProdSecretModal, setAlplaProdSecretModal] = useState<{ key: string; name: string } | null>(null);
    const [configModal, setConfigModal] = useState(false);
    const [alplaProdConfigModal, setAlplaProdConfigModal] = useState<string | null>(null);

    const [companyTesting, setCompanyTesting] = useState<string | null>(null);
    const [companyTestResults, setCompanyTestResults] = useState<Record<string, { success: boolean; message: string }>>({});
    const [plantTesting, setPlantTesting] = useState<string | null>(null);
    const [plantTestResults, setPlantTestResults] = useState<Record<string, { success: boolean; message: string }>>({});

    const icon = PROVIDER_ICONS[provider.code] || <Globe size={24} />;
    const isSQL = provider.connectionType === 'SQL';
    const isAPI = provider.connectionType === 'REST_API';
    const isSMTP = provider.code === 'SMTP';
    const isPrimavera = provider.code === 'PRIMAVERA';
    const isAlplaProd = provider.code === 'ALPLAPROD';

    let plasticoDb = '';
    let plasticoEnabled = true;
    let soproDb = '';
    let soproEnabled = true;

    if (isPrimavera && provider.additionalConfig) {
        try {
            const parsed = JSON.parse(provider.additionalConfig);
            if (parsed?.Companies) {
                const plastico = parsed.Companies.ALPLAPLASTICO;
                if (plastico) {
                    plasticoDb = plastico.DatabaseName || '';
                    plasticoEnabled = plastico.Enabled !== false;
                }
                const sopro = parsed.Companies.ALPLASOPRO;
                if (sopro) {
                    soproDb = sopro.DatabaseName || '';
                    soproEnabled = sopro.Enabled !== false;
                }
            }
        } catch {}
    }

    async function handleTest() {
        setTesting(true);
        setTestResult(null);
        try {
            const result = await api.admin.integrationSettings.testConnection(provider.code);
            setTestResult({
                success: result.success,
                message: result.success
                    ? `Conexão OK — ${result.responseTimeMs ?? '?'}ms`
                    : result.message || 'Falha na conexão.'
            });
        } catch (err: unknown) {
            setTestResult({ success: false, message: err instanceof Error ? err.message : 'Erro ao testar conexão.' });
        } finally {
            setTesting(false);
        }
    }

    async function handleToggle() {
        setToggling(true);
        try {
            if (provider.isEnabled) {
                await api.admin.integrationSettings.disable(provider.code);
            } else {
                await api.admin.integrationSettings.enable(provider.code);
            }
            onRefresh();
        } catch { /* ignored */ } finally {
            setToggling(false);
        }
    }

    async function handleTestCompany(companyKey: string) {
        setCompanyTesting(companyKey);
        setCompanyTestResults(prev => ({ ...prev, [companyKey]: undefined as any }));
        try {
            const result = await api.admin.integrationSettings.testConnection(provider.code, companyKey);
            setCompanyTestResults(prev => ({
                ...prev,
                [companyKey]: {
                    success: result.success,
                    message: result.success
                        ? `Conexão OK — ${result.responseTimeMs ?? '?'}ms`
                        : result.message || 'Falha na conexão.'
                }
            }));
        } catch (err: unknown) {
            setCompanyTestResults(prev => ({
                ...prev,
                [companyKey]: {
                    success: false,
                    message: err instanceof Error ? err.message : 'Erro ao testar conexão.'
                }
            }));
        } finally {
            setCompanyTesting(null);
        }
    }

    async function handleTestPlant(plantKey: string) {
        setPlantTesting(plantKey);
        setPlantTestResults(prev => ({ ...prev, [plantKey]: undefined as any }));
        try {
            const result = await api.admin.integrationSettings.testConnection(provider.code, plantKey);
            setPlantTestResults(prev => ({
                ...prev,
                [plantKey]: {
                    success: result.success,
                    message: result.success
                        ? `Conexão OK — ${result.responseTimeMs ?? '?'}ms`
                        : result.message || 'Falha na conexão.'
                }
            }));
        } catch (err: unknown) {
            setPlantTestResults(prev => ({
                ...prev,
                [plantKey]: {
                    success: false,
                    message: err instanceof Error ? err.message : 'Erro ao testar conexão.'
                }
            }));
        } finally {
            setPlantTesting(null);
        }
    }

    return (
        <div
            data-tour="integrations-provider-card"
            style={{
                backgroundColor: 'var(--color-bg-surface)',
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-lg)',
                overflow: 'hidden',
                transition: 'box-shadow 0.2s ease'
            }}
        >
            {/* Card Header */}
            <div
                data-tour="integrations-configure-btn"
                style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    padding: '1.25rem 1.5rem', cursor: 'pointer',
                    borderBottom: expanded ? '1px solid var(--color-border)' : 'none'
                }}
                onClick={() => setExpanded(!expanded)}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
                    <div style={{
                        width: '44px', height: '44px', borderRadius: 'var(--radius-md)',
                        backgroundColor: provider.isEnabled
                            ? 'color-mix(in srgb, var(--color-primary) 12%, transparent)'
                            : 'color-mix(in srgb, var(--color-text-muted) 8%, transparent)',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        color: provider.isEnabled ? 'var(--color-primary)' : 'var(--color-text-muted)'
                    }}>
                        {icon}
                    </div>
                    <div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                            <h3 style={{ margin: 0, fontSize: '1rem', fontWeight: 700 }}>{provider.name}</h3>
                            <span style={{
                                fontSize: '0.6875rem', fontWeight: 600, textTransform: 'uppercase',
                                padding: '0.125rem 0.5rem', borderRadius: '4px',
                                backgroundColor: 'color-mix(in srgb, var(--color-text-muted) 10%, transparent)',
                                color: 'var(--color-text-muted)', letterSpacing: '0.05em'
                            }}>
                                {provider.code}
                            </span>
                            {provider.isReadOnly && (
                                <span style={{
                                    fontSize: '0.6875rem', fontWeight: 600,
                                    padding: '0.125rem 0.5rem', borderRadius: '4px',
                                    backgroundColor: 'color-mix(in srgb, var(--color-status-red) 10%, transparent)',
                                    color: 'var(--color-status-red)',
                                    display: 'inline-flex', alignItems: 'center', gap: '0.25rem'
                                }}>
                                    <Lock size={10} /> SOMENTE LEITURA
                                </span>
                            )}
                        </div>
                        <p style={{ margin: '0.25rem 0 0', fontSize: '0.8125rem', color: 'var(--color-text-muted)' }}>
                            {DESCRIPTION_TRANSLATIONS[provider.description || ''] || provider.description || `${provider.providerType} / ${provider.connectionType}`}
                        </p>
                    </div>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
                    <StatusBadge status={provider.lastTestStatus} />
                    {expanded ? <ChevronUp size={18} /> : <ChevronDown size={18} />}
                </div>
            </div>

            {/* Expanded Detail */}
            {expanded && (
                <div style={{ padding: '1.5rem' }}>
                    {/* Settings Grid */}
                    <div style={{
                        display: 'grid',
                        gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
                        gap: '1rem', marginBottom: '1.5rem'
                    }}>
                        {isSQL && (
                            <>
                                <SettingField label="Servidor" value={provider.server} icon={<Server size={14} />} />
                                {isPrimavera ? (
                                    <>
                                        <SettingField 
                                            label="Alpla Plástico (Base)" 
                                            value={plasticoDb ? `${plasticoDb} (${plasticoEnabled ? 'Ativo' : 'Inativo'})` : undefined} 
                                            icon={<Database size={14} />} 
                                        />
                                        <SettingField 
                                            label="Alpla Plástico (Utilizador)" 
                                            value={provider.primaveraCompanies?.find(c => c.companyKey === 'ALPLAPLASTICO')?.username} 
                                        />
                                        <SettingField 
                                            label="Alpla Sopro (Base)" 
                                            value={soproDb ? `${soproDb} (${soproEnabled ? 'Ativo' : 'Inativo'})` : undefined} 
                                            icon={<Database size={14} />} 
                                        />
                                        <SettingField 
                                            label="Alpla Sopro (Utilizador)" 
                                            value={provider.primaveraCompanies?.find(c => c.companyKey === 'ALPLASOPRO')?.username} 
                                        />
                                    </>
                                ) : (
                                    <SettingField label="Base de Dados" value={provider.databaseName} icon={<Database size={14} />} />
                                )}
                                <SettingField label="Instância" value={provider.instanceName} />
                                <SettingField label="Autenticação" value={provider.authenticationMode} />
                                <SettingField label="Utilizador" value={provider.username} />
                            </>
                        )}
                        {isAPI && (
                            <>
                                <SettingField label="URL Base" value={provider.apiBaseUrl} icon={<Globe size={14} />} />
                            </>
                        )}
                        {isSMTP && (
                            <>
                                <SettingField label="Servidor (Host)" value={provider.server} icon={<Server size={14} />} />
                                <SettingField label="Porta" value={provider.port?.toString()} />
                                <SettingField label="SSL/TLS" value={provider.enableSsl !== undefined ? (provider.enableSsl ? 'Ativado' : 'Desativado') : undefined} icon={<Shield size={14} />} />
                                <SettingField label="E-mail Remetente" value={provider.senderEmail} icon={<Mail size={14} />} />
                                <SettingField label="Nome Remetente" value={provider.senderName} />
                                <SettingField label="Utilizador" value={provider.username} />
                            </>
                        )}
                        <SettingField label="Timeout" value={provider.timeoutSeconds ? `${provider.timeoutSeconds}s` : undefined} />
                    </div>

                    {/* Secrets Section */}
                    {!isPrimavera && (
                        <div style={{
                            padding: '1rem', borderRadius: 'var(--radius-md)',
                            backgroundColor: 'color-mix(in srgb, var(--color-text-muted) 5%, transparent)',
                            marginBottom: '1.5rem'
                        }}>
                            <h4 style={{ margin: '0 0 0.75rem', fontSize: '0.8125rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>
                                <Shield size={14} style={{ marginRight: '0.375rem', verticalAlign: 'middle' }} />
                                Segredos
                            </h4>
                            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '1.5rem', alignItems: 'center' }}>
                                {isSQL && (
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                                        <SecretIndicator hasSecret={provider.hasPassword} version={provider.secretVersion} />
                                        {!provider.isReadOnly && (
                                            <button
                                                data-tour="integrations-secret-btn"
                                                onClick={() => setSecretModal('PASSWORD')}
                                                style={{
                                                    padding: '0.375rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                    border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                                                    color: 'var(--color-text-main)', cursor: 'pointer',
                                                    fontSize: '0.75rem', fontWeight: 600,
                                                    display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                }}
                                            >
                                                <Key size={12} /> Substituir Senha
                                            </button>
                                        )}
                                    </div>
                                )}
                                {isSMTP && (
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                                        <SecretIndicator hasSecret={provider.hasPassword} version={provider.secretVersion} />
                                        {!provider.isReadOnly && (
                                            <button
                                                data-tour="integrations-secret-btn"
                                                onClick={() => setSecretModal('PASSWORD')}
                                                style={{
                                                    padding: '0.375rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                    border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                                                    color: 'var(--color-text-main)', cursor: 'pointer',
                                                    fontSize: '0.75rem', fontWeight: 600,
                                                    display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                }}
                                            >
                                                <Key size={12} /> Substituir Senha
                                            </button>
                                        )}
                                    </div>
                                )}
                                {isAPI && (
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                                        <SecretIndicator hasSecret={provider.hasApiKey} version={provider.secretVersion} />
                                        {!provider.isReadOnly && (
                                            <button
                                                data-tour="integrations-secret-btn"
                                                onClick={() => setSecretModal('API_KEY')}
                                                style={{
                                                    padding: '0.375rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                    border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                                                    color: 'var(--color-text-main)', cursor: 'pointer',
                                                    fontSize: '0.75rem', fontWeight: 600,
                                                    display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                }}
                                            >
                                                <Key size={12} /> Substituir Chave API
                                            </button>
                                        )}
                                    </div>
                                )}
                            </div>
                        </div>
                    )}

                    {isPrimavera && (
                        <div style={{
                            padding: '1.25rem', borderRadius: 'var(--radius-lg)',
                            backgroundColor: 'color-mix(in srgb, var(--color-text-muted) 4%, transparent)',
                            border: '1px solid var(--color-border)',
                            marginBottom: '1.5rem'
                        }}>
                            <h4 style={{
                                margin: '0 0 1rem 0',
                                fontSize: '0.875rem',
                                fontWeight: 700,
                                textTransform: 'uppercase',
                                color: 'var(--color-text-muted)',
                                display: 'flex',
                                alignItems: 'center',
                                gap: '0.5rem'
                            }}>
                                <Database size={16} /> Bases de Dados e Credenciais por Empresa
                            </h4>

                            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                                {[
                                    { key: 'ALPLAPLASTICO', name: 'Alpla Plástico' },
                                    { key: 'ALPLASOPRO', name: 'Alpla Sopro' }
                                ].map(companyInfo => {
                                    const cSettings = provider.primaveraCompanies?.find(c => c.companyKey === companyInfo.key);
                                    const dbName = cSettings?.databaseName || '—';
                                    const isEnabled = cSettings?.enabled !== false;
                                    const usr = cSettings?.username || '—';
                                    const hasPass = cSettings?.hasPassword ?? false;
                                    const secVer = cSettings?.secretVersion ?? 0;
                                    const testingCompany = companyTesting === companyInfo.key;
                                    const testRes = companyTestResults[companyInfo.key];

                                    return (
                                        <div key={companyInfo.key} style={{
                                            padding: '1rem',
                                            borderRadius: 'var(--radius-md)',
                                            border: '1px solid var(--color-border)',
                                            backgroundColor: 'var(--color-bg-surface)',
                                            display: 'flex',
                                            flexDirection: 'column',
                                            gap: '0.75rem'
                                        }}>
                                            {/* Company Row Header */}
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '0.5rem' }}>
                                                <h5 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 700 }}>
                                                    {companyInfo.name}
                                                </h5>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                                    {isEnabled && !hasPass && (
                                                        <span style={{
                                                            display: 'inline-flex', alignItems: 'center', gap: '0.25rem',
                                                            padding: '0.2rem 0.5rem', borderRadius: '4px',
                                                            fontSize: '0.7rem', fontWeight: 600,
                                                            color: 'var(--color-status-red)',
                                                            backgroundColor: 'color-mix(in srgb, var(--color-status-red) 12%, transparent)',
                                                            border: '1px solid var(--color-status-red)'
                                                        }}>
                                                            <AlertCircle size={10} /> Senha não configurada.
                                                        </span>
                                                    )}
                                                    <span style={{
                                                        display: 'inline-flex', alignItems: 'center', gap: '0.25rem',
                                                        padding: '0.2rem 0.5rem', borderRadius: '9999px',
                                                        fontSize: '0.7rem', fontWeight: 600,
                                                        color: isEnabled ? 'var(--color-status-green)' : 'var(--color-text-muted)',
                                                        backgroundColor: isEnabled 
                                                            ? 'color-mix(in srgb, var(--color-status-green) 12%, transparent)' 
                                                            : 'color-mix(in srgb, var(--color-text-muted) 12%, transparent)'
                                                    }}>
                                                        {isEnabled ? 'Ativo' : 'Inativo'}
                                                    </span>
                                                </div>
                                            </div>

                                            {/* Settings Grid */}
                                            <div style={{
                                                display: 'grid',
                                                gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
                                                gap: '0.75rem',
                                                fontSize: '0.8125rem'
                                            }}>
                                                <div>
                                                    <div style={{ color: 'var(--color-text-muted)', fontWeight: 600, marginBottom: '0.125rem' }}>BASE DE DADOS</div>
                                                    <div style={{ color: 'var(--color-text-main)', fontFamily: 'monospace' }}>{dbName}</div>
                                                </div>
                                                <div>
                                                    <div style={{ color: 'var(--color-text-muted)', fontWeight: 600, marginBottom: '0.125rem' }}>UTILIZADOR</div>
                                                    <div style={{ color: 'var(--color-text-main)' }}>{usr}</div>
                                                </div>
                                                <div>
                                                    <div style={{ color: 'var(--color-text-muted)', fontWeight: 600, marginBottom: '0.125rem' }}>SENHA</div>
                                                    <SecretIndicator hasSecret={hasPass} version={secVer} />
                                                </div>
                                            </div>

                                            {/* Company Test Result */}
                                            {testRes && (
                                                <div style={{
                                                    padding: '0.5rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                    backgroundColor: testRes.success
                                                        ? 'color-mix(in srgb, var(--color-status-green) 8%, transparent)'
                                                        : 'color-mix(in srgb, var(--color-status-red) 8%, transparent)',
                                                    color: testRes.success ? 'var(--color-status-green)' : 'var(--color-status-red)',
                                                    fontSize: '0.75rem', display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                }}>
                                                    {testRes.success ? <CheckCircle2 size={14} /> : <XCircle size={14} />}
                                                    {testRes.message}
                                                </div>
                                            )}

                                            {/* Actions */}
                                            <div style={{ display: 'flex', gap: '0.5rem', marginTop: '0.25rem' }}>
                                                {!provider.isReadOnly && (
                                                    <button
                                                        onClick={() => setPrimaveraSecretModal({ key: companyInfo.key as any, name: companyInfo.name })}
                                                        style={{
                                                            padding: '0.375rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                            border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                                                            color: 'var(--color-text-main)', cursor: 'pointer',
                                                            fontSize: '0.75rem', fontWeight: 600,
                                                            display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                        }}
                                                    >
                                                        <Key size={12} /> Substituir Senha
                                                    </button>
                                                )}
                                                <button
                                                    onClick={() => handleTestCompany(companyInfo.key)}
                                                    disabled={testingCompany}
                                                    style={{
                                                        padding: '0.375rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                        border: '1px solid var(--color-primary)', backgroundColor: 'transparent',
                                                        color: 'var(--color-primary)', cursor: testingCompany ? 'wait' : 'pointer',
                                                        fontSize: '0.75rem', fontWeight: 600,
                                                        display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                    }}
                                                >
                                                    {testingCompany ? <Loader2 size={12} className="spin" /> : <RefreshCw size={12} />}
                                                    Testar Conexão
                                                </button>
                                            </div>
                                        </div>
                                    );
                                })}
                            </div>
                        </div>
                    )}

                    {isAlplaProd && provider.alplaProdPlants && provider.alplaProdPlants.length > 0 && (
                        <div style={{
                            padding: '1.25rem', borderRadius: 'var(--radius-lg)',
                            backgroundColor: 'color-mix(in srgb, var(--color-text-muted) 4%, transparent)',
                            border: '1px solid var(--color-border)',
                            marginBottom: '1.5rem'
                        }}>
                            <h4 style={{
                                margin: '0 0 1rem 0',
                                fontSize: '0.875rem',
                                fontWeight: 700,
                                textTransform: 'uppercase',
                                color: 'var(--color-text-muted)',
                                display: 'flex',
                                alignItems: 'center',
                                gap: '0.5rem'
                            }}>
                                <Database size={16} /> Bases de Dados e Credenciais por Planta
                            </h4>

                            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                                {provider.alplaProdPlants.map(plantSettings => {
                                    const plantName = PLANT_DISPLAY_NAMES[plantSettings.plantKey] || plantSettings.plantKey;
                                    const isEnabled = plantSettings.enabled !== false;
                                    const hasPass = plantSettings.hasPassword ?? false;
                                    const secVer = plantSettings.secretVersion ?? 0;
                                    const testingPlant = plantTesting === plantSettings.plantKey;
                                    const testRes = plantTestResults[plantSettings.plantKey];

                                    return (
                                        <div key={plantSettings.plantKey} style={{
                                            padding: '1rem',
                                            borderRadius: 'var(--radius-md)',
                                            border: '1px solid var(--color-border)',
                                            backgroundColor: 'var(--color-bg-surface)',
                                            display: 'flex',
                                            flexDirection: 'column',
                                            gap: '0.75rem'
                                        }}>
                                            {/* Plant Row Header */}
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '0.5rem' }}>
                                                <h5 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 700 }}>
                                                    {plantName}
                                                </h5>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                                    {isEnabled && !hasPass && (
                                                        <span style={{
                                                            display: 'inline-flex', alignItems: 'center', gap: '0.25rem',
                                                            padding: '0.2rem 0.5rem', borderRadius: '4px',
                                                            fontSize: '0.7rem', fontWeight: 600,
                                                            color: plantSettings.usesGlobalCredentials ? '#d97706' : 'var(--color-status-red)',
                                                            backgroundColor: plantSettings.usesGlobalCredentials
                                                                ? 'rgba(217, 119, 6, 0.12)'
                                                                : 'color-mix(in srgb, var(--color-status-red) 12%, transparent)',
                                                            border: `1px solid ${plantSettings.usesGlobalCredentials ? '#d97706' : 'var(--color-status-red)'}`
                                                        }}>
                                                            <AlertCircle size={10} />
                                                            {plantSettings.usesGlobalCredentials ? 'Usando credencial global' : 'Senha não configurada.'}
                                                        </span>
                                                    )}
                                                    {isEnabled && hasPass && plantSettings.usesGlobalCredentials && (
                                                        <span style={{
                                                            display: 'inline-flex', alignItems: 'center', gap: '0.25rem',
                                                            padding: '0.2rem 0.5rem', borderRadius: '4px',
                                                            fontSize: '0.7rem', fontWeight: 600,
                                                            color: '#d97706',
                                                            backgroundColor: 'rgba(217, 119, 6, 0.12)'
                                                        }}>
                                                            Credencial global
                                                        </span>
                                                    )}
                                                    <span style={{
                                                        display: 'inline-flex', alignItems: 'center', gap: '0.25rem',
                                                        padding: '0.2rem 0.5rem', borderRadius: '9999px',
                                                        fontSize: '0.7rem', fontWeight: 600,
                                                        color: isEnabled ? 'var(--color-status-green)' : 'var(--color-text-muted)',
                                                        backgroundColor: isEnabled
                                                            ? 'color-mix(in srgb, var(--color-status-green) 12%, transparent)'
                                                            : 'color-mix(in srgb, var(--color-text-muted) 12%, transparent)'
                                                    }}>
                                                        {isEnabled ? 'Ativo' : 'Inativo'}
                                                    </span>
                                                </div>
                                            </div>

                                            {/* Settings Grid */}
                                            <div style={{
                                                display: 'grid',
                                                gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))',
                                                gap: '0.75rem',
                                                fontSize: '0.8125rem'
                                            }}>
                                                <div>
                                                    <div style={{ color: 'var(--color-text-muted)', fontWeight: 600, marginBottom: '0.125rem' }}>SERVIDOR</div>
                                                    <div style={{ color: 'var(--color-text-main)', fontFamily: 'monospace' }}>{plantSettings.server || '—'}</div>
                                                </div>
                                                <div>
                                                    <div style={{ color: 'var(--color-text-muted)', fontWeight: 600, marginBottom: '0.125rem' }}>BASE DE DADOS</div>
                                                    <div style={{ color: 'var(--color-text-main)', fontFamily: 'monospace' }}>{plantSettings.databaseName || '—'}</div>
                                                </div>
                                                <div>
                                                    <div style={{ color: 'var(--color-text-muted)', fontWeight: 600, marginBottom: '0.125rem' }}>UTILIZADOR</div>
                                                    <div style={{ color: 'var(--color-text-main)' }}>{plantSettings.username || '—'}</div>
                                                </div>
                                                <div>
                                                    <div style={{ color: 'var(--color-text-muted)', fontWeight: 600, marginBottom: '0.125rem' }}>SENHA</div>
                                                    <SecretIndicator hasSecret={hasPass} version={secVer} />
                                                </div>
                                            </div>

                                            {/* Plant Test Result */}
                                            {testRes && (
                                                <div style={{
                                                    padding: '0.5rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                    backgroundColor: testRes.success
                                                        ? 'color-mix(in srgb, var(--color-status-green) 8%, transparent)'
                                                        : 'color-mix(in srgb, var(--color-status-red) 8%, transparent)',
                                                    color: testRes.success ? 'var(--color-status-green)' : 'var(--color-status-red)',
                                                    fontSize: '0.75rem', display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                }}>
                                                    {testRes.success ? <CheckCircle2 size={14} /> : <XCircle size={14} />}
                                                    {testRes.message}
                                                </div>
                                            )}

                                            {/* Actions */}
                                            <div style={{ display: 'flex', gap: '0.5rem', marginTop: '0.25rem' }}>
                                                <button
                                                    onClick={() => setAlplaProdConfigModal(plantSettings.plantKey)}
                                                    style={{
                                                        padding: '0.375rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                        border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                                                        color: 'var(--color-text-main)', cursor: 'pointer',
                                                        fontSize: '0.75rem', fontWeight: 600,
                                                        display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                    }}
                                                >
                                                    <Server size={12} /> Configurar
                                                </button>
                                                <button
                                                    onClick={() => setAlplaProdSecretModal({ key: plantSettings.plantKey, name: plantName })}
                                                    style={{
                                                        padding: '0.375rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                        border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                                                        color: 'var(--color-text-main)', cursor: 'pointer',
                                                        fontSize: '0.75rem', fontWeight: 600,
                                                        display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                    }}
                                                >
                                                    <Key size={12} /> Substituir Senha
                                                </button>
                                                <button
                                                    onClick={() => handleTestPlant(plantSettings.plantKey)}
                                                    disabled={testingPlant}
                                                    style={{
                                                        padding: '0.375rem 0.75rem', borderRadius: 'var(--radius-sm)',
                                                        border: '1px solid var(--color-primary)', backgroundColor: 'transparent',
                                                        color: 'var(--color-primary)', cursor: testingPlant ? 'wait' : 'pointer',
                                                        fontSize: '0.75rem', fontWeight: 600,
                                                        display: 'flex', alignItems: 'center', gap: '0.375rem'
                                                    }}
                                                >
                                                    {testingPlant ? <Loader2 size={12} className="spin" /> : <RefreshCw size={12} />}
                                                    Testar Conexão
                                                </button>
                                            </div>
                                        </div>
                                    );
                                })}
                            </div>
                        </div>
                    )}

                    {/* Test Result */}
                    {testResult && (
                        <div style={{
                            padding: '0.75rem 1rem', borderRadius: 'var(--radius-md)', marginBottom: '1rem',
                            backgroundColor: testResult.success
                                ? 'color-mix(in srgb, var(--color-status-green) 10%, transparent)'
                                : 'color-mix(in srgb, var(--color-status-red) 10%, transparent)',
                            color: testResult.success ? 'var(--color-status-green)' : 'var(--color-status-red)',
                            fontSize: '0.8125rem', display: 'flex', alignItems: 'center', gap: '0.5rem'
                        }}>
                            {testResult.success ? <CheckCircle2 size={16} /> : <XCircle size={16} />}
                            {testResult.message}
                        </div>
                    )}

                    {/* Action Buttons */}
                    <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap' }}>
                        {!provider.isReadOnly && (
                            <button
                                onClick={() => setConfigModal(true)}
                                style={{
                                    padding: '0.5rem 1rem', borderRadius: 'var(--radius-sm)',
                                    border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                                    color: 'var(--color-text-main)', cursor: 'pointer',
                                    fontSize: '0.8125rem', fontWeight: 600,
                                    display: 'flex', alignItems: 'center', gap: '0.5rem'
                                }}
                            >
                                <Server size={14} /> Configurar
                            </button>
                        )}

                        <button
                            data-tour="integrations-test-btn"
                            onClick={handleTest}
                            disabled={testing}
                            style={{
                                padding: '0.5rem 1rem', borderRadius: 'var(--radius-sm)',
                                border: 'none', backgroundColor: 'var(--color-primary)',
                                color: 'white', cursor: testing ? 'wait' : 'pointer',
                                fontSize: '0.8125rem', fontWeight: 600,
                                display: 'flex', alignItems: 'center', gap: '0.5rem'
                            }}
                        >
                            {testing ? <Loader2 size={14} className="spin" /> : <RefreshCw size={14} />}
                            Testar Conexão
                        </button>

                        <button
                            onClick={handleToggle}
                            disabled={toggling}
                            style={{
                                padding: '0.5rem 1rem', borderRadius: 'var(--radius-sm)',
                                border: '1px solid var(--color-border)', backgroundColor: 'transparent',
                                color: provider.isEnabled ? 'var(--color-status-red)' : 'var(--color-status-green)',
                                cursor: toggling ? 'wait' : 'pointer',
                                fontSize: '0.8125rem', fontWeight: 600,
                                display: 'flex', alignItems: 'center', gap: '0.5rem'
                            }}
                        >
                            {provider.isEnabled ? <><Unlock size={14} /> Desabilitar</> : <><Lock size={14} /> Habilitar</>}
                        </button>
                    </div>

                    {/* Audit Footer */}
                    {provider.updatedAt && (
                        <div style={{
                            marginTop: '1rem', paddingTop: '0.75rem',
                            borderTop: '1px solid var(--color-border)',
                            fontSize: '0.75rem', color: 'var(--color-text-muted)',
                            display: 'flex', gap: '1rem'
                        }}>
                            <span>Última atualização: {new Date(provider.updatedAt).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })}</span>
                            {provider.updatedByUserName && <span>por {provider.updatedByUserName}</span>}
                        </div>
                    )}
                </div>
            )}

            {/* Secret Replace Modal */}
            {secretModal && (
                <SecretReplaceModal
                    provider={provider}
                    secretType={secretModal}
                    onClose={() => setSecretModal(null)}
                    onSuccess={() => { setSecretModal(null); onRefresh(); }}
                />
            )}

            {/* Configure Connection Modal */}
            {configModal && (
                <ConnectionConfigureModal
                    provider={provider}
                    onClose={() => setConfigModal(false)}
                    onSuccess={() => { setConfigModal(false); onRefresh(); }}
                />
            )}

            {/* Primavera Company Secret Modal */}
            {primaveraSecretModal && (
                <PrimaveraCompanySecretModal
                    companyKey={primaveraSecretModal.key}
                    companyName={primaveraSecretModal.name}
                    onClose={() => setPrimaveraSecretModal(null)}
                    onSuccess={() => { setPrimaveraSecretModal(null); onRefresh(); }}
                />
            )}

            {/* AlplaPROD Plant Secret Modal */}
            {alplaProdSecretModal && (
                <AlplaProdPlantSecretModal
                    plantKey={alplaProdSecretModal.key}
                    plantName={alplaProdSecretModal.name}
                    onClose={() => setAlplaProdSecretModal(null)}
                    onSuccess={() => { setAlplaProdSecretModal(null); onRefresh(); }}
                />
            )}

            {/* AlplaPROD Plant Config Modal */}
            {alplaProdConfigModal && (
                <AlplaProdPlantConfigModal
                    plantKey={alplaProdConfigModal}
                    provider={provider}
                    onClose={() => setAlplaProdConfigModal(null)}
                    onSuccess={() => { setAlplaProdConfigModal(null); onRefresh(); }}
                />
            )}
        </div>
    );
}

function SettingField({ label, value, icon }: { label: string; value?: string; icon?: React.ReactNode }) {
    return (
        <div>
            <div style={{ fontSize: '0.6875rem', fontWeight: 600, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '0.25rem', display: 'flex', alignItems: 'center', gap: '0.25rem' }}>
                {icon} {label}
            </div>
            <div style={{ fontSize: '0.875rem', color: value ? 'var(--color-text-main)' : 'var(--color-text-muted)', fontStyle: value ? 'normal' : 'italic' }}>
                {value || '—'}
            </div>
        </div>
    );
}

/*
 * ──────────────────────────────────────────────────────
 *  IntegrationSettings — Main Page
 * ──────────────────────────────────────────────────────
 */

export function IntegrationSettings() {
    const [providers, setProviders] = useState<IntegrationSettingsDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');

    const loadProviders = useCallback(async () => {
        try {
            setLoading(true);
            const data = await api.admin.integrationSettings.getAll();
            setProviders(data);
            setError('');
        } catch (err: unknown) {
            setError(err instanceof Error ? err.message : 'Falha ao carregar configurações.');
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => { loadProviders(); }, [loadProviders]);

    return (
        <PageContainer>
            <PageHeader
                title="Gestão de Integrações"
                subtitle="Configure credenciais, endpoints e segredos das integrações externas"
                data-tour="integrations-header"
            />

            {error && (
                <div style={{
                    padding: '1rem', marginBottom: '1.5rem', borderRadius: 'var(--radius-md)',
                    backgroundColor: 'color-mix(in srgb, var(--color-status-red) 10%, transparent)',
                    color: 'var(--color-status-red)', fontSize: '0.875rem',
                    display: 'flex', alignItems: 'center', gap: '0.5rem'
                }}>
                    <AlertCircle size={16} /> {error}
                </div>
            )}

            {loading ? (
                <div style={{ display: 'flex', justifyContent: 'center', padding: '4rem 0' }}>
                    <Loader2 size={32} className="spin" style={{ color: 'var(--color-primary)' }} />
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                    {providers.map(p => (
                        <ProviderCard key={p.code} provider={p} onRefresh={loadProviders} />
                    ))}

                    {providers.length === 0 && !error && (
                        <div style={{
                            textAlign: 'center', padding: '4rem 2rem',
                            color: 'var(--color-text-muted)', fontSize: '0.9375rem'
                        }}>
                            Nenhum provedor de integração encontrado.
                        </div>
                    )}
                </div>
            )}

            <style>{`
                @keyframes spin { to { transform: rotate(360deg); } }
                .spin { animation: spin 1s linear infinite; }
            `}</style>
        </PageContainer>
    );
}
