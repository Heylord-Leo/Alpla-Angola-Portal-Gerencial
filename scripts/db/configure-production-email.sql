-- =============================================================================
-- Alpla Angola - Portal Gerencial
-- Production Email Configuration Script  (v2.185.7)
-- =============================================================================
--
-- Purpose:  Copy working SMTP/email configuration from Test DB to Production DB
-- Source:   [Portal-Gerencial-Test]   (READ-ONLY — no modifications)
-- Target:   [Portal-Gerencial]        (INSERT/UPDATE only email tables)
-- Server:   AOVIA1VMS011 (SQL Server Express)
--
-- SAFETY:
--   * All sensitive values (passwords, API keys) are MASKED in output
--   * Backup is created BEFORE any changes (SQL Express — no COMPRESSION)
--   * No user accounts or passwords are modified
--   * No data outside email configuration is touched
--   * Test database is NEVER written to
--   * Script is idempotent (safe to re-run)
--
-- SCHEMA VALIDATED AGAINST AOVIA1VMS011 2026-06-03:
--   * IntegrationConnectionStatuses  (plural — EF convention)
--   * IntegrationProviderSettings    (FK: IntegrationProviderId)
--   * SmtpSettings
--
-- INSTRUCTIONS:
--   Run on AOVIA1VMS011 using SSMS or sqlcmd.
--   Execute each step in order. Review output before proceeding.
-- =============================================================================

SET NOCOUNT ON
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 0: Environment Verification
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT '============================================='
PRINT '  STEP 0: Environment Verification'
PRINT '============================================='
PRINT ''

SELECT
    SERVERPROPERTY('ServerName')     AS ServerName,
    SERVERPROPERTY('Edition')        AS Edition,
    SERVERPROPERTY('ProductVersion') AS ProductVersion
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 1: Schema Guard Checks
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 1: Schema Guard Checks'
PRINT '============================================='
PRINT ''

DECLARE @errors INT = 0

-- 1a) SmtpSettings must exist in BOTH databases
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial-Test].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'SmtpSettings'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial-Test].dbo.SmtpSettings does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial-Test].dbo.SmtpSettings exists.'

IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'SmtpSettings'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.SmtpSettings does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.SmtpSettings exists.'

-- 1b) IntegrationConnectionStatuses must exist with IntegrationProviderId
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'IntegrationConnectionStatuses'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.IntegrationConnectionStatuses does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.IntegrationConnectionStatuses exists.'

IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME   = 'IntegrationConnectionStatuses'
      AND COLUMN_NAME  = 'IntegrationProviderId'
)
BEGIN
    PRINT '[FAIL] Column IntegrationConnectionStatuses.IntegrationProviderId does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   IntegrationConnectionStatuses.IntegrationProviderId exists.'

-- 1c) IntegrationProviderSettings must exist with IntegrationProviderId
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'IntegrationProviderSettings'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.IntegrationProviderSettings does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.IntegrationProviderSettings exists.'

IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND TABLE_NAME   = 'IntegrationProviderSettings'
      AND COLUMN_NAME  = 'IntegrationProviderId'
)
BEGIN
    PRINT '[FAIL] Column IntegrationProviderSettings.IntegrationProviderId does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   IntegrationProviderSettings.IntegrationProviderId exists.'

-- 1d) IntegrationProviders must exist
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'IntegrationProviders'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.IntegrationProviders does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.IntegrationProviders exists.'

-- Abort if any guard failed
IF @errors > 0
BEGIN
    PRINT ''
    PRINT '*** ABORTING: ' + CAST(@errors AS VARCHAR) + ' schema guard check(s) failed. Fix the schema before re-running. ***'
    RETURN
END

PRINT ''
PRINT 'All schema guard checks passed.'
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 2: Diagnostic — Compare SmtpSettings (Test vs Production)
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 2: SmtpSettings Comparison'
PRINT '============================================='

PRINT ''
PRINT '--- Test Database (source) ---'
SELECT
    Id,
    [Server],
    Port,
    SenderEmail,
    SenderName,
    EnableSsl,
    CASE
        WHEN EncryptedPassword IS NOT NULL AND LEN(EncryptedPassword) > 0
        THEN '***ENCRYPTED(' + CAST(LEN(EncryptedPassword) AS VARCHAR) + ' chars)***'
        ELSE '(empty)'
    END AS EncryptedPassword_Status,
    CreatedAtUtc,
    UpdatedAtUtc
