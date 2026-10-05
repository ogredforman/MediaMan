using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace MediaMan.Server.Events;

/// <summary>
/// Writes envelopes as SSE frames (<c>event:</c> type, <c>id:</c> id, <c>data:</c> envelope JSON). Headers are flushed
/// with an opening comment so clients see the stream open on an idle server, and a keep-alive comment is sent
/// while idle so proxies don't drop the connection.
/// </summary>
public sealed class EventStreamResult(EventBroadcaster broadcaster) : IResult
{
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var ct = httpContext.RequestAborted;
        var json = httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var response = httpContext.Response;

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            // Subscribing here, not in the handler, means a response that never executes leaves no subscriber behind.
            await using var enumerator = broadcaster.Subscribe(ct).GetAsyncEnumerator(ct);
            await WriteAsync(response, ": connected\n\n", ct);
            await StreamAsync(enumerator, response, json, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client disconnected.
        }
    }

    private static async Task StreamAsync(
        IAsyncEnumerator<EventEnvelope> enumerator,
        HttpResponse response,
        JsonSerializerOptions json,
        CancellationToken ct)
    {
        var next = enumerator.MoveNextAsync().AsTask();
        try
        {
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var keepAlive = Task.Delay(KeepAliveInterval, idle.Token);
                if (await Task.WhenAny(next, keepAlive) != next)
                {
                    ct.ThrowIfCancellationRequested();
                    await WriteAsync(response, ": keep-alive\n\n", ct);
                    continue;
                }

                await idle.CancelAsync();
                if (!await next)
                {
                    return;
                }

                var envelope = enumerator.Current;
                var data = JsonSerializer.Serialize(envelope, json);
                await WriteAsync(response, $"event: {envelope.Type}\nid: {envelope.Id}\ndata: {data}\n\n", ct);
                next = enumerator.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            // The enumerator must not be disposed while a MoveNextAsync is still in flight.
            try
            {
                await next;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static async Task WriteAsync(HttpResponse response, string text, CancellationToken ct)
    {
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
        await response.Body.FlushAsync(ct);
    }
}
