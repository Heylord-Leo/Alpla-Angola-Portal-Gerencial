/* ============================================================================================
   final-approval-recipients-vs-approvers-readonly.sql  (READ-ONLY — SELECTs only)

   Purpose : Under the CURRENT single-final-approver model, show the difference between
             (a) who CAN approve the final stage  = active users holding the "Final Approver" role
                 (plus plant/department access scope, evaluated per request by the application), and
             (b) who is NOTIFIED                   = the request nominee Request.FinalApproverId
                 (one-shot AREA_APPROVED e-mail), falling back to the company nominee
                 Company.FinalApproverUserId for proforma alerts and reminder digests,
                 in both cases only if the user is active and has an e-mail.
             E-mail availability gates sending only; it never affects who may approve.
   Sections: A per company: nominee, notifiable?, holds role?  (nominee without role = notified but cannot approve)
             B role-holders who are nominee of no company           (can approve, never notified)
             C pending final-stage requests whose nominee is not notifiable (nobody gets alerts/digests)
             D pending final-stage requests whose request nominee differs from the company nominee
   Safe on : TEST [Portal-Gerencial-Test], PROD [Portal-Gerencial], DEV clone.
   ============================================================================================ */
SET NOCOUNT ON;

;WITH RoleHolders AS (
    SELECT DISTINCT ura.UserId
    FROM dbo.UserRoleAssignments ura JOIN dbo.Roles r ON r.Id = ura.RoleId
    WHERE r.RoleName = N'Final Approver'
)
SELECT 'A_COMPANY_NOMINEE' AS Section, c.Id AS CompanyId, c.Name AS Company, c.IsActive AS CompanyActive,
       Nominee = u.FullName,
       NomineeActive = u.IsActive,
       NomineeHasEmail = CASE WHEN ISNULL(u.Email, N'') <> N'' THEN 1 ELSE 0 END,
       NomineeHoldsRole = CASE WHEN rh.UserId IS NOT NULL THEN 1 ELSE 0 END,
       Verdict = CASE WHEN u.Id IS NULL THEN 'NO_NOMINEE (nobody notified)'
                      WHEN u.IsActive = 0 OR ISNULL(u.Email, N'') = N'' THEN 'NOT_NOTIFIABLE (inactive or no e-mail)'
                      WHEN rh.UserId IS NULL THEN 'NOTIFIED_BUT_CANNOT_APPROVE (no role)'
                      ELSE 'OK' END
FROM dbo.Companies c
LEFT JOIN dbo.Users u ON u.Id = c.FinalApproverUserId
LEFT JOIN RoleHolders rh ON rh.UserId = u.Id
ORDER BY c.Name;

;WITH RoleHolders AS (
    SELECT DISTINCT ura.UserId
    FROM dbo.UserRoleAssignments ura JOIN dbo.Roles r ON r.Id = ura.RoleId
    WHERE r.RoleName = N'Final Approver'
)
SELECT 'B_CAN_APPROVE_NEVER_NOTIFIED' AS Section, u.FullName, u.IsActive,
       HasEmail = CASE WHEN ISNULL(u.Email, N'') <> N'' THEN 1 ELSE 0 END,
       PlantScopes = (SELECT COUNT(*) FROM dbo.UserPlantScopes p WHERE p.UserId = u.Id),
       DepartmentScopes = (SELECT COUNT(*) FROM dbo.UserDepartmentScopes d WHERE d.UserId = u.Id),
       NomineeOfCompanies = (SELECT COUNT(*) FROM dbo.Companies c WHERE c.FinalApproverUserId = u.Id),
       PendingRequestsNominatedOn = (SELECT COUNT(*) FROM dbo.Requests r JOIN dbo.RequestStatuses s ON s.Id = r.StatusId
                                     WHERE r.FinalApproverId = u.Id AND s.Code = N'WAITING_FINAL_APPROVAL')
FROM dbo.Users u
JOIN RoleHolders rh ON rh.UserId = u.Id
WHERE u.IsActive = 1
ORDER BY NomineeOfCompanies, u.FullName;

SELECT 'C_PENDING_FINAL_NOT_NOTIFIABLE' AS Section, r.RequestNumber, c.Name AS Company,
       RequestNominee = ru.FullName, RequestNomineeNotifiable = CASE WHEN ru.Id IS NOT NULL AND ru.IsActive = 1 AND ISNULL(ru.Email, N'') <> N'' THEN 1 ELSE 0 END,
       CompanyNominee = cu.FullName, CompanyNomineeNotifiable = CASE WHEN cu.Id IS NOT NULL AND cu.IsActive = 1 AND ISNULL(cu.Email, N'') <> N'' THEN 1 ELSE 0 END
FROM dbo.Requests r
JOIN dbo.RequestStatuses s ON s.Id = r.StatusId
JOIN dbo.Companies c ON c.Id = r.CompanyId
LEFT JOIN dbo.Users ru ON ru.Id = r.FinalApproverId
LEFT JOIN dbo.Users cu ON cu.Id = c.FinalApproverUserId
WHERE (s.Code = N'WAITING_FINAL_APPROVAL' OR EXISTS (SELECT 1 FROM dbo.ApprovalBatches b WHERE b.RequestId = r.Id AND b.Status = N'WAITING_FINAL_APPROVAL'))
  AND NOT (ru.Id IS NOT NULL AND ru.IsActive = 1 AND ISNULL(ru.Email, N'') <> N'')
  AND NOT (cu.Id IS NOT NULL AND cu.IsActive = 1 AND ISNULL(cu.Email, N'') <> N'')
ORDER BY r.RequestNumber;

SELECT 'D_PENDING_FINAL_NOMINEE_DIFFERS' AS Section, r.RequestNumber, c.Name AS Company,
       RequestNominee = ru.FullName, CompanyNominee = cu.FullName,
       Note = 'AREA_APPROVED e-mail and digests go to the REQUEST nominee; the company nominee is only the fallback'
FROM dbo.Requests r
JOIN dbo.RequestStatuses s ON s.Id = r.StatusId
JOIN dbo.Companies c ON c.Id = r.CompanyId
LEFT JOIN dbo.Users ru ON ru.Id = r.FinalApproverId
LEFT JOIN dbo.Users cu ON cu.Id = c.FinalApproverUserId
WHERE (s.Code = N'WAITING_FINAL_APPROVAL' OR EXISTS (SELECT 1 FROM dbo.ApprovalBatches b WHERE b.RequestId = r.Id AND b.Status = N'WAITING_FINAL_APPROVAL'))
  AND ISNULL(r.FinalApproverId, '00000000-0000-0000-0000-000000000000') <> ISNULL(c.FinalApproverUserId, '00000000-0000-0000-0000-000000000000')
ORDER BY r.RequestNumber;
