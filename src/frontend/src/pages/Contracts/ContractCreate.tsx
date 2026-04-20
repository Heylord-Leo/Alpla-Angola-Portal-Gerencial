import { useState, useEffect, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft, Save, Loader2, AlertTriangle, ChevronDown, ChevronUp, Info, BookOpen } from 'lucide-react';
import { GuideModal, GuideModalSection } from '../../components/ui/GuideModal';
import { PageContainer } from '../../components/ui/PageContainer';
import { PageHeader } from '../../components/ui/PageHeader';
import {
    createContract, updateContract, fetchContractDetail,
    fetchPaymentTermTypes, fetchReferenceEventTypes,
    CreateContractPayload, ContractDetail,
    PaymentTermType, ReferenceEventType
} from '../../lib/contractsApi';
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

// ─── Helpers ───

const PENALTY_TYPES = [
    { code: 'PERCENTAGE', label: 'Percentagem (%)' },
    { code: 'FIXED_AMOUNT', label: 'Valor Fixo' }
];

function SectionToggle({ title, open, onToggle }: { title: string; open: boolean; onToggle: () => void }) {
    return (
        <button
            type="button"
            onClick={onToggle}
            style={{
                display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                width: '100%', background: 'none', border: 'none', cursor: 'pointer',
                padding: '0', gridColumn: '1 / -1'
            }}
        >
            <span style={{
                fontSize: '0.85rem', fontWeight: 800, color: 'var(--color-primary)',
                textTransform: 'uppercase', letterSpacing: '0.08em'
            }}>
                {title}
            </span>
            {open ? <ChevronUp size={16} color="var(--color-primary)" /> : <ChevronDown size={16} color="var(--color-primary)" />}
        </button>
    );
}

function InfoNote({ text }: { text: string }) {
    return (
        <div style={{
            display: 'flex', gap: '8px', alignItems: 'flex-start',
            padding: '10px 14px', borderRadius: 'var(--radius-md)',
            backgroundColor: 'rgba(var(--color-primary-rgb), 0.06)',
            border: '1px solid rgba(var(--color-primary-rgb), 0.2)',
            fontSize: '0.8rem', color: 'var(--color-text-muted)',
            gridColumn: '1 / -1'
        }}>
            <Info size={14} style={{ flexShrink: 0, marginTop: '1px', color: 'var(--color-primary)' }} />
            <span>{text}</span>
        </div>
    );
}

// ─── Help icon button — identical style to RequestsDashboard ───
function FieldHelpButton({ onClick }: { onClick: () => void }) {
    return (
        <button
            type="button"
            onClick={onClick}
            style={{
                background: 'none', border: 'none', cursor: 'pointer',
                color: '#94a3b8', display: 'inline-flex', alignItems: 'center',
                justifyContent: 'center', padding: '2px', borderRadius: '50%',
                transition: 'all 0.2s', verticalAlign: 'middle', marginLeft: '4px',
                lineHeight: 0,
            }}
            onMouseOver={(e) => { e.currentTarget.style.color = 'var(--color-primary)'; e.currentTarget.style.backgroundColor = '#EFF6FF'; }}
            onMouseOut={(e) => { e.currentTarget.style.color = '#94a3b8'; e.currentTarget.style.backgroundColor = 'transparent'; }}
            title="Ajuda sobre este campo"
        >
            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="12" cy="12" r="10" />
                <path d="M12 16v-4" />
                <path d="M12 8h.01" />
            </svg>
        </button>
    );
}

// ─── Help content catalogue for Regras de Pagamento ───
type PaymentHelpKey =
    | 'tipoDePrazo' | 'eventoDeReferencia' | 'diasParaPagamento' | 'diaFixo'
    | 'ajusteManual' | 'tolerancia'
    | 'possuiMulta' | 'tipoDeMulta' | 'valorDaMulta'
    | 'possuiJuros' | 'tipoDeJuros' | 'valorDosJuros'
    | 'resumoDaRegra' | 'observacoesFinanceiras' | 'observacoesMultaJuros';

