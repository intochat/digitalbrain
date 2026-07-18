using Ino.Core;

namespace Ino.Kernel;

/// <summary>
/// User-initiated free-text intent. The gateway wraps every inbound chat message in a
/// ChatIntent and fires it at <see cref="CortexNeuron"/>, which pattern-matches on the
/// text and re-fires as a concrete neuron synapse (FindFlightsRequest etc.) or
/// emits <see cref="UnroutedIntent"/> when nothing routes. Slice 5 wires the gateway
/// to fire this; slice 15 swaps the LLM-free router for a real model.
/// </summary>
[GenerateSerializer]
public sealed record ChatIntent(
    [property: Id(0)] string Text,
    [property: Id(1)] string UserId) : ISynapse;
