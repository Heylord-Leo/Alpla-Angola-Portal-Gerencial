<#
.SYNOPSIS
    Pure, DB-independent helpers for incremental (FROM/TO) EF Core migration scripting.

.DESCRIPTION
    Extracted from apply-migrations.ps1 so the prefix validation and range determination can be unit
    tested without a database (see migration-range.Tests.ps1). No side effects, no DB access.

    Rationale (DEC-145): a full idempotent script generated from the first migration re-emits the body
    of already-applied historical migrations. When a historical migration references a column that a
    LATER migration dropped (e.g. Departments.ResponsibleUserId), SQL Server fails to COMPILE the
    inline reference inside the guarded IF...BEGIN...END block (error 207) before the runtime
    IF NOT EXISTS guard can skip it. Generating only the pending range (last-applied -> last-expected)
    excludes those historical bodies.
#>

# ─────────────────────────────────────────────────────────────────────────────
# Test-MigrationPrefix
#   Confirms the applied migrations form an EXACT, CONTIGUOUS prefix of the expected (filesystem)
#   list, using the project's canonical order (the caller passes it in; we never re-sort).
#   Returns a PSCustomObject: Valid (bool), Reason (string), Index (int), Expected (string), Found (string).
# ─────────────────────────────────────────────────────────────────────────────
function Test-MigrationPrefix {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Expected,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Applied
    )

    $result = [PSCustomObject]@{ Valid = $true; Reason = ''; Index = -1; Expected = ''; Found = '' }

    # (7) Duplicates in either list.
    $expDup = @($Expected | Group-Object | Where-Object { $_.Count -gt 1 })
    if ($expDup.Count -gt 0) {
        return [PSCustomObject]@{ Valid = $false; Reason = "Duplicate MigrationId in expected (filesystem) list: $($expDup[0].Name)"; Index = -1; Expected = $expDup[0].Name; Found = $expDup[0].Name }
    }
    $appDup = @($Applied | Group-Object | Where-Object { $_.Count -gt 1 })
    if ($appDup.Count -gt 0) {
        return [PSCustomObject]@{ Valid = $false; Reason = "Duplicate MigrationId in applied (__EFMigrationsHistory) list: $($appDup[0].Name)"; Index = -1; Expected = $appDup[0].Name; Found = $appDup[0].Name }
    }

    # (2) More applied than expected.
    if ($Applied.Count -gt $Expected.Count) {
        return [PSCustomObject]@{ Valid = $false; Reason = "More applied migrations ($($Applied.Count)) than expected on filesystem ($($Expected.Count))."; Index = -1; Expected = ''; Found = '' }
    }

    # (1,3,4,5,6) Positional prefix: expected[0..applied.Count-1] must equal applied[0..applied.Count-1].
    for ($i = 0; $i -lt $Applied.Count; $i++) {
        if ($Expected[$i] -ne $Applied[$i]) {
            return [PSCustomObject]@{
                Valid    = $false
                Reason   = "Applied migrations are not an exact prefix of the filesystem list at position $i."
                Index    = $i
                Expected = $Expected[$i]
                Found    = $Applied[$i]
            }
        }
    }

    return $result
}

# ─────────────────────────────────────────────────────────────────────────────
# Get-MigrationRange
#   Given a VALIDATED prefix, returns From / To / Pending.
#     From = last applied MigrationId, or '0' when the database is empty.
#     To   = last expected (filesystem) MigrationId.
#     Pending = expected migrations not yet applied (in canonical order).
# ─────────────────────────────────────────────────────────────────────────────
function Get-MigrationRange {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Expected,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Applied
    )

    $pending = @($Expected | Where-Object { $Applied -notcontains $_ })
    $from = if ($Applied.Count -gt 0) { $Applied[$Applied.Count - 1] } else { '0' }
    $to = if ($Expected.Count -gt 0) { $Expected[$Expected.Count - 1] } else { '0' }

    return [PSCustomObject]@{ From = $from; To = $to; Pending = $pending }
}

