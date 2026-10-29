namespace DigitalBrain;

/// <summary>A neuron that exists: it has persisted state. The kernel directory's row.</summary>
/// <param name="Type">The neuron's grain type.</param>
/// <param name="Key">The neuron's key: its brain, or starting with its brain and a slash.</param>
/// <param name="Created">When its state was first written.</param>
/// <param name="Creator">The principal stamped on that first write; null when the system wrote it.</param>
public sealed record BrainInstance(string Type, string Key, DateTimeOffset Created, string? Creator);

/// <summary>Which of the words a <see cref="BrainType"/> is.</summary>
public enum TypeKind { Neuron, Signal, Connector }

/// <summary>One type the kernel knows: a neuron contract, a signal or a connector.</summary>
/// <param name="Name">The full type name (a connector's id).</param>
/// <param name="Alias">The neuron's grain type as instances record it; a signal's or connector's stable alias.</param>
/// <param name="Kind">Which of the words this type is.</param>
/// <param name="Origin">The module id (or feature) that brought the type.</param>
/// <param name="Description">What the type is for.</param>
/// <param name="Shape">A neuron's method signatures, a signal's properties, a connector's neuron and signal names.</param>
/// <param name="Implements">The neuron contracts a neuron contract extends, by full name.</param>
public sealed record BrainType(
    string Name,
    string Alias,
    TypeKind Kind,
    string Origin,
    string Description,
    string[] Shape,
    string[] Implements);
