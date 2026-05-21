import React, { useState, useRef } from 'react';
import { Upload, CheckCircle, AlertCircle, FileText, Loader2 } from 'lucide-react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { ModalWrapper, cancelBtnStyle } from './EquipmentFormModal';
import type { ITEquipmentImportResult } from '../../types/itEquipment';

interface Props { onClose: () => void; onSuccess: () => void; }

export function ImportEquipmentModal({ onClose, onSuccess }: Props) {
    const fileRef = useRef<HTMLInputElement>(null);
    const [file, setFile] = useState<File | null>(null);
    const [importing, setImporting] = useState(false);
    const [result, setResult] = useState<ITEquipmentImportResult | null>(null);
    const [error, setError] = useState('');

    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const f = e.target.files?.[0];
        if (f) {
            if (!f.name.endsWith('.csv')) { setError('Apenas ficheiros .csv são aceites.'); return; }
            setFile(f);
            setError('');
        }
    };

    const handleImport = async () => {
        if (!file) return;
        try {
            setImporting(true);
            setError('');
            const res = await itEquipmentApi.importCsv(file);
            setResult(res);
        } catch (err: any) {
            setError(err.message || 'Erro na importação.');
        } finally {
            setImporting(false);
        }
    };

    return (
        <ModalWrapper title="Importar Equipamentos (CSV)" onClose={onClose} wide>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                {!result ? (
                    <>
                        <div style={{
                            padding: '10px 14px', backgroundColor: 'rgba(59,130,246,0.06)',
                            borderRadius: 8, fontSize: '0.82rem', color: 'var(--color-text)',
                            border: '1px solid rgba(59,130,246,0.15)'
                        }}>
                            <strong>Formato esperado:</strong> CSV com colunas como "Asset Tag", "Hostname", "Status", "Type", "Manufacturer", "Model", "Serial Number", "MAC Address", "Current User", "Plant", "Processor", "Memory (RAM)", "Color", "Biometric / MFA", "ID Card", "Notes".
                            <br /><span style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                                A coluna "Asset Tag" é obrigatória. Registos com asset tags existentes serão ignorados. Hostnames duplicados são reportados.
                            </span>
                        </div>

                        {/* Dropzone */}
                        <div
                            onClick={() => fileRef.current?.click()}
                            style={{
                                border: '2px dashed var(--color-border)', borderRadius: 12,
                                padding: 40, textAlign: 'center', cursor: 'pointer',
                                backgroundColor: file ? 'rgba(16,185,129,0.04)' : 'transparent',
                                transition: 'all 0.2s'
                            }}
                        >
                            <input ref={fileRef} type="file" accept=".csv" onChange={handleFileChange} style={{ display: 'none' }} />
                            {file ? (
                                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 8 }}>
                                    <FileText size={20} style={{ color: '#10b981' }} />
                                    <span style={{ fontWeight: 600, color: 'var(--color-text)' }}>{file.name}</span>
                                    <span style={{ color: 'var(--color-text-muted)', fontSize: '0.82rem' }}>
                                        ({(file.size / 1024).toFixed(1)} KB)
                                    </span>
                                </div>
                            ) : (
                                <div>
                                    <Upload size={28} style={{ color: 'var(--color-text-muted)', marginBottom: 8 }} />
                                    <p style={{ color: 'var(--color-text-muted)', fontSize: '0.9rem', margin: 0 }}>
                                        Clique ou arraste o ficheiro CSV aqui
                                    </p>
                                </div>
                            )}
                        </div>

                        {error && (
                            <div style={{
                                padding: '8px 12px', backgroundColor: '#fef2f2', border: '1px solid #fecaca',
                                borderRadius: 8, color: '#dc2626', fontSize: '0.82rem'
                            }}>
                                {error}
                            </div>
                        )}

                        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                            <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                            <button
                                onClick={handleImport}
                                disabled={!file || importing}
                                style={{
                                    display: 'flex', alignItems: 'center', gap: 6, padding: '8px 20px',
                                    background: file ? 'linear-gradient(135deg, #3b82f6, #2563eb)' : '#e5e7eb',
                                    border: 'none', borderRadius: 8, color: file ? '#fff' : '#9ca3af',
                                    fontSize: '0.85rem', fontWeight: 600, cursor: file ? 'pointer' : 'default',
                                    opacity: importing ? 0.7 : 1
                                }}
                            >
                                {importing && <Loader2 size={14} style={{ animation: 'spin 1s linear infinite' }} />}
                                {importing ? 'Importando...' : 'Importar'}
                            </button>
                        </div>
                    </>
                ) : (
                    <>
                        {/* Import Result */}
                        <div style={{
                            display: 'flex', alignItems: 'center', gap: 10, padding: '12px 16px',
                            backgroundColor: result.created > 0 ? '#ecfdf5' : '#fffbeb',
                            borderRadius: 10, border: `1px solid ${result.created > 0 ? '#bbf7d0' : '#fde68a'}`
                        }}>
                            {result.created > 0 ? <CheckCircle size={20} style={{ color: '#10b981' }} /> : <AlertCircle size={20} style={{ color: '#f59e0b' }} />}
                            <span style={{ fontWeight: 600, fontSize: '0.9rem', color: 'var(--color-text)' }}>
                                {result.message}
                            </span>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 12 }}>
                            <StatBox label="Criados" value={result.created} color="#10b981" />
                            <StatBox label="Ignorados" value={result.skipped} color="#f59e0b" />
                            <StatBox label="Total Linhas" value={result.totalLines} color="#6b7280" />
                        </div>

                        {result.duplicateHostnames && result.duplicateHostnames.length > 0 && (
                            <div style={{ maxHeight: 200, overflowY: 'auto', border: '1px solid #fde68a', borderRadius: 8, padding: 10 }}>
                                <p style={{ fontWeight: 600, fontSize: '0.82rem', color: '#f59e0b', marginBottom: 6 }}>
                                    Hostnames Duplicados ({result.duplicateHostnames.length}):
                                </p>
                                {result.duplicateHostnames.map((d, i) => (
                                    <div key={i} style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', padding: '2px 0' }}>
                                        Linha {d.line}: <strong>{d.hostname}</strong> (conflito com {d.conflictWith})
                                    </div>
                                ))}
                            </div>
                        )}

                        {result.errors && result.errors.length > 0 && (
                            <div style={{ maxHeight: 200, overflowY: 'auto', border: '1px solid #fecaca', borderRadius: 8, padding: 10 }}>
                                <p style={{ fontWeight: 600, fontSize: '0.82rem', color: '#ef4444', marginBottom: 6 }}>
                                    Erros ({result.errors.length}):
                                </p>
                                {result.errors.map((e, i) => (
                                    <div key={i} style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', padding: '2px 0' }}>
                                        Linha {e.line}: {e.error}
                                    </div>
                                ))}
                            </div>
                        )}

                        <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
                            <button
                                onClick={onSuccess}
                                style={{
                                    padding: '8px 20px', background: 'linear-gradient(135deg, #3b82f6, #2563eb)',
                                    border: 'none', borderRadius: 8, color: '#fff', fontSize: '0.85rem',
                                    fontWeight: 600, cursor: 'pointer'
                                }}
                            >
                                Concluir
                            </button>
                        </div>
                    </>
                )}
            </div>
            <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
        </ModalWrapper>
    );
}

function StatBox({ label, value, color }: { label: string; value: number; color: string }) {
    return (
        <div style={{
            padding: 12, borderRadius: 8, border: '1px solid var(--color-border)',
            textAlign: 'center', backgroundColor: 'var(--color-bg-surface)'
        }}>
            <div style={{ fontSize: '1.5rem', fontWeight: 700, color }}>{value}</div>
            <div style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 600 }}>{label}</div>
        </div>
    );
}
