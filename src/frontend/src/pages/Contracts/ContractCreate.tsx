import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft, Save, Loader2, AlertTriangle } from 'lucide-react';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import { createContract, updateContract, fetchContractDetail, CreateContractPayload, ContractDetail } from '../../lib/contractsApi';
import { api } from '../../lib/api';
import { SupplierAutocomplete } from '../../components/SupplierAutocomplete';
import { DateInput } from '../../components/DateInput';
import { CurrencyInput } from '../../components/CurrencyInput';
import type { LookupDto } from '../../types';

// ─── Types matching existing master data DTO shapes ───

interface Company { id: number; name: string; isActive: boolean; }
interface Plant { id: number; name: string; code?: string; companyId: number; companyName?: string; isActive: boolean; }
interface Department { id: number; name: string; code?: string; isActive: boolean; }
interface Currency { id: number; code: string; symbol?: string; isActive: boolean; }
interface Supplier { id: number; name: string; portalCode?: string; primaveraCode?: string; taxId?: string; }

export default function ContractCreate() {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [initialLoad, setInitialLoad] = useState(!!id);

    // Lookup state
    const [contractTypes, setContractTypes] = useState<LookupDto[]>([]);
    const [companies, setCompanies] = useState<Company[]>([]);
    const [allPlants, setAllPlants] = useState<Plant[]>([]);
    const [departments, setDepartments] = useState<Department[]>([]);
    const [currencies, setCurrencies] = useState<Currency[]>([]);
    const [lookupsLoading, setLookupsLoading] = useState(true);
    const [lookupErrors, setLookupErrors] = useState<string[]>([]);

    // Form state
    const [title, setTitle] = useState('');
    const [description, setDescription] = useState('');
    const [contractTypeId, setContractTypeId] = useState<number | ''>('');
    const [companyId, setCompanyId] = useState<number | ''>('');
    const [plantId, setPlantId] = useState<number | ''>('');
    const [departmentId, setDepartmentId] = useState<number | ''>('');
    const [supplierId, setSupplierId] = useState<number | ''>('');
    const [counterpartyName, setCounterpartyName] = useState('');
    const [effectiveDate, setEffectiveDate] = useState('');
    const [expirationDate, setExpirationDate] = useState('');
    const [signedAt, setSignedAt] = useState('');
    const [autoRenew, setAutoRenew] = useState(false);
    const [renewalNoticeDays, setRenewalNoticeDays] = useState('');
    const [totalContractValue, setTotalContractValue] = useState('');
    const [currencyId, setCurrencyId] = useState<number | ''>('');
    const [paymentTerms, setPaymentTerms] = useState('');
    const [governingLaw, setGoverningLaw] = useState('');
    const [terminationClauses, setTerminationClauses] = useState('');

    // ─── Load lookups from existing master data endpoints ───

    useEffect(() => {
        const loadLookups = async () => {
            setLookupsLoading(true);
            const errors: string[] = [];

            // Use Promise.allSettled to load all lookups independently
            const [typesResult, companiesResult, plantsResult, departmentsResult, currenciesResult] =
                await Promise.allSettled([
                    api.lookups.getContractTypes(true),
                    api.lookups.getCompanies(),
                    api.lookups.getPlants(),
                    api.lookups.getDepartments(),
                    api.lookups.getCurrencies()
                ]);

            if (typesResult.status === 'fulfilled') setContractTypes(typesResult.value);
            else errors.push('Tipos de contrato');

            if (companiesResult.status === 'fulfilled') setCompanies(companiesResult.value);
            else errors.push('Empresas');

            if (plantsResult.status === 'fulfilled') setAllPlants(plantsResult.value);
            else errors.push('Fábricas');

            if (departmentsResult.status === 'fulfilled') setDepartments(departmentsResult.value);
            else errors.push('Departamentos');

            if (currenciesResult.status === 'fulfilled') setCurrencies(currenciesResult.value);
            else errors.push('Moedas');

            setLookupErrors(errors);
            setLookupsLoading(false);
        };
        loadLookups();
    }, []);

    // ─── Load Existing Contract for Edit ───
    useEffect(() => {
        if (!id) return;
        const loadContract = async () => {
            try {
                const data = await fetchContractDetail(id);
                setTitle(data.title || '');
                setDescription(data.description || '');
                setContractTypeId(data.contractTypeId || '');
                setCompanyId(data.companyId || '');
                setPlantId(data.plantId || '');
                setDepartmentId(data.departmentId || '');
                setSupplierId(data.supplierId || '');
                setCounterpartyName(data.counterpartyName || '');
                setEffectiveDate(data.effectiveDateUtc ? data.effectiveDateUtc.split('T')[0] : '');
                setExpirationDate(data.expirationDateUtc ? data.expirationDateUtc.split('T')[0] : '');
                setSignedAt(data.signedAtUtc ? data.signedAtUtc.split('T')[0] : '');
                setAutoRenew(data.autoRenew || false);
                setRenewalNoticeDays(data.renewalNoticeDays ? data.renewalNoticeDays.toString() : '');
                setTotalContractValue(data.totalContractValue != null ? data.totalContractValue.toString() : '');
                setCurrencyId(data.currencyId || '');
                setPaymentTerms(data.paymentTerms || '');
                setGoverningLaw(data.governingLaw || '');
                setTerminationClauses(data.terminationClauses || '');
            } catch (err: any) {
                setError(err.message || 'Erro ao carregar contrato para edição.');
            } finally {
                setInitialLoad(false);
            }
        };
        loadContract();
    }, [id]);

    // ─── Derived: plants filtered by selected company ───

    const filteredPlants = companyId
        ? allPlants.filter(p => p.companyId === companyId)
        : allPlants;

    // ─── Reset dependent fields when company changes ───

    const handleCompanyChange = useCallback((newCompanyId: number | '') => {
        setCompanyId(newCompanyId);
        // Reset plant if it doesn't belong to the new company
        if (newCompanyId && plantId) {
            const plantBelongs = allPlants.some(p => p.id === plantId && p.companyId === newCompanyId);
            if (!plantBelongs) setPlantId('');
        }
    }, [allPlants, plantId]);

    // ─── Submit ───

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');

        if (!title || !contractTypeId || !companyId || !departmentId || !effectiveDate || !expirationDate) {
            setError('Preencha todos os campos obrigatórios.');
            return;
        }

        const payload: CreateContractPayload = {
            title,
            description: description || undefined,
            contractTypeId: contractTypeId as number,
            companyId: companyId as number,
            plantId: plantId ? plantId as number : undefined,
            departmentId: departmentId as number,
            supplierId: supplierId ? supplierId as number : undefined,
            counterpartyName: counterpartyName || undefined,
            effectiveDateUtc: new Date(effectiveDate).toISOString(),
            expirationDateUtc: new Date(expirationDate).toISOString(),
            signedAtUtc: signedAt ? new Date(signedAt).toISOString() : undefined,
            autoRenew,
            renewalNoticeDays: renewalNoticeDays ? parseInt(renewalNoticeDays) : undefined,
            totalContractValue: totalContractValue ? parseFloat(totalContractValue) : undefined,
            currencyId: currencyId ? currencyId as number : undefined,
            paymentTerms: paymentTerms || undefined,
            governingLaw: governingLaw || undefined,
            terminationClauses: terminationClauses || undefined
        };

        setSaving(true);
        try {
            let result: ContractDetail;
            if (id) {
                result = await updateContract(id, payload);
            } else {
                result = await createContract(payload);
            }
            navigate(`/contracts/${result.id}`);
        } catch (err: any) {
            setError(err.message || (id ? 'Erro ao atualizar contrato.' : 'Erro ao criar contrato.'));
        } finally {
            setSaving(false);
        }
    };

    // ─── Styles ───

    const fieldStyle: React.CSSProperties = {
        width: '100%', padding: '10px 14px', borderRadius: 'var(--radius-md)',
        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
        fontSize: '0.9rem', fontFamily: 'var(--font-family-body)', color: 'var(--color-text-main)'
    };
    const labelStyle: React.CSSProperties = {
        display: 'block', marginBottom: '6px', fontSize: '0.8rem',
        fontWeight: 700, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em'
    };
    const sectionStyle: React.CSSProperties = {
        display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))',
        gap: '1.25rem', padding: '1.5rem',
        backgroundColor: 'var(--color-bg-page)', borderRadius: 'var(--radius-lg)',
        border: '1px solid var(--color-border)'
    };
    const sectionTitleStyle: React.CSSProperties = {
        fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-primary)',
        textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: '1rem',
        gridColumn: '1 / -1'
    };

    // ─── Render helpers ───

    const renderSelectEmpty = (label: string) => (
        <option value="" disabled style={{ color: 'var(--color-text-muted)' }}>
            {lookupsLoading ? 'Carregando...' : `Nenhum(a) ${label} disponível`}
        </option>
    );

    return (
        <PageContainer>
            <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', marginBottom: '1.5rem' }}>
                <button
                    onClick={() => navigate('/contracts/list')}
                    style={{
                        display: 'flex', alignItems: 'center', gap: '6px',
                        padding: '8px 16px', borderRadius: 'var(--radius-md)',
                        border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                        cursor: 'pointer', fontSize: '0.85rem', fontWeight: 600,
                        color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)'
                    }}
                >
                    <ArrowLeft size={16} /> Voltar
                </button>
            </div>

            <PageHeader 
                title={id ? "Editar Contrato" : "Novo Contrato"} 
                subtitle={id ? "Altere as informações do contrato" : "Preencha os dados para registar um novo contrato"} 
            />

            {/* Lookup loading indicator */}
            {lookupsLoading && (
                <div style={{
                    display: 'flex', alignItems: 'center', gap: '8px', padding: '12px 16px',
                    borderRadius: 'var(--radius-md)', marginBottom: '1rem',
                    backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)',
                    fontSize: '0.85rem', color: 'var(--color-text-muted)'
                }}>
                    <Loader2 size={16} style={{ animation: 'spin 1s linear infinite' }} />
                    Carregando dados mestres...
                </div>
            )}

            {/* Lookup errors */}
            {lookupErrors.length > 0 && (
                <div style={{
                    display: 'flex', alignItems: 'flex-start', gap: '8px', padding: '12px 16px',
                    borderRadius: 'var(--radius-md)', marginBottom: '1rem',
                    backgroundColor: '#fef3c7', border: '1px solid #f59e0b',
                    fontSize: '0.85rem', color: '#92400e'
                }}>
                    <AlertTriangle size={16} style={{ flexShrink: 0, marginTop: '2px' }} />
                    <span>Falha ao carregar: {lookupErrors.join(', ')}. Verifique a conexão e recarregue a página.</span>
                </div>
            )}

            {error && (
                <div style={{
                    padding: '12px 16px', borderRadius: 'var(--radius-md)', marginBottom: '1rem',
                    backgroundColor: '#fee2e2', color: '#dc2626', fontWeight: 600, fontSize: '0.85rem'
                }}>
                    {error}
                </div>
            )}

            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
                {/* ── General Info ── */}
                <div style={sectionStyle}>
                    <div style={sectionTitleStyle}>Informações Gerais</div>
                    <div style={{ gridColumn: '1 / -1' }}>
                        <label style={labelStyle}>Título *</label>
                        <input style={fieldStyle} value={title} onChange={e => setTitle(e.target.value)} placeholder="Título do contrato" required />
                    </div>
                    <div style={{ gridColumn: '1 / -1' }}>
                        <label style={labelStyle}>Descrição</label>
                        <textarea style={{ ...fieldStyle, minHeight: '80px', resize: 'vertical' }} value={description} onChange={e => setDescription(e.target.value)} placeholder="Descrição opcional" />
                    </div>
                    <div>
                        <label style={labelStyle}>Tipo de Contrato *</label>
                        <select style={fieldStyle} value={contractTypeId} onChange={e => setContractTypeId(e.target.value ? parseInt(e.target.value) : '')} required>
                            <option value="">Selecionar...</option>
                            {contractTypes.length > 0
                                ? contractTypes.map(t => <option key={t.id} value={t.id}>{t.name}</option>)
                                : !lookupsLoading && renderSelectEmpty('tipo de contrato')
                            }
                        </select>
                    </div>
                    <div>
                        <label style={labelStyle}>Fornecedor</label>
                        <SupplierAutocomplete onChange={(id) => setSupplierId(id || '')} placeholder="Pesquisar fornecedor..." />
                        <p style={{ marginTop: '4px', fontSize: '0.65rem', color: 'var(--color-text-muted)' }}>Deixe em branco se a contraparte for manual.</p>
                    </div>
                    <div>
                        <label style={labelStyle}>Contraparte (se não fornecedor)</label>
                        <input style={fieldStyle} value={counterpartyName} onChange={e => setCounterpartyName(e.target.value)} placeholder="Nome da contraparte" />
                    </div>
                </div>

                {/* ── Org Scope ── */}
                <div style={sectionStyle}>
                    <div style={sectionTitleStyle}>Âmbito Organizacional</div>
                    <div>
                        <label style={labelStyle}>Empresa *</label>
                        <select style={fieldStyle} value={companyId} onChange={e => handleCompanyChange(e.target.value ? parseInt(e.target.value) : '')} required>
                            <option value="">Selecionar...</option>
                            {companies.length > 0
                                ? companies.map(c => <option key={c.id} value={c.id}>{c.name}</option>)
                                : !lookupsLoading && renderSelectEmpty('empresa')
                            }
                        </select>
                    </div>
                    <div>
                        <label style={labelStyle}>Fábrica</label>
                        <select style={fieldStyle} value={plantId} onChange={e => setPlantId(e.target.value ? parseInt(e.target.value) : '')}>
                            <option value="">Todas (Empresa)</option>
                            {companyId
                                ? (filteredPlants.length > 0
                                    ? filteredPlants.map(p => <option key={p.id} value={p.id}>{p.name}{p.code ? ` (${p.code})` : ''}</option>)
                                    : !lookupsLoading && <option value="" disabled>Nenhuma fábrica nesta empresa</option>
                                )
                                : !lookupsLoading && <option value="" disabled>Selecione uma empresa primeiro</option>
                            }
                        </select>
                    </div>
                    <div>
                        <label style={labelStyle}>Departamento *</label>
                        <select style={fieldStyle} value={departmentId} onChange={e => setDepartmentId(e.target.value ? parseInt(e.target.value) : '')} required>
                            <option value="">Selecionar...</option>
                            {departments.length > 0
                                ? departments.map(d => <option key={d.id} value={d.id}>{d.name}{d.code ? ` (${d.code})` : ''}</option>)
                                : !lookupsLoading && renderSelectEmpty('departamento')
                            }
                        </select>
                    </div>
                </div>

                {/* ── Dates ── */}
                <div style={sectionStyle}>
                    <div style={sectionTitleStyle}>Vigência</div>
                    <div>
                        <label style={labelStyle}>Data de Início *</label>
                        <DateInput style={fieldStyle} value={effectiveDate} onChange={setEffectiveDate} required />
                    </div>
                    <div>
                        <label style={labelStyle}>Data de Expiração *</label>
                        <DateInput style={fieldStyle} value={expirationDate} onChange={setExpirationDate} required />
                    </div>
                    <div>
                        <label style={labelStyle}>Data de Assinatura</label>
                        <DateInput style={fieldStyle} value={signedAt} onChange={setSignedAt} />
                    </div>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer' }}>
                            <input type="checkbox" checked={autoRenew} onChange={e => setAutoRenew(e.target.checked)} />
                            <span style={{ fontSize: '0.85rem', fontWeight: 600 }}>Renovação Automática</span>
                        </label>
                        {autoRenew && (
                            <div>
                                <label style={labelStyle}>Dias de Aviso Prévio</label>
                                <input type="number" style={fieldStyle} value={renewalNoticeDays} onChange={e => setRenewalNoticeDays(e.target.value)} placeholder="30" />
                            </div>
                        )}
                    </div>
                </div>

                {/* ── Financial ── */}
                <div style={sectionStyle}>
                    <div style={sectionTitleStyle}>Dados Financeiros</div>
                    <div>
                        <label style={labelStyle}>Valor Total do Contrato</label>
                        <CurrencyInput style={fieldStyle} value={totalContractValue} onChange={setTotalContractValue} placeholder="0,00" />
                    </div>
                    <div>
                        <label style={labelStyle}>Moeda</label>
                        <select style={fieldStyle} value={currencyId} onChange={e => setCurrencyId(e.target.value ? parseInt(e.target.value) : '')}>
                            <option value="">Selecionar...</option>
                            {currencies.length > 0
                                ? currencies.map(c => (
                                    <option key={c.id} value={c.id}>
                                        {c.code}{c.symbol ? ` (${c.symbol})` : ''}
                                    </option>
                                ))
                                : !lookupsLoading && renderSelectEmpty('moeda')
                            }
                        </select>
                    </div>
                    <div style={{ gridColumn: '1 / -1' }}>
                        <label style={labelStyle}>Condições de Pagamento</label>
                        <textarea style={{ ...fieldStyle, minHeight: '60px', resize: 'vertical' }} value={paymentTerms} onChange={e => setPaymentTerms(e.target.value)} placeholder="Ex: 30 dias após fatura" />
                    </div>
                </div>

                {/* ── Legal ── */}
                <div style={sectionStyle}>
                    <div style={sectionTitleStyle}>Cláusulas Legais</div>
                    <div>
                        <label style={labelStyle}>Lei Aplicável</label>
                        <input style={fieldStyle} value={governingLaw} onChange={e => setGoverningLaw(e.target.value)} placeholder="Ex: Lei Angolana" />
                    </div>
                    <div style={{ gridColumn: '1 / -1' }}>
                        <label style={labelStyle}>Cláusulas de Rescisão</label>
                        <textarea style={{ ...fieldStyle, minHeight: '60px', resize: 'vertical' }} value={terminationClauses} onChange={e => setTerminationClauses(e.target.value)} placeholder="Condições de rescisão antecipada" />
                    </div>
                </div>

                {/* ── Actions ── */}
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '1rem', padding: '1rem 0' }}>
                    <button
                        type="button"
                        onClick={() => navigate('/contracts/list')}
                        style={{
                            padding: '12px 24px', borderRadius: 'var(--radius-md)',
                            border: '1px solid var(--color-border)', backgroundColor: 'var(--color-bg-surface)',
                            cursor: 'pointer', fontWeight: 700, fontSize: '0.85rem',
                            color: 'var(--color-text-muted)', fontFamily: 'var(--font-family-body)'
                        }}
                    >
                        Cancelar
                    </button>
                    <button
                        type="submit"
                        disabled={saving || lookupsLoading}
                        style={{
                            display: 'flex', alignItems: 'center', gap: '8px',
                            padding: '12px 32px', borderRadius: 'var(--radius-md)',
                            backgroundColor: (saving || lookupsLoading) ? 'var(--color-text-muted)' : 'var(--color-primary)', color: 'white',
                            border: 'none', cursor: (saving || lookupsLoading) ? 'default' : 'pointer',
                            fontWeight: 700, fontSize: '0.85rem', fontFamily: 'var(--font-family-body)',
                            transition: 'all 0.2s'
                        }}
                    >
                        <Save size={16} /> {saving ? 'Salvando...' : (id ? 'Guardar Alterações' : 'Criar Contrato')}
                    </button>
                </div>
            </form>
        </PageContainer>
    );
}
