import React, { useState, useEffect } from 'react';
import { X, Edit3, UserPlus, RotateCcw, Wrench, AlertTriangle, BookmarkCheck, Archive, Loader2, Download, FileText, Clock, User, Cpu, RefreshCw } from 'lucide-react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { EQUIPMENT_STATUS_CONFIG, EQUIPMENT_TYPE_CONFIG, MOVEMENT_TYPE_LABELS, ASSIGNMENT_STATUS_CONFIG, DOCUMENT_TYPE_LABELS } from '../../types/itEquipment';
import type { ITEquipmentDetail } from '../../types/itEquipment';
import { AssignEquipmentModal } from './AssignEquipmentModal';
import { ReturnEquipmentModal } from './ReturnEquipmentModal';
import { RepairEquipmentModal } from './RepairEquipmentModal';
import { LostEquipmentModal } from './LostEquipmentModal';
import { RetireEquipmentModal } from './RetireEquipmentModal';
import { ReserveEquipmentModal } from './ReserveEquipmentModal';
import { ChangeEquipmentUserModal } from './ChangeEquipmentUserModal';
import { EquipmentFormModal } from './EquipmentFormModal';

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
    const canAssign = detail && !['LOST', 'RETIRED', 'DISPOSED'].includes(detail.statusCode);
    const canReturn = detail && detail.statusCode === 'IN_USE';
    const canRepair = detail && !['LOST', 'RETIRED', 'DISPOSED'].includes(detail.statusCode);
    const canReserve = detail && detail.statusCode === 'AVAILABLE';
    const canRetire = detail && !['RETIRED', 'DISPOSED'].includes(detail.statusCode);

    return (
        <>
            {/* Backdrop */}
            <div
                onClick={onClose}
                style={{
                    position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.4)',
                    zIndex: 1400, transition: 'opacity 0.3s'
                }}
            />
            {/* Drawer */}
            <div style={{
                position: 'fixed', top: 0, right: 0, bottom: 0, width: 620,
                backgroundColor: 'var(--color-bg-surface)', borderLeft: '1px solid var(--color-border)',
                zIndex: 1401, display: 'flex', flexDirection: 'column',
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
                        <h2 style={{ margin: 0, fontSize: '1.1rem', fontWeight: 700, color: 'var(--color-text)' }}>
                            {detail?.assetTag || 'Carregando...'}
                        </h2>
                        {detail && statusCfg && (
                            <span style={{
                                display: 'inline-flex', alignItems: 'center', padding: '2px 10px',
                                borderRadius: 20, fontSize: '0.75rem', fontWeight: 600,
                                color: statusCfg.color, backgroundColor: statusCfg.bgColor,
                                marginTop: 4
                            }}>
                                {statusCfg.label}
                            </span>
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
                        flexWrap: 'wrap'
                    }}>
                        <ActionBtn label="Editar" icon={<Edit3 size={13} />} onClick={() => setActiveModal('edit')} />
                        {canAssign && <ActionBtn label="Atribuir" icon={<UserPlus size={13} />} onClick={() => setActiveModal('assign')} color="#3b82f6" />}
                        {canReturn && <ActionBtn label="Devolver" icon={<RotateCcw size={13} />} onClick={() => setActiveModal('return')} color="#8b5cf6" />}
                        {canReturn && <ActionBtn label="Trocar Utilizador" icon={<RefreshCw size={13} />} onClick={() => setActiveModal('change-user')} color="#14b8a6" />}
                        {canRepair && <ActionBtn label="Conserto" icon={<Wrench size={13} />} onClick={() => setActiveModal('repair')} color="#f97316" />}
                        <ActionBtn label="Perdido" icon={<AlertTriangle size={13} />} onClick={() => setActiveModal('lost')} color="#ef4444" />
                        {canReserve && <ActionBtn label="Reservar" icon={<BookmarkCheck size={13} />} onClick={() => setActiveModal('reserve')} color="#f59e0b" />}
                        {canRetire && <ActionBtn label="Baixar" icon={<Archive size={13} />} onClick={() => setActiveModal('retire')} color="#6b7280" />}
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
                        <AssignmentsTab assignments={detail.assignments} />
                    ) : detail && activeTab === 'movements' ? (
                        <MovementsTab movements={detail.movements} />
                    ) : null}
                </div>
            </div>

            {/* Modals */}
            {activeModal === 'edit' && detail && <EquipmentFormModal equipment={detail} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'assign' && detail && <AssignEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'return' && detail && <ReturnEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'repair' && detail && <RepairEquipmentModal equipmentId={detail.id} statusCode={detail.statusCode} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'lost' && detail && <LostEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'reserve' && detail && <ReserveEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'retire' && detail && <RetireEquipmentModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
            {activeModal === 'change-user' && detail && <ChangeEquipmentUserModal equipmentId={detail.id} onClose={handleModalClose} onSuccess={handleModalSuccess} />}
        </>
    );
}

