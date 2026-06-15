import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { QRCodeSVG } from 'qrcode.react';
import { Printer, ArrowLeft, Loader2 } from 'lucide-react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import type { ITEquipmentDetail } from '../../types/itEquipment';
import { EQUIPMENT_TYPE_CONFIG } from '../../types/itEquipment';

/**
 * Printable asset label page for IT Equipment.
 * Route: /it/equipment/:id/label
 *
 * Renders a clean label (70mm × 35mm) with QR code, asset code,
 * type, serial, plant, and company. Uses @media print to isolate the label area.
 */
export default function ITEquipmentLabelPage() {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const [detail, setDetail] = useState<ITEquipmentDetail | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!id) { setError('ID do equipamento não fornecido.'); setLoading(false); return; }
        itEquipmentApi.get(id)
            .then(data => setDetail(data))
            .catch(() => setError('Equipamento não encontrado.'))
            .finally(() => setLoading(false));
    }, [id]);

    if (loading) {
        return (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '100vh' }}>
                <Loader2 size={28} style={{ animation: 'spin 1s linear infinite', color: '#3b82f6' }} />
                <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
            </div>
        );
    }

    if (error || !detail) {
        return (
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', minHeight: '100vh', gap: 16 }}>
                <p style={{ fontSize: '1rem', color: '#ef4444', fontWeight: 600 }}>{error || 'Equipamento não encontrado.'}</p>
                <button onClick={() => navigate('/it/equipment')} style={btnSecondary}>← Voltar ao Inventário</button>
            </div>
        );
    }

    const typeCfg = EQUIPMENT_TYPE_CONFIG[detail.equipmentType] || EQUIPMENT_TYPE_CONFIG['UNKNOWN'];
    const qrValue = detail.qrCodeUrl || `${window.location.origin}/it/equipment/${detail.id}`;

    return (
        <>
            {/* Print-only CSS */}
            <style>{`
                @media print {
                    body * { visibility: hidden !important; }
                    .asset-label-print-area, .asset-label-print-area * { visibility: visible !important; }
                    .asset-label-print-area {
                        position: absolute !important;
                        left: 0 !important; top: 0 !important;
                        width: 70mm !important; height: 35mm !important;
                        margin: 0 !important; padding: 2mm 3mm !important;
                        border: none !important; box-shadow: none !important;
                        background: #fff !important;
                    }
                    .no-print { display: none !important; }
                    @page { size: 70mm 35mm; margin: 0; }
                }
            `}</style>

            {/* Screen toolbar */}
            <div className="no-print" style={{
                padding: '16px 24px', borderBottom: '1px solid #e2e8f0',
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                background: '#f8fafc'
            }}>
                <button onClick={() => navigate(`/it/equipment/${detail.id}`)} style={btnSecondary}>
                    <ArrowLeft size={15} /> Voltar ao Equipamento
                </button>
                <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
                    <span style={{ fontSize: '0.85rem', color: '#64748b' }}>Pré-visualização da etiqueta (70mm × 35mm)</span>
                    <button onClick={() => window.print()} style={btnPrimary}>
                        <Printer size={15} /> Imprimir Etiqueta
                    </button>
                </div>
            </div>

            {/* Screen preview container */}
            <div className="no-print" style={{
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                minHeight: 'calc(100vh - 60px)', background: '#f1f5f9', padding: 40
            }}>
                <div style={{
                    background: '#fff', boxShadow: '0 4px 20px rgba(0,0,0,0.12)',
                    borderRadius: 8, padding: 20
                }}>
                    <LabelContent detail={detail} typeCfg={typeCfg} qrValue={qrValue} />
                </div>
            </div>

            {/* Actual print area (invisible on screen, sole visible element on print) */}
            <div className="asset-label-print-area" style={{ position: 'fixed', left: -9999, top: -9999 }}>
                <LabelContent detail={detail} typeCfg={typeCfg} qrValue={qrValue} />
            </div>
        </>
    );
}

// ─── Label Layout (70mm × 35mm) ───
function LabelContent({ detail, typeCfg, qrValue }: {
    detail: ITEquipmentDetail;
    typeCfg: { label: string };
    qrValue: string;
}) {
    return (
        <div style={{
            width: '70mm', height: '35mm',
            display: 'flex', alignItems: 'center', gap: '3mm',
            fontFamily: "'Inter', 'Segoe UI', Arial, sans-serif",
            color: '#000', background: '#fff', overflow: 'hidden',
            padding: '2mm 3mm', boxSizing: 'border-box'
        }}>
            {/* QR Code (left) */}
            <div style={{ flexShrink: 0, display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                <QRCodeSVG
                    value={qrValue}
                    size={90}
                    level="M"
                    includeMargin={false}
                />
            </div>

            {/* Info (right) */}
            <div style={{
                flex: 1, display: 'flex', flexDirection: 'column',
                justifyContent: 'center', minWidth: 0, gap: '0.6mm'
            }}>
                <div style={{
                    fontSize: '7pt', fontWeight: 800, textTransform: 'uppercase',
                    letterSpacing: '0.08em', color: '#333', lineHeight: 1.1
                }}>
                    ALPLA ANGOLA
                </div>

                <div style={{
                    fontSize: '6pt', fontWeight: 700, textTransform: 'uppercase',
                    color: '#666', letterSpacing: '0.04em', marginTop: '0.5mm'
                }}>
                    CÓDIGO DO ATIVO
                </div>

                <div style={{
                    fontSize: '9pt', fontWeight: 900, fontFamily: "'Courier New', monospace",
                    letterSpacing: '0.3px', lineHeight: 1.2, color: '#000',
                    wordBreak: 'break-all'
                }}>
                    {detail.assetTag}
                </div>

                <div style={{
                    marginTop: '1mm', display: 'flex', flexDirection: 'column', gap: '0.3mm',
                    fontSize: '6pt', color: '#444', lineHeight: 1.3
                }}>
                    <span><b>Tipo:</b> {typeCfg.label}</span>
                    {detail.serialNumber && <span><b>S/N:</b> {detail.serialNumber}</span>}
                    {detail.model && <span><b>Modelo:</b> {detail.model}</span>}
                    <span><b>Planta:</b> {detail.plantCode || detail.plant}</span>
                    <span><b>Empresa:</b> {detail.companyCode || '—'}</span>
                    {detail.legacyAssetCode && <span><b>Legado:</b> {detail.legacyAssetCode}</span>}
                </div>
            </div>
        </div>
    );
}

// ─── Button styles ───
const btnPrimary: React.CSSProperties = {
    display: 'flex', alignItems: 'center', gap: 6, padding: '10px 20px',
    background: 'linear-gradient(135deg, #6366f1, #4f46e5)', border: 'none',
    borderRadius: 8, cursor: 'pointer', color: '#fff', fontSize: '0.85rem',
    fontWeight: 600, boxShadow: '0 2px 8px rgba(99,102,241,0.3)'
};

const btnSecondary: React.CSSProperties = {
    display: 'flex', alignItems: 'center', gap: 6, padding: '8px 16px',
    background: '#fff', border: '1px solid #e2e8f0',
    borderRadius: 8, cursor: 'pointer', color: '#334155', fontSize: '0.85rem', fontWeight: 600
};
