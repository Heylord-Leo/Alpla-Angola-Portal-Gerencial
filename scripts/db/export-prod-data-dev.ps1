param(
    [Parameter(Mandatory = $true)]
    [string]$ProdDbName,

    [Parameter(Mandatory = $true)]
    [string]$BackupDir,

    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$ReleaseVersion,

    [Parameter(Mandatory = $true)]
    [string]$RunId,

    [Parameter(Mandatory = $true)]
    [string]$RunAttempt
)

# =============================================================================
# Export PROD Data for Dev - AOVIA1VMS011
# =============================================================================
#
# Read-only against Production: the ONLY statement executed against
# Portal-Gerencial is BACKUP DATABASE, which never modifies source data.
# Reuses the same PROD_DB_CONNECTION_STRING secret already used by
# deploy-prod.yml / apply-migrations-prod.yml (no new secret is created).
#
# Output: a timestamped, run-scoped .bak file plus a paired .bak.sha256
# checksum file under $BackupDir, whose paths are written to $env:GITHUB_OUTPUT
# for the calling workflow to upload as a build artifact and later delete from
# this runner's disk.
#
# Safety notes:
#   - $BackupDir is resolved and validated before any file is written: drive
#     roots, the Windows directory, Program Files directories, the repository
#     root, and the known API/web deployment roots are all rejected, and the
#     final path segment must be exactly 'dev-export'.
#   - The backup filename embeds the GitHub Actions run ID and run attempt (in
#     addition to a timestamp) so two runs can never collide on a filename,
#     even if both start within the same second.
#   - If any step after the backup file is created fails before this script's
#     GITHUB_OUTPUT lines are written, the catch block below deletes whatever
#     partial file(s) it created, since the workflow's own cleanup step (which
#     only knows about $env:GITHUB_OUTPUT values) would otherwise have no path
#     to act on.
# =============================================================================

$ErrorActionPreference = "Stop"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Export PROD Data for Dev - AOVIA1VMS011" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# -----------------------------------------------------------------------------
# 0. Sanitize run identifiers before they are ever used in a filename
# -----------------------------------------------------------------------------
if ($RunId -notmatch '^[0-9]+$') {
    throw "VALIDATION FAILED: -RunId must be numeric (got '$RunId')."
}
if ($RunAttempt -notmatch '^[0-9]+$') {
    throw "VALIDATION FAILED: -RunAttempt must be numeric (got '$RunAttempt')."
}

# -----------------------------------------------------------------------------
# 1. Resolve and validate the backup directory before touching the filesystem
# -----------------------------------------------------------------------------
$repoRoot = Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -Parent

