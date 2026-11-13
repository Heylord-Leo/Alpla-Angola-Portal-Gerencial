namespace AlplaPortal.Domain.Configuration;

/// <summary>
/// Activation of supplier account credit on P.O. groups. Bound from the "SupplierCredit" section.
///
/// <para><see cref="NewReservationsEnabled"/> governs ONLY the creation or modification of a reservation
/// (first registration with credit, increase/decrease during a correction). Everything keyed on a group's
/// own credit facts stays active regardless of the flag: Finance application, the strict bank-amount rule,
/// cancellation release, keep/remove during correction, reversal, the legacy-endpoint guards and the ledger
/// reads — an in-flight credit-backed group must always have a supported path to settlement, release or
/// reversal, and must never become payable in full by bank because a switch was turned off.</para>
///
/// <para>Committed default: false in every environment. TEST enables it only with the complete stage 1
/// workflow deployed; PROD in the single rollout after both TEST stages are accepted.</para>
/// </summary>
public class SupplierCreditOptions
{
    public const string SectionName = "SupplierCredit";

    public bool NewReservationsEnabled { get; set; } = false;
}
