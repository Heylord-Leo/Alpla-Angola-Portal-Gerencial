param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,

    [Parameter(Mandatory = $true)]
    [string]$ProdDbName,

    [Parameter(Mandatory = $true)]
    [string]$TestDbName,

    [Parameter(Mandatory = $true)]
    [string]$ProdAppPath,

    [Parameter(Mandatory = $true)]
    [string]$TestAppPath
)

$ErrorActionPreference = "Stop"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Sync PROD Data to TEST - AOVIA1VMS011" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

# ─────────────────────────────────────────────────────────────────────────────
# 1. Validações de Segurança de Caminhos (Assertions Fortes)
# ─────────────────────────────────────────────────────────────────────────────
if ($TestDbName -ne "Portal-Gerencial-Test") {
    throw "ERRO DE SEGURANCA: O banco de destino deve ser estritamente 'Portal-Gerencial-Test'."
}
if ($ProdDbName -ne "Portal-Gerencial") {
    throw "ERRO DE SEGURANCA: O banco de origem deve ser estritamente 'Portal-Gerencial'."
}

# Validar terminacoes dos caminhos de aplicacao
if ($ProdAppPath -notmatch '\\Prod$') { throw "VALIDACAO FALHOU: ProdAppPath deve terminar com '\Prod'." }
if ($TestAppPath -notmatch '\\Test$') { throw "VALIDACAO FALHOU: TestAppPath deve terminar com '\Test'." }

$prodAttachments = Join-Path $ProdAppPath "data\attachments"
$testAttachments = Join-Path $TestAppPath "data\attachments"

# Validar terminacoes dos caminhos de anexos
if ($prodAttachments -notmatch '\\Prod\\data\\attachments$') { throw "VALIDACAO FALHOU: prodAttachments deve terminar com '\Prod\data\attachments'." }
if ($testAttachments -notmatch '\\Test\\data\\attachments$') { throw "VALIDACAO FALHOU: testAttachments deve terminar com '\Test\data\attachments'." }

if ($prodAttachments -eq $testAttachments) { throw "VALIDACAO FALHOU: Diretorios de anexos de PROD e TEST nao podem ser iguais." }

# Lista de caminhos proibidos para o destino de anexos (proteger pastas criticas do sistema)
$forbiddenPaths = @(
    "C:\", "C:\Apps", "C:\Apps\AlplaPortal", "C:\Apps\AlplaPortal\Test",
    "C:\Windows", "C:\Program Files"
)
foreach ($path in $forbiddenPaths) {
    if ($testAttachments -eq $path -or (Join-Path $TestAppPath "data") -eq $path) {
        throw "VALIDACAO FALHOU: O caminho de destino dos anexos nao pode ser um diretorio do sistema ou raiz: $path."
    }
}

if (-not (Test-Path $prodAttachments)) {
    throw "VALIDACAO FALHOU: Diretorio de anexos do PROD nao encontrado em: $prodAttachments"
}

if (-not (Test-Path $testAttachments)) {
    Write-Host "Criando diretorio de anexos do TEST: $testAttachments" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $testAttachments -Force | Out-Null
}

# ─────────────────────────────────────────────────────────────────────────────
# 2. Resgatar as Connection Strings dos Arquivos de Configuração
# ─────────────────────────────────────────────────────────────────────────────
$prodConfig = Join-Path $ProdAppPath "appsettings.json"
$testConfig = Join-Path $TestAppPath "appsettings.Test.json"
if (-not (Test-Path $testConfig)) {
    $testConfig = Join-Path $TestAppPath "appsettings.json"
}

if (-not (Test-Path $prodConfig)) { throw "Configuracao de Producao nao encontrada em $prodConfig" }
if (-not (Test-Path $testConfig)) { throw "Configuracao de Teste nao encontrada em $testConfig" }

$prodJson = Get-Content $prodConfig -Raw | ConvertFrom-Json
$testJson = Get-Content $testConfig -Raw | ConvertFrom-Json

