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
# 1. Resolver Pastas Reais das APIs (PROD e TEST)
# ─────────────────────────────────────────────────────────────────────────────
$prodApiPath = Join-Path $ProdAppPath "api"
if (-not (Test-Path $prodApiPath)) {
    $prodApiPath = $ProdAppPath
}

$testApiPath = Join-Path $TestAppPath "api"
if (-not (Test-Path $testApiPath)) {
    $testApiPath = $TestAppPath
}

Write-Host "Pasta da API de PROD resolvida: $prodApiPath" -ForegroundColor Green
Write-Host "Pasta da API de TEST resolvida: $testApiPath" -ForegroundColor Green

# ─────────────────────────────────────────────────────────────────────────────
# 2. Resgatar as Connection Strings dos Arquivos de Configuração
# ─────────────────────────────────────────────────────────────────────────────
function Get-APIConnectionString {
    param(
        [string]$ApiPath,
        [string]$EnvName
    )

    # Arquivos de configuracao em ordem de prioridade
    $configFiles = @()
    if ($EnvName -eq "PROD") {
        $configFiles = @(
            "appsettings.Production.json",
            "appsettings.Prod.json",
            "appsettings.json"
        )
    } else {
        $configFiles = @(
            "appsettings.Test.json",
            "appsettings.Development.json",
            "appsettings.json"
        )
    }

    # Chaves de connection string em ordem de prioridade
    $keys = @(
        "DefaultConnection",
        "PortalConnection",
        "SqlConnection",
        "ApplicationDbContext"
    )

    $resolvedConnStr = $null
    $usedFile = $null
    $usedKey = $null

    foreach ($file in $configFiles) {
        $fullPath = Join-Path $ApiPath $file
        if (Test-Path $fullPath) {
            try {
                $json = Get-Content $fullPath -Raw | ConvertFrom-Json
                if ($null -ne $json.ConnectionStrings) {
                    foreach ($key in $keys) {
                        $val = $json.ConnectionStrings.$key
                        if (-not [string]::IsNullOrWhiteSpace($val)) {
                            $resolvedConnStr = $val
                            $usedFile = $file
                            $usedKey = $key
                            break
                        }
                    }
                }
            } catch {
                Write-Host ("[WARN] Erro ao processar arquivo {0}: {1}" -f $file, $_) -ForegroundColor Yellow
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($resolvedConnStr)) {
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolvedConnStr)) {
        throw "ERRO: Connection string nao encontrada nos arquivos da API $EnvName em $ApiPath."
    }

    # Mascarar a connection string para exibir no log de forma segura
    $masked = $resolvedConnStr
    if ($masked -match 'Password=([^;]+)') {
        $masked = $masked -replace 'Password=[^;]+', 'Password=********'
    }
    if ($masked -match 'pwd=([^;]+)') {
        $masked = $masked -replace 'pwd=[^;]+', 'pwd=********'
    }

    Write-Host "ConnectionString resolvida para $EnvName a partir de '$usedFile' (Chave: '$usedKey')" -ForegroundColor Green
    Write-Host "Mascarada: $masked" -ForegroundColor Green

    return $resolvedConnStr
}

$prodConnStr = Get-APIConnectionString -ApiPath $prodApiPath -EnvName "PROD"
$testConnStr = Get-APIConnectionString -ApiPath $testApiPath -EnvName "TEST"

# ─────────────────────────────────────────────────────────────────────────────
# 2.5 Obter Connection String Administrativa (GitHub Secret)
# ─────────────────────────────────────────────────────────────────────────────
$adminConnStr = $env:TEST_SQL_ADMIN_CONNECTION_STRING

if ([string]::IsNullOrWhiteSpace($adminConnStr)) {
    throw "TEST_SQL_ADMIN_CONNECTION_STRING nao definida. Configure a secret no environment test antes de executar o workflow."
}

# Mascarar a connection string administrativa para o log
$adminMasked = $adminConnStr
if ($adminMasked -match 'Password=([^;]+)') {
    $adminMasked = $adminMasked -replace 'Password=[^;]+', 'Password=********'
}
if ($adminMasked -match 'pwd=([^;]+)') {
    $adminMasked = $adminMasked -replace 'pwd=[^;]+', 'pwd=********'
}

Write-Host "Connection string administrativa encontrada via GitHub Secret TEST_SQL_ADMIN_CONNECTION_STRING." -ForegroundColor Green
Write-Host "Mascarada: $adminMasked" -ForegroundColor Green

$adminMasterBuilder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($adminConnStr)
$adminServer = $adminMasterBuilder.DataSource
$adminMasterBuilder["Initial Catalog"] = "master"
$adminMasterConnStr = $adminMasterBuilder.ConnectionString

# ─────────────────────────────────────────────────────────────────────────────
# 2.6 Validar Login e Permissões Administrativas
# ─────────────────────────────────────────────────────────────────────────────
$connAdminCheck = New-Object System.Data.SqlClient.SqlConnection($adminMasterConnStr)
try {
    $connAdminCheck.Open()
    $chkCmd = $connAdminCheck.CreateCommand()
    $chkCmd.CommandText = "SELECT SYSTEM_USER AS SysUser, IS_SRVROLEMEMBER('sysadmin') AS IsSysAdmin, IS_SRVROLEMEMBER('dbcreator') AS IsDbCreator"
    $reader = $chkCmd.ExecuteReader()
    $isSysAdmin = 0
    $isDbCreator = 0
    $sysUser = "unknown"
    if ($reader.Read()) {
        $sysUser = $reader["SysUser"]
        $isSysAdmin = [int]$reader["IsSysAdmin"]
        $isDbCreator = [int]$reader["IsDbCreator"]
    }
    $reader.Close()
    $connAdminCheck.Close()

    Write-Host ("Conectando ao banco master com usuario administrativo: {0}" -f $sysUser) -ForegroundColor Green
    Write-Host ("Permissoes: SysAdmin={0}, DbCreator={1}" -f $isSysAdmin, $isDbCreator) -ForegroundColor Green

    if ($isSysAdmin -eq 0 -and $isDbCreator -eq 0) {
        throw "VALIDACAO FALHOU: O usuario '$sysUser' nao possui a role 'sysadmin' nem 'dbcreator' no SQL Server. Restore abortado."
    }
} catch {
    throw "ERRO AO CONECTAR COM CREDENCIAIS ADMINISTRATIVAS: $_"
}

# ─────────────────────────────────────────────────────────────────────────────
# 3. Validações de Segurança de Nomes de Banco nas Connection Strings
# ─────────────────────────────────────────────────────────────────────────────
function Get-DatabaseNameFromConnectionString {
    param([string]$ConnectionString)
    if ($ConnectionString -match 'Database=([^;]+)') {
        return $Matches[1].Trim()
    }
    if ($ConnectionString -match 'Initial Catalog=([^;]+)') {
        return $Matches[1].Trim()
    }
    return $null
}

$prodDbNameVal = Get-DatabaseNameFromConnectionString -ConnectionString $prodConnStr
$testDbNameVal = Get-DatabaseNameFromConnectionString -ConnectionString $testConnStr

Write-Host "Banco de dados extraido da Connection String de PROD: [$prodDbNameVal]" -ForegroundColor Green
Write-Host "Banco de dados extraido da Connection String de TEST: [$testDbNameVal]" -ForegroundColor Green

if ($prodDbNameVal -ne "Portal-Gerencial") {
    throw "VALIDACAO FALHOU: O banco da connection string de PROD deve ser 'Portal-Gerencial'. Encontrado: '$prodDbNameVal'."
}
if ($testDbNameVal -ne "Portal-Gerencial-Test") {
    throw "VALIDACAO FALHOU: O banco da connection string de TEST deve ser 'Portal-Gerencial-Test'. Encontrado: '$testDbNameVal'."
}

# Validacoes de caminhos fisicos
if ($ProdAppPath -notmatch '\\Prod$') { throw "VALIDACAO FALHOU: ProdAppPath deve terminar com '\Prod'." }
if ($TestAppPath -notmatch '\\Test$') { throw "VALIDACAO FALHOU: TestAppPath deve terminar com '\Test'." }

# ─────────────────────────────────────────────────────────────────────────────
# 4. Resolução Dinâmica do Caminho de Armazenamento de Anexos
# ─────────────────────────────────────────────────────────────────────────────
function Resolve-AttachmentPath {
    param(
        [string]$ApiPath,
        [string]$ConfigFileName,
        [string]$ConfigKey,
        [string]$DefaultRelativePath
    )

    $configVal = $null

    # 1. Ler arquivo de configuracao específico do ambiente
    if (-not [string]::IsNullOrWhiteSpace($ConfigFileName)) {
        $specificConfig = Join-Path $ApiPath $ConfigFileName
        if (Test-Path $specificConfig) {
            $json = Get-Content $specificConfig -Raw | ConvertFrom-Json
            if ($ConfigKey -eq "AppConfig:AttachmentsStoragePath") {
                $configVal = $json.AppConfig.AttachmentsStoragePath
            }
        }
    }

    # 2. Se vazio, ler arquivo base appsettings.json
    if ([string]::IsNullOrWhiteSpace($configVal)) {
        $baseConfig = Join-Path $ApiPath "appsettings.json"
        if (Test-Path $baseConfig) {
            $json = Get-Content $baseConfig -Raw | ConvertFrom-Json
            if ($ConfigKey -eq "AppConfig:AttachmentsStoragePath") {
                $configVal = $json.AppConfig.AttachmentsStoragePath
            }
        }
    }

    # 3. Fallback para default
    if ([string]::IsNullOrWhiteSpace($configVal)) {
        $configVal = $DefaultRelativePath
    }

    # Resolver caminho absoluto ou relativo
    if ([System.IO.Path]::IsPathRooted($configVal)) {
        return [System.IO.Path]::GetFullPath($configVal)
    } else {
        # Fallback 1: IIS ContentRootPath
        $iisAttempt = [System.IO.Path]::GetFullPath((Join-Path $ApiPath $configVal))
        if (Test-Path $iisAttempt) { return $iisAttempt }

        # Fallback 2: Legacy climbing (../../..)
        $legacyAttempt = [System.IO.Path]::GetFullPath((Join-Path $ApiPath (Join-Path "..\..\.." $configVal)))
        if (Test-Path $legacyAttempt) { return $legacyAttempt }

        return $legacyAttempt
    }
}

# Determinar os nomes dos arquivos específicos de configuracao para mapear anexos
$prodConfigFileName = "appsettings.Production.json"
if (-not (Test-Path (Join-Path $prodApiPath $prodConfigFileName))) { $prodConfigFileName = "appsettings.Prod.json" }
if (-not (Test-Path (Join-Path $prodApiPath $prodConfigFileName))) { $prodConfigFileName = "appsettings.json" }

$testConfigFileName = "appsettings.Test.json"
if (-not (Test-Path (Join-Path $testApiPath $testConfigFileName))) { $testConfigFileName = "appsettings.Development.json" }
if (-not (Test-Path (Join-Path $testApiPath $testConfigFileName))) { $testConfigFileName = "appsettings.json" }

$prodAttachments = Resolve-AttachmentPath -ApiPath $prodApiPath -ConfigFileName $prodConfigFileName -ConfigKey "AppConfig:AttachmentsStoragePath" -DefaultRelativePath "data/attachments"
$testAttachments = Resolve-AttachmentPath -ApiPath $testApiPath -ConfigFileName $testConfigFileName -ConfigKey "AppConfig:AttachmentsStoragePath" -DefaultRelativePath "data/attachments"

Write-Host "Caminho de anexos resolvido para PROD: $prodAttachments" -ForegroundColor Green
Write-Host "Caminho de anexos resolvido para TEST: $testAttachments" -ForegroundColor Green

# Validações fortes dos caminhos de anexos
if ($prodAttachments -notmatch 'attachments$') {
    throw "VALIDACAO FALHOU: O caminho resolvido de anexos da PROD deve terminar com 'attachments'. Caminho: $prodAttachments"
}
if ($testAttachments -notmatch 'attachments$') {
    throw "VALIDACAO FALHOU: O caminho resolvido de anexos do TEST deve terminar com 'attachments'. Caminho: $testAttachments"
}

# Lista de caminhos proibidos para o destino de anexos
$forbiddenPaths = @(
    "C:\", "C:\Apps", "C:\Apps\AlplaPortal", "C:\Apps\AlplaPortal\Test",
    "C:\Windows", "C:\Program Files", "C:\Apps\AlplaPortal\Prod"
)
foreach ($path in $forbiddenPaths) {
    if ($testAttachments -eq $path -or $testAttachments -eq (Join-Path $path "data")) {
        throw "VALIDACAO FALHOU: O caminho de destino dos anexos nao pode ser um diretorio do sistema ou raiz: $path."
    }
}

if ($prodAttachments -eq $testAttachments) {
    # Compartilhamento de pasta confirmado!
    Write-Host "[WARN] PROD e TEST compartilham a mesma pasta de anexos fisica: $prodAttachments. Nenhuma copia de arquivos sera feita." -ForegroundColor Yellow
} else {
    if (-not (Test-Path $prodAttachments)) {
        Write-Host "[WARN] Diretorio de anexos da PROD nao existe ou esta vazio. Pulando copia de anexos." -ForegroundColor Yellow
    }
}

# ─────────────────────────────────────────────────────────────────────────────
# 5. Interrupção do IIS App Pool do TEST
# ─────────────────────────────────────────────────────────────────────────────
Import-Module WebAdministration
$candidatePools = @(
    "AlplaPortal-Test-Api-Pool",
    "PortalGerencialTestApiPool",
    "PortalGerencialTestAppPool",
    "AlplaPortalTestPool",
    "PortalGerencial-Test-Api-Pool"
)

$stoppedPools = @()

foreach ($pool in $candidatePools) {
    $poolPath = "IIS:\AppPools\$pool"
    if (Test-Path $poolPath) {
        Write-Host ("Parando o App Pool do TEST ({0})..." -f $pool) -ForegroundColor Yellow
        Stop-WebAppPool -Name $pool -ErrorAction SilentlyContinue
        $stoppedPools += $pool
    }
}

if ($stoppedPools.Count -gt 0) {
    Start-Sleep -Seconds 3
} else {
    Write-Host "[WARN] Nenhum App Pool do TEST (candidatos: AlplaPortal-Test-Api-Pool, PortalGerencialTestApiPool, etc.) foi encontrado. Continuando sem parar o IIS." -ForegroundColor Yellow
}

# ─────────────────────────────────────────────────────────────────────────────
# 6. Criar Backups dos Bancos de Dados
# ─────────────────────────────────────────────────────────────────────────────
$backupDirDb = "C:\Apps\AlplaPortal\Test\backups\db"
if (-not (Test-Path $backupDirDb)) { New-Item -ItemType Directory -Path $backupDirDb -Force | Out-Null }

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

function Get-SqlBackupClause {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$BackupName
    )

    # 1. Detectar edicao do SQL Server
    $editionCmd = $Connection.CreateCommand()
    $editionCmd.CommandText = "SELECT CAST(SERVERPROPERTY('Edition') AS NVARCHAR(200)) AS Edition"
    $edition = $editionCmd.ExecuteScalar()

    Write-Host ("Detected SQL Server Edition: {0}" -f $edition) -ForegroundColor Green

    # 2. Se a edicao contiver "Express", desabilitar compressao
    if ($edition -like "*Express*") {
        Write-Host "SQL Server Express detected. Backup compression is DISABLED." -ForegroundColor Yellow
        return "WITH FORMAT, NAME = N'{0}'" -f $BackupName
    } else {
        Write-Host "Backup compression is ENABLED." -ForegroundColor Green
        return "WITH FORMAT, COMPRESSION, NAME = N'{0}'" -f $BackupName
    }
}

