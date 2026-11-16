using System.Runtime.InteropServices;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Log;
using DigitalBrain.Neurons;

namespace DigitalBrain.Units;

// Reentrant by design: the state is written once by Put and only read afterwards; each Run is one ask's I/O, and interleaving the Runs is the concurrency.
[Neuron(AskRules.BatchNeuronType), Reentrant]
internal sealed class AskBatchNeuron(ISynapseCatalogSource catalog, ISlotResolver slots, IUnitResolver? units = null, AskProgressHub? progress = null) : Neuron, IAskBatch
{
    private const int MaxErrorLength = 500;
    private static readonly TimeSpan MaxHeartbeatPeriod = TimeSpan.FromSeconds(10);

    private AskBatchState? _state;
    private bool _putting;

    protected override async Task OnActivate(CancellationToken cancellationToken) =>
        _state = await Seat.Load<AskBatchState>().ConfigureAwait(true);

    public async Task Put(string unit, IReadOnlyList<Ask> asks)
    {
        ArgumentException.ThrowIfNullOrEmpty(unit);
        ArgumentNullException.ThrowIfNull(asks);
        RequireAsksCaller(unit);
        if (_state is not null || _putting)
        {
            return;
        }

        _putting = true;
        try
        {
            var next = new AskBatchState(unit, [.. asks]);
            await Seat.Save(next, []).ConfigureAwait(true);
            _state = next;
        }
        finally
        {
            _putting = false;
        }
    }

    public Task<IReadOnlyList<Ask>> All() => Task.FromResult(_state?.Asks ?? []);

    public async Task Run(int index, int attempt)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(slots);
        RequireAsksCaller();
        if (_state is not { } state || (uint)index >= (uint)state.Asks.Count)
        {
            return;
        }

        var unit = state.Unit;
        var ask = state.Asks[index];
        var batch = BatchKey(unit);
        var runId = $"{unit}/{batch}/{index}/{attempt}";
        var startedAt = Context.Time.GetUtcNow();

        var requested = ask.Timeout ?? AskLimits.DefaultTimeout;
        var timeout = requested < AskLimits.MaxTimeout ? requested : AskLimits.MaxTimeout;
        if (timeout < TimeSpan.Zero)
        {
            timeout = TimeSpan.Zero;
        }

        using var lease = new RenewableAskLease(Context.Time, startedAt, timeout);
        var asksNeuron = Context.Get<IAsks>(new NeuronId(Id.Brain, AskRules.AsksNeuronType, unit));

        async ValueTask ReportProgressLocal(string progressStatus, double? fraction, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!lease.Renew(timeout))
            {
                return;
            }

