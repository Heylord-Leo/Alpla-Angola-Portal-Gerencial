import { useState, useEffect, useCallback } from 'react';
import { Network, Database, Search, Cpu, Activity, Zap, RefreshCw, Clock, AlertCircle, CheckCircle2, XCircle, HelpCircle, Loader2 } from 'lucide-react';
import { api } from '../../lib/api';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

/*
 * ──────────────────────────────────────────────────────
 *  Type definitions for integration health API responses
 * ──────────────────────────────────────────────────────
 */

interface IntegrationProviderStatus {
    code: string;
    name: string;
    providerType: string;
    connectionType: string;
    description?: string;
    environment?: string;
    isEnabled: boolean;
    isPlanned: boolean;
    displayOrder: number;
    capabilities: string[];
    currentStatus: string;
    lastSuccessUtc?: string;
    lastFailureUtc?: string;
    lastCheckedAtUtc?: string;
    lastResponseTimeMs?: number;
    lastErrorMessage?: string;
    consecutiveFailures: number;
    lastTestedByEmail?: string;
    hasConnectionSettings: boolean;
    hasImplementation: boolean;
    canTestConnection: boolean;
}

interface IntegrationHealthSummary {
    providers: IntegrationProviderStatus[];
    checkedAtUtc: string;
}

/*
 * ──────────────────────────────────────────────────────
 *  Status code → display mapping
 *  Machine-readable codes come from backend IntegrationStatusCodes.
 *  Display labels are managed exclusively here in the frontend.
 * ──────────────────────────────────────────────────────
 */

const STATUS_DISPLAY: Record<string, { label: string; color: string; bg: string; icon: React.ReactNode }> = {
    HEALTHY:        { label: 'Operacional',    color: 'var(--color-status-green)',  bg: 'color-mix(in srgb, var(--color-status-green) 15%, transparent)', icon: <CheckCircle2 size={14} /> },
    UNHEALTHY:      { label: 'Com Falhas',     color: 'var(--color-status-red)',    bg: 'color-mix(in srgb, var(--color-status-red) 15%, transparent)',   icon: <XCircle size={14} /> },
    UNREACHABLE:    { label: 'Inacessível',    color: 'var(--color-status-red)',    bg: 'color-mix(in srgb, var(--color-status-red) 15%, transparent)',   icon: <AlertCircle size={14} /> },
    NOT_CONFIGURED: { label: 'Não Configurado', color: 'var(--color-text-main)',    bg: 'color-mix(in srgb, var(--color-text-muted) 15%, transparent)',   icon: <HelpCircle size={14} /> },
    PLANNED:        { label: 'Prevista',       color: 'var(--color-status-blue)',   bg: 'color-mix(in srgb, var(--color-status-blue) 15%, transparent)',  icon: <Clock size={14} /> },
};

const PROVIDER_TYPE_LABELS: Record<string, string> = {
    ERP: 'Enterprise Resource Planning',
    BIOMETRIC: 'Biometric / Time & Attendance',
    PRODUCTION: 'Production & Supply Chain',
    API: 'API Service',
    OTHER: 'External System',
};

/**
 * Format a UTC timestamp to locale display string.
 */
function formatTimestamp(utcString?: string): string {
    if (!utcString) return '—';
    try {
        const d = new Date(utcString);
        return d.toLocaleString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    } catch { return '—'; }
}

/*
 * ──────────────────────────────────────────────────────
 *  IntegrationProviderCard — data-driven provider card
 * ──────────────────────────────────────────────────────
 */

