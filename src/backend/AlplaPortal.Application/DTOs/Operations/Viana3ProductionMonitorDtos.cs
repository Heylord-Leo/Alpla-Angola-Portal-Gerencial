using System.Text.Json.Serialization;

namespace AlplaPortal.Application.DTOs.Operations;

/// <summary>
/// Root response for the Viana 3 Production Monitor (TV page).
///
/// GET /api/operations/viana3-monitor
///
/// Read-only projection of AlplaPROD_aovia3 (reference query
/// AlplaPROD_aovia3_tv_preform_monitor_v5.sql, Queries 1 and 2). Every figure
/// keeps its own source and is never mixed with another one:
///   • plannedBottles   — T_ProdPlanung.PlanMenge (plan)
///   • reportedBottles  — AlplaREPORT production report (manual)
///   • scannedPreforms  — scanner postings (reason 231, ops 1420/1460) — "scanned", never "consumed"
///   • waste            — AlplaLABEL bookings (reason 140 / originator 1100), kg recorded, units estimated
///   • deviation        — PROVISIONAL (1 preform = 1 bottle not confirmed), waste excluded
///
/// All AlplaPROD timestamps are Angola local time (server time zone UTC+1, no DST) and
/// are transported unchanged. The historical open system balance at the machine
/// areas (lots 2687/2700) is NOT part of this payload — see the diagnostics endpoint.
///
/// Design reference: docs/OPERATIONS_VIANA3_PRODUCTION_MONITOR.md
/// </summary>
public class Viana3MonitorResponseDto
{
    [JsonPropertyName("plant")]
    public string Plant { get; set; } = "VIANA3";

    [JsonPropertyName("plantName")]
    public string PlantName { get; set; } = "Viana 3";

    /// <summary>Suggested client refresh interval in seconds (60–300).</summary>
    [JsonPropertyName("refreshSeconds")]
    public int RefreshSeconds { get; set; }

    /// <summary>Where the figures in this response came from (live query, warm cache, or stale snapshot).</summary>
    [JsonPropertyName("source")]
    public Viana3MonitorSourceDto Source { get; set; } = new();

    [JsonPropertyName("machines")]
    public List<Viana3MachinePanelDto> Machines { get; set; } = new();

    /// <summary>Preform stock in Warehouse 3 (agreed locations only), grouped by preform article.</summary>
    [JsonPropertyName("preformStock")]
    public List<Viana3PreformStockDto> PreformStock { get; set; } = new();

    /// <summary>Configuration echo so the screen can state what it counts.</summary>
    [JsonPropertyName("scope")]
    public Viana3MonitorScopeDto Scope { get; set; } = new();

    /// <summary>Human-readable rule reminders shown on the TV footer.</summary>
    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();
}

/// <summary>Freshness and provenance of a monitor payload.</summary>
public class Viana3MonitorSourceDto
{
    /// <summary>LIVE = queried now; CACHED = last successful query, still within the refresh window; STALE = AlplaPROD unavailable, last successful snapshot shown.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "LIVE";

    /// <summary>When the figures were read from AlplaPROD (Angola local wall-clock, no offset).</summary>
    [JsonPropertyName("fetchedAtLocal")]
    public DateTime FetchedAtLocal { get; set; }

    /// <summary>The same instant in UTC.</summary>
    [JsonPropertyName("fetchedAtUtc")]
    public DateTime FetchedAtUtc { get; set; }

    /// <summary>Age of the figures in seconds at the time of the response.</summary>
    [JsonPropertyName("ageSeconds")]
    public int AgeSeconds { get; set; }

    /// <summary>Time-zone label of every local timestamp in the payload.</summary>
    [JsonPropertyName("timeZoneLabel")]
    public string TimeZoneLabel { get; set; } = string.Empty;

    /// <summary>Present only when status = STALE: why the source could not be read (no technical details).</summary>
    [JsonPropertyName("unavailableReason")]
    public string? UnavailableReason { get; set; }

    [JsonPropertyName("queryDurationMs")]
    public long QueryDurationMs { get; set; }
}

/// <summary>Identifiers the monitor is configured with, echoed for transparency on screen.</summary>
public class Viana3MonitorScopeDto
{
    [JsonPropertyName("warehouseId")]
    public int WarehouseId { get; set; }

