-- ═══════════════════════════════════════════════════════════════════════════
-- ADMIN USER SEED TEMPLATE
-- Alpla Angola Portal Gerencial
-- ═══════════════════════════════════════════════════════════════════════════
-- Purpose:  Create the first administrator user on a clean database.
-- WARNING:  This template is for FIRST-TIME SETUP ONLY.
--           After initial setup, manage users through the application UI.
--
-- Instructions:
-- 1. Replace <PLACEHOLDERS> with actual values
-- 2. Generate a BCrypt hash for the initial password using:
--    dotnet run --project tools/PasswordHasher -- "YourTemporaryPassword"
--    Or use an online BCrypt generator (cost factor 11)
-- 3. Execute against the target database
-- 4. The user will be forced to change password on first login
-- ═══════════════════════════════════════════════════════════════════════════

-- Configuration: EDIT THESE VALUES BEFORE EXECUTING
DECLARE @AdminEmail NVARCHAR(256) = '<admin-email@company.com>';  -- e.g. 'leonardo.cintra@alpla.com'
DECLARE @AdminFullName NVARCHAR(256) = '<Full Name>';              -- e.g. 'Leonardo Cintra'
DECLARE @PasswordHash NVARCHAR(MAX) = '<BCrypt_Hash>';             -- BCrypt hash of temporary password

-- ═══════════════════════════════════════════════════════════════════════════
-- DO NOT EDIT BELOW THIS LINE
-- ═══════════════════════════════════════════════════════════════════════════

SET NOCOUNT ON;
BEGIN TRANSACTION;

-- Validate placeholders were replaced
IF @AdminEmail LIKE '<%' OR @AdminFullName LIKE '<%' OR @PasswordHash LIKE '<%'
BEGIN
    PRINT 'ERROR: Replace all <PLACEHOLDER> values before executing.';
    ROLLBACK;
    RETURN;
END

-- Check if user already exists
IF EXISTS (SELECT 1 FROM Users WHERE Email = @AdminEmail)
BEGIN
    PRINT 'WARNING: User with email ''' + @AdminEmail + ''' already exists. Skipping.';
    ROLLBACK;
    RETURN;
END

-- Generate a new GUID for the user
DECLARE @UserId UNIQUEIDENTIFIER = NEWID();

-- Create the user
INSERT INTO Users (Id, Email, FullName, PasswordHash, IsActive, CreatedAt, MustChangePassword, AccessFailedCount)
VALUES (@UserId, @AdminEmail, @AdminFullName, @PasswordHash, 1, GETUTCDATE(), 1, 0);

PRINT 'Created user: ' + @AdminEmail + ' (Id: ' + CAST(@UserId AS NVARCHAR(36)) + ')';

-- Assign System Administrator role (RoleId = 1)
INSERT INTO UserRoleAssignments (UserId, RoleId, DepartmentScopeId)
VALUES (@UserId, 1, NULL);

PRINT 'Assigned role: System Administrator';

-- Assign all plant scopes
INSERT INTO UserPlantScopes (UserId, PlantId)
SELECT @UserId, Id FROM Plants WHERE IsActive = 1;

PRINT 'Assigned plant scopes: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' active plants';

-- Assign all department scopes
INSERT INTO UserDepartmentScopes (UserId, DepartmentId)
SELECT @UserId, Id FROM Departments WHERE IsActive = 1;

PRINT 'Assigned department scopes: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' active departments';

COMMIT;

PRINT '';
PRINT '================================================================';
PRINT '  Admin user created successfully.';
PRINT '  Email:    ' + @AdminEmail;
PRINT '  Roles:    System Administrator';
PRINT '  Plants:   All active plants';
PRINT '  Depts:    All active departments';
PRINT '  Password: Must be changed on first login';
PRINT '================================================================';
