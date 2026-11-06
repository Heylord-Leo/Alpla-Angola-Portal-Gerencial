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
/// Template + per-machine card designs of the Viana 3 Production Monitor
/// (OperationsCardDesigns, BoardKey VIANA3_PRODUCTION_MONITOR, MachineId 0 = template).
///
/// Writes run inside <see cref="Viana3BoardLock"/>: the save validates the document,
/// checks the idempotency token (retry after a lost acknowledgement returns success),
/// checks the expected timestamp (409 DESIGN_MODIFIED), and verifies — under the same
/// lock the asset service uses for deletions — that every referenced image is an ACTIVE
/// asset of the board (409 ASSET_UNAVAILABLE). The validated model is re-serialised; the
/// raw body is never stored. Nothing here touches AlplaPROD or production rules.
/// </summary>
public class Viana3CardDesignService : IViana3CardDesignService
{
    private const string BoardKey = OperationsBoardKeys.Viana3ProductionMonitor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly AdminLogWriter _adminLog;
    private readonly Viana3CardCatalogVerifier _catalog;
    private readonly ILogger<Viana3CardDesignService> _logger;

    public Viana3CardDesignService(ApplicationDbContext db, AdminLogWriter adminLog, Viana3CardCatalogVerifier catalog, ILogger<Viana3CardDesignService> logger)
    {
        _db = db;
        _adminLog = adminLog;
        _catalog = catalog;
        _logger = logger;
    }

    // ─── Reads ───

    public async Task<Viana3CardDesignBundleDto> GetBundleAsync(CancellationToken ct = default)
    {
        var rows = await _db.OperationsCardDesigns.AsNoTracking().Where(d => d.BoardKey == BoardKey).ToListAsync(ct);
        var bundle = new Viana3CardDesignBundleDto();
        foreach (var row in rows)
        {
            var doc = Read(row);
            if (doc is null) continue;
            if (row.MachineId == 0) bundle.Template = doc;
            else bundle.Machines.Add(new Viana3CardDesignBundleEntryDto { MachineId = row.MachineId, Design = doc });
        }
        return bundle;
    }

    public async Task<Viana3CardDesignDto> GetAsync(int machineId, CancellationToken ct = default)
    {
        if (machineId < 0) throw new Viana3CardDesignValidationException("Máquina inválida.");
        var rows = await _db.OperationsCardDesigns.AsNoTracking().Include(d => d.UpdatedByUser)
            .Where(d => d.BoardKey == BoardKey && (d.MachineId == machineId || d.MachineId == 0)).ToListAsync(ct);
        var own = rows.FirstOrDefault(r => r.MachineId == machineId);
        var template = rows.FirstOrDefault(r => r.MachineId == 0);
        return ToDto(machineId, own, template);
    }

    public Task<Viana3CardItemCatalogDto> GetCatalogAsync(CancellationToken ct = default) =>
        Task.FromResult(_catalog.GetVerified());

    // ─── Writes ───