$prodConnStr = $prodJson.ConnectionStrings.DefaultConnection
$testConnStr = $testJson.ConnectionStrings.DefaultConnection

if ([string]::IsNullOrWhiteSpace($prodConnStr)) { throw "ConnectionString de PROD vazia." }
if ([string]::IsNullOrWhiteSpace($testConnStr)) { throw "ConnectionString de TEST vazia." }

# ─────────────────────────────────────────────────────────────────────────────
# 3. Interrupção do IIS App Pool do TEST
# ─────────────────────────────────────────────────────────────────────────────
Import-Module WebAdministration
$poolName = "AlplaPortalTestPool"
$poolPath = "IIS:\AppPools\$poolName"

if (Test-Path $poolPath) {
    Write-Host "Parando o App Pool do TEST ($poolName)..." -ForegroundColor Yellow
    Stop-WebAppPool -Name $poolName -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
} else {
    Write-Host "[WARN] App Pool '$poolName' nao encontrado no IIS. Continuando sem parar pool." -ForegroundColor Yellow
}

# ─────────────────────────────────────────────────────────────────────────────
# 4. Criar Backups dos Bancos de Dados
# ─────────────────────────────────────────────────────────────────────────────
$backupDirDb = "C:\Apps\AlplaPortal\Test\backups\db"
if (-not (Test-Path $backupDirDb)) { New-Item -ItemType Directory -Path $backupDirDb -Force | Out-Null }

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

# Backup do TEST Atual (Pre-Restore)
$testBackupFile = Join-Path $backupDirDb "${TestDbName}_${timestamp}_pre-sync.bak"
Write-Host "Gerando backup de seguranca do TEST em: $testBackupFile..." -ForegroundColor Yellow
$connTest = New-Object System.Data.SqlClient.SqlConnection($testConnStr)
$connTest.Open()
$bkCmdTest = $connTest.CreateCommand()
$bkCmdTest.CommandTimeout = 300
$bkCmdTest.CommandText = "BACKUP DATABASE [$TestDbName] TO DISK = N'$testBackupFile' WITH FORMAT, COMPRESSION, NAME = N'Pre-Sync Backup'"
$bkCmdTest.ExecuteNonQuery() | Out-Null
$connTest.Close()

# Backup da PROD Atual (Origem)
$prodBackupFile = Join-Path $backupDirDb "${ProdDbName}_${timestamp}_source.bak"
Write-Host "Gerando backup da PROD (Origem) em: $prodBackupFile..." -ForegroundColor Yellow
Write-Host "[ALERTA DE SEGURANCA] O backup '$prodBackupFile' CONTEM DADOS DE PRODUCAO. Acesso restrito obrigatorio!" -ForegroundColor Yellow
$connProd = New-Object System.Data.SqlClient.SqlConnection($prodConnStr)
$connProd.Open()
$bkCmdProd = $connProd.CreateCommand()
$bkCmdProd.CommandTimeout = 300
$bkCmdProd.CommandText = "BACKUP DATABASE [$ProdDbName] TO DISK = N'$prodBackupFile' WITH FORMAT, COMPRESSION, NAME = N'Source Backup'"
$bkCmdProd.ExecuteNonQuery() | Out-Null
$connProd.Close()

# ─────────────────────────────────────────────────────────────────────────────
# 5. Restaurar Backup da PROD sobre o TEST (Conectando ao banco MASTER)
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "Criando conexao administrativa ao banco MASTER..." -ForegroundColor Yellow
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($testConnStr)
$builder.InitialCatalog = "master"
$masterConnStr = $builder.ConnectionString

$connRestore = New-Object System.Data.SqlClient.SqlConnection($masterConnStr)
$connRestore.Open()

# Script SQL Dinamico de Restore
$restoreSql = @"
DECLARE @BackupFile NVARCHAR(500) = N'$prodBackupFile';
DECLARE @LogicalData NVARCHAR(128), @LogicalLog NVARCHAR(128);
DECLARE @PhysicalData NVARCHAR(500), @PhysicalLog NVARCHAR(500);

