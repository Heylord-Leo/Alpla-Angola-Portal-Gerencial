import React, { useState } from 'react';
import { RequestDetailsDto, ApprovalIntelligenceDto } from '../../types';
import {
    FileText, Calendar, Building, User, Tag, Paperclip, Download,
    ChevronDown, Users, History,
    AlertTriangle, Factory, TrendingUp, Brain
} from 'lucide-react';
import { formatCurrencyAO, formatDate } from '../../lib/utils';
import { DecisionFinancialTrendLine } from './components/DecisionFinancialTrendLine';
import { DecisionInsightsPanel } from './components/DecisionInsightsPanel';

// Inlined status color helper to avoid external resolution issues
const getStatusColor = (status: string) => {
    switch (status) {
        case 'WAITING_AREA_APPROVAL':
        case 'WAITING_FINAL_APPROVAL':
            return { bg: '#FEF3C7', text: '#D97706' };
        case 'APPROVED':
        case 'PO_ISSUED':
        case 'PAYMENT_SCHEDULED':
        case 'PAYMENT_COMPLETED':
        case 'COMPLETED':
            return { bg: '#D1FAE5', text: '#059669' };
        case 'REJECTED':
        case 'CANCELLED':
            return { bg: '#FEE2E2', text: '#DC2626' };
        case 'WAITING_QUOTATION':
        case 'WAITING_RECEIPT':
        case 'WAITING_SUPPLIER_DELIVERY':
            return { bg: '#DBEAFE', text: '#2563EB' };
        case 'AREA_ADJUSTMENT':
        case 'FINAL_ADJUSTMENT':
            return { bg: '#FEF3C7', text: '#B45309' };
        default:
            return { bg: '#F3F4F6', text: '#4B5563' };
    }
};

interface WizardStepOverviewProps {
    request: RequestDetailsDto;
    onDownloadAttachment?: (attachmentId: string, fileName: string) => void;
    intelligence?: ApprovalIntelligenceDto | null;
    approvalStage?: 'AREA' | 'FINAL';
}

/** Collapsible accordion section */
function AccordionSection({ title, icon, count, defaultOpen = false, children }: {
    title: string;
    icon: React.ReactNode;
    count?: number;
    defaultOpen?: boolean;
    children: React.ReactNode;
}) {
    const [isOpen, setIsOpen] = useState(defaultOpen);
    return (
        <div style={{
            border: '1px solid #E5E7EB', borderRadius: '8px', overflow: 'hidden',
            backgroundColor: '#FFFFFF'
        }}>
            <button
                onClick={() => setIsOpen(!isOpen)}
                style={{
                    width: '100%', padding: '12px 16px', display: 'flex', alignItems: 'center',
                    justifyContent: 'space-between', backgroundColor: isOpen ? '#F9FAFB' : '#FFFFFF',
                    border: 'none', cursor: 'pointer', transition: 'background-color 0.15s'
                }}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                    <span style={{ color: '#6B7280', display: 'flex' }}>{icon}</span>
                    <span style={{ fontSize: '0.875rem', fontWeight: 600, color: '#374151' }}>{title}</span>
                    {count !== undefined && (
                        <span style={{
                            fontSize: '0.6875rem', fontWeight: 600, color: '#6B7280',
                            backgroundColor: '#F3F4F6', padding: '2px 8px', borderRadius: '12px'
                        }}>
                            {count}
                        </span>
                    )}
                </div>
                <span style={{ color: '#9CA3AF', display: 'flex', transition: 'transform 0.2s', transform: isOpen ? 'rotate(0)' : 'rotate(-90deg)' }}>
                    <ChevronDown size={18} />
                </span>
            </button>
            {isOpen && (
                <div style={{ borderTop: '1px solid #E5E7EB', padding: '16px' }}>
                    {children}
                </div>
            )}
        </div>
    );
}

