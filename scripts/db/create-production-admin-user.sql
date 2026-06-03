-- =============================================================================
-- Alpla Angola - Portal Gerencial
-- Production Admin User Initialization Script
-- =============================================================================
--
-- Purpose:  Create the first administrator user on Production database.
-- Email:    leonardo.cintra1988@gmail.com
-- Source:   [Portal-Gerencial-Test]   (READ-ONLY — diagnostics/reference only)
-- Target:   [Portal-Gerencial]        (INSERT only — user, roles, scopes)
-- Server:   AOVIA1VMS011 (SQL Server Express)
--
-- SAFETY:
--   * PasswordHash is set to NULL — no temporary password created
--   * MustChangePassword = 1 — first login requires Forgot Password flow
--   * No password hashes, tokens, secrets, or security data exposed
--   * No user records copied from Test
--   * Test database is NEVER written to
--   * Backup created BEFORE any changes (SQL Express — NO COMPRESSION)
--   * Script is fully idempotent (safe to re-run)
--   * Only [Portal-Gerencial] is modified
--
-- POST-EXECUTION:
--   1. Go to https://portalgerencial.alpla.net
--   2. Click "Forgot Password" / "Esqueci minha senha"
--   3. Enter: leonardo.cintra1988@gmail.com
--   4. Check Gmail inbox for the reset link
--   5. Set password and log in
--
-- INSTRUCTIONS:
--   Run on AOVIA1VMS011 using SSMS or sqlcmd.
--   Execute each step in order. Review output before proceeding.
-- =============================================================================

SET NOCOUNT ON
GO

-- =============================================================================
-- STEP 0: Environment Verification
-- =============================================================================
PRINT '============================================='
PRINT '  STEP 0: Environment Verification'
PRINT '============================================='
PRINT ''

SELECT
    SERVERPROPERTY('ServerName')     AS ServerName,
    SERVERPROPERTY('Edition')        AS Edition,
    SERVERPROPERTY('ProductVersion') AS ProductVersion

PRINT ''

-- Verify both databases exist
IF DB_ID('Portal-Gerencial') IS NULL
BEGIN
    PRINT '[FAIL] Database [Portal-Gerencial] does not exist on this server.'
    PRINT '*** ABORTING ***'
    RETURN
END
ELSE
    PRINT '[OK]   Database [Portal-Gerencial] exists.'

IF DB_ID('Portal-Gerencial-Test') IS NULL
BEGIN
    PRINT '[WARN] Database [Portal-Gerencial-Test] does not exist. Diagnostic queries will be skipped.'
END
ELSE
    PRINT '[OK]   Database [Portal-Gerencial-Test] exists.'

PRINT ''
GO

-- =============================================================================
-- STEP 1: Schema Guard Checks
-- =============================================================================
PRINT ''
PRINT '============================================='
PRINT '  STEP 1: Schema Guard Checks (Production)'
PRINT '============================================='
PRINT ''

DECLARE @errors INT = 0

-- 1a) Users table
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Users'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.Users does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.Users exists.'

-- 1b) Roles table
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Roles'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.Roles does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.Roles exists.'

-- 1c) UserRoleAssignments table
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'UserRoleAssignments'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.UserRoleAssignments does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.UserRoleAssignments exists.'

-- 1d) UserPlantScopes table
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'UserPlantScopes'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.UserPlantScopes does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.UserPlantScopes exists.'

-- 1e) UserDepartmentScopes table
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'UserDepartmentScopes'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.UserDepartmentScopes does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.UserDepartmentScopes exists.'

-- 1f) Plants table
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Plants'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.Plants does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.Plants exists.'

-- 1g) Departments table
IF NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Departments'
)
BEGIN
    PRINT '[FAIL] Table [Portal-Gerencial].dbo.Departments does not exist.'
    SET @errors = @errors + 1
END
ELSE
    PRINT '[OK]   [Portal-Gerencial].dbo.Departments exists.'

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

-- =============================================================================
-- STEP 2: Diagnostic — Leonardo's Access in Test (READ-ONLY reference)
-- =============================================================================
PRINT ''
PRINT '============================================='
PRINT '  STEP 2: Diagnostic — Leonardo Access (Test)'
PRINT '============================================='
PRINT ''

