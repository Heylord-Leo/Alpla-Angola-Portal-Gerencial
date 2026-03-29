$baseUrl = "http://localhost:5000/api/v1"
$headers = @{ "Content-Type" = "application/json" }

function Test-Scenario ($name, $action) {
    Write-Host "`n--- SCENARIO: $name ---" -ForegroundColor Cyan
    try {
        &$action
        Write-Host "RESULT: SUCCESS" -ForegroundColor Green
    }
    catch {
        Write-Host "RESULT: FAILED" -ForegroundColor Red
        Write-Error $_.Exception.Message
        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            Write-Host "Response Body: $($reader.ReadToEnd())" -ForegroundColor Yellow
        }
    }
}

# Guaranteed valid user from DB (Developer User)
$uValid = "8470c55a-bde2-41d7-ad0c-c8e433ef00e6"

# 1. Create QUOTATION
Test-Scenario "Create QUOTATION Draft" {
    $payload = @{
        title           = "Quotation Test v0.9.3"
        description     = "Test quotation submission without items"
        requestTypeId   = 1 # QUOTATION
        needLevelId     = 1
        departmentId    = 1
        plantId         = 1
        needByDateUtc   = (Get-Date).AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ")
        buyerId         = $uValid
        areaApproverId  = $uValid
        finalApproverId = $uValid
    } | ConvertTo-Json
    
    $response = Invoke-RestMethod -Uri "$baseUrl/requests" -Method Post -Body $payload -Headers $headers
    # Debug: Write-Host "Full Response: $($response | ConvertTo-Json)"
    $global:quotationId = $response.id
    Write-Host "Created Quotation: $($global:quotationId)"
}

# 2. Submit QUOTATION without item
Test-Scenario "Submit QUOTATION without items (Should SUCCEED)" {
    if (-not $global:quotationId) { throw "No Quotation ID found" }
    $response = Invoke-RestMethod -Uri "$baseUrl/requests/$global:quotationId/submit" -Method Post -Headers $headers
    Write-Host "Response Message: $($response.message)"
    if ($response.message -ne "Pedido enviado para cotação com sucesso.") { throw "Wrong success message: $($response.message)" }
}

# 3. Create PAYMENT
Test-Scenario "Create PAYMENT Draft" {
    $payload = @{
        title           = "Payment Test v0.9.3"
        description     = "Test payment submission rules"
        requestTypeId   = 2 # PAYMENT
        needLevelId     = 1
        departmentId    = 1
        plantId         = 1
        needByDateUtc   = (Get-Date).AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ")
        buyerId         = $uValid
        areaApproverId  = $uValid
        finalApproverId = $uValid
        supplierId      = 1
    } | ConvertTo-Json
    
    $response = Invoke-RestMethod -Uri "$baseUrl/requests" -Method Post -Body $payload -Headers $headers
    $global:paymentId = $response.id
    Write-Host "Created Payment: $($global:paymentId)"
}

# 4. Submit PAYMENT without item (Should FAIL)
Test-Scenario "Submit PAYMENT without items (Should FAIL)" {
    if (-not $global:paymentId) { throw "No Payment ID found" }
    try {
        $response = Invoke-WebRequest -Uri "$baseUrl/requests/$global:paymentId/submit" -Method Post -Headers $headers
        throw "Should have failed but succeeded"
    }
    catch {
        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $respBody = $reader.ReadToEnd() | ConvertFrom-Json
            Write-Host "Error Detail: $($respBody.detail)"
            if ($respBody.detail -notlike "*Para submeter, o pedido deve conter pelo menos um item.*") { throw "Wrong error message: $($respBody.detail)" }
        }
        else {
            throw $_.Exception.Message
        }
    }
}

# 5. Add Item to PAYMENT
Test-Scenario "Add Line Item to PAYMENT" {
    if (-not $global:paymentId) { throw "No Payment ID found" }
    $itemPayload = @{
        description  = "Test Item"
        quantity     = 1
        unitId       = 1
        unitPrice    = 100
        itemPriority = "MEDIUM"
        currencyId   = 1
    } | ConvertTo-Json
    Invoke-RestMethod -Uri "$baseUrl/requests/$global:paymentId/line-items" -Method Post -Body $itemPayload -Headers $headers
}

# 6. Submit PAYMENT with item (Should SUCCEED)
Test-Scenario "Submit PAYMENT with items (Should SUCCEED)" {
    if (-not $global:paymentId) { throw "No Payment ID found" }
    $response = Invoke-RestMethod -Uri "$baseUrl/requests/$global:paymentId/submit" -Method Post -Headers $headers
    Write-Host "Response Message: $($response.message)"
    if ($response.message -ne "Pedido enviado para aprovação da área com sucesso.") { throw "Wrong success message: $($response.message)" }
}

Write-Host "`nWorkflow Verification Completed" -ForegroundColor White
