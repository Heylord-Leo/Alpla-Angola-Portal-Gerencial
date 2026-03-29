namespace AlplaPortal.Application.DTOs.Requests;

using System;
using System.Collections.Generic;

public class AttentionPointDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public string TargetPath { get; set; } = string.Empty;
    public string Type { get; set; } = "INFO"; // INFO, WARNING, DANGER, SUCCESS
}

public class PurchasingSummaryDto
{
    public int TotalActiveRequests { get; set; }
    public int WaitingQuotation { get; set; }
    public int AwaitingApproval { get; set; }
    public int AwaitingPayment { get; set; }
    public int PendingReceiving { get; set; }
    public List<AttentionPointDto> AttentionPoints { get; set; } = new();
}
