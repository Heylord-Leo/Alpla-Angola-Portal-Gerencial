using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using AlplaPortal.Infrastructure.Logging;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Operations;

/// <summary>
/// Raster images of the Viana 3 card designs (OperationsDesignAssets + files under the
/// configured folder, default data/attachments/operations/card-design).
///
/// Upload: validate (extension, declared type, magic bytes, size, dimensions), decode and
/// re-encode with Magick.NET (metadata stripped, transparency kept), hash the normalised
/// bytes, write the file under its unique asset id BEFORE any transaction, then — under
/// the per-board lock — return the identical ACTIVE asset if one exists, else enforce the
/// quota and insert the row. The delegate is database-only and idempotent on the asset id.
/// A reused or failed upload deletes the file it wrote.
///
/// Delete (two phases): phase 1 verifies references and tombstones the row (commit);
/// phase 2, in a new locked transaction, deletes the file idempotently and removes the
/// tombstone. Failures leave the tombstone (with attempt bookkeeping) for the sweep.
/// Reconciliation: purge tombstones; remove files without any row older than one hour;
/// report active rows whose file is missing (never deleted automatically).
///
/// Only PNG, JPEG and WebP are ever stored or served. No SVG.
/// </summary>
public class Viana3DesignAssetService : IViana3DesignAssetService
{
    private const string BoardKey = OperationsBoardKeys.Viana3ProductionMonitor;
    private const string StorageConfigKey = "AppConfig:OperationsCardDesignStoragePath";
    private static readonly string DefaultStorageRelative = Path.Combine("data", "attachments", "operations", "card-design");
    private static readonly TimeSpan OrphanFileAge = TimeSpan.FromHours(1);
    private static readonly Regex AssetFilePattern = new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.(png|jpg|webp)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ApplicationDbContext _db;
    private readonly AdminLogWriter _adminLog;
    private readonly ILogger<Viana3DesignAssetService> _logger;
    private readonly string _storagePath;

