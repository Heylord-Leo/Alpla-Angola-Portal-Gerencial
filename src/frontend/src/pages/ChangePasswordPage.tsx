import React, { useState } from 'react';
import { useAuth } from '../features/auth/AuthContext';
import { api } from '../lib/api';
import { Lock, Loader2, CheckCircle2, ShieldCheck, ArrowLeft } from 'lucide-react';
import { Feedback, FeedbackType } from '../components/ui/Feedback';
import { Link } from 'react-router-dom';

const ChangePasswordPage: React.FC = () => {
    const [currentPassword, setCurrentPassword] = useState('');
    const [newPassword, setNewPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'success', message: null });
    const [success, setSuccess] = useState(false);
    const [isLoading, setIsLoading] = useState(false);
    const { logout } = useAuth();

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setFeedback({ type: 'success', message: null });

        if (newPassword !== confirmPassword) {
            setFeedback({ type: 'error', message: 'As novas palavras-passe não coincidem.' });
            return;
        }

        if (newPassword.length < 8) {
            setFeedback({ type: 'error', message: 'A nova palavra-passe deve ter pelo menos 8 caracteres.' });
            return;
        }

        setIsLoading(true);

        try {
            await api.auth.changePassword({ currentPassword, newPassword });
            setSuccess(true);
            setFeedback({ type: 'success', message: 'Palavra-passe alterada com sucesso! Redirecionando...' });
            setTimeout(() => {
                logout(); // Logout after change to force re-login with new password
            }, 3000);
        } catch (err: any) {
            setFeedback({ type: 'error', message: err.message || 'Falha ao alterar palavra-passe. Verifique a senha atual.' });
        } finally {
            setIsLoading(false);
        }
    };

    if (success) {
        return (
            <div style={{ 
                display: 'flex', 
                alignItems: 'center', 
                justifyContent: 'center', 
                minHeight: 'calc(100vh - var(--header-height) - 100px)',
                padding: '2rem'
            }}>
                <div style={{ 
                    maxWidth: '480px', 
                    width: '100%', 
                    backgroundColor: 'var(--color-bg-surface)', 
                    border: '4px solid var(--color-primary)', 
                    boxShadow: 'var(--shadow-brutal)',
                    padding: '3rem',
                    textAlign: 'center'
                }}>
                    <div style={{ 
                        display: 'inline-flex', 
                        padding: '1.5rem', 
                        backgroundColor: 'var(--color-status-green)10', 
                        border: '2px solid var(--color-status-green)',
                        color: 'var(--color-status-green)',
                        marginBottom: '2rem'
                    }}>
                        <CheckCircle2 size={48} />
                    </div>
                    <h2 style={{ fontSize: '1.75rem', fontWeight: 900, color: 'var(--color-primary)', marginBottom: '1rem' }}>
                        Sucesso Absoluto
                    </h2>
                    <p style={{ color: 'var(--color-text-muted)', fontWeight: 600, lineHeight: 1.6 }}>
                        A sua palavra-passe foi atualizada no sistema. Por segurança, a sua sessão será terminada e deverá entrar novamente com as novas credenciais.
                    </p>
                    <div style={{ 
                        marginTop: '2rem', 
                        padding: '1rem', 
                        border: '2px dashed var(--color-border)', 
                        fontSize: '0.85rem', 
                        fontWeight: 800, 
                        color: 'var(--color-primary)',
                        textTransform: 'uppercase'
                    }}>
                        Redirecionando em 3 segundos...
                    </div>
                </div>
            </div>
        );
    }

    return (
        <div style={{ 
            display: 'flex', 
            flexDirection: 'column', 
            gap: '2rem',
            maxWidth: '600px',
            margin: '0 auto',
            padding: '2rem 1rem'
        }}>
            {/* Page Header */}
            <div>
                <Link to="/requests" style={{ 
                    display: 'flex', 
                    alignItems: 'center', 
                    gap: '8px', 
                    fontSize: '0.75rem', 
                    fontWeight: 800, 
                    color: 'var(--color-text-muted)', 
                    textTransform: 'uppercase',
                    marginBottom: '1rem'
                }}>
                    <ArrowLeft size={14} /> Voltar ao Início
                </Link>
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', borderBottom: '4px solid var(--color-primary)', paddingBottom: '1rem' }}>
                    <Lock size={32} color="var(--color-primary)" strokeWidth={2.5} />
                    <h1 style={{ margin: 0, fontSize: '2rem', color: 'var(--color-primary)' }}>Segurança da Conta</h1>
                </div>
                <p style={{ marginTop: '1rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>
                    Altere a sua palavra-passe de acesso ao portal. Utilize uma combinação forte de caracteres.
                </p>
            </div>

            {feedback.message && (
                <Feedback 
                    type={feedback.type} 
                    message={feedback.message} 
                    onClose={() => setFeedback(prev => ({ ...prev, message: null }))} 
                />
            )}

            <div style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                border: '4px solid var(--color-primary)', 
                boxShadow: 'var(--shadow-brutal)',
                padding: '2.5rem'
            }}>
                <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
                    
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', padding: '1rem', backgroundColor: 'var(--color-bg-page)', border: '2px solid var(--color-border)', marginBottom: '0.5rem' }}>
                        <ShieldCheck size={20} color="var(--color-primary)" />
                        <span style={{ fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-primary)' }}>Requisitos de Segurança Ativos</span>
                    </div>

                    <div>
                        <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '0.5rem' }}>
                            Palavra-passe Atual
                        </label>
                        <input
                            type="password"
                            required
                            placeholder="Insira a senha atual..."
                            style={{ width: '100%' }}
                            value={currentPassword}
                            onChange={(e) => setCurrentPassword(e.target.value)}
                        />
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1.5rem' }}>
                        <div>
                            <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '0.5rem' }}>
                                Nova Palavra-passe
                            </label>
                            <input
                                type="password"
                                required
                                placeholder="Mínimo 8 caracteres..."
                                style={{ width: '100%' }}
                                value={newPassword}
                                onChange={(e) => setNewPassword(e.target.value)}
                            />
                        </div>
                        <div>
                            <label style={{ display: 'block', fontSize: '0.75rem', fontWeight: 800, textTransform: 'uppercase', color: 'var(--color-text-muted)', marginBottom: '0.5rem' }}>
                                Confirmar Nova
                            </label>
                            <input
                                type="password"
                                required
                                placeholder="Repita a nova senha..."
                                style={{ width: '100%' }}
                                value={confirmPassword}
                                onChange={(e) => setConfirmPassword(e.target.value)}
                            />
                        </div>
                    </div>

                    <button
                        type="submit"
                        disabled={isLoading}
                        className="btn-primary"
                        style={{ 
                            marginTop: '1rem', 
                            display: 'flex', 
                            alignItems: 'center', 
                            justifyContent: 'center', 
                            gap: '12px',
                            height: '52px'
                        }}
                    >
                        {isLoading ? (
                            <Loader2 className="animate-spin" size={20} />
                        ) : (
                            <>
                                <Lock size={18} />
                                ATUALIZAR CREDENCIAIS
                            </>
                        )}
                    </button>
                </form>
            </div>

            <div style={{ 
                padding: '1.5rem', 
                backgroundColor: 'rgba(var(--color-primary-rgb), 0.03)', 
                border: '2px dashed var(--color-border)',
                fontSize: '0.8rem',
                color: 'var(--color-text-muted)',
                fontWeight: 600,
                lineHeight: 1.5
            }}>
                <strong>Dica de Segurança:</strong> Nunca partilhe a sua palavra-passe com terceiros. O suporte da ALPLA nunca solicitará a sua senha por e-mail ou telefone.
            </div>
        </div>
    );
};

export default ChangePasswordPage;
