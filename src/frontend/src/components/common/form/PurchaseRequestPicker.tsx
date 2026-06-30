import { useState, useEffect, useRef } from 'react';
import { Search, Loader2, X, FileText } from 'lucide-react';
import { api } from '../../../lib/api';

interface PurchaseRequestPickerProps {
    value?: string; // purchaseOrderNumber text (fallback/display)
    requestId?: string; // purchaseRequestId
    onChange: (requestId: string | undefined, requestNumber: string | undefined, textValue: string | undefined) => void;
    error?: string;
    disabled?: boolean;
}

const VALID_STATUSES = [
    'APPROVED',
    'WAITING_QUOTATION',
    'PO_ISSUED',
    'PAYMENT_REQUEST_SENT',
    'PAYMENT_SCHEDULED',
    'PAID',
    'COMPLETED',
    'WAITING_COST_CENTER',
    'WAITING_PO_CORRECTION',
    'ADVANCE_PAYMENT_REQUIRED',
    'ADVANCE_PAYMENT_COMPLETED',
    'WAITING_SUPPLIER_DELIVERY',
    'WAITING_RECONCILIATION'
].join(',');

export function PurchaseRequestPicker({ value, requestId, onChange, error, disabled }: PurchaseRequestPickerProps) {
    const [isOpen, setIsOpen] = useState(false);
    const [searchTerm, setSearchTerm] = useState('');
    const [results, setResults] = useState<any[]>([]);
    const [isLoading, setIsLoading] = useState(false);
    const containerRef = useRef<HTMLDivElement>(null);
    const inputRef = useRef<HTMLInputElement>(null);

    // Close when clicking outside
    useEffect(() => {
        function handleClickOutside(event: MouseEvent) {
            if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        }
        document.addEventListener('mousedown', handleClickOutside);
        return () => document.removeEventListener('mousedown', handleClickOutside);
    }, []);

    // Fetch results on search
    useEffect(() => {
        if (!isOpen) return;

        const fetchResults = async () => {
            setIsLoading(true);
            try {
                // Fetch max 5 results
                const response = await api.requests.list(searchTerm, { statusIds: VALID_STATUSES }, 1, 5);
                setResults(response.pagedResult.items || []);
            } catch (error) {
                console.error('Failed to search purchase requests', error);
                setResults([]);
            } finally {
                setIsLoading(false);
            }
        };

        const timeoutId = setTimeout(fetchResults, 300);
        return () => clearTimeout(timeoutId);
    }, [searchTerm, isOpen]);

    // Derived display value when closed
    let displayValue = '';
    if (value) {
        displayValue = value;
    } else if (requestId && results.find(r => r.id === requestId)) {
        const req = results.find(r => r.id === requestId);
        displayValue = `${req.requestNumber} — ${req.supplierName || 'Sem fornecedor'}`;
    }

    return (
        <div ref={containerRef} style={{ position: 'relative', width: '100%' }}>
            {/* Input Trigger */}
            <div
                onClick={() => {
                    if (!disabled) {
                        setIsOpen(true);
                        setTimeout(() => inputRef.current?.focus(), 10);
                    }
                }}
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    width: '100%',
                    padding: '8px 12px',
                    backgroundColor: disabled ? 'var(--color-bg-subtle)' : 'var(--color-bg-surface)',
                    border: `1px solid ${error ? 'var(--color-error)' : isOpen ? 'var(--color-primary)' : 'var(--color-border)'}`,
                    borderRadius: '8px',
                    cursor: disabled ? 'not-allowed' : 'pointer',
                    minHeight: '40px',
                    transition: 'all 0.2s',
                    boxShadow: isOpen ? '0 0 0 2px rgba(59, 130, 246, 0.1)' : 'none',
                }}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flex: 1, overflow: 'hidden' }}>
                    <Search size={16} color="var(--color-text-muted)" />
                    {isOpen ? (
                        <input
                            ref={inputRef}
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            placeholder="Buscar REQ, PO, Fornecedor..."
                            style={{
                                border: 'none',
                                outline: 'none',
                                width: '100%',
                                backgroundColor: 'transparent',
                                fontSize: '0.9rem',
                                color: 'var(--color-text)',
                            }}
                        />
                    ) : (
                        <span style={{ 
                            color: displayValue ? 'var(--color-text)' : 'var(--color-text-muted)',
                            fontSize: '0.9rem',
                            whiteSpace: 'nowrap',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis'
                        }}>
                            {displayValue || 'Buscar requisição / ordem de compra...'}
                        </span>
                    )}
                </div>

                {/* Clear button if value exists */}
                {(value || requestId) && !disabled && !isOpen && (
                    <button
                        type="button"
                        onClick={(e) => {
                            e.stopPropagation();
                            onChange(undefined, undefined, undefined);
                        }}
                        style={{
                            background: 'none',
                            border: 'none',
                            cursor: 'pointer',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            color: 'var(--color-text-muted)',
                            padding: '4px',
                            borderRadius: '50%',
                        }}
                    >
                        <X size={14} />
                    </button>
                )}
            </div>

            {/* Error Message */}
            {error && (
                <div style={{ color: 'var(--color-error)', fontSize: '0.8rem', marginTop: '4px' }}>
                    {error}
                </div>
            )}

            {/* Dropdown Menu */}
            {isOpen && (
                <div style={{
                    position: 'absolute',
                    top: 'calc(100% + 4px)',
                    left: 0,
                    right: 0,
                    backgroundColor: 'var(--color-bg-surface)',
                    border: '1px solid var(--color-border)',
                    borderRadius: '8px',
                    boxShadow: '0 4px 20px rgba(0, 0, 0, 0.1)',
                    zIndex: 50,
                    maxHeight: '300px',
                    overflowY: 'auto',
                }}>
                    {isLoading ? (
                        <div style={{ padding: '16px', display: 'flex', justifyContent: 'center' }}>
                            <Loader2 size={20} className="animate-spin" color="var(--color-text-muted)" />
                        </div>
                    ) : results.length > 0 ? (
                        <div style={{ padding: '8px 0' }}>
                            <div style={{ padding: '0 12px 8px 12px', fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600, textTransform: 'uppercase' }}>
                                Principais resultados ({results.length})
                            </div>
                            {results.map((req) => (
                                <button
                                    key={req.id}
                                    type="button"
                                    onClick={() => {
                                        const label = `${req.requestNumber} — ${req.supplierName || 'Sem fornecedor'} — ${req.department?.name || 'Sem depto'}`;
                                        onChange(req.id, req.requestNumber, label);
                                        setIsOpen(false);
                                        setSearchTerm('');
                                    }}
                                    style={{
                                        display: 'flex',
                                        flexDirection: 'column',
                                        width: '100%',
                                        padding: '8px 12px',
                                        backgroundColor: 'transparent',
                                        border: 'none',
                                        cursor: 'pointer',
                                        textAlign: 'left',
                                        borderBottom: '1px solid var(--color-bg-subtle)'
                                    }}
                                    onMouseEnter={(e) => e.currentTarget.style.backgroundColor = 'var(--color-bg-subtle)'}
                                    onMouseLeave={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                                >
                                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' }}>
                                        <span style={{ fontWeight: 600, fontSize: '0.85rem', color: 'var(--color-text)' }}>
                                            {req.requestNumber}
                                        </span>
                                        <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)' }}>
                                            {req.status?.name}
                                        </span>
                                    </div>
                                    <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '2px', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                        {req.supplierName || 'Sem fornecedor'} • {req.department?.name || 'Sem departamento'}
                                    </div>
                                    <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginTop: '2px', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                        {req.title}
                                    </div>
                                </button>
                            ))}
                        </div>
                    ) : (
                        <div style={{ padding: '16px', textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.85rem' }}>
                            Nenhuma requisição encontrada.
                        </div>
                    )}
                </div>
            )}

            {/* Legacy Text Indication */}
            {value && !requestId && (
                <div style={{ display: 'flex', alignItems: 'center', gap: '4px', marginTop: '4px', color: 'var(--color-text-muted)', fontSize: '0.75rem' }}>
                    <FileText size={12} />
                    Valor digitado manualmente
                </div>
            )}
        </div>
    );
}
