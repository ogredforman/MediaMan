using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Metadata.Tmdb;
using MediaMan.Playback.Probing;
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
}
