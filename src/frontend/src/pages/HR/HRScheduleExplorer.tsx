import { useState, useEffect, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import {
    Clock, Users, CalendarDays, Info, ExternalLink,
    Layers, AlertTriangle, ChevronRight, Shield, Factory
} from 'lucide-react';
import { api } from '../../lib/api';
import './hr-schedule-explorer.css';

// ─── Types ───

interface PlanCycleDay {
    dayIndex: number;
    scheduleId: number;
    scheduleCode: string;
    scheduleDescription: string;
    scheduleSigla: string | null;
    isRestDay: boolean;
    normalHours: string | null;
}

interface WorkPlan {
    id: number;
    code: string;
    description: string;
    type: string;
    cycleDays: number;
    restScheduleDescription: string | null;
    restScheduleSigla: string | null;
    entityId: number;
    entityName: string | null;
    employeeCount: number;
    cycleDays_Detail: PlanCycleDay[] | null;
}

interface SchedulePeriod {
    type: string;
    startTime: string;
    endTime: string;
    toleranceEntryMinutes: number;
    toleranceExitMinutes: number;
    roundingMinutes: number;
    workCodeDescription: string | null;
}

interface Schedule {
    id: number;
    code: string;
    description: string;
    sigla: string | null;
    normalHours: string | null;
    isRestDay: boolean;
    isScheduleAsRestDay: boolean;
    expectedPunches: number;
    minPunches: number;
    maxDailyToleranceMinutes: number | null;
    entityId: number;
    entityName: string | null;
    periods: SchedulePeriod[];
}

interface PlanEmployee {
    innuxEmployeeId: number;
    fullName: string;
    departmentName: string | null;
    plantName: string | null;
}

interface EmployeeResponse {
    employees: PlanEmployee[];
    totalInPlan: number;
    visibleCount: number;
    scopeType: string;
}

type ActiveTab = 'plans' | 'schedules';
type EntityFilter = 'all' | 1 | 6;
type SelectedItem = { type: 'plan'; data: WorkPlan } | { type: 'schedule'; data: Schedule } | null;

// ─── Helpers ───

const ENTITY_NAMES: Record<number, string> = { 1: 'AlplaPLASTICO', 6: 'AlplaSOPRO' };

function getEntityBadgeClass(entityId: number): string {
    if (entityId === 1) return 'sched-badge--entity-plastico';
    if (entityId === 6) return 'sched-badge--entity-sopro';
    return 'sched-badge--type';
}

function getEntityAccentClass(entityId: number): string {
    if (entityId === 1) return 'schedule-explorer__entity-accent--plastico';
    if (entityId === 6) return 'schedule-explorer__entity-accent--sopro';
    return '';
}

// ─── Day Labels ───

const DAY_LABELS: Record<number, string> = {
    0: 'Segunda', 1: 'Terça', 2: 'Quarta',
    3: 'Quinta', 4: 'Sexta', 5: 'Sábado', 6: 'Domingo'
};

function getDayLabel(dayIndex: number, cycleDays: number): string {
    if (cycleDays === 7) return DAY_LABELS[dayIndex] ?? `Dia ${dayIndex + 1}`;
    return `Dia ${dayIndex + 1}`;
}

// ─── Component ───

export default function HRScheduleExplorer() {
    const navigate = useNavigate();

    // Data state
    const [plans, setPlans] = useState<WorkPlan[]>([]);
    const [schedules, setSchedules] = useState<Schedule[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    // Selection state
    const [activeTab, setActiveTab] = useState<ActiveTab>('plans');
    const [selected, setSelected] = useState<SelectedItem>(null);
    const [activeEntity, setActiveEntity] = useState<EntityFilter>(1);

    // Employee drill-down
    const [employeeData, setEmployeeData] = useState<EmployeeResponse | null>(null);
    const [employeesLoading, setEmployeesLoading] = useState(false);
    const [employeeSearch, setEmployeeSearch] = useState('');

    // ─── Load data ───

    useEffect(() => {
        loadData();
    }, []);

    const loadData = async () => {
        setLoading(true);
        setError(null);
        try {
            const [plansData, schedulesData] = await Promise.all([
                api.hrSchedules.getPlans(),
                api.hrSchedules.getSchedules()
            ]);
            setPlans(plansData);
            setSchedules(schedulesData);
        } catch (err: any) {
            setError(err?.message || 'Falha ao carregar dados de escalas.');
        } finally {
            setLoading(false);
        }
    };

    // ─── Entity-filtered data ───

    const filteredPlans = useMemo(() => {
        if (activeEntity === 'all') return plans;
        return plans.filter(p => p.entityId === activeEntity);
    }, [plans, activeEntity]);

    const filteredSchedules = useMemo(() => {
        if (activeEntity === 'all') return schedules;
        return schedules.filter(s => s.entityId === activeEntity);
    }, [schedules, activeEntity]);

    // Codes that exist in both entities (for duplicate warnings)
    const duplicatePlanCodes = useMemo(() => {
        const byCode = new Map<string, Set<number>>();
        plans.forEach(p => {
            if (!p.code) return;
            if (!byCode.has(p.code)) byCode.set(p.code, new Set());
            byCode.get(p.code)!.add(p.entityId);
        });
        const dupes = new Set<string>();
        byCode.forEach((entities, code) => { if (entities.size > 1) dupes.add(code); });
        return dupes;
    }, [plans]);

    const duplicateScheduleCodes = useMemo(() => {
        const byCode = new Map<string, Set<number>>();
        schedules.forEach(s => {
            if (!s.code) return;
            if (!byCode.has(s.code)) byCode.set(s.code, new Set());
            byCode.get(s.code)!.add(s.entityId);
        });
        const dupes = new Set<string>();
        byCode.forEach((entities, code) => { if (entities.size > 1) dupes.add(code); });
        return dupes;
    }, [schedules]);

    // Entity counts for tab badges
    const entityPlanCounts = useMemo(() => ({
        1: plans.filter(p => p.entityId === 1).length,
        6: plans.filter(p => p.entityId === 6).length,
        all: plans.length
    }), [plans]);

    const entityScheduleCounts = useMemo(() => ({
        1: schedules.filter(s => s.entityId === 1).length,
        6: schedules.filter(s => s.entityId === 6).length,
        all: schedules.length
    }), [schedules]);

    // ─── Load employees when plan selected ───

    useEffect(() => {
        if (selected?.type === 'plan') {
            loadPlanEmployees(selected.data.id);
        } else {
            setEmployeeData(null);
            setEmployeeSearch('');
        }
    }, [selected?.type === 'plan' ? selected.data.id : null]);

    const loadPlanEmployees = async (planId: number) => {
        setEmployeesLoading(true);
        try {
            const data = await api.hrSchedules.getPlanEmployees(planId);
            setEmployeeData(data);
        } catch {
            setEmployeeData(null);
        } finally {
            setEmployeesLoading(false);
        }
    };

    // ─── Filtered employees ───

    const filteredEmployees = useMemo(() => {
        if (!employeeData?.employees) return [];
        if (!employeeSearch.trim()) return employeeData.employees;
        const term = employeeSearch.toLowerCase();
        return employeeData.employees.filter(e =>
            e.fullName.toLowerCase().includes(term) ||
            (e.departmentName && e.departmentName.toLowerCase().includes(term))
        );
    }, [employeeData, employeeSearch]);

    // ─── Handlers ───

    const handleEntityChange = (entity: EntityFilter) => {
        setActiveEntity(entity);
        setSelected(null); // clear selection on entity switch
    };

    const handleSelectPlan = (plan: WorkPlan) => {
        setSelected({ type: 'plan', data: plan });
    };

    const handleSelectSchedule = (schedule: Schedule) => {
        setSelected({ type: 'schedule', data: schedule });
    };

    const handleNavigateToAttendance = () => {
        navigate('/hr/attendance');
    };

    // ─── Render ───

    if (loading) {
        return (
            <div className="schedule-explorer__loading">
                <Clock size={18} style={{ marginRight: 8, animation: 'spin 1.5s linear infinite' }} />
                A carregar escalas e horários...
            </div>
        );
    }

    if (error) {
        return (
            <div className="schedule-explorer__loading" style={{ color: 'var(--color-danger, #dc2626)' }}>
                <AlertTriangle size={18} style={{ marginRight: 8 }} />
                {error}
            </div>
        );
    }

    const showEntityBadge = activeEntity === 'all';
    const entityCounts = activeTab === 'plans' ? entityPlanCounts : entityScheduleCounts;

    return (
        <div>
            {/* Hierarchy Explainer */}
            <div className="schedule-explorer__explainer">
                <Info size={18} className="schedule-explorer__explainer-icon" />
                <div>
                    <strong>Plano de Trabalho</strong> define o ciclo semanal ou escala de rotação de um funcionário.
                    Cada dia do ciclo aponta para um <strong>Horário</strong>, que define as janelas de trabalho obrigatórias,
                    tolerâncias e número de marcações esperado.
                    Planos do tipo <strong>Escala</strong> usam rotação dinâmica (turnos alternados), enquanto
                    planos <strong>Padrão</strong> seguem um ciclo semanal fixo.
                    Os registos estão separados por entidade Innux — <strong>AlplaPLASTICO</strong> e <strong>AlplaSOPRO</strong>.
                </div>
            </div>

            {/* Entity Tab Bar */}
            <div className="schedule-explorer__entity-bar">
                {([1, 6, 'all'] as EntityFilter[]).map(entity => {
                    const label = entity === 'all' ? 'Todas as Entidades' : ENTITY_NAMES[entity] || `Entidade ${entity}`;
                    const count = entityCounts[entity as keyof typeof entityCounts];
                    return (
                        <button
                            key={String(entity)}
                            className={`schedule-explorer__entity-tab ${activeEntity === entity ? 'active' : ''}`}
                            data-entity={String(entity)}
                            onClick={() => handleEntityChange(entity)}
                        >
                            <Factory size={14} />
                            {label}
                            <span className="schedule-explorer__entity-tab-count">{count}</span>
                        </button>
                    );
                })}
            </div>

            <div className="schedule-explorer">
                {/* ─── LEFT PANEL ─── */}
                <div className="schedule-explorer__sidebar">
                    <div className="schedule-explorer__sidebar-tabs">
                        <button
                            className={`schedule-explorer__sidebar-tab ${activeTab === 'plans' ? 'active' : ''}`}
                            onClick={() => { setActiveTab('plans'); setSelected(null); }}
                        >
                            <Layers size={14} style={{ marginRight: 4 }} />
                            Planos ({filteredPlans.length})
                        </button>
                        <button
                            className={`schedule-explorer__sidebar-tab ${activeTab === 'schedules' ? 'active' : ''}`}
                            onClick={() => { setActiveTab('schedules'); setSelected(null); }}
                        >
                            <Clock size={14} style={{ marginRight: 4 }} />
                            Horários ({filteredSchedules.length})
                        </button>
                    </div>

                    <div className="schedule-explorer__list">
                        {activeTab === 'plans' ? (
                            filteredPlans.length === 0 ? (
                                <div style={{ padding: 20, textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>
                                    Nenhum plano nesta entidade.
                                </div>
                            ) : filteredPlans.map(plan => (
                                <div
                                    key={plan.id}
                                    className={`schedule-explorer__item ${selected?.type === 'plan' && selected.data.id === plan.id ? 'selected' : ''}`}
                                    onClick={() => handleSelectPlan(plan)}
                                >
                                    <div className="schedule-explorer__item-header">
                                        <span className="schedule-explorer__item-code">{plan.code || `#${plan.id}`}</span>
                                        {duplicatePlanCodes.has(plan.code) && (
                                            <span className="sched-badge sched-badge--duplicate" title="Este código existe em ambas as entidades">
                                                <AlertTriangle size={10} />2 entid.
                                            </span>
                                        )}
                                        <ChevronRight size={14} style={{ color: 'var(--color-text-muted)', marginLeft: 'auto' }} />
                                    </div>
                                    <span className="schedule-explorer__item-desc">{plan.description}</span>
                                    <div className="schedule-explorer__item-meta">
                                        {showEntityBadge && (
                                            <span className={`sched-badge ${getEntityBadgeClass(plan.entityId)}`}>
                                                {plan.entityName}
                                            </span>
                                        )}
                                        <span className={`sched-badge ${plan.type === 'Escala' ? 'sched-badge--escala' : 'sched-badge--type'}`}>
                                            {plan.type}
                                        </span>
                                        {plan.cycleDays > 0 && (
                                            <span className="sched-badge sched-badge--type">{plan.cycleDays}d</span>
                                        )}
                                        {plan.type === 'Escala' && (!plan.cycleDays_Detail || plan.cycleDays_Detail.length === 0) && (
                                            <span className="sched-badge sched-badge--dynamic">Rotação dinâmica</span>
                                        )}
                                        <span className="sched-badge sched-badge--employees">
                                            <Users size={11} />{plan.employeeCount}
                                        </span>
                                        {plan.employeeCount === 0 && (
                                            <span className="sched-badge sched-badge--warning">
                                                <AlertTriangle size={11} />Sem funcionários
                                            </span>
                                        )}
                                    </div>
                                </div>
                            ))
                        ) : (
                            filteredSchedules.length === 0 ? (
                                <div style={{ padding: 20, textAlign: 'center', color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>
                                    Nenhum horário nesta entidade.
                                </div>
                            ) : filteredSchedules.map(schedule => (
                                <div
                                    key={schedule.id}
                                    className={`schedule-explorer__item ${selected?.type === 'schedule' && selected.data.id === schedule.id ? 'selected' : ''}`}
                                    onClick={() => handleSelectSchedule(schedule)}
                                >
                                    <div className="schedule-explorer__item-header">
                                        <span className="schedule-explorer__item-code">{schedule.code || `#${schedule.id}`}</span>
                                        {schedule.sigla && (
                                            <span className="sched-badge sched-badge--sigla">{schedule.sigla}</span>
                                        )}
                                        {duplicateScheduleCodes.has(schedule.code) && (
                                            <span className="sched-badge sched-badge--duplicate" title="Este código existe em ambas as entidades">
                                                <AlertTriangle size={10} />2 entid.
                                            </span>
                                        )}
                                        <ChevronRight size={14} style={{ color: 'var(--color-text-muted)', marginLeft: 'auto' }} />
                                    </div>
                                    <span className="schedule-explorer__item-desc">{schedule.description}</span>
                                    <div className="schedule-explorer__item-meta">
                                        {showEntityBadge && (
                                            <span className={`sched-badge ${getEntityBadgeClass(schedule.entityId)}`}>
                                                {schedule.entityName}
                                            </span>
                                        )}
                                        {schedule.isRestDay ? (
                                            <span className="sched-badge sched-badge--rest">Folga</span>
                                        ) : (
                                            <>
                                                {schedule.normalHours && (
                                                    <span className="sched-badge sched-badge--type">{schedule.normalHours}</span>
                                                )}
                                                <span className="sched-badge sched-badge--type">
                                                    {schedule.expectedPunches} marc.
                                                </span>
                                            </>
                                        )}
                                    </div>
                                </div>
                            ))
                        )}
                    </div>
                </div>

                {/* ─── RIGHT PANEL ─── */}
                <div className="schedule-explorer__detail">
                    {!selected ? (
                        <div className="schedule-explorer__detail-empty">
                            <CalendarDays size={48} />
                            <p>Selecione um plano de trabalho ou horário para ver os detalhes.</p>
                        </div>
                    ) : selected.type === 'plan' ? (
                        <PlanDetail
                            plan={selected.data}
                            employeeData={employeeData}
                            employeesLoading={employeesLoading}
                            employeeSearch={employeeSearch}
                            onSearchChange={setEmployeeSearch}
                            filteredEmployees={filteredEmployees}
                            onNavigateAttendance={handleNavigateToAttendance}
                            isDuplicate={duplicatePlanCodes.has(selected.data.code)}
                        />
                    ) : (
                        <ScheduleDetail
                            schedule={selected.data}
                            plans={plans}
                            isDuplicate={duplicateScheduleCodes.has(selected.data.code)}
                        />
                    )}
                </div>
            </div>
        </div>
    );
}

// ─── Plan Detail Sub-component ───

function PlanDetail({
    plan, employeeData, employeesLoading, employeeSearch,
    onSearchChange, filteredEmployees, onNavigateAttendance, isDuplicate
}: {
    plan: WorkPlan;
    employeeData: EmployeeResponse | null;
    employeesLoading: boolean;
    employeeSearch: string;
    onSearchChange: (v: string) => void;
    filteredEmployees: PlanEmployee[];
    onNavigateAttendance: () => void;
    isDuplicate: boolean;
}) {
    return (
        <div>
            {/* Header */}
            <div className="schedule-explorer__detail-header">
                <div>
                    <h2 className="schedule-explorer__detail-title">{plan.code || `Plano #${plan.id}`}</h2>
                    <p className="schedule-explorer__detail-subtitle">{plan.description}</p>
                    <div style={{ marginTop: 6, display: 'flex', alignItems: 'center', gap: 8 }}>
                        <span className={`schedule-explorer__entity-accent ${getEntityAccentClass(plan.entityId)}`}>
                            <Factory size={12} />
                            {plan.entityName}
                        </span>
                        {isDuplicate && (
                            <span className="sched-badge sched-badge--duplicate">
                                <AlertTriangle size={10} />Existe em ambas as entidades
                            </span>
                        )}
                    </div>
                </div>
                <button
                    className="schedule-explorer__action-btn"
                    onClick={onNavigateAttendance}
                    title="Abrir calendário de presenças para investigar este plano"
                >
                    <ExternalLink size={14} />
                    Ver Presenças
                </button>
            </div>

            {/* Properties */}
            <div className="schedule-explorer__props">
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Tipo</span>
                    <span className="schedule-explorer__prop-value">
                        <span className={`sched-badge ${plan.type === 'Escala' ? 'sched-badge--escala' : 'sched-badge--type'}`}>
                            {plan.type}
                        </span>
                    </span>
                </div>
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Ciclo</span>
                    <span className="schedule-explorer__prop-value">
                        {plan.cycleDays > 0 ? `${plan.cycleDays} dias` : 'Dinâmico'}
                    </span>
                </div>
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Folga padrão</span>
                    <span className="schedule-explorer__prop-value">
                        {plan.restScheduleDescription ?? '—'}
                        {plan.restScheduleSigla && (
                            <span className="sched-badge sched-badge--sigla" style={{ marginLeft: 6 }}>{plan.restScheduleSigla}</span>
                        )}
                    </span>
                </div>
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Entidade</span>
                    <span className="schedule-explorer__prop-value">
                        <span className={`sched-badge ${getEntityBadgeClass(plan.entityId)}`}>
                            {plan.entityName ?? '—'}
                        </span>
                    </span>
                </div>
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Funcionários</span>
                    <span className="schedule-explorer__prop-value">
                        <span className="sched-badge sched-badge--employees">
                            <Users size={12} />{plan.employeeCount}
                        </span>
                    </span>
                </div>
            </div>

            {/* Cycle Composition */}
            <div className="schedule-explorer__section">
                <div className="schedule-explorer__section-title">
                    <CalendarDays size={15} />
                    Composição do Ciclo
                </div>

                {plan.type === 'Escala' && (!plan.cycleDays_Detail || plan.cycleDays_Detail.length === 0) ? (
                    <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', padding: '12px 0' }}>
                        <span className="sched-badge sched-badge--dynamic" style={{ marginRight: 8 }}>Rotação dinâmica</span>
                        Este plano usa rotação de escala. Os horários são atribuídos dinamicamente pelo sistema Innux,
                        não seguindo um ciclo semanal fixo.
                    </div>
                ) : plan.cycleDays_Detail && plan.cycleDays_Detail.length > 0 ? (
                    <table className="schedule-explorer__cycle-table">
                        <thead>
                            <tr>
                                <th>Dia</th>
                                <th>Horário</th>
                                <th>Sigla</th>
                                <th>Horas</th>
                                <th>Tipo</th>
                            </tr>
                        </thead>
                        <tbody>
                            {plan.cycleDays_Detail.map(day => (
                                <tr key={day.dayIndex} className={day.isRestDay ? 'rest-day' : ''}>
                                    <td style={{ fontWeight: 600 }}>{getDayLabel(day.dayIndex, plan.cycleDays)}</td>
                                    <td>{day.scheduleCode || day.scheduleDescription}</td>
                                    <td>
                                        {day.scheduleSigla ? (
                                            <span className="sched-badge sched-badge--sigla">{day.scheduleSigla}</span>
                                        ) : '—'}
                                    </td>
                                    <td>{day.normalHours ?? '—'}</td>
                                    <td>
                                        {day.isRestDay ? (
                                            <span className="sched-badge sched-badge--rest">Folga</span>
                                        ) : (
                                            <span style={{ fontSize: '0.72rem', color: 'var(--color-success)' }}>Trabalho</span>
                                        )}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                ) : (
                    <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', padding: '12px 0' }}>
                        Nenhum mapeamento de ciclo encontrado.
                    </div>
                )}
            </div>

            {/* Employees */}
            <div className="schedule-explorer__section">
                <div className="schedule-explorer__employees-header">
                    <div className="schedule-explorer__section-title" style={{ margin: 0 }}>
                        <Users size={15} />
                        Funcionários ({employeeData?.visibleCount ?? 0}
                        {employeeData && employeeData.totalInPlan !== employeeData.visibleCount && (
                            <span style={{ fontWeight: 400, textTransform: 'none', letterSpacing: 0 }}>
                                {' '}de {employeeData.totalInPlan}
                            </span>
                        )})
                    </div>
                    <input
                        type="text"
                        className="schedule-explorer__employees-search"
                        placeholder="Pesquisar funcionário..."
                        value={employeeSearch}
                        onChange={e => onSearchChange(e.target.value)}
                    />
                </div>

                {employeeData && employeeData.scopeType !== 'all' && (
                    <div className="schedule-explorer__scope-notice">
                        <Shield size={14} />
                        A mostrar apenas funcionários dentro do seu âmbito de visibilidade ({employeeData.scopeType}).
                    </div>
                )}

                {employeesLoading ? (
                    <div className="schedule-explorer__loading">A carregar funcionários...</div>
                ) : filteredEmployees.length === 0 ? (
                    <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)', padding: '12px 0' }}>
                        {employeeSearch ? 'Nenhum resultado encontrado.' : 'Nenhum funcionário visível neste plano.'}
                    </div>
                ) : (
                    <div>
                        {filteredEmployees.map(emp => (
                            <div key={emp.innuxEmployeeId} className="schedule-explorer__employee-row">
                                <span className="schedule-explorer__employee-name">{emp.fullName}</span>
                                <span className="schedule-explorer__employee-dept">{emp.departmentName ?? '—'}</span>
                                {emp.plantName && (
                                    <span className="schedule-explorer__employee-plant">{emp.plantName}</span>
                                )}
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}

// ─── Schedule Detail Sub-component ───

function ScheduleDetail({ schedule, plans, isDuplicate }: { schedule: Schedule; plans: WorkPlan[]; isDuplicate: boolean }) {
    // Find which plans use this schedule
    const usingPlans = useMemo(() => {
        return plans.filter(p =>
            p.cycleDays_Detail?.some(d => d.scheduleId === schedule.id)
        );
    }, [schedule.id, plans]);

    return (
        <div>
            {/* Header */}
            <div className="schedule-explorer__detail-header">
                <div>
                    <h2 className="schedule-explorer__detail-title">
                        {schedule.code || `Horário #${schedule.id}`}
                        {schedule.sigla && (
                            <span className="sched-badge sched-badge--sigla" style={{ marginLeft: 10 }}>{schedule.sigla}</span>
                        )}
                    </h2>
                    <p className="schedule-explorer__detail-subtitle">{schedule.description}</p>
                    <div style={{ marginTop: 6, display: 'flex', alignItems: 'center', gap: 8 }}>
                        <span className={`schedule-explorer__entity-accent ${getEntityAccentClass(schedule.entityId)}`}>
                            <Factory size={12} />
                            {schedule.entityName}
                        </span>
                        {isDuplicate && (
                            <span className="sched-badge sched-badge--duplicate">
                                <AlertTriangle size={10} />Existe em ambas as entidades
                            </span>
                        )}
                    </div>
                </div>
            </div>

            {/* Properties */}
            <div className="schedule-explorer__props">
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Tipo</span>
                    <span className="schedule-explorer__prop-value">
                        {schedule.isRestDay ? (
                            <span className="sched-badge sched-badge--rest">Folga / Descanso</span>
                        ) : (
                            <span style={{ color: 'var(--color-success)' }}>Trabalho</span>
                        )}
                    </span>
                </div>
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Horas normais</span>
                    <span className="schedule-explorer__prop-value">{schedule.normalHours ?? '—'}</span>
                </div>
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Marcações esperadas</span>
                    <span className="schedule-explorer__prop-value">{schedule.expectedPunches}</span>
                </div>
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Marcações mínimas</span>
                    <span className="schedule-explorer__prop-value">{schedule.minPunches}</span>
                </div>
                {schedule.maxDailyToleranceMinutes != null && (
                    <div className="schedule-explorer__prop">
                        <span className="schedule-explorer__prop-label">Tolerância diária máx.</span>
                        <span className="schedule-explorer__prop-value">{schedule.maxDailyToleranceMinutes} min</span>
                    </div>
                )}
                <div className="schedule-explorer__prop">
                    <span className="schedule-explorer__prop-label">Entidade</span>
                    <span className="schedule-explorer__prop-value">
                        <span className={`sched-badge ${getEntityBadgeClass(schedule.entityId)}`}>
                            {schedule.entityName ?? '—'}
                        </span>
                    </span>
                </div>
            </div>

            {/* Periods */}
            {!schedule.isRestDay && schedule.periods.length > 0 && (
                <div className="schedule-explorer__section">
                    <div className="schedule-explorer__section-title">
                        <Clock size={15} />
                        Períodos de Trabalho
                    </div>
                    <table className="schedule-explorer__periods-table">
                        <thead>
                            <tr>
                                <th>Tipo</th>
                                <th>Início</th>
                                <th>Fim</th>
                                <th>Tol. Entrada</th>
                                <th>Tol. Saída</th>
                                <th>Arredond.</th>
                                <th>Código Trabalho</th>
                            </tr>
                        </thead>
                        <tbody>
                            {schedule.periods.map((period, idx) => (
                                <tr key={idx}>
                                    <td>
                                        <span className={`sched-badge ${period.type === 'Obrigatório' ? 'sched-badge--type' : 'sched-badge--rest'}`}>
                                            {period.type}
                                        </span>
                                    </td>
                                    <td style={{ fontWeight: 600 }}>{period.startTime}</td>
                                    <td style={{ fontWeight: 600 }}>{period.endTime}</td>
                                    <td>{period.toleranceEntryMinutes > 0 ? `${period.toleranceEntryMinutes} min` : '—'}</td>
                                    <td>{period.toleranceExitMinutes > 0 ? `${period.toleranceExitMinutes} min` : '—'}</td>
                                    <td>{period.roundingMinutes > 0 ? `${period.roundingMinutes} min` : '—'}</td>
                                    <td>{period.workCodeDescription ?? '—'}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {/* Which plans use this schedule */}
            <div className="schedule-explorer__section">
                <div className="schedule-explorer__section-title">
                    <Layers size={15} />
                    Planos que utilizam este horário
                </div>
                {usingPlans.length > 0 ? (
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                        {usingPlans.map(p => (
                            <span key={p.id} className="sched-badge sched-badge--type" style={{ padding: '4px 10px', fontSize: '0.75rem' }}>
                                {p.code || p.description}
                                <span style={{ opacity: 0.6, marginLeft: 4 }}>({p.employeeCount} func.)</span>
                            </span>
                        ))}
                    </div>
                ) : (
                    <div style={{ fontSize: '0.78rem', color: 'var(--color-text-muted)' }}>
                        {schedule.isRestDay
                            ? 'Este horário é usado como folga padrão em planos de trabalho.'
                            : 'Nenhum plano referencia directamente este horário no ciclo estático.'}
                    </div>
                )}
            </div>
        </div>
    );
}
