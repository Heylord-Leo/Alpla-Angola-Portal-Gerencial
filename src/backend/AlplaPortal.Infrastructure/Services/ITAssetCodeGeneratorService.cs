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
    /// Generates N sequential Asset Codes for the given company, plant, and equipment type.
    /// Atomically increments the SystemCounter by the requested quantity.
    /// </summary>
    public async Task<List<GeneratedAssetCode>> GenerateBatchAsync(
        int companyId,
        int plantId,
        string equipmentTypeCode,
        int quantity,
        CancellationToken ct = default)
    {
        if (quantity < 1 || quantity > 100)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be between 1 and 100.");

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

        // 5. Atomically increment counter by quantity
        int startSequence = await IncrementCounterByAsync(counterKey, quantity, ct);

        // 6. Build QR Code URL base
        var portalBaseUrl = _config["AppConfig:FrontendBaseUrl"]?.TrimEnd('/') ?? "";

        // 7. Generate all asset codes
        var results = new List<GeneratedAssetCode>(quantity);
        for (int i = 0; i < quantity; i++)
        {
            var seq = startSequence + i;
            var assetCode = $"{companyCode}-{plantCode}-IT-{typeShortCode}-{seq:D6}";
            results.Add(new GeneratedAssetCode(
                AssetCode: assetCode,
                SequenceNumber: seq,
                CompanyCode: companyCode,
                PlantCode: plantCode,
                TypeShortCode: typeShortCode,
                PortalBaseUrl: portalBaseUrl
            ));
        }

        _logger.LogInformation(
            "Generated {Count} IT Asset Codes: {First} to {Last} (Counter={CounterKey})",
            quantity, results[0].AssetCode, results[^1].AssetCode, counterKey);

        return results;
    }

    /// <summary>
    /// Atomically increments the SystemCounter for the given key.
    /// Creates the counter row if it doesn't exist.
    /// Retries up to 3 times on concurrency conflicts.
    /// </summary>
    private async Task<int> IncrementCounterAsync(string counterKey, CancellationToken ct)
    {
        return await IncrementCounterByAsync(counterKey, 1, ct);
    }

    /// <summary>
    /// Atomically increments the SystemCounter by the given amount.
    /// Returns the FIRST sequence number in the allocated range.
    /// E.g., if current=5 and amount=10, returns 6 (range is 6..15).
    /// </summary>
    private async Task<int> IncrementCounterByAsync(string counterKey, int amount, CancellationToken ct)
    {
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var counter = await _context.SystemCounters
                    .FirstOrDefaultAsync(sc => sc.Id == counterKey, ct);

                int startSequence;

                if (counter == null)
                {
                    startSequence = 1;
                    counter = new SystemCounter
                    {
                        Id = counterKey,
                        CurrentValue = amount,
                        LastUpdatedUtc = DateTime.UtcNow
                    };
                    _context.SystemCounters.Add(counter);
                }
                else
                {
                    startSequence = counter.CurrentValue + 1;
                    counter.CurrentValue += amount;
                    counter.LastUpdatedUtc = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync(ct);

                return startSequence;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                _logger.LogWarning(
                    "Concurrency conflict on counter {CounterKey}, attempt {Attempt}/{MaxRetries}. Retrying...",
                    counterKey, attempt, maxRetries);

                foreach (var entry in _context.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                await Task.Delay(50 * attempt, ct);
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
