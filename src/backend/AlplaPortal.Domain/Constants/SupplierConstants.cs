namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Constants for the Supplier Registration (Ficha de Fornecedor) lifecycle.
/// Mirrors the ContractConstants pattern for consistency.
/// </summary>
public static class SupplierConstants
{
    public static class Statuses
    {
        /// <summary>Created from QuickSupplierModal or manually. Minimal data.</summary>
        public const string Draft = "DRAFT";

        /// <summary>Assigned for data completion. Not yet ready for approval.</summary>
        public const string PendingCompletion = "PENDING_COMPLETION";

        /// <summary>All mandatory fields/docs complete. Awaiting DAF → DG approval.</summary>
        public const string PendingApproval = "PENDING_APPROVAL";

        /// <summary>Returned by DAF or DG with mandatory comment. Requires correction and resubmission.</summary>
        public const string AdjustmentRequested = "ADJUSTMENT_REQUESTED";

        /// <summary>Fully approved and operational. No restrictions.</summary>
        public const string Active = "ACTIVE";

        /// <summary>Temporarily deactivated. Can be reactivated.</summary>
        public const string Suspended = "SUSPENDED";

        /// <summary>Permanently blocked. Admin-only reactivation.</summary>
        public const string Blocked = "BLOCKED";
    }

    public static class HistoryEventTypes
    {
        public const string Created = "CREATED";
        public const string FieldUpdated = "FIELD_UPDATED";
        public const string StatusChanged = "STATUS_CHANGED";
        public const string DocumentUploaded = "DOCUMENT_UPLOADED";
        public const string DocumentRemoved = "DOCUMENT_REMOVED";
        public const string SubmittedForApproval = "SUBMITTED_FOR_APPROVAL";
        public const string DafApproved = "DAF_APPROVED";
        public const string DgApproved = "DG_APPROVED";
        public const string AdjustmentRequested = "ADJUSTMENT_REQUESTED";
        public const string Activated = "ACTIVATED";
        public const string Suspended = "SUSPENDED";
        public const string Blocked = "BLOCKED";
        public const string Reactivated = "REACTIVATED";
    }

    public static class DocumentTypes
    {
        public const string Contribuinte = "CONTRIBUINTE";
        public const string CertidaoComercial = "CERTIDAO_COMERCIAL";
        public const string DiarioRepublica = "DIARIO_REPUBLICA";
        public const string Alvara = "ALVARA";
    }

    /// <summary>
    /// Required document types that block submission for approval (hard requirement).
    /// Alvará is NOT included — it is recommended but not a hard blocker.
    /// </summary>
    public static readonly string[] MandatoryDocumentTypes = new[]
    {
        DocumentTypes.Contribuinte,
        DocumentTypes.CertidaoComercial,
        DocumentTypes.DiarioRepublica,
    };

    /// <summary>
    /// All document types displayed in the ficha checklist (including optional ones).
    /// </summary>
    public static readonly string[] AllDocumentTypes = new[]
    {
        DocumentTypes.Contribuinte,
        DocumentTypes.CertidaoComercial,
        DocumentTypes.DiarioRepublica,
        DocumentTypes.Alvara,
    };

    /// <summary>
    /// Supplier registration status → label (PT-PT) mapping for API responses.
    /// </summary>
    public static readonly Dictionary<string, string> StatusLabels = new()
    {
        [Statuses.Draft] = "Rascunho",
        [Statuses.PendingCompletion] = "Pendente de Preenchimento",
        [Statuses.PendingApproval] = "Pendente de Aprovação",
        [Statuses.AdjustmentRequested] = "Reajuste Solicitado",
        [Statuses.Active] = "Ativo",
        [Statuses.Suspended] = "Suspenso",
        [Statuses.Blocked] = "Bloqueado",
    };

    /// <summary>
    /// Allowed status transitions for the supplier registration lifecycle.
    /// </summary>
    public static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        [Statuses.Draft] = new[] { Statuses.PendingCompletion, Statuses.PendingApproval },
        [Statuses.PendingCompletion] = new[] { Statuses.Draft, Statuses.PendingApproval },
        [Statuses.PendingApproval] = new[] { Statuses.Active, Statuses.AdjustmentRequested },
        [Statuses.AdjustmentRequested] = new[] { Statuses.PendingApproval },
        [Statuses.Active] = new[] { Statuses.Suspended, Statuses.Blocked },
        [Statuses.Suspended] = new[] { Statuses.Active, Statuses.Blocked },
        [Statuses.Blocked] = new[] { Statuses.Active },
    };

    /// <summary>
    /// Role mapping for supplier approval workflow.
    /// DAF (Direcção Administrativa e Financeira) = Area Approver role.
    /// DG (Direcção Geral) = Final Approver role.
    /// </summary>
    public static class ApprovalRoles
    {
        /// <summary>DAF approval mapped to existing "Area Approver" role (RoleId = 5).</summary>
        public const string DafRole = "Area Approver";

        /// <summary>DG approval mapped to existing "Final Approver" role (RoleId = 6).</summary>
        public const string DgRole = "Final Approver";
    }
}
