using DentalApi.Services;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class SettlementEvidenceComparisonTests
{
    private static readonly SettlementTransactionKey Transaction = new("sandbox-provider", "seller-a", "transaction-1");
    private static readonly InvoiceAmountSnapshot Amount = InvoiceAmountSnapshot.Calculate("XTS", 2, [new(150, 10, 7)]);
    private static readonly ExpectedSettlement Expected = new("invoice-1", Transaction, Amount);
    private static readonly ServerVerifiedCapture Verified = new("invoice-1", Transaction, "XTS", 2, 147);
    private static readonly SettlementUseObservation NotObserved = new(Transaction, SettlementUseState.NotObserved);

    [Fact]
    public void Exact_verified_capture_is_only_a_candidate_when_correct_replay_lookup_has_no_match()
    {
        Assert.Equal(SettlementComparisonResult.Candidate,
            SettlementEvidenceComparison.Compare(Expected, Verified, NotObserved));
    }

    [Fact]
    public void Missing_verified_capture_is_not_evidence_even_with_a_replay_lookup()
    {
        Assert.Equal(SettlementComparisonResult.MissingVerifiedCapture,
            SettlementEvidenceComparison.Compare(Expected, null, NotObserved));
    }

    [Fact]
    public void Every_capture_association_and_amount_component_must_match_exactly()
    {
        var mismatches = new[]
        {
            Verified with { InvoiceId = "invoice-2" },
            Verified with { InvoiceId = "Invoice-1" },
            Verified with { InvoiceId = "invoice-1 " },
            Verified with { Transaction = Transaction with { ProviderScope = "different-sandbox-provider" } },
            Verified with { Transaction = Transaction with { SellerId = "seller-b" } },
            Verified with { Transaction = Transaction with { TransactionId = "transaction-2" } },
            Verified with { Currency = "XAU" },
            Verified with { Currency = "xts" },
            Verified with { Scale = 0 },
            Verified with { CapturedUnits = 146 },
            Verified with { CapturedUnits = 148 },
            Verified with { CapturedUnits = -147 }
        };

        foreach (var mismatch in mismatches)
            Assert.Equal(SettlementComparisonResult.CaptureMismatch,
                SettlementEvidenceComparison.Compare(Expected, mismatch, NotObserved));
    }

    [Fact]
    public void Observed_transaction_is_never_a_candidate_even_if_capture_matches()
    {
        Assert.Equal(SettlementComparisonResult.ReplayDetected,
            SettlementEvidenceComparison.Compare(Expected, Verified,
                new(Transaction, SettlementUseState.AlreadyObserved)));
    }

    [Fact]
    public void Absent_unknown_unrecognized_or_wrongly_scoped_replay_status_fails_closed()
    {
        SettlementUseObservation?[] unknown =
        [
            null,
            new(Transaction, SettlementUseState.Unknown),
            new(Transaction, (SettlementUseState)99),
            NotObserved with { Transaction = Transaction with { ProviderScope = "sandbox-provider-2" } },
            NotObserved with { Transaction = Transaction with { SellerId = "seller-b" } },
            NotObserved with { Transaction = Transaction with { TransactionId = "transaction-2" } }
        ];

        foreach (var observation in unknown)
            Assert.Equal(SettlementComparisonResult.ReplayStatusUnknown,
                SettlementEvidenceComparison.Compare(Expected, Verified, observation));
    }

    [Fact]
    public void Default_or_incomplete_associations_and_zero_total_are_out_of_scope()
    {
        ExpectedSettlement[] invalid =
        [
            default,
            Expected with { InvoiceId = "" },
            Expected with { Transaction = Transaction with { ProviderScope = " " } },
            Expected with { Transaction = Transaction with { SellerId = "" } },
            Expected with { Transaction = Transaction with { TransactionId = "" } },
            Expected with { Amount = InvoiceAmountSnapshot.Calculate("XTS", 2, [new(150, 150, 0)]) }
        ];

        foreach (var expected in invalid)
            Assert.Equal(SettlementComparisonResult.InvalidExpectation,
                SettlementEvidenceComparison.Compare(expected, Verified, NotObserved));
    }
}
