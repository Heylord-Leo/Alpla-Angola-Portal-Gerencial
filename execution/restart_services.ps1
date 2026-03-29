# Restart Services PowerShell Script

function Stop-ProcessByPort($port) {
    Write-Host "Checking for process on port $port..."
    $processId = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -First 1
    if ($processId) {
        Write-Host "Killing process $processId on port $port..."
        Stop-Process -Id $processId -Force
        Start-Sleep -Seconds 1
    } else {
        Write-Host "No process found on port $port."
    }
}

Write-Host "Stopping existing services..."
Stop-ProcessByPort 5000
Stop-ProcessByPort 5173

# Start Backend
$backendDir = "c:\dev\alpla-portal\src\backend\AlplaPortal.Api"
Write-Host "Starting backend in $backendDir..."
Start-Process "dotnet" -ArgumentList "run" -WorkingDirectory $backendDir -WindowStyle Normal

# Start Frontend
$frontendDir = "c:\dev\alpla-portal\src\frontend"
Write-Host "Starting frontend in $frontendDir..."
Start-Process "cmd" -ArgumentList "/c npm run dev" -WorkingDirectory $frontendDir -WindowStyle Normal

Write-Host "Services restart initiated in separate windows."
