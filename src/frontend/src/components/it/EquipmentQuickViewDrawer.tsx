import React, { useState, useEffect } from 'react';
import { X, Edit3, Wrench, AlertTriangle, BookmarkCheck, Archive, Loader2, Download, Upload, FileText, FileCheck, FileX, Clock, User, Cpu, RefreshCw, RotateCw, ExternalLink, Printer, Copy, QrCode } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { EQUIPMENT_STATUS_CONFIG, EQUIPMENT_TYPE_CONFIG, MOVEMENT_TYPE_LABELS, ASSIGNMENT_STATUS_CONFIG, DOCUMENT_TYPE_LABELS } from '../../types/itEquipment';
import type { ITEquipmentDetail } from '../../types/itEquipment';

import { RepairEquipmentModal } from './RepairEquipmentModal';
import { LostEquipmentModal } from './LostEquipmentModal';
import { RetireEquipmentModal } from './RetireEquipmentModal';
import { ReserveEquipmentModal } from './ReserveEquipmentModal';
import { ChangeEquipmentUserModal } from './ChangeEquipmentUserModal';
import { EquipmentFormModal } from './EquipmentFormModal';
import { ReactivateEquipmentModal } from './ReactivateEquipmentModal';
import { ActionDropdown } from '../common/ActionDropdown';
import { StatusBadge } from '../common/ui/StatusBadge';

interface Props {
    equipmentId: string;
    onClose: () => void;
    onRefresh: () => void;
}

