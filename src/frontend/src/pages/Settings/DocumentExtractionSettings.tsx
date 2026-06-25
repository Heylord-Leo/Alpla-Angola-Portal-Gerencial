import React, { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { DocumentExtractionSettingsDto, OcrModuleConfigDto } from '../../types';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { Save, Info, AlertTriangle, Globe, Braces, Settings, Layers } from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

export function DocumentExtractionSettings() {
    const [settings, setSettings] = useState<DocumentExtractionSettingsDto | null>(null);
    const [modules, setModules] = useState<OcrModuleConfigDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [feedback, setFeedback] = useState<{ message: string, type: FeedbackType } | null>(null);

    const [testing, setTesting] = useState(false);
    const [testResult, setTestResult] = useState<{ success: boolean, message: string, responseTimeMs?: number } | null>(null);

    useEffect(() => {
        loadSettings();
    }, []);

    const loadSettings = async () => {
        try {
            setLoading(true);
            const [data, mods] = await Promise.all([
                api.admin.extractionSettings.get(),
                api.admin.extractionSettings.getModules()
            ]);
            setSettings(data);
            setModules(mods);
        } catch (err: any) {
            setFeedback({ message: 'Falha ao carregar configurações de extração.', type: 'error' });
        } finally {
            setLoading(false);
        }
    };

    const handleSave = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!settings) return;

        try {
            setSaving(true);
            setFeedback(null);
            
            // Save global settings
            await api.admin.extractionSettings.update(settings);

            // Save modules
            for (const mod of modules) {
                await api.admin.extractionSettings.updateModule(mod.moduleKey, mod);
            }

            setFeedback({ message: 'Configurações guardadas com sucesso.', type: 'success' });
        } catch (err: any) {
            setFeedback({ message: err.message || 'Falha ao guardar configurações.', type: 'error' });
        } finally {
            setSaving(false);
        }
    };

    const handleTestConnection = async () => {
        try {
            setTesting(true);
            setTestResult(null);
            const result = await api.admin.extractionSettings.testConnection();
            setTestResult(result);
        } catch (err: any) {
            setTestResult({ 
                success: false, 
                message: err.message || 'Erro ao tentar contactar o servidor para o teste.' 
            });
        } finally {
            setTesting(false);
        }
    };

    const handleChange = (field: keyof DocumentExtractionSettingsDto, value: any) => {
        setSettings(prev => prev ? { ...prev, [field]: value } : null);
    };

    const handleModuleChange = (moduleKey: string, field: keyof OcrModuleConfigDto, value: any) => {
        setModules(prev => prev.map(m => m.moduleKey === moduleKey ? { ...m, [field]: value } : m));
    };

    if (loading) {
        return (
            <div style={{ padding: '2rem', display: 'flex', justifyContent: 'center' }}>
                <div style={{ 
                    width: '2rem', 
                    height: '2rem', 
                    border: '3px solid rgba(0, 77, 144, 0.2)', 
                    borderTopColor: 'var(--color-primary)', 
                    borderRadius: '50%', 
                    animation: 'spin 1s linear infinite' 
                }} />
                <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
            </div>
        );
    }

    return (
        <PageContainer>
            <PageHeader
                title="Extração de Documentos"
                subtitle="Administração de provedores e parâmetros operacionais de OCR e IA."
                icon={<Settings size={32} strokeWidth={2.5} />}
                actions={
                    settings ? (
                        <button
                            onClick={handleSave}
                            disabled={saving}
                            className="btn-primary"
                            style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', minWidth: '180px', justifyContent: 'center' }}
                        >
                            <Save size={18} />
                            {saving ? 'A GUARDAR...' : 'GUARDAR ALTERAÇÕES'}
                        </button>
                    ) : undefined
                }
            />

            {(feedback || testResult) && (
                <div style={{ marginBottom: '1.5rem', position: 'sticky', top: '1rem', zIndex: 10, display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                    {feedback && (
                        <Feedback 
                            message={feedback.message} 
                            type={feedback.type} 
                            onClose={() => setFeedback(null)} 
                        />
                    )}
                    {testResult && (
                        <div style={{ 
                            padding: '1rem', 
                            backgroundColor: testResult.success ? 'rgba(21, 128, 61, 0.1)' : 'rgba(185, 28, 28, 0.1)',
                            border: `1px solid ${testResult.success ? 'var(--color-status-green)' : 'var(--color-status-red)'}`,
                            borderLeftWidth: '4px',
                            display: 'flex',
                            justifyContent: 'space-between',
                            alignItems: 'center'
                        }}>
                            <div>
                                <p style={{ margin: 0, fontWeight: 700, fontSize: '0.9rem', color: testResult.success ? 'var(--color-status-green)' : 'var(--color-status-red)' }}>
                                    {testResult.success ? 'Teste de Conexão Bem-sucedido' : 'Falha no Teste de Conexão'}
                                </p>
                                <p style={{ margin: 0, fontSize: '0.85rem' }}>
                                    {testResult.message}
                                    {testResult.responseTimeMs !== undefined && ` (${testResult.responseTimeMs}ms)`}
                                </p>
                            </div>
                            <button 
                                onClick={() => setTestResult(null)}
                                style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '0.5rem', opacity: 0.6 }}
                            >
                                ✕
                            </button>
                        </div>
                    )}
                </div>
            )}

            {!settings && !loading && (
                <div style={{ 
                    padding: '3rem', 
                    textAlign: 'center', 
                    backgroundColor: 'var(--color-bg-surface)', 
                    border: '2px dashed var(--color-border)',
                    color: 'var(--color-text-muted)'
                }}>
                    <AlertTriangle size={48} style={{ marginBottom: '1rem', opacity: 0.5 }} />
                    <p>Não foi possível carregar as configurações.</p>
                    <button 
                        onClick={loadSettings}
                        className="btn-secondary"
                        style={{ marginTop: '1rem' }}
                    >
                        Tentar Novamente
                    </button>
                </div>
            )}

            {settings ? (
                <form onSubmit={handleSave} style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
                {/* Configurações Globais */}
                <section style={{ 
                    backgroundColor: 'var(--color-bg-surface)', 
                    border: '1px solid var(--color-border)', 
                    borderRadius: 'var(--radius-lg)',
                    padding: '1.5rem'
                }}>
                    <h2 style={{ fontSize: '1.1rem', marginBottom: '1.5rem', display: 'flex', alignItems: 'center', gap: '0.5rem', borderBottom: '1px solid var(--color-border)', paddingBottom: '0.75rem' }}>
                        <Settings size={20} color="var(--color-primary)" />
                        Configurações Gerais
                    </h2>
                    
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '1.5rem' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 700, textTransform: 'uppercase' }}>Sistema de Extração</label>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', padding: '0.75rem', backgroundColor: 'var(--color-bg-page)', border: '1px solid var(--color-border)' }}>
                                <input
                                    type="checkbox"
                                    checked={settings.isEnabled}
                                    onChange={(e) => handleChange('isEnabled', e.target.checked)}
                                    style={{ width: '1.25rem', height: '1.25rem', cursor: 'pointer' }}
                                />
                                <span style={{ fontSize: '0.9rem', fontWeight: 500 }}>Permitir processamento de documentos</span>
                            </div>
                        </div>

                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 700, textTransform: 'uppercase' }}>Provedor Ativo</label>
                            <select
                                value={settings.defaultProvider}
                                onChange={(e) => handleChange('defaultProvider', e.target.value)}
                                style={{ width: '100%' }}
                            >
                                <option value="OPENAI">OpenAI Vision</option>
                                <option value="AZURE_DOCUMENT_INTELLIGENCE">Azure Document Intelligence</option>
                            </select>
                        </div>

                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 700, textTransform: 'uppercase' }}>Timeout Global (Segundos)</label>
                            <input
                                type="number"
                                value={settings.globalTimeoutSeconds}
                                onChange={(e) => handleChange('globalTimeoutSeconds', parseInt(e.target.value) || 30)}
                                min="1"
                                style={{ width: '100%' }}
                            />
                        </div>
                    </div>

                    <div style={{ display: 'flex', justifyContent: 'flex-start', marginTop: '1.5rem', borderTop: '1px solid var(--color-border)', paddingTop: '1.5rem' }}>
                        <button
                            type="button"
                            onClick={handleTestConnection}
                            disabled={testing || !settings.isEnabled}
                            className="btn-secondary"
                            style={{ 
                                display: 'flex', 
                                alignItems: 'center', 
                                gap: '0.5rem', 
                                padding: '0 1rem',
                                fontSize: '0.8rem',
                                height: '2.5rem',
                                minWidth: '180px',
                                justifyContent: 'center'
                            }}
                        >
                            <Globe size={16} /> {testing ? 'A TESTAR...' : 'TESTAR CONEXÃO DO PROVEDOR'}
                        </button>
                    </div>
                </section>

                {/* Módulos OCR */}
                <section style={{ 
                    backgroundColor: 'var(--color-bg-surface)', 
                    border: '1px solid var(--color-border)', 
                    borderRadius: 'var(--radius-lg)',
                    padding: '1.5rem'
                }}>
                    <h2 style={{ fontSize: '1.1rem', marginBottom: '1.5rem', display: 'flex', alignItems: 'center', gap: '0.5rem', borderBottom: '1px solid var(--color-border)', paddingBottom: '0.75rem' }}>
                        <Layers size={20} color="var(--color-primary)" />
                        Módulos OCR
                    </h2>

                    {modules.length === 0 ? (
                        <p style={{ color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>Nenhum módulo OCR configurado.</p>
                    ) : (
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                            {modules.map(mod => (
                                <div key={mod.moduleKey} style={{ 
                                    padding: '1rem', 
                                    border: '1px solid var(--color-border)', 
                                    borderRadius: 'var(--radius-md)',
                                    display: 'flex',
                                    flexDirection: 'column',
                                    gap: '1rem',
                                    backgroundColor: mod.isEnabled ? 'var(--color-bg-page)' : 'rgba(0,0,0,0.02)'
                                }}>
                                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                        <div>
                                            <h3 style={{ margin: 0, fontSize: '1rem', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                                                {mod.displayName}
                                                <span style={{ fontSize: '0.7rem', padding: '0.1rem 0.4rem', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-sm)', color: 'var(--color-text-muted)' }}>
                                                    {mod.moduleKey}
                                                </span>
                                            </h3>
                                        </div>
                                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                                            <label style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: mod.isEnabled ? 'var(--color-status-blue)' : 'var(--color-text-muted)' }}>
                                                {mod.isEnabled ? 'ATIVO' : 'DESATIVADO'}
                                            </label>
                                            <input
                                                type="checkbox"
                                                checked={mod.isEnabled}
                                                onChange={(e) => handleModuleChange(mod.moduleKey, 'isEnabled', e.target.checked)}
                                                style={{ width: '1.1rem', height: '1.1rem', cursor: 'pointer' }}
                                            />
                                        </div>
                                    </div>

                                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '1rem' }}>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem' }}>
                                            <label style={{ fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Extensões Permitidas</label>
                                            <input
                                                type="text"
                                                value={mod.allowedExtensions || ''}
                                                onChange={(e) => handleModuleChange(mod.moduleKey, 'allowedExtensions', e.target.value)}
                                                placeholder="ex: .pdf,.png"
                                                style={{ width: '100%', fontSize: '0.85rem', padding: '0.4rem' }}
                                            />
                                        </div>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem' }}>
                                            <label style={{ fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Provedor Específico</label>
                                            <select
                                                value={mod.providerOverride || ''}
                                                onChange={(e) => handleModuleChange(mod.moduleKey, 'providerOverride', e.target.value || null)}
                                                style={{ width: '100%', fontSize: '0.85rem', padding: '0.4rem' }}
                                            >
                                                <option value="">(Usar Global)</option>
                                                <option value="OPENAI">OpenAI Vision</option>
                                                <option value="AZURE_DOCUMENT_INTELLIGENCE">Azure Document Intelligence</option>
                                            </select>
                                        </div>
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem' }}>
                                            <label style={{ fontSize: '0.75rem', fontWeight: 700, textTransform: 'uppercase', color: 'var(--color-text-muted)' }}>Modelo Específico</label>
                                            <input
                                                type="text"
                                                value={mod.modelOverride || ''}
                                                onChange={(e) => handleModuleChange(mod.moduleKey, 'modelOverride', e.target.value)}
                                                placeholder="(Usar Global)"
                                                style={{ width: '100%', fontSize: '0.85rem', padding: '0.4rem' }}
                                            />
                                        </div>
                                    </div>
                                </div>
                            ))}
                        </div>
                    )}
                </section>

                {/* OpenAI Section */}
                <section style={{ 
                    backgroundColor: 'var(--color-bg-surface)', 
                    border: '1px solid var(--color-border)', 
                    borderRadius: 'var(--radius-lg)',
                    padding: '1.5rem',
                    opacity: settings.openAiEnabled ? 1 : 0.8
                }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.5rem', borderBottom: '1px solid var(--color-border)', paddingBottom: '0.75rem' }}>
                        <h2 style={{ fontSize: '1.1rem', margin: 0, display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                            <Globe size={20} color="var(--color-primary)" />
                            OpenAI Vision
                        </h2>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                            <label style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: settings.openAiEnabled ? 'var(--color-status-blue)' : 'var(--color-text-muted)' }}>
                                {settings.openAiEnabled ? 'ATIVO' : 'DESATIVADO'}
                            </label>
                            <input
                                type="checkbox"
                                checked={settings.openAiEnabled}
                                onChange={(e) => handleChange('openAiEnabled', e.target.checked)}
                                style={{ width: '1.1rem', height: '1.1rem', cursor: 'pointer' }}
                            />
                        </div>
                    </div>
 
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '1.5rem' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 700, textTransform: 'uppercase' }}>Modelo OpenAI</label>
                            <select
                                value={settings.openAiModel || 'gpt-4o-mini'}
                                onChange={(e) => handleChange('openAiModel', e.target.value)}
                                style={{ width: '100%' }}
                            >
                                <option value="gpt-4o-mini">gpt-4o-mini (Recomendado - Rápido/Barato)</option>
                                <option value="gpt-4o">gpt-4o (Alta Performance)</option>
                            </select>
                            <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                O modelo gpt-4o-mini é ideal para extração de dados estruturados em POS.
                            </p>
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 700, textTransform: 'uppercase' }}>Timeout OpenAI (Segundos)</label>
                            <input
                                type="number"
                                value={settings.openAiTimeoutSeconds || ''}
                                onChange={(e) => handleChange('openAiTimeoutSeconds', e.target.value ? parseInt(e.target.value) : null)}
                                placeholder="Seguir Global"
                                style={{ width: '100%' }}
                            />
                            <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                Chaves de API (OPENAI_API_KEY) são geridas apenas no servidor.
                            </p>
                        </div>
                    </div>
                </section>

                {/* Azure Section (Placeholder) */}
                <section style={{ 
                    backgroundColor: 'var(--color-bg-surface)', 
                    border: '1px solid var(--color-border)', 
                    borderRadius: 'var(--radius-lg)',
                    padding: '1.5rem',
                    opacity: settings.azureDocumentIntelligenceEnabled ? 1 : 0.8
                }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem', borderBottom: '1px solid var(--color-border)', paddingBottom: '0.75rem' }}>
                        <h2 style={{ fontSize: '1.1rem', margin: 0, display: 'flex', alignItems: 'center', gap: '0.5rem', color: 'var(--color-text-muted)' }}>
                            <Braces size={20} />
                            Azure Document Intelligence
                            <span style={{ fontSize: '0.65rem', fontWeight: 800, backgroundColor: 'rgba(147, 51, 234, 0.1)', color: 'var(--color-status-purple)', padding: '0.2rem 0.5rem', border: '1px solid var(--color-status-purple)' }}>EM BREVE</span>
                        </h2>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                            <input
                                type="checkbox"
                                checked={settings.azureDocumentIntelligenceEnabled}
                                onChange={(e) => handleChange('azureDocumentIntelligenceEnabled', e.target.checked)}
                                style={{ width: '1.1rem', height: '1.1rem', cursor: 'pointer' }}
                            />
                        </div>
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                         <div style={{ backgroundColor: 'rgba(147, 51, 234, 0.05)', borderLeft: '4px solid var(--color-status-purple)', padding: '1rem', display: 'flex', gap: '0.75rem' }}>
                            <Info size={18} color="var(--color-status-purple)" style={{ flexShrink: 0 }} />
                            <p style={{ fontSize: '0.85rem', color: 'var(--color-text-main)', margin: 0 }}>
                                <strong>Preparado para Configuração:</strong> O suporte para Azure Document Intelligence está planeado. 
                                Os segredos e endpoints devem permanecer no ficheiro de configuração do servidor.
                            </p>
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', maxWidth: '300px', opacity: 0.5 }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 700, textTransform: 'uppercase' }}>Timeout Azure (Segundos)</label>
                            <input type="number" disabled placeholder="Bloqueado" style={{ width: '100%' }} />
                        </div>
                    </div>
                </section>
            </form>
            ) : null}

            <footer style={{ marginTop: '1rem', padding: '1rem', backgroundColor: 'rgba(217, 119, 6, 0.05)', borderLeft: '4px solid var(--color-status-amber)', display: 'flex', gap: '1rem', borderRadius: 'var(--radius-md)' }}>
                <AlertTriangle size={24} color="var(--color-status-amber)" style={{ flexShrink: 0 }} />
                <p style={{ fontSize: '0.85rem', margin: 0, fontWeight: 500 }}>
                    <strong>Atenção:</strong> Alterações nestas configurações afetam o comportamento imediato do motor de extração para todos os utilizadores. 
                    Verifique a conectividade do provedor selecionado antes de guardar.
                </p>
            </footer>
        </PageContainer>
    );
}