    [JsonPropertyName("warehouseName")]
    public string WarehouseName { get; set; } = string.Empty;

    [JsonPropertyName("locationIds")]
    public List<int> LocationIds { get; set; } = new();

    [JsonPropertyName("retentionLocationId")]
    public int RetentionLocationId { get; set; }

    [JsonPropertyName("bufferLocationId")]
    public int BufferLocationId { get; set; }

    [JsonPropertyName("wasteBottlesArticleId")]
    public int WasteBottlesArticleId { get; set; }

    [JsonPropertyName("wastePreformsArticleId")]
    public int WastePreformsArticleId { get; set; }
}

/// <summary>One machine / line panel: current lot, stock for its preform, two previous production lots.</summary>
public class Viana3MachinePanelDto
{
    [JsonPropertyName("machineId")]
    public int MachineId { get; set; }

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    /// <summary>Current slot (may be a maintenance / placeholder lot, or null = nothing planned now).</summary>
    [JsonPropertyName("current")]
    public Viana3LotDto? Current { get; set; }

    /// <summary>Up to two previous PRODUCTION lots, most recent first.</summary>
    [JsonPropertyName("previous")]
    public List<Viana3LotDto> Previous { get; set; } = new();

    /// <summary>Warehouse 3 stock of the preform required by the current lot (null when the lot has no preform or no stock row exists).</summary>
    [JsonPropertyName("preformStock")]
    public Viana3PreformStockDto? PreformStock { get; set; }

    /// <summary>Machine status code: RUNNING, MAINTENANCE, NO_ORDER, PLACEHOLDER, IDLE.</summary>
    [JsonPropertyName("statusCode")]
    public string StatusCode { get; set; } = "IDLE";

    [JsonPropertyName("statusLabel")]
    public string StatusLabel { get; set; } = string.Empty;
}

/// <summary>A production lot (T_ProdPlanung row) with its separate figures.</summary>
public class Viana3LotDto
{
    /// <summary>0 = current, 1 = previous 1, 2 = previous 2.</summary>
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("lotId")]
    public int LotId { get; set; }

    /// <summary>Production, Maintenance, NoOrder, Placeholder.</summary>
    [JsonPropertyName("lotKind")]
    public string LotKind { get; set; } = string.Empty;

    [JsonPropertyName("lotKindLabel")]
    public string LotKindLabel { get; set; } = string.Empty;

    /// <summary>Open / Closed / null.</summary>
    [JsonPropertyName("lotStatus")]
    public string? LotStatus { get; set; }

    [JsonPropertyName("bottleArticle")]
    public string? BottleArticle { get; set; }

    [JsonPropertyName("bottleWeightG")]
    public decimal? BottleWeightG { get; set; }

    [JsonPropertyName("preformArticleId")]
    public int? PreformArticleId { get; set; }

    [JsonPropertyName("preformArticle")]
    public string? PreformArticle { get; set; }

    [JsonPropertyName("preformWeightG")]
    public decimal? PreformWeightG { get; set; }

    [JsonPropertyName("planFrom")]
    public DateTime? PlanFrom { get; set; }

    [JsonPropertyName("planTo")]
    public DateTime? PlanTo { get; set; }

    [JsonPropertyName("plannedBottles")]
    public decimal? PlannedBottles { get; set; }

    [JsonPropertyName("reportedBottles")]
    public decimal? ReportedBottles { get; set; }

    [JsonPropertyName("lastReportedDay")]
    public DateTime? LastReportedDay { get; set; }

    [JsonPropertyName("reportLastUpdated")]
    public DateTime? ReportLastUpdated { get; set; }

    /// <summary>Number of scanner "consume" postings (op 1420) for the lot's own preform.</summary>
    [JsonPropertyName("scanCount")]
    public int ScanCount { get; set; }

    /// <summary>Net scanned quantity of the lot's own preform (op 1420 minus op 1460). Null when no scan exists.</summary>
    [JsonPropertyName("scannedPreforms")]
    public decimal? ScannedPreforms { get; set; }

    [JsonPropertyName("lastScan")]
    public DateTime? LastScan { get; set; }

    /// <summary>Scans on this lot of a preform other than the planned one (informative).</summary>
    [JsonPropertyName("otherPreformScanCount")]
    public int OtherPreformScanCount { get; set; }

    [JsonPropertyName("otherPreformsScanned")]
    public decimal? OtherPreformsScanned { get; set; }

    [JsonPropertyName("waste")]
    public Viana3WasteDto Waste { get; set; } = new();

    [JsonPropertyName("deviation")]
    public Viana3DeviationDto Deviation { get; set; } = new();
}

