using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Fakes;
using MediaMan.Library.Importing;
using MediaMan.Library.Matching;
using MediaMan.Parsing;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Tests;

public sealed class MetadataImporterTests : IAsyncLifetime
{
    private static readonly MatcherOptions _noDelays = new() { RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero] };

    private readonly FakeMetadataProvider _provider = new();
    private readonly FakeClock _clock = new();
    private readonly Guid _libraryId = Guid.NewGuid();
    private TempDatabase _db = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _db = await TempDatabase.CreateMigratedAsync(Ct);
        await using var context = _db.CreateContext();
        context.Libraries.Add(new Core.Domain.Library
        {
            Id = _libraryId,
            Name = "Media",
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = [Path.Combine(Path.GetTempPath(), "media")],
        });
        await context.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Movie_imports_title_artwork_playable_and_link()
    {
        var file = await AddFileAsync("Movies/The.Matrix.1999.1080p.mkv");

        var result = await MatchAndImportAsync();

        result.Should().Be(new ImportResult(1, 0, 0));
        await using var context = _db.CreateContext();
        var title = await context.Titles.SingleAsync(Ct);
        title.Kind.Should().Be(TitleKind.Movie);
        title.Name.Should().Be("The Matrix");
        title.Year.Should().Be(1999);
        title.Overview.Should().NotBeNullOrEmpty();
        title.Genres.Should().NotBeEmpty();
        title.Rating.Should().NotBeNull();
        title.ExternalIds.Tmdb.Should().Be("603");
        var artwork = await context.Artwork.Where(a => a.TitleId == title.Id).ToListAsync(Ct);
        artwork.Select(a => a.Type).Should().Contain([ArtworkType.Poster, ArtworkType.Backdrop]).And.OnlyHaveUniqueItems();
        var playable = await context.Playables.SingleAsync(Ct);
        playable.Should().BeEquivalentTo(Playable.ForMovie(playable.Id, title.Id));
        var link = await context.FileLinks.SingleAsync(Ct);
        link.Should().BeEquivalentTo(new FileLink { MediaFileId = file, PlayableId = playable.Id, Part = 1 });
        var stored = await context.MediaFiles.SingleAsync(Ct);
        stored.Status.Should().Be(MediaFileStatus.Matched);
        stored.AttentionReason.Should().BeNull();
    }

    [Fact]
    public async Task Two_versions_of_a_movie_share_one_title_and_playable()
    {
        await AddFileAsync("Movies/The.Matrix.1999.1080p.mkv");
        await AddFileAsync("Movies/The.Matrix.1999.2160p.HDR.mkv");

        var result = await MatchAndImportAsync();

        result.Linked.Should().Be(2);
        await using var context = _db.CreateContext();
        (await context.Titles.CountAsync(Ct)).Should().Be(1);
        var playable = await context.Playables.SingleAsync(Ct);
        (await context.FileLinks.ToListAsync(Ct)).Should().HaveCount(2).And.OnlyContain(l => l.PlayableId == playable.Id);
        _provider.TitleRequests.Should().ContainSingle("the second file reuses the stored title");
    }

    [Fact]
    public async Task Episode_imports_show_season_episodes_poster_and_stills()
    {
        var file = await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E02.720p.mkv");

        await MatchAndImportAsync();

        await using var context = _db.CreateContext();
        var title = await context.Titles.SingleAsync(Ct);
        title.Kind.Should().Be(TitleKind.Show);
        title.ExternalIds.Should().BeEquivalentTo(new TitleExternalIds { Tmdb = "1396", Tvdb = "81189" });
        var season = await context.Seasons.SingleAsync(Ct);
        season.Should().Match<Season>(s => s.TitleId == title.Id && s.Number == 1);
        var episodes = await context.Episodes.OrderBy(e => e.Number).ToListAsync(Ct);
        episodes.Select(e => e.Number).Should().Equal(1, 2, 3, 4, 5, 6, 7);
        episodes.Should().OnlyContain(e => e.SeasonId == season.Id && e.Name != null);
        (await context.Artwork.CountAsync(a => a.SeasonId == season.Id && a.Type == ArtworkType.Poster, Ct)).Should().Be(1);
        (await context.Artwork.CountAsync(a => a.EpisodeId != null && a.Type == ArtworkType.Still, Ct)).Should().Be(7);
        var playable = await context.Playables.SingleAsync(Ct);
        playable.EpisodeId.Should().Be(episodes[1].Id, "only episodes with files get a Playable");
        (await context.FileLinks.SingleAsync(Ct)).Should().Match<FileLink>(l => l.MediaFileId == file && l.PlayableId == playable.Id);
        _provider.SeasonRequests.Should().Equal((new ExternalId(ExternalProviders.Tmdb, "1396"), 1));
    }

    [Fact]
    public async Task Multi_episode_file_links_every_episode_in_order()
    {
        var file = await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E01-E03.720p.mkv");

        await MatchAndImportAsync();

        await using var context = _db.CreateContext();
        var links = await context.FileLinks.Where(l => l.MediaFileId == file).OrderBy(l => l.Part).ToListAsync(Ct);
        links.Select(l => l.Part).Should().Equal(1, 2, 3);
        var numbers = new List<int>();
        foreach (var link in links)
        {
            var playable = await context.Playables.SingleAsync(p => p.Id == link.PlayableId, Ct);
            numbers.Add((await context.Episodes.SingleAsync(e => e.Id == playable.EpisodeId, Ct)).Number);
        }

        numbers.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Anime_absolute_number_resolves_to_season_and_episode()
    {
        await AddFileAsync("Anime/One Piece/[SubsPlease] One Piece - 20 (1080p).mkv");

        var result = await MatchAndImportAsync();

        result.Linked.Should().Be(1);
        var episode = await LinkedEpisodeAsync();
        episode.Season.Should().Be(3);
        episode.Episode.Number.Should().Be(6);
        episode.Episode.AbsoluteNumber.Should().Be(20);
    }

    [Fact]
    public async Task Specials_import_as_season_zero()
    {
        await AddFileAsync("Shows/Sherlock/Specials/Sherlock.S00E01.The.Abominable.Bride.mkv");

        await MatchAndImportAsync();

        var episode = await LinkedEpisodeAsync();
        episode.Season.Should().Be(0);
        episode.Episode.Number.Should().Be(1);
    }

    [Fact]
    public async Task Later_episode_in_a_stored_season_skips_the_provider()
    {
        await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E02.720p.mkv");
        await MatchAndImportAsync();
        var titleRequests = _provider.TitleRequests.Count(r => r.Value == "1396");
        await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E05.720p.mkv");

        var result = await ImportAsync((await MatchAsync()).Decisions);

        result.Linked.Should().Be(1);
        _provider.SeasonRequests.Should().ContainSingle("season 1 is already stored with every episode");
        _provider.TitleRequests.Count(r => r.Value == "1396").Should().Be(titleRequests + 1, "only the matcher asks for the title");
        await using var context = _db.CreateContext();
        (await context.Titles.CountAsync(Ct)).Should().Be(1);
        (await context.Seasons.CountAsync(Ct)).Should().Be(1);
        (await context.Episodes.CountAsync(Ct)).Should().Be(7);
        (await context.Playables.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Importing_the_same_decision_twice_is_idempotent()
    {
        var file = await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E01-E03.720p.mkv");
        var decisions = (await MatchAsync()).Decisions;
        await ImportAsync(decisions);
        await SetStatusAsync(file, MediaFileStatus.Pending);

        var result = await ImportAsync(decisions);

        result.Linked.Should().Be(1);
        await using var context = _db.CreateContext();
        (await context.Titles.CountAsync(Ct)).Should().Be(1);
        (await context.Episodes.CountAsync(Ct)).Should().Be(7);
        (await context.Playables.CountAsync(Ct)).Should().Be(3);
        (await context.FileLinks.CountAsync(Ct)).Should().Be(3);
        (await context.Artwork.CountAsync(Ct)).Should().Be(await ExpectedArtworkAsync(context));
    }

    [Fact]
    public async Task Rematched_file_replaces_its_links()
    {
        var file = await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E01-E03.720p.mkv");
        await MatchAndImportAsync();
        await SetStatusAsync(file, MediaFileStatus.Pending);
        var rematch = new MatchDecision(file, new ExternalId(ExternalProviders.Tmdb, "1396"), TitleKind.Show, 2, [4], null, 1.0);

        await ImportAsync([rematch]);

        await using var context = _db.CreateContext();
        var link = await context.FileLinks.SingleAsync(Ct);
        link.Part.Should().Be(1);
        var playable = await context.Playables.SingleAsync(p => p.Id == link.PlayableId, Ct);
        var episode = await context.Episodes.SingleAsync(e => e.Id == playable.EpisodeId, Ct);
        episode.Number.Should().Be(4);
        (await context.Seasons.SingleAsync(s => s.Id == episode.SeasonId, Ct)).Number.Should().Be(2);
    }

    [Fact]
    public async Task Provider_failure_leaves_the_file_pending_with_nothing_written()
    {
        var file = await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E02.720p.mkv");
        var decisions = (await MatchAsync()).Decisions;
        _provider.FailNext(FakeProviderError.RateLimited, calls: 4);

        var result = await ImportAsync(decisions);

        result.Should().Be(new ImportResult(0, 0, 1));
        await using var context = _db.CreateContext();
        (await context.MediaFiles.SingleAsync(f => f.Id == file, Ct)).Status.Should().Be(MediaFileStatus.Pending);
        (await context.Titles.CountAsync(Ct)).Should().Be(0);
        (await context.FileLinks.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Provider_failure_mid_show_stages_nothing_for_that_file()
    {
        var file = await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E02.720p.mkv");
        var decisions = (await MatchAsync()).Decisions;

        _provider.FailNextSeason(FakeProviderError.Timeout, calls: 4);

        var result = await ImportAsync(decisions);

        result.Deferred.Should().Be(1, "the title call succeeded but the season call kept failing");
        await using var check = _db.CreateContext();
        (await check.MediaFiles.SingleAsync(f => f.Id == file, Ct)).Status.Should().Be(MediaFileStatus.Pending);
        (await check.Titles.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Episode_the_provider_lacks_needs_attention()
    {
        var file = await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E02.720p.mkv");
        var missing = new MatchDecision(file, new ExternalId(ExternalProviders.Tmdb, "1396"), TitleKind.Show, 1, [42], null, 1.0);

        var result = await ImportAsync([missing]);

        result.Should().Be(new ImportResult(0, 1, 0));
        await using var context = _db.CreateContext();
        var stored = await context.MediaFiles.SingleAsync(f => f.Id == file, Ct);
        stored.Status.Should().Be(MediaFileStatus.NeedsAttention);
        stored.AttentionReason.Should().Be(MetadataImporter.EpisodeNotFoundReason);
        (await context.Titles.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Files_no_longer_pending_are_skipped()
    {
        var file = await AddFileAsync("Movies/The.Matrix.1999.1080p.mkv");
        var decisions = (await MatchAsync()).Decisions;
        await SetStatusAsync(file, MediaFileStatus.Missing);

        var result = await ImportAsync(decisions);

        result.Should().Be(new ImportResult(0, 0, 0));
    }

    private static async Task<int> ExpectedArtworkAsync(Data.MediaManDbContext context)
    {
        var titleArt = await context.Artwork.Where(a => a.TitleId != null).Select(a => a.Type).Distinct().CountAsync(Ct);
        var seasons = await context.Seasons.CountAsync(Ct);
        var episodes = await context.Episodes.CountAsync(Ct);
        return titleArt + seasons + episodes;
    }

    private async Task<(int Season, Episode Episode)> LinkedEpisodeAsync()
    {
        await using var context = _db.CreateContext();
        var link = await context.FileLinks.SingleAsync(Ct);
        var playable = await context.Playables.SingleAsync(p => p.Id == link.PlayableId, Ct);
        var episode = await context.Episodes.SingleAsync(e => e.Id == playable.EpisodeId, Ct);
        var season = await context.Seasons.SingleAsync(s => s.Id == episode.SeasonId, Ct);
        return (season.Number, episode);
    }

    private async Task<ImportResult> MatchAndImportAsync() => await ImportAsync((await MatchAsync()).Decisions);

    private async Task<MatchRunResult> MatchAsync()
    {
        await using var context = _db.CreateContext();
        return await new LibraryMatcher(context, _provider, _clock, _noDelays).MatchPendingAsync(_libraryId, Ct);
    }

    private async Task<ImportResult> ImportAsync(IReadOnlyList<MatchDecision> decisions)
    {
        await using var context = _db.CreateContext();
        return await new MetadataImporter(context, _provider, _clock, _noDelays).ImportAsync(decisions, Ct);
    }

    private async Task<Guid> AddFileAsync(string relativePath)
    {
        var parts = relativePath.Split('/');
        await using var context = _db.CreateContext();
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            LibraryId = _libraryId,
            Path = Path.Combine([Path.GetTempPath(), "media", .. parts]),
            Mtime = _clock.UtcNow,
            Parsed = ReleaseParser.Parse(parts[^1], parts[..^1]),
            Status = MediaFileStatus.Pending,
        };
        context.MediaFiles.Add(file);
        await context.SaveChangesAsync(Ct);
        return file.Id;
    }

    private async Task SetStatusAsync(Guid id, MediaFileStatus status)
    {
        await using var context = _db.CreateContext();
        var file = await context.MediaFiles.SingleAsync(f => f.Id == id, Ct);
        file.Status = status;
        await context.SaveChangesAsync(Ct);
    }
}
