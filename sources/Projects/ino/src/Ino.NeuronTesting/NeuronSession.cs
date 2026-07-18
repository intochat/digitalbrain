using Grpc.Core;
using Ino.Core;
using Ino.Grpc;
using Ino.NeuronTesting.Internals;
using Microsoft.Playwright;

namespace Ino.NeuronTesting;

// One NeuronSession = one conversation. Multiple chat turns share the same
// correlation_id (assigned by the server on the first Chat response).
// To test independent conversations, create separate sessions.
public sealed class NeuronSession : IAsyncDisposable
{
    // global:: required: protoc generates Ino.Grpc.Ino (service name matches root namespace).
    readonly global::Ino.Grpc.Ino.InoClient _client;
    readonly PlaywrightLifecycle _playwright;
    readonly string _kernelHttpsUrl;
    readonly List<ChatFrame> _frames = [];
    readonly List<NeuronPage> _pages = [];

    SynapseObserver? _observer;
    CorrelationId _correlationId;

    internal NeuronSession(
        global::Ino.Grpc.Ino.InoClient client,
        PlaywrightLifecycle playwright,
        string kernelHttpsUrl,
        string userId)
    {
        _client = client;
        _playwright = playwright;
        _kernelHttpsUrl = kernelHttpsUrl;
        UserId = userId;
    }

    public string UserId { get; }
    public CorrelationId CorrelationId => _correlationId;
    public IReadOnlyList<ChatFrame> Frames => _frames.AsReadOnly();
    public IReadOnlyList<SynapseFire> Observed => _observer?.Observed ?? [];

    // Returns the most recent non-skeleton frame. Throws if Chat hasn't been called yet.
    public ChatFrame Last
    {
        get
        {
            for (var i = _frames.Count - 1; i >= 0; i--)
                if (!_frames[i].IsSkeleton) return _frames[i];
            throw new InvalidOperationException("No non-skeleton frame received yet.");
        }
    }

    // Sends a chat message and drains the response stream. Skeleton frames are
    // accumulated in Frames; the returned value is the final non-skeleton frame.
    // The correlation_id is set from the first server response and reused for
    // all subsequent turns and RFW events in this session.
    public async Task<ChatFrame> Chat(string prompt, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));

        using var call = _client.Chat(new ChatRequest
        {
            Message = prompt,
            UserId = UserId,
            CorrelationId = _correlationId.Value ?? string.Empty,
        });

        ChatFrame? final = null;
        await foreach (var resp in call.ResponseStream.ReadAllAsync()
                           .WithCancellation(cts.Token))
        {
            if (!string.IsNullOrEmpty(resp.CorrelationId))
            {
                _correlationId = new CorrelationId(resp.CorrelationId);
                // Bind once: the observer is keyed to the first correlation_id
                // the server assigns. Subsequent Chat calls on the same session
                // keep the same observer so all synapse fires across turns are
                // captured together.
                _observer ??= new SynapseObserver(resp.CorrelationId);
            }
            var frame = ToFrame(resp);
            _frames.Add(frame);
            if (!frame.IsSkeleton) final = frame;
        }

        return final ?? throw new InvalidOperationException(
            $"Chat({prompt}) closed without a non-skeleton frame.");
    }

    // Fires an RFW event. Requires Chat to have been called first so that a
    // correlation_id exists; calling Fire before Chat sends an empty
    // correlation_id and the gateway will reject it.
    public async Task<ChatFrame> Fire(string eventName, IReadOnlyDictionary<string, string> args)
    {
        var req = new RfwEventRequest
        {
            CorrelationId = _correlationId.Value ?? string.Empty,
            EventName = eventName,
        };
        foreach (var kv in args) req.Args[kv.Key] = kv.Value;

        var resp = await _client.RfwEventAsync(req);
        if (!resp.Accepted)
            throw new InvalidOperationException(
                $"RfwEvent({eventName}) rejected — reply: {resp.Reply}");

        if (string.IsNullOrEmpty(resp.CorrelationId))
            throw new InvalidOperationException(
                $"RfwEvent({eventName}) returned an empty correlation_id — gateway did not echo it back.");

        var frame = ToFrame(resp);
        _frames.Add(frame);
        return frame;
    }

    // Reflection helper — turns anonymous objects like new { flightId = "FL-001" }
    // into the string dictionary Fire(string, IReadOnlyDictionary) expects.
    public Task<ChatFrame> Fire(string eventName, object args) =>
        Fire(eventName, ReflectArgs(args));

    // Opens a browser page and navigates to the kernel URL. Playwright is
    // lazily initialised inside PlaywrightLifecycle so tests that never call
    // OpenBrowser pay no Chromium startup cost.
    public async Task<NeuronPage> OpenBrowser(string? prompt = null)
    {
        var ctx = await _playwright.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var url = prompt is null
            ? _kernelHttpsUrl
            : $"{_kernelHttpsUrl}?q={Uri.EscapeDataString(prompt)}";
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });

        var wrapper = new NeuronPage(ctx, page);
        _pages.Add(wrapper);
        return wrapper;
    }

    // Polls Frames until a non-skeleton frame whose ContentType contains the
    // given substring arrives, or the timeout elapses.
    public async Task<ChatFrame> WaitForRfw(string contentType, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            for (var i = _frames.Count - 1; i >= 0; i--)
            {
                var f = _frames[i];
                if (!f.IsSkeleton && f.ContentType.Contains(contentType, StringComparison.Ordinal))
                    return f;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"No frame with content_type containing '{contentType}' within " +
            $"{(timeout ?? TimeSpan.FromSeconds(15)).TotalSeconds}s. " +
            $"Saw: {string.Join(", ", _frames.Where(f => !f.IsSkeleton).Select(f => f.ContentType))}");
    }

    // Polls the synapse observer until a fire matching the given type is seen.
    // If Chat has not been called yet, no observer is bound and Observed is
    // always empty — the poll will time out and throw a TimeoutException with
    // the empty-list diagnostic.
    public async Task<SynapseFire> WaitForSynapse(string synapseType, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var match = _observer?.Observed.FirstOrDefault(s => s.Type == synapseType);
            if (match is not null) return match;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"No synapse '{synapseType}' fired within " +
            $"{(timeout ?? TimeSpan.FromSeconds(15)).TotalSeconds}s. " +
            $"Saw: {string.Join(", ", _observer?.Observed.Select(s => s.Type) ?? [])}");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var p in _pages) await p.DisposeAsync();
        _observer?.Dispose();
    }

    static ChatFrame ToFrame(ChatResponse r) => new(
        new CorrelationId(r.CorrelationId),
        r.ContentType,
        r.IsSkeleton,
        r.Reply,
        r.RfwDescription.Length == 0 ? null
            : RfwSnapshot.FromBytes(r.RfwDescription.Span, r.RfwData.Span));

    static ChatFrame ToFrame(RfwEventResponse r) => new(
        new CorrelationId(r.CorrelationId),
        r.ContentType,
        false,
        r.Reply,
        r.RfwDescription.Length == 0 ? null
            : RfwSnapshot.FromBytes(r.RfwDescription.Span, r.RfwData.Span));

    static IReadOnlyDictionary<string, string> ReflectArgs(object args)
    {
        if (args is IReadOnlyDictionary<string, string> already) return already;
        if (args is IDictionary<string, string> dict) return new Dictionary<string, string>(dict);

        var result = new Dictionary<string, string>();
        foreach (var prop in args.GetType().GetProperties())
            result[prop.Name] = prop.GetValue(args)?.ToString() ?? "";
        return result;
    }
}
