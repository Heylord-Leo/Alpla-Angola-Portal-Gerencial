$text = Get-Content -Raw -Path 'scripts/server/setup-production-environment.ps1'
$errors = $null
$tokens = $null
[System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors) {
    foreach($e in $errors) {
        Write-Output "Error at line $($e.Extent.StartLineNumber): $($e.Message)"
    }
} else {
    Write-Output "NO SYNTAX ERRORS FOUND BY POWERSHELL PARSER"
}
