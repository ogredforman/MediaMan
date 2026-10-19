using MediaMan.Core.Domain;
using MediaMan.Playback.Progress;
using MediaMan.Server.LibraryApi;

namespace MediaMan.Server.Tests;

public sealed class HomeRulesTests
{
    private static readonly DateTimeOffset _t0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid _movie = Guid.CreateVersion7();
    private static readonly Guid _show = Guid.CreateVersion7();
    private static readonly HomePlayable _moviePlayable = new(Guid.CreateVersion7(), _movie, null);
    private static readonly HomePlayable _special = Episode(0, 1);
    private static readonly HomePlayable _s1e1 = Episode(1, 1);
    private static readonly HomePlayable _s1e2 = Episode(1, 2);
    private static readonly HomePlayable _s2e1 = Episode(2, 1);
    private static readonly HomePlayable[] _playables = [_moviePlayable, _special, _s1e1, _s1e2, _s2e1];

    private static HomePlayable Episode(int season, int number) => new(Guid.CreateVersion7(), _show, new EpisodeSlot(season, number));

    private static WatchState Watch(HomePlayable playable, double position, double duration, bool watched, int minutes) => new()
    {
        PlayableId = playable.Id,
        PositionSeconds = position,
        DurationSeconds = duration,
        Watched = watched,
        LastWatchedAt = _t0.AddMinutes(minutes),
    };

    private static HomeTitle Title(string name, int addedDays, double? rating = null, bool backdrop = false, params string[] genres) =>
        new(Guid.CreateVersion7(), name, 2000, rating, genres, _t0.AddDays(addedDays), backdrop);

    [Fact]
    public void Part_way_through_is_continue_watching_most_recent_first_with_its_progress()
    {
        var rows = HomeRules.Watching(_playables, [Watch(_moviePlayable, 600, 6000, false, 1), Watch(_s1e2, 300, 0, false, 2)]);

        rows.ContinueWatching.Should().Equal(
            new WatchPick(_show, _s1e2.Id, 300, null),
            new WatchPick(_movie, _moviePlayable.Id, 600, 6000));
        rows.NextUp.Should().BeEmpty();
    }

    [Fact]
    public void Each_title_counts_once_by_its_most_recent_watch_state()
    {
        // _s1e1 is part-way through, but _s1e2 was finished after it: the show is Next Up, not Continue Watching.
        var rows = HomeRules.Watching(_playables, [Watch(_s1e1, 600, 1500, false, 1), Watch(_s1e2, 1450, 1500, true, 2)]);

        rows.ContinueWatching.Should().BeEmpty();
        rows.NextUp.Should().Equal(new WatchPick(_show, _s2e1.Id));
    }

    [Fact]
    public void Next_up_skips_specials_and_an_already_watched_next_episode()
    {
        HomeRules.Watching(_playables, [Watch(_s1e1, 1450, 1500, true, 1)]).NextUp
            .Should().Equal(new WatchPick(_show, _s1e2.Id));

        // A rewatch of _s1e1 after _s1e2 was seen: the first unwatched episode after it.
        HomeRules.Watching(_playables, [Watch(_s1e2, 1450, 1500, true, 1), Watch(_s1e1, 1450, 1500, true, 2)]).NextUp
            .Should().Equal(new WatchPick(_show, _s2e1.Id));

        // After the last episode, the earliest unwatched one; a finished movie isn't Next Up.
        HomeRules.Watching(_playables, [Watch(_s2e1, 1450, 1500, true, 1), Watch(_moviePlayable, 5900, 6000, true, 2)])
            .Should().BeEquivalentTo(new WatchRows([], [new WatchPick(_show, _s1e1.Id)]));

        // Every regular episode watched: nothing is next, whatever the specials.
        HomeRules.Watching(_playables, [Watch(_s1e1, 1450, 1500, true, 1), Watch(_s1e2, 1450, 1500, true, 2), Watch(_s2e1, 1450, 1500, true, 3)])
            .NextUp.Should().BeEmpty();
    }

    // A long show: seasons 1–4 with 3 episodes each, season 5 with 4, and one special.
    private static readonly Guid _long = Guid.CreateVersion7();
    private static readonly HomePlayable _longSpecial = new(Guid.CreateVersion7(), _long, new EpisodeSlot(0, 1));
    private static readonly HomePlayable[] _longEpisodes =
    [
        .. Enumerable.Range(1, 5).SelectMany(season => Enumerable.Range(1, season == 5 ? 4 : 3)
            .Select(number => new HomePlayable(Guid.CreateVersion7(), _long, new EpisodeSlot(season, number)))),
    ];
    private static readonly HomePlayable[] _longShow = [_longSpecial, .. _longEpisodes];

