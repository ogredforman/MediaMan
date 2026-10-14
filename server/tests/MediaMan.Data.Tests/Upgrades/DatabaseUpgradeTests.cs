using MediaMan.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaMan.Data.Tests.Upgrades;

/// <summary>
/// Upgrades copy the database aside first and never leave a half-migrated database behind (SPEC §9 resilience).
/// </summary>
public class DatabaseUpgradeTests
{
    private const string ScanTimes = "20261009175531_AddLibraryScanTimes";
    private const string MatchOverrides = "20261009230057_AddMatchOverrides";
    private const string VersionPreferences = "20261010024327_AddVersionPreferences";
    private const string LibraryRow =
        """
        INSERT INTO "Libraries" ("Id", "Name", "Kind", "ContentType", "RootPaths", "ScanIntervalMinutes")
        VALUES ('0199D0C0-0000-7000-8000-000000000001', 'Media', 'External', 'Mixed', '[]', 60);
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string BackupsOf(TempDatabase db) => Path.Combine(Path.GetDirectoryName(db.Path)!, "backups");

    private static Task<DatabaseUpgradeResult> UpgradeAsync(TempDatabase db, FakeClock? clock = null) =>
        DatabaseUpgrade.RunAsync(db.Path, BackupsOf(db), () => db.CreateContext(), clock ?? new FakeClock(), Ct);

    /// <summary>A database at <paramref name="migration"/> holding one library, written through MediaMan's (WAL) connection.</summary>
    private static async Task<TempDatabase> OlderDatabaseAsync(string migration, string extraSql = "")
    {
        var db = TempDatabase.CreateEmpty();
        await using var context = db.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(migration, Ct);
        await context.Database.OpenConnectionAsync(Ct);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = LibraryRow + extraSql;
        await command.ExecuteNonQueryAsync(Ct);
        return db;
    }

    [Fact]
    public async Task A_new_database_is_migrated_without_a_copy()
    {
        await using var db = TempDatabase.CreateEmpty();

        var result = await UpgradeAsync(db);

        result.BackupPath.Should().BeNull();
        await using var context = db.CreateContext();
        result.Applied.Should().Equal(context.Database.GetMigrations());
        Directory.Exists(BackupsOf(db)).Should().BeFalse();
    }

    [Fact]
    public async Task An_up_to_date_database_is_left_alone()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);

        var result = await UpgradeAsync(db);

        result.Should().BeEquivalentTo(new DatabaseUpgradeResult(null, []));
        Directory.Exists(BackupsOf(db)).Should().BeFalse();
    }

    [Fact]
    public async Task An_older_database_is_copied_aside_then_migrated()
    {
        await using var db = await OlderDatabaseAsync(VersionPreferences);

        var result = await UpgradeAsync(db);

        result.Applied.Should().Equal("20261011224526_AddMediaFileAddedAt");
        result.BackupPath.Should().Be(Path.Combine(BackupsOf(db), $"mediaman-20260101-000000Z-before-{VersionPreferences}.db"));
        File.Exists(result.BackupPath + ".partial").Should().BeFalse();
        using (var backup = OpenRaw(result.BackupPath!))
        {
            (await ScalarAsync(backup, LastMigrationSql)).Should().Be(VersionPreferences);
            (await ScalarAsync(backup, "SELECT \"Name\" FROM \"Libraries\";")).Should().Be("Media", "the copy holds what was still in the WAL");
        }

        await using var context = db.CreateContext();
        (await context.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
        (await context.Libraries.SingleAsync(Ct)).Name.Should().Be("Media");
    }

    [Fact]
    public async Task A_failed_migration_puts_the_database_back_and_says_what_happened()
    {
        // VersionPreferences already exists, so AddMatchOverrides applies and then AddVersionPreferences fails half way.
        await using var db = await OlderDatabaseAsync(ScanTimes, "CREATE TABLE \"VersionPreferences\" (\"X\" INTEGER);");

        var act = () => UpgradeAsync(db);

        var failure = (await act.Should().ThrowAsync<DatabaseUpgradeException>()).Which;
        failure.FailedMigration.Should().Be(VersionPreferences);
        failure.Restored.Should().BeTrue();
        failure.BackupPath.Should().NotBeNull();
        File.Exists(failure.BackupPath).Should().BeTrue("the copy is kept");
        failure.Message.Should().Contain(VersionPreferences).And.Contain(failure.BackupPath!)
            .And.Contain("back as it was").And.Contain("Reinstall the previous MediaMan version");

        File.Exists(db.Path + "-wal").Should().BeFalse("the half-migrated database's WAL is gone");
        using var restored = OpenRaw(db.Path);
        (await ScalarAsync(restored, LastMigrationSql)).Should().Be(ScanTimes);
        (await ScalarAsync(restored, "SELECT COUNT(*) FROM sqlite_master WHERE \"name\" = 'MatchOverrides';")).Should().Be(0L);
        (await ScalarAsync(restored, "SELECT \"Name\" FROM \"Libraries\";")).Should().Be("Media");
    }

    [Fact]
    public async Task Nothing_is_migrated_when_the_copy_cannot_be_made()
    {
        await using var db = await OlderDatabaseAsync(VersionPreferences);
        await File.WriteAllTextAsync(BackupsOf(db), "a file where the backups folder goes", Ct);

        var act = () => UpgradeAsync(db);

        (await act.Should().ThrowAsync<DatabaseUpgradeException>()).Which.Message
            .Should().Contain("couldn't save a copy").And.Contain("Nothing was changed");
        await using var context = db.CreateContext();
        (await context.Database.GetAppliedMigrationsAsync(Ct)).Last().Should().Be(VersionPreferences);
    }

    [Fact]
    public async Task Only_the_newest_copies_are_kept()
    {
        await using var db = await OlderDatabaseAsync(VersionPreferences);
        var backups = BackupsOf(db);
        Directory.CreateDirectory(backups);
        for (var day = 1; day <= 4; day++)
        {
            await File.WriteAllTextAsync(Path.Combine(backups, $"mediaman-2025010{day}-000000Z-before-{ScanTimes}.db"), "old", Ct);
        }

        await File.WriteAllTextAsync(Path.Combine(backups, "notes.txt"), "not a copy", Ct);

        var result = await UpgradeAsync(db);

        DatabaseUpgrade.BackupsIn(backups).Should().Equal(
            result.BackupPath,
            Path.Combine(backups, $"mediaman-20250104-000000Z-before-{ScanTimes}.db"),
            Path.Combine(backups, $"mediaman-20250103-000000Z-before-{ScanTimes}.db"));
        File.Exists(Path.Combine(backups, "notes.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task A_copy_made_in_the_same_second_does_not_replace_another()
    {
        await using var db = await OlderDatabaseAsync(VersionPreferences);
        var backups = BackupsOf(db);
        Directory.CreateDirectory(backups);
        var existing = Path.Combine(backups, $"mediaman-20260101-000000Z-before-{VersionPreferences}.db");
        await File.WriteAllTextAsync(existing, "earlier", Ct);

        var result = await UpgradeAsync(db);

        result.BackupPath.Should().Be(Path.Combine(backups, $"mediaman-20260101-000000Z-before-{VersionPreferences}-2.db"));
        (await File.ReadAllTextAsync(existing, Ct)).Should().Be("earlier");
    }

    private const string LastMigrationSql =
        "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1;";

    private static SqliteConnection OpenRaw(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Ct);
    }
}