export function EquipmentQuickViewDrawer({ equipmentId, onClose, onRefresh }: Props) {
    const [detail, setDetail] = useState<ITEquipmentDetail | null>(null);
    const [loading, setLoading] = useState(true);
    const [activeModal, setActiveModal] = useState<string | null>(null);
    const [activeTab, setActiveTab] = useState<'info' | 'assignments' | 'movements'>('info');

    const load = async () => {
        try {
            setLoading(true);
            const data = await itEquipmentApi.get(equipmentId);
            setDetail(data);
        } catch { }
        finally { setLoading(false); }
    };

    useEffect(() => { load(); }, [equipmentId]);

    const handleModalClose = () => setActiveModal(null);
    const handleModalSuccess = () => { setActiveModal(null); load(); onRefresh(); };

    const statusCfg = detail ? (EQUIPMENT_STATUS_CONFIG[detail.statusCode] || EQUIPMENT_STATUS_CONFIG['UNKNOWN']) : null;
    const canReturn = detail && detail.statusCode === 'IN_USE';
    const canRepair = detail && !['LOST', 'RETIRED', 'DISPOSED'].includes(detail.statusCode);
    const canReserve = detail && detail.statusCode === 'AVAILABLE';
    const canRetire = detail && !['RETIRED', 'DISPOSED'].includes(detail.statusCode);
    const canReactivate = detail && detail.statusCode === 'RETIRED';

    return (
        <>
            {/* Backdrop */}
            <div
                onClick={onClose}
                style={{
                    position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.4)',
                    zIndex: 'var(--z-drawer)' as any, transition: 'opacity 0.3s'
                }}
            />
            {/* Drawer */}
            <div style={{
                position: 'fixed', top: 0, right: 0, bottom: 0, width: 620,
                backgroundColor: 'var(--color-bg-surface)', borderLeft: '1px solid var(--color-border)',
                zIndex: 'calc(var(--z-drawer) + 1)' as any, display: 'flex', flexDirection: 'column',
                boxShadow: '-8px 0 30px rgba(0,0,0,0.15)',
                animation: 'slideIn 0.25s ease-out'
            }}>
                <style>{`@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }`}</style>

                {/* Header */}
                <div style={{
                    padding: '16px 20px', borderBottom: '1px solid var(--color-border)',
                    display: 'flex', justifyContent: 'space-between', alignItems: 'center'
                }}>
                    <div>
                        <span style={{ fontSize: '0.68rem', color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', fontWeight: 600 }}>Código do Ativo</span>
                        <h2 style={{ margin: 0, fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)', fontFamily: 'monospace', letterSpacing: '0.5px' }}>
                            {detail?.assetTag || 'Carregando...'}
                        </h2>
                        {detail && statusCfg && (
                            <div style={{ marginTop: 6 }}>
                                <StatusBadge status={detail.statusCode} label={statusCfg.label} />
                            </div>
                        )}
                        {detail?.purchaseDocumentPending && (
                            <div style={{
                                display: 'flex', alignItems: 'center', gap: 6, marginTop: 6,
                                padding: '4px 10px', borderRadius: 6, fontSize: '0.75rem', fontWeight: 500,
                                color: '#d97706', backgroundColor: '#fffbeb', border: '1px solid #fde68a'
                            }}>
                                <AlertTriangle size={13} />
                                Cadastro incompleto — documento de compra pendente
                            </div>
                        )}
                    </div>
                    <button onClick={onClose} style={{
                        background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)',
                        padding: 6, borderRadius: 6
                    }}>
                        <X size={20} />
                    </button>
                </div>

                {/* Actions */}
                {detail && (
                    <div style={{
                        display: 'flex', gap: 6, padding: '10px 20px', borderBottom: '1px solid var(--color-border)',
                        flexWrap: 'wrap', alignItems: 'center'
                    }}>
                        <ActionBtn label="Editar" icon={<Edit3 size={13} />} onClick={() => setActiveModal('edit')} />

                        {canReturn && (
                            <ActionBtn 
                                label="Trocar Utilizador" 
                                icon={<RefreshCw size={13} />} 
                                onClick={() => setActiveModal('change-user')} 
                                color="#14b8a6" 
                                disabled={detail.purchaseDocumentPending}
                                disabledReason="Cadastro incompleto: documento de compra/entrega pendente."
                            />
                        )}
                        {canReserve && (
                            <ActionBtn 
                                label="Reservar" 
                                icon={<BookmarkCheck size={13} />} 
                                onClick={() => setActiveModal('reserve')} 
                                color="#f59e0b" 
                                disabled={detail.purchaseDocumentPending}
                                disabledReason="Cadastro incompleto: documento de compra/entrega pendente."
                            />
                        )}

                        <div style={{ marginLeft: 'auto' }}>
                            <ActionDropdown options={[
                                canRepair ? { label: "Conserto", icon: <Wrench size={13} />, onClick: () => setActiveModal('repair'), color: "#f97316" } : null,
                                { label: "Perdido", icon: <AlertTriangle size={13} />, onClick: () => setActiveModal('lost'), color: "#ef4444" },
                                canRetire ? { label: "Baixar", icon: <Archive size={13} />, onClick: () => setActiveModal('retire'), color: "#6b7280" } : null,
                                canReactivate ? { label: "Reativar", icon: <RotateCw size={13} />, onClick: () => setActiveModal('reactivate'), color: "#22c55e" } : null
                            ].filter(Boolean) as any} />
                        </div>

                        {/* ── Asset quick actions ── */}
                        <div style={{ width: '100%', borderTop: '1px dashed var(--color-border)', marginTop: 2, paddingTop: 6, display: 'flex', gap: 6 }}>
                            {detail.qrCodeUrl && (
                                <ActionBtn label="Abrir Ficha" icon={<ExternalLink size={13} />} onClick={() => window.open(detail.qrCodeUrl!, '_blank')} color="#0ea5e9" />
                            )}
                            <ActionBtn label="Imprimir Etiqueta" icon={<Printer size={13} />} onClick={() => window.open(`/it/equipment/${detail.id}/label`, '_blank')} color="#6366f1" />
                            {detail.qrCodeUrl && (
                                <ActionBtn label="Copiar Link" icon={<Copy size={13} />} onClick={() => { navigator.clipboard.writeText(detail.qrCodeUrl!); }} color="#64748b" />
                            )}
                        </div>
                    </div>
                )}

                {/* Tabs */}
                <div style={{
                    display: 'flex', borderBottom: '1px solid var(--color-border)', padding: '0 20px'
                }}>
                    {(['info', 'assignments', 'movements'] as const).map(tab => (
                        <button
                            key={tab}
                            onClick={() => setActiveTab(tab)}
                            style={{
                                padding: '10px 16px', background: 'none', border: 'none', cursor: 'pointer',
                                fontSize: '0.82rem', fontWeight: activeTab === tab ? 700 : 500,
                                color: activeTab === tab ? '#3b82f6' : 'var(--color-text-muted)',
                                borderBottom: activeTab === tab ? '2px solid #3b82f6' : '2px solid transparent',
                                transition: 'all 0.2s'
                            }}
                        >
                            {tab === 'info' ? 'Detalhes' : tab === 'assignments' ? `Atribuições (${detail?.assignments?.length || 0})` : `Movimentações (${detail?.movements?.length || 0})`}
                        </button>
                    ))}
                </div>

                {/* Content */}
                <div style={{ flex: 1, overflowY: 'auto', padding: '16px 20px' }}>
                    {loading ? (
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 60 }}>
                            <Loader2 size={28} style={{ animation: 'spin 1s linear infinite', color: 'var(--color-primary)' }} />
                            <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
                        </div>
                    ) : detail && activeTab === 'info' ? (
                        <InfoTab detail={detail} />
                    ) : detail && activeTab === 'assignments' ? (
                        <AssignmentsTab assignments={detail.assignments} equipmentId={detail.id} documents={detail.documents} onRefresh={load} />
                    ) : detail && activeTab === 'movements' ? (
                        <MovementsTab movements={detail.movements} />
                    ) : null}
                </div>
            </div>

            {/* Modals */}
            {activeModal === 'edit' && detail && <EquipmentFormModal equipment={detail} onClose={handleModalClose} onSuccess={handleModalSuccess} />}

            {activeModal === 'repair' && detail && <RepairEquipmentModal equipmentId={detail.id} statusCode={detail.statusCode} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'lost' && detail && <LostEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'reserve' && detail && <ReserveEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'retire' && detail && <RetireEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'change-user' && detail && <ChangeEquipmentUserModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'reactivate' && detail && <ReactivateEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
        </>
    );
}

