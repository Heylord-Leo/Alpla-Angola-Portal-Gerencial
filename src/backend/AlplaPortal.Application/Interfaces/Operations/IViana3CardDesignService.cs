using AlplaPortal.Application.DTOs.Operations;

namespace AlplaPortal.Application.Interfaces.Operations;

/// <summary>
/// Machine-card designs of the Viana 3 Production Monitor (template + per-machine
/// overrides) and the read-only data-source catalogue. Display preferences only.
/// Every write runs under the per-board application lock shared with the asset service.
///
/// "Personalise this machine" is a save on a machine without its own row: the editor
/// sends the full document it is showing (template copy or generated default), so the
/// machine keeps that copy whatever happens to the template afterwards.
/// </summary>
public interface IViana3CardDesignService
{
    /// <summary>Anonymous bundle for the TV: template + machine overrides, no audit names.</summary>
    Task<Viana3CardDesignBundleDto> GetBundleAsync(CancellationToken ct = default);

    /// <summary>Editor view of one machine (0 = template): own design, template copy or "built-in" (empty document).</summary>
    Task<Viana3CardDesignDto> GetAsync(int machineId, CancellationToken ct = default);

    /// <summary>Validates and saves (upsert) the design of a machine or the template.</summary>
    Task<Viana3CardDesignDto> SaveAsync(int machineId, SaveViana3CardDesignRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Deletes the machine's own design (back to the template) or the template itself (back to the built-in card).</summary>
    Task<Viana3CardDesignDto> ResetAsync(int machineId, Guid userId, CancellationToken ct = default);

    /// <summary>Item catalogue with data-source metadata verified against the real queries.</summary>
    Task<Viana3CardItemCatalogDto> GetCatalogAsync(CancellationToken ct = default);
}

/// <summary>Uploaded images of the card designs: hash reuse, quota, two-phase deletion, reconciliation.</summary>
public interface IViana3DesignAssetService
{
    Task<Viana3DesignAssetListDto> ListAsync(CancellationToken ct = default);

    /// <summary>Validates, normalises and stores an image (or returns the identical active asset).</summary>
    Task<Viana3DesignAssetUploadResultDto> UploadAsync(Stream content, string fileName, string? declaredContentType, Guid userId, CancellationToken ct = default);

    /// <summary>Bytes and content type of an ACTIVE asset; null when unknown, tombstoned or the file is missing.</summary>
    Task<(byte[] Bytes, string ContentType)?> GetFileAsync(Guid id, CancellationToken ct = default);

    /// <summary>Phase 1 (tombstone) + phase 2 (purge) of one unreferenced asset.</summary>
    Task DeleteAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>Tombstones every unreferenced asset, then runs the reconciliation sweep.</summary>
    Task<Viana3DesignCleanupResultDto> CleanupUnreferencedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Reconciliation sweep: purge tombstones, remove orphan files, report missing files.</summary>
    Task<Viana3DesignCleanupResultDto> ReconcileAsync(CancellationToken ct = default);
}

/// <summary>Invalid design / asset request (400 at the API).</summary>
public class Viana3CardDesignValidationException : Exception
{
    public Viana3CardDesignValidationException(string message) : base(message) { }
}

/// <summary>Business conflict (409 at the API) with a stable code and optional detail payload.</summary>
public class Viana3CardDesignConflictException : Exception
{
    public string Code { get; }
    public object? Detail { get; }

    public Viana3CardDesignConflictException(string code, string message, object? detail = null) : base(message)
    {
        Code = code;
        Detail = detail;
    }
}

/// <summary>Unknown machine design / asset (404 at the API).</summary>
public class Viana3CardDesignNotFoundException : Exception
{
    public Viana3CardDesignNotFoundException(string message) : base(message) { }
}
