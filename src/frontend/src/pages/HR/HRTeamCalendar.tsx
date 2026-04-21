import { useEffect, useState, useRef, useCallback } from 'react';
import { api } from '../../lib/api';
import { RefreshCw, ChevronLeft, ChevronRight, CheckCircle, Calendar, LayoutGrid } from 'lucide-react';
import './hr-team-calendar.css';

// ─── Types ───

type ViewMode = 'month' | 'week';

interface CalendarEmployee {
    id: string;
    employeeCode: string;
    fullName: string;
    innuxDepartmentName?: string;
    portalDepartmentName?: string;
}

interface CalendarRecord {
    id: string;
    employeeId: string;
    employeeName: string;
    leaveType: string;
    leaveTypeCode: string;
    startDate: string;
    endDate: string;
    statusCode: string;
}

interface CalendarData {
    employees: CalendarEmployee[];
    records: CalendarRecord[];
    from: string;
    to: string;
    /** Access scope: 'all' | 'hr' | 'department' | 'self' */
    scopeType: string;
}

// ─── Date utilities ───

/** ISO 8601 week number */
function getISOWeek(date: Date): number {
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    d.setUTCDate(d.getUTCDate() + 4 - (d.getUTCDay() || 7));
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
    return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7);
}

function addDays(date: Date, n: number): Date {
    const d = new Date(date);
    d.setDate(d.getDate() + n);
    return d;
}

/** Monday of the ISO week containing `date` */
function getMonday(date: Date): Date {
    const d = new Date(date);
    const day = d.getDay();
    const diff = d.getDate() - day + (day === 0 ? -6 : 1);
    d.setDate(diff);
    return d;
}

function isSameDay(a: Date, b: Date): boolean {
    return a.getDate() === b.getDate() &&
           a.getMonth() === b.getMonth() &&
           a.getFullYear() === b.getFullYear();
}

function isWeekendDay(date: Date): boolean {
    return date.getDay() === 0 || date.getDay() === 6;
}

function formatQueryDate(d: Date): string {
    return d.toISOString().split('T')[0];
}

// ─── Component ───