-- Resgatar caminhos fisicos reais do banco TEST
SELECT @PhysicalData = physical_name FROM sys.master_files WHERE database_id = DB_ID('Portal-Gerencial-Test') AND type = 0;
SELECT @PhysicalLog = physical_name FROM sys.master_files WHERE database_id = DB_ID('Portal-Gerencial-Test') AND type = 1;

CREATE TABLE #FileList (
    LogicalName NVARCHAR(128), PhysicalName NVARCHAR(500), Type CHAR(1), FileGroupName NVARCHAR(128), Size NUMERIC(20,0),
    MaxSize NUMERIC(20,0), FileId BIGINT, CreateLSN NUMERIC(25,0), DropLSN NUMERIC(25,0), UniqueId UNIQUEIDENTIFIER,
    ReadOnlyLSN NUMERIC(25,0), ReadWriteLSN NUMERIC(25,0), BackupSizeInBytes BIGINT, SourceBlockSize INT, FileGroupId INT,
    LogGroupGUID UNIQUEIDENTIFIER, DifferentialBaseLSN NUMERIC(25,0), DifferentialBaseGUID UNIQUEIDENTIFIER, IsReadOnly BIT,
    IsPresent BIT, TDEThumbprint VARBINARY(32), SnapshotUrl NVARCHAR(360)
);

INSERT INTO #FileList EXEC('RESTORE FILELISTONLY FROM DISK = ''' + @BackupFile + '''');

SELECT @LogicalData = LogicalName FROM #FileList WHERE Type = 'D';
SELECT @LogicalLog = LogicalName FROM #FileList WHERE Type = 'L';

