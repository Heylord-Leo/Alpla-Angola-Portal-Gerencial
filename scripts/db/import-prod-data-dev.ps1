param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFilePath,

    [Parameter(Mandatory = $false)]
    [string]$ChecksumFilePath,

    [Parameter(Mandatory = $false)]
    [string]$ExpectedSha256,

    [Parameter(Mandatory = $false)]
    [ValidateSet('Portal-Gerencial-Dev-ProdClone')]
    [string]$TargetDbName = 'Portal-Gerencial-Dev-ProdClone',

    [Parameter(Mandatory = $false)]
    [string]$SqlInstance = "(localdb)\MSSQLLocalDB",

    [Parameter(Mandatory = $false)]
    [ValidateSet('FullClone', 'Incremental', 'None')]
    [string]$AttachmentMode = 'None',

    [Parameter(Mandatory = $false)]
    [string]$AttachmentSourcePath,

    [Parameter(Mandatory = $false)]
    [string]$AttachmentTargetPath,

    [Parameter(Mandatory = $false)]
    [string]$NeutralizationScriptPath,

    [Parameter(Mandatory = $false)]
    [string]$LocalBackupDir = (Join-Path $env:USERPROFILE "AlplaPortalDevCloneBackups"),

    [Parameter(Mandatory = $false)]
    [switch]$Apply,

    [Parameter(Mandatory = $false)]
    [string]$Confirmation
)

# =============================================================================
# Alpla Angola - Portal Gerencial
# Import PROD -> local Development clone (Portal-Gerencial-Dev-ProdClone)
# =============================================================================
#
# Runs LOCALLY on a developer machine (NOT on the self-hosted GitHub Actions
# runner) against a local LocalDB instance. Takes a Production backup file
# already downloaded from the "Export PROD Data for Dev" workflow artifact
# (which now always ships a paired <file>.bak.sha256 checksum) and restores it
# into a brand-new, isolated database, then neutralizes every outbound
# integration/email/secret path before allowing the application to be pointed
# at it.
#
# Safety model (same PreviewOnly/-Apply pattern used across this repository's
# other data-repair scripts):
#   - Default (no -Apply): prints the full plan, the computed local SHA-256,
#     and whether it matches the supplied checksum source, but performs every
#     other action read-only. No SQL connection is opened, no file is written.
#   - -Apply requires -Confirmation "APPLY-PROD-CLONE-IMPORT-DEV" exactly,
#     AND requires either -ChecksumFilePath or -ExpectedSha256 (or both) to be
#     supplied, AND requires the computed SHA-256 of -BackupFilePath to match.
#     Prefer -ChecksumFilePath (the paired .bak.sha256 from the export
#     artifact) in normal use; -ExpectedSha256 is available for cases where
#     only the raw hash was communicated. If both are supplied, the computed
#     hash must match both. The checksum comparison is pure local file I/O
#     (Get-FileHash) and happens BEFORE any SQL connection is opened - a
#     mismatch aborts before any database operation.
#   - Hard-coded forbidden target database names: Portal-Gerencial,
#     Portal-Gerencial-Test, AlplaPortalV1. This is enforced even though
#     -TargetDbName is already restricted by ValidateSet to a single value,
#     as defense in depth and to make the rule self-documenting.
#   - AlplaPortalV1 (the LocalDB database already used by
#     appsettings.Development.json) is never touched, renamed, or dropped.
#   - This script fails closed: if dev-safety-neutralization.sql throws, or
#     the post-restore PowerShell-side verification does not pass, the
#     $ErrorActionPreference = "Stop" below means the script terminates
#     BEFORE reaching the final success message and BEFORE printing the
#     ConnectionStrings__DefaultConnection instructions.
#
# Attachment modes (-AttachmentMode FullClone|Incremental|None):
#   Because attachments are GUID-named and immutable once created, FullClone
#   (intended for the very first run) and Incremental (intended for later,
#   top-up runs) currently execute the IDENTICAL, maximally-safe robocopy
#   invocation: /E /XC /XN /XO - recurse, and skip any file that already
#   exists locally regardless of its timestamp. Neither mode ever overwrites
#   or deletes a local file. The distinction between the two option names is
#   therefore operational/documentary (which run you are performing), not a
#   different copy algorithm - there is no timestamp-checkpoint logic here.
# =============================================================================