/// <summary>AlplaLABEL Waste of a lot. Two independent values; kg is the record, units are an estimate.</summary>
public class Viana3WasteDto
{
    [JsonPropertyName("bookings")]
    public int Bookings { get; set; }

    [JsonPropertyName("rejectedBottlesKg")]
    public decimal? RejectedBottlesKg { get; set; }

    /// <summary>ESTIMATE = kg × 1000 / bottle weight (g). Null when the weight is unknown.</summary>
    [JsonPropertyName("rejectedBottlesUnitsEstimate")]
    public decimal? RejectedBottlesUnitsEstimate { get; set; }

    [JsonPropertyName("rejectedPreformsKg")]
    public decimal? RejectedPreformsKg { get; set; }

    /// <summary>ESTIMATE = kg × 1000 / planned preform weight (g). Null when the weight is unknown.</summary>
    [JsonPropertyName("rejectedPreformsUnitsEstimate")]
    public decimal? RejectedPreformsUnitsEstimate { get; set; }

    [JsonPropertyName("lastBooking")]
    public DateTime? LastBooking { get; set; }

    [JsonPropertyName("estimateNote")]
    public string EstimateNote { get; set; } = string.Empty;
}

/// <summary>Provisional deviation between scanned preforms and reported bottles.</summary>
public class Viana3DeviationDto
{
    /// <summary>NOT_APPLICABLE, NO_SCAN, NO_REPORT, PROVISIONAL.</summary>
    [JsonPropertyName("statusCode")]
    public string StatusCode { get; set; } = "NOT_APPLICABLE";

    [JsonPropertyName("statusLabel")]
    public string StatusLabel { get; set; } = string.Empty;

    /// <summary>(scannedPreforms − reportedBottles) / reportedBottles, as a ratio (0.05 = +5 %). Null unless PROVISIONAL.</summary>
    [JsonPropertyName("ratio")]
    public decimal? Ratio { get; set; }

    [JsonPropertyName("differenceUnits")]
    public decimal? DifferenceUnits { get; set; }

    /// <summary>Always true: the 1 preform = 1 bottle rule is not confirmed.</summary>
    [JsonPropertyName("provisional")]
    public bool Provisional { get; set; } = true;
}

/// <summary>Stock of one preform article in Warehouse 3, split available / blocked.</summary>
public class Viana3PreformStockDto
{
    [JsonPropertyName("preformArticleId")]
    public int PreformArticleId { get; set; }

    [JsonPropertyName("preformArticle")]
    public string PreformArticle { get; set; } = string.Empty;

    /// <summary>Stored positions (pallets) outside retention and not blocked.</summary>
    [JsonPropertyName("availablePositions")]
    public int AvailablePositions { get; set; }

    /// <summary>Units (EinlagerungsMenge) outside retention and not blocked.</summary>
    [JsonPropertyName("availableQty")]
    public decimal AvailableQty { get; set; }

    /// <summary>Stored positions in retention (30012) or flagged GesperrtAktiv.</summary>
    [JsonPropertyName("blockedPositions")]
    public int BlockedPositions { get; set; }

    [JsonPropertyName("blockedQty")]
    public decimal BlockedQty { get; set; }

    [JsonPropertyName("lastChange")]
    public DateTime? LastChange { get; set; }

    [JsonPropertyName("locations")]
    public List<Viana3PreformStockLocationDto> Locations { get; set; } = new();
}

/// <summary>Stock of one preform at one agreed location.</summary>
public class Viana3PreformStockLocationDto
{
    [JsonPropertyName("locationId")]
    public int LocationId { get; set; }

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    /// <summary>BLOCKED_RETENTION, BUFFER, LINE_SIDE.</summary>
    [JsonPropertyName("groupCode")]
    public string GroupCode { get; set; } = string.Empty;