# Backup do TEST Atual (Pre-Restore)
$testBackupFile = Join-Path $backupDirDb "${TestDbName}_${timestamp}_pre-sync.bak"
Write-Host "Gerando backup de seguranca do TEST em: $testBackupFile..." -ForegroundColor Yellow
$connTest = New-Object System.Data.SqlClient.SqlConnection($testConnStr)
$connTest.Open()
$testBackupClause = Get-SqlBackupClause -Connection $connTest -BackupName "Pre-Sync Backup"
$bkCmdTest = $connTest.CreateCommand()
$bkCmdTest.CommandTimeout = 300
$bkCmdTest.CommandText = "BACKUP DATABASE [$TestDbName] TO DISK = N'$testBackupFile' $testBackupClause"
$bkCmdTest.ExecuteNonQuery() | Out-Null
$connTest.Close()

# Backup da PROD Atual (Origem)
$prodBackupFile = Join-Path $backupDirDb "${ProdDbName}_${timestamp}_source.bak"
Write-Host "Gerando backup da PROD (Origem) em: $prodBackupFile..." -ForegroundColor Yellow
Write-Host "[ALERTA DE SEGURANCA] O backup '$prodBackupFile' CONTEM DADOS DE PRODUCAO. Acesso restrito obrigatorio!" -ForegroundColor Yellow
$connProd = New-Object System.Data.SqlClient.SqlConnection($prodConnStr)
$connProd.Open()
$prodBackupClause = Get-SqlBackupClause -Connection $connProd -BackupName "Source Backup"
$bkCmdProd = $connProd.CreateCommand()
$bkCmdProd.CommandTimeout = 300
$bkCmdProd.CommandText = "BACKUP DATABASE [$ProdDbName] TO DISK = N'$prodBackupFile' $prodBackupClause"
$bkCmdProd.ExecuteNonQuery() | Out-Null
$connProd.Close()

