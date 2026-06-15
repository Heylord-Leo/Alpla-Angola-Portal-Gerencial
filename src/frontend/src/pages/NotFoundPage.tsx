import { useNavigate } from 'react-router-dom';
import { AlertTriangle } from 'lucide-react';

/**
 * 404 — Not Found page.
 * Rendered for unrecognised routes (catch-all in App router).
 */
export default function NotFoundPage() {
    const navigate = useNavigate();

    return (
        <div style={{
            display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
            minHeight: '100vh', padding: 32,
            background: 'var(--color-bg, #f8fafc)',
            fontFamily: "'Inter', 'Segoe UI', system-ui, sans-serif"
        }}>
            <div style={{
                textAlign: 'center', maxWidth: 440,
                background: 'var(--color-bg-surface, #fff)',
                border: '1px solid var(--color-border, #e2e8f0)',
                borderRadius: 16, padding: '48px 40px',
                boxShadow: '0 4px 24px rgba(0,0,0,0.06)'
            }}>
                <div style={{
                    width: 64, height: 64, borderRadius: '50%',
                    background: 'rgba(245,158,11,0.1)',
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    margin: '0 auto 20px'
                }}>
                    <AlertTriangle size={32} style={{ color: '#f59e0b' }} />
                </div>

                <h1 style={{
                    fontSize: '3rem', fontWeight: 800,
                    color: 'var(--color-text, #1e293b)',
                    margin: '0 0 8px', lineHeight: 1
                }}>
                    404
                </h1>

                <h2 style={{
                    fontSize: '1.15rem', fontWeight: 600,
                    color: 'var(--color-text, #1e293b)',
                    margin: '0 0 8px'
                }}>
                    Página não encontrada
                </h2>

                <p style={{
                    color: 'var(--color-text-muted, #64748b)',
                    fontSize: '0.9rem', lineHeight: 1.6,
                    margin: '0 0 28px'
                }}>
                    O endereço que você tentou acessar não existe ou foi removido.
                </p>

                <div style={{ display: 'flex', gap: 10, justifyContent: 'center' }}>
                    <button
                        onClick={() => navigate(-1)}
                        style={{
                            padding: '10px 20px', border: '1px solid var(--color-border, #e2e8f0)',
                            borderRadius: 8, cursor: 'pointer', fontSize: '0.85rem', fontWeight: 600,
                            background: 'var(--color-bg-surface, #fff)',
                            color: 'var(--color-text, #1e293b)',
                            transition: 'all 0.2s'
                        }}
                    >
                        ← Voltar
                    </button>
                    <button
                        onClick={() => navigate('/dashboard')}
                        style={{
                            padding: '10px 20px', border: 'none',
                            borderRadius: 8, cursor: 'pointer', fontSize: '0.85rem', fontWeight: 600,
                            background: 'linear-gradient(135deg, #3b82f6, #2563eb)',
                            color: '#fff', transition: 'all 0.2s',
                            boxShadow: '0 2px 8px rgba(59,130,246,0.3)'
                        }}
                    >
                        Ir para o Dashboard
                    </button>
                </div>
            </div>
        </div>
    );
}
