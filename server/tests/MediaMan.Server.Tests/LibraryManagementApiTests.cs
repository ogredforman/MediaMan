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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MediaMan.Server.Tests;

/// <summary>
/// Library management (SPEC §8 libraries): create, read, update and delete External libraries of several roots. Roots
/// are validated and never overlap; removing a root or a library deletes rows only, never files (SPEC §5).
/// </summary>
public sealed class LibraryManagementApiTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mediaman-tests", "libraries-" + Guid.NewGuid().ToString("N"));
    private readonly MediaManFactory _factory = new()
    {
        // Scanning is available, but no worker takes the queue, so scan states stay exactly as the API leaves them.
        ConfigureServices = s =>
        {
            s.AddSingleton<IMetadataProvider>(new FakeMetadataProvider());
            s.AddSingleton<IMediaProbe>(new FakeMediaProbe());
            s.Remove(s.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ScanWorker)));
        },
    };

    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Movies => Path.Combine(_root, "Movies");

    private string MoreMovies => Path.Combine(_root, "More Movies");

    private string Shows => Path.Combine(_root, "Shows");

    public async ValueTask InitializeAsync()
    {
        foreach (var file in new[] { "Movies/Alien (1979)/Alien.1979.mkv", "More Movies/Heat.1995.mkv", "Shows/Lost/Lost.S01E01.mkv" })
        {
            var path = Path.Combine([_root, .. file.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "placeholder", Ct);
        }

        _client = await _factory.CreateSignedInClientAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Creating_adds_an_External_library_of_several_roots_and_queues_its_scan()
    {
        var before = Snapshot();

        using var response = await _client.PostAsJsonAsync("/libraries",
            new { contentType = "movies", rootPaths = new[] { Movies + Path.DirectorySeparatorChar, MoreMovies } }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = await JsonOf(response);
        var library = body.RootElement;
        var id = library.GetProperty("id").GetString();
        response.Headers.Location!.ToString().Should().Be($"/libraries/{id}");
        library.GetProperty("name").GetString().Should().Be("Movies", "the name defaults to the first root's folder");
        library.GetProperty("kind").GetString().Should().Be("external");
        library.GetProperty("contentType").GetString().Should().Be("movies");
        library.GetProperty("rootPaths").EnumerateArray().Select(p => p.GetString()).Should().Equal(Movies, MoreMovies);
        library.GetProperty("scanIntervalMinutes").GetInt32().Should().Be(360);
        library.GetProperty("scan").GetProperty("state").GetString().Should().Be("queued");

        using var fetched = await _client.GetAsync(new Uri($"/libraries/{id}", UriKind.Relative), Ct);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(fetched, Ct);
        using var named = await CreateAsync(new { name = " TV ", contentType = "shows", rootPaths = new[] { Shows }, scanIntervalMinutes = 0 });
        named.RootElement.GetProperty("name").GetString().Should().Be("TV");
        named.RootElement.GetProperty("scanIntervalMinutes").GetInt32().Should().Be(0);
        Snapshot().Should().Equal(before, "adding an External library only reads it (SPEC §5)");
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_by_name_and_nothing_is_created()
    {
        using var response = await _client.PostAsJsonAsync("/libraries", new
        {
            name = new string('n', 101),
            contentType = "music",
            rootPaths = new[] { Path.Combine("relative", "Movies"), Path.Combine(_root, "Nowhere"), Movies },
            scanIntervalMinutes = 5,
        }, Ct);
        using var noRoots = await _client.PostAsJsonAsync("/libraries", new { contentType = "movies", rootPaths = Array.Empty<string>() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = await JsonOf(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("library.invalid");
        body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("name", "contentType", "rootPaths[0]", "rootPaths[1]", "scanIntervalMinutes");
        (await ErrorsOfAsync(noRoots)).Should().Equal("rootPaths");
        (await LibraryCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Overlapping_roots_and_a_root_holding_the_data_directory_are_refused()
    {
        using var existing = await CreateAsync(new { name = "Films", contentType = "movies", rootPaths = new[] { Movies } });

        using var twice = await _client.PostAsJsonAsync("/libraries", new { contentType = "shows", rootPaths = new[] { Shows, Shows } }, Ct);
        using var nested = await _client.PostAsJsonAsync("/libraries",
            new { contentType = "shows", rootPaths = new[] { Shows, Path.Combine(Shows, "Lost") } }, Ct);
        using var inside = await _client.PostAsJsonAsync("/libraries",
            new { contentType = "movies", rootPaths = new[] { Path.Combine(Movies, "Alien (1979)") } }, Ct);
        using var around = await _client.PostAsJsonAsync("/libraries", new { contentType = "mixed", rootPaths = new[] { _root } }, Ct);
        using var data = await _client.PostAsJsonAsync("/libraries",
            new { contentType = "mixed", rootPaths = new[] { Path.GetDirectoryName(_factory.DataDirectory) } }, Ct);

        (await ErrorsOfAsync(twice)).Should().Equal("rootPaths[1]");
        (await ErrorsOfAsync(nested)).Should().Equal("rootPaths[1]");
        (await MessageOfAsync(inside, "rootPaths[0]")).Should().Contain("'Films'");
        (await ErrorsOfAsync(around)).Should().Equal("rootPaths[0]");
        (await MessageOfAsync(data, "rootPaths[0]")).Should().Contain("data folder");
        (await LibraryCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Updating_changes_the_fields_given_and_a_removed_root_takes_its_files_along_without_touching_disk()
    {
        using var created = await CreateAsync(new { contentType = "movies", rootPaths = new[] { Movies, MoreMovies } });
        var id = Guid.Parse(created.RootElement.GetProperty("id").GetString()!);
        await AddFileAsync(id, Path.Combine(Movies, "Alien (1979)", "Alien.1979.mkv"));
        var heat = await AddFileAsync(id, Path.Combine(MoreMovies, "Heat.1995.mkv"));
        var before = Snapshot();
        Scans.TryStart(id).Should().BeTrue();
        Scans.Finish(id, succeeded: true, new ScanCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), error: null);

        using var renamed = await _client.PatchAsJsonAsync($"/libraries/{id}", new { name = "Films", scanIntervalMinutes = 60 }, Ct);
        using var response = await _client.PatchAsJsonAsync($"/libraries/{id}",
            new { contentType = "mixed", rootPaths = new[] { MoreMovies, Shows } }, Ct);

        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = await JsonOf(response);
        var library = body.RootElement;
        library.GetProperty("name").GetString().Should().Be("Films");
        library.GetProperty("contentType").GetString().Should().Be("mixed");
        library.GetProperty("scanIntervalMinutes").GetInt32().Should().Be(60);
        library.GetProperty("rootPaths").EnumerateArray().Select(p => p.GetString()).Should().Equal(MoreMovies, Shows);
        library.GetProperty("scan").GetProperty("state").GetString().Should().Be("queued", "an added root is scanned");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        (await db.MediaFiles.Select(f => f.Id).ToListAsync(Ct)).Should().Equal(heat);
        (await db.MatchReviews.Select(r => r.MediaFileId).ToListAsync(Ct)).Should().Equal(heat);
        Snapshot().Should().Equal(before, "removing a root deletes rows, never files (SPEC §5)");
    }

    [Fact]
    public async Task An_invalid_update_or_an_unknown_library_changes_nothing()
    {
        using var created = await CreateAsync(new { name = "Films", contentType = "movies", rootPaths = new[] { Movies } });
        var id = created.RootElement.GetProperty("id").GetString();
        using var other = await CreateAsync(new { contentType = "shows", rootPaths = new[] { Shows } });

        using var invalid = await _client.PatchAsJsonAsync($"/libraries/{id}",
            new { name = " ", contentType = "music", rootPaths = new[] { Path.Combine(Shows, "Lost") } }, Ct);
        using var unknown = await _client.PatchAsJsonAsync($"/libraries/{Guid.NewGuid()}", new { name = "Gone" }, Ct);
        using var unknownGet = await _client.GetAsync(new Uri($"/libraries/{Guid.NewGuid()}", UriKind.Relative), Ct);

        (await ErrorsOfAsync(invalid)).Should().BeEquivalentTo("name", "contentType", "rootPaths[0]");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ContractDocument.Instance.AssertConformsAsync(unknown, Ct);
        unknownGet.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var after = await JsonOf(await _client.GetAsync(new Uri($"/libraries/{id}", UriKind.Relative), Ct));
        after.RootElement.GetProperty("name").GetString().Should().Be("Films");
        after.RootElement.GetProperty("rootPaths").EnumerateArray().Select(p => p.GetString()).Should().Equal(Movies);
    }

    [Fact]
    public async Task Roots_can_not_change_and_the_library_can_not_be_deleted_while_its_scan_runs()
    {
        using var created = await CreateAsync(new { contentType = "movies", rootPaths = new[] { Movies } });
        var id = Guid.Parse(created.RootElement.GetProperty("id").GetString()!);
        Scans.TryStart(id).Should().BeTrue();

        using var roots = await _client.PatchAsJsonAsync($"/libraries/{id}", new { rootPaths = new[] { Movies, MoreMovies } }, Ct);
        using var rename = await _client.PatchAsJsonAsync($"/libraries/{id}", new { name = "Films", rootPaths = new[] { Movies } }, Ct);
        using var delete = await _client.DeleteAsync(new Uri($"/libraries/{id}", UriKind.Relative), Ct);

        roots.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await ContractDocument.Instance.AssertConformsAsync(roots, Ct);
        (await CodeOfAsync(roots)).Should().Be("library.scan_running");
        rename.StatusCode.Should().Be(HttpStatusCode.OK, "the same roots and a new name don't disturb the scan");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await ContractDocument.Instance.AssertConformsAsync(delete, Ct);
        (await LibraryCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_removes_the_library_and_its_files_from_MediaMan_only_and_drops_its_queued_scan()
    {
        using var created = await CreateAsync(new { contentType = "movies", rootPaths = new[] { Movies } });
        var id = Guid.Parse(created.RootElement.GetProperty("id").GetString()!);
        await AddFileAsync(id, Path.Combine(Movies, "Alien (1979)", "Alien.1979.mkv"));
        var before = Snapshot();

        using var response = await _client.DeleteAsync(new Uri($"/libraries/{id}", UriKind.Relative), Ct);
        using var again = await _client.DeleteAsync(new Uri($"/libraries/{id}", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ContractDocument.Instance.AssertConformsAsync(again, Ct);
        Scans.TryStart(id).Should().BeFalse("the queued scan was dropped");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        (await db.Libraries.AnyAsync(Ct)).Should().BeFalse();
        (await db.MediaFiles.AnyAsync(Ct)).Should().BeFalse();
        (await db.MatchReviews.AnyAsync(Ct)).Should().BeFalse();
        Snapshot().Should().Equal(before, "deleting a library never touches its folders (SPEC §5)");
    }

    [Fact]
    public async Task Setup_refuses_a_first_library_holding_the_data_directory()
    {
        await using var factory = new MediaManFactory { CompleteSetup = false };
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/setup", new
        {
            username = "owner",
            password = "a long setup password",
            mode = "local",
            library = new { path = Path.GetDirectoryName(factory.DataDirectory), contentType = "mixed" },
        }, Ct);

        (await MessageOfAsync(response, "library.path")).Should().Contain("data folder");
    }

    private ScanCoordinator Scans => _factory.Services.GetRequiredService<ScanCoordinator>();

    private async Task<JsonDocument> CreateAsync(object request)
    {
        using var response = await _client.PostAsJsonAsync("/libraries", request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await JsonOf(response);
    }

    /// <summary>A stored file with a MatchReview, so removing it shows the cascade.</summary>
    private async Task<Guid> AddFileAsync(Guid libraryId, string path)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaManDbContext>();
        var file = new MediaFile { Id = Guid.NewGuid(), LibraryId = libraryId, Path = path, Status = MediaFileStatus.NeedsAttention };
        db.MediaFiles.Add(file);
        db.MatchReviews.Add(new MatchReview { Id = Guid.NewGuid(), MediaFileId = file.Id, Reason = "no-match" });
        await db.SaveChangesAsync(Ct);
        return file.Id;
    }

    private async Task<int> LibraryCountAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediaManDbContext>().Libraries.CountAsync(Ct);
    }

    /// <summary>
    /// Every folder under the media root, and every file with its size and write time. Folder times are left out: NTFS
    /// can settle them after the setup writes, and any change inside a folder shows in its entries anyway.
    /// </summary>
    private List<string> Snapshot() =>
        [.. new DirectoryInfo(_root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
            .Select(i => i is FileInfo file ? $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}" : i.FullName)
            .Order(StringComparer.Ordinal)];

    private static async Task<JsonDocument> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = await JsonOf(response);
        return body.RootElement.GetProperty("code").GetString();
    }

    private static async Task<List<string>> ErrorsOfAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ContractDocument.Instance.AssertConformsAsync(response, Ct);
        using var body = await JsonOf(response);
        return [.. body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name)];
    }

    private static async Task<string?> MessageOfAsync(HttpResponseMessage response, string field)
    {
        (await ErrorsOfAsync(response)).Should().Equal(field);
        using var body = await JsonOf(response);
        return body.RootElement.GetProperty("errors").GetProperty(field)[0].GetString();
    }
}
