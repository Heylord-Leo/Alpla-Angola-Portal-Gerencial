/**
 * Viana 3 Production Monitor — Machine-card design editor (Portal, computer)
 *
 * Route: /operations/viana3-monitor/card-design[?machine={id}|template]  (OPERATIONS / System Administrator)
 *
 * Separate from the "Layout TV" editor: screens keep deciding WHICH machines appear and
 * the order/size/visibility of their cards; this page designs the items INSIDE a machine
 * card, shared by every screen where the machine appears.
 *
 *   • Targets: "Modelo padrão" (board-wide template) and each machine. Resolution on the TV:
 *     own design → template → built-in card.
 *   • Canvas (600×440 design units, scaled): select an item, drag to move, handles to resize,
 *     rotate handle on the illustration / images / free text, arrow keys to nudge.
 *   • Properties: visibility, position/size, rotation, colours, font, alignment, opacity,
 *     label, number format, unit suffix, secondary line, text, image (upload / pick / replace).
 *   • "Origem dos dados": read-only source table/field, filters, calculation, caveats and the
 *     verification status against the real query. No SQL editing, no credentials.
 *   • Readability: per screen placement, smallest rendered text in stage pixels (14 / 11 px).
 *   • Images: PNG/JPEG/WebP ≤ 2 MB ≤ 2048 px, 50 per board, hash reuse, two-phase delete.
 *
 * Production rules (scanner ≠ consumption, provisional deviation, Waste kg/estimate, blocked
 * stock apart) live in the renderer and cannot be changed here.
 */

import { useState, useEffect, useCallback, useMemo, useRef, type CSSProperties, type PointerEvent as ReactPointerEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
    Palette, Save, X, RotateCcw, Eye, EyeOff, Image as ImageIcon, Type, Upload, Trash2, RefreshCw, Copy, Info, AlertTriangle, CheckCircle2, ChevronUp, ChevronDown,
} from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { Feedback } from '../../components/ui/Feedback';
import { ConfirmationDialog } from '../../components/common/ConfirmationDialog';
import { useUnsavedChangesWarning } from '../../hooks/useUnsavedChangesWarning';
import { ApiError } from '../../lib/api';
import { fetchViana3Monitor, fetchViana3MonitorScreens, fetchViana3MonitorScreenLayout } from '../../lib/operationsApi';
import {
    fetchViana3CardDesignBundle, fetchViana3CardDesignCatalog, fetchViana3CardDesign, saveViana3CardDesign, resetViana3CardDesign,
    fetchViana3DesignAssets, uploadViana3DesignAsset, deleteViana3DesignAsset, cleanupViana3DesignAssets, viana3DesignAssetUrl,
} from '../../lib/viana3CardDesignApi';
import type { Viana3MonitorResponse, Viana3MachinePanel, Viana3MonitorScreen } from '../../types/operations.types';
import type {
    Viana3CardDesign, Viana3CardDesignBundle, Viana3CardDesignDocument, Viana3CardDesignItem, Viana3CardItemCatalog, Viana3CardItem,
    Viana3DesignAssetList, Viana3DesignNumberFormat, Viana3DesignAlign, Viana3DesignFontWeight,
} from '../../types/viana3CardDesign.types';
import { injectViana3CSS } from './viana3/Viana3Board';
import { Viana3DesignCanvas } from './viana3/Viana3DesignedPanel';
import {
    DESIGN_W, DESIGN_H, DESIGN_MIN_W, DESIGN_MAX_W, DESIGN_W_STEP, canvasSize, minCanvasWidthFor, suggestedCanvasWidth,
    MAX_FREE_ITEMS, READABILITY_OK_PX, READABILITY_WARN_PX, buildDefaultDesign, cloneDesign, designsEqual, nextFreeKey,
    isFreeKey, isFreeTextKey, isFreeImageKey, isRotatable, acceptsImage, effectiveFontSize, computeReadability, resolveDesign,
    type DesignPlacement, type ReadabilityIssue,
} from './viana3/viana3CardDesign';
import { resolveLayout, visibleCards, placeCards } from './viana3/viana3Layout';

// ─── Constants / helpers ───

const SNAP = 4;
const TEMPLATE_ID = 0;
const NUMBER_FORMATS: { value: Viana3DesignNumberFormat; label: string }[] = [
    { value: 'THOUSANDS', label: 'Milhares (729 600)' }, { value: 'INT', label: 'Inteiro (729600)' },
    { value: 'COMPACT', label: 'Compacto (729,6 mil)' }, { value: 'DECIMAL1', label: '1 decimal (729 600,0)' },
];
const NUMBER_KEYS = new Set(['stockAvailable', 'stockPositions', 'stockBlocked', 'planned', 'reported', 'scanned', 'wasteBottles', 'wastePreforms']);
const SECONDARY_KEYS = new Set(['stockAvailable', 'palletStack', 'wasteBottles', 'wastePreforms']);
const ACCENT_KEYS = new Set(['planned', 'reported', 'scanned', 'preformLabel', 'wasteBottles', 'wastePreforms', 'illustration']);
const BOX_KEYS = new Set(['wasteBottles', 'wastePreforms', 'status']);

