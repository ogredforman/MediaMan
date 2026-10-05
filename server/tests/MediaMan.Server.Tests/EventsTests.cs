using System.Net;
using System.Text.Json;
using MediaMan.Core;
using MediaMan.Server.Events;
using MediaMan.Server.Tests.Conformance;
using Microsoft.Extensions.DependencyInjection;

namespace MediaMan.Server.Tests;

public sealed class EventsTests
{
    [Fact]
    public async Task Published_event_arrives_as_an_sse_frame_whose_data_matches_the_contract()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new MediaManFactory();
        using var client = factory.CreateClient();
        var broadcaster = factory.Services.GetRequiredService<EventBroadcaster>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        // Headers arrive before any event is published, so idle clients see the stream open.
        using var response = await client.GetAsync(new Uri("/events", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        broadcaster.SubscriberCount.Should().Be(1);
        var published = broadcaster.Publish("test.ping", new { message = "hello" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        var frame = await ReadFrameAsync(response, timeout.Token);

        frame["event"].Should().Be("test.ping");
        frame["id"].Should().Be(published.Id);
        var contract = ContractDocument.Instance;
        var schema = contract.ResponseSchema(contract.Find("GET", "/events")!, 200, "text/event-stream");
        using var data = JsonDocument.Parse(frame["data"]);
        contract.Validate(schema, data.RootElement).Should().BeEmpty();
        data.RootElement.GetProperty("data").GetProperty("message").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task Subscriber_is_removed_when_its_stream_is_cancelled()
    {
        var broadcaster = new EventBroadcaster(new SystemClock());
        using var cts = new CancellationTokenSource();
        var events = broadcaster.Subscribe(cts.Token);
        broadcaster.Publish("test.ping", new { });

        await using (var enumerator = events.GetAsyncEnumerator(cts.Token))
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
            broadcaster.SubscriberCount.Should().Be(1);
            await cts.CancelAsync();
            var next = async () => await enumerator.MoveNextAsync();
            await next.Should().ThrowAsync<OperationCanceledException>();
        }

        broadcaster.SubscriberCount.Should().Be(0);
    }

    private static async Task<Dictionary<string, string>> ReadFrameAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0 && fields.Count > 0)
            {
                break;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                fields[line[..colon]] = line[(colon + 1)..].TrimStart(' ');
            }
        }

        return fields;
    }
}
