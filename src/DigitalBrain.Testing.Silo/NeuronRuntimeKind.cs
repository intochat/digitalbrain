using Xunit;

namespace DigitalBrain.Testing;

// The two runtimes a migrated plain neuron must behave the same on (R4-05).
public enum NeuronRuntimeKind
{
    // DigitalBrain.Testing TestOS: in-memory, a controlled clock.
    TestOS,

    // DigitalBrain.Runtime.Orleans on an in-process local silo (SiloHost.Local), real time and the runtime BrainLog.
    Silo,
}

public static class NeuronRuntimeKinds
{
    // [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public static TheoryData<NeuronRuntimeKind> Both => new(NeuronRuntimeKind.TestOS, NeuronRuntimeKind.Silo);
}
