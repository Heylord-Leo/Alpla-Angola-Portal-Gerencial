
$path = "c:\dev\alpla-portal\src\frontend\src\pages\Buyer\BuyerItemsList.tsx"
(Get-Content $path).Replace('class="quotation-form-body"', 'className="quotation-form-body"') | Set-Content $path
write-output "Replaced successfully"
