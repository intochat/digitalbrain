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
    private readonly Channel<T> _signals = Channel.CreateUnbounded<T>(new() { SingleReader = true });
    private readonly CancellationTokenSource _lifetime;
    private readonly Task _pump;
    private int _reader;

    private EdgeSignalSubscription(HttpResponseMessage stream, CancellationToken cancellationToken)
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pump = PumpAsync(stream, _lifetime.Token);
    }

    public Task Completion => _signals.Reader.Completion;

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
                    if (item.Data is { } signal) { await _signals.Writer.WriteAsync(signal, cancellationToken).ConfigureAwait(false); }
                }
                _signals.Writer.TryComplete(new InvalidOperationException("The brain ended the signal stream; subscribe again."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _signals.Writer.TryComplete(); }
            catch (Exception error) { _signals.Writer.TryComplete(error); }
        }
    }

    public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _reader, 1) != 0) { throw new InvalidOperationException("A subscription has one reader."); }
        await foreach (var signal in _signals.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return signal;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
