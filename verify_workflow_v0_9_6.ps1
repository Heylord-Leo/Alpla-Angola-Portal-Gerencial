# Verify Final Approval Workflow (v0.9.6)
$baseUrl = "http://localhost:5000/api/v1"
$userId = "8470c55a-bde2-41d7-ad0c-c8e433ef00e6" # Developer User
$buyerId = "22222222-2222-2222-2222-222222222222"
$approverId = "33333333-3333-3333-3333-333333333333"

function Setup-Request {
    param($title)
    $payload = @{
        title           = $title
        description     = "Verification for v0.9.6"
        requestTypeId   = 2 # PAYMENT
        needLevelId     = 2 # MEDIUM
        departmentId    = 1
        plantId         = 1
        needByDateUtc   = (Get-Date).AddDays(7).ToString("yyyy-MM-dd")
        requesterId     = $userId
        buyerId         = $buyerId
        areaApproverId  = $approverId
        finalApproverId = $approverId
        supplierId      = "1"
        currencyId      = 1
    }
    Write-Host "Creating request: $title"
    $res = Invoke-RestMethod -Uri "$baseUrl/requests" -Method Post -Body ($payload | ConvertTo-Json) -ContentType "application/json" -SkipCertificateCheck
    $requestId = $res.id
    Write-Host "Created Request ID: $requestId"

    # Add item
    $itemPayload = @{
        description  = "Test Item"
        quantity     = 1
        unit         = "EA"
        unitPrice    = 100
        itemPriority = "MEDIUM"
        unitId       = 1
        currencyId   = 1
    }
    $itemUrl = "$baseUrl/requests/$requestId/line-items"
    Invoke-RestMethod -Uri $itemUrl -Method Post -Body ($itemPayload | ConvertTo-Json) -ContentType "application/json" -SkipCertificateCheck | Out-Null
    
    # Submit
    $submitUrl = "$baseUrl/requests/$requestId/submit"
    Invoke-RestMethod -Uri $submitUrl -Method Post -SkipCertificateCheck | Out-Null
    
    # Area Approve -> WAITING_FINAL_APPROVAL
    $areaApproveUrl = "$baseUrl/requests/$requestId/area-approval/approve"
    Invoke-RestMethod -Uri $areaApproveUrl -Method Post -Body (@{comment = "Area Approved" } | ConvertTo-Json) -ContentType "application/json" -SkipCertificateCheck | Out-Null
    
    return $requestId
}

Write-Host "--- TEST 1: Final Approval -> APPROVED ---" -ForegroundColor Cyan
try {
    $id1 = Setup-Request "Final Approval Test - Approve"
    $approveFinalUrl = "$baseUrl/requests/$id1/final-approval/approve"
    $res1 = Invoke-RestMethod -Uri $approveFinalUrl -Method Post -Body (@{comment = "Final Approved" } | ConvertTo-Json) -ContentType "application/json" -SkipCertificateCheck
    $details1 = Invoke-RestMethod -Uri "$baseUrl/requests/$id1" -Method Get -SkipCertificateCheck
    Write-Host "Status: $($details1.statusName) ($($details1.statusCode))"
    if ($details1.statusCode -eq "APPROVED") { Write-Host "PASS" -ForegroundColor Green } else { Write-Host "FAIL" -ForegroundColor Red }
}
catch {
    Write-Host "ERROR in Test 1: $_" -ForegroundColor Red
}

Write-Host "`n--- TEST 2: Final Approval -> REJECTED ---" -ForegroundColor Cyan
try {
    $id2 = Setup-Request "Final Approval Test - Reject"
    $rejectFinalUrl = "$baseUrl/requests/$id2/final-approval/reject"
    $res2 = Invoke-RestMethod -Uri $rejectFinalUrl -Method Post -Body (@{comment = "Final Rejected Reason" } | ConvertTo-Json) -ContentType "application/json" -SkipCertificateCheck
    $details2 = Invoke-RestMethod -Uri "$baseUrl/requests/$id2" -Method Get -SkipCertificateCheck
    Write-Host "Status: $($details2.statusName) ($($details2.statusCode))"
    if ($details2.statusCode -eq "REJECTED") { Write-Host "PASS" -ForegroundColor Green } else { Write-Host "FAIL" -ForegroundColor Red }
}
catch {
    Write-Host "ERROR in Test 2: $_" -ForegroundColor Red
}

Write-Host "`n--- TEST 3: Final Approval -> FINAL_ADJUSTMENT ---" -ForegroundColor Cyan
try {
    $id3 = Setup-Request "Final Approval Test - Adjust"
    $adjustFinalUrl = "$baseUrl/requests/$id3/final-approval/request-adjustment"
    $res3 = Invoke-RestMethod -Uri $adjustFinalUrl -Method Post -Body (@{comment = "Final Adjustment Reason" } | ConvertTo-Json) -ContentType "application/json" -SkipCertificateCheck
    $details3 = Invoke-RestMethod -Uri "$baseUrl/requests/$id3" -Method Get -SkipCertificateCheck
    Write-Host "Status: $($details3.statusName) ($($details3.statusCode))"
    if ($details3.statusCode -eq "FINAL_ADJUSTMENT") { Write-Host "PASS" -ForegroundColor Green } else { Write-Host "FAIL" -ForegroundColor Red }
}
catch {
    Write-Host "ERROR in Test 3: $_" -ForegroundColor Red
}

Write-Host "`n--- Verification Complete ---" -ForegroundColor Yellow
