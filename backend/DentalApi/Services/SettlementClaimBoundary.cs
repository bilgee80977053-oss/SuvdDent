namespace DentalApi.Services;

internal readonly record struct SettlementClaim(SettlementTransactionKey Transaction, string InvoiceId);

internal enum AtomicSettlementClaimResult
{
    Unknown,
    Recorded,
    AlreadyClaimed
}

/// <summary>
/// A trusted store's single atomic insert-if-absent boundary. The entire provider/seller/
/// transaction key is unique without the invoice ID. An existing binding must stay unchanged,
/// including when the same invoice retries. Recorded must only be returned after commit.
/// No read-then-insert implementation or process-local store is sufficient for deployment.
/// An ambiguous commit must be reported as Unknown (or an error), never as Recorded.
/// </summary>
internal interface IAtomicSettlementClaimStore
{
    ValueTask<AtomicSettlementClaimResult> TryClaimAsync(SettlementClaim claim, CancellationToken cancellationToken);
}

internal enum SettlementClaimDecision
{
    EvidenceRejected,
    ClaimRecorded,
    AlreadyClaimed,
    ClaimUncertain
}

/// <summary>
/// Guards the future claim write with trusted server-side evidence. ClaimRecorded is only
/// a reservation: this does not change invoice status, post a ledger entry, or issue a receipt.
/// A future settlement write must bind its ledger/outbox effect in the same durable transaction.
/// </summary>
internal sealed class SettlementClaimBoundary(IAtomicSettlementClaimStore store)
{
    public async ValueTask<SettlementClaimDecision> TryReserveAsync(
        ExpectedSettlement expected, ServerVerifiedCapture? capture, SettlementUseObservation? observation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var comparison = SettlementEvidenceComparison.Compare(expected, capture, observation);
        if (comparison != SettlementComparisonResult.Candidate)
            return comparison == SettlementComparisonResult.ReplayDetected
                ? SettlementClaimDecision.AlreadyClaimed
                : SettlementClaimDecision.EvidenceRejected;

        // The earlier NotObserved lookup can be stale. Only the atomic write decides ownership.
        var result = await store.TryClaimAsync(new(expected.Transaction, expected.InvoiceId), cancellationToken);
        return result switch
        {
            AtomicSettlementClaimResult.Recorded => SettlementClaimDecision.ClaimRecorded,
            AtomicSettlementClaimResult.AlreadyClaimed => SettlementClaimDecision.AlreadyClaimed,
            _ => SettlementClaimDecision.ClaimUncertain
        };
    }
}
