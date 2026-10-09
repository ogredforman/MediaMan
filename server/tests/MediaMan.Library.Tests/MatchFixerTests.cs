using MediaMan.Core;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Fakes;
using MediaMan.Library.Importing;
using MediaMan.Library.Matching;
using MediaMan.Library.Scanning;
using MediaMan.Parsing;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Library.Tests;

public sealed class MatchFixerTests : IAsyncLifetime
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
    public async Task Relinking_a_file_in_review_links_the_chosen_movie_removes_the_review_and_records_an_override()
    {
        var file = await AddFileAsync("Movies/Dune.mkv");
        await MatchAndImportAsync();
        (await StatusOfAsync(file)).Should().Be(MediaFileStatus.NeedsAttention, "two Dune films make the name ambiguous");

        var outcome = await RelinkAsync(file, new MatchChoice("tmdb:movie/438631", null, null));

        await using var context = _db.CreateContext();
        var title = await context.Titles.SingleAsync(Ct);
        outcome.Should().Be(new RelinkOutcome(title.Id));
        title.Should().Match<Title>(t => t.Name == "Dune" && t.Year == 2021);
        var stored = await context.MediaFiles.SingleAsync(Ct);
        stored.Status.Should().Be(MediaFileStatus.Matched);
        stored.AttentionReason.Should().BeNull();
        (await context.MatchReviews.AnyAsync(Ct)).Should().BeFalse();
        var playable = await context.Playables.SingleAsync(Ct);
        (await context.FileLinks.SingleAsync(Ct)).Should().BeEquivalentTo(new FileLink { MediaFileId = file, PlayableId = playable.Id, Part = 1 });
        var recorded = await context.MatchOverrides.SingleAsync(Ct);
        recorded.Should().BeEquivalentTo(new
        {
            LibraryId = _libraryId,
            stored.Path,
            TitleRef = new ExternalId(ExternalProviders.Tmdb, "movie/438631"),
            Kind = TitleKind.Movie,
            Season = (int?)null,
            Episodes = Array.Empty<int>(),
            AbsoluteEpisode = (int?)null,
            CreatedAt = _clock.UtcNow,
        });
    }

    [Fact]
    public async Task A_show_without_a_mapping_uses_the_season_and_episode_the_file_name_gives()
    {
        var file = await AddFileAsync("Shows/The Office/Season 1/The.Office.S01E02.mkv");
        await MatchAndImportAsync();

        var outcome = await RelinkAsync(file, new MatchChoice("tmdb:tv/2316", null, null));

        outcome.Failure.Should().BeNull();
        var (title, season, episodes) = await LinkedEpisodesAsync(file);
        title.Should().Match<Title>(t => t.Year == 2005 && t.Id == outcome.TitleId);
        season.Should().Be(1);
        episodes.Should().Equal(2);
    }

    [Fact]
    public async Task A_show_mapping_links_the_chosen_season_and_episodes_in_order()
    {
        var file = await AddFileAsync("Shows/Unsorted/bb pilot and more.mkv");

        var outcome = await RelinkAsync(file, new MatchChoice("tmdb:tv/1396", 2, [3, 2]));

        outcome.Failure.Should().BeNull();
        var (title, season, episodes) = await LinkedEpisodesAsync(file);
        title.Name.Should().Be("Breaking Bad");
        season.Should().Be(2);
        episodes.Should().Equal(3, 2);
        await using var context = _db.CreateContext();
        (await context.MatchOverrides.SingleAsync(Ct)).Should().Match<MatchOverride>(o => o.Season == 2 && o.Episodes.SequenceEqual(episodes));
    }

    [Fact]
    public async Task Relinking_a_matched_file_replaces_its_links_and_a_second_fix_updates_the_override()
    {
        var file = await AddFileAsync("Movies/The.Matrix.1999.1080p.mkv");
        await MatchAndImportAsync();
        (await StatusOfAsync(file)).Should().Be(MediaFileStatus.Matched);

        (await RelinkAsync(file, new MatchChoice("tmdb:movie/11", null, null))).Failure.Should().BeNull();
        var outcome = await RelinkAsync(file, new MatchChoice("tmdb:movie/27205", null, null));

        await using var context = _db.CreateContext();
        var link = await context.FileLinks.SingleAsync(Ct);
        var playable = await context.Playables.SingleAsync(p => p.Id == link.PlayableId, Ct);
        var title = await context.Titles.SingleAsync(t => t.Id == playable.TitleId, Ct);
        title.Name.Should().Be("Inception");
        outcome.TitleId.Should().Be(title.Id);
        (await context.MatchOverrides.SingleAsync(Ct)).TitleRef.Should().Be(new ExternalId(ExternalProviders.Tmdb, "movie/27205"));
    }

    [Fact]
    public async Task An_episode_the_provider_does_not_list_is_refused_and_changes_nothing()
    {
        var file = await AddFileAsync("Shows/The Office/Season 1/The.Office.S01E02.mkv");
        await MatchAndImportAsync();
        var before = await SnapshotAsync();

        var outcome = await RelinkAsync(file, new MatchChoice("tmdb:tv/2316", 1, [99]));

        outcome.Should().Be(RelinkOutcome.Failed(RelinkFailure.EpisodeNotFound));
        (await SnapshotAsync()).Should().Be(before);
    }

    [Fact]
    public async Task A_provider_failure_during_the_import_rolls_back_and_changes_nothing()
    {
        var file = await AddFileAsync("Shows/The Office/Season 1/The.Office.S01E02.mkv");
        await MatchAndImportAsync();
        var before = await SnapshotAsync();
        _provider.FailNextSeason(FakeProviderError.Timeout, calls: _noDelays.RetryDelays.Count + 1);

        var outcome = await RelinkAsync(file, new MatchChoice("tmdb:tv/2316", null, null));

        outcome.Should().Be(RelinkOutcome.Failed(RelinkFailure.ProviderUnavailable));
        (await SnapshotAsync()).Should().Be(before);
    }

    [Fact]
    public async Task A_provider_failure_looking_up_the_title_is_reported()
    {
        var file = await AddFileAsync("Movies/Dune.mkv");
        _provider.FailNext(FakeProviderError.RateLimited, calls: _noDelays.RetryDelays.Count + 1);

        var outcome = await RelinkAsync(file, new MatchChoice("tmdb:movie/438631", null, null));

        outcome.Should().Be(RelinkOutcome.Failed(RelinkFailure.ProviderUnavailable));
        (await StatusOfAsync(file)).Should().Be(MediaFileStatus.Pending);
    }

    [Theory]
    [InlineData("Movies/Dune.mkv", "not-an-id", null, null, "titleRef")]
    [InlineData("Movies/Dune.mkv", "anilist:1", null, null, "titleRef")]
    [InlineData("Movies/Dune.mkv", "tmdb:movie/1", null, null, "titleRef")]
    [InlineData("Movies/Dune.mkv", "tmdb:movie/438631", 1, new[] { 1 }, "episodes")]
    [InlineData("Shows/Unsorted/bb.mkv", "tmdb:tv/1396", 1, null, "episodes")]
    [InlineData("Shows/Unsorted/bb.mkv", "tmdb:tv/1396", null, new[] { 1 }, "season")]
    [InlineData("Shows/Unsorted/bb.mkv", "tmdb:tv/1396", 1, new[] { 0 }, "episodes")]
    [InlineData("Shows/Unsorted/bb.mkv", "tmdb:tv/1396", null, null, "episodes")]
    public async Task Invalid_choices_name_the_field_and_change_nothing(string path, string titleRef, int? season, int[]? episodes, string field)
    {
        var file = await AddFileAsync(path);

        var outcome = await RelinkAsync(file, new MatchChoice(titleRef, season, episodes));

        outcome.Failure.Should().Be(RelinkFailure.Invalid);
        outcome.Errors.Should().ContainKey(field);
        (await StatusOfAsync(file)).Should().Be(MediaFileStatus.Pending);
        await using var context = _db.CreateContext();
        (await context.MatchOverrides.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Missing_unreadable_unknown_and_scanning_files_are_refused()
    {
        var missing = await AddFileAsync("Movies/Dune.mkv", MediaFileStatus.Missing);
        var unreadable = await AddFileAsync("Movies/Alien.1979.mkv", MediaFileStatus.NeedsAttention, LibraryScanner.UnreadableReason);
        var choice = new MatchChoice("tmdb:movie/348", null, null);

        (await RelinkAsync(missing, choice)).Failure.Should().Be(RelinkFailure.FileUnavailable);
        (await RelinkAsync(unreadable, choice)).Failure.Should().Be(RelinkFailure.FileUnavailable);
        (await RelinkAsync(Guid.NewGuid(), choice)).Failure.Should().Be(RelinkFailure.NotFound);

        var file = await AddFileAsync("Movies/Alien.mkv");
        await using var context = _db.CreateContext();
        var fixer = CreateFixer(context);
        (await fixer.RelinkAsync(file, choice, id => id == _libraryId, Ct)).Failure.Should().Be(RelinkFailure.ScanRunning);
    }

    [Fact]
    public async Task The_matcher_follows_the_override_when_the_file_comes_back_pending_without_searching()
    {
        var file = await AddFileAsync("Movies/Dune.mkv");
        await MatchAndImportAsync();
        await RelinkAsync(file, new MatchChoice("tmdb:movie/841", null, null));
        var searches = _provider.SearchQueries.Count;

        // A changed file is re-probed and comes back Pending; a purged one comes back as a new row at the same path.
        await using (var context = _db.CreateContext())
        {
            var stored = await context.MediaFiles.SingleAsync(Ct);
            context.MediaFiles.Remove(stored);
            await context.SaveChangesAsync(Ct);
        }

        var again = await AddFileAsync("Movies/Dune.mkv");
        var run = await MatchAsync();

        run.Decisions.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new MatchDecision(again, new ExternalId(ExternalProviders.Tmdb, "movie/841"), TitleKind.Movie, null, [], null, 1));
        _provider.SearchQueries.Should().HaveCount(searches, "an override replaces the search");
        await ImportAsync(run.Decisions);
        var (status, year) = await LinkedMovieAsync(again);
        status.Should().Be(MediaFileStatus.Matched);
        year.Should().Be(1984);
    }

    [Fact]
    public async Task An_override_clears_a_stale_review_on_the_next_match()
    {
        var file = await AddFileAsync("Movies/Dune.mkv");
        await using (var context = _db.CreateContext())
        {
            context.MatchOverrides.Add(new MatchOverride
            {
                Id = Guid.NewGuid(),
                LibraryId = _libraryId,
                Path = (await context.MediaFiles.SingleAsync(Ct)).Path,
                TitleRef = new ExternalId(ExternalProviders.Tmdb, "movie/438631"),
                Kind = TitleKind.Movie,
            });
            context.MatchReviews.Add(new MatchReview { Id = Guid.NewGuid(), MediaFileId = file, Reason = LibraryMatcher.AmbiguousReason });
            await context.SaveChangesAsync(Ct);
        }

        var result = await MatchAndImportAsync();

        result.Linked.Should().Be(1);
        await using var check = _db.CreateContext();
        (await check.MatchReviews.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Search_returns_provider_candidates_and_null_when_the_provider_fails()
    {
        await using var context = _db.CreateContext();
        var fixer = CreateFixer(context);

        var results = await fixer.SearchAsync(new TitleQuery("Dune", null, TitleKind.Movie), Ct);

        results!.Select(r => r.Id.Value).Should().BeEquivalentTo("movie/438631", "movie/841");
        _provider.FailNext(FakeProviderError.Timeout);
        (await fixer.SearchAsync(new TitleQuery("Dune", null, null), Ct)).Should().BeNull();
    }

    private MatchFixer CreateFixer(Data.MediaManDbContext context) =>
        new(context, _provider, new MetadataImporter(context, _provider, _clock, _noDelays), _clock, _noDelays);

    private async Task<RelinkOutcome> RelinkAsync(Guid file, MatchChoice choice)
    {
        await using var context = _db.CreateContext();
        return await CreateFixer(context).RelinkAsync(file, choice, _ => false, Ct);
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

    private async Task<Guid> AddFileAsync(string relativePath, MediaFileStatus status = MediaFileStatus.Pending, string? reason = null)
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
            Status = status,
            AttentionReason = reason,
        };
        context.MediaFiles.Add(file);
        await context.SaveChangesAsync(Ct);
        return file.Id;
    }

    private async Task<MediaFileStatus> StatusOfAsync(Guid file)
    {
        await using var context = _db.CreateContext();
        return (await context.MediaFiles.SingleAsync(f => f.Id == file, Ct)).Status;
    }

    private async Task<(Title Title, int Season, List<int> Episodes)> LinkedEpisodesAsync(Guid file)
    {
        await using var context = _db.CreateContext();
        var links = await context.FileLinks.Where(l => l.MediaFileId == file).OrderBy(l => l.Part).ToListAsync(Ct);
        var episodes = new List<Episode>();
        foreach (var link in links)
        {
            var playable = await context.Playables.SingleAsync(p => p.Id == link.PlayableId, Ct);
            episodes.Add(await context.Episodes.SingleAsync(e => e.Id == playable.EpisodeId, Ct));
        }

        var season = await context.Seasons.SingleAsync(s => s.Id == episodes[0].SeasonId, Ct);
        var title = await context.Titles.SingleAsync(t => t.Id == season.TitleId, Ct);
        return (title, season.Number, [.. episodes.Select(e => e.Number)]);
    }

    private async Task<(MediaFileStatus Status, int? Year)> LinkedMovieAsync(Guid file)
    {
        await using var context = _db.CreateContext();
        var stored = await context.MediaFiles.SingleAsync(f => f.Id == file, Ct);
        var link = await context.FileLinks.SingleAsync(l => l.MediaFileId == file, Ct);
        var playable = await context.Playables.SingleAsync(p => p.Id == link.PlayableId, Ct);
        return (stored.Status, (await context.Titles.SingleAsync(t => t.Id == playable.TitleId, Ct)).Year);
    }

    /// <summary>Every row a relink could touch, as one comparable string.</summary>
    private async Task<string> SnapshotAsync()
    {
        await using var context = _db.CreateContext();
        var files = await context.MediaFiles.OrderBy(f => f.Path).Select(f => $"{f.Path}|{f.Status}|{f.AttentionReason}").ToListAsync(Ct);
        var reviews = await context.MatchReviews.Select(r => $"{r.MediaFileId}|{r.Reason}").ToListAsync(Ct);
        var links = await context.FileLinks.Select(l => $"{l.MediaFileId}|{l.PlayableId}|{l.Part}").ToListAsync(Ct);
        return string.Join("\n", [.. files, .. reviews, .. links,
            $"titles={await context.Titles.CountAsync(Ct)}", $"seasons={await context.Seasons.CountAsync(Ct)}",
            $"playables={await context.Playables.CountAsync(Ct)}", $"overrides={await context.MatchOverrides.CountAsync(Ct)}"]);
    }
}
