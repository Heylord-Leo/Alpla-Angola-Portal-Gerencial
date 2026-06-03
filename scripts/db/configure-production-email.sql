-- =============================================================================
-- Alpla Angola - Portal Gerencial
-- Production Email Configuration Script
-- =============================================================================
--
-- Purpose:  Copy working SMTP/email configuration from Test DB to Production DB
-- Source:   [Portal-Gerencial-Test]
-- Target:   [Portal-Gerencial]
-- Server:   AOVIA1VMS011 (SQL Server Express)
--
-- SAFETY:
-- - All sensitive values (passwords, API keys) are masked in SELECT output
-- - Backup is created BEFORE any changes
-- - No user accounts or passwords are modified
-- - No data outside email configuration is touched
-- - Script is idempotent (safe to re-run)
--
-- INSTRUCTIONS:
-- Run this script on AOVIA1VMS011 using SSMS or sqlcmd.
-- Execute each section in order. Review output before proceeding.
-- =============================================================================

-- ─── STEP 0: Verify we are on the correct server ───
PRINT '============================================='
PRINT '  STEP 0: Environment Verification'
PRINT '============================================='

SELECT 
    SERVERPROPERTY('ServerName') AS ServerName,
    SERVERPROPERTY('Edition') AS Edition,
    SERVERPROPERTY('ProductVersion') AS ProductVersion
GO

-- ─── STEP 1: Diagnostic — Compare SmtpSettings between Test and Production ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 1: SmtpSettings Comparison'
PRINT '============================================='

PRINT ''
PRINT '--- Test Database SmtpSettings (source) ---'
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
    END AS EncryptedPassword_Masked,
    CreatedAtUtc,
    UpdatedAtUtc
FROM [Portal-Gerencial-Test].dbo.SmtpSettings

PRINT ''
PRINT '--- Production Database SmtpSettings (target — before changes) ---'
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
    END AS EncryptedPassword_Masked,
    CreatedAtUtc,
    UpdatedAtUtc
FROM [Portal-Gerencial].dbo.SmtpSettings
GO

-- ─── STEP 2: Diagnostic — Compare IntegrationProviders (SMTP provider) ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 2: IntegrationProviders SMTP Comparison'
PRINT '============================================='

PRINT ''
PRINT '--- Test SMTP Provider ---'
SELECT Id, Code, Name, IsEnabled, Environment, ConnectionType
FROM [Portal-Gerencial-Test].dbo.IntegrationProviders
WHERE Code = 'SMTP'

PRINT ''
PRINT '--- Production SMTP Provider ---'
SELECT Id, Code, Name, IsEnabled, Environment, ConnectionType
FROM [Portal-Gerencial].dbo.IntegrationProviders
WHERE Code = 'SMTP'
GO

-- ─── STEP 3: Diagnostic — Compare IntegrationConnectionStatus (SMTP) ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 3: IntegrationConnectionStatus SMTP'
PRINT '============================================='

PRINT ''
PRINT '--- Test SMTP Connection Status ---'
SELECT ics.Id, ics.IntegrationProviderId, ics.CurrentStatus, ics.LastTestedAtUtc, ics.LastSuccessAtUtc
FROM [Portal-Gerencial-Test].dbo.IntegrationConnectionStatus ics
INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationProviders ip ON ip.Id = ics.IntegrationProviderId
WHERE ip.Code = 'SMTP'

PRINT ''
PRINT '--- Production SMTP Connection Status ---'
SELECT ics.Id, ics.IntegrationProviderId, ics.CurrentStatus, ics.LastTestedAtUtc, ics.LastSuccessAtUtc
FROM [Portal-Gerencial].dbo.IntegrationConnectionStatus ics
INNER JOIN [Portal-Gerencial].dbo.IntegrationProviders ip ON ip.Id = ics.IntegrationProviderId
WHERE ip.Code = 'SMTP'
GO

-- ─── STEP 4: Diagnostic — Compare IntegrationProviderSettings (SMTP) ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 4: IntegrationProviderSettings SMTP'
PRINT '============================================='

