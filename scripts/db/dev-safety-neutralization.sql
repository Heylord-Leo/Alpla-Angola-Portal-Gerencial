-- =============================================================================
-- Alpla Angola - Portal Gerencial
-- Development Safety Neutralization (PROD -> Local Development clone)
-- =============================================================================
--
-- Executed by scripts/db/import-prod-data-dev.ps1 immediately after restoring a
-- Production backup into the local Portal-Gerencial-Dev-ProdClone database, and
-- BEFORE the import script is allowed to report success.
--
-- Purpose: neutralize everything that could cause the restored data to act like
-- Production once the application starts against it (sending real emails,
-- calling real external integrations, or exposing live password-reset tokens).
--
-- Defensive style: every table AND every column referenced below is guarded
-- with OBJECT_ID / COL_LENGTH existence checks, and every conditional UPDATE is
-- built and executed as dynamic SQL (sp_executesql). This is deliberately more
-- defensive than the existing scripts/db/sync-prod-data-test.ps1 inline SQL,
-- which only guards at the table level (safe today because every table it
-- touches always has every column it references) -- this script must also
-- tolerate a FUTURE or PAST schema where an individual column may be missing,
-- without the whole batch failing to compile.
--
-- This script does NOT:
--   - touch any table other than the ones explicitly listed below;
--   - pseudonymize Users, Suppliers, Requests, or any other transactional data;
--   - drop, rename, or alter any table or column;
--   - assume every table/column below exists -- each is checked independently.
--
-- On completion, this script re-queries every neutralized area and RAISES AN
-- ERROR (THROW) if any of them do not verify clean, so a calling script's
-- try/catch will see failure and must NOT report success. This is the
-- "fail closed" requirement: if this script cannot prove Production could not
-- act like Production, it does not exit quietly.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @sql NVARCHAR(MAX);
DECLARE @verificationFailures TABLE (Area NVARCHAR(100), Detail NVARCHAR(400));

PRINT '=============================================================';
PRINT 'Development Safety Neutralization - starting';
PRINT '=============================================================';

