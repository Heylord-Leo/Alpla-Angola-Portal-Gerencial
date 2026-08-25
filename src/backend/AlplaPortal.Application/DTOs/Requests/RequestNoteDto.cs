namespace AlplaPortal.Application.DTOs.Requests;

/// <summary>Body for the generic request-level note endpoint (POST /api/v1/requests/{id}/note).</summary>
public class RequestNoteDto
{
    public string Text { get; set; } = string.Empty;
}