    [JsonPropertyName("groupLabel")]
    public string GroupLabel { get; set; } = string.Empty;

    [JsonPropertyName("positions")]
    public int Positions { get; set; }

    [JsonPropertyName("availableQty")]
    public decimal AvailableQty { get; set; }

    [JsonPropertyName("blockedQty")]
    public decimal BlockedQty { get; set; }

    [JsonPropertyName("lastChange")]
    public DateTime? LastChange { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════
// Diagnostics (separate view — never on the main TV page)
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// GET /api/operations/viana3-monitor/diagnostics
///
/// Reference Queries 3 and 4: historical open SYSTEM balance at the machine
/// areas (booking balance, not physical stock, not Warehouse 3 stock — decision
/// 2026-11: lots 2687/2700 are not corrected) and AlplaLABEL Waste whose lot no
/// longer exists in the plan.
/// </summary>
public class Viana3MonitorDiagnosticsResponseDto
{
    [JsonPropertyName("plant")]
    public string Plant { get; set; } = "VIANA3";

    [JsonPropertyName("source")]
    public Viana3MonitorSourceDto Source { get; set; } = new();

    [JsonPropertyName("openSystemBalances")]
    public List<Viana3OpenSystemBalanceDto> OpenSystemBalances { get; set; } = new();

    [JsonPropertyName("orphanWaste")]
    public List<Viana3OrphanWasteDto> OrphanWaste { get; set; } = new();

    [JsonPropertyName("orphanWasteWindowDays")]
    public int OrphanWasteWindowDays { get; set; }

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = new();
}

/// <summary>Open booking balance of a preform barcode at a machine area (diagnostic only).</summary>
public class Viana3OpenSystemBalanceDto
{
    [JsonPropertyName("machineId")]
    public int MachineId { get; set; }

    [JsonPropertyName("machineName")]
    public string? MachineName { get; set; }

    [JsonPropertyName("preformArticleId")]
    public int PreformArticleId { get; set; }

    [JsonPropertyName("preformArticle")]
    public string PreformArticle { get; set; } = string.Empty;

    [JsonPropertyName("barcode")]
    public string? Barcode { get; set; }

    [JsonPropertyName("lastLotId")]
    public int? LastLotId { get; set; }

    [JsonPropertyName("lastOperation")]
    public int? LastOperation { get; set; }

    /// <summary>Booking balance — NOT physical stock.</summary>
    [JsonPropertyName("openSystemBalance")]
    public decimal OpenSystemBalance { get; set; }

    [JsonPropertyName("lastMovement")]
    public DateTime? LastMovement { get; set; }

    [JsonPropertyName("daysSinceLastMovement")]
    public int? DaysSinceLastMovement { get; set; }

    [JsonPropertyName("balanceStatus")]
    public string BalanceStatus { get; set; } = string.Empty;
}

/// <summary>AlplaLABEL Waste booking whose lot does not exist in T_ProdPlanung (deleted lot).</summary>
public class Viana3OrphanWasteDto
{
    [JsonPropertyName("bookingId")]
    public long BookingId { get; set; }

    [JsonPropertyName("bookedAt")]
    public DateTime? BookedAt { get; set; }

    [JsonPropertyName("wasteArticleId")]
    public int WasteArticleId { get; set; }

    [JsonPropertyName("wasteType")]
    public string WasteType { get; set; } = string.Empty;

    [JsonPropertyName("wasteKg")]
    public decimal WasteKg { get; set; }

    [JsonPropertyName("lotOnBooking")]
    public int? LotOnBooking { get; set; }

    [JsonPropertyName("machineId")]
    public int? MachineId { get; set; }

    [JsonPropertyName("bottleArticleId")]
    public int? BottleArticleId { get; set; }

    [JsonPropertyName("bottleArticle")]
    public string? BottleArticle { get; set; }

    [JsonPropertyName("bottleWeightG")]
    public decimal? BottleWeightG { get; set; }

    /// <summary>ESTIMATE: kg × 1000 / bottle weight (bottle weight used as proxy for preform waste too — assumption).</summary>
    [JsonPropertyName("unitsEstimate")]
    public decimal? UnitsEstimate { get; set; }
}
