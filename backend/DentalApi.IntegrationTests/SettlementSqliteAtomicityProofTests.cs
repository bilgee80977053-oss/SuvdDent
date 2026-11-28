using DentalApi.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class SettlementSqliteAtomicityProofTests
{
    private static readonly SettlementTransactionKey Key = new("synthetic-scope", "synthetic-seller", "synthetic-transaction");
    private static readonly InvoiceAmountSnapshot Amount = InvoiceAmountSnapshot.Calculate("XTS", 2, [new(100, 0, 0)]);

    [Fact]
    public async Task Real_sqlite_ledger_insert_failure_rolls_back_claim_and_fresh_retry_can_record_one_pair()
    {
        using var fixture = new SqliteProof();
        var bad = new SettlementClaimBoundary(new SqliteProofStore(fixture, injectLedgerConstraintFailure: true));
        var fault = await Assert.ThrowsAsync<SqliteException>(() => Reserve(bad, "synthetic-invoice-a").AsTask());
        Assert.Equal(19, fault.SqliteErrorCode); // SQLITE_CONSTRAINT
        Assert.Equal(275, fault.SqliteExtendedErrorCode); // SQLITE_CONSTRAINT_CHECK
        Assert.Empty(fixture.ReadClaims());
        Assert.Empty(fixture.ReadEffects());

        var fresh = new SettlementClaimBoundary(new SqliteProofStore(fixture));
        Assert.Equal(SettlementClaimDecision.ClaimRecorded, await Reserve(fresh, "synthetic-invoice-b"));
        AssertPair(fixture, "synthetic-invoice-b");
    }

    [Fact]
    public async Task Competing_sqlite_connections_and_stale_replays_end_with_exactly_one_claim_and_ledger_effect()
    {
        using var fixture = new SqliteProof();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var gate = new BothAttemptsGate();
        var first = Reserve(new(new SqliteProofStore(fixture, gate: gate)), "synthetic-invoice-a", timeout.Token).AsTask();
        var second = Reserve(new(new SqliteProofStore(fixture, gate: gate)), "synthetic-invoice-b", timeout.Token).AsTask();

        var results = await Task.WhenAll(first, second);
        Assert.Equal(2, gate.Arrivals);
        Assert.Single(results, r => r == SettlementClaimDecision.ClaimRecorded);
        Assert.Single(results, r => r == SettlementClaimDecision.AlreadyClaimed);
        var winner = results[0] == SettlementClaimDecision.ClaimRecorded ? "synthetic-invoice-a" : "synthetic-invoice-b";
        var loser = results[0] == SettlementClaimDecision.ClaimRecorded ? "synthetic-invoice-b" : "synthetic-invoice-a";
        AssertPair(fixture, winner);

        var replay = new SettlementClaimBoundary(new SqliteProofStore(fixture));
        Assert.Equal(SettlementClaimDecision.AlreadyClaimed, await Reserve(replay, winner));
        Assert.Equal(SettlementClaimDecision.AlreadyClaimed, await Reserve(replay, loser));
        AssertPair(fixture, winner);
    }

    private static void AssertPair(SqliteProof fixture, string invoiceId)
    {
        var expected = new SettlementClaim(Key, invoiceId);
        Assert.Equal(expected, Assert.Single(fixture.ReadClaims()));
        Assert.Equal(expected, Assert.Single(fixture.ReadEffects()));
    }

    private static ValueTask<SettlementClaimDecision> Reserve(
        SettlementClaimBoundary boundary, string invoiceId, CancellationToken cancellationToken = default) =>
        boundary.TryReserveAsync(
            new(invoiceId, Key, Amount), new(invoiceId, Key, "XTS", 2, 100),
            new(Key, SettlementUseState.NotObserved), cancellationToken);

    private sealed class BothAttemptsGate
    {
        private int arrivals;
        public int Arrivals => Volatile.Read(ref arrivals);
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) == 2)
                ready.TrySetResult();
            return ready.Task.WaitAsync(cancellationToken);
        }
    }

    // Only a test fixture. Unique named RAM-only DB, no app schema, migration, credentials,
    // or external connections. The keeper prevents it from expiring between test connections.
    private sealed class SqliteProof : IDisposable
    {
        private readonly SqliteConnection keeper;
        private readonly string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"synthetic-settlement-proof-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();

        public SqliteProof()
        {
            keeper = OpenConnection();
            using var command = keeper.CreateCommand();
            command.CommandText = """
                CREATE TABLE synthetic_claim (
                    provider_scope TEXT NOT NULL, seller_id TEXT NOT NULL, transaction_id TEXT NOT NULL,
                    invoice_id TEXT NOT NULL,
                    PRIMARY KEY (provider_scope, seller_id, transaction_id),
                    UNIQUE (provider_scope, seller_id, transaction_id, invoice_id)
                );
                CREATE TABLE synthetic_ledger_effect (
                    provider_scope TEXT NOT NULL, seller_id TEXT NOT NULL, transaction_id TEXT NOT NULL,
                    invoice_id TEXT NOT NULL, proof_marker INTEGER NOT NULL CHECK (proof_marker = 1),
                    PRIMARY KEY (provider_scope, seller_id, transaction_id),
                    FOREIGN KEY (provider_scope, seller_id, transaction_id, invoice_id)
                        REFERENCES synthetic_claim(provider_scope, seller_id, transaction_id, invoice_id)
                );
                """;
            command.ExecuteNonQuery();
        }

        public SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        public IReadOnlyList<SettlementClaim> ReadClaims() =>
            Read("SELECT provider_scope, seller_id, transaction_id, invoice_id FROM synthetic_claim");

        public IReadOnlyList<SettlementClaim> ReadEffects() =>
            Read("SELECT provider_scope, seller_id, transaction_id, invoice_id FROM synthetic_ledger_effect");

        private IReadOnlyList<SettlementClaim> Read(string sql)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            var rows = new List<SettlementClaim>();
            while (reader.Read())
                rows.Add(new SettlementClaim(new(reader.GetString(0), reader.GetString(1), reader.GetString(2)), reader.GetString(3)));
            return rows;
        }

        public void Dispose() => keeper.Dispose();
    }

    // Real transactional behavior is the subject under test; this adapter is never registered or deployed.
    private sealed class SqliteProofStore(
        SqliteProof fixture, bool injectLedgerConstraintFailure = false, BothAttemptsGate? gate = null)
        : IAtomicSettlementClaimStore
    {
        public async ValueTask<AtomicSettlementClaimResult> TryClaimAsync(
            SettlementClaim claim, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = fixture.OpenConnection();
            // Both competing connections are open before either starts a write transaction.
            if (gate is not null)
                await gate.ArriveAsync(cancellationToken);
            // SQLite commands are synchronous; use distinct workers for the overlap proof.
            return await Task.Run(() => TryClaim(connection, claim, cancellationToken), cancellationToken);
        }

        private AtomicSettlementClaimResult TryClaim(
            SqliteConnection connection, SettlementClaim claim, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var transaction = connection.BeginTransaction(deferred: false);
            using var reserve = CreateCommand(connection, transaction, claim, """
                INSERT INTO synthetic_claim(provider_scope, seller_id, transaction_id, invoice_id)
                VALUES ($scope, $seller, $transaction, $invoice)
                ON CONFLICT (provider_scope, seller_id, transaction_id) DO NOTHING
                """);
            if (reserve.ExecuteNonQuery() == 0)
                return AtomicSettlementClaimResult.AlreadyClaimed;

            using var ledger = CreateCommand(connection, transaction, claim, """
                INSERT INTO synthetic_ledger_effect(provider_scope, seller_id, transaction_id, invoice_id, proof_marker)
                VALUES ($scope, $seller, $transaction, $invoice, $marker)
                """);
            ledger.Parameters.AddWithValue("$marker", injectLedgerConstraintFailure ? 0 : 1);
            ledger.ExecuteNonQuery();
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return AtomicSettlementClaimResult.Recorded;
        }

        private static SqliteCommand CreateCommand(
            SqliteConnection connection, SqliteTransaction transaction, SettlementClaim claim, string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$scope", claim.Transaction.ProviderScope);
            command.Parameters.AddWithValue("$seller", claim.Transaction.SellerId);
            command.Parameters.AddWithValue("$transaction", claim.Transaction.TransactionId);
            command.Parameters.AddWithValue("$invoice", claim.InvoiceId);
            return command;
        }
    }
}
