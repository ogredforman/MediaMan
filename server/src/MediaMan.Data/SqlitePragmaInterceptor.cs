using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaMan.Data;

/// <summary>
/// Applies the connection pragmas on every open: WAL so a crash or power loss can't corrupt the
/// database (SPEC §9 resilience), enforced foreign keys, and a busy timeout for concurrent writers.
/// </summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public static readonly SqlitePragmaInterceptor Instance = new();

    private const string Pragmas =
        "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";

    // EF checks that the file exists on a read-only connection, which can't switch a database that isn't in WAL mode
    // yet (one restored from a copy, say); the next writable connection does.
    private const string ReadOnlyPragmas = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmasFor(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = PragmasFor(connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string PragmasFor(DbConnection connection) =>
        new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.ReadOnly ? ReadOnlyPragmas : Pragmas;
}
