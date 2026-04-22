$connString = "Server=AOVIA1VMS012\SQLINNUX;Database=Innux;User Id=sa;Password=ad#56&Hfe;TrustServerCertificate=True"
$conn = New-Object System.Data.SqlClient.SqlConnection($connString)
$conn.Open()

Write-Output "--- dbo.Alteracoes ---"
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT IDAlteracao, Data, Entrada1, Saida1, Marcacao, Falta, Ausencia, Objectivo, Validado, TipoAnomalia, Saldo FROM dbo.Alteracoes WHERE IDFuncionario = 1679 AND Data = '2026-04-01'"
$reader = $cmd.ExecuteReader()
$idAlteracao = $null
while ($reader.Read()) {
    $idAlteracao = $reader["IDAlteracao"]
    Write-Output ("IDAlteracao: " + $idAlteracao.ToString())
    Write-Output ("Entrada1: " + $reader["Entrada1"].ToString())
    Write-Output ("Saida1: " + $reader["Saida1"].ToString())
    Write-Output ("Marcacao: " + $reader["Marcacao"].ToString())
    Write-Output ("Falta: " + $reader["Falta"].ToString())
    Write-Output ("Ausencia: " + $reader["Ausencia"].ToString())
    Write-Output ("Objectivo: " + $reader["Objectivo"].ToString())
    Write-Output ("Validado: " + $reader["Validado"].ToString())
    Write-Output ("TipoAnomalia: " + $reader["TipoAnomalia"].ToString())
    Write-Output ("Saldo: " + $reader["Saldo"].ToString())
}
$reader.Close()

Write-Output ""
Write-Output "--- dbo.AlteracoesPeriodos ---"
if ($idAlteracao -ne $null) {
    $cmd.CommandText = "SELECT Inicio, Fim FROM dbo.AlteracoesPeriodos WHERE IDAlteracao = " + $idAlteracao
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Output ($reader["Inicio"].ToString() + " - " + $reader["Fim"].ToString())
    }
    $reader.Close()
} else {
    Write-Output "No IDAlteracao found"
}

Write-Output ""
Write-Output "--- dbo.TerminaisMarcacoes ---"
$cmd.CommandText = "SELECT Hora, TipoProcessado FROM dbo.TerminaisMarcacoes WHERE IDFuncionario = 1679 AND Data = '2026-04-01'"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ($reader["Hora"].ToString() + " - Tipo: " + $reader["TipoProcessado"].ToString())
}
$reader.Close()

$conn.Close()