FROM [Portal-Gerencial-Test].dbo.SmtpSettings

PRINT ''
PRINT '--- Production Database (target — before changes) ---'
SELECT
    Id,
    [Server],
    Port,
    SenderEmail,
    SenderName,
    EnableSsl,
    CASE
        WHEN EncryptedPassword IS NOT NULL AND LEN(EncryptedPassword) > 0
        THEN '***ENCRYPTED(' + CAST(LEN(EncryptedPassword) AS VARCHAR) + ' chars)***'
        ELSE '(empty)'
    END AS EncryptedPassword_Status,
    CreatedAtUtc,
    UpdatedAtUtc
FROM [Portal-Gerencial].dbo.SmtpSettings
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 3: Diagnostic — Compare IntegrationProviders (SMTP row)
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 3: IntegrationProviders SMTP Comparison'
PRINT '============================================='

PRINT ''
PRINT '--- Test ---'
SELECT Id, Code, Name, IsEnabled, Environment, ConnectionType
FROM [Portal-Gerencial-Test].dbo.IntegrationProviders
WHERE Code = 'SMTP'

PRINT ''
PRINT '--- Production ---'
SELECT Id, Code, Name, IsEnabled, Environment, ConnectionType
FROM [Portal-Gerencial].dbo.IntegrationProviders
WHERE Code = 'SMTP'
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 4: Diagnostic — Compare IntegrationConnectionStatuses (SMTP)
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 4: IntegrationConnectionStatuses SMTP'
PRINT '============================================='

PRINT ''
PRINT '--- Test ---'
SELECT
    ics.Id,
    ics.IntegrationProviderId,
    ics.CurrentStatus,
    ics.LastSuccessUtc,
    ics.LastFailureUtc,
    ics.LastResponseTimeMs,
    ics.ConsecutiveFailures,
    ics.LastTestedByEmail,
    ics.LastCheckedAtUtc
FROM [Portal-Gerencial-Test].dbo.IntegrationConnectionStatuses ics
INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationProviders ip
    ON ip.Id = ics.IntegrationProviderId
WHERE ip.Code = 'SMTP'

PRINT ''
PRINT '--- Production ---'
SELECT
    ics.Id,
    ics.IntegrationProviderId,
    ics.CurrentStatus,
    ics.LastSuccessUtc,
    ics.LastFailureUtc,
    ics.LastResponseTimeMs,
    ics.ConsecutiveFailures,
    ics.LastTestedByEmail,
    ics.LastCheckedAtUtc
FROM [Portal-Gerencial].dbo.IntegrationConnectionStatuses ics
INNER JOIN [Portal-Gerencial].dbo.IntegrationProviders ip
    ON ip.Id = ics.IntegrationProviderId
WHERE ip.Code = 'SMTP'
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 5: Diagnostic — Compare IntegrationProviderSettings (SMTP)
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 5: IntegrationProviderSettings SMTP'
PRINT '============================================='

PRINT ''
PRINT '--- Test ---'
SELECT
    ips.Id,
    ips.IntegrationProviderId,
    ips.[Server],
    ips.DatabaseName,
    ips.AuthenticationMode,
    ips.Username,
    CASE
        WHEN ips.EncryptedPassword IS NOT NULL AND LEN(ips.EncryptedPassword) > 0
        THEN '***MASKED***' ELSE '(empty)'
    END AS EncryptedPassword_Status,
    ips.ApiBaseUrl,
    CASE
        WHEN ips.ApiKeyEncrypted IS NOT NULL AND LEN(ips.ApiKeyEncrypted) > 0
        THEN '***MASKED***' ELSE '(empty)'
    END AS ApiKey_Status,
    ips.TimeoutSeconds,
    ips.AdditionalConfig,
    ips.IsReadOnly,
    ips.SecretVersion
FROM [Portal-Gerencial-Test].dbo.IntegrationProviderSettings ips
INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationProviders ip
    ON ip.Id = ips.IntegrationProviderId
WHERE ip.Code = 'SMTP'

