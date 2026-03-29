using System.ComponentModel.DataAnnotations;

namespace AlplaPortal.Application.DTOs.Requests;

public class CancelRequestDto
{
    [Required(ErrorMessage = "O motivo do cancelamento é obrigatório.")]
    public string Reason { get; set; } = string.Empty;
}
