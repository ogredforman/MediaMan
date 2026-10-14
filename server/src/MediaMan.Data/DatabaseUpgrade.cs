using MediaMan.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediaMan.Data;

/// <summary>What <see cref="DatabaseUpgrade.RunAsync"/> did: the copy it made first (null when none was needed) and the migrations it applied.</summary>
public sealed record DatabaseUpgradeResult(string? BackupPath, IReadOnlyList<string> Applied);

/// <summary>
/// The database couldn't be brought up to this version's schema, so the server must not start. <see cref="Exception.Message"/>
/// says what happened and what to do, for the user; it never holds secrets.
/// </summary>
public sealed class DatabaseUpgradeException : Exception
{
    public DatabaseUpgradeException()
    {
    }

    public DatabaseUpgradeException(string message)
        : base(message)
    {
    }

    public DatabaseUpgradeException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The migration that failed, if one did.</summary>
    public string? FailedMigration { get; init; }

    /// <summary>The copy taken before migrating, if one was.</summary>
    public string? BackupPath { get; init; }

    /// <summary>True when the database was put back as it was before the upgrade.</summary>
    public bool Restored { get; init; }
}

/// <summary>
/// Brings a database up to the current schema without risking it (SPEC §9 resilience): when migrations are pending on a
/// database that has data, it first copies the database to the backup folder; if a migration fails, it puts that copy
/// back and throws <see cref="DatabaseUpgradeException"/>, so nothing runs on a half-migrated database.
/// </summary>
public static class DatabaseUpgrade
{
    /// <summary>How many pre-upgrade copies are kept; older ones are deleted after a successful upgrade.</summary>
    public const int KeptBackups = 3;

    private const string BackupPrefix = "mediaman-";
    private const string BackupInfix = "-before-";
    private const string BackupExtension = ".db";

    /// <summary>
    /// Applies pending migrations to the database at <paramref name="databasePath"/>. <paramref name="createContext"/> makes
    /// a context on that file; each one is disposed here.
    /// </summary>
    public static async Task<DatabaseUpgradeResult> RunAsync(
        string databasePath,
        string backupDirectory,
        Func<MediaManDbContext> createContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentNullException.ThrowIfNull(createContext);
        ArgumentNullException.ThrowIfNull(clock);

        var existed = File.Exists(databasePath);
        IReadOnlyList<string> applied;
        IReadOnlyList<string> pending;
        var context = createContext();
        await using (context.ConfigureAwait(false))
        {
            applied = [.. await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)];
            pending = [.. await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)];
            if (pending.Count == 0)
            {
                return new DatabaseUpgradeResult(null, []);
            }

            if (!existed || applied.Count == 0)
            {
                // A new install: there is nothing to keep, so nothing to copy.
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                return new DatabaseUpgradeResult(null, pending);
            }
        }

        var backupPath = await BackUpAsync(databasePath, backupDirectory, applied[^1], createContext, clock, cancellationToken)
            .ConfigureAwait(false);

        string? current = null;
        try
        {
            context = createContext();
            await using (context.ConfigureAwait(false))
            {
                var migrator = context.GetService<IMigrator>();
                foreach (var migration in pending)
                {
                    current = migration;
                    await migrator.MigrateAsync(migration, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            var restoreProblem = Restore(databasePath, backupPath);
            if (ex is OperationCanceledException && restoreProblem is null)
            {
                throw;
            }

            var failed = current ?? pending[0];
            var reason = OneLine(ex.Message);
            if (restoreProblem is not null)
            {
                throw new DatabaseUpgradeException(
                    $"MediaMan couldn't update its database to this version: the step {failed} failed ({reason}), " +
                    $"and putting the database back failed too ({OneLine(restoreProblem.Message)}). Close MediaMan, replace " +
                    $"{databasePath} with the copy at {backupPath}, and delete {databasePath}-wal and {databasePath}-shm if they " +
                    "exist. Then reinstall the previous MediaMan version and report this problem with the server log.",
                    ex)
                {
                    FailedMigration = failed,
                    BackupPath = backupPath,
                    Restored = false,
                };
            }

            throw new DatabaseUpgradeException(
                $"MediaMan couldn't update its database to this version: the step {failed} failed ({reason}). Your " +
                $"database is back as it was before the update, and a copy is at {backupPath}. Reinstall the previous " +
                "MediaMan version to keep using your library, and report this problem with the server log.",
                ex)
            {
                FailedMigration = failed,
                BackupPath = backupPath,
                Restored = true,
            };
        }

        PruneBackups(backupDirectory);
        return new DatabaseUpgradeResult(backupPath, pending);
    }

    /// <summary>The pre-upgrade copies in <paramref name="backupDirectory"/>, newest first.</summary>
    public static IReadOnlyList<string> BackupsIn(string backupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(backupDirectory, $"{BackupPrefix}*{BackupInfix}*{BackupExtension}")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal),
        ];
    }

    /// <summary>A consistent single-file copy (VACUUM INTO reads through the WAL), written aside and then renamed.</summary>
    private static async Task<string> BackUpAsync(
        string databasePath,
        string backupDirectory,
        string lastApplied,
        Func<MediaManDbContext> createContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var stamp = clock.UtcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var name = $"{BackupPrefix}{stamp}Z{BackupInfix}{lastApplied}";
        string? partial = null;
        try
        {
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(backupDirectory, name + BackupExtension);
            for (var n = 2; File.Exists(backupPath); n++)
            {
                backupPath = Path.Combine(backupDirectory, $"{name}-{n}{BackupExtension}");
            }

            partial = backupPath + ".partial";
            File.Delete(partial);
            var context = createContext();
            await using (context.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlRawAsync("VACUUM INTO {0};", [partial], cancellationToken).ConfigureAwait(false);
            }

            File.Move(partial, backupPath);
            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            SqliteConnection.ClearAllPools();
            TryDelete(partial);
            throw new DatabaseUpgradeException(
                $"MediaMan didn't update its database to this version because it couldn't save a copy of it first " +
                $"({OneLine(ex.Message)}) in {backupDirectory}. Nothing was changed. Free some disk space or fix that " +
                $"folder's permissions, then start MediaMan again. The database is {databasePath}.",
                ex);
        }
    }

    /// <summary>Puts the copy back over a half-migrated database; returns the problem if that failed.</summary>
    private static Exception? Restore(string databasePath, string backupPath)
    {
        var restoring = databasePath + ".restoring";
        try
        {
            SqliteConnection.ClearAllPools();

            // The half-migrated database's WAL must go first, or SQLite would replay it onto the restored file.
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
            File.Copy(backupPath, restoring, overwrite: true);
            File.Move(restoring, databasePath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(restoring);
            return ex;
        }
    }

    private static void PruneBackups(string backupDirectory)
    {
        foreach (var old in BackupsIn(backupDirectory).Skip(KeptBackups))
        {
            TryDelete(old);
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a copy left behind is harmless.
        }
    }

    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
