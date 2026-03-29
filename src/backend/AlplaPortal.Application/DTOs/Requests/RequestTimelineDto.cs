namespace AlplaPortal.Application.DTOs.Requests;

public class RequestTimelineDto
{
    public List<TimelineStepDto> Steps { get; set; } = new();
}

public class TimelineStepDto
{
    public string Label { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
    public string State { get; set; } = "pending"; // completed, current, pending, blocked
}
