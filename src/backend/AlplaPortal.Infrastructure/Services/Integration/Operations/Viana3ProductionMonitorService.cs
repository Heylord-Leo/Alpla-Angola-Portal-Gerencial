using System.Diagnostics;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Application.Interfaces.Operations;
using AlplaPortal.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Viana 3 Production Monitor — read-only projection of AlplaPROD_aovia3 for the TV page.
///
/// Flow (monitor):
///   1. Serve the cached snapshot while it is younger than the refresh window.
///   2. Otherwise open a read-only connection (AlplaProdConnectionFactory, plant VIANA3),
///      run Query 1 (lots per machine) and Query 2 (Warehouse 3 preform stock).
///   3. Map rows; derive the provisional deviation and the Waste unit estimates
///      with decimal arithmetic; attach the stock of each current lot's preform.
///   4. Store the snapshot (no expiry — it is the "last known good").
///   5. If AlplaPROD cannot be read and a snapshot exists, return it as STALE.
///
/// Diagnostics (Queries 3 and 4) follow the same flow with a one-hour window.
///
/// Rules kept here, not in SQL, so they live in one place:
///   • Scanned ≠ consumed: no scan → "No scan recorded", never a −100 % deviation.
///   • Deviation = (scanned − reported) / reported, only with scans and reported production, PROVISIONAL.
///   • Waste kg is the record; units = kg × 1000 / article weight (g) are an ESTIMATE; never summed.
///   • The open system balance at the machine areas is never added to any stock total.
///
/// Design reference: docs/OPERATIONS_VIANA3_PRODUCTION_MONITOR.md
/// </summary>
public class Viana3ProductionMonitorService : IViana3ProductionMonitorService
{
    private const string MonitorCacheKey = "operations:viana3-monitor:snapshot";
    private const string DiagnosticsCacheKey = "operations:viana3-monitor:diagnostics";

    private static readonly SemaphoreSlim MonitorGate = new(1, 1);
    private static readonly SemaphoreSlim DiagnosticsGate = new(1, 1);

    private readonly AlplaProdConnectionFactory _factory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<Viana3ProductionMonitorService> _logger;
    private readonly Viana3ProductionMonitorOptions _options;

    private sealed record MonitorSnapshot(
        DateTime FetchedAtUtc,
        long QueryDurationMs,
        List<Viana3MachinePanelDto> Machines,
        List<Viana3PreformStockDto> Stock);

    private sealed record DiagnosticsSnapshot(
        DateTime FetchedAtUtc,
        long QueryDurationMs,
        List<Viana3OpenSystemBalanceDto> Balances,
        List<Viana3OrphanWasteDto> OrphanWaste);

