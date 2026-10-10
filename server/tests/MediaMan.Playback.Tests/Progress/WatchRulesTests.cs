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
