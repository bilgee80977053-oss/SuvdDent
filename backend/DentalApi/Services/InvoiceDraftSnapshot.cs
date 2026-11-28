namespace DentalApi.Services;

// In the existing clinical model, an encounter is identified by its appointment within a clinic.
// No patient, clinician, contact, or clinical details are carried into pricing.
internal readonly record struct InvoiceEncounterIdentity(int ClinicId, int AppointmentId)
{
    public bool IsValid => ClinicId > 0 && AppointmentId > 0;
}

/// <summary>
/// Future trusted server-side pricing output: the exact amount snapshot was priced for this
/// encounter under the stated immutable revision and an explicit, recorded approval.
/// The type does not authenticate provenance, authorize access, or decide when an approval
/// is effective. A patient estimate, request fields, or a catalog read are not valid sources.
/// </summary>
internal sealed record ServerApprovedInvoicePricing(
    InvoiceEncounterIdentity Encounter,
    Guid TariffRevisionId,
    Guid TariffApprovalId,
    InvoiceAmountSnapshot Amount);

/// <summary>
/// Immutable draft pricing context only, not an issued document, a balance, or evidence of
/// payment. Revision and approval are opaque references, not invoice numbers.
/// </summary>
internal sealed class InvoiceDraftSnapshot
{
    private InvoiceDraftSnapshot(InvoiceEncounterIdentity encounter, ServerApprovedInvoicePricing pricing)
    {
        Encounter = encounter;
        TariffRevisionId = pricing.TariffRevisionId;
        TariffApprovalId = pricing.TariffApprovalId;
        Amount = pricing.Amount;
    }

    public InvoiceEncounterIdentity Encounter { get; }
    public Guid TariffRevisionId { get; }
    public Guid TariffApprovalId { get; }
    public InvoiceAmountSnapshot Amount { get; }

    public static InvoiceDraftSnapshot Create(InvoiceEncounterIdentity encounter, ServerApprovedInvoicePricing pricing)
    {
        if (!encounter.IsValid)
            throw new ArgumentOutOfRangeException(nameof(encounter), "A clinic and its encounter must be specified.");
        ArgumentNullException.ThrowIfNull(pricing);
        if (pricing.Encounter != encounter)
            throw new ArgumentException("Pricing approval must be scoped to the same clinic and encounter.", nameof(pricing));
        if (pricing.TariffRevisionId == Guid.Empty || pricing.TariffApprovalId == Guid.Empty || pricing.Amount is null)
            throw new ArgumentException("An explicit tariff revision, its approval reference, and its exact amounts are required.", nameof(pricing));

        return new(encounter, pricing);
    }
}
