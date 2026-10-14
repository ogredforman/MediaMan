using MediaMan.Fakes;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Tests.Upgrades;

/// <summary>
/// Makes the database fixture for the current schema: <c>npm run fixture:database</c>, run when a release changes the
/// schema (SPEC §9 resilience). Explicit, so it never runs with <c>npm test</c>. It writes
/// <c>server/tests/fixtures/databases/&lt;latest migration&gt;.sql</c> and refuses to replace one: a released fixture is
/// frozen, since it stands for databases already out there.
/// </summary>
public class UpgradeFixtureWriter
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    public async Task Write_the_fixture_for_the_current_schema()
    {
        await using var db = await TempDatabase.CreateMigratedAsync(Ct);
        string head;
        await using (var context = db.CreateContext())
        {
            head = context.Database.GetMigrations().Last();
            await UpgradeFixtureData.SeedAsync(context, Ct);
        }

        string dump;
        using (var connection = db.OpenRawConnection())
        {
            dump = await SqlDump.DumpAsync(connection, Ct);
        }

        var target = Path.Combine(UpgradeFixtures.SourceDirectory(), head + ".sql");
        File.Exists(target).Should().BeFalse($"{target} is a released fixture and is never replaced");
        var header =
            $"-- MediaMan database fixture: the schema at migration {head}, with UpgradeFixtureData.\n" +
            "-- Made by `npm run fixture:database` (UpgradeFixtureWriter). Frozen: never edit or regenerate it.\n";
        await File.WriteAllTextAsync(target, header + dump, Ct);
    }
}
