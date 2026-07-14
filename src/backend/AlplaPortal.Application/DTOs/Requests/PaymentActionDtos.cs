using System;
using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests
{
    public class ScheduleAdvancePaymentDto
    {
        [Required]
        public Guid RequestPoGroupId { get; set; }
        
        [Required]
        public DateTime ScheduledDate { get; set; }
        
        public string? Comment { get; set; }
    }

    public class ConfirmAdvancePaymentDto
    {
        [Required]
        public Guid RequestPoGroupId { get; set; }

        [Required]
        public Guid PaymentProofAttachmentId { get; set; }

        [Required]
        public decimal ActualPaidAmount { get; set; }

        [Required]
        public DateTime PaidDate { get; set; }

        public string? Comment { get; set; }
    }

    public class SchedulePaymentDto
    {
        [Required]
        public Guid RequestPoGroupId { get; set; }

        [Required]
        public DateTime ScheduledDate { get; set; }

        public string? Comment { get; set; }
    }

    public class ConfirmPaymentDto
    {
        [Required]
        public Guid RequestPoGroupId { get; set; }

        [Required]
        public Guid PaymentProofAttachmentId { get; set; }

        [Required]
        public decimal ActualPaidAmount { get; set; }

        [Required]
        public DateTime PaidDate { get; set; }

        public string? Comment { get; set; }
    }
}