    public async Task<Viana3CardDesignDto> SaveAsync(int machineId, SaveViana3CardDesignRequest request, Guid userId, CancellationToken ct = default)
    {
        if (machineId < 0) throw new Viana3CardDesignValidationException("Máquina inválida.");
        if (request is null || request.SaveToken == Guid.Empty) throw new Viana3CardDesignValidationException("Pedido de gravação inválido (token em falta).");
        var validated = Validate(request.Design);
        var json = JsonSerializer.Serialize(validated, JsonOptions);
        var assetIds = validated.Items.Where(i => i.Props.ImageAssetId.HasValue).Select(i => i.Props.ImageAssetId!.Value).Distinct().ToList();

        var saved = await Viana3BoardLock.RunAsync(_db, BoardKey, async innerCt =>
        {
            var row = await _db.OperationsCardDesigns.FirstOrDefaultAsync(d => d.BoardKey == BoardKey && d.MachineId == machineId, innerCt);

            // Idempotent retry: this very save was already applied.
            if (row is not null && row.LastSaveToken == request.SaveToken) return row;

            if (row is not null && request.ExpectedUpdatedAtUtc.HasValue && Truncate(row.UpdatedAtUtc) != Truncate(request.ExpectedUpdatedAtUtc.Value))
            {
                var who = await _db.Users.AsNoTracking().Where(u => u.Id == row.UpdatedByUserId).Select(u => u.FullName).FirstOrDefaultAsync(innerCt);
                throw new Viana3CardDesignConflictException("DESIGN_MODIFIED",
                    $"Este design foi alterado entretanto{(who is null ? "" : $" por {who}")}. Recarregue antes de guardar.",
                    new { updatedAtUtc = row.UpdatedAtUtc, updatedByName = who });
            }

            if (assetIds.Count > 0)
            {
                var active = await _db.OperationsDesignAssets.AsNoTracking()
                    .Where(a => a.BoardKey == BoardKey && a.DeletedAtUtc == null && assetIds.Contains(a.Id))
                    .Select(a => a.Id).ToListAsync(innerCt);
                var missing = assetIds.Except(active).ToList();
                if (missing.Count > 0)
                {
                    var items = validated.Items.Where(i => i.Props.ImageAssetId.HasValue && missing.Contains(i.Props.ImageAssetId.Value)).Select(i => i.Key).ToList();
                    throw new Viana3CardDesignConflictException("ASSET_UNAVAILABLE",
                        "Uma ou mais imagens deste design já não existem. Substitua-as ou remova-as e guarde de novo.",
                        new { assetIds = missing, items });
                }
            }

            var now = DateTime.UtcNow;
            if (row is null)
            {
                row = new OperationsCardDesign { BoardKey = BoardKey, MachineId = machineId, CreatedAtUtc = now };
                _db.OperationsCardDesigns.Add(row);
            }
            row.SchemaVersion = validated.SchemaVersion;
            row.DesignJson = json;
            row.LastSaveToken = request.SaveToken;
            row.UpdatedByUserId = userId;
            row.UpdatedAtUtc = now;
            await _db.SaveChangesAsync(innerCt);
            return row;
        }, ct);

        await _adminLog.WriteAsync("Info", nameof(Viana3CardDesignService), "VIANA3_CARD_DESIGN_SAVED",
            $"Viana 3 card design saved for {(machineId == 0 ? "template" : $"machine {machineId}")}: {validated.Items.Count} items ({validated.Items.Count(i => !i.Enabled)} disabled), {assetIds.Count} images",
            payload: json);

        _db.ChangeTracker.Clear();
        return await GetAsync(machineId, ct);
    }

    public async Task<Viana3CardDesignDto> ResetAsync(int machineId, Guid userId, CancellationToken ct = default)
    {
        if (machineId < 0) throw new Viana3CardDesignValidationException("Máquina inválida.");
        var removed = await Viana3BoardLock.RunAsync(_db, BoardKey, async innerCt =>
        {
            var row = await _db.OperationsCardDesigns.FirstOrDefaultAsync(d => d.BoardKey == BoardKey && d.MachineId == machineId, innerCt);
            if (row is null) return false;
            _db.OperationsCardDesigns.Remove(row);
            await _db.SaveChangesAsync(innerCt);
            return true;
        }, ct);

        if (removed)
            await _adminLog.WriteAsync("Info", nameof(Viana3CardDesignService), "VIANA3_CARD_DESIGN_RESET",
                $"Viana 3 card design reset for {(machineId == 0 ? "template (machines return to the built-in card)" : $"machine {machineId} (back to the template)")} by user {userId}");

        _db.ChangeTracker.Clear();
        return await GetAsync(machineId, ct);
    }

    // ─── Validation ───

