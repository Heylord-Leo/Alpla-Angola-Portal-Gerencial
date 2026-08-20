-- ============================================================================
-- PO-FLOW REPAIR — SUPPLIER BACKFILL DRY-RUN (READ-ONLY)
-- ============================================================================
-- Lists every RequestPoGroup with SupplierId IS NULL together with its owning
-- request's structured supplier (the ONLY deterministic repair source) so the
-- PoSupplierBackfillRule decision can be reviewed BEFORE any write.
--
--   Population A = request header HAS a supplier  -> candidate for CopyHeaderSupplier
--   Population B = request header supplier NULL   -> RequiresManualConfirmation (never guessed)
--
-- The authoritative PlannedAction column is produced by the C# classifier
-- (AlplaPortal.Domain.Services.PoSupplierBackfillRule — the exact shipped rule);
-- the CASE below mirrors it for standalone SQL review only.
--
-- SAFETY: SELECT only. No UPDATE/INSERT/DELETE anywhere in this file.
-- Target: LocalDB clone [Portal-Gerencial-Dev-ProdClone] (or TEST/PROD read-only).
-- ============================================================================

SELECT
    r.RequestNumber,
    g.Id                                   AS GroupId,
    g.Status                               AS GroupStatus,
    rs.Code                                AS RequestStatus,
    g.SupplierNameSnapshot                 AS GroupSupplierSnapshot,
    r.SupplierId                           AS RequestSupplierId,
    s.Name                                 AS ResolvedSupplierName,
    s.TaxId                                AS ResolvedSupplierNif,
    s.IsActive                             AS SupplierIsActive,
    c.Name                                 AS Company,
    CASE
        WHEN g.Status NOT IN ('PENDING', 'WAITING_PO', 'WAITING_PO_CORRECTION')
            THEN 'SKIP (estado nao reparavel - P.O. ja avancou)'
        WHEN r.SupplierId IS NULL
            THEN 'REQUIRES_MANUAL_CONFIRMATION (Populacao B - sem fornecedor estruturado)'
        WHEN s.Id IS NULL
            THEN 'SKIP (fornecedor do cabecalho inexistente)'
        WHEN s.IsActive = 0
            THEN 'REQUIRES_MANUAL_CONFIRMATION (fornecedor inativo)'
        ELSE 'COPY_HEADER_SUPPLIER (Populacao A - deterministico)'
    END                                    AS PlannedAction_SqlMirror
FROM RequestPoGroups g
JOIN Requests        r  ON r.Id = g.RequestId
JOIN RequestStatuses rs ON rs.Id = r.StatusId
LEFT JOIN Suppliers  s  ON s.Id = r.SupplierId
LEFT JOIN Companies  c  ON c.Id = r.CompanyId
WHERE g.SupplierId IS NULL
ORDER BY r.RequestNumber;
