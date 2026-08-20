<#
.SYNOPSIS
PO-flow evidence scan (READ-ONLY): downloads each inventoried attachment through the deployed
API and runs it through the real extraction pipeline (direct-ocr) + the deterministic Primavera
parser surfaced by v2.229.12, producing the document-evidence report for:
  - Population B (supplier identity — RequiresHumanConfirmation is ALWAYS true; never writes)
  - Suspicious historical P.O numbers (RecommendedReplacement from positive parse only)

.DESCRIPTION
Stage 2 of the process. Stage 1 (po-flow-evidence-attachments-readonly.sql) produces the
attachment inventory CSV: Scope,RequestNumber,AttachmentId,FileName,AttachmentTypeCode,...
This script performs NO database writes and NO portal mutations — it only calls:
  GET  {ApiBase}/api/v1/attachments/{id}/download   (authenticated, scope-checked)
  POST {ApiBase}/api/v1/requests/direct-ocr         (extraction only)

.EXAMPLE
.\po-flow-evidence-scan.ps1 -ApiBase "http://AOVIA1VMS011:5001" -Token $jwt `
    -InventoryCsv .\inventory.csv -SuppliersCsv .\suppliers.csv -OutDir .\evidence-out
  (suppliers.csv: Id,Name,TaxId,IsActive — export read-only from Suppliers)
#>
param(
    [Parameter(Mandatory)] [string]$ApiBase,
    [Parameter(Mandatory)] [string]$Token,
    [Parameter(Mandatory)] [string]$InventoryCsv,
    [Parameter(Mandatory)] [string]$SuppliersCsv,
    [string]$OutDir = ".\po-evidence-out"
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$headers = @{ Authorization = "Bearer $Token" }
$suppliers = Import-Csv $SuppliersCsv
$rows = Import-Csv $InventoryCsv
$report = @()

function Get-DigitsOnly([string]$s) { if ($s) { ($s -replace '\D', '') } else { '' } }

foreach ($row in $rows) {
    $pdf = Join-Path $OutDir "$($row.AttachmentId).bin"
    $status = 'OK'
    try {
        Invoke-WebRequest -Uri "$ApiBase/api/v1/attachments/$($row.AttachmentId)/download" `
            -Headers $headers -OutFile $pdf -TimeoutSec 120 | Out-Null
    } catch { $status = "DOWNLOAD_FAILED: $($_.Exception.Message)" }

    $ocr = $null
    if ($status -eq 'OK') {
        try {
            $ocr = Invoke-RestMethod -Uri "$ApiBase/api/v1/requests/direct-ocr" -Method Post `
                -Form @{ file = Get-Item $pdf } -TimeoutSec 240
        } catch { $status = "EXTRACTION_FAILED: $($_.Exception.Message)" }
    }

    $h = $ocr.integration.headerSuggestions
    $ocrNifDigits = Get-DigitsOnly $h.supplierTaxId.value
    $candidate = $null
    $matchStatus = 'NO_MATCH'
    if ($ocrNifDigits.Length -ge 8) {
        $candidate = $suppliers | Where-Object { (Get-DigitsOnly $_.TaxId) -eq $ocrNifDigits } | Select-Object -First 1
        if ($candidate) { $matchStatus = 'NIF_EXACT' }
    }
    if (-not $candidate -and $h.supplierName.value) {
        $needle = ($h.supplierName.value -replace '[^A-Za-z0-9]', '').ToUpperInvariant()
        $candidate = $suppliers | Where-Object {
            $sn = ($_.Name -replace '[^A-Za-z0-9]', '').ToUpperInvariant()
            $sn -and $needle -and ($sn.Contains($needle) -or $needle.Contains($sn))
        } | Select-Object -First 1
        if ($candidate) { $matchStatus = 'NAME_PROBABLE' }
    }

    $isPoScope = $row.Scope -eq 'SUSPICIOUS_PO'
    $report += [pscustomobject]@{
        Scope                    = $row.Scope
        RequestNumber            = $row.RequestNumber
        AttachmentId             = $row.AttachmentId
        FileName                 = $row.FileName
        ScanStatus               = $status
        OcrSupplierName          = $h.supplierName.value
        OcrSupplierNif           = $h.supplierTaxId.value
        DetectedFamily           = $h.purchaseOrderFamily.value
        DetectedPoDisplay        = $h.purchaseOrderReference.value
        DetectedPoCanonical      = $h.purchaseOrderReferenceCanonical.value
        CandidatePortalSupplier  = $candidate.Name
        RegisteredNif            = $candidate.TaxId
        MatchStatus              = $matchStatus
        Evidence                 = "direct-ocr on $($row.FileName) ($($row.AttachmentTypeCode))"
        Confidence               = $(if ($isPoScope -and $h.purchaseOrderReference.value) { 'HIGH (positive parse)' }
                                     elseif ($matchStatus -eq 'NIF_EXACT') { 'HIGH (NIF)' }
                                     elseif ($matchStatus -eq 'NAME_PROBABLE') { 'MEDIUM (name)' } else { 'LOW' })
        RecommendedSupplierId    = $candidate.Id
        RecommendedReplacement   = $(if ($isPoScope) { $h.purchaseOrderReference.value } else { $null })
        RequiresHumanConfirmation = $true   # ALWAYS — this process never authorizes a write
    }
    Remove-Item $pdf -Force -Confirm:$false -ErrorAction SilentlyContinue
}

$outCsv = Join-Path $OutDir "po-flow-evidence-report.csv"
$report | Export-Csv $outCsv -NoTypeInformation -Encoding UTF8
$report | Format-Table RequestNumber, ScanStatus, OcrSupplierNif, DetectedPoDisplay, CandidatePortalSupplier, MatchStatus, Confidence -AutoSize
Write-Host "Report written to $outCsv — NO database writes performed."
