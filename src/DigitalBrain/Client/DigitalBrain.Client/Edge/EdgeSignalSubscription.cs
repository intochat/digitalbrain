using DigitalBrain.Contracts.Edge.V1;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Client;

// One server-sent event stream per subscription; the edge sends only signals of type T.
internal sealed class EdgeSignalSubscription<T> : ISignalSubscription<T> where T : Signal
{
    private readonly Channel<T> _signals = Channel.CreateBounded<T>(new BoundedChannelOptions(1024) { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource _lifetime;
    private readonly Task _pump;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _reader;
    private readonly Lock _gate = new();
    private Task? _cleanup;

    private EdgeSignalSubscription(HttpResponseMessage stream, CancellationToken cancellationToken)
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pump = PumpAsync(stream, _lifetime.Token);
    }

    public Task Completion => _completion.Task;

    public static async Task<ISignalSubscription<T>> OpenAsync(ScriptEdgeClient edge, NeuronProxy source, CancellationToken cancellationToken)
    {
        var stream = await edge.OpenSignalsAsync(source.Contract.FullName!, source.Key, typeof(T).Name, cancellationToken).ConfigureAwait(false);
        return new EdgeSignalSubscription<T>(stream, cancellationToken);
    }

    private async Task PumpAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var parser = SseParser.Create(body, static (eventType, data)
                    => eventType == ScriptEdgeProtocol.SignalEvent ? JsonSerializer.Deserialize<T>(data, ScriptEdgeProtocol.Json) : null);
                await foreach (var item in parser.EnumerateAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (item.Data is { } signal && !_signals.Writer.TryWrite(signal)) { throw new InvalidOperationException("Signal subscription buffer overflowed."); }
                }
                Finish(new InvalidOperationException("The brain ended the signal stream; subscribe again."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Finish(); }
            catch (Exception error) { Finish(error); }
        }
    }

    private void Finish(Exception? error = null)
    {
        _signals.Writer.TryComplete(error);
        if (error is null) { _completion.TrySetResult(); }
        else { _completion.TrySetException(error); }
    }

    public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _reader, 1) != 0) { throw new InvalidOperationException("A subscription has one reader."); }
        try
        {
            await foreach (var signal in _signals.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            { yield return signal; }
        }
        finally { await DisposeAsync().ConfigureAwait(false); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) { return new(_cleanup ??= CleanupAsync()); }
    }

    private async Task CleanupAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