function ActionBtn({ label, icon, onClick, color }: { label: string; icon: React.ReactNode; onClick: () => void; color?: string }) {
    return (
        <button
            onClick={onClick}
            style={{
                display: 'flex', alignItems: 'center', gap: 4, padding: '5px 10px',
                border: `1px solid ${color || 'var(--color-border)'}30`,
                borderRadius: 6, background: `${color || 'var(--color-text)'}08`,
                color: color || 'var(--color-text)', cursor: 'pointer',
                fontSize: '0.78rem', fontWeight: 600, transition: 'all 0.15s'
            }}
            onMouseOver={(e) => { e.currentTarget.style.backgroundColor = `${color || '#888'}18`; }}
            onMouseOut={(e) => { e.currentTarget.style.backgroundColor = `${color || '#888'}08`; }}
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
                    <InfoRow label="Fabricante" value={detail.manufacturer} />
                    <InfoRow label="Modelo" value={detail.model} />
                    <InfoRow label="Serial Number" value={detail.serialNumber} mono />
                    <InfoRow label="MAC Address" value={detail.macAddress} mono />
                    <InfoRow label="Processador" value={detail.processor} />
                    <InfoRow label="RAM" value={detail.memoryRam} />
                    <InfoRow label="Cor" value={detail.color} />
                    <InfoRow label="Biometric/MFA" value={detail.biometricMfaEnabled ? 'Sim' : 'Não'} />
                    <InfoRow label="ID Card" value={detail.idCard} />
                    <InfoRow label="Origem" value={detail.sourceType === 'IMPORTED_LEGACY' ? 'Importado (Legacy)' : detail.sourceType === 'MANUAL_PURCHASE' ? 'Compra' : 'Registo Manual'} />
                </div>
            </Section>

            {/* Acquisition */}
            {detail.acquisition && (
                <Section title="Aquisição / Compra" icon={<FileText size={15} />}>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 16px' }}>
                        <InfoRow label="Data" value={detail.acquisition.acquisitionDate ? new Date(detail.acquisition.acquisitionDate).toLocaleDateString('pt-PT') : null} />
                        <InfoRow label="Fornecedor" value={detail.acquisition.supplierName} />
                        <InfoRow label="Nº P.O" value={detail.acquisition.purchaseOrderNumber} />
                        <InfoRow label="Nº Fatura" value={detail.acquisition.invoiceNumber} />
                        {detail.acquisition.purchaseAmount != null && (
                            <InfoRow label="Valor" value={`${detail.acquisition.purchaseAmount.toLocaleString('pt-PT', { minimumFractionDigits: 2 })} ${detail.acquisition.currency || 'AOA'}`} />
                        )}
                        <InfoRow label="Ref. Pagamento" value={detail.acquisition.paymentReference} />
                        {detail.acquisition.warrantyEndDate && (
                            <InfoRow label="Garantia até" value={new Date(detail.acquisition.warrantyEndDate).toLocaleDateString('pt-PT')} />
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

function AssignmentsTab({ assignments }: { assignments: ITEquipmentDetail['assignments'] }) {
    if (!assignments || assignments.length === 0) {
        return <EmptyState text="Nenhuma atribuição registada." />;
    }
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {assignments.map(a => {
                const cfg = ASSIGNMENT_STATUS_CONFIG[a.assignmentStatus] || ASSIGNMENT_STATUS_CONFIG['ACTIVE'];
                return (
                    <div key={a.id} style={{
                        padding: 12, border: '1px solid var(--color-border)', borderRadius: 8,
                        backgroundColor: 'var(--color-bg-surface)'
                    }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                            <span style={{ fontWeight: 600, fontSize: '0.88rem', color: 'var(--color-text)' }}>{a.assignedToName}</span>
                            <span style={{
                                padding: '2px 8px', borderRadius: 12, fontSize: '0.72rem', fontWeight: 600,
                                color: cfg.color, backgroundColor: cfg.bgColor
                            }}>
                                {cfg.label}
                            </span>
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '2px 12px', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                            <span>Atribuído: {new Date(a.assignedDate).toLocaleDateString('pt-PT')}</span>
                            {a.returnedDate && <span>Devolvido: {new Date(a.returnedDate).toLocaleDateString('pt-PT')}</span>}
                            {a.assignedToDepartment && <span>Depto: {a.assignedToDepartment}</span>}
                            {a.assignedToPlant && <span>Planta: {a.assignedToPlant}</span>}
                        </div>
                        {a.notes && <p style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 6, fontStyle: 'italic' }}>{a.notes}</p>}
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
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 0 }}>
            {movements.map((m, i) => (
                <div key={m.id} style={{
                    display: 'flex', gap: 12, padding: '10px 0',
                    borderBottom: i < movements.length - 1 ? '1px solid var(--color-border)' : 'none'
                }}>
                    <div style={{
                        width: 28, height: 28, borderRadius: '50%', flexShrink: 0,
                        backgroundColor: 'rgba(59,130,246,0.1)', display: 'flex',
                        alignItems: 'center', justifyContent: 'center', marginTop: 2
                    }}>
                        <Clock size={13} style={{ color: '#3b82f6' }} />
                    </div>
                    <div style={{ flex: 1 }}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
                            <span style={{ fontWeight: 600, fontSize: '0.82rem', color: 'var(--color-text)' }}>
                                {MOVEMENT_TYPE_LABELS[m.movementType] || m.movementType}
                            </span>
                            <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                {new Date(m.createdAt).toLocaleString('pt-PT', { day: '2-digit', month: '2-digit', year: '2-digit', hour: '2-digit', minute: '2-digit' })}
                            </span>
                        </div>
                        {(m.previousStatus || m.newStatus) && (
                            <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 2 }}>
                                {m.previousStatus && <span>{EQUIPMENT_STATUS_CONFIG[m.previousStatus]?.label || m.previousStatus}</span>}
                                {m.previousStatus && m.newStatus && ' → '}
                                {m.newStatus && <span style={{ fontWeight: 500 }}>{EQUIPMENT_STATUS_CONFIG[m.newStatus]?.label || m.newStatus}</span>}
                            </div>
                        )}
                        {m.notes && <p style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 3, whiteSpace: 'pre-wrap' }}>{m.notes}</p>}
                        {m.createdByName && <span style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', opacity: 0.7 }}>por {m.createdByName}</span>}
                    </div>
                </div>
            ))}
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
