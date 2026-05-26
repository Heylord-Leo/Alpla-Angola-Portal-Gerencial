import React, { useState } from 'react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { ModalWrapper, SubmitBtn, ErrorBox, SelectField, TextArea, cancelBtnStyle } from './EquipmentFormModal';
import { Info, AlertTriangle } from 'lucide-react';

interface Props { equipmentId: string; onClose: () => void; onSuccess: () => void; }

export function ReturnEquipmentModal({ equipmentId, onClose, onSuccess }: Props) {
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [warnings, setWarnings] = useState<string[]>([]);
    const [condition, setCondition] = useState('GOOD');
    const [notes, setNotes] = useState('');

    const needsObservation = condition !== 'GOOD';

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (needsObservation && !notes.trim()) {
            setError('Informe uma observação quando o equipamento não estiver em bom estado.');
            return;
        }
        try {
            setSaving(true); setError(''); setWarnings([]);
            const result = await itEquipmentApi.return(equipmentId, { condition, notes });
            if (result.warnings && result.warnings.length > 0) {
                setWarnings(result.warnings);
                // Show warnings briefly then close
                setTimeout(() => onSuccess(), 4000);
            } else {
                onSuccess();
            }
        } catch (err: any) { setError(err.message); } finally { setSaving(false); }
    };

    return (
        <ModalWrapper title="Devolver Equipamento" onClose={onClose}>
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {error && <ErrorBox msg={error} />}

                {/* Warning banner for email failures */}
                {warnings.length > 0 && (
                    <div style={{
                        padding: '10px 14px', backgroundColor: '#fffbeb', border: '1px solid #fcd34d',
                        borderRadius: 8, color: '#92400e', fontSize: '0.82rem',
                        display: 'flex', gap: 8, alignItems: 'flex-start'
                    }}>
                        <AlertTriangle size={16} style={{ marginTop: 2, flexShrink: 0, color: '#f59e0b' }} />
                        <div>
                            <div style={{ fontWeight: 600, marginBottom: 4 }}>
                                O equipamento foi devolvido e o Termo de Devolução foi gerado, mas ocorreu uma falha ao enviar o e-mail.
                            </div>
                            {warnings.map((w, i) => <div key={i}>• {w}</div>)}
                        </div>
                    </div>
                )}

                {/* Info notice */}
                <div style={{
                    padding: '10px 14px', backgroundColor: 'rgba(59,130,246,0.08)',
                    border: '1px solid rgba(59,130,246,0.2)', borderRadius: 8,
                    color: 'var(--color-text-muted)', fontSize: '0.8rem',
                    display: 'flex', gap: 8, alignItems: 'flex-start'
                }}>
                    <Info size={16} style={{ marginTop: 2, flexShrink: 0, color: '#3b82f6' }} />
                    <span>
                        Ao devolver este equipamento, o sistema irá gerar e enviar automaticamente
                        o <strong>Termo de Devolução em PDF</strong> para o utilizador e para quem está recebendo a devolução.
                    </span>
                </div>

                <SelectField label="Condição" value={condition} onChange={setCondition}
                    options={[
                        { value: 'GOOD', label: 'Em bom estado' },
                        { value: 'DAMAGED', label: 'Danificado' },
                        { value: 'NEEDS_REPAIR', label: 'Necessita conserto' },
                    ]} />

                <div>
                    <TextArea label="Observações" value={notes} onChange={setNotes} rows={3} />
                    {needsObservation && !notes.trim() && (
                        <div style={{ color: '#dc2626', fontSize: '0.78rem', marginTop: 4 }}>
                            Informe uma observação quando o equipamento não estiver em bom estado.
                        </div>
                    )}
                    <div style={{ color: 'var(--color-text-muted)', fontSize: '0.75rem', marginTop: 4 }}>
                        O Termo de Devolução será gerado com base nas informações preenchidas.
                    </div>
                </div>

                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 8 }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                    <SubmitBtn label="Devolver" loading={saving} />
                </div>
            </form>
        </ModalWrapper>
    );
}
