using System.Collections.Generic;

namespace AlplaPortal.Domain.Constants;

/// <summary>
/// P.O. group cancellation / return to quotation after final approval (phase 1, docs/PO_GROUP_CANCELLATION.md).
/// Every value is a persisted code: never rename once released.
/// </summary>
public static class PoGroupCancellationConstants
{
    /// <summary>Minimum length of the Buyer's explanation and of a Finance rejection note.</summary>
    public const int MinJustificationLength = 20;
    public const int MaxJustificationLength = 1000;
    public const int MaxEcfReferenceLength = 100;

    /// <summary>What happens to the group's line items.</summary>
    public static class Kinds
    {
        /// <summary>Items go back to the quotation pool; a NEW batch and NEW area/final approvals follow.</summary>
        public const string RestartQuotation = "RESTART_QUOTATION";
        /// <summary>Items are closed as cancelled after approval; nothing is bought for them.</summary>
        public const string CancelItems = "CANCEL_ITEMS";
        public static readonly string[] All = { RestartQuotation, CancelItems };
    }

    /// <summary>Why the Buyer cancelled (D10). The explanation text is always mandatory in addition.</summary>
    public static class ReasonCodes
    {
        public const string SupplierCancelled = "SUPPLIER_CANCELLED";
        public const string OrderCancelledByCompany = "ORDER_CANCELLED_BY_COMPANY";
        public const string PriceOrTermsChanged = "PRICE_OR_TERMS_CHANGED";
        public const string WrongSupplierOrItem = "WRONG_SUPPLIER_OR_ITEM";
        public const string DuplicateOrder = "DUPLICATE_ORDER";
        public const string FinanceReturn = "FINANCE_RETURN";
        public const string Other = "OTHER";

        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
        {
            [SupplierCancelled] = "Fornecedor cancelou / não consegue fornecer",
            [OrderCancelledByCompany] = "Necessidade cancelada pela empresa",
            [PriceOrTermsChanged] = "Preço ou condições alterados — nova cotação necessária",
            [WrongSupplierOrItem] = "Fornecedor ou item errado na aprovação",
            [DuplicateOrder] = "Encomenda duplicada",
            [FinanceReturn] = "Devolvido por Finanças sem correção possível",
            [Other] = "Outro (explicar)",
        };

        public static bool IsValid(string? code) => code != null && Labels.ContainsKey(code);
        public static string Label(string? code) => code != null && Labels.TryGetValue(code, out var l) ? l : (code ?? "—");
    }

    /// <summary>The Buyer's HUMAN declaration about the external ECF/P.O. document. Never verified by the Portal.</summary>
    public static class EcfStatuses
    {
        /// <summary>No P.O. was ever registered on the group: there is no external document to annul.</summary>
        public const string NotApplicable = "NOT_APPLICABLE";
        /// <summary>The Buyer states the ECF still has to be annulled outside the Portal.</summary>
        public const string PendingExternal = "PENDING_EXTERNAL";
        /// <summary>The Buyer states the ECF was already annulled outside the Portal (reference mandatory).</summary>
        public const string DeclaredCancelled = "DECLARED_CANCELLED";
        /// <summary>The Buyer does not know the external state.</summary>
        public const string Unknown = "UNKNOWN";
        public static readonly string[] Declarable = { PendingExternal, DeclaredCancelled, Unknown };
    }

    /// <summary>Finance's SEPARATE confirmation of the external annulment (D4/D12).</summary>
    public static class FinanceConfirmationStatuses
    {
        public const string NotRequired = "NOT_REQUIRED";
        public const string Pending = "PENDING";
        public const string Confirmed = "CONFIRMED";
        public const string Rejected = "REJECTED";
    }

    /// <summary>Append-only audit of every human statement about the ECF.</summary>
    public static class EcfEventTypes
    {
        public const string BuyerDeclared = "BUYER_DECLARED";
        public const string BuyerUpdated = "BUYER_UPDATED";
        public const string FinanceConfirmed = "FINANCE_CONFIRMED";
        public const string FinanceRejected = "FINANCE_REJECTED";
        public const string NotificationResent = "NOTIFICATION_RESENT";
    }

    /// <summary>Finance return kind recorded on the group (RequestPoGroup.LastFinanceReturnKind). NULL = legacy, not classified.</summary>
    public static class FinanceReturnKinds
    {
        public const string PoCorrection = "PO_CORRECTION";
        public const string OrderCancellationRequested = "ORDER_CANCELLATION_REQUESTED";
        public static readonly string[] All = { PoCorrection, OrderCancellationRequested };
        public static string Label(string? kind) => kind switch
        {
            PoCorrection => "Corrigir P.O.",
            OrderCancellationRequested => "Cancelamento solicitado — não corrigir",
            _ => "Tipo não classificado (devolução anterior à versão)",
        };
    }

