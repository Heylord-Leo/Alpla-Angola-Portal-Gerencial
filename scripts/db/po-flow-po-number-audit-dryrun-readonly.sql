-- ============================================================================
-- PO-FLOW REPAIR — HISTORICAL P.O. NUMBER AUDIT DRY-RUN (READ-ONLY)
-- ============================================================================
-- Dumps every stored RequestPoGroups.PurchaseOrderNumber with the context needed
-- to classify it (own supplier NIF, company, request status). The authoritative
-- DetectedFamily / DetectedPoCanonical / SuspicionReason / RepairConfidence /
-- SafeToRepair columns are produced by the C# classifier running the exact
-- shipped parser (AlplaPortal.Domain.Services.PrimaveraPoReference) — SQL only
-- pre-flags the obvious shapes for standalone review.
--
-- Suspicion shapes flagged here:
--   NIF_AS_PO       stored value == the group's own supplier NIF (digits only)
--   NIF_SHAPED      bare 10-digit numeric (Angolan NIF shape)
--   FAMILYLESS_REF  year/sequence with no ECF family (e.g. '2026/107') — family
--                   must NEVER be invented; repair requires the source document
--   BARE_NUMERIC    digits only, not NIF-shaped (e.g. '26203249')
--
-- SAFETY: SELECT only. No UPDATE/INSERT/DELETE anywhere in this file.
-- Target: LocalDB clone [Portal-Gerencial-Dev-ProdClone] (or TEST/PROD read-only).
-- ============================================================================

SELECT
    r.RequestNumber,
    c.Name                                  AS Company,
    g.Id                                    AS GroupId,
    g.PurchaseOrderNumber                   AS StoredPoNumber,
    g.Status                                AS GroupStatus,
    rs.Code                                 AS RequestStatus,
    COALESCE(s.Name,  g.SupplierNameSnapshot) AS SupplierName,
    COALESCE(s.TaxId, g.SupplierNifSnapshot)  AS SupplierNif,
    CASE
        WHEN REPLACE(REPLACE(REPLACE(g.PurchaseOrderNumber, ' ', ''), '.', ''), '-', '')
             = COALESCE(s.TaxId, g.SupplierNifSnapshot)
            THEN 'NIF_AS_PO (== NIF do proprio fornecedor)'
        WHEN g.PurchaseOrderNumber NOT LIKE '%[^0-9]%' AND LEN(g.PurchaseOrderNumber) = 10
            THEN 'NIF_SHAPED (10 digitos)'
        WHEN g.PurchaseOrderNumber NOT LIKE '%[^0-9]%'
            THEN 'BARE_NUMERIC'
        WHEN g.PurchaseOrderNumber LIKE '20[0-9][0-9]/%'
             AND UPPER(g.PurchaseOrderNumber) NOT LIKE '%ECF%'
            THEN 'FAMILYLESS_REF (ano/sequencia sem familia ECF)'
        ELSE ''
    END                                     AS SqlPreFlag
FROM RequestPoGroups g
JOIN Requests        r  ON r.Id = g.RequestId
JOIN RequestStatuses rs ON rs.Id = r.StatusId
LEFT JOIN Suppliers  s  ON s.Id = g.SupplierId
LEFT JOIN Companies  c  ON c.Id = r.CompanyId
WHERE g.PurchaseOrderNumber IS NOT NULL
  AND LTRIM(RTRIM(g.PurchaseOrderNumber)) <> ''
ORDER BY r.RequestNumber;
