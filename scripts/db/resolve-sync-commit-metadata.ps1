param(
    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$EventSha,

    [Parameter(Mandatory = $false)]
    [string]$RepoRoot = (Get-Location).Path
)

# =============================================================================
# Sync PROD Data to TEST - Commit Metadata Resolution
#
# Resolves the authoritative commit SHA for traceability WITHOUT requiring
# Git to be installed on the runner. actions/checkout@v4 falls back to a
# REST API archive download when Git is not available in PATH; in that case
# the extracted working directory has no .git metadata at all. GITHUB_SHA
# (the event SHA supplied by GitHub Actions itself) is always authoritative
# and becomes the Resolved SHA. A local `git rev-parse HEAD` is attempted
# only opportunistically, purely as an extra integrity cross-check when both
# git and .git metadata are available - it is never required, and this
# script never invokes git unconditionally.
# =============================================================================

$ErrorActionPreference = "Stop"
$ShaPattern = '^[0-9a-fA-F]{40}$'

# ─────────────────────────────────────────────────────────────────────────────
# 1-3. Read and validate GITHUB_SHA; it is the resolved SHA unless a local
#      Git cross-check below proves it wrong.
# ─────────────────────────────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($EventSha) -or $EventSha -notmatch $ShaPattern) {
    throw "TRACEABILITY ERROR: GITHUB_SHA ('$EventSha') is missing or is not a valid 40-character hexadecimal commit SHA."
}

$resolvedSha = $EventSha
$localGitSha = $null

# ─────────────────────────────────────────────────────────────────────────────
# 4-6. Detect Git optionally. Only cross-check when both the git command and
#      .git metadata are present (i.e. checkout did NOT fall back to the REST
#      archive). Never fail merely because Git is unavailable.
# ─────────────────────────────────────────────────────────────────────────────
$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$gitDirPath = Join-Path $RepoRoot ".git"

if ($gitCommand -and (Test-Path $gitDirPath)) {
    $candidate = (& git -C $RepoRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $candidate) {
        $candidate = $candidate.Trim()
        if ($candidate -match $ShaPattern) {
            $localGitSha = $candidate
            if ($localGitSha -ne $EventSha) {
                throw "TRACEABILITY ERROR: local git HEAD ($localGitSha) differs from GITHUB_SHA ($EventSha)."
            }
        } else {
            Write-Host "[WARN] 'git rev-parse HEAD' returned an unexpected value; ignoring local Git result." -ForegroundColor Yellow
        }
    } else {
        Write-Host "[WARN] 'git rev-parse HEAD' failed (exit $LASTEXITCODE); ignoring local Git result." -ForegroundColor Yellow
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 3 (safe output). Distinguish Event SHA / Local Git SHA / Resolved SHA.
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "Event SHA:     $EventSha"
if ($localGitSha) {
    Write-Host "Local Git SHA: $localGitSha"
} else {
    Write-Host "Local Git SHA: unavailable - Git not in PATH or no .git metadata (REST checkout fallback)."
    Write-Host "Git metadata unavailable; using GITHUB_SHA supplied by GitHub Actions." -ForegroundColor DarkGray
}
Write-Host "Resolved SHA:  $resolvedSha"

# ─────────────────────────────────────────────────────────────────────────────
# 7. Export resolved SHA for the next step (only meaningful inside a real
#    GitHub Actions run, where GITHUB_OUTPUT points at a writable file).
# ─────────────────────────────────────────────────────────────────────────────
if ($env:GITHUB_OUTPUT) {
    "resolved_sha=$resolvedSha" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

return $resolvedSha