    private static HomePlayable Long(int season, int number) =>
        _longEpisodes.Single(p => p.Slot == new EpisodeSlot(season, number));

    private static IEnumerable<WatchState> MarkSeason(int season, int minutes) =>
        _longEpisodes.Where(p => p.Slot!.Value.SeasonNumber == season).Select(p => Watch(p, 0, 0, true, minutes));

    private static IReadOnlyList<WatchPick> LongNextUp(IEnumerable<WatchState> watches) =>
        HomeRules.Watching(_longShow, [.. watches]).NextUp;

    [Fact]
    public void Next_up_after_seasons_marked_watched_is_the_next_unwatched_episode()
    {
        LongNextUp([.. MarkSeason(1, 1), .. MarkSeason(2, 2), .. MarkSeason(3, 3), .. MarkSeason(4, 4)])
            .Should().Equal(new WatchPick(_long, Long(5, 1).Id));

        LongNextUp([.. MarkSeason(1, 1), .. MarkSeason(2, 2), .. MarkSeason(3, 3), .. MarkSeason(4, 4), Watch(Long(5, 1), 0, 0, true, 5)])
            .Should().Equal(new WatchPick(_long, Long(5, 2).Id));
    }

    [Fact]
    public void Next_up_after_episodes_marked_one_by_one()
    {
        var marked = _longEpisodes.Where(p => p.Slot!.Value.SeasonNumber < 5).Append(Long(5, 1)).ToList();

        LongNextUp(marked.Select((p, i) => Watch(p, 0, 0, true, i)))
            .Should().Equal(new WatchPick(_long, Long(5, 2).Id));
    }

    [Fact]
    public void Next_up_skips_episodes_marked_out_of_order()
    {
        // The reported case: the start of season 5 marked first, then seasons 1–4, so the latest stamp is S4's finale.
        var early = new[] { Long(5, 1), Long(5, 2), Long(5, 3) }.Select((p, i) => Watch(p, 0, 0, true, i));

        LongNextUp([.. early, .. MarkSeason(1, 10), .. MarkSeason(2, 11), .. MarkSeason(3, 12), .. MarkSeason(4, 13)])
            .Should().Equal(new WatchPick(_long, Long(5, 4).Id));

        // With everything after the latest watched one seen, the earliest unwatched episode, not a special.
        LongNextUp([.. MarkSeason(5, 1), .. MarkSeason(2, 2), .. MarkSeason(3, 3), .. MarkSeason(4, 4)])
            .Should().Equal(new WatchPick(_long, Long(1, 1).Id));
    }

    [Fact]
    public void Next_up_crosses_a_season_boundary_when_watched_naturally()
    {
        LongNextUp([Watch(Long(1, 3), 1450, 1500, true, 1)]).Should().Equal(new WatchPick(_long, Long(2, 1).Id));

        // A short start of the next episode doesn't hide it.
        LongNextUp([Watch(Long(1, 3), 1450, 1500, true, 1), Watch(Long(2, 1), 60, 1500, false, 2)])
            .Should().Equal(new WatchPick(_long, Long(2, 1).Id));

        // From a special: the later specials, then the regular seasons.
        LongNextUp([Watch(_longSpecial, 1450, 1500, true, 1)]).Should().Equal(new WatchPick(_long, Long(1, 1).Id));
    }

    [Fact]
    public void Next_up_skips_an_episode_without_a_file()
    {
        var withGap = _longShow.Where(p => p != Long(2, 2)).ToList();

        HomeRules.Watching(withGap, [Watch(Long(2, 1), 1450, 1500, true, 1)]).NextUp
            .Should().Equal(new WatchPick(_long, Long(2, 3).Id));
    }

    [Fact]
    public void A_fully_watched_show_has_no_next_up_and_plays_from_the_start()
    {
        var all = HomeRules.Watching(_longShow, [.. Enumerable.Range(1, 5).SelectMany(s => MarkSeason(s, s))]);

        all.NextUp.Should().BeEmpty();
        HomeRules.HeroPlay(_long, _longShow, all).Should().Be(new WatchPick(_long, Long(1, 1).Id));

        var partly = HomeRules.Watching(_longShow, [.. Enumerable.Range(1, 4).SelectMany(s => MarkSeason(s, s))]);
        HomeRules.HeroPlay(_long, _longShow, partly).Should().Be(new WatchPick(_long, Long(5, 1).Id));
    }

