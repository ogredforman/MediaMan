using MediaMan.Fakes;
using Microsoft.EntityFrameworkCore;

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
