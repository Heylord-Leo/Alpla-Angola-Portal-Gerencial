using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests
{
    public class PendingApprovalsResponseDto
    {
        public List<RequestListItemDto> AreaApprovals { get; set; } = new List<RequestListItemDto>();
        public List<RequestListItemDto> FinalApprovals { get; set; } = new List<RequestListItemDto>();
    }
}
