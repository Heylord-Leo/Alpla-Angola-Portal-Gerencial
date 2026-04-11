using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using AlplaPortal.Application.DTOs;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Interfaces;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

public class SmtpSettingsService : ISmtpSettingsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpSettingsService> _logger;

    public SmtpSettingsService(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ILogger<SmtpSettingsService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    private string EncryptionKey => _configuration["AppConfig:EncryptionKey"] ?? string.Empty;

    public async Task<SmtpSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var dbSettings = await GetDbSettingsAsync(ct);

        return new SmtpSettingsDto
        {
            Server = dbSettings?.Server ?? _configuration["SmtpSettings:Server"] ?? "",
            Port = dbSettings?.Port ?? (int.TryParse(_configuration["SmtpSettings:Port"], out var p) ? p : 587),
            SenderEmail = dbSettings?.SenderEmail ?? _configuration["SmtpSettings:SenderEmail"] ?? "",
            SenderName = dbSettings?.SenderName ?? _configuration["SmtpSettings:SenderName"] ?? "",
            EnableSsl = dbSettings?.EnableSsl ?? (bool.TryParse(_configuration["SmtpSettings:EnableSsl"], out var ssl) ? ssl : true),
            HasPassword = !string.IsNullOrEmpty(dbSettings?.EncryptedPassword) || !string.IsNullOrEmpty(_configuration["SmtpSettings:Password"]),
            Password = null // Never return the password
        };
    }

    public async Task<SmtpEffectiveSettings> GetEffectiveSettingsAsync(CancellationToken ct = default)
    {
        var dbSettings = await GetDbSettingsAsync(ct);

        var settings = new SmtpEffectiveSettings
        {
            Server = dbSettings?.Server ?? _configuration["SmtpSettings:Server"] ?? "",
            Port = dbSettings?.Port ?? (int.TryParse(_configuration["SmtpSettings:Port"], out var p) ? p : 587),
            SenderEmail = dbSettings?.SenderEmail ?? _configuration["SmtpSettings:SenderEmail"] ?? "",
            SenderName = dbSettings?.SenderName ?? _configuration["SmtpSettings:SenderName"] ?? "",
            EnableSsl = dbSettings?.EnableSsl ?? (bool.TryParse(_configuration["SmtpSettings:EnableSsl"], out var ssl) ? ssl : true)
        };

        // Password resolution: DB (encrypted) → appsettings (plaintext)
        if (!string.IsNullOrEmpty(dbSettings?.EncryptedPassword))
        {
            try
            {
                settings.Password = AesEncryptionHelper.Decrypt(dbSettings.EncryptedPassword, EncryptionKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt SMTP password from database. Falling back to appsettings.");
                settings.Password = _configuration["SmtpSettings:Password"] ?? "";
            }
        }
        else
        {
            settings.Password = _configuration["SmtpSettings:Password"] ?? "";
        }

        return settings;
    }

    public async Task UpdateSettingsAsync(SmtpSettingsDto dto, CancellationToken ct = default)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(dto.Server))
            throw new ArgumentException("O endereço do servidor SMTP é obrigatório.");

        if (dto.Port is null or <= 0 or > 65535)
            throw new ArgumentException("A porta SMTP deve ser um valor entre 1 e 65535.");

        if (string.IsNullOrWhiteSpace(dto.SenderEmail))
            throw new ArgumentException("O e-mail do remetente é obrigatório.");

        var dbSettings = await GetDbSettingsAsync(ct);

        if (dbSettings == null)
        {
            dbSettings = new SmtpSettings { CreatedAtUtc = DateTime.UtcNow };
            _dbContext.SmtpSettings.Add(dbSettings);
        }

        dbSettings.Server = dto.Server;
        dbSettings.Port = dto.Port;
        dbSettings.SenderEmail = dto.SenderEmail;
        dbSettings.SenderName = dto.SenderName;
        dbSettings.EnableSsl = dto.EnableSsl;
        dbSettings.UpdatedAtUtc = DateTime.UtcNow;

        // Only update password if a new value is provided
        if (!string.IsNullOrEmpty(dto.Password))
        {
            dbSettings.EncryptedPassword = AesEncryptionHelper.Encrypt(dto.Password, EncryptionKey);
            _logger.LogInformation("SMTP password updated (encrypted).");
        }

        await _dbContext.SaveChangesAsync(ct);
        _logger.LogInformation("SMTP settings updated. Server: {Server}, Port: {Port}", dto.Server, dto.Port);
    }

    public async Task<ConnectionTestResultDto> TestConnectionAsync(CancellationToken ct = default)
    {
        var effective = await GetEffectiveSettingsAsync(ct);

        if (string.IsNullOrEmpty(effective.Server))
        {
            return new ConnectionTestResultDto
            {
                Success = false,
                ProviderName = "SMTP",
                Message = "Servidor SMTP não configurado."
            };
        }

        if (string.IsNullOrEmpty(effective.Password))
        {
            return new ConnectionTestResultDto
            {
                Success = false,
                ProviderName = "SMTP",
                Message = "Senha SMTP não configurada. Configure a senha antes de testar."
            };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new SmtpClient(effective.Server, effective.Port)
            {
                Credentials = new NetworkCredential(effective.SenderEmail, effective.Password),
                EnableSsl = effective.EnableSsl,
                Timeout = 10000 // 10 second timeout for test
            };

            // Send a NOOP-equivalent by connecting (SmtpClient connects lazily on first send).
            // We'll send a real test message to the sender's own address.
            using var message = new MailMessage(
                new MailAddress(effective.SenderEmail, effective.SenderName),
                new MailAddress(effective.SenderEmail))
            {
                Subject = "ALPLA Portal - Teste de Conexão SMTP",
                Body = "Este é um e-mail de teste automático gerado pelo Portal Gerencial ALPLA para validar as configurações SMTP.",
                IsBodyHtml = false
            };

            await client.SendMailAsync(message, ct);
            sw.Stop();

            _logger.LogInformation("SMTP connection test OK. Server: {Server}, Port: {Port}, Time: {Elapsed}ms",
                effective.Server, effective.Port, sw.ElapsedMilliseconds);

            return new ConnectionTestResultDto
            {
                Success = true,
                ProviderName = "SMTP",
                Message = $"Conexão SMTP estabelecida com sucesso. E-mail de teste enviado para {effective.SenderEmail}.",
                ResponseTimeMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "SMTP connection test failed. Server: {Server}, Port: {Port}", effective.Server, effective.Port);

            return new ConnectionTestResultDto
            {
                Success = false,
                ProviderName = "SMTP",
                Message = $"Falha na conexão SMTP: {ex.Message}",
                ResponseTimeMs = sw.ElapsedMilliseconds
            };
        }
    }

    private async Task<SmtpSettings?> GetDbSettingsAsync(CancellationToken ct)
    {
        return await _dbContext.SmtpSettings
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
    }
}
