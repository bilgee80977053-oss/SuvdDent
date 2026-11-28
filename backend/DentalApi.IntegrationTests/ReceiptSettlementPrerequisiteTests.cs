using DentalApi.Services;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class ReceiptSettlementPrerequisiteTests
{
    private const string ExpectedInvoiceId = "synthetic-invoice-1";
    private static readonly InvoiceEncounterIdentity Encounter = new(101, 301);
    private static readonly Guid Revision = Guid.Parse("21a97189-639e-4bbc-b921-5efd06ec8efc");
    private static readonly Guid Approval = Guid.Parse("5245bb04-9eda-4817-8fb6-4d5a893ec606");
    private static readonly Guid Ledger = Guid.Parse("edfc66bc-2068-4732-9ac5-a02ae1d1268b");
    private static readonly SettlementTransactionKey Transaction = new("synthetic-scope", "synthetic-seller", "synthetic-transaction");

    [Fact]
    public void Separately_materialized_exact_draft_and_committed_amount_satisfy_only_the_prerequisite()
    {
        var draft = Draft();
        var independentlyMaterialized = Draft();
        Assert.NotSame(draft, independentlyMaterialized);
        Assert.Equal(ReceiptSettlementPrerequisiteResult.CommitmentPrerequisiteSatisfied,
            ReceiptSettlementPrerequisite.Evaluate(ExpectedInvoiceId, draft, Committed(independentlyMaterialized)));
    }

    [Fact]
    public void Missing_unknown_candidate_reservation_and_unrecognized_stage_are_not_commitment_proof()
    {
        var draft = Draft();
        Assert.Equal(ReceiptSettlementPrerequisiteResult.VerifiedCommitRequired,
            ReceiptSettlementPrerequisite.Evaluate(ExpectedInvoiceId, draft, null));
        foreach (var stage in new[] { ReceiptSettlementStage.Unknown, ReceiptSettlementStage.Candidate,
                     ReceiptSettlementStage.Reserved, (ReceiptSettlementStage)99 })
            Assert.Equal(ReceiptSettlementPrerequisiteResult.VerifiedCommitRequired,
                ReceiptSettlementPrerequisite.Evaluate(ExpectedInvoiceId, draft, Committed(draft) with { Stage = stage }));
    }

    [Fact]
    public void Committed_stage_needs_ledger_identity_transaction_and_a_bound_draft()
    {
        var draft = Draft();
        var evidence = Committed(draft);
        foreach (var incomplete in new[]
        {
            evidence with { CommittedLedgerEntryId = Guid.Empty },
            evidence with { BoundDraft = null },
            evidence with { InvoiceId = " " },
            evidence with { Transaction = Transaction with { SellerId = " " } },
            evidence with { Transaction = Transaction with { ProviderScope = "" } },
            evidence with { Transaction = Transaction with { TransactionId = "" } }
        })
            Assert.Equal(ReceiptSettlementPrerequisiteResult.InvalidCommitEvidence,
                ReceiptSettlementPrerequisite.Evaluate(ExpectedInvoiceId, draft, incomplete));
    }

    [Fact]
    public void Same_total_does_not_authorize_a_different_encounter_approval_revision_or_line_breakdown()
    {
        var draft = Draft();
        var alternatives = new[]
        {
            Draft(encounter: new(102, 301)),
            Draft(encounter: new(101, 302)),
            Draft(revision: Ledger),
            Draft(approval: Ledger),
            Draft(amount: InvoiceAmountSnapshot.Calculate("XTS", 2, [new(160, 20, 7)])),
            Draft(amount: InvoiceAmountSnapshot.Calculate("XTS", 2, [new(147, 0, 0)])),
            Draft(amount: InvoiceAmountSnapshot.Calculate("XTS", 2, [new(70, 0, 0), new(77, 0, 0)])),
            Draft(amount: InvoiceAmountSnapshot.Calculate("XAU", 2, [new(150, 10, 7)])),
            Draft(amount: InvoiceAmountSnapshot.Calculate("XTS", 3, [new(150, 10, 7)]))
        };
        foreach (var alternative in alternatives)
        {
            Assert.Equal(draft.Amount.TotalUnits, alternative.Amount.TotalUnits);
            Assert.Equal(ReceiptSettlementPrerequisiteResult.DraftMismatch,
                ReceiptSettlementPrerequisite.Evaluate(ExpectedInvoiceId, draft, Committed(alternative)));
        }
    }

    [Fact]
    public void Identical_draft_content_does_not_satisfy_the_prerequisite_for_a_different_invoice()
    {
        var draft = Draft();
        var evidence = Committed(Draft());
        Assert.Equal(ReceiptSettlementPrerequisiteResult.DraftMismatch,
            ReceiptSettlementPrerequisite.Evaluate("synthetic-invoice-2", draft, evidence));
        Assert.Equal(ReceiptSettlementPrerequisiteResult.DraftMismatch,
            ReceiptSettlementPrerequisite.Evaluate(ExpectedInvoiceId.ToUpperInvariant(), draft, evidence));
        Assert.Throws<ArgumentException>("expectedInvoiceId",
            () => ReceiptSettlementPrerequisite.Evaluate(" ", draft, evidence));
    }

    [Fact]
    public void Exact_draft_does_not_compensate_for_different_verified_currency_scale_or_settled_units()
    {
        var draft = Draft();
        var evidence = Committed(draft);
        foreach (var mismatch in new[]
        {
            evidence with { Currency = "xts" },
            evidence with { Currency = "XAU" },
            evidence with { Scale = 3 },
            evidence with { SettledUnits = 146 },
            evidence with { SettledUnits = 148 }
        })
            Assert.Equal(ReceiptSettlementPrerequisiteResult.DraftMismatch,
                ReceiptSettlementPrerequisite.Evaluate(ExpectedInvoiceId, draft, mismatch));
    }

    [Fact]
    public void Zero_total_draft_does_not_use_the_payment_commitment_path()
    {
        var draft = Draft(amount: InvoiceAmountSnapshot.Calculate("XTS", 2, [new(150, 150, 0)]));
        Assert.Equal(ReceiptSettlementPrerequisiteResult.UnsupportedAmount,
            ReceiptSettlementPrerequisite.Evaluate(ExpectedInvoiceId, draft, Committed(draft) with { SettledUnits = 0 }));
    }

    private static InvoiceDraftSnapshot Draft(InvoiceEncounterIdentity? encounter = null, Guid? revision = null,
        Guid? approval = null, InvoiceAmountSnapshot? amount = null)
    {
        var scope = encounter ?? Encounter;
        return InvoiceDraftSnapshot.Create(scope, new(scope, revision ?? Revision, approval ?? Approval,
            amount ?? InvoiceAmountSnapshot.Calculate("XTS", 2, [new(150, 10, 7)])));
    }

    private static ReceiptSettlementEvidence Committed(InvoiceDraftSnapshot draft) =>
        new(ReceiptSettlementStage.VerifiedCommitted, Ledger, Transaction, ExpectedInvoiceId, draft, "XTS", 2, 147);
}
