<#
.SYNOPSIS
PO-flow evidence scan (READ-ONLY, hardened): bounded retry/consensus extraction over the stored
attachments, producing the aggregated evidence report for Population B (supplier identity) and
the suspicious historical P.O numbers. Performs NO database writes and NO portal mutations.

.DESCRIPTION
Hardening rationale (2026-08-20 full-scan findings): OCR single-pass negatives are NOT reliable —
byte-identical attachments returned positive evidence in one run and empty fields in another.
Therefore:
  - each UNIQUE binary (grouped by DB FileHash, else downloaded SHA-256) is extracted up to
    -MaxAttempts times (default 3), stopping early once sufficient evidence exists;
  - evidence aggregation is MONOTONIC: an empty later attempt never overwrites earlier positive
    evidence; distinct conflicting positive parses => CONFLICTING_EXTRACTIONS (kept for review);
  - an OCR call that succeeds but yields no supplier/PO fields is INCONCLUSIVE, never NO_MATCH
    (NO_MATCH is reserved for actually-extracted evidence that failed comparison);
  - ALPLA legal-entity NIFs (5417567485 Plástico, 5001760246 SOPRO) are NEVER supplier evidence:
    party-role protection records warning BILLED_COMPANY_NIF_AS_SUPPLIER instead of NIF_EXACT;
  - supplier-name matching is accent/diacritic-insensitive (Portuguese names) BEFORE
    punctuation/spacing stripping; name-only matches stay NAME_PROBABLE (human confirmation);
  - -PriorEvidenceCsv carries forward confirmed/strong evidence from earlier reviewed runs so a
    later empty retry can never downgrade it (e.g. REQ-101 = ECF11 2026/386 CONFIRMED).

Endpoints used (read-only):
  GET  {ApiBase}/api/v1/attachments/{id}/download
  POST {ApiBase}/api/v1/requests/direct-ocr

