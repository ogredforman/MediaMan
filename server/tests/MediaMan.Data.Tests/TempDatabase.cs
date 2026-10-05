using MediaMan.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Tests;

/// <summary>A migrated SQLite file in a per-test temp directory, deleted on dispose.</summary>
internal sealed class TempDatabase : IAsyncDisposable
{
    public static readonly byte[] TestKey = [.. Enumerable.Range(0, AesGcmSecretProtector.KeySizeBytes).Select(i => (byte)i)];

    // One shared protector: the EF model is cached per protector instance.
    private static readonly AesGcmSecretProtector _sharedProtector = new(TestKey);

    private readonly string _directory;

    private TempDatabase(string directory)
    {
        _directory = directory;
        Path = System.IO.Path.Combine(directory, "mediaman.db");
        Protector = _sharedProtector;
    }

    public string Path { get; }

    public ISecretProtector Protector { get; }

    public static async Task<TempDatabase> CreateMigratedAsync(CancellationToken cancellationToken)
    {
        var db = CreateEmpty();
        await using var context = db.CreateContext();
        await context.Database.MigrateAsync(cancellationToken);
        return db;
    }

    public static TempDatabase CreateEmpty()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new TempDatabase(directory);
    }

    public MediaManDbContext CreateContext(ISecretProtector? protector = null) =>
        new(MediaManDb.CreateOptions(Path), protector ?? Protector);

    /// <summary>A plain connection with none of MediaMan's pragmas, to see what is actually on disk.</summary>
    public SqliteConnection OpenRawConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}
