using System;

namespace AlplaPortal.Application.DTOs.Requests
{
    public class NotificationDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Type { get; set; } = "INFO"; // INFO, WARNING, SUCCESS, ERROR
        public string Category { get; set; } = "GENERAL"; // APPROVAL, QUOTATION, RECEIPT, PAYMENT
        public int Count { get; set; }
        public bool IsRead { get; set; }
        public bool IsOperational { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
