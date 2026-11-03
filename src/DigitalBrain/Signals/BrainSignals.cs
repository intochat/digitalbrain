using DigitalBrain.Brains;
using DigitalBrain.Spi;

namespace DigitalBrain.Signals;

/// <summary>
/// The first signal in a brain, published exactly once by <see cref="IBrain.Create"/>.
/// </summary>
/// <remarks>
/// Handlers provision what a new brain is born with. <see cref="IBrain.Create"/> awaits them, so a
/// brain is complete before its creator sees it. One-time work belongs here; catch-up work belongs
/// on <see cref="Activated"/>.
/// </remarks>
/// <param name="Owner">The account the brain was created for.</param>
[Signal("brain.created")]
public sealed record Created(Owner Owner) : Signal;

/// <summary>
/// Published on every wake of a created brain, including right after <see cref="Created"/>.
/// </summary>
/// <remarks>
/// This is the signal the operating system boots on and any program may subscribe to. Because it
/// fires on every activation, handlers are idempotent: they catch up, they do not initialize.
/// </remarks>
/// <param name="Owner">The account the brain belongs to.</param>
[Signal("brain.activated")]
public sealed record Activated(Owner Owner) : Signal;

/// <summary>A neuron of this brain wrote its state for the first time. Published by the brain's <see cref="IBrain"/>.</summary>
/// <param name="Type">The neuron's grain type.</param>
/// <param name="Key">The neuron's key.</param>
[Signal("kernel.instance-created")]
public sealed record InstanceCreated(string Type, string Key) : Signal;

/// <summary>A neuron of this brain stopped existing. Published by the brain's <see cref="IBrain"/>.</summary>
/// <param name="Type">The neuron's grain type.</param>
/// <param name="Key">The neuron's key.</param>
[Signal("kernel.instance-retired")]
public sealed record InstanceRetired(string Type, string Key) : Signal;

/// <summary>A feature loaded into this brain brought a type the brain did not know. Published by the brain's <see cref="IBrain"/>.</summary>
/// <param name="Name">The type's full name, as <see cref="IBrainObserver.Types"/> lists it.</param>
[Signal("kernel.type-added")]
public sealed record TypeAdded(string Name) : Signal;