function ActionBtn({ label, icon, onClick, color, disabled, disabledReason }: { label: string; icon: React.ReactNode; onClick: () => void; color?: string; disabled?: boolean; disabledReason?: string }) {
    return (
        <button
            onClick={() => { if (!disabled) onClick(); }}
            title={disabled ? disabledReason : undefined}
            style={{
                display: 'flex', alignItems: 'center', gap: 4, padding: '5px 10px',
                border: `1px solid ${color || 'var(--color-border)'}30`,
                borderRadius: 6, background: `${color || 'var(--color-text)'}08`,
                color: disabled ? '#9ca3af' : (color || 'var(--color-text)'), 
                cursor: disabled ? 'not-allowed' : 'pointer',
                fontSize: '0.78rem', fontWeight: 600, transition: 'all 0.15s',
                opacity: disabled ? 0.6 : 1
            }}
            onMouseOver={(e) => { if (!disabled) e.currentTarget.style.backgroundColor = `${color || '#888'}18`; }}
            onMouseOut={(e) => { if (!disabled) e.currentTarget.style.backgroundColor = `${color || '#888'}08`; }}
        >
            {icon} {label}
        </button>
    );
}

function InfoTab({ detail }: { detail: ITEquipmentDetail }) {
    const typeCfg = EQUIPMENT_TYPE_CONFIG[detail.equipmentType] || EQUIPMENT_TYPE_CONFIG['UNKNOWN'];
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            {/* Current Owner */}
            {detail.currentOwnerName && (
                <Section title="Utilizador Atual" icon={<User size={15} />}>
                    <InfoRow label="Nome" value={detail.currentOwnerName} />
                    {detail.currentOwnerEmail && <InfoRow label="Email" value={detail.currentOwnerEmail} />}
                    {detail.currentOwnerEmployeeId && <InfoRow label="ID Funcionário" value={detail.currentOwnerEmployeeId} />}
                </Section>
            )}

            {/* Technical Details */}
            <Section title="Detalhes Técnicos" icon={<Cpu size={15} />}>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 16px' }}>
                    <InfoRow label="Tipo" value={typeCfg.label} />
                    <InfoRow label="Planta" value={detail.plant} />
                    {detail.legacyAssetCode && <InfoRow label="Código Legado" value={detail.legacyAssetCode} mono />}
                    {detail.companyCode && <InfoRow label="Empresa" value={detail.companyCode} />}
                    <InfoRow label="Fabricante" value={detail.manufacturer} />
                    <InfoRow label="Modelo" value={detail.model} />
                    <InfoRow label="Serial Number" value={detail.serialNumber} mono />
                    <InfoRow label="MAC Ethernet" value={detail.macAddress} mono />
                    <InfoRow label="MAC Wi-Fi" value={detail.wifiMacAddress} mono />
                    <InfoRow label="Processador" value={detail.processor} />
                    <InfoRow label="RAM" value={detail.memoryRam} />
                    <InfoRow label="Cor" value={detail.color} />
                    <InfoRow label="Data de Fabricação" value={detail.manufactureDate ? new Date(detail.manufactureDate).toLocaleDateString('pt-AO') : null} />
                    <InfoRow label="Biometric/MFA" value={detail.biometricMfaEnabled ? 'Sim' : 'Não'} />
                    <InfoRow label="ID Card" value={detail.idCard} />
                    <InfoRow label="Origem" value={detail.sourceType === 'IMPORTED_LEGACY' ? 'Importado (Legacy)' : detail.sourceType === 'MANUAL_PURCHASE' ? 'Compra' : 'Registo Manual'} />
                </div>
                {/* QR Code Visual Section */}
                <div style={{ marginTop: 14, padding: 16, background: '#f8fafc', border: '1px solid var(--color-border)', borderRadius: 10, textAlign: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6, marginBottom: 12 }}>
                        <QrCode size={15} style={{ color: 'var(--color-primary)' }} />
                        <span style={{ fontSize: '0.75rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>QR Code do Ativo</span>
                    </div>
                    {detail.qrCodeUrl ? (
                        <>
                            <div style={{ display: 'inline-block', padding: 10, background: '#fff', borderRadius: 8, border: '1px solid #e2e8f0' }}>
                                <QRCodeSVG
                                    value={detail.qrCodeUrl}
                                    size={120}
                                    level="M"
                                    includeMargin={false}
                                />
                            </div>
                            <div style={{ marginTop: 10, fontFamily: 'monospace', fontSize: '0.82rem', fontWeight: 600, color: 'var(--color-text)', letterSpacing: '0.5px' }}>
                                {detail.assetTag}
                            </div>
                            <div style={{ marginTop: 6, fontSize: '0.75rem' }}>
                                <a href={detail.qrCodeUrl} target="_blank" rel="noreferrer" style={{ color: '#0284c7', wordBreak: 'break-all' }}>
                                    {detail.qrCodeUrl}
                                </a>
                            </div>
                            {detail.qrCodeUrl.startsWith('/') && !detail.qrCodeUrl.startsWith('//') && (
                                <div style={{ marginTop: 6, padding: '3px 8px', display: 'inline-flex', alignItems: 'center', gap: 4, background: '#fffbeb', border: '1px solid #fde68a', borderRadius: 4, fontSize: '0.7rem', color: '#92400e' }}>
                                    <AlertTriangle size={11} /> URL relativa — configure FrontendBaseUrl para etiquetas físicas
                                </div>
                            )}
                        </>
                    ) : (
                        <div style={{ padding: 20, color: 'var(--color-text-muted)', fontSize: '0.82rem' }}>
                            QR Code ainda não disponível para este ativo.
                        </div>
                    )}
                </div>
            </Section>

            {/* Acquisition */}
            {detail.acquisition && (
                <Section title="Aquisição / Compra" icon={<FileText size={15} />}>
                    {detail.acquisition.purchaseInfoUnavailable ? (
                        <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                            Informações de compra indisponíveis{detail.acquisition.purchaseInfoUnavailableReason ? `: ${detail.acquisition.purchaseInfoUnavailableReason}` : ''}
                        </div>
                    ) : (
                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 16px' }}>
                            <InfoRow label="Data" value={detail.acquisition.acquisitionDate ? new Date(detail.acquisition.acquisitionDate).toLocaleDateString('pt-PT') : null} />
                            
                            <div style={{ gridColumn: '1 / -1', padding: '8px 12px', background: '#f8fafc', border: '1px solid var(--color-border)', borderRadius: 6, display: 'flex', flexDirection: 'column', gap: 4 }}>
                                <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--color-text-muted)' }}>Fornecedor</span>
                                <span style={{ fontSize: '0.85rem', fontWeight: 500, color: 'var(--color-text)' }}>
                                    {detail.acquisition.supplierName || '—'}
                                </span>
                                {(detail.acquisition.supplierTaxId || detail.acquisition.supplierPortalCode) && (
                                    <div style={{ display: 'flex', gap: 12, fontSize: '0.75rem', color: 'var(--color-text-muted)', marginTop: 2 }}>
                                        {detail.acquisition.supplierTaxId && <span>NIF: <span style={{ fontFamily: 'monospace' }}>{detail.acquisition.supplierTaxId}</span></span>}
                                        {detail.acquisition.supplierPortalCode && <span>Cód. Portal: <span style={{ fontFamily: 'monospace' }}>{detail.acquisition.supplierPortalCode}</span></span>}
                                    </div>
                                )}
                            </div>

                            <InfoRow label="Nº P.O" value={detail.acquisition.purchaseOrderNumber} />
                            <InfoRow label="Nº Documento" value={detail.acquisition.invoiceNumber} />
                            {detail.acquisition.purchaseAmount != null && (
                                <InfoRow label="Valor" value={`${detail.acquisition.purchaseAmount.toLocaleString('pt-PT', { minimumFractionDigits: 2 })} ${detail.acquisition.currency || 'AOA'}`} />
                            )}
                            <InfoRow label="Ref. Pagamento" value={detail.acquisition.paymentReference} />
                        </div>
                    )}

                    {/* Warranty sub-section */}
                    <div style={{ marginTop: 10, paddingTop: 8, borderTop: '1px solid var(--color-border)' }}>
                        <div style={{ fontSize: '0.78rem', fontWeight: 600, color: 'var(--color-text)', marginBottom: 4 }}>🛡️ Garantia</div>
                        {detail.acquisition.warrantyInfoUnavailable ? (
                            <div style={{ fontSize: '0.82rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                Informações de garantia indisponíveis{detail.acquisition.warrantyInfoUnavailableReason ? `: ${detail.acquisition.warrantyInfoUnavailableReason}` : ''}
                            </div>
                        ) : (
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 16px' }}>
                                {detail.acquisition.warrantyMonths != null && (
                                    <InfoRow label="Duração" value={`${detail.acquisition.warrantyMonths} meses`} />
                                )}
                                {detail.acquisition.warrantyStartDate && (
                                    <InfoRow label="Início" value={new Date(detail.acquisition.warrantyStartDate).toLocaleDateString('pt-PT')} />
                                )}
                                {detail.acquisition.warrantyEndDate && (
                                    <InfoRow label="Fim" value={new Date(detail.acquisition.warrantyEndDate).toLocaleDateString('pt-PT')} />
                                )}
                                {detail.acquisition.warrantyNotes && (
                                    <InfoRow label="Notas" value={detail.acquisition.warrantyNotes} />
                                )}
                            </div>
                        )}
                    </div>

                    {detail.acquisition.acquisitionNotes && (
                        <p style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: 8, fontStyle: 'italic' }}>
                            {detail.acquisition.acquisitionNotes}
                        </p>
                    )}
                </Section>
            )}

            {/* Documents */}
            {detail.documents && detail.documents.length > 0 && (
                <Section title={`Documentos (${detail.documents.length})`} icon={<FileText size={15} />}>
                    {detail.documents.map(doc => (
                        <div key={doc.id} style={{
                            display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                            padding: '6px 0', borderBottom: '1px solid var(--color-border)',
                            fontSize: '0.82rem'
                        }}>
                            <div>
                                <span style={{ fontWeight: 500, color: 'var(--color-text)' }}>{doc.fileName}</span>
                                <span style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', marginLeft: 8 }}>
                                    {DOCUMENT_TYPE_LABELS[doc.documentType] || doc.documentType}
                                </span>
                            </div>
                            <button
                                onClick={async () => {
                                    const blob = await itEquipmentApi.documents.download(detail.id, doc.id);
                                    const url = URL.createObjectURL(blob);
                                    const a = document.createElement('a');
                                    a.href = url; a.download = doc.fileName; a.click();
                                    URL.revokeObjectURL(url);
                                }}
                                style={{
                                    background: 'none', border: 'none', cursor: 'pointer', color: '#3b82f6',
                                    display: 'flex', alignItems: 'center', gap: 4, fontSize: '0.78rem'
                                }}
                            >
                                <Download size={13} /> Baixar
                            </button>
                        </div>
                    ))}
                </Section>
            )}

            {/* Notes */}
            {detail.notes && (
                <Section title="Notas" icon={<FileText size={15} />}>
                    <p style={{ fontSize: '0.85rem', color: 'var(--color-text)', whiteSpace: 'pre-wrap', margin: 0 }}>
                        {detail.notes}
                    </p>
                </Section>
            )}

            {/* Audit */}
            <Section title="Auditoria" icon={<Clock size={15} />}>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 16px' }}>
                    <InfoRow label="Criado em" value={detail.createdAt ? new Date(detail.createdAt).toLocaleString('pt-PT') : null} />
                    <InfoRow label="Criado por" value={detail.createdByName} />
                    <InfoRow label="Atualizado em" value={detail.updatedAt ? new Date(detail.updatedAt).toLocaleString('pt-PT') : null} />
                    <InfoRow label="Atualizado por" value={detail.updatedByName} />
                </div>
            </Section>
        </div>
    );
}