PRINT ''
PRINT '--- Test SMTP Provider Settings ---'
SELECT 
    ips.Id, 
    ips.IntegrationProviderId,
    ips.[Server],
    ips.DatabaseName,
    ips.AuthenticationMode,
    ips.Username,
    CASE 
        WHEN ips.EncryptedPassword IS NOT NULL AND LEN(ips.EncryptedPassword) > 0 
        THEN '***MASKED***'
        ELSE '(empty)'
    END AS EncryptedPassword_Masked,
    ips.ApiBaseUrl,
    CASE 
        WHEN ips.ApiKeyEncrypted IS NOT NULL AND LEN(ips.ApiKeyEncrypted) > 0 
        THEN '***MASKED***'
        ELSE '(empty)'
    END AS ApiKey_Masked,
    ips.TimeoutSeconds,
    ips.AdditionalConfig,
    ips.IsReadOnly,
    ips.SecretVersion
FROM [Portal-Gerencial-Test].dbo.IntegrationProviderSettings ips
INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationProviders ip ON ip.Id = ips.IntegrationProviderId
WHERE ip.Code = 'SMTP'

PRINT ''
PRINT '--- Production SMTP Provider Settings ---'
SELECT 
    ips.Id, 
    ips.IntegrationProviderId,
    ips.[Server],
    ips.DatabaseName,
    ips.AuthenticationMode,
    ips.Username,
    CASE 
        WHEN ips.EncryptedPassword IS NOT NULL AND LEN(ips.EncryptedPassword) > 0 
        THEN '***MASKED***'
        ELSE '(empty)'
    END AS EncryptedPassword_Masked,
    ips.ApiBaseUrl,
    CASE 
        WHEN ips.ApiKeyEncrypted IS NOT NULL AND LEN(ips.ApiKeyEncrypted) > 0 
        THEN '***MASKED***'
        ELSE '(empty)'
    END AS ApiKey_Masked,
    ips.TimeoutSeconds,
    ips.AdditionalConfig,
    ips.IsReadOnly,
    ips.SecretVersion
FROM [Portal-Gerencial].dbo.IntegrationProviderSettings ips
INNER JOIN [Portal-Gerencial].dbo.IntegrationProviders ip ON ip.Id = ips.IntegrationProviderId
WHERE ip.Code = 'SMTP'
GO

-- ─── STEP 5: Create Production backup (SQL Express — NO COMPRESSION) ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 5: Production Database Backup'
PRINT '============================================='

-- Ensure backup directory exists (run in PowerShell first if needed):
-- New-Item -ItemType Directory -Force -Path "C:\Apps\AlplaPortal\Prod\backups\db"

DECLARE @backupPath NVARCHAR(500)
DECLARE @timestamp NVARCHAR(20)
SET @timestamp = REPLACE(REPLACE(REPLACE(CONVERT(NVARCHAR(20), GETDATE(), 120), '-', ''), ':', ''), ' ', '_')
SET @backupPath = 'C:\Apps\AlplaPortal\Prod\backups\db\Portal-Gerencial_before_email_config_' + @timestamp + '.bak'

PRINT 'Creating backup at: ' + @backupPath

BACKUP DATABASE [Portal-Gerencial] 
TO DISK = @backupPath
WITH FORMAT, NAME = 'Pre-Email-Config Backup'

PRINT 'Backup completed successfully.'
GO

-- ─── STEP 6: Copy SmtpSettings from Test to Production ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 6: Copy SmtpSettings to Production'
PRINT '============================================='

-- Check if Production already has SmtpSettings
IF NOT EXISTS (SELECT 1 FROM [Portal-Gerencial].dbo.SmtpSettings)
BEGIN
    PRINT 'Production has NO SmtpSettings. Copying from Test...'
    
    SET IDENTITY_INSERT [Portal-Gerencial].dbo.SmtpSettings ON
    
    INSERT INTO [Portal-Gerencial].dbo.SmtpSettings 
        (Id, [Server], Port, SenderEmail, SenderName, EnableSsl, EncryptedPassword, CreatedAtUtc, UpdatedAtUtc)
    SELECT 
        Id, [Server], Port, SenderEmail, SenderName, EnableSsl, EncryptedPassword, GETUTCDATE(), GETUTCDATE()
    FROM [Portal-Gerencial-Test].dbo.SmtpSettings
    
    SET IDENTITY_INSERT [Portal-Gerencial].dbo.SmtpSettings OFF
    
    PRINT 'SmtpSettings copied successfully.'