.EXAMPLE
.\po-flow-evidence-scan.ps1 -ApiBase "https://portalgerencial-test.alpla.net" -Token $jwt `
    -InventoryCsv .\inventory.csv -SuppliersCsv .\suppliers.csv `
    -PriorEvidenceCsv .\po-flow-prior-evidence.csv `
    -RequestFilter 'REQ-20/07/2026-101','REQ-20/07/2026-098','REQ-31/07/2026-193',
                   'REQ-11/08/2026-230','REQ-11/08/2026-233','REQ-23/07/2026-146'
#>
param(
    [Parameter(Mandatory)] [string]$ApiBase,
    [Parameter(Mandatory)] [string]$Token,
    [Parameter(Mandatory)] [string]$InventoryCsv,
    [Parameter(Mandatory)] [string]$SuppliersCsv,
    [string]$PriorEvidenceCsv,
    [string[]]$RequestFilter,
    [int]$MaxAttempts = 3,
    [string]$OutDir = ".\po-evidence-out"
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$headers = @{ Authorization = "Bearer $Token" }
$suppliers = Import-Csv $SuppliersCsv
$prior = if ($PriorEvidenceCsv -and (Test-Path $PriorEvidenceCsv)) { Import-Csv $PriorEvidenceCsv } else { @() }

# ALPLA legal entities: their NIFs are the BILLED party on purchase documents, never the supplier.
$CompanyNifs = @('5417567485', '5001760246')

function Get-DigitsOnly([string]$s) { if ($s) { ($s -replace '\D', '') } else { '' } }

# Accent/diacritic-insensitive, then punctuation/spacing-insensitive, uppercase.
function Get-NormalizedName([string]$s) {
    if (-not $s) { return '' }
    $formD = $s.Normalize([Text.NormalizationForm]::FormD)
    $noMarks = ($formD.ToCharArray() | Where-Object {
        [Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne [Globalization.UnicodeCategory]::NonSpacingMark
    }) -join ''
    return ($noMarks.ToUpperInvariant() -replace '[^A-Z0-9]', '')
}

# Corporate boilerplate that carries no supplier identity — ignored in token comparison.
$NameStopTokens = @('LDA','LIMITADA','LTDA','SA','SU','EI','E','DE','DA','DO','DAS','DOS')

# Distinctive tokens of a name: diacritic-normalized, boilerplate removed.
function Get-NameTokens([string]$s) {
    if (-not $s) { return @() }
    $formD = $s.Normalize([Text.NormalizationForm]::FormD)
    $noMarks = ($formD.ToCharArray() | Where-Object {
        [Globalization.CharUnicodeInfo]::GetUnicodeCategory($_) -ne [Globalization.UnicodeCategory]::NonSpacingMark
    }) -join ''
    return @(($noMarks.ToUpperInvariant() -replace '[^A-Z0-9]', ' ') -split '\s+' |
        Where-Object { $_ -and ($NameStopTokens -notcontains $_) })
}

# Conservative name comparison: exact containment of the normalized strings, OR the smaller
# distinctive-token set fully contained in the larger (with enough distinctive substance).
# Never a write decision — callers classify the result as NAME_PROBABLE at most.
function Test-NameMatch([string]$ocrName, [string]$portalName) {
    $na = Get-NormalizedName $ocrName; $nb = Get-NormalizedName $portalName
    if ($na.Length -lt 4 -or $nb.Length -lt 4) { return $false }
    if ($na.Contains($nb) -or $nb.Contains($na)) { return $true }
    $ta = Get-NameTokens $ocrName; $tb = Get-NameTokens $portalName
    if ($ta.Count -eq 0 -or $tb.Count -eq 0) { return $false }
    $small = if ($ta.Count -le $tb.Count) { $ta } else { $tb }
    $large = if ($ta.Count -le $tb.Count) { $tb } else { $ta }
    $allContained = -not ($small | Where-Object { $large -notcontains $_ })
    $substantive = ($small.Count -ge 2) -or (($small | Measure-Object -Maximum Length).Maximum -ge 6)
    return $allContained -and $substantive
}

# ── Group inventory rows into unique binaries (FileHash from DB when present) ──────────────
$rows = Import-Csv $InventoryCsv
if ($RequestFilter) { $rows = $rows | Where-Object { $RequestFilter -contains $_.RequestNumber } }

$groups = @{}
foreach ($row in $rows) {
    $key = if ($row.FileHash) { "$($row.Scope)|$($row.RequestNumber)|$($row.FileHash)" }
           else { "$($row.Scope)|$($row.RequestNumber)|NOHASH|$($row.AttachmentId)" }
    if (-not $groups.ContainsKey($key)) { $groups[$key] = @() }
    $groups[$key] += $row
}

$report = @()
foreach ($key in $groups.Keys) {
    $members = $groups[$key]
    $first = $members[0]
    $isPoScope = $first.Scope -eq 'SUSPICIOUS_PO'
    $warnings = @()

    # ── Download ONE representative binary; SHA-256 both dedups and verifies vs DB hash ──
    # Preserve the original attachment extension — the extraction pipeline rejects/derates
    # extensionless uploads (.bin), which caused false-INCONCLUSIVE scans. Fallback: .pdf.
    $extension = [System.IO.Path]::GetExtension($first.FileName)
    if ([string]::IsNullOrWhiteSpace($extension)) { $extension = '.pdf' }
    $pdf = Join-Path $OutDir "$($first.AttachmentId)$extension"
    $downloadOk = $false; $sha = ''
    foreach ($m in $members) {   # fall through the copies if one download fails
        try {
            Invoke-WebRequest -Uri "$ApiBase/api/v1/attachments/$($m.AttachmentId)/download" `
                -Headers $headers -OutFile $pdf -TimeoutSec 120 | Out-Null
            $sha = (Get-FileHash $pdf -Algorithm SHA256).Hash
            $downloadOk = $true; break
        } catch { $warnings += "DOWNLOAD_FAILED:$($m.AttachmentId):$($_.Exception.Message)" }
    }
    if ($downloadOk -and $first.FileHash -and ($sha -ne $first.FileHash)) {
        $warnings += "HASH_MISMATCH_DB_VS_DOWNLOAD"
    }

    # ── Bounded retry with monotonic aggregation ─────────────────────────────────────────
    $attempts = 0; $successfulExtractions = 0; $positiveParses = 0
    $best = @{ PoDisplay=$null; PoCanonical=$null; PoFamily=$null; SupplierName=$null; SupplierNif=$null }
    $allCanonicals = @()

    while ($downloadOk -and $attempts -lt $MaxAttempts) {
        $attempts++
        $ocr = $null
        try { $ocr = Invoke-RestMethod -Uri "$ApiBase/api/v1/requests/direct-ocr" -Method Post `
                  -Form @{ file = Get-Item $pdf } -TimeoutSec 240 }
        catch { $warnings += "EXTRACTION_CALL_FAILED_ATTEMPT${attempts}"; continue }
        $successfulExtractions++

        $h = $ocr.integration.headerSuggestions
        # Monotonic: only ADD evidence, never blank it.
        if ($h.purchaseOrderReferenceCanonical.value) {
            $positiveParses++
            $allCanonicals += $h.purchaseOrderReferenceCanonical.value
            if (-not $best.PoCanonical) {
                $best.PoDisplay   = $h.purchaseOrderReference.value
                $best.PoCanonical = $h.purchaseOrderReferenceCanonical.value
                $best.PoFamily    = $h.purchaseOrderFamily.value
            }
        }
        if ($h.supplierName.value  -and -not $best.SupplierName) { $best.SupplierName = $h.supplierName.value }
        if ($h.supplierTaxId.value -and -not $best.SupplierNif)  { $best.SupplierNif  = $h.supplierTaxId.value }

        # Early-stop rules
        if ($isPoScope -and $best.PoCanonical -and $best.PoDisplay -and $best.PoFamily) { break }
        if (-not $isPoScope) {
            $d = Get-DigitsOnly $best.SupplierNif
            $nifUsable = $d.Length -ge 8 -and ($CompanyNifs -notcontains $d)
            if ($nifUsable -and ($suppliers | Where-Object { (Get-DigitsOnly $_.TaxId) -eq $d })) { break }
            if ($best.SupplierName -and $best.SupplierNif) { break }
        }
    }
    Remove-Item $pdf -Force -Confirm:$false -ErrorAction SilentlyContinue

    # ── Party-role protection: an ALPLA entity NIF is never supplier evidence ───────────
    $nifDigits = Get-DigitsOnly $best.SupplierNif
    $partyRoleViolation = $nifDigits -and ($CompanyNifs -contains $nifDigits)
    if ($partyRoleViolation) {
        $warnings += "BILLED_COMPANY_NIF_AS_SUPPLIER"
        $nifDigits = ''   # excluded from matching; raw value still reported
    }

    # ── Supplier candidate matching ─────────────────────────────────────────────────────
    $candidate = $null; $supplierStatus = 'INCONCLUSIVE'
    if ($nifDigits.Length -ge 8) {
        $candidate = $suppliers | Where-Object { (Get-DigitsOnly $_.TaxId) -eq $nifDigits } | Select-Object -First 1
        if ($candidate) { $supplierStatus = 'NIF_EXACT' }
        elseif ($best.SupplierNif) { $supplierStatus = 'NO_MATCH' }   # real extracted NIF, no supplier carries it
    }
    if (-not $candidate -and $best.SupplierName) {
        $candidate = $suppliers | Where-Object { Test-NameMatch $best.SupplierName $_.Name } | Select-Object -First 1
        if ($candidate -and $supplierStatus -ne 'NIF_EXACT') { $supplierStatus = 'NAME_PROBABLE' }
    }
    if ($partyRoleViolation -and -not $candidate) { $supplierStatus = 'INVALID_PARTY_ROLE_NIF' }

    # ── PO evidence status ──────────────────────────────────────────────────────────────
    $distinctCanonicals = $allCanonicals | Select-Object -Unique
    $poStatus = if ($distinctCanonicals.Count -gt 1) { 'CONFLICTING_EXTRACTIONS' }
                elseif ($best.PoCanonical) { 'POSITIVE_PO_PARSE' }
                else { 'INCONCLUSIVE' }

    # ── Merge prior reviewed evidence (monotonic across RUNS, not just attempts) ────────
    $priorRow = $prior | Where-Object { $_.RequestNumber -eq $first.RequestNumber -and $_.Scope -eq $first.Scope } | Select-Object -First 1
    if ($priorRow) {
        if ($priorRow.DetectedPoCanonical -and -not $best.PoCanonical) {
            $best.PoDisplay = $priorRow.DetectedPoDisplay; $best.PoCanonical = $priorRow.DetectedPoCanonical
            $best.PoFamily = $priorRow.DetectedFamily
            $poStatus = $priorRow.Status; $warnings += "EVIDENCE_FROM_PRIOR_REVIEWED_RUN"
        }
        elseif ($priorRow.DetectedPoCanonical -and $best.PoCanonical -and ($priorRow.DetectedPoCanonical -ne $best.PoCanonical)) {
            $poStatus = 'CONFLICTING_EXTRACTIONS'; $warnings += "PRIOR_VS_CURRENT_PARSE_CONFLICT:$($priorRow.DetectedPoCanonical)"
        }
        elseif ($priorRow.DetectedPoCanonical -and ($priorRow.Status -eq 'CONFIRMED')) { $poStatus = 'CONFIRMED' }
        if ($priorRow.RecommendedSupplierId -and -not $candidate) {
            $candidate = $suppliers | Where-Object { $_.Id -eq $priorRow.RecommendedSupplierId } | Select-Object -First 1
            if ($candidate) { $supplierStatus = $priorRow.Status; $warnings += "SUPPLIER_FROM_PRIOR_REVIEWED_RUN" }
        }
    }

    $confidence = if ($poStatus -eq 'CONFIRMED') { 'CONFIRMED' }
                  elseif ($poStatus -eq 'CONFLICTING_EXTRACTIONS') { 'CONFLICTING — review all values' }
                  elseif ($isPoScope -and $best.PoCanonical) { 'HIGH (positive parse)' }
                  elseif ($supplierStatus -eq 'NIF_EXACT') { 'HIGH (NIF)' }
                  elseif ($supplierStatus -eq 'NAME_PROBABLE') { 'MEDIUM (name, human confirmation)' }
                  elseif ($supplierStatus -eq 'NO_MATCH') { 'LOW (extracted but unmatched)' }
                  else { 'INCONCLUSIVE (absence of extraction is not evidence of absence)' }

    $report += [pscustomobject]@{
        Scope                     = $first.Scope
        RequestNumber             = $first.RequestNumber
        AttachmentIds             = ($members.AttachmentId -join ';')
        FileNames                 = (($members.FileName | Select-Object -Unique) -join ';')
        FileHash                  = if ($first.FileHash) { $first.FileHash } else { $sha }
        Attempts                  = $attempts
        SuccessfulExtractions     = $successfulExtractions
        PositiveParses            = $positiveParses
        OcrSupplierName           = $best.SupplierName
        OcrSupplierNif            = if ($best.SupplierNif) { $best.SupplierNif } else { 'not extracted' }
        SupplierEvidenceStatus    = $supplierStatus
        CandidatePortalSupplier   = $candidate.Name
        RecommendedSupplierId     = $candidate.Id
        DetectedFamily            = $best.PoFamily
        DetectedPoDisplay         = $best.PoDisplay
        DetectedPoCanonical       = $best.PoCanonical
        PoEvidenceStatus          = $poStatus
        Confidence                = $confidence
        Warnings                  = ($warnings -join ';')
        RecommendedReplacement    = if ($isPoScope -and $best.PoCanonical -and $poStatus -ne 'CONFLICTING_EXTRACTIONS') { $best.PoDisplay } else { $null }
        RequiresHumanConfirmation = $true   # ALWAYS — this process never authorizes a write
    }
}

$outCsv = Join-Path $OutDir "po-flow-evidence-report.csv"
$report | Sort-Object Scope, RequestNumber | Export-Csv $outCsv -NoTypeInformation -Encoding UTF8
$report | Sort-Object Scope, RequestNumber | Format-Table RequestNumber, Attempts, PositiveParses, OcrSupplierNif, SupplierEvidenceStatus, DetectedPoDisplay, PoEvidenceStatus, Confidence -AutoSize
Write-Host "Report written to $outCsv — NO database writes performed."
