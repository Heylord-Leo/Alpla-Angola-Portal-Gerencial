import React, { useState } from 'react';
import { itEquipmentApi } from '../../lib/itEquipmentApi';
import { ModalWrapper, SubmitBtn, ErrorBox, Field, TextArea, SelectField, cancelBtnStyle } from './EquipmentFormModal';

interface Props { equipmentId: string; statusCode: string; onClose: () => void; onSuccess: () => void; }

export function RepairEquipmentModal({ equipmentId, statusCode, onClose, onSuccess }: Props) {
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [form, setForm] = useState({ reason: '', repairVendor: '', notes: '', result: 'REPAIRED' });
    const set = (f: string, v: string) => setForm(p => ({ ...p, [f]: v }));

    const isInRepair = statusCode === 'IN_REPAIR';

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            setSaving(true); setError('');
            if (isInRepair) {
                await itEquipmentApi.returnFromRepair(equipmentId, { result: form.result, notes: form.notes });
            } else {
                await itEquipmentApi.sendToRepair(equipmentId, form);
            }
            onSuccess();
        } catch (err: any) { setError(err.message); } finally { setSaving(false); }
    };

    return (
        <ModalWrapper title={isInRepair ? 'Retorno de Conserto' : 'Enviar para Conserto'} onClose={onClose}>
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {error && <ErrorBox msg={error} />}
                {isInRepair ? (
                    <>
                        <SelectField label="Resultado" value={form.result} onChange={v => set('result', v)}
                            options={[
                                { value: 'REPAIRED', label: 'Reparado (volta para Disponível)' },
                                { value: 'NOT_REPAIRABLE', label: 'Irreparável (baixa)' },
                            ]}
                        />
                        <TextArea label="Observações" value={form.notes} onChange={v => set('notes', v)} />
                    </>
                ) : (
                    <>
                        <Field label="Motivo" value={form.reason} onChange={v => set('reason', v)} />
                        <Field label="Fornecedor / Oficina" value={form.repairVendor} onChange={v => set('repairVendor', v)} />
                        <TextArea label="Observações" value={form.notes} onChange={v => set('notes', v)} />
                    </>
                )}
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 16, paddingTop: 16, borderTop: '1px solid var(--color-border)' }}>
                    <button type="button" onClick={onClose} style={cancelBtnStyle}>Cancelar</button>
                    <SubmitBtn label={isInRepair ? 'Confirmar Retorno' : 'Enviar para Conserto'} loading={saving} />
                </div>
            </form>
        </ModalWrapper>
    );
}
