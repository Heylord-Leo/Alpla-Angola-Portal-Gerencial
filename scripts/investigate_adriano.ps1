$connString = "Server=AOVIA1VMS012\SQLINNUX;Database=Innux;User Id=sa;Password=ad#56&Hfe;TrustServerCertificate=True"
$conn = New-Object System.Data.SqlClient.SqlConnection($connString)
$conn.Open()

# ───────────────────────────────────────────────
# Step 1: Find the Innux employee ID
# ───────────────────────────────────────────────
Write-Output "=== STEP 1: Employee Lookup ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT IDFuncionario, Nome, Numero FROM dbo.Funcionarios WHERE Nome LIKE '%ADRIANO%SILVA%'"
$reader = $cmd.ExecuteReader()
$empId = $null
while ($reader.Read()) {
    $empId = $reader["IDFuncionario"]
    Write-Output ("IDFuncionario: " + $empId.ToString() + " | Nome: " + $reader["Nome"].ToString() + " | Numero: " + $reader["Numero"].ToString())
}
$reader.Close()

if ($empId -eq $null) {
    Write-Output "Employee not found. Aborting."
    $conn.Close()
    exit
}

Write-Output ""

# ───────────────────────────────────────────────
# Step 2: dbo.Alteracoes — full row for 2026-04-11
# ───────────────────────────────────────────────
Write-Output "=== STEP 2: dbo.Alteracoes (daily summary) ==="
$cmd.CommandText = "SELECT IDAlteracao, IDFuncionario, IDHorario, Data, Entrada1, Saida1, Marcacao, Falta, Ausencia, Objectivo, Saldo, Validado, FaltouPeriodosObrigatorios, TipoAnomalia, Justificacao FROM dbo.Alteracoes WHERE IDFuncionario = $empId AND Data = '2026-04-11'"
$reader = $cmd.ExecuteReader()
$idAlteracao = $null
$idHorario = $null
while ($reader.Read()) {
    $idAlteracao = $reader["IDAlteracao"]
    $idHorario = $reader["IDHorario"]
    Write-Output ("IDAlteracao: " + $idAlteracao.ToString())
    Write-Output ("IDHorario: " + $idHorario.ToString())
    Write-Output ("Data: " + $reader["Data"].ToString())
    Write-Output ("Entrada1: " + $reader["Entrada1"].ToString())
    Write-Output ("Saida1: " + $reader["Saida1"].ToString())
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
# Step 3: dbo.AlteracoesPeriodos — periods for this day
# ───────────────────────────────────────────────
Write-Output "=== STEP 3: dbo.AlteracoesPeriodos (period breakdown) ==="
if ($idAlteracao -ne $null) {
    $cmd.CommandText = @"
SELECT ap.Inicio, ap.Fim, ap.Dispensa,
       ct.Codigo AS WorkCode, ct.Descricao AS WorkDesc,
       ca.Codigo AS AbsCode, ca.Descricao AS AbsDesc,
       ap.IDCodigoTrabalho, ap.IDCodigoAusencia
FROM dbo.AlteracoesPeriodos ap
LEFT JOIN dbo.CodigosTrabalho ct ON ap.IDCodigoTrabalho = ct.IDCodigoTrabalho
LEFT JOIN dbo.CodigosAusencia ca ON ap.IDCodigoAusencia = ca.IDCodigoAusencia
WHERE ap.IDAlteracao = $idAlteracao
ORDER BY ap.Inicio
"@
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Output ($reader["Inicio"].ToString() + " - " + $reader["Fim"].ToString() + " | Work: [" + $reader["WorkCode"].ToString() + "] " + $reader["WorkDesc"].ToString() + " | Abs: [" + $reader["AbsCode"].ToString() + "] " + $reader["AbsDesc"].ToString() + " | Dispensa: " + $reader["Dispensa"].ToString())
    }
    $reader.Close()
} else {
    Write-Output "No IDAlteracao found"
}