-- Only run diagnostics if Test database exists
IF DB_ID('Portal-Gerencial-Test') IS NOT NULL
BEGIN
    PRINT '--- Leonardo roles in Test ---'
    SELECT
        r.Id       AS RoleId,
        r.RoleName AS RoleName
    FROM [Portal-Gerencial-Test].dbo.Users u
    INNER JOIN [Portal-Gerencial-Test].dbo.UserRoleAssignments ura ON ura.UserId = u.Id
    INNER JOIN [Portal-Gerencial-Test].dbo.Roles r ON r.Id = ura.RoleId
    WHERE u.Email = 'leonardo.cintra@alpla.com'
    ORDER BY r.Id

    PRINT ''
    PRINT '--- Leonardo plant scopes in Test ---'
    SELECT
        p.Id   AS PlantId,
        p.Code AS PlantCode,
        p.Name AS PlantName
    FROM [Portal-Gerencial-Test].dbo.Users u
    INNER JOIN [Portal-Gerencial-Test].dbo.UserPlantScopes ups ON ups.UserId = u.Id
    INNER JOIN [Portal-Gerencial-Test].dbo.Plants p ON p.Id = ups.PlantId
    WHERE u.Email = 'leonardo.cintra@alpla.com'
    ORDER BY p.Id

    PRINT ''
    PRINT '--- Leonardo department scopes in Test ---'
    SELECT
        d.Id   AS DeptId,
        d.Code AS DeptCode,
        d.Name AS DeptName
    FROM [Portal-Gerencial-Test].dbo.Users u
    INNER JOIN [Portal-Gerencial-Test].dbo.UserDepartmentScopes uds ON uds.UserId = u.Id
    INNER JOIN [Portal-Gerencial-Test].dbo.Departments d ON d.Id = uds.DepartmentId
    WHERE u.Email = 'leonardo.cintra@alpla.com'
    ORDER BY d.Id
END
ELSE
BEGIN
    PRINT '[SKIP] Test database not available. Diagnostic skipped.'
END

PRINT ''
PRINT '--- Available roles in Production ---'
SELECT Id, RoleName FROM [Portal-Gerencial].dbo.Roles ORDER BY Id

PRINT ''
PRINT '--- Active plants in Production ---'
SELECT Id, Code, Name FROM [Portal-Gerencial].dbo.Plants WHERE IsActive = 1 ORDER BY Id

PRINT ''
PRINT '--- Active departments in Production ---'
SELECT Id, Code, Name FROM [Portal-Gerencial].dbo.Departments WHERE IsActive = 1 ORDER BY Id
GO

-- =============================================================================
-- STEP 3: Production Database Backup (SQL Express — NO COMPRESSION)
-- =============================================================================
PRINT ''
PRINT '============================================='
PRINT '  STEP 3: Production Database Backup'
PRINT '============================================='
PRINT ''

-- Prerequisite: ensure backup directory exists on the server.
-- PowerShell: New-Item -ItemType Directory -Force -Path "C:\Apps\AlplaPortal\Prod\backups\db"

DECLARE @backupPath NVARCHAR(500)
DECLARE @timestamp  NVARCHAR(20)
SET @timestamp  = REPLACE(REPLACE(REPLACE(
                      CONVERT(NVARCHAR(20), GETDATE(), 120), '-', ''), ':', ''), ' ', '_')
SET @backupPath = 'C:\Apps\AlplaPortal\Prod\backups\db\Portal-Gerencial_before_admin_user_'
                  + @timestamp + '.bak'

PRINT 'Creating backup at: ' + @backupPath

BACKUP DATABASE [Portal-Gerencial]
TO DISK = @backupPath
WITH FORMAT, NAME = 'Pre-Admin-User Backup'

PRINT 'Backup completed successfully.'
GO

-- =============================================================================
-- STEP 4: Create User — leonardo.cintra1988@gmail.com
-- =============================================================================
PRINT ''
PRINT '============================================='
PRINT '  STEP 4: Create Production Admin User'
PRINT '============================================='
PRINT ''

DECLARE @AdminEmail    NVARCHAR(256) = 'leonardo.cintra1988@gmail.com'
DECLARE @AdminFullName NVARCHAR(256) = 'System Administrator'
DECLARE @UserId        UNIQUEIDENTIFIER

