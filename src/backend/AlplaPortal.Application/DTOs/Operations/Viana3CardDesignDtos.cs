using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

// ═══════════════════════════════════════════════════════════════════════
// Design document (schema 1) — display preferences only, no production data
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Visual design of the items inside a machine card (600×440 design units, scaled uniformly into the card box).</summary>
public class Viana3CardDesignDocumentDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = Viana3CardDesignRules.SchemaVersion;

    [JsonPropertyName("canvasWidth")] public int CanvasWidth { get; set; } = Viana3CardDesignRules.CanvasWidth;

    [JsonPropertyName("canvasHeight")] public int CanvasHeight { get; set; } = Viana3CardDesignRules.CanvasHeight;

    /// <summary>Card background colour (#rrggbb or #rrggbbaa); null = default surface.</summary>
    [JsonPropertyName("background")] public string? Background { get; set; }

    [JsonPropertyName("items")] public List<Viana3CardDesignItemDto> Items { get; set; } = new();
}

public class Viana3CardDesignItemDto
{
    /// <summary>Catalogue key (e.g. "reported") or a free item key "free-text-1" … "free-image-6".</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;

    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("w")] public double W { get; set; }
    [JsonPropertyName("h")] public double H { get; set; }

    /// <summary>Degrees, −180…180. Only honoured on the illustration, images and free text.</summary>
    [JsonPropertyName("rotation")] public double Rotation { get; set; }

    [JsonPropertyName("z")] public int Z { get; set; }

    [JsonPropertyName("style")] public Viana3CardDesignStyleDto Style { get; set; } = new();

    [JsonPropertyName("props")] public Viana3CardDesignPropsDto Props { get; set; } = new();
}

public class Viana3CardDesignStyleDto
{
    /// <summary>#rrggbb / #rrggbbaa; null = item default.</summary>
    [JsonPropertyName("color")] public string? Color { get; set; }

    [JsonPropertyName("background")] public string? Background { get; set; }

    [JsonPropertyName("borderColor")] public string? BorderColor { get; set; }

    /// <summary>Accent colour (bar fill, chip tint, pallet colour); null = identity colour of the entity.</summary>
    [JsonPropertyName("accent")] public string? Accent { get; set; }

    [JsonPropertyName("fontSize")] public double? FontSize { get; set; }

    /// <summary>400 | 600 | 700 | 800</summary>
    [JsonPropertyName("fontWeight")] public int? FontWeight { get; set; }

    /// <summary>LEFT | CENTER | RIGHT</summary>
    [JsonPropertyName("align")] public string? Align { get; set; }

    [JsonPropertyName("opacity")] public double? Opacity { get; set; }

    [JsonPropertyName("radius")] public double? Radius { get; set; }
}

public class Viana3CardDesignPropsDto
{
    /// <summary>Label shown next to / above the value (null = catalogue default).</summary>
    [JsonPropertyName("label")] public string? Label { get; set; }

    /// <summary>INT | THOUSANDS | COMPACT | DECIMAL1 (presentation only; values are never changed).</summary>
    [JsonPropertyName("numberFormat")] public string? NumberFormat { get; set; }

    [JsonPropertyName("unitSuffix")] public string? UnitSuffix { get; set; }

    /// <summary>Show the secondary line (unit estimate, positions, timestamps…).</summary>
    [JsonPropertyName("showSecondary")] public bool? ShowSecondary { get; set; }

    /// <summary>Free text content.</summary>
    [JsonPropertyName("text")] public string? Text { get; set; }

    /// <summary>Uploaded asset id (illustration replacement or free image).</summary>
    [JsonPropertyName("imageAssetId")] public Guid? ImageAssetId { get; set; }

