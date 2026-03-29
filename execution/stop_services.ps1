# Stop Services PowerShell Script
# Stops Alpla Portal Backend (5000) and Frontend (5173)

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

Write-Host "Stopping Alpla Portal services..."
Stop-ProcessByPort 5000
Stop-ProcessByPort 5173

Write-Host "All portal services stopped."
