namespace DentalApi.Services;

// Opaque, exact identifiers originating from server-side configuration and records.
internal readonly record struct SettlementTransactionKey(string ProviderScope, string SellerId, string TransactionId)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ProviderScope) &&
        !string.IsNullOrWhiteSpace(SellerId) &&
        !string.IsNullOrWhiteSpace(TransactionId);
}

internal readonly record struct ExpectedSettlement(
    string InvoiceId, SettlementTransactionKey Transaction, InvoiceAmountSnapshot Amount);

/// <summary>
/// Normalized output of a future trusted server-side verifier. This type does not authenticate
/// evidence. A request body, client status or raw callback is not a valid source.
/// </summary>
internal readonly record struct ServerVerifiedCapture(
    string InvoiceId, SettlementTransactionKey Transaction, string Currency, int Scale, long CapturedUnits);

internal enum SettlementUseState
{
    Unknown,
    NotObserved,
    AlreadyObserved
}

// Replay lookup is scoped to the provider's transaction, not to its proposed invoice.
internal readonly record struct SettlementUseObservation(
    SettlementTransactionKey Transaction, SettlementUseState State);

internal enum SettlementComparisonResult
{
    InvalidExpectation,
    MissingVerifiedCapture,
    CaptureMismatch,
    ReplayDetected,
    ReplayStatusUnknown,
    Candidate
}

/// <summary>
/// Read-only comparison. Candidate never means paid, settled, or safe to issue a receipt.
/// A future settlement workflow must authenticate evidence and atomically claim the transaction;
/// a NotObserved lookup alone cannot exclude a race or replace that claim.
/// </summary>
internal static class SettlementEvidenceComparison
{
    public static SettlementComparisonResult Compare(
        ExpectedSettlement expected, ServerVerifiedCapture? capture, SettlementUseObservation? observation)
    {
        if (!expected.Transaction.IsValid || string.IsNullOrWhiteSpace(expected.InvoiceId) ||
            expected.Amount is null || expected.Amount.TotalUnits <= 0)
            return SettlementComparisonResult.InvalidExpectation;

        if (capture is not { } verified)
            return SettlementComparisonResult.MissingVerifiedCapture;

        if (verified.Transaction != expected.Transaction ||
            !string.Equals(verified.InvoiceId, expected.InvoiceId, StringComparison.Ordinal) ||
            !string.Equals(verified.Currency, expected.Amount.Currency, StringComparison.Ordinal) ||
            verified.Scale != expected.Amount.Scale ||
            verified.CapturedUnits != expected.Amount.TotalUnits)
            return SettlementComparisonResult.CaptureMismatch;

        // Never use replay information from a different provider, seller, or transaction.
        if (observation is not { } replay || replay.Transaction != expected.Transaction)
            return SettlementComparisonResult.ReplayStatusUnknown;

        return replay.State switch
        {
            SettlementUseState.AlreadyObserved => SettlementComparisonResult.ReplayDetected,
            SettlementUseState.NotObserved => SettlementComparisonResult.Candidate,
            _ => SettlementComparisonResult.ReplayStatusUnknown
        };
    }
}
