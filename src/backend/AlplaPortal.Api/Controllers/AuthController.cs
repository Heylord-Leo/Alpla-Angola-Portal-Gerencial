using AlplaPortal.Application.Interfaces;
using AlplaPortal.Application.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace AlplaPortal.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _config;

    public AuthController(IAuthService authService, IConfiguration config)
    {
        _authService = authService;
        _config = config;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);
        if (response == null)
        {
            return Unauthorized(new { message = "E-mail ou palavra-passe incorretos." });
        }

        return Ok(response);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();

        var userId = Guid.Parse(userIdClaim.Value);
        var success = await _authService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);

        if (!success)
        {
            return BadRequest(new { message = "Falha ao alterar a palavra-passe. Verifique os dados introduzidos." });
        }

        return Ok(new { message = "Palavra-passe alterada com sucesso." });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var baseUrl = _config["AppConfig:FrontendBaseUrl"];
        if (string.IsNullOrEmpty(baseUrl))
        {
            return StatusCode(500, new { message = "A configuração do URL base do Frontend está em falta no servidor." });
        }

        await _authService.ForgotPasswordAsync(request.Email, baseUrl);
        
        return Ok(new { message = "Se o seu e-mail estiver registado, receberá as instruções para redefinição em breve." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var success = await _authService.ResetPasswordWithTokenAsync(request.Email, request.Token, request.NewPassword);
        
        if (!success)
        {
            return BadRequest(new { message = "Link de recuperação inválido ou expirado. Por favor, solicite um novo." });
        }

        return Ok(new { message = "A sua Palavra-passe foi atualizada com sucesso. Pode agora iniciar sessão." });
    }
}
