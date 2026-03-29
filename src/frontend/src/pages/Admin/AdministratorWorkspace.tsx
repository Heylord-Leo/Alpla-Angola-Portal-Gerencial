import React from 'react';
import { Shield, FileText, Activity, Network, ChevronRight, Users } from 'lucide-react';
import { Link } from 'react-router-dom';

interface AdminTileProps {
    to: string;
    icon: React.ReactNode;
    title: string;
    description: string;
    color: string;
}

function AdminTile({ to, icon, title, description, color }: AdminTileProps) {
    return (
        <Link 
            to={to} 
            style={{ 
                display: 'flex', 
                flexDirection: 'column', 
                padding: '2rem', 
                backgroundColor: 'var(--color-bg-surface)', 
                border: '2px solid var(--color-border-heavy)',
                boxShadow: 'var(--shadow-brutal)',
                transition: 'all 0.2s ease',
                cursor: 'pointer',
                textDecoration: 'none',
                color: 'inherit'
            }}
            className="admin-tile-hover"
        >
            <div style={{ 
                backgroundColor: color, 
                width: '48px', 
                height: '48px', 
                display: 'flex', 
                alignItems: 'center', 
                justifyContent: 'center', 
                color: 'white',
                marginBottom: '1.5rem',
                border: `2px solid ${color}`
            }}>
                {icon}
            </div>
            <h3 style={{ margin: '0 0 0.5rem 0', fontSize: '1.25rem', fontWeight: 800, textTransform: 'uppercase' }}>
                {title}
            </h3>
            <p style={{ margin: '0 0 1.5rem 0', color: 'var(--color-text-muted)', fontSize: '0.925rem', lineHeight: 1.5, flex: 1 }}>
                {description}
            </p>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontWeight: 700, fontSize: '0.875rem', textTransform: 'uppercase', color: 'var(--color-primary)' }}>
                Aceder <ChevronRight size={16} />
            </div>
        </Link>
    );
}

export function AdministratorWorkspace() {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2rem' }}>
            <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', borderBottom: '4px solid var(--color-primary)', paddingBottom: '16px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '0.5rem' }}>
                    <div style={{ 
                        backgroundColor: 'var(--color-primary)', 
                        padding: '0.5rem', 
                        display: 'flex', 
                        border: '2px solid var(--color-primary)',
                        color: 'white'
                    }}>
                        <Shield size={24} />
                    </div>
                    <div>
                        <h1 style={{ margin: 0, fontSize: '2.5rem', fontWeight: 900, color: 'var(--color-primary)', textTransform: 'uppercase', letterSpacing: '-0.02em' }}>
                            Workspace do Administrador
                        </h1>
                        <p style={{ margin: '8px 0 0', color: 'var(--color-text-muted)', fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase', fontSize: '0.875rem' }}>
                            Gestão técnica, diagnósticos e saúde do ecossistema Portal Gerencial
                        </p>
                    </div>
                </div>
            </header>

            <div style={{ 
                display: 'grid', 
                gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', 
                gap: '1.5rem' 
            }}>
                <AdminTile 
                    to="/admin/users"
                    icon={<Users size={24} />}
                    title="Gestão de Utilizadores"
                    description="Administre e proteja o acesso ao portal gerindo utilizadores, funções e escopos de planta/departamento."
                    color="var(--color-status-green)"
                />
                <AdminTile 
                    to="/admin/logs"
                    icon={<FileText size={24} />}
                    title="Logs do Sistema"
                    description="Visualize eventos, erros e atividades de execução do backend para troubleshooting."
                    color="var(--color-primary)"
                />
                <AdminTile 
                    to="/admin/diagnosis"
                    icon={<Activity size={24} />}
                    title="Diagnóstico de Serviços"
                    description="Verifique o estado de saúde e latência dos serviços internos e dependências core."
                    color="var(--color-status-purple)"
                />
                <AdminTile 
                    to="/admin/health"
                    icon={<Network size={24} />}
                    title="Saúde das Integrações"
                    description="Monitorize a integridade das comunicações com AlplaPROD e Primavera ERP."
                    color="var(--color-status-blue)"
                />
            </div>
            
            <style>{`
                .admin-tile-hover:hover {
                    transform: translateY(-4px);
                    box-shadow: var(--shadow-brutal-hover) !important;
                }
            `}</style>
        </div>
    );
}
