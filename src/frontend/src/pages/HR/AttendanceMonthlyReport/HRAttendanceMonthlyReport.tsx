import React, { useState, useCallback, useRef } from 'react';
import { api } from '../../../lib/api';
import { useAuth } from '../../../features/auth/AuthContext';
import { ROLES } from '../../../constants/roles';
import { Printer, Loader2, AlertTriangle, FileText, Calendar as CalendarIcon, Filter, AlertCircle, ShieldAlert } from 'lucide-react';
import { DepartmentMasterAutocomplete } from '../../../components/DepartmentMasterAutocomplete';
import './hr-attendance-monthly-report.css';

// ─── DTOs aligned to backend C# AttendanceReportDtos.cs ───

interface AttendanceDailyRecordDto {
    date: string;
    weekday: string;
    entrada1: string | null;
    saida1: string | null;
    entrada2: string | null;
    saida2: string | null;
    entrada3: string | null;
    saida3: string | null;
    entrada4: string | null;
    saida4: string | null;
    basicMinutes: number;
    extraMinutes: number;
    unpaidMinutes: number;
    totalMinutes: number;
    missingMinutes: number;
    absenceMinutes: number;
    absenceDescription: string | null;
    justification: string | null;
    dailyBalance: number;
    status: string;
    isDayOff: boolean;
    isVacation: boolean;
    isHoliday: boolean;
    hasMissingPunch: boolean;
    hasInconsistentData: boolean;
    isPortalInterpreted: boolean;
    warningMessage: string | null;
}

interface AttendanceMonthlySummaryDto {
    year: number;
    month: number;
    monthLabel: string;
    basicMinutes: number;
    extraMinutes: number;
    totalMinutes: number;
    balanceMinutes: number;
    workedDays: number;
    vacationDays: number;
    dayOffDays: number;
    missingPunchDays: number;
    inconsistentDays: number;
}

interface AttendanceReportTotalsDto {
    basicMinutes: number;
    extraMinutes: number;
    totalMinutes: number;
    balanceMinutes: number;
    workedDays: number;
    vacationDays: number;
    dayOffDays: number;
    missingPunchDays: number;
    inconsistentDays: number;
}

interface AttendanceEmployeeReportDto {
    innuxId: number;
    employeeId: string | null;
    name: string;
    departmentName: string;
    plantName: string | null;
    dailyRecords: AttendanceDailyRecordDto[];
    monthlyTotals: AttendanceMonthlySummaryDto[];
    grandTotals: AttendanceReportTotalsDto;
    warnings: string[];
}

interface AttendanceDepartmentMonthlyReportDto {
    departmentId: number;
    departmentName: string;
    startDate: string;
    endDate: string;
    daysFilter: string | null;
    generatedAt: string;
    generatedBy: string;
    employees: AttendanceEmployeeReportDto[];
    departmentMonthlyTotals: AttendanceMonthlySummaryDto[];
    departmentGrandTotals: AttendanceReportTotalsDto;
    warnings: string[];
}

// ─── Helpers ───

