using System;
using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests;

public class ApprovalFinancialTrendDto
{
    public Guid RequestId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Resolution { get; set; } = "MONTH"; // "MONTH" or "WEEK"
    public string Scope { get; set; } = "PLANT"; // "PLANT" or "DEPARTMENT"
    public List<FinancialTrendPointDto> DataPoints { get; set; } = new();
}

public class FinancialTrendPointDto
{
    public string PeriodLabel { get; set; } = string.Empty; // e.g. "03/2026" or "Semana 14"
    public DateTime SortDate { get; set; } // Only used for sorting payload
    public decimal ApprovedAmount { get; set; } // Pendente de Pagamento
    public int ApprovedCount { get; set; }
    public decimal PaidAmount { get; set; }    // Já pago
    public int PaidCount { get; set; }
}