-- =============================================================================
-- 1. EmailOutbox - cancel every PENDING / PROCESSING / retryable FAILED row
-- =============================================================================
IF OBJECT_ID('dbo.EmailOutbox', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.EmailOutbox', 'Status') IS NOT NULL
    BEGIN
        SET @sql = N'UPDATE dbo.EmailOutbox SET Status = ''DEAD_LETTER''';
        IF COL_LENGTH('dbo.EmailOutbox', 'LastError') IS NOT NULL
            SET @sql = @sql + N', LastError = ''Neutralized by dev-safety-neutralization.sql (PROD clone import)''';
        SET @sql = @sql + N' WHERE Status IN (''PENDING'',''PROCESSING'',''FAILED'');';
        EXEC sp_executesql @sql;
        PRINT 'EmailOutbox: neutralized (PENDING/PROCESSING/FAILED -> DEAD_LETTER).';
    END
    ELSE
        PRINT 'EmailOutbox: Status column not found - skipped (nothing to neutralize).';
END
ELSE
    PRINT 'EmailOutbox: table not found - skipped.';

-- =============================================================================
-- 2. SmtpSettings - force Development-safe redirection and warning banners
-- =============================================================================
IF OBJECT_ID('dbo.SmtpSettings', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.SmtpSettings)
    BEGIN
        SET @sql = N'UPDATE dbo.SmtpSettings SET ';
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.SmtpSettings','AllowRealRecipientsInNonProduction') IS NOT NULL THEN N'AllowRealRecipientsInNonProduction = 0, ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.SmtpSettings','RedirectAllToTestRecipient') IS NOT NULL THEN N'RedirectAllToTestRecipient = 1, ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.SmtpSettings','TestRecipientEmail') IS NOT NULL THEN N'TestRecipientEmail = ''dev-local-alerts@alpla.com'', ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.SmtpSettings','EnableSubjectPrefix') IS NOT NULL THEN N'EnableSubjectPrefix = 1, ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.SmtpSettings','SubjectPrefixText') IS NOT NULL THEN N'SubjectPrefixText = ''[DEV LOCAL - IGNORE]'', ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.SmtpSettings','EnableBodyWarningBanner') IS NOT NULL THEN N'EnableBodyWarningBanner = 1, ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.SmtpSettings','WarningBannerText') IS NOT NULL THEN N'WarningBannerText = ''AMBIENTE DE DESENVOLVIMENTO LOCAL - IGNORAR ESTE EMAIL'', ' ELSE N'' END;
        -- Defense in depth beyond the existing TEST script: also clear the encrypted SMTP
        -- password itself, so even a misconfigured redirect cannot authenticate outbound.
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.SmtpSettings','EncryptedPassword') IS NOT NULL THEN N'EncryptedPassword = NULL, ' ELSE N'' END;
        IF COL_LENGTH('dbo.SmtpSettings','UpdatedAtUtc') IS NOT NULL
            SET @sql = @sql + N'UpdatedAtUtc = GETUTCDATE();'
        ELSE
            SET @sql = LEFT(@sql, LEN(@sql) - 1) + N';'; -- trim trailing comma if no UpdatedAtUtc column

        EXEC sp_executesql @sql;
        PRINT 'SmtpSettings: Development-safe redirection applied and SMTP password cleared.';
    END
    ELSE
        PRINT 'SmtpSettings: table exists but has no rows - nothing to neutralize.';
END
ELSE
    PRINT 'SmtpSettings: table not found - skipped.';

-- =============================================================================
-- 3. IntegrationProviders - disable every external integration provider
-- =============================================================================
IF OBJECT_ID('dbo.IntegrationProviders', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.IntegrationProviders', 'IsEnabled') IS NOT NULL
    BEGIN
        SET @sql = N'UPDATE dbo.IntegrationProviders SET IsEnabled = 0';
        IF COL_LENGTH('dbo.IntegrationProviders','UpdatedAtUtc') IS NOT NULL
            SET @sql = @sql + N', UpdatedAtUtc = GETUTCDATE()';
        SET @sql = @sql + N';';
        EXEC sp_executesql @sql;
        PRINT 'IntegrationProviders: all providers disabled.';
    END
    ELSE
        PRINT 'IntegrationProviders: IsEnabled column not found - skipped.';
END
ELSE
    PRINT 'IntegrationProviders: table not found - skipped.';

-- =============================================================================
-- 4. IntegrationProviderSettings - clear encrypted passwords/API keys and
--    Production endpoints (Server, ApiBaseUrl), and any free-form config blob
-- =============================================================================
IF OBJECT_ID('dbo.IntegrationProviderSettings', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.IntegrationProviderSettings)
    BEGIN
        SET @sql = N'UPDATE dbo.IntegrationProviderSettings SET ';
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.IntegrationProviderSettings','EncryptedPassword') IS NOT NULL THEN N'EncryptedPassword = NULL, ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.IntegrationProviderSettings','ApiKeyEncrypted') IS NOT NULL THEN N'ApiKeyEncrypted = NULL, ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.IntegrationProviderSettings','Server') IS NOT NULL THEN N'Server = NULL, ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.IntegrationProviderSettings','ApiBaseUrl') IS NOT NULL THEN N'ApiBaseUrl = NULL, ' ELSE N'' END;
        -- Free-form JSON blob for provider-specific technical config; shape is not
        -- guaranteed not to contain secrets, so it is cleared defensively.
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.IntegrationProviderSettings','AdditionalConfig') IS NOT NULL THEN N'AdditionalConfig = NULL, ' ELSE N'' END;
        SET @sql = @sql + CASE WHEN COL_LENGTH('dbo.IntegrationProviderSettings','IsReadOnly') IS NOT NULL THEN N'IsReadOnly = 1, ' ELSE N'' END;

        IF COL_LENGTH('dbo.IntegrationProviderSettings','UpdatedAtUtc') IS NOT NULL
            SET @sql = @sql + N'UpdatedAtUtc = GETUTCDATE();'
        ELSE
            SET @sql = LEFT(@sql, LEN(@sql) - 1) + N';';

        EXEC sp_executesql @sql;
        PRINT 'IntegrationProviderSettings: encrypted secrets and Production endpoints cleared.';
    END
    ELSE
        PRINT 'IntegrationProviderSettings: table exists but has no rows - nothing to neutralize.';
END
ELSE
    PRINT 'IntegrationProviderSettings: table not found - skipped.';

-- =============================================================================
-- 5. Users - clear password-reset tokens (per explicit scope: this script does
--    NOT pseudonymize user identities, emails, or password hashes in this
--    first implementation; only the live reset-token pair is cleared)
-- =============================================================================
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Users', 'PasswordResetToken') IS NOT NULL
       AND COL_LENGTH('dbo.Users', 'PasswordResetTokenExpiryUtc') IS NOT NULL
    BEGIN
        SET @sql = N'UPDATE dbo.Users SET PasswordResetToken = NULL, PasswordResetTokenExpiryUtc = NULL WHERE PasswordResetToken IS NOT NULL OR PasswordResetTokenExpiryUtc IS NOT NULL;';
        EXEC sp_executesql @sql;
        PRINT 'Users: PasswordResetToken and PasswordResetTokenExpiryUtc cleared for all rows.';
    END
    ELSE
        PRINT 'Users: PasswordResetToken/PasswordResetTokenExpiryUtc columns not found - skipped.';
END
ELSE
    PRINT 'Users: table not found - skipped.';

PRINT '=============================================================';
PRINT 'Development Safety Neutralization - update phase complete.';
PRINT 'Running fail-closed verification...';
PRINT '=============================================================';

-- =============================================================================
-- 6. Fail-closed verification - re-query every neutralized area and THROW if
--    any check does not verify clean. The calling PowerShell script must treat
--    any error from this script as an import FAILURE, not a partial success.
-- =============================================================================

-- 6.1 EmailOutbox: zero active/retryable rows
IF OBJECT_ID('dbo.EmailOutbox', 'U') IS NOT NULL AND COL_LENGTH('dbo.EmailOutbox', 'Status') IS NOT NULL
BEGIN
    DECLARE @activeEmailCount INT;
    SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.EmailOutbox WHERE Status IN (''PENDING'',''PROCESSING'',''FAILED'');';
    EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @activeEmailCount OUTPUT;
    IF @activeEmailCount > 0
        INSERT INTO @verificationFailures VALUES ('EmailOutbox', CONCAT(@activeEmailCount, ' row(s) still in an active/retryable status.'));
END

-- 6.2 IntegrationProviders: zero enabled rows
IF OBJECT_ID('dbo.IntegrationProviders', 'U') IS NOT NULL AND COL_LENGTH('dbo.IntegrationProviders', 'IsEnabled') IS NOT NULL
BEGIN
    DECLARE @enabledProviderCount INT;
    SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.IntegrationProviders WHERE IsEnabled = 1;';
    EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @enabledProviderCount OUTPUT;
    IF @enabledProviderCount > 0
        INSERT INTO @verificationFailures VALUES ('IntegrationProviders', CONCAT(@enabledProviderCount, ' provider(s) still enabled.'));
END

-- 6.3 SmtpSettings: redirection enabled AND real recipients disabled, for every row present
IF OBJECT_ID('dbo.SmtpSettings', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.SmtpSettings','RedirectAllToTestRecipient') IS NOT NULL
   AND COL_LENGTH('dbo.SmtpSettings','AllowRealRecipientsInNonProduction') IS NOT NULL
BEGIN
    DECLARE @badSmtpRowCount INT;
    SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.SmtpSettings WHERE RedirectAllToTestRecipient = 0 OR AllowRealRecipientsInNonProduction = 1;';
    EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @badSmtpRowCount OUTPUT;
    IF @badSmtpRowCount > 0
        INSERT INTO @verificationFailures VALUES ('SmtpSettings', CONCAT(@badSmtpRowCount, ' row(s) do not have safe redirection settings.'));
END

-- 6.4 Users: no remaining password-reset tokens
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.Users', 'PasswordResetToken') IS NOT NULL
BEGIN
    DECLARE @residualTokenCount INT;
    SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.Users WHERE PasswordResetToken IS NOT NULL;';
    EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @residualTokenCount OUTPUT;
    IF @residualTokenCount > 0
        INSERT INTO @verificationFailures VALUES ('Users.PasswordResetToken', CONCAT(@residualTokenCount, ' row(s) still have a non-null PasswordResetToken.'));
END

-- 6.5 IntegrationProviderSettings: no remaining encrypted secrets or Production
-- endpoints, and IsReadOnly = 1 (IsEnabled lives on the parent IntegrationProviders
-- table and is already covered by 6.2 - IntegrationProviderSettings itself has no
-- IsEnabled column). Each check is independently gated by COL_LENGTH so a schema
-- that lacks one of these columns simply skips that specific check instead of
-- failing the whole batch.
IF OBJECT_ID('dbo.IntegrationProviderSettings', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.IntegrationProviderSettings', 'EncryptedPassword') IS NOT NULL
    BEGIN
        DECLARE @residualEncryptedPasswordCount INT;
        SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.IntegrationProviderSettings WHERE EncryptedPassword IS NOT NULL;';
        EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @residualEncryptedPasswordCount OUTPUT;
        IF @residualEncryptedPasswordCount > 0
            INSERT INTO @verificationFailures VALUES ('IntegrationProviderSettings.EncryptedPassword', CONCAT(@residualEncryptedPasswordCount, ' row(s) still have a non-null EncryptedPassword.'));
    END

    IF COL_LENGTH('dbo.IntegrationProviderSettings', 'ApiKeyEncrypted') IS NOT NULL
    BEGIN
        DECLARE @residualApiKeyCount INT;
        SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.IntegrationProviderSettings WHERE ApiKeyEncrypted IS NOT NULL;';
        EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @residualApiKeyCount OUTPUT;
        IF @residualApiKeyCount > 0
            INSERT INTO @verificationFailures VALUES ('IntegrationProviderSettings.ApiKeyEncrypted', CONCAT(@residualApiKeyCount, ' row(s) still have a non-null ApiKeyEncrypted.'));
    END

    IF COL_LENGTH('dbo.IntegrationProviderSettings', 'Server') IS NOT NULL
    BEGIN
        DECLARE @residualServerCount INT;
        SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.IntegrationProviderSettings WHERE Server IS NOT NULL;';
        EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @residualServerCount OUTPUT;
        IF @residualServerCount > 0
            INSERT INTO @verificationFailures VALUES ('IntegrationProviderSettings.Server', CONCAT(@residualServerCount, ' row(s) still have a non-null Server.'));
    END

    IF COL_LENGTH('dbo.IntegrationProviderSettings', 'ApiBaseUrl') IS NOT NULL
    BEGIN
        DECLARE @residualApiBaseUrlCount INT;
        SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.IntegrationProviderSettings WHERE ApiBaseUrl IS NOT NULL;';
        EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @residualApiBaseUrlCount OUTPUT;
        IF @residualApiBaseUrlCount > 0
            INSERT INTO @verificationFailures VALUES ('IntegrationProviderSettings.ApiBaseUrl', CONCAT(@residualApiBaseUrlCount, ' row(s) still have a non-null ApiBaseUrl.'));
    END

    -- IsEnabled is intentionally NOT checked here: it does not exist on
    -- IntegrationProviderSettings (confirmed against the current entity shape).
    -- It exists on the parent IntegrationProviders table and is verified in 6.2.
    IF COL_LENGTH('dbo.IntegrationProviderSettings', 'IsEnabled') IS NOT NULL
    BEGIN
        DECLARE @residualSettingsEnabledCount INT;
        SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.IntegrationProviderSettings WHERE IsEnabled = 1;';
        EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @residualSettingsEnabledCount OUTPUT;
        IF @residualSettingsEnabledCount > 0
            INSERT INTO @verificationFailures VALUES ('IntegrationProviderSettings.IsEnabled', CONCAT(@residualSettingsEnabledCount, ' row(s) still have IsEnabled = 1.'));
    END

    IF COL_LENGTH('dbo.IntegrationProviderSettings', 'IsReadOnly') IS NOT NULL
    BEGIN
        DECLARE @notReadOnlyCount INT;
        SET @sql = N'SELECT @cnt = COUNT(*) FROM dbo.IntegrationProviderSettings WHERE IsReadOnly = 0;';
        EXEC sp_executesql @sql, N'@cnt INT OUTPUT', @cnt = @notReadOnlyCount OUTPUT;
        IF @notReadOnlyCount > 0
            INSERT INTO @verificationFailures VALUES ('IntegrationProviderSettings.IsReadOnly', CONCAT(@notReadOnlyCount, ' row(s) still have IsReadOnly = 0.'));
    END
END

IF EXISTS (SELECT 1 FROM @verificationFailures)
BEGIN
    DECLARE @failureSummary NVARCHAR(MAX);
    SELECT @failureSummary = STRING_AGG(CONCAT(Area, ': ', Detail), ' | ') FROM @verificationFailures;
    PRINT '=============================================================';
    PRINT 'FAIL-CLOSED: Development safety verification did NOT pass.';
    PRINT @failureSummary;
    PRINT '=============================================================';
    THROW 51000, N'Development safety neutralization failed verification. See printed detail above. Import must be treated as FAILED.', 1;
END

PRINT '=============================================================';
PRINT 'Development Safety Neutralization - verification PASSED.';
PRINT '=============================================================';
