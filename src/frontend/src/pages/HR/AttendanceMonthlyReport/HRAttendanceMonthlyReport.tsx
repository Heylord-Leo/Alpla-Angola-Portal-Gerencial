import { useState, useCallback, useRef } from 'react';
import { api } from '../../../lib/api';
import { useAuth } from '../../../features/auth/AuthContext';
import { ROLES } from '../../../constants/roles';
import { Printer, Loader2, AlertTriangle, FileText, Calendar as CalendarIcon, Filter, AlertCircle, ShieldAlert, UserCheck, UserX, Database, Info } from 'lucide-react';
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

interface AttendanceConsolidatedReportDto {
    startDate: string;
    endDate: string;
    daysFilter: string | null;
    generatedAt: string;
    generatedBy: string;
    totalDepartments: number;
    totalEmployees: number;
    departments: AttendanceDepartmentMonthlyReportDto[];
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
    const [attendanceActivity, setAttendanceActivity] = useState<'active' | 'noRecent' | 'all'>('active');

    const [loading, setLoading] = useState(false);
    const [reportData, setReportData] = useState<AttendanceDepartmentMonthlyReportDto | null>(null);
    const [consolidatedData, setConsolidatedData] = useState<AttendanceConsolidatedReportDto | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [validationError, setValidationError] = useState<string | null>(null);

    const printAreaRef = useRef<HTMLDivElement>(null);

    // true when "Todos os Departamentos" is selected (departmentId === 0)
    const isConsolidatedMode = departmentId === 0;

    const handleDepartmentChange = useCallback((id: number | null, name: string) => {
        setDepartmentId(id);
        setDepartmentName(name);
    }, []);

    const handleGenerate = useCallback(async () => {
        setValidationError(null);
        setError(null);
        setReportData(null);
        setConsolidatedData(null);

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

        // Require either a department or "all departments" (departmentId === 0)
        if (departmentId === null) {
            setValidationError('Por favor, seleccione um departamento ou "Todos os Departamentos".');
            return;
        }

        setLoading(true);
        try {
            const apiDeptId = isConsolidatedMode ? null : departmentId;
            const data = await api.hrAttendance.getMonthlyDepartmentReport({
                departmentId: apiDeptId,
                startDate,
                endDate,
                daysFilter: daysFilter === 'all' ? undefined : daysFilter,
                attendanceActivity
            });

            if (isConsolidatedMode) {
                setConsolidatedData(data as AttendanceConsolidatedReportDto);
            } else {
                setReportData(data as AttendanceDepartmentMonthlyReportDto);
            }
        } catch (err: any) {
            setError(err?.message || 'Falha ao gerar relatório mensal.');
        } finally {
            setLoading(false);
        }
    }, [departmentId, startDate, endDate, daysFilter, attendanceActivity, isConsolidatedMode]);

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

                {/* 30-day attendance activity filter */}
                <div className="att-report-activity-filter">
                    <div className="att-report-activity-filter__group">
                        <button
                            className={`att-report-activity-filter__btn${attendanceActivity === 'active' ? ' att-report-activity-filter__btn--active' : ''}`}
                            onClick={() => setAttendanceActivity('active')}
                        >
                            <UserCheck size={14} />
                            <span>Com ponto recente</span>
                        </button>
                        <button
                            className={`att-report-activity-filter__btn${attendanceActivity === 'noRecent' ? ' att-report-activity-filter__btn--active' : ''}`}
                            onClick={() => setAttendanceActivity('noRecent')}
                        >
                            <UserX size={14} />
                            <span>Sem ponto há +30 dias</span>
                        </button>
                        <button
                            className={`att-report-activity-filter__btn${attendanceActivity === 'all' ? ' att-report-activity-filter__btn--active' : ''}`}
                            onClick={() => setAttendanceActivity('all')}
                        >
                            <Database size={14} />
                            <span>Todos</span>
                        </button>
                    </div>
                    {attendanceActivity === 'active' && (
                        <p className="att-report-activity-filter__hint">
                            <Info size={12} />
                            Funcionários sem marcação real de ponto há mais de 30 dias são excluídos por padrão.
                        </p>
                    )}
                </div>
                
