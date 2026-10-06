using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaMan.Core.Domain;
using MediaMan.Core.Modules;
using MediaMan.Data;
using MediaMan.Fakes;
using MediaMan.Server.LibraryApi;
using MediaMan.Server.Scanning;
using MediaMan.Server.Tests.Conformance;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

/// <summary>
/// Scanning through the API, end to end on the fakes: trigger a scan, follow its progress over SSE, then browse the
/// matches and the attention list. Every response and event is checked against the contract.
/// </summary>
public sealed class ScanApiTests : IAsyncLifetime
{
    private readonly string _media = Path.Combine(Path.GetTempPath(), "mediaman-tests", "media-" + Guid.NewGuid().ToString("N"));
    private readonly MediaManFactory _factory;
    private readonly Guid _libraryId = Guid.NewGuid();
    private HttpClient _client = null!;

    public ScanApiTests()
    {
        var probe = new FakeMediaProbe().Fail("Broken*");
        _factory = new MediaManFactory
        {
            ConfigureServices = s =>
            {
                s.AddSingleton<IMetadataProvider>(new FakeMetadataProvider());
                s.AddSingleton<IMediaProbe>(probe);
            },
        };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        foreach (var file in new[]
        {
            "Movies/The.Matrix.1999.1080p.mkv",
            "Movies/Dune.mkv",
            "Movies/Broken.File.2010.mkv",
            "Shows/Breaking Bad/Season 1/Breaking.Bad.S01E01-E03.720p.mkv",
        })
        {
            var path = Path.Combine([_media, .. file.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [], Ct);
        }

        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        db.Libraries.Add(new Core.Domain.Library
        {
            Id = _libraryId,
            Name = "Media",
            Kind = LibraryKind.External,
            ContentType = ContentType.Mixed,
            RootPaths = [_media],
        });
        await db.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        if (Directory.Exists(_media))
        {
            Directory.Delete(_media, recursive: true);
        }
    }

    [Fact]
    public async Task A_scan_runs_in_the_background_and_reports_each_phase_until_idle()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var stream = await _client.GetAsync(new Uri("/events", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var events = new SseReader(await stream.Content.ReadAsStreamAsync(timeout.Token));

        using var response = await _client.PostAsync(new Uri($"/libraries/{_libraryId}/scan", UriKind.Relative), null, timeout.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(timeout.Token));
        await ContractDocument.Instance.AssertConformsAsync(response, timeout.Token);
        var accepted = (await response.Content.ReadFromJsonAsync<LibraryDto>(JsonSerializerOptions.Web, timeout.Token))!;
        accepted.Scan.State.Should().BeOneOf("queued", "running");

        var progress = new List<ScanState>();
        while (progress.LastOrDefault()?.State != "idle")
        {
            var frame = await events.ReadFrameAsync(timeout.Token);
            if (frame["event"] != ScanCoordinator.ProgressEventType)
            {
                continue;
            }

            using var envelope = JsonDocument.Parse(frame["data"]);
            var data = envelope.RootElement.GetProperty("data");
            ContractDocument.Instance.Validate(ContractDocument.ComponentSchema("ScanProgressEvent"), data).Should().BeEmpty();
            var update = data.Deserialize<ScanProgressEvent>(JsonSerializerOptions.Web)!;
            update.LibraryId.Should().Be(_libraryId);
            progress.Add(update.Scan);
        }

        progress.Select(s => s.Phase is null ? s.State : $"{s.State}/{s.Phase}").Should().Equal(
            "queued", "running/scanning", "running/matching", "running/importing", "idle");
        var last = progress[^1].LastScan!;
        last.Outcome.Should().Be("succeeded");
        last.Error.Should().BeNull();
        last.Counts.Should().BeEquivalentTo(new { Added = 4, Unreadable = 1, Linked = 2, NeedsAttention = 1, Deferred = 0 });

        var titles = await GetAsync<TitlePage>("/titles");
        titles.Items.Select(t => t.Name).Should().BeEquivalentTo("The Matrix", "Breaking Bad");
        var libraries = await GetAsync<LibraryPage>("/libraries");
        libraries.Items.Should().ContainSingle().Which.Scan.Should().BeEquivalentTo(progress[^1]);
    }

    [Fact]
    public async Task Files_needing_attention_list_their_reason_parsed_guess_and_review_candidates_by_path_across_pages()
    {
        await ScanAndWaitAsync();

        var first = await GetAsync<AttentionFilePage>($"/libraries/{_libraryId}/attention?limit=1");
        var second = await GetAsync<AttentionFilePage>($"/libraries/{_libraryId}/attention?limit=1&cursor={first.PageInfo.NextCursor}");

        var broken = first.Items.Should().ContainSingle().Subject;
        broken.FileName.Should().Be("Broken.File.2010.mkv");
        broken.Path.Should().Be(Path.Combine(_media, "Movies", "Broken.File.2010.mkv"));
        broken.Reason.Should().Be("unreadable");
        broken.Review.Should().BeNull();
        second.PageInfo.NextCursor.Should().BeNull();
        var dune = second.Items.Should().ContainSingle().Subject;
        dune.FileName.Should().Be("Dune.mkv");
        dune.Reason.Should().Be("ambiguous");
        dune.Parsed!.Title.Should().Be("Dune");
        dune.Parsed.Year.Should().BeNull();
        dune.Review!.Reason.Should().Be("ambiguous");
        dune.Review.Candidates.Select(c => (c.TitleRef, c.Kind, c.Name, c.Year)).Should().BeEquivalentTo(
            [("tmdb:438631", "movie", "Dune", 2021), ("tmdb:841", "movie", "Dune", 1984)]);
    }

    [Fact]
    public async Task Scanning_or_listing_an_unknown_library_is_not_found()
    {
        var unknown = Guid.NewGuid();
        using var scan = await _client.PostAsync(new Uri($"/libraries/{unknown}/scan", UriKind.Relative), null, Ct);
        using var attention = await _client.GetAsync(new Uri($"/libraries/{unknown}/attention", UriKind.Relative), Ct);

        foreach (var response in new[] { scan, attention })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            await ContractDocument.Instance.AssertConformsAsync(response, Ct);
            (await ProblemCodeAsync(response)).Should().Be("library.not_found");
        }
    }

    [Fact]
    public async Task Scanning_without_a_metadata_provider_or_probe_is_unavailable()
    {
        await using var factory = new MediaManFactory();
        using var client = factory.CreateClient();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
            db.Libraries.Add(new Core.Domain.Library { Id = _libraryId, Name = "Media", RootPaths = [_media] });
            await db.SaveChangesAsync(Ct);
        }

        using var response = await client.PostAsync(new Uri($"/libraries/{_libraryId}/scan", UriKind.Relative), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        (await ProblemCodeAsync(response)).Should().Be("scan.unavailable");
        factory.Services.GetRequiredService<ScanCoordinator>().Get(_libraryId).State.Should().Be("idle");
    }

    private async Task ScanAndWaitAsync()
    {
        using var response = await _client.PostAsync(new Uri($"/libraries/{_libraryId}/scan", UriKind.Relative), null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var coordinator = _factory.Services.GetRequiredService<ScanCoordinator>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (coordinator.Get(_libraryId).LastScan is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        coordinator.Get(_libraryId).LastScan!.Outcome.Should().Be("succeeded");
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await _client.GetAsync(new Uri(url, UriKind.Relative), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonSerializerOptions.Web, Ct))!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("code").GetString();
    }

    /// <summary>Reads SSE frames (field → value) from one open stream, skipping comment lines.</summary>
    private sealed class SseReader(Stream stream) : IDisposable
    {
        private readonly StreamReader _reader = new(stream);

        public async Task<Dictionary<string, string>> ReadFrameAsync(CancellationToken ct)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            while (await _reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length == 0 && fields.Count > 0)
                {
                    return fields;
                }

                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    fields[line[..colon]] = line[(colon + 1)..].TrimStart(' ');
                }
            }

            throw new EndOfStreamException("The event stream closed.");
        }

        public void Dispose() => _reader.Dispose();
    }
}
