import React, { useState, useRef, useCallback, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
    Search, AlertTriangle, User, Upload, Printer,
    Database, UserCheck, Users, Camera, CheckCircle, XCircle, Info, Tag, CreditCard,
    Loader2, History, ExternalLink
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { PageHeader } from '../../components/ui/PageHeader';
import { ModernTooltip } from '../../components/ui/ModernTooltip';
import { BadgePreview, BadgeData, LayoutConfigV3 as LayoutConfig } from './BadgePreview';
import { apiFetch, API_BASE_URL } from '../../lib/api';
import './employee-workspace.css';

// ─── Types ───

interface EmployeeSearchResult {
    code: string;
    name: string;
    firstName?: string;
    lastName?: string;
    departmentName?: string;
    company?: string;
    cardNumber?: string;
    category?: string;
    hasPhoto: boolean;
    isActiveOperational?: boolean;
    isTemporarilyInactive?: boolean;
}

interface UnifiedProfile {
    employeeCode: string;
    company: string;
    primavera: {
        code: string;
        name: string;
        shortName?: string;
        firstName?: string;
        firstLastName?: string;
        secondLastName?: string;
        departmentCode?: string;
        departmentName?: string;
        isTemporarilyInactive?: boolean;
        sourceCompany?: string;
        identityDocNumber?: string;
        passportNumber?: string;
        nationality?: string;
    };
    innux?: {
        employeeNumber: string;
        cardNumber?: string;
        isActiveOperational?: boolean;
        departmentName?: string;
        category?: string;
        costCenter?: string;
        hasPhoto: boolean;
    };
    hasInnuxMatch: boolean;
    innuxLookupStatus: string;
}

const COMPANY_OPTIONS = [
    { value: 'ALPLAPLASTICO', label: 'Alpla Plástico' },
    { value: 'ALPLASOPRO', label: 'Alpla Sopro' }
];

/**
 * EmployeeWorkspace — HR Employee Registration & Badge Preparation (V1)
 *
 * Sections:
 * 1. Page header
 * 2. Primavera read-only notice
 * 3. Search panel (company + query)
 * 4. Search results table
 * 5. Employee details card
 * 6. Photo panel with upload
 * 7. Badge preview with print
 */
export default function EmployeeWorkspace() {
    const navigate = useNavigate();

    // ─── State ───
    const [company, setCompany] = useState('ALPLAPLASTICO');
    const [searchQuery, setSearchQuery] = useState('');
    const [searchResults, setSearchResults] = useState<EmployeeSearchResult[]>([]);
    const [isSearching, setIsSearching] = useState(false);
    const [searchError, setSearchError] = useState<string | null>(null);
    const [hasSearched, setHasSearched] = useState(false);

    const [selectedEmployee, setSelectedEmployee] = useState<EmployeeSearchResult | null>(null);
    const [profile, setProfile] = useState<UnifiedProfile | null>(null);
    const [isLoadingProfile, setIsLoadingProfile] = useState(false);

    // Manual Entry State
    const [isManualMode, setIsManualMode] = useState(false);
    const [manualFullName, setManualFullName] = useState('');
    const [manualDepartment, setManualDepartment] = useState('');

    // Badge Preparation State (Local/Temporal)
    const [badgeCategory, setBadgeCategory] = useState('');
    const [badgeCardNumber, setBadgeCardNumber] = useState('');

    // Photo state
    const [photoUrl, setPhotoUrl] = useState<string | null>(null);
    const [photoSource, setPhotoSource] = useState<'innux' | 'local' | null>(null);
    const [isLoadingPhoto, setIsLoadingPhoto] = useState(false);
    const fileInputRef = useRef<HTMLInputElement>(null);

    // Badge
    const badgePrintRef = useRef<HTMLDivElement>(null);

    // Layout selection
    interface LayoutSummary { id: string; name: string; version: number; status: string; }
    const [availableLayouts, setAvailableLayouts] = useState<LayoutSummary[]>([]);
    const [activeLayout, setActiveLayout] = useState<{ id: string; name: string; version: number; config?: LayoutConfig } | null>(null);
    const [isLoadingLayouts, setIsLoadingLayouts] = useState(false);

    // Print persistence state
    const [isPrinting, setIsPrinting] = useState(false);
    const [printResult, setPrintResult] = useState<{ success: boolean; historyId?: string; message?: string } | null>(null);

    // ─── Load Available Layouts on mount ───
    useEffect(() => {
        (async () => {
            setIsLoadingLayouts(true);
            try {
                const res = await apiFetch(`${API_BASE_URL}/api/badges/layouts?status=ACTIVE`);
                const data = await res.json();
                const layouts: LayoutSummary[] = Array.isArray(data) ? data : [];
                setAvailableLayouts(layouts);

                // Auto-select the first layout
                if (layouts.length > 0) {
                    await selectLayoutById(layouts[0].id, layouts[0].name, layouts[0].version);
                }
            } catch (err) {
                console.warn('Could not load active badge layouts, using defaults', err);
                setAvailableLayouts([]);
            } finally {
                setIsLoadingLayouts(false);
            }
        })();
    }, []);

    // ─── Select a specific layout by ID ───
    const selectLayoutById = async (layoutId: string, name: string, version: number) => {
        try {
            const detailRes = await apiFetch(`${API_BASE_URL}/api/badges/layouts/${layoutId}`);
            const detail = await detailRes.json();
            setActiveLayout({
                id: detail.id,
                name: detail.name,
                version: detail.version,
                config: detail.layoutConfigJson ? (() => {
                    try { return JSON.parse(detail.layoutConfigJson); } catch { return undefined; }
                })() : undefined,
            });
        } catch (err) {
            console.warn('Could not load layout detail:', err);
            setActiveLayout({ id: layoutId, name, version, config: undefined });
        }
    };

    const handleLayoutChange = async (layoutId: string) => {
        const found = availableLayouts.find(l => l.id === layoutId);
        if (found) {
            await selectLayoutById(found.id, found.name, found.version);
        }
    };

    const handleToggleManualMode = () => {
        setIsManualMode(!isManualMode);
        setSelectedEmployee(null);
        setProfile(null);
        setSearchResults([]);
        setSearchQuery('');
        setHasSearched(false);
        setPhotoUrl(null);
        setPhotoSource(null);
        setBadgeCategory('');
        setBadgeCardNumber('');
        setManualFullName('');
        setManualDepartment('');
        setPrintResult(null);
    };

    // ─── Search ───
    const handleSearch = useCallback(async () => {
        if (!searchQuery.trim() || searchQuery.trim().length < 2) return;

        setIsSearching(true);
        setSearchError(null);
        setHasSearched(true);
        // Reset selection on new search
        setSelectedEmployee(null);
        setProfile(null);
        setPhotoUrl(null);
        setPhotoSource(null);
        setBadgeCategory('');
        setBadgeCardNumber('');
        setPrintResult(null);

        try {
            const params = new URLSearchParams({
                query: searchQuery.trim(),
                company,
                limit: '20'
            });
            const res = await apiFetch(`${API_BASE_URL}/api/hr/employees/search?${params}`);
            const data = await res.json();
            setSearchResults(data.results || []);
        } catch (err: any) {
            console.error('Search error:', err);
            setSearchError(
                err?.message?.includes('503') || err?.message?.includes('502')
                    ? 'Serviço de integração indisponível. Tente novamente.'
                    : 'Erro ao pesquisar funcionários. Verifique a conexão.'
            );
            setSearchResults([]);
        } finally {
            setIsSearching(false);
        }
    }, [searchQuery, company]);

    const handleSearchKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') handleSearch();
    };

    // ─── Employee Selection ───
    const handleSelectEmployee = useCallback(async (emp: EmployeeSearchResult) => {
        setSelectedEmployee(emp);
        setIsLoadingProfile(true);
        setPhotoUrl(null);
        setPhotoSource(null);
        setBadgeCardNumber(''); // Always start empty (RFID)
        setPrintResult(null);

        try {
            // Load full profile
            const res = await apiFetch(
                `${API_BASE_URL}/api/hr/employees/${emp.code}?company=${company}`
            );
            const data: UnifiedProfile = await res.json();
            setProfile(data);

            // Initialize badge category from source
            setBadgeCategory(data.innux?.category || emp.category || '');

            // Try to load photo from Innux
            if (data.innux?.hasPhoto) {
                loadInnuxPhoto(emp.code);
            }
        } catch (err) {
            console.error('Profile load error:', err);
        } finally {
            setIsLoadingProfile(false);
        }
    }, [company]);

    // ─── Photo Loading ───
    const loadInnuxPhoto = async (code: string) => {
        setIsLoadingPhoto(true);
        try {
            const res = await apiFetch(`${API_BASE_URL}/api/hr/employees/${code}/photo`);
            if (res.ok) {
                const blob = await res.blob();
                const url = URL.createObjectURL(blob);
                setPhotoUrl(url);
                setPhotoSource('innux');
            }
        } catch {
            // Photo not available — silent fail
        } finally {
            setIsLoadingPhoto(false);
        }
    };

    // ─── Local Photo Upload ───
    const handlePhotoUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        // Validate file type
        if (!file.type.startsWith('image/')) {
            alert('Por favor, selecione um ficheiro de imagem válido.');
            return;
        }

        // Validate file size (max 5MB)
        if (file.size > 5 * 1024 * 1024) {
            alert('A imagem não pode exceder 5 MB.');
            return;
        }

        const url = URL.createObjectURL(file);
        setPhotoUrl(url);
        setPhotoSource('local');

        // Reset input
        if (fileInputRef.current) fileInputRef.current.value = '';
    };

    // ─── Print with Persistence ───
    const handlePrint = async () => {
        if (!badgeData || (!isManualMode && !profile) || isPrinting) return;

        setIsPrinting(true);
        setPrintResult(null);

        // Map photo source to API enum
        const apiPhotoSource = photoSource === 'innux'
            ? 'INNUX'
            : photoSource === 'local'
                ? 'MANUAL_UPLOAD'
                : 'NONE';

        // Build the snapshot: the exact data used to render the badge
        const snapshotPayload = {
            firstName: badgeData.firstName,
            lastName: badgeData.lastName,
            fullName: badgeData.fullName,
            department: badgeData.department,
            category: badgeData.category,
            employeeCode: badgeData.employeeCode,
            cardNumber: badgeData.cardNumber,
            company: badgeData.company,
            photoUrl: badgeData.photoUrl,
            documentLabel: badgeData.documentLabel,
            documentValue: badgeData.documentValue,
        };

        try {
            // 1. Persist the print record BEFORE triggering the print dialog
            const res = await apiFetch(`${API_BASE_URL}/api/badges/history`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    employeeCode: badgeData.employeeCode,
                    employeeName: badgeData.fullName || `${badgeData.firstName || ''} ${badgeData.lastName || ''}`.trim(),
                    department: badgeData.department || null,
                    category: badgeData.category || null,
                    cardNumber: badgeData.cardNumber || null,
                    companyCode: isManualMode ? company : (profile?.company || null),
                    plantCode: null,
                    photoSource: apiPhotoSource,
                    photoReference: photoSource === 'innux' ? `innux://${badgeData.employeeCode}` : null,
                    snapshotPayloadJson: JSON.stringify(snapshotPayload),
                    badgeLayoutId: activeLayout?.id || null,
                }),
            });

            const result = await res.json();

            // 2. Trigger the browser print dialog
            window.print();

            setPrintResult({
                success: true,
                historyId: result.id,
                message: 'Crachá impresso e registado com sucesso!',
            });
        } catch (err: any) {
            console.error('Print persistence error:', err);

            // Still allow printing even if persistence fails
            window.print();

            setPrintResult({
                success: false,
                message: 'Crachá enviado para impressão, mas o registro no histórico falhou. Tente reimprimir pela área de Histórico.',
            });
        } finally {
            setIsPrinting(false);
        }
    };

    // ─── Resolve Identity Document (B.I. vs Passaporte) ───
    // Rule: Nacionalidade-based primary, document-number fallback
    const resolveDocument = (p: UnifiedProfile['primavera']): { label?: string; value?: string } => {
        const nat = p.nationality?.trim().toUpperCase();
        const bi = p.identityDocNumber?.trim();
        const passport = p.passportNumber?.trim();

        // Primary rule: nationality determines document type
        if (nat) {
            const isNational = ['AO', 'ANGOLA', 'ANGOLANA'].includes(nat);
            if (isNational && bi) return { label: 'B.I.', value: bi };
            if (!isNational && passport) return { label: 'Passaporte', value: passport };
            // Nationality known but preferred document missing — try the other
            if (isNational && passport) return { label: 'Passaporte', value: passport };
            if (!isNational && bi) return { label: 'B.I.', value: bi };
        }

        // Fallback: no nationality — use whichever document is available
        if (bi) return { label: 'B.I.', value: bi };
        if (passport) return { label: 'Passaporte', value: passport };

        return {}; // No document data available
    };

    const docInfo = selectedEmployee && profile ? resolveDocument(profile.primavera) : {};

    // ─── Build Badge Data ───
    const badgeData: BadgeData | null = (() => {
        if (isManualMode) {
            const lastSpaceIndex = manualFullName.lastIndexOf(' ');
            return {
                firstName: lastSpaceIndex > 0 ? manualFullName.substring(0, lastSpaceIndex).trim() : manualFullName,
                lastName: lastSpaceIndex > 0 ? manualFullName.substring(lastSpaceIndex + 1).trim() : undefined,
                fullName: manualFullName,
                department: manualDepartment || undefined,
                category: badgeCategory || undefined,
                employeeCode: `MANUAL-${Date.now()}`,
                cardNumber: badgeCardNumber || undefined,
                company,
                photoUrl,
            };
        }
        if (selectedEmployee && profile) {
            return {
                firstName: profile.primavera.firstName || undefined,
                lastName: profile.primavera.firstLastName || undefined,
                fullName: profile.primavera.name,
                department: profile.primavera.departmentName || profile.innux?.departmentName || undefined,
                category: badgeCategory || undefined, // Use session override
                employeeCode: profile.primavera.code,
                cardNumber: badgeCardNumber || undefined, // Use session override (RFID)
                company: profile.primavera.sourceCompany || profile.company,
                photoUrl,
                documentLabel: docInfo.label,
                documentValue: docInfo.value,
            };
        }
        return null;
    })();

    const isPrintDisabled = isManualMode
        ? (!manualFullName || !badgeCategory || !badgeCardNumber || isPrinting)
        : (!badgeCategory || !badgeCardNumber || isPrinting);


    return (
        <div>
            <PageHeader
                title="Cadastro de Funcionários"
                subtitle="Consulta de dados e preparação de crachás"
                icon={<Users size={24} strokeWidth={2.5} />}
            />

            {/* ── Primavera Read-Only Notice ── */}
            <div className="hr-primavera-notice">
                <AlertTriangle size={20} className="notice-icon" />
                <div>
                    <div className="notice-title">Consulta de Dados — Somente Leitura</div>
                    <div className="notice-body">
                        Este espaço de trabalho consulta dados de funcionários diretamente do Primavera e do Innux.
                        <strong> Não é possível editar, criar ou remover registos de funcionários</strong> a partir deste portal.
                        Para alterações cadastrais, utilize os sistemas de origem.
                    </div>
                </div>
            </div>

            {/* ── Search Panel ── */}
            <div className="hr-search-panel">
                <div className="hr-search-row">
                    {!isManualMode ? (
                        <>
                            <div className="hr-search-field">
                                <label htmlFor="hr-company-select">Empresa</label>
                                <select
                                    id="hr-company-select"
                                    value={company}
                                    onChange={(e) => setCompany(e.target.value)}
                                >
                                    {COMPANY_OPTIONS.map(c => (
                                        <option key={c.value} value={c.value}>{c.label}</option>
                                    ))}
                                </select>
                            </div>
                            <div className="hr-search-field" style={{ flex: 1 }}>
                                <label htmlFor="hr-search-input">Pesquisar Funcionário (Primavera)</label>
                                <input
                                    id="hr-search-input"
                                    type="text"
                                    placeholder="Código, nome ou nº de cartão..."
                                    value={searchQuery}
                                    onChange={(e) => setSearchQuery(e.target.value)}
                                    onKeyDown={handleSearchKeyDown}
                                    autoComplete="off"
                                />
                            </div>
                        </>
                    ) : (
                        <div style={{ padding: '10px 0', fontWeight: 500, color: 'var(--color-primary)', flex: 1, display: 'flex', alignItems: 'center' }}>
                            <UserCheck size={18} style={{ marginRight: '8px' }} /> 
                            Modo de Criação Manual Ativo (Visitantes / Provisórios)
                        </div>
                    )}
                    
                    <button
                        className={`hr-search-btn ${isManualMode ? 'outline' : ''}`}
                        onClick={isManualMode ? handleToggleManualMode : handleSearch}
                        disabled={!isManualMode && (isSearching || searchQuery.trim().length < 2)}
                    >
                        {isSearching ? <span className="hr-spinner" /> : <Search size={16} />}
                        {isManualMode ? 'Voltar à Pesquisa' : 'Pesquisar'}
                    </button>

                    {!isManualMode && (
                        <button
                            className="hr-search-btn outline"
                            onClick={handleToggleManualMode}
                            style={{ marginLeft: '12px', background: 'transparent', color: 'var(--color-text-main)', border: '1px solid var(--color-border)' }}
                            title="Criar crachá provisório ou de visitante"
                        >
                            <User size={16} /> Entrada Manual
                        </button>
                    )}
                </div>

                {/* Search Error */}
                {searchError && (
                    <motion.div
                        initial={{ opacity: 0, y: -8 }}
                        animate={{ opacity: 1, y: 0 }}
                        style={{
                            padding: '10px 16px', background: 'rgba(220, 38, 38, 0.08)',
                            border: '1px solid rgba(220, 38, 38, 0.2)', borderRadius: 'var(--radius-md)',
                            color: '#dc2626', fontSize: '0.85rem', fontWeight: 500
                        }}
                    >
                        {searchError}
                    </motion.div>
                )}

                {/* Search Results */}
                <AnimatePresence>
                    {hasSearched && !isSearching && searchResults.length > 0 && (
                        <motion.div
                            initial={{ opacity: 0, y: 10 }}
                            animate={{ opacity: 1, y: 0 }}
                            exit={{ opacity: 0, y: -10 }}
                            transition={{ duration: 0.2 }}
                        >
                            <table className="hr-results-table">
                                <thead>
                                    <tr>
                                        <th>Código</th>
                                        <th>Nome</th>
                                        <th>Departamento</th>
                                        <th>Cartão</th>
                                        <th>Foto</th>
                                        <th>Status</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {searchResults.map(emp => (
                                        <tr
                                            key={emp.code}
                                            onClick={() => handleSelectEmployee(emp)}
                                            className={selectedEmployee?.code === emp.code ? 'selected' : ''}
                                        >
                                            <td style={{ fontWeight: 700, fontFamily: 'var(--font-family-display)' }}>
                                                {emp.code}
                                            </td>
                                            <td>{emp.name}</td>
                                            <td>{emp.departmentName || '—'}</td>
                                            <td style={{ fontFamily: 'monospace', fontWeight: 600 }}>
                                                {emp.cardNumber || '—'}
                                            </td>
                                            <td>
                                                {emp.hasPhoto ? (
                                                    <Camera size={14} color="#16a34a" />
                                                ) : (
                                                    <span style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>—</span>
                                                )}
                                            </td>
                                            <td>
                                                <EmployeeStatusBadge
                                                    isInactive={emp.isTemporarilyInactive}
                                                    isActiveOp={emp.isActiveOperational}
                                                />
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                            <div style={{
                                fontSize: '0.75rem', color: 'var(--color-text-muted)',
                                marginTop: '8px', fontWeight: 500
                            }}>
                                {searchResults.length} resultado{searchResults.length !== 1 ? 's' : ''} encontrado{searchResults.length !== 1 ? 's' : ''}
                            </div>
                        </motion.div>
                    )}

                    {hasSearched && !isSearching && searchResults.length === 0 && !searchError && (
                        <motion.div
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            style={{
                                textAlign: 'center', padding: '24px',
                                color: 'var(--color-text-muted)', fontSize: '0.85rem'
                            }}
                        >
                            Nenhum funcionário encontrado para "<strong>{searchQuery}</strong>" em{' '}
                            {COMPANY_OPTIONS.find(c => c.value === company)?.label}.
                        </motion.div>
                    )}
                </AnimatePresence>
            </div>

            {/* ── Workspace Grid (appears after selection) ── */}
            <AnimatePresence>
                {(selectedEmployee || isManualMode) && (
                    <motion.div
                        initial={{ opacity: 0, y: 20 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0, y: -20 }}
                        transition={{ duration: 0.3 }}
                        className="hr-workspace-grid"
                    >
                        {/* Left Column — Employee Details & Configuration */}
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                            {/* Badge Configuration Card (Manual Overrides) */}
                            <div className="hr-card" style={{ borderColor: 'var(--color-primary-soft)' }}>
                                <div className="hr-card-header" style={{ color: 'var(--color-primary)' }}>
                                    <Tag size={16} />
                                    Configuração do Crachá
                                </div>
                                <div className="hr-card-body">
                                    <div style={{ marginBottom: '16px', fontSize: '0.8rem', color: 'var(--color-text-muted)', fontStyle: 'italic' }}>
                                        Estas definições são temporárias e aplicáveis apenas para esta sessão de impressão.
                                    </div>
                                    <div className="hr-form-grid">
                                        {/* Layout Selector */}
                                        <div className="hr-form-field" style={{ gridColumn: '1 / -1' }}>
                                            <label style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                Layout do Crachá
                                                <ModernTooltip content="Selecione o layout publicado a utilizar para a impressão do crachá. Apenas layouts com estado 'Activo' aparecem aqui.">
                                                    <Info size={14} style={{ cursor: 'help', color: 'var(--color-primary)' }} />
                                                </ModernTooltip>
                                            </label>
                                            {isLoadingLayouts ? (
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 12px', fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
                                                    <Loader2 size={14} className="hr-spinner" /> Carregando layouts...
                                                </div>
                                            ) : availableLayouts.length === 0 ? (
                                                <div style={{
                                                    display: 'flex', alignItems: 'center', gap: '8px',
                                                    padding: '10px 14px', fontSize: '0.8rem',
                                                    color: '#92400e', background: 'rgba(245, 158, 11, 0.08)',
                                                    borderRadius: '6px', border: '1px solid rgba(245, 158, 11, 0.15)'
                                                }}>
                                                    <AlertTriangle size={14} />
                                                    Nenhum layout publicado disponível. Crie e publique um layout na secção de Layouts de Crachá.
                                                </div>
                                            ) : availableLayouts.length === 1 ? (
                                                <div style={{
                                                    display: 'flex', alignItems: 'center', gap: '8px',
                                                    padding: '8px 12px', fontSize: '0.82rem',
                                                    color: 'var(--color-text-primary)', fontWeight: 600,
                                                    background: 'rgba(37, 99, 235, 0.04)',
                                                    borderRadius: '6px', border: '1px solid var(--color-border)'
                                                }}>
                                                    <CheckCircle size={14} style={{ color: '#059669' }} />
                                                    {availableLayouts[0].name} <span style={{ fontWeight: 400, color: 'var(--color-text-muted)' }}>v{availableLayouts[0].version}</span>
                                                </div>
                                            ) : (
                                                <select
                                                    value={activeLayout?.id || ''}
                                                    onChange={(e) => handleLayoutChange(e.target.value)}
                                                    style={{ fontWeight: 600 }}
                                                >
                                                    <option value="" disabled>Selecionar layout...</option>
                                                    {availableLayouts.map(l => (
                                                        <option key={l.id} value={l.id}>
                                                            {l.name} (v{l.version})
                                                        </option>
                                                    ))}
                                                </select>
                                            )}
                                        </div>
                                        <div className="hr-form-field">
                                            <label>Categoria / Função no Crachá</label>
                                            <input
                                                type="text"
                                                value={badgeCategory}
                                                onChange={(e) => setBadgeCategory(e.target.value)}
                                                placeholder="Ex: Operador de Produção"
                                            />
                                        </div>
                                        <div className="hr-form-field">
                                            <label style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                Nº do Cartão RFID
                                                <ModernTooltip content="Introduza o número de 8 caracteres (letras e números) impresso no cartão físico (ex: 0097AD86).">
                                                    <Info size={14} style={{ cursor: 'help', color: 'var(--color-primary)' }} />
                                                </ModernTooltip>
                                            </label>
                                            <div style={{ position: 'relative' }}>
                                                <input
                                                    type="text"
                                                    value={badgeCardNumber}
                                                    onChange={(e) => setBadgeCardNumber(e.target.value.toUpperCase())}
                                                    placeholder="Ex: 0097AD86"
                                                    maxLength={8}
                                                    style={{ fontFamily: 'monospace', fontWeight: 600 }}
                                                />
                                                <CreditCard size={14} style={{ position: 'absolute', right: '12px', top: '50%', transform: 'translateY(-50%)', opacity: 0.3 }} />
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>

                            {/* Employee Data Card (Source System or Manual Input) */}
                            <div className="hr-card" style={{ opacity: isManualMode ? 1 : 0.85 }}>
                                <div className="hr-card-header">
                                    <Database size={16} />
                                    {isManualMode ? 'Dados do Crachá (Manual)' : 'Dados do Funcionário (Origem)'}
                                    {isLoadingProfile && <span className="hr-spinner" style={{ marginLeft: 'auto' }} />}
                                </div>
                                <div className="hr-card-body">
                                    {isManualMode ? (
                                        <div className="hr-form-grid">
                                            <div className="hr-form-field">
                                                <label>Nome no Crachá *</label>
                                                <input
                                                    type="text"
                                                    value={manualFullName}
                                                    onChange={e => setManualFullName(e.target.value.toUpperCase())}
                                                    placeholder="Ex: JOÃO SILVA"
                                                />
                                            </div>
                                            <div className="hr-form-field">
                                                <label>Departamento</label>
                                                <input
                                                    type="text"
                                                    value={manualDepartment}
                                                    onChange={e => setManualDepartment(e.target.value)}
                                                    placeholder="Ex: Visitantes"
                                                />
                                            </div>
                                            <div className="hr-form-field">
                                                <label>Empresa Correspondente</label>
                                                <select
                                                    value={company}
                                                    onChange={(e) => setCompany(e.target.value)}
                                                >
                                                    {COMPANY_OPTIONS.map(c => (
                                                        <option key={c.value} value={c.value}>{c.label}</option>
                                                    ))}
                                                </select>
                                            </div>
                                        </div>
                                    ) : profile ? (
                                        <div className="hr-details-grid">
                                            <DetailItem
                                                label="Código Primavera"
                                                value={profile.primavera.code}
                                            />
                                            <DetailItem
                                                label="Nome Completo"
                                                value={profile.primavera.name}
                                            />
                                            <DetailItem
                                                label="Departamento"
                                                value={profile.primavera.departmentName || profile.innux?.departmentName}
                                            />
                                            <DetailItem
                                                label="Categoria (Innux)"
                                                value={profile.innux?.category}
                                            />
                                            <DetailItem
                                                label="Empresa"
                                                value={resolveCompanyDisplay(profile.primavera.sourceCompany)}
                                            />
                                            <DetailItem
                                                label="Nº Cartão (Innux)"
                                                value={profile.innux?.cardNumber}
                                                mono
                                            />
                                            <DetailItem
                                                label="Status"
                                                value={<EmployeeStatusBadge
                                                    isInactive={profile.primavera.isTemporarilyInactive}
                                                    isActiveOp={profile.innux?.isActiveOperational}
                                                />}
                                            />
                                        </div>
                                    ) : isLoadingProfile ? (
                                        <div style={{ textAlign: 'center', padding: '24px', color: 'var(--color-text-muted)' }}>
                                            A carregar dados do funcionário...
                                        </div>
                                    ) : null}

                                    {/* Data Sources Diagnostic */}
                                    {!isManualMode && profile && (
                                        <div style={{
                                            marginTop: '20px', paddingTop: '16px',
                                            borderTop: '1px solid var(--color-border)',
                                            display: 'flex', gap: '16px', flexWrap: 'wrap',
                                            fontSize: '0.72rem', color: 'var(--color-text-muted)'
                                        }}>
                                            <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                <Database size={12} /> Primavera: <strong style={{ color: 'var(--color-status-green)' }}>OK</strong>
                                            </span>
                                            <span style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
                                                <Database size={12} /> Innux: <strong style={{
                                                    color: profile.innuxLookupStatus === 'MATCHED'
                                                        ? 'var(--color-status-green)'
                                                        : profile.innuxLookupStatus === 'ERROR'
                                                            ? 'var(--color-status-red)'
                                                            : 'var(--color-text-muted)'
                                                }}>
                                                    {profile.innuxLookupStatus === 'MATCHED' ? 'OK' :
                                                        profile.innuxLookupStatus === 'ERROR' ? 'Erro' : 'Não encontrado'}
                                                </strong>
                                            </span>
                                        </div>
                                    )}
                                </div>
                            </div>
                        </div>

                        {/* Right Column — Photo + Badge */}
                        <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
                            {/* Photo Panel */}
                            <div className="hr-card">
                                <div className="hr-card-header">
                                    <Camera size={16} />
                                    Foto do Funcionário
                                </div>
                                <div className="hr-card-body">
                                    <div className="hr-photo-container">
                                        <div className={`hr-photo-frame ${photoUrl ? 'has-photo' : ''}`}>
                                            {isLoadingPhoto ? (
                                                <span className="hr-spinner" />
                                            ) : photoUrl ? (
                                                <img src={photoUrl} alt="Employee" />
                                            ) : (
                                                <User size={48} color="var(--color-text-muted)" style={{ opacity: 0.3 }} />
                                            )}
                                        </div>

                                        {/* Photo source indicator */}
                                        {photoSource && (
                                            <span className={`hr-photo-source ${photoSource}`}>
                                                {photoSource === 'innux' ? (
                                                    <><Database size={10} /> Foto Innux</>
                                                ) : (
                                                    <><Upload size={10} /> Upload Local</>
                                                )}
                                            </span>
                                        )}

                                        {/* Upload button */}
                                        <button
                                            className="hr-photo-upload-btn"
                                            onClick={() => fileInputRef.current?.click()}
                                        >
                                            <Upload size={14} />
                                            {photoUrl ? 'Substituir Foto' : 'Carregar Foto'}
                                        </button>
                                        <input
                                            ref={fileInputRef}
                                            type="file"
                                            accept="image/*"
                                            onChange={handlePhotoUpload}
                                            style={{ display: 'none' }}
                                        />

                                        {photoSource === 'local' && (
                                            <div style={{
                                                fontSize: '0.72rem', color: '#ca8a04',
                                                textAlign: 'center', fontWeight: 500,
                                                maxWidth: '200px', lineHeight: 1.4
                                            }}>
                                                Esta foto é apenas para uso operacional do portal / preparação de crachá.
                                            </div>
                                        )}
                                    </div>
                                </div>
                            </div>

                            {/* Badge Preview Panel */}
                            <div className="hr-card">
                                <div className="hr-card-header">
                                    <UserCheck size={16} />
                                    Pré-visualização do Crachá
                                </div>
                                <div className="hr-card-body">
                                    <div className="hr-badge-preview-wrapper">
                                        <BadgePreview
                                            data={badgeData}
                                            printRef={badgePrintRef}
                                            layoutConfig={activeLayout?.config}
                                        />


                                        {badgeData && (
                                            <div className="hr-action-bar" style={{ flexDirection: 'column', alignItems: 'center', gap: '12px' }}>
                                                {!isPrinting && !printResult && isPrintDisabled && (
                                                    <div className="hr-validation-bubble">
                                                        <AlertTriangle size={14} />
                                                        <span>
                                                            {isManualMode 
                                                                ? <>Preencha o <strong>Nome no Crachá</strong>, a <strong>Categoria</strong> e o <strong>Nº do Cartão RFID</strong> para imprimir.</>
                                                                : <>Preencha a <strong>Categoria</strong> e o <strong>Nº do Cartão RFID</strong> para imprimir.</>
                                                            }
                                                        </span>
                                                    </div>
                                                )}

                                                {/* Print Result Feedback */}
                                                {printResult && (
                                                    <motion.div
                                                        initial={{ opacity: 0, y: -8 }}
                                                        animate={{ opacity: 1, y: 0 }}
                                                        style={{
                                                            padding: '12px 16px',
                                                            borderRadius: 'var(--radius-md)',
                                                            fontSize: '0.85rem',
                                                            fontWeight: 500,
                                                            display: 'flex',
                                                            flexDirection: 'column',
                                                            gap: '8px',
                                                            alignItems: 'center',
                                                            width: '100%',
                                                            background: printResult.success
                                                                ? 'rgba(16, 185, 129, 0.08)'
                                                                : 'rgba(220, 38, 38, 0.08)',
                                                            border: `1px solid ${printResult.success
                                                                ? 'rgba(16, 185, 129, 0.25)'
                                                                : 'rgba(220, 38, 38, 0.25)'}`,
                                                            color: printResult.success ? '#059669' : '#dc2626',
                                                        }}
                                                    >
                                                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                                            {printResult.success
                                                                ? <CheckCircle size={16} />
                                                                : <AlertTriangle size={16} />}
                                                            {printResult.message}
                                                        </div>
                                                        {printResult.success && (
                                                            <button
                                                                onClick={() => navigate('/hr/badges/history')}
                                                                style={{
                                                                    display: 'flex', alignItems: 'center', gap: '4px',
                                                                    fontSize: '0.8rem', color: 'var(--color-primary)',
                                                                    background: 'none', border: 'none', cursor: 'pointer',
                                                                    textDecoration: 'underline', fontWeight: 600,
                                                                    padding: 0,
                                                                }}
                                                            >
                                                                <History size={13} />
                                                                Ver no Histórico de Impressão
                                                                <ExternalLink size={11} />
                                                            </button>
                                                        )}
                                                    </motion.div>
                                                )}
                                                
                                                <button
                                                    className="hr-action-primary"
                                                    onClick={handlePrint}
                                                    disabled={isPrintDisabled}
                                                    title={isPrintDisabled ? "Campos obrigatórios em falta" : "Imprimir crachá"}
                                                >
                                                    {isPrinting ? (
                                                        <><Loader2 size={16} className="hr-spinner" /> Registando...</>
                                                    ) : (
                                                        <><Printer size={16} /> Imprimir Crachá</>
                                                    )}
                                                </button>
                                            </div>
                                        )}
                                    </div>
                                </div>
                            </div>
                        </div>
                    </motion.div>
                )}
            </AnimatePresence>

            {/* ── Empty State (before first search) ── */}
            {!hasSearched && !selectedEmployee && !isManualMode && (
                <div className="hr-empty-state">
                    <Search size={48} className="icon" />
                    <div className="title">Pesquise um funcionário para começar</div>
                    <div className="subtitle">
                        Utilize o campo acima para localizar um funcionário por código, nome ou número de cartão.
                    </div>
                </div>
            )}
        </div>
    );
}

// ─── Sub-components ───

function DetailItem({ label, value, mono }: {
    label: string;
    value?: React.ReactNode;
    mono?: boolean;
}) {
    const isEmpty = value === undefined || value === null || value === '';
    return (
        <div className="hr-detail-item">
            <span className="hr-detail-label">{label}</span>
            <span
                className={`hr-detail-value ${isEmpty ? 'empty' : ''}`}
                style={mono && !isEmpty ? { fontFamily: 'monospace', fontWeight: 600 } : undefined}
            >
                {isEmpty ? 'Não disponível' : value}
            </span>
        </div>
    );
}

function EmployeeStatusBadge({ isInactive, isActiveOp }: {
    isInactive?: boolean | null;
    isActiveOp?: boolean | null;
}) {
    // Primavera inactive takes priority
    if (isInactive === true) {
        return (
            <span className="hr-status-badge inactive">
                <XCircle size={12} /> Inactivo
            </span>
        );
    }

    // Fall back to Innux operational status
    if (isActiveOp === false) {
        return (
            <span className="hr-status-badge inactive">
                <XCircle size={12} /> Inactivo
            </span>
        );
    }

    if (isActiveOp === true || isInactive === false) {
        return (
            <span className="hr-status-badge active">
                <CheckCircle size={12} /> Activo
            </span>
        );
    }

    // Unknown
    return <span style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>—</span>;
}

function resolveCompanyDisplay(company?: string): string {
    if (!company) return 'Não disponível';
    const labels: Record<string, string> = {
        'ALPLAPLASTICO': 'Alpla Plástico',
        'ALPLASOPRO': 'Alpla Sopro'
    };
    return labels[company.toUpperCase()] || company;
}