# ─────────────────────────────────────────────────────────────────────────────
# 7. Restaurar Backup da PROD sobre o TEST (Conectando ao banco MASTER)
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ("Criando conexao administrativa ao banco master no servidor: {0}" -f $adminServer) -ForegroundColor Green
Write-Host "Database: master" -ForegroundColor Green

$connRestore = New-Object System.Data.SqlClient.SqlConnection($adminMasterConnStr)
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
# 8. Neutralização e Ajustes Pós-Restore no TEST (Comandos Condicionais)
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
# 9. Sincronização de Anexos Físicos (Quando as pastas forem distintas)
# ─────────────────────────────────────────────────────────────────────────────
if ($prodAttachments -ne $testAttachments -and (Test-Path $prodAttachments)) {
    $backupDirAttachments = "C:\Apps\AlplaPortal\Test\backups\attachments"
    
    # Backup dos anexos atuais do TEST se existirem
    if (Test-Path $testAttachments) {
        $attBackupFolder = Join-Path $backupDirAttachments "attachments_${timestamp}"
        New-Item -ItemType Directory -Path $attBackupFolder -Force | Out-Null
        
        Write-Host "Criando backup dos anexos do TEST em: $attBackupFolder..." -ForegroundColor Yellow
        $backupParams = @($testAttachments, $attBackupFolder, "/E", "/NFL", "/NDL", "/NJH", "/NJS")
        & robocopy.exe @backupParams
        if ($LASTEXITCODE -ge 8) {
            throw "Robocopy falhou com exit code $LASTEXITCODE ao gerar backup de anexos do TEST."
        }
    }
    
    Write-Host "Espelhando anexos de PROD para o TEST (/MIR)..." -ForegroundColor Yellow
    $mirrorParams = @($prodAttachments, $testAttachments, "/MIR", "/COPY:DAT", "/R:2", "/W:2", "/NFL", "/NDL", "/NJH", "/NJS")
    & robocopy.exe @mirrorParams
    
    if ($LASTEXITCODE -ge 8) {
        throw "Robocopy falhou com exit code $LASTEXITCODE ao espelhar os anexos de PROD para TEST."
    }
    Write-Host "OK - Anexos espelhados com sucesso." -ForegroundColor Green
}

# ─────────────────────────────────────────────────────────────────────────────
# 10. Reiniciar o IIS App Pool do TEST
# ─────────────────────────────────────────────────────────────────────────────
foreach ($pool in $stoppedPools) {
    $poolPath = "IIS:\AppPools\$pool"
    if (Test-Path $poolPath) {
        Write-Host ("Reiniciando o App Pool do TEST ({0})..." -f $pool) -ForegroundColor Yellow
        Start-WebAppPool -Name $pool -ErrorAction SilentlyContinue
    }
}
Write-Host "Processo concluido com sucesso!" -ForegroundColor Green
