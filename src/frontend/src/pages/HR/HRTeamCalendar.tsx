import { useEffect, useState } from 'react';
import { api } from '../../lib/api';
import { RefreshCw, Navigation, CheckCircle } from 'lucide-react';

export default function HRTeamCalendar() {
    const [data, setData] = useState<any>(null);
    const [loading, setLoading] = useState(true);
    
    // Default to current month viewing
    const [currentDate, setCurrentDate] = useState(new Date());

    useEffect(() => {
        loadData();
    }, [currentDate]);

    const loadData = () => {
        setLoading(true);
        // Calculate first and last day of current month
        const year = currentDate.getFullYear();
        const month = currentDate.getMonth();
        const fromDate = new Date(year, month, 1);
        const toDate = new Date(year, month + 1, 0); // last day of month
        
        const formatQueryDate = (d: Date) => d.toISOString().split('T')[0];

        api.hrLeave.getCalendar({ from: formatQueryDate(fromDate), to: formatQueryDate(toDate) })
            .then(res => {
                setData(res);
                setLoading(false);
            })
            .catch(err => {
                console.error(err);
                setLoading(false);
            });
    };

    const nextMonth = () => {
        setCurrentDate(new Date(currentDate.getFullYear(), currentDate.getMonth() + 1, 1));
    };

    const prevMonth = () => {
        setCurrentDate(new Date(currentDate.getFullYear(), currentDate.getMonth() - 1, 1));
    };

    const today = new Date();

    // Helper functions for calendar rendering
    const getDaysInView = () => {
        const year = currentDate.getFullYear();
        const month = currentDate.getMonth();
        const daysInMonth = new Date(year, month + 1, 0).getDate();
        return Array.from({ length: daysInMonth }, (_, i) => new Date(year, month, i + 1));
    };

    const isWeekend = (date: Date) => {
        return date.getDay() === 0 || date.getDay() === 6;
    };

    const isToday = (date: Date) => {
        return date.getDate() === today.getDate() && 
               date.getMonth() === today.getMonth() && 
               date.getFullYear() === today.getFullYear();
    };

    const days = getDaysInView();
    const monthName = currentDate.toLocaleString('pt-AO', { month: 'long', year: 'numeric' });

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            {/* Header */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '16px' }}>
                <div>
                    <h2 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 800, color: '#0f172a' }}>Calendário da Equipa</h2>
                    <p style={{ margin: '4px 0 0 0', color: '#64748b', fontWeight: 500 }}>
                        Visão de bloqueio operacional por departamento.
                    </p>
                </div>
                
                <div style={{ display: 'flex', alignItems: 'center', gap: '16px', backgroundColor: 'var(--color-bg-surface)', padding: '8px', borderRadius: '12px', border: '1px solid var(--color-border)', boxShadow: '0 1px 2px rgba(0,0,0,0.05)' }}>
                    <button 
                        onClick={prevMonth}
                        style={{ background: 'none', border: 'none', padding: '8px', cursor: 'pointer', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}
                        onMouseOver={e => e.currentTarget.style.backgroundColor = '#f1f5f9'}
                        onMouseOut={e => e.currentTarget.style.backgroundColor = 'transparent'}
                    >
                        <Navigation size={18} style={{ transform: 'rotate(-90deg)', color: '#475569' }} />
                    </button>
                    <span style={{ fontWeight: 800, color: '#0f172a', minWidth: '150px', textAlign: 'center', textTransform: 'capitalize' }}>
                        {monthName}
                    </span>
                    <button 
                        onClick={nextMonth}
                        style={{ background: 'none', border: 'none', padding: '8px', cursor: 'pointer', borderRadius: '8px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}
                        onMouseOver={e => e.currentTarget.style.backgroundColor = '#f1f5f9'}
                        onMouseOut={e => e.currentTarget.style.backgroundColor = 'transparent'}
                    >
                        <Navigation size={18} style={{ transform: 'rotate(90deg)', color: '#475569' }} />
                    </button>
                    <button 
                        onClick={() => setCurrentDate(new Date())}
                        style={{ marginLeft: '8px', padding: '8px 16px', borderRadius: '8px', border: '1px solid #cbd5e1', backgroundColor: '#fff', color: '#475569', fontWeight: 600, cursor: 'pointer', fontSize: '13px' }}
                    >
                        Hoje
                    </button>
                </div>
            </div>

            {loading ? (
                <div style={{ padding: '60px', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', borderRadius: '12px', border: '1px solid var(--color-border)' }}>
                    <RefreshCw className="animate-spin" size={24} color="#94a3b8" style={{ marginBottom: '16px' }} />
                    <div style={{ fontWeight: 600, color: '#64748b' }}>A construir escala...</div>
                </div>
            ) : data && data.employees.length > 0 ? (
                <div style={{ backgroundColor: 'var(--color-bg-surface)', border: '1px solid var(--color-border)', borderRadius: '12px', boxShadow: '0 4px 6px rgba(0,0,0,0.05)', overflow: 'hidden' }}>
                    <div style={{ overflowX: 'auto', paddingBottom: '16px' }}>
                        <table style={{ width: '100%', minWidth: `${300 + days.length * 36}px`, borderCollapse: 'collapse', tableLayout: 'fixed' }}>
                            <thead>
                                <tr>
                                    <th style={{ width: '300px', backgroundColor: '#f8fafc', padding: '16px', textAlign: 'left', borderBottom: '2px solid #e2e8f0', borderRight: '1px solid #e2e8f0', position: 'sticky', left: 0, zIndex: 10 }}>
                                        <span style={{ fontWeight: 800, fontSize: '13px', color: '#475569', textTransform: 'uppercase' }}>Funcionário</span>
                                    </th>
                                    {days.map(d => (
                                        <th key={d.toISOString()} style={{ width: '36px', backgroundColor: isToday(d) ? '#e0f2fe' : isWeekend(d) ? '#f1f5f9' : '#f8fafc', padding: '8px 0', textAlign: 'center', borderBottom: '2px solid #e2e8f0', borderRight: '1px solid #e2e8f0' }}>
                                            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
                                                <span style={{ fontSize: '10px', fontWeight: 800, color: isToday(d) ? '#0284c7' : isWeekend(d) ? '#94a3b8' : '#64748b', textTransform: 'uppercase' }}>
                                                    {d.toLocaleString('pt-AO', { weekday: 'narrow' })}
                                                </span>
                                                <span style={{ fontSize: '13px', fontWeight: isToday(d) ? 800 : 600, color: isToday(d) ? '#0284c7' : '#0f172a' }}>
                                                    {d.getDate()}
                                                </span>
                                            </div>
                                        </th>
                                    ))}
                                </tr>
                            </thead>
                            <tbody>
                                {data.employees.map((emp: any) => {
                                    // Get records for this employee that overlap with the current view
                                    const employeeRecords = data.records.filter((r: any) => r.employeeId === emp.id);
                                    
                                    return (
                                        <tr key={emp.id} style={{ borderBottom: '1px solid #e2e8f0', height: '56px' }}>
                                            <td style={{ backgroundColor: '#fff', padding: '0 16px', borderRight: '1px solid #e2e8f0', position: 'sticky', left: 0, zIndex: 5 }}>
                                                <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
                                                    <span style={{ fontWeight: 700, fontSize: '13px', color: '#0f172a', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={emp.fullName}>
                                                        {emp.fullName}
                                                    </span>
                                                    <span style={{ fontSize: '11px', color: '#64748b', fontWeight: 600 }}>
                                                        {emp.portalDepartmentName || emp.innuxDepartmentName || 'Sem Departamento'}
                                                    </span>
                                                </div>
                                            </td>
                                            {days.map(d => {
                                                const dStartContext = new Date(d);
                                                dStartContext.setHours(0,0,0,0);
                                                
                                                // Find if any record covers this day
                                                const recordForDay = employeeRecords.find((r: any) => {
                                                    const rStart = new Date(r.startDate);
                                                    rStart.setHours(0,0,0,0);
                                                    const rEnd = new Date(r.endDate);
                                                    rEnd.setHours(23,59,59,999);
                                                    return dStartContext >= rStart && dStartContext <= rEnd;
                                                });

                                                let cellContent = null;
                                                let cellBg = isWeekend(d) ? '#f8fafc' : '#fff';
                                                
                                                if (recordForDay) {
                                                    const color = recordForDay.leaveTypeColor || '#0ea5e9';
                                                    const isDraftOrSubmitted = recordForDay.statusCode === 'DRAFT' || recordForDay.statusCode === 'SUBMITTED';
                                                    cellBg = isDraftOrSubmitted ? `${color}1A` : color;
                                                    
                                                    // Determine if it's the first day, middle, or last day to adjust visual connecting blocks
                                                    const rStart = new Date(recordForDay.startDate); rStart.setHours(0,0,0,0);
                                                    const rEnd = new Date(recordForDay.endDate); rEnd.setHours(0,0,0,0);
                                                    
                                                    const isFirst = dStartContext.getTime() === rStart.getTime();
                                                    const isLast = dStartContext.getTime() === rEnd.getTime();
                                                    
                                                    const borderRadius = `${isFirst ? '6px' : '0'} ${isLast ? '6px' : '0'} ${isLast ? '6px' : '0'} ${isFirst ? '6px' : '0'}`;
                                                    
                                                    cellContent = (
                                                        <div 
                                                            title={`${recordForDay.leaveType} (${recordForDay.statusCode})`}
                                                            style={{ 
                                                                height: '32px', 
                                                                backgroundColor: cellBg, 
                                                                width: '100%', 
                                                                position: 'relative',
                                                                border: isDraftOrSubmitted ? `1px dashed ${color}` : 'none',
                                                                borderRadius: borderRadius,
                                                                display: 'flex',
                                                                alignItems: 'center',
                                                                justifyContent: 'center',
                                                                margin: '2px 0' // to avoid touching rows above/below
                                                            }}
                                                        >
                                                            {recordForDay.statusCode === 'APPROVED' && isFirst && (
                                                                <CheckCircle size={12} color="#fff" style={{ position: 'absolute', left: '4px' }} />
                                                            )}
                                                        </div>
                                                    );
                                                }

                                                return (
                                                    <td key={d.toISOString()} style={{ padding: 0, borderRight: '1px solid #f1f5f9', backgroundColor: !recordForDay && isWeekend(d) ? '#f8fafc' : 'transparent', verticalAlign: 'middle' }}>
                                                        {cellContent}
                                                    </td>
                                                );
                                            })}
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                    </div>
                    {/* Legend */}
                    <div style={{ padding: '16px', borderTop: '1px solid #e2e8f0', display: 'flex', gap: '24px', flexWrap: 'wrap', backgroundColor: '#f8fafc' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <div style={{ width: '16px', height: '16px', borderRadius: '4px', backgroundColor: '#0ea5e9' }}></div>
                            <span style={{ fontSize: '12px', fontWeight: 600, color: '#475569' }}>Férias (Aprovado)</span>
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <div style={{ width: '16px', height: '16px', borderRadius: '4px', backgroundColor: '#0ea5e91A', border: '1px dashed #0ea5e9' }}></div>
                            <span style={{ fontSize: '12px', fontWeight: 600, color: '#475569' }}>Férias (Pendente)</span>
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                            <div style={{ width: '16px', height: '16px', borderRadius: '4px', backgroundColor: '#f43f5e' }}></div>
                            <span style={{ fontSize: '12px', fontWeight: 600, color: '#475569' }}>Faltas / Doença</span>
                        </div>
                        <span style={{ marginLeft: 'auto', fontSize: '12px', color: '#94a3b8', fontStyle: 'italic' }}>
                            Cores exatas variam conforme o padrão configurado no RH.
                        </span>
                    </div>
                </div>
            ) : (
                <div style={{ padding: '60px', textAlign: 'center', backgroundColor: 'var(--color-bg-surface)', borderRadius: '12px', border: '1px dashed #cbd5e1', color: '#64748b', fontWeight: 600 }}>
                    Nenhum funcionário mapeado no seu escopo de hierarquia.
                </div>
            )}
        </div>
    );
}
