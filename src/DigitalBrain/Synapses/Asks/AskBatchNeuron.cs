using System.Runtime.InteropServices;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Neurons;

namespace DigitalBrain.Units;

// Reentrant by design: the state is written once by Put and only read afterwards; each Run is one ask's I/O, and interleaving the Runs is the concurrency.
[Neuron(AskRules.BatchNeuronType), Reentrant]
internal sealed class AskBatchNeuron(ISynapseCatalogSource catalog, ISlotResolver slots) : Neuron, IAskBatch
{
    private const int MaxErrorLength = 500;

    private AskBatchState? _state;
    private bool _putting;

    protected override async Task OnActivate(CancellationToken cancellationToken) =>
        _state = await Seat.Load<AskBatchState>().ConfigureAwait(true);

    public async Task Put(string unit, IReadOnlyList<Ask> asks)
    {
        ArgumentException.ThrowIfNullOrEmpty(unit);
        ArgumentNullException.ThrowIfNull(asks);
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
        if (_state is not { } state || (uint)index >= (uint)state.Asks.Count)
        {
            return;
        }

        var unit = state.Unit;
        var ask = state.Asks[index];
        var batch = BatchKey(unit);
        if (DeferredSlot(catalog.Static.For(unit), ask.Slot) is not { } slot)
        {
            await Settle(unit, new AskSettlement(batch, index, attempt, ask.Slot, ask.Operation, ask.Key, ask.Repeatable, AnswerStatus.Refused, AskRules.NoResult, "slot-dropped", Transient: false)).ConfigureAwait(true);
            return;
        }

        var requested = ask.Timeout ?? AskLimits.DefaultTimeout;
        var timeout = requested < AskLimits.MaxTimeout ? requested : AskLimits.MaxTimeout;
        if (timeout < TimeSpan.Zero)
        {
            timeout = TimeSpan.Zero;
        }

        using var cts = new CancellationTokenSource(timeout, Context.Time);
        AnswerStatus status;
        var result = AskRules.NoResult;
        string? error = null;
        var transient = false;
        try
        {
            var capability = await slots.Resolve(new NeuronId(Id.Brain, unit, "default"), slot, cts.Token).ConfigureAwait(true);
            var invoked = await AskCall.Invoke(slot.Contract, capability, ask.Operation, ask.Arguments, cts.Token).WaitAsync(cts.Token).ConfigureAwait(true);
            if (JsonMarshal.GetRawUtf8Value(invoked).Length > AskLimits.MaxResultBytes)
            {
                status = AnswerStatus.Failed;
                error = "too-large";
            }
            else
            {
                status = AnswerStatus.Ok;
                result = invoked;
            }
        }
        catch (SlotUnboundException)
        {
            status = AnswerStatus.Failed;
            error = "unbound";
        }
        catch (BindingException binding)
        {
            status = AnswerStatus.Failed;
            error = Cap("bad-arguments: " + binding.Message);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            status = ask.Repeatable ? AnswerStatus.Failed : AnswerStatus.Unknown;
            error = "timeout";
            transient = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status = AnswerStatus.Failed;
            error = Cap(ex.Message);
            transient = true;
        }

        await Settle(unit, new AskSettlement(batch, index, attempt, ask.Slot, ask.Operation, ask.Key, ask.Repeatable, status, result, error, transient)).ConfigureAwait(true);
    }

    public async Task Drop()
    {
        if (_state is null)
        {
            return;
        }

        _state = null;
        await Seat.Clear().ConfigureAwait(true);
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
}

internal sealed record AskBatchState(string Unit, IReadOnlyList<Ask> Asks);
