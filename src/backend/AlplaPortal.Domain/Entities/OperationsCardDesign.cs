namespace AlplaPortal.Domain.Entities;

/// <summary>
/// Visual design of the items inside a machine card of an Operations TV board.
///
/// One row per board × machine. <see cref="MachineId"/> 0 is the board-wide
/// template: a machine without its own row renders the template; without a
/// template it renders the built-in card. A machine row is a full copy made when
/// the user personalises the machine, so later template edits never change it;
/// "reset to template" deletes the row.
///
/// The design is a versioned JSON document (<see cref="DesignJson"/>) — same
/// pattern as <see cref="BadgeLayout"/> and <see cref="OperationsBoardLayout"/> —
/// holding item positions, sizes, colours, rotation, visibility, display
/// properties and image references. It carries display preferences only: it never
/// references AlplaPROD records and never changes production data or rules.
///
/// <see cref="LastSaveToken"/> is the idempotency key of the last applied save, so a
/// retried transaction (lost commit acknowledgement) is recognised instead of being
/// reported as a concurrent modification.
/// </summary>
public class OperationsCardDesign
{
    public int Id { get; set; }

    /// <summary>Board identifier, e.g. "VIANA3_PRODUCTION_MONITOR".</summary>
    public string BoardKey { get; set; } = string.Empty;

    /// <summary>AlplaPROD machine id; 0 = board-wide template.</summary>
    public int MachineId { get; set; }

    public int SchemaVersion { get; set; } = 1;

    /// <summary>Validated design document.</summary>
    public string DesignJson { get; set; } = "{}";

    /// <summary>Client-generated token of the last applied save (idempotent retries).</summary>
    public Guid? LastSaveToken { get; set; }

    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Raster image uploaded for the card designs of a board (illustration replacement,
/// free image items). The bytes live on disk under the configured storage folder as
/// <c>{Id}.{Extension}</c>; this row is the metadata and the lifecycle record.
///
/// Lifecycle (two-phase deletion):
///   active   — <see cref="DeletedAtUtc"/> null: listed, servable, referenceable, counted in the quota;
///   tombstone — <see cref="DeletedAtUtc"/> set: invisible and unservable; the file is still to be
///               deleted; <see cref="PurgeAttempts"/> / <see cref="LastPurgeError"/> record retries;
///   purged   — row removed once the file is gone.
/// Only active rows take part in the (BoardKey, Sha256) uniqueness, so an identical upload reuses
/// the stored asset.
/// </summary>
public class OperationsDesignAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string BoardKey { get; set; } = string.Empty;

    /// <summary>Original (sanitised) file name, for display only.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>image/png, image/jpeg or image/webp — the only types ever served.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>".png", ".jpg" or ".webp" (lower case, with the dot).</summary>
    public string Extension { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 (hex, lower case) of the normalised (re-encoded, metadata-stripped) bytes.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>File name inside the storage folder: "{Id}{Extension}".</summary>
    public string StorageReference { get; set; } = string.Empty;

    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ─── Two-phase deletion bookkeeping ───

    public DateTime? DeletedAtUtc { get; set; }

    public int PurgeAttempts { get; set; }

    public DateTime? LastPurgeAttemptUtc { get; set; }

    public string? LastPurgeError { get; set; }
}
