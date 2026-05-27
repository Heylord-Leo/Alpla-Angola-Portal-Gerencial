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
--    dotnet script:
--      dotnet run --project tools/PasswordHasher -- "YourTemporaryPassword"
--    Or inline from the backend directory:
--      cd src/backend/AlplaPortal.Api
--      dotnet exec -- -e "Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(\"TempPass123!\"));"
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
DECLARE @UserId UNIQUEIDENTIFIER;

IF EXISTS (SELECT 1 FROM Users WHERE Email = @AdminEmail)
BEGIN
    SELECT @UserId = Id FROM Users WHERE Email = @AdminEmail;
    PRINT '[SKIP] User ''' + @AdminEmail + ''' already exists (Id: ' + CAST(@UserId AS NVARCHAR(36)) + ')';
    PRINT '       Updating roles, plant scopes, and department scopes...';

    -- Ensure user is active
    UPDATE Users SET IsActive = 1 WHERE Id = @UserId AND IsActive = 0;
END
ELSE
BEGIN
    -- Generate a new GUID for the user
    SET @UserId = NEWID();

    -- Create the user
    INSERT INTO Users (Id, Email, FullName, PasswordHash, IsActive, CreatedAt, MustChangePassword, AccessFailedCount)
    VALUES (@UserId, @AdminEmail, @AdminFullName, @PasswordHash, 1, GETUTCDATE(), 1, 0);

    PRINT '[CREATED] User: ' + @AdminEmail + ' (Id: ' + CAST(@UserId AS NVARCHAR(36)) + ')';
END

-- ═══════════════════════════════════════════════════════════════════════════
-- ROLE ASSIGNMENTS (idempotent — skips existing assignments)
-- ═══════════════════════════════════════════════════════════════════════════
-- Role IDs from seed data:
--   1  = System Administrator
--   2  = Local Manager
--   3  = Requester
--   4  = Buyer
--   5  = Area Approver
--   6  = Final Approver
--   7  = Finance
--   8  = Receiving
--   9  = Contracts
--   10 = Import
--   11 = Viewer / Management
--   12 = HR
--   13 = IT (if exists)

DECLARE @adminRoles TABLE (RoleId INT);
INSERT INTO @adminRoles VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12);

-- Only insert roles that exist in the Roles table and are not already assigned
INSERT INTO UserRoleAssignments (UserId, RoleId, DepartmentScopeId)
SELECT @UserId, ar.RoleId, NULL
FROM @adminRoles ar
INNER JOIN Roles r ON r.Id = ar.RoleId
WHERE NOT EXISTS (
    SELECT 1 FROM UserRoleAssignments ura
    WHERE ura.UserId = @UserId AND ura.RoleId = ar.RoleId
);

PRINT '[OK] Role assignments: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' new roles assigned';

-- Show current roles
SELECT r.Id, r.RoleName, 'ASSIGNED' AS [Status]
FROM UserRoleAssignments ura
INNER JOIN Roles r ON r.Id = ura.RoleId
WHERE ura.UserId = @UserId
ORDER BY r.Id;

-- ═══════════════════════════════════════════════════════════════════════════
-- PLANT SCOPES (idempotent — assign all active plants)
-- ═══════════════════════════════════════════════════════════════════════════
INSERT INTO UserPlantScopes (UserId, PlantId)
SELECT @UserId, p.Id
FROM Plants p
WHERE p.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM UserPlantScopes ups
      WHERE ups.UserId = @UserId AND ups.PlantId = p.Id
  );

PRINT '[OK] Plant scopes: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' new plants assigned';

-- ═══════════════════════════════════════════════════════════════════════════
-- DEPARTMENT SCOPES (idempotent — assign all active departments)
-- ═══════════════════════════════════════════════════════════════════════════
INSERT INTO UserDepartmentScopes (UserId, DepartmentId)
SELECT @UserId, d.Id
FROM Departments d
WHERE d.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM UserDepartmentScopes uds
      WHERE uds.UserId = @UserId AND uds.DepartmentId = d.Id
  );

PRINT '[OK] Department scopes: ' + CAST(@@ROWCOUNT AS VARCHAR) + ' new departments assigned';

COMMIT;

PRINT '';
PRINT '================================================================';
PRINT '  Admin user bootstrap complete.';
PRINT '  Email:    ' + @AdminEmail;
PRINT '  Roles:    All administrative roles (see above)';
PRINT '  Plants:   All active plants';
PRINT '  Depts:    All active departments';
PRINT '  Password: Must be changed on first login';
PRINT '================================================================';
