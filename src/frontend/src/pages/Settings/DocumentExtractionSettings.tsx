import React, { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { DocumentExtractionSettingsDto } from '../../types';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { Save, Info, AlertTriangle, Cpu, Globe, Braces, Settings } from 'lucide-react';

export function DocumentExtractionSettings() {
    const [settings, setSettings] = useState<DocumentExtractionSettingsDto | null>(null);
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
            const data = await api.admin.extractionSettings.get();
            setSettings(data);
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
            await api.admin.extractionSettings.update(settings);
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
        <div style={{ padding: '2rem', maxWidth: '900px', margin: '0 auto' }}>
            <header style={{ marginBottom: '2rem', display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                <div>
                    <h1 style={{ fontSize: '1.75rem', margin: 0 }}>Extração de Documentos</h1>
                    <p style={{ color: 'var(--color-text-muted)', marginTop: '0.25rem' }}>
                        Administração de provedores e parâmetros operacionais de OCR e IA.
                    </p>
                </div>
                {settings && (
                    <button
                        onClick={handleSave}
                        disabled={saving}
                        className="btn-primary"
                        style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', minWidth: '180px', justifyContent: 'center' }}
                    >
                        <Save size={18} />
                        {saving ? 'A GUARDAR...' : 'GUARDAR ALTERAÇÕES'}
                    </button>
                )}
            </header>

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
                    border: '2px solid var(--color-border-heavy)', 
                    padding: '1.5rem',
                    boxShadow: 'var(--shadow-brutal)'
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
                            <div style={{ display: 'flex', gap: '0.5rem' }}>
                                <select
                                    value={settings.defaultProvider}
                                    onChange={(e) => handleChange('defaultProvider', e.target.value)}
                                    style={{ flex: 1 }}
                                >
                                    <option value="LOCAL_OCR">Local OCR (Tesseract / Python)</option>
                                    <option value="OPENAI">OpenAI Vision (Experimental)</option>
                                    <option value="AZURE_DOCUMENT_INTELLIGENCE">Azure Document Intelligence</option>
                                </select>
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
                                        fontSize: '0.75rem',
                                        height: '2.5rem',
                                        minWidth: '150px',
                                        justifyContent: 'center'
                                    }}
                                >
                                    {testing ? 'A TESTAR...' : 'TESTAR CONEXÃO'}
                                </button>
                            </div>
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
                </section>

                {/* Local OCR Section */}
                <section style={{ 
                    backgroundColor: 'var(--color-bg-surface)', 
                    border: '2px solid var(--color-border-heavy)', 
                    padding: '1.5rem',
                    boxShadow: 'var(--shadow-brutal)'
                }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.5rem', borderBottom: '1px solid var(--color-border)', paddingBottom: '0.75rem' }}>
                        <h2 style={{ fontSize: '1.1rem', margin: 0, display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                            <Cpu size={20} color="var(--color-primary)" />
                            Local OCR
                        </h2>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                            <label style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: settings.localOcrEnabled ? 'var(--color-status-green)' : 'var(--color-text-muted)' }}>
                                {settings.localOcrEnabled ? 'ATIVO' : 'DESATIVADO'}
                            </label>
                            <input
                                type="checkbox"
                                checked={settings.localOcrEnabled}
                                onChange={(e) => handleChange('localOcrEnabled', e.target.checked)}
                                style={{ width: '1.1rem', height: '1.1rem', cursor: 'pointer' }}
                            />
                        </div>
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: '1.5rem' }}>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 700, textTransform: 'uppercase' }}>URL Base do Serviço</label>
                            <input
                                type="text"
                                value={settings.localOcrBaseUrl || ''}
                                onChange={(e) => handleChange('localOcrBaseUrl', e.target.value)}
                                placeholder="ex: http://local-ocr:5000"
                                required={settings.localOcrEnabled}
                                style={{ width: '100%' }}
                            />
                            <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                Endereço interno do serviço Python responsável pelo processamento Tesseract.
                            </p>
                        </div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', maxWidth: '300px' }}>
                            <label style={{ fontSize: '0.85rem', fontWeight: 700, textTransform: 'uppercase' }}>Timeout Específico (Segundos)</label>
                            <input
                                type="number"
                                value={settings.localOcrTimeoutSeconds || ''}
                                onChange={(e) => handleChange('localOcrTimeoutSeconds', e.target.value ? parseInt(e.target.value) : null)}
                                placeholder="Seguir Global"
                                style={{ width: '100%' }}
                            />
                        </div>
                    </div>
                </section>

                {/* OpenAI Section */}
                <section style={{ 
                    backgroundColor: 'var(--color-bg-surface)', 
                    border: '2px solid var(--color-border-heavy)', 
                    padding: '1.5rem',
                    boxShadow: 'var(--shadow-brutal)',
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
                    border: '2px solid var(--color-border)', 
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

            <footer style={{ marginTop: '3rem', padding: '1rem', backgroundColor: 'rgba(217, 119, 6, 0.05)', borderLeft: '4px solid var(--color-status-amber)', display: 'flex', gap: '1rem' }}>
                <AlertTriangle size={24} color="var(--color-status-amber)" style={{ flexShrink: 0 }} />
                <p style={{ fontSize: '0.85rem', margin: 0, fontWeight: 500 }}>
                    <strong>Atenção:</strong> Alterações nestas configurações afetam o comportamento imediato do motor de extração para todos os utilizadores. 
                    Verifique a conectividade do provedor selecionado antes de guardar.
                </p>
            </footer>
        </div>
    );
}
