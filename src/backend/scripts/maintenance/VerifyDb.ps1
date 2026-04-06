$maxSupp = sqlcmd -S "(localdb)\MSSQLLocalDB" -d AlplaPortalV1 -Q "SELECT MAX(PortalCode) FROM Suppliers" -h -1
$currCount = sqlcmd -S "(localdb)\MSSQLLocalDB" -d AlplaPortalV1 -Q "SELECT CurrentValue FROM SystemCounters WHERE Id = 'SUPPLIER_PORTAL_CODE'" -h -1

Write-Host "Max Supplier in DB: [$($maxSupp.Trim())]"
Write-Host "Current Counter: [$($currCount.Trim())]"
