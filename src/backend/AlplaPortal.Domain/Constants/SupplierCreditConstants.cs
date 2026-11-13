using System.Collections.Generic;

namespace AlplaPortal.Domain.Constants;

/// <summary>
/// Supplier account credit ("Créditos em conta corrente") applied to P.O. groups — stage 1
/// (QUOTATION, Pós-Pago and 100 % Antecipado, full and partial coverage).
///
/// Ownership is supplier × buying company × currency (D1). Purchasing records credits on the supplier
/// ficha under the Contracts role; the Buyer reserves part or all of a balance when registering the P.O.;
/// Finance applies the reserved credit (alone, or atomically with the bank remainder) and may reverse it.
/// Every money fact lives in the ledger (<c>SupplierCreditEntries</c>); a reservation is a HOLD, never a
/// movement, until it is applied.
/// </summary>
public static class SupplierCreditConstants
{
    /// <summary>CCS-000001 … (SystemCounters key <see cref="AccountNumberCounterKey"/>).</summary>
    public const string AccountNumberPrefix = "CCS-";
    public const string AccountNumberCounterKey = "SUPPLIER_CREDIT_ACCOUNT_NO";

    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 1000;
    public const int MinReversalReasonLength = 20;

    public static class AccountStatuses
    {
        public const string Active = "ACTIVE";
        public const string Closed = "CLOSED";
    }

    /// <summary>Ledger entry types. The SignedAmount carries the sign: inflows positive, outflows negative.</summary>
    public static class EntryTypes
    {
        /// <summary>Purchasing records a credit the supplier granted (positive).</summary>
        public const string CreditIn = "CREDIT_IN";
        /// <summary>Purchasing corrects the balance upward (positive, reason mandatory).</summary>
        public const string AdjustmentIn = "ADJUSTMENT_IN";
        /// <summary>Purchasing corrects the balance downward (negative, reason mandatory; never below the reserved total).</summary>
        public const string AdjustmentOut = "ADJUSTMENT_OUT";
        /// <summary>Finance applied reserved credit to a P.O. group stage (negative).</summary>
        public const string Application = "APPLICATION";
        /// <summary>Finance reversed an application (positive, equal to the applied amount only).</summary>
        public const string Reversal = "REVERSAL";
        /// <summary>Account closed with its remaining balance written off (negative).</summary>
        public const string CloseOut = "CLOSE_OUT";

        public static readonly string[] PurchasingTypes = { CreditIn, AdjustmentIn, AdjustmentOut };
        public static readonly string[] All = { CreditIn, AdjustmentIn, AdjustmentOut, Application, Reversal, CloseOut };

        public static string Label(string type) => type switch
        {
            CreditIn => "Crédito registado",
            AdjustmentIn => "Ajuste a favor",
            AdjustmentOut => "Ajuste a desfavor",
            Application => "Crédito aplicado",
            Reversal => "Aplicação revertida",
            CloseOut => "Conta encerrada",
            _ => type
        };
    }

    /// <summary>
    /// Reservation (hold) lifecycle. RESERVED → PARTIALLY_APPLIED → APPLIED; RESERVED → RELEASED;
    /// APPLIED | PARTIALLY_APPLIED → REVERSED. Hold = Amount − AppliedAmount while RESERVED or
    /// PARTIALLY_APPLIED; 0 in every other status.
    /// </summary>
    public static class ReservationStatuses
    {
        public const string Reserved = "RESERVED";
        public const string PartiallyApplied = "PARTIALLY_APPLIED";
        public const string Applied = "APPLIED";
        public const string Released = "RELEASED";
        public const string Reversed = "REVERSED";

        /// <summary>Statuses that hold or consumed money on the group: exactly one may exist per group (filtered unique index).</summary>
        public static readonly string[] Live = { Reserved, PartiallyApplied, Applied };
        /// <summary>Statuses that still count against the account's availability.</summary>
        public static readonly string[] Holding = { Reserved, PartiallyApplied };

        public static string Label(string status) => status switch
        {
            Reserved => "Reservado",
            PartiallyApplied => "Parcialmente aplicado",
            Applied => "Aplicado",
            Released => "Libertado",
            Reversed => "Revertido",
            _ => status
        };
    }

    /// <summary>Which stage of the group's payment schedule a row or an application settles.</summary>
    public static class Stages
    {
        /// <summary>Single-stage terms (Pós-Pago, 100 % Antecipado): the whole group is one stage.</summary>
        public const string Single = "SINGLE";
        /// <summary>Antecipado Parcial — the advance (stage 2).</summary>
        public const string Advance = "ADVANCE";
        /// <summary>Antecipado Parcial — the post-delivery balance (stage 2).</summary>
        public const string Balance = "BALANCE";
    }

    public static class ReversalKinds
    {
        /// <summary>C == T: the whole group was settled by credit; reopened in full by bank.</summary>
        public const string FullCoverage = "FULL_COVERAGE";
        /// <summary>C &lt; T and no cash was paid (stage 2).</summary>
        public const string PartialNoCash = "PARTIAL_NO_CASH";
        /// <summary>C &lt; T and a cash row is kept (stage 2).</summary>
        public const string PartialWithCash = "PARTIAL_WITH_CASH";
    }

