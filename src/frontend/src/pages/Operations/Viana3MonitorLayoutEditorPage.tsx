/**
 * Viana 3 Production Monitor — TV Layout Editor (Portal, computer)
 *
 * Route: /operations/viana3-monitor/layout[?screen=<screenKey>]  (OPERATIONS / System Administrator)
 *
 * Manages the SHARED TV screens of the read-only monitor:
 *   • create / rename / delete named screens (e.g. "Envase 1", "Envase 2"); the default screen
 *     backs the historical TV URL and can only be renamed or reset;
 *   • per screen: which machines appear (add / remove), their order (drag or ↑ / ↓), a practical
 *     size per card (Compacto 1×1, Normal 1×1, Largo 2×1, Grande 2×2), hidden cards (restore
 *     later), the number of grid columns and whether newly discovered machines are included;
 *   • preview the exact 1920×1080 composition with live data before saving;
 *   • copy the dedicated read-only TV URL of each screen.
 *
 * Persistence: Portal database (OperationsBoardLayouts, one row per screen), so a layout saved
 * here is what every TV and browser shows. Nothing is kept in browser storage. Hiding or
 * removing a card is a display decision only — machine data and AlplaPROD are never touched.
 */

import { useState, useEffect, useCallback, useMemo, useRef, type CSSProperties } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
    LayoutGrid, Eye, EyeOff, ArrowUp, ArrowDown, GripVertical, RotateCcw, Save, X, ExternalLink, RefreshCw,
    Plus, Pencil, Trash2, Link as LinkIcon, Check, MonitorPlay, PlusCircle, MinusCircle,
} from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { Feedback } from '../../components/ui/Feedback';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { useUnsavedChangesWarning } from '../../hooks/useUnsavedChangesWarning';
import { useEnvironment } from '../../contexts/EnvironmentContext';
import { ApiError } from '../../lib/api';
import {
    fetchViana3Monitor, fetchViana3MonitorScreens, fetchViana3MonitorScreenLayout,
    createViana3MonitorScreen, renameViana3MonitorScreen, saveViana3MonitorScreenLayout,
    resetViana3MonitorScreenLayout, deleteViana3MonitorScreen,
} from '../../lib/operationsApi';
import type {
    Viana3MonitorResponse, Viana3MonitorLayout, Viana3MonitorScreen, Viana3CardSize, Viana3MachinePanel,
} from '../../types/operations.types';
import type { Viana3CardDesignBundle } from '../../types/viana3CardDesign.types';
import { fetchViana3CardDesignBundle } from '../../lib/viana3CardDesignApi';
import {
    Viana3Board, Viana3Stage, injectViana3CSS, getFreshness, clampRefresh, DEFAULT_REFRESH, FIT_LEVEL_LABEL, type FitLevel,
} from './viana3/Viana3Board';
import {
    resolveLayout, placeCards, visibleCards, toLayoutCards, sizeSpec, machinesOffScreen,
    CARD_SIZES, MIN_COLUMNS, MAX_COLUMNS, STAGE_WIDTH, STAGE_HEIGHT, type ResolvedLayout,
} from './viana3/viana3Layout';

// ─── Draft model ───

interface DraftCard {
    machineId: number;
    size: Viana3CardSize;
    hidden: boolean;
}

interface Draft {
    columns: number;
    includeNewMachines: boolean;
    cards: DraftCard[];
}

function draftFrom(layout: Viana3MonitorLayout | null, machines: Viana3MachinePanel[]): Draft {
    const r = resolveLayout(layout, machines);
    return {
        columns: r.columns,
        includeNewMachines: layout?.includeNewMachines ?? true,
        cards: r.cards.map(c => ({ machineId: c.machineId, size: c.size, hidden: c.hidden })),
    };
}

function draftsEqual(a: Draft, b: Draft): boolean {
    if (a.columns !== b.columns || a.includeNewMachines !== b.includeNewMachines || a.cards.length !== b.cards.length) return false;
    for (let i = 0; i < a.cards.length; i++) {
        const x = a.cards[i], y = b.cards[i];
        if (x.machineId !== y.machineId || x.size !== y.size || x.hidden !== y.hidden) return false;
    }
    return true;
}

type FitMap = Record<number, { level: FitLevel; forced: boolean; shrink: number }>;

const PREVIEW_REFRESH_MS = DEFAULT_REFRESH * 1000;
const DEFAULT_SCREEN_KEY = 'default';

function tvUrl(path: string): string {
    return `${window.location.origin}${path}?fullscreen=true`;
}

// ─── Small UI helpers (Portal light theme) ───

