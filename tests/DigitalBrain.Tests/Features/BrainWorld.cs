using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Testing;

namespace DigitalBrain.Tests;

public sealed class BrainWorld
{
    public JournalFaultPlan JournalFaults { get; } = new();

    public Dictionary<string, NeuronId> Fixtures { get; } = new(StringComparer.Ordinal);

    internal ScriptedChatClient Scripted { get; } = new();

    public BrainSimulation? Simulation { get; set; }

    public BrainSimulation Brain => Simulation ?? throw new InvalidOperationException("Given a running brain first.");
}