    private static Viana3CardDesignDocumentDto Validate(Viana3CardDesignDocumentDto? doc)
    {
        if (doc is null) throw new Viana3CardDesignValidationException("Design em falta.");
        if (doc.SchemaVersion != Viana3CardDesignRules.SchemaVersion)
            throw new Viana3CardDesignValidationException($"Versão de esquema não suportada ({doc.SchemaVersion}).");
        var h = Viana3CardDesignRules.CanvasHeight;
        if (doc.CanvasHeight != h)
            throw new Viana3CardDesignValidationException($"A altura da área de desenho tem de ser {h} unidades.");
        var w = doc.CanvasWidth;
        if (w < Viana3CardDesignRules.CanvasMinWidth || w > Viana3CardDesignRules.CanvasMaxWidth)
            throw new Viana3CardDesignValidationException($"A largura da área de desenho tem de estar entre {Viana3CardDesignRules.CanvasMinWidth} e {Viana3CardDesignRules.CanvasMaxWidth} unidades.");
        if (doc.Background is not null && !ColorPattern.IsMatch(doc.Background))
            throw new Viana3CardDesignValidationException("Cor de fundo inválida (use #rrggbb ou #rrggbbaa).");

        var items = doc.Items ?? new List<Viana3CardDesignItemDto>();
        if (items.Count > Viana3CardDesignRules.MaxItems)
            throw new Viana3CardDesignValidationException($"O design não pode ter mais de {Viana3CardDesignRules.MaxItems} itens.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var free = 0;
        var result = new Viana3CardDesignDocumentDto { SchemaVersion = doc.SchemaVersion, CanvasWidth = w, CanvasHeight = h, Background = doc.Background };

        foreach (var item in items)
        {
            if (item is null) throw new Viana3CardDesignValidationException("Item inválido.");
            var key = (item.Key ?? string.Empty).Trim();
            if (!Viana3CardItemCatalog.IsKnownKey(key)) throw new Viana3CardDesignValidationException($"Item desconhecido «{key}».");
            if (!seen.Add(key)) throw new Viana3CardDesignValidationException($"O item «{key}» aparece mais do que uma vez.");
            if (Viana3CardItemCatalog.IsFreeKey(key) && ++free > Viana3CardDesignRules.MaxFreeItems)
                throw new Viana3CardDesignValidationException($"No máximo {Viana3CardDesignRules.MaxFreeItems} itens livres (texto/imagem) por cartão.");

            if (!Finite(item.X) || !Finite(item.Y) || !Finite(item.W) || !Finite(item.H) || !Finite(item.Rotation))
                throw new Viana3CardDesignValidationException($"Coordenadas inválidas no item «{key}».");
            if (item.W < Viana3CardDesignRules.MinSize || item.H < Viana3CardDesignRules.MinSize)
                throw new Viana3CardDesignValidationException($"O item «{key}» é demasiado pequeno (mínimo {Viana3CardDesignRules.MinSize} unidades).");
            if (item.X < 0 || item.Y < 0 || item.X + item.W > w + 0.01 || item.Y + item.H > h + 0.01)
                throw new Viana3CardDesignValidationException($"O item «{key}» sai da área do cartão.");
            if (item.Rotation != 0 && !Viana3CardItemCatalog.IsRotatable(key))
                throw new Viana3CardDesignValidationException($"O item «{key}» não pode ser rodado.");
            if (item.Rotation < -180 || item.Rotation > 180)
                throw new Viana3CardDesignValidationException($"Rotação inválida no item «{key}» (−180…180).");
            if (item.Z < 0 || item.Z > 100) throw new Viana3CardDesignValidationException($"Ordem (z) inválida no item «{key}».");

            var st = item.Style ?? new Viana3CardDesignStyleDto();
            foreach (var (name, value) in new[] { ("cor", st.Color), ("fundo", st.Background), ("contorno", st.BorderColor), ("realce", st.Accent) })
                if (value is not null && !ColorPattern.IsMatch(value))
                    throw new Viana3CardDesignValidationException($"Cor de {name} inválida no item «{key}» (use #rrggbb ou #rrggbbaa).");
            if (st.FontSize.HasValue && (st.FontSize < Viana3CardDesignRules.MinFontSize || st.FontSize > Viana3CardDesignRules.MaxFontSize))
                throw new Viana3CardDesignValidationException($"Tamanho de letra inválido no item «{key}».");
            if (st.FontWeight.HasValue && !Viana3CardDesignRules.FontWeights.Contains(st.FontWeight.Value))
                throw new Viana3CardDesignValidationException($"Peso de letra inválido no item «{key}».");
            if (st.Align is not null && !Viana3CardDesignRules.Aligns.Contains(st.Align))
                throw new Viana3CardDesignValidationException($"Alinhamento inválido no item «{key}».");
            if (st.Opacity.HasValue && (st.Opacity < 0 || st.Opacity > 1))
                throw new Viana3CardDesignValidationException($"Opacidade inválida no item «{key}».");
            if (st.Radius.HasValue && (st.Radius < 0 || st.Radius > 100))
                throw new Viana3CardDesignValidationException($"Raio inválido no item «{key}».");

            var pr = item.Props ?? new Viana3CardDesignPropsDto();
            if (pr.Label is not null && pr.Label.Length > Viana3CardDesignRules.MaxLabelLength)
                throw new Viana3CardDesignValidationException($"Etiqueta demasiado longa no item «{key}».");
            if (pr.Text is not null && pr.Text.Length > Viana3CardDesignRules.MaxTextLength)
                throw new Viana3CardDesignValidationException($"Texto demasiado longo no item «{key}».");
            if (pr.UnitSuffix is not null && pr.UnitSuffix.Length > 20)
                throw new Viana3CardDesignValidationException($"Sufixo demasiado longo no item «{key}».");
            if (pr.NumberFormat is not null && !Viana3CardDesignRules.NumberFormats.Contains(pr.NumberFormat))
                throw new Viana3CardDesignValidationException($"Formato numérico inválido no item «{key}».");
            if (pr.ImageFit is not null && !Viana3CardDesignRules.ImageFits.Contains(pr.ImageFit))
                throw new Viana3CardDesignValidationException($"Ajuste de imagem inválido no item «{key}».");
            if (pr.ImageAssetId.HasValue && !(key == "illustration" || Viana3CardItemCatalog.IsFreeImageKey(key)))
                throw new Viana3CardDesignValidationException($"O item «{key}» não aceita imagem.");
            if (Viana3CardItemCatalog.IsFreeImageKey(key) && item.Enabled && !pr.ImageAssetId.HasValue)
                throw new Viana3CardDesignValidationException($"O item de imagem «{key}» não tem imagem associada.");

            result.Items.Add(new Viana3CardDesignItemDto
            {
                Key = key, Enabled = item.Enabled,
                X = Math.Round(item.X, 2), Y = Math.Round(item.Y, 2), W = Math.Round(item.W, 2), H = Math.Round(item.H, 2),
                Rotation = Math.Round(item.Rotation, 2), Z = item.Z,
                Style = new Viana3CardDesignStyleDto
                {
                    Color = st.Color, Background = st.Background, BorderColor = st.BorderColor, Accent = st.Accent,
                    FontSize = st.FontSize, FontWeight = st.FontWeight, Align = st.Align, Opacity = st.Opacity, Radius = st.Radius,
                },
                Props = new Viana3CardDesignPropsDto
                {
                    Label = pr.Label?.Trim(), NumberFormat = pr.NumberFormat, UnitSuffix = pr.UnitSuffix?.Trim(), ShowSecondary = pr.ShowSecondary,
                    Text = pr.Text?.Trim(), ImageAssetId = pr.ImageAssetId, ImageFit = pr.ImageFit,
                },
            });
        }
        return result;
    }

    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    private static DateTime Truncate(DateTime d) => new(d.Ticks - (d.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

    // ─── Mapping ───

    private Viana3CardDesignDocumentDto? Read(OperationsCardDesign row)
    {
        try
        {
            return JsonSerializer.Deserialize<Viana3CardDesignDocumentDto>(row.DesignJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "OperationsCardDesign {Board}/{Machine} holds an unreadable document; ignored", BoardKey, row.MachineId);
            return null;
        }
    }

    private Viana3CardDesignDto ToDto(int machineId, OperationsCardDesign? own, OperationsCardDesign? template)
    {
        var ownDoc = own is null ? null : Read(own);
        var templateDoc = template is null ? null : Read(template);
        var source = ownDoc is not null ? "OWN" : templateDoc is not null ? "TEMPLATE" : "BUILTIN";
        var effective = ownDoc ?? templateDoc ?? new Viana3CardDesignDocumentDto();
        var row = ownDoc is not null ? own : null;
        return new Viana3CardDesignDto
        {
            MachineId = machineId,
            IsTemplate = machineId == 0,
            IsPersisted = ownDoc is not null,
            EffectiveSource = machineId == 0 ? (ownDoc is not null ? "OWN" : "BUILTIN") : source,
            Design = effective,
            UpdatedAtUtc = row?.UpdatedAtUtc,
            UpdatedByName = row?.UpdatedByUser?.FullName,
        };
    }
}
