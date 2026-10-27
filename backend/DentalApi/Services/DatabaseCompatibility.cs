using System.Data;
using DentalApi.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace DentalApi.Services;

internal static class DatabaseCompatibility
{
    // SQLite's default non-deferred transaction acquires its write lock before reads.
    // PostgreSQL serializable also protects predicate reads (e.g. an absent code/schedule).
    internal static Task<IDbContextTransaction> BeginWriteAsync(AppDbContext db) =>
        db.Database.IsNpgsql()
            ? db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
            : db.Database.BeginTransactionAsync();

    internal static bool IsConflict(Exception error, bool sqliteUnique = false, bool sqlitePrimaryKey = false)
    {
        if (error is DbUpdateConcurrencyException) return true;
        // Npgsql's non-retrying execution strategy may wrap transient errors in
        // InvalidOperationException, including a DbUpdateException from SaveChanges.
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            if (cause is PostgresException pg)
                return pg.SqlState is "23505" or "40001" or "40P01";

        // Preserve each caller's existing SQLite constraint handling.
        var provider = error is DbUpdateException ? error.InnerException : error;
        return provider switch
        {
            SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6 ||
                (sqliteUnique && sqlite.SqliteExtendedErrorCode == 2067) ||
                (sqlitePrimaryKey && sqlite.SqliteExtendedErrorCode == 1555),
            _ => false
        };
    }
}
