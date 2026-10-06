using MediaMan.Fakes;

namespace MediaMan.Core.Tests;

public class ClockAndIdsTests
{
    [Fact]
    public void SystemClock_returns_utc()
    {
        new SystemClock().UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Ids_New_is_a_version_7_guid_stamped_from_the_clock()
    {
        var early = Ids.New(new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var late = Ids.New(new FakeClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));

        early.Version.Should().Be(7);
        early.CompareTo(late).Should().BeNegative();
    }
}
