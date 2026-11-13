import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
    ArrowLeft, Search, RefreshCw, Building, Users, Layers, Sparkles, CheckCircle2, AlertTriangle,
    Edit2, Save, X, Eye, ListChecks, Split
} from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { Feedback, FeedbackType } from '../../components/ui/Feedback';
import { DepartmentMasterAutocomplete } from '../../components/DepartmentMasterAutocomplete';
import type {
    HRMappingContextDto, HRMappingPlantDto, HRManagerCandidateDto, HRInnuxDepartmentDto, HRDirectoryLightRow,
    HRBulkMappingPlanRequest, HRBulkMappingPreviewResult, HRBulkMappingPreviewRow, HRSuggestionState, HRMappingState
} from '../../types';
import './hr-bulk-mapping.css';

/**
 * HR → Directory → "Mapeamento em lote".
 *
 * One workspace to review and map employees by team: filter (suggested company, Innux department, assigned plant,
 * missing fields, state), select across pages (page vs. all filtered results, exact count always shown), then either
 *   1. confirm the ACTIONABLE plant suggestions of the selection (server rule, HRDirectoryMappingPolicy), or
 *      assign Viana 1 / Viana 2 explicitly to a selected subset of APA-suggested employees — the shared Innux
 *      department never chooses the plant;
 *   2. assign any combination of plant / department master / responsible person to the selection (empty fields are
 *      filled, existing values only replaced on explicit request; untouched fields are preserved).
 * Every operation goes through a server preview (exact employees and field changes) and an all-or-nothing apply that
 * re-validates scope, current values and each final plant/department pair. Individual exceptions: inline edit (single save).
 */

const PAGE_SIZE = 25;

type SuggestedCompanyFilter = '' | 'APA' | 'APS' | 'MULTI' | 'NONE';
type MissingFilter = '' | 'any' | 'plant' | 'department' | 'manager';
type StatusFilter = '' | 'complete' | 'partial' | 'unmapped';

interface DirectoryRow {
    id: string;
    employeeCode: string;
    fullName: string;
    innuxDepartmentName?: string | null;
    plantId?: number | null;
    plantName?: string | null;
    plantCompanyCode?: string | null;
    departmentMasterId?: number | null;
    departmentMasterName?: string | null;
    departmentMasterCompanyCode?: string | null;
    managerUserId?: string | null;
    managerName?: string | null;
    managerIsActive?: boolean | null;
    portalDepartmentId?: number | null;
    mappingState: HRMappingState;
    suggestionState: HRSuggestionState;
    suggestedCompanyCode?: string | null;
    suggestedPlantCandidateIds?: number[];
    companyConsistent?: boolean;
}

interface Summary {
    total: number; complete: number; partial: number; unmapped: number;
    withoutPlant: number; withoutDepartment: number; withoutManager: number;
    suggestionActionable: number; suggestionNeedsChoice: number; suggestionManual: number; suggestionUnverified: number;
}

const toLight = (r: DirectoryRow): HRDirectoryLightRow => ({
    id: r.id, employeeCode: r.employeeCode, fullName: r.fullName,
    plantId: r.plantId ?? null, plantCompanyCode: r.plantCompanyCode ?? null,
    departmentMasterId: r.departmentMasterId ?? null, managerUserId: r.managerUserId ?? null,
    mappingState: r.mappingState, suggestionState: r.suggestionState, suggestedCompanyCode: r.suggestedCompanyCode ?? null,
});

const STATE_LABEL: Record<HRMappingState, string> = { COMPLETE: 'Completo', PARTIAL: 'Parcial', UNMAPPED: 'Não mapeado' };

