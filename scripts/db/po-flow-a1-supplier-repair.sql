-- ============================================================================
-- ███  MANUAL DATA REPAIR  ███
-- ███  EXPLICIT AUTHORIZATION REQUIRED  ███
-- ███  ENVIRONMENT MUST BE VERIFIED BEFORE EXECUTION  ███
-- ============================================================================
-- PO-FLOW REPAIR — POPULATION A1 SUPPLIER BACKFILL
-- Rehearsed end-to-end (execute + audit + rollback) against the PROD clone on
-- 2026-08-20; execution against the real environment requires explicit
-- authorization AND verifying the connected database is the intended one.
-- ============================================================================
-- Scope: EXACTLY the two operationally-active (A1) rows approved for eventual repair.
-- Everything else — the 9 A2 inert rows, Population B, historical P.O numbers — is
-- explicitly OUT of scope and this script cannot touch them.
--
--   REQ-09/07/2026-031  group bc42031e-2dc1-4ce6-a4ef-1ec960da8e7e  -> supplier 254 (RBC)
--   REQ-14/07/2026-067  group b8f9c46f-34e1-4b8c-a30e-85ba03e91b52  -> supplier 102 (Gasp)
--
-- Writes ONLY: RequestPoGroup.SupplierId / SupplierNameSnapshot / SupplierNifSnapshot
--              + one RequestStatusHistories audit row per request (status unchanged).
-- Never touches: request status, group status, amounts, approval state, PurchaseOrderNumber.
--
-- One transaction per request. Each transaction re-validates, at execution time, every
-- assumption pinned by the dry-run of 2026-08-20 and ROLLS BACK (with a PRINT) if any fails:
--   group.SupplierId IS NULL
--   request.SupplierId IS NOT NULL and matches the dry-run value
--   supplier still exists and is active
--   request/group ids still match the dry-run identifiers
--   workflow state unchanged (request ADVANCE_PAYMENT_REQUIRED, group PENDING)
--
-- The captured previous values are printed and also embedded in the rollback section at the
-- bottom (previous values were NULL/NULL/NULL for both groups per the dry-run).
-- ============================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @actor UNIQUEIDENTIFIER;
-- Audit actor: the administrator executing the repair. REPLACE before execution:
-- SET @actor = '<admin user guid>';
IF @actor IS NULL
BEGIN
    PRINT 'ABORTED: set @actor to the executing administrator user id first.';
    RETURN;
END

-- ────────────────────────────── REQ-09/07/2026-031 ──────────────────────────────
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @g1 UNIQUEIDENTIFIER = 'bc42031e-2dc1-4ce6-a4ef-1ec960da8e7e';
    DECLARE @r1 UNIQUEIDENTIFIER = '949cbc05-ae7c-48e3-83d8-8f3c685176bc';
    DECLARE @s1 INT = 254;

    DECLARE @prevSupplierId1 INT, @prevName1 NVARCHAR(400), @prevNif1 NVARCHAR(100);
    SELECT @prevSupplierId1 = g.SupplierId, @prevName1 = g.SupplierNameSnapshot, @prevNif1 = g.SupplierNifSnapshot
    FROM RequestPoGroups g WHERE g.Id = @g1;
    PRINT CONCAT('REQ-031 previous values: SupplierId=', ISNULL(CAST(@prevSupplierId1 AS VARCHAR(20)),'NULL'),
                 ' Name=', ISNULL(@prevName1,'NULL'), ' Nif=', ISNULL(@prevNif1,'NULL'));

    IF NOT EXISTS (
        SELECT 1
        FROM RequestPoGroups g
        JOIN Requests r        ON r.Id = g.RequestId
        JOIN RequestStatuses rs ON rs.Id = r.StatusId
        JOIN Suppliers s       ON s.Id = r.SupplierId
        WHERE g.Id = @g1
          AND r.Id = @r1
          AND r.RequestNumber = 'REQ-09/07/2026-031'
          AND g.SupplierId IS NULL                       -- still unrepaired
          AND r.SupplierId = @s1                          -- deterministic source unchanged
          AND s.IsActive = 1                              -- supplier exists and is active
          AND g.Status = 'PENDING'                        -- group state unchanged vs dry-run
          AND rs.Code = 'ADVANCE_PAYMENT_REQUIRED'        -- workflow state unchanged vs dry-run
          AND g.PurchaseOrderNumber IS NULL               -- no P.O appeared meanwhile
    )
    BEGIN
        PRINT 'REQ-031: PRECONDITIONS FAILED — nothing written, rolled back. Re-run the dry-run.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.SupplierId = r.SupplierId,
            g.SupplierNameSnapshot = s.Name,
            g.SupplierNifSnapshot = s.TaxId,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN Requests r  ON r.Id = g.RequestId
        JOIN Suppliers s ON s.Id = r.SupplierId
        WHERE g.Id = @g1;

        INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
        SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
               '[Reparo de integridade — P.O Flow v2.229.12] Fornecedor do grupo bc42031e preenchido a partir do cabeçalho do pedido (fonte determinística): 254 — RBC AGÊNCIA DE VIAGENS E TURISMO LTDA. Valores anteriores: SupplierId=NULL, Name=NULL, Nif=NULL. Nenhum estado de workflow, valor ou P.O alterado.',
               SYSUTCDATETIME()
        FROM Requests r WHERE r.Id = @r1;

        PRINT 'REQ-031: repaired (supplier 254 copied to group, audit row written).';
        COMMIT TRANSACTION;
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('REQ-031: ERROR — rolled back: ', ERROR_MESSAGE());
END CATCH;

