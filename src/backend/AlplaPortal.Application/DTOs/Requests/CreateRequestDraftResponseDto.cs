namespace AlplaPortal.Application.DTOs.Requests;

public class CreateRequestDraftResponseDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
