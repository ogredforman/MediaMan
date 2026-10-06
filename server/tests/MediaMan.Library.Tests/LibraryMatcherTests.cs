using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Fakes;
using MediaMan.Library.Matching;
using MediaMan.Parsing;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Tests;

public sealed class LibraryMatcherTests : IAsyncLifetime
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

    [Theory]
    [InlineData("Movies/Dune.2021.2160p.WEB-DL.mkv", "438631")]
    [InlineData("Movies/Dune (1984)/Dune.1984.1080p.BluRay.mkv", "841")]
    [InlineData("Movies/Total.Recall.1990.720p.mkv", "861")]
    [InlineData("Movies/Total.Recall.2012.Extended.1080p.mkv", "64635")]
    [InlineData("Movies/RoboCop.2014.1080p.mkv", "97020")]
    [InlineData("Movies/The.Lion.King.1994.1080p.mkv", "8587")]
    [InlineData("Movies/Alien.1979.Directors.Cut.1080p.mkv", "348")]
    [InlineData("Movies/Inception.mkv", "27205")]
    [InlineData("Movies/Amelie.2001.1080p.mkv", "194")]
    public async Task Remakes_and_lookalikes_link_to_the_right_movie(string path, string tmdbId)
    {
        var file = await AddFileAsync(path);

        var result = await MatchAsync();

        var decision = result.Decisions.Should().ContainSingle().Subject;
        decision.MediaFileId.Should().Be(file);
        decision.TitleRef.Should().Be(new ExternalId(ExternalProviders.Tmdb, tmdbId));
        decision.Kind.Should().Be(TitleKind.Movie);
        decision.Score.Should().BeGreaterThanOrEqualTo(0.85);
        (await FileAsync(file)).Status.Should().Be(MediaFileStatus.Pending, "the importer marks it Matched once linked");
    }

    [Theory]
    [InlineData("Movies/The.Matrix.1998.1080p.mkv", "603")]
    [InlineData("Movies/Dune.2020.1080p.mkv", "438631")]
    [InlineData("Movies/Ghostbusters.1985.mkv", "620")]
    public async Task Year_off_by_one_still_links(string path, string tmdbId)
    {
        await AddFileAsync(path);

        var result = await MatchAsync();

        result.Decisions.Should().ContainSingle().Which.TitleRef.Should().Be(new ExternalId(ExternalProviders.Tmdb, tmdbId));
    }

    [Theory]
    [InlineData("Shows/Doctor Who/Season 2/Doctor.Who.S02E05.720p.mkv", "57243", 2, 5)]
    [InlineData("Shows/Doctor Who (1963)/Season 1/Doctor.Who.1963.S01E02.mkv", "121", 1, 2)]
    [InlineData("Shows/The Office (US)/The.Office.2005.S02E03.mkv", "2316", 2, 3)]
    [InlineData("Shows/Battlestar Galactica 2004/Battlestar.Galactica.2004.S02E01.mkv", "1972", 2, 1)]
    [InlineData("Shows/Shameless (2011)/Shameless.2011.S01E06.mkv", "34307", 1, 6)]
    [InlineData("Shows/Breaking Bad/Season 2/Breaking.Bad.S02E06.mkv", "1396", 2, 6)]
    [InlineData("Shows/Sherlock/Specials/Sherlock.S00E01.The.Abominable.Bride.mkv", "19885", 0, 1)]
    public async Task Same_name_shows_and_specials_link_by_year_and_episode_existence(string path, string tmdbId, int season, int episode)
    {
        await AddFileAsync(path);

        var result = await MatchAsync();

        var decision = result.Decisions.Should().ContainSingle().Subject;
        decision.TitleRef.Should().Be(new ExternalId(ExternalProviders.Tmdb, tmdbId));
        decision.Kind.Should().Be(TitleKind.Show);
        decision.Season.Should().Be(season);
        decision.Episodes.Should().Equal(episode);
    }

    [Fact]
    public async Task Anime_absolute_number_links_when_the_show_has_that_many_episodes()
    {
        await AddFileAsync("Anime/One Piece/[SubsPlease] One Piece - 20 (1080p).mkv");

        var result = await MatchAsync();

        var decision = result.Decisions.Should().ContainSingle().Subject;
        decision.TitleRef.Should().Be(new ExternalId(ExternalProviders.Tmdb, "37854"));
        decision.AbsoluteEpisode.Should().Be(20);
    }

    [Theory]
    [InlineData("Movies/Dune.mkv", LibraryMatcher.AmbiguousReason, new[] { "438631", "841" })]
    [InlineData("Movies/Ghostbusters.mkv", LibraryMatcher.AmbiguousReason, new[] { "43074", "620" })]
    [InlineData("Shows/The Office/Season 1/The.Office.S01E01.mkv", LibraryMatcher.AmbiguousReason, new[] { "2316", "2996" })]
    [InlineData("Shows/Breaking Bad/Season 5/Breaking.Bad.S05E01.mkv", LibraryMatcher.LowConfidenceReason, new[] { "1396" })]
    [InlineData("Movies/Heat.1995.1080p.mkv", LibraryMatcher.NoMatchReason, new string[0])]
    public async Task Unsure_matches_go_to_review_with_the_top_candidates(string path, string reason, string[] candidateIds)
    {
        var file = await AddFileAsync(path);

        var result = await MatchAsync();

        result.Decisions.Should().BeEmpty();
        result.Reviewed.Should().Be(1);
        var stored = await FileAsync(file);
        stored.Status.Should().Be(MediaFileStatus.NeedsAttention);
        stored.AttentionReason.Should().Be(reason);
        var review = await ReviewAsync(file);
        review!.Reason.Should().Be(reason);
        review.Candidates.Select(c => c.TitleRef.Value).Should().BeEquivalentTo(candidateIds);
        review.Candidates.Select(c => c.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Rematching_a_reviewed_file_replaces_its_review()
    {
        var file = await AddFileAsync("Movies/Dune.mkv");
        await MatchAsync();
        var first = await ReviewAsync(file);
        await SetStatusAsync(file, MediaFileStatus.Pending);

        await MatchAsync();

        await using var context = _db.CreateContext();
        var reviews = await context.MatchReviews.Where(r => r.MediaFileId == file).ToListAsync(Ct);
        reviews.Should().ContainSingle().Which.Id.Should().Be(first!.Id);
    }

    [Fact]
    public async Task A_confident_match_removes_a_stale_review()
    {
        var file = await AddFileAsync("Movies/Inception.2010.mkv");
        await using (var context = _db.CreateContext())
        {
            context.MatchReviews.Add(new MatchReview { Id = Guid.NewGuid(), MediaFileId = file, Reason = LibraryMatcher.NoMatchReason });
            await context.SaveChangesAsync(Ct);
        }

        var result = await MatchAsync();

        result.Decisions.Should().ContainSingle();
        (await ReviewAsync(file)).Should().BeNull();
    }

    [Fact]
    public async Task Only_pending_files_of_the_library_are_matched()
    {
        await AddFileAsync("Movies/Inception.2010.mkv", MediaFileStatus.Matched);
        await AddFileAsync("Movies/Alien.1979.mkv", MediaFileStatus.Missing);
        await AddFileAsync("Movies/Dune.2021.mkv", MediaFileStatus.NeedsAttention);

        var result = await MatchAsync();

        result.Should().BeEquivalentTo(new MatchRunResult([], 0, 0));
        _provider.SearchQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task Persistent_provider_errors_leave_this_and_later_files_pending()
    {
        var first = await AddFileAsync("Movies/A/Alien.1979.mkv");
        var second = await AddFileAsync("Movies/B/Inception.2010.mkv");
        _provider.FailNext(FakeProviderError.RateLimited, calls: 4);

        var result = await MatchAsync();

        result.Should().BeEquivalentTo(new MatchRunResult([], 0, 2));
        _provider.SearchQueries.Should().HaveCount(4, "one call and three retries, then the provider is left alone");
        foreach (var id in new[] { first, second })
        {
            var file = await FileAsync(id);
            file.Status.Should().Be(MediaFileStatus.Pending);
            file.AttentionReason.Should().BeNull();
            (await ReviewAsync(id)).Should().BeNull();
        }

        var retry = await MatchAsync();

        retry.Decisions.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(FakeProviderError.Timeout)]
    [InlineData(FakeProviderError.RateLimited)]
    [InlineData(FakeProviderError.HttpTimeout)]
    public async Task A_transient_provider_error_is_retried(FakeProviderError error)
    {
        await AddFileAsync("Shows/Breaking Bad/Season 1/Breaking.Bad.S01E01.mkv");
        _provider.FailNext(error, calls: 2);

        var result = await MatchAsync();

        result.Decisions.Should().ContainSingle().Which.TitleRef.Value.Should().Be("1396");
        result.Deferred.Should().Be(0);
    }

    [Fact]
    public async Task Searches_and_titles_are_fetched_once_per_run()
    {
        for (var e = 1; e <= 6; e++)
        {
            await AddFileAsync($"Shows/Breaking Bad/Season 1/Breaking.Bad.S01E0{e}.mkv");
        }

        var result = await MatchAsync();

        result.Decisions.Should().HaveCount(6);
        _provider.SearchQueries.Should().ContainSingle();
        _provider.TitleRequests.Should().ContainSingle();
    }

    private async Task<MatchRunResult> MatchAsync()
    {
        await using var context = _db.CreateContext();
        return await new LibraryMatcher(context, _provider, _clock, _noDelays).MatchPendingAsync(_libraryId, Ct);
    }

    private async Task<Guid> AddFileAsync(string relativePath, MediaFileStatus status = MediaFileStatus.Pending)
    {
        var parts = relativePath.Split('/');
        var path = Path.Combine([Path.GetTempPath(), "media", .. parts]);
        await using var context = _db.CreateContext();
        var file = new MediaFile
        {
            Id = Guid.NewGuid(),
            LibraryId = _libraryId,
            Path = path,
            Mtime = _clock.UtcNow,
            Parsed = ReleaseParser.Parse(parts[^1], parts[..^1]),
            Status = status,
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
        file.AttentionReason = null;
        await context.SaveChangesAsync(Ct);
    }

    private async Task<MediaFile> FileAsync(Guid id)
    {
        await using var context = _db.CreateContext();
        return await context.MediaFiles.SingleAsync(f => f.Id == id, Ct);
    }

    private async Task<MatchReview?> ReviewAsync(Guid fileId)
    {
        await using var context = _db.CreateContext();
        return await context.MatchReviews.SingleOrDefaultAsync(r => r.MediaFileId == fileId, Ct);
    }
}