const label: CSSProperties = { fontSize: 12, fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' };
const section: CSSProperties = { border: '1px solid var(--color-border)', borderRadius: 12, background: 'var(--color-bg-surface)', padding: 12 };
const smallBtn: CSSProperties = { padding: '6px 10px', fontSize: 12 };
const field: CSSProperties = { width: '100%', fontSize: 12, padding: '4px 6px' };
const chip = (color: string): CSSProperties => ({ display: 'inline-block', padding: '2px 8px', borderRadius: 999, fontSize: 11, fontWeight: 700, color, background: `${color}1a`, border: `1px solid ${color}55`, whiteSpace: 'nowrap' });

function snap(v: number): number { return Math.round(v / SNAP) * SNAP; }
function clamp(v: number, min: number, max: number): number { return Math.max(min, Math.min(max, v)); }
function newToken(): string { return typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`; }

/** Sample panel used when AlplaPROD data is unavailable (illustrative values only). */
const SAMPLE_PANEL: Viana3MachinePanel = {
    machineId: 0, machineName: 'Máquina (exemplo)', statusCode: 'RUNNING', statusLabel: 'Em produção',
    current: {
        slot: 0, lotId: 4000, lotKind: 'Production', lotKindLabel: 'Produção', lotStatus: 'OPEN', bottleArticle: 'Garrafa 500ml - Exemplo', bottleWeightG: 12,
        preformArticleId: 1, preformArticle: 'Preforma 12gr Exemplo', preformWeightG: 12, planFrom: '2026-01-01T05:00:00', planTo: '2026-01-02T05:00:00',
        plannedBottles: 100000, reportedBottles: 42000, lastReportedDay: null, reportLastUpdated: null, scanCount: 3, scannedPreforms: 40500, lastScan: null,
        otherPreformScanCount: 0, otherPreformsScanned: 0,
        waste: { bookings: 2, rejectedBottlesKg: 12.5, rejectedBottlesUnitsEstimate: 1041, rejectedPreformsKg: 30, rejectedPreformsUnitsEstimate: 2500, lastBooking: null, estimateNote: '' },
        deviation: { statusCode: 'PROVISIONAL', statusLabel: 'Desvio provisório', ratio: -0.0357, differenceUnits: -1500, provisional: true },
    },
    previous: [],
    preformStock: { preformArticleId: 1, preformArticle: 'Preforma 12gr Exemplo', availablePositions: 9, availableQty: 180000, blockedPositions: 0, blockedQty: 0, lastChange: null, locations: [] },
} as unknown as Viana3MachinePanel;

interface Target { machineId: number; name: string; tag: 'TEMPLATE' | 'OWN' | 'FOLLOWS_TEMPLATE' | 'BUILTIN'; }

type Drag = { kind: 'move' | 'resize' | 'rotate'; key: string; startX: number; startY: number; orig: Viana3CardDesignItem; handle?: string; cx: number; cy: number };

export default function Viana3CardDesignEditorPage() {
    const [searchParams, setSearchParams] = useSearchParams();

    // Live data
    const [data, setData] = useState<Viana3MonitorResponse | null>(null);
    const [dataError, setDataError] = useState<string | null>(null);
    const [catalog, setCatalog] = useState<Viana3CardItemCatalog | null>(null);
    const [bundle, setBundle] = useState<Viana3CardDesignBundle | null>(null);
    const [screens, setScreens] = useState<Viana3MonitorScreen[]>([]);
    const [placementsByMachine, setPlacementsByMachine] = useState<Record<number, DesignPlacement[]>>({});
    const [assets, setAssets] = useState<Viana3DesignAssetList | null>(null);

    // Target + draft
    const initialTarget = searchParams.get('machine');
    const [targetId, setTargetId] = useState<number>(initialTarget === 'template' ? TEMPLATE_ID : parseInt(initialTarget || '0', 10) || TEMPLATE_ID);
    const [saved, setSaved] = useState<Viana3CardDesign | null>(null);
    const [draft, setDraft] = useState<Viana3CardDesignDocument | null>(null);
    const [baseline, setBaseline] = useState<Viana3CardDesignDocument | null>(null);
    const [selectedKey, setSelectedKey] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(true);
    const [isBusy, setIsBusy] = useState(false);
    const [loadError, setLoadError] = useState<string | null>(null);
    const [feedback, setFeedback] = useState<{ type: 'success' | 'error' | 'info' | 'warning'; message: string } | null>(null);
    const [unavailableItems, setUnavailableItems] = useState<string[]>([]);
    const [modifiedByOther, setModifiedByOther] = useState(false);

    // Dialogs
    const [showReset, setShowReset] = useState(false);
    const [showDiscard, setShowDiscard] = useState(false);
    const [pendingTarget, setPendingTarget] = useState<number | null>(null);
    const [showImages, setShowImages] = useState(false);
    const [pickingImageFor, setPickingImageFor] = useState<string | null>(null);

    // Canvas
    const canvasBoxRef = useRef<HTMLDivElement>(null);
    const [boxWidth, setBoxWidth] = useState(900);
    const [showGrid, setShowGrid] = useState(true);
    const dragRef = useRef<Drag | null>(null);
    const fileInputRef = useRef<HTMLInputElement>(null);

    useEffect(() => { injectViana3CSS(); }, []);

    // ─── Loading ───

    const loadScreensAndPlacements = useCallback(async (machines: Viana3MachinePanel[]) => {
        const list = await fetchViana3MonitorScreens();
        setScreens(list);
        const layouts = await Promise.allSettled(list.map(s => fetchViana3MonitorScreenLayout(s.screenKey)));
        const map: Record<number, DesignPlacement[]> = {};
        layouts.forEach((r, i) => {
            if (r.status !== 'fulfilled') return;
            const s = list[i];
            const resolved = resolveLayout(r.value, machines);
            const cards = visibleCards(resolved);
            const { rows } = placeCards(cards, resolved.columns);
            for (const c of cards) {
                (map[c.machineId] ??= []).push({ screenKey: s.screenKey, screenName: s.name, columns: resolved.columns, rows, size: c.size });
            }
        });
        setPlacementsByMachine(map);
    }, []);

    const loadTarget = useCallback(async (machineId: number) => {
        const dto = await fetchViana3CardDesign(machineId);
        setSaved(dto);
        const doc = dto.effectiveSource === 'BUILTIN' ? buildDefaultDesign() : cloneDesign(dto.design);
        setDraft(doc);
        setBaseline(cloneDesign(doc));
        setSelectedKey(null);
        setUnavailableItems([]);
        setModifiedByOther(false);
        return dto;
    }, []);

    const refreshAssets = useCallback(async () => {
        try { setAssets(await fetchViana3DesignAssets()); } catch { /* shown as unavailable */ }
    }, []);

    useEffect(() => {
        let cancelled = false;
        (async () => {
            setIsLoading(true);
            try {
                const [d, c, b] = await Promise.allSettled([fetchViana3Monitor(), fetchViana3CardDesignCatalog(), fetchViana3CardDesignBundle()]);
                if (cancelled) return;
                let machines: Viana3MachinePanel[] = [];
                if (d.status === 'fulfilled') { setData(d.value); machines = d.value.machines; } else setDataError(d.reason instanceof Error ? d.reason.message : 'Erro de conexão');
                if (c.status === 'fulfilled') setCatalog(c.value);
                if (b.status === 'fulfilled') setBundle(b.value);
                await Promise.allSettled([loadScreensAndPlacements(machines), refreshAssets()]);
                try { await loadTarget(targetId); }
                catch (err: unknown) { if (!cancelled) setLoadError(err instanceof Error ? err.message : 'Não foi possível carregar o design.'); }
            } finally {
                if (!cancelled) setIsLoading(false);
            }
        })();
        return () => { cancelled = true; };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    useEffect(() => {
        const current = searchParams.get('machine');
        const wanted = targetId === TEMPLATE_ID ? 'template' : String(targetId);
        if (current === wanted) return;
        const next = new URLSearchParams(searchParams);
        next.set('machine', wanted);
        setSearchParams(next, { replace: true });
    }, [targetId, searchParams, setSearchParams]);

    useEffect(() => {
        const el = canvasBoxRef.current;
        if (!el) return;
        const update = () => setBoxWidth(el.clientWidth);
        update();
        const ro = new ResizeObserver(update);
        ro.observe(el);
        return () => ro.disconnect();
    }, [isLoading]);

    // ─── Derived ───

    const machines = useMemo(() => data?.machines ?? [], [data]);
    const machineById = useMemo(() => new Map(machines.map(m => [m.machineId, m])), [machines]);

    const targets: Target[] = useMemo(() => {
        const ids = new Set<number>(machines.map(m => m.machineId));
        for (const m of bundle?.machines ?? []) ids.add(m.machineId);
        const list: Target[] = [{ machineId: TEMPLATE_ID, name: 'Modelo padrão (todas as máquinas)', tag: 'TEMPLATE' }];
        for (const id of [...ids].sort((a, b) => a - b)) {
            const own = bundle?.machines.some(m => m.machineId === id);
            list.push({ machineId: id, name: machineById.get(id)?.machineName ?? `Máquina ${id}`, tag: own ? 'OWN' : bundle?.template ? 'FOLLOWS_TEMPLATE' : 'BUILTIN' });
        }
        return list;
    }, [machines, bundle, machineById]);

    const panel: Viana3MachinePanel = useMemo(() => {
        if (targetId !== TEMPLATE_ID) return machineById.get(targetId) ?? { ...SAMPLE_PANEL, machineId: targetId, machineName: `Máquina ${targetId} (sem dados atuais)` };
        return machines.find(m => m.statusCode === 'RUNNING') ?? machines[0] ?? SAMPLE_PANEL;
    }, [targetId, machineById, machines]);

    const isDirty = !!draft && !designsEqual(draft, baseline);
    useUnsavedChangesWarning({ isDirty, isSubmitted: false });

    // Canvas size comes from the document: width adjustable (600–1200), height fixed.
    const { w: cw, h: ch } = draft ? canvasSize(draft) : { w: DESIGN_W, h: DESIGN_H };
    const minCanvasW = draft ? minCanvasWidthFor(draft) : DESIGN_MIN_W;
    const scale = useMemo(() => Math.max(0.6, Math.min(1.6, (boxWidth - 24) / cw)), [boxWidth, cw]);
    const selected = draft?.items.find(i => i.key === selectedKey) ?? null;
    const catalogItem: Viana3CardItem | null = selected && catalog ? catalog.items.find(i => i.key === selected.key) ?? null : null;

    const placements: DesignPlacement[] = useMemo(() => {
        if (targetId !== TEMPLATE_ID) return placementsByMachine[targetId] ?? [];
        // template: every placement of every machine that follows it
        const all: DesignPlacement[] = [];
        for (const [idStr, list] of Object.entries(placementsByMachine)) {
            const id = Number(idStr);
            if (bundle?.machines.some(m => m.machineId === id)) continue;
            all.push(...list);
        }
        return all;
    }, [targetId, placementsByMachine, bundle]);

    const readability: ReadabilityIssue[] = useMemo(() => draft ? computeReadability(draft, placements) : [], [draft, placements]);
    const readabilityWorst = readability.reduce<'OK' | 'WARN' | 'BAD'>((acc, r) => (r.level === 'BAD' || acc === 'BAD') ? 'BAD' : (r.level === 'WARN' || acc === 'WARN') ? 'WARN' : 'OK', 'OK');
    const geometry = readability[0] ?? (draft ? computeReadability(draft, [{ screenKey: '-', screenName: '-', columns: 3, rows: 2, size: 'STANDARD' }])[0] : undefined);
    const overlaps = geometry?.overlaps ?? [];
    const outside = geometry?.outside ?? [];

    // ─── Draft mutations ───

    const updateItem = useCallback((key: string, fn: (it: Viana3CardDesignItem) => Viana3CardDesignItem) => {
        setDraft(d => d ? { ...d, items: d.items.map(i => i.key === key ? fn(i) : i) } : d);
    }, []);
    const patchItem = (key: string, patch: Partial<Viana3CardDesignItem>) => updateItem(key, it => ({ ...it, ...patch }));
    /** Changes only the canvas width (height fixed, items keep their coordinates); clamped to [extent of the items, max]. */
    const setCanvasWidthValue = (value: number) => {
        setDraft(d => {
            if (!d) return d;
            const raw = Number.isFinite(value) ? value : DESIGN_W;
            const rounded = Math.round(raw / DESIGN_W_STEP) * DESIGN_W_STEP;
            const next = Math.min(DESIGN_MAX_W, Math.max(minCanvasWidthFor(d), rounded));
            return next === canvasSize(d).w ? d : { ...d, canvasWidth: next, canvasHeight: DESIGN_H };
        });
    };
    const widthSuggestions = useMemo(() => {
        const seen = new Map<number, string[]>();
        for (const p of placements) {
            const w = suggestedCanvasWidth(p);
            const label = `${p.screenName} · ${p.size === 'LARGE' ? 'Grande' : p.size === 'STANDARD' ? 'Normal' : p.size}`;
            const list = seen.get(w) ?? [];
            if (!list.includes(label)) list.push(label);
            seen.set(w, list);
        }
        return [...seen.entries()].sort((a, b) => a[0] - b[0]).map(([w, labels]) => ({ w, labels }));
    }, [placements]);
    const patchStyle = (key: string, patch: Partial<Viana3CardDesignItem['style']>) => updateItem(key, it => ({ ...it, style: { ...it.style, ...patch } }));
    const patchProps = (key: string, patch: Partial<Viana3CardDesignItem['props']>) => updateItem(key, it => ({ ...it, props: { ...it.props, ...patch } }));

    const addFree = (kind: 'text' | 'image') => {
        if (!draft) return;
        const key = nextFreeKey(draft, kind);
        if (!key) { setFeedback({ type: 'warning', message: `No máximo ${MAX_FREE_ITEMS} itens livres por cartão.` }); return; }
        const maxZ = draft.items.reduce((m, i) => Math.max(m, i.z), 0);
        const it: Viana3CardDesignItem = kind === 'text'
            ? { key, enabled: true, x: 200, y: 200, w: 200, h: 28, rotation: 0, z: maxZ + 1, style: { fontSize: 16, fontWeight: 700 }, props: { text: 'Texto' } }
            : { key, enabled: true, x: 220, y: 160, w: 160, h: 120, rotation: 0, z: maxZ + 1, style: {}, props: { imageFit: 'CONTAIN' } };
        setDraft({ ...draft, items: [...draft.items, it] });
        setSelectedKey(key);
        if (kind === 'image') { setPickingImageFor(key); setShowImages(true); }
    };
    const removeFree = (key: string) => { setDraft(d => d ? { ...d, items: d.items.filter(i => i.key !== key) } : d); setSelectedKey(null); };
    const bringZ = (key: string, delta: number) => updateItem(key, it => ({ ...it, z: clamp(it.z + delta, 0, 100) }));

    // ─── Canvas interaction ───

    const onItemPointerDown = (e: ReactPointerEvent<HTMLDivElement>, it: Viana3CardDesignItem, kind: Drag['kind'], handle?: string) => {
        e.preventDefault();
        e.stopPropagation();
        setSelectedKey(it.key);
        const box = canvasBoxRef.current?.getBoundingClientRect();
        const cx = (box?.left ?? 0) + 12 + (it.x + it.w / 2) * scale;
        const cy = (box?.top ?? 0) + 12 + (it.y + it.h / 2) * scale;
        dragRef.current = { kind, key: it.key, startX: e.clientX, startY: e.clientY, orig: { ...it, style: { ...it.style }, props: { ...it.props } }, handle, cx, cy };
    };

    useEffect(() => {
        const move = (e: PointerEvent) => {
            const d = dragRef.current;
            if (!d) return;
            const dx = (e.clientX - d.startX) / scale;
            const dy = (e.clientY - d.startY) / scale;
            const o = d.orig;
            if (d.kind === 'move') {
                const x = clamp(snap(o.x + dx), 0, cw - o.w);
                const y = clamp(snap(o.y + dy), 0, ch - o.h);
                patchItem(d.key, { x, y });
            } else if (d.kind === 'resize') {
                let { x, y, w, h } = o;
                const hd = d.handle ?? '';
                if (hd.includes('e')) w = clamp(snap(o.w + dx), 8, cw - o.x);
                if (hd.includes('s')) h = clamp(snap(o.h + dy), 8, ch - o.y);
                if (hd.includes('w')) { const nx = clamp(snap(o.x + dx), 0, o.x + o.w - 8); w = o.w + (o.x - nx); x = nx; }
                if (hd.includes('n')) { const ny = clamp(snap(o.y + dy), 0, o.y + o.h - 8); h = o.h + (o.y - ny); y = ny; }
                patchItem(d.key, { x, y, w, h });
            } else {
                const angle = Math.atan2(e.clientY - d.cy, e.clientX - d.cx) * 180 / Math.PI + 90;
                let r = ((angle + 180) % 360 + 360) % 360 - 180;
                if (e.shiftKey) r = Math.round(r / 15) * 15;
                patchItem(d.key, { rotation: Math.round(r) });
            }
        };
        const up = () => { dragRef.current = null; };
        window.addEventListener('pointermove', move);
        window.addEventListener('pointerup', up);
        return () => { window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); };
    }, [scale, patchItem, cw, ch]);

    const onCanvasKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
        if (!selected) return;
        const step = e.shiftKey ? 10 : 1;
        const it = selected;
        if (e.key === 'ArrowLeft') { e.preventDefault(); patchItem(it.key, { x: clamp(it.x - step, 0, cw - it.w) }); }
        if (e.key === 'ArrowRight') { e.preventDefault(); patchItem(it.key, { x: clamp(it.x + step, 0, cw - it.w) }); }
        if (e.key === 'ArrowUp') { e.preventDefault(); patchItem(it.key, { y: clamp(it.y - step, 0, ch - it.h) }); }
        if (e.key === 'ArrowDown') { e.preventDefault(); patchItem(it.key, { y: clamp(it.y + step, 0, ch - it.h) }); }
        if (e.key === 'Delete') { e.preventDefault(); if (isFreeKey(it.key)) removeFree(it.key); else patchItem(it.key, { enabled: false }); }
    };

    // ─── Actions ───

    const selectTarget = async (id: number) => {
        if (id === targetId) return;
        if (isDirty) { setPendingTarget(id); return; }
        await doSelectTarget(id);
    };
    const doSelectTarget = async (id: number) => {
        setPendingTarget(null);
        setFeedback(null);
        setIsBusy(true);
        try { await loadTarget(id); setTargetId(id); }
        catch (err: unknown) { setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível carregar o design.' }); }
        finally { setIsBusy(false); }
    };

    const refreshBundle = async () => { try { setBundle(await fetchViana3CardDesignBundle()); } catch { /* keep */ } };

    const handleSave = async (retry = true) => {
        if (!draft || !saved) return;
        setIsBusy(true);
        setFeedback(null);
        try {
            const result = await saveViana3CardDesign(targetId, {
                design: draft, saveToken: newToken(), expectedUpdatedAtUtc: saved.isPersisted ? saved.updatedAtUtc : null,
            });
            setSaved(result);
            setDraft(cloneDesign(result.design));
            setBaseline(cloneDesign(result.design));
            setUnavailableItems([]);
            setModifiedByOther(false);
            await refreshBundle();
            setFeedback({ type: 'success', message: targetId === TEMPLATE_ID ? 'Modelo padrão guardado. Aplica-se a todas as máquinas sem design próprio, em todos os ecrãs.' : `Design da máquina guardado. Aplica-se em todos os ecrãs onde a máquina aparece.` });
        } catch (err: unknown) {
            if (err instanceof ApiError && err.errorCode === 'BOARD_BUSY' && retry) {
                await new Promise(r => setTimeout(r, 1000));
                setIsBusy(false);
                return handleSave(false);
            }
            if (err instanceof ApiError && err.errorCode === 'DESIGN_MODIFIED') { setModifiedByOther(true); setFeedback({ type: 'error', message: err.message }); }
            else if (err instanceof ApiError && err.errorCode === 'ASSET_UNAVAILABLE') {
                const detail = (err.details as { detail?: { items?: string[] } })?.detail;
                setUnavailableItems(detail?.items ?? []);
                setFeedback({ type: 'error', message: err.message });
                await refreshAssets();
            }
            else setFeedback({ type: 'error', message: err instanceof ApiError ? (err.status === 403 || err.status === 401 ? 'Sem permissão para alterar o design.' : err.message) : 'Não foi possível guardar o design.' });
        } finally {
            setIsBusy(false);
        }
    };

    const handleReload = async () => { setIsBusy(true); try { await loadTarget(targetId); setFeedback({ type: 'info', message: 'Design recarregado.' }); } finally { setIsBusy(false); } };

    const handleDiscard = async () => { setShowDiscard(false); await handleReload(); };

    const handleReset = async () => {
        setShowReset(false);
        setIsBusy(true);
        setFeedback(null);
        try {
            const result = await resetViana3CardDesign(targetId);
            setSaved(result);
            const doc = result.effectiveSource === 'BUILTIN' ? buildDefaultDesign() : cloneDesign(result.design);
            setDraft(doc);
            setBaseline(cloneDesign(doc));
            await refreshBundle();
            setFeedback({ type: 'success', message: targetId === TEMPLATE_ID ? 'Modelo padrão removido: as máquinas sem design próprio voltam ao cartão original.' : 'Design próprio removido: a máquina volta a seguir o modelo padrão.' });
        } catch (err: unknown) {
            setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível repor o design.' });
        } finally { setIsBusy(false); }
    };

    const copyFrom = async (id: number) => {
        if (!draft) return;
        try {
            const src = await fetchViana3CardDesign(id);
            const doc = src.effectiveSource === 'BUILTIN' ? buildDefaultDesign() : cloneDesign(src.design);
            setDraft(doc);
            setSelectedKey(null);
            setFeedback({ type: 'info', message: `Design copiado de «${id === TEMPLATE_ID ? 'Modelo padrão' : machineById.get(id)?.machineName ?? `Máquina ${id}`}». Guarde para aplicar.` });
        } catch { setFeedback({ type: 'error', message: 'Não foi possível copiar o design.' }); }
    };

    // ─── Images ───

    const handleUpload = async (file: File | null) => {
        if (!file) return;
        setIsBusy(true);
        setFeedback(null);
        try {
            const result = await uploadViana3DesignAsset(file);
            await refreshAssets();
            if (pickingImageFor) { patchProps(pickingImageFor, { imageAssetId: result.asset.id }); setPickingImageFor(null); setShowImages(false); }
            setFeedback({ type: 'success', message: result.reused ? 'Imagem já existente reutilizada.' : `Imagem «${result.asset.fileName}» carregada (${result.asset.width}×${result.asset.height}).` });
        } catch (err: unknown) {
            setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível carregar a imagem.' });
        } finally { setIsBusy(false); if (fileInputRef.current) fileInputRef.current.value = ''; }
    };
    const handleDeleteAsset = async (id: string) => {
        setIsBusy(true);
        try { await deleteViana3DesignAsset(id); await refreshAssets(); setFeedback({ type: 'success', message: 'Imagem eliminada.' }); }
        catch (err: unknown) { setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível eliminar a imagem.' }); await refreshAssets(); }
        finally { setIsBusy(false); }
    };
    const handleCleanup = async () => {
        setIsBusy(true);
        try {
            const r = await cleanupViana3DesignAssets();
            await refreshAssets();
            setFeedback({ type: 'info', message: `Limpeza: ${r.tombstoned} marcadas, ${r.purged} eliminadas, ${r.orphanFilesRemoved} ficheiros órfãos removidos, ${r.missingFiles} em falta${r.errors.length ? `, ${r.errors.length} erro(s)` : ''}.` });
        } catch (err: unknown) { setFeedback({ type: 'error', message: err instanceof ApiError ? err.message : 'Não foi possível limpar as imagens.' }); }
        finally { setIsBusy(false); }
    };

    // ─── Render ───

    if (isLoading) {
        return <PageContainer><PageHeader title="Monitor Viana 3 — Design dos cartões" icon={<Palette size={22} />} /><div style={{ color: 'var(--color-text-muted)', padding: 24 }}>A carregar…</div></PageContainer>;
    }
    if (!draft || !saved) {
        return <PageContainer><PageHeader title="Monitor Viana 3 — Design dos cartões" icon={<Palette size={22} />} /><Feedback type="error" message={loadError ?? 'Não foi possível carregar o design.'} autoCloseMs={0} /></PageContainer>;
    }

    const isTemplate = targetId === TEMPLATE_ID;
    const canSave = !isBusy && (isDirty || (!isTemplate && !saved.isPersisted));
    const saveLabel = isTemplate ? 'Guardar modelo' : saved.isPersisted ? 'Guardar design' : 'Personalizar esta máquina';
    const savedInfo = isTemplate
        ? (saved.isPersisted ? `Modelo guardado ${saved.updatedAtUtc ? new Date(saved.updatedAtUtc).toLocaleString('pt-PT') : ''}${saved.updatedByName ? ` por ${saved.updatedByName}` : ''}.` : 'Sem modelo guardado: as máquinas sem design próprio usam o cartão original.')
        : saved.effectiveSource === 'OWN' ? `Design próprio guardado ${saved.updatedAtUtc ? new Date(saved.updatedAtUtc).toLocaleString('pt-PT') : ''}${saved.updatedByName ? ` por ${saved.updatedByName}` : ''}.`
        : saved.effectiveSource === 'TEMPLATE' ? 'Esta máquina segue o modelo padrão. «Personalizar» guarda uma cópia completa só para esta máquina.'
        : 'Esta máquina usa o cartão original. «Personalizar» guarda um design só para esta máquina.';
    const freeCount = draft.items.filter(i => isFreeKey(i.key)).length;

    return (
        <PageContainer maxWidth="1800px">
            <PageHeader
                title="Monitor Viana 3 — Design dos cartões"
                subtitle={<span>Itens dentro do cartão de cada máquina, partilhados por todos os ecrãs da TV. A seleção de máquinas por ecrã continua no editor «Layout TV».</span>}
                icon={<Palette size={22} />}
                actions={
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                        <button type="button" className="btn-secondary" onClick={() => setShowImages(true)} disabled={isBusy}><ImageIcon size={16} /> Imagens {assets ? `${assets.activeCount}/${assets.quota}` : ''}</button>
                        <button type="button" className="btn-secondary" onClick={() => setShowDiscard(true)} disabled={isBusy || !isDirty}><X size={16} /> Cancelar</button>
                        <button type="button" className="btn-primary" onClick={() => handleSave()} disabled={!canSave} data-testid="save-design"><Save size={16} /> {isBusy ? 'A processar…' : saveLabel}</button>
                    </div>
                }
            />

            {feedback && <Feedback type={feedback.type} message={feedback.message} onClose={() => setFeedback(null)} />}
            {dataError && <Feedback type="warning" message={`Dados do AlplaPROD indisponíveis: a pré-visualização usa valores de exemplo. ${dataError}`} autoCloseMs={0} />}
            {modifiedByOther && (
                <div style={{ margin: '8px 0', display: 'flex', gap: 10, alignItems: 'center', fontSize: 13, color: '#b91c1c' }}>
                    O design foi alterado por outro utilizador. <button type="button" className="btn-secondary" style={smallBtn} onClick={handleReload}>Recarregar (descarta as alterações)</button>
                </div>
            )}

            <div style={{ display: 'grid', gridTemplateColumns: '260px minmax(0, 1fr) 360px', gap: 16, alignItems: 'start' }}>
                {/* ── Left: targets ── */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                    <section style={section}>
                        <div style={{ ...label, marginBottom: 8 }}>Máquina a desenhar</div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }} data-testid="target-list">
                            {targets.map(t => {
                                const active = t.machineId === targetId;
                                const tag = t.tag === 'TEMPLATE' ? (bundle?.template ? 'modelo guardado' : 'sem modelo') : t.tag === 'OWN' ? 'personalizado' : t.tag === 'FOLLOWS_TEMPLATE' ? 'segue o modelo' : 'cartão original';
                                const color = t.tag === 'OWN' ? '#16a34a' : t.tag === 'FOLLOWS_TEMPLATE' || (t.tag === 'TEMPLATE' && bundle?.template) ? '#2563eb' : '#64748b';
                                return (
                                    <button key={t.machineId} type="button" onClick={() => selectTarget(t.machineId)} disabled={isBusy} data-target-id={t.machineId} style={{
                                        textAlign: 'left', padding: '8px 10px', borderRadius: 10, cursor: 'pointer',
                                        border: active ? '2px solid var(--color-primary)' : '1px solid var(--color-border)',
                                        background: active ? 'rgba(var(--color-primary-rgb), 0.06)' : 'var(--color-bg-page)', color: 'var(--color-text-main)',
                                    }}>
                                        <div style={{ fontWeight: 800, fontSize: 13, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{t.name}</div>
                                        <span style={chip(color)}>{tag}</span>
                                    </button>
                                );
                            })}
                        </div>
                    </section>
                    <section style={section}>
                        <div style={{ ...label, marginBottom: 8 }}>Ações do design</div>
                        <div style={{ fontSize: 12, color: 'var(--color-text-muted)', marginBottom: 8 }}>{savedInfo}</div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                            <button type="button" className="btn-secondary" style={smallBtn} onClick={() => setShowReset(true)} disabled={isBusy || !saved.isPersisted} data-testid="reset-design">
                                <RotateCcw size={14} /> {isTemplate ? 'Repor cartão original' : 'Repor para o modelo'}
                            </button>
                            <label style={{ fontSize: 12, display: 'flex', flexDirection: 'column', gap: 4 }}>
                                Copiar design de…
                                <select value="" onChange={e => { const v = e.target.value; if (v !== '') copyFrom(parseInt(v, 10)); }} style={field} disabled={isBusy}>
                                    <option value="">— escolher —</option>
                                    {targets.filter(t => t.machineId !== targetId).map(t => <option key={t.machineId} value={t.machineId}>{t.name}</option>)}
                                </select>
                            </label>
                            <div style={{ display: 'flex', gap: 6 }}>
                                <button type="button" className="btn-secondary" style={smallBtn} onClick={() => addFree('text')} disabled={isBusy || freeCount >= MAX_FREE_ITEMS}><Type size={14} /> Texto</button>
                                <button type="button" className="btn-secondary" style={smallBtn} onClick={() => addFree('image')} disabled={isBusy || freeCount >= MAX_FREE_ITEMS}><ImageIcon size={14} /> Imagem</button>
                            </div>
                            <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>Itens livres: {freeCount} / {MAX_FREE_ITEMS}</div>
                        </div>
                    </section>
                    <section style={section}>
                        <div style={{ ...label, marginBottom: 8 }}>Itens do cartão</div>
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 2, maxHeight: 360, overflow: 'auto' }}>
                            {[...draft.items].sort((a, b) => b.z - a.z).map(it => {
                                const ci = catalog?.items.find(c => c.key === it.key);
                                const name = ci?.label ?? (isFreeTextKey(it.key) ? `Texto livre (${it.props.text?.slice(0, 14) ?? ''})` : isFreeImageKey(it.key) ? 'Imagem livre' : it.key);
                                return (
                                    <div key={it.key} onClick={() => setSelectedKey(it.key)} style={{ display: 'flex', alignItems: 'center', gap: 6, padding: '4px 6px', borderRadius: 6, cursor: 'pointer', fontSize: 12, background: selectedKey === it.key ? 'rgba(var(--color-primary-rgb), 0.08)' : undefined, opacity: it.enabled ? 1 : 0.55 }}>
                                        <button type="button" title={it.enabled ? 'Desativar' : 'Ativar'} onClick={e => { e.stopPropagation(); patchItem(it.key, { enabled: !it.enabled }); }} style={{ border: 'none', background: 'none', cursor: 'pointer', color: 'var(--color-text-muted)', display: 'inline-flex' }}>
                                            {it.enabled ? <Eye size={14} /> : <EyeOff size={14} />}
                                        </button>
                                        <span style={{ flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{name}</span>
                                        {unavailableItems.includes(it.key) && <span style={chip('#b91c1c')}>imagem em falta</span>}
                                    </div>
                                );
                            })}
                        </div>
                    </section>
                </div>

                {/* ── Centre: canvas ── */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: 10, minWidth: 0 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
                        <div style={label}>
                            Cartão «{isTemplate ? 'Modelo padrão' : panel.machineName}» · <span data-testid="canvas-size">{cw}×{ch}</span> · escala {(scale * 100).toFixed(0)} %
                            {isDirty && <span style={{ ...chip('#b45309'), marginLeft: 8 }}>por guardar</span>}
                        </div>
                        <div style={{ display: 'flex', gap: 10, alignItems: 'center', fontSize: 12 }}>
                            <label style={{ display: 'flex', alignItems: 'center', gap: 4 }}><input type="checkbox" checked={showGrid} onChange={e => setShowGrid(e.target.checked)} /> Grelha</label>
                            <span style={chip(readabilityWorst === 'OK' ? '#16a34a' : readabilityWorst === 'WARN' ? '#b45309' : '#b91c1c')} data-testid="readability-worst">
                                legibilidade: {readabilityWorst === 'OK' ? 'OK' : readabilityWorst === 'WARN' ? 'texto pequeno' : 'ilegível numa TV'}
                            </span>
                        </div>
                    </div>

                    {/* Canvas width: more room for a wide TV card; the height is fixed and items never move when the width changes */}
                    <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap', fontSize: 12 }} data-testid="canvas-width-row">
                        <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                            Largura da área de desenho
                            <input type="number" data-testid="canvas-width" value={cw} min={minCanvasW} max={DESIGN_MAX_W} step={DESIGN_W_STEP} style={{ width: 80 }}
                                onChange={e => setCanvasWidthValue(parseInt(e.target.value, 10))} />
                            <span style={{ color: 'var(--color-text-muted)' }}>× {ch} (altura fixa)</span>
                        </label>
                        <button type="button" className="btn-secondary" style={smallBtn} onClick={() => setCanvasWidthValue(DESIGN_W)} disabled={cw === DESIGN_W || minCanvasW > DESIGN_W}
                            title={minCanvasW > DESIGN_W ? `Há itens além de ${DESIGN_W}: mova-os para a esquerda antes de reduzir` : 'Largura original'} data-testid="canvas-width-reset">{DESIGN_W} (original)</button>
                        {widthSuggestions.map(sug => (
                            <button key={sug.w} type="button" className="btn-secondary" style={smallBtn} onClick={() => setCanvasWidthValue(sug.w)} disabled={cw === sug.w}
                                title={`Largura que preenche o cartão em: ${sug.labels.join('; ')}`} data-testid="canvas-width-suggest">{sug.w} ← {sug.labels.join('; ')}</button>
                        ))}
                        <span style={{ color: 'var(--color-text-muted)' }}>
                            {DESIGN_MIN_W}–{DESIGN_MAX_W} · os itens mantêm as coordenadas; o espaço novo fica à direita{minCanvasW > DESIGN_MIN_W ? ` · mínimo com os itens atuais: ${minCanvasW}` : ''}
                        </span>
                    </div>

                    <div ref={canvasBoxRef} tabIndex={0} onKeyDown={onCanvasKeyDown} onPointerDown={() => setSelectedKey(null)} data-testid="design-canvas" style={{ outline: 'none', background: '#0a0f1e', borderRadius: 12, border: '1px solid var(--color-border)', padding: 12, overflow: 'auto' }}>
                        <div style={{ position: 'relative', width: cw * scale, height: ch * scale }}>
                            <div style={{ position: 'absolute', inset: 0, backgroundImage: showGrid ? 'linear-gradient(rgba(148,163,184,0.08) 1px, transparent 1px), linear-gradient(90deg, rgba(148,163,184,0.08) 1px, transparent 1px)' : undefined, backgroundSize: `${20 * scale}px ${20 * scale}px`, pointerEvents: 'none', zIndex: 0 }} />
                            <Viana3DesignCanvas panel={panel} design={draft} retentionId={data?.scope.retentionLocationId ?? 30012} scale={scale} showDisabled />
                            {/* overlay: hit boxes + handles */}
                            <div style={{ position: 'absolute', inset: 0 }}>
                                {draft.items.map(it => {
                                    const sel = it.key === selectedKey;
                                    const s: CSSProperties = {
                                        position: 'absolute', left: it.x * scale, top: it.y * scale, width: it.w * scale, height: it.h * scale,
                                        transform: it.rotation ? `rotate(${it.rotation}deg)` : undefined, transformOrigin: 'center center',
                                        border: sel ? '2px solid #38bdf8' : '1px dashed rgba(56,189,248,0.25)', boxSizing: 'border-box', cursor: 'move',
                                        zIndex: sel ? 1000 : 100 + it.z, background: sel ? 'rgba(56,189,248,0.04)' : 'transparent',
                                    };
                                    const handles = sel ? ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'] : [];
                                    return (
                                        <div key={it.key} data-overlay-item={it.key} style={s} onPointerDown={e => onItemPointerDown(e, it, 'move')} title={it.key}>
                                            {handles.map(hd => {
                                                const pos: CSSProperties = { position: 'absolute', width: 10, height: 10, background: '#38bdf8', border: '1px solid #0f172a', borderRadius: 2, zIndex: 2 };
                                                if (hd.includes('n')) pos.top = -5; if (hd.includes('s')) pos.bottom = -5;
                                                if (hd.includes('w')) pos.left = -5; if (hd.includes('e')) pos.right = -5;
                                                if (hd === 'n' || hd === 's') { pos.left = 'calc(50% - 5px)'; }
                                                if (hd === 'e' || hd === 'w') { pos.top = 'calc(50% - 5px)'; }
                                                const cursor = hd === 'n' || hd === 's' ? 'ns-resize' : hd === 'e' || hd === 'w' ? 'ew-resize' : hd === 'ne' || hd === 'sw' ? 'nesw-resize' : 'nwse-resize';
                                                return <div key={hd} data-handle={hd} style={{ ...pos, cursor }} onPointerDown={e => onItemPointerDown(e, it, 'resize', hd)} />;
                                            })}
                                            {sel && isRotatable(it.key) && (
                                                <div data-handle="rotate" title="Rodar (Shift = passos de 15°)" onPointerDown={e => onItemPointerDown(e, it, 'rotate')} style={{ position: 'absolute', top: -26, left: 'calc(50% - 7px)', width: 14, height: 14, borderRadius: '50%', background: '#f59e0b', border: '1px solid #0f172a', cursor: 'grab', zIndex: 2 }} />
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        </div>
                    </div>

                    {/* Readability */}
                    <section style={section}>
                        <div style={{ ...label, marginBottom: 6 }}>Legibilidade na TV (provisório: ≥ {READABILITY_OK_PX} px OK · {READABILITY_WARN_PX}–{READABILITY_OK_PX - 1} px pequeno · &lt; {READABILITY_WARN_PX} px ilegível)</div>
                        {readability.length === 0 ? (
                            <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{isTemplate ? 'Nenhuma máquina sem design próprio está colocada num ecrã.' : 'Esta máquina não está colocada em nenhum ecrã da TV.'}</div>
                        ) : (
                            <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }} data-testid="readability-list">
                                {readability.map((r, i) => (
                                    <div key={`${r.screenKey}-${i}`} style={{ display: 'flex', gap: 8, alignItems: 'center', fontSize: 12, flexWrap: 'wrap' }}>
                                        <span style={chip(r.level === 'OK' ? '#16a34a' : r.level === 'WARN' ? '#b45309' : '#b91c1c')}>{r.level === 'OK' ? 'OK' : r.level === 'WARN' ? 'texto pequeno' : 'ilegível numa TV'}</span>
                                        <span>Ecrã «{r.screenName}» · escala {(r.scale * 100).toFixed(0)} % · texto mais pequeno {r.minTextPx.toFixed(1)} px{r.smallestItems.length ? ` (${r.smallestItems.join(', ')})` : ''}</span>
                                    </div>
                                ))}
                            </div>
                        )}
                        {overlaps.length > 0 && <div style={{ fontSize: 12, color: '#b45309', marginTop: 6 }}><AlertTriangle size={12} /> Itens sobrepostos: {overlaps.map(o => o.join(' × ')).join('; ')}</div>}
                        {outside.length > 0 && <div style={{ fontSize: 12, color: '#b91c1c', marginTop: 6 }} data-testid="outside-warning"><AlertTriangle size={12} /> Itens fora da área de desenho (recortados na TV): {outside.join(', ')}</div>}
                        <div style={{ fontSize: 11, color: 'var(--color-text-muted)', marginTop: 6 }}>Avisos informativos — não impedem guardar. A TV real pode ter outra dimensão física.</div>
                    </section>
                </div>

                {/* ── Right: properties + sources ── */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                    <section style={section}>
                        <div style={{ ...label, marginBottom: 8 }}>Propriedades {selected ? `· ${catalogItem?.label ?? selected.key}` : ''}</div>
                        {!selected ? (
                            <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>Selecione um item no cartão ou na lista.</div>
                        ) : (
                            <PropertiesPanel it={selected} catalogItem={catalogItem} assets={assets} canvasW={cw} canvasH={ch} onPatch={p => patchItem(selected.key, p)} onStyle={p => patchStyle(selected.key, p)} onProps={p => patchProps(selected.key, p)}
                                onPickImage={() => { setPickingImageFor(selected.key); setShowImages(true); }} onRemoveFree={() => removeFree(selected.key)} onZ={d => bringZ(selected.key, d)} />
                        )}
                    </section>
                    <section style={section}>
                        <div style={{ ...label, marginBottom: 8 }}><Info size={12} /> Origem dos dados (só leitura)</div>
                        {!selected ? <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>—</div>
                            : isFreeKey(selected.key) ? <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>Item livre: não tem origem de dados.</div>
                            : !catalogItem ? <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>Catálogo indisponível.</div>
                            : <SourcePanel item={catalogItem} sourceDatabase={catalog?.sourceDatabase ?? ''} />}
                    </section>
                </div>
            </div>

            {/* ── Images panel ── */}
            {showImages && (
                <div role="dialog" aria-modal="true" style={{ position: 'fixed', inset: 0, background: 'rgba(15,23,42,0.45)', zIndex: 2000, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 20 }} onClick={() => { setShowImages(false); setPickingImageFor(null); }}>
                    <div onClick={e => e.stopPropagation()} style={{ background: 'var(--color-bg-surface)', borderRadius: 14, width: 'min(900px, 100%)', maxHeight: '85vh', overflow: 'auto', padding: 16, border: '1px solid var(--color-border)' }} data-testid="images-panel">
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10, flexWrap: 'wrap', marginBottom: 10 }}>
                            <div style={{ fontWeight: 800, fontSize: 16 }}>Imagens {assets ? `· ${assets.activeCount} / ${assets.quota} ativas` : ''}{pickingImageFor ? ` · escolher para «${pickingImageFor}»` : ''}</div>
                            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                                <input ref={fileInputRef} type="file" accept=".png,.jpg,.jpeg,.webp,image/png,image/jpeg,image/webp" style={{ display: 'none' }} onChange={e => handleUpload(e.target.files?.[0] ?? null)} data-testid="asset-file-input" />
                                <button type="button" className="btn-primary" style={smallBtn} onClick={() => fileInputRef.current?.click()} disabled={isBusy || (assets ? assets.activeCount >= assets.quota : false)}><Upload size={14} /> Carregar PNG / JPEG / WebP</button>
                                <button type="button" className="btn-secondary" style={smallBtn} onClick={handleCleanup} disabled={isBusy}><Trash2 size={14} /> Limpar não utilizadas</button>
                                <button type="button" className="btn-secondary" style={smallBtn} onClick={refreshAssets} disabled={isBusy}><RefreshCw size={14} /></button>
                                <button type="button" className="btn-secondary" style={smallBtn} onClick={() => { setShowImages(false); setPickingImageFor(null); }}>Fechar</button>
                            </div>
                        </div>
                        <div style={{ fontSize: 11, color: 'var(--color-text-muted)', marginBottom: 10 }}>
                            Máx. {assets ? Math.round(assets.maxSizeBytes / 1024 / 1024) : 2} MB e {assets?.maxDimension ?? 2048} px por lado; SVG não é aceite. Conteúdo idêntico é reutilizado. Só é possível eliminar imagens que nenhum design usa.
                            {assets && assets.pendingPurge > 0 && ` ${assets.pendingPurge} em eliminação pendente.`}{assets && assets.missingFiles > 0 && ` ${assets.missingFiles} com ficheiro em falta.`}
                        </div>
                        {!assets ? <div style={{ fontSize: 12 }}>Lista indisponível.</div> : assets.assets.length === 0 ? <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>Sem imagens.</div> : (
                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 10 }}>
                                {assets.assets.map(a => (
                                    <div key={a.id} data-asset-id={a.id} style={{ border: '1px solid var(--color-border)', borderRadius: 10, padding: 8, display: 'flex', flexDirection: 'column', gap: 6, background: 'var(--color-bg-page)' }}>
                                        <div style={{ height: 90, background: 'repeating-conic-gradient(#e2e8f0 0 25%, #f8fafc 0 50%) 0 0 / 16px 16px', borderRadius: 6, display: 'flex', alignItems: 'center', justifyContent: 'center', overflow: 'hidden' }}>
                                            {a.fileMissing ? <span style={{ fontSize: 11, color: '#b91c1c' }}>ficheiro em falta</span> : <img src={a.url} alt={a.fileName} style={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }} />}
                                        </div>
                                        <div style={{ fontSize: 12, fontWeight: 700, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={a.fileName}>{a.fileName}</div>
                                        <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>{a.width}×{a.height} · {(a.sizeBytes / 1024).toFixed(0)} KB · {a.referencedBy.length === 0 ? 'não utilizada' : `usada em: ${a.referencedBy.map(r => r === 0 ? 'Modelo' : machineById.get(r)?.machineName ?? `Máquina ${r}`).join(', ')}`}</div>
                                        <div style={{ display: 'flex', gap: 6 }}>
                                            {pickingImageFor && !a.fileMissing && <button type="button" className="btn-primary" style={smallBtn} onClick={() => { patchProps(pickingImageFor, { imageAssetId: a.id }); setPickingImageFor(null); setShowImages(false); }}>Usar</button>}
                                            <button type="button" className="btn-secondary" style={{ ...smallBtn, color: '#b91c1c', borderColor: '#b91c1c' }} disabled={isBusy || a.referencedBy.length > 0} title={a.referencedBy.length > 0 ? 'Usada por um design' : 'Eliminar'} onClick={() => handleDeleteAsset(a.id)}><Trash2 size={12} /></button>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>
                </div>
            )}

            <ConfirmationDialog isOpen={showReset} title={isTemplate ? 'Repor o cartão original?' : 'Repor para o modelo?'}
                message={isTemplate ? 'O modelo padrão é removido. As máquinas sem design próprio voltam ao cartão original em todos os ecrãs. Nenhum dado de produção é alterado.' : 'O design próprio desta máquina é removido e ela volta a seguir o modelo padrão (ou o cartão original). Nenhum dado de produção é alterado.'}
                confirmText="Repor" variant="warning" onConfirm={handleReset} onCancel={() => setShowReset(false)} />
            <ConfirmationDialog isOpen={showDiscard} title="Descartar alterações?" message="As alterações por guardar serão perdidas." confirmText="Descartar" variant="warning" onConfirm={handleDiscard} onCancel={() => setShowDiscard(false)} />
            <ConfirmationDialog isOpen={pendingTarget !== null} title="Mudar de máquina sem guardar?" message="As alterações por guardar neste design serão perdidas." confirmText="Mudar e descartar" variant="warning" onConfirm={() => { if (pendingTarget !== null) doSelectTarget(pendingTarget); }} onCancel={() => setPendingTarget(null)} />
        </PageContainer>
    );
}

// ─── Properties panel ───

function PropertiesPanel({ it, catalogItem, assets, canvasW, canvasH, onPatch, onStyle, onProps, onPickImage, onRemoveFree, onZ }: {
    it: Viana3CardDesignItem; catalogItem: Viana3CardItem | null; assets: Viana3DesignAssetList | null; canvasW: number; canvasH: number;
    onPatch: (p: Partial<Viana3CardDesignItem>) => void; onStyle: (p: Partial<Viana3CardDesignItem['style']>) => void; onProps: (p: Partial<Viana3CardDesignItem['props']>) => void;
    onPickImage: () => void; onRemoveFree: () => void; onZ: (d: number) => void;
}) {
    const num = (v: number) => Number.isFinite(v) ? v : 0;
    const row: CSSProperties = { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 6 };
    const lbl: CSSProperties = { fontSize: 11, color: 'var(--color-text-muted)', display: 'flex', flexDirection: 'column', gap: 2 };
    const colorField = (name: string, key: 'color' | 'background' | 'borderColor' | 'accent') => (
        <label style={lbl}>{name}
            <div style={{ display: 'flex', gap: 4 }}>
                <input type="color" value={(it.style[key] ?? '#ffffff').slice(0, 7)} onChange={e => onStyle({ [key]: e.target.value })} style={{ width: 36, height: 26, padding: 0, border: '1px solid var(--color-border)', borderRadius: 4 }} />
                <button type="button" className="btn-secondary" style={{ ...smallBtn, padding: '2px 6px' }} onClick={() => onStyle({ [key]: null })} title="Predefinido">auto</button>
            </div>
        </label>
    );
    const free = isFreeKey(it.key);
    const imageAsset = it.props.imageAssetId ? assets?.assets.find(a => a.id === it.props.imageAssetId) : null;
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontSize: 12 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}><input type="checkbox" checked={it.enabled} onChange={e => onPatch({ enabled: e.target.checked })} data-testid="prop-enabled" /> Visível no cartão</label>
            <div style={row}>
                <label style={lbl}>X <input type="number" style={field} value={it.x} onChange={e => onPatch({ x: clamp(num(parseFloat(e.target.value)), 0, canvasW - it.w) })} /></label>
                <label style={lbl}>Y <input type="number" style={field} value={it.y} onChange={e => onPatch({ y: clamp(num(parseFloat(e.target.value)), 0, canvasH - it.h) })} /></label>
                <label style={lbl}>Largura <input type="number" style={field} value={it.w} onChange={e => onPatch({ w: clamp(num(parseFloat(e.target.value)), 8, canvasW - it.x) })} /></label>
                <label style={lbl}>Altura <input type="number" style={field} value={it.h} onChange={e => onPatch({ h: clamp(num(parseFloat(e.target.value)), 8, canvasH - it.y) })} /></label>
            </div>
            {isRotatable(it.key) && <label style={lbl}>Rotação (°) <input type="number" style={field} min={-180} max={180} value={it.rotation} onChange={e => onPatch({ rotation: clamp(num(parseFloat(e.target.value)), -180, 180) })} data-testid="prop-rotation" /></label>}
            <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                <span style={{ color: 'var(--color-text-muted)', fontSize: 11 }}>Ordem (z = {it.z})</span>
                <button type="button" className="btn-secondary" style={{ ...smallBtn, padding: '2px 6px' }} onClick={() => onZ(1)}><ChevronUp size={12} /></button>
                <button type="button" className="btn-secondary" style={{ ...smallBtn, padding: '2px 6px' }} onClick={() => onZ(-1)}><ChevronDown size={12} /></button>
            </div>
            <div style={row}>
                {colorField('Cor do texto', 'color')}
                {(BOX_KEYS.has(it.key) || free) && colorField('Fundo', 'background')}
                {(BOX_KEYS.has(it.key) || free) && colorField('Contorno', 'borderColor')}
                {ACCENT_KEYS.has(it.key) && colorField('Realce (barra/ícone)', 'accent')}
            </div>
            <div style={row}>
                <label style={lbl}>Tamanho de letra <input type="number" style={field} min={6} max={96} value={it.style.fontSize ?? effectiveFontSize(it)} onChange={e => onStyle({ fontSize: clamp(num(parseFloat(e.target.value)), 6, 96) })} data-testid="prop-fontsize" /></label>
                <label style={lbl}>Peso
                    <select style={field} value={it.style.fontWeight ?? ''} onChange={e => onStyle({ fontWeight: e.target.value === '' ? null : parseInt(e.target.value, 10) as Viana3DesignFontWeight })}>
                        <option value="">auto</option><option value={400}>Normal</option><option value={600}>Semi-negrito</option><option value={700}>Negrito</option><option value={800}>Extra-negrito</option>
                    </select>
                </label>
                <label style={lbl}>Alinhamento
                    <select style={field} value={it.style.align ?? 'LEFT'} onChange={e => onStyle({ align: e.target.value as Viana3DesignAlign })}>
                        <option value="LEFT">Esquerda</option><option value="CENTER">Centro</option><option value="RIGHT">Direita</option>
                    </select>
                </label>
                <label style={lbl}>Opacidade <input type="number" style={field} min={0} max={1} step={0.05} value={it.style.opacity ?? 1} onChange={e => onStyle({ opacity: clamp(num(parseFloat(e.target.value)), 0, 1) })} /></label>
            </div>
            {!free && it.key !== 'illustration' && (
                <label style={lbl}>Etiqueta (vazio = predefinida) <input type="text" style={field} maxLength={60} value={it.props.label ?? ''} onChange={e => onProps({ label: e.target.value === '' ? null : e.target.value })} data-testid="prop-label" /></label>
            )}
            {NUMBER_KEYS.has(it.key) && (
                <label style={lbl}>Formato numérico (apresentação)
                    <select style={field} value={it.props.numberFormat ?? 'THOUSANDS'} onChange={e => onProps({ numberFormat: e.target.value as Viana3DesignNumberFormat })} data-testid="prop-numberformat">
                        {NUMBER_FORMATS.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
                    </select>
                </label>
            )}
            {it.key === 'stockAvailable' && <label style={lbl}>Sufixo de unidade <input type="text" style={field} maxLength={20} value={it.props.unitSuffix ?? ''} placeholder="un. disponíveis" onChange={e => onProps({ unitSuffix: e.target.value === '' ? null : e.target.value })} /></label>}
            {SECONDARY_KEYS.has(it.key) && <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}><input type="checkbox" checked={it.props.showSecondary !== false} onChange={e => onProps({ showSecondary: e.target.checked })} /> Mostrar linha secundária (estimativa / legenda / sufixo)</label>}
            {isFreeTextKey(it.key) && <label style={lbl}>Texto <textarea style={{ ...field, minHeight: 56 }} maxLength={200} value={it.props.text ?? ''} onChange={e => onProps({ text: e.target.value })} data-testid="prop-text" /></label>}
            {acceptsImage(it.key) && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>Imagem {it.key === 'illustration' ? '(substitui o desenho da linha)' : ''}: {imageAsset ? `${imageAsset.fileName} (${imageAsset.width}×${imageAsset.height})` : it.props.imageAssetId ? 'em falta' : 'nenhuma'}</div>
                    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                        <button type="button" className="btn-secondary" style={smallBtn} onClick={onPickImage} data-testid="prop-pick-image"><ImageIcon size={12} /> {it.props.imageAssetId ? 'Substituir' : 'Inserir'} imagem</button>
                        {it.props.imageAssetId && <button type="button" className="btn-secondary" style={smallBtn} onClick={() => onProps({ imageAssetId: null })}>Remover imagem</button>}
                        {it.props.imageAssetId && (
                            <select style={{ ...field, width: 'auto' }} value={it.props.imageFit ?? 'CONTAIN'} onChange={e => onProps({ imageFit: e.target.value as 'CONTAIN' | 'COVER' })}>
                                <option value="CONTAIN">Ajustar (contain)</option><option value="COVER">Preencher (cover)</option>
                            </select>
                        )}
                    </div>
                </div>
            )}
            {free && <button type="button" className="btn-secondary" style={{ ...smallBtn, color: '#b91c1c', borderColor: '#b91c1c', alignSelf: 'flex-start' }} onClick={onRemoveFree}><Trash2 size={12} /> Remover item livre</button>}
            {catalogItem && <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>{catalogItem.description}</div>}
        </div>
    );
}

// ─── Source panel (read-only) ───

function SourcePanel({ item, sourceDatabase }: { item: Viana3CardItem; sourceDatabase: string }) {
    const [showTech, setShowTech] = useState(false);
    if (!item.dataDriven) return <div style={{ fontSize: 12, color: 'var(--color-text-muted)' }}>{item.description}</div>;
    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8, fontSize: 12 }} data-testid="source-panel">
            <div>{item.description}</div>
            <div style={{ fontSize: 11, color: 'var(--color-text-muted)' }}>Base de dados: {sourceDatabase}</div>
            {item.sources.map(s => (
                <div key={`${s.query}-${s.alias}`} style={{ border: '1px solid var(--color-border)', borderRadius: 8, padding: 8, display: 'flex', flexDirection: 'column', gap: 3 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 6, alignItems: 'center' }}>
                        <code style={{ fontSize: 11 }}>{s.payloadField}</code>
                        <span style={chip(s.verification === 'VERIFIED' ? '#16a34a' : '#b91c1c')} title={s.verificationDetail ?? ''}>
                            {s.verification === 'VERIFIED' ? <><CheckCircle2 size={10} /> verificado na consulta real</> : <><AlertTriangle size={10} /> não verificado</>}
                        </span>
                    </div>
                    <div>{s.query === 'SERVICE' ? <>Calculado no serviço do Portal (<code>{s.alias}</code>)</> : <>Tabela <b>{s.table}</b>{s.field ? <> · campo <b>{s.field}</b></> : null} · consulta {s.query}, alias <code>{s.alias}</code></>}</div>
                    {s.filters.length > 0 && <div style={{ color: 'var(--color-text-muted)' }}>Filtros: {s.filters.join('; ')}</div>}
                    {s.verification === 'VERIFIED' && s.expression && showTech && <pre style={{ fontSize: 10, whiteSpace: 'pre-wrap', margin: 0, background: 'var(--color-bg-page)', padding: 6, borderRadius: 6 }}>{s.expression}</pre>}
                    {s.verification === 'NOT_VERIFIED' && <div style={{ color: '#b91c1c' }}>{s.verificationDetail}</div>}
                </div>
            ))}
            {item.calculation && <div><b>Cálculo:</b> {item.calculation}</div>}
            {item.caveats.length > 0 && <ul style={{ margin: 0, paddingLeft: 16 }}>{item.caveats.map((c, i) => <li key={i}>{c}</li>)}</ul>}
            {Object.keys(item.configuredValues).length > 0 && (
                <div style={{ color: 'var(--color-text-muted)' }}>Identificadores configurados: {Object.entries(item.configuredValues).map(([k, v]) => `${k} = ${v}`).join(' · ')}</div>
            )}
            {item.sources.some(s => s.expression) && (
                <button type="button" className="btn-secondary" style={{ ...smallBtn, alignSelf: 'flex-start' }} onClick={() => setShowTech(v => !v)}>
                    <Copy size={12} /> {showTech ? 'Ocultar' : 'Mostrar'} detalhe técnico (extraído da consulta real, só leitura)
                </button>
            )}
        </div>
    );
}