    [Fact]
    public void A_short_start_and_playables_without_a_file_are_ignored()
    {
        var gone = new WatchState { PlayableId = Guid.CreateVersion7(), PositionSeconds = 600, DurationSeconds = 6000, LastWatchedAt = _t0 };

        var rows = HomeRules.Watching(_playables, [Watch(_moviePlayable, 60, 6000, false, 1), gone]);

        rows.Should().BeEquivalentTo(new WatchRows([], []));
    }

    [Fact]
    public void A_watched_title_played_again_is_continue_watching()
    {
        HomeRules.Watching(_playables, [Watch(_moviePlayable, 600, 6000, true, 1)]).ContinueWatching
            .Should().Equal(new WatchPick(_movie, _moviePlayable.Id, 600, 6000));
    }

    [Fact]
    public void Recently_added_is_newest_first_and_cut_to_a_row()
    {
        var titles = Enumerable.Range(0, 25).Select(i => Title($"T{i:00}", i)).ToList();

        var row = HomeRules.RecentlyAdded(titles);

        row.Should().HaveCount(HomeRules.RowSize);
        row[0].Name.Should().Be("T24");
        row[^1].Name.Should().Be("T05");
    }

    [Fact]
    public void Genre_rows_are_the_biggest_genres_with_enough_titles_each_by_rating()
    {
        List<HomeTitle> titles =
        [
            Title("Alien", 0, 8.5, false, "Horror", "Science Fiction"),
            Title("Arrival", 0, 7.9, false, "Science Fiction", "Drama"),
            Title("Moon", 0, null, false, "Science Fiction", "Drama"),
            Title("Heat", 0, 8.3, false, "Drama", "Crime"),
            Title("Halloween", 0, 7.7, false, "Horror"),
        ];

        var rows = HomeRules.Genres(titles);

        rows.Select(r => r.Genre).Should().Equal("Drama", "Science Fiction");
        rows[0].Titles.Select(t => t.Name).Should().Equal("Heat", "Arrival", "Moon");
        rows[1].Titles.Select(t => t.Name).Should().Equal("Alien", "Arrival", "Moon");
    }

    [Fact]
    public void At_most_six_genre_rows()
    {
        string[] genres = ["A", "B", "C", "D", "E", "F", "G"];
        var titles = Enumerable.Range(0, 3).Select(i => Title($"T{i}", 0, null, false, genres)).ToList();

        HomeRules.Genres(titles).Select(r => r.Genre).Should().Equal("A", "B", "C", "D", "E", "F");
    }

    [Fact]
    public void The_hero_rotates_daily_among_the_newest_titles_with_a_backdrop()
    {
        var titles = Enumerable.Range(0, 12).Select(i => Title($"T{i:00}", i, backdrop: true))
            .Append(Title("No backdrop", 99))
            .ToList();
        var newest = titles.Where(t => t.HasBackdrop).OrderByDescending(t => t.AddedAt).Take(HomeRules.HeroCandidates).ToList();

        var picks = Enumerable.Range(0, HomeRules.HeroCandidates).Select(d => HomeRules.Hero(titles, _t0.AddDays(d))).ToList();

        picks.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(newest);
        HomeRules.Hero(titles, _t0.AddHours(11)).Should().Be(picks[0], "the pick holds for the whole UTC day");
        HomeRules.Hero(titles, _t0.AddDays(HomeRules.HeroCandidates)).Should().Be(picks[0]);
        HomeRules.Hero([Title("No backdrop", 0)], _t0).Should().BeNull();
    }

    [Fact]
    public void Hero_play_resumes_then_goes_next_then_starts_at_the_first_regular_episode()
    {
        var resume = HomeRules.Watching(_playables, [Watch(_s1e2, 600, 1500, false, 1)]);
        var next = HomeRules.Watching(_playables, [Watch(_s1e1, 1450, 1500, true, 1)]);
        var none = new WatchRows([], []);
        var showPlayables = _playables.Where(p => p.TitleId == _show).ToList();

        HomeRules.HeroPlay(_show, showPlayables, resume).Should().Be(new WatchPick(_show, _s1e2.Id, 600, 1500));
        HomeRules.HeroPlay(_show, showPlayables, next).Should().Be(new WatchPick(_show, _s1e2.Id));
        HomeRules.HeroPlay(_show, showPlayables, none).Should().Be(new WatchPick(_show, _s1e1.Id));
        HomeRules.HeroPlay(_show, [_special], none).Should().Be(new WatchPick(_show, _special.Id));
        HomeRules.HeroPlay(_movie, [_moviePlayable], none).Should().Be(new WatchPick(_movie, _moviePlayable.Id));
        HomeRules.HeroPlay(_movie, [], none).Should().BeNull();
    }
}
