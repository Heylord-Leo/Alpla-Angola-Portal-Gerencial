using AlplaPortal.Application.DTOs.Integration;
using AlplaPortal.Application.Interfaces.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Integration;

/// <summary>
/// Portal-side Raw Punch Interpreter — read-only.
///
/// Interprets raw terminal punches from TerminaisMarcacoes independently of
/// Innux processed results (Alteracoes). Applies Portal-side direction inference,
/// duplicate detection (without removal), punch pairing, worked-time calculation,
/// and confidence scoring.
///
/// Supported direction inference rules:
///   - Standard EN/SA codes (High confidence)
///   - Codes 17/18: treated as direction-ambiguous — same as empty/null (Medium confidence)
///     Production data confirmed Code 17 is used for both entry and exit by terminals.
///   - Empty/null direction: position-based inference first=Entry, last=Exit (Medium confidence)
///   - Unknown codes: preserved with "Unknown" direction (Low confidence)
///
/// Duplicate detection: consecutive same-direction punches within a configurable
/// time window (default 15 minutes) are flagged as duplicate candidates.
///
/// Duplicate punches are flagged but NEVER removed from the output.
///
/// For overnight shifts, next-day punch collection is bounded by the resolved
/// schedule window when available, with a 12:00 fallback if no schedule exists.
///
/// Read-only: SELECT only, parameterized queries. No writes to Innux.
/// </summary>
public class PortalPunchInterpreter : IPortalPunchInterpreter
{
    private readonly InnuxConnectionFactory _connectionFactory;
    private readonly IPortalScheduleResolver _scheduleResolver;
    private readonly ILogger<PortalPunchInterpreter> _logger;

    /// <summary>
    /// Maximum gap in minutes between two consecutive same-direction punches to
    /// be considered duplicates. Punches within this window are flagged as
    /// IsDuplicateCandidate but kept in the response for audit.
    /// Raised from 2 to 15 minutes to catch real-world duplicate terminal punches
    /// (e.g., employee punches at 07:49 and again at 08:02 on the same shift).
    /// </summary>
    private const int DuplicateThresholdMinutes = 15;

    /// <summary>
    /// Default next-day cutoff hour (00:00–12:00) when no schedule is available
    /// to bound overnight punch collection. A warning is added when this is used.
    /// </summary>
    private const int FallbackOvernightCutoffHour = 12;

