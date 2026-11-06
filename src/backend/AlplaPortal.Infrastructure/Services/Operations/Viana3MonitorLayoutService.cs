using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Operations;

/// <summary>
/// Persists the TV screens of the Viana 3 Production Monitor in OperationsBoardLayouts
/// (BoardKey VIANA3_PRODUCTION_MONITOR, one row per screen).
///
/// • The default screen ("default") backs the historical TV URL. Without a row it is
///   served virtually (discovery order, every machine visible); it can be renamed and
///   reset, never deleted.
/// • Named screens get a generated, stable, URL-safe key; their layout is independent.
/// • Save: the document is validated (columns, sizes, unique positive machine ids, card
///   count) and re-serialized from the validated model — the raw request body is never stored.
/// • Hiding or removing a card is a display decision in this document; it never touches AlplaPROD.
///
/// Every write records an AdminLog entry (best effort).
/// </summary>
public class Viana3MonitorLayoutService : IViana3MonitorLayoutService
{
    private const string BoardKey = OperationsBoardKeys.Viana3ProductionMonitor;
    private const string DefaultKey = OperationsBoardKeys.DefaultScreenKey;

    private static readonly Regex ScreenKeyPattern = new("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.Compiled);
    private const string KeyAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    private readonly ApplicationDbContext _db;
    private readonly AdminLogWriter _adminLog;
    private readonly ILogger<Viana3MonitorLayoutService> _logger;

    public Viana3MonitorLayoutService(ApplicationDbContext db, AdminLogWriter adminLog, ILogger<Viana3MonitorLayoutService> logger)
    {
        _db = db;
        _adminLog = adminLog;
        _logger = logger;
    }

    /// <summary>Stored document (schemaVersion 1).</summary>
    private sealed class StoredLayout
    {
        public int SchemaVersion { get; set; } = Viana3MonitorLayoutRules.SchemaVersion;
        public int Columns { get; set; } = Viana3MonitorLayoutRules.DefaultColumns;
        public bool IncludeNewMachines { get; set; } = true;
        public List<Viana3MonitorLayoutCardDto> Cards { get; set; } = new();
    }

    // ─── Reads ───

    public async Task<IReadOnlyList<Viana3MonitorScreenDto>> ListScreensAsync(CancellationToken ct = default)
    {
        var rows = await _db.OperationsBoardLayouts
            .AsNoTracking()
            .Include(l => l.UpdatedByUser)
            .Where(l => l.BoardKey == BoardKey)
            .ToListAsync(ct);

        var screens = rows.Select(ToScreenDto).ToList();
        if (!screens.Any(s => s.IsDefault))
            screens.Insert(0, ToScreenDto(VirtualDefault()));

        return screens
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<Viana3MonitorLayoutDto?> GetLayoutAsync(string screenKey, CancellationToken ct = default)
    {
        var key = NormalizeKey(screenKey);
        if (key is null) return null;

        var row = await _db.OperationsBoardLayouts
            .AsNoTracking()
            .Include(l => l.UpdatedByUser)
            .FirstOrDefaultAsync(l => l.BoardKey == BoardKey && l.ScreenKey == key, ct);

        if (row is null) return key == DefaultKey ? VirtualDefault() : null;
        return ToLayoutDto(row);
    }

    // ─── Writes ───

    public async Task<Viana3MonitorLayoutDto> CreateScreenAsync(CreateViana3MonitorScreenRequest request, Guid userId, CancellationToken ct = default)
    {
        var name = ValidateName(request?.Name);
        var existing = await _db.OperationsBoardLayouts.Where(l => l.BoardKey == BoardKey).ToListAsync(ct);
        var screenCount = existing.Count + (existing.Any(l => l.IsDefault) ? 0 : 1);
        if (screenCount >= Viana3MonitorLayoutRules.MaxScreens)
            throw new Viana3MonitorLayoutValidationException($"Já existem {Viana3MonitorLayoutRules.MaxScreens} ecrãs; elimine um antes de criar outro.");
        EnsureNameUnique(existing, name, exceptScreenKey: null);

        var layout = new StoredLayout { IncludeNewMachines = false };
        if (!string.IsNullOrWhiteSpace(request!.CopyFromScreenKey))
        {
            var sourceKey = NormalizeKey(request.CopyFromScreenKey)
                ?? throw new Viana3MonitorLayoutValidationException("Ecrã de origem inválido.");
            var source = existing.FirstOrDefault(l => l.ScreenKey == sourceKey);
            if (source is not null) layout = ReadStored(source) ?? layout;
            else if (sourceKey != DefaultKey) throw new Viana3MonitorLayoutValidationException("O ecrã de origem não existe.");
            // copying the virtual default = empty selection + include new machines (same as the TV shows today)
            else layout = new StoredLayout { IncludeNewMachines = true };
        }

        var key = GenerateKey(existing.Select(l => l.ScreenKey).ToHashSet());
        var now = DateTime.UtcNow;
        var row = new OperationsBoardLayout
        {
            BoardKey = BoardKey,
            ScreenKey = key,
            Name = name,
            IsDefault = false,
            SchemaVersion = layout.SchemaVersion,
            LayoutJson = JsonSerializer.Serialize(layout, JsonOptions),
            UpdatedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.OperationsBoardLayouts.Add(row);
        await _db.SaveChangesAsync(ct);

        await _adminLog.WriteAsync("Info", nameof(Viana3MonitorLayoutService), "VIANA3_MONITOR_SCREEN_CREATED",
            $"Viana 3 monitor screen created: '{name}' ({key}), {layout.Cards.Count} cards", payload: row.LayoutJson);

        return (await GetLayoutAsync(key, ct))!;
    }

    public async Task<Viana3MonitorScreenDto?> RenameScreenAsync(string screenKey, RenameViana3MonitorScreenRequest request, Guid userId, CancellationToken ct = default)
    {
        var key = NormalizeKey(screenKey);
        if (key is null) return null;
        var name = ValidateName(request?.Name);

        var rows = await _db.OperationsBoardLayouts.Where(l => l.BoardKey == BoardKey).ToListAsync(ct);
        EnsureNameUnique(rows, name, exceptScreenKey: key);

        var row = rows.FirstOrDefault(l => l.ScreenKey == key);
        if (row is null)
        {
            if (key != DefaultKey) return null;
            row = NewDefaultRow(userId);
            _db.OperationsBoardLayouts.Add(row);
        }

        var previous = row.Name;
        row.Name = name;
        row.UpdatedByUserId = userId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _adminLog.WriteAsync("Info", nameof(Viana3MonitorLayoutService), "VIANA3_MONITOR_SCREEN_RENAMED",
            $"Viana 3 monitor screen {key} renamed from '{previous}' to '{name}'");

        return ToScreenDto((await LoadRowAsync(key, ct))!);
    }

    public async Task<Viana3MonitorLayoutDto?> SaveLayoutAsync(string screenKey, SaveViana3MonitorLayoutRequest request, Guid userId, CancellationToken ct = default)
    {
        var key = NormalizeKey(screenKey);
        if (key is null) return null;

        var validated = Validate(request);
        var json = JsonSerializer.Serialize(validated, JsonOptions);
        var now = DateTime.UtcNow;

        var row = await _db.OperationsBoardLayouts.FirstOrDefaultAsync(l => l.BoardKey == BoardKey && l.ScreenKey == key, ct);
        if (row is null)
        {
            if (key != DefaultKey) return null;
            row = NewDefaultRow(userId);
            _db.OperationsBoardLayouts.Add(row);
        }

        row.SchemaVersion = validated.SchemaVersion;
        row.LayoutJson = json;
        row.UpdatedByUserId = userId;
        row.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(ct);

        await _adminLog.WriteAsync("Info", nameof(Viana3MonitorLayoutService), "VIANA3_MONITOR_LAYOUT_SAVED",
            $"Viana 3 monitor layout saved for screen {key}: {validated.Cards.Count} cards ({validated.Cards.Count(c => c.Hidden)} hidden), {validated.Columns} columns, includeNewMachines={validated.IncludeNewMachines}",
            payload: json);

        return await GetLayoutAsync(key, ct);
    }

    public async Task<Viana3MonitorLayoutDto?> ResetLayoutAsync(string screenKey, Guid userId, CancellationToken ct = default)
    {
        var key = NormalizeKey(screenKey);
        if (key is null) return null;

        var row = await _db.OperationsBoardLayouts.FirstOrDefaultAsync(l => l.BoardKey == BoardKey && l.ScreenKey == key, ct);
        if (row is null) return key == DefaultKey ? VirtualDefault() : null;

        if (row.IsDefault)
        {
            _db.OperationsBoardLayouts.Remove(row);
        }
        else
        {
            row.LayoutJson = JsonSerializer.Serialize(new StoredLayout { IncludeNewMachines = false }, JsonOptions);
            row.SchemaVersion = Viana3MonitorLayoutRules.SchemaVersion;
            row.UpdatedByUserId = userId;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        await _adminLog.WriteAsync("Info", nameof(Viana3MonitorLayoutService), "VIANA3_MONITOR_LAYOUT_RESET",
            $"Viana 3 monitor layout reset for screen {key} by user {userId}");

        return await GetLayoutAsync(key, ct);
    }

    public async Task<bool> DeleteScreenAsync(string screenKey, Guid userId, CancellationToken ct = default)
    {
        var key = NormalizeKey(screenKey);
        if (key is null) return false;
        if (key == DefaultKey)
            throw new Viana3MonitorLayoutValidationException("O ecrã principal não pode ser eliminado; pode ser renomeado ou reposto.");

        var row = await _db.OperationsBoardLayouts.FirstOrDefaultAsync(l => l.BoardKey == BoardKey && l.ScreenKey == key, ct);
        if (row is null) return false;
        if (row.IsDefault)
            throw new Viana3MonitorLayoutValidationException("O ecrã principal não pode ser eliminado; pode ser renomeado ou reposto.");

        _db.OperationsBoardLayouts.Remove(row);
        await _db.SaveChangesAsync(ct);

        await _adminLog.WriteAsync("Info", nameof(Viana3MonitorLayoutService), "VIANA3_MONITOR_SCREEN_DELETED",
            $"Viana 3 monitor screen deleted: '{row.Name}' ({key}) by user {userId}", payload: row.LayoutJson);

        return true;
    }

    // ─── Helpers ───

    private Task<OperationsBoardLayout?> LoadRowAsync(string key, CancellationToken ct) =>
        _db.OperationsBoardLayouts.AsNoTracking().Include(l => l.UpdatedByUser)
            .FirstOrDefaultAsync(l => l.BoardKey == BoardKey && l.ScreenKey == key, ct);

    private static OperationsBoardLayout NewDefaultRow(Guid userId)
    {
        var now = DateTime.UtcNow;
        return new OperationsBoardLayout
        {
            BoardKey = BoardKey,
            ScreenKey = DefaultKey,
            Name = Viana3MonitorLayoutRules.DefaultScreenName,
            IsDefault = true,
            SchemaVersion = Viana3MonitorLayoutRules.SchemaVersion,
            LayoutJson = JsonSerializer.Serialize(new StoredLayout(), JsonOptions),
            UpdatedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static string? NormalizeKey(string? screenKey)
    {
        var key = (screenKey ?? string.Empty).Trim().ToLowerInvariant();
        return ScreenKeyPattern.IsMatch(key) ? key : null;
    }

    private static string GenerateKey(HashSet<string> taken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++) chars[i] = KeyAlphabet[RandomNumberGenerator.GetInt32(KeyAlphabet.Length)];
            var key = "s" + new string(chars);
            if (key != DefaultKey && !taken.Contains(key)) return key;
        }
        throw new InvalidOperationException("Could not generate a unique screen key.");
    }

    private static string ValidateName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length == 0) throw new Viana3MonitorLayoutValidationException("O nome do ecrã é obrigatório.");
        if (name.Length > Viana3MonitorLayoutRules.MaxNameLength)
            throw new Viana3MonitorLayoutValidationException($"O nome do ecrã não pode exceder {Viana3MonitorLayoutRules.MaxNameLength} caracteres.");
        return name;
    }

    private static void EnsureNameUnique(IEnumerable<OperationsBoardLayout> rows, string name, string? exceptScreenKey)
    {
        var clash = rows.Any(l => l.ScreenKey != exceptScreenKey && string.Equals(l.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (!clash && exceptScreenKey != DefaultKey && !rows.Any(l => l.IsDefault)
            && string.Equals(name, Viana3MonitorLayoutRules.DefaultScreenName, StringComparison.CurrentCultureIgnoreCase))
            clash = true; // the virtual default screen still owns its name
        if (clash) throw new Viana3MonitorLayoutValidationException($"Já existe um ecrã chamado «{name}».");
    }

    private StoredLayout? ReadStored(OperationsBoardLayout row)
    {
        try
        {
            return JsonSerializer.Deserialize<StoredLayout>(row.LayoutJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            // A corrupt document must never take the TV down: fall back to an empty/default layout.
            _logger.LogError(ex, "OperationsBoardLayout {BoardKey}/{ScreenKey} holds an unreadable document; serving the default layout", BoardKey, row.ScreenKey);
            return null;
        }
    }

    private Viana3MonitorLayoutDto ToLayoutDto(OperationsBoardLayout row)
    {
        var stored = ReadStored(row) ?? new StoredLayout { IncludeNewMachines = row.IsDefault };
        return new Viana3MonitorLayoutDto
        {
            ScreenKey = row.ScreenKey,
            Name = row.Name,
            IsDefault = row.IsDefault,
            IsPersisted = true,
            IncludeNewMachines = stored.IncludeNewMachines,
            SchemaVersion = stored.SchemaVersion,
            Columns = ClampColumns(stored.Columns),
            Cards = stored.Cards.Select(Clone).ToList(),
            UpdatedAtUtc = row.UpdatedAtUtc,
            UpdatedByName = row.UpdatedByUser?.FullName,
            TvPath = Viana3MonitorLayoutRules.TvPathFor(row.ScreenKey),
        };
    }

    private Viana3MonitorScreenDto ToScreenDto(OperationsBoardLayout row) => ToScreenDto(ToLayoutDto(row));

    private static Viana3MonitorScreenDto ToScreenDto(Viana3MonitorLayoutDto l) => new()
    {
        ScreenKey = l.ScreenKey,
        Name = l.Name,
        IsDefault = l.IsDefault,
        IsPersisted = l.IsPersisted,
        IncludeNewMachines = l.IncludeNewMachines,
        Columns = l.Columns,
        CardCount = l.Cards.Count,
        VisibleCount = l.Cards.Count(c => !c.Hidden),
        UpdatedAtUtc = l.UpdatedAtUtc,
        UpdatedByName = l.UpdatedByName,
        TvPath = l.TvPath,
    };

    private static StoredLayout Validate(SaveViana3MonitorLayoutRequest request)
    {
        if (request is null) throw new Viana3MonitorLayoutValidationException("Layout em falta.");

        if (request.Columns < Viana3MonitorLayoutRules.MinColumns || request.Columns > Viana3MonitorLayoutRules.MaxColumns)
            throw new Viana3MonitorLayoutValidationException(
                $"O número de colunas deve estar entre {Viana3MonitorLayoutRules.MinColumns} e {Viana3MonitorLayoutRules.MaxColumns}.");

        var cards = request.Cards ?? new List<Viana3MonitorLayoutCardDto>();
        if (cards.Count > Viana3MonitorLayoutRules.MaxCards)
            throw new Viana3MonitorLayoutValidationException($"O layout não pode ter mais de {Viana3MonitorLayoutRules.MaxCards} cartões.");

        var seen = new HashSet<int>();
        var result = new StoredLayout { Columns = request.Columns, IncludeNewMachines = request.IncludeNewMachines };
        foreach (var card in cards)
        {
            if (card is null) throw new Viana3MonitorLayoutValidationException("Cartão inválido.");
            if (card.MachineId <= 0)
                throw new Viana3MonitorLayoutValidationException("Cada cartão tem de referir uma máquina válida.");
            if (!seen.Add(card.MachineId))
                throw new Viana3MonitorLayoutValidationException($"A máquina {card.MachineId} aparece mais do que uma vez no layout.");

            var size = (card.Size ?? string.Empty).Trim().ToUpperInvariant();
            if (!Viana3MonitorLayoutRules.Sizes.Contains(size))
                throw new Viana3MonitorLayoutValidationException(
                    $"Tamanho de cartão inválido para a máquina {card.MachineId}. Valores aceites: {string.Join(", ", Viana3MonitorLayoutRules.Sizes)}.");

            result.Cards.Add(new Viana3MonitorLayoutCardDto { MachineId = card.MachineId, Size = size, Hidden = card.Hidden });
        }

        return result;
    }

    private static int ClampColumns(int columns) =>
        Math.Clamp(columns, Viana3MonitorLayoutRules.MinColumns, Viana3MonitorLayoutRules.MaxColumns);

    private static Viana3MonitorLayoutCardDto Clone(Viana3MonitorLayoutCardDto c) =>
        new() { MachineId = c.MachineId, Size = c.Size, Hidden = c.Hidden };

    private static Viana3MonitorLayoutDto VirtualDefault() => new()
    {
        ScreenKey = DefaultKey,
        Name = Viana3MonitorLayoutRules.DefaultScreenName,
        IsDefault = true,
        IsPersisted = false,
        IncludeNewMachines = true,
        SchemaVersion = Viana3MonitorLayoutRules.SchemaVersion,
        Columns = Viana3MonitorLayoutRules.DefaultColumns,
        Cards = new List<Viana3MonitorLayoutCardDto>(),
        UpdatedAtUtc = null,
        UpdatedByName = null,
        TvPath = Viana3MonitorLayoutRules.TvPathFor(DefaultKey),
    };
}
