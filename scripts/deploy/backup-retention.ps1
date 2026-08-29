# ===========================================================================
# scripts/deploy/backup-retention.ps1 - Approved deployment-backup retention
#
# APPROVED POLICY (2026-08-29, intentionally conservative):
#   KEEP a backup if it is among the NEWEST 5 completed backups
#        OR its age is <= 14 days.
#   DELETE only if it is NOT among the newest 5 AND older than 14 days.
#
# Modes:
#   Application - directories named backup_<yyyyMMdd_HHmmss> under -BackupRoot.
#                 Only directories carrying backup-complete.marker are ever
#                 eligible; the current run's backup (-CurrentBackupName) is
#                 always protected; anything else (db\, dev-export\, unrelated
#                 or malformed names, incomplete backups) is never touched.
#   Database    - files matching the EXACT repository backup filename patterns:
#                   Portal-Gerencial_<ts>.bak                (deploy-prod pre-deploy)
#                   Portal-Gerencial_<ts>_pre-migration.bak  (apply-migrations PROD)
#                   Portal-Gerencial-Test_<ts>_pre-migration.bak (apply-migrations TEST)
#                 Unrelated .bak files and dev-export content are never touched
#                 (dev-export lives OUTSIDE the db\ root this mode is given).
#
# Failure semantics: retention is BEST-EFFORT and never throws for cleanup
# problems - failed removals are reported as GitHub Actions warnings and the
# script exits 0, so an already-successful backup/deployment is never
# invalidated by cleanup trouble. (Backup CREATION failures remain fail-closed
# in the calling workflows - that is a different, blocking gate.)
#
# Age is derived from the timestamp embedded in the backup NAME (creation
# moment, server-local time), never from mtime, so copies/scans cannot
# accidentally rejuvenate a backup.
# ===========================================================================
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupRoot,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Application', 'Database')]
    [string]$Mode,

    # Application mode: directory NAME of the backup created by the current run.
    [string]$CurrentBackupName = '',

    [int]$KeepNewest = 5,
    [int]$MaxAgeDays = 14,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Get-NameTimestamp([string]$stamp) {
    # yyyyMMdd_HHmmss (server-local), the single timestamp convention of every
    # repository backup producer.
    return [datetime]::ParseExact($stamp, 'yyyyMMdd_HHmmss', [System.Globalization.CultureInfo]::InvariantCulture)
}

try {
    if (-not (Test-Path -LiteralPath $BackupRoot)) {
        Write-Host "Retention: backup root '$BackupRoot' does not exist - nothing to do."
        exit 0
    }
    $root = (Resolve-Path -LiteralPath $BackupRoot).Path.TrimEnd('\')
    $cutoff = (Get-Date).AddDays(-$MaxAgeDays)

    Write-Host "Retention run - root: $root"
    Write-Host ("Policy: always keep newest {0} completed; delete others only when older than {1} days (cutoff {2:yyyy-MM-dd HH:mm})." -f $KeepNewest, $MaxAgeDays, $cutoff)
    if ($DryRun) { Write-Host "MODE: DRY-RUN - nothing will be deleted." }

    # ── Collect eligible items with their name-derived timestamps ──
    $items = @()
    if ($Mode -eq 'Application') {
        $pattern = '^backup_(\d{8}_\d{6})$'
        foreach ($dir in (Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
            $m = [regex]::Match($dir.Name, $pattern)
            if (-not $m.Success) { continue } # unrelated/malformed names are invisible to retention
            if ($CurrentBackupName -and ($dir.Name -ieq $CurrentBackupName)) {
                Write-Host "Protected (current run's backup): $($dir.Name)"
                continue
            }
            if (-not (Test-Path -LiteralPath (Join-Path $dir.FullName 'backup-complete.marker'))) {
                Write-Host "Protected (incomplete - no backup-complete.marker): $($dir.Name)"
                continue
            }
            $items += [pscustomobject]@{ Name = $dir.Name; FullName = $dir.FullName; Stamp = Get-NameTimestamp $m.Groups[1].Value; IsDir = $true }
        }
    }
    else {
        $pattern = '^(Portal-Gerencial|Portal-Gerencial-Test)_(\d{8}_\d{6})(_pre-migration)?\.bak$'
        foreach ($file in (Get-ChildItem -LiteralPath $root -File -ErrorAction SilentlyContinue)) {
            $m = [regex]::Match($file.Name, $pattern)
            if (-not $m.Success) { continue } # unrelated .bak / other files are invisible to retention
            $items += [pscustomobject]@{ Name = $file.Name; FullName = $file.FullName; Stamp = Get-NameTimestamp $m.Groups[2].Value; IsDir = $false }
        }
    }

    $sorted = @($items | Sort-Object Stamp -Descending)
    $protectedNewest = @($sorted | Select-Object -First $KeepNewest)
    $candidates = @($sorted | Select-Object -Skip $KeepNewest | Where-Object { $_.Stamp -lt $cutoff })

    Write-Host "Completed backups considered: $($sorted.Count)"
    Write-Host "Newest-$KeepNewest protected set: $(if ($protectedNewest) { ($protectedNewest.Name -join ', ') } else { '(none)' })"

    if ($candidates.Count -eq 0) {
        Write-Host "No backups eligible for retention cleanup."
        exit 0
    }

    Write-Host "Deletion candidates ($($candidates.Count)): $($candidates.Name -join ', ')"

    $removed = @(); $failed = @()
    foreach ($item in $candidates) {
        # Path-containment guard: the target's PARENT must be exactly the backup root.
        $parent = [System.IO.Path]::GetDirectoryName($item.FullName)
        if ($parent.TrimEnd('\') -ne $root) {
            Write-Host "::warning::Retention skipped '$($item.FullName)' - not a direct child of '$root'."
            $failed += $item.Name
            continue
        }
        if ($DryRun) {
            Write-Host "DRY-RUN would remove: $($item.FullName)"
            $removed += $item.Name
            continue
        }
        try {
            if ($item.IsDir) { Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction Stop }
            else { Remove-Item -LiteralPath $item.FullName -Force -ErrorAction Stop }
            $removed += $item.Name
        }
        catch {
            # Best-effort: a cleanup failure never invalidates the successful backup.
            Write-Host "::warning::Retention could not remove '$($item.FullName)': $($_.Exception.Message)"
            $failed += $item.Name
        }
    }

    Write-Host "Removed ($($removed.Count)): $(if ($removed) { $removed -join ', ' } else { '(none)' })"
    if ($failed.Count -gt 0) {
        Write-Host "::warning::Retention finished with $($failed.Count) failed removal(s): $($failed -join ', ')"
    }
    exit 0
}
catch {
    # Even unexpected retention errors are non-blocking by design.
    Write-Host "::warning::Retention cleanup encountered an unexpected error and was skipped: $($_.Exception.Message)"
    exit 0
}
