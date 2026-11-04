namespace DigitalBrain.Time;

/// <summary>An owner registered a schedule with the time neuron. Durable.</summary>
/// <param name="Owner">Who registered.</param>
/// <param name="Schedule">The schedule text.</param>
[Signal("time.registered")]
public sealed record Registered(string Owner, string Schedule) : Signal;

/// <summary>An owner's schedule registration was removed. Durable.</summary>
/// <param name="Owner">Whose registration.</param>
/// <param name="Schedule">The schedule text.</param>
[Signal("time.unregistered")]
public sealed record Unregistered(string Owner, string Schedule) : Signal;
