using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaMan.Server.Tests;

/// <summary>
/// When libraries scan on their own: watcher changes after a quiet period, the periodic full scan, and resuming an
/// interrupted scan (SPEC §7.2 step 8). The scan worker is removed, so a queued scan stays queued for the assertions.
/// </summary>
public sealed class ScanSchedulerTests : IAsyncLifetime
{
    private static readonly string _roots = Path.Combine(Path.GetTempPath(), "mediaman-tests", "scheduler");

    private readonly FakeClock _clock = new();
    private readonly MediaManFactory _factory = new() { ConfigureServices = AddScanningFakes };
    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly string _root = Path.Combine(_roots, "Movies");
    private ScanScheduler _scheduler = null!;
    private ScanCoordinator _coordinator = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _coordinator = _factory.Services.GetRequiredService<ScanCoordinator>();
        _scheduler = new ScanScheduler(_coordinator, _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.FileWatcher, _clock, NullLogger<ScanScheduler>.Instance);
        await AddLibraryAsync(_factory, _libraryId, [_root], intervalMinutes: 360, lastFinishedAt: _clock.UtcNow);
    }

    public async ValueTask DisposeAsync()
    {
        _scheduler.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task A_change_queues_a_scan_once_the_library_has_been_quiet_for_the_debounce()
    {
        await _scheduler.TickAsync(Ct);
        _factory.FileWatcher.WatchedRoots.Should().Equal(Path.GetFullPath(_root));

        Raise("Dune (2021)/Dune.mkv");
        _clock.Advance(TimeSpan.FromSeconds(5));
        Raise("Dune (2021)/Dune.en.srt");
        _clock.Advance(ScanScheduler.Debounce - TimeSpan.FromSeconds(1));
        await _scheduler.TickAsync(Ct);
        State().Should().Be("idle", "the last change is still within the debounce");

        _clock.Advance(TimeSpan.FromSeconds(1));
        await _scheduler.TickAsync(Ct);
        State().Should().Be("queued");
    }

    [Fact]
    public async Task Changes_during_a_running_scan_queue_another_once_it_finishes()
    {
        await _scheduler.TickAsync(Ct);
        _coordinator.Request(_libraryId);
        _coordinator.TryStart(_libraryId).Should().BeTrue();

        Raise("Arrival.mkv");
        _clock.Advance(ScanScheduler.Debounce);
        await _scheduler.TickAsync(Ct);
        State().Should().Be("running");

        _coordinator.Finish(_libraryId, succeeded: true, new ScanCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), error: null);
        await _scheduler.TickAsync(Ct);
        State().Should().Be("queued", "the running scan may have walked past the change");
    }

    [Fact]
    public async Task A_full_scan_is_queued_when_the_interval_has_passed_since_the_last_one()
    {
        _clock.Advance(TimeSpan.FromMinutes(359));
        await _scheduler.TickAsync(Ct);
        State().Should().Be("idle");

        _clock.Advance(TimeSpan.FromMinutes(1));
        await _scheduler.TickAsync(Ct);
        State().Should().Be("queued");
    }

    [Fact]
    public async Task A_library_that_never_finished_a_scan_is_due_at_once_but_one_without_an_interval_never_is()
    {
        var neverScanned = Guid.NewGuid();
        var noInterval = Guid.NewGuid();
        await AddLibraryAsync(_factory, neverScanned, [Path.Combine(_roots, "Shows")], intervalMinutes: 360, lastFinishedAt: null);
        await AddLibraryAsync(_factory, noInterval, [Path.Combine(_roots, "Anime")], intervalMinutes: 0, lastFinishedAt: null);

        _clock.Advance(TimeSpan.FromDays(30));
        await _scheduler.TickAsync(Ct);

        _coordinator.Get(neverScanned).State.Should().Be("queued");
        _coordinator.Get(noInterval).State.Should().Be("idle");
    }

    [Fact]
    public async Task Watchers_follow_libraries_as_their_roots_change_and_they_are_deleted()
    {
        await _scheduler.TickAsync(Ct);
        var moved = Path.Combine(_roots, "Films");
        await UpdateLibraryAsync(l => l.RootPaths = [moved, Path.Combine(_roots, "Extra")]);

        await _scheduler.TickAsync(Ct);
        _factory.FileWatcher.WatchedRoots.Should().BeEquivalentTo([Path.GetFullPath(moved), Path.GetFullPath(Path.Combine(_roots, "Extra"))]);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Libraries.Where(l => l.Id == _libraryId).ExecuteDeleteAsync(Ct);
        }

        await _scheduler.TickAsync(Ct);
        _factory.FileWatcher.WatchedRoots.Should().BeEmpty();
    }

    [Fact]
    public async Task A_scan_interrupted_by_a_shutdown_is_queued_again_at_startup()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));
        var interrupted = Guid.NewGuid();
        var finished = Guid.NewGuid();

        // The restarted host reads the system clock, so both libraries finished a scan just now and neither is due by interval.
        var justNow = DateTimeOffset.UtcNow;
        await using (var first = new MediaManFactory { DataDirectory = dataDirectory, KeepDataDirectory = true })
        {
            await AddLibraryAsync(first, interrupted, [Path.Combine(_roots, "A")], intervalMinutes: 360, lastFinishedAt: justNow, scanStartedAt: justNow);
            await AddLibraryAsync(first, finished, [Path.Combine(_roots, "B")], intervalMinutes: 360, lastFinishedAt: justNow);
        }

        await using var second = new MediaManFactory
        {
            DataDirectory = dataDirectory,
            RunScanScheduler = true,
            ConfigureServices = AddScanningFakes,
        };
        var coordinator = second.Services.GetRequiredService<ScanCoordinator>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (coordinator.Get(interrupted).State != "queued")
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        coordinator.Get(finished).State.Should().Be("idle");
    }

    private static void AddScanningFakes(IServiceCollection s)
    {
        s.AddSingleton<IMetadataProvider>(new FakeMetadataProvider());
        s.AddSingleton<IMediaProbe>(new FakeMediaProbe());
        s.Remove(s.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ScanWorker)));
    }

    private static async Task AddLibraryAsync(
        MediaManFactory factory, Guid id, List<string> roots, int intervalMinutes, DateTimeOffset? lastFinishedAt, DateTimeOffset? scanStartedAt = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        db.Libraries.Add(new Core.Domain.Library
        {
            Id = id,
            Name = "Library " + id,
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = roots,
            ScanIntervalMinutes = intervalMinutes,
            LastScanFinishedAt = lastFinishedAt,
            ScanStartedAt = scanStartedAt,
        });
        await db.SaveChangesAsync(Ct);
    }

    private async Task UpdateLibraryAsync(Action<Core.Domain.Library> change)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        change(await db.Libraries.SingleAsync(l => l.Id == _libraryId, Ct));
        await db.SaveChangesAsync(Ct);
    }

    private void Raise(string relativePath) =>
        _factory.FileWatcher.Raise(new FileChange(FileChangeKind.Created, Path.Combine([_root, .. relativePath.Split('/')])));

    private string State() => _coordinator.Get(_libraryId).State;
}