PRINT ''
PRINT '--- Production ---'
SELECT
    ips.Id,
    ips.IntegrationProviderId,
    ips.[Server],
    ips.DatabaseName,
    ips.AuthenticationMode,
    ips.Username,
    CASE
        WHEN ips.EncryptedPassword IS NOT NULL AND LEN(ips.EncryptedPassword) > 0
        THEN '***MASKED***' ELSE '(empty)'
    END AS EncryptedPassword_Status,
    ips.ApiBaseUrl,
    CASE
        WHEN ips.ApiKeyEncrypted IS NOT NULL AND LEN(ips.ApiKeyEncrypted) > 0
        THEN '***MASKED***' ELSE '(empty)'
    END AS ApiKey_Status,
    ips.TimeoutSeconds,
    ips.AdditionalConfig,
    ips.IsReadOnly,
    ips.SecretVersion
FROM [Portal-Gerencial].dbo.IntegrationProviderSettings ips
INNER JOIN [Portal-Gerencial].dbo.IntegrationProviders ip
    ON ip.Id = ips.IntegrationProviderId
WHERE ip.Code = 'SMTP'
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 6: Production Database Backup (SQL Express — NO COMPRESSION)
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 6: Production Database Backup'
PRINT '============================================='

-- Prerequisite: ensure backup directory exists on the server.
-- PowerShell: New-Item -ItemType Directory -Force -Path "C:\Apps\AlplaPortal\Prod\backups\db"

DECLARE @backupPath NVARCHAR(500)
DECLARE @timestamp  NVARCHAR(20)
SET @timestamp  = REPLACE(REPLACE(REPLACE(
                      CONVERT(NVARCHAR(20), GETDATE(), 120), '-', ''), ':', ''), ' ', '_')
SET @backupPath = 'C:\Apps\AlplaPortal\Prod\backups\db\Portal-Gerencial_before_email_config_'
                  + @timestamp + '.bak'

PRINT 'Creating backup at: ' + @backupPath

BACKUP DATABASE [Portal-Gerencial]
TO DISK = @backupPath
WITH FORMAT, NAME = 'Pre-Email-Config Backup'

PRINT 'Backup completed successfully.'
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 7: Copy SmtpSettings from Test to Production
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 7: Copy SmtpSettings to Production'
PRINT '============================================='

IF NOT EXISTS (SELECT 1 FROM [Portal-Gerencial].dbo.SmtpSettings)
BEGIN
    PRINT 'Production has NO SmtpSettings rows. Inserting from Test...'

    SET IDENTITY_INSERT [Portal-Gerencial].dbo.SmtpSettings ON

    INSERT INTO [Portal-Gerencial].dbo.SmtpSettings
        (Id, [Server], Port, SenderEmail, SenderName, EnableSsl, EncryptedPassword,
         CreatedAtUtc, UpdatedAtUtc)
    SELECT
        Id, [Server], Port, SenderEmail, SenderName, EnableSsl, EncryptedPassword,
        GETUTCDATE(), GETUTCDATE()
    FROM [Portal-Gerencial-Test].dbo.SmtpSettings

    SET IDENTITY_INSERT [Portal-Gerencial].dbo.SmtpSettings OFF

    PRINT 'SmtpSettings inserted from Test.'
END
ELSE
BEGIN
    PRINT 'Production already has SmtpSettings. Updating from Test...'

    UPDATE prod
    SET
        prod.[Server]            = src.[Server],
        prod.Port                = src.Port,
        prod.SenderEmail         = src.SenderEmail,
        prod.SenderName          = src.SenderName,
        prod.EnableSsl           = src.EnableSsl,
        prod.EncryptedPassword   = src.EncryptedPassword,
        prod.UpdatedAtUtc        = GETUTCDATE()
    FROM [Portal-Gerencial].dbo.SmtpSettings prod
    CROSS JOIN (
        SELECT TOP 1 * FROM [Portal-Gerencial-Test].dbo.SmtpSettings ORDER BY Id DESC
    ) src
    WHERE prod.Id = (
        SELECT TOP 1 Id FROM [Portal-Gerencial].dbo.SmtpSettings ORDER BY Id DESC
    )

    PRINT 'SmtpSettings updated from Test.'
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 8: Update IntegrationConnectionStatuses for SMTP provider
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 8: Update SMTP IntegrationConnectionStatuses'
PRINT '============================================='

