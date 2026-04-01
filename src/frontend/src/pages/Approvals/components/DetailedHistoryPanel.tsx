import React, { useEffect, useState } from 'react';
import { api } from '../../../lib/api';
import { ItemIntelligenceDto, HistoricalPurchaseRecordDto } from '../../../types';
import { formatCurrencyAO, formatDate } from '../../../lib/utils';
import { ArrowLeft, Info, Package, TrendingDown, TrendingUp } from 'lucide-react';

interface DetailedHistoryPanelProps {
    item: ItemIntelligenceDto;
    requestId: string;
    onClose: () => void;
}

export const DetailedHistoryPanel: React.FC<DetailedHistoryPanelProps> = ({ item, requestId, onClose }) => {
    const [history, setHistory] = useState<HistoricalPurchaseRecordDto[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const loadHistory = async () => {
            try {
                setIsLoading(true);
                const data = await api.approvals.getItemHistory(requestId, item.lineItemId);
                setHistory(data);
            } catch (err) {
                console.error('Failed to load item history:', err);
                setError('Falha ao carregar histórico detalhado.');
            } finally {
                setIsLoading(false);
            }
        };

        loadHistory();
    }, [requestId, item.lineItemId]);

    return (
        <div 
            className="flex-1 bg-[var(--color-bg-page)] z-[60] flex flex-col animate-in slide-in-from-right duration-300 relative"
            style={{ height: '100%', width: '100%' }}
        >
            {/* Header */}
            <div style={{ 
                padding: '20px 24px', 
                borderBottom: '4px solid black', 
                display: 'flex', 
                alignItems: 'center', 
                gap: '20px', 
                backgroundColor: 'white' 
            }}>
                <button 
                    onClick={onClose}
                    style={{
                        padding: '10px 20px',
                        backgroundColor: 'white',
                        border: '2px solid black',
                        fontWeight: 900,
                        fontSize: '0.75rem',
                        textTransform: 'uppercase',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        cursor: 'pointer',
                        boxShadow: '4px 4px 0px rgba(0,0,0,1)',
                        transition: 'all 0.1s'
                    }}
                    className="hover:translate-x-[1px] hover:translate-y-[1px] hover:shadow-none"
                >
                    <ArrowLeft size={16} strokeWidth={3} /> Voltar
                </button>
                <div>
                    <div style={{ 
                        fontSize: '0.65rem', 
                        fontWeight: 950, 
                        textTransform: 'uppercase', 
                        letterSpacing: '0.15em', 
                        color: 'black',
                        display: 'flex',
                        alignItems: 'center',
                        gap: '8px',
                        marginBottom: '4px'
                    }}>
                        <div style={{ width: '4px', height: '10px', backgroundColor: 'var(--color-primary)' }} />
                        Inteligência de Compra
                    </div>
                    <h2 style={{ fontSize: '1.25rem', fontWeight: 900, margin: 0, textTransform: 'uppercase', letterSpacing: '-0.02em', color: 'black' }}>
                        {item.description}
                    </h2>
                </div>
            </div>

            <div style={{ flex: 1, overflowY: 'auto', padding: '32px 24px', display: 'flex', flexDirection: 'column', gap: '32px' }}>
                
                {/* Compact Summary Metrics */}
                <section>
                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '16px' }}>
                        <MetricCard 
                            label="Preço Unit. Atual" 
                            value={formatCurrencyAO(item.currentUnitPrice, item.currency)} 
                        />
                        <MetricCard 
                            label="Média Histórica" 
                            value={item.averageHistoricalPrice ? formatCurrencyAO(item.averageHistoricalPrice, item.currency) : '---'} 
                        />
                        {item.variationVsAvgPercentage !== undefined && (
                            <div style={{ 
                                padding: '20px', 
                                border: '2px solid black', 
                                backgroundColor: item.variationVsAvgPercentage > 0 ? '#fef2f2' : '#f0fdf4',
                                display: 'flex',
                                flexDirection: 'column',
                                gap: '4px',
                                boxShadow: 'var(--shadow-brutal)'
                            }}>
                                <span style={{ fontSize: '0.65rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.1em' }}>Variação</span>
                                <div style={{ 
                                    fontSize: '1.5rem', 
                                    fontWeight: 950, 
                                    color: item.variationVsAvgPercentage > 0 ? 'var(--color-status-red)' : '#16a34a',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '8px'
                                }}>
                                    {item.variationVsAvgPercentage > 0 ? <TrendingUp size={24} /> : <TrendingDown size={24} />}
                                    {item.variationVsAvgPercentage > 0 ? '+' : ''}{item.variationVsAvgPercentage.toFixed(1)}%
                                </div>
                            </div>
                        )}
                    </div>
                </section>

                {/* Match Insight Block */}
                <div style={{ 
                    padding: '20px', 
                    backgroundColor: '#eff6ff', 
                    border: '2px solid #2563eb', 
                    display: 'flex', 
                    gap: '20px',
                    boxShadow: 'var(--shadow-brutal)'
                }}>
                    <div style={{ 
                        width: '40px', 
                        height: '40px', 
                        backgroundColor: '#2563eb', 
                        color: 'white', 
                        display: 'flex', 
                        alignItems: 'center', 
                        justifyContent: 'center',
                        flexShrink: 0,
                        border: '2px solid black',
                        boxShadow: '2px 2px 0px black'
                    }}>
                        <Info size={24} />
                    </div>
                    <div>
                        <span style={{ fontSize: '0.7rem', fontWeight: 950, textTransform: 'uppercase', color: '#2563eb', letterSpacing: '0.1em', display: 'block', marginBottom: '4px' }}>
                            Grau de Confiança do Match
                        </span>
                        <div style={{ fontSize: '0.9rem', fontWeight: 900, color: 'black' }}>Descrição Normalizada Filtrada</div>
                        <p style={{ fontSize: '0.8rem', fontWeight: 600, color: '#374151', margin: '8px 0 0', lineHeight: 1.5, borderLeft: '3px solid #2563eb', paddingLeft: '12px' }}>
                            Esta análise considera registros dos últimos 12 meses. O sistema normaliza as descrições para identificar variações do mesmo item.
                        </p>
                    </div>
                </div>

                {/* Historical Table Section */}
                <section style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', borderLeft: '4px solid black', paddingLeft: '16px' }}>
                        <h3 style={{ margin: 0, fontSize: '0.9rem', fontWeight: 950, textTransform: 'uppercase', letterSpacing: '0.1em' }}>Histórico de Aquisições</h3>
                    </div>
                    
                    {isLoading ? (
                        <div style={{ padding: '60px', textAlign: 'center', border: '2px dashed #e5e7eb', backgroundColor: 'white' }}>
                            <div style={{ width: '40px', height: '40px', border: '4px solid #e5e7eb', borderTopColor: 'black', borderRadius: '50%', animation: 'spin 1s linear infinite', margin: '0 auto 16px' }} />
                            <span style={{ fontSize: '0.7rem', fontWeight: 900, textTransform: 'uppercase', letterSpacing: '0.1em', color: 'var(--color-text-muted)' }}>Procurando registros...</span>
                        </div>
                    ) : error ? (
                        <div style={{ padding: '24px', border: '2px solid var(--color-status-red)', backgroundColor: '#fef2f2', color: 'var(--color-status-red)', textAlign: 'center', fontWeight: 900 }}>
                            {error}
                        </div>
                    ) : history.length === 0 ? (
                        <div style={{ padding: '60px', textAlign: 'center', border: '2px dashed #e5e7eb', backgroundColor: 'var(--color-bg-page)' }}>
                            <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-muted)' }}>Nenhum registro histórico encontrado.</span>
                        </div>
                    ) : (
                        <div style={{ border: '2px solid black', boxShadow: 'var(--shadow-brutal)', overflow: 'hidden' }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse', backgroundColor: 'white', tableLayout: 'fixed' }}>
                                <thead>
                                    <tr style={{ backgroundColor: '#f9fafb', borderBottom: '2px solid black' }}>
                                        <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.65rem', fontWeight: 950, textTransform: 'uppercase', letterSpacing: '0.1em', width: '30%', borderRight: '1.5px solid black' }}>Data & Pedido</th>
                                        <th style={{ padding: '16px', textAlign: 'left', fontSize: '0.65rem', fontWeight: 950, textTransform: 'uppercase', letterSpacing: '0.1em', borderRight: '1.5px solid black' }}>Fornecedor</th>
                                        <th style={{ padding: '16px', textAlign: 'right', fontSize: '0.65rem', fontWeight: 950, textTransform: 'uppercase', letterSpacing: '0.1em', width: '25%' }}>Preço Unit.</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {history.map((record, idx) => (
                                        <tr 
                                            key={record.requestId + idx}
                                            style={{ borderBottom: idx === history.length - 1 ? 'none' : '1.5px solid #000', backgroundColor: record.isLastPurchase ? '#fffbeb' : 'white' }}
                                            className="hover:bg-gray-50"
                                        >
                                            <td style={{ padding: '16px', borderRight: '1.5px solid black', verticalAlign: 'top' }}>
                                                <div style={{ fontWeight: 900, fontSize: '0.85rem', color: 'black', marginBottom: '8px' }}>{formatDate(record.purchaseDate)}</div>
                                                <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
                                                    <span style={{ fontSize: '10px', fontWeight: 900, backgroundColor: 'black', color: 'white', padding: '2px 6px', textTransform: 'uppercase' }}>#{record.requestNumber}</span>
                                                    {record.isLastPurchase && <span style={{ fontSize: '10px', fontWeight: 900, backgroundColor: 'var(--color-warning)', color: 'black', padding: '2px 6px', textTransform: 'uppercase', border: '1px solid black' }}>Última</span>}
                                                </div>
                                            </td>
                                            <td style={{ padding: '16px', borderRight: '1.5px solid black', verticalAlign: 'top' }}>
                                                <div style={{ fontWeight: 900, fontSize: '0.85rem', color: 'black', marginBottom: '4px', textOverflow: 'ellipsis', overflow: 'hidden', whiteSpace: 'nowrap' }}>{record.supplierName}</div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.7rem', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase' }}>
                                                    <Package size={12} /> {record.plantName || record.departmentName}
                                                </div>
                                            </td>
                                            <td style={{ padding: '16px', textAlign: 'right', verticalAlign: 'top', backgroundColor: '#fcfcfc' }}>
                                                <div style={{ fontWeight: 950, fontSize: '1rem', color: 'black' }}>{formatCurrencyAO(record.unitPrice, record.currency)}</div>
                                                {record.unitPrice !== item.currentUnitPrice && (
                                                    <div style={{ 
                                                        marginTop: '4px', 
                                                        fontSize: '0.7rem', 
                                                        fontWeight: 900, 
                                                        display: 'flex', 
                                                        alignItems: 'center', 
                                                        justifyContent: 'flex-end', 
                                                        gap: '4px',
                                                        color: record.unitPrice < item.currentUnitPrice ? 'var(--color-status-red)' : '#16a34a'
                                                    }}>
                                                        {record.unitPrice < item.currentUnitPrice ? <TrendingUp size={12} /> : <TrendingDown size={12} />}
                                                        {Math.abs(((item.currentUnitPrice - record.unitPrice) / record.unitPrice) * 100).toFixed(1)}%
                                                    </div>
                                                )}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                </section>
            </div>
        </div>
    );
};

// --- Sub-components (Internal to this view) ---

function MetricCard({ label, value }: { label: string; value: string }) {
    return (
        <div style={{ 
            padding: '20px', 
            border: '2px solid black', 
            backgroundColor: 'white',
            display: 'flex',
            flexDirection: 'column',
            gap: '4px',
            boxShadow: 'var(--shadow-brutal)'
        }}>
            <span style={{ fontSize: '0.65rem', fontWeight: 900, textTransform: 'uppercase', color: 'var(--color-text-muted)', letterSpacing: '0.1em' }}>{label}</span>
            <div style={{ fontSize: '1.5rem', fontWeight: 950, color: 'black', letterSpacing: '-0.02em' }}>{value}</div>
        </div>
    );
}
