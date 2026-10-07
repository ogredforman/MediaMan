using MediaMan.Data;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>The server creates the per-install key on first run and encrypts secrets with it (ADR 0003, ADR 0008).</summary>
public sealed class InstallKeyHostingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_start_creates_the_key_next_to_the_database_and_a_restart_reuses_it()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
        var keyPath = Path.Combine(dataDirectory, "mediaman.key");
        string protectedSecret;
        byte[] key;

        await using (var first = new MediaManFactory { DataDirectory = dataDirectory, KeepDataDirectory = true })
        {
            (await first.CreateClient().GetAsync("/health", Ct)).EnsureSuccessStatusCode();
            key = await File.ReadAllBytesAsync(keyPath, Ct);
            protectedSecret = first.Services.GetRequiredService<ISecretProtector>().Protect("socks5-password");
        }

        await using var second = new MediaManFactory { DataDirectory = dataDirectory };
        (await second.CreateClient().GetAsync("/health", Ct)).EnsureSuccessStatusCode();

        key.Should().HaveCount(32);
        (await File.ReadAllBytesAsync(keyPath, Ct)).Should().Equal(key);
        protectedSecret.Should().NotContain("socks5-password");
        second.Services.GetRequiredService<ISecretProtector>().Unprotect(protectedSecret).Should().Be("socks5-password");
    }
}