function AssignmentsTab({ assignments, equipmentId, documents, onRefresh }: {
    assignments: ITEquipmentDetail['assignments'];
    equipmentId: string;
    documents: ITEquipmentDetail['documents'];
    onRefresh: () => void;
}) {
    const [uploading, setUploading] = useState<string | null>(null);
    const [uploadError, setUploadError] = useState<string | null>(null);

    if (!assignments || assignments.length === 0) {
        return <EmptyState text="Nenhuma atribuição registada." />;
    }

    const getSignedDoc = (assignmentId: string) => {
        return documents?.find(d =>
            d.assignmentId === assignmentId &&
            (d.documentType === 'SIGNED_ASSIGNMENT_AGREEMENT' || d.documentType === 'SIGNED_RETURN_AGREEMENT')
        );
    };

    const getGeneratedDoc = (assignmentId: string) => {
        return documents?.find(d =>
            d.assignmentId === assignmentId &&
            (d.documentType === 'ASSIGNMENT_AGREEMENT' || d.documentType === 'RETURN_AGREEMENT')
        );
    };

    const handleUploadSignedTerm = async (assignmentId: string, assignmentStatus: string) => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.pdf,.jpg,.jpeg,.png';
        input.onchange = async (e) => {
            const file = (e.target as HTMLInputElement).files?.[0];
            if (!file) return;

            setUploading(assignmentId);
            setUploadError(null);
            try {
                const docType = assignmentStatus === 'RETURNED'
                    ? 'SIGNED_RETURN_AGREEMENT'
                    : 'SIGNED_ASSIGNMENT_AGREEMENT';
                await itEquipmentApi.documents.upload(
                    equipmentId, file, docType, 'Termo assinado pelo utilizador', undefined, assignmentId
                );
                onRefresh();
            } catch (err: any) {
                setUploadError(err?.message || 'Falha ao carregar o termo assinado.');
            } finally {
                setUploading(null);
            }
        };
        input.click();
    };

    const handleDownloadSignedTerm = async (docId: string, fileName: string) => {
        try {
            const blob = await itEquipmentApi.documents.download(equipmentId, docId);
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = fileName; a.click();
            URL.revokeObjectURL(url);
        } catch { }
    };

    const handleReplaceSignedTerm = async (existingDocId: string, assignmentId: string, assignmentStatus: string) => {
        // Delete old, then upload new
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.pdf,.jpg,.jpeg,.png';
        input.onchange = async (e) => {
            const file = (e.target as HTMLInputElement).files?.[0];
            if (!file) return;

            setUploading(assignmentId);
            setUploadError(null);
            try {
                await itEquipmentApi.documents.delete(equipmentId, existingDocId);
                const docType = assignmentStatus === 'RETURNED'
                    ? 'SIGNED_RETURN_AGREEMENT'
                    : 'SIGNED_ASSIGNMENT_AGREEMENT';
                await itEquipmentApi.documents.upload(
                    equipmentId, file, docType, 'Termo assinado pelo utilizador (substituído)', undefined, assignmentId
                );
                onRefresh();
            } catch (err: any) {
                setUploadError(err?.message || 'Falha ao substituir o termo assinado.');
            } finally {
                setUploading(null);
            }
        };
        input.click();
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {uploadError && (
                <div style={{
                    padding: '8px 12px', borderRadius: 6, fontSize: '0.8rem',
                    backgroundColor: '#fef2f2', color: '#ef4444', border: '1px solid #fecaca'
                }}>
                    {uploadError}
                </div>
            )}
            {assignments.map(a => {
                const cfg = ASSIGNMENT_STATUS_CONFIG[a.assignmentStatus] || ASSIGNMENT_STATUS_CONFIG['ACTIVE'];
                const signedDoc = getSignedDoc(a.id);
                const generatedDoc = getGeneratedDoc(a.id);
                const isUploading = uploading === a.id;

                return (
                    <div key={a.id} style={{
                        padding: 12, border: '1px solid var(--color-border)', borderRadius: 8,
                        backgroundColor: 'var(--color-bg-surface)'
                    }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                            <span style={{ fontWeight: 600, fontSize: '0.88rem', color: 'var(--color-text)' }}>{a.assignedToName}</span>
                            <StatusBadge 
                                status={a.assignmentStatus} 
                                label={cfg.label} 
                                variant={a.assignmentStatus === 'ACTIVE' ? 'blue' : a.assignmentStatus === 'RETURNED' ? 'green' : 'gray'} 
                            />
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '2px 12px', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                            <span>Atribuído: {new Date(a.assignedDate).toLocaleDateString('pt-PT')}</span>
                            {a.returnedDate && <span>Devolvido: {new Date(a.returnedDate).toLocaleDateString('pt-PT')}</span>}
                            {a.assignedToDepartment && <span>Depto: {a.assignedToDepartment}</span>}
                            {a.assignedToPlant && <span>Planta: {a.assignedToPlant}</span>}
                        </div>
                        {a.notes && <p style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 6, fontStyle: 'italic' }}>{a.notes}</p>}

                        {/* ── Document actions section ── */}
                        <div style={{
                            marginTop: 10, paddingTop: 8, borderTop: '1px solid var(--color-border)',
                            display: 'flex', flexDirection: 'column', gap: 6
                        }}>
                            {/* Generated term */}
                            {generatedDoc && (
                                <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: '0.78rem' }}>
                                    <FileText size={13} style={{ color: '#3b82f6', flexShrink: 0 }} />
                                    <span style={{ color: 'var(--color-text-muted)' }}>Termo gerado:</span>
                                    <button
                                        onClick={() => handleDownloadSignedTerm(generatedDoc.id, generatedDoc.fileName)}
                                        style={{
                                            background: 'none', border: 'none', cursor: 'pointer',
                                            color: '#3b82f6', fontSize: '0.78rem', padding: 0,
                                            display: 'flex', alignItems: 'center', gap: 3
                                        }}
                                    >
                                        <Download size={12} /> Baixar
                                    </button>
                                </div>
                            )}

                            {/* Signed term status + actions */}
                            {signedDoc ? (
                                <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: '0.78rem' }}>
                                    <FileCheck size={13} style={{ color: '#10b981', flexShrink: 0 }} />
                                    <span style={{ color: '#10b981', fontWeight: 600 }}>Termo assinado:</span>
                                    <span style={{ color: 'var(--color-text-muted)', fontSize: '0.72rem' }}>
                                        {signedDoc.fileName}
                                        {signedDoc.uploadedByName && ` • por ${signedDoc.uploadedByName}`}
                                    </span>
                                    <div style={{ marginLeft: 'auto', display: 'flex', gap: 6 }}>
                                        <button
                                            onClick={() => handleDownloadSignedTerm(signedDoc.id, signedDoc.fileName)}
                                            style={{
                                                background: 'none', border: 'none', cursor: 'pointer',
                                                color: '#3b82f6', fontSize: '0.75rem', padding: 0,
                                                display: 'flex', alignItems: 'center', gap: 2
                                            }}
                                        >
                                            <Download size={11} /> Ver
                                        </button>
                                        <button
                                            onClick={() => handleReplaceSignedTerm(signedDoc.id, a.id, a.assignmentStatus)}
                                            disabled={isUploading}
                                            style={{
                                                background: 'none', border: 'none', cursor: 'pointer',
                                                color: '#f59e0b', fontSize: '0.75rem', padding: 0,
                                                display: 'flex', alignItems: 'center', gap: 2,
                                                opacity: isUploading ? 0.5 : 1
                                            }}
                                        >
                                            <Upload size={11} /> Substituir
                                        </button>
                                    </div>
                                </div>
                            ) : (
                                <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: '0.78rem' }}>
                                    <FileX size={13} style={{ color: '#f97316', flexShrink: 0 }} />
                                    <span style={{ color: '#f97316' }}>Termo assinado: pendente</span>
                                    <button
                                        onClick={() => handleUploadSignedTerm(a.id, a.assignmentStatus)}
                                        disabled={isUploading}
                                        style={{
                                            marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 4,
                                            padding: '3px 10px', borderRadius: 5,
                                            border: '1px solid #3b82f630', background: '#3b82f608',
                                            color: '#3b82f6', cursor: 'pointer', fontSize: '0.75rem',
                                            fontWeight: 600, opacity: isUploading ? 0.5 : 1
                                        }}
                                    >
                                        {isUploading ? (
                                            <><Loader2 size={11} style={{ animation: 'spin 1s linear infinite' }} /> Carregando...</>
                                        ) : (
                                            <><Upload size={11} /> Carregar termo assinado</>
                                        )}
                                    </button>
                                </div>
                            )}
                        </div>
                    </div>
                );
            })}
        </div>
    );
}

