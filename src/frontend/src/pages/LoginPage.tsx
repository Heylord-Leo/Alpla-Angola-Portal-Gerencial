import React, { useState } from 'react';
import { useAuth } from '../features/auth/AuthContext';
import { api } from '../lib/api';
import { AlertCircle, Lock, Mail, Loader2, Eye, EyeOff } from 'lucide-react';
import { APP_VERSION } from '../config';

const LoginPage: React.FC = () => {
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(false);
    const { login } = useAuth();

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        setIsLoading(true);

        try {
            const response = await api.auth.login({ email, password });
            login(response);
        } catch (err: any) {
            setError(err.message || 'Falha ao autenticar.');
        } finally {
            setIsLoading(false);
        }
    };

    const s = {
        page: { minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center', backgroundColor: 'var(--color-bg-page)', padding: '24px' },
        card: { maxWidth: '440px', width: '100%', backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', boxShadow: 'var(--shadow-premium)', borderRadius: 'var(--radius-lg)', padding: '48px 40px', display: 'flex', flexDirection: 'column' as const, gap: '32px' },
        header: { textAlign: 'center' as const, display: 'flex', flexDirection: 'column' as const, gap: '16px' },
        logo: { maxWidth: '180px', margin: '0 auto' },
        title: { margin: 0, fontSize: '1.25rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase' as const, letterSpacing: '0.05em' },
        subtitle: { fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.15em', opacity: 0.7 },
        form: { display: 'flex', flexDirection: 'column' as const, gap: '24px' },
        fieldGroup: { display: 'flex', flexDirection: 'column' as const, gap: '8px' },
        label: { fontSize: '0.7rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.05em' },
        inputWrapper: { position: 'relative' as const },
        iconLeft: { position: 'absolute' as const, left: '16px', top: '13px', color: 'var(--color-primary)', opacity: 0.5 },
        iconRight: { position: 'absolute' as const, right: '16px', top: '13px', background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)' },
        input: { width: '100%', padding: '12px 16px 12px 48px', border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)', fontSize: '0.95rem', fontWeight: 500, transition: 'all 0.2s', outline: 'none', backgroundColor: '#fcfcfc' },
        inputFocus: { borderColor: 'var(--color-primary)', boxShadow: '0 0 0 4px rgba(var(--color-primary-rgb), 0.1)', backgroundColor: 'var(--color-bg-surface)' },
        errorBox: { backgroundColor: 'rgba(var(--color-status-red-rgb), 0.05)', border: '1px solid var(--color-status-red)', padding: '14px 16px', display: 'flex', alignItems: 'flex-start', gap: '12px', color: 'var(--color-status-red)', fontSize: '0.85rem', fontWeight: 600, borderRadius: 'var(--radius-md)' },
        footer: { textAlign: 'center' as const, pt: '16px' }
    };

    return (
        <div style={s.page}>
            <div style={s.card}>
                <div style={s.header}>
                    <img src="/login-animation-960w.gif" alt="ALPLA Logo" style={s.logo} />
                    <p style={s.subtitle}>Acesso Corporativo</p>
                </div>

                <form style={s.form} onSubmit={handleSubmit}>
                    {error && (
                        <div style={s.errorBox}>
                            <AlertCircle size={18} style={{ flexShrink: 0 }} />
                            <span>{error}</span>
                        </div>
                    )}

                    <div style={s.fieldGroup}>
                        <label style={s.label}>E-mail Corporativo</label>
                        <div style={s.inputWrapper}>
                            <Mail size={18} style={s.iconLeft} />
                            <input
                                type="email"
                                required
                                style={s.input}
                                placeholder="exemplo@alpla.com"
                                value={email}
                                onChange={(e) => setEmail(e.target.value)}
                            />
                        </div>
                    </div>

                    <div style={s.fieldGroup}>
                        <label style={s.label}>Palavra-passe</label>
                        <div style={s.inputWrapper}>
                            <Lock size={18} style={s.iconLeft} />
                            <input
                                type={showPassword ? "text" : "password"}
                                required
                                style={s.input}
                                placeholder="••••••••"
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

                    <button
                        type="submit"
                        disabled={isLoading}
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
                        {isLoading ? <Loader2 className="animate-spin" size={20} /> : "Entrar no Portal"}
                    </button>
                </form>

                <div style={{ textAlign: 'center', marginTop: '32px', opacity: 0.5, fontSize: '0.65rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
                    &copy; {new Date().getFullYear()} ALPLA. Todos os direitos reservados.
                    <div style={{ marginTop: '8px' }}>v{APP_VERSION}</div>
                </div>
            </div>
        </div>
    );
};

export default LoginPage;