$ErrorActionPreference = "Stop"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Import PROD Data to local Development clone" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# -----------------------------------------------------------------------------
# 0. Resolve repository-relative defaults (script lives in scripts\db)
# -----------------------------------------------------------------------------
$repoRoot = Split-Path -Path (Split-Path -Path $PSScriptRoot -Parent) -Parent

if ([string]::IsNullOrWhiteSpace($NeutralizationScriptPath)) {
    $NeutralizationScriptPath = Join-Path $PSScriptRoot "dev-safety-neutralization.sql"
}

if ([string]::IsNullOrWhiteSpace($AttachmentTargetPath)) {
    $AttachmentTargetPath = Join-Path $repoRoot "data\attachments"
}

function Test-PathIsUnderOrEqual {
    param([string]$Path, [string]$Root)
    $normalizedPath = $Path.TrimEnd('\')
    $normalizedRoot = $Root.TrimEnd('\')
    return ($normalizedPath -ieq $normalizedRoot) -or ($normalizedPath -like "$normalizedRoot\*")
}

function Get-Sha256FromChecksumFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "CHECKSUM FILE NOT FOUND: '$Path'."
    }
    $rawContent = (Get-Content -LiteralPath $Path -Raw -ErrorAction Stop).Trim()
    if ([string]::IsNullOrWhiteSpace($rawContent)) {
        throw "CHECKSUM FILE EMPTY: '$Path'."
    }
    # Conventional format: "<64-hex-char SHA-256>  <filename>" (sha256sum-style).
    $match = [regex]::Match($rawContent, '^([0-9a-fA-F]{64})\s')
    if ($match.Success) {
        return $match.Groups[1].Value.ToLowerInvariant()
    }
    # Also accept a file containing ONLY the hash, with no filename suffix.
    if ($rawContent -match '^[0-9a-fA-F]{64}$') {
        return $rawContent.ToLowerInvariant()
    }
    throw "CHECKSUM FILE FORMAT INVALID: '$Path' does not contain a recognizable 64-character SHA-256 hex value."
}

# -----------------------------------------------------------------------------
# 1. Forbidden Database Name Guard (checked before ANY SQL connection)
# -----------------------------------------------------------------------------
$forbiddenDatabaseNames = @('Portal-Gerencial', 'Portal-Gerencial-Test', 'AlplaPortalV1')
if ($forbiddenDatabaseNames -contains $TargetDbName) {
    throw "SAFETY CHECK FAILED: '$TargetDbName' is a forbidden target database name (forbidden list: $($forbiddenDatabaseNames -join ', ')). Aborting before any SQL connection was opened."
}

Write-Host "Target database:              $TargetDbName" -ForegroundColor Green
Write-Host "SQL instance:                  $SqlInstance" -ForegroundColor Green
Write-Host "Backup file:                   $BackupFilePath" -ForegroundColor Green
Write-Host "Neutralization script:        $NeutralizationScriptPath" -ForegroundColor Green
Write-Host "Attachment mode:               $AttachmentMode" -ForegroundColor Green
if ($AttachmentMode -ne 'None') {
    Write-Host "Attachment source path:       $AttachmentSourcePath" -ForegroundColor Green
    Write-Host "Attachment target path:       $AttachmentTargetPath" -ForegroundColor Green
}
Write-Host "Local pre-replace backup dir: $LocalBackupDir" -ForegroundColor Green
Write-Host ""

# -----------------------------------------------------------------------------
# 2. Input Validation (read-only; always runs, Preview or Apply)
# -----------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $BackupFilePath)) {
    throw "VALIDATION FAILED: backup file not found at '$BackupFilePath'."
}
if ($BackupFilePath -notmatch '\.bak$') {
    throw "VALIDATION FAILED: backup file must have a .bak extension. Got: '$BackupFilePath'."
}

if (-not (Test-Path -LiteralPath $NeutralizationScriptPath)) {
    throw "VALIDATION FAILED: neutralization script not found at '$NeutralizationScriptPath'."
}

