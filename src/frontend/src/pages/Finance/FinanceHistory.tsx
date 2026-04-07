import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { FinanceHistoryItemDto, PagedResult } from '../../types';

export default function FinanceHistory() {
    const [data, setData] = useState<PagedResult<FinanceHistoryItemDto> | null>(null);

    useEffect(() => {
        api.finance.getHistory().then(res => setData(res));
    }, []);

    if (!data) return <div style={{ padding: '60px', textAlign: 'center', fontWeight: 'bold' }}>Carregando Histórico...</div>;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <h2 style={{ margin: 0, fontSize: '1.8rem', fontWeight: 900, textTransform: 'uppercase' }}>Auditoria & Histórico</h2>
            </div>
            
            <div style={{ 
                backgroundColor: 'var(--color-bg-surface)', 
                border: '2px solid var(--color-border)', 
                boxShadow: 'var(--shadow-brutal)',
                overflowX: 'auto'
            }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', textAlign: 'left' }}>
                    <thead style={{ backgroundColor: '#1e293b', color: '#fff' }}>
                        <tr>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Data/Hora</th>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Ação Executada</th>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Responsável</th>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Detalhes / Comentários</th>
                            <th style={{ padding: '16px', fontWeight: 900, textTransform: 'uppercase' }}>Pedido Ref.</th>
                        </tr>
                    </thead>
                    <tbody>
                        {data.items.map((item, i) => (
                            <tr key={item.id} style={{ borderBottom: '1px solid var(--color-border)', backgroundColor: i % 2 === 0 ? '#fff' : '#f8fafc' }}>
                                <td style={{ padding: '16px', fontWeight: 700, color: '#475569', fontSize: '0.9rem' }}>
                                    {new Date(item.createdAtUtc).toLocaleString('pt-AO')}
                                </td>
                                <td style={{ padding: '16px', fontWeight: 800, color: 'var(--color-primary)' }}>
                                    {item.actionTaken === 'PAYMENT_SCHEDULED' ? 'Pagamento Agendado' :
                                     item.actionTaken === 'PAYMENT_COMPLETED' ? 'Pagamento Realizado' :
                                     item.actionTaken === 'DOCUMENTO ADICIONADO' ? 'Comprovativo Anexado' :
                                     item.actionTaken === 'NOTA_FINANCEIRA' ? 'Nota Adicionada' :
                                     item.actionTaken === 'FINANCE_RETURN_ADJUSTMENT' ? 'Devolvido para Ajuste' : item.actionTaken}
                                </td>
                                <td style={{ padding: '16px', fontWeight: 600 }}>
                                    {item.actorName}
                                </td>
                                <td style={{ padding: '16px', fontSize: '0.9rem' }}>
                                    {item.comment || <span style={{ color: '#94a3b8', fontStyle: 'italic' }}>Sem detalhes adicionais</span>}
                                </td>
                                <td style={{ padding: '16px' }}>
                                    <a href={`/requests/${item.requestId}`} target="_blank" rel="noreferrer" style={{ fontWeight: 800, color: 'var(--color-primary)', textDecoration: 'underline' }}>
                                        Acessar Pedido
                                    </a>
                                </td>
                            </tr>
                        ))}
                        {data.items.length === 0 && (
                            <tr>
                                <td colSpan={5} style={{ padding: '40px', textAlign: 'center', fontWeight: 600, color: '#64748b' }}>
                                    Nenhuma movimentação financeira registrada até o momento.
                                </td>
                            </tr>
                        )}
                    </tbody>
                </table>
            </div>
        </div>
    );
}
