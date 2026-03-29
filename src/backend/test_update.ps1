$ErrorActionPreference = "Stop"
$body = @{
    supplierId = 1
    documentNumber = "FIX-TEST-CURL3"
    documentDate = "2026-03-20T00:00:00"
    currency = "USD"
    totalAmount = 60.0
    items = @(
        @{
            description = "Final Fix Check Mod 3"
            quantity = 3.0
            unitPrice = 20.0
            lineTotal = 60.0
        }
    )
} | ConvertTo-Json -Depth 5

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5000/api/v1/requests/be6948cf-0df1-4960-b667-824a7c8245e4/quotations/6fd76ed7-23fe-4a5d-a066-07084dab285a" -Method Put -Body $body -ContentType "application/json"
    Write-Host "Success!"
} catch {
    Write-Host "Error!"
    $stream = $_.Exception.Response.GetResponseStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $msg = $reader.ReadToEnd()
    Write-Host "Response Body: $msg"
}
