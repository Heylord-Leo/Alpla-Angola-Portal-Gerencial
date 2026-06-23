using System.Net.Mail;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlplaPortal.Api.Controllers;

/// <summary>
/// Manages Accounts Payable notification email configurations.
/// Accessible only to System Administrators via Master Data module.
/// </summary>
[ApiController]
[Route("api/v1/ap-notification-configs")]
[Authorize(Roles = "System Administrator")]
public class AccountsPayableConfigController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AccountsPayableConfigController> _logger;

    public AccountsPayableConfigController(ApplicationDbContext context, ILogger<AccountsPayableConfigController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lists all AP notification configurations with company name.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var configs = await _context.AccountsPayableNotificationConfigs
            .AsNoTracking()
            .Include(c => c.Company)
            .OrderBy(c => c.Company.Name)
            .Select(c => new
            {
                c.Id,
                c.CompanyId,
                CompanyName = c.Company.Name,
                c.Email,
                c.CcEmails,
                c.Label,
                c.IsActive,
                c.NotifyOnScheduled,
                c.NotifyOnCompleted,
                c.CreatedAtUtc,
                c.UpdatedAtUtc
            })
            .ToListAsync();

        return Ok(configs);
    }

    /// <summary>
    /// Creates a new AP notification configuration for a company.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApConfigDto dto)
    {
        // Validate company exists
        var companyExists = await _context.Companies.AnyAsync(c => c.Id == dto.CompanyId);
        if (!companyExists)
            return BadRequest(new ProblemDetails { Title = "Empresa Inválida", Detail = "A empresa selecionada não existe.", Status = 400 });

        // Check for existing config for this company
        var exists = await _context.AccountsPayableNotificationConfigs.AnyAsync(c => c.CompanyId == dto.CompanyId);
        if (exists)
            return Conflict(new ProblemDetails { Title = "Configuração Duplicada", Detail = "Já existe uma configuração de Contas a Pagar para esta empresa.", Status = 409 });

        // Validate primary email
        if (string.IsNullOrWhiteSpace(dto.Email) || !IsValidEmail(dto.Email))
            return BadRequest(new ProblemDetails { Title = "E-mail Inválido", Detail = "O e-mail principal é obrigatório e deve ter um formato válido.", Status = 400 });

        // Validate CC emails
        var ccValidationError = ValidateCcEmails(dto.CcEmails);
        if (ccValidationError != null)
            return BadRequest(new ProblemDetails { Title = "E-mail CC Inválido", Detail = ccValidationError, Status = 400 });

        var config = new AccountsPayableNotificationConfig
        {
            CompanyId = dto.CompanyId,
            Email = dto.Email.Trim(),
            CcEmails = NormalizeCcEmails(dto.CcEmails),
            Label = dto.Label?.Trim(),
            IsActive = true,
            NotifyOnScheduled = dto.NotifyOnScheduled,
            NotifyOnCompleted = dto.NotifyOnCompleted,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.AccountsPayableNotificationConfigs.Add(config);
        await _context.SaveChangesAsync();

        _logger.LogInformation("AP notification config created for CompanyId {CompanyId} with email {Email}", dto.CompanyId, dto.Email);

        return Ok(new
        {
            config.Id,
            config.CompanyId,
            config.Email,
            config.CcEmails,
            config.Label,
            config.IsActive,
            config.NotifyOnScheduled,
            config.NotifyOnCompleted
        });
    }

    /// <summary>
    /// Updates an existing AP notification configuration.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateApConfigDto dto)
    {
        var config = await _context.AccountsPayableNotificationConfigs.FindAsync(id);
        if (config == null) return NotFound();

        // Validate primary email
        if (string.IsNullOrWhiteSpace(dto.Email) || !IsValidEmail(dto.Email))
            return BadRequest(new ProblemDetails { Title = "E-mail Inválido", Detail = "O e-mail principal é obrigatório e deve ter um formato válido.", Status = 400 });

        // Validate CC emails
        var ccValidationError = ValidateCcEmails(dto.CcEmails);
        if (ccValidationError != null)
            return BadRequest(new ProblemDetails { Title = "E-mail CC Inválido", Detail = ccValidationError, Status = 400 });

        config.Email = dto.Email.Trim();
        config.CcEmails = NormalizeCcEmails(dto.CcEmails);
        config.Label = dto.Label?.Trim();
        config.NotifyOnScheduled = dto.NotifyOnScheduled;
        config.NotifyOnCompleted = dto.NotifyOnCompleted;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("AP notification config {ConfigId} updated for CompanyId {CompanyId}", id, config.CompanyId);
        return Ok();
    }

    /// <summary>
    /// Toggles the IsActive flag on an AP notification configuration.
    /// </summary>
    [HttpPut("{id:int}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var config = await _context.AccountsPayableNotificationConfigs.FindAsync(id);
        if (config == null) return NotFound();

        config.IsActive = !config.IsActive;
        config.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("AP notification config {ConfigId} toggled to IsActive={IsActive}", id, config.IsActive);
        return Ok(new { config.IsActive });
    }

    // ── Validation Helpers ──────────────────────────────────────────

    private static bool IsValidEmail(string email)
    {
        try { _ = new MailAddress(email.Trim()); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Validates CC emails. Returns null if valid, or an error message if invalid.
    /// </summary>
    private static string? ValidateCcEmails(string? ccEmails)
    {
        if (string.IsNullOrWhiteSpace(ccEmails)) return null;

        var addresses = ccEmails.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (addresses.Length > 10)
            return "O número máximo de e-mails CC é 10.";

        foreach (var addr in addresses)
        {
            if (!IsValidEmail(addr))
                return $"O endereço CC '{addr}' não é um e-mail válido.";
        }

        return null;
    }

    /// <summary>
    /// Normalizes CC emails: trims, removes empty entries, and joins with semicolon.
    /// Returns null if the result is empty.
    /// </summary>
    private static string? NormalizeCcEmails(string? ccEmails)
    {
        if (string.IsNullOrWhiteSpace(ccEmails)) return null;

        var addresses = ccEmails.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cleaned = string.Join("; ", addresses);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    // ── DTOs ────────────────────────────────────────────────────────

    public class CreateApConfigDto
    {
        public int CompanyId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? CcEmails { get; set; }
        public string? Label { get; set; }
        public bool NotifyOnScheduled { get; set; } = true;
        public bool NotifyOnCompleted { get; set; } = true;
    }

    public class UpdateApConfigDto
    {
        public string Email { get; set; } = string.Empty;
        public string? CcEmails { get; set; }
        public string? Label { get; set; }
        public bool NotifyOnScheduled { get; set; } = true;
        public bool NotifyOnCompleted { get; set; } = true;
    }
}