export default function HRTeamCalendar() {
    const [data, setData] = useState<CalendarData | null>(null);
    const [loading, setLoading] = useState(true);
    const [viewMode, setViewMode] = useState<ViewMode>('month');
    const [currentDate, setCurrentDate] = useState(new Date());
    const [isScrolled, setIsScrolled] = useState(false);
    const [showScrollHint, setShowScrollHint] = useState(false);

    const scrollRef = useRef<HTMLDivElement>(null);
    const today = new Date();

    // ─── Date range computation ───

    const getDateRange = useCallback((): { from: Date; to: Date } => {
        if (viewMode === 'week') {
            const monday = getMonday(currentDate);
            const sunday = addDays(monday, 6);
            return { from: monday, to: sunday };
        }
        // month
        const year = currentDate.getFullYear();
        const month = currentDate.getMonth();
        return {
            from: new Date(year, month, 1),
            to: new Date(year, month + 1, 0),
        };
    }, [currentDate, viewMode]);

    const getDaysInView = useCallback((): Date[] => {
        const { from, to } = getDateRange();
        const days: Date[] = [];
        const d = new Date(from);
        while (d <= to) {
            days.push(new Date(d));
            d.setDate(d.getDate() + 1);
        }
        return days;
    }, [getDateRange]);

    // ─── Data loading ───

    useEffect(() => {
        setLoading(true);
        const { from, to } = getDateRange();
        api.hrLeave
            .getCalendar({ from: formatQueryDate(from), to: formatQueryDate(to) })
            .then((res: CalendarData) => {
                setData(res);
                setLoading(false);
            })
            .catch((err: unknown) => {
                console.error(err);
                setLoading(false);
            });
    }, [currentDate, viewMode, getDateRange]);

    // ─── Scroll tracking ───

    useEffect(() => {
        const el = scrollRef.current;
        if (!el) return;

        const handleScroll = () => {
            setIsScrolled(el.scrollLeft > 2);
            setShowScrollHint(el.scrollWidth > el.clientWidth && (el.scrollLeft + el.clientWidth) < el.scrollWidth - 4);
        };
        handleScroll(); // initial check
        el.addEventListener('scroll', handleScroll, { passive: true });
        const obs = new ResizeObserver(handleScroll);
        obs.observe(el);
        return () => {
            el.removeEventListener('scroll', handleScroll);
            obs.disconnect();
        };
    }, [data]);

    // ─── Navigation ───

    const navPrev = () => {
        if (viewMode === 'week') {
            setCurrentDate(addDays(currentDate, -7));
        } else {
            setCurrentDate(new Date(currentDate.getFullYear(), currentDate.getMonth() - 1, 1));
        }
    };

    const navNext = () => {
        if (viewMode === 'week') {
            setCurrentDate(addDays(currentDate, 7));
        } else {
            setCurrentDate(new Date(currentDate.getFullYear(), currentDate.getMonth() + 1, 1));
        }
    };

    const navToday = () => setCurrentDate(new Date());

    // ─── Labels ───

    const getNavLabel = (): string => {
        if (viewMode === 'week') {
            const { from, to } = getDateRange();
            const fmtD = (d: Date) => d.toLocaleDateString('pt-AO', { day: 'numeric', month: 'short' });
            return `${fmtD(from)} – ${fmtD(to)} ${to.getFullYear()}`;
        }
        return currentDate.toLocaleString('pt-AO', { month: 'long', year: 'numeric' });
    };

    const getWeekBadge = (): string | null => {
        if (viewMode !== 'week') return null;
        return `Sem. ${getISOWeek(currentDate)}`;
    };

    const getScopeHeader = (): { title: string; subtitle: string } => {
        const scope = data?.scopeType || 'self';
        if (scope === 'self') {
            return {
                title: 'Meu Calendário',
                subtitle: 'A sua disponibilidade pessoal.'
            };
        }
        return {
            title: 'Calendário da Equipa',
            subtitle: 'Visão de bloqueio operacional por departamento.'
        };
    };

    const getScopeDescription = (): string => {
        const scope = data?.scopeType || 'self';
        switch (scope) {
            case 'all': return 'Visão completa — todos os funcionários.';
            case 'hr': return 'Escopo R.H. — funcionários mapeados ao seu departamento/planta.';
            case 'department': return 'Escopo departamental — funcionários do seu departamento.';
            case 'self': return 'Visão pessoal — o seu calendário individual.';
            default: return '';
        }
    };

    // ─── Record matching ───

    const getRecordForDay = (employeeRecords: CalendarRecord[], day: Date): CalendarRecord | undefined => {
        const dayStart = new Date(day);
        dayStart.setHours(0, 0, 0, 0);
        return employeeRecords.find(r => {
            const rStart = new Date(r.startDate);
            rStart.setHours(0, 0, 0, 0);
            const rEnd = new Date(r.endDate);
            rEnd.setHours(23, 59, 59, 999);
            return dayStart >= rStart && dayStart <= rEnd;
        });
    };

    const getBlockBorderRadius = (record: CalendarRecord, day: Date): string => {
        const dayStart = new Date(day); dayStart.setHours(0, 0, 0, 0);
        const rStart = new Date(record.startDate); rStart.setHours(0, 0, 0, 0);
        const rEnd = new Date(record.endDate); rEnd.setHours(0, 0, 0, 0);
        const isFirst = isSameDay(dayStart, rStart);
        const isLast = isSameDay(dayStart, rEnd);
        return `${isFirst ? '6px' : '0'} ${isLast ? '6px' : '0'} ${isLast ? '6px' : '0'} ${isFirst ? '6px' : '0'}`;
    };

    const isFirstDay = (record: CalendarRecord, day: Date): boolean => {
        const dayStart = new Date(day); dayStart.setHours(0, 0, 0, 0);
        const rStart = new Date(record.startDate); rStart.setHours(0, 0, 0, 0);
        return isSameDay(dayStart, rStart);
    };

    // ─── Derived values ───

    const days = getDaysInView();
    const scope = getScopeHeader();
    const weekBadge = getWeekBadge();
    const dayColWidth = viewMode === 'week' ? 80 : 40;

    // ─── Render ───

    return (
        <div className="cal-root">
            {/* ── Header ── */}
            <div className="cal-header">
                <div>
                    <h2 className="cal-header__title">{scope.title}</h2>
                    <p className="cal-header__subtitle">{scope.subtitle}</p>
                </div>

                <div className="cal-controls">
                    {/* View toggle */}
                    <div className="cal-view-toggle">
                        <button
                            className={`cal-view-toggle__btn ${viewMode === 'month' ? 'cal-view-toggle__btn--active' : ''}`}
                            onClick={() => setViewMode('month')}
                        >
                            <Calendar size={14} /> Mês
                        </button>
                        <button
                            className={`cal-view-toggle__btn ${viewMode === 'week' ? 'cal-view-toggle__btn--active' : ''}`}
                            onClick={() => setViewMode('week')}
                        >
                            <LayoutGrid size={14} /> Semana
                        </button>
                    </div>

                    {/* Navigation */}
                    <div className="cal-nav">
                        <button className="cal-nav__btn" onClick={navPrev} title={viewMode === 'week' ? 'Semana anterior' : 'Mês anterior'}>
                            <ChevronLeft size={18} />
                        </button>
                        <span className="cal-nav__label">
                            {getNavLabel()}
                            {weekBadge && <span className="cal-week-badge">{weekBadge}</span>}
                        </span>
                        <button className="cal-nav__btn" onClick={navNext} title={viewMode === 'week' ? 'Próxima semana' : 'Próximo mês'}>
                            <ChevronRight size={18} />
                        </button>
                    </div>
                    <button className="cal-nav__today" onClick={navToday}>
                        Hoje
                    </button>
                </div>
            </div>

            {/* ── Grid ── */}
            {loading ? (
                <div className="cal-loading">
                    <RefreshCw size={24} color="#94a3b8" className="cal-loading__icon" />
                    <div className="cal-loading__text">A construir escala...</div>
                </div>
            ) : data && data.employees.length > 0 ? (
                <div className="cal-grid-wrapper">
                    <div
                        ref={scrollRef}
                        className={`cal-grid-scroll ${isScrolled ? 'cal-grid-scroll--scrolled' : ''}`}
                    >
                        <table
                            className="cal-table"
                            style={{ minWidth: `${280 + days.length * dayColWidth}px` }}
                        >
                            <colgroup>
                                <col style={{ width: '280px' }} />
                                {days.map(d => (
                                    <col key={d.toISOString()} style={{ width: `${dayColWidth}px` }} />
                                ))}
                            </colgroup>
                            <thead>
                                <tr>
                                    <th className="cal-frozen-col">
                                        <span className="cal-frozen-col__label">Funcionário</span>
                                    </th>
                                    {days.map(d => {
                                        const todayClass = isSameDay(d, today) ? ' cal-day-header--today' : '';
                                        const weekendClass = isWeekendDay(d) ? ' cal-day-header--weekend' : '';
                                        return (
                                            <th
                                                key={d.toISOString()}
                                                className={`cal-day-header${weekendClass}${todayClass}`}
                                            >
                                                <span className="cal-day-header__weekday">
                                                    {d.toLocaleString('pt-AO', { weekday: 'narrow' })}
                                                </span>
                                                <span className="cal-day-header__number">
                                                    {d.getDate()}
                                                </span>
                                            </th>
                                        );
                                    })}
                                </tr>
                            </thead>
                            <tbody>
                                {data.employees.map((emp) => {
                                    const employeeRecords = data.records.filter(
                                        (r) => r.employeeId === emp.id
                                    );
                                    return (
                                        <tr key={emp.id}>
                                            <td className="cal-frozen-col">
                                                <div className="cal-employee">
                                                    <span className="cal-employee__name" title={emp.fullName}>
                                                        {emp.fullName}
                                                    </span>
                                                    <span className="cal-employee__dept">
                                                        {emp.portalDepartmentName || emp.innuxDepartmentName || 'Sem Departamento'}
                                                    </span>
                                                </div>
                                            </td>
                                            {days.map(d => {
                                                const record = getRecordForDay(employeeRecords, d);
                                                const weekend = isWeekendDay(d);
                                                const isCurrentDay = isSameDay(d, today);
                                                const todayBorder = isCurrentDay ? ' cal-day-cell--today-border' : '';
                                                const cellClass = `cal-day-cell${weekend ? ' cal-day-cell--weekend' : ''}${isCurrentDay && !record ? ' cal-day-cell--today' : ''}${todayBorder}`;

                                                let blockEl = null;
                                                if (record) {
                                                    const isDraftOrSubmitted = record.statusCode === 'DRAFT' || record.statusCode === 'SUBMITTED';
                                                    const isApproved = record.statusCode === 'APPROVED';
                                                    const statusClass = isApproved
                                                        ? 'cal-leave-block--approved'
                                                        : isDraftOrSubmitted
                                                            ? 'cal-leave-block--pending'
                                                            : 'cal-leave-block--other';

                                                    blockEl = (
                                                        <div
                                                            className={`cal-leave-block ${statusClass}`}
                                                            title={`${record.leaveType} (${record.statusCode})`}
                                                            style={{
                                                                borderRadius: getBlockBorderRadius(record, d),
                                                            }}
                                                        >
                                                            {isApproved && isFirstDay(record, d) && (
                                                                <CheckCircle size={12} color="#fff" style={{ position: 'absolute', left: '4px' }} />
                                                            )}
                                                        </div>
                                                    );
                                                }

                                                return (
                                                    <td key={d.toISOString()} className={cellClass}>
                                                        {isCurrentDay && <div className="cal-today-indicator" />}
                                                        {blockEl}
                                                    </td>
                                                );
                                            })}
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>

                    {/* Scroll hint */}
                    <div className={`cal-scroll-hint ${showScrollHint ? 'cal-scroll-hint--visible' : ''}`} />

                    {/* Legend */}
                    <div className="cal-legend">
                        <div className="cal-legend__item">
                            <div className="cal-legend__swatch cal-legend__swatch--approved" />
                            <span className="cal-legend__text">Férias (Aprovado)</span>
                        </div>
                        <div className="cal-legend__item">
                            <div className="cal-legend__swatch cal-legend__swatch--pending" />
                            <span className="cal-legend__text">Férias (Pendente)</span>
                        </div>
                        <div className="cal-legend__item">
                            <div className="cal-legend__swatch cal-legend__swatch--today" />
                            <span className="cal-legend__text">Hoje</span>
                        </div>
                        <span className="cal-legend__scope">{getScopeDescription()}</span>
                    </div>
                </div>
            ) : (
                <div className="cal-empty">
                    {data?.scopeType === 'self'
                        ? 'Nenhum registo de calendário encontrado para a sua conta.'
                        : 'Nenhum funcionário mapeado no seu escopo de hierarquia.'}
                </div>
            )}
        </div>
    );
}
