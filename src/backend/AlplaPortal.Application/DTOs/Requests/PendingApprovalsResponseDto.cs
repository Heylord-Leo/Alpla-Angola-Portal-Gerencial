using System.Collections.Generic;

namespace AlplaPortal.Application.DTOs.Requests
{
    /// <summary>
    /// Approval Center queue. Each list is one row per ACTIONABLE unit (an ApprovalBatch, or a
    /// request-level PAYMENT/legacy action) — never collapsed to one row per request. See
    /// <see cref="ApprovalQueueItemDto"/> for the queue-identity rule.
    /// </summary>
    public class PendingApprovalsResponseDto
    {
        public List<ApprovalQueueItemDto> AreaApprovals { get; set; } = new List<ApprovalQueueItemDto>();
        public List<ApprovalQueueItemDto> FinalApprovals { get; set; } = new List<ApprovalQueueItemDto>();
    }
}