function formatMinutesToHours(minutes: number): string {
    if (minutes === 0) return '00:00';
    const isNegative = minutes < 0;
    const absMinutes = Math.abs(minutes);
    const h = Math.floor(absMinutes / 60);
    const m = absMinutes % 60;
    const timeStr = `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
    return isNegative ? `-${timeStr}` : timeStr;
}

function formatDateDisplay(dateStr: string): string {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    return d.toLocaleDateString('pt-AO', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function formatDateShort(dateStr: string): string {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    return d.toLocaleDateString('pt-AO', { day: '2-digit', month: '2-digit' });
}

function defaultStartDate() {
    const d = new Date();
    d.setDate(1);
    return d.toISOString().split('T')[0];
}

function defaultEndDate() {
    const d = new Date();
    d.setMonth(d.getMonth() + 1);
    d.setDate(0);
    return d.toISOString().split('T')[0];
}

function getStatusClassName(day: AttendanceDailyRecordDto): string {
    if (day.isDayOff) return 'status-dayoff';
    if (day.isVacation) return 'status-vacation';
    if (day.isHoliday) return 'status-holiday';
    if (day.hasMissingPunch || day.hasInconsistentData) return 'status-anomaly';
    const s = (day.status || '').toLowerCase();
    if (s === 'present') return 'status-present';
    if (s === 'absent') return 'status-absent';
    if (s === 'justifiedabsence') return 'status-justified';
    return '';
}

function getStatusLabel(day: AttendanceDailyRecordDto): string {
    if (day.isDayOff) return 'Folga';
    if (day.isVacation) return 'Férias';
    if (day.isHoliday) return 'Feriado';
    const s = (day.status || '').toLowerCase();
    if (s === 'present') return 'Presente';
    if (s === 'absent') return 'Falta';
    if (s === 'justifiedabsence') return 'Falta Just.';
    if (s === 'anomaly') return 'Anomalia';
    return day.status || '';
}

// ─── Component ───

export default function HRAttendanceMonthlyReport() {
    const { user } = useAuth();
    const hasAccess = user?.roles.some(r => r === ROLES.SYSTEM_ADMINISTRATOR || r === ROLES.HR) ?? false;

    const [departmentId, setDepartmentId] = useState<number | null>(null);
    const [departmentName, setDepartmentName] = useState('');
    const [startDate, setStartDate] = useState(defaultStartDate());
    const [endDate, setEndDate] = useState(defaultEndDate());
    const [daysFilter, setDaysFilter] = useState('all');

    const [loading, setLoading] = useState(false);
    const [reportData, setReportData] = useState<AttendanceDepartmentMonthlyReportDto | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [validationError, setValidationError] = useState<string | null>(null);

    const printAreaRef = useRef<HTMLDivElement>(null);

    const handleDepartmentChange = useCallback((id: number | null, name: string) => {
        setDepartmentId(id);
        setDepartmentName(name);
    }, []);

    const handleGenerate = useCallback(async () => {
        setValidationError(null);
        setError(null);
        setReportData(null);

        if (!departmentId) {
            setValidationError('Por favor, seleccione um departamento.');
            return;
        }

        if (!startDate || !endDate) {
            setValidationError('Data inicial e data final são obrigatórias.');
            return;
        }

        const sDate = new Date(startDate);
        const eDate = new Date(endDate);
        if (sDate > eDate) {
            setValidationError('A data inicial deve ser anterior à data final.');
            return;
        }

        const daysDiff = (eDate.getTime() - sDate.getTime()) / (1000 * 3600 * 24);
        if (daysDiff > 62) {
            setValidationError('O intervalo máximo permitido é de 62 dias.');
            return;
        }

        setLoading(true);
        try {
            const data = await api.hrAttendance.getMonthlyDepartmentReport({
                departmentId,
                startDate,
                endDate,
                daysFilter: daysFilter === 'all' ? undefined : daysFilter
            });
            setReportData(data);
        } catch (err: any) {
            setError(err?.message || 'Falha ao gerar relatório mensal.');
        } finally {
            setLoading(false);
        }
    }, [departmentId, startDate, endDate, daysFilter]);

    const handlePrint = () => {
        window.print();
    };

    if (!hasAccess) {
        return (
            <div className="att-report-no-access">
                <AlertTriangle size={40} className="att-report-no-access-icon" />
                <p className="att-report-no-access-title">Acesso restrito</p>
                <p className="att-report-no-access-subtitle">Esta página está disponível apenas para Administradores e utilizadores de RH.</p>
            </div>
        );
    }

    return (
        <div className="att-report-container">
            {/* Control Panel (Hidden on print) */}
            <div className="att-report-controls no-print">
                <div className="att-report-controls-header">
                    <h2><FileText size={20} /> Relatórios de Assiduidade: Mensal por Departamento</h2>
                </div>

                {/* Read-only disclaimer banner */}
                <div className="att-report-disclaimer">
                    <ShieldAlert size={16} />
                    <span>Este relatório é <strong>somente leitura</strong>. Os dados são obtidos do Innux e não são alterados pelo Portal.</span>
                </div>
                
                <div className="att-report-filters">
                    <div className="att-report-filter-group att-report-filter-department">
                        <label>Departamento</label>
                        <DepartmentMasterAutocomplete
                            initialId={departmentId}
                            initialName={departmentName}
                            onChange={handleDepartmentChange}
                            placeholder="Pesquisar departamento..."
                        />
                    </div>

                    <div className="att-report-filter-group">
                        <label>Data Inicial</label>
                        <div className="att-report-date-input">
                            <CalendarIcon size={16} />
                            <input type="date" value={startDate} onChange={e => setStartDate(e.target.value)} />
                        </div>
                    </div>

                    <div className="att-report-filter-group">
                        <label>Data Final</label>
                        <div className="att-report-date-input">
                            <CalendarIcon size={16} />
                            <input type="date" value={endDate} onChange={e => setEndDate(e.target.value)} />
                        </div>
                    </div>

                    <div className="att-report-filter-group">
                        <label>Filtro de Dias</label>
                        <div className="att-report-date-input">
                            <Filter size={16} />
                            <select value={daysFilter} onChange={e => setDaysFilter(e.target.value)}>
                                <option value="all">Todos os Dias</option>
                                <option value="business">Dias Úteis</option>
                                <option value="weekends">Fins de Semana</option>
                            </select>
                        </div>
                    </div>

                    <div className="att-report-actions">
                        <button 
                            className="btn-generate" 
                            onClick={handleGenerate} 
                            disabled={loading || !departmentId}
                        >
                            {loading ? <Loader2 size={16} className="spin" /> : <FileText size={16} />}
                            {loading ? 'A gerar...' : 'Gerar Relatório'}
                        </button>

                        <button 
                            className="btn-print" 
                            onClick={handlePrint} 
                            disabled={!reportData || loading}
                        >
                            <Printer size={16} />
                            Imprimir PDF
                        </button>
                    </div>
                </div>

                {validationError && (
                    <div className="att-report-validation-error">
                        <AlertTriangle size={16} />
                        {validationError}
                    </div>
                )}
                
                {error && (
                    <div className="att-report-error">
                        <AlertCircle size={16} />
                        {error}
                    </div>
                )}
            </div>

            {/* Print Area */}
            {reportData && (
                <div className="att-report-print-area" ref={printAreaRef}>
                    {/* Report Header */}
                    <div className="att-report-header">
                        <h1>Resultados mensais por departamento</h1>
                        <div className="att-report-meta">
                            <div><strong>Departamento:</strong> {reportData.departmentName}</div>
                            <div><strong>Período:</strong> {formatDateDisplay(reportData.startDate)} a {formatDateDisplay(reportData.endDate)}</div>
                            <div><strong>Gerado em:</strong> {formatDateDisplay(reportData.generatedAt)} por {reportData.generatedBy}</div>
                        </div>
                    </div>

                    {/* Report-level warnings */}
                    {reportData.warnings && reportData.warnings.length > 0 && (
                        <div className="att-report-warnings no-print">
                            {reportData.warnings.map((w, i) => (
                                <div key={i} className="att-report-warning-item">
                                    <AlertTriangle size={14} /> {w}
                                </div>
                            ))}
                        </div>
                    )}

                    {reportData.employees.length === 0 ? (
                        <div className="att-report-empty">
                            Nenhum registo de assiduidade encontrado para este departamento neste período.
                        </div>
                    ) : (
                        <div className="att-report-employees">
                            {reportData.employees.map(employee => (
                                <div key={employee.innuxId} className="att-report-employee-section">
                                    <div className="att-report-employee-header">
                                        <div className="att-report-employee-title">
                                            <strong>Funcionário:</strong> {employee.name}
                                            <span className="employee-code">(Nº {employee.employeeId || employee.innuxId})</span>
                                            {employee.plantName && <span className="employee-plant"> — {employee.plantName}</span>}
                                        </div>
                                        {employee.warnings && employee.warnings.length > 0 && (
                                            <div className="att-report-employee-warnings no-print">
                                                {employee.warnings.map((w, i) => (
                                                    <span key={i} className="employee-warning-badge">
                                                        <AlertTriangle size={12} /> {w}
                                                    </span>
                                                ))}
                                            </div>
                                        )}
                                    </div>

                                    <table className="att-report-table">
                                        <thead>
                                            <tr>
                                                <th className="col-date">Data</th>
                                                <th className="col-day">Dia</th>
                                                <th className="col-punch">Ent.1</th>
                                                <th className="col-punch">Saí.1</th>
                                                <th className="col-punch">Ent.2</th>
                                                <th className="col-punch">Saí.2</th>
                                                <th className="col-punch">Ent.3</th>
                                                <th className="col-punch">Saí.3</th>
                                                <th className="col-hours">H.Básicas</th>
                                                <th className="col-hours">H.Extra</th>
                                                <th className="col-hours">H.N.Rem.</th>
                                                <th className="col-hours">H.Falta</th>
                                                <th className="col-hours">H.Totais</th>
                                                <th className="col-balance">Saldo</th>
                                                <th className="col-status">Estado</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {employee.dailyRecords.map(day => (
                                                <tr key={day.date} className={getStatusClassName(day)}>
                                                    <td className="col-date">{formatDateShort(day.date)}</td>
                                                    <td className="col-day">{day.weekday}</td>
                                                    <td className="col-punch">{day.entrada1 || ''}</td>
                                                    <td className="col-punch">{day.saida1 || ''}</td>
                                                    <td className="col-punch">{day.entrada2 || ''}</td>
                                                    <td className="col-punch">{day.saida2 || ''}</td>
                                                    <td className="col-punch">{day.entrada3 || ''}</td>
                                                    <td className="col-punch">{day.saida3 || ''}</td>
                                                    <td className="col-hours">{formatMinutesToHours(day.basicMinutes)}</td>
                                                    <td className="col-hours">{day.extraMinutes > 0 ? formatMinutesToHours(day.extraMinutes) : ''}</td>
                                                    <td className="col-hours">{day.unpaidMinutes > 0 ? formatMinutesToHours(day.unpaidMinutes) : ''}</td>
                                                    <td className="col-hours">{day.absenceMinutes > 0 ? formatMinutesToHours(day.absenceMinutes) : ''}</td>
                                                    <td className="col-hours font-bold">{formatMinutesToHours(day.totalMinutes)}</td>
                                                    <td className="col-balance">{formatMinutesToHours(day.dailyBalance)}</td>
                                                    <td className="col-status">
                                                        <span className="status-label">{getStatusLabel(day)}</span>
                                                        {day.justification && <span className="justification" title={day.justification}>{day.justification}</span>}
                                                        {day.warningMessage && (
                                                            <span className="warning-indicator" title={day.warningMessage}>
                                                                <AlertTriangle size={10} />
                                                            </span>
                                                        )}
                                                        {day.isPortalInterpreted && (
                                                            <span className="portal-badge" title="Interpretado pelo Portal">P</span>
                                                        )}
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>

                                    {/* Employee Monthly Totals */}
                                    {employee.monthlyTotals && employee.monthlyTotals.length > 0 && (
                                        <div className="att-report-employee-totals">
                                            {employee.monthlyTotals.map(mt => (
                                                <div key={`${mt.year}-${mt.month}`} className="totals-row">
                                                    <strong>{mt.monthLabel}:</strong>
                                                    <span>H.Básicas: {formatMinutesToHours(mt.basicMinutes)}</span>
                                                    <span>H.Extra: {formatMinutesToHours(mt.extraMinutes)}</span>
                                                    <span className="font-bold">H.Totais: {formatMinutesToHours(mt.totalMinutes)}</span>
                                                    <span>Saldo: {formatMinutesToHours(mt.balanceMinutes)}</span>
                                                    <span>Dias Trab.: {mt.workedDays}</span>
                                                    <span>Férias: {mt.vacationDays}</span>
                                                    <span>Folgas: {mt.dayOffDays}</span>
                                                </div>
                                            ))}
                                        </div>
                                    )}

                                    {/* Employee Grand Totals */}
                                    <div className="att-report-employee-totals grand-totals">
                                        <div className="totals-row">
                                            <strong>Totais Gerais:</strong>
                                            <span>H.Básicas: {formatMinutesToHours(employee.grandTotals.basicMinutes)}</span>
                                            <span>H.Extra: {formatMinutesToHours(employee.grandTotals.extraMinutes)}</span>
                                            <span className="font-bold">H.Totais: {formatMinutesToHours(employee.grandTotals.totalMinutes)}</span>
                                            <span>Saldo: {formatMinutesToHours(employee.grandTotals.balanceMinutes)}</span>
                                        </div>
                                        <div className="totals-row totals-days">
                                            <span>Dias Trab.: {employee.grandTotals.workedDays}</span>
                                            <span>Férias: {employee.grandTotals.vacationDays}</span>
                                            <span>Folgas: {employee.grandTotals.dayOffDays}</span>
                                            {employee.grandTotals.missingPunchDays > 0 && (
                                                <span className="totals-warning">Marc. Falta: {employee.grandTotals.missingPunchDays}</span>
                                            )}
                                            {employee.grandTotals.inconsistentDays > 0 && (
                                                <span className="totals-warning">Inconsist.: {employee.grandTotals.inconsistentDays}</span>
                                            )}
                                        </div>
                                    </div>
                                </div>
                            ))}

                            {/* Department Grand Totals */}
                            <div className="att-report-department-totals">
                                <h3>Totais do Departamento</h3>
                                <div className="totals-row">
                                    <span>H.Básicas: {formatMinutesToHours(reportData.departmentGrandTotals.basicMinutes)}</span>
                                    <span>H.Extra: {formatMinutesToHours(reportData.departmentGrandTotals.extraMinutes)}</span>
                                    <span className="font-bold">H.Totais: {formatMinutesToHours(reportData.departmentGrandTotals.totalMinutes)}</span>
                                    <span>Saldo: {formatMinutesToHours(reportData.departmentGrandTotals.balanceMinutes)}</span>
                                </div>
                                <div className="totals-row totals-days">
                                    <span>Total Funcionários: {reportData.employees.length}</span>
                                    <span>Dias Trab.: {reportData.departmentGrandTotals.workedDays}</span>
                                    <span>Férias: {reportData.departmentGrandTotals.vacationDays}</span>
                                    <span>Folgas: {reportData.departmentGrandTotals.dayOffDays}</span>
                                </div>
                            </div>
                        </div>
                    )}

                    {/* Print Footer */}
                    <div className="att-report-footer">
                        <span>Portal Gerencial — Relatório gerado automaticamente. Somente leitura.</span>
                    </div>
                </div>
            )}
        </div>
    );
}
