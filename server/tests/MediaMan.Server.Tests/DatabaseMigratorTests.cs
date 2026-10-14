using System.Net;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Server.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.Tests;

/// <summary>
/// The server upgrades an older database after copying it aside, and doesn't start on one it couldn't upgrade
/// (SPEC §9 resilience).
/// </summary>
public class DatabaseMigratorTests
{
    private const string ScanTimes = "20261009175531_AddLibraryScanTimes";
    private const string VersionPreferences = "20261010024327_AddVersionPreferences";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A data directory holding a database at <paramref name="migration"/>, closed so it is one file.</summary>
    private static async Task<string> DataDirectoryWithAsync(string migration, string? extraSql = null)
    {
        await using var source = TempDatabase.CreateEmpty();
        await using (var context = source.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(migration, Ct);
            if (extraSql is not null)
            {
                await context.Database.OpenConnectionAsync(Ct);
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandText = extraSql;
                await command.ExecuteNonQueryAsync(Ct);
            }
        }

        SqliteConnection.ClearAllPools();
        var dataDirectory = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        File.Copy(source.Path, Path.Combine(dataDirectory, MediaManOptions.DatabaseFileName));
        return dataDirectory;
    }

    [Fact]
    public async Task An_older_database_is_copied_aside_and_upgraded_at_startup()
    {
        await using var factory = new MediaManFactory { DataDirectory = await DataDirectoryWithAsync(VersionPreferences) };
        using var client = factory.CreateClient();

        (await client.GetAsync(new Uri("/health", UriKind.Relative), Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var backup = DatabaseUpgrade.BackupsIn(Path.Combine(factory.DataDirectory, "backups")).Should().ContainSingle().Which;
        Path.GetFileName(backup).Should().EndWith($"-before-{VersionPreferences}.db");
    }

    [Fact]
    public async Task The_server_does_not_start_on_a_database_it_could_not_upgrade()
    {
        var dataDirectory = await DataDirectoryWithAsync(ScanTimes, "CREATE TABLE \"VersionPreferences\" (\"X\" INTEGER);");
        await using var factory = new MediaManFactory { DataDirectory = dataDirectory, CompleteSetup = false };

        var start = () => factory.CreateClient();

        start.Should().Throw<Exception>("the host doesn't start");
        await AssertRestoredAsync(dataDirectory);
    }

    [Fact]
    public async Task The_migrator_says_which_step_failed_and_puts_the_database_back()
    {
        var dataDirectory = await DataDirectoryWithAsync(ScanTimes, "CREATE TABLE \"VersionPreferences\" (\"X\" INTEGER);");
        try
        {
            using var services = new ServiceCollection()
                .AddSingleton<ISecretProtector>(new AesGcmSecretProtector(TempDatabase.TestKey))
                .BuildServiceProvider();
            var migrator = new DatabaseMigrator(
                services,
                Options.Create(new MediaManOptions { DataDirectory = dataDirectory }),
                new FakeClock(),
                NullLogger<DatabaseMigrator>.Instance);

            var start = () => migrator.StartAsync(Ct);

            var failure = (await start.Should().ThrowAsync<DatabaseUpgradeException>()).Which;
            failure.FailedMigration.Should().Be(VersionPreferences);
            failure.Restored.Should().BeTrue();
            failure.Message.Should().Contain(Path.Combine(dataDirectory, "backups"));
            await AssertRestoredAsync(dataDirectory);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static async Task AssertRestoredAsync(string dataDirectory)
    {
        SqliteConnection.ClearAllPools();
        await using var restored = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataDirectory, MediaManOptions.DatabaseFileName), Pooling = false }.ToString());
        await restored.OpenAsync(Ct);
        await using var command = restored.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1;";
        (await command.ExecuteScalarAsync(Ct)).Should().Be(ScanTimes, "the database is back as it was");
    }

    [Fact]
    public void A_failed_upgrade_is_reported_as_one_line_and_an_exit_code()
    {
        var previous = Environment.ExitCode;
        try
        {
            using var error = new StringWriter();

            DatabaseMigrator.ReportFatal(new DatabaseUpgradeException("It failed.\nReinstall the previous version."), error);

            error.ToString().Should().Be($"MEDIAMAN-FATAL: It failed. Reinstall the previous version.{Environment.NewLine}");
            Environment.ExitCode.Should().Be(DatabaseMigrator.UpgradeFailedExitCode);
        }
        finally
        {
            Environment.ExitCode = previous;
        }
    }
}