export default function HRBulkMappingPage() {
    // ─── master data ───
    const [ctx, setCtx] = useState<HRMappingContextDto | null>(null);
    const [innuxDepartments, setInnuxDepartments] = useState<HRInnuxDepartmentDto[]>([]);
    const plants: HRMappingPlantDto[] = ctx?.plants ?? [];
    const managers: HRManagerCandidateDto[] = ctx?.managerCandidates ?? [];

    // ─── filters / grid ───
    const [search, setSearch] = useState('');
    const [suggestedCompany, setSuggestedCompany] = useState<SuggestedCompanyFilter>('');
    const [innuxDepartment, setInnuxDepartment] = useState('');
    const [plantFilter, setPlantFilter] = useState(''); // '' all · '0' without plant · id
    const [missingField, setMissingField] = useState<MissingFilter>('');
    const [statusFilter, setStatusFilter] = useState<StatusFilter>('');
    const [page, setPage] = useState(1);
    const [rows, setRows] = useState<DirectoryRow[]>([]);
    const [total, setTotal] = useState(0);
    const [summary, setSummary] = useState<Summary | null>(null);
    const [loading, setLoading] = useState(false);
    // Only the latest grid request may update the grid: a slower, older response (e.g. the initial load finishing after a
    // filter change) must never overwrite newer rows, total and KPIs.
    const gridRequestSeq = useRef(0);

    // ─── selection (persists across pages; id → light row) ───
    const [selection, setSelection] = useState<Map<string, HRDirectoryLightRow>>(new Map());
    const [selectingAll, setSelectingAll] = useState(false);

    // ─── action panel ───
    const [tab, setTab] = useState<'suggestions' | 'team'>('suggestions');
    const [splitPlantId, setSplitPlantId] = useState<string>('');
    const [team, setTeam] = useState({
        plantId: '' as string,
        departmentMasterId: null as number | null,
        departmentMasterName: '',
        departmentCompany: '' as string, // only used when no plant is chosen and the selection has no plant yet
        managerUserId: '' as string,
        overwritePlant: false,
        overwriteDepartment: false,
        overwriteManager: false,
    });

    // ─── preview / apply ───
    const [preview, setPreview] = useState<HRBulkMappingPreviewResult | null>(null);
    const [previewPlan, setPreviewPlan] = useState<HRBulkMappingPlanRequest | null>(null);
    const [previewing, setPreviewing] = useState(false);
    const [applying, setApplying] = useState(false);
    const [previewError, setPreviewError] = useState<string | null>(null);

    // ─── inline edit ───
    const [editingId, setEditingId] = useState<string | null>(null);
    const [editForm, setEditForm] = useState({ plantId: '' as string, departmentMasterId: null as number | null, departmentMasterName: '', managerUserId: '' as string, portalDepartmentId: null as number | null });
    const [saving, setSaving] = useState(false);

    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'info', message: null });

    // ───────────────────────── data loading ─────────────────────────

    useEffect(() => {
        (async () => {
            try {
                const [c, d] = await Promise.all([api.hrLeave.getMappingContext(), api.hrLeave.getInnuxDepartments()]);
                setCtx(c);
                setInnuxDepartments(d);
            } catch (e) {
                console.error('bulk mapping: master data', e);
                setFeedback({ type: 'error', message: 'Falha ao carregar plantas, responsáveis e departamentos Innux.' });
            }
        })();
    }, []);

    const filterParams = useCallback((): Record<string, string | number | boolean> => {
        const p: Record<string, string | number | boolean> = {};
        if (search.trim()) p.search = search.trim();
        if (suggestedCompany) p.suggestedCompany = suggestedCompany;
        if (innuxDepartment) { p.innuxDepartment = innuxDepartment; p.innuxDepartmentExact = true; }
        if (plantFilter !== '') p.plantId = Number(plantFilter);
        if (missingField) p.missingField = missingField;
        if (statusFilter) p.mappingStatus = statusFilter;
        return p;
    }, [search, suggestedCompany, innuxDepartment, plantFilter, missingField, statusFilter]);

    const loadRows = useCallback(async () => {
        const seq = ++gridRequestSeq.current;
        setLoading(true);
        try {
            const res = await api.hrLeave.getEmployees({ ...filterParams(), page, pageSize: PAGE_SIZE });
            if (seq !== gridRequestSeq.current) return; // superseded by a newer request
            setRows(res.items);
            setTotal(res.total);
            if (res.summary) setSummary(res.summary);
        } catch (e) {
            if (seq !== gridRequestSeq.current) return;
            console.error('bulk mapping: rows', e);
            setFeedback({ type: 'error', message: 'Falha ao carregar os funcionários.' });
        } finally {
            if (seq === gridRequestSeq.current) setLoading(false);
        }
    }, [filterParams, page]);

    useEffect(() => { loadRows(); }, [loadRows]);
    useEffect(() => { setPage(1); }, [search, suggestedCompany, innuxDepartment, plantFilter, missingField, statusFilter]);

    const refreshAfterWrite = useCallback(async () => {
        await loadRows();
        try { setInnuxDepartments(await api.hrLeave.getInnuxDepartments()); } catch { /* counts only */ }
    }, [loadRows]);

    // ───────────────────────── selection ─────────────────────────

    const selected = useMemo(() => Array.from(selection.values()), [selection]);
    const selectedCount = selection.size;
    const pageSelectedCount = rows.filter(r => selection.has(r.id)).length;
    const allPageSelected = rows.length > 0 && pageSelectedCount === rows.length;

    const toggleRow = (r: DirectoryRow) => setSelection(prev => {
        const next = new Map(prev);
        if (next.has(r.id)) next.delete(r.id); else next.set(r.id, toLight(r));
        return next;
    });
    const selectPage = () => setSelection(prev => { const next = new Map(prev); rows.forEach(r => next.set(r.id, toLight(r))); return next; });
    const deselectPage = () => setSelection(prev => { const next = new Map(prev); rows.forEach(r => next.delete(r.id)); return next; });
    const clearSelection = () => setSelection(new Map());
    const removeIds = (ids: string[]) => setSelection(prev => { const next = new Map(prev); ids.forEach(id => next.delete(id)); return next; });
    const keepOnly = (pred: (r: HRDirectoryLightRow) => boolean) => setSelection(prev => { const next = new Map(); prev.forEach((v, k) => { if (pred(v)) next.set(k, v); }); return next; });

    const selectAllFiltered = async () => {
        setSelectingAll(true);
        try {
            const res = await api.hrLeave.getEmployeeIds(filterParams());
            setSelection(prev => { const next = new Map(prev); res.items.forEach(r => next.set(r.id, r)); return next; });
            setFeedback({ type: 'info', message: `${res.total} funcionário(s) do filtro atual adicionados à seleção.` });
        } catch (e) {
            console.error('bulk mapping: select all', e);
            setFeedback({ type: 'error', message: 'Falha ao selecionar todos os resultados filtrados.' });
        } finally {
            setSelectingAll(false);
        }
    };

    // Selection composition — drives the panel texts and the department-company rule.
    const sel = useMemo(() => {
        const bySuggestion: Record<HRSuggestionState, number> = { ACTIONABLE: 0, NEEDS_CHOICE: 0, MANUAL: 0, UNVERIFIED: 0, NONE: 0 };
        const plantCompanies = new Map<string, number>();
        let withoutPlant = 0, withPlant = 0, withDepartment = 0, withManager = 0;
        selected.forEach(r => {
            bySuggestion[r.suggestionState] = (bySuggestion[r.suggestionState] ?? 0) + 1;
            if (r.plantId) { withPlant++; const c = r.plantCompanyCode || '?'; plantCompanies.set(c, (plantCompanies.get(c) ?? 0) + 1); } else withoutPlant++;
            if (r.departmentMasterId) withDepartment++;
            if (r.managerUserId) withManager++;
        });
        return { bySuggestion, plantCompanies, withoutPlant, withPlant, withDepartment, withManager };
    }, [selected]);

    // ───────────────────────── helpers ─────────────────────────

    const plantById = (id?: number | string | null) => plants.find(p => p.id.toString() === (id ?? '').toString());
    const plantName = (id?: number | null) => plantById(id)?.name || (id ? `#${id}` : '');
    const companyOfPlant = (id?: number | string | null) => plantById(id)?.companyCode || '';
    const masterLabelOfPlant = (id?: number | string | null) => plantById(id)?.departmentMasterCompanyCode || '';
    const masterLabelOfCompany = (code: string) => ctx?.companyMap.find(c => c.companyCode === code)?.departmentMasterCompanyCode || '';
    const plantsOfCompany = (code: string) => plants.filter(p => p.companyCode === code);
    const companiesWithChoice = useMemo(() => {
        const codes = Array.from(new Set(plants.map(p => p.companyCode).filter(Boolean))) as string[];
        return codes.filter(c => plantsOfCompany(c).length > 1);
    }, [plants]);

    const renderManagerOptions = (currentId?: string | null, currentName?: string | null, currentActive?: boolean | null) => {
        const eligible = managers.filter(m => m.eligibleOptionC);
        const others = managers.filter(m => !m.eligibleOptionC);
        const inList = !!currentId && managers.some(m => m.id === currentId);
        return (
            <>
                {currentId && !inList && (
                    <option value={currentId}>{currentName || currentId} (atual{currentActive === false ? ' — utilizador inativo' : ' — não listado'})</option>
                )}
                <optgroup label="Elegíveis — Opção C (política em revisão pelos RH)">
                    {eligible.map(m => <option key={m.id} value={m.id}>{m.fullName} · {m.reasons.join(', ')}</option>)}
                </optgroup>
                <optgroup label="Outros utilizadores ativos">
                    {others.map(m => <option key={m.id} value={m.id}>{m.fullName}</option>)}
                </optgroup>
            </>
        );
    };

    const describeApiError = (e: unknown, fallback: string): string => {
        if (e instanceof ApiError) {
            const list: any[] = e.details?.employees || [];
            const names = list.slice(0, 6).map(x => `${x.fullName}${x.employeeCode ? ` (#${x.employeeCode})` : ''}${x.reason ? ` — ${x.reason}` : ''}`);
            const more = list.length > 6 ? ` … e mais ${list.length - 6}` : '';
            return names.length ? `${e.message} Afetados: ${names.join('; ')}${more}.` : e.message;
        }
        return e instanceof Error && e.message ? e.message : fallback;
    };

    // ───────────────────────── plan builders ─────────────────────────

    const actionableIds = selected.filter(r => r.suggestionState === 'ACTIONABLE').map(r => r.id);
    const splitCompany = companyOfPlant(splitPlantId);
    const splitIds = selected.filter(r => r.suggestionState === 'NEEDS_CHOICE' && (!splitCompany || r.suggestedCompanyCode === splitCompany)).map(r => r.id);

    // Department company for the team picker: chosen plant → its company; otherwise the single company of the
    // selection's existing plants; otherwise an explicit choice. Mixed companies without a plant = conflict.
    const teamPlantCompany = companyOfPlant(team.plantId);
    const existingCompanies = Array.from(sel.plantCompanies.keys()).filter(c => c !== '?');
    const departmentCompany: string = team.plantId
        ? teamPlantCompany
        : existingCompanies.length === 1 ? existingCompanies[0]
        : existingCompanies.length === 0 ? team.departmentCompany
        : '';
    const departmentConflict = !team.plantId && existingCompanies.length > 1;
    const departmentCompanyLabel = departmentCompany ? masterLabelOfCompany(departmentCompany) : '';

    const teamHasField = !!team.plantId || !!team.departmentMasterId || !!team.managerUserId;

    const runPreview = async (plan: HRBulkMappingPlanRequest) => {
        setPreviewing(true);
        setPreviewError(null);
        try {
            const res = await api.hrLeave.previewBulkMapping(plan);
            setPreview(res);
            setPreviewPlan(plan);
        } catch (e) {
            setPreview(null);
            setPreviewPlan(null);
            setPreviewError(describeApiError(e, 'Falha ao pré-visualizar.'));
            setFeedback({ type: 'error', message: describeApiError(e, 'Falha ao pré-visualizar.') });
        } finally {
            setPreviewing(false);
        }
    };

    const previewConfirmSuggestions = () => runPreview({ employeeIds: actionableIds, mode: 'CONFIRM_SUGGESTIONS' });
    const previewSplit = () => runPreview({ employeeIds: splitIds, mode: 'ASSIGN', plantId: Number(splitPlantId) });
    const previewTeam = () => runPreview({
        employeeIds: selected.map(r => r.id),
        mode: 'ASSIGN',
        plantId: team.plantId ? Number(team.plantId) : null,
        departmentMasterId: team.departmentMasterId || null,
        managerUserId: team.managerUserId || null,
        overwritePlant: !!team.plantId && team.overwritePlant,
        overwriteDepartment: !!team.departmentMasterId && team.overwriteDepartment,
        overwriteManager: !!team.managerUserId && team.overwriteManager,
    });

    const applyPreview = async () => {
        if (!preview || !previewPlan || !preview.canApply) return;
        setApplying(true);
        try {
            const res = await api.hrLeave.applyBulkMapping({ ...previewPlan, expected: preview.expected });
            const changedIds = preview.rows.filter(r => r.status === 'CHANGE').map(r => r.employeeId);
            removeIds(changedIds);
            setPreview(null);
            setPreviewPlan(null);
            setFeedback({ type: 'success', message: res.message });
            await refreshAfterWrite();
        } catch (e) {
            // 409 STALE_SELECTION / 400 BULK_MAPPING_BLOCKED: the server names the employees; nothing was written.
            setPreviewError(describeApiError(e, 'Falha ao gravar o mapeamento.'));
            await refreshAfterWrite();
        } finally {
            setApplying(false);
        }
    };

    const removeBlockedAndRetry = (ids: string[]) => {
        if (!previewPlan) return;
        removeIds(ids);
        const remaining = previewPlan.employeeIds.filter(id => !ids.includes(id));
        if (remaining.length === 0) { setPreview(null); setPreviewPlan(null); return; }
        runPreview({ ...previewPlan, employeeIds: remaining });
    };

    // ───────────────────────── inline edit (single save, existing endpoint) ─────────────────────────

    const startEditing = (r: DirectoryRow) => {
        if (r.companyConsistent === false)
            setFeedback({ type: 'warning', message: `${r.fullName}: a planta e o departamento mestre guardados pertencem a empresas diferentes. Ao alterar a planta ou o departamento, o par terá de ser coerente.` });
        setEditingId(r.id);
        setEditForm({
            plantId: r.plantId ? String(r.plantId) : '',
            departmentMasterId: r.departmentMasterId ?? null,
            departmentMasterName: r.departmentMasterName || '',
            managerUserId: r.managerUserId || '',
            portalDepartmentId: r.portalDepartmentId ?? null,
        });
    };

    const editPlantChange = (newPlantId: string) => {
        const oldCompany = companyOfPlant(editForm.plantId);
        const newCompany = companyOfPlant(newPlantId);
        setEditForm(prev => ({
            ...prev,
            plantId: newPlantId,
            ...(prev.departmentMasterId && newCompany !== oldCompany ? { departmentMasterId: null, departmentMasterName: '' } : {}),
        }));
    };

    const saveInline = async (id: string) => {
        setSaving(true);
        try {
            await api.hrLeave.updateEmployeeMapping(id, {
                plantId: editForm.plantId ? Number(editForm.plantId) : null,
                departmentMasterId: editForm.departmentMasterId || null,
                managerUserId: editForm.managerUserId || null,
                portalDepartmentId: editForm.portalDepartmentId,
            });
            setEditingId(null);
            setFeedback({ type: 'success', message: 'Mapeamento individual atualizado.' });
            removeIds([id]);
            await refreshAfterWrite();
        } catch (e) {
            setFeedback({ type: 'error', message: describeApiError(e, 'Falha ao atualizar o mapeamento.') });
        } finally {
            setSaving(false);
        }
    };

    // ───────────────────────── rendering ─────────────────────────

    const renderSuggestion = (r: DirectoryRow) => {
        const cands = (r.suggestedPlantCandidateIds || []).map(plantName).filter(Boolean);
        const company = r.suggestedCompanyCode;
        switch (r.suggestionState) {
            case 'ACTIONABLE':
                return <div className="hrbm-suggestion"><div className="line"><span className="hrbm-chip company">{company}</span>→ <b>{cands[0]}</b> <span className="hrbm-chip actionable"><Sparkles size={11} /> aplicável</span></div></div>;
            case 'NEEDS_CHOICE':
                return <div className="hrbm-suggestion"><div className="line"><span className="hrbm-chip company">{company}</span>→ {cands.join(' ou ')} <span className="hrbm-chip choice">escolher</span></div></div>;
            case 'MANUAL':
                return <div className="hrbm-suggestion"><div className="line">Ambas as empresas <span className="hrbm-chip manual">manual</span></div></div>;
            case 'UNVERIFIED':
                return <div className="hrbm-suggestion"><div className="line">{company ? <span className="hrbm-chip company">{company}</span> : <span className="empty">Primavera</span>} <span className="hrbm-chip unverified">não verificada</span></div></div>;
            default:
                return <span className="empty">{r.plantId ? '—' : 'sem sugestão'}</span>;
        }
    };

    const renderDiff = (current: string | null | undefined, proposed: string | null | undefined, changed: boolean, preserved: boolean) => {
        if (changed) return <div className="hrbm-diff">{current ? <span className="from">{current}</span> : <span className="from" style={{ textDecoration: 'none' }}>vazio</span>}<span className="to">{proposed || '—'}</span></div>;
        return <div className="hrbm-diff"><span className="keep">{current || <span className="empty">—</span>}{preserved && <span className="kept-tag">mantido</span>}</span></div>;
    };

    const fieldLabel = (f: string) => f === 'PLANT' ? 'Planta' : f === 'DEPARTMENT' ? 'Departamento' : 'Responsável';

    return (
        <div className="hrbm-page">
            <div className="hrbm-header">
                <div>
                    <Link to="/hr/directory" className="hrbm-back"><ArrowLeft size={14} /> Directório &amp; Mapeamento</Link>
                    <h1><Layers size={22} color="#0284c7" /> Mapeamento em lote</h1>
                    <p>Reveja e mapeie funcionários por equipa: filtre, selecione (nesta página ou em todos os resultados), confirme sugestões de planta ou atribua planta, departamento mestre e responsável. Cada operação mostra os funcionários e as alterações exatas antes de gravar.</p>
                </div>
                <button className="hrbm-btn" onClick={() => loadRows()} disabled={loading}><RefreshCw size={14} className={loading ? 'hrbm-spin' : ''} /> Atualizar</button>
            </div>

            {feedback.message && <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback({ ...feedback, message: null })} />}

            {summary && (
                <div className="hrbm-kpis" data-testid="kpis">
                    <div className="hrbm-kpi"><div className="v">{summary.total}</div><div className="l">No filtro</div></div>
                    <div className="hrbm-kpi good"><div className="v">{summary.complete}</div><div className="l">Completos</div></div>
                    <div className="hrbm-kpi warn"><div className="v">{summary.partial}</div><div className="l">Parciais</div></div>
                    <div className="hrbm-kpi bad"><div className="v">{summary.unmapped}</div><div className="l">Não mapeados</div></div>
                    <div className="hrbm-kpi info"><div className="v">{summary.withoutPlant}</div><div className="l">Sem planta</div></div>
                    <div className="hrbm-kpi good"><div className="v">{summary.suggestionActionable}</div><div className="l">Sugestão aplicável</div></div>
                    <div className="hrbm-kpi warn"><div className="v">{summary.suggestionNeedsChoice}</div><div className="l">Escolher planta</div></div>
                </div>
            )}

            <div className="hrbm-filters">
                <div className="hrbm-filter search">
                    <label>Pesquisa</label>
                    <div className="with-icon"><Search size={14} /><input data-testid="filter-search" placeholder="Nome, número ou departamento Innux…" value={search} onChange={e => setSearch(e.target.value)} /></div>
                </div>
                <div className="hrbm-filter">
                    <label>Empresa (sugestão Primavera)</label>
                    <select data-testid="filter-company" value={suggestedCompany} onChange={e => setSuggestedCompany(e.target.value as SuggestedCompanyFilter)}>
                        <option value="">Todas</option>
                        {ctx?.companyMap.map(c => <option key={c.companyCode} value={c.companyCode}>{c.companyCode} — {plantsOfCompany(c.companyCode).map(p => p.name).join(' / ') || c.departmentMasterCompanyCode}</option>)}
                        <option value="MULTI">Ambas (manual)</option>
                        <option value="NONE">Sem sugestão</option>
                    </select>
                </div>
                <div className="hrbm-filter">
                    <label>Departamento Innux</label>
                    <select data-testid="filter-innux" value={innuxDepartment} onChange={e => setInnuxDepartment(e.target.value)}>
                        <option value="">Todos</option>
                        {innuxDepartments.map(d => <option key={d.name} value={d.name}>{d.name} ({d.withoutPlant}/{d.total} sem planta)</option>)}
                    </select>
                </div>
                <div className="hrbm-filter">
                    <label>Planta atribuída</label>
                    <select data-testid="filter-plant" value={plantFilter} onChange={e => setPlantFilter(e.target.value)}>
                        <option value="">Todas</option>
                        <option value="0">Sem planta</option>
                        {plants.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                </div>
                <div className="hrbm-filter">
                    <label>Campos em falta</label>
                    <select data-testid="filter-missing" value={missingField} onChange={e => setMissingField(e.target.value as MissingFilter)}>
                        <option value="">Indiferente</option>
                        <option value="any">Qualquer campo</option>
                        <option value="plant">Sem planta</option>
                        <option value="department">Sem departamento</option>
                        <option value="manager">Sem responsável</option>
                    </select>
                </div>
                <div className="hrbm-filter">
                    <label>Estado</label>
                    <select data-testid="filter-status" value={statusFilter} onChange={e => setStatusFilter(e.target.value as StatusFilter)}>
                        <option value="">Todos</option>
                        <option value="complete">Completos</option>
                        <option value="partial">Parciais</option>
                        <option value="unmapped">Não mapeados</option>
                    </select>
                </div>
            </div>

            <div className="hrbm-body">
                <div style={{ display: 'flex', flexDirection: 'column', gap: 12, minWidth: 0 }}>
                    <div className="hrbm-selbar" data-testid="selection-bar">
                        <span className="count">Selecionados: <b data-testid="selected-count">{selectedCount}</b>
                            <span className="muted">{pageSelectedCount} nesta página · {total} no filtro atual</span>
                        </span>
                        <div className="actions">
                            {allPageSelected
                                ? <button className="hrbm-btn sm" data-testid="deselect-page" onClick={deselectPage} disabled={rows.length === 0}>Desmarcar página ({rows.length})</button>
                                : <button className="hrbm-btn sm" data-testid="select-page" onClick={selectPage} disabled={rows.length === 0}>Selecionar página ({rows.length})</button>}
                            <button className="hrbm-btn sm" data-testid="select-all-filtered" onClick={selectAllFiltered} disabled={selectingAll || total === 0}>
                                {selectingAll ? <RefreshCw size={12} className="hrbm-spin" /> : <ListChecks size={12} />} Selecionar todos os {total} filtrados
                            </button>
                            <button className="hrbm-btn sm danger" data-testid="clear-selection" onClick={clearSelection} disabled={selectedCount === 0}><X size={12} /> Limpar seleção</button>
                        </div>
                    </div>

                    <div className="hrbm-grid">
                        <div className="scroll">
                            <table data-testid="grid">
                                <thead>
                                    <tr>
                                        <th style={{ width: 36 }}>
                                            <input type="checkbox" data-testid="page-checkbox" checked={allPageSelected} ref={el => { if (el) el.indeterminate = pageSelectedCount > 0 && !allPageSelected; }} onChange={e => e.target.checked ? selectPage() : deselectPage()} style={{ cursor: 'pointer' }} />
                                        </th>
                                        <th>Funcionário</th>
                                        <th>Dept. Innux</th>
                                        <th>Sugestão (empresa → planta)</th>
                                        <th>Planta</th>
                                        <th>Departamento mestre</th>
                                        <th>Responsável</th>
                                        <th>Estado</th>
                                        <th style={{ textAlign: 'right' }}>Ações</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {loading && rows.length === 0 ? (
                                        <tr><td colSpan={9} className="center"><RefreshCw size={22} className="hrbm-spin" style={{ display: 'block', margin: '0 auto 10px' }} />A carregar…</td></tr>
                                    ) : rows.length === 0 ? (
                                        <tr><td colSpan={9} className="center">Nenhum funcionário corresponde aos filtros.</td></tr>
                                    ) : rows.map(r => {
                                        const isSel = selection.has(r.id);
                                        const isEditing = editingId === r.id;
                                        return (
                                            <tr key={r.id} className={`${isSel ? 'selected' : ''} ${r.mappingState !== 'COMPLETE' ? 'incomplete' : ''}`} data-employee-row={r.employeeCode}>
                                                <td><input type="checkbox" checked={isSel} onChange={() => toggleRow(r)} style={{ cursor: 'pointer' }} /></td>
                                                <td>
                                                    <span className="name">{r.fullName}{r.companyConsistent === false && <Building size={13} color="#dc2626" style={{ marginLeft: 6, verticalAlign: 'middle' }} />}</span>
                                                    <span className="code">#{r.employeeCode}</span>
                                                </td>
                                                <td>{r.innuxDepartmentName || <span className="empty">—</span>}</td>
                                                <td>{renderSuggestion(r)}</td>
                                                <td style={{ minWidth: 140 }}>
                                                    {isEditing ? (
                                                        <select value={editForm.plantId} onChange={e => editPlantChange(e.target.value)}>
                                                            <option value="">Não atribuída</option>
                                                            {plants.map(p => <option key={p.id} value={p.id}>{p.name}{(r.suggestedPlantCandidateIds || []).includes(p.id) ? ' ★' : ''}</option>)}
                                                        </select>
                                                    ) : <span className={`cell-icon ${r.plantName ? '' : 'empty'}`}><Building size={13} />{r.plantName || 'Não atribuída'}</span>}
                                                </td>
                                                <td style={{ minWidth: 200 }}>
                                                    {isEditing ? (
                                                        <DepartmentMasterAutocomplete
                                                            initialId={editForm.departmentMasterId}
                                                            initialName={editForm.departmentMasterName}
                                                            suggestedMatchText={r.innuxDepartmentName || ''}
                                                            allowedCompanyCode={masterLabelOfPlant(editForm.plantId)}
                                                            plantNotSelected={!editForm.plantId}
                                                            onChange={(id, name) => setEditForm(prev => ({ ...prev, departmentMasterId: id, departmentMasterName: name }))}
                                                        />
                                                    ) : <span className={r.departmentMasterName ? '' : 'empty'}>{r.departmentMasterName || 'Não atribuído'}</span>}
                                                </td>
                                                <td style={{ minWidth: 160 }}>
                                                    {isEditing ? (
                                                        <select value={editForm.managerUserId} onChange={e => setEditForm(prev => ({ ...prev, managerUserId: e.target.value }))}>
                                                            <option value="">Sem responsável</option>
                                                            {renderManagerOptions(r.managerUserId, r.managerName, r.managerIsActive)}
                                                        </select>
                                                    ) : <span className={`cell-icon ${r.managerName ? '' : 'empty'}`}><Users size={13} />{r.managerName || 'Sem responsável'}</span>}
                                                </td>
                                                <td><span className={`hrbm-chip ${r.mappingState.toLowerCase()}`}>{STATE_LABEL[r.mappingState]}</span></td>
                                                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                                                    {isEditing ? (
                                                        <span style={{ display: 'inline-flex', gap: 6 }}>
                                                            <button className="hrbm-btn sm" onClick={() => setEditingId(null)} disabled={saving}><X size={13} /></button>
                                                            <button className="hrbm-btn sm primary" onClick={() => saveInline(r.id)} disabled={saving}>{saving ? <RefreshCw size={13} className="hrbm-spin" /> : <Save size={13} />} Guardar</button>
                                                        </span>
                                                    ) : (
                                                        <button className="hrbm-btn sm" onClick={() => startEditing(r)} title="Editar só este funcionário (exceção individual)"><Edit2 size={13} /> Editar</button>
                                                    )}
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                        <div className="hrbm-pager">
                            <span>{total === 0 ? 'Sem resultados' : `Mostrando ${(page - 1) * PAGE_SIZE + 1}–${Math.min(page * PAGE_SIZE, total)} de ${total}`}</span>
                            <div className="btns">
                                <button className="hrbm-btn sm" data-testid="prev-page" disabled={page === 1} onClick={() => setPage(p => p - 1)}>Anterior</button>
                                <span style={{ alignSelf: 'center' }}>Página {page} de {Math.max(1, Math.ceil(total / PAGE_SIZE))}</span>
                                <button className="hrbm-btn sm" data-testid="next-page" disabled={page * PAGE_SIZE >= total} onClick={() => setPage(p => p + 1)}>Seguinte</button>
                            </div>
                        </div>
                    </div>
                </div>

                {/* ─── Action panel ─── */}
                <aside className="hrbm-panel" data-testid="action-panel">
                    <div className="hrbm-tabs">
                        <button className={tab === 'suggestions' ? 'active' : ''} data-testid="tab-suggestions" onClick={() => setTab('suggestions')}><Sparkles size={14} /> Rever sugestões de planta</button>
                        <button className={tab === 'team' ? 'active' : ''} data-testid="tab-team" onClick={() => setTab('team')}><Users size={14} /> Atribuir equipa</button>
                    </div>
                    <div className="content">
                        <div className="selstats">
                            <span className="hrbm-chip company">{selectedCount} selecionados</span>
                            {sel.withPlant > 0 && <span className="hrbm-chip manual">{sel.withPlant} com planta</span>}
                            {sel.withoutPlant > 0 && <span className="hrbm-chip unmapped">{sel.withoutPlant} sem planta</span>}
                            {sel.withDepartment > 0 && <span className="hrbm-chip manual">{sel.withDepartment} com departamento</span>}
                            {sel.withManager > 0 && <span className="hrbm-chip manual">{sel.withManager} com responsável</span>}
                        </div>

                        {selectedCount === 0 && <p className="hint">Selecione funcionários na grelha (ou "Selecionar todos os filtrados") para ativar as ações.</p>}

                        {tab === 'suggestions' ? (
                            <>
                                <div className="block ok">
                                    <h3><CheckCircle2 size={15} color="#16a34a" /> Confirmar sugestões aplicáveis</h3>
                                    <div className="stat"><b data-testid="actionable-count">{sel.bySuggestion.ACTIONABLE}</b> de {selectedCount} selecionados têm sugestão aplicável (empresa com uma única planta, consulta Primavera completa, sem planta atribuída).</div>
                                    <p className="hint">Só estes serão incluídos; os restantes selecionados ficam de fora desta ação. Regra do servidor: a mesma que o botão "Aceitar" do directório.</p>
                                    <button className="hrbm-btn success" data-testid="preview-confirm" disabled={actionableIds.length === 0 || previewing} onClick={previewConfirmSuggestions}>
                                        <Eye size={14} /> Pré-visualizar confirmação ({actionableIds.length})
                                    </button>
                                </div>

                                <div className="block choice">
                                    <h3><Split size={15} color="#b45309" /> Escolher planta ({companiesWithChoice.map(c => plantsOfCompany(c).map(p => p.name).join(' / ')).join('; ') || 'empresa com várias plantas'})</h3>
                                    <div className="stat"><b data-testid="choice-count">{sel.bySuggestion.NEEDS_CHOICE}</b> selecionados têm a empresa identificada mas precisam de escolha de planta.</div>
                                    <p className="hint">Filtre pelo departamento Innux, marque apenas os funcionários da planta pretendida e escolha-a aqui. <b>A planta é sempre a sua escolha explícita: um departamento Innux partilhado nunca decide entre estas plantas.</b></p>
                                    <div className="radios" data-testid="split-plants">
                                        {companiesWithChoice.flatMap(c => plantsOfCompany(c)).map(p => (
                                            <label key={p.id} className={splitPlantId === String(p.id) ? 'on' : ''}>
                                                <input type="radio" name="splitPlant" value={p.id} checked={splitPlantId === String(p.id)} onChange={() => setSplitPlantId(String(p.id))} /> {p.name} <span className="hrbm-chip company">{p.companyCode}</span>
                                            </label>
                                        ))}
                                    </div>
                                    {splitPlantId && splitIds.length < sel.bySuggestion.NEEDS_CHOICE && (
                                        <div className="hrbm-warn">{sel.bySuggestion.NEEDS_CHOICE - splitIds.length} selecionado(s) com escolha pendente são de outra empresa e não serão incluídos.</div>
                                    )}
                                    <button className="hrbm-btn primary" data-testid="preview-split" disabled={!splitPlantId || splitIds.length === 0 || previewing} onClick={previewSplit}>
                                        <Eye size={14} /> Pré-visualizar atribuição de {splitPlantId ? plantName(Number(splitPlantId)) : 'planta'} ({splitIds.length})
                                    </button>
                                    <p className="hint">Para funcionários com sugestão "manual" ou "não verificada" use "Atribuir equipa" ou a edição individual.</p>
                                </div>
                            </>
                        ) : (
                            <>
                                <div className="field">
                                    <label>Planta</label>
                                    <select data-testid="team-plant" value={team.plantId} onChange={e => {
                                        const v = e.target.value;
                                        const sameCompany = companyOfPlant(v) === companyOfPlant(team.plantId);
                                        setTeam(prev => ({ ...prev, plantId: v, ...(prev.departmentMasterId && !sameCompany ? { departmentMasterId: null, departmentMasterName: '' } : {}) }));
                                    }}>
                                        <option value="">Manter a planta atual</option>
                                        {plants.map(p => <option key={p.id} value={p.id}>{p.name} ({p.companyCode})</option>)}
                                    </select>
                                    {team.plantId && sel.withPlant > 0 && (
                                        <label className="check"><input type="checkbox" data-testid="team-overwrite-plant" checked={team.overwritePlant} onChange={e => setTeam(prev => ({ ...prev, overwritePlant: e.target.checked }))} /> Substituir a planta dos {sel.withPlant} que já têm planta (por omissão só preenche vazios)</label>
                                    )}
                                </div>

                                <div className="field">
                                    <label>Departamento mestre {departmentCompanyLabel && <span className="hrbm-chip company">{departmentCompanyLabel}</span>}</label>
                                    {departmentConflict ? (
                                        <div className="hrbm-conflict" data-testid="department-conflict">
                                            <div><AlertTriangle size={14} style={{ verticalAlign: 'middle', marginRight: 6 }} />A seleção tem plantas de empresas diferentes: {existingCompanies.map(c => `${c} (${sel.plantCompanies.get(c)})`).join(', ')}{sel.withoutPlant > 0 ? `, sem planta (${sel.withoutPlant})` : ''}. Um departamento mestre pertence a uma única empresa.</div>
                                            <div>Escolha uma planta acima ou reduza a seleção a uma empresa:</div>
                                            <div className="row">
                                                {existingCompanies.map(c => (
                                                    <button key={c} className="hrbm-btn sm" data-testid={`keep-only-${c}`} onClick={() => keepOnly(r => !r.plantId || r.plantCompanyCode === c)}>Manter só {c} ({(sel.plantCompanies.get(c) ?? 0) + sel.withoutPlant})</button>
                                                ))}
                                            </div>
                                        </div>
                                    ) : (
                                        <>
                                            {!team.plantId && existingCompanies.length === 0 && (
                                                <select data-testid="team-department-company" value={team.departmentCompany} onChange={e => setTeam(prev => ({ ...prev, departmentCompany: e.target.value, departmentMasterId: null, departmentMasterName: '' }))}>
                                                    <option value="">Empresa do departamento…</option>
                                                    {ctx?.companyMap.map(c => <option key={c.companyCode} value={c.companyCode}>{c.companyCode} — {c.departmentMasterCompanyCode}</option>)}
                                                </select>
                                            )}
                                            <DepartmentMasterAutocomplete
                                                initialId={team.departmentMasterId}
                                                initialName={team.departmentMasterName}
                                                allowedCompanyCode={departmentCompanyLabel}
                                                plantNotSelected={!departmentCompanyLabel}
                                                disabled={!departmentCompanyLabel}
                                                placeholder={departmentCompanyLabel ? 'Manter o departamento atual…' : 'Escolha a planta ou a empresa primeiro'}
                                                onChange={(id, name) => setTeam(prev => ({ ...prev, departmentMasterId: id, departmentMasterName: name }))}
                                            />
                                            {team.departmentMasterId && sel.withDepartment > 0 && (
                                                <label className="check"><input type="checkbox" data-testid="team-overwrite-department" checked={team.overwriteDepartment} onChange={e => setTeam(prev => ({ ...prev, overwriteDepartment: e.target.checked }))} /> Substituir o departamento dos {sel.withDepartment} que já têm departamento</label>
                                            )}
                                            {!team.plantId && existingCompanies.length === 1 && <p className="hint">Departamentos filtrados pela empresa das plantas já atribuídas ({existingCompanies[0]}).{sel.withoutPlant > 0 ? ` Os ${sel.withoutPlant} sem planta recebem o departamento; a planta terá de ser da mesma empresa.` : ''}</p>}
                                        </>
                                    )}
                                </div>

                                <div className="field">
                                    <label>Responsável / chefe</label>
                                    <select data-testid="team-manager" value={team.managerUserId} onChange={e => setTeam(prev => ({ ...prev, managerUserId: e.target.value }))}>
                                        <option value="">Manter o responsável atual</option>
                                        {renderManagerOptions()}
                                    </select>
                                    {team.managerUserId && sel.withManager > 0 && (
                                        <label className="check"><input type="checkbox" data-testid="team-overwrite-manager" checked={team.overwriteManager} onChange={e => setTeam(prev => ({ ...prev, overwriteManager: e.target.checked }))} /> Substituir o responsável dos {sel.withManager} que já têm responsável</label>
                                    )}
                                </div>

                                <p className="hint">Campos deixados em "Manter…" não são alterados. Por omissão só se preenchem campos vazios; valores existentes só mudam com a opção "Substituir".</p>
                                <button className="hrbm-btn primary" data-testid="preview-team" disabled={selectedCount === 0 || !teamHasField || departmentConflict || previewing} onClick={previewTeam}>
                                    {previewing ? <RefreshCw size={14} className="hrbm-spin" /> : <Eye size={14} />} Pré-visualizar ({selectedCount})
                                </button>
                            </>
                        )}
                    </div>
                </aside>
            </div>

            {/* ─── Preview / confirmation modal ─── */}
            {(preview || previewing) && (
                <div className="hrbm-modal-bg">
                    <div className="hrbm-modal" data-testid="preview-modal">
                        <div className="head">
                            <h3>{previewPlan?.mode === 'CONFIRM_SUGGESTIONS' ? 'Confirmar sugestões de planta' : 'Atribuição em lote'} — pré-visualização</h3>
                            <button className="hrbm-btn sm" onClick={() => { setPreview(null); setPreviewPlan(null); setPreviewError(null); }} disabled={applying}><X size={14} /></button>
                        </div>
                        <div className="body">
                            {previewing && !preview && <div className="center"><RefreshCw size={20} className="hrbm-spin" /> A calcular…</div>}
                            {preview && (
                                <>
                                    <div className="summary" data-testid="preview-summary">
                                        <span className="pill">Selecionados <b>{preview.selected}</b></span>
                                        <span className="pill change">Serão atualizados <b data-testid="preview-willchange">{preview.willChange}</b></span>
                                        <span className="pill">Sem alteração <b>{preview.unchanged}</b></span>
                                        <span className={`pill ${preview.blocked > 0 ? 'blocked' : ''}`}>Bloqueados <b data-testid="preview-blocked">{preview.blocked}</b></span>
                                    </div>
                                    <div className="fieldsum">
                                        <span><Building size={13} /> Planta: <b>{preview.plantChanges}</b></span>
                                        <span><Layers size={13} /> Departamento: <b>{preview.departmentChanges}</b></span>
                                        <span><Users size={13} /> Responsável: <b>{preview.managerChanges}</b></span>
                                        {previewPlan?.mode === 'ASSIGN' && <span style={{ color: '#64748b' }}>{previewPlan.overwritePlant || previewPlan.overwriteDepartment || previewPlan.overwriteManager ? `Substituição explícita: ${[previewPlan.overwritePlant && 'planta', previewPlan.overwriteDepartment && 'departamento', previewPlan.overwriteManager && 'responsável'].filter(Boolean).join(', ')}` : 'Só campos vazios são preenchidos; valores existentes mantidos.'}</span>}
                                    </div>
                                    {preview.message && <div className={preview.blocked > 0 ? 'hrbm-conflict' : 'hrbm-warn'} data-testid="preview-message">{preview.message}</div>}
                                    {preview.conflicts.map(c => (
                                        <div key={c.code + c.message} className="hrbm-conflict" data-testid="preview-conflict">
                                            <div className="row">
                                                <span><AlertTriangle size={14} style={{ verticalAlign: 'middle', marginRight: 6 }} />{c.message}</span>
                                                <button className="hrbm-btn sm" onClick={() => removeBlockedAndRetry(c.employeeIds)} disabled={previewing || applying}>Remover da seleção ({c.count}) e recalcular</button>
                                            </div>
                                        </div>
                                    ))}
                                    {previewError && <div className="hrbm-conflict" data-testid="apply-error">{previewError}</div>}
                                    <table data-testid="preview-table">
                                        <thead>
                                            <tr><th>Funcionário</th><th>Planta</th><th>Departamento mestre</th><th>Responsável</th><th>Resultado</th></tr>
                                        </thead>
                                        <tbody>
                                            {preview.rows.map((r: HRBulkMappingPreviewRow) => (
                                                <tr key={r.employeeId} className={r.status.toLowerCase()} data-preview-row={r.employeeCode}>
                                                    <td><b>{r.fullName}</b><br /><span style={{ fontSize: 12, color: '#64748b' }}>#{r.employeeCode} · {r.innuxDepartmentName || '—'}</span></td>
                                                    <td>{renderDiff(r.current.plantName, r.proposed.plantName, r.changedFields.includes('PLANT'), r.preservedFields.includes('PLANT'))}</td>
                                                    <td>{renderDiff(r.current.departmentMasterName, r.proposed.departmentMasterName, r.changedFields.includes('DEPARTMENT'), r.preservedFields.includes('DEPARTMENT'))}</td>
                                                    <td>{renderDiff(r.current.managerName, r.proposed.managerName, r.changedFields.includes('MANAGER'), r.preservedFields.includes('MANAGER'))}</td>
                                                    <td>
                                                        {r.status === 'CHANGE' && <span className="hrbm-chip complete">Atualiza: {r.changedFields.map(fieldLabel).join(', ')}</span>}
                                                        {r.status === 'UNCHANGED' && <span className="hrbm-chip manual">Sem alteração</span>}
                                                        {r.status === 'BLOCKED' && <span className="hrbm-chip unmapped">Bloqueado</span>}
                                                        {r.reason && <div className="reason" style={r.status !== 'BLOCKED' ? { color: '#64748b' } : undefined}>{r.reason}</div>}
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </>
                            )}
                        </div>
                        <div className="foot">
                            <span style={{ fontSize: 12, color: '#64748b' }}>Ao confirmar, o servidor volta a validar o âmbito, os valores atuais e cada par planta/departamento; se algo mudou desde esta pré-visualização, nada é gravado.</span>
                            <div style={{ display: 'flex', gap: 10 }}>
                                <button className="hrbm-btn" onClick={() => { setPreview(null); setPreviewPlan(null); setPreviewError(null); }} disabled={applying}>Cancelar</button>
                                <button className="hrbm-btn success" data-testid="apply" onClick={applyPreview} disabled={!preview?.canApply || applying || previewing}>
                                    {applying ? <RefreshCw size={14} className="hrbm-spin" /> : <Save size={14} />} Confirmar e gravar ({preview?.willChange ?? 0})
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
