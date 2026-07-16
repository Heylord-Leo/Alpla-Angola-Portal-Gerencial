using System;

namespace AlplaPortal.Application.DTOs.Requests
{
    public class ConfirmReceivingDto
    {
        public Guid RequestPoGroupId { get; set; }
        public string? Comment { get; set; }
    }
}