END
ELSE
BEGIN
    PRINT 'Production already has SmtpSettings. Updating from Test...'
    
    UPDATE prod
    SET 
        prod.[Server] = test.[Server],
        prod.Port = test.Port,
        prod.SenderEmail = test.SenderEmail,
        prod.SenderName = test.SenderName,
        prod.EnableSsl = test.EnableSsl,
        prod.EncryptedPassword = test.EncryptedPassword,
        prod.UpdatedAtUtc = GETUTCDATE()
    FROM [Portal-Gerencial].dbo.SmtpSettings prod
    CROSS JOIN (
        SELECT TOP 1 * FROM [Portal-Gerencial-Test].dbo.SmtpSettings ORDER BY Id DESC
    ) test
    WHERE prod.Id = (SELECT TOP 1 Id FROM [Portal-Gerencial].dbo.SmtpSettings ORDER BY Id DESC)
    
    PRINT 'SmtpSettings updated successfully.'
END
GO

-- ─── STEP 7: Update IntegrationConnectionStatus for SMTP provider ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 7: Update SMTP Integration Status'
PRINT '============================================='

-- Copy the connection status from Test (typically "Connected" if SMTP is working)
UPDATE prod_ics
SET 
    prod_ics.CurrentStatus = test_ics.CurrentStatus,
    prod_ics.LastTestedAtUtc = test_ics.LastTestedAtUtc,
    prod_ics.LastSuccessAtUtc = test_ics.LastSuccessAtUtc
FROM [Portal-Gerencial].dbo.IntegrationConnectionStatus prod_ics
INNER JOIN [Portal-Gerencial].dbo.IntegrationProviders prod_ip ON prod_ip.Id = prod_ics.IntegrationProviderId
INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationConnectionStatus test_ics ON 1=1
INNER JOIN [Portal-Gerencial-Test].dbo.IntegrationProviders test_ip ON test_ip.Id = test_ics.IntegrationProviderId
WHERE prod_ip.Code = 'SMTP' AND test_ip.Code = 'SMTP'

PRINT 'SMTP IntegrationConnectionStatus updated.'
GO

-- ─── STEP 8: Copy IntegrationProviderSettings for SMTP (if exists in Test) ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 8: Copy SMTP IntegrationProviderSettings'
PRINT '============================================='

DECLARE @testSmtpProviderId INT
DECLARE @prodSmtpProviderId INT

SELECT @testSmtpProviderId = Id FROM [Portal-Gerencial-Test].dbo.IntegrationProviders WHERE Code = 'SMTP'
SELECT @prodSmtpProviderId = Id FROM [Portal-Gerencial].dbo.IntegrationProviders WHERE Code = 'SMTP'

IF @testSmtpProviderId IS NOT NULL AND EXISTS (
    SELECT 1 FROM [Portal-Gerencial-Test].dbo.IntegrationProviderSettings 
    WHERE IntegrationProviderId = @testSmtpProviderId
)
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM [Portal-Gerencial].dbo.IntegrationProviderSettings 
        WHERE IntegrationProviderId = @prodSmtpProviderId
    )
    BEGIN
        PRINT 'Copying SMTP IntegrationProviderSettings from Test to Production...'
        
        INSERT INTO [Portal-Gerencial].dbo.IntegrationProviderSettings
            (IntegrationProviderId, [Server], DatabaseName, InstanceName, AuthenticationMode, 
             Username, EncryptedPassword, ApiBaseUrl, ApiKeyEncrypted, TimeoutSeconds, 
             AdditionalConfig, IsReadOnly, SecretVersion, CreatedAtUtc, UpdatedAtUtc)
        SELECT 
            @prodSmtpProviderId, [Server], DatabaseName, InstanceName, AuthenticationMode,
            Username, EncryptedPassword, ApiBaseUrl, ApiKeyEncrypted, TimeoutSeconds,
            AdditionalConfig, IsReadOnly, SecretVersion, GETUTCDATE(), GETUTCDATE()
        FROM [Portal-Gerencial-Test].dbo.IntegrationProviderSettings
        WHERE IntegrationProviderId = @testSmtpProviderId
        
        PRINT 'SMTP IntegrationProviderSettings copied.'
    END
    ELSE
    BEGIN
        PRINT 'Production already has SMTP IntegrationProviderSettings. Updating...'
        
        UPDATE prod_ips
        SET 
            prod_ips.[Server] = test_ips.[Server],
            prod_ips.DatabaseName = test_ips.DatabaseName,
            prod_ips.InstanceName = test_ips.InstanceName,
            prod_ips.AuthenticationMode = test_ips.AuthenticationMode,
            prod_ips.Username = test_ips.Username,
            prod_ips.EncryptedPassword = test_ips.EncryptedPassword,
            prod_ips.ApiBaseUrl = test_ips.ApiBaseUrl,
            prod_ips.ApiKeyEncrypted = test_ips.ApiKeyEncrypted,
            prod_ips.TimeoutSeconds = test_ips.TimeoutSeconds,
            prod_ips.AdditionalConfig = test_ips.AdditionalConfig,
            prod_ips.SecretVersion = test_ips.SecretVersion,
            prod_ips.UpdatedAtUtc = GETUTCDATE()
        FROM [Portal-Gerencial].dbo.IntegrationProviderSettings prod_ips
        CROSS JOIN [Portal-Gerencial-Test].dbo.IntegrationProviderSettings test_ips
        WHERE prod_ips.IntegrationProviderId = @prodSmtpProviderId
          AND test_ips.IntegrationProviderId = @testSmtpProviderId
        
        PRINT 'SMTP IntegrationProviderSettings updated.'
    END
