using System.Threading.Channels;

namespace DigitalBrain.Signals.Watch;

// The merge behind ISignalWatch.On<T>(Sources): one pump per member, each the member's On<T>(neuron) session synapse
// subscription, all writing to one channel. A registry change re-selects: members that left are
// cancelled, members that joined are pumped from their current position, members that stayed
// keep their subscription (and so their watermark). Any pump or select failure ends the stream
// with that error. Disposal ends it quietly; cancelling the token throws OperationCanceledException
// from the pending read. A hand-written enumerator, not a compiler
// iterator, because a consumer may dispose it while a read is in flight: disposal cancels the
// pending read, lets it settle, and only then releases every member.
internal static class SourceSet
{
    // reselected is a test seam: raised after each re-selection (not the first select) once the members that joined
    // have registered their subscriptions and the members that left are released. Nothing in the product passes it.
    public static IAsyncEnumerable<T> Open<T>(ISignalWatch brain, Sources sources, Func<Task>? subscribed, bool coalesce,
        CancellationToken cancellationToken, Action? reselected = null) where T : DigitalBrain.Signal
        => new Stream<T>(brain, sources, static (brain, member, onSubscribed, stop) => brain.On<T>(member, onSubscribed, stop),
            subscribed, coalesce, reselected, cancellationToken);

    // The same merge over any member subscription: open is asked once per member.
    public static IAsyncEnumerable<T> Open<T>(ISignalWatch brain, Sources sources,
        Func<ISignalWatch, object, Func<Task>, CancellationToken, IAsyncEnumerable<T>> open, CancellationToken cancellationToken,
        Func<Task>? subscribed = null, bool coalesce = false)
        => new Stream<T>(brain, sources, open, subscribed, coalesce, null, cancellationToken);