function IntegrationProviderCard({ provider, onTestConnection, isTesting }: {
    provider: IntegrationProviderStatus;
    onTestConnection: (code: string) => void;
    isTesting: boolean;
}) {
    const statusInfo = STATUS_DISPLAY[provider.currentStatus] || STATUS_DISPLAY.NOT_CONFIGURED;
    const typeLabel = PROVIDER_TYPE_LABELS[provider.providerType] || provider.providerType;
    const isPlanned = provider.isPlanned;

    return (
        <div style={{
            backgroundColor: isPlanned ? 'transparent' : 'var(--color-bg-surface)',
            border: isPlanned ? '1px dashed var(--color-border)' : '1px solid var(--color-border)',
            borderTop: isPlanned ? '1px dashed var(--color-border)' : `4px solid ${provider.currentStatus === 'NOT_CONFIGURED' ? 'var(--color-text-muted)' : statusInfo.color}`,
            borderRadius: 'var(--radius-lg)',
            padding: '1.5rem',
            opacity: isPlanned ? 0.8 : 1,
            transition: 'box-shadow 0.2s',
        }}>
            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '1rem' }}>
                <div>
                    <h3 style={{ margin: 0, fontSize: '1.25rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-text-primary)' }}>
                        {provider.name}
                    </h3>
                    <span style={{ fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.5px' }}>
                        {typeLabel}
                    </span>
                </div>
                <span style={{
                    fontSize: '0.7rem',
                    fontWeight: 800,
                    padding: '6px 12px',
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '6px',
                    backgroundColor: statusInfo.bg,
                    color: statusInfo.color,
                    borderRadius: 'var(--radius-sm)',
                    textTransform: 'uppercase',
                    letterSpacing: '0.5px',
                    height: 'fit-content',
                }}>
                    {statusInfo.icon} {statusInfo.label}
                </span>
            </div>

            {/* Description */}
            <p style={{ margin: '0 0 1.25rem 0', fontSize: '0.875rem', color: 'var(--color-text-main)', lineHeight: 1.6, fontWeight: 500 }}>
                {provider.description}
            </p>

            {/* Capabilities */}
            {provider.capabilities.length > 0 && (
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.35rem', marginBottom: '1rem' }}>
                    {provider.capabilities.map(cap => (
                        <span key={cap} style={{
                            fontSize: '0.65rem',
                            fontWeight: 800,
                            padding: '3px 8px',
                            backgroundColor: 'color-mix(in srgb, var(--color-text-muted) 5%, transparent)',
                            border: '1px solid color-mix(in srgb, var(--color-border) 50%, transparent)',
                            borderRadius: '4px',
                            color: 'var(--color-text-main)',
                            textTransform: 'uppercase',
                            letterSpacing: '0.5px'
                        }}>
                            {cap.replace(/_/g, ' ')}
                        </span>
                    ))}
                </div>
            )}

            {/* Connection metadata (only for non-planned) */}
            {!isPlanned && (
                <div style={{
                    fontSize: '0.8rem',
                    color: 'var(--color-text-primary)',
                    display: 'grid',
                    gridTemplateColumns: '1fr 1fr',
                    gap: '0.75rem 1rem',
                    marginBottom: '1rem',
                    padding: '1rem',
                    backgroundColor: 'color-mix(in srgb, var(--color-bg-page) 50%, transparent)',
                    borderRadius: 'var(--radius-md)',
                    border: '1px solid color-mix(in srgb, var(--color-border) 60%, transparent)',
                }}>
                    <div>
                        <span style={{ fontWeight: 800, display: 'block', fontSize: '0.65rem', textTransform: 'uppercase', marginBottom: '4px', color: 'var(--color-text-muted)', letterSpacing: '0.5px' }}>Conexão</span>
                        <span style={{ fontWeight: 600 }}>{provider.connectionType}</span>
                    </div>
                    <div>
                        <span style={{ fontWeight: 800, display: 'block', fontSize: '0.65rem', textTransform: 'uppercase', marginBottom: '4px', color: 'var(--color-text-muted)', letterSpacing: '0.5px' }}>Ambiente</span>
                        <span style={{ fontWeight: 600 }}>{provider.environment || '—'}</span>
                    </div>
                    <div>
                        <span style={{ fontWeight: 800, display: 'block', fontSize: '0.65rem', textTransform: 'uppercase', marginBottom: '4px', color: 'var(--color-text-muted)', letterSpacing: '0.5px' }}>Último Teste</span>
                        <span style={{ fontWeight: 500 }}>{formatTimestamp(provider.lastCheckedAtUtc)}</span>
                    </div>
                    <div>
                        <span style={{ fontWeight: 800, display: 'block', fontSize: '0.65rem', textTransform: 'uppercase', marginBottom: '4px', color: 'var(--color-text-muted)', letterSpacing: '0.5px' }}>Tempo Resposta</span>
                        <span style={{ fontWeight: 500 }}>{provider.lastResponseTimeMs != null ? `${provider.lastResponseTimeMs}ms` : '—'}</span>
                    </div>
                    {provider.lastSuccessUtc && (
                        <div>
                            <span style={{ fontWeight: 800, display: 'block', fontSize: '0.65rem', textTransform: 'uppercase', marginBottom: '4px', color: 'var(--color-text-muted)', letterSpacing: '0.5px' }}>Último Sucesso</span>
                            <span style={{ fontWeight: 500 }}>{formatTimestamp(provider.lastSuccessUtc)}</span>
                        </div>
                    )}
                    {provider.consecutiveFailures > 0 && (
                        <div>
                            <span style={{ fontWeight: 800, display: 'block', fontSize: '0.65rem', textTransform: 'uppercase', marginBottom: '4px', color: 'var(--color-status-red)', letterSpacing: '0.5px' }}>Falhas Consecutivas</span>
                            <span style={{ color: 'var(--color-status-red)', fontWeight: 800 }}>{provider.consecutiveFailures}</span>
                        </div>
                    )}
                </div>
            )}

            {/* Error message */}
            {provider.lastErrorMessage && !isPlanned && (
                <div style={{
                    fontSize: '0.75rem',
                    color: 'var(--color-status-red)',
                    padding: '0.5rem 0.75rem',
                    backgroundColor: 'color-mix(in srgb, var(--color-status-red) 8%, transparent)',
                    borderRadius: 'var(--radius-sm)',
                    marginBottom: '1rem',
                    border: '1px solid color-mix(in srgb, var(--color-status-red) 20%, transparent)',
                }}>
                    <strong style={{ textTransform: 'uppercase', fontSize: '0.65rem' }}>Último Erro:</strong><br />
                    {provider.lastErrorMessage}
                </div>
            )}

            {/* Footer: roadmap badge or test connection button */}
            {isPlanned ? (
                <div style={{ fontSize: '0.7rem', fontWeight: 900, color: 'var(--color-status-blue)', textTransform: 'uppercase' }}>
                    <Clock size={12} style={{ display: 'inline', marginRight: '4px', verticalAlign: 'middle' }} />
                    Prevista uma fase futura
                </div>
            ) : (
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    {/* Connection test availability info */}
                    {!provider.canTestConnection && (
                        <span style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                        {!provider.isEnabled
                            ? (provider.hasImplementation ? 'Provedor não activado — configurar em appsettings' : 'Provedor desactivado')
                            : !provider.hasConnectionSettings ? 'Sem configuração de conexão'
                            : !provider.hasImplementation ? 'Sem implementação registada' : ''}
                        </span>
                    )}
                    {provider.canTestConnection && <span />}

                    <button
                        onClick={() => onTestConnection(provider.code)}
                        disabled={!provider.canTestConnection || isTesting}
                        style={{
                            fontSize: '0.75rem',
                            fontWeight: 800,
                            padding: '8px 16px',
                            border: provider.canTestConnection ? '2px solid var(--color-border)' : '2px dashed color-mix(in srgb, var(--color-border) 40%, transparent)',
                            borderRadius: 'var(--radius-sm)',
                            backgroundColor: provider.canTestConnection ? 'var(--color-bg-surface)' : 'color-mix(in srgb, var(--color-bg-page) 50%, transparent)',
                            color: provider.canTestConnection ? 'var(--color-text-primary)' : 'var(--color-text-muted)',
                            cursor: provider.canTestConnection && !isTesting ? 'pointer' : 'not-allowed',
                            opacity: provider.canTestConnection ? 1 : 0.65,
                            textTransform: 'uppercase',
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: '6px',
                            transition: 'background-color 0.15s, border-color 0.15s, opacity 0.15s',
                        }}
                    >
                        {isTesting ? <Loader2 size={12} className="spin" /> : <Zap size={12} />}
                        Testar Conexão
                    </button>
                </div>
            )}
        </div>
    );
}

