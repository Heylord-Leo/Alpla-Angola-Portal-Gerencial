# Verification Workflow v0.9.4 - Post-Submission package

$baseUrl = "http://localhost:5000/api/v1"
$userId = "8470c55a-bde2-41d7-ad0c-c8e433ef00e6" # Developer User

function Test-RequestFlow {
    param($typeCode, $expectedStatus)
    
    Write-Host "`n--- Testing Flow for $typeCode (Expected Status: $expectedStatus) ---" -ForegroundColor Cyan
    
    # 1. Create Draft
    $typeId = if ($typeCode -eq "QUOTATION") { 1 } else { 2 }
    $createPayload = @{
        title           = "Test $typeCode $(Get-Date)"
        description     = "Automated test for v0.9.4"
        requestTypeId   = $typeId
        requesterId     = $userId
        departmentId    = 1
        plantId         = 1
        needLevelId     = 1
        currencyId      = 1
        needByDateUtc   = (Get-Date).AddDays(7).ToString("yyyy-MM-dd")
        buyerId         = "22222222-2222-2222-2222-222222222222"
        areaApproverId  = "33333333-3333-3333-3333-333333333333"
        finalApproverId = "33333333-3333-3333-3333-333333333333"
        supplierId      = if ($typeCode -eq "PAYMENT") { 1 } else { $null }
    }

    $draft = Invoke-RestMethod -Uri "$baseUrl/requests" -Method Post -Body ($createPayload | ConvertTo-Json) -ContentType "application/json"
    $id = $draft.id
    Write-Host "Draft created: $id"

    # 2. Add Item if PAYMENT
    if ($typeCode -eq "PAYMENT") {
        $itemPayload = @{
            description  = "Test Item"
            quantity     = 1
            unit         = "EA"
            unitPrice    = 100
            itemPriority = "MEDIUM"
        }
        Invoke-RestMethod -Uri "$baseUrl/requests/$id/line-items" -Method Post -Body ($itemPayload | ConvertTo-Json) -ContentType "application/json" | Out-Null
        Write-Host "Item added for PAYMENT request."
    }

    # 3. Submit
    Write-Host "Submitting request..."
    Invoke-RestMethod -Uri "$baseUrl/requests/$id/submit" -Method Post | Out-Null

    # 4. Verify Final Status
    $final = Invoke-RestMethod -Uri "$baseUrl/requests/$id" -Method Get
    Write-Host "Final Status Code: $($final.statusCode)"
    Write-Host "Final Status Name: $($final.statusName)"
    
    if ($final.statusCode -eq $expectedStatus) {
        Write-Host "SUCCESS: Correct status transition." -ForegroundColor Green
    }
    else {
        Write-Host "FAILED: Expected $expectedStatus but got $($final.statusCode)" -ForegroundColor Red
        exit 1
    }

    # 5. Verify History
    $historyCount = $final.statusHistory.Count
    Write-Host "History records: $historyCount"
    $latest = $final.statusHistory[0]
    Write-Host "Latest History Action: $($latest.actionTaken)"
    Write-Host "Latest History Comment: $($latest.comment)"
    Write-Host "Latest History Status: $($latest.newStatusName)"
    
    if ($latest.actionTaken -eq "SUBMIT") {
        Write-Host "SUCCESS: History record created correctly." -ForegroundColor Green
    }
    else {
        Write-Host "FAILED: Latest history action is $($latest.actionTaken)" -ForegroundColor Red
        exit 1
    }
}

# Run tests
Test-RequestFlow -typeCode "PAYMENT" -expectedStatus "WAITING_AREA_APPROVAL"
Test-RequestFlow -typeCode "QUOTATION" -expectedStatus "WAITING_QUOTATION"

Write-Host "`n--- ALL WORKFLOW TESTS PASSED ---" -ForegroundColor Green
