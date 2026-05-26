import { useState, useEffect } from 'react';
import { api } from '../../lib/api';
import ContractProjectionSection from './ContractProjectionSection';

export default function FinanceContracts() {
    const [companies, setCompanies] = useState<any[]>([]);
    const [selectedCompanyId, setSelectedCompanyId] = useState<number | null>(null);

    useEffect(() => {
        api.lookups.getCompanies(false).then((data: any[]) => {
            setCompanies(data);
        }).catch((err: any) => console.error(err));
    }, []);

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '32px' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', marginBottom: '-16px' }}>
                <span style={{ fontSize: '11px', fontWeight: 800, color: '#94a3b8', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Filtrar por Entidade</span>
                <div style={{ display: 'flex', gap: '8px', overflowX: 'auto', paddingBottom: '4px' }}>
                    <button
                        onClick={() => setSelectedCompanyId(null)}
                        style={{
                            padding: '8px 16px',
                            backgroundColor: selectedCompanyId === null ? 'var(--color-primary)' : 'var(--color-bg-surface)',
                            color: selectedCompanyId === null ? '#fff' : 'var(--color-text-muted)',
                            border: selectedCompanyId === null ? '1px solid var(--color-primary)' : '1px solid var(--color-border)',
                            borderRadius: '8px',
                            fontWeight: 600,
                            fontSize: '14px',
                            cursor: 'pointer',
                            transition: 'all 0.2s',
                            boxShadow: selectedCompanyId === null ? '0 4px 6px rgba(0,0,0,0.1)' : '0 1px 2px rgba(0,0,0,0.05)',
                        }}
                    >
                        Consolidado Global
                    </button>
                    {companies.map(c => (
                        <button
                            key={c.id}
                            onClick={() => setSelectedCompanyId(c.id)}
                            style={{
                                padding: '8px 16px',
                                backgroundColor: selectedCompanyId === c.id ? 'var(--color-primary)' : 'var(--color-bg-surface)',
                                color: selectedCompanyId === c.id ? '#fff' : 'var(--color-text-muted)',
                                border: selectedCompanyId === c.id ? '1px solid var(--color-primary)' : '1px solid var(--color-border)',
                                borderRadius: '8px',
                                fontWeight: 600,
                                fontSize: '14px',
                                cursor: 'pointer',
                                transition: 'all 0.2s',
                                boxShadow: selectedCompanyId === c.id ? '0 4px 6px rgba(0,0,0,0.1)' : '0 1px 2px rgba(0,0,0,0.05)',
                            }}
                        >
                            {c.name}
                        </button>
                    ))}
                </div>
            </div>

            <ContractProjectionSection selectedCompanyId={selectedCompanyId} />
        </div>
    );
}
