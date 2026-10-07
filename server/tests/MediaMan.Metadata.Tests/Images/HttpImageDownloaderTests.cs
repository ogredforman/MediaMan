using System.Net;
using MediaMan.Metadata.Images;

namespace MediaMan.Metadata.Tests.Images;

/// <summary>The real downloader over a stub handler; nothing touches the network.</summary>
public sealed class HttpImageDownloaderTests
{
    private static readonly Uri _url = new("https://image.tmdb.org/t/p/original/poster.jpg");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_image_bytes()
    {
        byte[] image = [0xFF, 0xD8, 0xFF, 1, 2, 3];
        using var downloader = Create(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) });

        (await downloader.DownloadAsync(_url, Ct)).Should().Equal(image);
    }

    [Fact]
    public async Task A_failed_status_throws_an_http_error()
    {
        using var downloader = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await downloader.Invoking(d => d.DownloadAsync(_url, Ct)).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task A_timeout_throws_an_http_error()
    {
        using var downloader = Create(_ => throw new TaskCanceledException("timed out"));

        await downloader.Invoking(d => d.DownloadAsync(_url, Ct)).Should().ThrowAsync<HttpRequestException>().WithMessage("*timed out*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_image_over_the_limit_is_rejected_whether_or_not_its_length_is_declared(bool declared)
    {
        using var downloader = Create(
            _ =>
            {
                HttpContent content = declared ? new ByteArrayContent(new byte[11]) : new StreamContent(new UnknownLengthStream(new byte[11]));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            },
            maxBytes: 10);

        await downloader.Invoking(d => d.DownloadAsync(_url, Ct)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Only_http_and_https_are_fetched()
    {
        var requests = 0;
        using var downloader = Create(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await downloader.Invoking(d => d.DownloadAsync(new Uri("file:///etc/passwd"), Ct)).Should().ThrowAsync<ArgumentException>();
        requests.Should().Be(0);
    }

    private static HttpImageDownloader Create(Func<HttpRequestMessage, HttpResponseMessage> respond, long maxBytes = HttpImageDownloader.DefaultMaxBytes) =>
        new(new HttpClient(new StubHandler(respond)), maxBytes);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    /// <summary>A body with no Content-Length, so the limit is enforced while reading.</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
