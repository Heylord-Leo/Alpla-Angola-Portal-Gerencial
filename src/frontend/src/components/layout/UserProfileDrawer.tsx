import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { X, Shield, MapPin, Building2, Mail, ExternalLink, Key, CheckCircle, Info } from 'lucide-react';
import { api } from '../../lib/api';
import { DropdownPortal } from '../ui/DropdownPortal';
import { Z_INDEX } from '../../constants/ui';

interface UserProfileDrawerProps {
    isOpen: boolean;
    onClose: () => void;
}

export function UserProfileDrawer({ isOpen, onClose }: UserProfileDrawerProps) {
    const [profile, setProfile] = useState<any>(null);
    const [plants, setPlants] = useState<any[]>([]);
    const [departments, setDepartments] = useState<any[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (isOpen) {
            loadProfile();
        }
    }, [isOpen]);

    const loadProfile = async () => {
        setLoading(true);
        setError(null);
        try {
            const [profileData, plantsData, departmentsData] = await Promise.all([
                api.users.me(),
                api.lookups.getPlants(undefined, true),
                api.lookups.getDepartments(true)
            ]);
            setProfile(profileData);
            setPlants(plantsData);
            setDepartments(departmentsData);
        } catch (err: any) {
            setError(err.message || 'Erro ao carregar perfil');
        } finally {
            setLoading(false);
        }
    };

    const getInitials = (name: string) => {
        return name
            .split(' ')
            .map(n => n[0])
            .slice(0, 2)
            .join('')
            .toUpperCase();
    };

    return (
        <DropdownPortal>
            <AnimatePresence>
                {isOpen && (
                    <>
                        {/* Backdrop */}
                        <motion.div
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            exit={{ opacity: 0 }}
                            onClick={onClose}
                            style={{
                                position: 'fixed',
                                inset: 0,
                                backgroundColor: 'rgba(0, 0, 0, 0.4)',
                                backdropFilter: 'blur(4px)',
                                zIndex: Z_INDEX.DRAWER as any,
                                cursor: 'pointer'
                            }}
                        />

                        {/* Drawer */}
                        <motion.div
                            initial={{ x: '100%' }}
                            animate={{ x: 0 }}
                            exit={{ x: '100%' }}
                            transition={{ type: 'spring', damping: 25, stiffness: 200 }}
                            style={{
                                position: 'fixed',
                                top: 0,
                                right: 0,
                                bottom: 0,
                                width: '100%',
                                maxWidth: '480px',
                                backgroundColor: 'var(--color-bg-page)',
                                borderLeft: '4px solid var(--color-primary)',
                                boxShadow: '-8px 0 32px rgba(0, 0, 0, 0.15)',
                                zIndex: `calc(${Z_INDEX.DRAWER} + 1)` as any,
                                display: 'flex',
                                flexDirection: 'column'
                            }}
                        >
                            {/* Header Area */}
                            <div style={{
                                padding: '32px',
                                backgroundColor: 'var(--color-primary)',
                                color: 'white',
                                position: 'relative'
                            }}>
                                <button
                                    onClick={onClose}
                                    style={{
                                        position: 'absolute',
                                        top: '20px',
                                        right: '20px',
                                        background: 'rgba(255, 255, 255, 0.1)',
                                        border: 'none',
                                        color: 'white',
                                        width: '32px',
                                        height: '32px',
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center',
                                        cursor: 'pointer',
                                        transition: 'all 0.2s',
                                        borderRadius: '2px'
                                    }}

                                >
                                    <X size={20} />
                                </button>

                                <div style={{ display: 'flex', alignItems: 'center', gap: '24px' }}>
                                    <div style={{
                                        width: '80px',
                                        height: '80px',
                                        backgroundColor: 'var(--color-bg-surface)',
                                        color: 'var(--color-primary)',
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center',
                                        fontSize: '2rem',
                                        fontWeight: 900,
                                        border: '4px solid var(--color-accent)',
                                        boxShadow: 'var(--shadow-md)'
                                    }}>
                                        {profile ? getInitials(profile.fullName) : '--'}
                                    </div>
                                    <div>
                                        <h2 style={{
                                            margin: 0,
                                            fontSize: '1.5rem',
                                            fontWeight: 900,
                                            textTransform: 'uppercase',
                                            letterSpacing: '-0.02em',
                                            lineHeight: 1
                                        }}>
                                            {profile?.fullName || 'Carregando...'}
                                        </h2>
                                        <div style={{
                                            marginTop: '12px',
                                            display: 'flex',
                                            flexWrap: 'wrap',
                                            gap: '6px'
                                        }}>
                                            <div style={{
                                                fontSize: '0.65rem',
                                                fontWeight: 800,
                                                backgroundColor: profile?.isActive ? 'rgba(76, 175, 80, 0.2)' : 'rgba(244, 67, 54, 0.2)',
                                                color: 'white',
                                                padding: '2px 8px',
                                                border: `1px solid ${profile?.isActive ? 'rgba(76, 175, 80, 0.5)' : 'rgba(244, 67, 54, 0.5)'}`,
                                                textTransform: 'uppercase',
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: '4px'
                                            }}>
                                                {profile?.isActive ? <CheckCircle size={10} /> : <X size={10} />}
                                                {profile?.isActive ? 'CONTA ATIVA' : 'CONTA INATIVA'}
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>

                            {/* Content Area */}
                            <div style={{
                                flex: 1,
                                overflowY: 'auto',
                                padding: '32px',
                                display: 'flex',
                                flexDirection: 'column',
                                gap: '32px'
                            }}>
                                {loading ? (
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                        {[1, 2, 3].map(i => (
                                            <div key={i} style={{ height: '80px', backgroundColor: 'var(--color-bg-surface)', opacity: 0.5, animation: 'pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite' }} />
                                        ))}
                                    </div>
                                ) : error ? (
                                    <div style={{
                                        padding: '16px',
                                        backgroundColor: 'var(--color-status-red)10',
                                        border: '2px solid var(--color-status-red)',
                                        color: 'var(--color-status-red)',
                                        fontWeight: 700,
                                        fontSize: '0.85rem'
                                    }}>
                                        {error}
                                    </div>
                                ) : profile && (
                                    <>
                                        {/* Account Info */}
                                        <section>
                                            <h3 style={{
                                                fontSize: '0.75rem',
                                                fontWeight: 800,
                                                color: 'var(--color-text-muted)',
                                                textTransform: 'uppercase',
                                                letterSpacing: '0.1em',
                                                marginBottom: '16px',
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: '8px'
                                            }}>
                                                <Mail size={14} /> Informações da Conta
                                            </h3>
                                            <div style={{
                                                backgroundColor: 'var(--color-bg-surface)',
                                                padding: '16px',
                                                border: '2px solid var(--color-border)',
                                                boxShadow: '4px 4px 0 var(--color-border)',
                                                display: 'flex',
                                                flexDirection: 'column',
                                                gap: '12px'
                                            }}>
                                                <div>
                                                    <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 800, marginBottom: '2px' }}>Nome Completo</div>
                                                    <div style={{ fontSize: '0.9rem', fontWeight: 800, color: 'var(--color-text-main)' }}>{profile.fullName}</div>
                                                </div>
                                                <div>
                                                    <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 800, marginBottom: '2px' }}>E-mail Corporativo</div>
                                                    <div style={{ fontSize: '0.9rem', fontWeight: 800, color: 'var(--color-primary)' }}>{profile.email}</div>
                                                </div>
                                            </div>
                                        </section>

                                        {/* Roles Section */}
                                        <section>
                                            <h3 style={{
                                                fontSize: '0.75rem',
                                                fontWeight: 800,
                                                color: 'var(--color-text-muted)',
                                                textTransform: 'uppercase',
                                                letterSpacing: '0.1em',
                                                marginBottom: '16px',
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: '8px'
                                            }}>
                                                <Shield size={14} /> Funções e Permissões
                                            </h3>
                                            <div style={{
                                                backgroundColor: 'var(--color-bg-surface)',
                                                padding: '16px',
                                                border: '2px solid var(--color-border)',
                                                display: 'flex',
                                                flexWrap: 'wrap',
                                                gap: '8px'
                                            }}>
                                                {profile.roles && profile.roles.length > 0 ? (
                                                    profile.roles.map((role: string) => (
                                                        <span 
                                                            key={role}
                                                            style={{
                                                                fontSize: '0.7rem',
                                                                fontWeight: 800,
                                                                backgroundColor: 'var(--color-bg-page)',
                                                                color: 'var(--color-primary)',
                                                                padding: '4px 12px',
                                                                border: '2px solid var(--color-border)',
                                                                textTransform: 'uppercase',
                                                                letterSpacing: '0.02em'
                                                            }}
                                                        >
                                                            {role}
                                                        </span>
                                                    ))
                                                ) : (
                                                    <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontStyle: 'italic', display: 'flex', alignItems: 'center', gap: '8px', padding: '4px 0' }}>
                                                        <Info size={14} /> Nenhuma função atribuída.
                                                    </div>
                                                )}
                                            </div>
                                        </section>

                                        {/* Organization Info */}
                                        <section>
                                            <h3 style={{
                                                fontSize: '0.75rem',
                                                fontWeight: 800,
                                                color: 'var(--color-text-muted)',
                                                textTransform: 'uppercase',
                                                letterSpacing: '0.1em',
                                                marginBottom: '16px',
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: '8px'
                                            }}>
                                                <Building2 size={14} /> Escopo de Acesso
                                            </h3>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                                                {/* Plants */}
                                                <div style={{
                                                    backgroundColor: 'var(--color-bg-surface)',
                                                    padding: '16px',
                                                    border: '2px solid var(--color-border)',
                                                    display: 'flex',
                                                    flexDirection: 'column',
                                                    gap: '12px'
                                                }}>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                                                        <div style={{ color: 'var(--color-primary)' }}><MapPin size={24} /></div>
                                                        <div style={{ fontSize: '0.8rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Plantas Atribuídas</div>
                                                    </div>
                                                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                                                        {profile.plants && profile.plants.length > 0 ? (
                                                            profile.plants.map((code: string) => {
                                                                const plantName = plants.find(p => p.code === code)?.name || code;
                                                                return (
                                                                    <span 
                                                                        key={code}
                                                                        style={{
                                                                            fontSize: '0.7rem',
                                                                            fontWeight: 800,
                                                                            backgroundColor: 'white',
                                                                            color: 'var(--color-text-main)',
                                                                            padding: '4px 12px',
                                                                            border: '2px solid var(--color-border)',
                                                                            textTransform: 'uppercase',
                                                                            boxShadow: '2px 2px 0 var(--color-border)'
                                                                        }}
                                                                        title={code}
                                                                    >
                                                                        {plantName}
                                                                    </span>
                                                                );
                                                            })
                                                        ) : (
                                                            <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontStyle: 'italic', display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                                <Info size={14} /> Nenhuma planta atribuída.
                                                            </div>
                                                        )}
                                                    </div>
                                                </div>

                                                {/* Departments */}
                                                <div style={{
                                                    backgroundColor: 'var(--color-bg-surface)',
                                                    padding: '16px',
                                                    border: '2px solid var(--color-border)',
                                                    display: 'flex',
                                                    flexDirection: 'column',
                                                    gap: '12px'
                                                }}>
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                                                        <div style={{ color: 'var(--color-primary)' }}><Building2 size={24} /></div>
                                                        <div style={{ fontSize: '0.8rem', fontWeight: 800, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Departamentos</div>
                                                    </div>
                                                    <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                                                        {profile.departments && profile.departments.length > 0 ? (
                                                            profile.departments.map((code: string) => {
                                                                const deptName = departments.find(d => d.code === code)?.name || code;
                                                                return (
                                                                    <span 
                                                                        key={code}
                                                                        style={{
                                                                            fontSize: '0.7rem',
                                                                            fontWeight: 800,
                                                                            backgroundColor: 'white',
                                                                            color: 'var(--color-text-main)',
                                                                            padding: '4px 12px',
                                                                            border: '2px solid var(--color-border)',
                                                                            textTransform: 'uppercase',
                                                                            boxShadow: '2px 2px 0 var(--color-border)'
                                                                        }}
                                                                        title={code}
                                                                    >
                                                                        {deptName}
                                                                    </span>
                                                                );
                                                            })
                                                        ) : (
                                                            <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', fontStyle: 'italic', display: 'flex', alignItems: 'center', gap: '8px' }}>
                                                                <Info size={14} /> Nenhum departamento atribuído.
                                                            </div>
                                                        )}
                                                    </div>
                                                </div>
                                            </div>
                                        </section>

                                        {/* Account Actions */}
                                        <section>
                                            <h3 style={{
                                                fontSize: '0.75rem',
                                                fontWeight: 800,
                                                color: 'var(--color-text-muted)',
                                                textTransform: 'uppercase',
                                                letterSpacing: '0.1em',
                                                marginBottom: '16px',
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: '8px'
                                            }}>
                                                <ExternalLink size={14} /> Ações da Conta
                                            </h3>
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                                <button 
                                                    onClick={() => window.location.href = '/change-password'}
                                                    style={{
                                                        display: 'flex',
                                                        alignItems: 'center',
                                                        gap: '12px',
                                                        width: '100%',
                                                        padding: '12px 16px',
                                                        backgroundColor: 'white',
                                                        border: '2px solid var(--color-primary)',
                                                        color: 'var(--color-primary)',
                                                        fontWeight: 800,
                                                        fontSize: '0.85rem',
                                                        cursor: 'pointer',
                                                        transition: 'all 0.1s',
                                                        textAlign: 'left'
                                                    }}
                                                    onMouseOver={(e) => ((e.currentTarget as HTMLElement).style.transform = 'translateX(4px)')}
                                                    onMouseOut={(e) => ((e.currentTarget as HTMLElement).style.transform = '')}
                                                >
                                                    <Key size={18} /> Alterar Palavra-passe
                                                </button>

                                                <a 
                                                    href="mailto:suporte@alpla.com"
                                                    style={{
                                                        display: 'flex',
                                                        alignItems: 'center',
                                                        gap: '12px',
                                                        width: '100%',
                                                        padding: '12px 16px',
                                                        backgroundColor: 'var(--color-bg-page)',
                                                        border: '2px solid var(--color-border)',
                                                        color: 'var(--color-text-main)',
                                                        fontWeight: 800,
                                                        fontSize: '0.85rem',
                                                        cursor: 'pointer',
                                                        transition: 'all 0.1s',
                                                        textDecoration: 'none'
                                                    }}
                                                    onMouseOver={(e) => ((e.currentTarget as HTMLElement).style.transform = 'translateX(4px)')}
                                                    onMouseOut={(e) => ((e.currentTarget as HTMLElement).style.transform = '')}
                                                >
                                                    <Mail size={18} /> Contactar Suporte TI
                                                </a>
                                            </div>
                                        </section>
                                    </>
                                )}
                            </div>

                            {/* Footer Area */}
                            <div style={{
                                padding: '24px 32px',
                                backgroundColor: 'white',
                                borderTop: '2px solid var(--color-border)',
                                display: 'flex',
                                justifyContent: 'flex-end'
                            }}>
                                <button 
                                    onClick={onClose}
                                    style={{
                                        padding: '10px 24px',
                                        backgroundColor: 'var(--color-primary)',
                                        color: 'white',
                                        border: 'none',
                                        fontWeight: 800,
                                        textTransform: 'uppercase',
                                        fontSize: '0.8rem',
                                        cursor: 'pointer',
                                        boxShadow: 'var(--shadow-md)'
                                    }}
                                    onMouseOver={(e) => (e.currentTarget.style.opacity = '0.88')}
                                    onMouseOut={(e) => (e.currentTarget.style.opacity = '1')}
                                >
                                    Fechar Perfil
                                </button>
                            </div>
                        </motion.div>
                    </>
                )}
            </AnimatePresence>
        </DropdownPortal>
    );
}
