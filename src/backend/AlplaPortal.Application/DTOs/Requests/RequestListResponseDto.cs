using AlplaPortal.Application.DTOs.Common;

namespace AlplaPortal.Application.DTOs.Requests;

public class RequestListResponseDto
{
    public PagedResult<RequestListItemDto> PagedResult { get; set; } = new();
    public DashboardSummaryDto Summary { get; set; } = new();
}
