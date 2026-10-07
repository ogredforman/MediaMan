using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Fakes;

namespace MediaMan.Core.Tests.Fakes;

public sealed class FakeMediaToolsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("mediaman-fakes-").FullName;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Clock_starts_at_a_fixed_time_and_advances()
    {
        var clock = new FakeClock();

        clock.Advance(TimeSpan.FromHours(2));

        clock.UtcNow.Should().Be(FakeClock.DefaultStart.AddHours(2));
        FluentActions.Invoking(() => clock.Advance(TimeSpan.FromSeconds(-1))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Artwork_returns_added_refs_or_none_and_records_requests()
    {
        var matrix = ExternalId.Parse("tmdb:movie/603");
        var poster = new ArtworkRef(ArtworkType.Poster, "https://images.invalid/fanart/603.jpg", "en");
        var provider = new FakeArtworkProvider().Add(matrix, poster);

        (await provider.GetArtworkAsync(matrix, Ct)).Should().Equal(poster);
        (await provider.GetArtworkAsync(ExternalId.Parse("tmdb:movie/1"), Ct)).Should().BeEmpty();
        provider.Requests.Should().Equal(matrix, ExternalId.Parse("tmdb:movie/1"));
    }

    [Fact]
    public async Task Probe_uses_the_first_matching_pattern_then_the_default()
    {
        var probe = new FakeMediaProbe()
            .Map("*2160p*", FakeMediaProbe.Uhd2160Hdr10)
            .Map("*.DV.*", FakeMediaProbe.DolbyVision)
            .Fail("broken*");

        (await probe.ProbeAsync(Placeholder("Dune.2021.2160p.DV.mkv"), Ct)).Should().Be(FakeMediaProbe.Uhd2160Hdr10);
        (await probe.ProbeAsync(Placeholder("dune.2021.dv.mkv"), Ct)).HdrFormat.Should().Be("Dolby Vision");
        (await probe.ProbeAsync(Placeholder("The.Matrix.1999.mkv"), Ct)).Should().Be(FakeMediaProbe.Hd1080);
        await FluentActions.Awaiting(() => probe.ProbeAsync(Placeholder("broken.mkv"), Ct)).Should().ThrowAsync<InvalidDataException>();
        probe.ProbedPaths.Should().HaveCount(4);
    }

    [Fact]
    public async Task Probe_fails_for_a_missing_file()
    {
        var probe = new FakeMediaProbe();

        await FluentActions.Awaiting(() => probe.ProbeAsync(Path.Combine(_root, "missing.mkv"), Ct))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void Watcher_delivers_changes_under_a_root_until_disposed()
    {
        var watcher = new FakeFileWatcher();
        var movies = Path.Combine(_root, "Movies");
        var received = new List<FileChange>();
        var subscription = watcher.Watch(movies, received.Add);
        var inside = new FileChange(FileChangeKind.Created, Path.Combine(movies, "Dune (2021)", "Dune.mkv"));
        var renamedIn = new FileChange(FileChangeKind.Renamed, Path.Combine(_root, "Elsewhere.mkv"), Path.Combine(movies, "a.mkv"));

        watcher.Raise(inside);
        watcher.Raise(new FileChange(FileChangeKind.Created, Path.Combine(_root, "Movies2", "x.mkv")));
        watcher.Raise(renamedIn);
        watcher.WatchedRoots.Should().Equal(movies);
        subscription.Dispose();
        watcher.Raise(inside);

        received.Should().Equal(inside, renamedIn);
        watcher.WatchedRoots.Should().BeEmpty();
    }

    private string Placeholder(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, []);
        return path;
    }
}
