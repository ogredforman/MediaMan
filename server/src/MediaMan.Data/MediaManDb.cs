using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data;

/// <summary>Entry point for hosting the database (ADR 0003).</summary>
public static class MediaManDb
{
    /// <summary>Points the context at the SQLite file at <paramref name="databasePath"/> with WAL and foreign keys on.</summary>
    public static DbContextOptionsBuilder<MediaManDbContext> Configure(
        DbContextOptionsBuilder<MediaManDbContext> builder,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
        }.ToString();

        builder.UseSqlite(connectionString).AddInterceptors(SqlitePragmaInterceptor.Instance);
        return builder;
    }

    /// <summary>Options for the SQLite file at <paramref name="databasePath"/>.</summary>
    public static DbContextOptions<MediaManDbContext> CreateOptions(string databasePath) =>
        Configure(new DbContextOptionsBuilder<MediaManDbContext>(), databasePath).Options;
}