UPDATE prod_ics
SET
    prod_ics.CurrentStatus      = test_ics.CurrentStatus,
    prod_ics.LastSuccessUtc     = test_ics.LastSuccessUtc,
    prod_ics.LastFailureUtc     = test_ics.LastFailureUtc,
    prod_ics.LastResponseTimeMs = test_ics.LastResponseTimeMs,
    prod_ics.LastErrorMessage   = test_ics.LastErrorMessage,
    prod_ics.ConsecutiveFailures = test_ics.ConsecutiveFailures,
    prod_ics.LastTestedByEmail  = test_ics.LastTestedByEmail,
    prod_ics.LastCheckedAtUtc   = test_ics.LastCheckedAtUtc
FROM [Portal-Gerencial].dbo.IntegrationConnectionStatuses prod_ics
INNER JOIN [Portal-Gerencial].dbo.IntegrationProviders prod_ip
    ON prod_ip.Id = prod_ics.IntegrationProviderId
INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationProviders test_ip
    ON test_ip.Code = 'SMTP'
INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationConnectionStatuses test_ics
    ON test_ics.IntegrationProviderId = test_ip.Id
WHERE prod_ip.Code = 'SMTP'

PRINT 'IntegrationConnectionStatuses SMTP row updated.'
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 9: Copy IntegrationProviderSettings for SMTP (if present in Test)
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 9: Copy SMTP IntegrationProviderSettings'
PRINT '============================================='

DECLARE @testSmtpId INT
DECLARE @prodSmtpId INT

SELECT @testSmtpId = Id
FROM [Portal-Gerencial-Test].dbo.IntegrationProviders
WHERE Code = 'SMTP'

SELECT @prodSmtpId = Id
FROM [Portal-Gerencial].dbo.IntegrationProviders
WHERE Code = 'SMTP'

IF @testSmtpId IS NOT NULL
   AND EXISTS (
       SELECT 1
       FROM [Portal-Gerencial-Test].dbo.IntegrationProviderSettings
       WHERE IntegrationProviderId = @testSmtpId
   )
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM [Portal-Gerencial].dbo.IntegrationProviderSettings
        WHERE IntegrationProviderId = @prodSmtpId
    )
    BEGIN
        PRINT 'Inserting SMTP IntegrationProviderSettings from Test...'

        INSERT INTO [Portal-Gerencial].dbo.IntegrationProviderSettings
            (IntegrationProviderId, [Server], DatabaseName, InstanceName,
             AuthenticationMode, Username, EncryptedPassword,
             ApiBaseUrl, ApiKeyEncrypted, TimeoutSeconds,
             AdditionalConfig, IsReadOnly, SecretVersion,
             CreatedAtUtc, UpdatedAtUtc)
        SELECT
            @prodSmtpId, [Server], DatabaseName, InstanceName,
            AuthenticationMode, Username, EncryptedPassword,
            ApiBaseUrl, ApiKeyEncrypted, TimeoutSeconds,
            AdditionalConfig, IsReadOnly, SecretVersion,
            GETUTCDATE(), GETUTCDATE()
        FROM [Portal-Gerencial-Test].dbo.IntegrationProviderSettings
        WHERE IntegrationProviderId = @testSmtpId

        PRINT 'IntegrationProviderSettings SMTP inserted.'
    END
    ELSE
    BEGIN
        PRINT 'Updating existing SMTP IntegrationProviderSettings...'

        UPDATE prod_ips
        SET
            prod_ips.[Server]            = test_ips.[Server],
            prod_ips.DatabaseName        = test_ips.DatabaseName,
            prod_ips.InstanceName        = test_ips.InstanceName,
            prod_ips.AuthenticationMode  = test_ips.AuthenticationMode,
            prod_ips.Username            = test_ips.Username,
            prod_ips.EncryptedPassword   = test_ips.EncryptedPassword,
            prod_ips.ApiBaseUrl          = test_ips.ApiBaseUrl,
            prod_ips.ApiKeyEncrypted     = test_ips.ApiKeyEncrypted,
            prod_ips.TimeoutSeconds      = test_ips.TimeoutSeconds,
            prod_ips.AdditionalConfig    = test_ips.AdditionalConfig,
            prod_ips.SecretVersion       = test_ips.SecretVersion,
            prod_ips.UpdatedAtUtc        = GETUTCDATE()
        FROM [Portal-Gerencial].dbo.IntegrationProviderSettings prod_ips
        INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationProviderSettings test_ips
            ON test_ips.IntegrationProviderId = @testSmtpId
        WHERE prod_ips.IntegrationProviderId = @prodSmtpId

        PRINT 'IntegrationProviderSettings SMTP updated.'
    END
