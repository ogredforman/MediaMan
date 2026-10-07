using System.Net;
using System.Text;

namespace MediaMan.Metadata.Tests.Tmdb;

/// <summary>
/// Serves recorded TMDB responses by request path and query, so tests never reach the network (SPEC §10.4).
/// Unknown requests fail the test; queued responses for a request are served before its recorded one.
/// </summary>
internal sealed class StubTmdbHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _recorded = [];
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _queued = [];
    private readonly List<HttpRequestMessage> _requests = [];

    public static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures", "tmdb");

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    /// <summary>Answers <paramref name="request"/> (path and query after <c>/3/</c>) with a fixture file's body.</summary>
    public StubTmdbHandler Record(string request, string fixture)
    {
        _recorded[request] = File.ReadAllText(Path.Combine(FixtureDirectory, fixture + ".json"));
        return this;
    }

    public StubTmdbHandler Enqueue(string request, Func<HttpResponseMessage> response)
    {
        if (!_queued.TryGetValue(request, out var queue))
        {
            _queued[request] = queue = new Queue<Func<HttpResponseMessage>>();
        }

        queue.Enqueue(response);
        return this;
    }

    public int CountOf(string request) => _requests.Count(r => RequestKey(r) == request);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Add(request);
        var key = RequestKey(request);
        if (_queued.TryGetValue(key, out var queue) && queue.Count > 0)
        {
            return Task.FromResult(queue.Dequeue()());
        }

        if (_recorded.TryGetValue(key, out var body))
        {
            return Task.FromResult(Json(HttpStatusCode.OK, body));
        }

        throw new InvalidOperationException($"No recorded TMDB response for '{key}'.");
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string RequestKey(HttpRequestMessage request) =>
        request.RequestUri!.PathAndQuery["/3/".Length..];
}