    public PortalPunchInterpreter(
        InnuxConnectionFactory connectionFactory,
        IPortalScheduleResolver scheduleResolver,
        ILogger<PortalPunchInterpreter> logger)
    {
        _connectionFactory = connectionFactory;
        _scheduleResolver = scheduleResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PunchInterpretationResultDto> InterpretPunchesAsync(
        int innuxEmployeeId, DateTime date, ResolvedScheduleDto? schedule = null)
    {
        var result = new PunchInterpretationResultDto
        {
            InnuxEmployeeId = innuxEmployeeId,
            Date = date.Date
        };

        try
        {
            // ── Step 1: Resolve schedule if not provided ──
            schedule ??= await _scheduleResolver.ResolveScheduleForDateAsync(innuxEmployeeId, date);
            result.ResolvedSchedule = schedule;

            if (schedule == null)
            {
                result.Warnings.Add("No work plan assigned — schedule could not be resolved.");
            }

            // ── Step 2: Fetch raw punches ──
            await using var connection = await _connectionFactory.CreateConnectionAsync();
            var rawPunches = await FetchRawPunchesAsync(connection, innuxEmployeeId, date, schedule, result.Warnings);

            if (rawPunches.Count == 0)
            {
                result.ConfidenceLevel = "None";
                result.Warnings.Add("No raw terminal punches found for this employee/date.");
                _logger.LogDebug(
                    "PortalPunchInterpreter: No punches for Employee {EmployeeId}, Date {Date:yyyy-MM-dd}",
                    innuxEmployeeId, date);
                return result;
            }

            // ── Step 3: Interpret directions ──
            InterpretDirections(rawPunches, schedule, result.AppliedRules);

            // ── Step 4: Flag duplicates (never remove) ──
            FlagDuplicates(rawPunches, result.Warnings);

            // ── Step 5: Build punch pairs from non-duplicate punches ──
            var activePunches = rawPunches
                .Where(p => !p.IgnoredForCalculation)
                .OrderBy(p => p.PunchTime)
                .ToList();

            var pairs = BuildPunchPairs(activePunches, result.Warnings);

            // ── Step 6: Compute worked minutes ──
            var totalWorkedMinutes = pairs
                .Where(p => p.PairType == "Complete")
                .Sum(p => p.WorkedMinutes);

            // ── Step 7: Extract first entry / last exit ──
            var firstEntry = activePunches
                .Where(p => p.InterpretedDirection == "Entry")
                .OrderBy(p => p.PunchTime)
                .FirstOrDefault();

            var lastExit = activePunches
                .Where(p => p.InterpretedDirection == "Exit")
                .OrderByDescending(p => p.PunchTime)
                .FirstOrDefault();

            // ── Step 8: Compute confidence ──
            var confidence = ComputeConfidence(rawPunches, pairs, result.Warnings);

            // ── Step 9: Populate result ──
            result.RawPunches = rawPunches;
            result.PunchPairs = pairs;
            result.InterpretedFirstEntry = firstEntry?.PunchTime.ToString("HH:mm");
            result.InterpretedLastExit = lastExit?.PunchTime.ToString("HH:mm");
            result.TotalWorkedMinutes = totalWorkedMinutes;
            result.ConfidenceLevel = confidence;

            _logger.LogDebug(
                "PortalPunchInterpreter: Employee {EmployeeId}, Date {Date:yyyy-MM-dd} → " +
                "{PunchCount} punches, {PairCount} pairs, {WorkedMin} worked min, " +
                "Confidence={Confidence}, Warnings={WarningCount}, Rules={RuleCount}",
                innuxEmployeeId, date,
                rawPunches.Count, pairs.Count, totalWorkedMinutes,
                confidence, result.Warnings.Count, result.AppliedRules.Count);

            return result;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PortalPunchInterpreter: Failed for Employee {EmployeeId}, Date {Date:yyyy-MM-dd}",
                innuxEmployeeId, date);
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 2 — Fetch raw punches from TerminaisMarcacoes
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<List<InterpretedPunchDto>> FetchRawPunchesAsync(
        SqlConnection connection, int employeeId, DateTime date,
        ResolvedScheduleDto? schedule, List<string> warnings)
    {
        // Determine next-day cutoff for overnight shifts
        var includeNextDay = schedule?.IsOvernightShift == true;
        string? nextDayCutoff = null;

        if (includeNextDay && schedule != null && !string.IsNullOrEmpty(schedule.ExpectedEndTime))
        {
            // Use schedule-bounded cutoff: expected end time + 2 hours margin
            if (TimeSpan.TryParse(schedule.ExpectedEndTime, out var endTime))
            {
                var cutoffTime = endTime.Add(TimeSpan.FromHours(2));
                if (cutoffTime.TotalHours > 24) cutoffTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59));
                nextDayCutoff = $"1900-01-01 {cutoffTime:hh\\:mm}:00";
            }
        }

        if (includeNextDay && nextDayCutoff == null)
        {
            // Fallback: no schedule or unparsable end time
            nextDayCutoff = $"1900-01-01 {FallbackOvernightCutoffHour:D2}:00:00";
            warnings.Add(
                $"Overnight shift detected but schedule end time unavailable. " +
                $"Using fallback cutoff of {FallbackOvernightCutoffHour:D2}:00 for next-day punch collection.");
        }

        // Also check for potential overnight even without schedule flag
        // (schedule could be null but employee might still be on overnight)
        if (!includeNextDay && schedule == null)
        {
            // When no schedule is available, include next-day punches with fallback to be safe
            includeNextDay = true;
            nextDayCutoff = $"1900-01-01 {FallbackOvernightCutoffHour:D2}:00:00";
            warnings.Add(
                "No schedule available — including next-day punches up to " +
                $"{FallbackOvernightCutoffHour:D2}:00 as fallback for potential overnight shift.");
        }

        var query = @"
            SELECT
                tm.Hora,
                tm.TipoProcessado,
                tm.IDTerminal,
                t.Nome AS TerminalName,
                ISNULL(tm.Gerada, 0) AS Gerada,
                tm.Data AS PunchDate
            FROM dbo.TerminaisMarcacoes tm
            LEFT JOIN dbo.Terminais t ON tm.IDTerminal = t.IDTerminal
            WHERE tm.IDFuncionario = @EmployeeId
              AND (
                  tm.Data = @Date" +
            (includeNextDay ? @"
                  OR (
                      tm.Data = DATEADD(DAY, 1, @Date)
                      AND tm.Hora < @NextDayCutoff
                  )" : "") + @"
              )
            ORDER BY tm.Data, tm.Hora";

        await using var cmd = new SqlCommand(query, connection);
        cmd.Parameters.AddWithValue("@EmployeeId", employeeId);
        cmd.Parameters.AddWithValue("@Date", date.Date);
        if (includeNextDay && nextDayCutoff != null)
        {
            cmd.Parameters.AddWithValue("@NextDayCutoff", nextDayCutoff);
        }
        cmd.CommandTimeout = 15;

        var results = new List<InterpretedPunchDto>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var hora = reader["Hora"] as DateTime?;
            var dir = reader["TipoProcessado"]?.ToString()?.Trim();

            results.Add(new InterpretedPunchDto
            {
                PunchTime = hora ?? DateTime.MinValue,
                PunchTimeFormatted = hora?.ToString("HH:mm:ss") ?? "--:--:--",
                RawDirection = string.IsNullOrWhiteSpace(dir) ? null : dir,
                TerminalName = reader["TerminalName"]?.ToString()?.Trim(),
                TerminalId = reader["IDTerminal"] is int tid ? tid : null,
                IsAutoGenerated = Convert.ToBoolean(reader["Gerada"])
            });
        }

        _logger.LogDebug(
            "PortalPunchInterpreter: Fetched {Count} raw punches for Employee {EmployeeId}, " +
            "Date {Date:yyyy-MM-dd}, IncludeNextDay={IncludeNextDay}",
            results.Count, employeeId, date, includeNextDay);

        return results;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 3 — Interpret directions
    // ═══════════════════════════════════════════════════════════════════════════

    private static void InterpretDirections(
        List<InterpretedPunchDto> punches, ResolvedScheduleDto? schedule, List<string> appliedRules)
    {
        // Pass 1: Apply explicit direction rules.
        // Only EN/SA are treated as reliable explicit directions.
        // Code 17/18 are direction-ambiguous (production data confirmed terminals
        // send Code 17 for both entry and exit) — handled in Pass 2.
        foreach (var punch in punches)
        {
            var dir = punch.RawDirection;

            if (string.Equals(dir, "EN", StringComparison.OrdinalIgnoreCase))
            {
                punch.InterpretedDirection = "Entry";
                punch.DirectionLabel = "Entrada";
                punch.InterpretationRule = "StandardEN";
                punch.InterpretationReason = "Standard EN direction code interpreted as Entry.";
                punch.Confidence = "High";
                AddRuleOnce(appliedRules, "StandardEN");
            }
            else if (string.Equals(dir, "SA", StringComparison.OrdinalIgnoreCase))
            {
                punch.InterpretedDirection = "Exit";
                punch.DirectionLabel = "Saída";
                punch.InterpretationRule = "StandardSA";
                punch.InterpretationReason = "Standard SA direction code interpreted as Exit.";
                punch.Confidence = "High";
                AddRuleOnce(appliedRules, "StandardSA");
            }
            else if (dir is "17" or "18")
            {
                // Code 17/18: direction-ambiguous terminal codes.
                // Validation (2026-05-12) confirmed these codes do NOT reliably
                // indicate Entry vs Exit. Treated as pending for positional inference.
                punch.InterpretedDirection = "Unknown";
                punch.DirectionLabel = $"Código {dir}";
                punch.InterpretationRule = "PendingInference";
                punch.InterpretationReason =
                    $"Code {dir} is direction-ambiguous (terminal sends same code for entry and exit). " +
                    "Pending positional inference.";
                punch.Confidence = "Low";
                AddRuleOnce(appliedRules, "Code17_18Ambiguous");
            }
            else if (string.IsNullOrWhiteSpace(dir))
            {
                // Mark as pending — will be resolved in Pass 2
                punch.InterpretedDirection = "Unknown";
                punch.DirectionLabel = "Sem direção";
                punch.InterpretationRule = "PendingInference";
                punch.InterpretationReason = "Empty/null direction — pending positional inference.";
                punch.Confidence = "Low";
            }
            else
            {
                // Truly unknown code
                punch.InterpretedDirection = "Unknown";
                punch.DirectionLabel = int.TryParse(dir, out _) ? $"Código {dir}" : dir;
                punch.InterpretationRule = "UnknownCode";
                punch.InterpretationReason = $"Unknown direction code '{dir}' — cannot be interpreted automatically.";
                punch.Confidence = "Low";
                AddRuleOnce(appliedRules, "UnknownCode");
            }
        }

        // Pass 2: Infer directions for all pending punches using positional logic.
        // This covers empty/null, Code 17, and Code 18 punches.
        var pendingPunches = punches
            .Where(p => p.InterpretationRule == "PendingInference")
            .OrderBy(p => p.PunchTime)
            .ToList();

        if (pendingPunches.Count >= 2)
        {
            // First pending punch = Entry, Last = Exit
            var first = pendingPunches.First();
            var rawLabel = first.RawDirection != null ? $" (raw code '{first.RawDirection}')" : "";
            first.InterpretedDirection = "Entry";
            first.DirectionLabel = "Entrada";
            first.InterpretationRule = "InferredFirstEntry";
            first.InterpretationReason =
                $"Direction{rawLabel} interpreted as Entry — first punch within the shift window (position-based inference).";
            first.Confidence = "Medium";
            AddRuleOnce(appliedRules, "InferredFirstEntry");

            var last = pendingPunches.Last();
            if (last != first)
            {
                var lastRawLabel = last.RawDirection != null ? $" (raw code '{last.RawDirection}')" : "";
                last.InterpretedDirection = "Exit";
                last.DirectionLabel = "Saída";
                last.InterpretationRule = "InferredLastExit";
                last.InterpretationReason =
                    $"Direction{lastRawLabel} interpreted as Exit — last punch within the shift window (position-based inference).";
                last.Confidence = "Medium";
                AddRuleOnce(appliedRules, "InferredLastExit");
            }

            // Middle punches: alternate Entry/Exit
            for (var i = 1; i < pendingPunches.Count - 1; i++)
            {
                var p = pendingPunches[i];
                var isEntry = i % 2 == 0;
                var midRawLabel = p.RawDirection != null ? $" (raw code '{p.RawDirection}')" : "";
                p.InterpretedDirection = isEntry ? "Entry" : "Exit";
                p.DirectionLabel = isEntry ? "Entrada" : "Saída";
                p.InterpretationRule = "InferredAlternating";
                p.InterpretationReason =
                    $"Direction{midRawLabel} interpreted as {(isEntry ? "Entry" : "Exit")} based on alternating position (index {i}).";
                p.Confidence = "Low";
                AddRuleOnce(appliedRules, "InferredAlternating");
            }
        }
        else if (pendingPunches.Count == 1)
        {
            // Single pending punch — try to infer from context
            var single = pendingPunches[0];
            var singleRawLabel = single.RawDirection != null ? $" (raw code '{single.RawDirection}')" : "";
            var hasAnyEntry = punches.Any(p =>
                p != single && p.InterpretedDirection == "Entry" && !p.IgnoredForCalculation);
            var hasAnyExit = punches.Any(p =>
                p != single && p.InterpretedDirection == "Exit" && !p.IgnoredForCalculation);

            if (hasAnyEntry && !hasAnyExit)
            {
                single.InterpretedDirection = "Exit";
                single.DirectionLabel = "Saída";
                single.InterpretationRule = "InferredMissingExit";
                single.InterpretationReason =
                    $"Direction{singleRawLabel} interpreted as Exit because an Entry already exists but no Exit was found.";
                single.Confidence = "Medium";
                AddRuleOnce(appliedRules, "InferredMissingExit");
            }
            else if (!hasAnyEntry && hasAnyExit)
            {
                single.InterpretedDirection = "Entry";
                single.DirectionLabel = "Entrada";
                single.InterpretationRule = "InferredMissingEntry";
                single.InterpretationReason =
                    $"Direction{singleRawLabel} interpreted as Entry because an Exit already exists but no Entry was found.";
                single.Confidence = "Medium";
                AddRuleOnce(appliedRules, "InferredMissingEntry");
            }
            else
            {
                // Cannot determine — leave as unknown
                single.InterpretationRule = "InferredAmbiguous";
                single.InterpretationReason =
                    $"Single punch{singleRawLabel} with no pairing context — cannot determine Entry or Exit.";
                single.Confidence = "Low";
                AddRuleOnce(appliedRules, "InferredAmbiguous");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 4 — Flag duplicates
    // ═══════════════════════════════════════════════════════════════════════════

    private static void FlagDuplicates(List<InterpretedPunchDto> punches, List<string> warnings)
    {
        var ordered = punches.OrderBy(p => p.PunchTime).ToList();
        var duplicateCount = 0;

        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var curr = ordered[i];

            // Already flagged as duplicate → skip (don't cascade)
            if (prev.IsDuplicateCandidate)
                continue;

            // Same interpreted direction, within threshold minutes.
            // Terminal matching is NOT required — production data shows duplicate
            // punches can occur on different terminals (e.g., employee punches at
            // entry terminal twice, once at 07:49 and again at 08:02).
            var withinThreshold = (curr.PunchTime - prev.PunchTime).TotalMinutes <= DuplicateThresholdMinutes;
            var sameDirection = curr.InterpretedDirection == prev.InterpretedDirection &&
                                curr.InterpretedDirection != "Unknown";

            if (withinThreshold && sameDirection)
            {
                var terminalInfo = curr.TerminalId.HasValue && prev.TerminalId.HasValue &&
                                   curr.TerminalId == prev.TerminalId
                    ? $" on same terminal (ID={curr.TerminalId})"
                    : "";

                // Flag the later punch as duplicate (keep the earlier one)
                curr.IsDuplicateCandidate = true;
                curr.IgnoredForCalculation = true;
                curr.DuplicateReason =
                    $"Same-direction ({curr.InterpretedDirection}) punch within {DuplicateThresholdMinutes} minutes " +
                    $"of previous punch ({prev.PunchTimeFormatted}){terminalInfo}. " +
                    $"Gap: {(curr.PunchTime - prev.PunchTime).TotalMinutes:F0} min.";
                duplicateCount++;
            }
        }

        if (duplicateCount > 0)
        {
            warnings.Add($"{duplicateCount} duplicate punch(es) flagged (same direction within {DuplicateThresholdMinutes}-minute window). Kept in response for audit.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 5 — Build punch pairs
    // ═══════════════════════════════════════════════════════════════════════════

    private static List<PunchPairDto> BuildPunchPairs(
        List<InterpretedPunchDto> activePunches, List<string> warnings)
    {
        var pairs = new List<PunchPairDto>();
        var entries = new Queue<InterpretedPunchDto>(
            activePunches.Where(p => p.InterpretedDirection == "Entry").OrderBy(p => p.PunchTime));
        var exits = new Queue<InterpretedPunchDto>(
            activePunches.Where(p => p.InterpretedDirection == "Exit").OrderBy(p => p.PunchTime));

        while (entries.Count > 0 || exits.Count > 0)
        {
            if (entries.Count > 0 && exits.Count > 0)
            {
                var entry = entries.Peek();
                var exit = exits.Peek();

                if (entry.PunchTime <= exit.PunchTime)
                {
                    // Normal pair
                    entries.Dequeue();
                    exits.Dequeue();
                    var workedMinutes = (int)(exit.PunchTime - entry.PunchTime).TotalMinutes;
                    pairs.Add(new PunchPairDto
                    {
                        Entry = entry,
                        Exit = exit,
                        WorkedMinutes = workedMinutes,
                        PairType = "Complete"
                    });
                }
                else
                {
                    // Exit before entry — exit has no matching entry
                    exits.Dequeue();
                    pairs.Add(new PunchPairDto
                    {
                        Exit = exit,
                        WorkedMinutes = 0,
                        PairType = "MissingEntry"
                    });
                    warnings.Add($"Exit punch at {exit.PunchTimeFormatted} has no preceding Entry — missing entry.");
                }
            }
            else if (entries.Count > 0)
            {
                // Remaining entries with no exits
                var entry = entries.Dequeue();
                pairs.Add(new PunchPairDto
                {
                    Entry = entry,
                    WorkedMinutes = 0,
                    PairType = "MissingExit"
                });
                warnings.Add($"Entry punch at {entry.PunchTimeFormatted} has no matching Exit — missing exit.");
            }
            else
            {
                // Remaining exits with no entries
                var exit = exits.Dequeue();
                pairs.Add(new PunchPairDto
                {
                    Exit = exit,
                    WorkedMinutes = 0,
                    PairType = "MissingEntry"
                });
                warnings.Add($"Exit punch at {exit.PunchTimeFormatted} has no preceding Entry — missing entry.");
            }
        }

        // Check for unknown-direction punches that couldn't be paired
        var unknownPunches = activePunches.Where(p => p.InterpretedDirection == "Unknown").ToList();
        if (unknownPunches.Count > 0)
        {
            warnings.Add($"{unknownPunches.Count} punch(es) with unknown direction could not be paired.");
        }

        return pairs;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 8 — Compute confidence
    // ═══════════════════════════════════════════════════════════════════════════

    private static string ComputeConfidence(
        List<InterpretedPunchDto> allPunches, List<PunchPairDto> pairs, List<string> warnings)
    {
        if (allPunches.Count == 0)
            return "None";

        var hasUnknownCodes = allPunches.Any(p =>
            p.InterpretationRule == "UnknownCode" || p.InterpretationRule == "InferredAmbiguous");
        var hasIncompletePairs = pairs.Any(p => p.PairType != "Complete");
        var hasFallbackOvernight = warnings.Any(w => w.Contains("fallback", StringComparison.OrdinalIgnoreCase));
        var hasAlternateOrInferred = allPunches.Any(p =>
            p.InterpretationRule is "Code17_18Ambiguous"
                or "InferredFirstEntry" or "InferredLastExit"
                or "InferredAlternating" or "InferredMissingEntry" or "InferredMissingExit");

        // Low: unknown codes, incomplete pairs, or fallback overnight
        if (hasUnknownCodes || hasIncompletePairs || hasFallbackOvernight)
            return "Low";

        // Medium: alternate codes or inferred directions, but result is coherent
        if (hasAlternateOrInferred)
            return "Medium";

        // High: all standard EN/SA, all pairs complete
        return "High";
    }

    // ─── Helpers ───

    private static void AddRuleOnce(List<string> rules, string rule)
    {
        if (!rules.Contains(rule))
            rules.Add(rule);
    }
}