END
ELSE
BEGIN
    PRINT 'No SMTP IntegrationProviderSettings found in Test. Skipping.'
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════
-- STEP 10: Final Validation
-- ═══════════════════════════════════════════════════════════════════════════════
PRINT ''
PRINT '============================================='
PRINT '  STEP 10: Final Validation'
PRINT '============================================='

-- 10a) SmtpSettings row count and non-sensitive fields
PRINT ''
PRINT '--- Production SmtpSettings (counts and status) ---'
SELECT
    COUNT(*)                                                        AS RowCount,
    MAX([Server])                                                   AS SmtpServer,
    MAX(Port)                                                       AS SmtpPort,
    MAX(SenderEmail)                                                AS SenderEmail,
    MAX(SenderName)                                                 AS SenderName,
    MAX(CAST(EnableSsl AS INT))                                     AS EnableSsl,
    CASE
        WHEN MAX(LEN(EncryptedPassword)) > 0 THEN 'YES'
        ELSE 'NO — PASSWORD MISSING'
    END                                                             AS HasEncryptedPassword
FROM [Portal-Gerencial].dbo.SmtpSettings

-- 10b) SMTP IntegrationConnectionStatuses
PRINT ''
PRINT '--- Production SMTP Integration Status ---'
SELECT
    ip.Code,
    ip.Name,
    ip.IsEnabled,
    ics.CurrentStatus,
    ics.LastSuccessUtc,
    ics.LastCheckedAtUtc,
    ics.ConsecutiveFailures
FROM [Portal-Gerencial].dbo.IntegrationProviders ip
LEFT JOIN [Portal-Gerencial].dbo.IntegrationConnectionStatuses ics
    ON ics.IntegrationProviderId = ip.Id
WHERE ip.Code = 'SMTP'

-- 10c) SMTP IntegrationProviderSettings (count only, no secrets)
PRINT ''
PRINT '--- Production SMTP Provider Settings (count) ---'
SELECT
    COUNT(*) AS SettingsRowCount
FROM [Portal-Gerencial].dbo.IntegrationProviderSettings ips
INNER JOIN [Portal-Gerencial].dbo.IntegrationProviders ip
    ON ip.Id = ips.IntegrationProviderId
WHERE ip.Code = 'SMTP'

-- 10d) Safety: no Test-environment references leaked into Production
PRINT ''
IF EXISTS (
    SELECT 1 FROM [Portal-Gerencial].dbo.SmtpSettings
    WHERE [Server] LIKE '%test%'
       OR SenderEmail LIKE '%test%'
       OR SenderName LIKE '%test%'
)
    PRINT '[WARN] Test-related values detected in Production SmtpSettings!'
ELSE
    PRINT '[OK]   No Test references in Production SmtpSettings.'

-- 10e) Verify Test database was NOT modified
PRINT ''
PRINT '--- Test SmtpSettings (unchanged verification) ---'
SELECT
    COUNT(*) AS TestSmtpRowCount,
    MAX(UpdatedAtUtc) AS TestLastUpdatedUtc
FROM [Portal-Gerencial-Test].dbo.SmtpSettings

PRINT ''
PRINT '============================================='
PRINT '  Configuration Complete'
PRINT '============================================='
PRINT ''
PRINT 'NEXT STEPS:'
PRINT '  1. Open Production Portal > Administracao > Integracoes'
PRINT '  2. Click "Testar Conexao" on the Email / SMTP Service provider'
PRINT '  3. If test passes, trigger a password reset for leonardo.cintra@alpla.com'
PRINT '  4. Check inbox for the reset email'
PRINT ''
GO
