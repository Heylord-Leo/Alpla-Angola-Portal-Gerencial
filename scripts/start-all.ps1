# ═══════════════════════════════════════════════════════════════════════════════
# scripts/start-all.ps1 — DEPRECATED
#
# This script is deprecated. Use the canonical startup script instead:
#   execution/restart_services.ps1
#
# This wrapper delegates entirely to the canonical script.
# It does NOT independently build, stop, or start services.
# ═══════════════════════════════════════════════════════════════════════════════
Write-Warning "scripts/start-all.ps1 is DEPRECATED. Delegating to execution/restart_services.ps1."

$canonical = Join-Path (Resolve-Path "$PSScriptRoot/..") "execution/restart_services.ps1"
if (-not (Test-Path $canonical)) {
    Write-Error "[FATAL] Canonical script not found: $canonical" -ErrorAction Stop
}

& $canonical
exit $LASTEXITCODE
