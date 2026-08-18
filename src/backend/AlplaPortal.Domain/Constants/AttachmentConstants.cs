namespace AlplaPortal.Domain.Constants;

public static class AttachmentConstants
{
    public static class Types
    {
        public const string Proforma = "PROFORMA";
        /// <summary>Supplier quotation/orçamento document uploaded by the Buyer quotation flow.
        /// Distinct from <see cref="Proforma"/>, which belongs to the payment/P.O. workflow —
        /// historical quotation documents stamped PROFORMA are NEVER reclassified automatically.</summary>
        public const string Quotation = "QUOTATION";
        public const string PurchaseOrder = "PO";
        public const string PaymentSchedule = "PAYMENT_SCHEDULE";
        public const string PaymentProof = "PAYMENT_PROOF";
        public const string Receipt = "RECEIPT";

        // ── Buy-to-Pay (Advance Payment / Reconciliation) ──
        public const string AdvancePaymentProof = "ADVANCE_PAYMENT_PROOF";
        public const string CreditNote = "CREDIT_NOTE";
        public const string DebitNote = "DEBIT_NOTE";
    }
}