END
ELSE
BEGIN
    PRINT 'No SMTP IntegrationProviderSettings found in Test. Skipping.'
END
GO

-- ─── STEP 9: Validation — Verify Production email config ───
PRINT ''
PRINT '============================================='
PRINT '  STEP 9: Post-Configuration Validation'
PRINT '============================================='

PRINT ''
PRINT '--- Production SmtpSettings (after changes) ---'
SELECT 
    Id,
    [Server],
    Port,
    SenderEmail,
    SenderName,
    EnableSsl,
    CASE 
        WHEN EncryptedPassword IS NOT NULL AND LEN(EncryptedPassword) > 0 
        THEN 'YES (' + CAST(LEN(EncryptedPassword) AS VARCHAR) + ' chars)'
        ELSE 'NO — PASSWORD MISSING'
    END AS HasPassword,
    CreatedAtUtc,
    UpdatedAtUtc
FROM [Portal-Gerencial].dbo.SmtpSettings

PRINT ''
PRINT '--- Validation: No Test URLs in Production SmtpSettings ---'
IF EXISTS (
    SELECT 1 FROM [Portal-Gerencial].dbo.SmtpSettings 
    WHERE [Server] LIKE '%test%' 
       OR SenderEmail LIKE '%test%'
       OR SenderName LIKE '%test%'
)
BEGIN
    PRINT '*** WARNING: Test-related values found in Production SmtpSettings! ***'
    SELECT 'FAIL' AS ValidationResult, 'Test references found in SmtpSettings' AS Detail
END
ELSE
BEGIN
    PRINT 'PASS: No Test URLs found in Production SmtpSettings.'
END

PRINT ''
PRINT '--- Validation: No Portal-Gerencial-Test references in Production ---'
IF EXISTS (
    SELECT 1 FROM [Portal-Gerencial].dbo.IntegrationProviderSettings ips
    INNER JOIN [Portal-Gerencial].dbo.IntegrationProviders ip ON ip.Id = ips.IntegrationProviderId
    WHERE ip.Code = 'SMTP'
      AND (ips.[Server] LIKE '%Portal-Gerencial-Test%' 
        OR ips.DatabaseName LIKE '%Portal-Gerencial-Test%'
        OR ips.ApiBaseUrl LIKE '%test%')
)
BEGIN
    PRINT '*** WARNING: Portal-Gerencial-Test references found in Production! ***'
END
ELSE
BEGIN
    PRINT 'PASS: No Portal-Gerencial-Test references in SMTP settings.'
END

PRINT ''
PRINT '--- Production SMTP Integration Status ---'
SELECT 
    ip.Code,
    ip.Name,
    ip.IsEnabled,
    ics.CurrentStatus,
    ics.LastTestedAtUtc,
    ics.LastSuccessAtUtc
FROM [Portal-Gerencial].dbo.IntegrationProviders ip
LEFT JOIN [Portal-Gerencial].dbo.IntegrationConnectionStatus ics ON ics.IntegrationProviderId = ip.Id
WHERE ip.Code = 'SMTP'

PRINT ''
PRINT '============================================='
PRINT '  Configuration Complete'
PRINT '============================================='
PRINT ''
PRINT 'NEXT STEPS:'
PRINT '  1. Go to Production Portal > Administracao > Integracoes'
PRINT '  2. Click "Testar Conexao" on the SMTP provider'
PRINT '  3. If test passes, trigger a password reset for leonardo.cintra@alpla.com'
PRINT '  4. Check inbox for the reset email'
PRINT ''
PRINT 'NOTE: The SMTP password is AES-encrypted with the same EncryptionKey'
PRINT 'used in appsettings.Production.json. Both Test and Production must share'
PRINT 'the same AppConfig:EncryptionKey for the encrypted password to be'
PRINT 'decryptable in both environments.'
GO
