using MediaMan.Core.Modules;

namespace MediaMan.Metadata.Images;

/// <summary>
/// The real <see cref="IImageDownloader"/>: a plain GET over http or https, capped at <paramref name="maxBytes"/>.
/// A timeout surfaces as <see cref="HttpRequestException"/>, like any other failed fetch. Takes ownership of
/// <paramref name="http"/>.
/// </summary>
public sealed class HttpImageDownloader(HttpClient http, long maxBytes = HttpImageDownloader.DefaultMaxBytes)
    : IImageDownloader, IDisposable
{
    /// <summary>TMDB originals top out at a few MB; anything far bigger isn't artwork.</summary>
    public const long DefaultMaxBytes = 20 * 1024 * 1024;

    public async Task<byte[]> DownloadAsync(Uri url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Artwork is fetched only over http or https.", nameof(url));
        }

        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > maxBytes)
            {
                throw TooLarge();
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw TooLarge();
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("The image request timed out.", ex);
        }
    }

    public void Dispose() => http.Dispose();

    private InvalidDataException TooLarge() => new($"The image is larger than {maxBytes} bytes.");
}
