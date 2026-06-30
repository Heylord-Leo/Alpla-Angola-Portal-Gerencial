const fs = require('fs');
const path = require('path');

const targetFile = 'C:\\dev\\alpla-portal\\src\\frontend\\src\\components\\it\\EquipmentFormModal.tsx';
let content = fs.readFileSync(targetFile, 'utf8');

// 1. Add imports
const imports = `import { SupplierAutocomplete } from '../SupplierAutocomplete';
import { FormInput } from '../common/form/FormInput';
import { FormSelect } from '../common/form/FormSelect';
import { FormTextarea } from '../common/form/FormTextarea';
import { FormCheckbox } from '../common/form/FormCheckbox';
import { FileUpload } from '../common/form/FileUpload';
import { SectionCard } from '../common/ui/SectionCard';`;

content = content.replace(
  `import { SupplierAutocomplete } from '../SupplierAutocomplete';`,
  imports
);

// 2. Replace Row
content = content.replace(/<Row>/g, '<div style={{ display: \'flex\', gap: 12 }}>');
content = content.replace(/<\/Row>/g, '</div>');

// 3. Replace Field
content = content.replace(/<Field /g, '<FormInput ');

// 4. Replace SelectField
content = content.replace(/<SelectField /g, '<FormSelect ');

// 5. Replace TextArea
content = content.replace(/<TextArea /g, '<FormTextarea ');

// 6. Replace Checkboxes
// We have two checkboxes. Let's find them manually:
// a) biometricMfaEnabled
content = content.replace(
    /<div style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 8, paddingTop: 18 }}>\s*<input type="checkbox" checked={form.biometricMfaEnabled} onChange={e => set\('biometricMfaEnabled', e.target.checked\)} id="biocheck" \/>\s*<label htmlFor="biocheck" style={{ fontSize: '0.85rem', color: 'var\(--color-text\)' }}>Biometria \/ MFA<\/label>\s*<\/div>/g,
    `<div style={{ flex: 1, paddingTop: 18 }}><FormCheckbox label="Biometria / MFA" checked={form.biometricMfaEnabled} onChange={e => set('biometricMfaEnabled', e.target.checked)} id="biocheck" /></div>`
);

// b) purchaseInfoUnavailable
content = content.replace(
    /<div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12 }}>\s*<input type="checkbox" checked={purchase.purchaseInfoUnavailable}\s*onChange={e => setPur\('purchaseInfoUnavailable', e.target.checked\)}\s*id="purchaseUnavailableCheck" \/>\s*<label htmlFor="purchaseUnavailableCheck" style={{ fontSize: '0.85rem', color: 'var\(--color-text\)' }}>\s*Informações de compra indisponíveis\s*<\/label>\s*<\/div>/g,
    `<FormCheckbox label="Informações de compra indisponíveis" checked={purchase.purchaseInfoUnavailable} onChange={e => setPur('purchaseInfoUnavailable', e.target.checked)} id="purchaseUnavailableCheck" style={{ marginBottom: 12 }} />`
);

// c) warrantyInfoUnavailable
content = content.replace(
    /<div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 12 }}>\s*<input type="checkbox" checked={warranty.warrantyInfoUnavailable}\s*onChange={e => setWar\('warrantyInfoUnavailable', e.target.checked\)}\s*id="warrantyUnavailableCheck" \/>\s*<label htmlFor="warrantyUnavailableCheck" style={{ fontSize: '0.85rem', color: 'var\(--color-text\)' }}>\s*Informações de garantia indisponíveis\s*<\/label>\s*<\/div>/g,
    `<FormCheckbox label="Informações de garantia indisponíveis" checked={warranty.warrantyInfoUnavailable} onChange={e => setWar('warrantyInfoUnavailable', e.target.checked)} id="warrantyUnavailableCheck" style={{ marginBottom: 12 }} />`
);

// 7. Replace FileUpload for purchaseDocFile
const oldUpload = `<div style={{ marginTop: 8 }}>
                                <label style={labelStyle}>Cópia da nota de compra / guia de entrega *</label>
                                {isEdit && equipment && equipment.documents.some(d => d.documentType === 'PURCHASE_DOCUMENT') && !purchaseDocFile && (
                                    <div style={{
                                        padding: '6px 10px', backgroundColor: '#ecfdf5', border: '1px solid #a7f3d0',
                                        borderRadius: 6, fontSize: '0.8rem', color: '#059669', marginBottom: 6
                                    }}>
                                        ✅ Documento já carregado — selecione um novo ficheiro para substituir.
                                    </div>
                                )}
                                <input
                                    type="file"
                                    accept=".pdf,.jpg,.jpeg,.png"
                                    onChange={e => {
                                        const file = e.target.files?.[0];
                                        if (file) {
                                            if (file.size > 10 * 1024 * 1024) {
                                                setPurchaseDocError('O ficheiro excede o limite de 10MB.');
                                                setPurchaseDocFile(null);
                                            } else {
                                                setPurchaseDocError('');
                                                setPurchaseDocFile(file);
                                            }
                                        }
                                    }}
                                    style={{ ...inputStyle, padding: '6px 8px' }}
                                />
                                {purchaseDocFile && (
                                    <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', marginTop: 4 }}>
                                        📎 {purchaseDocFile.name} ({(purchaseDocFile.size / 1024).toFixed(0)} KB)
                                    </div>
                                )}
                                {(purchaseDocError || fieldErrors.purchaseDocFile) && (
                                    <div style={{ fontSize: '0.78rem', color: '#dc2626', marginTop: 4 }}>{purchaseDocError || fieldErrors.purchaseDocFile}</div>
                                )}
                                <div style={{ fontSize: '0.72rem', color: 'var(--color-text-muted)', marginTop: 2 }}>
                                    PDF, JPG ou PNG — máximo 10 MB
                                </div>
                            </div>`;

