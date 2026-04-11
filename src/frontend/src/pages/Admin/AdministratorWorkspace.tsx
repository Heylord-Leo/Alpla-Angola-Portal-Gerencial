import React from 'react';
import { Shield, FileText, Activity, Network, ChevronRight, Users } from 'lucide-react';
import { Link } from 'react-router-dom';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';

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
                border: '1px solid var(--color-border)',
                borderRadius: 'var(--radius-lg)',
                boxShadow: '0 4px 6px rgba(0,0,0,0.05)',
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
        <PageContainer>
            <PageHeader 
                title="Workspace do Administrador"
                subtitle="Gestão técnica, diagnósticos e saúde do ecossistema Portal Gerencial"
            />

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
                    transform: translateY(-2px);
                    box-shadow: 0 8px 16px rgba(0,0,0,0.1) !important;
                }
            `}</style>
        </PageContainer>
    );
}
