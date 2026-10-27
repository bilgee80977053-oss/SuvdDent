using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Net.Http.Json;
using DentalApi.Contracts;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using static DentalApi.IntegrationTests.PostgreSqlWorkflowTests;

namespace DentalApi.IntegrationTests;

[Collection("Real PostgreSQL"), Trait("Category", "PostgreSqlConcurrency")]
public sealed class PostgreSqlConcurrencyTests
{
    [PostgreSqlFact]
    public async Task Concurrent_service_updates_with_one_version_commit_one_complete_winner_and_one_safe_conflict()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        var beforeB = await app.SnapshotBAsync();
        var gate = new CompetingUpdates("DoctorProfiles");
        await using var first = app.Context(gate);
        await using var second = app.Context(gate);
        Assert.NotEqual(first.ContextId, second.ContextId);
        var requests = new[]
        {
            new UpdateDoctorServicesRequest(app.ProfileA.Version, [new(app.ServiceA.Id, 30, 5, 10)]),
            new UpdateDoctorServicesRequest(app.ProfileA.Version, [new(app.ServiceA2.Id, 60, 10, 15)])
        };
        var results = await Task.WhenAll(
            new SchedulingService(first).WriteServices(app.ClinicA.Id, app.ProfileA.Id, requests[0]),
            new SchedulingService(second).WriteServices(app.ClinicA.Id, app.ProfileA.Id, requests[1]));
        Assert.Equal(new[] { 200, 409 }, results.Select(r => r.Status).Order());
        Assert.All(gate.Isolations, level => Assert.Equal(IsolationLevel.Serializable, level));
        Assert.Equal(2, gate.Backends.Distinct().Count());
        Assert.Contains("40001", gate.States); // A real PostgreSQL serialization failure, not an injected error.
        var winner = Array.FindIndex(results, r => r.Status == 200);
        var response = Assert.IsType<DoctorServicesResponse>(results[winner].Data);
        Assert.Null(results[1 - winner].Data);
        await using var verify = app.Context();
        var rows = await verify.DoctorServices.AsNoTracking().Where(s => s.ClinicId == app.ClinicA.Id && s.DoctorProfileId == app.ProfileA.Id).ToArrayAsync();
        var row = Assert.Single(rows);
        var expected = Assert.Single(requests[winner].Services);
        Assert.Equal(expected.ServiceId, row.ServiceId);
        Assert.Equal(expected.DurationMinutes, row.DurationMinutes);
        Assert.Equal(expected.BufferBeforeMinutes, row.BufferBeforeMinutes);
        Assert.Equal(expected.BufferAfterMinutes, row.BufferAfterMinutes);
        Assert.Equal(response.Version, (await verify.DoctorProfiles.SingleAsync(d => d.Id == app.ProfileA.Id && d.ClinicId == app.ClinicA.Id)).Version);
        Assert.Equal(beforeB, await app.SnapshotBAsync());
    }

    [PostgreSqlFact]
    public async Task Concurrent_availability_replacements_never_merge_or_partially_commit_the_loser()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        var patientId = await app.CreatePatientAsync();
        using var patient = app.Client(app.PatientA);
        using var initial = await patient.PutAsJsonAsync(AvailabilityUrl, new UpdateAvailabilityRequest(Guid.Empty, [Window()]));
        Assert.Equal(System.Net.HttpStatusCode.OK, initial.StatusCode);
        var schedule = (await initial.Content.ReadFromJsonAsync<AvailabilityResponse>())!;
        var beforeB = await app.SnapshotBAsync();
        var gate = new CompetingUpdates("PatientAvailabilitySchedules");
        await using var first = app.Context(gate);
        await using var second = app.Context(gate);
        var requests = new[]
        {
            new UpdateAvailabilityRequest(schedule.Version, [Window(3), Window(5)]),
            new UpdateAvailabilityRequest(schedule.Version, [Window(7), Window(9)])
        };
        var results = await Task.WhenAll(
            new SchedulingService(first).WriteAvailability(app.ClinicA.Id, app.PatientA.Id, requests[0]),
            new SchedulingService(second).WriteAvailability(app.ClinicA.Id, app.PatientA.Id, requests[1]));
        Assert.Equal(new[] { 200, 409 }, results.Select(r => r.Status).Order());
        Assert.Equal(2, gate.Backends.Distinct().Count());
        Assert.Contains("40001", gate.States);
        var winner = Array.FindIndex(results, r => r.Status == 200);
        var response = Assert.IsType<AvailabilityResponse>(results[winner].Data);
        Assert.Null(results[1 - winner].Data);
        await using var verify = app.Context();
        var rows = await verify.PatientAvailabilities.AsNoTracking().Where(s => s.ClinicId == app.ClinicA.Id && s.PatientId == patientId).OrderBy(s => s.StartUtc).ToArrayAsync();
        Assert.Equal(requests[winner].Intervals, rows.Select(r => new AvailabilityInput(r.StartUtc, r.EndUtc)).ToArray());
        Assert.Equal(response.Version, (await verify.PatientAvailabilitySchedules.SingleAsync(s => s.ClinicId == app.ClinicA.Id && s.PatientId == patientId)).Version);
        Assert.NotEqual(schedule.Version, response.Version);
        Assert.Equal(beforeB, await app.SnapshotBAsync());
    }

    [PostgreSqlFact]
    public async Task RepeatableRead_scheduling_snapshot_returns_matching_version_and_list_during_a_committed_update()
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        await using (var setup = app.Context())
        {
            var result = await new SchedulingService(setup).WriteServices(app.ClinicA.Id, app.ProfileA.Id,
                new(app.ProfileA.Version, [new(app.ServiceA.Id, 30, 0, 0)]));
            Assert.Equal(200, result.Status);
        }
        var beforeB = await app.SnapshotBAsync();
        var gate = new SnapshotGate();
        await using var reader = app.Context(gate);
        await using var writer = app.Context();
        var reading = new SchedulingService(reader).ReadServices(app.ClinicA.Id, app.ProfileA.Id);
        DoctorServicesResponse? updated = null;
        Guid oldVersion = Guid.Empty;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var current = await writer.DoctorProfiles.AsNoTracking().SingleAsync(d => d.Id == app.ProfileA.Id && d.ClinicId == app.ClinicA.Id);
            oldVersion = current.Version;
            // Hold writer's connection open to prove the two active backends differ.
            await writer.Database.OpenConnectionAsync();
            Assert.NotEqual(gate.Backend, ((NpgsqlConnection)writer.Database.GetDbConnection()).ProcessID);
            var result = await new SchedulingService(writer).WriteServices(app.ClinicA.Id, app.ProfileA.Id,
                new(current.Version, [new(app.ServiceA2.Id, 60, 0, 0)]));
            Assert.Equal(200, result.Status);
            updated = Assert.IsType<DoctorServicesResponse>(result.Data);
        }
        finally
        {
            gate.Release.TrySetResult();
            await reading; // Finish the independent reader before disposing its context, even on assertion failure.
        }
        var read = await reading;
        Assert.Equal(200, read.Status);
        var snapshot = Assert.IsType<DoctorServicesResponse>(read.Data);
        Assert.Equal(IsolationLevel.RepeatableRead, gate.Isolation);
        Assert.Equal(oldVersion, snapshot.Version);
        Assert.Equal(app.ServiceA.Id, Assert.Single(snapshot.Services).ServiceId);
        await using var fresh = app.Context();
        var after = Assert.IsType<DoctorServicesResponse>((await new SchedulingService(fresh).ReadServices(app.ClinicA.Id, app.ProfileA.Id)).Data);
        Assert.Equal(updated!.Version, after.Version);
        Assert.NotEqual(snapshot.Version, after.Version);
        Assert.Equal(app.ServiceA2.Id, after.Services.Single(s => s.IsActive).ServiceId);
        Assert.Equal(beforeB, await app.SnapshotBAsync());
    }

    private sealed class CompetingUpdates(string table) : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentBag<int> Backends { get; } = [];
        internal ConcurrentBag<string> States { get; } = [];
        internal ConcurrentBag<IsolationLevel> Isolations { get; } = [];
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE \"" + table + "\"", StringComparison.Ordinal))
            {
                Backends.Add(((NpgsqlConnection)command.Connection!).ProcessID);
                Isolations.Add(command.Transaction!.IsolationLevel);
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            for (Exception? error = eventData.Exception; error is not null; error = error.InnerException)
                if (error is PostgresException pg) States.Add(pg.SqlState);
            return Task.CompletedTask;
        }
    }

    private sealed class SnapshotGate : DbCommandInterceptor
    {
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Backend { get; private set; }
        internal IsolationLevel Isolation { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"DoctorServices\"", StringComparison.Ordinal))
            {
                Backend = ((NpgsqlConnection)command.Connection!).ProcessID;
                Isolation = command.Transaction!.IsolationLevel;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