const newUpload = `<div style={{ marginTop: 8 }}>
                                <FileUpload
                                    label="Cópia da nota de compra / guia de entrega *"
                                    file={purchaseDocFile}
                                    existingFileName={isEdit && equipment && equipment.documents.some(d => d.documentType === 'PURCHASE_DOCUMENT') ? "Documento existente" : undefined}
                                    existingFileUrl={isEdit && equipment && equipment.documents.find(d => d.documentType === 'PURCHASE_DOCUMENT')?.fileUrl}
                                    onChange={(file) => {
                                        setPurchaseDocError('');
                                        setPurchaseDocFile(file);
                                    }}
                                    onRemoveExisting={() => {
                                        // The user is replacing or removing it.
                                    }}
                                    accept=".pdf,.jpg,.jpeg,.png"
                                    maxSizeMB={10}
                                    error={purchaseDocError || fieldErrors.purchaseDocFile}
                                    helperText="PDF, JPG ou PNG — máximo 10 MB"
                                />
                                {isEdit && equipment && equipment.documents.some(d => d.documentType === 'PURCHASE_DOCUMENT') && !purchaseDocFile && (
                                    <div style={{
                                        padding: '6px 10px', backgroundColor: '#ecfdf5', border: '1px solid #a7f3d0',
                                        borderRadius: 6, fontSize: '0.8rem', color: '#059669', marginTop: 8
                                    }}>
                                        ✅ Documento já carregado — carregue um novo arquivo acima para substituir.
                                    </div>
                                )}
                            </div>`;

content = content.replace(oldUpload, newUpload);

// 8. Replace native inputs with FormInput for dates
content = content.replace(
    /<div style={{ flex: 1 }}>\s*<label style={labelStyle}>Data de Fabricação<\/label>\s*<input type="date" value={form.manufactureDate} onChange={e => set\('manufactureDate', e.target.value\)} style={inputStyle} \/>\s*<\/div>/g,
    `<FormInput label="Data de Fabricação" type="date" value={form.manufactureDate} onChange={v => set('manufactureDate', v)} style={{ flex: 1 }} />`
);

content = content.replace(
    /<div style={{ flex: 1 }}>\s*<label style={labelStyle}>Data de compra \*<\/label>\s*<input type="date" value={purchase.acquisitionDate} onChange={e => setPur\('acquisitionDate', e.target.value\)} style={{ ...inputStyle, borderColor: fieldErrors.acquisitionDate \? '#ef4444' : 'var\(--color-border\)' }} \/>\s*{fieldErrors.acquisitionDate && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{fieldErrors.acquisitionDate}<\/div>}\s*<\/div>/g,
    `<FormInput label="Data de compra *" type="date" value={purchase.acquisitionDate} onChange={v => setPur('acquisitionDate', v)} error={fieldErrors.acquisitionDate} style={{ flex: 1 }} />`
);

content = content.replace(
    /<div style={{ flex: 1 }}>\s*<label style={labelStyle}>Início da garantia<\/label>\s*<input type="date" value={warranty.warrantyStartDate}\s*onChange={e => setWar\('warrantyStartDate', e.target.value\)} style={inputStyle} \/>\s*<\/div>/g,
    `<FormInput label="Início da garantia" type="date" value={warranty.warrantyStartDate} onChange={v => setWar('warrantyStartDate', v)} style={{ flex: 1 }} />`
);

content = content.replace(
    /<div style={{ flex: 1 }}>\s*<label style={labelStyle}>Fim da garantia<\/label>\s*<input type="date" value={warranty.warrantyEndDate}\s*onChange={e => setWar\('warrantyEndDate', e.target.value\)} style={{ ...inputStyle, borderColor: fieldErrors.warrantyEndDate \? '#ef4444' : 'var\(--color-border\)' }} \/>\s*{fieldErrors.warrantyEndDate && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{fieldErrors.warrantyEndDate}<\/div>}\s*{warranty.warrantyMonths && warranty.warrantyEndDate && \(\s*<div style={{ fontSize: '0.72rem', color: 'var\(--color-text-muted\)', marginTop: 2 }}>\s*Calculado automaticamente a partir de {warranty.warrantyMonths} meses. Editável.\s*<\/div>\s*\)}\s*<\/div>/g,
    `<FormInput label="Fim da garantia" type="date" value={warranty.warrantyEndDate} onChange={v => setWar('warrantyEndDate', v)} error={fieldErrors.warrantyEndDate} helperText={warranty.warrantyMonths && warranty.warrantyEndDate ? \`Calculado automaticamente a partir de \${warranty.warrantyMonths} meses. Editável.\` : undefined} style={{ flex: 1 }} />`
);