function MovementsTab({ movements }: { movements: ITEquipmentDetail['movements'] }) {
    if (!movements || movements.length === 0) {
        return <EmptyState text="Nenhuma movimentação registada." />;
    }

    // Color-coded movement type styling
    const getMovementStyle = (type: string): { color: string; bg: string } => {
        switch (type) {
            case 'CREATED': case 'IMPORTED': return { color: '#10b981', bg: 'rgba(16,185,129,0.1)' };
            case 'ASSIGNED': case 'USER_CHANGE_ASSIGNED': return { color: '#3b82f6', bg: 'rgba(59,130,246,0.1)' };
            case 'RETURNED': case 'USER_CHANGE_RETURNED': return { color: '#8b5cf6', bg: 'rgba(139,92,246,0.1)' };
            case 'SENT_TO_REPAIR': return { color: '#f97316', bg: 'rgba(249,115,22,0.1)' };
            case 'RETURNED_FROM_REPAIR': return { color: '#14b8a6', bg: 'rgba(20,184,166,0.1)' };
            case 'MARKED_AS_LOST': return { color: '#ef4444', bg: 'rgba(239,68,68,0.1)' };
            case 'RESERVED': case 'RELEASED_FROM_RESERVATION': return { color: '#f59e0b', bg: 'rgba(245,158,11,0.1)' };
            case 'RETIRED': return { color: '#6b7280', bg: 'rgba(107,114,128,0.1)' };
            case 'REACTIVATED': return { color: '#22c55e', bg: 'rgba(34,197,94,0.1)' };
            case 'UPDATED': case 'PHOTO_UPDATED': case 'NOTES_UPDATED': return { color: '#6366f1', bg: 'rgba(99,102,241,0.1)' };
            case 'AGREEMENT_GENERATED': case 'RETURN_DOCUMENT_GENERATED': case 'SIGNED_TERM_UPLOADED': return { color: '#0ea5e9', bg: 'rgba(14,165,233,0.1)' };
            case 'EMAIL_SENT': case 'RETURN_EMAIL_SENT': return { color: '#10b981', bg: 'rgba(16,185,129,0.08)' };
            case 'EMAIL_FAILED': case 'RETURN_EMAIL_FAILED': return { color: '#ef4444', bg: 'rgba(239,68,68,0.08)' };
            case 'USER_CHANGED': return { color: '#14b8a6', bg: 'rgba(20,184,166,0.1)' };
            default: return { color: '#3b82f6', bg: 'rgba(59,130,246,0.1)' };
        }
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 0 }}>
            {movements.map((m, i) => {
                const style = getMovementStyle(m.movementType);
                return (
                    <div key={m.id} style={{
                        display: 'flex', gap: 12, padding: '10px 0',
                        borderBottom: i < movements.length - 1 ? '1px solid var(--color-border)' : 'none'
                    }}>
                        <div style={{
                            width: 28, height: 28, borderRadius: '50%', flexShrink: 0,
                            backgroundColor: style.bg, display: 'flex',
                            alignItems: 'center', justifyContent: 'center', marginTop: 2
                        }}>
                            <Clock size={13} style={{ color: style.color }} />
                        </div>
                        <div style={{ flex: 1 }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
                                <span style={{ fontWeight: 600, fontSize: '0.82rem', color: style.color }}>
                                    {MOVEMENT_TYPE_LABELS[m.movementType] || m.movementType}
                                </span>
                                <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                    {new Date(m.createdAt).toLocaleString('pt-PT', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' })}
                                </span>
                            </div>
                            {(m.previousStatus || m.newStatus) && (
                                <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 4, display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                                    {m.previousStatus && <StatusBadge status={m.previousStatus} label={EQUIPMENT_STATUS_CONFIG[m.previousStatus]?.label || m.previousStatus} />}
                                    {m.previousStatus && m.newStatus && <span>→</span>}
                                    {m.newStatus && <StatusBadge status={m.newStatus} label={EQUIPMENT_STATUS_CONFIG[m.newStatus]?.label || m.newStatus} />}
                                </div>
                            )}
                            {m.notes && <p style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 3, whiteSpace: 'pre-wrap' }}>{m.notes}</p>}
                            {m.createdByName && <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', opacity: 0.7 }}>por {m.createdByName}</span>}
                        </div>
                    </div>
                );
            })}
        </div>
    );
}

function Section({ title, icon, children }: { title: string; icon: React.ReactNode; children: React.ReactNode }) {
    return (
        <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 8 }}>
                <span style={{ color: 'var(--color-primary)' }}>{icon}</span>
                <h3 style={{ margin: 0, fontSize: '0.85rem', fontWeight: 700, color: 'var(--color-text)', textTransform: 'uppercase', letterSpacing: '0.03em' }}>{title}</h3>
            </div>
            {children}
        </div>
    );
}

function InfoRow({ label, value, mono }: { label: string; value?: string | null; mono?: boolean }) {
    return (
        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '3px 0', fontSize: '0.82rem' }}>
            <span style={{ color: 'var(--color-text-muted)' }}>{label}</span>
            <span style={{ color: value ? 'var(--color-text)' : 'var(--color-text-muted)', fontWeight: value ? 500 : 400, fontFamily: mono ? 'monospace' : 'inherit', opacity: value ? 1 : 0.5 }}>
                {value || '—'}
            </span>
        </div>
    );
}

function EmptyState({ text }: { text: string }) {
    return (
        <div style={{ textAlign: 'center', padding: 40, color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
            {text}
        </div>
    );
}
