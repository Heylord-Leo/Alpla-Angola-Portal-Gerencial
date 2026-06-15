using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Generates unique Asset Codes for IT Equipment using the format:
/// {COMPANY_CODE}-{PLANT_CODE}-IT-{TYPE_SHORT_CODE}-{SEQUENCE:D6}
/// 
/// Example: APA-AOVIA1-IT-LAP-000001
/// 
/// Sequence is scoped per Company + Plant + Equipment Type.
/// Uses the SystemCounter table with key: IT_ASSET:{COMPANY_CODE}:{PLANT_CODE}:{TYPE_SHORT_CODE}
/// </summary>
public class ITAssetCodeGeneratorService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<ITAssetCodeGeneratorService> _logger;

    public ITAssetCodeGeneratorService(
        ApplicationDbContext context,
        IConfiguration config,
        ILogger<ITAssetCodeGeneratorService> logger)
    {
        _context = context;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Generates a new Asset Code for the given company, plant, and equipment type.
    /// Atomically increments the SystemCounter for the scoped key.
    /// </summary>
    public async Task<GeneratedAssetCode> GenerateAsync(
        int companyId,
        int plantId,
        string equipmentTypeCode,
        CancellationToken ct = default)
    {
        // 1. Resolve Company code
        var company = await _context.Set<Company>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new InvalidOperationException($"Company with Id {companyId} not found.");

        if (string.IsNullOrWhiteSpace(company.Code))
            throw new InvalidOperationException($"Company '{company.Name}' (Id={companyId}) does not have a Code configured.");

        // 2. Resolve Plant code
        var plant = await _context.Set<Plant>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == plantId, ct)
            ?? throw new InvalidOperationException($"Plant with Id {plantId} not found.");

        if (string.IsNullOrWhiteSpace(plant.Code))
            throw new InvalidOperationException($"Plant '{plant.Name}' (Id={plantId}) does not have a Code configured.");

        // Verify plant belongs to company
        if (plant.CompanyId != companyId)
            throw new InvalidOperationException($"Plant '{plant.Name}' does not belong to Company '{company.Name}'.");

        // 3. Resolve Equipment Type short code
        var eqType = await _context.Set<ITEquipmentType>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == equipmentTypeCode && t.IsActive, ct)
            ?? throw new InvalidOperationException($"Equipment type with Code '{equipmentTypeCode}' not found or inactive.");

        if (string.IsNullOrWhiteSpace(eqType.ShortCode))
            throw new InvalidOperationException($"Equipment type '{eqType.DisplayName}' (Code={eqType.Code}) does not have a ShortCode configured.");

        var companyCode = company.Code.Trim().ToUpperInvariant();
        var plantCode = plant.Code.Trim().ToUpperInvariant();
        var typeShortCode = eqType.ShortCode.Trim().ToUpperInvariant();

        // 4. Build counter key
        var counterKey = $"IT_ASSET:{companyCode}:{plantCode}:{typeShortCode}";

        // 5. Increment counter with retry for concurrency
        int sequenceNumber = await IncrementCounterAsync(counterKey, ct);

        // 6. Format Asset Code
        var assetCode = $"{companyCode}-{plantCode}-IT-{typeShortCode}-{sequenceNumber:D6}";

        // 7. Build QR Code URL (reuses FrontendBaseUrl — already configured per environment)
        var portalBaseUrl = _config["AppConfig:FrontendBaseUrl"]?.TrimEnd('/') ?? "";

        _logger.LogInformation(
            "Generated IT Asset Code: {AssetCode} (Counter={CounterKey}, Sequence={Sequence})",
            assetCode, counterKey, sequenceNumber);

        return new GeneratedAssetCode(
            AssetCode: assetCode,
            SequenceNumber: sequenceNumber,
            CompanyCode: companyCode,
            PlantCode: plantCode,
            TypeShortCode: typeShortCode,
            PortalBaseUrl: portalBaseUrl
        );
    }

    /// <summary>
    /// Atomically increments the SystemCounter for the given key.
    /// Creates the counter row if it doesn't exist.
    /// Retries up to 3 times on concurrency conflicts.
    /// </summary>
    private async Task<int> IncrementCounterAsync(string counterKey, CancellationToken ct)
    {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var counter = await _context.SystemCounters
                    .FirstOrDefaultAsync(sc => sc.Id == counterKey, ct);

                int seqNumber;

                if (counter == null)
                {
                    seqNumber = 1;
                    counter = new SystemCounter
                    {
                        Id = counterKey,
                        CurrentValue = seqNumber,
                        LastUpdatedUtc = DateTime.UtcNow
                    };
                    _context.SystemCounters.Add(counter);
                }
                else
                {
                    counter.CurrentValue++;
                    counter.LastUpdatedUtc = DateTime.UtcNow;
                    seqNumber = counter.CurrentValue;
                }

                await _context.SaveChangesAsync(ct);

                return seqNumber;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                _logger.LogWarning(
                    "Concurrency conflict on counter {CounterKey}, attempt {Attempt}/{MaxRetries}. Retrying...",
                    counterKey, attempt, maxRetries);

                // Detach tracked entities to force re-read
                foreach (var entry in _context.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                await Task.Delay(50 * attempt, ct); // brief backoff
            }
        }

        throw new InvalidOperationException($"Failed to increment counter '{counterKey}' after {maxRetries} attempts due to concurrency conflicts.");
    }
}

/// <summary>
/// Result of Asset Code generation containing all components for denormalization.
/// </summary>
public record GeneratedAssetCode(
    string AssetCode,
    int SequenceNumber,
    string CompanyCode,
    string PlantCode,
    string TypeShortCode,
    string PortalBaseUrl
)
{
    /// <summary>Builds the QR Code URL for the given equipment ID.</summary>
    public string BuildQrCodeUrl(Guid equipmentId)
        => string.IsNullOrEmpty(PortalBaseUrl)
            ? $"/it/equipment/{equipmentId}"
            : $"{PortalBaseUrl}/it/equipment/{equipmentId}";
}
