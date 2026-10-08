using System.Security.Cryptography;
using MediaMan.Auth;
using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.Dev;

/// <summary>The dev configuration's Superadmin, and whether requests without a token are treated as it.</summary>
public sealed class DevAccount(bool autoSignIn)
{
    public bool AutoSignIn { get; } = autoSignIn;

    /// <summary>Set by <see cref="DevAccountSeeder"/> on startup, before the server takes requests.</summary>
    public Guid? UserId { get; internal set; }
}

/// <summary>
/// The dev configuration has no setup wizard (SPEC §8 setup [P]), so it makes sure a Superadmin exists: the one already
/// stored, or a new <c>dev</c> account with a random password nobody knows (it's never logged, SPEC §9).
/// </summary>
public sealed partial class DevAccountSeeder(IServiceScopeFactory scopes, DevAccount account, ILogger<DevAccountSeeder> logger)
    : IHostedService
{
    public const string Username = "dev";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        var existing = await db.Users.AsNoTracking()
            .Where(u => u.IsSuperadmin)
            .Select(u => new { u.Id, u.Username })
            .SingleOrDefaultAsync(cancellationToken);

        var (id, username) = existing is not null
            ? (existing.Id, existing.Username)
            : await CreateAsync(scope.ServiceProvider.GetRequiredService<AccountService>(), cancellationToken);
        account.UserId = id;
        if (account.AutoSignIn)
        {
            LogAutoSignIn(logger, username);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<(Guid, string)> CreateAsync(AccountService accounts, CancellationToken cancellationToken)
    {
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var user = await accounts.CreateSuperadminAsync(Username, password, cancellationToken);
        return (user.Id, user.Username);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dev auto sign-in is on: requests without a token act as Superadmin {Username}")]
    private static partial void LogAutoSignIn(ILogger logger, string username);
}
