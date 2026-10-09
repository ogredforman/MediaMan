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

/// <summary>"Needs attention" and "Fix match" through the API on the fakes (SPEC §7.3), checked against the contract.</summary>
public sealed class MatchApiTests : IAsyncLifetime
{
    private readonly string _media = Path.Combine(Path.GetTempPath(), "mediaman-tests", "media-" + Guid.NewGuid().ToString("N"));
    private readonly FakeMetadataProvider _provider = new();
    private readonly MediaManFactory _factory;
    private readonly Guid _libraryId = Guid.NewGuid();
    private HttpClient _client = null!;

    public MatchApiTests()
    {
        var probe = new FakeMediaProbe().Fail("Broken*");
        _factory = new MediaManFactory
        {
            ConfigureServices = s =>
            {
                s.AddSingleton<IMetadataProvider>(_provider);
                s.AddSingleton<IMediaProbe>(probe);
            },
        };
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string DunePath => Path.Combine(_media, "Movies", "Dune.mkv");

    public async ValueTask InitializeAsync()
    {
        foreach (var file in new[] { "Movies/Dune.mkv", "Movies/Broken.File.2010.mkv", "Shows/The Office/Season 1/The.Office.S01E02.mkv" })
        {
            var path = Path.Combine([_media, .. file.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [], Ct);
        }

        _client = await _factory.CreateSignedInClientAsync(Ct);
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
        await ScanAndWaitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        if (Directory.Exists(_media))
        {
            ScanApiTests.SetReadOnly(_media, readOnly: false);
            Directory.Delete(_media, recursive: true);
        }
    }

    [Fact]
    public async Task Fixing_a_file_from_needs_attention_links_it_and_a_rescan_of_the_changed_file_keeps_the_choice()
    {
        var dune = (await AttentionAsync()).Single(f => f.FileName == "Dune.mkv");
        var pick = dune.Review!.Candidates.Single(c => c.Year == 1984);

        using var response = await PutMatchAsync(dune.MediaFileId, new { titleRef = pick.TitleRef });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        var match = (await response.Content.ReadFromJsonAsync<MediaFileMatch>(JsonSerializerOptions.Web, Ct))!;
        match.Status.Should().Be("matched");
        (await AttentionAsync()).Select(f => f.FileName).Should().NotContain("Dune.mkv");
        (await GetAsync<TitleDetail>($"/titles/{match.TitleId}")).Year.Should().Be(1984);

        // The file changes on disk: the next scan re-probes it and the matcher follows the override, not a search.
        await File.WriteAllBytesAsync(DunePath, [1, 2, 3], Ct);
        await ScanAndWaitAsync();

        (await AttentionAsync()).Select(f => f.FileName).Should().NotContain("Dune.mkv");
        var titles = await GetAsync<TitlePage>("/titles?kind=movie");
        titles.Items.Should().ContainSingle(t => t.Name == "Dune").Which.Id.Should().Be(match.TitleId);
    }

    [Fact]
    public async Task A_show_is_fixed_with_its_season_and_episode_mapping()
    {
        var office = (await AttentionAsync()).Single(f => f.FileName == "The.Office.S01E02.mkv");

        using var response = await PutMatchAsync(office.MediaFileId, new { titleRef = "tmdb:tv/2996", season = 1, episodes = (int[])[3] });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var match = (await response.Content.ReadFromJsonAsync<MediaFileMatch>(JsonSerializerOptions.Web, Ct))!;
        var detail = await GetAsync<TitleDetail>($"/titles/{match.TitleId}");
        detail.Year.Should().Be(2001);
        detail.Seasons!.Single(s => s.Number == 1).Episodes.Single(e => e.Number == 3).Versions.Should().ContainSingle()
            .Which.MediaFileId.Should().Be(office.MediaFileId);
    }

    [Fact]
    public async Task Relink_errors_say_what_happened_and_conform()
    {
        var files = await AttentionAsync();
        var dune = files.Single(f => f.FileName == "Dune.mkv").MediaFileId;
        var office = files.Single(f => f.FileName == "The.Office.S01E02.mkv").MediaFileId;
        var broken = files.Single(f => f.FileName == "Broken.File.2010.mkv").MediaFileId;

        await AssertProblemAsync(await PutMatchAsync(Guid.NewGuid(), new { titleRef = "tmdb:movie/841" }), HttpStatusCode.NotFound, "media_file.not_found");
        await AssertProblemAsync(await PutMatchAsync(broken, new { titleRef = "tmdb:movie/841" }), HttpStatusCode.Conflict, "match.file_unavailable");
        await AssertProblemAsync(await PutMatchAsync(office, new { titleRef = "tmdb:tv/2996", season = 1, episodes = (int[])[99] }),
            HttpStatusCode.UnprocessableEntity, "match.episode_not_found");

        var invalid = await AssertProblemAsync(await PutMatchAsync(dune, new { titleRef = "tmdb:movie/1" }), HttpStatusCode.BadRequest, "match.invalid");
        invalid.GetProperty("errors").TryGetProperty("titleRef", out _).Should().BeTrue();

        _provider.FailNext(FakeProviderError.Timeout, calls: 10);
        await AssertProblemAsync(await PutMatchAsync(dune, new { titleRef = "tmdb:movie/438631" }), HttpStatusCode.ServiceUnavailable, "metadata.unavailable");
        (await AttentionAsync()).Should().HaveCount(3, "a refused relink changes nothing");
    }

    [Fact]
    public async Task Catalog_search_returns_candidates_and_explains_bad_queries_and_provider_failures()
    {
        var result = await GetAsync<CatalogSearchResult>("/catalog/search?query=Dune&kind=movie&year=1984");

        result.Items.Select(c => c.TitleRef).Should().BeEquivalentTo("tmdb:movie/841", "tmdb:movie/438631");
        result.Items.Should().OnlyContain(c => c.Kind == "movie" && c.Name == "Dune");

        await AssertProblemAsync(await _client.GetAsync(new Uri("/catalog/search?query=%20", UriKind.Relative), Ct), HttpStatusCode.BadRequest, "catalog.invalid");
        await AssertProblemAsync(await _client.GetAsync(new Uri("/catalog/search?query=Dune&kind=film", UriKind.Relative), Ct), HttpStatusCode.BadRequest, "catalog.invalid");
        _provider.FailNext(FakeProviderError.RateLimited);
        await AssertProblemAsync(await _client.GetAsync(new Uri("/catalog/search?query=Dune", UriKind.Relative), Ct), HttpStatusCode.ServiceUnavailable, "metadata.unavailable");
    }

    [Fact]
    public async Task Fixing_a_match_in_a_read_only_external_library_leaves_every_file_and_folder_as_it_was()
    {
        var dune = (await AttentionAsync()).Single(f => f.FileName == "Dune.mkv");
        ScanApiTests.SetReadOnly(_media, readOnly: true);
        var before = ScanApiTests.Snapshot(_media);

        using var response = await PutMatchAsync(dune.MediaFileId, new { titleRef = "tmdb:movie/438631" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        ScanApiTests.Snapshot(_media).Should().Equal(before);
    }

    private async Task<List<AttentionFile>> AttentionAsync() =>
        [.. (await GetAsync<AttentionFilePage>($"/libraries/{_libraryId}/attention")).Items];

    private Task<HttpResponseMessage> PutMatchAsync(Guid mediaFileId, object body) =>
        _client.PutAsJsonAsync(new Uri($"/media-files/{mediaFileId}/match", UriKind.Relative), body, Ct);

    private async Task ScanAndWaitAsync()
    {
        var coordinator = _factory.Services.GetRequiredService<ScanCoordinator>();
        var previous = coordinator.Get(_libraryId).LastScan?.FinishedAt;
        using var response = await _client.PostAsync(new Uri($"/libraries/{_libraryId}/scan", UriKind.Relative), null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (coordinator.Get(_libraryId) is var state && (state.State != "idle" || state.LastScan?.FinishedAt == previous))
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

    /// <summary>Asserts the problem and returns its body.</summary>
    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync(Ct));
            await ContractDocument.Instance.AssertConformsAsync(response, Ct);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            body.RootElement.GetProperty("code").GetString().Should().Be(code);
            body.RootElement.TryGetProperty("action", out _).Should().BeTrue("every error offers the next action");
            return body.RootElement.Clone();
        }
    }
}
