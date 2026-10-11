using MediaMan.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaMan.Data.Tests;

public class MigrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        await using var db = TempDatabase.CreateEmpty();
        await using var context = db.CreateContext();

        await context.Database.MigrateAsync(Ct);

        (await context.Database.GetAppliedMigrationsAsync(Ct)).Should().ContainSingle(m => m.EndsWith("_Initial", StringComparison.Ordinal));
        (await context.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Model_has_no_changes_missing_from_the_migrations()
    {
        await using var db = TempDatabase.CreateEmpty();
        await using var context = db.CreateContext();

        context.Database.HasPendingModelChanges().Should().BeFalse("run `dotnet ef migrations add` after changing the model");
    }

    [Fact]
    public async Task Files_found_before_added_at_existed_take_their_mtime()
    {
        await using var db = TempDatabase.CreateEmpty();
        await using (var context = db.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("AddVersionPreferences", Ct);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Libraries" ("Id", "Name", "Kind", "ContentType", "RootPaths", "ScanIntervalMinutes")
                VALUES ('0199D0C0-0000-7000-8000-000000000001', 'Media', 'External', 'Mixed', '[]', 60);
                INSERT INTO "MediaFiles" ("Id", "LibraryId", "Path", "SizeBytes", "Mtime", "Status")
                VALUES ('0199D0C0-0000-7000-8000-000000000002', '0199D0C0-0000-7000-8000-000000000001', 'a.mkv', 1, 1700000000000, 'Matched');
                """, Ct);
            await context.Database.MigrateAsync(Ct);
        }

        await using var migrated = db.CreateContext();
        var file = await migrated.MediaFiles.SingleAsync(Ct);
        file.AddedAt.Should().Be(file.Mtime).And.Be(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
    }

    [Fact]
    public async Task Database_file_is_in_wal_mode()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);

        // WAL is persistent, so a plain connection without MediaMan's pragmas must still see it.
        using var connection = db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        (await command.ExecuteScalarAsync(Ct)).Should().Be("wal");
    }

    [Fact]
    public async Task Context_connections_enforce_foreign_keys()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = db.CreateContext();
        await context.Database.OpenConnectionAsync(Ct);

        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        (await command.ExecuteScalarAsync(Ct)).Should().Be(1L);
    }
}