    /// <summary>CONTAIN | COVER</summary>
    [JsonPropertyName("imageFit")] public string? ImageFit { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
// Editor / TV contracts
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Design of one machine (or the template) for the editor.</summary>
public class Viana3CardDesignDto
{
    /// <summary>0 = template.</summary>
    [JsonPropertyName("machineId")] public int MachineId { get; set; }

    [JsonPropertyName("isTemplate")] public bool IsTemplate { get; set; }

    /// <summary>True when a row exists for this machine (own design); false = follows the template / built-in card.</summary>
    [JsonPropertyName("isPersisted")] public bool IsPersisted { get; set; }

    /// <summary>OWN | TEMPLATE | BUILTIN — what the TV renders for this machine today.</summary>
    [JsonPropertyName("effectiveSource")] public string EffectiveSource { get; set; } = "BUILTIN";

    /// <summary>The document the editor should start from (own, template copy, or generated default).</summary>
    [JsonPropertyName("design")] public Viana3CardDesignDocumentDto Design { get; set; } = new();

    [JsonPropertyName("updatedAtUtc")] public DateTime? UpdatedAtUtc { get; set; }

    [JsonPropertyName("updatedByName")] public string? UpdatedByName { get; set; }
}

/// <summary>Anonymous bundle read by the TV with every refresh: template + machine overrides, no audit names.</summary>
public class Viana3CardDesignBundleDto
{
    [JsonPropertyName("template")] public Viana3CardDesignDocumentDto? Template { get; set; }

    [JsonPropertyName("machines")] public List<Viana3CardDesignBundleEntryDto> Machines { get; set; } = new();
}

public class Viana3CardDesignBundleEntryDto
{
    [JsonPropertyName("machineId")] public int MachineId { get; set; }

    [JsonPropertyName("design")] public Viana3CardDesignDocumentDto Design { get; set; } = new();
}

/// <summary>Body of PUT …/card-design/{machineId} and …/card-design/template.</summary>
public class SaveViana3CardDesignRequest
{
    [JsonPropertyName("design")] public Viana3CardDesignDocumentDto Design { get; set; } = new();

    /// <summary>Client-generated idempotency key of this save.</summary>
    [JsonPropertyName("saveToken")] public Guid SaveToken { get; set; }

    /// <summary>UpdatedAtUtc the editor loaded (null for a first save); mismatch → 409 DESIGN_MODIFIED.</summary>
    [JsonPropertyName("expectedUpdatedAtUtc")] public DateTime? ExpectedUpdatedAtUtc { get; set; }
}

// ─── Assets ───

public class Viana3DesignAssetDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
    [JsonPropertyName("contentType")] public string ContentType { get; set; } = string.Empty;
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; set; }
    [JsonPropertyName("createdByName")] public string? CreatedByName { get; set; }
    /// <summary>Designs that reference the asset (0 = template).</summary>
    [JsonPropertyName("referencedBy")] public List<int> ReferencedBy { get; set; } = new();
    [JsonPropertyName("fileMissing")] public bool FileMissing { get; set; }
    /// <summary>Served path (anonymous).</summary>
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
}

public class Viana3DesignAssetListDto
{
    [JsonPropertyName("assets")] public List<Viana3DesignAssetDto> Assets { get; set; } = new();
    [JsonPropertyName("activeCount")] public int ActiveCount { get; set; }
    [JsonPropertyName("quota")] public int Quota { get; set; } = Viana3CardDesignRules.AssetQuota;
    [JsonPropertyName("pendingPurge")] public int PendingPurge { get; set; }
    [JsonPropertyName("missingFiles")] public int MissingFiles { get; set; }
    [JsonPropertyName("maxSizeBytes")] public long MaxSizeBytes { get; set; } = Viana3CardDesignRules.AssetMaxBytes;
    [JsonPropertyName("maxDimension")] public int MaxDimension { get; set; } = Viana3CardDesignRules.AssetMaxDimension;
}

public class Viana3DesignAssetUploadResultDto
{
    [JsonPropertyName("asset")] public Viana3DesignAssetDto Asset { get; set; } = new();
    /// <summary>True when identical content already existed and was reused.</summary>
    [JsonPropertyName("reused")] public bool Reused { get; set; }
}

public class Viana3DesignCleanupResultDto
{
    [JsonPropertyName("purged")] public int Purged { get; set; }
    [JsonPropertyName("tombstoned")] public int Tombstoned { get; set; }
    [JsonPropertyName("orphanFilesRemoved")] public int OrphanFilesRemoved { get; set; }
    [JsonPropertyName("missingFiles")] public int MissingFiles { get; set; }
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = new();
}

// ─── Catalogue (read-only data-source metadata) ───

