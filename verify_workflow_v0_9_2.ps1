
$baseUrl = "http://localhost:5000/api/v1"

function Test-Request {
    param($method, $uri, $body)
    try {
        if ($body) {
            return Invoke-RestMethod -Uri $uri -Method $method -ContentType "application/json" -Body $body
        }
        else {
            return Invoke-RestMethod -Uri $uri -Method $method
        }
    }
    catch {
        Write-Host "ERROR on $method $uri"
        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            Write-Host "Response Body: $responseBody"
        }
        else {
            Write-Host "No response body. Error: $($_.Exception.Message)"
        }
        return $null
    }
}

$futureDate = (Get-Date).AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ")

# 1. Test PAYMENT transition
Write-Host "`nTesting PAYMENT transition..."
$bodyP = @{
    title         = "Test Payment Status v0.9.2"
    description   = "Verification of specific transitions"
    requestTypeId = 2 # PAYMENT
    supplierId    = 1
    needByDateUtc = $futureDate
    departmentId  = 1
    plantId       = 1
    needLevelId   = 1
} | ConvertTo-Json

$reqP = Test-Request -method Post -uri "$baseUrl/Requests" -body $bodyP
if (!$reqP) { return }
$reqId = $reqP.id
Write-Host "Created PAYMENT Request ID: $reqId"

# Add dummy item
$bodyItem = @{
    description  = "Test Item"
    itemPriority = "MEDIUM"
    quantity     = 1
    unitId       = 1
    unitPrice    = 100
} | ConvertTo-Json
Test-Request -method Post -uri "$baseUrl/Requests/$reqId/line-items" -body $bodyItem > $null

# Fill required header before submission
$bodyHeader = @{
    title           = "Test Payment Status v0.9.2"
    description     = "Verification of specific transitions"
    requestTypeId   = 2
    needLevelId     = 1
    departmentId    = 1
    plantId         = 1
    needByDateUtc   = $futureDate
    buyerId         = "22222222-2222-2222-2222-222222222222"
    areaApproverId  = "33333333-3333-3333-3333-333333333333"
    finalApproverId = "33333333-3333-3333-3333-333333333333"
    supplierId      = 1
} | ConvertTo-Json
Test-Request -method Put -uri "$baseUrl/Requests/$reqId/draft" -body $bodyHeader > $null

# Submit
Write-Host "Submitting PAYMENT..."
Test-Request -method Post -uri "$baseUrl/Requests/$reqId/submit" > $null

$finalReq = Test-Request -method Get -uri "$baseUrl/Requests/$reqId"
Write-Host "Submitted PAYMENT Request Status: $($finalReq.statusCode) ($($finalReq.statusName))"

if ($finalReq.statusCode -eq "WAITING_AREA_APPROVAL") { Write-Host "✅ PAYMENT transition CORRECT" } else { Write-Error "❌ PAYMENT transition WRONG" }

# 2. Test QUOTATION transition
Write-Host "`nTesting QUOTATION transition..."
$bodyQ = @{
    title         = "Test Quotation Status v0.9.2"
    description   = "Verification of specific transitions"
    requestTypeId = 1 # QUOTATION
    needByDateUtc = $futureDate
    departmentId  = 1
    plantId       = 1
    needLevelId   = 1
} | ConvertTo-Json
$reqQ = Test-Request -method Post -uri "$baseUrl/Requests" -body $bodyQ
if (!$reqQ) { return }
$reqIdQ = $reqQ.id
Write-Host "Created QUOTATION Request ID: $reqIdQ"

# Add dummy item
Test-Request -method Post -uri "$baseUrl/Requests/$reqIdQ/line-items" -body $bodyItem > $null

# Fill required header
$bodyHeaderQ = @{
    title           = "Test Quotation Status v0.9.2"
    description     = "Verification of specific transitions"
    requestTypeId   = 1
    needLevelId     = 1
    departmentId    = 1
    plantId         = 1
    needByDateUtc   = $futureDate
    buyerId         = "22222222-2222-2222-2222-222222222222"
    areaApproverId  = "33333333-3333-3333-3333-333333333333"
    finalApproverId = "33333333-3333-3333-3333-333333333333"
} | ConvertTo-Json
Test-Request -method Put -uri "$baseUrl/Requests/$reqIdQ/draft" -body $bodyHeaderQ > $null

# Submit
Write-Host "Submitting QUOTATION..."
Test-Request -method Post -uri "$baseUrl/Requests/$reqIdQ/submit" > $null

$finalReqQ = Test-Request -method Get -uri "$baseUrl/Requests/$reqIdQ"
Write-Host "Submitted QUOTATION Request Status: $($finalReqQ.statusCode) ($($finalReqQ.statusName))"

if ($finalReqQ.statusCode -eq "WAITING_QUOTATION") { Write-Host "✅ QUOTATION transition CORRECT" } else { Write-Error "❌ QUOTATION transition WRONG" }