Write-Output ""

# ───────────────────────────────────────────────
# Step 4: dbo.TerminaisMarcacoes — raw punches
# ───────────────────────────────────────────────
Write-Output "=== STEP 4: dbo.TerminaisMarcacoes (raw punches) ==="
$cmd.CommandText = @"
SELECT tm.Hora, tm.TipoProcessado, tm.Gerada, tm.Data, tm.IDTerminal,
       t.Nome AS TerminalName
FROM dbo.TerminaisMarcacoes tm
LEFT JOIN dbo.Terminais t ON tm.IDTerminal = t.IDTerminal
WHERE tm.IDFuncionario = $empId AND tm.Data = '2026-04-11'
ORDER BY tm.Hora
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("Hora: " + $reader["Hora"].ToString() + " | TipoProcessado: [" + $reader["TipoProcessado"].ToString() + "] | Gerada: " + $reader["Gerada"].ToString() + " | Terminal: " + $reader["TerminalName"].ToString())
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# Step 5: Inspect TipoProcessado values across all data
# to understand what codes exist (EN/SA/17/18 etc.)
# ───────────────────────────────────────────────
Write-Output "=== STEP 5: All distinct TipoProcessado values in TerminaisMarcacoes ==="
$cmd.CommandText = "SELECT DISTINCT TipoProcessado, COUNT(*) AS Cnt FROM dbo.TerminaisMarcacoes GROUP BY TipoProcessado ORDER BY Cnt DESC"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("TipoProcessado: [" + $reader["TipoProcessado"].ToString() + "] | Count: " + $reader["Cnt"].ToString())
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# Step 6: Schedule/Horario details for this employee on this day
# ───────────────────────────────────────────────
Write-Output "=== STEP 6: dbo.Horarios (schedule for IDHorario=$idHorario) ==="
if ($idHorario -ne $null) {
    $cmd.CommandText = "SELECT IDHorario, Codigo, Descricao, Sigla, DiaFolga FROM dbo.Horarios WHERE IDHorario = $idHorario"
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Output ("IDHorario: " + $reader["IDHorario"].ToString())
        Write-Output ("Codigo: " + $reader["Codigo"].ToString())
        Write-Output ("Descricao: " + $reader["Descricao"].ToString())
        Write-Output ("Sigla: " + $reader["Sigla"].ToString())
        Write-Output ("DiaFolga: " + $reader["DiaFolga"].ToString())
    }
    $reader.Close()

    Write-Output ""
    Write-Output "=== STEP 6b: dbo.HorariosPeriodos (schedule periods) ==="
    $cmd.CommandText = "SELECT Tipo, Inicio, Fim FROM dbo.HorariosPeriodos WHERE IDHorario = $idHorario ORDER BY Inicio"
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) {
        Write-Output ("Tipo: " + $reader["Tipo"].ToString() + " | " + $reader["Inicio"].ToString() + " - " + $reader["Fim"].ToString())
    }
    $reader.Close()
} else {
    Write-Output "No IDHorario found"
}

Write-Output ""

# ───────────────────────────────────────────────
# Step 7: Check if there is a TipoMarcacao / direction reference table
# ───────────────────────────────────────────────
Write-Output "=== STEP 7: Looking for direction/type reference tables ==="
$cmd.CommandText = @"
SELECT TABLE_NAME, COLUMN_NAME 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE (COLUMN_NAME LIKE '%TipoMarcacao%' OR COLUMN_NAME LIKE '%Direccao%' OR COLUMN_NAME LIKE '%Direcao%' OR COLUMN_NAME LIKE '%Direction%' OR COLUMN_NAME LIKE '%TipoProcessado%')
  AND TABLE_SCHEMA = 'dbo'
