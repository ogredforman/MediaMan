using MediaMan.Playback.Progress;

namespace MediaMan.Playback.Tests.Progress;

public class WatchRulesTests
{
    [Theory]
    [InlineData(0, 6000, null)]
    [InlineData(119.9, 6000, null)]
    [InlineData(120, 6000, 120.0)]
    [InlineData(3000, 6000, 3000.0)]
    [InlineData(5399.9, 6000, 5399.9)]
    [InlineData(5400, 6000, null)]
    [InlineData(6000, 6000, null)]
    [InlineData(600, 0, 600.0)]
    public void Resume_is_offered_after_two_minutes_and_before_ninety_percent(double position, double duration, double? expected)
    {
        WatchRules.ResumeAt(position, duration).Should().Be(expected);
    }
}

public class WatchRulesRecordTests
{
    [Theory]
    [InlineData(5399, 6000, false)]
    [InlineData(5400, 6000, true)]
    [InlineData(6000, 6000, true)]
    [InlineData(99999, 0, false)]
    public void Ninety_percent_of_a_known_duration_marks_it_watched(double position, double duration, bool watched)
    {
        WatchRules.Record(null, position, duration).Watched.Should().Be(watched);
    }

    [Fact]
    public void Watched_stays_set_when_a_later_report_goes_back()
    {
        WatchRules.Record(new RecordedProgress(5800, 6000, true), 30, 6000).Should().Be(new RecordedProgress(30, 6000, true));
    }

    [Fact]
    public void A_position_past_the_end_counts_as_the_end()
    {
        WatchRules.Record(null, 7000, 6000).Should().Be(new RecordedProgress(6000, 6000, true));
        WatchRules.Record(null, 7000, 0).PositionSeconds.Should().Be(7000);
    }

    [Fact]
    public void An_unknown_duration_keeps_the_one_known_from_earlier()
    {
        WatchRules.Record(new RecordedProgress(100, 6000, false), 5500, 0).Should().Be(new RecordedProgress(5500, 6000, true));
    }

    [Fact]
    public void Negative_numbers_are_refused()
    {
        FluentActions.Invoking(() => WatchRules.Record(null, -1, 6000)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => WatchRules.Record(null, 1, -1)).Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class WatchRulesNextEpisodeTests
{
    private static readonly EpisodeSlot[] _show =
    [
        new(0, 1), new(0, 2),
        new(1, 1), new(1, 2), new(1, 4),
        new(3, 2), new(3, 1),
    ];

    [Theory]
    [InlineData(1, 1, 1, 2)]
    [InlineData(1, 2, 1, 4)] // E03 has no file, so it's skipped.
    [InlineData(1, 4, 3, 1)] // Season 2 has no files; the next season's first episode follows.
    [InlineData(0, 1, 0, 2)]
    [InlineData(0, 2, 1, 1)] // After the last special comes season 1.
    [InlineData(1, 3, 1, 4)] // The current episode needn't have a file itself.
    public void The_next_number_in_the_season_else_the_next_regular_season(int season, int episode, int nextSeason, int nextEpisode)
    {
        WatchRules.NextEpisode(new EpisodeSlot(season, episode), _show).Should().Be(new EpisodeSlot(nextSeason, nextEpisode));
    }

    [Fact]
    public void The_last_episode_has_no_next()
    {
        WatchRules.NextEpisode(new EpisodeSlot(3, 2), _show).Should().BeNull();
    }

    [Fact]
    public void Specials_never_follow_a_regular_season()
    {
        WatchRules.NextEpisode(new EpisodeSlot(1, 2), [new(0, 5), new(1, 1)]).Should().BeNull();
    }
}
