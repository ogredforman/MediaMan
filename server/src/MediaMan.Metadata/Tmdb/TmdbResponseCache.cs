using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaMan.Core;

namespace MediaMan.Metadata.Tmdb;

/// <summary>
/// Successful TMDB responses on disk, one file per request named by a hash of the request path and query. The access
/// token travels in a header, so it is never part of a file name or its contents. Age is read through <see cref="IClock"/>.
/// </summary>
internal sealed class TmdbResponseCache(string directory, TimeSpan lifetime, IClock clock)
{
    public async Task<string?> TryReadAsync(string request, CancellationToken ct)
    {
        var path = PathFor(request);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<Entry>(stream, cancellationToken: ct);
            return entry is not null && entry.Request == request && clock.UtcNow - entry.FetchedAt < lifetime
                ? entry.Body
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // A damaged or locked entry is a miss; the next successful fetch rewrites it.
        }
    }

    public async Task WriteAsync(string request, string body, CancellationToken ct)
    {
        var path = PathFor(request);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, new Entry(request, clock.UtcNow, body), cancellationToken: ct);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The cache only saves calls; a failed write must not fail the lookup that already succeeded.
            File.Delete(temp);
        }
    }

    private string PathFor(string request) =>
        Path.Combine(directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request))) + ".json");

    private sealed record Entry(string Request, DateTimeOffset FetchedAt, string Body);
}
