using MediaMan.Core.Modules;
using MediaMan.Fakes;
using MediaMan.Server.Events;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

public sealed class ScanCoordinatorTests
{
    private readonly FakeClock _clock = new();
    private readonly Guid _library = Guid.NewGuid();

    [Fact]
    public void A_scan_requested_while_one_is_queued_or_running_is_not_queued_again()
    {
        var coordinator = Create(available: true);

        var queued = coordinator.Request(_library);
        var again = coordinator.Request(_library);
        coordinator.Start(_library);
        var whileRunning = coordinator.Request(_library);

        queued.State.Should().Be("queued");
        again.Should().Be(queued);
        whileRunning.State.Should().Be("running");
        coordinator.Queue.TryRead(out var id).Should().BeTrue();
        id.Should().Be(_library);
        coordinator.Queue.TryRead(out _).Should().BeFalse("only one scan was queued");
    }

    [Fact]
    public void A_finished_scan_returns_to_idle_with_its_summary_and_keeps_it_for_the_next_request()
    {
        var coordinator = Create(available: true);
        var started = _clock.UtcNow;
        coordinator.Request(_library);
        coordinator.Start(_library);
        coordinator.Enter(_library, ScanPhase.Matching);
        coordinator.Get(_library).Should().BeEquivalentTo(new { State = "running", Phase = "matching", StartedAt = started });
        _clock.Advance(TimeSpan.FromMinutes(2));

        var counts = new ScanCounts(1, 0, 0, 0, 0, 0, 0, 1, 0, 0);
        coordinator.Finish(_library, succeeded: true, counts, error: null);
        var next = coordinator.Request(_library);

        var idle = new ScanSummary(started, started.AddMinutes(2), "succeeded", counts, null);
        next.Should().Be(new ScanState("queued", null, null, idle));
    }

    [Fact]
    public void Scanning_is_available_only_with_a_metadata_provider_and_a_media_probe()
    {
        Create(available: true).IsAvailable.Should().BeTrue();
        Create(available: false).IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void An_unknown_library_is_idle() =>
        Create(available: true).Get(Guid.NewGuid()).Should().Be(new ScanState("idle", null, null, null));

    private ScanCoordinator Create(bool available)
    {
        var services = new ServiceCollection();
        if (available)
        {
            services.AddSingleton<IMetadataProvider>(new FakeMetadataProvider());
            services.AddSingleton<IMediaProbe>(new FakeMediaProbe());
        }

        var provider = services.BuildServiceProvider();
        return new ScanCoordinator(new EventBroadcaster(_clock), _clock, provider.GetRequiredService<IServiceProviderIsService>());
    }
}