public class Viana3CardItemCatalogDto
{
    [JsonPropertyName("items")] public List<Viana3CardItemDto> Items { get; set; } = new();
    [JsonPropertyName("verifiedAtUtc")] public DateTime VerifiedAtUtc { get; set; }
    [JsonPropertyName("sourceDatabase")] public string SourceDatabase { get; set; } = string.Empty;
    [JsonPropertyName("maxFreeItems")] public int MaxFreeItems { get; set; } = Viana3CardDesignRules.MaxFreeItems;
}

public class Viana3CardItemDto
{
    [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("group")] public string Group { get; set; } = string.Empty;
    /// <summary>TEXT | NUMBER | BAR | CHIP | ILLUSTRATION | PALLETS | LIST | WASTE</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("dataDriven")] public bool DataDriven { get; set; }
    [JsonPropertyName("rotatable")] public bool Rotatable { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("sources")] public List<Viana3CardItemSourceDto> Sources { get; set; } = new();
    [JsonPropertyName("calculation")] public string? Calculation { get; set; }
    [JsonPropertyName("caveats")] public List<string> Caveats { get; set; } = new();
    [JsonPropertyName("configuredValues")] public Dictionary<string, string> ConfiguredValues { get; set; } = new();
}

/// <summary>One source figure of an item, verified against the real query text at runtime.</summary>
public class Viana3CardItemSourceDto
{
    /// <summary>JSON path on the machine panel payload, e.g. "current.reportedBottles".</summary>
    [JsonPropertyName("payloadField")] public string PayloadField { get; set; } = string.Empty;
    /// <summary>AlplaPROD table, e.g. "T_ProduktionsRueckmeldungDaten".</summary>
    [JsonPropertyName("table")] public string Table { get; set; } = string.Empty;
    [JsonPropertyName("field")] public string Field { get; set; } = string.Empty;
    [JsonPropertyName("filters")] public List<string> Filters { get; set; } = new();
    /// <summary>"Q1" | "Q2" | "SERVICE" — where the figure is produced.</summary>
    [JsonPropertyName("query")] public string Query { get; set; } = string.Empty;
    /// <summary>Output alias in that query (or method name in the service).</summary>
    [JsonPropertyName("alias")] public string Alias { get; set; } = string.Empty;
    /// <summary>VERIFIED | NOT_VERIFIED</summary>
    [JsonPropertyName("verification")] public string Verification { get; set; } = "NOT_VERIFIED";
    [JsonPropertyName("verificationDetail")] public string? VerificationDetail { get; set; }
    /// <summary>Expression extracted from the rendered query at verification time (read-only, optional detail).</summary>
    [JsonPropertyName("expression")] public string? Expression { get; set; }
}

/// <summary>Limits and enumerations shared by the service and documented for the frontend.</summary>
public static class Viana3CardDesignRules
{
    public const int SchemaVersion = 1;
    /// <summary>Default (and minimum) canvas width in design units; a design may widen its canvas up to <see cref="CanvasMaxWidth"/>.</summary>
    public const int CanvasWidth = 600;
    public const int CanvasMinWidth = 600;
    public const int CanvasMaxWidth = 1200;
    /// <summary>Canvas height is fixed: widening never rescales or moves existing items.</summary>
    public const int CanvasHeight = 440;
    public const int MaxItems = 40;
    public const int MaxFreeItems = 6;
    public const int MaxLabelLength = 60;
    public const int MaxTextLength = 200;
    public const double MinSize = 8;
    public const double MinFontSize = 6;
    public const double MaxFontSize = 96;
    public const int AssetQuota = 50;
    public const long AssetMaxBytes = 2 * 1024 * 1024;
    public const int AssetMaxDimension = 2048;
    public const int AssetUploadRequestLimitBytes = 3 * 1024 * 1024;
    public static readonly IReadOnlyList<string> NumberFormats = new[] { "INT", "THOUSANDS", "COMPACT", "DECIMAL1" };
    public static readonly IReadOnlyList<string> Aligns = new[] { "LEFT", "CENTER", "RIGHT" };
    public static readonly IReadOnlyList<int> FontWeights = new[] { 400, 600, 700, 800 };
    public static readonly IReadOnlyList<string> ImageFits = new[] { "CONTAIN", "COVER" };
    public static readonly IReadOnlyList<string> AssetContentTypes = new[] { "image/png", "image/jpeg", "image/webp" };
    public const string AssetUrlBase = "/api/operations/viana3-monitor/card-design/assets/";
}
