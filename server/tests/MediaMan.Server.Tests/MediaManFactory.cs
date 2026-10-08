using System.Net;
using System.Net.Http.Headers;
using MediaMan.Auth;
using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>A signed-in test user: an access token for a fresh session of a user holding exactly its role's permissions.</summary>
public sealed record TestUser(Guid UserId, Guid SessionId, string AccessToken);

/// <summary>Hosts the real server in-process with its data directory in a per-test temp folder.</summary>
public sealed class MediaManFactory(string environment = "Testing", IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    public string DataDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Keeps the data directory on dispose, so a second factory can restart on the same database.</summary>
    public bool KeepDataDirectory { get; init; }

    /// <summary>Extra service registrations applied after the server's own, e.g. fakes for the scan pipeline.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Stands in for the real image downloader in every test, so no test fetches artwork over the network.</summary>
    public FakeImageDownloader Images { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("MediaMan:DataDirectory", DataDirectory);
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IStartupFilter, TestRemoteAddress>();
            s.AddSingleton<IImageDownloader>(Images);

            // Argon2id at its production cost makes every sign-in slow; tests only need it to work.
            s.AddSingleton(new PasswordHasher(new Argon2Parameters(MemoryKib: 1024, Iterations: 1, Parallelism: 1)));
        });
        if (ConfigureServices is not null)
        {
            builder.ConfigureTestServices(ConfigureServices);
        }
    }

    /// <summary>A client signed in as a user holding every permission (not the Superadmin, so setup stays free).</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(CancellationToken ct) =>
        CreateClient(await CreateUserAsync(ct, [.. Permissions.All]));

    /// <summary>A client sending <paramref name="user"/>'s access token.</summary>
    public HttpClient CreateClient(TestUser user)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    /// <summary>Creates a user whose role holds exactly <paramref name="permissions"/>, and signs it in.</summary>
    public async Task<TestUser> CreateUserAsync(CancellationToken ct, params string[] permissions)
    {
        await using var scope = Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MediaManDbContext>();
        var clock = services.GetRequiredService<IClock>();
        var role = new Role { Id = Ids.New(clock), Name = "Role " + Guid.NewGuid().ToString("N"), Permissions = [.. permissions] };
        var username = "user-" + Guid.NewGuid().ToString("N");
        const string password = "a test user's password";
        db.Roles.Add(role);
        db.Users.Add(new User
        {
            Id = Ids.New(clock),
            Username = username,
            PasswordHash = services.GetRequiredService<PasswordHasher>().Hash(password),
            RoleId = role.Id,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        var tokens = (await services.GetRequiredService<SessionService>().SignInAsync(username, password, "Test", ct)).Tokens!;
        var claims = (await services.GetRequiredService<AccessTokens>().ValidateAsync(tokens.AccessToken))!;
        return new TestUser(claims.UserId, claims.SessionId, tokens.AccessToken);
    }

    /// <summary>
    /// The in-process server has no client address, so requests come from loopback unless they send
    /// <see cref="RemoteAddressHeader"/> (an IP, or <c>none</c> for no address). Exists only in tests.
    /// </summary>
    public const string RemoteAddressHeader = "X-Test-Remote-Address";

    private sealed class TestRemoteAddress : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((http, nextMiddleware) =>
            {
                var value = http.Request.Headers[RemoteAddressHeader].ToString();
                http.Request.Headers.Remove(RemoteAddressHeader);
                http.Connection.RemoteIpAddress = value switch
                {
                    "" => IPAddress.Loopback,
                    "none" => null,
                    _ => IPAddress.Parse(value),
                };
                return nextMiddleware(http);
            });
            next(app);
        };
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
