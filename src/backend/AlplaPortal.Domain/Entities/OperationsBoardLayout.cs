namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Shared display layout of one Operations TV **screen** (one row per board × screen).
///
/// A board (e.g. the Viana 3 Production Monitor) can be shown on several independent
/// TVs. Each TV opens a dedicated screen identified by a stable <see cref="ScreenKey"/>
/// in its URL; each screen has its own name, machine selection, column count, card
/// order, sizes and hidden cards. The default screen (<see cref="IsDefault"/>, key
/// "default") backs the historical TV URL and cannot be deleted.
///
/// The TV is read-only; screens are edited from the Portal (computer) by an authorized
/// user and persisted server-side so every TV and browser sees the same layout — never
/// browser storage.
///
/// The layout itself is a versioned JSON document (<see cref="LayoutJson"/>, schema
/// version in <see cref="SchemaVersion"/>) — the same JSON-blob pattern as
/// <see cref="BadgeLayout"/> — so the card model can evolve without further migrations.
/// Absence of the default row means "default layout" (every machine visible, discovery
/// order, standard size).
///
/// This table holds display preferences only: it never references AlplaPROD records and
/// hiding or removing a card never deletes or changes production data.
/// </summary>
public class OperationsBoardLayout
{
    public int Id { get; set; }

    /// <summary>Board identifier, e.g. "VIANA3_PRODUCTION_MONITOR".</summary>
    public string BoardKey { get; set; } = string.Empty;

    /// <summary>Stable, URL-safe screen identifier ("default" or a generated short key). Unique per board.</summary>
    public string ScreenKey { get; set; } = OperationsBoardKeys.DefaultScreenKey;

    /// <summary>Display name of the screen, e.g. "Envase 1".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>True for the screen that backs the historical TV URL. Exactly one per board; never deleted.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Schema version of <see cref="LayoutJson"/>.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Validated layout document (card order, sizes, hidden flags, grid columns, new-machine policy).</summary>
    public string LayoutJson { get; set; } = "{}";

    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Known board / screen keys.</summary>
public static class OperationsBoardKeys
{
    public const string Viana3ProductionMonitor = "VIANA3_PRODUCTION_MONITOR";

    /// <summary>Screen key of the default screen of every board (historical TV URL).</summary>
    public const string DefaultScreenKey = "default";
}
