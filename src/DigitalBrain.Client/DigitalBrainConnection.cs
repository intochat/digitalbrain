using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities;
using DigitalBrain.Invocation;
using DigitalBrain.Signals.Watch;

namespace DigitalBrain.Client;

/// <summary>
/// A connection to a DigitalBrain runtime session: neuron calls and signal subscriptions made in a behavior's name.
/// </summary>
public sealed class DigitalBrainConnection : ISignalWatch, ISelectiveWatch, IAsyncDisposable, IInitialSignalWait
{
    private readonly SessionConnection _session;
    private readonly string _behavior;
    private readonly IReadOnlyDictionary<string, string> _settings;
    private readonly bool _ownsSession;
    private readonly ConditionalWeakTable<object, CapabilityCaller> _provenance = new();
    private readonly AsyncLocal<int> _nestedWait = new();
    private readonly Lock _initialGate = new();
    private Task? _initialized;

    public DigitalBrainConnection(SessionConnection session, string behavior, IReadOnlyDictionary<string, string>? settings = null, CancellationToken stopping = default, bool ownsSession = false)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        _settings = settings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        Stopping = stopping;
        _ownsSession = ownsSession;
    }

    public static async Task<DigitalBrainConnection> OpenAsync(
        Uri edge,
        string token,
        string behavior,
        HttpMessageHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        var session = await SessionConnection.OpenAsync(
            edge,
            token,
            handler,
            cancellationToken).ConfigureAwait(false);
        return new DigitalBrainConnection(
            session,
            behavior,
            new Dictionary<string, string>(StringComparer.Ordinal),
            session.Stopping,
            ownsSession: true);
    }

    public CapabilityCaller? Provenance(object signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return _provenance.TryGetValue(signal, out var caller) ? caller : null;
    }

    public CancellationToken Stopping { get; }

    public string? Setting(string name) => _settings.TryGetValue(name, out var value) ? value : null;

    public T Get<T>(string fullKey) where T : class
        => NeuronProxy.Create<T>(_session, _behavior, fullKey, null);

    public T Neuron<T>(string relative) where T : class => _session.Brain is { Length: > 0 } brain
        ? Get<T>($"{brain}/{relative}")
        : throw new InvalidOperationException("This session was not told its brain.");

    public IBrain Self => _session.Brain is { Length: > 0 } brain
        ? NeuronProxy.Create<IBrain>(_session, _behavior, IBrain.Key(brain), null)
        : throw new InvalidOperationException("This session was not told its brain.");

    public IAsyncEnumerable<T> On<T>(object neuron, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => Watch<T>(neuron, static () => Task.CompletedTask, selector: null, cancellationToken);

    public IAsyncEnumerable<T> On<T>(object neuron, Func<Task> onSubscribed, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => Watch<T>(neuron, onSubscribed, selector: null, cancellationToken);

    public IAsyncEnumerable<T> On<T>(object neuron, string selector, Func<Task> onSubscribed, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
    {
        ArgumentNullException.ThrowIfNull(neuron);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        return Watch<T>(neuron, onSubscribed, selector, cancellationToken);
    }

    private WatchStream<T> Watch<T>(object source, Func<Task> onSubscribed, string? selector, CancellationToken cancellationToken) where T : class
    {
        ArgumentNullException.ThrowIfNull(onSubscribed);
        if (source is not NeuronProxy neuron) { throw new ArgumentException("Watch a neuron obtained from this connection's Get.", nameof(source)); }
        return new WatchStream<T>(async ct =>
        {
            var subscription = await SessionSignalSubscription<T>.OpenAsync(_session, _behavior, neuron, ct, selector, (signal, caller) => _provenance.AddOrUpdate(signal, caller)).ConfigureAwait(false);
            return (subscription.ReadAllAsync(ct), subscription);
        }, cancellationToken, Stopping, async () =>
        {
            await onSubscribed().ConfigureAwait(false);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Stopping);
            await ((IInitialSignalWait)this).ReachedInitialWaitAsync(wait.Token).ConfigureAwait(false);
        });
    }

    public IAsyncEnumerable<T> On<T>(Sources sources, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => Sources.Stream<T>(this, sources, cancellationToken);

    public Task React<T>(object neuron, Func<CancellationToken, Task> rebuild, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => Sources.React<T>(this, Sources.Of(neuron), rebuild, cancellationToken);

    public Task React<T>(Sources sources, Func<CancellationToken, Task> rebuild, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => Sources.React<T>(this, sources, rebuild, cancellationToken);

    public ValueTask DisposeAsync() => _ownsSession ? _session.DisposeAsync() : ValueTask.CompletedTask;

    IDisposable IInitialSignalWait.SuppressInitialWait()
    {
        var previous = _nestedWait.Value;
        _nestedWait.Value = previous + 1;
        return new RestoreWait(() => _nestedWait.Value = previous);
    }

    Task IInitialSignalWait.ReachedInitialWaitAsync(CancellationToken cancellationToken)
    {
        if (_nestedWait.Value != 0) { return Task.CompletedTask; }
        lock (_initialGate)
        {
            return _initialized ??= ReportInitialWaitAsync(cancellationToken);
        }
    }

    private async Task ReportInitialWaitAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(100);
        while (true)
        {
            try
            {
                await _session.ReportStatusAsync(new(_behavior, "", "Initialized", null, null), cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
            }
        }
    }

    private sealed class RestoreWait(Action restore) : IDisposable { public void Dispose() => restore(); }

    private sealed class SessionSignalSubscription<T> : IAsyncDisposable where T : class
    {
        private readonly SharedSubscription.Reader _reader;
        private readonly Action<T, CapabilityCaller>? _onProvenance;

        private SessionSignalSubscription(SharedSubscription.Reader reader, Action<T, CapabilityCaller>? onProvenance)
        {
            _reader = reader;
            _onProvenance = onProvenance;
        }

        public static async Task<SessionSignalSubscription<T>> OpenAsync(SessionConnection session, string behavior, NeuronProxy source, CancellationToken cancellationToken, string? selector = null, Action<T, CapabilityCaller>? onProvenance = null)
        {
            var signal = typeof(DigitalBrain.Signal).IsAssignableFrom(typeof(T))
                ? typeof(T).GetCustomAttribute<DigitalBrain.SignalAttribute>(inherit: false)?.Alias
                    ?? throw new ArgumentException($"{typeof(T).Name} is not a [Signal] record.")
                : typeof(T).Name;
            var id = await session.SubscribeAsync(new SubscribeReport(behavior, source.Contract.FullName!, source.Key, signal, selector), cancellationToken).ConfigureAwait(false);
            return new SessionSignalSubscription<T>(session.Join(id), onProvenance);
        }

        public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var batch in _reader.Batches.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var wallStart = Stopwatch.GetTimestamp();
                var skipped = batch.Signals.Length > 0;
                foreach (var json in batch.Signals)
                {
                    var signal = JsonSerializer.Deserialize<T>(json, RuntimeProtocol.Json)!;
                    if (_onProvenance is not null && TryReadProvenance(json) is { } caller)
                    {
                        _onProvenance(signal, caller);
                    }
                    yield return signal;
                    skipped &= SignalDeliveries.WasSkipped(signal);
                }
                var wallMicros = (long)Stopwatch.GetElapsedTime(wallStart).TotalMicroseconds;
                var cpuMicros = wallMicros;
                await _reader.AcknowledgeAsync(batch.Watermark, cpuMicros, wallMicros, skipped, cancellationToken).ConfigureAwait(false);
            }
        }

        private static CapabilityCaller? TryReadProvenance(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("provenance", out var prop)
                    && prop.ValueKind == JsonValueKind.Object)
                {
                    return prop.Deserialize<CapabilityCaller>(RuntimeProtocol.Json);
                }
            }
            catch (JsonException) { }
            return null;
        }

        public ValueTask DisposeAsync()
        {
            _reader.Leave();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Obsolete alias for <see cref="DigitalBrainConnection"/> for backwards compatibility with earlier IntoChat OS versions.
/// </summary>
[Obsolete("IntoChatOSConnection has been renamed to DigitalBrainConnection in DigitalBrain.Client.")]
public class IntoChatOSConnection : ISignalWatch, ISelectiveWatch, IAsyncDisposable, IInitialSignalWait
{
    private readonly DigitalBrainConnection _inner;

    public IntoChatOSConnection(SessionConnection session, string behavior, IReadOnlyDictionary<string, string>? settings = null, CancellationToken stopping = default, bool ownsSession = false)
    {
        _inner = new DigitalBrainConnection(session, behavior, settings, stopping, ownsSession);
    }

    public static async Task<IntoChatOSConnection> OpenAsync(Uri edge, string token, string behavior, HttpMessageHandler? handler = null, CancellationToken cancellationToken = default)
    {
        var conn = await DigitalBrainConnection.OpenAsync(edge, token, behavior, handler, cancellationToken).ConfigureAwait(false);
        return new IntoChatOSConnection(conn);
    }

    private IntoChatOSConnection(DigitalBrainConnection inner) => _inner = inner;

    public CancellationToken Stopping => _inner.Stopping;

    public string? Setting(string name) => _inner.Setting(name);

    public CapabilityCaller? Provenance(object signal) => _inner.Provenance(signal);

    public T Get<T>(string fullKey) where T : class => _inner.Get<T>(fullKey);

    public T Neuron<T>(string relative) where T : class => _inner.Neuron<T>(relative);

    public IBrain Self => _inner.Self;

    public IAsyncEnumerable<T> On<T>(object neuron, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => _inner.On<T>(neuron, cancellationToken);

    public IAsyncEnumerable<T> On<T>(object neuron, Func<Task> onSubscribed, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => _inner.On<T>(neuron, onSubscribed, cancellationToken);

    public IAsyncEnumerable<T> On<T>(object neuron, string selector, Func<Task> onSubscribed, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => _inner.On<T>(neuron, selector, onSubscribed, cancellationToken);

    public IAsyncEnumerable<T> On<T>(Sources sources, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => _inner.On<T>(sources, cancellationToken);

    public Task React<T>(object neuron, Func<CancellationToken, Task> rebuild, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => _inner.React<T>(neuron, rebuild, cancellationToken);

    public Task React<T>(Sources sources, Func<CancellationToken, Task> rebuild, CancellationToken cancellationToken = default) where T : DigitalBrain.Signal
        => _inner.React<T>(sources, rebuild, cancellationToken);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _inner.DisposeAsync();
    }

    IDisposable IInitialSignalWait.SuppressInitialWait() => ((IInitialSignalWait)_inner).SuppressInitialWait();

    Task IInitialSignalWait.ReachedInitialWaitAsync(CancellationToken cancellationToken) => ((IInitialSignalWait)_inner).ReachedInitialWaitAsync(cancellationToken);
}