const label: CSSProperties = { fontSize: '12px', fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' };
const section: CSSProperties = { border: '1px solid var(--color-border)', borderRadius: 12, background: 'var(--color-bg-surface)', padding: 14 };
const iconBtn: CSSProperties = {
    display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 30, height: 30,
    border: '1px solid var(--color-border)', borderRadius: 8, background: 'var(--color-bg-surface)', color: 'var(--color-text-main)', cursor: 'pointer',
};
const smallBtn: CSSProperties = { padding: '6px 12px', fontSize: 12 };
const chip = (color: string): CSSProperties => ({
    display: 'inline-block', padding: '2px 8px', borderRadius: 999, fontSize: 11, fontWeight: 700,
    color, background: `${color}1a`, border: `1px solid ${color}55`, whiteSpace: 'nowrap',
});

export default function Viana3MonitorLayoutEditorPage() {
    const { isTest } = useEnvironment();
    const [searchParams, setSearchParams] = useSearchParams();

    // Live data (same endpoint as the TV) — used for machine names and for the preview.
    const [data, setData] = useState<Viana3MonitorResponse | null>(null);
    const [dataError, setDataError] = useState<string | null>(null);
    const [lastSuccessMs, setLastSuccessMs] = useState<number | null>(null);
    const [isFetchingData, setIsFetchingData] = useState(false);

    // Card designs (template + overrides) for the preview — read-only here; edited in the card-design editor.
    const [designs, setDesigns] = useState<Viana3CardDesignBundle | null>(null);
    useEffect(() => { fetchViana3CardDesignBundle().then(setDesigns).catch(() => setDesigns(null)); }, []);

    // Screens
    const [screens, setScreens] = useState<Viana3MonitorScreen[]>([]);
    const [selectedKey, setSelectedKey] = useState<string>(searchParams.get('screen') || DEFAULT_SCREEN_KEY);
    const [saved, setSaved] = useState<Viana3MonitorLayout | null>(null);
    const [draft, setDraft] = useState<Draft | null>(null);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [isBusy, setIsBusy] = useState(false);
    const [feedback, setFeedback] = useState<{ type: 'success' | 'error' | 'info'; message: string } | null>(null);

    // Dialogs / inline forms
    const [showResetDialog, setShowResetDialog] = useState(false);
    const [showCancelDialog, setShowCancelDialog] = useState(false);
    const [showDeleteDialog, setShowDeleteDialog] = useState(false);
    const [pendingSwitch, setPendingSwitch] = useState<string | null>(null);
    const [newScreenOpen, setNewScreenOpen] = useState(false);
    const [newScreenName, setNewScreenName] = useState('');
    const [newScreenCopy, setNewScreenCopy] = useState(false);
    const [renaming, setRenaming] = useState(false);
    const [renameValue, setRenameValue] = useState('');
    const [copiedUrl, setCopiedUrl] = useState(false);

    // Preview
    const [zoom, setZoom] = useState<'fit' | 0.5 | 1>('fit');
    const [fit, setFit] = useState<FitMap>({});
    const previewBoxRef = useRef<HTMLDivElement>(null);
    const [previewWidth, setPreviewWidth] = useState(960);

    // DnD
    const dragIndexRef = useRef<number | null>(null);
    const [overIndex, setOverIndex] = useState<number | null>(null);

    useEffect(() => { injectViana3CSS(); }, []);

    const loadData = useCallback(async () => {
        setIsFetchingData(true);
        try {
            const d = await fetchViana3Monitor();
            setData(d);
            setLastSuccessMs(Date.now());
            setDataError(null);
            return d;
        } catch (err: unknown) {
            setDataError(err instanceof Error ? err.message : 'Erro de conexão');
            return null;
        } finally {
            setIsFetchingData(false);
        }
    }, []);

    const loadScreens = useCallback(async () => {
        const list = await fetchViana3MonitorScreens();
        setScreens(list);
        return list;
    }, []);

    /** Loads one screen's saved layout and rebuilds the draft from it. */
    const loadScreen = useCallback(async (key: string, machines: Viana3MachinePanel[]) => {
        const l = await fetchViana3MonitorScreenLayout(key);
        setSaved(l);
        setDraft(draftFrom(l, machines));
        setFit({});
        fitRef.current = {};
        return l;
    }, []);

    // Initial load: data + screens + selected screen.
    useEffect(() => {
        let cancelled = false;
        (async () => {
            setIsLoading(true);
            try {
                const [d, s] = await Promise.allSettled([fetchViana3Monitor(), fetchViana3MonitorScreens()]);
                if (cancelled) return;
                let machines: Viana3MachinePanel[] = [];
                if (d.status === 'fulfilled') { setData(d.value); setLastSuccessMs(Date.now()); machines = d.value.machines; }
                else setDataError(d.reason instanceof Error ? d.reason.message : 'Erro de conexão');
                if (s.status === 'fulfilled') {
                    setScreens(s.value);
                    const wanted = searchParams.get('screen') || DEFAULT_SCREEN_KEY;
                    const key = s.value.some(x => x.screenKey === wanted) ? wanted : DEFAULT_SCREEN_KEY;
                    setSelectedKey(key);
                    try {
                        await loadScreen(key, machines);
                    } catch (err: unknown) {
                        if (!cancelled) setLoadError(err instanceof Error ? err.message : 'Não foi possível carregar o ecrã.');
                    }
                } else {
                    setLoadError(s.reason instanceof Error ? s.reason.message : 'Não foi possível carregar os ecrãs.');
                }
            } finally {
                if (!cancelled) setIsLoading(false);
            }
        })();
        return () => { cancelled = true; };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    // Keep the preview data alive (same cadence as the TV default).
    useEffect(() => {
        const t = setInterval(loadData, PREVIEW_REFRESH_MS);
        return () => clearInterval(t);
    }, [loadData]);

    // Keep ?screen= in the URL so a screen can be deep-linked / refreshed.
    useEffect(() => {
        const current = searchParams.get('screen') || DEFAULT_SCREEN_KEY;
        if (current === selectedKey) return;
        const next = new URLSearchParams(searchParams);
        if (selectedKey === DEFAULT_SCREEN_KEY) next.delete('screen'); else next.set('screen', selectedKey);
        setSearchParams(next, { replace: true });
    }, [selectedKey, searchParams, setSearchParams]);

    // Machines discovered after the draft was built are appended visible when the screen includes new machines.
    useEffect(() => {
        if (!data || !draft || !draft.includeNewMachines) return;
        const known = new Set(draft.cards.map(c => c.machineId));
        const missing = data.machines.filter(m => !known.has(m.machineId));
        if (missing.length === 0) return;
        setDraft(d => d ? { ...d, cards: [...d.cards, ...missing.map(m => ({ machineId: m.machineId, size: 'STANDARD' as Viana3CardSize, hidden: false }))] } : d);
    }, [data, draft]);

    // Preview box width → scale.
    useEffect(() => {
        const el = previewBoxRef.current;
        if (!el) return;
        const update = () => setPreviewWidth(el.clientWidth);
        update();
        const ro = new ResizeObserver(update);
        ro.observe(el);
        return () => ro.disconnect();
    }, [isLoading]);

    const machines = useMemo(() => data?.machines ?? [], [data]);
    const machineById = useMemo(() => new Map(machines.map(m => [m.machineId, m])), [machines]);

    const savedDraft = useMemo(() => draftFrom(saved, machines), [saved, machines]);
    const isDirty = !!draft && !draftsEqual(draft, savedDraft);
    useUnsavedChangesWarning({ isDirty, isSubmitted: false });

    const resolvedPreview: ResolvedLayout | null = useMemo(() => {
        if (!draft) return null;
        return {
            columns: draft.columns,
            cards: draft.cards.map(c => ({ ...c, panel: machineById.get(c.machineId) ?? null, fromLayout: true })),
        };
    }, [draft, machineById]);

    const previewRows = useMemo(() => resolvedPreview ? placeCards(visibleCards(resolvedPreview), resolvedPreview.columns).rows : 0, [resolvedPreview]);

    // Fit results reported by the panels. The ref mirrors the state so an unchanged report
    // never reaches setState (a state update from a child's layout effect must not be issued
    // on every render: React may re-apply queued updaters against an older base state).
    const fitRef = useRef<FitMap>({});
    const onCardFit = useCallback((machineId: number, level: FitLevel, forced: boolean, shrink: number) => {
        const cur = fitRef.current[machineId];
        if (cur && cur.level === level && cur.forced === forced && cur.shrink === shrink) return;
        fitRef.current = { ...fitRef.current, [machineId]: { level, forced, shrink } };
        setFit(fitRef.current);
    }, []);

    // ─── Draft mutations ───

    const updateCards = (fn: (cards: DraftCard[]) => DraftCard[]) => setDraft(d => d ? { ...d, cards: fn(d.cards) } : d);

    const move = (from: number, to: number) => {
        if (from === to) return;
        updateCards(cards => {
            const next = [...cards];
            const [item] = next.splice(from, 1);
            next.splice(to, 0, item);
            return next;
        });
    };
    const setSize = (machineId: number, size: Viana3CardSize) => updateCards(cards => cards.map(c => c.machineId === machineId ? { ...c, size } : c));
    const setHidden = (machineId: number, hidden: boolean) => updateCards(cards => cards.map(c => c.machineId === machineId ? { ...c, hidden } : c));
    const addMachine = (machineId: number) => updateCards(cards => cards.some(c => c.machineId === machineId) ? cards : [...cards, { machineId, size: 'STANDARD', hidden: false }]);
    const removeMachine = (machineId: number) => updateCards(cards => cards.filter(c => c.machineId !== machineId));
    const setColumns = (columns: number) => setDraft(d => d ? { ...d, columns } : d);
    const setIncludeNew = (includeNewMachines: boolean) => setDraft(d => d ? { ...d, includeNewMachines } : d);

    // ─── Screen actions ───

    const selectScreen = async (key: string) => {
        if (key === selectedKey) return;
        if (isDirty) { setPendingSwitch(key); return; }
        await doSelectScreen(key);
    };

    const doSelectScreen = async (key: string) => {
        setPendingSwitch(null);
        setFeedback(null);
        setRenaming(false);
        setCopiedUrl(false);
        setIsBusy(true);
        try {
            await loadScreen(key, machines);
            setSelectedKey(key);
        } catch (err: unknown) {
            setFeedback({ type: 'error', message: err instanceof ApiError && err.status === 404 ? 'Esse ecrã já não existe.' : 'Não foi possível carregar o ecrã.' });
            await loadScreens().catch(() => undefined);
        } finally {
            setIsBusy(false);
        }
    };

    const handleCreateScreen = async () => {
        const name = newScreenName.trim();
        if (!name) return;
        setIsBusy(true);
        setFeedback(null);
        try {
            const created = await createViana3MonitorScreen({ name, copyFromScreenKey: newScreenCopy ? selectedKey : null });
            await loadScreens();
            setSaved(created);
            setDraft(draftFrom(created, machines));
            setFit({}); fitRef.current = {};
            setSelectedKey(created.screenKey);
            setNewScreenOpen(false);
            setNewScreenName('');
            setNewScreenCopy(false);
            setFeedback({ type: 'success', message: `Ecrã «${created.name}» criado. Selecione as máquinas e guarde o layout.` });
        } catch (err: unknown) {
            setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível criar o ecrã.' });
        } finally {
            setIsBusy(false);
        }
    };

    const handleRename = async () => {
        const name = renameValue.trim();
        if (!name || !saved) return;
        setIsBusy(true);
        setFeedback(null);
        try {
            await renameViana3MonitorScreen(saved.screenKey, { name });
            await loadScreens();
            setSaved(s => s ? { ...s, name, isPersisted: true } : s);
            setRenaming(false);
            setFeedback({ type: 'success', message: `Ecrã renomeado para «${name}».` });
        } catch (err: unknown) {
            setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível renomear o ecrã.' });
        } finally {
            setIsBusy(false);
        }
    };

    const handleDelete = async () => {
        setShowDeleteDialog(false);
        if (!saved || saved.isDefault) return;
        setIsBusy(true);
        setFeedback(null);
        try {
            await deleteViana3MonitorScreen(saved.screenKey);
            const name = saved.name;
            await loadScreens();
            await loadScreen(DEFAULT_SCREEN_KEY, machines);
            setSelectedKey(DEFAULT_SCREEN_KEY);
            setFeedback({ type: 'success', message: `Ecrã «${name}» eliminado. A TV que o usava passa a mostrar «Ecrã da TV não encontrado».` });
        } catch (err: unknown) {
            setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível eliminar o ecrã.' });
        } finally {
            setIsBusy(false);
        }
    };

    const handleCopyUrl = async () => {
        if (!saved) return;
        const url = tvUrl(saved.tvPath);
        try {
            await navigator.clipboard.writeText(url);
            setCopiedUrl(true);
            setTimeout(() => setCopiedUrl(false), 2500);
        } catch {
            setFeedback({ type: 'info', message: `Copie o URL manualmente: ${url}` });
        }
    };

    // ─── Layout actions ───

    const handleSave = async () => {
        if (!draft || !saved) return;
        setIsBusy(true);
        setFeedback(null);
        try {
            const result = await saveViana3MonitorScreenLayout(saved.screenKey, {
                columns: draft.columns, cards: toLayoutCards(draft.cards), includeNewMachines: draft.includeNewMachines,
            });
            setSaved(result);
            setDraft(draftFrom(result, machines));
            await loadScreens().catch(() => undefined);
            setFeedback({ type: 'success', message: `Layout de «${result.name}» guardado. As TVs aplicam-no na próxima atualização (até 5 min).` });
        } catch (err: unknown) {
            const msg = err instanceof ApiError
                ? (err.status === 403 || err.status === 401 ? 'Sem permissão para alterar o layout da TV.' : err.message)
                : 'Não foi possível guardar o layout.';
            setFeedback({ type: 'error', message: msg });
        } finally {
            setIsBusy(false);
        }
    };

    const handleCancel = async () => {
        setShowCancelDialog(false);
        setFeedback(null);
        try {
            await loadScreen(selectedKey, machines);
            setFeedback({ type: 'info', message: 'Alterações descartadas. A mostrar o layout guardado.' });
        } catch {
            setDraft(draftFrom(saved, machines));
        }
    };

    const handleReset = async () => {
        setShowResetDialog(false);
        if (!saved) return;
        setIsBusy(true);
        setFeedback(null);
        try {
            const result = await resetViana3MonitorScreenLayout(saved.screenKey);
            setSaved(result);
            setDraft(draftFrom(result, machines));
            await loadScreens().catch(() => undefined);
            setFeedback({
                type: 'success',
                message: result.isDefault
                    ? 'Layout reposto: ordem de descoberta, todos os cartões visíveis, tamanho normal.'
                    : `Seleção de «${result.name}» esvaziada. Adicione máquinas e guarde.`,
            });
        } catch (err: unknown) {
            setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível repor o layout.' });
        } finally {
            setIsBusy(false);
        }
    };

    // ─── Render ───

    if (isLoading) {
        return (
            <PageContainer>
                <PageHeader title="Monitor Viana 3 — Layout da TV" icon={<LayoutGrid size={22} />} />
                <div style={{ color: 'var(--color-text-muted)', padding: 24 }}>A carregar…</div>
            </PageContainer>
        );
    }

    if (!draft || !saved) {
        return (
            <PageContainer>
                <PageHeader title="Monitor Viana 3 — Layout da TV" icon={<LayoutGrid size={22} />} />
                <Feedback type="error" message={loadError ?? 'Não foi possível carregar os ecrãs da TV.'} autoCloseMs={0} />
            </PageContainer>
        );
    }

    const visible = draft.cards.filter(c => !c.hidden);
    const hidden = draft.cards.filter(c => c.hidden);
    const offScreen = machinesOffScreen(draft.cards, machines);
    const previewScale = zoom === 'fit' ? Math.max(0.1, Math.min(1, previewWidth / STAGE_WIDTH)) : zoom;
    const freshness = getFreshness(data, lastSuccessMs, !data);
    const fitWarnings = visible
        .map(c => ({ c, f: fit[c.machineId] }))
        .filter(x => x.f && (x.f.level > (x.c.size === 'COMPACT' ? 2 : 0) || x.f.forced || x.f.shrink < 1));
    const currentUrl = tvUrl(saved.tvPath);

    const savedInfo = !saved.isPersisted
        ? (saved.isDefault
            ? 'Layout predefinido (nada guardado): ordem de descoberta, todos os cartões visíveis.'
            : 'Ecrã ainda sem layout guardado.')
        : `Guardado ${saved.updatedAtUtc ? new Date(saved.updatedAtUtc).toLocaleString('pt-PT') : ''}${saved.updatedByName ? ` por ${saved.updatedByName}` : ''}.`;

    return (
        <PageContainer maxWidth="1700px">
            <PageHeader
                title="Monitor Viana 3 — Layout da TV"
                subtitle={<span>Ecrãs partilhados por todas as TVs e computadores. Cada TV abre o URL dedicado do seu ecrã.</span>}
                icon={<LayoutGrid size={22} />}
                actions={
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                        <button type="button" className="btn-secondary" onClick={() => setShowCancelDialog(true)} disabled={isBusy || !isDirty}>
                            <X size={16} /> Cancelar
                        </button>
                        <button type="button" className="btn-primary" onClick={handleSave} disabled={isBusy || !isDirty}>
                            <Save size={16} /> {isBusy ? 'A processar…' : 'Guardar layout'}
                        </button>
                    </div>
                }
            />

            {feedback && <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(null)} />}
            {dataError && (
                <Feedback type="warning" message={`Dados do AlplaPROD indisponíveis para a pré-visualização: ${dataError}. Os ecrãs podem ser editados na mesma.`} autoCloseMs={0} />
            )}

            {/* ── Screens bar ── */}
            <section style={{ ...section, marginBottom: 16 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap', marginBottom: 10 }}>
                    <div style={label}>Ecrãs da TV ({screens.length})</div>
                    <button type="button" className="btn-secondary" style={smallBtn} onClick={() => { setNewScreenOpen(o => !o); setNewScreenName(''); }} disabled={isBusy}>
                        <Plus size={14} /> Novo ecrã
                    </button>
                </div>
                <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }} data-testid="screen-list">
                    {screens.map(s => {
                        const active = s.screenKey === selectedKey;
                        return (
                            <button
                                key={s.screenKey}
                                type="button"
                                onClick={() => selectScreen(s.screenKey)}
                                disabled={isBusy}
                                data-screen-key={s.screenKey}
                                style={{
                                    display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 2, minWidth: 180,
                                    padding: '8px 12px', borderRadius: 10, cursor: 'pointer', textAlign: 'left',
                                    border: active ? '2px solid var(--color-primary)' : '1px solid var(--color-border)',
                                    background: active ? 'rgba(var(--color-primary-rgb), 0.06)' : 'var(--color-bg-page)',
                                    color: 'var(--color-text-main)',
                                }}
                            >
                                <span style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 800, fontSize: 14 }}>
                                    <MonitorPlay size={14} /> {s.name}
                                    {s.isDefault && <span style={chip('#2563eb')}>URL original</span>}
                                </span>
                                <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                                    {s.isDefault && !s.isPersisted
                                        ? 'todas as máquinas (descoberta)'
                                        : `${s.visibleCount} visíveis · ${s.cardCount - s.visibleCount} ocultos · ${s.columns} col.${s.includeNewMachines ? ' · inclui novas' : ''}`}
                                </span>
                            </button>
                        );
                    })}
                </div>
                {newScreenOpen && (
                    <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap', marginTop: 12, padding: 10, borderRadius: 10, border: '1px dashed var(--color-border)' }}>
                        <input
                            type="text" placeholder="Nome do ecrã (ex.: Envase 1)" value={newScreenName} maxLength={80}
                            onChange={e => setNewScreenName(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') handleCreateScreen(); }}
                            style={{ minWidth: 260 }} data-testid="new-screen-name"
                        />
                        <label style={{ fontSize: 13, display: 'flex', alignItems: 'center', gap: 6 }}>
                            <input type="checkbox" checked={newScreenCopy} onChange={e => setNewScreenCopy(e.target.checked)} />
                            Copiar máquinas e layout de «{saved.name}»
                        </label>
                        <button type="button" className="btn-primary" style={smallBtn} onClick={handleCreateScreen} disabled={isBusy || !newScreenName.trim()} data-testid="new-screen-create">
                            <Check size={14} /> Criar
                        </button>
                        <button type="button" className="btn-secondary" style={smallBtn} onClick={() => setNewScreenOpen(false)}>Fechar</button>
                    </div>
                )}
            </section>

            {/* ── Selected screen header ── */}
            <section style={{ ...section, marginBottom: 16 }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
                    <div style={{ minWidth: 0 }}>
                        {renaming ? (
                            <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                                <input type="text" value={renameValue} maxLength={80} onChange={e => setRenameValue(e.target.value)}
                                    onKeyDown={e => { if (e.key === 'Enter') handleRename(); if (e.key === 'Escape') setRenaming(false); }}
                                    style={{ minWidth: 260 }} autoFocus data-testid="rename-input" />
                                <button type="button" className="btn-primary" style={smallBtn} onClick={handleRename} disabled={isBusy || !renameValue.trim()}><Check size={14} /> Guardar nome</button>
                                <button type="button" className="btn-secondary" style={smallBtn} onClick={() => setRenaming(false)}>Cancelar</button>
                            </div>
                        ) : (
                            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                                <span style={{ fontSize: 18, fontWeight: 800 }} data-testid="screen-name">{saved.name}</span>
                                {saved.isDefault && <span style={chip('#2563eb')}>ecrã principal · URL original</span>}
                                {isDirty && <span style={chip('#b45309')}>alterações por guardar</span>}
                                <button type="button" style={iconBtn} title="Renomear ecrã" onClick={() => { setRenameValue(saved.name); setRenaming(true); }} disabled={isBusy}><Pencil size={14} /></button>
                            </div>
                        )}
                        <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginTop: 4 }}>{savedInfo}</div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 8, flexWrap: 'wrap' }}>
                            <span style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>URL da TV:</span>
                            <input type="text" readOnly value={currentUrl} onFocus={e => e.currentTarget.select()} style={{ minWidth: 380, fontSize: 12, fontFamily: 'monospace' }} data-testid="tv-url" />
                            <button type="button" className="btn-secondary" style={smallBtn} onClick={handleCopyUrl}>
                                {copiedUrl ? <><Check size={14} /> Copiado</> : <><LinkIcon size={14} /> Copiar URL da TV</>}
                            </button>
                            <a className="btn-secondary" style={{ ...smallBtn, textDecoration: 'none' }} href={currentUrl} target="_blank" rel="noopener noreferrer">
                                <ExternalLink size={14} /> Abrir TV
                            </a>
                        </div>
                    </div>
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                        <button type="button" className="btn-secondary" style={smallBtn} onClick={() => setShowResetDialog(true)} disabled={isBusy || (!saved.isPersisted && saved.isDefault)}>
                            <RotateCcw size={14} /> {saved.isDefault ? 'Repor predefinido' : 'Esvaziar seleção'}
                        </button>
                        {!saved.isDefault && (
                            <button type="button" className="btn-secondary" style={{ ...smallBtn, color: '#b91c1c', borderColor: '#b91c1c' }} onClick={() => setShowDeleteDialog(true)} disabled={isBusy} data-testid="delete-screen">
                                <Trash2 size={14} /> Eliminar ecrã
                            </button>
                        )}
                    </div>
                </div>
            </section>

            <div style={{ display: 'grid', gridTemplateColumns: 'minmax(360px, 440px) minmax(0, 1fr)', gap: 20, alignItems: 'start' }}>
                {/* ── Left: card list ── */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                    <section style={section}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
                            <div style={label}>Grelha</div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
                                <span>Colunas</span>
                                <select value={draft.columns} onChange={e => setColumns(parseInt(e.target.value, 10))} style={{ width: 70 }} data-testid="columns-select">
                                    {Array.from({ length: MAX_COLUMNS - MIN_COLUMNS + 1 }, (_, i) => MIN_COLUMNS + i).map(n => (
                                        <option key={n} value={n}>{n}</option>
                                    ))}
                                </select>
                            </div>
                        </div>
                        <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                            {visible.length} cartões visíveis em {draft.columns} colunas → {previewRows} linha{previewRows === 1 ? '' : 's'} na TV (1920×1080).
                            {previewRows >= 3 && ' Com 3 ou mais linhas os cartões ficam baixos: o conteúdo é reduzido automaticamente.'}
                        </div>
                        <label style={{ fontSize: 13, display: 'flex', alignItems: 'center', gap: 6, marginTop: 10 }}>
                            <input type="checkbox" checked={draft.includeNewMachines} onChange={e => setIncludeNew(e.target.checked)} data-testid="include-new" />
                            Incluir automaticamente máquinas novas (e as que não estão neste ecrã)
                        </label>
                    </section>

                    <section style={section}>
                        <div style={{ ...label, marginBottom: 10 }}>Cartões na TV (arraste para ordenar)</div>
                        {visible.length === 0 && (
                            <div style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>Nenhum cartão visível. Adicione máquinas abaixo ou restaure um cartão oculto.</div>
                        )}
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                            {draft.cards.map((c, idx) => {
                                if (c.hidden) return null;
                                const m = machineById.get(c.machineId);
                                const f = fit[c.machineId];
                                const warn = f && (f.level > (c.size === 'COMPACT' ? 2 : 0) || f.forced || f.shrink < 1);
                                const spec = sizeSpec(c.size);
                                return (
                                    <div
                                        key={c.machineId}
                                        draggable
                                        data-machine-id={c.machineId}
                                        onDragStart={e => { dragIndexRef.current = idx; e.dataTransfer.effectAllowed = 'move'; }}
                                        onDragOver={e => { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; if (overIndex !== idx) setOverIndex(idx); }}
                                        onDragLeave={() => { if (overIndex === idx) setOverIndex(null); }}
                                        onDrop={e => { e.preventDefault(); const from = dragIndexRef.current; dragIndexRef.current = null; setOverIndex(null); if (from !== null) move(from, idx); }}
                                        onDragEnd={() => { dragIndexRef.current = null; setOverIndex(null); }}
                                        style={{
                                            display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr) auto', gap: 10, alignItems: 'center',
                                            padding: '8px 10px', borderRadius: 10,
                                            border: overIndex === idx ? '2px dashed var(--color-primary)' : '1px solid var(--color-border)',
                                            background: 'var(--color-bg-page)',
                                        }}
                                    >
                                        <span title="Arrastar" style={{ color: 'var(--color-text-muted)', cursor: 'grab', display: 'inline-flex' }}><GripVertical size={18} /></span>
                                        <div style={{ minWidth: 0 }}>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                                                <span style={{ fontWeight: 800, fontSize: 14, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} data-testid="card-name">
                                                    {m?.machineName ?? `Máquina ${c.machineId}`}
                                                </span>
                                                {!m && <span style={chip('#64748b')}>sem dados atuais</span>}
                                                {m && <span style={chip(m.statusCode === 'RUNNING' ? '#16a34a' : '#64748b')}>{m.statusLabel}</span>}
                                            </div>
                                            <div style={{ fontSize: 12, color: 'var(--color-text-muted)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                                {m?.current ? `Lote #${m.current.lotId} · ${m.current.bottleArticle ?? ''}` : m ? 'Sem lote planeado agora' : 'Aparece na TV quando voltar a ter planeamento'}
                                            </div>
                                            <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 6, flexWrap: 'wrap' }}>
                                                <select value={c.size} onChange={e => setSize(c.machineId, e.target.value as Viana3CardSize)} title={spec.hint} style={{ fontSize: 12, padding: '4px 8px' }}>
                                                    {CARD_SIZES.map(s => <option key={s.value} value={s.value}>{s.label} ({s.colSpan}×{s.rowSpan})</option>)}
                                                </select>
                                                {warn && (
                                                    <span style={chip('#b45309')} title="O cartão não tem altura para todo o conteúdo; use menos linhas, outro tamanho ou oculte cartões.">
                                                        ⚠ {f.forced ? 'conteúdo não cabe (cortado)' : FIT_LEVEL_LABEL[f.level]}{!f.forced && f.shrink < 1 ? ` · texto a ${Math.round(f.shrink * 100)} %` : ''}
                                                    </span>
                                                )}
                                            </div>
                                        </div>
                                        <div style={{ display: 'flex', gap: 4 }}>
                                            <button type="button" style={iconBtn} title="Subir" onClick={() => move(idx, Math.max(0, idx - 1))} disabled={idx === 0}><ArrowUp size={16} /></button>
                                            <button type="button" style={iconBtn} title="Descer" onClick={() => move(idx, Math.min(draft.cards.length - 1, idx + 1))} disabled={idx === draft.cards.length - 1}><ArrowDown size={16} /></button>
                                            <button type="button" style={iconBtn} title="Ocultar da TV (não apaga dados)" onClick={() => setHidden(c.machineId, true)}><EyeOff size={16} /></button>
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                    </section>

                    <section style={section}>
                        <div style={{ ...label, marginBottom: 10 }}>Cartões ocultos ({hidden.length})</div>
                        {hidden.length === 0 ? (
                            <div style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>Nenhum cartão oculto. Ocultar remove o cartão da TV sem apagar dados da máquina.</div>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                                {hidden.map(c => {
                                    const m = machineById.get(c.machineId);
                                    return (
                                        <div key={c.machineId} data-hidden-machine-id={c.machineId} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10, padding: '8px 10px', borderRadius: 10, border: '1px dashed var(--color-border)', opacity: 0.85 }}>
                                            <div style={{ minWidth: 0 }}>
                                                <div style={{ fontWeight: 700, fontSize: 14, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{m?.machineName ?? `Máquina ${c.machineId}`}</div>
                                                <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{m ? m.statusLabel : 'sem dados atuais'}</div>
                                            </div>
                                            <div style={{ display: 'flex', gap: 6 }}>
                                                <button type="button" className="btn-secondary" style={smallBtn} onClick={() => setHidden(c.machineId, false)}>
                                                    <Eye size={14} /> Restaurar
                                                </button>
                                                <button type="button" style={iconBtn} title={draft.includeNewMachines ? 'Desligue «Incluir automaticamente» para remover máquinas deste ecrã' : 'Remover deste ecrã (não apaga dados)'}
                                                    onClick={() => removeMachine(c.machineId)} disabled={draft.includeNewMachines}>
                                                    <MinusCircle size={16} />
                                                </button>
                                            </div>
                                        </div>
                                    );
                                })}
                            </div>
                        )}
                    </section>

                    <section style={section}>
                        <div style={{ ...label, marginBottom: 10 }}>Máquinas fora deste ecrã ({offScreen.length})</div>
                        {draft.includeNewMachines ? (
                            <div style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>Este ecrã inclui automaticamente todas as máquinas descobertas. Desligue a opção na grelha para escolher máquinas uma a uma.</div>
                        ) : offScreen.length === 0 ? (
                            <div style={{ fontSize: 13, color: 'var(--color-text-muted)' }}>Todas as máquinas com planeamento recente estão neste ecrã.</div>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                                {offScreen.map(m => (
                                    <div key={m.machineId} data-off-machine-id={m.machineId} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 10, padding: '8px 10px', borderRadius: 10, border: '1px solid var(--color-border)' }}>
                                        <div style={{ minWidth: 0 }}>
                                            <div style={{ fontWeight: 700, fontSize: 14, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{m.machineName}</div>
                                            <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{m.statusLabel}{m.current ? ` · Lote #${m.current.lotId}` : ''}</div>
                                        </div>
                                        <button type="button" className="btn-secondary" style={smallBtn} onClick={() => addMachine(m.machineId)}>
                                            <PlusCircle size={14} /> Adicionar
                                        </button>
                                    </div>
                                ))}
                            </div>
                        )}
                    </section>
                </div>

                {/* ── Right: preview ── */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: 10, minWidth: 0 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
                        <div style={label}>
                            Pré-visualização «{saved.name}» {STAGE_WIDTH}×{STAGE_HEIGHT} · escala {(previewScale * 100).toFixed(0)} %
                            {isDirty && <span style={{ ...chip('#b45309'), marginLeft: 8 }}>por guardar</span>}
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
                            <span>Zoom</span>
                            <select value={String(zoom)} onChange={e => setZoom(e.target.value === 'fit' ? 'fit' : (parseFloat(e.target.value) as 0.5 | 1))} style={{ width: 110 }}>
                                <option value="fit">Ajustar</option>
                                <option value="0.5">50 %</option>
                                <option value="1">100 %</option>
                            </select>
                            <button type="button" style={iconBtn} title="Atualizar dados" onClick={loadData} disabled={isFetchingData}><RefreshCw size={16} /></button>
                        </div>
                    </div>

                    <div ref={previewBoxRef} style={{
                        width: '100%', overflow: zoom === 'fit' ? 'hidden' : 'auto', borderRadius: 12, border: '1px solid var(--color-border)', background: '#0a0f1e',
                        height: zoom === 'fit' ? Math.round(previewWidth * STAGE_HEIGHT / STAGE_WIDTH) : Math.min(STAGE_HEIGHT * previewScale + 20, 900),
                    }}>
                        {data && resolvedPreview ? (
                            zoom === 'fit' ? (
                                <Viana3Stage background="#0a0f1e">
                                    <Viana3Board
                                        data={data}
                                        layout={resolvedPreview}
                                        freshness={freshness}
                                        countdown={0}
                                        refreshSeconds={clampRefresh(data.refreshSeconds || DEFAULT_REFRESH)}
                                        isFetching={isFetchingData}
                                        connectionLost={!!dataError}
                                        connectionMessage={dataError}
                                        isTest={isTest}
                                        screenName={saved.isDefault ? null : saved.name}
                                        designs={designs}
                                        onCardFit={onCardFit}
                                    />
                                </Viana3Stage>
                            ) : (
                                <div style={{ width: STAGE_WIDTH * previewScale, height: STAGE_HEIGHT * previewScale, position: 'relative' }}>
                                    <div style={{ position: 'absolute', left: 0, top: 0, width: STAGE_WIDTH, height: STAGE_HEIGHT, transform: `scale(${previewScale})`, transformOrigin: 'top left' }}>
                                        <Viana3Board
                                            data={data}
                                            layout={resolvedPreview}
                                            freshness={freshness}
                                            countdown={0}
                                            refreshSeconds={clampRefresh(data.refreshSeconds || DEFAULT_REFRESH)}
                                            isFetching={isFetchingData}
                                            connectionLost={!!dataError}
                                            connectionMessage={dataError}
                                            isTest={isTest}
                                            screenName={saved.isDefault ? null : saved.name}
                                            designs={designs}
                                            onCardFit={onCardFit}
                                        />
                                    </div>
                                </div>
                            )
                        ) : (
                            <div style={{ height: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#94a3b8', fontSize: 16, padding: 24, textAlign: 'center' }}>
                                Sem dados do AlplaPROD para pré-visualizar. A seleção, a ordem, os tamanhos e os cartões ocultos podem ser guardados na mesma.
                            </div>
                        )}
                    </div>

                    {fitWarnings.length > 0 && (
                        <div style={{ fontSize: 12, color: '#b45309' }}>
                            ⚠ {fitWarnings.length} {fitWarnings.length === 1 ? 'cartão' : 'cartões'} com conteúdo reduzido para caber. Para mostrar tudo: menos linhas (menos cartões visíveis ou mais colunas), tamanho «Grande», ou oculte cartões.
                        </div>
                    )}
                    <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>
                        A pré-visualização usa os mesmos dados ao vivo e as mesmas regras da TV (valores, desvio provisório, Waste separado, indicadores de atualização).
                        Numa TV com outra resolução a composição é apenas redimensionada; a disposição não muda. Uma máquina pode estar em vários ecrãs.
                    </div>
                </div>
            </div>

            <ConfirmationDialog
                isOpen={showResetDialog}
                title={saved.isDefault ? 'Repor o layout predefinido?' : `Esvaziar a seleção de «${saved.name}»?`}
                message={saved.isDefault
                    ? 'O layout guardado do ecrã principal é removido. As TVs com o URL original voltam à ordem de descoberta com todos os cartões visíveis em tamanho normal. Nenhum dado de produção é alterado.'
                    : 'Todas as máquinas são retiradas deste ecrã (o ecrã e o seu URL mantêm-se). Nenhum dado de produção é alterado.'}
                confirmText={saved.isDefault ? 'Repor' : 'Esvaziar'}
                variant="warning"
                onConfirm={handleReset}
                onCancel={() => setShowResetDialog(false)}
            />
            <ConfirmationDialog
                isOpen={showCancelDialog}
                title="Descartar alterações?"
                message="As alterações por guardar serão perdidas e o editor volta ao layout guardado."
                confirmText="Descartar"
                variant="warning"
                onConfirm={handleCancel}
                onCancel={() => setShowCancelDialog(false)}
            />
            <ConfirmationDialog
                isOpen={pendingSwitch !== null}
                title="Mudar de ecrã sem guardar?"
                message="As alterações por guardar neste ecrã serão perdidas."
                confirmText="Mudar e descartar"
                variant="warning"
                onConfirm={() => { if (pendingSwitch) doSelectScreen(pendingSwitch); }}
                onCancel={() => setPendingSwitch(null)}
            />
            <ConfirmationDialog
                isOpen={showDeleteDialog}
                title={`Eliminar o ecrã «${saved.name}»?`}
                message={`O ecrã e o seu layout são eliminados definitivamente. A TV que abre ${currentUrl} passa a mostrar «Ecrã da TV não encontrado». Nenhum dado de produção é alterado.`}
                confirmText="Eliminar ecrã"
                variant="destructive"
                onConfirm={handleDelete}
                onCancel={() => setShowDeleteDialog(false)}
            />
        </PageContainer>
    );
}
