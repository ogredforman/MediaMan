using MediaMan.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Tests.Upgrades;

/// <summary>Where the database fixtures are: one per released schema, copied to <c>fixtures/databases</c> in the test output.</summary>
public static class UpgradeFixtures
{
    public static string OutputDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "databases");

    public static TheoryData<string> All()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(OutputDirectory, "*.sql").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    /// <summary>The fixtures folder in the source tree, found by walking up from the test output to <c>server/</c>.</summary>
    public static string SourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MediaMan.sln")))
            {
                return Path.Combine(dir.FullName, "tests", "fixtures", "databases");
            }
        }

        throw new DirectoryNotFoundException("No server/MediaMan.sln above the test output.");
    }
}

/// <summary>
/// Every released database migrates to the current schema with its data intact (SPEC §9 resilience). Each release that
/// changes the schema adds its fixture with <c>npm run fixture:database</c>.
/// </summary>
public class UpgradeFixtureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The first fixture is at today's head migration, so until the schema next changes the theory below migrates nothing;
    // it starts proving real upgrades once a later release adds a migration (DatabaseUpgradeTests covers them meanwhile).
    [Fact]
    public void There_is_a_fixture_for_the_first_released_schema()
    {
        UpgradeFixtures.All().Should().Contain(row => row.Data == "20261011224526_AddMediaFileAddedAt.sql");
    }

    [Theory]
    [MemberData(nameof(UpgradeFixtures.All), MemberType = typeof(UpgradeFixtures))]
    public async Task Fixture_migrates_to_the_current_schema_with_its_data_intact(string fixture)
    {
        await using var db = TempDatabase.CreateEmpty();
        await SqlDump.LoadAsync(db.Path, await File.ReadAllTextAsync(Path.Combine(UpgradeFixtures.OutputDirectory, fixture), Ct), Ct);
        var before = await RowCountsAsync(db);

        var result = await DatabaseUpgrade.RunAsync(
            db.Path, Path.Combine(Path.GetDirectoryName(db.Path)!, "backups"), () => db.CreateContext(), new FakeClock(), Ct);

        await using (var context = db.CreateContext())
        {
            (await context.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
            await UpgradeFixtureData.AssertIntactAsync(context, Ct);
        }

        (result.BackupPath is null).Should().Be(result.Applied.Count == 0, "a copy is taken only when migrations ran");
        var after = await RowCountsAsync(db);
        foreach (var (table, count) in before)
        {
            if (after.TryGetValue(table, out var now))
            {
                now.Should().BeGreaterThanOrEqualTo(count, $"no rows of {table} are lost");
            }
        }

        using var connection = db.OpenRawConnection();
        (await ScalarAsync(connection, "PRAGMA integrity_check;")).Should().Be("ok");
        (await ScalarAsync(connection, "PRAGMA foreign_key_check;")).Should().BeNull("every reference still points at a row");
    }

    private static async Task<Dictionary<string, long>> RowCountsAsync(TempDatabase db)
    {
        using var connection = db.OpenRawConnection();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT \"name\" FROM sqlite_master WHERE \"type\" = 'table' AND \"name\" NOT LIKE 'sqlite_%';";
            await using var reader = await list.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            counts[table] = (long)(await ScalarAsync(connection, $"SELECT COUNT(*) FROM \"{table}\";"))!;
        }

        return counts;
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Ct);
    }
}