    public Viana3ProductionMonitorService(
        AlplaProdConnectionFactory factory,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<Viana3ProductionMonitorService> logger)
    {
        _factory = factory;
        _cache = cache;
        _logger = logger;
        _options = (configuration.GetSection(Viana3ProductionMonitorOptions.SectionName)
            .Get<Viana3ProductionMonitorOptions>() ?? new Viana3ProductionMonitorOptions()).Normalize();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Monitor
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<Viana3MonitorResponseDto> GetMonitorAsync(int refreshSeconds, CancellationToken ct = default)
    {
        var refresh = Math.Clamp(refreshSeconds, _options.MinRefreshSeconds, _options.MaxRefreshSeconds);

        if (_cache.TryGetValue(MonitorCacheKey, out MonitorSnapshot? cached) && cached != null
            && (DateTime.UtcNow - cached.FetchedAtUtc).TotalSeconds < refresh)
        {
            return BuildMonitorResponse(cached, "CACHED", null, refresh);
        }

        await MonitorGate.WaitAsync(ct);
        try
        {
            // Another request may have refreshed while we waited.
            if (_cache.TryGetValue(MonitorCacheKey, out cached) && cached != null
                && (DateTime.UtcNow - cached.FetchedAtUtc).TotalSeconds < refresh)
            {
                return BuildMonitorResponse(cached, "CACHED", null, refresh);
            }

            try
            {
                var fresh = await QueryMonitorAsync(ct);
                _cache.Set(MonitorCacheKey, fresh);
                return BuildMonitorResponse(fresh, "LIVE", null, refresh);
            }
            catch (Exception ex) when (IsSourceFailure(ex))
            {
                if (_cache.TryGetValue(MonitorCacheKey, out cached) && cached != null)
                {
                    _logger.LogWarning(ex,
                        "[Operations] Viana3 monitor: AlplaPROD unavailable, serving snapshot from {FetchedAt:u}",
                        cached.FetchedAtUtc);
                    return BuildMonitorResponse(cached, "STALE", DescribeFailure(ex), refresh);
                }
                throw;
            }
        }
        finally
        {
            MonitorGate.Release();
        }
    }

    private async Task<MonitorSnapshot> QueryMonitorAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var o = _options;

        _logger.LogInformation("[Operations] Viana3 monitor: querying AlplaPROD plant {Plant}", o.Plant);

        await using var connection = await _factory.CreateConnectionAsync(o.Plant, ct);

        // ─── Query 1: lots per machine ───
        var panels = new Dictionary<int, Viana3MachinePanelDto>();
        var order = new List<int>();

        await using (var cmd = new SqlCommand())
        {
            cmd.Connection = connection;
            cmd.CommandTimeout = o.CommandTimeoutSeconds;
            cmd.CommandText = Viana3ProductionMonitorQueryBuilder.BuildLotsQuery(cmd, o, o.MachineIds);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var machineId = GetInt(reader, "Machine") ?? 0;
                if (!panels.TryGetValue(machineId, out var panel))
                {
                    panel = new Viana3MachinePanelDto
                    {
                        MachineId = machineId,
                        MachineName = GetString(reader, "MachineName") ?? $"Máquina {machineId}",
                    };
                    panels[machineId] = panel;
                    order.Add(machineId);
                }

                var slot = GetInt(reader, "SlotNo") ?? 0;
                var lotId = GetInt(reader, "Lot");

                if (slot == 0)
                {
                    panel.Current = lotId.HasValue ? MapLot(reader, slot, o) : null;
                }
                else if (lotId.HasValue)
                {
                    panel.Previous.Add(MapLot(reader, slot, o));
                }
            }
        }

        // ─── Query 2: Warehouse 3 preform stock ───
        var stockByPreform = new Dictionary<int, Viana3PreformStockDto>();