    public static class ReleaseReasons
    {
        public const string GroupCancelled = "GROUP_CANCELLED";
        public const string CorrectionRemoved = "CORRECTION_REMOVED";
        public const string CorrectionChanged = "CORRECTION_CHANGED";
        public const string AbandonedRequest = "ABANDONED_REQUEST";
    }

    /// <summary>RequestStatusHistory.ActionTaken codes (Finance history and request timeline).</summary>
    public static class HistoryActions
    {
        public const string Reserved = "SUPPLIER_CREDIT_RESERVED";
        public const string ReservationChanged = "SUPPLIER_CREDIT_RESERVATION_CHANGED";
        public const string Released = "SUPPLIER_CREDIT_RELEASED";
        public const string Applied = "SUPPLIER_CREDIT_APPLIED";
        public const string Reversed = "SUPPLIER_CREDIT_REVERSED";

        public static readonly string[] All = { Reserved, ReservationChanged, Released, Applied, Reversed };

        public static string Label(string action) => action switch
        {
            Reserved => "Crédito reservado",
            ReservationChanged => "Reserva de crédito alterada",
            Released => "Reserva de crédito libertada",
            Applied => "Crédito aplicado",
            Reversed => "Crédito revertido",
            _ => action
        };
    }

    /// <summary>Correction wizard decision about an existing hold (keep | change | remove).</summary>
    public static class CorrectionActions
    {
        public const string Keep = "KEEP";
        public const string Change = "CHANGE";
        public const string Remove = "REMOVE";
        public static readonly string[] All = { Keep, Change, Remove };
    }

    /// <summary>ProblemDetails `code` extension values.</summary>
    public static class ErrorCodes
    {
        public const string Disabled = "SUPPLIER_CREDIT_DISABLED";
        public const string UnsupportedRequestType = "SUPPLIER_CREDIT_UNSUPPORTED_REQUEST_TYPE";
        public const string UnsupportedTerms = "SUPPLIER_CREDIT_UNSUPPORTED_TERMS";
        public const string AccountNotFound = "CREDIT_ACCOUNT_NOT_FOUND";
        public const string AccountInactive = "CREDIT_ACCOUNT_INACTIVE";
        public const string AccountExists = "CREDIT_ACCOUNT_EXISTS";
        public const string InternalCompanySupplier = "CREDIT_ACCOUNT_INTERNAL_COMPANY";
        public const string InvalidAmount = "CREDIT_AMOUNT_INVALID";
        public const string Insufficient = "INSUFFICIENT_SUPPLIER_CREDIT";
        public const string AdjustmentBelowReserved = "CREDIT_ADJUSTMENT_BELOW_RESERVED";
        public const string ReservationConflict = "CREDIT_RESERVATION_CONFLICT";
        public const string ApplicationRequired = "CREDIT_APPLICATION_REQUIRED";
        /// <summary>The group still owes a bank part: the credit is applied atomically by pay / confirm-advance, never alone.</summary>
        public const string AppliesWithBankPayment = "CREDIT_APPLIES_WITH_BANK_PAYMENT";
        public const string NothingToSettle = "CREDIT_NOTHING_TO_SETTLE";
        public const string PaymentAmountMismatch = "CREDIT_PAYMENT_AMOUNT_MISMATCH";
        public const string AlreadyApplied = "CREDIT_ALREADY_APPLIED";
        public const string NothingToApply = "CREDIT_NOTHING_TO_APPLY";
        public const string AlreadyReversed = "CREDIT_ALREADY_REVERSED";
        public const string ReversalBlockedByEvidence = "CREDIT_REVERSAL_BLOCKED_BY_EVIDENCE";
        public const string ReversalPartialUnsupported = "CREDIT_REVERSAL_PARTIAL_UNSUPPORTED";
        public const string ReversalNotApplied = "CREDIT_REVERSAL_NOT_APPLIED";
        public const string SelfReversalForbidden = "SELF_REVERSAL_FORBIDDEN";
        public const string FinanceScopeRequired = "FINANCE_SCOPE_REQUIRED";
        public const string NotResponsibleBuyer = "NOT_RESPONSIBLE_BUYER";
        public const string RequestOutOfScope = "REQUEST_OUT_OF_SCOPE";
        public const string ReleaseNotAllowed = "CREDIT_RELEASE_NOT_ALLOWED";
        public const string LegacySettlementBlocked = "CREDIT_BACKED_GROUP_SETTLEMENT_REQUIRED";
        public const string ReconciliationNotSupported = "RECONCILIATION_NOT_SUPPORTED_FOR_CREDIT_GROUPS";
        public const string ConcurrentChange = "CREDIT_CONCURRENT_CHANGE";
        public const string CorrectionActionInvalid = "CREDIT_CORRECTION_ACTION_INVALID";
    }

    /// <summary>Finance queue execution flow for a group whose current stage is settled by credit only.</summary>
    public const string CreditPaymentFlow = "CREDIT";

    /// <summary>Supported payment terms in stage 1 (single-stage).</summary>
    public static readonly IReadOnlyList<string> Stage1PaymentConditions = new[]
    {
        RequestConstants.PaymentConditions.PostPaid,
        RequestConstants.PaymentConditions.AdvanceFull,
    };
}
