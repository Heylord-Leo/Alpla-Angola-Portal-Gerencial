import React, { useState } from 'react';
import { useAuth } from '../features/auth/AuthContext';
import { api } from '../lib/api';
import { AlertCircle, Lock, Mail, Loader2, Eye, EyeOff } from 'lucide-react';

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
        card: { maxWidth: '440px', width: '100%', backgroundColor: 'var(--color-bg-surface)', border: '2px solid var(--color-primary)', boxShadow: 'var(--shadow-brutal)', padding: '48px 40px', display: 'flex', flexDirection: 'column' as const, gap: '32px' },
        header: { textAlign: 'center' as const, display: 'flex', flexDirection: 'column' as const, gap: '12px' },
        logo: { maxWidth: '240px', margin: '0 auto', marginBottom: '8px' },
        title: { margin: 0, fontSize: '1.25rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase' as const, letterSpacing: '0.05em' },
        subtitle: { fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.1em' },
        form: { display: 'flex', flexDirection: 'column' as const, gap: '24px' },
        fieldGroup: { display: 'flex', flexDirection: 'column' as const, gap: '8px' },
        label: { fontSize: '0.75rem', fontWeight: 800, color: 'var(--color-text-muted)', textTransform: 'uppercase' as const, letterSpacing: '0.05em' },
        inputWrapper: { position: 'relative' as const },
        iconLeft: { position: 'absolute' as const, left: '14px', top: '14px', color: 'var(--color-primary)', opacity: 0.6 },
        iconRight: { position: 'absolute' as const, right: '14px', top: '14px', background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)' },
        input: { width: '100%', padding: '12px 16px 12px 42px', border: '2px solid var(--color-border)', fontSize: '0.9rem', fontWeight: 600, transition: 'all 0.2s', outline: 'none' },
        inputFocus: { borderColor: 'var(--color-primary)', boxShadow: '4px 4px 0px var(--color-border-heavy)' },
        errorBox: { backgroundColor: 'var(--color-status-red)08', border: '2px solid var(--color-status-red)', padding: '12px 16px', display: 'flex', alignItems: 'flex-start', gap: '12px', color: 'var(--color-status-red)', fontSize: '0.85rem', fontWeight: 700, borderRadius: '4px' },
        footer: { textAlign: 'center' as const, pt: '16px' }
    };

    return (
        <div style={s.page}>
            <div style={s.card}>
                <div style={s.header}>
                    <img src="/logo-v2.gif" alt="ALPLA Logo" style={s.logo} />
                    <h2 style={s.title}>Portal Gerencial</h2>
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
                        style={{ width: '100%', padding: '14px', fontSize: '1rem', marginTop: '8px' }}
                    >
                        {isLoading ? (
                            <Loader2 className="animate-spin" />
                        ) : (
                            "Iniciar Sessão"
                        )}
                    </button>
                </form>

                <div style={{ textAlign: 'center', opacity: 0.6, fontSize: '0.65rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.1em' }}>
                    &copy; {new Date().getFullYear()} ALPLA Angola. Todos os direitos reservados.
                </div>
            </div>
        </div>
    );
};

export default LoginPage;
