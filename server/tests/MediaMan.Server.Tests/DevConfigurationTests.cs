using System.Net;
using System.Net.Http.Json;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Metadata.Tmdb;
using MediaMan.Playback.Probing;
using MediaMan.Server.Dev;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>The dev configuration exists only in Debug builds of the server (`MEDIAMAN_DEV`), so these tests need a Debug test run.</summary>
public sealed class DevConfigurationTests : IDisposable
{
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"), "Media");

    public DevConfigurationTests() => Directory.CreateDirectory(_mediaPath);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_mediaPath)!, recursive: true);

    private MediaManFactory DevFactory(string? dataDirectory = null, Dictionary<string, string>? extra = null)
    {
        var settings = new Dictionary<string, string> { ["MediaMan:Dev:MediaPath"] = _mediaPath };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return dataDirectory is null
            ? new MediaManFactory("Development", settings)
            : new MediaManFactory("Development", settings) { DataDirectory = dataDirectory, KeepDataDirectory = true };
    }

    private static async Task<List<Core.Domain.Library>> LibrariesAsync(MediaManFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        return await db.Libraries.AsNoTracking().ToListAsync(ct);
    }

    [Fact]
    public async Task Dev_configuration_registers_the_media_folder_as_an_external_library()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = DevFactory();

        var libraries = await LibrariesAsync(factory, ct);

        var library = libraries.Should().ContainSingle().Subject;
        library.Kind.Should().Be(LibraryKind.External);
        library.ContentType.Should().Be(ContentType.Mixed);
        library.Name.Should().Be("Media");
        library.RootPaths.Should().Equal(_mediaPath);
    }

    [Fact]
    public async Task Restarting_on_the_same_database_does_not_register_the_folder_twice()
    {
        var ct = TestContext.Current.CancellationToken;
        var dataDirectory = Path.Combine(Path.GetDirectoryName(_mediaPath)!, "data");

        await using (var first = DevFactory(dataDirectory))
        {
            (await LibrariesAsync(first, ct)).Should().ContainSingle();
        }

        await using var second = DevFactory(dataDirectory);
        (await LibrariesAsync(second, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task Dev_configuration_does_not_write_under_the_media_folder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = DevFactory();

        await LibrariesAsync(factory, ct);

        Directory.EnumerateFileSystemEntries(_mediaPath).Should().BeEmpty();
    }

    [Fact]
    public async Task Fake_providers_are_registered_by_default()
    {
        await using var factory = DevFactory();

        factory.Services.GetRequiredService<IMetadataProvider>().Should().BeOfType<FakeMetadataProvider>();
        factory.Services.GetRequiredService<IArtworkProvider>().Should().BeOfType<FakeArtworkProvider>();
        factory.Services.GetRequiredService<IMediaProbe>().Should().BeOfType<FakeMediaProbe>();
        factory.Services.GetRequiredService<IFileWatcher>().Should().BeOfType<FakeFileWatcher>();
    }

    [Fact]
    public async Task Choosing_a_real_provider_that_does_not_exist_yet_fails_with_the_fix()
    {
        await using var factory = DevFactory(extra: new() { ["MediaMan:Dev:Providers:Artwork"] = "Real" });

        var start = () => factory.Services;

        start.Should().Throw<InvalidOperationException>()
            .WithMessage("*Artwork*a later stage*Set MediaMan:Dev:Providers:Artwork to Fake*");
    }

    [Fact]
    public async Task Choosing_the_real_metadata_provider_registers_tmdb()
    {
        await using var factory = DevFactory(extra: new() { ["MediaMan:Dev:Providers:Metadata"] = "Real" });

        factory.Services.GetRequiredService<IMetadataProvider>().Should().BeOfType<TmdbMetadataProvider>();
    }

    [Fact]
    public async Task Choosing_the_real_media_probe_registers_ffprobe()
    {
        await using var factory = DevFactory(extra: new() { ["MediaMan:Dev:Providers:MediaProbe"] = "Real" });

        factory.Services.GetRequiredService<IMediaProbe>().Should().BeOfType<FfprobeMediaProbe>();
    }

    [Fact]
    public async Task A_missing_media_folder_fails_with_the_fix()
    {
        await using var factory = new MediaManFactory(
            "Development",
            new Dictionary<string, string> { ["MediaMan:Dev:MediaPath"] = Path.Combine(_mediaPath, "missing") });

        var start = () => factory.Services;

        start.Should().Throw<InvalidOperationException>().WithMessage("*doesn't exist*Set MediaMan:Dev:MediaPath*");
    }

    [Fact]
    public async Task Outside_development_no_library_or_fake_is_registered()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new MediaManFactory(
            "Testing",
            new Dictionary<string, string> { ["MediaMan:Dev:MediaPath"] = _mediaPath });

        (await LibrariesAsync(factory, ct)).Should().BeEmpty();
        factory.Services.GetRequiredService<IMetadataProvider>().Should().BeOfType<TmdbMetadataProvider>();
        factory.Services.GetService<IMediaProbe>().Should().BeNull();
    }

    [Fact]
    public async Task Dev_configuration_seeds_one_superadmin_and_keeps_it_across_restarts()
    {
        var ct = TestContext.Current.CancellationToken;
        var dataDirectory = Path.Combine(Path.GetDirectoryName(_mediaPath)!, "data");
        Guid firstId;
        await using (var first = DevFactory(dataDirectory))
        {
            firstId = (await SuperadminsAsync(first, ct)).Should().ContainSingle().Subject.Id;
            first.Services.GetRequiredService<DevAccount>().UserId.Should().Be(firstId);
        }

        await using var second = DevFactory(dataDirectory);
        (await SuperadminsAsync(second, ct)).Should().ContainSingle().Which.Id.Should().Be(firstId);
        second.Services.GetRequiredService<DevAccount>().UserId.Should().Be(firstId);
    }

    [Fact]
    public async Task With_auto_sign_in_a_request_without_a_token_acts_as_the_dev_superadmin()
    {
        await using var factory = DevFactory(extra: new() { ["MediaMan:Dev:AutoSignIn"] = "true" });
        using var client = factory.CreateClient();

        using var libraries = await client.GetAsync(new Uri("/libraries", UriKind.Relative), TestContext.Current.CancellationToken);
        using var logout = await client.PostAsync(new Uri("/auth/logout", UriKind.Relative), null, TestContext.Current.CancellationToken);

        libraries.StatusCode.Should().Be(HttpStatusCode.OK);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Auto_sign_in_does_not_accept_an_invalid_token()
    {
        await using var factory = DevFactory(extra: new() { ["MediaMan:Dev:AutoSignIn"] = "true" });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-token");

        using var response = await client.GetAsync(new Uri("/libraries", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Development", true, false)] // dev configuration, auto sign-in not set
    [InlineData("Testing", true, true)] // not Development
    [InlineData("Development", false, true)] // no media path
    [InlineData("Production", true, true)]
    public async Task Outside_auto_sign_in_requests_without_a_token_are_refused(string environment, bool withMediaPath, bool autoSignIn)
    {
        var settings = new Dictionary<string, string> { ["MediaMan:Dev:AutoSignIn"] = autoSignIn ? "true" : "false" };
        if (withMediaPath)
        {
            settings["MediaMan:Dev:MediaPath"] = _mediaPath;
        }

        await using var factory = new MediaManFactory(environment, settings);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/libraries", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Without_seeding_the_server_starts_un_set_up_until_setup_runs()
    {
        var ct = TestContext.Current.CancellationToken;
        var settings = new Dictionary<string, string>
        {
            ["MediaMan:Dev:MediaPath"] = _mediaPath,
            ["MediaMan:Dev:Seed"] = "false",
            ["MediaMan:Dev:AutoSignIn"] = "true",
        };
        await using var factory = new MediaManFactory("Development", settings) { CompleteSetup = false };
        using var client = factory.CreateClient();

        using var before = await client.GetAsync(new Uri("/libraries", UriKind.Relative), ct);
        (await SuperadminsAsync(factory, ct)).Should().BeEmpty();
        (await LibrariesAsync(factory, ct)).Should().BeEmpty();

        using var setup = await client.PostAsJsonAsync(
            new Uri("/setup", UriKind.Relative),
            new { username = "owner", password = "correct horse battery", mode = "local", library = new { path = _mediaPath, contentType = "mixed" } },
            ct);
        using var after = await client.GetAsync(new Uri("/libraries", UriKind.Relative), ct);

        before.StatusCode.Should().Be((HttpStatusCode)423);
        (await before.Content.ReadAsStringAsync(ct)).Should().Contain("setup.required");
        setup.StatusCode.Should().Be(HttpStatusCode.OK);
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "auto sign-in has no dev account to act as");
        (await SuperadminsAsync(factory, ct)).Should().ContainSingle().Which.Username.Should().Be("owner");
        (await LibrariesAsync(factory, ct)).Should().ContainSingle().Which.RootPaths.Should().Equal(_mediaPath);
    }

    private static async Task<List<User>> SuperadminsAsync(MediaManFactory factory, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        return await db.Users.AsNoTracking().Where(u => u.IsSuperadmin).ToListAsync(ct);
    }
}
