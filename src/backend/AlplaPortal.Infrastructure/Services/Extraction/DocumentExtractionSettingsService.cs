using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http;

namespace AlplaPortal.Infrastructure.Services.Extraction;

public class DocumentExtractionSettingsService : IDocumentExtractionSettingsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DocumentExtractionOptions _configOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DocumentExtractionSettingsService> _logger;
    private readonly AdminLogWriter _adminLog;

    private const string Source = nameof(DocumentExtractionSettingsService);

    public DocumentExtractionSettingsService(
        ApplicationDbContext dbContext,
        IOptions<DocumentExtractionOptions> configOptions,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DocumentExtractionSettingsService> logger,
        AdminLogWriter adminLog)
    {
        _dbContext = dbContext;
        _configOptions = configOptions.Value;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _adminLog = adminLog;
    }

    public async Task<DocumentExtractionOptions> GetEffectiveSettingsAsync(CancellationToken ct = default)
    {
        var dbSettings = await GetDbSettingsAsync(ct);

        if (dbSettings == null)
        {
            return _configOptions;
        }

        return new DocumentExtractionOptions
        {
            IsEnabled = dbSettings.IsEnabled ?? _configOptions.IsEnabled,
            DefaultProvider = dbSettings.DefaultProvider ?? _configOptions.DefaultProvider,
            GlobalTimeoutSeconds = dbSettings.GlobalTimeoutSeconds ?? _configOptions.GlobalTimeoutSeconds,

            LocalOcr = new ProviderSettings
            {
                Enabled = dbSettings.LocalOcrEnabled ?? _configOptions.LocalOcr.Enabled,
                BaseUrl = dbSettings.LocalOcrBaseUrl ?? _configOptions.LocalOcr.BaseUrl,
                TimeoutSeconds = dbSettings.LocalOcrTimeoutSeconds ?? _configOptions.LocalOcr.TimeoutSeconds
            },

            OpenAi = new OpenAiSettings
            {
                Enabled = dbSettings.OpenAiEnabled ?? _configOptions.OpenAi.Enabled,
                TimeoutSeconds = dbSettings.OpenAiTimeoutSeconds ?? _configOptions.OpenAi.TimeoutSeconds,
                Endpoint = _configOptions.OpenAi.Endpoint,
                Model = dbSettings.OpenAiModel ?? _configOptions.OpenAi.Model,
                DeploymentName = _configOptions.OpenAi.DeploymentName
            },

            AzureDocumentIntelligence = new AzureSettings
            {
                Enabled = dbSettings.AzureDocumentIntelligenceEnabled ?? _configOptions.AzureDocumentIntelligence.Enabled,
                TimeoutSeconds = dbSettings.AzureDocumentIntelligenceTimeoutSeconds ?? _configOptions.AzureDocumentIntelligence.TimeoutSeconds,
                Endpoint = _configOptions.AzureDocumentIntelligence.Endpoint
            }
        };
    }

    public async Task<DocumentExtractionSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var dbSettings = await GetDbSettingsAsync(ct);

        return new DocumentExtractionSettingsDto
        {
            IsEnabled = dbSettings?.IsEnabled ?? _configOptions.IsEnabled,
            DefaultProvider = dbSettings?.DefaultProvider ?? _configOptions.DefaultProvider,
            GlobalTimeoutSeconds = dbSettings?.GlobalTimeoutSeconds ?? _configOptions.GlobalTimeoutSeconds,

            LocalOcrEnabled = dbSettings?.LocalOcrEnabled ?? _configOptions.LocalOcr.Enabled,
            LocalOcrBaseUrl = dbSettings?.LocalOcrBaseUrl ?? _configOptions.LocalOcr.BaseUrl,
            LocalOcrTimeoutSeconds = dbSettings?.LocalOcrTimeoutSeconds ?? _configOptions.LocalOcr.TimeoutSeconds,

            OpenAiEnabled = dbSettings?.OpenAiEnabled ?? _configOptions.OpenAi.Enabled,
            OpenAiModel = dbSettings?.OpenAiModel ?? _configOptions.OpenAi.Model,
            OpenAiTimeoutSeconds = dbSettings?.OpenAiTimeoutSeconds ?? _configOptions.OpenAi.TimeoutSeconds,

            AzureDocumentIntelligenceEnabled = dbSettings?.AzureDocumentIntelligenceEnabled ?? _configOptions.AzureDocumentIntelligence.Enabled,
            AzureDocumentIntelligenceTimeoutSeconds = dbSettings?.AzureDocumentIntelligenceTimeoutSeconds ?? _configOptions.AzureDocumentIntelligence.TimeoutSeconds
        };
    }

    public async Task UpdateSettingsAsync(DocumentExtractionSettingsDto dto, CancellationToken ct = default)
    {
        // --- Validation (predictable business failures → Warning, not Error) ---
        try
        {
            ValidateSettings(dto);
        }
        catch (ArgumentException ex)
        {
            // Not a backend exception — a predictable validation failure.
            _logger.LogWarning("OCR settings validation failed: {Message}", ex.Message);
            await _adminLog.WriteAsync(
                level: "Warning",
                source: Source,
                eventType: "OCR_SETTINGS_VALIDATION_FAILED",
                message: $"Validação das configurações OCR falhou: {ex.Message}",
                payload: SafePayload.From(new { dto.DefaultProvider, dto.IsEnabled }));
            throw;
        }

        // --- Persistence ---
        try
        {
            var dbSettings = await GetDbSettingsAsync(ct);

            if (dbSettings == null)
            {
                dbSettings = new DocumentExtractionSettings { CreatedAtUtc = DateTime.UtcNow };
                _dbContext.DocumentExtractionSettings.Add(dbSettings);
            }

            dbSettings.IsEnabled = dto.IsEnabled;
            dbSettings.DefaultProvider = dto.DefaultProvider;
            dbSettings.GlobalTimeoutSeconds = dto.GlobalTimeoutSeconds;

            dbSettings.LocalOcrEnabled = dto.LocalOcrEnabled;
            dbSettings.LocalOcrBaseUrl = dto.LocalOcrBaseUrl;
            dbSettings.LocalOcrTimeoutSeconds = dto.LocalOcrTimeoutSeconds;

            dbSettings.OpenAiEnabled = dto.OpenAiEnabled;
            dbSettings.OpenAiModel = dto.OpenAiModel;
            dbSettings.OpenAiTimeoutSeconds = dto.OpenAiTimeoutSeconds;

            dbSettings.AzureDocumentIntelligenceEnabled = dto.AzureDocumentIntelligenceEnabled;
            dbSettings.AzureDocumentIntelligenceTimeoutSeconds = dto.AzureDocumentIntelligenceTimeoutSeconds;

            dbSettings.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("OCR settings updated. Provider: {Provider}, Enabled: {IsEnabled}", dto.DefaultProvider, dto.IsEnabled);

            await _adminLog.WriteAsync(
                level: "Information",
                source: Source,
                eventType: "OCR_SETTINGS_SAVED",
                message: $"Configurações OCR guardadas com sucesso. Provedor: {dto.DefaultProvider}, Ativo: {dto.IsEnabled}.",
                payload: SafePayload.From(new { dto.DefaultProvider, dto.IsEnabled, dto.LocalOcrEnabled, dto.OpenAiEnabled, dto.AzureDocumentIntelligenceEnabled }));
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "Failed to save OCR settings. Provider: {Provider}", dto.DefaultProvider);

            await _adminLog.WriteAsync(
                level: "Error",
                source: Source,
                eventType: "OCR_SETTINGS_SAVE_FAILED",
                message: $"Falha ao guardar configurações OCR. Provedor: {dto.DefaultProvider}.",
                exceptionDetail: $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}",
                payload: SafePayload.From(new { dto.DefaultProvider, dto.IsEnabled }));
            throw;
        }
    }

    private async Task<DocumentExtractionSettings?> GetDbSettingsAsync(CancellationToken ct)
    {
        return await _dbContext.DocumentExtractionSettings
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
    }

    private void ValidateSettings(DocumentExtractionSettingsDto dto)
    {
        if (dto.GlobalTimeoutSeconds <= 0) throw new ArgumentException("O timeout global deve ser um valor positivo.");

        var supportedProviders = new[] { "LOCAL_OCR", "OPENAI", "AZURE_DOCUMENT_INTELLIGENCE" };
        if (!supportedProviders.Contains(dto.DefaultProvider.ToUpperInvariant()))
            throw new ArgumentException($"O provedor '{dto.DefaultProvider}' não é suportado.");

        // Check if active provider is enabled
        bool isProviderEnabled = dto.DefaultProvider.ToUpperInvariant() switch
        {
            "LOCAL_OCR" => dto.LocalOcrEnabled,
            "OPENAI" => dto.OpenAiEnabled,
            "AZURE_DOCUMENT_INTELLIGENCE" => dto.AzureDocumentIntelligenceEnabled,
            _ => false
        };

        if (!isProviderEnabled)
            throw new ArgumentException("Não é possível definir um provedor como ativo se ele estiver desativado.");

        if (dto.LocalOcrEnabled)
        {
            if (string.IsNullOrWhiteSpace(dto.LocalOcrBaseUrl))
                throw new ArgumentException("A URL base para o Local OCR é obrigatória quando o provedor está ativado.");
            
            if (dto.LocalOcrTimeoutSeconds <= 0)
                throw new ArgumentException("O timeout do Local OCR deve ser um valor positivo.");
        }

        if (dto.OpenAiEnabled && dto.OpenAiTimeoutSeconds <= 0)
            throw new ArgumentException("O timeout do OpenAI deve ser um valor positivo.");

        if (dto.AzureDocumentIntelligenceEnabled && dto.AzureDocumentIntelligenceTimeoutSeconds <= 0)
            throw new ArgumentException("O timeout do Azure Document Intelligence deve ser um valor positivo.");
    }

    public async Task<ConnectionTestResultDto> TestConnectionAsync(CancellationToken ct = default)
    {
        var options = await GetEffectiveSettingsAsync(ct);
        var provider = options.DefaultProvider?.ToUpperInvariant();

        if (string.IsNullOrEmpty(provider))
        {
            return new ConnectionTestResultDto
            {
                Success = false,
                Message = "Nenhum provedor selecionado como ativo."
            };
        }

        if (!options.IsEnabled)
        {
            return new ConnectionTestResultDto
                {
                    Success = false,
                    ProviderName = provider,
                    Message = "A extração de documentos está desativada globalmente."
                };
        }

        return provider switch
        {
            "LOCAL_OCR" => await TestLocalOcrConnectionAsync(options.LocalOcr, ct),
            "OPENAI" => await TestOpenAiConnectionAsync(options.OpenAi, ct),
            "AZURE_DOCUMENT_INTELLIGENCE" => new ConnectionTestResultDto 
            { 
                Success = false, 
                ProviderName = "Azure Document Intelligence", 
                Message = "Provedor Azure Document Intelligence ainda não está implementado (Apenas Placeholder)." 
            },
            _ => new ConnectionTestResultDto 
            { 
                Success = false, 
                ProviderName = provider, 
                Message = $"Provedor '{provider}' não suportado para testes." 
            }
        };
    }

    private async Task<ConnectionTestResultDto> TestLocalOcrConnectionAsync(ProviderSettings settings, CancellationToken ct)
    {
        if (!settings.Enabled)
        {
            return new ConnectionTestResultDto { Success = false, ProviderName = "Local OCR", Message = "O provedor Local OCR está desativado." };
        }

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return new ConnectionTestResultDto { Success = false, ProviderName = "Local OCR", Message = "URL base do Local OCR não configurada." };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Min(settings.TimeoutSeconds ?? 30, 10));

            var response = await client.GetAsync(settings.BaseUrl, ct);
            sw.Stop();

            _logger.LogInformation("Local OCR connection test OK. Status: {StatusCode}, Time: {Elapsed}ms", response.StatusCode, sw.ElapsedMilliseconds);

            await _adminLog.WriteAsync(
                level: "Information",
                source: Source,
                eventType: "OCR_PROVIDER_TEST_OK",
                message: $"Teste de conexão Local OCR bem-sucedido. Status HTTP: {(int)response.StatusCode}. Tempo: {sw.ElapsedMilliseconds}ms.",
                payload: SafePayload.From(new { Provider = "LOCAL_OCR", BaseUrl = settings.BaseUrl, StatusCode = (int)response.StatusCode, ResponseTimeMs = sw.ElapsedMilliseconds }));

            return new ConnectionTestResultDto { Success = true, ProviderName = "Local OCR", Message = $"Conexão com Local OCR estabelecida ({response.StatusCode}).", ResponseTimeMs = sw.ElapsedMilliseconds };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("Local OCR connection test timed out. URL: {Url}", settings.BaseUrl);

            await _adminLog.WriteAsync(
                level: "Warning",
                source: Source,
                eventType: "OCR_PROVIDER_TEST_FAILED",
                message: "Teste de conexão Local OCR falhou: timeout.",
                payload: SafePayload.From(new { Provider = "LOCAL_OCR", BaseUrl = settings.BaseUrl, FailureReason = "Timeout" }));

            return new ConnectionTestResultDto { Success = false, ProviderName = "Local OCR", Message = "Timeout ao tentar conectar ao Local OCR." };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Local OCR connection test failed. URL: {Url}", settings.BaseUrl);

            await _adminLog.WriteAsync(
                level: "Error",
                source: Source,
                eventType: "OCR_PROVIDER_TEST_FAILED",
                message: $"Teste de conexão Local OCR falhou: {ex.Message}",
                exceptionDetail: $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}",
                payload: SafePayload.From(new { Provider = "LOCAL_OCR", BaseUrl = settings.BaseUrl }));

            return new ConnectionTestResultDto { Success = false, ProviderName = "Local OCR", Message = $"Falha ao conectar ao Local OCR: {ex.Message}" };
        }
    }

    private async Task<ConnectionTestResultDto> TestOpenAiConnectionAsync(OpenAiSettings settings, CancellationToken ct)
    {
        if (!settings.Enabled)
        {
            return new ConnectionTestResultDto { Success = false, ProviderName = "OpenAI", Message = "O provedor OpenAI está desativado." };
        }

        var apiKey = _configuration["OPENAI_API_KEY"];
        var isApiKeyPresent = !string.IsNullOrWhiteSpace(apiKey);
        var isModelPresent = !string.IsNullOrWhiteSpace(settings.Model);

        if (!isApiKeyPresent || !isModelPresent)
        {
            var missing = new List<string>();
            if (!isApiKeyPresent) missing.Add("OPENAI_API_KEY (Environment/Config)");
            if (!isModelPresent) missing.Add("Model");

            await _adminLog.WriteAsync(
                level: "Warning",
                source: Source,
                eventType: "OCR_PROVIDER_TEST_FAILED",
                message: $"Teste de conexão OpenAI falhou: configuração incompleta. Faltando: {string.Join(", ", missing)}.",
                payload: SafePayload.From(new { Provider = "OPENAI", Model = settings.Model, MissingFields = missing }));

            return new ConnectionTestResultDto { Success = false, ProviderName = "OpenAI", Message = $"Configuração de OpenAI incompleta. Faltando: {string.Join(", ", missing)}." };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync("https://api.openai.com/v1/models", ct);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("OpenAI connection test OK. Model: {Model}, Time: {Elapsed}ms", settings.Model, sw.ElapsedMilliseconds);

                await _adminLog.WriteAsync(
                    level: "Information",
                    source: Source,
                    eventType: "OCR_PROVIDER_TEST_OK",
                    message: $"Teste de conexão OpenAI bem-sucedido. Modelo: {settings.Model}. Tempo: {sw.ElapsedMilliseconds}ms.",
                    payload: SafePayload.From(new { Provider = "OPENAI", Model = settings.Model, ResponseTimeMs = sw.ElapsedMilliseconds }));

                return new ConnectionTestResultDto { Success = true, ProviderName = "OpenAI", Message = "Conexão com OpenAI estabelecida com sucesso.", ResponseTimeMs = sw.ElapsedMilliseconds };
            }

            _logger.LogWarning("OpenAI connection test failed. Status: {StatusCode}, Model: {Model}", response.StatusCode, settings.Model);

            await _adminLog.WriteAsync(
                level: "Warning",
                source: Source,
                eventType: "OCR_PROVIDER_TEST_FAILED",
                message: $"Teste de conexão OpenAI falhou. Status HTTP: {(int)response.StatusCode}.",
                payload: SafePayload.From(new { Provider = "OPENAI", Model = settings.Model, StatusCode = (int)response.StatusCode }));

            return new ConnectionTestResultDto { Success = false, ProviderName = "OpenAI", Message = $"Falha ao conectar à OpenAI: {response.StatusCode}.", ResponseTimeMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "OpenAI connection test error. Model: {Model}", settings.Model);

            await _adminLog.WriteAsync(
                level: "Error",
                source: Source,
                eventType: "OCR_PROVIDER_TEST_FAILED",
                message: $"Erro ao testar conexão OpenAI: {ex.Message}",
                exceptionDetail: $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}",
                payload: SafePayload.From(new { Provider = "OPENAI", Model = settings.Model }));

            return new ConnectionTestResultDto { Success = false, ProviderName = "OpenAI", Message = $"Erro ao testar conexão OpenAI: {ex.Message}" };
        }
    }
}