-- Desconectar usuarios do TEST (Comando executado a partir do master)
ALTER DATABASE [Portal-Gerencial-Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

-- Restore com substituicao e mapeamento correto de caminhos fisicos
DECLARE @RestoreCommand NVARCHAR(MAX);
SET @RestoreCommand = N'RESTORE DATABASE [Portal-Gerencial-Test] FROM DISK = ''' + @BackupFile + ''' WITH REPLACE, ' +
                      N'MOVE ''' + @LogicalData + ''' TO ''' + @PhysicalData + ''', ' +
                      N'MOVE ''' + @LogicalLog + ''' TO ''' + @PhysicalLog + ''';';
EXEC sp_executesql @RestoreCommand;

ALTER DATABASE [Portal-Gerencial-Test] SET MULTI_USER;
DROP TABLE #FileList;
"@

Write-Host "Executando restore a partir do master..." -ForegroundColor Yellow
$rstCmd = $connRestore.CreateCommand()
$rstCmd.CommandTimeout = 600
$rstCmd.CommandText = $restoreSql
$rstCmd.ExecuteNonQuery() | Out-Null
$connRestore.Close()
Write-Host "OK - Banco de dados restaurado com sucesso." -ForegroundColor Green

# ─────────────────────────────────────────────────────────────────────────────
# 6. Neutralização e Ajustes Pós-Restore no TEST (Comandos Condicionais)
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "Executando comandos de neutralizacao pos-restore no TEST..." -ForegroundColor Yellow
$connAdjust = New-Object System.Data.SqlClient.SqlConnection($testConnStr)
$connAdjust.Open()

$adjustSql = @"
-- 1. Cancelar e-mails pendentes/enviados da PROD na tabela de Outbox (Se existir)
IF OBJECT_ID('EmailOutbox', 'U') IS NOT NULL
BEGIN
    UPDATE EmailOutbox 
    SET Status = 'DEAD_LETTER', 
        LastError = 'Cancelled during PROD->TEST DB sync script' 
    WHERE Status IN ('PENDING', 'PROCESSING', 'FAILED');
    PRINT 'Tabela EmailOutbox neutralizada.';
END
ELSE
BEGIN
    PRINT 'Tabela EmailOutbox nao encontrada.';
END

-- 2. Forcar redirecionamento de e-mails para caixa segura no TEST (Se existir)
IF OBJECT_ID('SmtpSettings', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM SmtpSettings)
    BEGIN
        UPDATE SmtpSettings 
        SET AllowRealRecipientsInNonProduction = 0, 
            RedirectAllToTestRecipient = 1, 
            TestRecipientEmail = 'test-alerts@alpla.com',
            EnableSubjectPrefix = 1,
            SubjectPrefixText = '[TEST - IGNORE]',
            EnableBodyWarningBanner = 1,
            WarningBannerText = 'AMBIENTE DE TESTE - FAVOR IGNORAR ESTE E-MAIL',
            UpdatedAtUtc = GETUTCDATE();
        PRINT 'Configuracoes de SmtpSettings ajustadas para ambiente TEST.';
    END
END
ELSE
BEGIN
    PRINT 'Tabela SmtpSettings nao encontrada.';
END

-- 3. Desativar integracoes com sistemas externos no TEST (Evitar chamadas a PROD)
IF OBJECT_ID('IntegrationProviderSettings', 'U') IS NOT NULL
BEGIN
    UPDATE IntegrationProviderSettings SET [Value] = 'false' WHERE [Key] LIKE '%Enabled%';
    PRINT 'Integracoes externas inativadas em IntegrationProviderSettings.';
END
ELSE
BEGIN
    PRINT 'Tabela IntegrationProviderSettings nao encontrada.';
END
"@

$adjCmd = $connAdjust.CreateCommand()
$adjCmd.CommandText = $adjustSql
$adjCmd.ExecuteNonQuery() | Out-Null
$connAdjust.Close()
Write-Host "OK - Ajustes de seguranca aplicados." -ForegroundColor Green

# ─────────────────────────────────────────────────────────────────────────────
# 7. Backup e Cópia com Espelhamento (/MIR) de Anexos Físicos
# ─────────────────────────────────────────────────────────────────────────────
$backupDirAttachments = "C:\Apps\AlplaPortal\Test\backups\attachments"

if (Test-Path $testAttachments) {
    $attBackupFolder = Join-Path $backupDirAttachments "attachments_${timestamp}"
    New-Item -ItemType Directory -Path $attBackupFolder -Force | Out-Null
    
    Write-Host "Criando backup dos anexos do TEST em: $attBackupFolder..." -ForegroundColor Yellow
    
    # Robocopy para backup
    $backupParams = @($testAttachments, $attBackupFolder, "/E", "/NFL", "/NDL", "/NJH", "/NJS")
    & robocopy.exe @backupParams
    if ($LASTEXITCODE -ge 8) {
        throw "Robocopy falhou com exit code $LASTEXITCODE ao gerar backup de anexos do TEST."
    }
}

Write-Host "Espelhando anexos de PROD para o TEST (/MIR)..." -ForegroundColor Yellow
# /MIR: Garante espelhamento exato, eliminando arquivos orfaos no TEST que nao existem mais no banco PROD
$mirrorParams = @($prodAttachments, $testAttachments, "/MIR", "/COPY:DAT", "/R:2", "/W:2", "/NFL", "/NDL", "/NJH", "/NJS")
& robocopy.exe @mirrorParams

if ($LASTEXITCODE -ge 8) {
    throw "Robocopy falhou com exit code $LASTEXITCODE ao espelhar os anexos de PROD para TEST."
}
Write-Host "OK - Anexos espelhados com sucesso." -ForegroundColor Green

# ─────────────────────────────────────────────────────────────────────────────
# 8. Reiniciar o IIS App Pool do TEST
# ─────────────────────────────────────────────────────────────────────────────
if (Test-Path $poolPath) {
    Write-Host "Reiniciando o App Pool do TEST ($poolName)..." -ForegroundColor Yellow
    Start-WebAppPool -Name $poolName -ErrorAction SilentlyContinue
}
Write-Host "Processo concluido com sucesso!" -ForegroundColor Green