// 9. Replace textareas not using TextArea component (like reasons for unavailability)
content = content.replace(
    /<div>\s*<label style={labelStyle}>Motivo da indisponibilidade \*<\/label>\s*<textarea\s*value={purchase.purchaseInfoUnavailableReason}\s*onChange={e => setPur\('purchaseInfoUnavailableReason', e.target.value\)}\s*rows={2}\s*placeholder="Ex: Equipamento adquirido antes da implementação do sistema de rastreabilidade."\s*style={{ \.\.\.inputStyle, resize: 'vertical', borderColor: fieldErrors.purchaseInfoUnavailableReason \? '#ef4444' : 'var\(--color-border\)' }}\s*\/>\s*{fieldErrors.purchaseInfoUnavailableReason && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{fieldErrors.purchaseInfoUnavailableReason}<\/div>}\s*<\/div>/g,
    `<FormTextarea label="Motivo da indisponibilidade *" value={purchase.purchaseInfoUnavailableReason} onChange={v => setPur('purchaseInfoUnavailableReason', v)} placeholder="Ex: Equipamento adquirido antes da implementação do sistema de rastreabilidade." rows={2} error={fieldErrors.purchaseInfoUnavailableReason} />`
);

content = content.replace(
    /<div>\s*<label style={labelStyle}>Motivo da indisponibilidade \*<\/label>\s*<textarea\s*value={warranty.warrantyInfoUnavailableReason}\s*onChange={e => setWar\('warrantyInfoUnavailableReason', e.target.value\)}\s*rows={2}\s*placeholder="Ex: Informações de garantia não disponíveis — equipamento recebido sem documentação."\s*style={{ \.\.\.inputStyle, resize: 'vertical', borderColor: fieldErrors.warrantyInfoUnavailableReason \? '#ef4444' : 'var\(--color-border\)' }}\s*\/>\s*{fieldErrors.warrantyInfoUnavailableReason && <div style={{ color: '#ef4444', fontSize: '0.75rem', marginTop: 4 }}>{fieldErrors.warrantyInfoUnavailableReason}<\/div>}\s*<\/div>/g,
    `<FormTextarea label="Motivo da indisponibilidade *" value={warranty.warrantyInfoUnavailableReason} onChange={v => setWar('warrantyInfoUnavailableReason', v)} placeholder="Ex: Informações de garantia não disponíveis — equipamento recebido sem documentação." rows={2} error={fieldErrors.warrantyInfoUnavailableReason} />`
);

content = content.replace(
    /<div>\s*<label style={{ \.\.\.labelStyle }}>Notas<\/label>\s*<textarea value={form.notes} onChange={e => set\('notes', e.target.value\)} rows={3}\s*style={{ \.\.\.inputStyle, resize: 'vertical' }} \/>\s*<\/div>/g,
    `<FormTextarea label="Notas" value={form.notes} onChange={v => set('notes', v)} rows={3} />`
);

// 10. Replace the Purchase/Traceability section container with SectionCard
content = content.replace(
    /<div style={{\s*border: '1px solid var\(--color-border\)', borderRadius: 10, padding: 14,\s*background: 'var\(--color-bg-surface\)', marginTop: 4\s*}}>\s*<div style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var\(--color-text\)', marginBottom: 10 }}>\s*📋 Compra \/ Rastreabilidade\s*<\/div>/g,
    `<SectionCard title="Compra / Rastreabilidade" icon={<span style={{fontSize: '1rem'}}>📋</span>}>`
);

// 11. Replace the Warranty section container with SectionCard
content = content.replace(
    /<div style={{\s*border: '1px solid var\(--color-border\)', borderRadius: 10, padding: 14,\s*background: 'var\(--color-bg-surface\)', marginTop: 4\s*}}>\s*<div style={{ fontSize: '0.82rem', fontWeight: 600, color: 'var\(--color-text\)', marginBottom: 10 }}>\s*🛡️ Garantia\s*<\/div>/g,
    `<SectionCard title="Garantia" icon={<span style={{fontSize: '1rem'}}>🛡️</span>}>`
);

// Fix the closing divs of SectionCards
// They were `</div>` at the end of the sections. We'll replace them manually.
content = content.replace(
    /<\/div>\s*\{\/\* ── Warranty Section ── \*\/\}/g,
    `</SectionCard>\n\n                {/* ── Warranty Section ── */}`
);

content = content.replace(
    /<\/div>\s*<FormTextarea label="Notas"/g,
    `</SectionCard>\n\n                <FormTextarea label="Notas"`
);


fs.writeFileSync(targetFile, content);
console.log('Done!');