const PAYMENT_HELP: Record<PaymentHelpKey, { title: string; descricao: string; comoFunciona: string; exemplos: string[] }> = {
    tipoDePrazo: {
        title: 'Tipo de Prazo',
        descricao: 'Define a regra usada para calcular ou informar o vencimento das parcelas do contrato.',
        comoFunciona: 'Cada tipo determina uma lógica diferente de vencimento. O pagamento pode ser calculado a partir de um evento de referência, em um dia fixo do mês, ou ser informado manualmente.',
        exemplos: [
            'Dias após evento de referência: 30 dias após recebimento da fatura',
            'Dia fixo do mês: todo dia 10',
            'Dia fixo do mês seguinte: dia 15 do próximo mês',
            'À vista: pagamento na data do evento',
            'Pagamento antecipado (manual): vencimento informado manualmente',
            'Vencimento manual: operador informa o vencimento da parcela',
            'Regra personalizada (texto livre): quando a regra não se encaixa nas opções estruturadas',
        ],
    },
    eventoDeReferencia: {
        title: 'Evento de Referência',
        descricao: 'Define a data-base usada para calcular o vencimento quando o prazo depende de um evento.',
        comoFunciona: 'O sistema usa essa data como ponto de partida para calcular o vencimento da parcela.',
        exemplos: [
            'Data de criação da parcela',
            'Data de assinatura do contrato',
            'Data de recebimento da fatura',
            'Data de referência manual',
        ],
    },
    diasParaPagamento: {
        title: 'Dias para Pagamento',
        descricao: 'Quantidade de dias adicionada ao evento de referência para calcular o vencimento.',
        comoFunciona: 'Este campo é usado quando o tipo de prazo é «Dias após evento de referência».',
        exemplos: [
            'Evento = Data de recebimento da fatura, Dias = 30',
            'Fatura recebida em 05/04/2026 → Vencimento = 05/05/2026',
        ],
    },
    diaFixo: {
        title: 'Dia Fixo',
        descricao: 'Define o dia do mês em que a parcela vence.',
        comoFunciona: 'Usado nos tipos «Dia fixo do mês» e «Dia fixo do mês seguinte». Limitado a 28 para compatibilidade com Fevereiro.',
        exemplos: [
            'Se o dia fixo for 10, o vencimento será no dia 10',
            'No tipo Dia fixo do mês seguinte, uma referência em abril pode gerar vencimento em 10/05',
        ],
    },
    ajusteManual: {
        title: 'Permitir ajuste manual do vencimento por parcela',
        descricao: 'Permite alterar manualmente o vencimento de uma parcela, mesmo quando o sistema calculou automaticamente uma data.',
        comoFunciona: 'O sistema calcula um vencimento inicial, mas o operador pode sobrescrevê-lo numa parcela específica. Útil para exceções negociadas pontualmente.',
        exemplos: [
            'Sistema calcula 05/05/2026, operador altera para 08/05/2026 por acordo verbal',
        ],
    },
    tolerancia: {
        title: 'Tolerância (dias de carência)',
        descricao: 'Número de dias adicionais após o vencimento antes do início da penalidade.',
        comoFunciona: 'A carência é somada ao vencimento. A penalidade começa no dia seguinte ao fim da carência. Zero significa que a multa começa no dia seguinte ao vencimento.',
        exemplos: [
            'Vencimento = 30/04/2026, Tolerância = 5 dias',
            'Data de carência = 05/05/2026',
            'Início da penalidade = 06/05/2026',
        ],
    },
    possuiMulta: {
        title: 'Possui multa por atraso?',
        descricao: 'Indica se o contrato prevê multa por atraso no pagamento.',
        comoFunciona: 'Ao marcar esta opção, o sistema habilita os campos de tipo e valor da multa. A multa é aplicada após o vencimento (ou após o período de carência, se configurado).',
        exemplos: [
            'Multa de 2% após o vencimento/carência',
        ],
    },
    tipoDeMulta: {
        title: 'Tipo de Multa',
        descricao: 'Define como a multa será registada: em percentagem sobre o valor em atraso, ou um valor fixo.',
        comoFunciona: 'Percentual = multiplicado sobre o montante em dívida. Valor fixo = montante absoluto por evento de atraso.',
        exemplos: [
            'Percentual: 2% sobre o valor em atraso',
            'Valor fixo: 10.000 AOA por atraso',
        ],
    },
    valorDaMulta: {
        title: 'Valor da Multa',
        descricao: 'Informa o valor da multa conforme o tipo selecionado.',
        comoFunciona: 'Se o tipo for percentual, introduza o número sem o símbolo %. Se for valor fixo, introduza o montante monetário.',
        exemplos: [
            '2 = 2% (tipo percentual)',
            '10000 = 10.000 AOA (tipo valor fixo)',
        ],
    },
    possuiJuros: {
        title: 'Possui juros por atraso?',
        descricao: 'Indica se o contrato prevê juros de mora em caso de atraso.',
        comoFunciona: 'Ao ativar, o sistema habilita os campos de tipo e valor dos juros. Os juros de mora acrescem ao valor em dívida por cada período de atraso.',
        exemplos: [
            'Juros de 1% ao mês sobre o saldo em atraso',
        ],
    },
    tipoDeJuros: {
        title: 'Tipo de Juros',
        descricao: 'Define como os juros serão interpretados: em percentagem ou valor fixo.',
        comoFunciona: 'Percentual mensal, percentual diário, ou o padrão suportado pelo sistema. O tipo determina como o valor será aplicado ao montante em atraso.',
        exemplos: [
            '1% ao mês (tipo percentual)',
        ],
    },
    valorDosJuros: {
        title: 'Valor dos Juros',
        descricao: 'Informa o valor numérico dos juros conforme o tipo escolhido.',
        comoFunciona: 'Se o tipo for percentual mensal, o valor 1 significa 1% ao mês. Se for valor fixo, representa o montante absoluto por período.',
        exemplos: [
            '1 = 1% ao mês (tipo percentual)',
            '500 = 500 AOA por mês (tipo valor fixo)',
        ],
    },
    resumoDaRegra: {
        title: 'Resumo da Regra',
        descricao: 'Texto resumido que descreve a condição de pagamento do contrato.',
        comoFunciona: 'Em regras estruturadas, o sistema pode gerar este resumo automaticamente. Em regra personalizada (texto livre), o utilizador escreve-o manualmente.',
        exemplos: [
            'Pagamento em 30 dias após recebimento da fatura',
            'Pagamento no dia 10 do mês seguinte',
            'Condição negociada conforme cronograma aprovado',
        ],
    },
    observacoesFinanceiras: {
        title: 'Observações Financeiras',
        descricao: 'Campo livre para registar detalhes adicionais sobre a condição financeira do contrato.',
        comoFunciona: 'Use este campo para notas internas que complementem a regra estruturada. Não é gerado automaticamente.',
        exemplos: [
            'Pagamento sujeito a validação da fatura',
            'Necessário anexar comprovativo do serviço executado',
            'Pagamento condicionado à aprovação do gestor',
        ],
    },
    observacoesMultaJuros: {
        title: 'Observações de Multa / Juros',
        descricao: 'Campo livre para detalhar regras específicas de atraso, multa e juros.',
        comoFunciona: 'Útil para registar condições contratuais específicas que não se encaixam nos campos estruturados de multa e juros.',
        exemplos: [
            'Multa aplicável apenas após notificação formal',
            'Juros não acumulam com multa fixa',
            'Condição válida conforme cláusula 8 do contrato',
        ],
    },
};