ORDER BY TABLE_NAME
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ($reader["TABLE_NAME"].ToString() + "." + $reader["COLUMN_NAME"].ToString())
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# Step 8: Check TerminaisMarcacoes schema for additional columns we may not be reading
# ───────────────────────────────────────────────
Write-Output "=== STEP 8: Full schema of TerminaisMarcacoes ==="
$cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'TerminaisMarcacoes' AND TABLE_SCHEMA = 'dbo' ORDER BY ORDINAL_POSITION"
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    $maxLen = $reader["CHARACTER_MAXIMUM_LENGTH"].ToString()
    Write-Output ($reader["COLUMN_NAME"].ToString() + " | " + $reader["DATA_TYPE"].ToString() + $(if ($maxLen -ne '') { " ($maxLen)" } else { "" }))
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# Step 9: Check this employee's nearby days (surrounding context)
# ───────────────────────────────────────────────
Write-Output "=== STEP 9: dbo.Alteracoes context (2026-04-06 to 2026-04-13) ==="
$cmd.CommandText = @"
SELECT a.Data, a.Entrada1, a.Saida1, a.Marcacao, a.Falta, a.Ausencia, a.Objectivo, a.Validado, a.TipoAnomalia,
       h.Descricao AS ScheduleDesc, h.DiaFolga
FROM dbo.Alteracoes a
LEFT JOIN dbo.Horarios h ON a.IDHorario = h.IDHorario
WHERE a.IDFuncionario = $empId AND a.Data >= '2026-04-06' AND a.Data <= '2026-04-13'
ORDER BY a.Data
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    $dataStr = ([datetime]$reader["Data"]).ToString("yyyy-MM-dd (dddd)")
    Write-Output ($dataStr + " | E1: " + $reader["Entrada1"].ToString() + " | S1: " + $reader["Saida1"].ToString() + " | Marc: " + $reader["Marcacao"].ToString() + " | Falta: " + $reader["Falta"].ToString() + " | Aus: " + $reader["Ausencia"].ToString() + " | Obj: " + $reader["Objectivo"].ToString() + " | Val: " + $reader["Validado"].ToString() + " | Anomalia: [" + $reader["TipoAnomalia"].ToString() + "] | Sched: " + $reader["ScheduleDesc"].ToString() + " | DiaFolga: " + $reader["DiaFolga"].ToString())
}
$reader.Close()

Write-Output ""

# ───────────────────────────────────────────────
# Step 10: Check if TipoProcessado has a lookup table
# ───────────────────────────────────────────────
Write-Output "=== STEP 10: Search for any 'Tipo' reference tables ==="
$cmd.CommandText = @"
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_SCHEMA = 'dbo' 
  AND (TABLE_NAME LIKE '%TipoMarcac%' OR TABLE_NAME LIKE '%TiposMovimento%' OR TABLE_NAME LIKE '%Direcco%' OR TABLE_NAME LIKE '%MovimentosTipo%')
ORDER BY TABLE_NAME
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output $reader["TABLE_NAME"].ToString()
}
$reader.Close()

# Also check TerminaisMarcacoes for the raw Tipo column vs TipoProcessado
Write-Output ""
Write-Output "=== STEP 10b: TerminaisMarcacoes raw vs processed for this punch ==="
$cmd.CommandText = @"
SELECT tm.Hora, tm.TipoProcessado, tm.Tipo, tm.Gerada, tm.Anulada, tm.IDTerminal, tm.IDFuncionario
FROM dbo.TerminaisMarcacoes tm
WHERE tm.IDFuncionario = $empId AND tm.Data = '2026-04-11'
ORDER BY tm.Hora
"@
$reader = $cmd.ExecuteReader()
while ($reader.Read()) {
    Write-Output ("Hora: " + $reader["Hora"].ToString() + " | TipoProcessado: [" + $reader["TipoProcessado"].ToString() + "] | Tipo: [" + $reader["Tipo"].ToString() + "] | Gerada: " + $reader["Gerada"].ToString() + " | Anulada: " + $reader["Anulada"].ToString())
}
$reader.Close()

$conn.Close()
Write-Output ""
Write-Output "=== Investigation complete ==="
