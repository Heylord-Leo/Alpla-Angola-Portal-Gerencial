import React from 'react';
import { RequestDetailsDto } from '../../../types';

interface WizardStepRequestOverviewProps {
    request: RequestDetailsDto | null;
}

export const WizardStepRequestOverview: React.FC<WizardStepRequestOverviewProps> = ({ request }) => {
    if (!request) return null;

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <h3 style={{ fontSize: '1.125rem', fontWeight: 500, color: 'var(--color-text-main)', borderBottom: '1px solid var(--color-border)', paddingBottom: '8px', margin: 0 }}>
                Resumo da Requisição
            </h3>
            
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '16px', backgroundColor: '#f8fafc', padding: '16px', borderRadius: '6px', border: '1px solid var(--color-border)' }}>
                <div>
                    <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Número da Requisição</p>
                    <p style={{ fontSize: '0.875rem', fontWeight: 500, color: 'var(--color-text-main)', margin: 0 }}>{request.requestNumber || 'N/A'}</p>
                </div>
                <div>
                    <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Solicitante</p>
                    <p style={{ fontSize: '0.875rem', fontWeight: 500, color: 'var(--color-text-main)', margin: 0 }}>{request.requesterName}</p>
                </div>
                <div>
                    <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Departamento</p>
                    <p style={{ fontSize: '0.875rem', color: 'var(--color-text-main)', margin: 0 }}>{request.departmentName}</p>
                </div>
                <div>
                    <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Planta</p>
                    <p style={{ fontSize: '0.875rem', color: 'var(--color-text-main)', margin: 0 }}>{request.plantName}</p>
                </div>
                <div>
                    <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Tipo</p>
                    <p style={{ fontSize: '0.875rem', color: 'var(--color-text-main)', margin: 0 }}>{request.requestTypeName}</p>
                </div>
                <div>
                    <p style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', fontWeight: 600, margin: '0 0 4px 0' }}>Status Atual</p>
                    <p style={{ fontSize: '0.875rem', color: 'var(--color-text-main)', margin: 0 }}>{request.statusName}</p>
                </div>
            </div>

            <div style={{ marginTop: '24px' }}>
                <h4 style={{ fontSize: '0.875rem', fontWeight: 600, color: 'var(--color-text-main)', marginBottom: '12px' }}>
                    Itens Solicitados ({request.lineItems?.length || 0})
                </h4>
                <div style={{ overflowX: 'auto' }}>
                    <table style={{ minWidth: '100%', borderCollapse: 'collapse', border: '1px solid var(--color-border)', borderRadius: '6px' }}>
                        <thead style={{ backgroundColor: '#f9fafb' }}>
                            <tr>
                                <th style={{ padding: '12px 16px', textAlign: 'left', fontSize: '0.75rem', fontWeight: 500, color: '#6b7280', textTransform: 'uppercase', borderBottom: '1px solid #e5e7eb' }}>#</th>
                                <th style={{ padding: '12px 16px', textAlign: 'left', fontSize: '0.75rem', fontWeight: 500, color: '#6b7280', textTransform: 'uppercase', borderBottom: '1px solid #e5e7eb' }}>Descrição</th>
                                <th style={{ padding: '12px 16px', textAlign: 'right', fontSize: '0.75rem', fontWeight: 500, color: '#6b7280', textTransform: 'uppercase', borderBottom: '1px solid #e5e7eb' }}>Qtd</th>
                                <th style={{ padding: '12px 16px', textAlign: 'left', fontSize: '0.75rem', fontWeight: 500, color: '#6b7280', textTransform: 'uppercase', borderBottom: '1px solid #e5e7eb' }}>Unidade</th>
                            </tr>
                        </thead>
                        <tbody style={{ backgroundColor: '#fff' }}>
                            {request.lineItems?.map((item: any) => (
                                <tr key={item.id} style={{ borderBottom: '1px solid #e5e7eb' }}>
                                    <td style={{ padding: '8px 16px', whiteSpace: 'nowrap', fontSize: '0.875rem', color: '#111827', fontWeight: 500 }}>{item.lineNumber}</td>
                                    <td style={{ padding: '8px 16px', fontSize: '0.875rem', color: '#111827' }}>{item.description}</td>
                                    <td style={{ padding: '8px 16px', whiteSpace: 'nowrap', fontSize: '0.875rem', color: '#111827', textAlign: 'right' }}>{item.quantity}</td>
                                    <td style={{ padding: '8px 16px', whiteSpace: 'nowrap', fontSize: '0.875rem', color: '#111827' }}>{item.unitName || '-'}</td>
                                </tr>
                            ))}
                            {(!request.lineItems || request.lineItems.length === 0) && (
                                <tr>
                                    <td colSpan={4} style={{ padding: '16px', textAlign: 'center', fontSize: '0.875rem', color: '#6b7280' }}>Nenhum item encontrado</td>
                                </tr>
                            )}
                        </tbody>
                    </table>
                </div>
            </div>
            
            <div style={{ backgroundColor: '#eff6ff', padding: '16px', borderRadius: '6px', border: '1px solid #dbeafe', display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: '24px' }}>
                <p style={{ fontSize: '0.75rem', color: '#64748b', textTransform: 'uppercase', fontWeight: 600, margin: 0 }}>Valor Estimado Total</p>
                <p style={{ fontSize: '1.125rem', fontWeight: 700, color: '#0f172a', margin: 0 }}>
                    {new Intl.NumberFormat('pt-AO', { style: 'currency', currency: request.currencyCode || 'AOA' }).format(request.estimatedTotalAmount || 0)}
                </p>
            </div>
        </div>
    );
};
