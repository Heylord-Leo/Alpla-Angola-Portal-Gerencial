$ErrorActionPreference = "Stop"

# 1. Login to get token
$loginUrl = "http://localhost:5000/api/auth/login"
$loginBody = @{
    Email = "admin.portal@alpla.com"
    Password = "temp123"
} | ConvertTo-Json

Write-Host "Logging in to $loginUrl..."
$loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResponse.token
Write-Host "Token obtained."

# 2. Create Supplier
$supplierUrl = "http://localhost:5000/api/lookups/suppliers"
$supplierBody = @{
    Name = "Test Supplier " + (Get-Date -Format "HHmmss")
    TaxId = "123456789"
} | ConvertTo-Json

Write-Host "Creating supplier at $supplierUrl..."
$creationResponse = Invoke-RestMethod -Uri $supplierUrl -Method Post -Headers @{ Authorization = "Bearer $token" } -Body $supplierBody -ContentType "application/json"

Write-Host "SUCCESS!"
Write-Host "Created Supplier ID: $($creationResponse.id)"
Write-Host "Generated Portal Code: $($creationResponse.portalCode)"
Write-Host "Supplier Name: $($creationResponse.name)"