-- ────────────────────────────── REQ-14/07/2026-067 ──────────────────────────────
BEGIN TRANSACTION;
BEGIN TRY
    DECLARE @g2 UNIQUEIDENTIFIER = 'b8f9c46f-34e1-4b8c-a30e-85ba03e91b52';
    DECLARE @r2 UNIQUEIDENTIFIER = 'c59270d3-9dd4-40fa-90b5-5502171aa98c';
    DECLARE @s2 INT = 102;

    DECLARE @prevSupplierId2 INT, @prevName2 NVARCHAR(400), @prevNif2 NVARCHAR(100);
    SELECT @prevSupplierId2 = g.SupplierId, @prevName2 = g.SupplierNameSnapshot, @prevNif2 = g.SupplierNifSnapshot
    FROM RequestPoGroups g WHERE g.Id = @g2;
    PRINT CONCAT('REQ-067 previous values: SupplierId=', ISNULL(CAST(@prevSupplierId2 AS VARCHAR(20)),'NULL'),
                 ' Name=', ISNULL(@prevName2,'NULL'), ' Nif=', ISNULL(@prevNif2,'NULL'));

    IF NOT EXISTS (
        SELECT 1
        FROM RequestPoGroups g
        JOIN Requests r        ON r.Id = g.RequestId
        JOIN RequestStatuses rs ON rs.Id = r.StatusId
        JOIN Suppliers s       ON s.Id = r.SupplierId
        WHERE g.Id = @g2
          AND r.Id = @r2
          AND r.RequestNumber = 'REQ-14/07/2026-067'
          AND g.SupplierId IS NULL
          AND r.SupplierId = @s2
          AND s.IsActive = 1
          AND g.Status = 'PENDING'
          AND rs.Code = 'ADVANCE_PAYMENT_REQUIRED'
          AND g.PurchaseOrderNumber IS NULL
    )
    BEGIN
        PRINT 'REQ-067: PRECONDITIONS FAILED — nothing written, rolled back. Re-run the dry-run.';
        ROLLBACK TRANSACTION;
    END
    ELSE
    BEGIN
        UPDATE g
        SET g.SupplierId = r.SupplierId,
            g.SupplierNameSnapshot = s.Name,
            g.SupplierNifSnapshot = s.TaxId,
            g.UpdatedAtUtc = SYSUTCDATETIME(),
            g.UpdatedByUserId = @actor
        FROM RequestPoGroups g
        JOIN Requests r  ON r.Id = g.RequestId
        JOIN Suppliers s ON s.Id = r.SupplierId
        WHERE g.Id = @g2;

        INSERT INTO RequestStatusHistories (Id, RequestId, ActorUserId, ActionTaken, PreviousStatusId, NewStatusId, Comment, CreatedAtUtc)
        SELECT NEWID(), r.Id, @actor, 'DATA_INTEGRITY_REPAIR', r.StatusId, r.StatusId,
               '[Reparo de integridade — P.O Flow v2.229.12] Fornecedor do grupo b8f9c46f preenchido a partir do cabeçalho do pedido (fonte determinística): 102 — Gasp Transportes - Comércio & Prestação de Se. Valores anteriores: SupplierId=NULL, Name=NULL, Nif=NULL. Nenhum estado de workflow, valor ou P.O alterado.',
               SYSUTCDATETIME()
        FROM Requests r WHERE r.Id = @r2;

        PRINT 'REQ-067: repaired (supplier 102 copied to group, audit row written).';
        COMMIT TRANSACTION;
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT CONCAT('REQ-067: ERROR — rolled back: ', ERROR_MESSAGE());
END CATCH;

-- ============================================================================
-- ROLLBACK (only if the repair above was committed and must be reverted)
-- Restores the exact previous values captured by the dry-run (all NULL) and
-- removes the audit rows this script created. Run each block deliberately.
-- ============================================================================
-- BEGIN TRANSACTION;
--   UPDATE RequestPoGroups
--   SET SupplierId = NULL, SupplierNameSnapshot = NULL, SupplierNifSnapshot = NULL,
--       UpdatedAtUtc = SYSUTCDATETIME()
--   WHERE Id = 'bc42031e-2dc1-4ce6-a4ef-1ec960da8e7e' AND SupplierId = 254;
--   DELETE FROM RequestStatusHistories
--   WHERE RequestId = '949cbc05-ae7c-48e3-83d8-8f3c685176bc'
--     AND ActionTaken = 'DATA_INTEGRITY_REPAIR' AND Comment LIKE '%grupo bc42031e%';
-- COMMIT TRANSACTION;
--
-- BEGIN TRANSACTION;
--   UPDATE RequestPoGroups
--   SET SupplierId = NULL, SupplierNameSnapshot = NULL, SupplierNifSnapshot = NULL,
--       UpdatedAtUtc = SYSUTCDATETIME()
--   WHERE Id = 'b8f9c46f-34e1-4b8c-a30e-85ba03e91b52' AND SupplierId = 102;
--   DELETE FROM RequestStatusHistories
--   WHERE RequestId = 'c59270d3-9dd4-40fa-90b5-5502171aa98c'
--     AND ActionTaken = 'DATA_INTEGRITY_REPAIR' AND Comment LIKE '%grupo b8f9c46f%';
-- COMMIT TRANSACTION;
