using System;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Requests;

namespace AlplaPortal.Application.Interfaces;

public interface IApprovalIntelligenceService
{
    Task<ApprovalIntelligenceDto> GetIntelligenceAsync(Guid requestId);
    Task<List<HistoricalPurchaseRecordDto>> GetItemHistoryAsync(Guid requestId, Guid lineItemId);
}
