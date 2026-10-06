using MediaMan.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Server.Hosting;

/// <summary>Brings the database schema up to date before the server takes requests (ADR 0003).</summary>
public sealed class DatabaseMigrator(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
