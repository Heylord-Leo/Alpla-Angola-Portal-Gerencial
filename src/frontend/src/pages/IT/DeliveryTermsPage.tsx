import { useState, useEffect, useCallback } from 'react';
import { ClipboardList, Plus, Search, ChevronLeft, ChevronRight, RefreshCw, X, Download, Send, Upload, Undo2, FileText, Trash2 } from 'lucide-react';
import { deliveryTermsApi, itEquipmentApi } from '../../lib/itEquipmentApi';
import { api } from '../../lib/api';
import type {
    ITDeliveryTermListResponse, ITDeliveryTermDetail,
    ITDeliveryItemDetail, MasterDataCompany, MasterDataPlant, MasterDataDepartment,
    ITEquipmentListItem
} from '../../types/itEquipment';
import { DELIVERY_TERM_STATUS_CONFIG, DELIVERY_ITEM_STATUS_CONFIG, RETURN_CONDITION_CONFIG } from '../../types/itEquipment';

export default function DeliveryTermsPage() {
    const [listData, setListData] = useState<ITDeliveryTermListResponse | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [actionLoading, setActionLoading] = useState(false);
    const [toast, setToast] = useState<{ message: string; type: 'success' | 'error' } | null>(null);

    // Filters
    const [search, setSearch] = useState('');
    const [statusFilter, setStatusFilter] = useState('');
    const [page, setPage] = useState(1);
    const pageSize = 20;

    // Modals & Drawer
    const [showCreateModal, setShowCreateModal] = useState(false);
    const [selectedTermId, setSelectedTermId] = useState<string | null>(null);
    const [termDetail, setTermDetail] = useState<ITDeliveryTermDetail | null>(null);
    const [drawerLoading, setDrawerLoading] = useState(false);

    // Return modal
    const [returnItemId, setReturnItemId] = useState<string | null>(null);
    const [returnCondition, setReturnCondition] = useState('GOOD');
    const [returnNotes, setReturnNotes] = useState('');

    // ── Data Loading ──

    const loadList = useCallback(async () => {
        try {
            setLoading(true);
            setError(null);
            const data = await deliveryTermsApi.list({
                search: search || undefined,
                status: statusFilter || undefined,
                page,
                pageSize
            });
            setListData(data);
        } catch (err: any) {
            setError(err.message || 'Erro ao carregar termos de entrega.');
        } finally {
            setLoading(false);
        }
    }, [search, statusFilter, page, pageSize]);

    useEffect(() => { loadList(); }, [loadList]);

    const loadTermDetail = useCallback(async (id: string) => {
        try {
            setDrawerLoading(true);
            const data = await deliveryTermsApi.getById(id);
            setTermDetail(data);
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao carregar detalhes.';
            showToast(message, 'error');
        } finally {
            setDrawerLoading(false);
        }
    }, []);

    const openDetail = (id: string) => {
        setSelectedTermId(id);
        loadTermDetail(id);
    };

    const closeDetail = () => {
        setSelectedTermId(null);
        setTermDetail(null);
    };

    // ── Toast ──

    const showToast = (message: string, type: 'success' | 'error') => {
        setToast({ message, type });
        setTimeout(() => setToast(null), 4000);
    };

    // ── Actions ──

    const handleGenerate = async () => {
        if (!termDetail || !confirm('Tem certeza que deseja CONFIRMAR A ENTREGA e gerar o PDF? Esta ação irá atribuir todos os equipamentos ao funcionário.')) return;
        try {
            setActionLoading(true);
            const result = await deliveryTermsApi.generate(termDetail.id);
            showToast(result.detail, 'success');
            loadTermDetail(termDetail.id);
            loadList();
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao confirmar.';
            showToast(message, 'error');
        } finally {
            setActionLoading(false);
        }
    };

    const handleSend = async () => {
        if (!termDetail) return;
        try {
            setActionLoading(true);
            const result = await deliveryTermsApi.send(termDetail.id);
            showToast(result.detail, 'success');
            loadTermDetail(termDetail.id);
            loadList();
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao enviar.';
            showToast(message, 'error');
        } finally {
            setActionLoading(false);
        }
    };

    const handleUploadSigned = async (e: React.ChangeEvent<HTMLInputElement>) => {
        if (!termDetail || !e.target.files?.[0]) return;
        try {
            setActionLoading(true);
            const result = await deliveryTermsApi.uploadSigned(termDetail.id, e.target.files[0]);
            showToast(result.detail, 'success');
            loadTermDetail(termDetail.id);
            loadList();
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao carregar documento.';
            showToast(message, 'error');
        } finally {
            setActionLoading(false);
        }
    };

    const handleUploadSignedReturn = async (e: React.ChangeEvent<HTMLInputElement>) => {
        if (!termDetail || !e.target.files?.[0]) return;
        try {
            setActionLoading(true);
            const result = await deliveryTermsApi.uploadSignedReturn(termDetail.id, e.target.files[0]);
            showToast(result.detail, 'success');
            loadTermDetail(termDetail.id);
            loadList();
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao carregar documento de devolução.';
            showToast(message, 'error');
        } finally {
            setActionLoading(false);
        }
    };

    const handleCancel = async () => {
        if (!termDetail || !confirm('Tem certeza que deseja cancelar este termo de entrega?')) return;
        try {
            setActionLoading(true);
            const result = await deliveryTermsApi.cancel(termDetail.id);
            showToast(result.detail, 'success');
            closeDetail();
            loadList();
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao cancelar.';
            showToast(message, 'error');
        } finally {
            setActionLoading(false);
        }
    };

    const handleReturnItem = async () => {
        if (!termDetail || !returnItemId) return;
        try {
            setActionLoading(true);
            const result = await deliveryTermsApi.returnItem(termDetail.id, returnItemId, {
                returnCondition,
                notes: returnNotes || undefined
            });
            showToast(result.detail, 'success');
            setReturnItemId(null);
            setReturnCondition('GOOD');
            setReturnNotes('');
            loadTermDetail(termDetail.id);
            loadList();
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao devolver item.';
            showToast(message, 'error');
        } finally {
            setActionLoading(false);
        }
    };

    const handleRemoveItem = async (itemId: string) => {
        if (!termDetail || !confirm('Remover este equipamento do termo?')) return;
        try {
            setActionLoading(true);
            await deliveryTermsApi.removeItem(termDetail.id, itemId);
            showToast('Item removido.', 'success');
            loadTermDetail(termDetail.id);
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao remover item.';
            showToast(message, 'error');
        } finally {
            setActionLoading(false);
        }
    };

    // ── Pagination ──

    const totalPages = listData ? Math.ceil(listData.totalCount / pageSize) : 1;

    // ── Render ──

    return (
        <div className="it-delivery-terms-page" style={{ padding: '24px 32px', maxWidth: '1400px', margin: '0 auto' }}>
            {/* Toast */}
            {toast && (
                <div style={{
                    position: 'fixed', top: 20, right: 20, zIndex: 9999,
                    padding: '12px 20px', borderRadius: 8, color: '#fff', fontWeight: 500, fontSize: 14,
                    background: toast.type === 'success' ? '#10b981' : '#ef4444',
                    boxShadow: '0 4px 16px rgba(0,0,0,0.2)', animation: 'fadeIn 0.2s ease'
                }}>
                    {toast.message}
                    <button onClick={() => setToast(null)} style={{ marginLeft: 12, background: 'none', border: 'none', color: '#fff', cursor: 'pointer' }}>✕</button>
                </div>
            )}

            {/* Page Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                    <ClipboardList size={28} strokeWidth={2} style={{ color: '#3b82f6' }} />
                    <div>
                        <h1 style={{ margin: 0, fontSize: 24, fontWeight: 700, color: '#111827' }}>Termos de Entrega</h1>
                        <p style={{ margin: 0, fontSize: 13, color: '#6b7280' }}>
                            Gestão de termos de entrega agrupados de equipamento de T.I.
                        </p>
                    </div>
                </div>
                <div style={{ display: 'flex', gap: 8 }}>
                    <button onClick={loadList} style={btnSecondaryStyle}>
                        <RefreshCw size={16} /> Atualizar
                    </button>
                    <button onClick={() => setShowCreateModal(true)} style={btnPrimaryStyle}>
                        <Plus size={16} /> Novo Termo
                    </button>
                </div>
            </div>

            {/* Filters */}
            <div style={{ display: 'flex', gap: 12, marginBottom: 20, flexWrap: 'wrap' }}>
                <div style={{ position: 'relative', flex: '1 1 280px', maxWidth: 400 }}>
                    <Search size={16} style={{ position: 'absolute', left: 12, top: 10, color: '#9ca3af' }} />
                    <input
                        type="text"
                        placeholder="Pesquisar por nº termo, nome ou e-mail..."
                        value={search}
                        onChange={e => { setSearch(e.target.value); setPage(1); }}
                        style={searchInputStyle}
                    />
                    {search && (
                        <button onClick={() => { setSearch(''); setPage(1); }} style={{ position: 'absolute', right: 8, top: 8, background: 'none', border: 'none', cursor: 'pointer', color: '#9ca3af' }}>
                            <X size={16} />
                        </button>
                    )}
                </div>
                <select
                    value={statusFilter}
                    onChange={e => { setStatusFilter(e.target.value); setPage(1); }}
                    style={selectStyle}
                >
                    <option value="">Todos os status</option>
                    {Object.entries(DELIVERY_TERM_STATUS_CONFIG).map(([code, { label }]) => (
                        <option key={code} value={code}>{label}</option>
                    ))}
                </select>
            </div>

            {/* Error */}
            {error && (
                <div style={{ padding: 16, marginBottom: 16, borderRadius: 8, background: '#fef2f2', border: '1px solid #fecaca', color: '#dc2626', fontSize: 14 }}>
                    {error}
                </div>
            )}

            {/* Table */}
            <div style={{ background: '#fff', borderRadius: 12, border: '1px solid #e5e7eb', overflow: 'hidden' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                    <thead>
                        <tr style={{ background: '#f9fafb', borderBottom: '2px solid #e5e7eb' }}>
                            <th style={thStyle}>Nº Termo</th>
                            <th style={thStyle}>Funcionário</th>
                            <th style={thStyle}>Planta</th>
                            <th style={thStyle}>Data de Entrega</th>
                            <th style={thStyle}>Status</th>
                            <th style={{ ...thStyle, textAlign: 'center' }}>Itens</th>
                            <th style={thStyle}>Criado por</th>
                            <th style={thStyle}>Data</th>
                        </tr>
                    </thead>
                    <tbody>
                        {loading ? (
                            <tr><td colSpan={8} style={{ padding: 40, textAlign: 'center', color: '#9ca3af' }}>Carregando...</td></tr>
                        ) : !listData?.items?.length ? (
                            <tr><td colSpan={8} style={{ padding: 40, textAlign: 'center', color: '#9ca3af' }}>Nenhum termo de entrega encontrado.</td></tr>
                        ) : (
                            listData.items.map(term => (
                                <tr
                                    key={term.id}
                                    onClick={() => openDetail(term.id)}
                                    style={{ borderBottom: '1px solid #f3f4f6', cursor: 'pointer', transition: 'background 0.15s' }}
                                    onMouseEnter={e => (e.currentTarget.style.background = '#f0f7ff')}
                                    onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
                                >
                                    <td style={{ ...tdStyle, fontWeight: 600, color: '#3b82f6' }}>{term.termNumber}</td>
                                    <td style={tdStyle}>
                                        <div style={{ fontWeight: 500 }}>{term.employeeName}</div>
                                        {term.employeeEmail && <div style={{ fontSize: 12, color: '#9ca3af' }}>{term.employeeEmail}</div>}
                                    </td>
                                    <td style={tdStyle}>{term.employeePlant || '—'}</td>
                                    <td style={tdStyle}>{new Date(term.deliveryDate).toLocaleDateString('pt-PT')}</td>
                                    <td style={tdStyle}>
                                        <StatusBadge status={term.status} config={DELIVERY_TERM_STATUS_CONFIG} />
                                    </td>
                                    <td style={{ ...tdStyle, textAlign: 'center' }}>
                                        <span style={{ background: '#eff6ff', color: '#3b82f6', padding: '2px 10px', borderRadius: 10, fontWeight: 600, fontSize: 13 }}>
                                            {term.itemCount}
                                        </span>
                                    </td>
                                    <td style={{ ...tdStyle, fontSize: 13, color: '#6b7280' }}>{term.createdByName || '—'}</td>
                                    <td style={{ ...tdStyle, fontSize: 13, color: '#6b7280' }}>{new Date(term.createdAt).toLocaleDateString('pt-PT')}</td>
                                </tr>
                            ))
                        )}
                    </tbody>
                </table>
            </div>

            {/* Pagination */}
            {listData && listData.totalCount > pageSize && (
                <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 12, marginTop: 16, color: '#6b7280', fontSize: 14 }}>
                    <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1} style={paginationBtnStyle}>
                        <ChevronLeft size={16} />
                    </button>
                    <span>Página {page} de {totalPages} ({listData.totalCount} termos)</span>
                    <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page >= totalPages} style={paginationBtnStyle}>
                        <ChevronRight size={16} />
                    </button>
                </div>
            )}

            {/* Create Modal */}
            {showCreateModal && (
                <CreateDeliveryTermModal
                    onClose={() => setShowCreateModal(false)}
                    onCreated={(id: string) => {
                        setShowCreateModal(false);
                        loadList();
                        openDetail(id);
                    }}
                    showToast={showToast}
                />
            )}

            {/* Detail Drawer */}
            {selectedTermId && (
                <DetailDrawer
                    detail={termDetail}
                    loading={drawerLoading}
                    actionLoading={actionLoading}
                    onClose={closeDetail}
                    onGenerate={handleGenerate}
                    onSend={handleSend}
                    onUploadSigned={handleUploadSigned}
                    onUploadSignedReturn={handleUploadSignedReturn}
                    onCancel={handleCancel}
                    onReturn={(itemId: string) => setReturnItemId(itemId)}
                    onRemoveItem={handleRemoveItem}
                    onRefresh={() => loadTermDetail(selectedTermId)}
                />
            )}

            {/* Return Modal */}
            {returnItemId && termDetail && (
                <ReturnItemModal
                    item={termDetail.items.find(i => i.id === returnItemId)!}
                    condition={returnCondition}
                    notes={returnNotes}
                    loading={actionLoading}
                    onConditionChange={setReturnCondition}
                    onNotesChange={setReturnNotes}
                    onConfirm={handleReturnItem}
                    onClose={() => { setReturnItemId(null); setReturnCondition('GOOD'); setReturnNotes(''); }}
                />
            )}
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════
//  EMBEDDED COMPONENTS
// ═══════════════════════════════════════════════════════════════

