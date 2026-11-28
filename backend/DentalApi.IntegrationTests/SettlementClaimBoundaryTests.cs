using System.Collections.Concurrent;
using DentalApi.Services;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class SettlementClaimBoundaryTests
{
    private static readonly SettlementTransactionKey Key = new("synthetic-sandbox", "seller-a", "transaction-1");
    private static readonly InvoiceAmountSnapshot Amount = InvoiceAmountSnapshot.Calculate("XTS", 2, [new(100, 0, 0)]);

    // Test-only atomic process-local store. The gate forces both callers past evidence comparison
    // before either claims, so the stale negative replay lookup cannot serialize this scenario.
    private sealed class SyntheticStore(bool overlap = false) : IAtomicSettlementClaimStore
    {
        public ConcurrentDictionary<SettlementTransactionKey, string> Claims { get; } = new();
        private readonly TaskCompletionSource bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int attempts;
        public int Attempts => Volatile.Read(ref attempts);

        public async ValueTask<AtomicSettlementClaimResult> TryClaimAsync(
            SettlementClaim claim, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref attempts) == 2)
                bothArrived.TrySetResult();
            if (overlap)
                await bothArrived.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Claims.TryAdd(claim.Transaction, claim.InvoiceId)
                ? AtomicSettlementClaimResult.Recorded
                : AtomicSettlementClaimResult.AlreadyClaimed;
        }
    }

    private sealed class UnknownStore : IAtomicSettlementClaimStore
    {
        public ValueTask<AtomicSettlementClaimResult> TryClaimAsync(
            SettlementClaim claim, CancellationToken cancellationToken) =>
            ValueTask.FromResult((AtomicSettlementClaimResult)99);
    }

    [Fact]
    public async Task Concurrent_candidates_for_two_invoices_bind_one_transaction_to_only_one_invoice()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new SyntheticStore(overlap: true);
        var first = Reserve(new(store), "invoice-a", Key, timeout.Token).AsTask();
        var second = Reserve(new(store), "invoice-b", Key, timeout.Token).AsTask();

        var results = await Task.WhenAll(first, second);
        Assert.Equal(2, store.Attempts);
        Assert.Single(results, r => r == SettlementClaimDecision.ClaimRecorded);
        Assert.Single(results, r => r == SettlementClaimDecision.AlreadyClaimed);
        var winner = results[0] == SettlementClaimDecision.ClaimRecorded ? "invoice-a" : "invoice-b";
        var binding = Assert.Single(store.Claims);
        Assert.Equal(Key, binding.Key);
        Assert.Equal(winner, binding.Value);
    }

    [Fact]
    public async Task Replaying_the_same_invoice_does_not_record_a_second_claim_even_with_stale_observation()
    {
        var store = new SyntheticStore();
        Assert.Equal(SettlementClaimDecision.ClaimRecorded, await Reserve(new(store), "invoice-a", Key));
        Assert.Equal(SettlementClaimDecision.AlreadyClaimed, await Reserve(new(store), "invoice-a", Key));
        Assert.Equal("invoice-a", Assert.Single(store.Claims).Value);
        Assert.Equal(2, store.Attempts);
    }

    [Fact]
    public async Task The_same_transaction_string_is_distinct_in_a_different_provider_or_seller_scope()
    {
        var store = new SyntheticStore();
        Assert.Equal(SettlementClaimDecision.ClaimRecorded, await Reserve(new(store), "invoice-a", Key));
        Assert.Equal(SettlementClaimDecision.ClaimRecorded,
            await Reserve(new(store), "invoice-b", Key with { SellerId = "seller-b" }));
        Assert.Equal(SettlementClaimDecision.ClaimRecorded,
            await Reserve(new(store), "invoice-c", Key with { ProviderScope = "synthetic-sandbox-2" }));
        Assert.Equal(3, store.Claims.Count);
    }

    [Fact]
    public async Task No_store_write_for_missing_mismatched_or_replayed_evidence()
    {
        var store = new SyntheticStore();
        var boundary = new SettlementClaimBoundary(store);
        var expected = new ExpectedSettlement("invoice-a", Key, Amount);
        var verified = new ServerVerifiedCapture("invoice-a", Key, "XTS", 2, 100);
        var observedNoClaim = new SettlementUseObservation(Key, SettlementUseState.NotObserved);
        Assert.Equal(SettlementClaimDecision.EvidenceRejected,
            await boundary.TryReserveAsync(expected, null, observedNoClaim));
        Assert.Equal(SettlementClaimDecision.EvidenceRejected,
            await boundary.TryReserveAsync(expected, verified with { CapturedUnits = 99 }, observedNoClaim));
        Assert.Equal(SettlementClaimDecision.EvidenceRejected,
            await boundary.TryReserveAsync(expected, verified, null));
        Assert.Equal(SettlementClaimDecision.AlreadyClaimed,
            await boundary.TryReserveAsync(expected, verified, observedNoClaim with { State = SettlementUseState.AlreadyObserved }));
        Assert.Empty(store.Claims);
        Assert.Equal(0, store.Attempts);
    }

    [Fact]
    public async Task Unrecognized_store_outcome_is_uncertain_and_never_recorded()
    {
        Assert.Equal(SettlementClaimDecision.ClaimUncertain,
            await Reserve(new(new UnknownStore()), "invoice-a", Key));
    }

    [Fact]
    public async Task A_cancelled_attempt_never_calls_the_store()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = new SyntheticStore();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Reserve(new(store), "invoice-a", Key, cancellation.Token).AsTask());
        Assert.Equal(0, store.Attempts);
    }

    private static ValueTask<SettlementClaimDecision> Reserve(
        SettlementClaimBoundary boundary, string invoiceId, SettlementTransactionKey key,
        CancellationToken cancellationToken = default) =>
        boundary.TryReserveAsync(new(invoiceId, key, Amount), new(invoiceId, key, "XTS", 2, 100),
            new(key, SettlementUseState.NotObserved), cancellationToken);
}