/*
 * ──────────────────────────────────────────────────────
 *  OcrServiceCard — existing internal service health card
 *
 *  OCR is an existing internal service/diagnostic item (uses AdminDiagnosticsController).
 *  Primavera/Innux are external provider integrations (uses IntegrationHealthController).
 *  Both are shown on this screen, but they are not the same architectural category.
 *  This separation is intentional — see DECISIONS.md.
 * ──────────────────────────────────────────────────────
 */

function OcrServiceCard({ status }: { status: string | null }) {
    const isHealthy = status === 'Operacional';

    return (
        <div style={{
            backgroundColor: 'var(--color-bg-surface)',
            border: '1px solid var(--color-border)',
            borderRadius: 'var(--radius-lg)',
            padding: '1.5rem',
        }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '1rem' }}>
                <div>
                    <h3 style={{ margin: 0, fontSize: '1.15rem', fontWeight: 900, textTransform: 'uppercase' }}>
                        Extração de Documentos
                    </h3>
                    <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                        OCR / AI Service — Serviço Interno
                    </span>
                </div>
                <span style={{
                    fontSize: '0.7rem',
                    fontWeight: 800,
                    padding: '4px 10px',
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '4px',
                    backgroundColor: status === null ? 'var(--color-text-muted)' : (isHealthy ? 'var(--color-status-green)' : 'var(--color-status-red)'),
                    color: 'white',
                    borderRadius: '2px',
                    textTransform: 'uppercase',
                    height: 'fit-content',
                }}>
                    {status === null ? <Loader2 size={12} /> : (isHealthy ? <CheckCircle2 size={14} /> : <XCircle size={14} />)}
                    {status || 'A verificar...'}
                </span>
            </div>
            <p style={{ margin: 0, fontSize: '0.875rem', color: 'var(--color-text-muted)', lineHeight: 1.5 }}>
                Monitorização da comunicação com os serviços de extração de dados de facturas, cotações e documentos operacionais (Local OCR e OpenAI).
            </p>
            <div style={{ marginTop: '0.75rem', fontSize: '0.65rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', opacity: 0.6 }}>
                Diagnóstico interno — não é provedor de integração externo
            </div>
        </div>
    );
}

