using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

/// <summary>
/// One TV screen of the Viana 3 Production Monitor (summary for the editor's screen list).
/// Contains no production data.
/// </summary>
public class Viana3MonitorScreenDto
{
    [JsonPropertyName("screenKey")] public string ScreenKey { get; set; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>The screen behind the historical TV URL; cannot be deleted.</summary>
    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }

    /// <summary>False until the screen (or the default layout) has been saved once.</summary>
    [JsonPropertyName("isPersisted")] public bool IsPersisted { get; set; }

    [JsonPropertyName("includeNewMachines")] public bool IncludeNewMachines { get; set; }

    [JsonPropertyName("columns")] public int Columns { get; set; }

    /// <summary>Cards in the document (visible + hidden).</summary>
    [JsonPropertyName("cardCount")] public int CardCount { get; set; }

    [JsonPropertyName("visibleCount")] public int VisibleCount { get; set; }

    [JsonPropertyName("updatedAtUtc")] public DateTime? UpdatedAtUtc { get; set; }

    [JsonPropertyName("updatedByName")] public string? UpdatedByName { get; set; }

    /// <summary>Portal path of the read-only TV page for this screen (append ?fullscreen=true on the device).</summary>
    [JsonPropertyName("tvPath")] public string TvPath { get; set; } = string.Empty;
}

/// <summary>
/// Shared layout of one screen (card order, sizes, hidden cards, columns, new-machine policy).
/// Served to the read-only TV and to the Portal layout editor. Contains no production data.
/// </summary>
public class Viana3MonitorLayoutDto
{
    [JsonPropertyName("screenKey")] public string ScreenKey { get; set; } = Viana3MonitorLayoutRules.DefaultScreenKey;

    [JsonPropertyName("name")] public string Name { get; set; } = Viana3MonitorLayoutRules.DefaultScreenName;

    [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }

    /// <summary>False when nothing was saved yet for this screen (default screen = discovery order).</summary>
    [JsonPropertyName("isPersisted")] public bool IsPersisted { get; set; }

    /// <summary>Machines discovered later are appended visible at standard size (true) or stay off the screen (false).</summary>
    [JsonPropertyName("includeNewMachines")] public bool IncludeNewMachines { get; set; } = true;

    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = Viana3MonitorLayoutRules.SchemaVersion;

    /// <summary>Grid columns of the 1920×1080 stage (2–4).</summary>
    [JsonPropertyName("columns")] public int Columns { get; set; } = Viana3MonitorLayoutRules.DefaultColumns;

    /// <summary>Cards in display order (visible + hidden). Machines absent from this list are not on the screen.</summary>
    [JsonPropertyName("cards")] public List<Viana3MonitorLayoutCardDto> Cards { get; set; } = new();

    [JsonPropertyName("updatedAtUtc")] public DateTime? UpdatedAtUtc { get; set; }

    [JsonPropertyName("updatedByName")] public string? UpdatedByName { get; set; }

    [JsonPropertyName("tvPath")] public string TvPath { get; set; } = string.Empty;
}

public class Viana3MonitorLayoutCardDto
{
    /// <summary>AlplaPROD machine id (T_LagerAbteilungen / T_ProdPlanung.IdMaschine).</summary>
    [JsonPropertyName("machineId")] public int MachineId { get; set; }

    /// <summary>COMPACT | STANDARD | WIDE | LARGE (see <see cref="Viana3MonitorLayoutRules.Sizes"/>).</summary>
    [JsonPropertyName("size")] public string Size { get; set; } = Viana3MonitorLayoutRules.DefaultSize;

    /// <summary>Hidden from the TV. Never deletes or alters machine data.</summary>
    [JsonPropertyName("hidden")] public bool Hidden { get; set; }
}

/// <summary>Body of PUT /api/operations/viana3-monitor/screens/{screenKey}/layout.</summary>
public class SaveViana3MonitorLayoutRequest
{
    [JsonPropertyName("columns")] public int Columns { get; set; } = Viana3MonitorLayoutRules.DefaultColumns;

    [JsonPropertyName("cards")] public List<Viana3MonitorLayoutCardDto> Cards { get; set; } = new();

    [JsonPropertyName("includeNewMachines")] public bool IncludeNewMachines { get; set; }
}

/// <summary>Body of POST /api/operations/viana3-monitor/screens.</summary>
public class CreateViana3MonitorScreenRequest
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>Optional: copy the layout (machines, order, sizes, columns) of an existing screen.</summary>
    [JsonPropertyName("copyFromScreenKey")] public string? CopyFromScreenKey { get; set; }
}

/// <summary>Body of PUT /api/operations/viana3-monitor/screens/{screenKey}.</summary>
public class RenameViana3MonitorScreenRequest
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

/// <summary>Validation rules shared by the service and documented for the frontend.</summary>
public static class Viana3MonitorLayoutRules
{
    public const int SchemaVersion = 1;
    public const int MinColumns = 2;
    public const int MaxColumns = 4;
    public const int DefaultColumns = 3;
    public const int MaxCards = 24;
    public const int MaxScreens = 12;
    public const int MaxNameLength = 80;
    public const string DefaultSize = "STANDARD";
    public const string DefaultScreenKey = "default";
    public const string DefaultScreenName = "Ecrã principal";
    public const string TvBasePath = "/operations/viana3-monitor";
    public static readonly IReadOnlyList<string> Sizes = new[] { "COMPACT", "STANDARD", "WIDE", "LARGE" };

    /// <summary>TV path of a screen (the default screen keeps the historical URL).</summary>
    public static string TvPathFor(string screenKey) =>
        screenKey == DefaultScreenKey ? TvBasePath : $"{TvBasePath}/screen/{screenKey}";
}