export const WizardStepOverview: React.FC<WizardStepOverviewProps> = ({ request, onDownloadAttachment, intelligence, approvalStage }) => {
    const activeItems = request.lineItems?.filter((i: any) => !i.isDeleted) || [];

    // Check for rework/adjustment status
    const isRework = request.statusCode === 'AREA_ADJUSTMENT' || request.statusCode === 'FINAL_ADJUSTMENT';
    const lastAdjustmentEntry = request.statusHistory?.find(h =>
        h.actionTaken === 'REQUEST_ADJUSTMENT' || h.newStatusName?.includes('Reajuste') || h.newStatusName?.includes('Ajuste')
    );

    // Check for price alerts
    // (Simplified: we just flag if there are quotes to review, the detailed analysis is in Step 3)

    const summaryFields = [
        { label: 'Solicitante', value: request.requesterName, icon: <User size={14} color="#6B7280" /> },
        { label: 'Departamento', value: request.departmentName || '-', icon: <Tag size={14} color="#6B7280" /> },
        { label: 'Empresa', value: request.companyName || '-', icon: <Building size={14} color="#6B7280" /> },
        { label: 'Planta', value: request.plantName || '-', icon: <Factory size={14} color="#6B7280" /> },
        { label: 'Necessário Até', value: request.needByDateUtc ? formatDate(request.needByDateUtc) : '---', icon: <Calendar size={14} color="#6B7280" /> },
        { label: 'Grau de Necessidade', value: request.needLevelName || '-' },
    ];

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
            {/* Section header */}
            <div>
                <h3 style={{ fontSize: '1.125rem', fontWeight: 700, color: '#1F2937', margin: 0 }}>Visão Geral do Pedido</h3>
                <div style={{
                    backgroundColor: '#EFF6FF', border: '1px solid #BFDBFE',
                    borderRadius: '6px', padding: '12px 16px', color: '#1E3A8A',
                    fontSize: '0.875rem', marginTop: '12px', display: 'flex', gap: '10px', alignItems: 'flex-start'
                }}>
                    <span style={{ fontSize: '1.25rem', lineHeight: 1 }}>💡</span>
                    <div>
                        Revise os dados principais do pedido, os itens solicitados e o valor estimado antes de avançar para a atribuição financeira.
                    </div>
                </div>
            </div>

            {/* Rework/Adjustment warning */}
            {isRework && (
                <div style={{
                    padding: '14px 16px', backgroundColor: '#FEF3C7', border: '1px solid #FDE68A',
                    borderRadius: '8px', display: 'flex', alignItems: 'flex-start', gap: '12px'
                }}>
                    <AlertTriangle color="#D97706" size={20} style={{ flexShrink: 0, marginTop: '1px' }} />
                    <div>
                        <div style={{ fontWeight: 700, color: '#92400E', fontSize: '0.875rem' }}>Pedido em Reajuste</div>
                        <div style={{ fontSize: '0.8125rem', color: '#A16207', marginTop: '2px' }}>
                            Este pedido foi devolvido para correção.
                            {lastAdjustmentEntry?.comment && (
                                <span> Motivo: <strong>"{lastAdjustmentEntry.comment}"</strong></span>
                            )}
                        </div>
                    </div>
                </div>
            )}

            {/* Header card: identity + financial */}
            <div style={{
                display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '16px'
            }}>
                {/* Left: Request identity */}
                <div style={{
                    backgroundColor: '#FFFFFF', padding: '20px', borderRadius: '8px',
                    border: '1px solid #E5E7EB', display: 'flex', flexDirection: 'column', gap: '14px'
                }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                        <div>
                            <div style={{ fontSize: '0.75rem', color: '#6B7280', fontWeight: 500, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Nº do Pedido</div>
                            <div style={{ fontSize: '1.125rem', fontWeight: 700, color: '#111827', display: 'flex', alignItems: 'center', gap: '8px', marginTop: '2px' }}>
                                <FileText size={18} color="#6B7280" />
                                {request.requestNumber}
                            </div>
                        </div>
                        <span style={{
                            padding: '4px 12px', borderRadius: '16px', fontSize: '0.6875rem', fontWeight: 600,
                            backgroundColor: getStatusColor(request.statusCode).bg,
                            color: getStatusColor(request.statusCode).text
                        }}>
                            {request.statusName}
                        </span>
                    </div>
                    <div>
                        <div style={{ fontSize: '0.75rem', color: '#6B7280', fontWeight: 500 }}>Título</div>
                        <div style={{ fontSize: '0.875rem', color: '#1F2937', marginTop: '2px' }}>{request.title || 'Sem título'}</div>
                    </div>
                    <div style={{ display: 'flex', gap: '20px' }}>
                        <div>
                            <div style={{ fontSize: '0.75rem', color: '#6B7280', fontWeight: 500 }}>Total Estimado</div>
                            <div style={{ fontSize: '1.25rem', fontWeight: 700, color: '#10B981', marginTop: '2px' }}>
                                {formatCurrencyAO(request.estimatedTotalAmount, request.currencyCode || 'AOA')}
                            </div>
                        </div>
                        <div>
                            <div style={{ fontSize: '0.75rem', color: '#6B7280', fontWeight: 500 }}>Moeda</div>
                            <div style={{ fontSize: '0.875rem', fontWeight: 600, color: '#374151', marginTop: '4px' }}>{request.currencyCode || 'AOA'}</div>
                        </div>
                    </div>
                </div>

                {/* Right: Organizational context */}
                <div style={{
                    backgroundColor: '#FFFFFF', padding: '20px', borderRadius: '8px',
                    border: '1px solid #E5E7EB', display: 'flex', flexDirection: 'column', gap: '12px'
                }}>
                    {summaryFields.map((field, idx) => (
                        <div key={idx} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                {field.icon && <span style={{ display: 'flex' }}>{field.icon}</span>}
                                <span style={{ fontSize: '0.8125rem', color: '#6B7280', fontWeight: 500 }}>{field.label}</span>
                            </div>
                            <span style={{ fontSize: '0.8125rem', fontWeight: 600, color: '#111827', textAlign: 'right', maxWidth: '200px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {field.value || '—'}
                            </span>
                        </div>
                    ))}
                </div>
            </div>

            {/* Description / Justification */}
            {request.description && (
                <div style={{
                    backgroundColor: '#FFFFFF', padding: '16px', borderRadius: '8px',
                    border: '1px solid #E5E7EB'
                }}>
                    <div style={{ fontSize: '0.75rem', color: '#6B7280', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '8px' }}>
                        Descrição / Justificativa
                    </div>
                    <div style={{ fontSize: '0.875rem', color: '#374151', whiteSpace: 'pre-wrap', lineHeight: 1.6 }}>
                        {request.description}
                    </div>
                </div>
            )}

            {/* Compact items summary */}
            <div style={{
                backgroundColor: '#FFFFFF', borderRadius: '8px', border: '1px solid #E5E7EB',
                overflow: 'hidden'
            }}>
                <div style={{
                    padding: '12px 16px', backgroundColor: '#F9FAFB',
                    borderBottom: '1px solid #E5E7EB',
                    fontSize: '0.75rem', fontWeight: 700, color: '#374151', textTransform: 'uppercase', letterSpacing: '0.05em'
                }}>
                    Itens Solicitados ({activeItems.length})
                </div>
                <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                    <thead>
                        <tr style={{ borderBottom: '1px solid #E5E7EB', backgroundColor: '#FAFAFA' }}>
                            <th style={{ padding: '8px 16px', fontSize: '0.6875rem', fontWeight: 600, color: '#6B7280', textAlign: 'left', textTransform: 'uppercase' }}>#</th>
                            <th style={{ padding: '8px 16px', fontSize: '0.6875rem', fontWeight: 600, color: '#6B7280', textAlign: 'left', textTransform: 'uppercase' }}>Descrição</th>
                            <th style={{ padding: '8px 16px', fontSize: '0.6875rem', fontWeight: 600, color: '#6B7280', textAlign: 'center', textTransform: 'uppercase' }}>Qtd</th>
                            <th style={{ padding: '8px 16px', fontSize: '0.6875rem', fontWeight: 600, color: '#6B7280', textAlign: 'right', textTransform: 'uppercase' }}>Valor Estimado</th>
                        </tr>
                    </thead>
                    <tbody>
                        {activeItems.map((item: any, idx: number) => (
                            <tr key={item.id} style={{ borderBottom: idx < activeItems.length - 1 ? '1px solid #F3F4F6' : 'none' }}>
                                <td style={{ padding: '10px 16px', fontSize: '0.8125rem', color: '#6B7280', fontWeight: 600 }}>{item.lineNumber}</td>
                                <td style={{ padding: '10px 16px', fontSize: '0.8125rem', color: '#111827', maxWidth: '350px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={item.description}>
                                    {item.description}
                                </td>
                                <td style={{ padding: '10px 16px', fontSize: '0.8125rem', color: '#6B7280', textAlign: 'center' }}>
                                    {item.quantity} {item.unit || 'UN'}
                                </td>
                                <td style={{ padding: '10px 16px', fontSize: '0.8125rem', color: '#374151', fontWeight: 600, textAlign: 'right' }}>
                                    {formatCurrencyAO(item.totalAmount, request.currencyCode || 'AOA')}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

            {/* Collapsed accordions for ancillary information */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                {/* Attachments */}
                {request.attachments && request.attachments.length > 0 && (
                    <AccordionSection
                        title="Anexos do Pedido"
                        icon={<Paperclip size={16} />}
                        count={request.attachments.length}
                    >
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                            {request.attachments.map((att: any) => (
                                <div key={att.id} style={{
                                    display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                                    padding: '12px 14px', backgroundColor: '#F9FAFB',
                                    border: '1px solid #E5E7EB', borderRadius: '8px'
                                }}>
                                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', flex: 1, minWidth: 0 }}>
                                        <div style={{
                                            width: '36px', height: '36px', borderRadius: '6px',
                                            backgroundColor: '#EFF6FF', display: 'flex', alignItems: 'center', justifyContent: 'center',
                                            flexShrink: 0, border: '1px solid #DBEAFE'
                                        }}>
                                            <Paperclip size={16} color="#2563EB" />
                                        </div>
                                        <div style={{ minWidth: 0 }}>
                                            <div style={{
                                                fontSize: '0.8125rem', fontWeight: 700, color: '#111827',
                                                whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: '350px'
                                            }} title={att.fileName}>
                                                {att.fileName}
                                            </div>
                                            <div style={{ fontSize: '0.6875rem', fontWeight: 600, color: '#6B7280', marginTop: '2px' }}>
                                                {/* Legacy quotation documents were stamped PROFORMA — when the file is
                                                    referenced by a saved quotation it reads as "Cotação" contextually. */}
                                                {att.attachmentTypeCode === 'QUOTATION' ? 'Cotação' :
                                                 att.attachmentTypeCode === 'PROFORMA'
                                                    ? ((request.quotations || []).some(q => q.proformaAttachmentId === att.id) ? 'Cotação' : 'Proforma') :
                                                 att.attachmentTypeCode === 'GENERAL' ? 'Documento Geral' :
                                                 att.attachmentTypeCode || 'Anexo'}
                                                {att.uploadedByName && ` • Por ${att.uploadedByName}`}
                                            </div>
                                        </div>
                                    </div>
                                    {onDownloadAttachment && (
                                        <button
                                            onClick={() => onDownloadAttachment(att.id, att.fileName)}
                                            title={`Baixar ${att.fileName}`}
                                            style={{
                                                display: 'flex', alignItems: 'center', gap: '6px',
                                                padding: '6px 12px', backgroundColor: '#FFFFFF',
                                                border: '1px solid #D1D5DB', borderRadius: '6px',
                                                color: '#374151', fontSize: '0.6875rem', fontWeight: 600,
                                                cursor: 'pointer', transition: 'all 0.15s', flexShrink: 0,
                                                marginLeft: '12px'
                                            }}
                                            onMouseOver={(e) => { e.currentTarget.style.backgroundColor = '#EFF6FF'; e.currentTarget.style.borderColor = '#93C5FD'; e.currentTarget.style.color = '#2563EB'; }}
                                            onMouseOut={(e) => { e.currentTarget.style.backgroundColor = '#FFFFFF'; e.currentTarget.style.borderColor = '#D1D5DB'; e.currentTarget.style.color = '#374151'; }}
                                        >
                                            <Download size={14} /> Baixar
                                        </button>
                                    )}
                                </div>
                            ))}
                        </div>
                    </AccordionSection>
                )}

                {/* Workflow participants */}
                <AccordionSection
                    title="Participantes do Fluxo"
                    icon={<Users size={16} />}
                >
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
                        {[
                            { label: 'Solicitante', value: request.requesterName },
                            { label: 'Comprador', value: request.buyerName || '—' },
                            {
                                // Fase B: antes da decisão mostra os managers elegíveis; depois, o decisor real.
                                label: 'Aprovador de Área',
                                value: request.areaApproverName
                                    || (request.eligibleAreaManagerNames && request.eligibleAreaManagerNames.length > 0
                                        ? `Pendente — ${request.eligibleAreaManagerNames.length} responsáve${request.eligibleAreaManagerNames.length > 1 ? 'is' : 'l'}: ${request.eligibleAreaManagerNames.join(', ')}`
                                        : '—')
                            },
                            { label: 'Aprovador Final', value: request.finalApproverName || '—' },
                        ].map((p, idx) => (
                            <div key={idx} style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                                <span style={{ fontSize: '0.6875rem', fontWeight: 600, color: '#6B7280', textTransform: 'uppercase' }}>{p.label}</span>
                                <span style={{ fontSize: '0.8125rem', fontWeight: 600, color: '#111827' }}>{p.value}</span>
                            </div>
                        ))}
                    </div>
                </AccordionSection>

                {/* Status history */}
                {request.statusHistory && request.statusHistory.length > 0 && (
                    <AccordionSection
                        title="Histórico do Pedido"
                        icon={<History size={16} />}
                        count={request.statusHistory.length}
                    >
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', maxHeight: '200px', overflowY: 'auto' }}>
                            {request.statusHistory.map((entry: any, idx: number) => (
                                <div key={entry.id || idx} style={{
                                    display: 'flex', gap: '12px', alignItems: 'flex-start',
                                    padding: '8px 0',
                                    borderBottom: idx < request.statusHistory.length - 1 ? '1px solid #F3F4F6' : 'none'
                                }}>
                                    <div style={{ width: '8px', height: '8px', borderRadius: '50%', backgroundColor: '#D1D5DB', marginTop: '6px', flexShrink: 0 }} />
                                    <div style={{ flex: 1 }}>
                                        <div style={{ fontSize: '0.8125rem', fontWeight: 600, color: '#374151' }}>
                                            {entry.newStatusName}
                                        </div>
                                        <div style={{ fontSize: '0.75rem', color: '#6B7280' }}>
                                            {entry.actorName} • {formatDate(entry.createdAtUtc)}
                                        </div>
                                        {entry.comment && (
                                            <div style={{ fontSize: '0.75rem', color: '#92400E', backgroundColor: '#FFFBEB', padding: '4px 8px', borderRadius: '4px', marginTop: '4px' }}>
                                                "{entry.comment}"
                                            </div>
                                        )}
                                    </div>
                                </div>
                            ))}
                        </div>
                    </AccordionSection>
                )}

                {/* Contexto Financeiro Visual */}
                <AccordionSection
                    title="Contexto Financeiro Visual"
                    icon={<TrendingUp size={16} />}
                >
                    <div style={{ padding: '4px 0' }}>
                        <DecisionFinancialTrendLine requestId={request.id} />
                    </div>
                </AccordionSection>

                {/* Inteligência para Decisão */}
                {intelligence && (
                    <AccordionSection
                        title="Inteligência para Decisão"
                        icon={<Brain size={16} />}
                    >
                        <div style={{ padding: '4px 0' }}>
                            <DecisionInsightsPanel
                                intelligence={intelligence}
                                approvalStage={approvalStage || 'AREA'}
                                requestData={{
                                    description: request.description,
                                    supplierName: (request as any).supplierName,
                                    costCenterCode: (request as any).costCenterCode,
                                    requestTypeCode: (request as any).requestTypeCode,
                                    hasQuotations: ((request as any).quotations?.length || 0) > 0
                                }}
                            />
                        </div>
                    </AccordionSection>
                )}
            </div>
        </div>
    );
};
