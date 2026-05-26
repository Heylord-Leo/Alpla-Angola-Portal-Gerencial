import React, { useState, useEffect } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { api } from '../lib/api';
import { AlertCircle, Lock, Loader2, Eye, EyeOff, CheckCircle } from 'lucide-react';
import { APP_VERSION } from '../config';

const ResetPasswordPage: React.FC = () => {
    const [searchParams] = useSearchParams();
    const navigate = useNavigate();

    const [email, setEmail] = useState('');
    const [token, setToken] = useState('');
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(false);

    useEffect(() => {
        const queryEmail = searchParams.get('email');
        const queryToken = searchParams.get('token');

        if (queryEmail && queryToken) {
            setEmail(decodeURIComponent(queryEmail));
            setToken(queryToken);
        } else {
            setError("Os dados do link de recuperação são inválidos ou estão incompletos.");
        }
    }, [searchParams]);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        setSuccessMessage(null);

        if (!email || !token) {
            setError("Link inválido. Por favor certifique-se que copiou o link completo do seu e-mail.");
            return;
        }

        if (password.length < 8) {
            setError("A palavra-passe deve ter pelo menos 8 caracteres.");
            return;
        }

        if (password !== confirmPassword) {
            setError("As palavras-passe não coincidem.");
            return;
        }

        setIsLoading(true);

        try {
            const response = await api.auth.resetPassword({ email, token, newPassword: password });
            setSuccessMessage(response.message || 'Palavra-passe foi atualizada com sucesso.');
            setTimeout(() => {
                navigate('/login');
            }, 3000);
        } catch (err: any) {
            setError(err.message || 'Falha ao redefinir a palavra-passe.');
        } finally {
            setIsLoading(false);
        }
    };

    const s = {
        page: { minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center', backgroundColor: 'var(--color-bg-page)', padding: '24px' },
        card: { maxWidth: '440px', width: '100%', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-premium)', borderRadius: 'var(--radius-lg)', padding: '48px 40px', display: 'flex', flexDirection: 'column' as const, gap: '32px' },
        header: { textAlign: 'center' as const, display: 'flex', flexDirection: 'column' as const, gap: '16px' },
        title: { margin: 0, fontSize: '1.25rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase' as const, letterSpacing: '0.05em' },
        subtitle: { fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.15em', opacity: 0.7 },
        form: { display: 'flex', flexDirection: 'column' as const, gap: '24px' },
        fieldGroup: { display: 'flex', flexDirection: 'column' as const, gap: '8px' },
        label: { fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.05em' },
        inputWrapper: { position: 'relative' as const },
        iconLeft: { position: 'absolute' as const, left: '16px', top: '13px', color: 'var(--color-primary)', opacity: 0.5 },
        iconRight: { position: 'absolute' as const, right: '16px', top: '13px', background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)' },
        input: { width: '100%', padding: '12px 16px 12px 48px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)', fontSize: '0.95rem', fontWeight: 500, transition: 'all 0.2s', outline: 'none', backgroundColor: '#fcfcfc' },
        errorBox: { backgroundColor: 'rgba(var(--color-status-red-rgb), 0.05)', border: '1px solid var(--color-status-red)', padding: '14px 16px', display: 'flex', alignItems: 'flex-start', gap: '12px', color: 'var(--color-status-red)', fontSize: '0.85rem', fontWeight: 600, borderRadius: 'var(--radius-md)' },
        successBox: { backgroundColor: 'rgba(34, 197, 94, 0.05)', border: '1px solid #22c55e', padding: '14px 16px', display: 'flex', alignItems: 'flex-start', gap: '12px', color: '#16a34a', fontSize: '0.85rem', fontWeight: 600, borderRadius: 'var(--radius-md)' }
    };

    return (
        <div style={s.page}>
            <div style={s.card}>
                <div style={s.header}>
                    <img src="/login-animation-960w.gif" alt="ALPLA Logo" style={{ maxWidth: '180px', margin: '0 auto' }} />
                    <p style={s.subtitle}>Definir Nova Palavra-passe</p>
                </div>

                <form style={s.form} onSubmit={handleSubmit}>
                    {error && (
                        <div style={s.errorBox}>
                            <AlertCircle size={18} style={{ flexShrink: 0 }} />
                            <span>{error}</span>
                        </div>
                    )}

                    {successMessage && (
                        <div style={s.successBox}>
                            <CheckCircle size={18} style={{ flexShrink: 0 }} />
                            <span>{successMessage} Redirecionando para segurança...</span>
                        </div>
                    )}

                    <div style={s.fieldGroup}>
                        <label style={s.label}>Para a conta de</label>
                        <div style={s.inputWrapper}>
                            <input
                                type="text"
                                readOnly
                                disabled
                                style={{...s.input, backgroundColor: '#f3f4f6', cursor: 'not-allowed', color: '#6b7280', paddingLeft: '16px'}}
                                value={email}
                            />
                        </div>
                    </div>

                    <div style={s.fieldGroup}>
                        <label style={s.label}>Nova Palavra-passe</label>
                        <div style={s.inputWrapper}>
                            <Lock size={18} style={s.iconLeft} />
                            <input
                                type={showPassword ? "text" : "password"}
                                required
                                minLength={8}
                                style={s.input}
                                placeholder="Pelo menos 8 caracteres"
                                value={password}
                                onChange={(e) => setPassword(e.target.value)}
                            />
                            <button
                                type="button"
                                onClick={() => setShowPassword(!showPassword)}
                                style={s.iconRight}
                            >
                                {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                            </button>
                        </div>
                    </div>

                    <div style={s.fieldGroup}>
                        <label style={s.label}>Confirmar Nova Palavra-passe</label>
                        <div style={s.inputWrapper}>
                            <Lock size={18} style={s.iconLeft} />
                            <input
                                type={showPassword ? "text" : "password"}
                                required
                                minLength={8}
                                style={s.input}
                                placeholder="Confirmar Palavra-passe"
                                value={confirmPassword}
                                onChange={(e) => setConfirmPassword(e.target.value)}
                            />
                        </div>
                    </div>

                    <button
                        type="submit"
                        disabled={isLoading || successMessage !== null}
                        className="btn btn-primary"
                        style={{ 
                            width: '100%', 
                            padding: '16px', 
                            fontSize: '0.85rem', 
                            fontWeight: 900, 
                            textTransform: 'uppercase', 
                            letterSpacing: '0.08em', 
                            cursor: 'pointer',
                            borderRadius: 'var(--radius-md)',
                            boxShadow: 'var(--shadow-md)'
                        }}
                    >
                        {isLoading ? <Loader2 style={{ animation: 'spin 1s linear infinite' }} size={20} /> : "Finalizar Acesso Restrito"}
                    </button>
                    
                    <button 
                        type="button" 
                        onClick={() => navigate('/login')}
                        style={{ 
                            background: 'none', border: 'none', color: 'var(--color-primary)', 
                            fontSize: '0.75rem', fontWeight: 700, cursor: 'pointer', 
                            textDecoration: 'underline', width: '100%', textAlign: 'center' 
                        }}
                    >
                        Voltar ao ecrã de Login
                    </button>
                </form>

                <div style={{ textAlign: 'center', marginTop: '32px', opacity: 0.5, fontSize: '0.65rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
                    &copy; {new Date().getFullYear()} ALPLA.<br />
                    v{APP_VERSION}
                </div>
            </div>
        </div>
    );
};

export default ResetPasswordPage;