if ($AttachmentMode -ne 'None') {
    if ([string]::IsNullOrWhiteSpace($AttachmentSourcePath)) {
        throw "VALIDATION FAILED: -AttachmentSourcePath is required when -AttachmentMode is '$AttachmentMode'."
    }
    if (-not (Test-Path -LiteralPath $AttachmentSourcePath)) {
        throw "VALIDATION FAILED: attachment source path not found: '$AttachmentSourcePath'."
    }

    $resolvedAttachmentSource = [System.IO.Path]::GetFullPath($AttachmentSourcePath)
    $resolvedAttachmentTarget = [System.IO.Path]::GetFullPath($AttachmentTargetPath)

    $attachmentDriveRoot = [System.IO.Path]::GetPathRoot($resolvedAttachmentTarget)
    if ($resolvedAttachmentTarget.TrimEnd('\') -ieq $attachmentDriveRoot.TrimEnd('\')) {
        throw "VALIDATION FAILED: attachment target path resolves to a drive root ('$resolvedAttachmentTarget')."
    }

    # Windows / Program Files: reject the root AND anything nested under it.
    $forbiddenAttachmentRoots = @($env:WINDIR, "C:\Program Files", "C:\Program Files (x86)") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($forbiddenRoot in $forbiddenAttachmentRoots) {
        if (Test-PathIsUnderOrEqual -Path $resolvedAttachmentTarget -Root $forbiddenRoot) {
            throw "VALIDATION FAILED: attachment target path ('$resolvedAttachmentTarget') resolves under a protected directory ('$forbiddenRoot')."
        }
    }

    # Repository root: reject only EXACT equality. Nested paths are expected and
    # required, since the default target ($repoRoot\data\attachments) is itself
    # nested under the repository root.
    if ($resolvedAttachmentTarget.TrimEnd('\') -ieq $repoRoot.TrimEnd('\')) {
        throw "VALIDATION FAILED: attachment target path must not be the repository root itself ('$repoRoot')."
    }

    # Require the target to end in a normalized 'data\attachments' path rather
    # than merely ending in the word 'attachments' (which any path could match).
    if ($resolvedAttachmentTarget.TrimEnd('\') -notmatch '(?i)\\data\\attachments$') {
        throw "VALIDATION FAILED: attachment target path must end in '\data\attachments'. Got: '$resolvedAttachmentTarget'."
    }

    if ($resolvedAttachmentSource.TrimEnd('\') -ieq $resolvedAttachmentTarget.TrimEnd('\')) {
        throw "VALIDATION FAILED: attachment source and target must not resolve to the same directory ('$resolvedAttachmentTarget')."
    }

    # Use the normalized, validated forms for every later step.
    $AttachmentSourcePath = $resolvedAttachmentSource
    $AttachmentTargetPath = $resolvedAttachmentTarget
}

# -----------------------------------------------------------------------------
# 2.5 Checksum Resolution (always computed; read-only; safe in Preview mode)
# -----------------------------------------------------------------------------
$computedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $BackupFilePath).Hash.ToLowerInvariant()

$expectedHashes = @()
$checksumSourceDescription = "none supplied"

if (-not [string]::IsNullOrWhiteSpace($ChecksumFilePath)) {
    $hashFromFile = Get-Sha256FromChecksumFile -Path $ChecksumFilePath
    $expectedHashes += $hashFromFile
    $checksumSourceDescription = "checksum file '$ChecksumFilePath'"
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
    if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "VALIDATION FAILED: -ExpectedSha256 must be a 64-character hexadecimal SHA-256 value. Got: '$ExpectedSha256'."
    }
    $expectedHashes += $ExpectedSha256.ToLowerInvariant()
    if ($checksumSourceDescription -eq "none supplied") {
        $checksumSourceDescription = "-ExpectedSha256 parameter"
    } else {
        $checksumSourceDescription = "$checksumSourceDescription AND -ExpectedSha256 parameter (both must match)"
    }
}

$checksumVerified = $null
if ($expectedHashes.Count -gt 0) {
    $checksumVerified = $true
    foreach ($expectedHash in $expectedHashes) {
        if ($expectedHash -ne $computedHash) { $checksumVerified = $false }
    }
}

Write-Host ""
Write-Host "Backup path:            $BackupFilePath" -ForegroundColor Cyan
Write-Host "Computed SHA-256:       $computedHash" -ForegroundColor Cyan
Write-Host "Checksum source:        $checksumSourceDescription" -ForegroundColor Cyan
if ($null -eq $checksumVerified) {
    Write-Host "Checksum verification:  NOT PERFORMED (no -ChecksumFilePath or -ExpectedSha256 supplied)" -ForegroundColor Yellow
} elseif ($checksumVerified) {
    Write-Host "Checksum verification:  PASSED" -ForegroundColor Green
} else {
    Write-Host "Checksum verification:  FAILED" -ForegroundColor Red
}
Write-Host ""

# -----------------------------------------------------------------------------
# 3. Preview / Apply Gate
# -----------------------------------------------------------------------------
$RequiredConfirmation = "APPLY-PROD-CLONE-IMPORT-DEV"

if (-not $Apply) {
    Write-Host "=============================================" -ForegroundColor Yellow
    Write-Host "  PREVIEW MODE (no -Apply supplied)" -ForegroundColor Yellow
    Write-Host "=============================================" -ForegroundColor Yellow
    Write-Host "The following actions WOULD be performed with -Apply -Confirmation `"$RequiredConfirmation`" (and a passing checksum):"
    Write-Host "  1. Connect to LocalDB instance '$SqlInstance' (master database)."
    Write-Host "  2. Read RESTORE FILELISTONLY from '$BackupFilePath' to discover real logical file names."
    Write-Host "  3. Discover LocalDB default data/log directories via SERVERPROPERTY."
    Write-Host "  4. If '$TargetDbName' already exists: back it up to '$LocalBackupDir', then set SINGLE_USER and drop connections."
    Write-Host "  5. RESTORE DATABASE [$TargetDbName] WITH REPLACE, MOVE (data + log) to the discovered LocalDB paths."
    Write-Host "  6. SET MULTI_USER on '$TargetDbName'."
    Write-Host "  7. Execute dev-safety-neutralization.sql against '$TargetDbName' (fails closed on any verification failure)."
    Write-Host "  8. Verify '$TargetDbName' reports state_desc = ONLINE."
    if ($AttachmentMode -ne 'None') {
        Write-Host "  9. Robocopy attachments from '$AttachmentSourcePath' to '$AttachmentTargetPath' (mode: $AttachmentMode - FullClone and Incremental currently run the identical additive copy: /E /XC /XN /XO, no /MIR, no /PURGE, never overwrites or deletes)."
    } else {
        Write-Host "  9. Attachment sync skipped (-AttachmentMode None)."
    }
    Write-Host " 10. Print the ConnectionStrings__DefaultConnection instructions for local Development."
    Write-Host ""
    if ($expectedHashes.Count -eq 0) {
        Write-Host "NOTE: -Apply will be REFUSED until either -ChecksumFilePath or -ExpectedSha256 is supplied and matches." -ForegroundColor Yellow
    } elseif (-not $checksumVerified) {
        Write-Host "NOTE: -Apply will be REFUSED - the checksum verification above FAILED." -ForegroundColor Red
    } else {
        Write-Host "Checksum verification already PASSED against: $checksumSourceDescription." -ForegroundColor Green
    }
    Write-Host "No SQL connection has been opened. No files have been modified. Re-run with -Apply -Confirmation `"$RequiredConfirmation`" to execute." -ForegroundColor Yellow
    return
}

if ($Confirmation -ne $RequiredConfirmation) {
    throw "CONFIRMATION FAILED: -Confirmation must be exactly `"$RequiredConfirmation`" when -Apply is supplied."
}

# ---- Checksum gate: pure file I/O already completed above; no SQL connection
#      has been opened anywhere in this script up to this point. ----
if ($expectedHashes.Count -eq 0) {
    throw "CHECKSUM REQUIRED: -Apply requires either -ChecksumFilePath or -ExpectedSha256 (or both) to be supplied. Aborting before any SQL connection or database operation."
}
if (-not $checksumVerified) {
    throw "CHECKSUM VERIFICATION FAILED: computed SHA-256 ($computedHash) does not match the expected value(s) from $checksumSourceDescription. Aborting before any SQL connection or database operation."
}
Write-Host "OK - checksum verification passed ($checksumSourceDescription)." -ForegroundColor Green

Write-Host "=============================================" -ForegroundColor Red
Write-Host "  APPLY MODE - executing against local LocalDB" -ForegroundColor Red
Write-Host "=============================================" -ForegroundColor Red

# -----------------------------------------------------------------------------
# 4. Connect to LocalDB (master) and validate reachability
# -----------------------------------------------------------------------------
$masterConnStr = "Server=$SqlInstance;Database=master;Trusted_Connection=True;TrustServerCertificate=True"

$connMaster = New-Object System.Data.SqlClient.SqlConnection($masterConnStr)
try {
    $connMaster.Open()
} catch {
    throw "SQL CONNECTION FAILED: could not connect to LocalDB instance '$SqlInstance'. Is 'sqllocaldb start MSSQLLocalDB' required? Underlying error: $_"
}

$verCmd = $connMaster.CreateCommand()
$verCmd.CommandText = "SELECT @@VERSION AS Version, SERVERPROPERTY('InstanceDefaultDataPath') AS DataPath, SERVERPROPERTY('InstanceDefaultLogPath') AS LogPath"
$verReader = $verCmd.ExecuteReader()
$dataPath = $null
$logPath = $null
if ($verReader.Read()) {
    Write-Host ("Connected to: {0}" -f ($verReader["Version"] -split "`n")[0]) -ForegroundColor Green
    $dataPath = [string]$verReader["DataPath"]
    $logPath = [string]$verReader["LogPath"]
}
$verReader.Close()

if ([string]::IsNullOrWhiteSpace($dataPath) -or [string]::IsNullOrWhiteSpace($logPath)) {
    $connMaster.Close()
    throw "VALIDATION FAILED: could not resolve InstanceDefaultDataPath/InstanceDefaultLogPath from LocalDB instance '$SqlInstance'."
}

Write-Host "Discovered LocalDB data path:  $dataPath" -ForegroundColor Green
Write-Host "Discovered LocalDB log path:   $logPath" -ForegroundColor Green

if (-not (Test-Path -LiteralPath $dataPath)) { New-Item -ItemType Directory -Path $dataPath -Force | Out-Null }
if (-not (Test-Path -LiteralPath $logPath)) { New-Item -ItemType Directory -Path $logPath -Force | Out-Null }

# -----------------------------------------------------------------------------
# 5. RESTORE FILELISTONLY - discover the REAL logical file names in the backup
#    (never trust hardcoded logical names; always read them from the file)
# -----------------------------------------------------------------------------
$fileListCmd = $connMaster.CreateCommand()
$fileListCmd.CommandTimeout = 300
$fileListCmd.CommandText = "RESTORE FILELISTONLY FROM DISK = N'$BackupFilePath'"
$fileListReader = $fileListCmd.ExecuteReader()

$logicalData = $null
$logicalLog = $null
while ($fileListReader.Read()) {
    $type = [string]$fileListReader["Type"]
    $logicalName = [string]$fileListReader["LogicalName"]
    if ($type -eq 'D' -and -not $logicalData) { $logicalData = $logicalName }
    if ($type -eq 'L' -and -not $logicalLog) { $logicalLog = $logicalName }
}
$fileListReader.Close()

if ([string]::IsNullOrWhiteSpace($logicalData) -or [string]::IsNullOrWhiteSpace($logicalLog)) {
    $connMaster.Close()
    throw "VALIDATION FAILED: RESTORE FILELISTONLY did not return both a data (D) and log (L) logical file for '$BackupFilePath'."
}

Write-Host "Discovered logical data file:  $logicalData" -ForegroundColor Green
Write-Host "Discovered logical log file:   $logicalLog" -ForegroundColor Green

$expectedLogicalData = 'PortalGerencial'
$expectedLogicalLog = 'Portal-Gerencial_log'
if ($logicalData -ne $expectedLogicalData -or $logicalLog -ne $expectedLogicalLog) {
    Write-Host "[WARN] Discovered logical names differ from the previously confirmed Production names ('$expectedLogicalData' / '$expectedLogicalLog'). Proceeding with the discovered (real) names, since FILELISTONLY is always authoritative." -ForegroundColor Yellow
}

$physicalDataFile = Join-Path $dataPath "$TargetDbName.mdf"
$physicalLogFile = Join-Path $logPath "${TargetDbName}_log.ldf"

# -----------------------------------------------------------------------------
# 6. If target database already exists, back it up before WITH REPLACE
# -----------------------------------------------------------------------------
$existsCmd = $connMaster.CreateCommand()
$existsCmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @dbName"
$existsCmd.Parameters.AddWithValue("@dbName", $TargetDbName) | Out-Null
$targetExists = ([int]$existsCmd.ExecuteScalar()) -gt 0

if ($targetExists) {
    if (-not (Test-Path -LiteralPath $LocalBackupDir)) { New-Item -ItemType Directory -Path $LocalBackupDir -Force | Out-Null }

    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $preReplaceBackupFile = Join-Path $LocalBackupDir "${TargetDbName}_${timestamp}_pre-replace.bak"

    Write-Host "Target database already exists. Backing it up to: $preReplaceBackupFile" -ForegroundColor Yellow
    $editionCmd = $connMaster.CreateCommand()
    $editionCmd.CommandText = "SELECT CAST(SERVERPROPERTY('Edition') AS NVARCHAR(200))"
    $edition = [string]$editionCmd.ExecuteScalar()
    $backupClause = if ($edition -like "*Express*") { "WITH FORMAT, NAME = N'Pre-Replace Backup'" } else { "WITH FORMAT, COMPRESSION, NAME = N'Pre-Replace Backup'" }

    $preBackupCmd = $connMaster.CreateCommand()
    $preBackupCmd.CommandTimeout = 300
    $preBackupCmd.CommandText = "BACKUP DATABASE [$TargetDbName] TO DISK = N'$preReplaceBackupFile' $backupClause"
    $preBackupCmd.ExecuteNonQuery() | Out-Null
    Write-Host "OK - pre-replace backup created." -ForegroundColor Green

    $singleUserCmd = $connMaster.CreateCommand()
    $singleUserCmd.CommandText = "ALTER DATABASE [$TargetDbName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
    $singleUserCmd.ExecuteNonQuery() | Out-Null
} else {
    Write-Host "Target database does not exist yet - this will be a first-time clone." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 7. RESTORE DATABASE WITH REPLACE, MOVE
# -----------------------------------------------------------------------------
Write-Host "Restoring '$BackupFilePath' into [$TargetDbName]..." -ForegroundColor Yellow
$restoreCmd = $connMaster.CreateCommand()
$restoreCmd.CommandTimeout = 600
$restoreCmd.CommandText = @"
RESTORE DATABASE [$TargetDbName] FROM DISK = N'$BackupFilePath'
WITH REPLACE,
MOVE N'$logicalData' TO N'$physicalDataFile',
MOVE N'$logicalLog' TO N'$physicalLogFile';
"@
$restoreCmd.ExecuteNonQuery() | Out-Null

$multiUserCmd = $connMaster.CreateCommand()
$multiUserCmd.CommandText = "ALTER DATABASE [$TargetDbName] SET MULTI_USER;"
$multiUserCmd.ExecuteNonQuery() | Out-Null
Write-Host "OK - database restored: [$TargetDbName]" -ForegroundColor Green

$connMaster.Close()

# -----------------------------------------------------------------------------
# 8. Development Safety Neutralization (fails closed via THROW inside the SQL)
# -----------------------------------------------------------------------------
Write-Host "Running dev-safety-neutralization.sql against [$TargetDbName]..." -ForegroundColor Yellow

$targetConnStr = "Server=$SqlInstance;Database=$TargetDbName;Trusted_Connection=True;TrustServerCertificate=True"
$connTarget = New-Object System.Data.SqlClient.SqlConnection($targetConnStr)
$connTarget.Open()

$neutralizationSql = Get-Content -LiteralPath $NeutralizationScriptPath -Raw
$neutralizeCmd = $connTarget.CreateCommand()
$neutralizeCmd.CommandTimeout = 300
$neutralizeCmd.CommandText = $neutralizationSql

try {
    $infoMessageHandler = {
        param($sender, $e)
        Write-Host ("  [SQL] {0}" -f $e.Message) -ForegroundColor DarkGray
    }
    $connTarget.add_InfoMessage($infoMessageHandler)
    $neutralizeCmd.ExecuteNonQuery() | Out-Null
} catch {
    $connTarget.Close()
    throw "DEVELOPMENT SAFETY NEUTRALIZATION FAILED: $_. The import is INCOMPLETE and UNSAFE. Do not point the application at [$TargetDbName]. Fix the underlying issue and re-run this script with -Apply."
}

Write-Host "OK - development safety neutralization verification PASSED." -ForegroundColor Green

# -----------------------------------------------------------------------------
# 9. PowerShell-side post-restore verification (defense in depth alongside the
#    SQL script's own fail-closed checks)
# -----------------------------------------------------------------------------
$stateCmd = $connTarget.CreateCommand()
$stateCmd.CommandText = "SELECT DB_NAME() AS CurrentDb, (SELECT state_desc FROM sys.databases WHERE name = DB_NAME()) AS StateDesc"
$stateReader = $stateCmd.ExecuteReader()
$currentDb = $null
$stateDesc = $null
if ($stateReader.Read()) {
    $currentDb = [string]$stateReader["CurrentDb"]
    $stateDesc = [string]$stateReader["StateDesc"]
}
$stateReader.Close()
$connTarget.Close()

if ($currentDb -ne $TargetDbName) {
    throw "VERIFICATION FAILED: connected database name '$currentDb' does not match expected target '$TargetDbName'."
}
if ($stateDesc -ne 'ONLINE') {
    throw "VERIFICATION FAILED: database '$TargetDbName' reports state_desc = '$stateDesc' (expected ONLINE)."
}

Write-Host ("OK - verified DB_NAME() = [{0}], state_desc = {1}" -f $currentDb, $stateDesc) -ForegroundColor Green

# -----------------------------------------------------------------------------
# 10. Attachment Sync (additive-only; never /MIR, never /PURGE, never deletes)
#     FullClone and Incremental currently run the identical robocopy
#     invocation - see the header comment for why this is intentional.
# -----------------------------------------------------------------------------
if ($AttachmentMode -ne 'None') {
    if (-not (Test-Path -LiteralPath $AttachmentTargetPath)) {
        New-Item -ItemType Directory -Path $AttachmentTargetPath -Force | Out-Null
    }

    Write-Host ("Copying attachments ({0} mode) from '{1}' to '{2}' - additive only, no overwrite, no delete..." -f $AttachmentMode, $AttachmentSourcePath, $AttachmentTargetPath) -ForegroundColor Yellow
    $robocopyParams = @($AttachmentSourcePath, $AttachmentTargetPath, "/E", "/XC", "/XN", "/XO", "/R:2", "/W:2", "/NFL", "/NDL", "/NJH", "/NJS")
    & robocopy.exe @robocopyParams
    if ($LASTEXITCODE -ge 8) {
        throw "ATTACHMENT SYNC FAILED: robocopy exit code $LASTEXITCODE while copying from '$AttachmentSourcePath' to '$AttachmentTargetPath'."
    }
    Write-Host "OK - attachment sync completed (existing local files were never overwritten or deleted)." -ForegroundColor Green
    $global:LASTEXITCODE = 0
} else {
    Write-Host "Attachment sync skipped (-AttachmentMode None)." -ForegroundColor DarkGray
}

# -----------------------------------------------------------------------------
# 11. Success - only reached if every step above completed without throwing
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "=============================================" -ForegroundColor Green
Write-Host "  Import completed successfully" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""
Write-Host "To start the local API against this clone, set (in the SAME shell you run 'dotnet run' from):" -ForegroundColor Cyan
Write-Host ""
Write-Host '$env:ConnectionStrings__DefaultConnection = "Server=(localdb)\MSSQLLocalDB;Database=Portal-Gerencial-Dev-ProdClone;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"' -ForegroundColor White
Write-Host ""
Write-Host "This does not modify appsettings.Development.json and does not affect AlplaPortalV1." -ForegroundColor DarkGray
Write-Host "Delete the downloaded .bak and .bak.sha256 files now that the import has succeeded." -ForegroundColor DarkGray
