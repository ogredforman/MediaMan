using MediaMan.Core.Domain;
using MediaMan.Fakes;
using MediaMan.Library.Scanning;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Tests;

public sealed class LibraryScannerTests : IAsyncLifetime
{
    private readonly FakeMediaProbe _probe = new();
    private readonly FakeClock _clock = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"), "media");
    private readonly Guid _libraryId = Guid.NewGuid();
    private TempDatabase _db = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = _db.CreateContext();
        context.Libraries.Add(new Core.Domain.Library
        {
            Id = _libraryId,
            Name = "Media",
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = [_root],
        });
        await context.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);
    }

    [Fact]
    public async Task First_scan_stores_probed_and_parsed_video_files_and_skips_the_rest()
    {
        CreateFile("Movies/Heat (1995)/Heat.1995.1080p.BluRay.x264-GRP.mkv");
        CreateFile("Shows/Example Show/Season 1/Example.Show.S01E01E02.720p.mkv");
        CreateFile("Shows/Example Show/Specials/Example.Show.S00E01.mkv");
        CreateFile("Movies/Heat (1995)/Heat.1995.1080p.BluRay.x264-GRP.nfo");
        CreateFile("Movies/Heat (1995)/Heat.1995.en.srt");
        CreateFile("Movies/Heat (1995)/poster.jpg");
        CreateFile("Movies/Heat (1995)/heat-sample.mkv");
        CreateFile("Movies/Heat (1995)/Extras/Making Of.mkv");
        CreateFile("Movies/Heat (1995)/behind the scenes/Interview.mp4");
        CreateFile("Movies/Heat (1995)/Sample/heat.mkv");

        var result = await ScanAsync();

        result.Should().Be(new ScanResult(Added: 3, Changed: 0, Unchanged: 0, Restored: 0, Missing: 0, Unreadable: 0, Purged: 0));
        var files = await FilesAsync();
        files.Select(f => Path.GetFileName(f.Path)).Should().BeEquivalentTo(
            "Heat.1995.1080p.BluRay.x264-GRP.mkv", "Example.Show.S01E01E02.720p.mkv", "Example.Show.S00E01.mkv");
        files.Should().AllSatisfy(f =>
        {
            f.Status.Should().Be(MediaFileStatus.Pending);
            f.Probe.Should().BeEquivalentTo(FakeMediaProbe.Hd1080);
            f.AttentionReason.Should().BeNull();
            f.LibraryId.Should().Be(_libraryId);
        });
        var heat = files.Single(f => f.Path.EndsWith(".x264-GRP.mkv", StringComparison.Ordinal));
        heat.Path.Should().Be(Path.Combine(_root, "Movies", "Heat (1995)", "Heat.1995.1080p.BluRay.x264-GRP.mkv"));
        heat.Parsed!.Title.Should().Be("Heat");
        heat.Parsed.Year.Should().Be(1995);
        heat.SizeBytes.Should().Be(new FileInfo(heat.Path).Length);
        var episode = files.Single(f => f.Path.Contains("S01E01E02", StringComparison.Ordinal));
        episode.Parsed!.Season.Should().Be(1);
        episode.Parsed.Episodes.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Rescan_of_unchanged_files_probes_nothing()
    {
        CreateFile("a/Movie.One.2001.mkv");
        CreateFile("b/Movie.Two.2002.mkv");
        await ScanAsync();
        var probes = _probe.ProbedPaths.Count;

        var result = await ScanAsync();

        result.Should().Be(new ScanResult(Added: 0, Changed: 0, Unchanged: 2, Restored: 0, Missing: 0, Unreadable: 0, Purged: 0));
        _probe.ProbedPaths.Should().HaveCount(probes);
    }

    [Fact]
    public async Task Changed_size_or_mtime_reprobes_and_resets_to_pending()
    {
        var bigger = CreateFile("Movie.One.2001.mkv");
        var touched = CreateFile("Movie.Two.2002.mkv");
        CreateFile("Movie.Three.2003.mkv");
        await ScanAsync();
        await using (var context = _db.CreateContext())
        {
            await context.MediaFiles.ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, MediaFileStatus.Matched), Ct);
        }

        _probe.Map("Movie.One.*", FakeMediaProbe.Uhd2160Hdr10);
        File.WriteAllText(bigger, "a longer placeholder than before");
        File.SetLastWriteTimeUtc(touched, File.GetLastWriteTimeUtc(touched).AddMinutes(5));

        var result = await ScanAsync();

        result.Should().Be(new ScanResult(Added: 0, Changed: 2, Unchanged: 1, Restored: 0, Missing: 0, Unreadable: 0, Purged: 0));
        _probe.ProbedPaths.Should().HaveCount(5);
        var files = await FilesAsync();
        var one = files.Single(f => f.Path == bigger);
        one.Probe.Should().BeEquivalentTo(FakeMediaProbe.Uhd2160Hdr10);
        one.SizeBytes.Should().Be(new FileInfo(bigger).Length);
        one.Status.Should().Be(MediaFileStatus.Pending);
        files.Single(f => f.Path == touched).Status.Should().Be(MediaFileStatus.Pending);
        files.Single(f => f.Path.EndsWith("Three.2003.mkv", StringComparison.Ordinal)).Status.Should().Be(MediaFileStatus.Matched);
    }

    [Fact]
    public async Task Unreadable_file_needs_attention_and_the_scan_continues()
    {
        var broken = CreateFile("Broken.Movie.2010.mkv");
        CreateFile("Good.Movie.2011.mkv");
        _probe.Fail("Broken.*");

        var result = await ScanAsync();

        result.Should().Be(new ScanResult(Added: 2, Changed: 0, Unchanged: 0, Restored: 0, Missing: 0, Unreadable: 1, Purged: 0));
        var files = await FilesAsync();
        var bad = files.Single(f => f.Path == broken);
        bad.Status.Should().Be(MediaFileStatus.NeedsAttention);
        bad.AttentionReason.Should().Be(LibraryScanner.UnreadableReason);
        bad.Probe.Should().BeNull();
        bad.Parsed!.Title.Should().Be("Broken Movie");
        files.Single(f => f.Path != broken).Status.Should().Be(MediaFileStatus.Pending);
    }

    [Fact]
    public async Task Deleted_file_is_missing_keeps_its_links_and_is_restored_when_it_returns()
    {
        var path = CreateFile("Movie.One.2001.mkv");
        await ScanAsync();
        var playableId = await LinkToMovieAsync(path, MediaFileStatus.Matched);
        var hidden = path + ".away";
        File.Move(path, hidden);
        _clock.Advance(TimeSpan.FromHours(1));

        var gone = await ScanAsync();

        gone.Missing.Should().Be(1);
        var missing = (await FilesAsync()).Single();
        missing.Status.Should().Be(MediaFileStatus.Missing);
        missing.MissingSince.Should().Be(_clock.UtcNow);
        (await LinksAsync()).Should().ContainSingle(l => l.PlayableId == playableId);

        File.Move(hidden, path);
        var back = await ScanAsync();

        back.Should().Be(new ScanResult(Added: 0, Changed: 0, Unchanged: 0, Restored: 1, Missing: 0, Unreadable: 0, Purged: 0));
        var restored = (await FilesAsync()).Single();
        restored.Status.Should().Be(MediaFileStatus.Matched);
        restored.MissingSince.Should().BeNull();
        restored.Id.Should().Be(missing.Id);
        (await LinksAsync()).Should().ContainSingle(l => l.PlayableId == playableId);
    }

    [Fact]
    public async Task Ignored_file_stays_ignored_while_missing_and_after_it_returns()
    {
        var path = CreateFile("Movie.One.2001.mkv");
        await ScanAsync();
        await using (var context = _db.CreateContext())
        {
            await context.MediaFiles.ExecuteUpdateAsync(s => s.SetProperty(f => f.Status, MediaFileStatus.Ignored), Ct);
        }

        File.Move(path, path + ".away");
        (await ScanAsync()).Missing.Should().Be(1);
        var away = (await FilesAsync()).Single();
        away.Status.Should().Be(MediaFileStatus.Ignored);
        away.MissingSince.Should().Be(_clock.UtcNow);

        File.Move(path + ".away", path);
        (await ScanAsync()).Restored.Should().Be(1);
        var back = (await FilesAsync()).Single();
        back.Status.Should().Be(MediaFileStatus.Ignored);
        back.MissingSince.Should().BeNull();
    }

    [Fact]
    public async Task Missing_files_are_purged_with_their_links_after_thirty_days()
    {
        var path = CreateFile("Movie.One.2001.mkv");
        await ScanAsync();
        await LinkToMovieAsync(path, MediaFileStatus.Matched);
        File.Delete(path);
        await ScanAsync();

        _clock.Advance(LibraryScanner.MissingRetention - TimeSpan.FromMinutes(1));
        (await ScanAsync()).Purged.Should().Be(0);
        (await FilesAsync()).Should().ContainSingle();

        _clock.Advance(TimeSpan.FromMinutes(1));
        (await ScanAsync()).Purged.Should().Be(1);
        (await FilesAsync()).Should().BeEmpty();
        (await LinksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_root_marks_its_files_missing_without_failing()
    {
        CreateFile("Movie.One.2001.mkv");
        await ScanAsync();
        Directory.Move(_root, _root + "-unmounted");

        try
        {
            var result = await ScanAsync();

            result.Missing.Should().Be(1);
            (await FilesAsync()).Single().Status.Should().Be(MediaFileStatus.Missing);
        }
        finally
        {
            Directory.Move(_root + "-unmounted", _root);
        }
    }

    [Fact]
    public async Task Files_of_other_libraries_are_left_alone()
    {
        var otherId = Guid.NewGuid();
        var otherPath = Path.Combine(Path.GetDirectoryName(_root)!, "other", "Other.Movie.1999.mkv");
        await using (var context = _db.CreateContext())
        {
            context.Libraries.Add(new Core.Domain.Library { Id = otherId, Name = "Other", RootPaths = [Path.GetDirectoryName(otherPath)!] });
            context.MediaFiles.Add(new MediaFile { Id = Guid.NewGuid(), LibraryId = otherId, Path = otherPath, Status = MediaFileStatus.Matched });
            await context.SaveChangesAsync(Ct);
        }

        CreateFile("Movie.One.2001.mkv");
        await ScanAsync();

        await using var read = _db.CreateContext();
        (await read.MediaFiles.SingleAsync(f => f.LibraryId == otherId, Ct)).Status.Should().Be(MediaFileStatus.Matched);
    }

    [Fact]
    public async Task Scan_writes_nothing_under_the_root()
    {
        CreateFile("Movies/Heat (1995)/Heat.1995.mkv");
        CreateFile("Movies/Heat (1995)/Heat.1995.nfo");
        var before = Snapshot();

        await ScanAsync();
        await ScanAsync();

        Snapshot().Should().Equal(before);
    }

    [Fact]
    public async Task Cancelled_scan_throws()
    {
        CreateFile("Movie.One.2001.mkv");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await using var context = _db.CreateContext();
        var act = () => new LibraryScanner(context, _probe, _clock).ScanAsync(_libraryId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Unknown_library_throws()
    {
        await using var context = _db.CreateContext();
        var act = () => new LibraryScanner(context, _probe, _clock).ScanAsync(Guid.NewGuid(), Ct);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private string CreateFile(string relativePath)
    {
        var path = Path.Combine([_root, .. relativePath.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "placeholder");
        return path;
    }

    private async Task<ScanResult> ScanAsync()
    {
        await using var context = _db.CreateContext();
        return await new LibraryScanner(context, _probe, _clock).ScanAsync(_libraryId, Ct);
    }

    private async Task<List<MediaFile>> FilesAsync()
    {
        await using var context = _db.CreateContext();
        return await context.MediaFiles.Where(f => f.LibraryId == _libraryId).ToListAsync(Ct);
    }

    private async Task<List<FileLink>> LinksAsync()
    {
        await using var context = _db.CreateContext();
        return await context.FileLinks.ToListAsync(Ct);
    }

    private async Task<Guid> LinkToMovieAsync(string path, MediaFileStatus status)
    {
        await using var context = _db.CreateContext();
        var file = await context.MediaFiles.SingleAsync(f => f.Path == path, Ct);
        var title = new Title { Id = Guid.NewGuid(), Kind = TitleKind.Movie, Name = "Movie One" };
        var playable = Playable.ForMovie(Guid.NewGuid(), title.Id);
        context.AddRange(title, playable, new FileLink { MediaFileId = file.Id, PlayableId = playable.Id });
        file.Status = status;
        await context.SaveChangesAsync(Ct);
        return playable.Id;
    }

    private List<(string Path, long Size, DateTime Mtime)> Snapshot() =>
        [.. Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(p => File.Exists(p) ? (p, new FileInfo(p).Length, File.GetLastWriteTimeUtc(p)) : (p, -1L, Directory.GetLastWriteTimeUtc(p)))];
}
