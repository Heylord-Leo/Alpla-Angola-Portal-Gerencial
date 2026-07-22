# ===========================================================================
# execution/restart_services.ps1 - Canonical Alpla Portal local startup
#
# Safety-validated: verifies actual DB_NAME() against the canonical Development
# clone before starting the backend. This is the ONLY supported startup script.
#
# Canonical database: Portal-Gerencial-Dev-ProdClone
# SQL instance:       (localdb)\MSSQLLocalDB
# ===========================================================================
$ErrorActionPreference = 'Stop'

# -- Constants ---------------------------------------------------------------
$CanonicalDb   = 'Portal-Gerencial-Dev-ProdClone'
$SqlInstance   = '(localdb)\MSSQLLocalDB'
$ForbiddenDbs  = @('AlplaPortalV1', 'Portal-Gerencial', 'Portal-Gerencial-Test')
$CloneConnStr  = "Server=$SqlInstance;Database=$CanonicalDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
$BackendDir    = 'c:\dev\alpla-portal\src\backend\AlplaPortal.Api'
$FrontendDir   = 'c:\dev\alpla-portal\src\frontend'
$BackendPort   = 5000
$FrontendPort  = 5173

# -- Helper: Stop process by port -------------------------------------------
function Stop-ProcessByPort($port) {
    $procId = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue |
              Select-Object -ExpandProperty OwningProcess -First 1
    if ($procId) {
        Write-Host "[STOP] Killing process $procId on port $port..."
        Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }
}

# -- Step 1: Stop existing services -----------------------------------------
Write-Host "`n[1/6] Stopping existing services..."
Stop-ProcessByPort $BackendPort
Stop-ProcessByPort $FrontendPort

# -- Step 2: Ensure LocalDB instance is running ------------------------------
Write-Host "[2/6] Ensuring LocalDB instance is running..."
$info = sqllocaldb info MSSQLLocalDB 2>&1
if ($info -match 'State:\s+Stopped') {
    Write-Host "       Starting MSSQLLocalDB..."
    sqllocaldb start MSSQLLocalDB | Out-Null
    Start-Sleep -Seconds 2
}

# -- Step 3: Verify the canonical database exists and is ONLINE ---------------
Write-Host "[3/6] Verifying database '$CanonicalDb' exists and is ONLINE..."
try {
    $conn = New-Object System.Data.SqlClient.SqlConnection($CloneConnStr)
    $conn.Open()
}
catch {
    Write-Host "[FATAL] Cannot connect to '$CanonicalDb' on '$SqlInstance'." -ForegroundColor Red
    Write-Host "[FATAL] Ensure the database exists. Run 'scripts/db/import-prod-data-dev.ps1' to create it." -ForegroundColor Red
    exit 1
}

# -- Step 4: Execute SELECT DB_NAME() - actual runtime proof -----------------
Write-Host "[4/6] Verifying actual DB_NAME()..."
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT DB_NAME()"
$actualDb = $cmd.ExecuteScalar()
$conn.Close()
$conn.Dispose()

if ($actualDb -ne $CanonicalDb) {
    Write-Host "[FATAL] DB_NAME() returned '$actualDb' - expected '$CanonicalDb'." -ForegroundColor Red
    exit 1
}

# -- Step 5: Reject forbidden databases --------------------------------------
foreach ($forbidden in $ForbiddenDbs) {
    if ($actualDb -eq $forbidden) {
        Write-Host "[FATAL] Connected to forbidden database '$forbidden'. Aborting." -ForegroundColor Red
        exit 1
    }
}

# Reject remote instances (anything not localdb)
if ($SqlInstance -notmatch '^\(localdb\)') {
    Write-Host "[FATAL] SQL instance '$SqlInstance' is not LocalDB. Aborting." -ForegroundColor Red
    exit 1
}

Write-Host "[OK]   Verified: Server=$SqlInstance, Database=$actualDb" -ForegroundColor Green

# -- Step 6: Start services --------------------------------------------------
Write-Host "[5/6] Starting backend in $BackendDir..."

# Safe CMD quoting: each variable is quoted separately
$cmdArgs = "/k dotnet build && " +
           "set `"ASPNETCORE_ENVIRONMENT=Development`" && " +
           "set `"ConnectionStrings__DefaultConnection=$CloneConnStr`" && " +
           "dotnet bin\Debug\net8.0\AlplaPortal.Api.dll"
Start-Process "cmd" -ArgumentList $cmdArgs -WorkingDirectory $BackendDir -WindowStyle Normal

Write-Host "[6/6] Starting frontend in $FrontendDir..."
Start-Process "cmd" -ArgumentList "/c npm run dev" -WorkingDirectory $FrontendDir -WindowStyle Normal

Write-Host "`n[DONE] Services starting in separate windows." -ForegroundColor Green
Write-Host "       Backend:  http://localhost:$BackendPort"
Write-Host "       Frontend: http://localhost:$FrontendPort"
Write-Host "       Database: $actualDb on $SqlInstance"
