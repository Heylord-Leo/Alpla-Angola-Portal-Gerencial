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
    UNHEALTHY:      { label: 'Com Falhas',      color: 'var(--color-status-red)',    bg: 'color-mix(in srgb, var(--color-status-red) 15%, transparent)',   icon: <XCircle size={14} /> },
    UNREACHABLE:    { label: 'Inacessível',     color: 'var(--color-status-red)',    bg: 'color-mix(in srgb, var(--color-status-red) 15%, transparent)',   icon: <AlertCircle size={14} /> },
    NOT_CONFIGURED: { label: 'Não Configurado', color: 'var(--color-text-main)',     bg: 'color-mix(in srgb, var(--color-text-muted) 15%, transparent)',   icon: <HelpCircle size={14} /> },
    PLANNED:        { label: 'Prevista',        color: 'var(--color-status-blue)',   bg: 'color-mix(in srgb, var(--color-status-blue) 15%, transparent)',  icon: <Clock size={14} /> },
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

    const icon = PROVIDER_ICONS[provider.code] || <Globe size={24} />;
    const isSQL = provider.connectionType === 'SQL';
    const isAPI = provider.connectionType === 'REST_API' || provider.connectionType === 'SMTP';

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
                            {provider.description || `${provider.providerType} / ${provider.connectionType}`}
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
                                <SettingField label="Base de Dados" value={provider.databaseName} icon={<Database size={14} />} />
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
                        <SettingField label="Timeout" value={provider.timeoutSeconds ? `${provider.timeoutSeconds}s` : undefined} />
                    </div>

                    {/* Secrets Section */}
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
