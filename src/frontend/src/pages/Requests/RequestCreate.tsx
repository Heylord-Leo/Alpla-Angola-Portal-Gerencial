import React, { useState, useEffect, useRef } from 'react';
import { useNavigate, useLocation, useSearchParams } from 'react-router-dom';
import { Save, X, Paperclip, Trash2, AlertTriangle } from 'lucide-react';
import { api, ApiError } from '../../lib/api';
import { FeedbackType } from '../../components/ui/Feedback';
import { LookupDto, UserDto } from '../../types';
import { motion, AnimatePresence } from 'framer-motion';
import { RequestActionHeader, BreadcrumbItem } from './components/RequestActionHeader';
import { scrollToFirstError } from '../../lib/validation';
import { ROLES } from '../../constants/roles';
import { DateInput } from '../../components/DateInput';


export function RequestCreate() {
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();
    const location = useLocation() as { state: { fromList?: string } | null };
    
    // Copy Mode Detection
    const copyFromId = searchParams.get('copyFrom');
    const isCopyMode = !!copyFromId;

    const [loading, setLoading] = useState(false);
    const [isTemplateLoading, setIsTemplateLoading] = useState(false);
    const [feedback, setFeedback] = useState<{ type: FeedbackType; message: string | null }>({ type: 'error', message: null });
    const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
    const [requestTypes, setRequestTypes] = useState<any[]>([]);
    const [needLevels, setNeedLevels] = useState<LookupDto[]>([]);
    const [departments, setDepartments] = useState<LookupDto[]>([]);
    const [companies, setCompanies] = useState<any[]>([]);
    const [plants, setPlants] = useState<any[]>([]);
    const [users, setUsers] = useState<UserDto[]>([]);
    const [allowedPlantCodes, setAllowedPlantCodes] = useState<string[]>([]);
    const [isScopeLoading, setIsScopeLoading] = useState(true);
    const [attachments, setAttachments] = useState<File[]>([]);

    const [formData, setFormData] = useState({
        title: '',
        description: '',
        requestTypeId: '', 
        needByDateUtc: '',
        needLevelId: '',
        estimatedTotalAmount: '',
        currencyId: 1,    // Internal implementation default (Implementation detail, not a copied field)
        departmentId: '',
        companyId: '',
        plantId: '',
        buyerId: '',
        areaApproverId: '',
        finalApproverId: ''
    });

    const initialFormDataRef = useRef(formData);

    // Navigation Away Protection
    useEffect(() => {
        const handleBeforeUnload = (e: BeforeUnloadEvent) => {
            const isDirty = JSON.stringify(formData) !== JSON.stringify(initialFormDataRef.current);
            if (isDirty && !loading) {
                e.preventDefault();
                e.returnValue = '';
            }
        };

        window.addEventListener('beforeunload', handleBeforeUnload);
        return () => window.removeEventListener('beforeunload', handleBeforeUnload);
    }, [formData, loading]);

    // Load Lookups
    useEffect(() => {
        async function loadLookups() {
            try {
                setIsScopeLoading(true);
                const [levelsData, departmentsData, companiesData, plantsData, usersData, rtData, meData] = await Promise.all([
                    api.lookups.getNeedLevels(true),
                    api.lookups.getDepartments(true),
                    api.lookups.getCompanies(true),
                    api.lookups.getPlants(undefined, true),
                    api.users.list(),
                    api.lookups.getRequestTypes(true),
                    api.users.me()
                ]);
                setNeedLevels(levelsData);
                setDepartments(departmentsData);
                setCompanies(companiesData);
                setPlants(plantsData);
                setUsers(usersData);
                setRequestTypes(rtData);
                setAllowedPlantCodes(meData.plants || []);
            } catch (err) {
                console.error('Falha ao carregar dados auxiliares', err);
            } finally {
                setIsScopeLoading(false);
            }
        }
        loadLookups();
    }, []);

    // Handle Copy Mode Data Fetching
    useEffect(() => {
        if (!isCopyMode || isScopeLoading || plants.length === 0) return;

        async function loadTemplate() {
            try {
                setIsTemplateLoading(true);
                const data = await api.requests.getTemplate(copyFromId!);
                
                setFormData(prev => {
                    const next = {
                        ...prev,
                        title: `Cópia ${data.sourceRequestNumber} ${data.title}`,
                        description: data.description || '',
                        requestTypeId: data.requestTypeId ? String(data.requestTypeId) : '',
                        needLevelId: data.needLevelId ? String(data.needLevelId) : '',
                        departmentId: data.departmentId ? String(data.departmentId) : '',
                        companyId: data.companyId ? String(data.companyId) : '',
                        plantId: data.plantId ? String(data.plantId) : '',
                        buyerId: data.buyerId || '',
                        areaApproverId: data.areaApproverId || '',
                        finalApproverId: data.finalApproverId || '',
                        // Specifically NOT copied: currencyId (stays default 1), needByDateUtc (stays blank)
                    };
                    initialFormDataRef.current = next;
                    return next;
                });
            } catch (err: any) {
                setFeedback({ type: 'error', message: err.message || 'Falha ao carregar pedido para cópia.' });
            } finally {
                setIsTemplateLoading(false);
            }
        }

        loadTemplate();
    }, [isCopyMode, copyFromId, isScopeLoading, plants.length]);

    // Derived filtered data based on user scope
    const filteredPlants = plants.filter(p => allowedPlantCodes.includes(p.code));
    const filteredCompanies = companies.filter(c => 
        filteredPlants.some(p => p.companyId === c.id)
    );

    // Filter participants by their specific roles
    const buyers = users.filter(u => u.roles?.includes(ROLES.BUYER));
    const areaApprovers = users.filter(u => u.roles?.includes(ROLES.AREA_APPROVER));
    const finalApprovers = users.filter(u => u.roles?.includes(ROLES.FINAL_APPROVER));

    // Auto-selection of Company/Plant based on restricted scope
    useEffect(() => {
        if (isScopeLoading || isTemplateLoading || plants.length === 0 || companies.length === 0 || isCopyMode) return;

        setFormData(prev => {
            const next = { ...prev };
            
            // Case A: Only 1 authorized plant in total
            if (filteredPlants.length === 1) {
                const soloPlant = filteredPlants[0];
                next.plantId = String(soloPlant.id);
                next.companyId = String(soloPlant.companyId);
            } 
            // Case B: Multiple plants but only 1 authorized company
            else if (filteredCompanies.length === 1 && !next.companyId) {
                next.companyId = String(filteredCompanies[0].id);
            }

            return next;
        });
    }, [isScopeLoading, isTemplateLoading, plants.length, companies.length, filteredPlants.length, filteredCompanies.length, isCopyMode]);

    const clearFieldError = (fieldName: string) => {
        setFieldErrors(prev => {
            const next = { ...prev };
            const normalizedField = fieldName.toLowerCase();
            const key = Object.keys(next).find(k => {
                const normalizedKey = k.toLowerCase().replace(/^\$\./, '');
                return normalizedKey === normalizedField || normalizedKey.endsWith('.' + normalizedField);
            });
            if (key) delete next[key];
            return next;
        });
    };

    const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        if (e.target.files) {
            const newFiles = Array.from(e.target.files);
            setAttachments(prev => [...prev, ...newFiles]);
        }
    };

    const removeFile = (index: number) => {
        setAttachments(prev => prev.filter((_, i) => i !== index));
    };

    const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => {
        const { name, value } = e.target;
        setFormData(prev => {
            const next = { ...prev, [name]: value };
            // Clear plant if company explicitly changes
            if (name === 'companyId') {
                next.plantId = ''; // Start by clearing
                if (value) {
                    // Auto-select the matching plant if only one is authorized for the selected company
                    const plantsForCompany = filteredPlants.filter(p => p.companyId === Number(value));
                    if (plantsForCompany.length === 1) {
                        next.plantId = String(plantsForCompany[0].id);
                    }
                }
            }
            
            // Auto-select company if plant is explicitly chosen and not already aligned
            if (name === 'plantId' && value) {
                const selectedPlant = plants.find(p => p.id === Number(value));
                if (selectedPlant) {
                    next.companyId = String(selectedPlant.companyId);
                }
            }

            // Auto-fill Area Approver from Department - DEC-082
            if (name === 'departmentId' && value) {
                const dept = departments.find(d => d.id === Number(value));
                if (dept && dept.responsibleUserId) {
                    next.areaApproverId = dept.responsibleUserId;
                }
            }
            
            return next;
        });
        clearFieldError(name);
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        // Manual Validation for Required Fields
        const newErrors: Record<string, string[]> = {};
        if (!formData.requestTypeId) newErrors['RequestTypeId'] = ['O Tipo de Pedido é obrigatório.'];
        if (!formData.buyerId) newErrors['BuyerId'] = ['O Comprador Atribuído é obrigatório. (Selecione um comprador)'];
        if (!formData.areaApproverId) newErrors['AreaApproverId'] = ['O Aprovador de Área é obrigatório.'];
        if (!formData.finalApproverId) newErrors['FinalApproverId'] = ['O Aprovador Final é obrigatório.'];
        // New required checks
        if (!formData.needLevelId) newErrors['NeedLevelId'] = ['O grau de necessidade é obrigatório.'];
        if (!formData.departmentId) newErrors['DepartmentId'] = ['O departamento é obrigatório.'];
        if (!formData.companyId) newErrors['CompanyId'] = ['A empresa é obrigatória.'];
        if (!formData.plantId) newErrors['PlantId'] = ['A planta é obrigatória.'];

        // Conditional Validation: Data de Necessidade is mandatory for Cotação (TypeId === 1)
        if (Number(formData.requestTypeId) === 1) {
            if (!formData.needByDateUtc) {
                newErrors['NeedByDateUtc'] = ['A data Necessário Até é obrigatória para pedidos de Cotação.'];
            } else if (new Date(formData.needByDateUtc).getTime() < new Date().setHours(0, 0, 0, 0)) {
                newErrors['NeedByDateUtc'] = ['A data Necessário Até não pode ser no passado.'];
            }
        }

        // Conditional Validation: Data de Necessidade is mandatory for Cotação (TypeId === 1)
        if (Number(formData.requestTypeId) === 1) {
            if (!formData.needByDateUtc) {
                newErrors['NeedByDateUtc'] = ['A data Necessário Até é obrigatória para pedidos de Cotação.'];
            } else if (new Date(formData.needByDateUtc).getTime() < new Date().setHours(0, 0, 0, 0)) {
                newErrors['NeedByDateUtc'] = ['A data Necessário Até não pode ser no passado.'];
            }
        }
        if (Object.keys(newErrors).length > 0) {
            setFieldErrors(newErrors);
            setFeedback({ type: 'error', message: 'Preencha todos os campos obrigatórios antes de continuar.' });
            // Scroll to first error and focus
            scrollToFirstError(newErrors);
            return;
        }

        setLoading(true);
        setFeedback({ type: 'error', message: null });
        setFieldErrors({});

        // Prepare payload parsing numbers and nulls properly
        const payload = {
            title: formData.title,
            description: formData.description,
            requestTypeId: Number(formData.requestTypeId),
            needLevelId: formData.needLevelId ? Number(formData.needLevelId) : null,
            estimatedTotalAmount: 0, // Always 0 on create
            currencyId: formData.currencyId ? Number(formData.currencyId) : null,
            departmentId: formData.departmentId ? Number(formData.departmentId) : null,
            companyId: formData.companyId ? Number(formData.companyId) : null,
            plantId: formData.plantId ? Number(formData.plantId) : null,
            buyerId: formData.buyerId || null,
            areaApproverId: formData.areaApproverId || null,
            finalApproverId: formData.finalApproverId || null,
            needByDateUtc: Number(formData.requestTypeId) === 1 && formData.needByDateUtc 
                ? new Date(formData.needByDateUtc).toISOString() 
                : null
        };

        try {
            const result = await api.requests.create(payload);

            // Step 2: Upload attachments if any
            if (attachments.length > 0) {
                await api.attachments.upload(result.id, attachments, 'SUPPORTING');
            }

            const isQuotation = Number(formData.requestTypeId) === 1;
            const fallbackSuccessMessage = isQuotation ? 'Pedido de Cotação criado com sucesso.' : 'Rascunho salvo com sucesso.';

            if (isQuotation) {
                const quotationSuccessMessage = 'Pedido de Cotação criado. Os itens serão inseridos na tela "Gestão de Cotações".';
                navigate(`/requests${location.state?.fromList || ''}`, {
                    replace: true,
                    state: { successMessage: quotationSuccessMessage }
                });
            } else {
                const isPayment = Number(formData.requestTypeId) === 2;
                const successMsg = isPayment ? 'Pedido de Pagamento gerado. Preencha os restantes dados e anexe a fatura/nota.' : fallbackSuccessMessage;

                navigate(`/requests/${result.id}/edit`, {
                    replace: true,
                    state: {
                        successMessage: successMsg,
                        fromList: location.state?.fromList
                    }
                });
            }
        } catch (err: any) {
            if (err instanceof ApiError && err.fieldErrors) {
                setFieldErrors(err.fieldErrors);
                setFeedback({ type: 'error', message: err.message || 'Existem campos preenchidos incorretamente.' });
                scrollToFirstError(err.fieldErrors);
            } else {
                setFeedback({ type: 'error', message: err.message || 'Erro ao criar o rascunho.' });
                window.scrollTo({ top: 0, behavior: 'smooth' });
            }
        } finally {
            setLoading(false);
        }
    };

    const inputStyle = {
        width: '100%',
        padding: '12px 14px',
        borderRadius: 'var(--radius-sm)',
        border: '1px solid var(--color-border-heavy)',
        boxShadow: 'var(--shadow-brutal)',
        fontSize: '0.875rem',
        fontFamily: 'var(--font-family-body)',
        color: 'var(--color-text-main)',
        backgroundColor: 'var(--color-bg-page)',
        marginTop: '8px',
        transition: 'all 0.2s ease',
        outline: 'none'
    };

    const labelStyle = {
        display: 'block',
        fontSize: '0.75rem',
        fontWeight: 600,
        textTransform: 'uppercase' as const,
        letterSpacing: '0.05em',
        color: 'var(--color-text-main)',
        marginBottom: '24px',
        position: 'relative' as const
    };

    const sectionTitleStyle = {
        fontSize: '1.1rem',
        fontWeight: 700,
        color: 'var(--color-primary)',
        borderBottom: '2px solid var(--color-border)',
        paddingBottom: '8px',
        marginBottom: '24px',
        marginTop: '16px',
        textTransform: 'uppercase' as const,
        letterSpacing: '0.05em'
    };

    // Helper for rendering inline error texts
    const getFieldErrors = (fieldName: string) => {
        if (!fieldErrors) return null;
        const normalizedField = fieldName.toLowerCase();
        const key = Object.keys(fieldErrors).find(k => {
            const normalizedKey = k.toLowerCase().replace(/^\$\./, '');
            return normalizedKey === normalizedField || normalizedKey.endsWith('.' + normalizedField);
        });
        return key ? fieldErrors[key] : null;
    };

    const renderFieldError = (fieldName: string) => {
        const errors = getFieldErrors(fieldName);
        if (!errors) return null;
        return (
            <div style={{ color: '#EF4444', fontSize: '0.75rem', marginTop: '4px', position: 'absolute' }}>
                {errors[0]}
            </div>
        );
    };

    const getInputStyle = (fieldName: string) => ({
        ...inputStyle,
        ...(getFieldErrors(fieldName) ? { borderColor: '#EF4444', backgroundColor: '#FEF2F2', boxShadow: '3px 3px 0px #EF4444' } : {})
    });

    const headerProps = {
        breadcrumbs: [
            { label: 'Dashboard', to: '/' },
            { label: 'Pedidos', to: `/requests${location.state?.fromList || ''}` },
            { label: isCopyMode ? 'Cópia de Pedido' : 'Novo Rascunho' }
        ] as BreadcrumbItem[],
        title: isCopyMode ? 'Cópia de Pedido' : 'Novo Rascunho',
        secondaryActions: (
            <button
                type="button"
                onClick={() => navigate(`/requests${location.state?.fromList || ''}`)}
                style={{
                    height: '36px', padding: '0 12px', borderRadius: 'var(--radius-sm)', border: '2px solid var(--color-border-heavy)',
                    backgroundColor: 'var(--color-bg-page)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px',
                    fontWeight: 800, fontFamily: 'var(--font-family-display)', fontSize: '0.75rem', color: 'var(--color-text-main)'
                }}
            >
                <X size={14} /> {isCopyMode ? 'DESCARTAR CÓPIA' : 'CANCELAR'}
            </button>
        ),
        primaryActions: (
            <button
                onClick={handleSubmit}
                disabled={loading || isTemplateLoading}
                className="btn-primary"
                style={{ height: '36px', padding: '0 16px', display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.75rem', borderRadius: 'var(--radius-sm)' }}
            >
                <Save size={14} /> 
                {loading ? 'GERANDO...' : (
                    Number(formData.requestTypeId) === 1 ? 'CRIAR PEDIDO' : 
                    Number(formData.requestTypeId) === 2 ? 'GERAR PEDIDO' : 
                    isCopyMode ? 'CRIAR RASCUNHO' : 'CRIAR RASCUNHO'
                )}
            </button>
        ),
        feedback,
        onCloseFeedback: () => setFeedback(prev => ({ ...prev, message: null }))
    };

    return (

        <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.4 }}
            style={{ display: 'flex', flexDirection: 'column', gap: '24px', width: '100%', maxWidth: '1440px', margin: '0 auto', minWidth: 0 }}
        >

            <RequestActionHeader {...headerProps} />

            {/* User Scope Guardrail */}
            {!isScopeLoading && allowedPlantCodes.length === 0 && (
                <div style={{
                    backgroundColor: '#FEF2F2',
                    border: '2px solid #EF4444',
                    padding: '24px',
                    borderRadius: 'var(--radius-sm)',
                    boxShadow: 'var(--shadow-brutal)',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '12px',
                    alignItems: 'center',
                    textAlign: 'center'
                }}>
                    <div style={{ color: '#EF4444', fontWeight: 800, fontSize: '1.25rem' }}>ACESSO RESTRITO</div>
                    <p style={{ color: 'var(--color-text-main)', fontSize: '0.875rem', maxWidth: '500px' }}>
                        O seu utilizador não possui nenhuma <strong>Planta</strong> atribuída ao seu âmbito de acesso. 
                        Por favor, contacte o Administrador do Sistema para configurar o seu perfil antes de tentar criar novos pedidos.
                    </p>
                    <button 
                        onClick={() => navigate('/')}
                        style={{
                            marginTop: '8px', padding: '8px 16px', backgroundColor: 'var(--color-text-main)', color: 'white',
                            border: 'none', borderRadius: 'var(--radius-sm)', fontWeight: 700, cursor: 'pointer'
                        }}
                    >
                        VOLTAR AO DASHBOARD
                    </button>
                </div>
            )}

            {/* Form Card */}
            <form 
                onSubmit={handleSubmit} 
                style={{
                    display: allowedPlantCodes.length === 0 ? 'none' : 'flex',
                    flexDirection: 'column',
                    gap: '32px',
                    opacity: isScopeLoading ? 0.5 : 1,
                    pointerEvents: isScopeLoading ? 'none' : 'auto'
                }}
            >

                {/* Section A: Dados Gerais do Pedido */}
                <section style={{ backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-brutal)', border: '2px solid var(--color-border-heavy)' }}>
                    <h2 style={sectionTitleStyle}>Dados Gerais do Pedido</h2>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
                        <label style={labelStyle}>
                            Título do Pedido <span style={{ color: 'red' }}>*</span>
                            <input
                                required
                                type="text"
                                name="title"
                                value={formData.title}
                                onChange={handleChange}
                                placeholder="Ex: Aquisição de Laptops para TI"
                                style={getInputStyle('Title')}
                            />
                            {renderFieldError('Title')}
                        </label>

                        <label style={labelStyle}>
                            Descrição ou Justificativa <span style={{ color: 'red' }}>*</span>
                            <textarea
                                required
                                name="description"
                                value={formData.description}
                                onChange={handleChange}
                                rows={4}
                                placeholder="Explique o motivo e os detalhes primários..."
                                style={{ ...getInputStyle('Description'), resize: 'vertical' }}
                            />
                            {isCopyMode && (
                                <motion.div
                                    initial={{ opacity: 0, y: -10 }}
                                    animate={{ opacity: 1, y: 0 }}
                                    style={{
                                        marginTop: '12px',
                                        padding: '12px 16px',
                                        backgroundColor: '#FFFBEB',
                                        border: '2px solid #F59E0B',
                                        borderRadius: 'var(--radius-sm)',
                                        display: 'flex',
                                        alignItems: 'flex-start',
                                        gap: '12px'
                                    }}
                                >
                                    <AlertTriangle size={18} style={{ color: '#D97706', flexShrink: 0, marginTop: '2px' }} />
                                    <div style={{ fontSize: '0.8rem', color: '#92400E', fontWeight: 500, lineHeight: '1.4' }}>
                                        <strong>Atenção:</strong> Esta descrição foi copiada do pedido original. 
                                        Revise o conteúdo antes de submeter para confirmar que ele ainda corresponde à necessidade atual.
                                    </div>
                                </motion.div>
                            )}
                            {renderFieldError('Description')}
                        </label>

                        {/* Integrated Attachment Area - DEC-073 */}
                        <div style={{ marginTop: '-8px', marginBottom: '8px' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '12px' }}>
                                <Paperclip size={16} style={{ color: 'var(--color-primary)' }} />
                                <span style={{ fontSize: '0.8rem', fontWeight: 700, color: 'var(--color-text-main)', textTransform: 'uppercase', letterSpacing: '0.025em' }}>
                                    Documentos de Apoio
                                </span>
                                <span style={{ fontSize: '0.75rem', color: 'var(--color-text-muted)', fontWeight: 400 }}>
                                    (Opcional)
                                </span>
                            </div>

                            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                                <div style={{ 
                                    border: '2px dashed var(--color-border)', 
                                    padding: '16px', 
                                    textAlign: 'center', 
                                    borderRadius: 'var(--radius-sm)',
                                    backgroundColor: 'rgba(255, 255, 255, 0.5)',
                                    cursor: 'pointer',
                                    position: 'relative',
                                    transition: 'all 0.2s ease',
                                    display: 'flex',
                                    flexDirection: 'column',
                                    alignItems: 'center',
                                    gap: '4px'
                                }}>
                                    <input 
                                        type="file" 
                                        multiple 
                                        onChange={handleFileChange}
                                        style={{
                                            position: 'absolute', top: 0, left: 0, width: '100%', height: '100%', opacity: 0, cursor: 'pointer'
                                        }}
                                    />
                                    <div style={{ fontWeight: 700, fontSize: '0.75rem', color: 'var(--color-text-main)' }}>ADICIONAR DOCUMENTOS</div>
                                    <div style={{ fontSize: '0.7rem', color: 'var(--color-text-muted)' }}>PDF, JPG, PNG, DOCX (Máx 5MB)</div>
                                </div>

                                {attachments.length > 0 && (
                                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: '8px' }}>
                                        {attachments.map((file, idx) => (
                                            <div key={idx} style={{ 
                                                display: 'flex', 
                                                justifyContent: 'space-between', 
                                                alignItems: 'center', 
                                                padding: '8px 12px', 
                                                backgroundColor: 'white', 
                                                border: '1px solid var(--color-border)', 
                                                borderRadius: 'var(--radius-sm)',
                                                boxShadow: '1px 1px 0px var(--color-border)'
                                            }}>
                                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                                                    <Paperclip size={14} style={{ flexShrink: 0 }} />
                                                    <span style={{ 
                                                        fontSize: '0.75rem', 
                                                        fontWeight: 600, 
                                                        whiteSpace: 'nowrap', 
                                                        overflow: 'hidden', 
                                                        textOverflow: 'ellipsis' 
                                                    }}>
                                                        {file.name}
                                                    </span>
                                                </div>
                                                <button 
                                                    type="button" 
                                                    onClick={() => removeFile(idx)}
                                                    style={{ color: '#EF4444', background: 'none', border: 'none', cursor: 'pointer', padding: '4px', flexShrink: 0 }}
                                                >
                                                    <Trash2 size={14} />
                                                </button>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>

                             <label style={labelStyle}>
                                 Tipo de Pedido <span style={{ color: 'red' }}>*</span>
                                 <select
                                     name="requestTypeId"
                                     value={formData.requestTypeId}
                                     onChange={handleChange}
                                     style={getInputStyle('RequestTypeId')}
                                 >
                                     <option value="">-- Selecione --</option>
                                     {requestTypes.filter(rt => rt.isActive).map(rt => (
                                         <option key={rt.id} value={rt.id}>{rt.name}</option>
                                     ))}
                                 </select>
                                 {renderFieldError('RequestTypeId')}
                             </label>

                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '24px' }}>
                            <label style={labelStyle}>
                                Grau de Necessidade <span style={{ color: 'red' }}>*</span>
                                <select name="needLevelId" value={formData.needLevelId} onChange={handleChange} style={getInputStyle('NeedLevelId')}>
                                    <option value="">-- Selecione --</option>
                                    {needLevels.filter(nl => nl.isActive).map(nl => (
                                        <option key={nl.id} value={nl.id}>{nl.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('NeedLevelId')}
                            </label>

                            <AnimatePresence>
                                {Number(formData.requestTypeId) === 1 && (
                                    <motion.div
                                        initial={{ opacity: 0, height: 0 }}
                                        animate={{ opacity: 1, height: 'auto' }}
                                        exit={{ opacity: 0, height: 0 }}
                                        style={{ overflow: 'hidden' }}
                                    >
                                        <label style={labelStyle}>
                                            Necessário até (Data limite) <span style={{ color: 'red' }}>*</span>
                                            <DateInput
                                                required
                                                name="needByDateUtc"
                                                value={formData.needByDateUtc}
                                                onChange={(val) => {
                                                    setFormData(prev => ({ ...prev, needByDateUtc: val }));
                                                    clearFieldError('NeedByDateUtc');
                                                }}
                                                hasError={!!getFieldErrors('NeedByDateUtc')}
                                                style={getInputStyle('NeedByDateUtc')}
                                            />
                                            {renderFieldError('NeedByDateUtc')}
                                        </label>
                                    </motion.div>
                                )}
                            </AnimatePresence>

                            <label style={labelStyle}>
                                Departamento <span style={{ color: 'red' }}>*</span>
                                <select name="departmentId" value={formData.departmentId} onChange={handleChange} style={getInputStyle('DepartmentId')}>
                                    <option value="">-- Selecione --</option>
                                    {departments.filter(d => d.isActive).map(d => (
                                        <option key={d.id} value={d.id}>{d.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('DepartmentId')}
                            </label>

                             <label style={labelStyle}>
                                Empresa <span style={{ color: 'red' }}>*</span>
                                <select 
                                    name="companyId" 
                                    value={formData.companyId} 
                                    onChange={handleChange} 
                                    style={getInputStyle('CompanyId')}
                                    disabled={filteredCompanies.length <= 1}
                                >
                                    <option value="">-- Selecione --</option>
                                    {filteredCompanies.map(c => (
                                        <option key={c.id} value={c.id}>{c.name}</option>
                                    ))}
                                </select>
                                {renderFieldError('CompanyId')}
                            </label>

                            <label style={labelStyle}>
                                Planta <span style={{ color: 'red' }}>*</span>
                                <select 
                                    name="plantId" 
                                    value={formData.plantId} 
                                    onChange={handleChange} 
                                    style={getInputStyle('PlantId')}
                                    disabled={filteredPlants.length <= 1 || (!!formData.companyId && filteredPlants.filter(p => Number(formData.companyId) === p.companyId).length <= 1)}
                                >
                                    <option value="">-- Selecione --</option>
                                    {filteredPlants
                                        .filter(p => p.isActive && (!formData.companyId || p.companyId === Number(formData.companyId)))
                                        .map(p => (
                                            <option key={p.id} value={p.id}>{p.name}</option>
                                        ))
                                    }
                                </select>
                                {renderFieldError('PlantId')}
                            </label>
                        </div>
                    </div>
                </section>

                {/* Section B: Participantes do Fluxo */}
                <section style={{ backgroundColor: 'var(--color-bg-surface)', padding: '32px', borderRadius: 'var(--radius-md)', boxShadow: 'var(--shadow-brutal)', border: '2px solid var(--color-border-heavy)' }}>
                    <h2 style={sectionTitleStyle}>Participantes do Fluxo</h2>
                    <p style={{ fontSize: '0.875rem', color: 'var(--color-text-muted)', marginBottom: '24px' }}>
                        Nota: O Solicitante será preenchido automaticamente com o seu usuário logado atual.
                    </p>

                    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: '20px' }}>
                        <label style={labelStyle}>
                            Comprador Atribuído <span style={{ color: 'red' }}>*</span>
                            <select name="buyerId" value={formData.buyerId} onChange={handleChange} style={getInputStyle('BuyerId')}>
                                <option value="">-- Selecione --</option>
                                {buyers.map(u => (
                                    <option key={u.id} value={u.id}>{u.fullName}</option>
                                ))}
                            </select>
                            {renderFieldError('BuyerId')}
                        </label>

                        <label style={labelStyle}>
                            Aprovador de Área <span style={{ color: 'red' }}>*</span>
                            <select name="areaApproverId" value={formData.areaApproverId} onChange={handleChange} style={getInputStyle('AreaApproverId')}>
                                <option value="">-- Selecione --</option>
                                {areaApprovers.map(u => (
                                    <option key={u.id} value={u.id}>{u.fullName}</option>
                                ))}
                            </select>
                            {renderFieldError('AreaApproverId')}
                        </label>

                        <label style={labelStyle}>
                            Aprovador Final <span style={{ color: 'red' }}>*</span>
                            <select name="finalApproverId" value={formData.finalApproverId} onChange={handleChange} style={getInputStyle('FinalApproverId')}>
                                <option value="">-- Selecione --</option>
                                {finalApprovers.map(u => (
                                    <option key={u.id} value={u.id}>{u.fullName}</option>
                                ))}
                            </select>
                            {renderFieldError('FinalApproverId')}
                        </label>
                    </div>
                </section>


            </form>
        </motion.div >
    );
}