# ─────────────────────────────────────────────────────────────────────────────
# Get-MigrationIdsFromScript
#   Extracts the MigrationIds that the generated SQL INSERTs into __EFMigrationsHistory.
#   This is the authoritative "what will be recorded as applied" set — not a generic text search.
# ─────────────────────────────────────────────────────────────────────────────
function Get-MigrationIdsFromScript {
    param([Parameter(Mandatory = $true)][string]$SqlContent)

    $ids = @()
    $pattern = "INSERT\s+INTO\s+\[__EFMigrationsHistory\]\s*\(\s*\[MigrationId\]\s*,\s*\[ProductVersion\]\s*\)\s*VALUES\s*\(\s*N'([^']+)'"
    foreach ($m in [regex]::Matches($SqlContent, $pattern, 'IgnoreCase, Singleline')) {
        $ids += $m.Groups[1].Value
    }
    return $ids
}

# ─────────────────────────────────────────────────────────────────────────────
# Test-ModelSnapshotNoLegacyProperty
#   Confirms the CURRENT EF model (ApplicationDbContextModelSnapshot.cs) does not
#   define a given property (or a foreign key on it) for a given entity — e.g. that
#   Departments.ResponsibleUserId was actually removed from the model, not just
#   physically dropped by a migration while a stale property mapping lingers.
#
#   Scoped to the named entity's block(s) only — EF Core snapshots repeat
#   'modelBuilder.Entity("<FullName>", b => ...)' once for properties and once for
#   relationships. An unrelated, differently-named property on the SAME or a
#   DIFFERENT entity (e.g. "CurrentResponsibleUserId" on another table) must never
#   match — this checks the exact quoted property/FK name, not a substring.
# ─────────────────────────────────────────────────────────────────────────────
function Test-ModelSnapshotNoLegacyProperty {
    param(
        [Parameter(Mandatory = $true)][string]$SnapshotContent,
        [Parameter(Mandatory = $true)][string]$EntityFullName,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $entityMarker = [regex]::Escape("modelBuilder.Entity(`"$EntityFullName`"")
    $blockStarts = @([regex]::Matches($SnapshotContent, $entityMarker) | ForEach-Object { $_.Index })
    $allMarkers = @([regex]::Matches($SnapshotContent, 'modelBuilder\.Entity\(') | ForEach-Object { $_.Index } | Sort-Object)

    $prop = [regex]::Escape($PropertyName)
    $propPattern = "Property<[^>]*>\(\s*`"$prop`"\s*\)"
    $fkPattern = "HasForeignKey\(\s*`"$prop`"\s*\)"

    foreach ($start in $blockStarts) {
        $nextMarker = $allMarkers | Where-Object { $_ -gt $start } | Select-Object -First 1
        $blockText = if ($nextMarker) { $SnapshotContent.Substring($start, $nextMarker - $start) } else { $SnapshotContent.Substring($start) }

        if ([regex]::IsMatch($blockText, $propPattern) -or [regex]::IsMatch($blockText, $fkPattern)) {
            return [PSCustomObject]@{ Safe = $false; Reason = "Entity '$EntityFullName' still defines or references property '$PropertyName' in the current model snapshot." }
        }
    }

    return [PSCustomObject]@{ Safe = $true; Reason = '' }
}

# ─────────────────────────────────────────────────────────────────────────────
# Test-ResponsibleUserIdSafety
#   Guards a dropped historical column (e.g. Departments.ResponsibleUserId) in an
#   INCREMENTAL idempotent script without blindly rejecting every mention of its name.
#   A migration that legitimately reads the column before it is dropped (a safe
#   backfill into a new model) or that drops it must be permitted; a migration that
#   recreates, repopulates, indexes or constrains it must not.
#
#   Two independent layers, both must pass:
#     1. Position layer — every occurrence is attributed to the migration block it
#        appears in (the nearest preceding EF idempotent guard marker,
#        "[MigrationId] = N'<id>'"). Any occurrence owned by a migration positioned
#        AFTER $BoundaryMigrationId in $ExpectedMigrations is rejected outright — from
#        that point on the column no longer exists, so any reference is a bug.
#     2. Pattern layer — regardless of position, a fixed set of dangerous SQL shapes
#        is always rejected: adding the column back, indexing it, adding a
#        constraint/FK against it, altering it, or writing (UPDATE/INSERT) into the
#        column on its OWN table. Reads, DROP COLUMN/INDEX/CONSTRAINT, catalog
#        lookups, and inserts into a DIFFERENT table (e.g. an audit backup) that
#        merely reference the column name are never flagged.
#
#   Returns a PSCustomObject: Safe (bool), PositionViolations (string[] of migration
#   ids), PatternViolations (string[] of matched dangerous-pattern names).
# ─────────────────────────────────────────────────────────────────────────────
function Test-ResponsibleUserIdSafety {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$SqlContent,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$ExpectedMigrations,
        [Parameter(Mandatory = $true)][string]$BoundaryMigrationId,
        [Parameter(Mandatory = $false)][string]$OwnerTable = "Departments",
        [Parameter(Mandatory = $false)][string]$ColumnName = "ResponsibleUserId"
    )

    $result = [PSCustomObject]@{ Safe = $true; PositionViolations = @(); PatternViolations = @() }

    if ($SqlContent -notmatch [regex]::Escape($ColumnName)) {
        return $result
    }

    $boundaryIndex = [array]::IndexOf($ExpectedMigrations, $BoundaryMigrationId)

    # --- Layer 1: position (which migration block owns each occurrence) ---
    $markerRegex = [regex]"\[MigrationId\]\s*=\s*N'(?<id>[0-9A-Za-z_]+)'"
    $markers = @($markerRegex.Matches($SqlContent) | ForEach-Object {
        [PSCustomObject]@{ Id = $_.Groups['id'].Value; Index = $_.Index }
    } | Sort-Object Index)

    $occurrences = [regex]::Matches($SqlContent, [regex]::Escape($ColumnName))
    $violatingMigrations = @()
    foreach ($occ in $occurrences) {
        $owner = $markers | Where-Object { $_.Index -le $occ.Index } | Select-Object -Last 1
        if (-not $owner) { continue }
        $ownerIndex = [array]::IndexOf($ExpectedMigrations, $owner.Id)
        if ($ownerIndex -gt $boundaryIndex) {
            $violatingMigrations += $owner.Id
        }
    }
    $violatingMigrations = @($violatingMigrations | Select-Object -Unique)
    if ($violatingMigrations.Count -gt 0) {
        $result.Safe = $false
        $result.PositionViolations = $violatingMigrations
    }

    # --- Layer 2: dangerous patterns (position-independent; legitimate usage never matches) ---
    $col = [regex]::Escape($ColumnName)
    $tbl = [regex]::Escape($OwnerTable)
    $dangerousPatterns = @(
        @{ Name = "ADD COLUMN $ColumnName"; Regex = "ADD\s+\[?$col\]?\s+\w" }
        @{ Name = "CREATE INDEX referencing $ColumnName"; Regex = "CREATE\s+(UNIQUE\s+)?(NONCLUSTERED\s+|CLUSTERED\s+)?INDEX\s+\[[^\]]*\]\s+ON\s+\[[^\]]*\]\s*\([^)]*$col[^)]*\)" }
        @{ Name = "ADD CONSTRAINT/FOREIGN KEY referencing $ColumnName"; Regex = "ADD\s+CONSTRAINT\s+\[[^\]]*\]\s+FOREIGN\s+KEY\s*\([^)]*$col[^)]*\)" }
        @{ Name = "ALTER COLUMN $ColumnName"; Regex = "ALTER\s+TABLE\s+\[$tbl\]\s+ALTER\s+COLUMN\s+\[$col\]" }
        @{ Name = "UPDATE $OwnerTable SET $ColumnName"; Regex = "UPDATE\s+\[?$tbl\]?[\s\S]{0,200}?SET[\s\S]{0,200}?\[?$col\]?\s*=" }
        @{ Name = "INSERT INTO $OwnerTable referencing $ColumnName"; Regex = "INSERT\s+INTO\s+\[$tbl\][\s\S]{0,300}?$col" }
    )
    $patternHits = @()
    foreach ($p in $dangerousPatterns) {
        if ([regex]::IsMatch($SqlContent, $p.Regex, 'IgnoreCase')) {
            $patternHits += $p.Name
        }
    }
    if ($patternHits.Count -gt 0) {
        $result.Safe = $false
        $result.PatternViolations = $patternHits
    }

    return $result
}