    public Viana3DesignAssetService(ApplicationDbContext db, AdminLogWriter adminLog, IWebHostEnvironment env, IConfiguration configuration, ILogger<Viana3DesignAssetService> logger)
    {
        _db = db;
        _adminLog = adminLog;
        _logger = logger;
        var configured = configuration[StorageConfigKey];
        _storagePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, DefaultStorageRelative)
            : (Path.IsPathRooted(configured) ? configured : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured)));
        Directory.CreateDirectory(_storagePath);
    }

    // ─── Reads ───

    public async Task<Viana3DesignAssetListDto> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.OperationsDesignAssets.AsNoTracking().Include(a => a.CreatedByUser)
            .Where(a => a.BoardKey == BoardKey).OrderByDescending(a => a.CreatedAtUtc).ToListAsync(ct);
        var refs = await ReferencesAsync(ct);
        var list = new Viana3DesignAssetListDto();
        foreach (var r in rows)
        {
            if (r.DeletedAtUtc is not null) { list.PendingPurge++; continue; }
            var missing = !File.Exists(Path.Combine(_storagePath, r.StorageReference));
            if (missing) list.MissingFiles++;
            list.Assets.Add(ToDto(r, refs.TryGetValue(r.Id, out var by) ? by : new List<int>(), missing));
        }
        list.ActiveCount = list.Assets.Count;
        return list;
    }

    public async Task<(byte[] Bytes, string ContentType)?> GetFileAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.OperationsDesignAssets.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.BoardKey == BoardKey && a.DeletedAtUtc == null, ct);
        if (row is null) return null;
        var path = Path.Combine(_storagePath, row.StorageReference);
        if (!File.Exists(path)) return null;
        return (await File.ReadAllBytesAsync(path, ct), row.ContentType);
    }

    // ─── Upload ───

    public async Task<Viana3DesignAssetUploadResultDto> UploadAsync(Stream content, string fileName, string? declaredContentType, Guid userId, CancellationToken ct = default)
    {
        // 1. Validate and normalise entirely in memory (no shared state).
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext == ".jpeg") ext = ".jpg";
        if (ext is not (".png" or ".jpg" or ".webp"))
            throw new Viana3CardDesignValidationException("Formato não aceite. Use PNG, JPEG ou WebP (SVG não é aceite nesta fase).");

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var original = ms.ToArray();
        if (original.Length == 0) throw new Viana3CardDesignValidationException("Ficheiro vazio.");
        if (original.Length > Viana3CardDesignRules.AssetMaxBytes)
            throw new Viana3CardDesignValidationException($"A imagem excede {Viana3CardDesignRules.AssetMaxBytes / (1024 * 1024)} MB.");

        var sniffed = SniffContentType(original);
        var expected = ext switch { ".png" => "image/png", ".jpg" => "image/jpeg", _ => "image/webp" };
        if (sniffed != expected)
            throw new Viana3CardDesignValidationException("O conteúdo do ficheiro não corresponde à extensão (assinatura inválida).");
        if (!string.IsNullOrWhiteSpace(declaredContentType) && !string.Equals(declaredContentType.Split(';')[0].Trim(), expected, StringComparison.OrdinalIgnoreCase))
            throw new Viana3CardDesignValidationException("O tipo de conteúdo declarado não corresponde à extensão.");

        byte[] normalised;
        int width, height;
        try
        {
            using var image = new MagickImage(original);
            var format = image.Format;
            var ok = (ext == ".png" && format == MagickFormat.Png) || (ext == ".jpg" && format is MagickFormat.Jpeg or MagickFormat.Jpg)
                     || (ext == ".webp" && format == MagickFormat.WebP);
            if (!ok) throw new Viana3CardDesignValidationException("A imagem não pôde ser descodificada como o formato indicado.");
            width = (int)image.Width;
            height = (int)image.Height;
            if (width > Viana3CardDesignRules.AssetMaxDimension || height > Viana3CardDesignRules.AssetMaxDimension)
                throw new Viana3CardDesignValidationException($"A imagem excede {Viana3CardDesignRules.AssetMaxDimension}×{Viana3CardDesignRules.AssetMaxDimension} px.");
            image.Strip();
            image.Format = ext switch { ".png" => MagickFormat.Png, ".jpg" => MagickFormat.Jpeg, _ => MagickFormat.WebP };
            normalised = image.ToByteArray();
        }
        catch (Viana3CardDesignValidationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Viana3 design asset upload: decode/re-encode failed for {FileName}", fileName);
            throw new Viana3CardDesignValidationException("A imagem não pôde ser lida. Confirme que é um PNG, JPEG ou WebP válido.");
        }
        if (normalised.Length > Viana3CardDesignRules.AssetMaxBytes)
            throw new Viana3CardDesignValidationException($"A imagem normalizada excede {Viana3CardDesignRules.AssetMaxBytes / (1024 * 1024)} MB.");

        var sha = Convert.ToHexString(SHA256.HashData(normalised)).ToLowerInvariant();
        var assetId = Guid.NewGuid();
        var storageRef = $"{assetId:D}{ext}";
        var safeName = SanitizeFileName(fileName ?? "imagem" + ext, ext);

        // 2. Write the file under its unique id BEFORE any transaction (the delegate must stay database-only).
        var path = Path.Combine(_storagePath, storageRef);
        await File.WriteAllBytesAsync(path, normalised, ct);

        OperationsDesignAsset? result = null;
        var reused = false;
        try
        {
            // 3. Locked, database-only, idempotent on the asset id.
            result = await Viana3BoardLock.RunAsync(_db, BoardKey, async innerCt =>
            {
                var mine = await _db.OperationsDesignAssets.FirstOrDefaultAsync(a => a.Id == assetId, innerCt);
                if (mine is not null) return mine; // retry after a lost acknowledgement

                var same = await _db.OperationsDesignAssets.FirstOrDefaultAsync(a => a.BoardKey == BoardKey && a.DeletedAtUtc == null && a.Sha256 == sha, innerCt);
                if (same is not null) { reused = true; return same; }

                // Authoritative quota check, inside the locked transaction, before any insert.
                var active = await _db.OperationsDesignAssets.CountAsync(a => a.BoardKey == BoardKey && a.DeletedAtUtc == null, innerCt);
                if (active >= Viana3CardDesignRules.AssetQuota)
                    throw new Viana3CardDesignConflictException("QUOTA_EXCEEDED",
                        $"Limite de {Viana3CardDesignRules.AssetQuota} imagens atingido. Elimine imagens não utilizadas antes de carregar outra.",
                        new { activeCount = active, quota = Viana3CardDesignRules.AssetQuota });

                var row = new OperationsDesignAsset
                {
                    Id = assetId, BoardKey = BoardKey, FileName = safeName, ContentType = expected, Extension = ext,
                    SizeBytes = normalised.Length, Sha256 = sha, Width = width, Height = height, StorageReference = storageRef,
                    CreatedByUserId = userId, CreatedAtUtc = DateTime.UtcNow,
                };
                _db.OperationsDesignAssets.Add(row);
                try
                {
                    await _db.SaveChangesAsync(innerCt);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // Backstop for the filtered unique index: somebody stored the same content first.
                    _db.Entry(row).State = EntityState.Detached;
                    var existing = await _db.OperationsDesignAssets.AsNoTracking().FirstOrDefaultAsync(a => a.BoardKey == BoardKey && a.DeletedAtUtc == null && a.Sha256 == sha, innerCt);
                    if (existing is null) throw;
                    reused = true;
                    return existing;
                }
                return row;
            }, ct);
        }
        finally
        {
            // 4. A reuse or a failure must not leave this request's file behind.
            if (result is null || result.Id != assetId) TryDeleteFile(path);
        }

        _db.ChangeTracker.Clear();
        var diagnosticCount = await _db.OperationsDesignAssets.CountAsync(a => a.BoardKey == BoardKey && a.DeletedAtUtc == null, ct);
        if (diagnosticCount > Viana3CardDesignRules.AssetQuota)
            _logger.LogWarning("Viana3 design assets: active count {Count} exceeds the quota {Quota} (diagnostic only; the authoritative check runs inside the lock)", diagnosticCount, Viana3CardDesignRules.AssetQuota);

        await _adminLog.WriteAsync("Info", nameof(Viana3DesignAssetService), reused ? "VIANA3_DESIGN_ASSET_REUSED" : "VIANA3_DESIGN_ASSET_UPLOADED",
            $"Viana 3 design asset {(reused ? "reused" : "uploaded")}: {result!.Id} {safeName} {width}×{height} {normalised.Length} bytes");

        var refs = await ReferencesAsync(ct);
        return new Viana3DesignAssetUploadResultDto
        {
            Asset = ToDto(result, refs.TryGetValue(result.Id, out var by) ? by : new List<int>(), false),
            Reused = reused,
        };
    }

    // ─── Two-phase deletion ───

    public async Task DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tombstoned = await TombstoneAsync(id, ct);
        if (!tombstoned) throw new Viana3CardDesignNotFoundException("Imagem não encontrada.");
        await _adminLog.WriteAsync("Info", nameof(Viana3DesignAssetService), "VIANA3_DESIGN_ASSET_DELETED", $"Viana 3 design asset {id} tombstoned by user {userId}");
        await PurgeAsync(id, ct);
    }

    public async Task<Viana3DesignCleanupResultDto> CleanupUnreferencedAsync(Guid userId, CancellationToken ct = default)
    {
        var candidates = await Viana3BoardLock.RunAsync(_db, BoardKey, async innerCt =>
        {
            var refs = await ReferencesAsync(innerCt);
            var rows = await _db.OperationsDesignAssets.Where(a => a.BoardKey == BoardKey && a.DeletedAtUtc == null).ToListAsync(innerCt);
            var now = DateTime.UtcNow;
            var ids = new List<Guid>();
            foreach (var r in rows.Where(r => !refs.ContainsKey(r.Id)))
            {
                r.DeletedAtUtc = now;
                ids.Add(r.Id);
            }
            if (ids.Count > 0) await _db.SaveChangesAsync(innerCt);
            return ids;
        }, ct);
        _db.ChangeTracker.Clear();

        var result = await ReconcileAsync(ct);
        result.Tombstoned = candidates.Count;
        await _adminLog.WriteAsync("Info", nameof(Viana3DesignAssetService), "VIANA3_DESIGN_ASSET_CLEANUP",
            $"Viana 3 design assets cleanup by user {userId}: {candidates.Count} tombstoned, {result.Purged} purged, {result.OrphanFilesRemoved} orphan files removed, {result.MissingFiles} missing");
        return result;
    }

    public async Task<Viana3DesignCleanupResultDto> ReconcileAsync(CancellationToken ct = default)
    {
        var result = new Viana3DesignCleanupResultDto();

        // Tombstones: retry phase 2 for each.
        var tombstones = await _db.OperationsDesignAssets.AsNoTracking().Where(a => a.BoardKey == BoardKey && a.DeletedAtUtc != null).Select(a => a.Id).ToListAsync(ct);
        foreach (var id in tombstones)
        {
            ct.ThrowIfCancellationRequested();
            var purged = await PurgeAsync(id, ct);
            if (purged is null) result.Errors.Add($"{id}: ficheiro ainda não eliminado");
            else if (purged.Value) result.Purged++;
        }

        // Orphan files: in the dedicated folder, asset-id names only, no row in any state, older than one hour.
        try
        {
            var known = (await _db.OperationsDesignAssets.AsNoTracking().Where(a => a.BoardKey == BoardKey).Select(a => a.StorageReference).ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(_storagePath))
            {
                var name = Path.GetFileName(file);
                if (!AssetFilePattern.IsMatch(name) || known.Contains(name)) continue;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < OrphanFileAge) continue;
                if (TryDeleteFile(file)) result.OrphanFilesRemoved++;
                else result.Errors.Add($"{name}: ficheiro órfão não eliminado");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Viana3 design assets: orphan scan failed");
            result.Errors.Add("verificação de ficheiros órfãos falhou");
        }

        // Active rows whose file is missing: reported, never deleted automatically.
        var active = await _db.OperationsDesignAssets.AsNoTracking().Where(a => a.BoardKey == BoardKey && a.DeletedAtUtc == null).Select(a => a.StorageReference).ToListAsync(ct);
        result.MissingFiles = active.Count(r => !File.Exists(Path.Combine(_storagePath, r)));
        return result;
    }

    /// <summary>Phase 1: verify references and tombstone. False when the asset is unknown.</summary>
    private async Task<bool> TombstoneAsync(Guid id, CancellationToken ct)
    {
        var outcome = await Viana3BoardLock.RunAsync(_db, BoardKey, async innerCt =>
        {
            var row = await _db.OperationsDesignAssets.FirstOrDefaultAsync(a => a.Id == id && a.BoardKey == BoardKey, innerCt);
            if (row is null) return false;
            if (row.DeletedAtUtc is not null) return true; // idempotent
            var refs = await ReferencesAsync(innerCt);
            if (refs.TryGetValue(id, out var by))
                throw new Viana3CardDesignConflictException("ASSET_REFERENCED",
                    "A imagem está a ser usada por um design e não pode ser eliminada.",
                    new { referencedBy = by });
            row.DeletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(innerCt);
            return true;
        }, ct);
        _db.ChangeTracker.Clear();
        return outcome;
    }

    /// <summary>Phase 2: delete the file idempotently and remove the tombstone. True = purged, false = nothing to do, null = file still present (retry later).</summary>
    private async Task<bool?> PurgeAsync(Guid id, CancellationToken ct)
    {
        var outcome = await Viana3BoardLock.RunAsync<bool?>(_db, BoardKey, async innerCt =>
        {
            var row = await _db.OperationsDesignAssets.FirstOrDefaultAsync(a => a.Id == id && a.BoardKey == BoardKey, innerCt);
            if (row is null) return false;
            if (row.DeletedAtUtc is null) return false; // active assets are never purged
            var path = Path.Combine(_storagePath, row.StorageReference);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                row.PurgeAttempts++;
                row.LastPurgeAttemptUtc = DateTime.UtcNow;
                row.LastPurgeError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                await _db.SaveChangesAsync(innerCt);
                _logger.LogWarning(ex, "Viana3 design asset {Id}: file deletion failed (attempt {Attempt}); tombstone kept for the sweep", id, row.PurgeAttempts);
                return null;
            }
            _db.OperationsDesignAssets.Remove(row);
            await _db.SaveChangesAsync(innerCt);
            return true;
        }, ct);
        _db.ChangeTracker.Clear();
        return outcome;
    }

    // ─── Helpers ───

    /// <summary>Asset id → designs (0 = template) that reference it, computed from the committed documents.</summary>
    private async Task<Dictionary<Guid, List<int>>> ReferencesAsync(CancellationToken ct)
    {
        var docs = await _db.OperationsCardDesigns.AsNoTracking().Where(d => d.BoardKey == BoardKey).Select(d => new { d.MachineId, d.DesignJson }).ToListAsync(ct);
        var refs = new Dictionary<Guid, List<int>>();
        foreach (var d in docs)
        {
            try
            {
                using var json = JsonDocument.Parse(d.DesignJson);
                if (!json.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("props", out var props) || props.ValueKind != JsonValueKind.Object) continue;
                    if (!props.TryGetProperty("imageAssetId", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                    if (!Guid.TryParse(idEl.GetString(), out var id)) continue;
                    if (!refs.TryGetValue(id, out var list)) refs[id] = list = new List<int>();
                    if (!list.Contains(d.MachineId)) list.Add(d.MachineId);
                }
            }
            catch (JsonException) { /* unreadable document: treated as referencing nothing; logged by the design service */ }
        }
        return refs;
    }

    private static string SniffContentType(byte[] data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return "image/png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return "image/webp";
        return "unknown";
    }

    private static string SanitizeFileName(string fileName, string ext)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        name = Regex.Replace(name, @"[^a-zA-Z0-9 _\-]", "").Replace(' ', '_').Trim();
        if (name.Length == 0) name = "imagem";
        if (name.Length > 100) name = name[..100];
        return name + ext;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627);

    private bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Viana3 design assets: could not delete file {Path}", path);
            return false;
        }
    }

    private static Viana3DesignAssetDto ToDto(OperationsDesignAsset a, List<int> referencedBy, bool fileMissing) => new()
    {
        Id = a.Id, FileName = a.FileName, ContentType = a.ContentType, SizeBytes = a.SizeBytes, Width = a.Width, Height = a.Height,
        CreatedAtUtc = a.CreatedAtUtc, CreatedByName = a.CreatedByUser?.FullName, ReferencedBy = referencedBy, FileMissing = fileMissing,
        Url = Viana3CardDesignRules.AssetUrlBase + a.Id.ToString("D"),
    };
}
