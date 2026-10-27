using System.Data;
using System.Data.Common;
using System.Text.Json;
using DentalApi.Contracts;
using DentalApi.Data;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace DentalApi.IntegrationTests;

public sealed class AppointmentProviderTests
{
    private sealed class NeverConnect : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }
    private sealed class FailStart(Exception error) : DbTransactionInterceptor
    {
        internal IsolationLevel? Level;
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        { Level = eventData.IsolationLevel; throw error; }
    }

    [Theory]
    [InlineData("23505", false)] [InlineData("40001", false)] [InlineData("40P01", false)]
    [InlineData("23505", true)] [InlineData("40001", true)] [InlineData("40P01", true)]
    public async Task PostgreSql_booking_and_slots_use_Serializable_and_safe_SQLSTATE_conflicts_offline(string state, bool wrapped)
    {
        foreach (var slots in new[] {false, true})
        {
            Exception error = new PostgresException("synthetic-private-database-error", "ERROR", "ERROR", state);
            if (wrapped) error = new InvalidOperationException("synthetic-private-wrapper", new DbUpdateException("synthetic-private-save", error));
            var probe = new FailStart(error);
            await using var db = new PostgreSqlAppDbContext(new DbContextOptionsBuilder<PostgreSqlAppDbContext>()
                .UseNpgsql("Host=localhost;Database=synthetic_never_opened").AddInterceptors(new NeverConnect(), probe).Options);
            var service = new AppointmentService(db, new ClinicSchedulingPolicy(db, new MongoliaLocations()), TimeProvider.System);
            var result = slots ? await service.Slots(101,11,31,51,"2026-11-02") :
                await service.Create(101,11,new CreateAppointmentRequest(31,51,"2026-11-02T09:15:00+08:00",Guid.NewGuid()));
            Assert.Equal(409, result.Status);
            Assert.Equal(IsolationLevel.Serializable, probe.Level);
            Assert.DoesNotContain("synthetic-private", JsonSerializer.Serialize(result));
            Assert.Null(db.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
        }
    }
}
