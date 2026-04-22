$connString = "Server=AOVIA1VMS012\SQLINNUX;Database=Innux;User Id=sa;Password=ad#56&Hfe;TrustServerCertificate=True"
$conn = New-Object System.Data.SqlClient.SqlConnection($connString)
$conn.Open()
$empId = 1595

# ───────────────────────────────────────────────
# A: Check 2026-04-10 (Friday) — the REAL problematic day
# ───────────────────────────────────────────────
Write-Output "=== A: dbo.Alteracoes for 2026-04-10 (Friday) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT IDAlteracao, IDHorario, Data, Entrada1, Saida1, Marcacao, Falta, Ausencia, Objectivo, Saldo, Validado, FaltouPeriodosObrigatorios, TipoAnomalia, Justificacao FROM dbo.Alteracoes WHERE IDFuncionario = $empId AND Data = '2026-04-10'"
$reader = $cmd.ExecuteReader()
$idAlteracao10 = $null
$idHorario10 = $null
while ($reader.Read()) {
    $idAlteracao10 = $reader["IDAlteracao"]
    $idHorario10 = $reader["IDHorario"]
    Write-Output ("IDAlteracao: " + $idAlteracao10.ToString())
    Write-Output ("IDHorario: " + $idHorario10.ToString())
    Write-Output ("Entrada1: [" + $reader["Entrada1"].ToString() + "]")
    Write-Output ("Saida1: [" + $reader["Saida1"].ToString() + "]")
    Write-Output ("Marcacao: " + $reader["Marcacao"].ToString())
    Write-Output ("Falta: " + $reader["Falta"].ToString())
    Write-Output ("Ausencia: " + $reader["Ausencia"].ToString())
    Write-Output ("Objectivo: " + $reader["Objectivo"].ToString())
    Write-Output ("Saldo: " + $reader["Saldo"].ToString())
    Write-Output ("Validado: " + $reader["Validado"].ToString())
    Write-Output ("FaltouPeriodosObrigatorios: " + $reader["FaltouPeriodosObrigatorios"].ToString())
    Write-Output ("TipoAnomalia: [" + $reader["TipoAnomalia"].ToString() + "]")
    Write-Output ("Justificacao: [" + $reader["Justificacao"].ToString() + "]")
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# B: Periods for 2026-04-10
# ───────────────────────────────────────────────
Write-Output "=== B: dbo.AlteracoesPeriodos for 2026-04-10 ==="
if ($idAlteracao10 -ne $null) {
    $cmd.CommandText = @"
SELECT ap.Inicio, ap.Fim, ap.Dispensa,
       ct.Codigo AS WorkCode, ct.Descricao AS WorkDesc,
       ca.Codigo AS AbsCode, ca.Descricao AS AbsDesc
FROM dbo.AlteracoesPeriodos ap
LEFT JOIN dbo.CodigosTrabalho ct ON ap.IDCodigoTrabalho = ct.IDCodigoTrabalho
LEFT JOIN dbo.CodigosAusencia ca ON ap.IDCodigoAusencia = ca.IDCodigoAusencia
WHERE ap.IDAlteracao = $idAlteracao10
ORDER BY ap.Inicio
"@
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Output ($reader["Inicio"].ToString() + " - " + $reader["Fim"].ToString() + " | Work: [" + $reader["WorkCode"].ToString() + "] " + $reader["WorkDesc"].ToString() + " | Abs: [" + $reader["AbsCode"].ToString() + "] " + $reader["AbsDesc"].ToString() + " | Dispensa: " + $reader["Dispensa"].ToString())
    }
    $reader.Close()
} else {
    Write-Output "No IDAlteracao found for 2026-04-10"
}

Write-Output ""

# ───────────────────────────────────────────────
# C: Raw punches for 2026-04-10 AND 2026-04-11
# ───────────────────────────────────────────────
Write-Output "=== C: TerminaisMarcacoes for 2026-04-10 ==="
$cmd.CommandText = @"
SELECT tm.Hora, tm.TipoProcessado, tm.Tipo, tm.Gerada, tm.IDTerminal,
       t.Nome AS TerminalName, tm.TipodeMarcacaoNoTerminal
FROM dbo.TerminaisMarcacoes tm
LEFT JOIN dbo.Terminais t ON tm.IDTerminal = t.IDTerminal
WHERE tm.IDFuncionario = $empId AND tm.Data = '2026-04-10'
ORDER BY tm.Hora
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("Hora: " + $reader["Hora"].ToString() + " | TipoProcessado: [" + $reader["TipoProcessado"].ToString() + "] | Tipo: [" + $reader["Tipo"].ToString() + "] | Gerada: " + $reader["Gerada"].ToString() + " | Terminal: " + $reader["TerminalName"].ToString() + " | TipoNoTerminal: [" + $reader["TipodeMarcacaoNoTerminal"].ToString() + "]")
}
$reader.Close()

