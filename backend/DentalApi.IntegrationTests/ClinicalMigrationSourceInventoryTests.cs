using System.Data;
using System.Security.Cryptography;
using System.Text;
using DentalApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DentalApi.IntegrationTests;

// Offline review contract, intentionally not consumed by the live runners.
// Update after reviewing a deliberate migration change; never from live inspection.
public sealed class ClinicalMigrationSourceInventoryTests
{
    private static readonly string[] LegacySqlite =
    [
        "20261021062135_initialCreate",
        "20261021183338_AddEmailAndVerification",
        "20261022082458_AddVerificationPurpose",
        "20261025121740_AddPatientsTable",
        "20261026074904_AddUserTokenVersion",
        "20261026100808_AddPatientUserTenantIntegrity",
        "20261026104238_AddDoctorProfiles",
        "20261026120838_AddSchedulingFoundations",
        "20261026124849_AddClinicLocationVerification",
        "20261026131551_AddPlatformAccounts",
        "20261027180600_AddAppointmentFoundation",
        "20261102165207_AddNotificationOutbox",
        "20261102180259_AddPhoneVerificationChallenges",
        "20261125115238_AddClinicTenantRootForeignKeys",
        "20261126070328_EnforceAppointmentIntegrity",
        "20261126073027_ProtectAppointmentOverlap",
        "20261127122653_AddAppointmentReschedules"
    ];

    private static readonly string[] LegacyPostgreSql =
    [
        "20261027132306_PostgreSqlBaseline",
        "20261027180725_AddPostgreSqlAppointmentFoundation",
        "20261102165221_AddPostgreSqlNotificationOutbox",
        "20261102180315_AddPostgreSqlPhoneVerificationChallenges",
        "20261125115303_AddPostgreSqlClinicTenantRootForeignKeys",
        "20261126070352_EnforceAppointmentIntegrity",
        "20261127122656_AddPostgreSqlAppointmentReschedules"
    ];

    private sealed record ReviewedClinicalMigration(
        string Table, string Sqlite, string PostgreSql, int IndexCount, int ForeignKeyCount, int RawSqlCount,
        string SqliteRawSqlSha256, string PostgreSqlRawSqlSha256);

    // SHA256 of each Up's transaction-bound SqlOperation text, normalized to LF,
    // in declaration order with "\n---\n" separators. Protects complete trigger/function
    // bodies, including the parts provider-model comparison cannot see.
    private static readonly ReviewedClinicalMigration[] Clinical =
    [
        new("ClinicalAuditEvents", "20261127123139_AddClinicalAuditTrail",
            "20261127123142_AddPostgreSqlClinicalAuditTrail", 2, 2, 3,
            "C34E7931F9DA5FD8B7162656BD12F590B54568DB45BDF9FABD10646E48C408D4",
            "608B4454C30316B640BEC793BE4C9F4AC2130824512E0147699F8BB06EDE1AA5"),
        new("TreatmentRecords", "20261127124415_AddTreatmentRecords",
            "20261127124418_AddPostgreSqlTreatmentRecords", 1, 2, 4,
            "0B6D8D2B9D8CAC410BF07AA6C2C58DF0AF433EAC52F15D3C1E888C3122E27ADB",
            "9BB9AEEF5AD28F38FA79FBDF82168D54410145515C2577AC7D7C315AB53B6B9E"),
        new("TreatmentAddenda", "20261127125153_AddTreatmentAddenda",
            "20261127125155_AddPostgreSqlTreatmentAddenda", 1, 2, 4,
            "3757469FB55BDD15A058C9462A6FC4E7D5B700D2C758C1B0C9724CF329F6F78B",
            "DB454C0B73635C9AADAE906CAE64422FA820CEE9FF99DA5249DF9B026F6B2A47"),
        new("PerformedProcedures", "20261127150423_AddPerformedProcedures",
            "20261127150425_AddPostgreSqlPerformedProcedures", 2, 3, 4,
            "BF98C7FF51199F3A3F4EEE6B84BAB5C1DCDC921A54F592E47C382704CADE4F27",
            "29A3D9734F72075942184B0D4055B0D399F17FE15ABF104E0149494F08E8F59F")
    ];

