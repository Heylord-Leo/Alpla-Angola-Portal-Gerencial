using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.HR
{
    /// <summary>
    /// Consolidated report wrapper containing multiple department reports.
    /// Returned when departmentId is null/0 (all departments).
    /// </summary>
    public class AttendanceConsolidatedReportDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? DaysFilter { get; set; }
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = "";
        public int TotalDepartments { get; set; }
        public int TotalEmployees { get; set; }
        public List<AttendanceDepartmentMonthlyReportDto> Departments { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class AttendanceDepartmentMonthlyReportDto
    {
        public int DepartmentId { get; set; }
        public string DepartmentName { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? DaysFilter { get; set; }
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = "";
        
        public List<AttendanceEmployeeReportDto> Employees { get; set; } = new();
        public List<AttendanceMonthlySummaryDto> DepartmentMonthlyTotals { get; set; } = new();
        public AttendanceReportTotalsDto DepartmentGrandTotals { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class AttendanceEmployeeReportDto
    {
        public int InnuxId { get; set; }
        public string? EmployeeId { get; set; }
        public string Name { get; set; } = "";
        public string DepartmentName { get; set; } = "";
        public string? PlantName { get; set; }
        
        public List<AttendanceDailyRecordDto> DailyRecords { get; set; } = new();
        public List<AttendanceMonthlySummaryDto> MonthlyTotals { get; set; } = new();
        public AttendanceReportTotalsDto GrandTotals { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class AttendanceDailyRecordDto
    {
        public DateTime Date { get; set; }
        public string Weekday { get; set; } = "";
        
        public string? Entrada1 { get; set; }
        public string? Saida1 { get; set; }
        public string? Entrada2 { get; set; }
        public string? Saida2 { get; set; }
        public string? Entrada3 { get; set; }
        public string? Saida3 { get; set; }
        public string? Entrada4 { get; set; }
        public string? Saida4 { get; set; }
        
        public int BasicMinutes { get; set; }
        public int ExtraMinutes { get; set; }
        public int UnpaidMinutes { get; set; }
        public int TotalMinutes { get; set; }
        public int MissingMinutes { get; set; }
        public int AbsenceMinutes { get; set; }
        
        public string? AbsenceDescription { get; set; }
        public string? Justification { get; set; }
        public int DailyBalance { get; set; }
        public string? Status { get; set; }
        
        public bool IsDayOff { get; set; }
        public bool IsVacation { get; set; }
        public bool IsHoliday { get; set; }
        public bool HasMissingPunch { get; set; }
        public bool HasInconsistentData { get; set; }
        public bool IsPortalInterpreted { get; set; }
        public string? WarningMessage { get; set; }

        /// <summary>Whether the day has a direction-related warning (ambiguous code, Portal interpretation, etc.).</summary>
        public bool HasDirectionWarning { get; set; }

        /// <summary>Portuguese direction warning message for the UI tooltip.</summary>
        public string? DirectionWarningMessage { get; set; }

        /// <summary>
        /// Estimated worked minutes calculated by the Portal from interpreted entry/exit punches.
        /// Only populated when Status = "PunchWithoutPeriod" (Innux has no processed work period
        /// but Portal-interpreted punches show a valid entry/exit pair).
        /// NOT official — for diagnostic/tooltip display only.
        /// </summary>
        public int PortalEstimatedMinutes { get; set; }
    }

    public class AttendanceMonthlySummaryDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthLabel { get; set; } = "";
        
        public int BasicMinutes { get; set; }
        public int ExtraMinutes { get; set; }
        public int UnpaidMinutes { get; set; }
        public int TotalMinutes { get; set; }
        public int MissingMinutes { get; set; }
        public int AbsenceMinutes { get; set; }
        
        public int VacationDays { get; set; }
        public int DayOffDays { get; set; }
        public int WorkedDays { get; set; }
        public int MissingPunchDays { get; set; }
        public int InconsistentDays { get; set; }
        public int BalanceMinutes { get; set; }
        
        // Reserved fields for future complete Innux alignment
        public string? SaldoPeriodoA { get; set; }
        public string? SaldoInicialB { get; set; }
        public string? CreditoUsadoC { get; set; }
        public string? DispensaUsadaD { get; set; }
        public string? Resultado { get; set; }
        public string? ATransportar { get; set; }
    }

    public class AttendanceReportTotalsDto
    {
        public int BasicMinutes { get; set; }
        public int ExtraMinutes { get; set; }
        public int UnpaidMinutes { get; set; }
        public int TotalMinutes { get; set; }
        public int MissingMinutes { get; set; }
        public int AbsenceMinutes { get; set; }
        
        public int VacationDays { get; set; }
        public int DayOffDays { get; set; }
        public int WorkedDays { get; set; }
        public int MissingPunchDays { get; set; }
        public int InconsistentDays { get; set; }
        public int BalanceMinutes { get; set; }
    }
}
