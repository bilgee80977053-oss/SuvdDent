namespace DentalApi.Services;

internal enum ReceiptSettlementStage
{
    Unknown,
    Candidate,
    Reserved,
    VerifiedCommitted
}

/// <summary>
/// Future trusted server output. VerifiedCommitted requires a verified provider settlement and
/// confirmed durable ledger commit for this exact draft. The type does not establish provenance:
/// client data, callbacks, a comparison candidate, and a reservation are not proof.
/// </summary>
internal sealed record ReceiptSettlementEvidence(
    ReceiptSettlementStage Stage,
    Guid CommittedLedgerEntryId,
    SettlementTransactionKey Transaction,
    string InvoiceId,
    InvoiceDraftSnapshot? BoundDraft,
    string Currency,
    int Scale,
    long SettledUnits);

internal enum ReceiptSettlementPrerequisiteResult
{
    VerifiedCommitRequired,
    InvalidCommitEvidence,
    DraftMismatch,
    UnsupportedAmount,
    CommitmentPrerequisiteSatisfied
}

/// <summary>
/// A pure prerequisite check, not permission to issue a receipt or mark an invoice paid.
/// A future issuance boundary must independently verify provenance, lifecycle, authorization,
/// tax/document rules, and idempotent issuance. The expected invoice ID is an independently
/// supplied opaque server-side reference, not an identifier this check generates or assigns.
/// </summary>
internal static class ReceiptSettlementPrerequisite
{
    public static ReceiptSettlementPrerequisiteResult Evaluate(
        string expectedInvoiceId, InvoiceDraftSnapshot draft, ReceiptSettlementEvidence? evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedInvoiceId);
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Amount.TotalUnits <= 0)
            return ReceiptSettlementPrerequisiteResult.UnsupportedAmount;

        if (evidence is not { Stage: ReceiptSettlementStage.VerifiedCommitted })
            return ReceiptSettlementPrerequisiteResult.VerifiedCommitRequired;

        if (evidence.CommittedLedgerEntryId == Guid.Empty || !evidence.Transaction.IsValid ||
            string.IsNullOrWhiteSpace(evidence.InvoiceId) || evidence.BoundDraft is not { } committedDraft)
            return ReceiptSettlementPrerequisiteResult.InvalidCommitEvidence;

        // A matching total alone cannot substitute for the bound draft identity or exact line amounts.
        if (!string.Equals(evidence.InvoiceId, expectedInvoiceId, StringComparison.Ordinal) ||
            committedDraft.Encounter != draft.Encounter ||
            committedDraft.TariffRevisionId != draft.TariffRevisionId ||
            committedDraft.TariffApprovalId != draft.TariffApprovalId ||
            !string.Equals(committedDraft.Amount.Currency, draft.Amount.Currency, StringComparison.Ordinal) ||
            committedDraft.Amount.Scale != draft.Amount.Scale ||
            !committedDraft.Amount.Lines.SequenceEqual(draft.Amount.Lines) ||
            !string.Equals(evidence.Currency, draft.Amount.Currency, StringComparison.Ordinal) ||
            evidence.Scale != draft.Amount.Scale ||
            evidence.SettledUnits != draft.Amount.TotalUnits)
            return ReceiptSettlementPrerequisiteResult.DraftMismatch;

        return ReceiptSettlementPrerequisiteResult.CommitmentPrerequisiteSatisfied;
    }
}
