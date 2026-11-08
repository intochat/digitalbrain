using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using DigitalBrain.Spi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Http.Surface;

/// <summary>
/// <c>GET /brains/{brainId}/journal/tail?since=&lt;seq&gt;</c>: the brain journal as server-sent events, for the debugger's brain
/// graph. Behind the <see cref="BrainRouteGroups.Brain"/> membership check like every brain read. Each event is
/// <c>journaled-signal</c> with data <c>{"signals":[{seq,publisher,alias,traceId,causationId,at}]}</c>: the signals
/// <see cref="ISignalHistory.Watch"/> yields after <c>since</c> (default 0), coalesced into batches at most
/// <see cref="Window"/> apart. Payloads are not sent; a client reads one through the brain's history reads. Mapped by
/// <see cref="ModuleHttpSurfaces.MapModuleHttpSurfaces"/>; the history is the <see cref="Func{BrainId, ISignalHistory}"/> service the
/// Orleans runtime registers with its brain storage (503 when no runtime registered one).
/// </summary>
public static class JournalTailEndpoints
{
    /// <summary>The server-sent event type of a batch.</summary>
    public const string EventType = "journaled-signal";

    /// <summary>How long a batch collects after its first signal before it is sent.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(100);

    private const int MaxBatch = SignalPage.MaxLimit;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the tail route.</summary>
    /// <param name="endpoints">The route builder.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        BrainRoutes.Group(endpoints, "/journal").MapGet("/tail", Tail);
    }

    private static IResult Tail(HttpContext http, string brainId, long? since)
    {
        if (since is < 0) { return Results.BadRequest(); }
        if (http.RequestServices.GetService<Func<BrainId, ISignalHistory>>() is not { } journals)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var history = journals(new BrainId(brainId));
        return TypedResults.ServerSentEvents(Batches(ct => history.Watch(since ?? 0, ct), http.RequestAborted));
    }

    // Pumps the tail into a channel and sends what arrived within a window of the first signal; a backlog goes out without waiting.
    internal static async IAsyncEnumerable<SseItem<string>> Batches(Func<CancellationToken, IAsyncEnumerable<JournaledSignal>> tail,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<JournaledSignal>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pump = Pump(tail(stop.Token), channel.Writer, stop.Token);
        try
        {
            var reader = channel.Reader;
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                var batch = new List<TailedSignal>();
                Drain(reader, batch);
                if (batch.Count < MaxBatch)
                {
                    await Task.Delay(Window, ct).ConfigureAwait(false);
                    Drain(reader, batch);
                }

                yield return new SseItem<string>(JsonSerializer.Serialize(new TailBatch(batch), Json), EventType);
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await pump.ConfigureAwait(false);
        }
    }

    private static void Drain(ChannelReader<JournaledSignal> reader, List<TailedSignal> batch)
    {
        while (batch.Count < MaxBatch && reader.TryRead(out var item))
        {
            batch.Add(new TailedSignal(item.Sequence, item.Publisher.ToString(), SignalAliases.Of(item.Signal), item.TraceId, item.CausationId, item.At));
        }
    }

    private static async Task Pump(IAsyncEnumerable<JournaledSignal> tail, ChannelWriter<JournaledSignal> writer, CancellationToken ct)
    {
        Exception? failure = null;
        try
        {
            await foreach (var item in tail.WithCancellation(ct).ConfigureAwait(false)) { await writer.WriteAsync(item, ct).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
#pragma warning disable CA1031 // A failing tail ends the stream; the reader sees it as the channel's completion.
        catch (Exception error) { failure = error; }
#pragma warning restore CA1031
        writer.TryComplete(failure);
    }

    private sealed record TailBatch(IReadOnlyList<TailedSignal> Signals);

    private sealed record TailedSignal(long Seq, string Publisher, string Alias, string? TraceId, string? CausationId, DateTimeOffset At);
}
