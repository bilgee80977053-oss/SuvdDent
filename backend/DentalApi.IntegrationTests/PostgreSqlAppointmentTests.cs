using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using DentalApi.Contracts;
using DentalApi.Models;
using DentalApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace DentalApi.IntegrationTests;

[Collection("Real PostgreSQL"), Trait("Category", "PostgreSqlConcurrency")]
public sealed class PostgreSqlAppointmentTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }

    // Both independent serializable snapshots reach the absent-overlap predicate
    // before either INSERT. This tests SSI rather than merely sequential rejection.
    private sealed class OverlapGate : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ConcurrentBag<int> Backends = new();
        internal readonly ConcurrentBag<IsolationLevel> Levels = new();
        internal readonly ConcurrentBag<string> States = new();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal) && command.CommandText.Contains("\"Appointments\""))
            {
                Backends.Add(((NpgsqlConnection)command.Connection!).ProcessID);
                Levels.Add(command.Transaction!.IsolationLevel);
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            for (Exception? error = eventData.Exception; error is not null; error = error.InnerException)
                if (error is PostgresException pg) States.Add(pg.SqlState);
            return Task.CompletedTask;
        }
    }

    private sealed class TransactionFailures(ConcurrentBag<string> states) : DbTransactionInterceptor
    {
        public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            for (Exception? error = eventData.Exception; error is not null; error = error.InnerException)
                if (error is PostgresException pg) states.Add(pg.SqlState);
            return Task.CompletedTask;
        }
    }

    [PostgreSqlFact]
    public Task Two_patients_cannot_commit_the_same_doctor_slot() => Race(false);
    [PostgreSqlFact]
    public Task Simultaneous_duplicate_request_commits_once_and_can_replay() => Race(true);

    private static async Task Race(bool duplicate)
    {
        await using var app = await PostgreSqlApiFactory.StartAsync();
        var keys = new[] {Guid.NewGuid(), Guid.NewGuid()};
        var hasAppointments = false;
        try
        {
            await using var setup = app.Context();
            if (!(await setup.Database.GetAppliedMigrationsAsync()).Contains("20261027180725_AddPostgreSqlAppointmentFoundation"))
                throw new InvalidOperationException("Appointment migration must already be applied by an authorized operator. This test will not apply migrations.");
            hasAppointments = true;
            await app.CreatePatientAsync();
            var clinic = await setup.Clinics.SingleAsync(c => c.Id == app.ClinicA.Id);
            clinic.CountryCode="MN"; clinic.ProvinceOrCapitalCode="11"; clinic.SoumOrDistrictCode="1101";
            clinic.AddressLine="Synthetic test address"; clinic.TimeZoneId="Asia/Ulaanbaatar";
            clinic.TimeZoneStatus=TimeZoneStatus.Verified; clinic.TimeZoneVerifiedByUserId=app.DirectorA.Id;
            clinic.TimeZoneVerifiedClinicId=clinic.Id; clinic.TimeZoneVerifiedAtUtc=DateTime.UtcNow;
            var secondUser = new User { ClinicId=clinic.Id, FullName=app.Marker+"-second-patient",
                Email=app.Marker+"-second@example.invalid", Role=Role.Patient, IsEmailVerified=true, PasswordHash="synthetic-unused" };
            setup.Users.Add(secondUser); await setup.SaveChangesAsync();
            setup.Patients.Add(new Patient {ClinicId=clinic.Id, UserId=secondUser.Id, FullName=app.Marker+"-second"});
            setup.DoctorServices.Add(new DoctorService {ClinicId=clinic.Id, DoctorProfileId=app.ProfileA.Id,
                ServiceId=app.ServiceA.Id, DurationMinutes=30, BufferBeforeMinutes=5, BufferAfterMinutes=10});
            var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Ulaanbaatar");
            var now=DateTimeOffset.UtcNow;
            var date=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,zone).DateTime).AddDays(1);
            setup.DoctorWorkingHours.Add(new DoctorWorkingHours {ClinicId=clinic.Id, DoctorProfileId=app.ProfileA.Id,
                DayOfWeek=(int)date.DayOfWeek, StartMinute=540, EndMinute=780});
            await setup.SaveChangesAsync();
            var start=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(9,15)),zone));
            var firstRequest=new CreateAppointmentRequest(app.ProfileA.Id,app.ServiceA.Id,start.ToString("O"),keys[0]);
            var secondRequest=duplicate ? firstRequest : firstRequest with {RequestId=keys[1]};
            var gate=new OverlapGate();
            var transactionFailures = new TransactionFailures(gate.States);
            await using var first=app.Context(gate, transactionFailures);
            await using var second=app.Context(gate, transactionFailures);
            AppointmentService Service(DentalApi.Data.AppDbContext db) => new(db,new ClinicSchedulingPolicy(db,new MongoliaLocations()),new FixedClock(now));
            var results=await Task.WhenAll(Service(first).Create(clinic.Id,app.PatientA.Id,firstRequest),
                Service(second).Create(clinic.Id,duplicate ? app.PatientA.Id : secondUser.Id,secondRequest));
            Assert.Equal(1,results.Count(r=>r.Status==201));
            Assert.All(results,r=>Assert.True(r.Status==201 || r.Status==409 || (duplicate && r.Status==200)));
            Assert.Equal(2,gate.Backends.Distinct().Count());
            Assert.All(gate.Levels,l=>Assert.Equal(IsolationLevel.Serializable,l));
            if (duplicate) Assert.Contains(gate.States, state => state is "40001" or "23505");
            else Assert.Contains("40001",gate.States);
            await using var verify=app.Context();
            var row=Assert.Single(await verify.Appointments.AsNoTracking().Where(a=>a.ClinicId==clinic.Id).ToArrayAsync());
            Assert.Equal(start,row.StartUtc); Assert.Equal(30,row.DurationMinutesSnapshot);
            if (duplicate)
            {
                var replay=await Service(verify).Create(clinic.Id,app.PatientA.Id,firstRequest);
                Assert.Equal(200,replay.Status);
                Assert.Equal(row.Id,Assert.IsType<AppointmentResponse>(replay.Data).Id);
            }
        }
        finally
        {
            // Remove only this test's generated request keys, after verifying fixture ownership.
            // Existing factory cleanup then removes its remaining synthetic rows in FK order.
            if (hasAppointments)
            {
                await using var cleanup=app.Context();
                if (!await cleanup.Clinics.AnyAsync(c=>c.Id==app.ClinicA.Id && c.Name==app.Marker+"-A"))
                    throw new InvalidOperationException("Synthetic clinic ownership changed; no appointment cleanup attempted.");
                await cleanup.Appointments.Where(a=>a.ClinicId==app.ClinicA.Id && keys.Contains(a.RequestId)).ExecuteDeleteAsync();
            }
        }
    }
}