            await asksNeuron.ReportProgress(new AskProgressReport(batch, index, attempt, runId, ask.Key, progressStatus, fraction, timeout)).ConfigureAwait(true);
        }

        async ValueTask<bool> RenewLeaseLocal(TimeSpan? extension, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var window = ClampWindow(extension ?? timeout, timeout);
            if (!lease.Renew(window))
            {
                return false;
            }

            await asksNeuron.Renew(new AskLeaseRenewal(batch, index, attempt, window)).ConfigureAwait(true);
            return true;
        }

        using var hubRegistration = progress?.RegisterRun(runId, unit, ask.Key, ReportProgressLocal);
        using var ambientRun = AskRun.Enter(new AskRun(runId, unit, ask.Key, ReportProgressLocal, RenewLeaseLocal));
        using var keepAliveTimer = ask.KeepAlive && timeout > TimeSpan.Zero
            ? StartKeepAlive(Context.Time, timeout, () => RenewLeaseLocal(timeout, CancellationToken.None).AsTask())
            : null;

        var cts = lease.TokenSource;
        AnswerStatus status;
        var result = AskRules.NoResult;
        string? error = null;
        string? detail = null;
        var transient = false;

        bool isUnitAlias = ask.Slot.StartsWith("u.", StringComparison.Ordinal);
        string targetUnitAlias = isUnitAlias ? ask.Slot[2..] : ask.Slot;

        SynapseCatalog? dynamicCat = null;
        if (units is not null)
        {
            try
            {
                using var handle = await units.Resolve(Id.Brain, unit, UnitView.Live, cts.Token).ConfigureAwait(true);
                dynamicCat = handle?.Catalog;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                GetLogger("DigitalBrain.Synapses").LogSwallowed(ex, "Resolving live unit catalog before ask run failed.");
            }
        }
        var cat = dynamicCat ?? catalog.Static;
        var slot = DeferredSlot(cat.For(unit), ask.Slot);

        if (slot is null && !isUnitAlias)
        {
            await Settle(unit, new AskSettlement(batch, index, attempt, ask.Slot, ask.Operation, ask.Key, ask.Repeatable, AnswerStatus.Refused, AskRules.NoResult, "slot-dropped", Transient: false, Detail: $"Slot '{ask.Slot}' was not found.")).ConfigureAwait(true);
            return;
        }

        bool isUnitContract = isUnitAlias || slot?.Contract == typeof(IUnit);

        try
        {
            if (isUnitAlias)
            {
                var unitGrain = Context.Get<IUnit>(new NeuronId(Id.Brain, targetUnitAlias, "default"));
                var ans = await unitGrain.Handle(ask, cts.Token).ConfigureAwait(true);
                status = ans.Status;
                result = ans.Result;
                error = ans.Error;
                detail = ans.Detail;
            }
            else
            {
                var capability = await slots.Resolve(new NeuronId(Id.Brain, unit, "default"), slot!, cts.Token).ConfigureAwait(true);
                try
                {
                    var invoked = await AskCall.Invoke(slot!.Contract, capability, ask.Operation, ask.Arguments, cts.Token).WaitAsync(cts.Token).ConfigureAwait(true);
                    if (JsonMarshal.GetRawUtf8Value(invoked).Length > AskLimits.MaxResultBytes)
                    {
                        status = AnswerStatus.Failed;
                        error = "too-large";
                        detail = "Result exceeded maximum allowed bytes";
                    }
                    else
                    {
                        status = AnswerStatus.Ok;
                        result = invoked;
                    }
                }
                catch (BindingException bindEx) when (capability is IUnit unitCap)
                {
                    var ans = await unitCap.Handle(ask, cts.Token).ConfigureAwait(true);
                    status = ans.Status;
                    result = ans.Result;
                    error = ans.Error;
                    detail = ans.Detail ?? bindEx.Message;
                }
            }
        }
        catch (SlotUnboundException unboundEx)
        {
            if (isUnitContract)
            {
                try
                {
                    var unitGrain = Context.Get<IUnit>(new NeuronId(Id.Brain, targetUnitAlias, "default"));
                    var ans = await unitGrain.Handle(ask, cts.Token).ConfigureAwait(true);
                    status = ans.Status;
                    result = ans.Result;
                    error = ans.Error;
                    detail = ans.Detail;
                }
                catch (Exception unitEx) when (unitEx is not OutOfMemoryException)
                {
                    status = AnswerStatus.Failed;
                    error = "dispatch-failed";
                    detail = Cap(unitEx.Message);
                }
            }
            else
            {
                status = AnswerStatus.Failed;
                error = "unbound";
                detail = Cap(unboundEx.Message);
            }
        }
        catch (BindingException binding)
        {
            if (isUnitContract)
            {
                try
                {
                    var unitGrain = Context.Get<IUnit>(new NeuronId(Id.Brain, targetUnitAlias, "default"));
                    var ans = await unitGrain.Handle(ask, cts.Token).ConfigureAwait(true);
                    status = ans.Status;
                    result = ans.Result;
                    error = ans.Error;
                    detail = ans.Detail;
                }
                catch (Exception unitEx) when (unitEx is not OutOfMemoryException)
                {
                    status = AnswerStatus.Failed;
                    error = "bad-arguments: " + binding.Message;
                    detail = Cap(unitEx.Message);
                }
            }
            else
            {
                status = AnswerStatus.Failed;
                error = Cap("bad-arguments: " + binding.Message);
                detail = Cap(binding.ToString());
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            status = ask.Repeatable ? AnswerStatus.Failed : AnswerStatus.Unknown;
            error = "timeout";
            detail = "Operation timed out";
            transient = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status = AnswerStatus.Failed;
            error = Cap(ex.Message);
            detail = Cap(ex.ToString());
            transient = true;
        }

        await Settle(unit, new AskSettlement(batch, index, attempt, ask.Slot, ask.Operation, ask.Key, ask.Repeatable, status, result, error, transient, detail)).ConfigureAwait(true);
    }

    public async Task Drop()
    {
        RequireAsksCaller();
        if (_state is null)
        {
            return;
        }

        _state = null;
        await Seat.Clear().ConfigureAwait(true);
    }

    private void RequireAsksCaller(string? unit = null)
    {
        var tilde = Id.Key.IndexOf('~', StringComparison.Ordinal);
        var owner = tilde >= 0 ? Id.Key[..tilde] : Id.Key;
        if (unit is not null && tilde >= 0 && !string.Equals(unit, owner, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the asks neuron of the unit stores and runs its asks.");
        }

        if (Context.Caller is { } caller
            && caller.Via != AskRules.AsksOf(new NeuronId(Id.Brain, owner, "default")))
        {
            throw new UnauthorizedAccessException("Only the asks neuron of the unit stores and runs its asks.");
        }
    }

    private string BatchKey(string unit)
    {
        var key = Id.Key;
        var prefix = unit + "~";
        if (key.StartsWith(prefix, StringComparison.Ordinal))
        {
            return key[prefix.Length..];
        }

        var tilde = key.IndexOf('~', StringComparison.Ordinal);
        return tilde >= 0 ? key[(tilde + 1)..] : key;
    }

    private Task Settle(string unit, AskSettlement settlement) =>
        Context.Get<IAsks>(new NeuronId(Id.Brain, AskRules.AsksNeuronType, unit)).Settled(settlement);

    private static SlotDescriptor? DeferredSlot(IReadOnlyList<SynapseDescriptor> synapses, string? slotName)
    {
        for (var i = 0; i < synapses.Count; i++)
        {
            var slots = synapses[i].Slots;
            for (var j = 0; j < slots.Count; j++)
            {
                var slot = slots[j];
                if (slot.Deferred && string.Equals(slot.Name, slotName, StringComparison.Ordinal))
                {
                    return slot;
                }
            }
        }

        return null;
    }

    private static string Cap(string message) =>
        message.Length > MaxErrorLength ? message[..MaxErrorLength] : message;

    private static TimeSpan ClampWindow(TimeSpan requested, TimeSpan fallback) =>
        requested <= TimeSpan.Zero
            ? fallback
            : requested < AskLimits.MaxTimeout
                ? requested
                : AskLimits.MaxTimeout;

    private static ITimer StartKeepAlive(TimeProvider time, TimeSpan timeout, Func<Task> renew)
    {
        var half = TimeSpan.FromTicks(Math.Max(1L, timeout.Ticks / 2));
        var period = half < MaxHeartbeatPeriod ? half : MaxHeartbeatPeriod;
        return time.CreateTimer(
            static state =>
            {
                var callback = (Func<Task>)state!;
                _ = callback();
            },
            renew,
            period,
            period);
    }

    private sealed class RenewableAskLease : IDisposable
    {
        private readonly TimeProvider _time;
        private readonly DateTimeOffset _maxExpiry;
        private readonly Lock _gate = new();
        private readonly ITimer _timer;
        private bool _disposed;

        public RenewableAskLease(TimeProvider time, DateTimeOffset startedAt, TimeSpan initialTimeout)
        {
            _time = time;
            _maxExpiry = startedAt + AskLimits.MaxAge;
            TokenSource = new CancellationTokenSource();
            _timer = time.CreateTimer(static state => ((RenewableAskLease)state!).Cancel(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (initialTimeout == TimeSpan.Zero)
            {
                TokenSource.Cancel();
            }
            else
            {
                var capped = initialTimeout < AskLimits.MaxAge ? initialTimeout : AskLimits.MaxAge;
                _timer.Change(capped, Timeout.InfiniteTimeSpan);
            }
        }

        public CancellationTokenSource TokenSource { get; }

        public bool Renew(TimeSpan window)
        {
            lock (_gate)
            {
                if (_disposed || TokenSource.IsCancellationRequested)
                {
                    return false;
                }

                var now = _time.GetUtcNow();
                if (now >= _maxExpiry)
                {
                    return false;
                }

                var remaining = _maxExpiry - now;
                var nextDue = window < remaining ? window : remaining;
                if (nextDue <= TimeSpan.Zero)
                {
                    return false;
                }

                _timer.Change(nextDue, Timeout.InfiniteTimeSpan);
                return true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _timer.Dispose();
            TokenSource.Dispose();
        }

        private void Cancel()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            try
            {
                TokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}

internal sealed record AskBatchState(string Unit, IReadOnlyList<Ask> Asks);
