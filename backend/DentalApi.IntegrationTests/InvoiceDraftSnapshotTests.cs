using DentalApi.Services;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class InvoiceDraftSnapshotTests
{
    private static readonly InvoiceEncounterIdentity Encounter = new(101, 301);
    private static readonly Guid Tariff = Guid.Parse("29f45b52-1efd-4b19-b49e-5845d0d7ffa5");
    private static readonly Guid Approval = Guid.Parse("e4cdad36-bb25-4530-aced-4dd77ca5658b");

    private static ServerApprovedInvoicePricing SyntheticApproval() =>
        new(Encounter, Tariff, Approval, InvoiceAmountSnapshot.Calculate("XTS", 2, [new(150, 10, 7)]));

    [Fact]
    public void Captures_the_exact_approved_scope_revision_reference_and_amounts()
    {
        var pricing = SyntheticApproval();
        var draft = InvoiceDraftSnapshot.Create(Encounter, pricing);

        Assert.Equal(Encounter, draft.Encounter);
        Assert.Equal(Tariff, draft.TariffRevisionId);
        Assert.Equal(Approval, draft.TariffApprovalId);
        Assert.Same(pricing.Amount, draft.Amount);
        Assert.Equal("XTS", draft.Amount.Currency);
        Assert.Equal(2, draft.Amount.Scale);
        Assert.Equal(147L, draft.Amount.TotalUnits);
    }

    [Theory]
    [InlineData(0, 301)]
    [InlineData(101, 0)]
    [InlineData(-1, 301)]
    [InlineData(101, -1)]
    public void Rejects_invalid_server_encounter_scope(int clinicId, int appointmentId) =>
        Assert.Throws<ArgumentOutOfRangeException>("encounter",
            () => InvoiceDraftSnapshot.Create(new(clinicId, appointmentId), SyntheticApproval()));

    [Theory]
    [InlineData(102, 301)]
    [InlineData(101, 302)]
    [InlineData(0, 0)]
    public void Rejects_approval_from_another_clinic_or_encounter(int clinicId, int appointmentId) =>
        Assert.Throws<ArgumentException>("pricing",
            () => InvoiceDraftSnapshot.Create(Encounter, SyntheticApproval() with { Encounter = new(clinicId, appointmentId) }));

    [Fact]
    public void Requires_explicit_tariff_approval_revision_and_price_snapshot()
    {
        var pricing = SyntheticApproval();
        Assert.Throws<ArgumentNullException>("pricing", () => InvoiceDraftSnapshot.Create(Encounter, null!));
        Assert.Throws<ArgumentException>("pricing",
            () => InvoiceDraftSnapshot.Create(Encounter, pricing with { TariffRevisionId = Guid.Empty }));
        Assert.Throws<ArgumentException>("pricing",
            () => InvoiceDraftSnapshot.Create(Encounter, pricing with { TariffApprovalId = Guid.Empty }));
        Assert.Throws<ArgumentException>("pricing",
            () => InvoiceDraftSnapshot.Create(Encounter, pricing with { Amount = null! }));
    }

    [Fact]
    public void Revised_pricing_and_changes_to_original_input_lines_cannot_modify_a_prior_draft()
    {
        var line = new InvoiceChargeLine(150, 10, 7);
        InvoiceChargeLine[] sourceLines = [line];
        var first = SyntheticApproval() with { Amount = InvoiceAmountSnapshot.Calculate("XTS", 2, sourceLines) };
        var original = InvoiceDraftSnapshot.Create(Encounter, first);

        sourceLines[0] = new(350, 30, 11);
        var next = first with
        {
            TariffRevisionId = Guid.Parse("e67f6c93-4bd7-4f76-86e0-9e99e073888d"),
            TariffApprovalId = Guid.Parse("23a6d684-baa4-4a3a-bb58-c3cec2ad5680"),
            Amount = InvoiceAmountSnapshot.Calculate("XTS", 2, sourceLines)
        };
        var revised = InvoiceDraftSnapshot.Create(Encounter, next);

        Assert.Equal(Tariff, original.TariffRevisionId);
        Assert.Equal(Approval, original.TariffApprovalId);
        Assert.Equal(line, Assert.Single(original.Amount.Lines));
        Assert.Equal(147L, original.Amount.TotalUnits);
        Assert.NotEqual(original.TariffRevisionId, revised.TariffRevisionId);
        Assert.NotEqual(original.TariffApprovalId, revised.TariffApprovalId);
        Assert.Equal(331L, revised.Amount.TotalUnits);
    }
}