function StatusBadge({ status, config }: { status: string; config: Record<string, { label: string; color: string }> }) {
    const cfg = config[status] || { label: status, color: '#6b7280' };
    return (
        <span style={{
            display: 'inline-block', padding: '3px 10px', borderRadius: 6, fontSize: 12, fontWeight: 600,
            color: cfg.color, background: `${cfg.color}15`, border: `1px solid ${cfg.color}30`
        }}>
            {cfg.label}
        </span>
    );
}

// ─── Create Delivery Term Modal ───

function CreateDeliveryTermModal({ onClose, onCreated, showToast }: {
    onClose: () => void;
    onCreated: (id: string) => void;
    showToast: (msg: string, type: 'success' | 'error') => void;
}) {
    const [name, setName] = useState('');
    const [email, setEmail] = useState('');
    const [companyId, setCompanyId] = useState('');
    const [departmentId, setDepartmentId] = useState('');
    const [departmentName, setDepartmentName] = useState('');
    const [position, setPosition] = useState('');
    const [plantId, setPlantId] = useState('');
    const [plantName, setPlantName] = useState('');
    const [deliveryDate, setDeliveryDate] = useState(new Date().toISOString().slice(0, 10));
    const [notes, setNotes] = useState('');
    const [step, setStep] = useState<'info' | 'equipment'>('info');
    const [saving, setSaving] = useState(false);

    // Master Data lookups
    const [companies, setCompanies] = useState<MasterDataCompany[]>([]);
    const [plants, setPlants] = useState<MasterDataPlant[]>([]);
    const [departments, setDepartments] = useState<MasterDataDepartment[]>([]);

    useEffect(() => {
        api.lookups.getCompanies().then(setCompanies).catch((err: unknown) => console.error('[DeliveryTerms] Failed to load companies:', err));
        api.lookups.getDepartments().then(setDepartments).catch((err: unknown) => console.error('[DeliveryTerms] Failed to load departments:', err));
    }, []);

    // Company → Plant cascade
    useEffect(() => {
        if (companyId) {
            api.lookups.getPlants(Number(companyId)).then(setPlants).catch(() => setPlants([]));
        } else {
            setPlants([]);
        }
    }, [companyId]);

    const handleCompanyChange = (v: string) => {
        setCompanyId(v);
        setPlantId('');
        setPlantName('');
    };

    const handlePlantChange = (v: string) => {
        setPlantId(v);
        const p = plants.find(p => String(p.id) === v);
        setPlantName(p?.name || '');
    };

    const handleDepartmentChange = (v: string) => {
        setDepartmentId(v);
        const d = departments.find(d => String(d.id) === v);
        setDepartmentName(d?.name || '');
    };

    // Equipment selection
    const [equipmentSearch, setEquipmentSearch] = useState('');
    const [availableEquipment, setAvailableEquipment] = useState<ITEquipmentListItem[]>([]);
    const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
    const [eqLoading, setEqLoading] = useState(false);

    const loadAvailable = async () => {
        try {
            setEqLoading(true);
            const data = await itEquipmentApi.list({
                statusCode: 'AVAILABLE',
                search: equipmentSearch || undefined,
                pageSize: 50
            });
            setAvailableEquipment(data.items || []);
        } catch {
            showToast('Erro ao carregar equipamentos.', 'error');
        } finally {
            setEqLoading(false);
        }
    };

    useEffect(() => {
        if (step === 'equipment') loadAvailable();
    }, [step, equipmentSearch]);

    const toggleSelect = (id: string) => {
        setSelectedIds(prev => {
            const next = new Set(prev);
            next.has(id) ? next.delete(id) : next.add(id);
            return next;
        });
    };

    const handleCreate = async () => {
        if (!name.trim()) { showToast('Nome do funcionário é obrigatório.', 'error'); return; }
        if (!companyId) { showToast('Empresa é obrigatória.', 'error'); return; }
        if (!plantId) { showToast('Planta é obrigatória.', 'error'); return; }
        try {
            setSaving(true);
            const result = await deliveryTermsApi.create({
                employeeName: name.trim(),
                employeeEmail: email.trim() || undefined,
                employeeDepartment: departmentName || undefined,
                employeePosition: position.trim() || undefined,
                employeePlant: plantName || undefined,
                companyId: Number(companyId),
                employeePlantId: Number(plantId),
                employeeDepartmentId: departmentId ? Number(departmentId) : undefined,
                deliveryDate,
                notes: notes.trim() || undefined,
                equipmentIds: selectedIds.size > 0 ? Array.from(selectedIds) : undefined
            });
            showToast(`Termo ${result.termNumber} criado.`, 'success');
            onCreated(result.id);
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : 'Erro ao criar termo.';
            showToast(message, 'error');
        } finally {
            setSaving(false);
        }
    };

    return (
        <div style={overlayStyle}>
            <div style={{ ...modalStyle, maxWidth: 640, maxHeight: '85vh', overflow: 'auto' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
                    <h2 style={{ margin: 0, fontSize: 18, fontWeight: 700 }}>
                        {step === 'info' ? 'Novo Termo de Entrega' : 'Selecionar Equipamentos'}
                    </h2>
                    <button onClick={onClose} style={iconBtnStyle}><X size={18} /></button>
                </div>

                {step === 'info' && (
                    <>
                        <div style={formGridStyle}>
                            <div style={fieldStyle}>
                                <label style={labelStyle}>Nome do Funcionário *</label>
                                <input value={name} onChange={e => setName(e.target.value)} style={inputStyle} placeholder="Nome completo" />
                            </div>
                            <div style={fieldStyle}>
                                <label style={labelStyle}>E-mail</label>
                                <input value={email} onChange={e => setEmail(e.target.value)} style={inputStyle} placeholder="email@alpla.com" />
                            </div>
                            <div style={fieldStyle}>
                                <label style={labelStyle}>Empresa *</label>
                                <select value={companyId} onChange={e => handleCompanyChange(e.target.value)} style={inputStyle}>
                                    <option value="">Selecione...</option>
                                    {companies.filter(c => c.isActive).map(c => (
                                        <option key={c.id} value={c.id}>{c.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div style={fieldStyle}>
                                <label style={labelStyle}>Planta *</label>
                                <select value={plantId} onChange={e => handlePlantChange(e.target.value)} style={{...inputStyle, opacity: companyId ? 1 : 0.5}} disabled={!companyId}>
                                    <option value="">{companyId ? 'Selecione...' : 'Selecione empresa primeiro'}</option>
                                    {plants.filter(p => p.isActive).map(p => (
                                        <option key={p.id} value={p.id}>{p.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div style={fieldStyle}>
                                <label style={labelStyle}>Departamento</label>
                                <select value={departmentId} onChange={e => handleDepartmentChange(e.target.value)} style={inputStyle}>
                                    <option value="">Selecione...</option>
                                    {departments.filter(d => d.isActive).map(d => (
                                        <option key={d.id} value={d.id}>{d.name}</option>
                                    ))}
                                </select>
                            </div>
                            <div style={fieldStyle}>
                                <label style={labelStyle}>Cargo</label>
                                <input value={position} onChange={e => setPosition(e.target.value)} style={inputStyle} />
                            </div>
                            <div style={fieldStyle}>
                                <label style={labelStyle}>Data de Entrega *</label>
                                <input type="date" value={deliveryDate} onChange={e => setDeliveryDate(e.target.value)} style={inputStyle} />
                            </div>
                        </div>
                        <div style={{ ...fieldStyle, marginTop: 12 }}>
                            <label style={labelStyle}>Observações</label>
                            <textarea value={notes} onChange={e => setNotes(e.target.value)} style={{ ...inputStyle, minHeight: 60 }} />
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 }}>
                            <button onClick={onClose} style={btnSecondaryStyle}>Cancelar</button>
                            <button onClick={() => setStep('equipment')} style={btnPrimaryStyle}>Selecionar Equipamentos →</button>
                        </div>
                    </>
                )}

                {step === 'equipment' && (
                    <>
                        <div style={{ position: 'relative', marginBottom: 16 }}>
                            <Search size={16} style={{ position: 'absolute', left: 12, top: 10, color: '#9ca3af' }} />
                            <input
                                placeholder="Pesquisar equipamento disponível..."
                                value={equipmentSearch}
                                onChange={e => setEquipmentSearch(e.target.value)}
                                style={{ ...searchInputStyle, width: '100%' }}
                            />
                        </div>
                        {selectedIds.size > 0 && (
                            <div style={{ padding: '8px 12px', background: '#eff6ff', borderRadius: 8, marginBottom: 12, fontSize: 13, color: '#3b82f6', fontWeight: 500 }}>
                                {selectedIds.size} equipamento(s) selecionado(s)
                            </div>
                        )}
                        <div style={{ maxHeight: 320, overflow: 'auto', border: '1px solid #e5e7eb', borderRadius: 8 }}>
                            {eqLoading ? (
                                <div style={{ padding: 24, textAlign: 'center', color: '#9ca3af' }}>Carregando...</div>
                            ) : !availableEquipment.length ? (
                                <div style={{ padding: 24, textAlign: 'center', color: '#9ca3af' }}>Nenhum equipamento disponível encontrado.</div>
                            ) : (
                                <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                                    <thead>
                                        <tr style={{ background: '#f9fafb', borderBottom: '1px solid #e5e7eb' }}>
                                            <th style={{ ...thStyle, width: 40 }}></th>
                                            <th style={thStyle}>Asset Tag</th>
                                            <th style={thStyle}>Tipo</th>
                                            <th style={thStyle}>Hostname</th>
                                            <th style={thStyle}>S/N</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {availableEquipment.map(eq => (
                                            <tr
                                                key={eq.id}
                                                onClick={() => toggleSelect(eq.id)}
                                                style={{
                                                    borderBottom: '1px solid #f3f4f6', cursor: 'pointer',
                                                    background: selectedIds.has(eq.id) ? '#eff6ff' : 'transparent'
                                                }}
                                            >
                                                <td style={{ ...tdStyle, textAlign: 'center' }}>
                                                    <input type="checkbox" checked={selectedIds.has(eq.id)} readOnly style={{ accentColor: '#3b82f6' }} />
                                                </td>
                                                <td style={{ ...tdStyle, fontWeight: 600 }}>{eq.assetTag}</td>
                                                <td style={tdStyle}>{eq.equipmentType || '—'}</td>
                                                <td style={tdStyle}>{eq.hostname || '—'}</td>
                                                <td style={{ ...tdStyle, fontSize: 12 }}>{eq.serialNumber || '—'}</td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            )}
                        </div>
                        <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, marginTop: 20 }}>
                            <button onClick={() => setStep('info')} style={btnSecondaryStyle}>← Voltar</button>
                            <div style={{ display: 'flex', gap: 8 }}>
                                <button onClick={onClose} style={btnSecondaryStyle}>Cancelar</button>
                                <button onClick={handleCreate} disabled={saving} style={btnPrimaryStyle}>
                                    {saving ? 'Criando...' : `Criar Termo (${selectedIds.size} itens)`}
                                </button>
                            </div>
                        </div>
                    </>
                )}
            </div>
        </div>
    );
}

// ─── Detail Drawer ───

function DetailDrawer({ detail, loading, actionLoading, onClose, onGenerate, onSend, onUploadSigned, onUploadSignedReturn, onCancel, onReturn, onRemoveItem, onRefresh }: {
    detail: ITDeliveryTermDetail | null;
    loading: boolean;
    actionLoading: boolean;
    onClose: () => void;
    onGenerate: () => void;
    onSend: () => void;
    onUploadSigned: (e: React.ChangeEvent<HTMLInputElement>) => void;
    onUploadSignedReturn: (e: React.ChangeEvent<HTMLInputElement>) => void;
    onCancel: () => void;
    onReturn: (itemId: string) => void;
    onRemoveItem: (itemId: string) => void;
    onRefresh: () => void;
}) {
    return (
        <div style={drawerOverlayStyle} onClick={onClose}>
            <div style={drawerStyle} onClick={e => e.stopPropagation()}>
                {loading || !detail ? (
                    <div style={{ padding: 40, textAlign: 'center', color: '#9ca3af' }}>Carregando...</div>
                ) : (
                    <>
                        {/* Header */}
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 20, padding: '20px 24px 0 24px' }}>
                            <div>
                                <h2 style={{ margin: 0, fontSize: 18, fontWeight: 700, color: '#111827' }}>{detail.termNumber}</h2>
                                <StatusBadge status={detail.status} config={DELIVERY_TERM_STATUS_CONFIG} />
                            </div>
                            <div style={{ display: 'flex', gap: 6 }}>
                                <button onClick={onRefresh} style={iconBtnStyle}><RefreshCw size={16} /></button>
                                <button onClick={onClose} style={iconBtnStyle}><X size={18} /></button>
                            </div>
                        </div>

                        <div style={{ padding: '0 24px 24px 24px', overflow: 'auto', flex: 1 }}>
                            {/* Employee Info */}
                            <div style={sectionStyle}>
                                <h3 style={sectionTitleStyle}>Informações do Funcionário</h3>
                                <div style={infoGridStyle}>
                                    <InfoRow label="Nome" value={detail.employeeName} />
                                    <InfoRow label="E-mail" value={detail.employeeEmail} />
                                    <InfoRow label="Departamento" value={detail.employeeDepartment} />
                                    <InfoRow label="Cargo" value={detail.employeePosition} />
                                    <InfoRow label="Planta" value={detail.employeePlant} />
                                    <InfoRow label="Data de Entrega" value={new Date(detail.deliveryDate).toLocaleDateString('pt-PT')} />
                                    {detail.notes && <InfoRow label="Observações" value={detail.notes} />}
                                </div>
                            </div>

                            {/* Equipment Items */}
                            <div style={sectionStyle}>
                                <h3 style={sectionTitleStyle}>Equipamentos ({detail.items.length})</h3>
                                {detail.items.length === 0 ? (
                                    <div style={{ padding: 16, textAlign: 'center', color: '#9ca3af', fontSize: 14 }}>Nenhum equipamento adicionado.</div>
                                ) : (
                                    detail.items.map(item => (
                                        <div key={item.id} style={itemCardStyle}>
                                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                <div>
                                                    <div style={{ fontWeight: 600, fontSize: 14 }}>
                                                        {item.equipment?.assetTag || '—'}
                                                        <span style={{ marginLeft: 8, fontWeight: 400, color: '#6b7280', fontSize: 13 }}>
                                                            {item.equipment?.equipmentType || ''}
                                                        </span>
                                                    </div>
                                                    <div style={{ fontSize: 12, color: '#9ca3af', marginTop: 2 }}>
                                                        {[item.equipment?.hostname, item.equipment?.manufacturer, item.equipment?.model, item.equipment?.serialNumber].filter(Boolean).join(' • ')}
                                                    </div>
                                                </div>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                                                    <StatusBadge status={item.itemStatus} config={DELIVERY_ITEM_STATUS_CONFIG} />
                                                    {item.itemStatus === 'DELIVERED' && ['SIGNED', 'PARTIALLY_RETURNED', 'SENT', 'GENERATED'].includes(detail.status) && (
                                                        <button onClick={() => onReturn(item.id)} style={{ ...iconBtnStyle, color: '#f59e0b' }} title="Devolver">
                                                            <Undo2 size={14} />
                                                        </button>
                                                    )}
                                                    {item.itemStatus === 'PENDING' && detail.status === 'DRAFT' && (
                                                        <button onClick={() => onRemoveItem(item.id)} style={{ ...iconBtnStyle, color: '#ef4444' }} title="Remover">
                                                            <Trash2 size={14} />
                                                        </button>
                                                    )}
                                                </div>
                                            </div>
                                            {item.returnCondition && (
                                                <div style={{ marginTop: 4, fontSize: 12, color: '#6b7280' }}>
                                                    Condição: <StatusBadge status={item.returnCondition} config={RETURN_CONDITION_CONFIG} />
                                                    {item.returnedAt && ` — ${new Date(item.returnedAt).toLocaleDateString('pt-PT')}`}
                                                </div>
                                            )}
                                        </div>
                                    ))
                                )}
                            </div>

                            {/* Documents */}
                            {(detail.generatedDocumentId || detail.signedDocumentId || detail.returnDocumentId) && (
                                <div style={sectionStyle}>
                                    <h3 style={sectionTitleStyle}>Documentos</h3>
                                    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                                        {detail.generatedDocumentId && (
                                            <a href={deliveryTermsApi.downloadDocument(detail.id)} target="_blank" rel="noreferrer" style={docLinkStyle}>
                                                <FileText size={16} style={{ color: '#3b82f6' }} /> Termo de Entrega (PDF Gerado)
                                            </a>
                                        )}
                                        {detail.signedDocumentId && (
                                            <a href={deliveryTermsApi.downloadSignedDocument(detail.id)} target="_blank" rel="noreferrer" style={docLinkStyle}>
                                                <FileText size={16} style={{ color: '#10b981' }} /> Documento Assinado
                                            </a>
                                        )}
                                        {detail.returnDocumentId && (
                                            <a href={deliveryTermsApi.downloadReturnDocument(detail.id)} target="_blank" rel="noreferrer" style={docLinkStyle}>
                                                <FileText size={16} style={{ color: '#f59e0b' }} /> Termo de Devolução (PDF)
                                            </a>
                                        )}
                                    </div>
                                </div>
                            )}

                            {/* Actions */}
                            <div style={{ marginTop: 20, display: 'flex', flexWrap: 'wrap', gap: 8 }}>
                                {detail.status === 'DRAFT' && (
                                    <>
                                        <button onClick={onGenerate} disabled={actionLoading || !detail.items.length} style={btnPrimaryStyle}>
                                            <FileText size={14} /> Confirmar Entrega e Gerar PDF
                                        </button>
                                        <button onClick={onCancel} disabled={actionLoading} style={btnDangerStyle}>
                                            Cancelar Termo
                                        </button>
                                    </>
                                )}
                                {detail.status === 'GENERATED' && (
                                    <>
                                        <button onClick={onSend} disabled={actionLoading} style={btnPrimaryStyle}>
                                            <Send size={14} /> Enviar por E-mail
                                        </button>
                                        <label style={{ ...btnSecondaryStyle, cursor: 'pointer' }}>
                                            <Upload size={14} /> Carregar Assinado
                                            <input type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={onUploadSigned} style={{ display: 'none' }} />
                                        </label>
                                    </>
                                )}
                                {detail.status === 'SENT' && (
                                    <>
                                        <button onClick={onSend} disabled={actionLoading} style={btnSecondaryStyle}>
                                            <Send size={14} /> Reenviar E-mail
                                        </button>
                                        <label style={{ ...btnPrimaryStyle, cursor: 'pointer' }}>
                                            <Upload size={14} /> Carregar Assinado
                                            <input type="file" accept=".pdf,.jpg,.jpeg,.png" onChange={onUploadSigned} style={{ display: 'none' }} />
                                        </label>
                                    </>
                                )}
                                {(detail.status === 'SIGNED' || detail.status === 'PARTIALLY_RETURNED') && detail.generatedDocumentId && (
                                    <a href={deliveryTermsApi.downloadDocument(detail.id)} target="_blank" rel="noreferrer" style={btnSecondaryStyle}>
                                        <Download size={14} /> Baixar PDF
                                    </a>
                                )}
                                {detail.status === 'CLOSED' && (
                                    <>
                                        {detail.returnDocumentId && (
                                            <a href={deliveryTermsApi.downloadReturnDocument(detail.id)} target="_blank" rel="noreferrer" style={btnSecondaryStyle}>
                                                <Download size={14} /> Baixar Termo de Devolução
                                            </a>
                                        )}
                                        {detail.returnDocumentId && (
                                            <label style={{ ...btnPrimaryStyle, cursor: 'pointer' }}>
                                                <Upload size={14} /> Carregar Devolução Assinada
                                                <input type="file" accept=".pdf,.jpg,.jpeg,.png,.doc,.docx" onChange={onUploadSignedReturn} style={{ display: 'none' }} />
                                            </label>
                                        )}
                                    </>
                                )}
                            </div>

                            {/* Audit */}
                            <div style={{ marginTop: 24, padding: '12px 0', borderTop: '1px solid #f3f4f6', fontSize: 12, color: '#9ca3af' }}>
                                Criado por {detail.createdByName || '—'} em {new Date(detail.createdAt).toLocaleString('pt-PT')}
                                {detail.updatedAt && (
                                    <> • Atualizado por {detail.updatedByName || '—'} em {new Date(detail.updatedAt).toLocaleString('pt-PT')}</>
                                )}
                            </div>
                        </div>
                    </>
                )}
            </div>
        </div>
    );
}

// ─── Info Row ───

function InfoRow({ label, value }: { label: string; value: string | null | undefined }) {
    return (
        <div style={{ display: 'flex', gap: 8 }}>
            <span style={{ color: '#6b7280', fontSize: 13, minWidth: 120 }}>{label}:</span>
            <span style={{ fontSize: 13, fontWeight: 500 }}>{value || '—'}</span>
        </div>
    );
}

// ─── Return Item Modal ───

function ReturnItemModal({ item, condition, notes, loading, onConditionChange, onNotesChange, onConfirm, onClose }: {
    item: ITDeliveryItemDetail;
    condition: string;
    notes: string;
    loading: boolean;
    onConditionChange: (v: string) => void;
    onNotesChange: (v: string) => void;
    onConfirm: () => void;
    onClose: () => void;
}) {
    return (
        <div style={overlayStyle}>
            <div style={{ ...modalStyle, maxWidth: 440 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
                    <h2 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>Devolver Equipamento</h2>
                    <button onClick={onClose} style={iconBtnStyle}><X size={18} /></button>
                </div>
                <div style={{ padding: '12px 16px', background: '#f9fafb', borderRadius: 8, marginBottom: 16, fontSize: 13 }}>
                    <strong>{item.equipment?.assetTag}</strong> — {item.equipment?.equipmentType} {item.equipment?.hostname && `(${item.equipment.hostname})`}
                </div>
                <div style={fieldStyle}>
                    <label style={labelStyle}>Condição de Devolução *</label>
                    <select value={condition} onChange={e => onConditionChange(e.target.value)} style={inputStyle}>
                        {Object.entries(RETURN_CONDITION_CONFIG).map(([code, { label }]) => (
                            <option key={code} value={code}>{label}</option>
                        ))}
                    </select>
                </div>
                <div style={{ ...fieldStyle, marginTop: 12 }}>
                    <label style={labelStyle}>Observações</label>
                    <textarea value={notes} onChange={e => onNotesChange(e.target.value)} style={{ ...inputStyle, minHeight: 60 }} placeholder="Opcional" />
                </div>
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 }}>
                    <button onClick={onClose} style={btnSecondaryStyle}>Cancelar</button>
                    <button onClick={onConfirm} disabled={loading} style={btnPrimaryStyle}>
                        {loading ? 'Devolvendo...' : 'Confirmar Devolução'}
                    </button>
                </div>
            </div>
        </div>
    );
}

// ═══════════════════════════════════════════════════════════════
//  STYLES
// ═══════════════════════════════════════════════════════════════

const btnPrimaryStyle: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: 6, padding: '8px 16px',
    background: '#3b82f6', color: '#fff', border: 'none', borderRadius: 8,
    fontSize: 13, fontWeight: 600, cursor: 'pointer', transition: 'all 0.15s',
    textDecoration: 'none'
};

const btnSecondaryStyle: React.CSSProperties = {
    display: 'inline-flex', alignItems: 'center', gap: 6, padding: '8px 16px',
    background: '#f3f4f6', color: '#374151', border: '1px solid #e5e7eb', borderRadius: 8,
    fontSize: 13, fontWeight: 500, cursor: 'pointer', transition: 'all 0.15s',
    textDecoration: 'none'
};

const btnDangerStyle: React.CSSProperties = {
    ...btnSecondaryStyle, color: '#ef4444', borderColor: '#fecaca'
};

const iconBtnStyle: React.CSSProperties = {
    background: 'none', border: 'none', cursor: 'pointer', padding: 6, borderRadius: 6,
    color: '#6b7280', display: 'flex', alignItems: 'center'
};

const searchInputStyle: React.CSSProperties = {
    width: '100%', padding: '8px 12px 8px 36px', border: '1px solid #e5e7eb',
    borderRadius: 8, fontSize: 14, outline: 'none', background: '#fff'
};

const selectStyle: React.CSSProperties = {
    padding: '8px 12px', border: '1px solid #e5e7eb', borderRadius: 8,
    fontSize: 14, background: '#fff', color: '#374151', cursor: 'pointer'
};

const thStyle: React.CSSProperties = {
    textAlign: 'left', padding: '10px 14px', fontSize: 12, fontWeight: 600,
    color: '#6b7280', textTransform: 'uppercase', letterSpacing: '0.05em'
};

const tdStyle: React.CSSProperties = {
    padding: '12px 14px', fontSize: 14, color: '#111827'
};

const paginationBtnStyle: React.CSSProperties = {
    padding: '6px 10px', border: '1px solid #e5e7eb', borderRadius: 6,
    background: '#fff', cursor: 'pointer', display: 'flex', alignItems: 'center'
};

const overlayStyle: React.CSSProperties = {
    position: 'fixed', inset: 0, zIndex: 1000, display: 'flex',
    alignItems: 'center', justifyContent: 'center',
    background: 'rgba(0,0,0,0.4)', backdropFilter: 'blur(2px)'
};

const modalStyle: React.CSSProperties = {
    background: '#fff', borderRadius: 16, padding: 24,
    boxShadow: '0 20px 60px rgba(0,0,0,0.2)', width: '90vw'
};

const drawerOverlayStyle: React.CSSProperties = {
    position: 'fixed', inset: 0, zIndex: 1000,
    background: 'rgba(0,0,0,0.3)', backdropFilter: 'blur(1px)'
};

const drawerStyle: React.CSSProperties = {
    position: 'fixed', right: 0, top: 0, bottom: 0, width: '520px', maxWidth: '90vw',
    background: '#fff', boxShadow: '-8px 0 32px rgba(0,0,0,0.15)',
    display: 'flex', flexDirection: 'column', overflow: 'auto',
    animation: 'slideInRight 0.2s ease'
};

const sectionStyle: React.CSSProperties = {
    marginTop: 20, padding: '16px', background: '#f9fafb', borderRadius: 10
};

const sectionTitleStyle: React.CSSProperties = {
    margin: '0 0 12px 0', fontSize: 14, fontWeight: 700, color: '#374151',
    textTransform: 'uppercase', letterSpacing: '0.03em'
};

const infoGridStyle: React.CSSProperties = {
    display: 'flex', flexDirection: 'column', gap: 6
};

const itemCardStyle: React.CSSProperties = {
    padding: '10px 12px', background: '#fff', borderRadius: 8,
    border: '1px solid #e5e7eb', marginBottom: 6
};

const docLinkStyle: React.CSSProperties = {
    display: 'flex', alignItems: 'center', gap: 8, padding: '8px 12px',
    background: '#fff', borderRadius: 8, border: '1px solid #e5e7eb',
    textDecoration: 'none', color: '#374151', fontSize: 13, fontWeight: 500
};

const formGridStyle: React.CSSProperties = {
    display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 12
};

const fieldStyle: React.CSSProperties = {
    display: 'flex', flexDirection: 'column', gap: 4
};

const labelStyle: React.CSSProperties = {
    fontSize: 12, fontWeight: 600, color: '#6b7280', textTransform: 'uppercase', letterSpacing: '0.03em'
};

const inputStyle: React.CSSProperties = {
    padding: '8px 12px', border: '1px solid #e5e7eb', borderRadius: 8,
    fontSize: 14, outline: 'none', background: '#fff', width: '100%'
};
