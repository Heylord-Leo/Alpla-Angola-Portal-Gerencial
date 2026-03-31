# Starts backend and frontend in separate PowerShell windows

$root = Resolve-Path "$PSScriptRoot/.."

# Backend
$backendPath = Join-Path $root "src/backend"
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd `"$backendPath`"; dotnet run --project AlplaPortal.Api"

# Frontend
$frontendPath = Join-Path $root "src/frontend"
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd `"$frontendPath`"; npm run dev"

Write-Host "Backend and Frontend starting in separate windows..."
