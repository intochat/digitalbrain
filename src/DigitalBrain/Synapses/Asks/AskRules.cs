using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DigitalBrain;

internal static class AskRules
{
    internal const string AsksNeuronType = "unit.asks";
    internal const string BatchNeuronType = "unit.ask.batch";
    internal const string AnsweredAlias = "unit.answered";

    /// <summary>The JSON null element: the Result of every answer that has no result. Never default(JsonElement), which cannot be journaled.</summary>
    internal static readonly JsonElement NoResult = JsonSerializer.SerializeToElement<object?>(null);

    /// <summary>The asks neuron of the unit whose state neuron is <paramref name="state"/>: {brain}/unit.asks/{alias}.</summary>
    internal static NeuronId AsksOf(NeuronId state) => new(state.Brain, AsksNeuronType, state.Type);

    /// <summary>The store of one batch: {brain}/unit.ask.batch/{alias}~{batch}.</summary>
    internal static NeuronId BatchOf(NeuronId asks, string batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return new(asks.Brain, BatchNeuronType, asks.Key + "~" + batch);
    }

    /// <summary>Returns <paramref name="asks"/> when every ask may be recorded.</summary>
    /// <exception cref="InvalidOperationException">An ask breaks a rule; the message names the first one.</exception>
    internal static IReadOnlyList<Ask> Checked(IReadOnlyList<Ask> asks, IReadOnlyList<SynapseDescriptor> synapses)
    {
        ArgumentNullException.ThrowIfNull(asks);
        ArgumentNullException.ThrowIfNull(synapses);

        if (asks.Count > AskLimits.MaxAsksPerReaction)
        {
            throw new InvalidOperationException($"{asks.Count} asks exceed {AskLimits.MaxAsksPerReaction} per reaction");
        }

        var keys = new HashSet<string>(asks.Count, StringComparer.Ordinal);
        long batchBytes = 0;

        foreach (var ask in asks)
        {
            if (ask is null)
            {
                throw new InvalidOperationException("an ask is null");
            }

            if (string.IsNullOrEmpty(ask.Key))
            {
                throw new InvalidOperationException("an ask has no key");
            }

            if (ask.Key.Length > AskLimits.MaxKeyLength)
            {
                throw new InvalidOperationException($"an ask key is longer than {AskLimits.MaxKeyLength} characters");
            }

            if (string.IsNullOrEmpty(ask.Operation))
            {
                throw new InvalidOperationException($"ask '{ask.Key}' names no operation");
            }

            if (!HasDeferredSlot(synapses, ask.Slot))
            {
                throw new InvalidOperationException($"ask '{ask.Key}' names slot '{ask.Slot}', which the unit does not declare as an Asks<> slot");
            }

            if (ask.Arguments.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"ask '{ask.Key}': arguments must be a JSON object");
            }

            var argBytes = JsonMarshal.GetRawUtf8Value(ask.Arguments).Length;
            if (argBytes > AskLimits.MaxArgumentsBytes)
            {
                throw new InvalidOperationException($"ask '{ask.Key}': arguments of {argBytes} bytes exceed {AskLimits.MaxArgumentsBytes}");
            }

            if (ask.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"ask '{ask.Key}': the timeout must be positive");
            }

            if (!keys.Add(ask.Key))
            {
                throw new InvalidOperationException($"two asks share the key '{ask.Key}'");
            }

            batchBytes += Encoding.UTF8.GetByteCount(ask.Slot)
                + Encoding.UTF8.GetByteCount(ask.Operation)
                + Encoding.UTF8.GetByteCount(ask.Key)
                + argBytes;
        }

        if (batchBytes > AskLimits.MaxBatchBytes)
        {
            throw new InvalidOperationException($"asks of {batchBytes} bytes exceed {AskLimits.MaxBatchBytes} per reaction");
        }

        return asks;
    }

    private static bool HasDeferredSlot(IReadOnlyList<SynapseDescriptor> synapses, string? slotName)
    {
        for (var i = 0; i < synapses.Count; i++)
        {
            var slots = synapses[i].Slots;
            for (var j = 0; j < slots.Count; j++)
            {
                var slot = slots[j];
                if (slot.Deferred && string.Equals(slot.Name, slotName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