        await using (var cmd = new SqlCommand())
        {
            cmd.Connection = connection;
            cmd.CommandTimeout = o.CommandTimeoutSeconds;
            cmd.CommandText = Viana3ProductionMonitorQueryBuilder.BuildStockQuery(cmd, o);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var preformId = GetInt(reader, "PreformId") ?? 0;
                if (!stockByPreform.TryGetValue(preformId, out var stock))
                {
                    stock = new Viana3PreformStockDto
                    {
                        PreformArticleId = preformId,
                        PreformArticle = GetString(reader, "Preform") ?? $"Pré-forma {preformId}",
                    };
                    stockByPreform[preformId] = stock;
                }

                var locationId = GetInt(reader, "LocationId") ?? 0;
                var (groupCode, groupLabel) = ClassifyLocation(locationId, o);
                var loc = new Viana3PreformStockLocationDto
                {
                    LocationId = locationId,
                    Location = GetString(reader, "Location") ?? locationId.ToString(),
                    GroupCode = groupCode,
                    GroupLabel = groupLabel,
                    Positions = GetInt(reader, "Positions") ?? 0,
                    AvailableQty = GetDecimal(reader, "AvailableQty") ?? 0m,
                    BlockedQty = GetDecimal(reader, "BlockedQty") ?? 0m,
                    LastChange = GetDateTime(reader, "LastChange"),
                };
                stock.Locations.Add(loc);

                stock.AvailableQty += loc.AvailableQty;
                stock.BlockedQty += loc.BlockedQty;
                stock.AvailablePositions += GetInt(reader, "AvailablePositions") ?? 0;
                stock.BlockedPositions += GetInt(reader, "BlockedPositions") ?? 0;
                if (loc.LastChange.HasValue && (!stock.LastChange.HasValue || loc.LastChange > stock.LastChange))
                    stock.LastChange = loc.LastChange;
            }
        }

        // ─── Attach the current lot's preform stock to each panel ───
        var machines = new List<Viana3MachinePanelDto>();
        foreach (var id in order)
        {
            var panel = panels[id];
            panel.Previous.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            (panel.StatusCode, panel.StatusLabel) = DeriveMachineStatus(panel.Current);

            if (panel.Current?.PreformArticleId is int pid && stockByPreform.TryGetValue(pid, out var st))
                panel.PreformStock = st;

            machines.Add(panel);
        }
        machines.Sort((a, b) => a.MachineId.CompareTo(b.MachineId));

        var stockList = stockByPreform.Values.OrderBy(s => s.PreformArticle).ToList();

        sw.Stop();
        _logger.LogInformation(
            "[Operations] Viana3 monitor query completed: Machines={Machines}, Preforms={Preforms}, Duration={Ms}ms",
            machines.Count, stockList.Count, sw.ElapsedMilliseconds);

        return new MonitorSnapshot(DateTime.UtcNow, sw.ElapsedMilliseconds, machines, stockList);
    }

    private Viana3MonitorResponseDto BuildMonitorResponse(MonitorSnapshot s, string status, string? reason, int refresh)
    {
        var o = _options;
        return new Viana3MonitorResponseDto
        {
            Plant = o.Plant.ToString(),
            PlantName = o.PlantName,
            RefreshSeconds = refresh,
            Source = BuildSource(s.FetchedAtUtc, s.QueryDurationMs, status, reason),
            Machines = s.Machines,
            PreformStock = s.Stock,
            Scope = new Viana3MonitorScopeDto
            {
                WarehouseId = o.WarehouseId,
                WarehouseName = o.WarehouseName,
                LocationIds = o.LocationIds.ToList(),
                RetentionLocationId = o.RetentionLocationId,
                BufferLocationId = o.BufferLocationId,
                WasteBottlesArticleId = o.WasteBottlesArticleId,
                WastePreformsArticleId = o.WastePreformsArticleId,
            },
            Notes = new List<string>
            {
                "Pré-formas lidas = leituras do scanner na linha (não é consumo físico).",
                "Desvio provisório: a regra 1 pré-forma = 1 garrafa não está confirmada; o Waste não entra no cálculo.",
                "Waste: os kg são o valor registado (AlplaLABEL); as unidades são uma estimativa.",
                $"Stock: Armazém {o.WarehouseId} ({o.WarehouseName}), localizações {string.Join(", ", o.LocationIds)}; {o.RetentionLocationId} = retenção (bloqueado), fora do disponível.",
                "O saldo histórico das áreas de máquina só aparece na vista de diagnóstico e nunca entra no stock disponível.",
            },
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<Viana3MonitorDiagnosticsResponseDto> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var window = Math.Max(60, _options.DiagnosticsCacheSeconds);

        if (_cache.TryGetValue(DiagnosticsCacheKey, out DiagnosticsSnapshot? cached) && cached != null
            && (DateTime.UtcNow - cached.FetchedAtUtc).TotalSeconds < window)
        {
            return BuildDiagnosticsResponse(cached, "CACHED", null);
        }

        await DiagnosticsGate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(DiagnosticsCacheKey, out cached) && cached != null
                && (DateTime.UtcNow - cached.FetchedAtUtc).TotalSeconds < window)
            {
                return BuildDiagnosticsResponse(cached, "CACHED", null);
            }

            try
            {
                var fresh = await QueryDiagnosticsAsync(ct);
                _cache.Set(DiagnosticsCacheKey, fresh);
                return BuildDiagnosticsResponse(fresh, "LIVE", null);
            }
            catch (Exception ex) when (IsSourceFailure(ex))
            {
                if (_cache.TryGetValue(DiagnosticsCacheKey, out cached) && cached != null)
                {
                    _logger.LogWarning(ex,
                        "[Operations] Viana3 diagnostics: AlplaPROD unavailable, serving snapshot from {FetchedAt:u}",
                        cached.FetchedAtUtc);
                    return BuildDiagnosticsResponse(cached, "STALE", DescribeFailure(ex));
                }
                throw;
            }
        }
        finally
        {
            DiagnosticsGate.Release();
        }
    }

    private async Task<DiagnosticsSnapshot> QueryDiagnosticsAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var o = _options;

        await using var connection = await _factory.CreateConnectionAsync(o.Plant, ct);

        // ─── Query 3: open system balance at machine areas ───
        var balances = new List<Viana3OpenSystemBalanceDto>();
        await using (var cmd = new SqlCommand())
        {
            cmd.Connection = connection;
            cmd.CommandTimeout = o.CommandTimeoutSeconds;
            cmd.CommandText = Viana3ProductionMonitorQueryBuilder.BuildOpenSystemBalanceQuery(cmd, o);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                balances.Add(new Viana3OpenSystemBalanceDto
                {
                    MachineId = GetInt(reader, "Machine") ?? 0,
                    MachineName = GetString(reader, "MachineName"),
                    PreformArticleId = GetInt(reader, "PreformId") ?? 0,
                    PreformArticle = GetString(reader, "Preform") ?? string.Empty,
                    Barcode = GetString(reader, "Barcode"),
                    LastLotId = GetInt(reader, "LastLot"),
                    LastOperation = GetInt(reader, "LastOperation"),
                    OpenSystemBalance = GetDecimal(reader, "OpenSystemBalance") ?? 0m,
                    LastMovement = GetDateTime(reader, "LastMovement"),
                    DaysSinceLastMovement = GetInt(reader, "DaysSinceLastMovement"),
                    BalanceStatus = "Saldo histórico em aberto no sistema — não corrigido (apenas diagnóstico)",
                });
            }
        }

        // ─── Query 4: waste on deleted lots ───
        var orphan = new List<Viana3OrphanWasteDto>();
        await using (var cmd = new SqlCommand())
        {
            cmd.Connection = connection;
            cmd.CommandTimeout = o.CommandTimeoutSeconds;
            cmd.CommandText = Viana3ProductionMonitorQueryBuilder.BuildOrphanWasteQuery(cmd, o);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var articleId = GetInt(reader, "WasteArticleId") ?? 0;
                var kg = GetDecimal(reader, "Waste_kg") ?? 0m;
                var weight = GetDecimal(reader, "BottleWeight_g");
                orphan.Add(new Viana3OrphanWasteDto
                {
                    BookingId = GetLong(reader, "BookingId") ?? 0,
                    BookedAt = GetDateTime(reader, "BookedAt"),
                    WasteArticleId = articleId,
                    WasteType = articleId == o.WasteBottlesArticleId ? "Garrafas rejeitadas"
                        : articleId == o.WastePreformsArticleId ? "Pré-formas / semi-acabado rejeitado"
                        : "Outro waste",
                    WasteKg = kg,
                    LotOnBooking = GetInt(reader, "LotOnBooking"),
                    MachineId = GetInt(reader, "Machine"),
                    BottleArticleId = GetInt(reader, "BottleArticleId"),
                    BottleArticle = GetString(reader, "BottleArticle"),
                    BottleWeightG = weight,
                    UnitsEstimate = EstimateUnits(kg, weight),
                });
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "[Operations] Viana3 diagnostics query completed: Balances={Balances}, OrphanWaste={Orphan}, Duration={Ms}ms",
            balances.Count, orphan.Count, sw.ElapsedMilliseconds);

        return new DiagnosticsSnapshot(DateTime.UtcNow, sw.ElapsedMilliseconds, balances, orphan);
    }

    private Viana3MonitorDiagnosticsResponseDto BuildDiagnosticsResponse(DiagnosticsSnapshot s, string status, string? reason)
    {
        return new Viana3MonitorDiagnosticsResponseDto
        {
            Plant = _options.Plant.ToString(),
            Source = BuildSource(s.FetchedAtUtc, s.QueryDurationMs, status, reason),
            OpenSystemBalances = s.Balances,
            OrphanWaste = s.OrphanWaste,
            OrphanWasteWindowDays = _options.OrphanWasteDays,
            Notes = new List<string>
            {
                "Saldo em aberto no sistema = saldo de lançamentos na área de máquina; NÃO é stock físico nem stock do Armazém 3.",
                "Decisão 2026-11: os lançamentos históricos dos lotes 2687/2700 não são corrigidos; mostra-se o estado registado.",
                "Waste sem lote válido: o lote do lançamento já não existe no plano (lote apagado). Unidades = estimativa com o peso da garrafa (também para o artigo 55 — pressuposto).",
            },
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Mapping & rules
    // ═══════════════════════════════════════════════════════════════════════

    private static Viana3LotDto MapLot(SqlDataReader r, int slot, Viana3ProductionMonitorOptions o)
    {
        var lotKind = GetString(r, "LotKind") ?? "Production";
        var bottleWeight = GetDecimal(r, "BottleWeight_g");
        var preformWeight = GetDecimal(r, "PreformWeight_g");
        var reported = GetDecimal(r, "ReportedBottles");
        var scanCount = GetInt(r, "ScanCount") ?? 0;
        var scanned = GetDecimal(r, "ScannedPreforms");
        var bottlesKg = GetDecimal(r, "RejectedBottles_kg");
        var preformsKg = GetDecimal(r, "RejectedPreforms_kg");
        var statusRaw = GetInt(r, "LotStatus");

        return new Viana3LotDto
        {
            Slot = slot,
            LotId = GetInt(r, "Lot") ?? 0,
            LotKind = lotKind,
            LotKindLabel = LotKindLabel(lotKind),
            LotStatus = statusRaw switch { 1 => "CLOSED", 0 => "OPEN", _ => null },
            BottleArticle = GetString(r, "Bottle"),
            BottleWeightG = bottleWeight,
            PreformArticleId = GetInt(r, "PreformId"),
            PreformArticle = GetString(r, "PlannedPreform"),
            PreformWeightG = preformWeight,
            PlanFrom = GetDateTime(r, "PlanVon"),
            PlanTo = GetDateTime(r, "PlanBis"),
            PlannedBottles = GetDecimal(r, "PlannedBottles"),
            ReportedBottles = reported,
            LastReportedDay = GetDateTime(r, "LastReportedDay"),
            ReportLastUpdated = GetDateTime(r, "ReportLastUpdated"),
            ScanCount = scanCount,
            ScannedPreforms = scanCount > 0 ? scanned : null,
            LastScan = GetDateTime(r, "LastScan"),
            OtherPreformScanCount = GetInt(r, "OtherPreformScanCount") ?? 0,
            OtherPreformsScanned = GetDecimal(r, "OtherPreformsScanned"),
            Waste = new Viana3WasteDto
            {
                Bookings = GetInt(r, "WasteBookings") ?? 0,
                RejectedBottlesKg = bottlesKg,
                RejectedBottlesUnitsEstimate = EstimateUnits(bottlesKg, bottleWeight),
                RejectedPreformsKg = preformsKg,
                RejectedPreformsUnitsEstimate = EstimateUnits(preformsKg, preformWeight),
                LastBooking = GetDateTime(r, "LastWasteBooking"),
                EstimateNote = $"Unidades = estimativa (kg × 1000 ÷ peso do artigo em g). Artigos Waste {o.WasteBottlesArticleId} (garrafas) e {o.WastePreformsArticleId} (pré-formas) mantidos separados.",
            },
            Deviation = ComputeDeviation(lotKind, scanCount, scanned, reported),
        };
    }

    /// <summary>
    /// PROVISIONAL deviation = (scanned − reported) / reported. Only when the lot is a
    /// production lot, at least one scan exists and reported production is positive.
    /// Missing scans are reported as NO_SCAN — never as a zero-based −100 %.
    /// </summary>
    internal static Viana3DeviationDto ComputeDeviation(string lotKind, int scanCount, decimal? scanned, decimal? reported)
    {
        if (!string.Equals(lotKind, "Production", StringComparison.Ordinal))
            return new Viana3DeviationDto { StatusCode = "NOT_APPLICABLE", StatusLabel = "Não aplicável" };

        if (scanCount <= 0 || !scanned.HasValue)
            return new Viana3DeviationDto { StatusCode = "NO_SCAN", StatusLabel = "Sem leitura de scanner" };

        if (!reported.HasValue || reported.Value <= 0m)
            return new Viana3DeviationDto { StatusCode = "NO_REPORT", StatusLabel = "Sem produção reportada" };

        var diff = scanned.Value - reported.Value;
        var ratio = decimal.Round(diff / reported.Value, 4, MidpointRounding.AwayFromZero);

        return new Viana3DeviationDto
        {
            StatusCode = "PROVISIONAL",
            StatusLabel = "Provisório (1 pré-forma = 1 garrafa; Waste não incluído)",
            Ratio = ratio,
            DifferenceUnits = diff,
            Provisional = true,
        };
    }

    /// <summary>ESTIMATE: kg × 1000 / weight (g), rounded to whole units; null without a positive weight.</summary>
    internal static decimal? EstimateUnits(decimal? kg, decimal? weightGrams)
    {
        if (!kg.HasValue || !weightGrams.HasValue || weightGrams.Value <= 0m) return null;
        return decimal.Round(kg.Value * 1000m / weightGrams.Value, 0, MidpointRounding.AwayFromZero);
    }

    private static string LotKindLabel(string lotKind) => lotKind switch
    {
        "Production" => "Produção",
        "Maintenance" => "Manutenção",
        "NoOrder" => "Sem ordem",
        "Placeholder" => "Reservado",
        _ => lotKind,
    };

    private static (string Code, string Label) DeriveMachineStatus(Viana3LotDto? current)
    {
        if (current == null) return ("IDLE", "Sem lote planeado agora");
        return current.LotKind switch
        {
            "Production" => ("RUNNING", "Em produção"),
            "Maintenance" => ("MAINTENANCE", "Manutenção"),
            "NoOrder" => ("NO_ORDER", "Sem ordem"),
            "Placeholder" => ("PLACEHOLDER", "Lote reservado"),
            _ => ("RUNNING", "Em produção"),
        };
    }

    private static (string Code, string Label) ClassifyLocation(int locationId, Viana3ProductionMonitorOptions o)
    {
        if (locationId == o.RetentionLocationId) return ("BLOCKED_RETENTION", "Retenção (bloqueado)");
        if (locationId == o.BufferLocationId) return ("BUFFER", "Buffer");
        return ("LINE_SIDE", "Junto à linha");
    }

    private static Viana3MonitorSourceDto BuildSource(DateTime fetchedAtUtc, long durationMs, string status, string? reason)
    {
        var utc = DateTime.SpecifyKind(fetchedAtUtc, DateTimeKind.Utc);
        var local = DateTime.SpecifyKind(AppTimeZone.ToLocal(utc), DateTimeKind.Unspecified);
        return new Viana3MonitorSourceDto
        {
            Status = status,
            FetchedAtUtc = utc,
            FetchedAtLocal = local,
            AgeSeconds = Math.Max(0, (int)(DateTime.UtcNow - utc).TotalSeconds),
            TimeZoneLabel = AppTimeZone.Label,
            UnavailableReason = reason,
            QueryDurationMs = durationMs,
        };
    }

    private static bool IsSourceFailure(Exception ex) =>
        ex is SqlException || ex is InvalidOperationException || ex is TimeoutException;

    private static string DescribeFailure(Exception ex) => ex switch
    {
        InvalidOperationException ioe => ioe.Message,
        SqlException sql when sql.Number == -2 || sql.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            => "A consulta ao AlplaPROD excedeu o tempo limite.",
        SqlException => "Sem ligação ao AlplaPROD.",
        _ => "Fonte de dados indisponível.",
    };

    // ─── Reader helpers (tolerant of the exact SQL types of AlplaPROD columns) ───

    private static int? GetInt(SqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    }

    private static long? GetLong(SqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToInt64(r.GetValue(i));
    }

    private static decimal? GetDecimal(SqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
    }

    private static string? GetString(SqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i))?.Trim();
    }

    /// <summary>AlplaPROD datetimes are Angola local wall-clock; returned with Kind = Unspecified so they serialise without an offset.</summary>
    private static DateTime? GetDateTime(SqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        if (r.IsDBNull(i)) return null;
        var value = Convert.ToDateTime(r.GetValue(i));
        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }
}