function PaymentRuleHelpModal({ field, onClose }: { field: PaymentHelpKey | null; onClose: () => void }) {
    if (!field) return null;
    const content = PAYMENT_HELP[field];
    return (
        <GuideModal isOpen={true} onClose={onClose} title={content.title}>
            <GuideModalSection
                icon={<Info size={24} />}
                iconBgColor="#EFF6FF"
                iconColor="var(--color-primary)"
                title="Descrição"
            >
                <p style={{ margin: '8px 0 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                    {content.descricao}
                </p>
            </GuideModalSection>

            <GuideModalSection
                icon={<BookOpen size={24} />}
                iconBgColor="#F0FDF4"
                iconColor="#16a34a"
                title="Como funciona"
            >
                <p style={{ margin: '8px 0 0', fontSize: '14px', lineHeight: 1.6, color: '#334155' }}>
                    {content.comoFunciona}
                </p>
            </GuideModalSection>

            {content.exemplos.length > 0 && (
                <GuideModalSection
                    icon={<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 20h9" /><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z" /></svg>}
                    iconBgColor="#FFFBEB"
                    iconColor="#d97706"
                    title="Exemplo(s)"
                >
                    <ul style={{ margin: '8px 0 0 20px', fontSize: '14px', color: '#334155', lineHeight: 1.7 }}>
                        {content.exemplos.map((ex, i) => <li key={i}>{ex}</li>)}
                    </ul>
                </GuideModalSection>
            )}
        </GuideModal>
    );
}

export default function ContractCreate() {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState('');
    const [loading, setLoading] = useState(!!id);

    // Lookup state
    const [contractTypes, setContractTypes] = useState<LookupDto[]>([]);
    const [companies, setCompanies] = useState<Company[]>([]);
    const [allPlants, setAllPlants] = useState<Plant[]>([]);
    const [departments, setDepartments] = useState<Department[]>([]);
    const [currencies, setCurrencies] = useState<Currency[]>([]);
    const [paymentTermTypes, setPaymentTermTypes] = useState<PaymentTermType[]>([]);
    const [referenceEventTypes, setReferenceEventTypes] = useState<ReferenceEventType[]>([]);
    const [lookupsLoading, setLookupsLoading] = useState(true);
    const [lookupErrors, setLookupErrors] = useState<string[]>([]);

    // UI state
    const [paymentRulesOpen, setPaymentRulesOpen] = useState(false);
    const [paymentHelpField, setPaymentHelpField] = useState<string | null>(null);

    // ─── General form state ───
    const [title, setTitle] = useState('');
    const [description, setDescription] = useState('');
    const [contractTypeId, setContractTypeId] = useState<number | ''>('');
    const [companyId, setCompanyId] = useState<number | ''>('');
    const [plantId, setPlantId] = useState<number | ''>('');
    const [departmentId, setDepartmentId] = useState<number | ''>('');
    const [supplierId, setSupplierId] = useState<number | ''>('');
    const [supplierInitialName, setSupplierInitialName] = useState('');
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

    // ─── Payment Rule form state (DEC-117) ───
    const [paymentTermTypeCode, setPaymentTermTypeCode] = useState('');
    const [referenceEventTypeCode, setReferenceEventTypeCode] = useState('');
    const [paymentTermDays, setPaymentTermDays] = useState('');
    const [paymentFixedDay, setPaymentFixedDay] = useState('');
    const [allowsManualDueDateOverride, setAllowsManualDueDateOverride] = useState(false);
    const [gracePeriodDays, setGracePeriodDays] = useState('');
    const [hasLatePenalty, setHasLatePenalty] = useState(false);
    const [latePenaltyTypeCode, setLatePenaltyTypeCode] = useState('');
    const [latePenaltyValue, setLatePenaltyValue] = useState('');
    const [hasLateInterest, setHasLateInterest] = useState(false);
    const [lateInterestTypeCode, setLateInterestTypeCode] = useState('');
    const [lateInterestValue, setLateInterestValue] = useState('');
    const [paymentRuleSummary, setPaymentRuleSummary] = useState('');
    const [financialNotes, setFinancialNotes] = useState('');
    const [penaltyNotes, setPenaltyNotes] = useState('');

    // ─── Derived helpers ───

    const selectedTermType = paymentTermTypes.find(t => t.code === paymentTermTypeCode);
    const isCustomText = paymentTermTypeCode === 'CUSTOM_TEXT';
    const isManualType = paymentTermTypeCode === 'MANUAL' || paymentTermTypeCode === 'ADVANCE_PAYMENT';
    const requiresReference = selectedTermType?.requiresReferenceEvent ?? false;
    const requiresDays = selectedTermType?.requiresDays ?? false;
    const requiresFixedDay = selectedTermType?.requiresFixedDay ?? false;

    // ─── Load lookups ───

    useEffect(() => {
        const loadLookups = async () => {
            setLookupsLoading(true);
            const errors: string[] = [];

            const [typesResult, companiesResult, plantsResult, departmentsResult, currenciesResult, termTypesResult, refEventTypesResult] =
                await Promise.allSettled([
                    api.lookups.getContractTypes(true),
                    api.lookups.getCompanies(),
                    api.lookups.getPlants(),
                    api.lookups.getDepartments(),
                    api.lookups.getCurrencies(),
                    fetchPaymentTermTypes(),
                    fetchReferenceEventTypes()
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

            if (termTypesResult.status === 'fulfilled') setPaymentTermTypes(termTypesResult.value);
            else errors.push('Tipos de prazo de pagamento');

            if (refEventTypesResult.status === 'fulfilled') setReferenceEventTypes(refEventTypesResult.value);
            else errors.push('Eventos de referência');

            setLookupErrors(errors);
            setLookupsLoading(false);
        };
        loadLookups();
    }, []);

    // ─── Load existing contract for edit ───
    useEffect(() => {
        if (!id) return;
        const loadContract = async () => {
            setLoading(true);
            try {
                const data: ContractDetail = await fetchContractDetail(id);
                setTitle(data.title || '');
                setDescription(data.description || '');
                setContractTypeId(data.contractTypeId || '');
                setCompanyId(data.companyId || '');
                setPlantId(data.plantId || '');
                setDepartmentId(data.departmentId || '');
                if (data.supplierId) {
                    setSupplierId(data.supplierId);
                    setSupplierInitialName(data.supplierName || '');
                }
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

                // Payment rule fields
                if (data.paymentTermTypeCode) {
                    setPaymentTermTypeCode(data.paymentTermTypeCode);
                    setPaymentRulesOpen(true); // auto-open if rule exists
                }
                setReferenceEventTypeCode(data.referenceEventTypeCode || '');
                setPaymentTermDays(data.paymentTermDays != null ? data.paymentTermDays.toString() : '');
                setPaymentFixedDay(data.paymentFixedDay != null ? data.paymentFixedDay.toString() : '');
                setAllowsManualDueDateOverride(data.allowsManualDueDateOverride || false);
                setGracePeriodDays(data.gracePeriodDays != null ? data.gracePeriodDays.toString() : '');
                setHasLatePenalty(data.hasLatePenalty || false);
                setLatePenaltyTypeCode(data.latePenaltyTypeCode || '');
                setLatePenaltyValue(data.latePenaltyValue != null ? data.latePenaltyValue.toString() : '');
                setHasLateInterest(data.hasLateInterest || false);
                setLateInterestTypeCode(data.lateInterestTypeCode || '');
                setLateInterestValue(data.lateInterestValue != null ? data.lateInterestValue.toString() : '');
                setPaymentRuleSummary(data.paymentRuleSummary || '');
                setFinancialNotes(data.financialNotes || '');
                setPenaltyNotes(data.penaltyNotes || '');
            } catch (err: any) {
                setError(err.message || 'Erro ao carregar contrato para edição.');
            } finally {
                setLoading(false);
            }
        };
        loadContract();
    }, [id]);

    // ─── Derived: plants filtered by selected company ───

    const filteredPlants = companyId
        ? allPlants.filter(p => p.companyId === companyId)
        : allPlants;

    // ─── Reset plant when company changes ───

    const handleCompanyChange = useCallback((newCompanyId: number | '') => {
        setCompanyId(newCompanyId);
        if (newCompanyId && plantId) {
            const plantBelongs = allPlants.some(p => p.id === plantId && p.companyId === newCompanyId);
            if (!plantBelongs) setPlantId('');
        }
    }, [allPlants, plantId]);

    // ─── Reset reference event when term type changes ───

    const handleTermTypeChange = (code: string) => {
        setPaymentTermTypeCode(code);
        setReferenceEventTypeCode('');
        setPaymentTermDays('');
        setPaymentFixedDay('');
        // Clear auto-generated summary when user picks a new structured type
        if (code !== 'CUSTOM_TEXT') setPaymentRuleSummary('');
    };

    // ─── Submit ───

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError('');

        if (!title || !contractTypeId || !companyId || !departmentId || !effectiveDate || !expirationDate) {
            setError('Preencha todos os campos obrigatórios.');
            return;
        }

        // Payment rule validation
        if (requiresDays && !paymentTermDays) {
            setError('O tipo de prazo selecionado requer o número de dias para pagamento.');
            return;
        }
        if (requiresFixedDay && !paymentFixedDay) {
            setError('O tipo de prazo selecionado requer a definição do dia fixo de pagamento.');
            return;
        }
        if (requiresReference && !referenceEventTypeCode) {
            setError('O tipo de prazo selecionado requer a definição do evento de referência.');
            return;
        }
        if (isCustomText && !paymentRuleSummary) {
            setError('Para o tipo "Texto livre", é obrigatório descrever a regra no campo Resumo da Regra.');
            return;
        }
        if (hasLatePenalty && (!latePenaltyTypeCode || !latePenaltyValue)) {
            setError('Preencha o tipo e valor da multa por atraso.');
            return;
        }
        if (hasLateInterest && (!lateInterestTypeCode || !lateInterestValue)) {
            setError('Preencha o tipo e valor dos juros por atraso.');
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
            terminationClauses: terminationClauses || undefined,

            // Payment Rule (DEC-117)
            paymentTermTypeCode: paymentTermTypeCode || undefined,
            referenceEventTypeCode: requiresReference ? (referenceEventTypeCode || undefined) : undefined,
            paymentTermDays: requiresDays && paymentTermDays ? parseInt(paymentTermDays) : undefined,
            paymentFixedDay: requiresFixedDay && paymentFixedDay ? parseInt(paymentFixedDay) : undefined,
            allowsManualDueDateOverride,
            gracePeriodDays: gracePeriodDays ? parseInt(gracePeriodDays) : undefined,
            hasLatePenalty,
            latePenaltyTypeCode: hasLatePenalty ? latePenaltyTypeCode || undefined : undefined,
            latePenaltyValue: hasLatePenalty && latePenaltyValue ? parseFloat(latePenaltyValue) : undefined,
            hasLateInterest,
            lateInterestTypeCode: hasLateInterest ? lateInterestTypeCode || undefined : undefined,
            lateInterestValue: hasLateInterest && lateInterestValue ? parseFloat(lateInterestValue) : undefined,
            // CUSTOM_TEXT: user-authored. Structured types: backend generates it.
            paymentRuleSummary: isCustomText ? (paymentRuleSummary || undefined) : undefined,
            financialNotes: financialNotes || undefined,
            penaltyNotes: penaltyNotes || undefined,
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
    const checkLabel: React.CSSProperties = {
        display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer',
        fontSize: '0.85rem', fontWeight: 600, color: 'var(--color-text-main)'
    };

    const renderSelectEmpty = (label: string) => (
        <option value="" disabled style={{ color: 'var(--color-text-muted)' }}>
            {lookupsLoading ? 'Carregando...' : `Nenhum(a) ${label} disponível`}
        </option>
    );

    if (loading) {
        return (
            <PageContainer>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '3rem', color: 'var(--color-text-muted)', justifyContent: 'center' }}>
                    <Loader2 size={20} style={{ animation: 'spin 1s linear infinite' }} />
                    Carregando contrato...
                </div>
            </PageContainer>
        );
    }

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
                        <SupplierAutocomplete
                            onChange={(sid) => setSupplierId(sid || '')}
                            placeholder="Pesquisar fornecedor..."
                            initialName={supplierInitialName}
                        />
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
                        <label style={checkLabel}>
                            <input type="checkbox" checked={autoRenew} onChange={e => setAutoRenew(e.target.checked)} />
                            <span>Renovação Automática</span>
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
                        <label style={labelStyle}>Condições de Pagamento (Texto Livre)</label>
                        <textarea
                            style={{ ...fieldStyle, minHeight: '60px', resize: 'vertical' }}
                            value={paymentTerms}
                            onChange={e => setPaymentTerms(e.target.value)}
                            placeholder="Ex: 30 dias após fatura — campo livre para referência histórica"
                        />
                        <p style={{ marginTop: '4px', fontSize: '0.65rem', color: 'var(--color-text-muted)' }}>
                            Campo de texto livre mantido para compatibilidade. Use a secção Regras de Pagamento abaixo para configurar a regra estruturada.
                        </p>
                    </div>
                </div>

                {/* ── Regras de Pagamento (DEC-117) ── */}
                <div style={{
                    ...sectionStyle,
                    border: paymentRulesOpen
                        ? '1px solid rgba(var(--color-primary-rgb), 0.35)'
                        : '1px solid var(--color-border)',
                    transition: 'border-color 0.2s'
                }}>
                    <SectionToggle
                        title="Regras de Pagamento"
                        open={paymentRulesOpen}
                        onToggle={() => setPaymentRulesOpen(o => !o)}
                    />

                    {paymentRulesOpen && (
                        <>
                            {/* Tipo de Prazo */}
                            <div style={{ gridColumn: '1 / -1' }}>
                                <label style={labelStyle}>
                                    Tipo de Prazo
                                    <FieldHelpButton onClick={() => setPaymentHelpField('tipoDePrazo')} />
                                </label>
                                <select style={fieldStyle} value={paymentTermTypeCode} onChange={e => handleTermTypeChange(e.target.value)}>
                                    <option value="">Nenhuma regra estruturada</option>
                                    {paymentTermTypes.map(t => (
                                        <option key={t.code} value={t.code}>{t.label}</option>
                                    ))}
                                </select>
                                {selectedTermType?.description && (
                                    <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                        {selectedTermType.description}
                                    </p>
                                )}
                            </div>

                            {/* ADVANCE_PAYMENT / MANUAL info note */}
                            {isManualType && (
                                <InfoNote text="Este tipo de prazo requer que o vencimento seja definido manualmente em cada parcela/obrigação. Não há cálculo automático." />
                            )}

                            {/* Evento de Referência — only when the rule requires it */}
                            {requiresReference && (
                                <div>
                                    <label style={labelStyle}>
                                        Evento de Referência *
                                        <FieldHelpButton onClick={() => setPaymentHelpField('eventoDeReferencia')} />
                                    </label>
                                    <select
                                        style={fieldStyle}
                                        value={referenceEventTypeCode}
                                        onChange={e => setReferenceEventTypeCode(e.target.value)}
                                        required={requiresReference}
                                    >
                                        <option value="">Selecionar...</option>
                                        {referenceEventTypes.map(r => (
                                            <option key={r.code} value={r.code}>{r.label}</option>
                                        ))}
                                    </select>
                                    {referenceEventTypes.find(r => r.code === referenceEventTypeCode)?.description && (
                                        <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                            {referenceEventTypes.find(r => r.code === referenceEventTypeCode)?.description}
                                        </p>
                                    )}
                                </div>
                            )}

                            {/* Dias para Pagamento — only for FIXED_DAYS_AFTER_REFERENCE */}
                            {requiresDays && (
                                <div>
                                    <label style={labelStyle}>
                                        Dias para Pagamento *
                                        <FieldHelpButton onClick={() => setPaymentHelpField('diasParaPagamento')} />
                                    </label>
                                    <input
                                        type="number" min="1" max="365"
                                        style={fieldStyle}
                                        value={paymentTermDays}
                                        onChange={e => setPaymentTermDays(e.target.value)}
                                        placeholder="Ex: 30"
                                        required
                                    />
                                    <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                        Número de dias a adicionar ao evento de referência.
                                    </p>
                                </div>
                            )}

                            {/* Dia Fixo — only for FIXED_DAY_OF_MONTH / NEXT_MONTH_FIXED_DAY */}
                            {requiresFixedDay && (
                                <div>
                                    <label style={labelStyle}>
                                        Dia Fixo do Mês *
                                        <FieldHelpButton onClick={() => setPaymentHelpField('diaFixo')} />
                                    </label>
                                    <input
                                        type="number" min="1" max="28"
                                        style={fieldStyle}
                                        value={paymentFixedDay}
                                        onChange={e => setPaymentFixedDay(e.target.value)}
                                        placeholder="Ex: 10"
                                        required
                                    />
                                    <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                        Dia do mês de pagamento (1–28; limitado a 28 para compatibilidade com Fevereiro).
                                    </p>
                                </div>
                            )}

                            {/* CUSTOM_TEXT — user-authored summary */}
                            {isCustomText && (
                                <div style={{ gridColumn: '1 / -1' }}>
                                    <label style={labelStyle}>
                                        Resumo da Regra *
                                        <FieldHelpButton onClick={() => setPaymentHelpField('resumoDaRegra')} />
                                    </label>
                                    <textarea
                                        style={{ ...fieldStyle, minHeight: '70px', resize: 'vertical' }}
                                        value={paymentRuleSummary}
                                        onChange={e => setPaymentRuleSummary(e.target.value)}
                                        placeholder="Descreva a regra de pagamento de forma clara e completa"
                                        required
                                    />
                                    <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                        Para regras de texto livre, este resumo é definido por si. Para tipos estruturados, é gerado automaticamente pelo sistema.
                                    </p>
                                </div>
                            )}

                            {/* Separator */}
                            {paymentTermTypeCode && (
                                <div style={{ gridColumn: '1 / -1', borderTop: '1px solid var(--color-border)', paddingTop: '0.5rem' }} />
                            )}

                            {/* Permitir ajuste manual */}
                            {paymentTermTypeCode && !isManualType && (
                                <div style={{ gridColumn: '1 / -1' }}>
                                    <label style={checkLabel}>
                                        <input
                                            type="checkbox"
                                            checked={allowsManualDueDateOverride}
                                            onChange={e => setAllowsManualDueDateOverride(e.target.checked)}
                                        />
                                        <span>Permitir ajuste manual do vencimento por parcela</span>
                                        <FieldHelpButton onClick={() => setPaymentHelpField('ajusteManual')} />
                                    </label>
                                    <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)', marginLeft: '22px' }}>
                                        Quando ativado, o operador pode sobrepor o vencimento calculado automaticamente em cada parcela.
                                    </p>
                                </div>
                            )}

                            {/* Tolerância */}
                            {paymentTermTypeCode && (
                                <div>
                                    <label style={labelStyle}>
                                        Tolerância (dias de carência)
                                        <FieldHelpButton onClick={() => setPaymentHelpField('tolerancia')} />
                                    </label>
                                    <input
                                        type="number" min="0"
                                        style={fieldStyle}
                                        value={gracePeriodDays}
                                        onChange={e => setGracePeriodDays(e.target.value)}
                                        placeholder="0"
                                    />
                                    <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>
                                        Dias após o vencimento antes de aplicar multa. Zero significa que a multa começa no dia seguinte ao vencimento.
                                    </p>
                                </div>
                            )}

                            {/* Separator */}
                            {paymentTermTypeCode && (
                                <div style={{ gridColumn: '1 / -1', borderTop: '1px solid var(--color-border)', paddingTop: '0.5rem' }} />
                            )}

                            {/* Multa */}
                            {paymentTermTypeCode && (
                                <>
                                    <div style={{ gridColumn: '1 / -1' }}>
                                        <label style={checkLabel}>
                                            <input type="checkbox" checked={hasLatePenalty} onChange={e => setHasLatePenalty(e.target.checked)} />
                                            <span>Possui multa por atraso?</span>
                                            <FieldHelpButton onClick={() => setPaymentHelpField('possuiMulta')} />
                                        </label>
                                    </div>
                                    {hasLatePenalty && (
                                        <>
                                            <div>
                                                <label style={labelStyle}>
                                                    Tipo de Multa *
                                                    <FieldHelpButton onClick={() => setPaymentHelpField('tipoDeMulta')} />
                                                </label>
                                                <select style={fieldStyle} value={latePenaltyTypeCode} onChange={e => setLatePenaltyTypeCode(e.target.value)} required={hasLatePenalty}>
                                                    <option value="">Selecionar...</option>
                                                    {PENALTY_TYPES.map(p => <option key={p.code} value={p.code}>{p.label}</option>)}
                                                </select>
                                            </div>
                                            <div>
                                                <label style={labelStyle}>
                                                    Valor da Multa *
                                                    <FieldHelpButton onClick={() => setPaymentHelpField('valorDaMulta')} />
                                                </label>
                                                <input
                                                    type="number" min="0" step="0.01"
                                                    style={fieldStyle}
                                                    value={latePenaltyValue}
                                                    onChange={e => setLatePenaltyValue(e.target.value)}
                                                    placeholder={latePenaltyTypeCode === 'PERCENTAGE' ? 'Ex: 2.0' : 'Ex: 500.00'}
                                                    required={hasLatePenalty}
                                                />
                                                {latePenaltyTypeCode === 'PERCENTAGE' && (
                                                    <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>Valor em percentagem (%)</p>
                                                )}
                                            </div>
                                        </>
                                    )}
                                </>
                            )}

                            {/* Juros */}
                            {paymentTermTypeCode && (
                                <>
                                    <div style={{ gridColumn: '1 / -1' }}>
                                        <label style={checkLabel}>
                                            <input type="checkbox" checked={hasLateInterest} onChange={e => setHasLateInterest(e.target.checked)} />
                                            <span>Possui juros por atraso?</span>
                                            <FieldHelpButton onClick={() => setPaymentHelpField('possuiJuros')} />
                                        </label>
                                    </div>
                                    {hasLateInterest && (
                                        <>
                                            <div>
                                                <label style={labelStyle}>
                                                    Tipo de Juros *
                                                    <FieldHelpButton onClick={() => setPaymentHelpField('tipoDeJuros')} />
                                                </label>
                                                <select style={fieldStyle} value={lateInterestTypeCode} onChange={e => setLateInterestTypeCode(e.target.value)} required={hasLateInterest}>
                                                    <option value="">Selecionar...</option>
                                                    {PENALTY_TYPES.map(p => <option key={p.code} value={p.code}>{p.label}</option>)}
                                                </select>
                                            </div>
                                            <div>
                                                <label style={labelStyle}>
                                                    Valor dos Juros *
                                                    <FieldHelpButton onClick={() => setPaymentHelpField('valorDosJuros')} />
                                                </label>
                                                <input
                                                    type="number" min="0" step="0.01"
                                                    style={fieldStyle}
                                                    value={lateInterestValue}
                                                    onChange={e => setLateInterestValue(e.target.value)}
                                                    placeholder={lateInterestTypeCode === 'PERCENTAGE' ? 'Ex: 1.5' : 'Ex: 200.00'}
                                                    required={hasLateInterest}
                                                />
                                                {lateInterestTypeCode === 'PERCENTAGE' && (
                                                    <p style={{ marginTop: '4px', fontSize: '0.72rem', color: 'var(--color-text-muted)' }}>Valor em percentagem (%)</p>
                                                )}
                                            </div>
                                        </>
                                    )}
                                </>
                            )}

                            {/* Notes */}
                            {paymentTermTypeCode && (
                                <>
                                    <div style={{ gridColumn: '1 / -1', borderTop: '1px solid var(--color-border)', paddingTop: '0.5rem' }} />
                                    <div style={{ gridColumn: '1 / -1' }}>
                                        <label style={labelStyle}>
                                            Observações Financeiras
                                            <FieldHelpButton onClick={() => setPaymentHelpField('observacoesFinanceiras')} />
                                        </label>
                                        <textarea style={{ ...fieldStyle, minHeight: '60px', resize: 'vertical' }} value={financialNotes} onChange={e => setFinancialNotes(e.target.value)} placeholder="Observações internas sobre condições financeiras" />
                                    </div>
                                    {(hasLatePenalty || hasLateInterest) && (
                                        <div style={{ gridColumn: '1 / -1' }}>
                                            <label style={labelStyle}>
                                                Observações de Multa / Juros
                                                <FieldHelpButton onClick={() => setPaymentHelpField('observacoesMultaJuros')} />
                                            </label>
                                            <textarea style={{ ...fieldStyle, minHeight: '60px', resize: 'vertical' }} value={penaltyNotes} onChange={e => setPenaltyNotes(e.target.value)} placeholder="Observações sobre aplicação de multa e juros" />
                                        </div>
                                    )}
                                </>
                            )}

                            {!paymentTermTypeCode && (
                                <InfoNote text="Selecione um Tipo de Prazo para configurar a regra de pagamento estruturada. Sem regra estruturada, o vencimento de cada parcela deverá ser inserido manualmente." />
                            )}
                        </>
                    )}
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

            {/* ── Payment Rule Help Modals ── */}
            <PaymentRuleHelpModal
                field={paymentHelpField as PaymentHelpKey | null}
                onClose={() => setPaymentHelpField(null)}
            />
        </PageContainer>
    );
}