    private sealed class Stream<T>(ISignalWatch brain, Sources sources, Func<ISignalWatch, object, Func<Task>, CancellationToken, IAsyncEnumerable<T>> open,
        Func<Task>? subscribed, bool coalesce, Action? reselected, CancellationToken streamCancellation) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new Enumerator<T>(brain, sources, open, subscribed, coalesce, reselected, streamCancellation, cancellationToken);
    }

    private sealed class Enumerator<T>(ISignalWatch brain, Sources sources, Func<ISignalWatch, object, Func<Task>, CancellationToken, IAsyncEnumerable<T>> open,
        Func<Task>? subscribed, bool coalesce, Action? reselected, CancellationToken streamCancellation, CancellationToken enumeratorCancellation) : IAsyncEnumerator<T>
    {
        private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(streamCancellation, enumeratorCancellation);
        private readonly Channel<T> _channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });
        // Members are keyed by the neuron itself: a grain reference equates by grain id, a script's neuron by its key, a plain
        // neuron's proxy by its target.
        private readonly Dictionary<object, (Task Pump, CancellationTokenSource Stop)> _members = [];
        private readonly TaskCompletionSource _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _follow;
        private T? _current;
        private Task<bool>? _inFlight;
        private volatile bool _disposing;

        public T Current => _current!;

        public ValueTask<bool> MoveNextAsync()
        {
            ObjectDisposedException.ThrowIf(_disposing, this);
            var move = MoveCoreAsync();
            _inFlight = move;
            return new(move);
        }

        private async Task<bool> MoveCoreAsync()
        {
            var ct = _lifetime.Token;
            try
            {
                if (_follow is null)
                {
                    _follow = Follow(ct);
                    await _registered.Task.WaitAsync(ct).ConfigureAwait(false);
                    if (subscribed is not null) { await subscribed().ConfigureAwait(false); }
                }
                if (!_channel.Reader.TryPeek(out _) && brain is IInitialSignalWait readiness)
                { await readiness.ReachedInitialWaitAsync(ct).ConfigureAwait(false); }
                while (true)
                {
                    if (coalesce && brain is ISignalDelivery delivery)
                    { await delivery.DeliveredAsync(ct).ConfigureAwait(false); }
                    if (!_channel.Reader.TryRead(out var signal))
                    {
                        if (!await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) { return false; }
                        continue;
                    }
                    // A rebuild reads authoritative state, so one element covers everything queued behind it,
                    // including a signal posted during the rebuild that had not reached this channel yet.
                    if (coalesce)
                    {
                        if (brain is ISignalDelivery again) { await again.DeliveredAsync(ct).ConfigureAwait(false); }
                        while (_channel.Reader.TryRead(out _)) { }
                    }
                    _current = signal;
                    return true;
                }
            }
            catch (OperationCanceledException) when (_disposing)
            {
                // Disposal ended the read; the consumer asked for that, so it is not an error.
                return false;
            }
        }

        private async Task Pump(object neuron, Func<Task> onSubscribed, CancellationToken stop)
        {
            using var nested = (brain as IInitialSignalWait)?.SuppressInitialWait();
            try
            {
                await foreach (var signal in open(brain, neuron, onSubscribed, stop).WithCancellation(stop).ConfigureAwait(false))
                { await _channel.Writer.WriteAsync(signal, stop).ConfigureAwait(false); }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception error) { _registered.TrySetException(error); _channel.Writer.TryComplete(error); }
        }

        private async Task Select(bool first, CancellationToken ct)
        {
            var selected = await sources.SelectAsync(ct).ConfigureAwait(false);
            var wanted = selected.ToHashSet();
            foreach (var (key, member) in _members.Where(m => !wanted.Contains(m.Key)).ToArray())
            {
                _members.Remove(key);
                await member.Stop.CancelAsync().ConfigureAwait(false);
                await member.Pump.ConfigureAwait(false);
                member.Stop.Dispose();
            }
            var joining = wanted.Where(w => !_members.ContainsKey(w)).ToArray();
            var ready = new TaskCompletionSource[joining.Length];
            for (var i = 0; i < joining.Length; i++)
            {
                var gate = ready[i] = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _members[joining[i]] = (Pump(joining[i], () => { gate.TrySetResult(); return Task.CompletedTask; }, stop.Token), stop);
            }
            if (first)
            {
                await Task.WhenAll(ready.Select(r => r.Task)).WaitAsync(ct).ConfigureAwait(false);
                _registered.TrySetResult();
            }
            else if (reselected is not null) { _ = Announce(ready, ct); }
        }

        private async Task Announce(TaskCompletionSource[] joined, CancellationToken ct)
        {
            try
            {
                await Task.WhenAll(joined.Select(r => r.Task)).WaitAsync(ct).ConfigureAwait(false);
                reselected!();
            }
            catch (OperationCanceledException) { }
        }

        private async Task Follow(CancellationToken ct)
        {
            using var nested = (brain as IInitialSignalWait)?.SuppressInitialWait();
            try
            {
                if (sources.IsFixed) { await Select(first: true, ct).ConfigureAwait(false); return; }
                // The first select runs once the registry subscription is registered, so a change published
                // while it runs is delivered to the loop below and re-selects.
                await foreach (var _ in sources.Changes(brain, () => Select(first: true, ct), ct).WithCancellation(ct).ConfigureAwait(false))
                { await Select(first: false, ct).ConfigureAwait(false); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception error) { _registered.TrySetException(error); _channel.Writer.TryComplete(error); }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposing) { return; }
            _disposing = true;
            await _lifetime.CancelAsync().ConfigureAwait(false);
            if (_inFlight is { } inFlight)
            {
                // The read already reported to its awaiter; disposal only waits for it to settle.
                await Task.WhenAny(inFlight).ConfigureAwait(false);
            }
            if (_follow is { } follow)
            {
                // A failure is reported through the channel; disposal only waits for the loop to settle.
                await Task.WhenAny(follow).ConfigureAwait(false);
            }
            foreach (var member in _members.Values)
            {
                await Task.WhenAny(member.Pump).ConfigureAwait(false);
                member.Stop.Dispose();
            }
            _members.Clear();
            _lifetime.Dispose();
        }
    }
}
