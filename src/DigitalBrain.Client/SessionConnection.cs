using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading.Channels;
using DigitalBrain.Invocation;

namespace DigitalBrain.Client;

/// <summary>
/// Session-level connection to a DigitalBrain runtime edge over HTTP and SSE.
/// </summary>
public sealed class SessionConnection : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<(string Kind, string Data)> _control = Channel.CreateUnbounded<(string, string)>(new() { SingleReader = true });
    private readonly ConcurrentDictionary<string, SharedSubscription> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _behaviorOf = new(StringComparer.Ordinal);
    private readonly HashSet<string> _paused = new(StringComparer.Ordinal);
    private readonly Lock _pauses = new();
    private volatile string _token;
    private Task _pump = Task.CompletedTask;
    private Task _heartbeat = Task.CompletedTask;
    private readonly Channel<PendingAcknowledgement> _acknowledgements = Channel.CreateUnbounded<PendingAcknowledgement>(new() { SingleReader = true });
    private Task _acknowledger = Task.CompletedTask;
    private readonly TimeProvider _timeProvider;

    private SessionConnection(Uri edge, string token, HttpMessageHandler? handler, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = edge;
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _token = token;
        Brain = edge.Segments is [.., "brains/", var brain] ? Uri.UnescapeDataString(brain.TrimEnd('/')) : "";
    }

    public string Brain { get; }

    public CancellationToken Stopping => _lifetime.Token;

    public IAsyncEnumerable<(string Kind, string Data)> Control => _control.Reader.ReadAllAsync();

    public static Task<SessionConnection> OpenAsync(Uri edge, string token, CancellationToken cancellationToken)
        => OpenAsync(edge, token, handler: null, cancellationToken);

    public static Task<SessionConnection> OpenAsync(Uri edge, string token, HttpMessageHandler? handler, CancellationToken cancellationToken)
        => OpenAsync(edge, token, handler, TimeProvider.System, cancellationToken);

    public static async Task<SessionConnection> OpenAsync(Uri edge, string token, HttpMessageHandler? handler, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var connection = new SessionConnection(new Uri(edge.ToString().TrimEnd('/') + "/"), token, handler, timeProvider);
        try
        {
            await connection.ReportAsync(new RuntimeReport(Heartbeat: true), "connect", cancellationToken).ConfigureAwait(false);
            connection._pump = connection.PumpAsync(connection._lifetime.Token);
            connection._heartbeat = connection.HeartbeatAsync(connection._lifetime.Token);
            connection._acknowledger = connection.AcknowledgePendingAsync(connection._lifetime.Token);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<JsonElement?> InvokeAsync(RuntimeInvocation invocation, CancellationToken cancellationToken)
        => InvokeAsync(invocation, traceParent: null, cancellationToken);

    public async Task<JsonElement?> InvokeAsync(RuntimeInvocation invocation, string? traceParent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        using var activity = traceParent is null ? null : new System.Diagnostics.Activity("DigitalBrain.Behavior.Invoke").SetParentId(traceParent).Start();
        using var response = await SendAsync(HttpMethod.Post, "runtime/v1/invoke", invocation, HttpCompletionOption.ResponseContentRead, cancellationToken,
            System.Diagnostics.Activity.Current?.Id).ConfigureAwait(false);
        await EnsureSuccessAsync(response, invocation.Contract + "." + invocation.Method, cancellationToken).ConfigureAwait(false);
        return response.Content.Headers.ContentLength == 0 || response.StatusCode == HttpStatusCode.NoContent
            ? null
            : await response.Content.ReadFromJsonAsync<JsonElement>(RuntimeProtocol.Json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SubscribeAsync(SubscribeReport subscribe, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "runtime/v1/report", new RuntimeReport(Subscribe: subscribe), HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "subscribe to " + subscribe.Signal, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(RuntimeProtocol.Json, cancellationToken).ConfigureAwait(false);
        var id = body.GetProperty("subscription").GetString() ?? throw new InvalidOperationException("The runtime edge answered a subscribe without a subscription id.");
        _behaviorOf[id] = subscribe.Behavior;
        bool paused;
        lock (_pauses) { paused = _paused.Contains(subscribe.Behavior); }
        if (paused) { _ = Subscription(id).Pause(); }
        return id;
    }

    public async Task<bool> PauseAsync(string behavior, TimeSpan deadline, TimeProvider? time, CancellationToken cancellationToken)
    {
        lock (_pauses) { _paused.Add(behavior); }
        var idle = Task.WhenAll(_behaviorOf.Where(pair => pair.Value == behavior).Select(pair => Subscription(pair.Key).Pause()).ToArray());
        try
        {
            await idle.WaitAsync(deadline, time ?? TimeProvider.System, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    public void Resume(string behavior)
    {
        lock (_pauses) { _paused.Remove(behavior); }
        foreach (var pair in _behaviorOf.Where(pair => pair.Value == behavior)) { Subscription(pair.Key).Resume(); }
    }

    public void Release(string behavior)
    {
        lock (_pauses) { _paused.Remove(behavior); }
        foreach (var pair in _behaviorOf.Where(pair => pair.Value == behavior).ToArray())
        {
            _behaviorOf.TryRemove(pair.Key, out _);
            if (_subscriptions.TryRemove(pair.Key, out var gone)) { gone.Complete(); }
        }
    }

    public async Task AcknowledgeAsync(string subscription, long watermark, CancellationToken cancellationToken, long cpuMicroseconds = 0, long wallMicroseconds = 0, int batches = 1, bool skipped = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new PendingAcknowledgement(new(subscription, watermark, cpuMicroseconds, wallMicroseconds, batches, skipped), System.Diagnostics.Activity.Current?.Id);
        ObjectDisposedException.ThrowIf(!_acknowledgements.Writer.TryWrite(pending), typeof(SessionConnection));
        await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AcknowledgePendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _acknowledgements.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var pending = new List<PendingAcknowledgement>();
                while (pending.Count < 256 && _acknowledgements.Reader.TryRead(out var item))
                { pending.Add(item); }
                foreach (var group in pending.GroupBy(item => item.Parent, StringComparer.Ordinal))
                {
                    using var activity = group.Key is null ? null : new System.Diagnostics.Activity("DigitalBrain.Behavior.Acknowledge").SetParentId(group.Key).Start();
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _timeProvider);
                    using var reportLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                    try
                    {
                        var reports = group.GroupBy(item => item.Report.Subscription, StringComparer.Ordinal)
                            .Select(items => new AcknowledgeReport(items.Key, items.Max(item => item.Report.Watermark),
                                items.Sum(item => item.Report.CpuMicroseconds), items.Sum(item => item.Report.WallMicroseconds),
                                items.Sum(item => item.Report.Batches), items.All(item => item.Report.Skipped))).ToArray();
                        await ReportAsync(new RuntimeReport(Acknowledgements: reports), "acknowledge", reportLifetime.Token).ConfigureAwait(false);
                        foreach (var item in group) { item.Completion.TrySetResult(); }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    { foreach (var item in group) { item.Completion.TrySetCanceled(cancellationToken); } }
                    catch (OperationCanceledException error) when (deadline.IsCancellationRequested)
                    { foreach (var item in group) { item.Completion.TrySetException(new TimeoutException("The acknowledgement report timed out.", error)); } }
                    catch (Exception error)
                    { foreach (var item in group) { item.Completion.TrySetException(error); } }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            while (_acknowledgements.Reader.TryRead(out var pending)) { pending.Completion.TrySetCanceled(cancellationToken); }
        }
    }

    private sealed record PendingAcknowledgement(AcknowledgeReport Report, string? Parent)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task ReportStatusAsync(BehaviorStatusReport status, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "runtime/v1/report", new RuntimeReport(Status: status),
            HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (status.Status == "Initialized" && ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
        { throw new HttpRequestException("Initialization report temporarily unavailable.", null, response.StatusCode); }
        await EnsureSuccessAsync(response, "report status", cancellationToken).ConfigureAwait(false);
    }

    public Task ReportLogsAsync(string request, string text, CancellationToken cancellationToken)
        => ReportAsync(new RuntimeReport(Logs: new(request, text)), "report logs", cancellationToken);

    internal SharedSubscription.Reader Join(string subscription) => Subscription(subscription).Join();

    private SharedSubscription Subscription(string id) => _subscriptions.GetOrAdd(id, static (id, session) => new(session, id), this);

    public bool TryUpdateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) { return false; }
        _token = token;
        return true;
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.WhenAny(ReadStreamAsync()).ConfigureAwait(false);
            try { await Task.Delay(backoff, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaximumBackoff.Ticks));
        }

        async Task ReadStreamAsync()
        {
            using var response = await SendAsync(HttpMethod.Get, "runtime/v1/stream", null, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "open the session stream", cancellationToken).ConfigureAwait(false);
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await foreach (var item in SseParser.Create(body).EnumerateAsync(cancellationToken).ConfigureAwait(false))
            {
                backoff = TimeSpan.FromSeconds(1);
                Dispatch(item);
            }
        }
    }

    private void Dispatch(SseItem<string> item)
    {
        switch (item.EventType)
        {
            case SignalBatchEvent.Kind:
                var batch = JsonSerializer.Deserialize<SignalBatchEvent>(item.Data, RuntimeProtocol.Json)!;
                Subscription(batch.Subscription).Arrive(batch);
                return;
            case TokenEvent.Kind:
                TryUpdateToken(JsonSerializer.Deserialize<TokenEvent>(item.Data, RuntimeProtocol.Json)!.Token);
                return;
            default:
                _control.Writer.TryWrite((item.EventType, item.Data));
                return;
        }
    }

    private async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HeartbeatPeriod);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try { await ReportAsync(new RuntimeReport(Heartbeat: true), "heartbeat", cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (error is HttpRequestException or InvalidOperationException) { continue; }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReportAsync(RuntimeReport report, string operation, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "runtime/v1/report", report, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, HttpCompletionOption completion, CancellationToken cancellationToken, string? traceParent = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: RuntimeProtocol.Json) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (traceParent is not null) { request.Headers.TryAddWithoutValidation("traceparent", traceParent); }
        return _http.SendAsync(request, completion, cancellationToken);
    }

    private static RevisionConflictException? TryReadConflict(string detail)
    {
        try
        {
            using var body = JsonDocument.Parse(detail);
            return body.RootElement is { ValueKind: JsonValueKind.Object } root
                && root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                && root.TryGetProperty("currentRevision", out var revision) && revision.ValueKind == JsonValueKind.Number && revision.TryGetInt64(out var current)
                ? new RevisionConflictException(message.GetString()!, current)
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) { return; }
        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? refusalCode = null;
        if (response.Headers.TryGetValues("X-DigitalBrain-Refusal", out var codes) || response.Headers.TryGetValues("X-IntoChatOS-Refusal", out codes))
        {
            refusalCode = codes.FirstOrDefault();
        }

        if (refusalCode is not null)
        {
            if (refusalCode == RevisionConflictException.Code && TryReadConflict(detail) is { } conflict) { throw conflict; }
            if (refusalCode == "RuleViolationException" || refusalCode == nameof(DigitalBrain.Rules.RuleViolationException))
            {
                throw new DigitalBrain.Rules.RuleViolationException(new DigitalBrain.Rules.Rejected(operation, [refusalCode], [detail]));
            }
            throw new RemoteInvocationException(detail, refusalCode);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized) { throw new UnauthorizedAccessException(detail); }
        var message = $"The runtime edge refused {operation} ({(int)response.StatusCode}): {detail}";
        if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)response.StatusCode is >= 500 and <= 599)
        { throw new HttpRequestException(message, null, response.StatusCode); }
        throw new InvalidOperationException(message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime.IsCancellationRequested) { return; }
        _acknowledgements.Writer.TryComplete();
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_pump, _heartbeat, _acknowledger).ConfigureAwait(false);
        foreach (var subscription in _subscriptions.Values) { subscription.Complete(); }
        _control.Writer.TryComplete();
        _http.Dispose();
        _lifetime.Dispose();
    }
}

public sealed record SubscribeReport(string Behavior, string Contract, string Key, string Signal, string? Selector = null);
public sealed record AcknowledgeReport(string Subscription, long Watermark, long CpuMicroseconds = 0, long WallMicroseconds = 0, int Batches = 1, bool Skipped = false);
public sealed record BehaviorStatusReport(string Behavior, string Hash, string Status, int? ExitCode, string? Error, string? AssignmentRevision = null);
public sealed record LogsReport(string Request, string Text);

public sealed record RuntimeReport(
    string? Fingerprint = null,
    SubscribeReport? Subscribe = null,
    AcknowledgeReport? Acknowledge = null,
    BehaviorStatusReport? Status = null,
    LogsReport? Logs = null,
    bool Heartbeat = false,
    AcknowledgeReport[]? Acknowledgements = null);

public sealed record AssignmentEvent(string Behavior, string Hash, Uri Assembly, Uri Symbols, Dictionary<string, string> Settings, string? Revision = null)
{ public const string Kind = "assign"; }
public sealed record RetireEvent(string Behavior) { public const string Kind = "retire"; }
public sealed record SubscribedEvent(string Behavior, string Contract, string Key, string Signal, string Subscription) { public const string Kind = "subscribed"; }
/// <summary>A batch of signals; <see cref="TraceIds"/> and <see cref="CausationIds"/>, when sent, hold one entry per signal, null where unknown.</summary>
public sealed record SignalBatchEvent(string Subscription, string[] Signals, long Watermark, string? TraceParent = null,
    string?[]? TraceIds = null, string?[]? CausationIds = null) { public const string Kind = "signals"; }
public sealed record TokenEvent(string Token) { public const string Kind = "token"; }
public sealed record ReadyEvent(string Session, string[] Behaviors) { public const string Kind = "ready"; }

internal sealed class SharedSubscription(SessionConnection session, string id)
{
    private readonly Lock _gate = new();
    private readonly List<Reader> _readers = [];
    private readonly List<SignalBatchEvent> _unacknowledged = [];
    private long _queued;
    private long _reported;
    private long _confirmed;
    private long _cpuMicroseconds;
    private long _wallMicroseconds;
    private readonly HashSet<long> _handled = [];
    private readonly List<SignalBatchEvent> _held = [];
    private bool _paused;
    private long _delivered;
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Reader Join()
    {
        lock (_gate)
        {
            var reader = new Reader(this, _reported);
            foreach (var batch in _unacknowledged.Where(batch => !_held.Contains(batch)))
            {
                reader.Queue.Writer.TryWrite(batch);
                _delivered = Math.Max(_delivered, batch.Watermark);
            }
            _readers.Add(reader);
            return reader;
        }
    }

    public void Arrive(SignalBatchEvent batch)
    {
        lock (_gate)
        {
            var first = batch.Watermark - batch.Signals.Length + 1;
            var fresh = batch.Signals.Skip((int)Math.Clamp(_queued - first + 1, 0, batch.Signals.Length)).ToArray();
            if (fresh.Length == 0) { return; }
            _queued = batch.Watermark;
            var arrived = batch with { Signals = fresh };
            _unacknowledged.Add(arrived);
            if (_paused) { _held.Add(arrived); return; }
            Deliver(arrived);
        }
    }

    private void Deliver(SignalBatchEvent batch)
    {
        foreach (var reader in _readers) { reader.Queue.Writer.TryWrite(batch); }
        _delivered = Math.Max(_delivered, batch.Watermark);
    }

    public Task Pause()
    {
        lock (_gate)
        {
            _paused = true;
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            SettleIdle();
            return _idle.Task;
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            _paused = false;
            var held = _held.ToArray();
            _held.Clear();
            foreach (var batch in held) { Deliver(batch); }
        }
    }

    private void SettleIdle()
    {
        if (_paused && (_readers.Count == 0 || _readers.Min(reader => reader.Acknowledged) >= _delivered)) { _idle.TrySetResult(); }
    }

    public void Complete()
    {
        lock (_gate) { foreach (var reader in _readers) { reader.Queue.Writer.TryComplete(); } }
    }

    private Task AcknowledgeAsync(Reader reader, long watermark, long cpuMicroseconds, long wallMicroseconds, bool skipped, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Report? report;
        lock (_gate)
        {
            if (reader.Left) { return Task.CompletedTask; }
            reader.Acknowledged = Math.Max(reader.Acknowledged, watermark);
            _cpuMicroseconds += cpuMicroseconds;
            _wallMicroseconds += wallMicroseconds;
            if (!skipped && watermark > _reported) { _handled.Add(watermark); }
            report = Advance();
            SettleIdle();
        }
        return report is { } advanced ? SendAsync(advanced, cancellationToken) : Task.CompletedTask;
    }

    private void Leave(Reader reader)
    {
        Report? report;
        lock (_gate)
        {
            if (reader.Left) { return; }
            reader.Left = true;
            _readers.Remove(reader);
            reader.Queue.Writer.TryComplete();
            if (_readers.Count == 0)
            {
                _unacknowledged.Clear();
                _handled.Clear();
                _held.Clear();
                _queued = _reported = _confirmed;
                SettleIdle();
                return;
            }
            report = Advance();
            SettleIdle();
        }
        if (report is { } advanced) { _ = SendQuietlyAsync(advanced); }
    }

    private Report? Advance()
    {
        var watermark = _readers.Min(reader => reader.Acknowledged);
        if (watermark <= _reported) { return null; }
        var batches = _unacknowledged.RemoveAll(batch => batch.Watermark <= watermark);
        _reported = watermark;
        var report = new Report(watermark, _cpuMicroseconds, _wallMicroseconds, batches, _handled.RemoveWhere(handled => handled <= watermark) == 0);
        _cpuMicroseconds = _wallMicroseconds = 0;
        return report;
    }

    private async Task SendAsync(Report report, CancellationToken cancellationToken)
    {
        await session.AcknowledgeAsync(id, report.Watermark, cancellationToken, report.CpuMicroseconds, report.WallMicroseconds, report.Batches, report.Skipped).ConfigureAwait(false);
        lock (_gate) { _confirmed = Math.Max(_confirmed, report.Watermark); }
    }

    private async Task SendQuietlyAsync(Report report)
    {
        await Task.WhenAny(SendAsync(report, CancellationToken.None)).ConfigureAwait(false);
    }

    private readonly record struct Report(long Watermark, long CpuMicroseconds, long WallMicroseconds, int Batches, bool Skipped);

    public sealed class Reader
    {
        private readonly SharedSubscription _subscription;

        internal Reader(SharedSubscription subscription, long acknowledged)
        {
            _subscription = subscription;
            Acknowledged = acknowledged;
        }

        internal Channel<SignalBatchEvent> Queue { get; } = Channel.CreateUnbounded<SignalBatchEvent>(new() { SingleReader = true });
        internal long Acknowledged { get; set; }
        internal bool Left { get; set; }

        public ChannelReader<SignalBatchEvent> Batches => Queue.Reader;

        public Task AcknowledgeAsync(long watermark, long cpuMicroseconds, long wallMicroseconds, bool skipped, CancellationToken cancellationToken)
            => _subscription.AcknowledgeAsync(this, watermark, cpuMicroseconds, wallMicroseconds, skipped, cancellationToken);

        public void Leave() => _subscription.Leave(this);
    }
}
