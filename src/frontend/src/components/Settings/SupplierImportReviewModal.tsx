import React, { useState, useMemo, useCallback } from 'react';
import { X, AlertTriangle, CheckCircle2, Download, Edit3, FileText, Loader2, Info, ShieldAlert } from 'lucide-react';
import { DropdownPortal } from '../ui/DropdownPortal';
import { Z_INDEX } from '../../constants/ui';
import { SupplierSyncPreviewItemDto, ReviewedSupplierItemDto, SyncImportResultDto } from '../../types';
import { api } from '../../lib/api';
import '../../styles/sync-workspace.css';

// ─── NIF Validation ──────────────────────────────────────────────────────────

interface NifWarning {
    type: 'artifact' | 'letters' | 'slash' | 'length' | 'spacing';
    message: string;
}

function validateNif(nif: string | null | undefined): NifWarning[] {
    if (!nif || nif.trim().length === 0) return [];
    const warnings: NifWarning[] = [];
    const trimmed = nif.trim();

    // Leading colon or other artifacts
    if (/^[:\-–—]/.test(trimmed)) {
        warnings.push({ type: 'artifact', message: 'NIF começa com caractere inválido (ex: ":")' });
    }

    // Extra internal spaces
    if (/\s{2,}/.test(trimmed) || (trimmed.includes(' ') && /\d/.test(trimmed))) {
        warnings.push({ type: 'spacing', message: 'NIF contém espaços inesperados' });
    }

    // Letters mixed with digits (e.g. AO5417002348, 007784459KS048)
    if (/[a-zA-Z]/.test(trimmed) && /\d/.test(trimmed)) {
        warnings.push({ type: 'letters', message: 'NIF contém letras misturadas com dígitos' });
    }

    // Slash (e.g. 0135565/006)
    if (trimmed.includes('/')) {
        warnings.push({ type: 'slash', message: 'NIF contém barra (/)' });
    }

    // Suspicious length
    if (trimmed.length < 9 || trimmed.length > 14) {
        warnings.push({ type: 'length', message: `Comprimento incomum (${trimmed.length} caracteres)` });
    }

    return warnings;
}

// ─── Props ───────────────────────────────────────────────────────────────────

interface SupplierImportReviewModalProps {
    isOpen: boolean;
    onClose: () => void;
    onImportSuccess: (result: SyncImportResultDto, importedCodes: string[]) => void;
    selectedSuppliers: SupplierSyncPreviewItemDto[];
    companyId: number;
}

// ─── Row state ───────────────────────────────────────────────────────────────

interface ReviewRow {
    primaveraCode: string;
    originalName: string;
    originalTaxId: string;
    editedName: string;
    editedTaxId: string;
    editedNotes: string;
}

// ─── Component ───────────────────────────────────────────────────────────────

