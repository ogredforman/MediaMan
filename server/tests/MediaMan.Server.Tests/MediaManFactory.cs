using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>Hosts the real server in-process with its data directory in a per-test temp folder.</summary>
public sealed class MediaManFactory(string environment = "Testing", IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    public string DataDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Keeps the data directory on dispose, so a second factory can restart on the same database.</summary>
    public bool KeepDataDirectory { get; init; }

    /// <summary>Extra service registrations applied after the server's own, e.g. fakes for the scan pipeline.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("MediaMan:DataDirectory", DataDirectory);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        if (ConfigureServices is not null)
        {
            builder.ConfigureTestServices(ConfigureServices);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools(); // pooled connections keep the database file open
        if (!KeepDataDirectory && Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}