/*
 * ──────────────────────────────────────────────────────
 *  IntegrationHealth — Main data-driven page
 * ──────────────────────────────────────────────────────
 */

export function IntegrationHealth() {
    const [ocrStatus, setOcrStatus] = useState<string | null>(null);
    const [integrationData, setIntegrationData] = useState<IntegrationHealthSummary | null>(null);
    const [loading, setLoading] = useState(true);
    const [testingProvider, setTestingProvider] = useState<string | null>(null);

    const loadData = useCallback(async () => {
        setLoading(true);
        try {
            const [healthData, intData] = await Promise.all([
                api.admin.diagnostics.getHealth().catch(() => null),
                api.admin.integrations.getHealth().catch(() => null),
            ]);

            if (healthData) {
                setOcrStatus(
                    healthData.localOcr?.status === 'Healthy' || healthData.openAi?.status === 'Healthy'
                        ? 'Operacional'
                        : 'Indisponível'
                );
            } else {
                setOcrStatus('Erro na verificação');
            }

            if (intData) {
                setIntegrationData(intData);
            }
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => { loadData(); }, [loadData]);

    const handleTestConnection = async (providerCode: string) => {
        setTestingProvider(providerCode);
        try {
            await api.admin.integrations.testConnection(providerCode);
            // Reload data to show updated status
            await loadData();
        } catch {
            // Error handling already in loadData
        } finally {
            setTestingProvider(null);
        }
    };

    const activeProviders = integrationData?.providers.filter(p => !p.isPlanned) || [];
    const plannedProviders = integrationData?.providers.filter(p => p.isPlanned) || [];

    return (
        <PageContainer>
            <PageHeader
                title="Saúde das Integrações"
                subtitle="Estado das comunicações com sistemas externos e provedores"
                icon={<Network size={32} strokeWidth={2.5} />}
            />

            {/* Refresh button */}
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '1rem' }}>
                <button
                    onClick={loadData}
                    disabled={loading}
                    style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        gap: '6px',
                        fontSize: '0.75rem',
                        fontWeight: 800,
                        padding: '6px 14px',
                        border: '2px solid var(--color-border)',
                        borderRadius: 'var(--radius-sm)',
                        backgroundColor: 'var(--color-bg-surface)',
                        color: 'var(--color-text-primary)',
                        cursor: loading ? 'not-allowed' : 'pointer',
                        textTransform: 'uppercase',
                    }}
                >
                    <RefreshCw size={12} className={loading ? 'spin' : ''} />
                    Actualizar
                </button>
            </div>

            {/* Section: Internal Service Diagnostics */}
            <div style={{ marginBottom: '2rem' }}>
                <h2 style={{
                    fontSize: '0.8rem',
                    fontWeight: 900,
                    textTransform: 'uppercase',
                    color: 'var(--color-text-muted)',
                    borderBottom: '2px solid var(--color-border)',
                    paddingBottom: '0.5rem',
                    marginBottom: '1rem',
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.5rem',
                }}>
                    <Activity size={14} /> Serviços Internos
                </h2>
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(350px, 1fr))', gap: '1.5rem' }}>
                    <OcrServiceCard status={ocrStatus} />
                </div>
            </div>

            {/* Section: Active Integration Providers */}
            {activeProviders.length > 0 && (
                <div style={{ marginBottom: '2rem' }}>
                    <h2 style={{
                        fontSize: '0.8rem',
                        fontWeight: 900,
                        textTransform: 'uppercase',
                        color: 'var(--color-text-muted)',
                        borderBottom: '2px solid var(--color-border)',
                        paddingBottom: '0.5rem',
                        marginBottom: '1rem',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.5rem',
                    }}>
                        <Zap size={14} /> Provedores Activos
                    </h2>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(350px, 1fr))', gap: '1.5rem' }}>
                        {activeProviders.map(provider => (
                            <IntegrationProviderCard
                                key={provider.code}
                                provider={provider}
                                onTestConnection={handleTestConnection}
                                isTesting={testingProvider === provider.code}
                            />
                        ))}
                    </div>
                </div>
            )}

            {/* Section: Planned/Roadmap Providers */}
            {plannedProviders.length > 0 && (
                <div style={{ marginBottom: '2rem' }}>
                    <h2 style={{
                        fontSize: '0.8rem',
                        fontWeight: 900,
                        textTransform: 'uppercase',
                        color: 'var(--color-text-muted)',
                        borderBottom: '2px solid var(--color-border)',
                        paddingBottom: '0.5rem',
                        marginBottom: '1rem',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '0.5rem',
                    }}>
                        <Clock size={14} /> Provedores Previstos
                    </h2>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(350px, 1fr))', gap: '1.5rem' }}>
                        {plannedProviders.map(provider => (
                            <IntegrationProviderCard
                                key={provider.code}
                                provider={provider}
                                onTestConnection={handleTestConnection}
                                isTesting={testingProvider === provider.code}
                            />
                        ))}
                    </div>
                </div>
            )}

            {/* Empty state / loading */}
            {loading && !integrationData && (
                <div style={{
                    border: '1px dashed var(--color-border)',
                    borderRadius: 'var(--radius-lg)',
                    padding: '3rem',
                    textAlign: 'center',
                }}>
                    <Loader2 size={32} style={{ margin: '0 auto', opacity: 0.3 }} className="spin" />
                    <p style={{ color: 'var(--color-text-muted)', marginTop: '1rem', fontWeight: 500 }}>
                        A carregar estado das integrações...
                    </p>
                </div>
            )}

            {/* Footer context */}
            {!loading && (
                <div style={{
                    border: '1px dashed var(--color-border)',
                    borderRadius: 'var(--radius-lg)',
                    padding: '2rem',
                    textAlign: 'center',
                }}>
                    <div style={{ display: 'flex', justifyContent: 'center', gap: '2rem', opacity: 0.15 }}>
                        <Database size={48} />
                        <Search size={48} />
                        <Cpu size={48} />
                    </div>
                    <p style={{ color: 'var(--color-text-muted)', marginTop: '1rem', fontWeight: 500, fontSize: '0.875rem' }}>
                        A plataforma de integrações está em evolução contínua. Novos provedores e domínios de dados serão adicionados progressivamente.
                    </p>
                </div>
            )}
        </PageContainer>
    );
}