export function SupplierImportReviewModal({
    isOpen,
    onClose,
    onImportSuccess,
    selectedSuppliers,
    companyId
}: SupplierImportReviewModalProps) {
    // ── Initialize editable rows from selected suppliers ──
    const initialRows = useMemo((): ReviewRow[] => {
        return selectedSuppliers.map(s => ({
            primaveraCode: s.primaveraCode,
            originalName: s.primaveraName ?? '',
            originalTaxId: s.primaveraTaxId ?? '',
            editedName: s.primaveraName ?? '',
            editedTaxId: s.primaveraTaxId ?? '',
            editedNotes: ''
        }));
    }, [selectedSuppliers]);

    const [rows, setRows] = useState<ReviewRow[]>(initialRows);
    const [importing, setImporting] = useState(false);
    const [showWarningGate, setShowWarningGate] = useState(false);
    const [importError, setImportError] = useState<string | null>(null);

    // Reset state when modal opens
    React.useEffect(() => {
        if (isOpen) {
            setRows(initialRows);
            setImporting(false);
            setShowWarningGate(false);
            setImportError(null);
        }
    }, [isOpen, initialRows]);

    // ── Row update handler ──
    const updateRow = useCallback((idx: number, field: 'editedName' | 'editedTaxId' | 'editedNotes', value: string) => {
        setRows(prev => prev.map((r, i) => i === idx ? { ...r, [field]: value } : r));
    }, []);

    // ── Computed validation ──
    const rowWarnings = useMemo(() => {
        return rows.map(r => validateNif(r.editedTaxId));
    }, [rows]);

    const hasAnyWarnings = rowWarnings.some(w => w.length > 0);
    const hasEmptyNames = rows.some(r => !r.editedName.trim());
    const totalSuppliers = rows.length;

    const warningCount = rowWarnings.filter(w => w.length > 0).length;
    const cleanCount = totalSuppliers - warningCount;

    // ── Did user modify the NIF from original? ──
    const nifModifiedFlags = useMemo(() => {
        return rows.map(r => r.editedTaxId.trim() !== r.originalTaxId.trim());
    }, [rows]);

    // ── Import handler ──
    const handleConfirmImport = async () => {
        if (hasEmptyNames) return;

        // If there are warnings and user hasn't confirmed, show gate
        if (hasAnyWarnings && !showWarningGate) {
            setShowWarningGate(true);
            return;
        }

        setImporting(true);
        setImportError(null);

        try {
            const body = {
                suppliers: rows.map((r): ReviewedSupplierItemDto => ({
                    primaveraCode: r.primaveraCode,
                    name: r.editedName.trim(),
                    taxId: r.editedTaxId.trim() || null,
                    notes: r.editedNotes.trim() || null
                }))
            };

            const result = await api.sync.suppliers.importReviewed(companyId, body);
            const importedCodes = rows.map(r => r.primaveraCode);
            onImportSuccess(result, importedCodes);
        } catch (err: any) {
            setImportError(err.message || 'Erro ao importar fornecedores.');
        } finally {
            setImporting(false);
            setShowWarningGate(false);
        }
    };

    if (!isOpen) return null;

    // ─── Styles ──────────────────────────────────────────────────────────

    const overlayStyle: React.CSSProperties = {
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: 'rgba(0,0,0,0.7)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: Z_INDEX.MODAL as any,
        padding: '24px',
        animation: 'syncSlideIn 0.2s ease'
    };

    const modalStyle: React.CSSProperties = {
        backgroundColor: 'var(--color-bg-surface)',
        borderRadius: 'var(--radius-md)',
        maxWidth: '1000px',
        width: '100%',
        maxHeight: '85vh',
        display: 'flex',
        flexDirection: 'column',
        border: '1px solid var(--color-border)',
        boxShadow: 'var(--shadow-lg, 0 10px 40px rgba(0,0,0,0.2))',
        overflow: 'hidden'
    };

    const headerStyle: React.CSSProperties = {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '20px 24px',
        borderBottom: '1px solid var(--color-border)',
        flexShrink: 0
    };

    const bodyStyle: React.CSSProperties = {
        flex: 1,
        overflowY: 'auto',
        padding: '0'
    };

    const footerStyle: React.CSSProperties = {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '16px 24px',
        borderTop: '1px solid var(--color-border)',
        backgroundColor: 'var(--color-bg-page)',
        flexShrink: 0,
        gap: '12px'
    };

    const inputStyle: React.CSSProperties = {
        width: '100%',
        padding: '7px 10px',
        backgroundColor: 'var(--color-bg-page)',
        border: '1px solid var(--color-border)',
        borderRadius: 'var(--radius-sm)',
        fontSize: '12px',
        color: 'var(--color-text-main)',
        fontFamily: 'inherit',
        outline: 'none',
        transition: 'border-color 0.15s'
    };

    const warningInputStyle: React.CSSProperties = {
        ...inputStyle,
        borderColor: '#f59e0b',
        backgroundColor: '#FFFBEB'
    };

    const errorInputStyle: React.CSSProperties = {
        ...inputStyle,
        borderColor: 'var(--color-status-red, #ef4444)',
        backgroundColor: '#FEF2F2'
    };

    return (
        <DropdownPortal>
            <div style={overlayStyle} onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
                <div style={modalStyle}>
                    {/* ── Header ── */}
                    <div style={headerStyle}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                            <Edit3 size={20} style={{ color: 'var(--color-primary)' }} />
                            <div>
                                <h2 style={{
                                    fontSize: '16px',
                                    fontWeight: 700,
                                    color: 'var(--color-text-main)',
                                    margin: 0,
                                    fontFamily: 'var(--font-family-display)',
                                    letterSpacing: '-0.01em'
                                }}>
                                    Revisar Importação de Fornecedores
                                </h2>
                                <p style={{
                                    fontSize: '12px',
                                    color: 'var(--color-text-muted)',
                                    margin: '2px 0 0 0'
                                }}>
                                    {totalSuppliers} fornecedor{totalSuppliers !== 1 ? 'es' : ''} selecionado{totalSuppliers !== 1 ? 's' : ''} para revisão
                                </p>
                            </div>
                        </div>
                        <button
                            onClick={onClose}
                            disabled={importing}
                            style={{
                                background: 'none',
                                border: 'none',
                                cursor: importing ? 'not-allowed' : 'pointer',
                                color: 'var(--color-text-muted)',
                                padding: '4px',
                                borderRadius: 'var(--radius-sm)',
                                transition: 'color 0.15s'
                            }}
                        >
                            <X size={20} />
                        </button>
                    </div>

                    {/* ── Info banner ── */}
                    <div style={{
                        padding: '12px 24px',
                        backgroundColor: 'rgba(52, 152, 219, 0.06)',
                        borderBottom: '1px solid rgba(52, 152, 219, 0.12)',
                        display: 'flex',
                        alignItems: 'flex-start',
                        gap: '10px',
                        flexShrink: 0
                    }}>
                        <Info size={15} style={{ color: 'var(--color-primary)', flexShrink: 0, marginTop: '1px' }} />
                        <p style={{ fontSize: '12px', color: 'var(--color-text-main)', margin: 0, lineHeight: 1.5 }}>
                            Revise e corrija os dados antes de importar. O <strong>Código Primavera</strong> é apenas leitura.
                            Os fornecedores serão criados como <strong>rascunho</strong> — complete a ficha em Contratos → Fichas de Fornecedor.
                        </p>
                    </div>

                    {/* ── Error banner ── */}
                    {importError && (
                        <div style={{
                            padding: '10px 24px',
                            backgroundColor: 'rgba(239, 68, 68, 0.08)',
                            borderBottom: '1px solid rgba(239, 68, 68, 0.2)',
                            display: 'flex',
                            alignItems: 'center',
                            gap: '8px',
                            flexShrink: 0,
                            animation: 'syncSlideIn 0.2s ease'
                        }}>
                            <AlertTriangle size={14} style={{ color: 'var(--color-status-red, #ef4444)', flexShrink: 0 }} />
                            <span style={{ fontSize: '12px', color: 'var(--color-status-red, #ef4444)', fontWeight: 500 }}>
                                {importError}
                            </span>
                        </div>
                    )}

                    {/* ── Table body ── */}
                    <div style={bodyStyle}>
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
                            <thead style={{
                                backgroundColor: 'var(--color-bg-page)',
                                position: 'sticky',
                                top: 0,
                                zIndex: 2
                            }}>
                                <tr>
                                    <th style={thStyle}>#</th>
                                    <th style={thStyle}>Código Primavera</th>
                                    <th style={{ ...thStyle, minWidth: '200px' }}>Nome do Fornecedor *</th>
                                    <th style={{ ...thStyle, minWidth: '180px' }}>NIF / Contribuinte</th>
                                    <th style={{ ...thStyle, minWidth: '140px' }}>Notas</th>
                                    <th style={{ ...thStyle, width: '32px' }}></th>
                                </tr>
                            </thead>
                            <tbody>
                                {rows.map((row, idx) => {
                                    const warnings = rowWarnings[idx];
                                    const hasWarning = warnings.length > 0;
                                    const nameEmpty = !row.editedName.trim();
                                    const nifModified = nifModifiedFlags[idx];

                                    return (
                                        <React.Fragment key={row.primaveraCode}>
                                            <tr style={{
                                                borderBottom: hasWarning ? 'none' : '1px solid var(--color-border)',
                                                backgroundColor: hasWarning ? 'rgba(245, 158, 11, 0.03)' : 'transparent',
                                                transition: 'background-color 0.15s'
                                            }}>
                                                <td style={{ ...tdStyle, color: 'var(--color-text-muted)', fontSize: '11px', fontWeight: 600 }}>
                                                    {idx + 1}
                                                </td>
                                                <td style={tdStyle}>
                                                    <span style={{
                                                        fontFamily: "'JetBrains Mono', 'Fira Code', monospace",
                                                        fontSize: '12px',
                                                        fontWeight: 500,
                                                        color: 'var(--color-text-main)'
                                                    }}>
                                                        {row.primaveraCode}
                                                    </span>
                                                </td>
                                                <td style={tdStyle}>
                                                    <input
                                                        type="text"
                                                        value={row.editedName}
                                                        onChange={(e) => updateRow(idx, 'editedName', e.target.value)}
                                                        placeholder="Nome obrigatório"
                                                        style={nameEmpty ? errorInputStyle : inputStyle}
                                                        disabled={importing}
                                                    />
                                                    {nameEmpty && (
                                                        <span style={{ fontSize: '10px', color: 'var(--color-status-red, #ef4444)', fontWeight: 600, marginTop: '2px', display: 'block' }}>
                                                            Nome é obrigatório
                                                        </span>
                                                    )}
                                                </td>
                                                <td style={tdStyle}>
                                                    <div style={{ position: 'relative' }}>
                                                        <input
                                                            type="text"
                                                            value={row.editedTaxId}
                                                            onChange={(e) => updateRow(idx, 'editedTaxId', e.target.value)}
                                                            placeholder="NIF (opcional)"
                                                            style={hasWarning ? warningInputStyle : inputStyle}
                                                            disabled={importing}
                                                        />
                                                        {nifModified && row.originalTaxId && (
                                                            <span style={{
                                                                fontSize: '10px',
                                                                color: 'var(--color-text-muted)',
                                                                marginTop: '2px',
                                                                display: 'block',
                                                                fontStyle: 'italic'
                                                            }}>
                                                                Original: {row.originalTaxId}
                                                            </span>
                                                        )}
                                                    </div>
                                                </td>
                                                <td style={tdStyle}>
                                                    <input
                                                        type="text"
                                                        value={row.editedNotes}
                                                        onChange={(e) => updateRow(idx, 'editedNotes', e.target.value)}
                                                        placeholder="—"
                                                        style={inputStyle}
                                                        disabled={importing}
                                                    />
                                                </td>
                                                <td style={{ ...tdStyle, textAlign: 'center' }}>
                                                    {hasWarning ? (
                                                        <AlertTriangle size={14} style={{ color: '#f59e0b' }} />
                                                    ) : (
                                                        <CheckCircle2 size={14} style={{ color: '#22c55e' }} />
                                                    )}
                                                </td>
                                            </tr>
                                            {/* Warning detail row */}
                                            {hasWarning && (
                                                <tr style={{
                                                    borderBottom: '1px solid var(--color-border)',
                                                    backgroundColor: 'rgba(245, 158, 11, 0.03)'
                                                }}>
                                                    <td></td>
                                                    <td colSpan={5} style={{ padding: '0 14px 8px 14px' }}>
                                                        <div style={{
                                                            display: 'flex',
                                                            flexWrap: 'wrap',
                                                            gap: '6px'
                                                        }}>
                                                            {warnings.map((w, wi) => (
                                                                <span key={wi} style={{
                                                                    fontSize: '10px',
                                                                    fontWeight: 600,
                                                                    color: '#92400E',
                                                                    backgroundColor: '#FEF3C7',
                                                                    padding: '2px 8px',
                                                                    borderRadius: '10px',
                                                                    display: 'inline-flex',
                                                                    alignItems: 'center',
                                                                    gap: '4px'
                                                                }}>
                                                                    <AlertTriangle size={10} />
                                                                    {w.message}
                                                                </span>
                                                            ))}
                                                        </div>
                                                    </td>
                                                </tr>
                                            )}
                                        </React.Fragment>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>

                    {/* ── Warning gate overlay ── */}
                    {showWarningGate && (
                        <div style={{
                            padding: '16px 24px',
                            backgroundColor: '#FFFBEB',
                            borderTop: '1px solid #F59E0B',
                            display: 'flex',
                            alignItems: 'flex-start',
                            gap: '12px',
                            flexShrink: 0,
                            animation: 'syncSlideIn 0.2s ease'
                        }}>
                            <ShieldAlert size={20} style={{ color: '#D97706', flexShrink: 0, marginTop: '2px' }} />
                            <div style={{ flex: 1 }}>
                                <p style={{ fontSize: '13px', fontWeight: 700, color: '#92400E', margin: '0 0 4px 0' }}>
                                    {warningCount} fornecedor{warningCount !== 1 ? 'es' : ''} com avisos no NIF
                                </p>
                                <p style={{ fontSize: '12px', color: '#92400E', margin: '0 0 10px 0', lineHeight: 1.5 }}>
                                    Confirme que revisou os NIFs com avisos. Os fornecedores serão importados como rascunho e poderão ser corrigidos posteriormente.
                                </p>
                                <div style={{ display: 'flex', gap: '8px' }}>
                                    <button
                                        onClick={() => setShowWarningGate(false)}
                                        disabled={importing}
                                        style={{
                                            padding: '6px 14px',
                                            fontSize: '12px',
                                            fontWeight: 600,
                                            backgroundColor: 'transparent',
                                            border: '1px solid #D97706',
                                            borderRadius: 'var(--radius-sm)',
                                            color: '#92400E',
                                            cursor: 'pointer',
                                            fontFamily: 'inherit'
                                        }}
                                    >
                                        Voltar e corrigir
                                    </button>
                                    <button
                                        onClick={handleConfirmImport}
                                        disabled={importing}
                                        style={{
                                            padding: '6px 14px',
                                            fontSize: '12px',
                                            fontWeight: 600,
                                            backgroundColor: '#D97706',
                                            border: 'none',
                                            borderRadius: 'var(--radius-sm)',
                                            color: '#fff',
                                            cursor: importing ? 'not-allowed' : 'pointer',
                                            fontFamily: 'inherit',
                                            display: 'flex',
                                            alignItems: 'center',
                                            gap: '6px',
                                            opacity: importing ? 0.7 : 1
                                        }}
                                    >
                                        {importing && <Loader2 size={13} className="sync-spin" />}
                                        Confirmar mesmo assim
                                    </button>
                                </div>
                            </div>
                        </div>
                    )}

                    {/* ── Footer ── */}
                    <div style={footerStyle}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '16px', fontSize: '12px' }}>
                            <span style={{ color: 'var(--color-text-muted)' }}>
                                <strong style={{ color: '#22c55e' }}>{cleanCount}</strong> válido{cleanCount !== 1 ? 's' : ''}
                            </span>
                            {warningCount > 0 && (
                                <span style={{ color: 'var(--color-text-muted)' }}>
                                    <strong style={{ color: '#f59e0b' }}>{warningCount}</strong> com aviso{warningCount !== 1 ? 's' : ''}
                                </span>
                            )}
                        </div>
                        <div style={{ display: 'flex', gap: '10px' }}>
                            <button
                                onClick={onClose}
                                disabled={importing}
                                className="sync-btn sync-btn-secondary"
                                style={{ fontSize: '13px' }}
                            >
                                Cancelar
                            </button>
                            <button
                                onClick={handleConfirmImport}
                                disabled={importing || hasEmptyNames}
                                className="sync-btn sync-btn-primary"
                                style={{
                                    fontSize: '13px',
                                    display: 'flex',
                                    alignItems: 'center',
                                    gap: '6px',
                                    opacity: (importing || hasEmptyNames) ? 0.5 : 1
                                }}
                            >
                                {importing ? (
                                    <Loader2 size={14} className="sync-spin" />
                                ) : (
                                    <Download size={14} />
                                )}
                                {importing ? 'Importando...' : 'Confirmar Importação'}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        </DropdownPortal>
    );
}

// ─── Shared Cell Styles ──────────────────────────────────────────────────────

const thStyle: React.CSSProperties = {
    padding: '10px 14px',
    textAlign: 'left',
    fontWeight: 600,
    fontSize: '11px',
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    color: 'var(--color-text-muted)',
    borderBottom: '1px solid var(--color-border)',
    whiteSpace: 'nowrap'
};

const tdStyle: React.CSSProperties = {
    padding: '8px 14px',
    verticalAlign: 'top'
};
