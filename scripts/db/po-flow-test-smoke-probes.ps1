<#
.SYNOPSIS
TEST smoke probes for the v2.229.12 PO guards — ZERO-MUTATION by design.

Every probe targets requests whose state makes a successful registration impossible
(the guards under test all answer 400 BEFORE the endpoint's mutation section), so nothing
in TEST is written regardless of outcome. Requires a normal TEST login token (any Buyer).

Expected results:
  T22  (own supplier NIF 5402118531)  -> 400 "Número de P.O inválido … parece ser um número fiscal (NIF)"
  T22b (ALPLA SOPRO NIF 5001760246)   -> 400 same message
  T23  (ECF11 2026-417, same company) -> 400 "DUPLICATE_PO … já está registrado no Pedido REQ-31/07/2026-201"
  T24  (ECF10 2026-214, SOPRO-owned)  -> 400 "Ação Inválida … status atual do pedido (PAYMENT_COMPLETED)"
        i.e. NOT a duplicate refusal — it passed the NIF and duplicate guards (cross-company is
        informational) and only then hit the status guard of this deliberately-closed request.
#>
param(
    [Parameter(Mandatory)] [string]$Token,
    [string]$ApiBase = 'https://portalgerencial-test.alpla.net'
)
$h = @{ Authorization = "Bearer $Token" }
$r190 = 'f688e8d8-596e-4d3a-975a-5c093144b9d8'   # REQ-30/07/2026-190 (PAYMENT_COMPLETED, Plástico)
$g190 = '54fbe437-4269-470f-87fe-2bdd847db2a0'   # its group (has TYPE_PO attachment, supplier set)

function Probe($body, $label) {
    try {
        $resp = Invoke-RestMethod -Uri "$ApiBase/api/v1/requests/$r190/operational/register-po" `
            -Method Post -Headers $h -ContentType 'application/json' -Body ($body | ConvertTo-Json) -TimeoutSec 60
        "[$label] 200 (UNEXPECTED — investigate): $($resp.message)"
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        $detail = $_.ErrorDetails.Message
        try { $err = $detail | ConvertFrom-Json; $detail = "$($err.title) — $($err.detail)" } catch {}
        "[$label] $code $detail"
    }
}

Probe @{ PoGroupId = $g190; PurchaseOrderNumber = '5402118531';     PaymentConditionCode = 'POST_PAID' } 'T22-own-supplier-NIF'
Probe @{ PoGroupId = $g190; PurchaseOrderNumber = '5001760246';     PaymentConditionCode = 'POST_PAID' } 'T22b-ALPLA-company-NIF'
Probe @{ PoGroupId = $g190; PurchaseOrderNumber = 'ECF11 2026-417'; PaymentConditionCode = 'POST_PAID' } 'T23-same-company-canonical'
Probe @{ PoGroupId = $g190; PurchaseOrderNumber = 'ECF10 2026-214'; PaymentConditionCode = 'POST_PAID' } 'T24-cross-company-canonical'