Write-Output ""
Write-Output "=== C2: TerminaisMarcacoes for 2026-04-11 ==="
$cmd.CommandText = @"
SELECT tm.Hora, tm.TipoProcessado, tm.Tipo, tm.Gerada, tm.IDTerminal,
       t.Nome AS TerminalName, tm.TipodeMarcacaoNoTerminal
FROM dbo.TerminaisMarcacoes tm
LEFT JOIN dbo.Terminais t ON tm.IDTerminal = t.IDTerminal
WHERE tm.IDFuncionario = $empId AND tm.Data = '2026-04-11'
ORDER BY tm.Hora
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("Hora: " + $reader["Hora"].ToString() + " | TipoProcessado: [" + $reader["TipoProcessado"].ToString() + "] | Tipo: [" + $reader["Tipo"].ToString() + "] | Gerada: " + $reader["Gerada"].ToString() + " | Terminal: " + $reader["TerminalName"].ToString() + " | TipoNoTerminal: [" + $reader["TipodeMarcacaoNoTerminal"].ToString() + "]")
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# D: Schedule for IDHorario for Friday (2026-04-10)
# ───────────────────────────────────────────────
Write-Output "=== D: Schedule details for Friday IDHorario=$idHorario10 ==="
if ($idHorario10 -ne $null) {
    $cmd.CommandText = "SELECT IDHorario, Codigo, Descricao, Sigla, DiaFolga FROM dbo.Horarios WHERE IDHorario = $idHorario10"
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Output ("IDHorario: " + $reader["IDHorario"].ToString() + " | Codigo: " + $reader["Codigo"].ToString() + " | Descricao: " + $reader["Descricao"].ToString() + " | Sigla: " + $reader["Sigla"].ToString() + " | DiaFolga: " + $reader["DiaFolga"].ToString())
    }
    $reader.Close()

    Write-Output ""
    Write-Output "=== D2: HorariosPeriodos ==="
    $cmd.CommandText = "SELECT Tipo, Inicio, Fim FROM dbo.HorariosPeriodos WHERE IDHorario = $idHorario10 ORDER BY Inicio"
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Output ("Tipo: " + $reader["Tipo"].ToString() + " | " + $reader["Inicio"].ToString() + " - " + $reader["Fim"].ToString())
    }
    $reader.Close()
}

Write-Output ""

# ───────────────────────────────────────────────
# E: Understand TipoProcessado 17 and 18 — check cross-reference
# ───────────────────────────────────────────────
Write-Output "=== E: Sample punches with TipoProcessado = 17 or 18 ==="
$cmd.CommandText = @"
SELECT TOP 20 tm.IDFuncionario, tm.Data, tm.Hora, tm.TipoProcessado, tm.Tipo, tm.TipodeMarcacaoNoTerminal, tm.Gerada,
       t.Nome AS TerminalName
FROM dbo.TerminaisMarcacoes tm
LEFT JOIN dbo.Terminais t ON tm.IDTerminal = t.IDTerminal
WHERE tm.TipoProcessado IN ('17', '18')
ORDER BY tm.Data DESC
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    $dataStr = ([datetime]$reader["Data"]).ToString("yyyy-MM-dd")
    Write-Output ("Emp: " + $reader["IDFuncionario"].ToString() + " | " + $dataStr + " " + $reader["Hora"].ToString() + " | Processed: [" + $reader["TipoProcessado"].ToString() + "] | Raw: [" + $reader["Tipo"].ToString() + "] | TerminalType: [" + $reader["TipodeMarcacaoNoTerminal"].ToString() + "] | Gerada: " + $reader["Gerada"].ToString() + " | Terminal: " + $reader["TerminalName"].ToString())
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# F: Check all distinct Tipo (raw) values vs TipoProcessado
# ───────────────────────────────────────────────
Write-Output "=== F: Cross-tab of Tipo vs TipoProcessado ==="
$cmd.CommandText = "SELECT Tipo, TipoProcessado, COUNT(*) AS Cnt FROM dbo.TerminaisMarcacoes GROUP BY Tipo, TipoProcessado ORDER BY Cnt DESC"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("Tipo: [" + $reader["Tipo"].ToString() + "] -> TipoProcessado: [" + $reader["TipoProcessado"].ToString() + "] | Count: " + $reader["Cnt"].ToString())
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# G: Check if there are any TipodeMarcacaoNoTerminal distinct values
# ───────────────────────────────────────────────
Write-Output "=== G: Distinct TipodeMarcacaoNoTerminal values ==="
$cmd.CommandText = "SELECT DISTINCT TipodeMarcacaoNoTerminal, COUNT(*) AS Cnt FROM dbo.TerminaisMarcacoes WHERE TipodeMarcacaoNoTerminal IS NOT NULL AND TipodeMarcacaoNoTerminal <> '' GROUP BY TipodeMarcacaoNoTerminal ORDER BY Cnt DESC"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("TipodeMarcacaoNoTerminal: [" + $reader["TipodeMarcacaoNoTerminal"].ToString() + "] | Count: " + $reader["Cnt"].ToString())
}
$reader.Close()

$conn.Close()
Write-Output ""
Write-Output "=== Investigation 2 complete ==="