                <div className="att-report-filters">
                    <div className="att-report-filter-group att-report-filter-department">
                        <label>Departamento</label>
                        <DepartmentMasterAutocomplete
                            initialId={departmentId}
                            initialName={departmentName}
                            onChange={handleDepartmentChange}
                            placeholder="Pesquisar departamento..."
                            showAllOption={true}
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
                            disabled={loading || departmentId === null}
                        >
                            {loading ? <Loader2 size={16} className="spin" /> : <FileText size={16} />}
                            {loading
                                ? (isConsolidatedMode ? 'A gerar relatório consolidado de todos os departamentos...' : 'A gerar...')
                                : 'Gerar Relatório'}
                        </button>

                        <button 
                            className="btn-print" 
                            onClick={handlePrint} 
                            disabled={(!reportData && !consolidatedData) || loading}
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

            {/* ─── Single-department Print Area ─── */}
            {reportData && !consolidatedData && (
                <div className="att-report-print-area" ref={printAreaRef}>
                    {/* ─── Print-Only Official Document Header ─── */}
                    <div className="print-only att-print-doc-header">
                        <div className="att-print-doc-header-top">
                            <div className="att-print-doc-company">ALPLA Angola | Portal Gerencial</div>
                            <div className="att-print-doc-generated">
                                Gerado em: {formatDateDisplay(reportData.generatedAt)} por {reportData.generatedBy}
                            </div>
                        </div>
                        <h1 className="att-print-doc-title">Relatório Mensal de Presenças</h1>
                        <div className="att-print-doc-meta">
                            <span><strong>Departamento:</strong> {reportData.departmentName}</span>
                            <span><strong>Período:</strong> {formatDateDisplay(reportData.startDate)} a {formatDateDisplay(reportData.endDate)}</span>
                            {reportData.daysFilter && reportData.daysFilter !== 'all' && (
                                <span><strong>Filtro:</strong> {reportData.daysFilter === 'business' ? 'Dias Úteis' : 'Fins de Semana'}</span>
                            )}
                        </div>
                        {attendanceActivity === 'active' && (
                            <div className="att-print-doc-notice">
                                * Funcionários sem marcação real de ponto há mais de 30 dias são excluídos por padrão.
                            </div>
                        )}
                    </div>

                    {/* ─── Screen-Only Report Header ─── */}
                    <div className="att-report-header screen-only">
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
                            {renderEmployeeList(reportData.employees)}

                            {/* Department Grand Totals */}
                            <div className="att-report-department-totals">
                                <h3>Totais do Departamento</h3>
                                <div className="totals-row">
                                    <span>H.Básicas: {formatMinutesToHours(reportData.departmentGrandTotals.basicMinutes)}</span>
                                    <span>H.Extra: {formatMinutesToHours(reportData.departmentGrandTotals.extraMinutes)}</span>
                                    <span className="font-bold">H.Totais: {formatMinutesToHours(reportData.departmentGrandTotals.totalMinutes)}</span>
                                    <span className={reportData.departmentGrandTotals.balanceMinutes < 0 ? 'balance-negative' : reportData.departmentGrandTotals.balanceMinutes > 0 ? 'balance-positive' : ''}>Saldo: {formatMinutesToHours(reportData.departmentGrandTotals.balanceMinutes)}</span>
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

            {/* ─── Consolidated All-Departments Print Area ─── */}
            {consolidatedData && (
                <div className="att-report-print-area" ref={printAreaRef}>
                    {/* Screen-only consolidated header */}
                    <div className="att-report-header screen-only">
                        <h1>Relatório Consolidado — Todos os Departamentos</h1>
                        <div className="att-report-meta">
                            <div><strong>Período:</strong> {formatDateDisplay(consolidatedData.startDate)} a {formatDateDisplay(consolidatedData.endDate)}</div>
                            <div><strong>Departamentos:</strong> {consolidatedData.totalDepartments}</div>
                            <div><strong>Funcionários:</strong> {consolidatedData.totalEmployees}</div>
                            <div><strong>Gerado em:</strong> {formatDateDisplay(consolidatedData.generatedAt)} por {consolidatedData.generatedBy}</div>
                        </div>
                    </div>

                    {/* Consolidated-level warnings */}
                    {consolidatedData.warnings && consolidatedData.warnings.length > 0 && (
                        <div className="att-report-warnings no-print">
                            {consolidatedData.warnings.map((w, i) => (
                                <div key={i} className="att-report-warning-item">
                                    <AlertTriangle size={14} /> {w}
                                </div>
                            ))}
                        </div>
                    )}

                    {consolidatedData.departments.length === 0 ? (
                        <div className="att-report-empty">
                            Nenhum departamento com dados de assiduidade encontrado neste período.
                        </div>
                    ) : (
                        consolidatedData.departments.map((dept, deptIdx) => (
                            <div key={dept.departmentId} className={`att-report-department-section${deptIdx > 0 ? ' att-report-department-section-break' : ''}`}>
                                {/* Print-only department header (repeated per department for separated pages) */}
                                <div className="print-only att-print-doc-header">
                                    <div className="att-print-doc-header-top">
                                        <div className="att-print-doc-company">ALPLA Angola | Portal Gerencial</div>
                                        <div className="att-print-doc-generated">
                                            Gerado em: {formatDateDisplay(dept.generatedAt)} por {dept.generatedBy}
                                        </div>
                                    </div>
                                    <h1 className="att-print-doc-title">Relatório Mensal de Presenças</h1>
                                    <div className="att-print-doc-meta">
                                        <span><strong>Departamento:</strong> {dept.departmentName}</span>
                                        <span><strong>Período:</strong> {formatDateDisplay(dept.startDate)} a {formatDateDisplay(dept.endDate)}</span>
                                        {dept.daysFilter && dept.daysFilter !== 'all' && (
                                            <span><strong>Filtro:</strong> {dept.daysFilter === 'business' ? 'Dias Úteis' : 'Fins de Semana'}</span>
                                        )}
                                        <span><strong>Departamento {deptIdx + 1} de {consolidatedData.totalDepartments}</strong></span>
                                    </div>
                                    {attendanceActivity === 'active' && (
                                        <div className="att-print-doc-notice">
                                            * Funcionários sem marcação real de ponto há mais de 30 dias são excluídos por padrão.
                                        </div>
                                    )}
                                </div>

                                {/* Screen-only department section title */}
                                <div className="att-report-department-section-title screen-only">
                                    {dept.departmentName}
                                </div>

                                <div className="att-report-employees">
                                    {renderEmployeeList(dept.employees)}

                                    {/* Department subtotals */}
                                    <div className="att-report-department-totals">
                                        <h3>Totais — {dept.departmentName}</h3>
                                        <div className="totals-row">
                                            <span>H.Básicas: {formatMinutesToHours(dept.departmentGrandTotals.basicMinutes)}</span>
                                            <span>H.Extra: {formatMinutesToHours(dept.departmentGrandTotals.extraMinutes)}</span>
                                            <span className="font-bold">H.Totais: {formatMinutesToHours(dept.departmentGrandTotals.totalMinutes)}</span>
                                            <span className={dept.departmentGrandTotals.balanceMinutes < 0 ? 'balance-negative' : dept.departmentGrandTotals.balanceMinutes > 0 ? 'balance-positive' : ''}>Saldo: {formatMinutesToHours(dept.departmentGrandTotals.balanceMinutes)}</span>
                                        </div>
                                        <div className="totals-row totals-days">
                                            <span>Total Funcionários: {dept.employees.length}</span>
                                            <span>Dias Trab.: {dept.departmentGrandTotals.workedDays}</span>
                                            <span>Férias: {dept.departmentGrandTotals.vacationDays}</span>
                                            <span>Folgas: {dept.departmentGrandTotals.dayOffDays}</span>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        ))
                    )}

                    {/* Print Footer */}
                    <div className="att-report-footer">
                        <span>Portal Gerencial — Relatório consolidado gerado automaticamente. Somente leitura.</span>
                    </div>
                </div>
            )}
        </div>
    );

    // ─── Shared Employee List Renderer ───
    function renderEmployeeList(employees: AttendanceEmployeeReportDto[]) {
        return employees.map(employee => (
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
                                <td className={`col-balance${day.dailyBalance < 0 ? ' balance-negative' : day.dailyBalance > 0 ? ' balance-positive' : ''}`}>{formatMinutesToHours(day.dailyBalance)}</td>
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
                                <span className={`font-bold${mt.balanceMinutes < 0 ? ' balance-negative' : mt.balanceMinutes > 0 ? ' balance-positive' : ''}`}>Saldo: {formatMinutesToHours(mt.balanceMinutes)}</span>
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
                        <span className={employee.grandTotals.balanceMinutes < 0 ? 'balance-negative' : employee.grandTotals.balanceMinutes > 0 ? 'balance-positive' : ''}>Saldo: {formatMinutesToHours(employee.grandTotals.balanceMinutes)}</span>
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
        ));
    }
}