    /// <summary>Optional structured reason of a Finance return (free text stays mandatory).</summary>
    public static class FinanceReturnReasonCodes
    {
        public const string WrongAmount = "WRONG_AMOUNT";
        public const string WrongSupplier = "WRONG_SUPPLIER";
        public const string DocumentIllegible = "DOCUMENT_ILLEGIBLE";
        public const string PaymentConditions = "PAYMENT_CONDITIONS";
        public const string OrderCancelled = "ORDER_CANCELLED";
        public const string SupplierCancelled = "SUPPLIER_CANCELLED";
        public const string Other = "OTHER";
        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
        {
            [WrongAmount] = "Valor da P.O incorreto", [WrongSupplier] = "Fornecedor incorreto", [DocumentIllegible] = "Documento ilegível / incompleto",
            [PaymentConditions] = "Condições de pagamento a corrigir", [OrderCancelled] = "Pedido/encomenda cancelado", [SupplierCancelled] = "Fornecedor cancelou", [Other] = "Outro",
        };
        public static bool IsValid(string? code) => code != null && Labels.ContainsKey(code);
        public static string Label(string? code) => code != null && Labels.TryGetValue(code, out var l) ? l : (code ?? "—");
    }

    /// <summary>RequestStatusHistory.ActionTaken values written by the feature.</summary>
    public static class HistoryActions
    {
        public const string GroupCancelled = "PO_GROUP_CANCELLED";
        public const string ItemsReleased = "PO_GROUP_ITEMS_RELEASED";
        public const string ItemsClosed = "PO_GROUP_ITEMS_CLOSED";
        public const string EcfDeclared = "PO_CANCELLATION_ECF_DECLARED";
        public const string FinanceConfirmed = "PO_CANCELLATION_CONFIRMED";
        public const string FinanceRejected = "PO_CANCELLATION_REJECTED";
        public const string NotificationResent = "PO_CANCELLATION_NOTIFICATION_RESENT";
        public const string PoNumberReused = "PO_NUMBER_REUSED_AFTER_CANCELLATION";
    }

    /// <summary>Machine-readable ProblemDetails codes (Extensions["code"]).</summary>
    public static class ErrorCodes
    {
        public const string RequestTypeNotSupported = "REQUEST_TYPE_NOT_SUPPORTED";
        public const string GroupStatusNotCancellable = "GROUP_STATUS_NOT_CANCELLABLE";
        public const string PaymentScheduledActive = "PAYMENT_SCHEDULED_ACTIVE";
        public const string PaymentOrReceiptEvidence = "PAYMENT_OR_RECEIPT_EVIDENCE";
        public const string OrderedLineRequiresInvestigation = "ORDERED_LINE_REQUIRES_INVESTIGATION";
        public const string GroupLinkageInconsistent = "GROUP_LINKAGE_INCONSISTENT";
        public const string GroupAlreadyCancelled = "GROUP_ALREADY_CANCELLED";
        public const string JustificationRequired = "JUSTIFICATION_REQUIRED";
        public const string ReasonCodeInvalid = "REASON_CODE_INVALID";
        public const string KindInvalid = "KIND_INVALID";
        public const string EcfDeclarationRequired = "ECF_DECLARATION_REQUIRED";
        public const string EcfReferenceRequired = "ECF_REFERENCE_REQUIRED";
        public const string RequestTerminal = "REQUEST_TERMINAL";
        public const string NotResponsibleBuyer = "NOT_RESPONSIBLE_BUYER";
        public const string PriorEcfConfirmationPending = "PRIOR_ECF_CONFIRMATION_PENDING";
        public const string PreviousPoNumberReused = "PREVIOUS_PO_NUMBER_REUSED";
        public const string CancellationConcurrentChange = "CANCELLATION_CONCURRENT_CHANGE";
        public const string FinanceConfirmationNotRequired = "FINANCE_CONFIRMATION_NOT_REQUIRED";
        public const string FinanceConfirmationAlreadyDone = "FINANCE_CONFIRMATION_ALREADY_DONE";
        public const string FinanceScopeRequired = "FINANCE_SCOPE_REQUIRED";
        public const string SelfConfirmationForbidden = "SELF_CONFIRMATION_FORBIDDEN";
        public const string DeclarationLocked = "DECLARATION_LOCKED";
        public const string ResendTooSoon = "RESEND_TOO_SOON";
    }

    /// <summary>Minimum interval between two notification resends of one cancellation (accidental duplicate guard).</summary>
    public const int ResendMinimumIntervalMinutes = 2;
}
