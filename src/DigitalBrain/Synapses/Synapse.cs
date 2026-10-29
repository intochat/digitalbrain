namespace DigitalBrain;

/// <summary>A durable route: signals of type <paramref name="Signal"/> published by <paramref name="From"/> reach <paramref name="To"/>.</summary>
public sealed record Synapse(NeuronId From, Type Signal, NeuronId To);

/// <summary>One durable synapse: signals of <paramref name="Signal"/> published by <paramref name="From"/> reach <paramref name="To"/>.</summary>
/// <param name="From">The publishing neuron, <c>brain/type/key</c>.</param>
/// <param name="To">The subscribing neuron, <c>brain/type/key</c>.</param>
/// <param name="Signal">The <c>[Signal]</c> alias.</param>
public sealed record ObservedSynapse(string From, string To, string Signal);

/// <summary>A signal handed to a subscriber along a synapse.</summary>
/// <param name="From">The publishing neuron, <c>brain/type/key</c>.</param>
/// <param name="To">The subscribing neuron, <c>brain/type/key</c>.</param>
/// <param name="Signal">The <c>[Signal]</c> alias.</param>
/// <param name="Sequence">The publisher sequence of the signal.</param>
public sealed record SignalDelivery(string From, string To, string Signal, long Sequence);
