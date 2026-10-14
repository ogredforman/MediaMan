using MediaMan.Core;
using MediaMan.Data;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.Hosting;

/// <summary>
/// Brings the database schema up to date before the server takes requests (ADR 0003), copying the database aside first
/// and putting it back if a migration fails (SPEC §9 resilience). A failure stops the host before anything else starts.
/// </summary>
public sealed partial class DatabaseMigrator(
    IServiceProvider services,
    IOptions<MediaManOptions> options,
    IClock clock,
    ILogger<DatabaseMigrator> logger) : IHostedService
{
    /// <summary>Starts the one line the server writes when it can't start, so the desktop app can show it.</summary>
    public const string FatalPrefix = "MEDIAMAN-FATAL: ";

    /// <summary>The exit code when the database couldn't be upgraded.</summary>
    public const int UpgradeFailedExitCode = 3;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var paths = options.Value;
        Directory.CreateDirectory(paths.DataDirectory);
        var protector = services.GetRequiredService<ISecretProtector>();
        try
        {
            var result = await DatabaseUpgrade.RunAsync(
                paths.DatabasePath,
                paths.BackupDirectory,
                () => new MediaManDbContext(MediaManDb.CreateOptions(paths.DatabasePath), protector),
                clock,
                cancellationToken);
            if (result.BackupPath is not null)
            {
                LogUpgraded(result.Applied.Count, result.Applied, result.BackupPath);
            }
        }
        catch (DatabaseUpgradeException ex)
        {
            LogUpgradeFailed(ex.Message);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Writes the failure as one <see cref="FatalPrefix"/> line and sets the exit code; used by <c>Program</c>.</summary>
    public static void ReportFatal(DatabaseUpgradeException exception, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(error);
        error.WriteLine(FatalPrefix + exception.Message.ReplaceLineEndings(" "));
        Environment.ExitCode = UpgradeFailedExitCode;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Updated the database with {Count} migration(s) ({Migrations}); the copy taken first is at {BackupPath}")]
    private partial void LogUpgraded(int count, IReadOnlyList<string> migrations, string backupPath);

    [LoggerMessage(Level = LogLevel.Critical, Message = "{Problem}")]
    private partial void LogUpgradeFailed(string problem);
}
