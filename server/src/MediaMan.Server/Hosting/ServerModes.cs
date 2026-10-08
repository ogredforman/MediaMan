using MediaMan.Core.Domain;
using MediaMan.Data;
using MediaMan.Server.LibraryApi;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaMan.Server.Hosting;

/// <summary>
/// The mode setup recorded (the <c>server.mode</c> Setting), which overrides <c>MediaMan:Server:Mode</c>, and the mode
/// this process is listening in. A recorded change takes effect on restart (SPEC §8 setup [M]).
/// </summary>
public sealed class ServerModes
{
    public const string SettingKey = "server.mode";

    /// <summary>
    /// Reads the recorded mode once. It runs before the database migrator (Kestrel is configured when the host is built),
    /// so a database that doesn't exist yet simply has no recorded mode.
    /// </summary>
    public ServerModes(IOptions<MediaManOptions> options, IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scopes);

        if (File.Exists(options.Value.DatabasePath))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var value = scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Settings.AsNoTracking()
                    .Where(s => s.Key == SettingKey).Select(s => s.Value).SingleOrDefault();
                RecordedAtStartup = Wire.TryParse<ServerMode>(value, out var mode) ? mode : null;
            }
            catch (SqliteException)
            {
                // Not migrated yet (e.g. a first start that stopped early): nothing has been recorded.
            }
        }

        Active = RecordedAtStartup ?? options.Value.Server.Mode;
    }

    /// <summary>The mode recorded when the process started; null if setup hasn't recorded one.</summary>
    public ServerMode? RecordedAtStartup { get; }

    /// <summary>The mode this process listens in.</summary>
    public ServerMode Active { get; }

    /// <summary>Records <paramref name="mode"/> in the tracked context; the caller saves.</summary>
    public static async Task RecordAsync(MediaManDbContext db, ServerMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var setting = await db.Settings.SingleOrDefaultAsync(s => s.Key == SettingKey, cancellationToken);
        if (setting is null)
        {
            db.Settings.Add(new Setting { Key = SettingKey, Value = Wire.Name(mode) });
        }
        else
        {
            setting.Value = Wire.Name(mode);
        }
    }
}