-- Check if user already exists (idempotent)
IF EXISTS (SELECT 1 FROM [Portal-Gerencial].dbo.Users WHERE Email = @AdminEmail)
BEGIN
    SELECT @UserId = Id FROM [Portal-Gerencial].dbo.Users WHERE Email = @AdminEmail
    PRINT '[SKIP] User ''' + @AdminEmail + ''' already exists (Id: ' + CAST(@UserId AS NVARCHAR(36)) + ')'

    -- Ensure user is active and MustChangePassword is set
    UPDATE [Portal-Gerencial].dbo.Users
    SET IsActive = 1,
        MustChangePassword = 1,
        UpdatedAt = GETUTCDATE()
    WHERE Id = @UserId
      AND (IsActive = 0 OR MustChangePassword = 0)

    IF @@ROWCOUNT > 0
        PRINT '       Updated: ensured IsActive=1, MustChangePassword=1'
    ELSE
        PRINT '       No updates needed (already active with MustChangePassword).'
END
ELSE
BEGIN
    -- Generate a new GUID for the user
    SET @UserId = NEWID()

    INSERT INTO [Portal-Gerencial].dbo.Users
        (Id, Email, FullName, PasswordHash, IsActive, CreatedAt, MustChangePassword, AccessFailedCount)
    VALUES
        (@UserId, @AdminEmail, @AdminFullName, NULL, 1, GETUTCDATE(), 1, 0)

    PRINT '[CREATED] User: ' + @AdminEmail
    PRINT '          Id:   ' + CAST(@UserId AS NVARCHAR(36))
    PRINT '          PasswordHash: NULL (Forgot Password flow required)'
END

-- =============================================================================
-- STEP 5: Assign All Roles (idempotent)
-- =============================================================================
PRINT ''
PRINT '============================================='
PRINT '  STEP 5: Assign All Roles'
PRINT '============================================='
PRINT ''

-- Insert all roles that exist in the Roles table and are not already assigned
INSERT INTO [Portal-Gerencial].dbo.UserRoleAssignments (UserId, RoleId, DepartmentScopeId)
SELECT @UserId, r.Id, NULL
FROM [Portal-Gerencial].dbo.Roles r
WHERE NOT EXISTS (
    SELECT 1 FROM [Portal-Gerencial].dbo.UserRoleAssignments ura
    WHERE ura.UserId = @UserId AND ura.RoleId = r.Id
)

PRINT '[OK] Role assignments: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' new role(s) assigned'

-- Show current role assignments
PRINT ''
PRINT '--- Current role assignments ---'
SELECT r.Id AS RoleId, r.RoleName, 'ASSIGNED' AS [Status]
FROM [Portal-Gerencial].dbo.UserRoleAssignments ura
INNER JOIN [Portal-Gerencial].dbo.Roles r ON r.Id = ura.RoleId
WHERE ura.UserId = @UserId
ORDER BY r.Id

-- =============================================================================
-- STEP 6: Assign All Active Plant Scopes (idempotent)
-- =============================================================================
PRINT ''
PRINT '============================================='
PRINT '  STEP 6: Assign All Active Plant Scopes'
PRINT '============================================='
PRINT ''

INSERT INTO [Portal-Gerencial].dbo.UserPlantScopes (UserId, PlantId)
SELECT @UserId, p.Id
FROM [Portal-Gerencial].dbo.Plants p
WHERE p.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM [Portal-Gerencial].dbo.UserPlantScopes ups
      WHERE ups.UserId = @UserId AND ups.PlantId = p.Id
  )

PRINT '[OK] Plant scopes: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' new plant(s) assigned'

-- Show current plant scopes
PRINT ''
PRINT '--- Current plant scopes ---'
SELECT p.Id AS PlantId, p.Code AS PlantCode, p.Name AS PlantName
FROM [Portal-Gerencial].dbo.UserPlantScopes ups
INNER JOIN [Portal-Gerencial].dbo.Plants p ON p.Id = ups.PlantId
WHERE ups.UserId = @UserId
ORDER BY p.Id

-- =============================================================================
-- STEP 7: Assign All Active Department Scopes (idempotent)
-- =============================================================================
PRINT ''
PRINT '============================================='
PRINT '  STEP 7: Assign All Active Department Scopes'
PRINT '============================================='
PRINT ''

INSERT INTO [Portal-Gerencial].dbo.UserDepartmentScopes (UserId, DepartmentId)
SELECT @UserId, d.Id
FROM [Portal-Gerencial].dbo.Departments d
WHERE d.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM [Portal-Gerencial].dbo.UserDepartmentScopes uds
      WHERE uds.UserId = @UserId AND uds.DepartmentId = d.Id
  )

PRINT '[OK] Department scopes: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' new department(s) assigned'

-- Show current department scopes
PRINT ''
PRINT '--- Current department scopes ---'
SELECT d.Id AS DeptId, d.Code AS DeptCode, d.Name AS DeptName
FROM [Portal-Gerencial].dbo.UserDepartmentScopes uds
INNER JOIN [Portal-Gerencial].dbo.Departments d ON d.Id = uds.DepartmentId
WHERE uds.UserId = @UserId
ORDER BY d.Id
GO

-- =============================================================================
-- STEP 8: Final Validation Summary
-- =============================================================================
PRINT ''
PRINT '============================================='
PRINT '  STEP 8: Final Validation Summary'
PRINT '============================================='
PRINT ''

-- 8a) User record (no secrets)
PRINT '--- Production Admin User ---'
SELECT
    u.Id,
    u.Email,
    u.FullName,
    u.IsActive,
    u.MustChangePassword,
    u.AccessFailedCount,
    CASE
        WHEN u.PasswordHash IS NULL THEN 'NULL (Forgot Password required)'
        ELSE '***SET*** (length: ' + CAST(LEN(u.PasswordHash) AS VARCHAR) + ')'
    END AS PasswordStatus,
    CASE
        WHEN u.PasswordResetToken IS NULL THEN 'NULL'
        ELSE '***SET***'
    END AS ResetTokenStatus,
    u.CreatedAt
FROM [Portal-Gerencial].dbo.Users u
WHERE u.Email = 'leonardo.cintra1988@gmail.com'

-- 8b) Roles assigned
PRINT ''
PRINT '--- Roles Assigned ---'
SELECT
    r.Id       AS RoleId,
    r.RoleName AS RoleName
FROM [Portal-Gerencial].dbo.UserRoleAssignments ura
INNER JOIN [Portal-Gerencial].dbo.Roles r ON r.Id = ura.RoleId
INNER JOIN [Portal-Gerencial].dbo.Users u ON u.Id = ura.UserId
WHERE u.Email = 'leonardo.cintra1988@gmail.com'
ORDER BY r.Id

DECLARE @totalRoles INT
SELECT @totalRoles = COUNT(*)
FROM [Portal-Gerencial].dbo.UserRoleAssignments ura
INNER JOIN [Portal-Gerencial].dbo.Users u ON u.Id = ura.UserId
WHERE u.Email = 'leonardo.cintra1988@gmail.com'

DECLARE @availableRoles INT
SELECT @availableRoles = COUNT(*) FROM [Portal-Gerencial].dbo.Roles

PRINT ''
PRINT 'Roles assigned: ' + CAST(@totalRoles AS VARCHAR) + ' / ' + CAST(@availableRoles AS VARCHAR) + ' available'

-- 8c) Plant scopes
PRINT ''
PRINT '--- Plant Scopes Assigned ---'
SELECT
    p.Id   AS PlantId,
    p.Code AS PlantCode,
    p.Name AS PlantName
FROM [Portal-Gerencial].dbo.UserPlantScopes ups
INNER JOIN [Portal-Gerencial].dbo.Plants p ON p.Id = ups.PlantId
INNER JOIN [Portal-Gerencial].dbo.Users u ON u.Id = ups.UserId
WHERE u.Email = 'leonardo.cintra1988@gmail.com'
ORDER BY p.Id

DECLARE @totalPlants INT
SELECT @totalPlants = COUNT(*)
FROM [Portal-Gerencial].dbo.UserPlantScopes ups
INNER JOIN [Portal-Gerencial].dbo.Users u ON u.Id = ups.UserId
WHERE u.Email = 'leonardo.cintra1988@gmail.com'

DECLARE @activePlants INT
SELECT @activePlants = COUNT(*) FROM [Portal-Gerencial].dbo.Plants WHERE IsActive = 1

PRINT ''
PRINT 'Plant scopes: ' + CAST(@totalPlants AS VARCHAR) + ' / ' + CAST(@activePlants AS VARCHAR) + ' active plants'

-- 8d) Department scopes
PRINT ''
PRINT '--- Department Scopes Assigned ---'
SELECT
    d.Id   AS DeptId,
    d.Code AS DeptCode,
    d.Name AS DeptName
FROM [Portal-Gerencial].dbo.UserDepartmentScopes uds
INNER JOIN [Portal-Gerencial].dbo.Departments d ON d.Id = uds.DepartmentId
INNER JOIN [Portal-Gerencial].dbo.Users u ON u.Id = uds.UserId
WHERE u.Email = 'leonardo.cintra1988@gmail.com'
ORDER BY d.Id

DECLARE @totalDepts INT
SELECT @totalDepts = COUNT(*)
FROM [Portal-Gerencial].dbo.UserDepartmentScopes uds
INNER JOIN [Portal-Gerencial].dbo.Users u ON u.Id = uds.UserId
WHERE u.Email = 'leonardo.cintra1988@gmail.com'

DECLARE @activeDepts INT
SELECT @activeDepts = COUNT(*) FROM [Portal-Gerencial].dbo.Departments WHERE IsActive = 1

PRINT ''
PRINT 'Department scopes: ' + CAST(@totalDepts AS VARCHAR) + ' / ' + CAST(@activeDepts AS VARCHAR) + ' active departments'

-- 8e) Safety: total users in Production (should be 1)
PRINT ''
DECLARE @totalUsers INT
SELECT @totalUsers = COUNT(*) FROM [Portal-Gerencial].dbo.Users

PRINT 'Total users in Production: ' + CAST(@totalUsers AS VARCHAR)

IF @totalUsers = 1
    PRINT '[OK]   Exactly 1 user in Production (no unrelated users copied).'
ELSE IF @totalUsers = 0
    PRINT '[WARN] No users found in Production. Script may have failed.'
ELSE
    PRINT '[INFO] Multiple users exist. Verify no unrelated users were copied.'

-- 8f) Safety: verify Test database was NOT modified
PRINT ''
IF DB_ID('Portal-Gerencial-Test') IS NOT NULL
BEGIN
    DECLARE @testUserCount INT
    SELECT @testUserCount = COUNT(*) FROM [Portal-Gerencial-Test].dbo.Users

    PRINT 'Test database user count: ' + CAST(@testUserCount AS VARCHAR) + ' (should be unchanged)'

    IF NOT EXISTS (
        SELECT 1 FROM [Portal-Gerencial-Test].dbo.Users
        WHERE Email = 'leonardo.cintra1988@gmail.com'
    )
        PRINT '[OK]   leonardo.cintra1988@gmail.com does NOT exist in Test (expected — not copied).'
    ELSE
        PRINT '[INFO] leonardo.cintra1988@gmail.com also exists in Test (pre-existing, not created by this script).'
END
ELSE
    PRINT '[SKIP] Test database not available for verification.'

-- 8g) Final summary
PRINT ''
PRINT '================================================================'
PRINT '  Production Admin User Bootstrap — COMPLETE'
PRINT '================================================================'
PRINT ''
PRINT '  Email:            leonardo.cintra1988@gmail.com'
PRINT '  Full Name:        System Administrator'
PRINT '  Active:           Yes'
PRINT '  MustChangePassword: Yes'
PRINT '  Password:         NULL (no temporary password)'
PRINT '  Roles:            All available (' + CAST(@totalRoles AS VARCHAR) + ')'
PRINT '  Plants:           All active (' + CAST(@totalPlants AS VARCHAR) + ')'
PRINT '  Departments:      All active (' + CAST(@totalDepts AS VARCHAR) + ')'
PRINT ''
PRINT '  NEXT STEPS:'
PRINT '  1. Open https://portalgerencial.alpla.net'
PRINT '  2. Click "Forgot Password" / "Esqueci minha senha"'
PRINT '  3. Enter: leonardo.cintra1988@gmail.com'
PRINT '  4. Check Gmail inbox for the password reset link'
PRINT '  5. Set your password and log in'
PRINT ''
PRINT '  IMPORTANT:'
PRINT '  - The user CANNOT log in until the password is set via Forgot Password'
PRINT '  - SMTP must be operational (already configured in Production)'
PRINT '  - No password hashes, tokens, or secrets were created or exposed'
PRINT '  - Test database was NOT modified'
PRINT '================================================================'
GO
