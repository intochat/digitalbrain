using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Testing;
using Microsoft.Extensions.Time.Testing;

namespace DigitalBrain.Tests;

public sealed class BrainWorld
{
    public FakeTimeProvider Clock { get; } = new();

    public JournalFaultPlan JournalFaults { get; } = new();

    public Dictionary<string, NeuronId> Fixtures { get; } = new(StringComparer.Ordinal);

    public BrainSimulation? Simulation { get; set; }

    public BrainSimulation Brain => Simulation ?? throw new InvalidOperationException("Given a running brain first.");
}