function Test-PathIsUnderOrEqual {
    param([string]$Path, [string]$Root)
    $normalizedPath = $Path.TrimEnd('\')
    $normalizedRoot = $Root.TrimEnd('\')
    return ($normalizedPath -ieq $normalizedRoot) -or ($normalizedPath -like "$normalizedRoot\*")
}

if ([string]::IsNullOrWhiteSpace($BackupDir)) {
    throw "VALIDATION FAILED: -BackupDir must not be empty."
}
if (-not [System.IO.Path]::IsPathRooted($BackupDir)) {
    throw "VALIDATION FAILED: -BackupDir must be an absolute path. Got: '$BackupDir'."
}

$resolvedBackupDir = [System.IO.Path]::GetFullPath($BackupDir)

$forbiddenRoots = @(
    $env:WINDIR,
    "C:\Program Files",
    "C:\Program Files (x86)",
    $repoRoot,
    "C:\Apps\AlplaPortal\Prod\api",
    "C:\Apps\AlplaPortal\Prod\web",
    "C:\Apps\AlplaPortal\Test\api",
    "C:\Apps\AlplaPortal\Test\web"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$driveRoot = [System.IO.Path]::GetPathRoot($resolvedBackupDir)
if ($resolvedBackupDir.TrimEnd('\') -ieq $driveRoot.TrimEnd('\')) {
    throw "VALIDATION FAILED: -BackupDir resolves to a drive root ('$resolvedBackupDir'). Refusing to use a drive root for Production backups."
}

foreach ($forbiddenRoot in $forbiddenRoots) {
    if (Test-PathIsUnderOrEqual -Path $resolvedBackupDir -Root $forbiddenRoot) {
        throw "VALIDATION FAILED: -BackupDir ('$resolvedBackupDir') resolves under a protected directory ('$forbiddenRoot'). Refusing to proceed."
    }
}

$leafDirName = Split-Path -Leaf $resolvedBackupDir
if ($leafDirName -ne 'dev-export') {
    throw "VALIDATION FAILED: -BackupDir must resolve to a directory named exactly 'dev-export' (the dedicated export subfolder below BACKUP_PATH). Got leaf directory: '$leafDirName' (full path: '$resolvedBackupDir')."
}

Write-Host "Resolved backup directory: $resolvedBackupDir" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 2. Read Connection String (GitHub Secret - reused, not duplicated)
# -----------------------------------------------------------------------------
$connStr = $env:PROD_DB_CONNECTION_STRING

if ([string]::IsNullOrWhiteSpace($connStr)) {
    throw "PROD_DB_CONNECTION_STRING nao definida. Configure a secret no environment production antes de executar o workflow."
}

$maskedConnStr = $connStr
if ($maskedConnStr -match 'Password=([^;]+)') {
    $maskedConnStr = $maskedConnStr -replace 'Password=[^;]+', 'Password=********'
}
if ($maskedConnStr -match 'pwd=([^;]+)') {
    $maskedConnStr = $maskedConnStr -replace 'pwd=[^;]+', 'pwd=********'
}
Write-Host "Connection string de PROD encontrada via GitHub Secret PROD_DB_CONNECTION_STRING." -ForegroundColor Green
Write-Host "Mascarada: $maskedConnStr" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 3. Database Identity Safety Check (before any BACKUP is attempted)
# -----------------------------------------------------------------------------
$forbiddenDatabaseNames = @('Portal-Gerencial-Test', 'AlplaPortalV1')

$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
$backupFile = $null
$checksumFile = $null

try {
    $conn.Open()

    $dbCmd = $conn.CreateCommand()
    $dbCmd.CommandText = "SELECT DB_NAME() AS DbName"
    $connectedDbName = [string]$dbCmd.ExecuteScalar()

    Write-Host "Conectado ao banco: [$connectedDbName]" -ForegroundColor Green

    if ($forbiddenDatabaseNames -contains $connectedDbName) {
        throw "CRITICAL SAFETY CHECK FAILED: connection string resolves to a forbidden database [$connectedDbName]. Export BLOCKED."
    }

    if ($connectedDbName -ne "Portal-Gerencial" -or $ProdDbName -ne "Portal-Gerencial") {
        throw "SAFETY CHECK FAILED: expected database [Portal-Gerencial] (connected: [$connectedDbName], parameter: [$ProdDbName]). Export BLOCKED."
    }

    Write-Host "OK - database identity verified: [Portal-Gerencial]" -ForegroundColor Green

    # -------------------------------------------------------------------------
    # 4. Edition-aware Backup Clause (Express edition cannot use WITH COMPRESSION)
    # -------------------------------------------------------------------------
    $editionCmd = $conn.CreateCommand()
    $editionCmd.CommandText = "SELECT CAST(SERVERPROPERTY('Edition') AS NVARCHAR(200)) AS Edition"
    $edition = [string]$editionCmd.ExecuteScalar()
    Write-Host "Detected SQL Server Edition: $edition" -ForegroundColor Green

    $backupLabel = "Dev Clone Export $ReleaseVersion (run $RunId attempt $RunAttempt)".Trim()
    if ($edition -like "*Express*") {
        Write-Host "SQL Server Express detected. Backup compression is DISABLED." -ForegroundColor Yellow
        $backupClause = "WITH FORMAT, NAME = N'$backupLabel'"
    } else {
        Write-Host "Backup compression is ENABLED." -ForegroundColor Green
        $backupClause = "WITH FORMAT, COMPRESSION, NAME = N'$backupLabel'"
    }

    # -------------------------------------------------------------------------
    # 5. Create the Export Backup (unique, collision-proof filename)
    # -------------------------------------------------------------------------
    if (-not (Test-Path -LiteralPath $resolvedBackupDir)) {
        New-Item -ItemType Directory -Path $resolvedBackupDir -Force | Out-Null
    }

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $backupFileName = "Portal-Gerencial_dev-export_run-${RunId}_attempt-${RunAttempt}_${timestamp}.bak"
    $backupFile = Join-Path $resolvedBackupDir $backupFileName

    Write-Host "Gerando backup de exportacao em: $backupFile..." -ForegroundColor Yellow
    $backupCmd = $conn.CreateCommand()
    $backupCmd.CommandTimeout = 300
    $backupCmd.CommandText = "BACKUP DATABASE [$connectedDbName] TO DISK = N'$backupFile' $backupClause"
    $backupCmd.ExecuteNonQuery() | Out-Null

    if (-not (Test-Path -LiteralPath $backupFile)) {
        throw "EXPORT FAILED: BACKUP DATABASE reported success but the backup file was not found at '$backupFile'."
    }

    $backupSizeMb = [math]::Round((Get-Item -LiteralPath $backupFile).Length / 1MB, 2)
    Write-Host "OK - backup de exportacao criado com sucesso." -ForegroundColor Green
    Write-Host "Arquivo: $backupFile" -ForegroundColor Green
    Write-Host "Tamanho: $backupSizeMb MB" -ForegroundColor Green

    # -------------------------------------------------------------------------
    # 6. Compute and Write the SHA-256 Checksum File
    # -------------------------------------------------------------------------
    $computedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $backupFile).Hash.ToLowerInvariant()
    $checksumFile = "$backupFile.sha256"
    $checksumContent = "$computedHash  $backupFileName`n"
    [System.IO.File]::WriteAllText($checksumFile, $checksumContent, (New-Object System.Text.ASCIIEncoding))

    Write-Host "SHA-256: $computedHash" -ForegroundColor Green
    Write-Host "Checksum file: $checksumFile" -ForegroundColor Green

    # -------------------------------------------------------------------------
    # 7. Export Output for the Calling Workflow (artifact upload + cleanup steps)
    # -------------------------------------------------------------------------
    if ($env:GITHUB_OUTPUT) {
        "backup_file_path=$backupFile" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "backup_file_name=$backupFileName" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "checksum_file_path=$checksumFile" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "backup_sha256=$computedHash" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }

    # -------------------------------------------------------------------------
    # 8. Step Summary (safe metadata only - never the connection string)
    # -------------------------------------------------------------------------
    if ($env:GITHUB_STEP_SUMMARY) {
        $summaryLines = @(
            "## Export PROD Data for Dev - Backup Created"
            ""
            "| Field | Value |"
            "|---|---|"
            "| Release version | $ReleaseVersion |"
            "| Run ID | $RunId |"
            "| Run attempt | $RunAttempt |"
            "| Backup file name | $backupFileName |"
            "| Backup size | $backupSizeMb MB |"
            "| SHA-256 | $computedHash |"
            ""
        )
        $summaryLines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }

    Write-Host ""
    Write-Host "Processo concluido com sucesso! Nenhum dado de Portal-Gerencial foi modificado (somente BACKUP DATABASE)." -ForegroundColor Green
}
catch {
    Write-Host "[ERROR] Export failed: $_" -ForegroundColor Red

    # Defensive cleanup: if the backup and/or checksum file were already created
    # on disk but we failed before the GITHUB_OUTPUT lines above were written,
    # the workflow-level cleanup step (which only knows steps.export.outputs.*)
    # would have no path to act on. Clean up here so a failed export never
    # leaves a Production backup behind either.
    if ($backupFile -and (Test-Path -LiteralPath $backupFile)) {
        Write-Host "Defensive cleanup: removing partially-created backup file..." -ForegroundColor Yellow
        Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
    }
    if ($checksumFile -and (Test-Path -LiteralPath $checksumFile)) {
        Write-Host "Defensive cleanup: removing partially-created checksum file..." -ForegroundColor Yellow
        Remove-Item -LiteralPath $checksumFile -Force -ErrorAction SilentlyContinue
    }

    throw
}
finally {
    if ($conn.State -eq [System.Data.ConnectionState]::Open) {
        $conn.Close()
    }
}
