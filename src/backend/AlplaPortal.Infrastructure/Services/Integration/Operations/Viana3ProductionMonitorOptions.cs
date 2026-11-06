using AlplaPortal.Domain.Enums;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Configuration of the Viana 3 Production Monitor, bound from
/// <c>Integrations:AlplaProd:Viana3Monitor</c>. Every default equals the
/// agreed reference query (AlplaPROD_aovia3_tv_preform_monitor_v5.sql), so the
/// section may be omitted entirely; only overrides need to be written.
///
/// Identifiers are AlplaPROD master-data ids and are passed to SQL as
/// parameters — never concatenated into the query text.
/// </summary>
public class Viana3ProductionMonitorOptions
{
    public const string SectionName = "Integrations:AlplaProd:Viana3Monitor";

    /// <summary>AlplaPROD plant whose connection is used. The monitor is a Viana 3 screen.</summary>
    public AlplaProdPlant Plant { get; set; } = AlplaProdPlant.VIANA3;

    /// <summary>Display name of the plant on the TV.</summary>
    public string PlantName { get; set; } = "Viana 3";

    /// <summary>
    /// Machine ids (T_ProdPlanung.IdMaschine = T_LagerAbteilungen 1–6) shown as panels, in order.
    /// Empty = discover every machine with a lot ending within <see cref="MachineLookbackDays"/>.
    /// </summary>
    public List<int> MachineIds { get; set; } = new();

    /// <summary>Discovery window for machines when <see cref="MachineIds"/> is empty.</summary>
    public int MachineLookbackDays { get; set; } = 60;

    /// <summary>Number of previous production lots per machine.</summary>
    public int PreviousLots { get; set; } = 2;

    // ─── Stock (Query 2) ───

    /// <summary>T_LagerAbteilungen.IdWarenLager of Warehouse 3 "SOPRO – Insumos".</summary>
    public int WarehouseId { get; set; } = 3;

    public string WarehouseName { get; set; } = "SOPRO – Insumos (Armazém 3)";

    /// <summary>Agreed locations. 30012 is retention (blocked).</summary>
    public List<int> LocationIds { get; set; } = new() { 30003, 30004, 30005, 30008, 30009, 30010, 30011, 30012 };

    public int RetentionLocationId { get; set; } = 30012;

    public int BufferLocationId { get; set; } = 30003;

    /// <summary>T_Konten.IdArtikelVariantenTyp of purchased preforms.</summary>
    public int PreformArticleTypeId { get; set; } = 13;

    // ─── Scanner (Query 1) ───

    /// <summary>T_ProduktionsBuchungen.IdBuchungsGrund of the line scanner consumption.</summary>
    public int ScanReasonId { get; set; } = 231;

    /// <summary>Location of the scanner postings (0 = line).</summary>
    public int ScanLocationId { get; set; } = 0;

    /// <summary>Urheber of a scan "consume" (+).</summary>
    public int ScanConsumeOperation { get; set; } = 1420;

    /// <summary>Urheber of a scan "un-consume" (−).</summary>
    public int ScanUnconsumeOperation { get; set; } = 1460;

    // ─── Waste (Queries 1 and 4) ───

    /// <summary>T_LagerBuchungen.IdBuchungsGrund of an AlplaLABEL waste booking.</summary>
    public int WasteReasonId { get; set; } = 140;

    /// <summary>T_LagerBuchungen.Urheber of an AlplaLABEL waste booking.</summary>
    public int WasteOriginatorId { get; set; } = 1100;

    /// <summary>T_Konten.IdArtikelVariantenTyp "PET-Waste".</summary>
    public int WasteArticleTypeId { get; set; } = 20;

    /// <summary>Waste article 56 'PET Produto Acabado' → rejected bottles.</summary>
    public int WasteBottlesArticleId { get; set; } = 56;

    /// <summary>Waste article 55 'PET Produto Semi Acabado' → rejected preforms.</summary>
    public int WastePreformsArticleId { get; set; } = 55;

    // ─── Diagnostics (Queries 3 and 4) ───

    /// <summary>Machine-area range for the open system balance (T_ProduktionsBuchungen.IdLagerAbteilung).</summary>
    public int MachineAreaMinId { get; set; } = 1;

    public int MachineAreaMaxId { get; set; } = 6;

    /// <summary>Window of the "Waste without a valid lot" diagnostic.</summary>
    public int OrphanWasteDays { get; set; } = 90;

    // ─── Refresh / cache ───

    public int DefaultRefreshSeconds { get; set; } = 120;

    public int MinRefreshSeconds { get; set; } = 60;

    public int MaxRefreshSeconds { get; set; } = 300;

    public int DiagnosticsCacheSeconds { get; set; } = 3600;

    /// <summary>SQL command timeout.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// The configuration binder APPENDS configured items to a pre-populated list
    /// (dotnet/runtime#36384), so a section that repeats the defaults would duplicate
    /// them. Called once after binding: de-duplicates the id lists and clamps the windows.
    /// </summary>
    public Viana3ProductionMonitorOptions Normalize()
    {
        LocationIds = LocationIds.Distinct().ToList();
        MachineIds = MachineIds.Where(id => id > 0).Distinct().ToList();
        MachineLookbackDays = Math.Max(1, MachineLookbackDays);
        PreviousLots = Math.Clamp(PreviousLots, 0, 5);
        MinRefreshSeconds = Math.Max(30, MinRefreshSeconds);
        MaxRefreshSeconds = Math.Max(MinRefreshSeconds, MaxRefreshSeconds);
        DefaultRefreshSeconds = Math.Clamp(DefaultRefreshSeconds, MinRefreshSeconds, MaxRefreshSeconds);
        DiagnosticsCacheSeconds = Math.Max(60, DiagnosticsCacheSeconds);
        CommandTimeoutSeconds = Math.Max(5, CommandTimeoutSeconds);
        OrphanWasteDays = Math.Max(1, OrphanWasteDays);
        return this;
    }
}