    [Fact]
    public void Design_time_provider_histories_match_the_reviewed_source_inventory_in_order()
    {
        using var sqlite = new AppDbContextDesignTimeFactory().CreateDbContext([]);
        using var pg = new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([]);

        Assert.Equal(LegacySqlite.Concat(Clinical.Select(m => m.Sqlite)), sqlite.Database.GetMigrations());
        Assert.Equal(LegacyPostgreSql.Concat(Clinical.Select(m => m.PostgreSql)), pg.Database.GetMigrations());
        Assert.False(sqlite.Database.HasPendingModelChanges());
        Assert.False(pg.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, sqlite.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, pg.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Clinical_Up_adds_only_the_reviewed_table_indexes_and_unchanged_protections(int migrationIndex)
    {
        var expected = Clinical[migrationIndex];
        using var sqlite = new AppDbContextDesignTimeFactory().CreateDbContext([]);
        using var pg = new PostgreSqlAppDbContextDesignTimeFactory().CreateDbContext([]);

        var sqliteShape = VerifyUp(sqlite, expected.Sqlite, expected, expected.SqliteRawSqlSha256, postgres: false);
        var pgShape = VerifyUp(pg, expected.PostgreSql, expected, expected.PostgreSqlRawSqlSha256, postgres: true);
        Assert.Equal(sqliteShape, pgShape);
        Assert.Equal(ConnectionState.Closed, sqlite.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, pg.Database.GetDbConnection().State);
    }

    private static string[] VerifyUp(AppDbContext db, string id, ReviewedClinicalMigration expected,
        string expectedRawSqlSha256, bool postgres)
    {
        var assembly = db.GetService<IMigrationsAssembly>();
        var up = assembly.CreateMigration(assembly.Migrations[id], db.Database.ProviderName!).UpOperations;
        Assert.All(up, op => Assert.True(op is CreateTableOperation or CreateIndexOperation or SqlOperation));
        var table = Assert.Single(up.OfType<CreateTableOperation>());
        Assert.Equal(expected.Table, table.Name);
        Assert.Null(table.Schema);
        Assert.NotNull(table.PrimaryKey);
        Assert.Equal(expected.ForeignKeyCount, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk =>
        {
            Assert.Null(fk.PrincipalSchema);
            Assert.Equal(ReferentialAction.Restrict, fk.OnDelete);
            Assert.Equal("ClinicId", fk.Columns[0]);
            Assert.Equal("ClinicId", fk.PrincipalColumns![0]);
        });

        var indexes = up.OfType<CreateIndexOperation>().ToArray();
        Assert.Equal(expected.IndexCount, indexes.Length);
        Assert.All(indexes, i =>
        {
            Assert.Equal(expected.Table, i.Table);
            Assert.Null(i.Schema);
        });

        var rawSql = up.OfType<SqlOperation>().ToArray();
        Assert.Equal(expected.RawSqlCount, rawSql.Length);
        Assert.All(rawSql, command => Assert.False(command.SuppressTransaction));
        var body = string.Join("\n---\n", rawSql.Select(command => command.Sql.ReplaceLineEndings("\n")));
        Assert.Equal(expectedRawSqlSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))));
        if (postgres)
        {
            Assert.Contains($"BEFORE UPDATE OR DELETE ON \"{expected.Table}\"", body, StringComparison.Ordinal);
            Assert.Contains($"BEFORE TRUNCATE ON \"{expected.Table}\"", body, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains($"BEFORE UPDATE ON \"{expected.Table}\"", body, StringComparison.Ordinal);
            Assert.Contains($"BEFORE DELETE ON \"{expected.Table}\"", body, StringComparison.Ordinal);
            // The frozen raw SQL additionally includes the provider's replace guard.
            Assert.Contains("_NoReplace", body, StringComparison.Ordinal);
        }

        // Cross-provider parity is limited to these logical fields; SQL dialect,
        // type mappings, generated expressions and runtime enforcement are separate.
        return new[] { $"PK:{table.PrimaryKey.Name}:{string.Join(",", table.PrimaryKey.Columns)}" }
            .Concat(table.Columns.Select(col => $"COL:{col.Name}:{col.IsNullable}"))
            .Concat(table.ForeignKeys.Select(fk => $"FK:{fk.Name}:{fk.PrincipalTable}:{string.Join(",", fk.Columns)}:{string.Join(",", fk.PrincipalColumns!)}"))
            .Concat(indexes.Select(i => $"IX:{i.Name}:{i.IsUnique}:{string.Join(",", i.Columns)}"))
            .Order(StringComparer.Ordinal).ToArray();
    }
}
