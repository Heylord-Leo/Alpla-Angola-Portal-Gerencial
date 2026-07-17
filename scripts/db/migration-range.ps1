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
