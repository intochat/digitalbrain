using DigitalBrain.Abstractions;
using Orleans.Runtime;

namespace DigitalBrain.Testing;

public static class SimulationCluster
{
    public static IGrainFactory Grains => SimulationClusterHost.Grains;

    public static SynapseObserver Observed => SimulationClusterHost.Observed;

    public static long CompletedJournalWrites(GrainId grain)
        => SimulationClusterHost.CompletedJournalWrites(grain);

    public static void FailJournalWriteAfter(
        GrainId grain,
        int completedWritesBeforeFailure,
        string message)
        => SimulationClusterHost.FailJournalWriteAfter(grain, completedWritesBeforeFailure, message);

    public static void ClearJournalWriteFailure(GrainId grain)
        => SimulationClusterHost.ClearJournalWriteFailure(grain);

    public static Task StartAsync() => SimulationClusterHost.EnsureStartedAsync();

    public static Task RestartHostOfAsync(NeuronId neuron)
        => SimulationClusterHost.RestartHostOfAsync(neuron);

    public static Task StopAsync() => SimulationClusterHost.StopAsync();

    internal static string LabelOf(string siloName) => SimulationClusterHost.LabelOf(siloName);

    public static void AddJsonSerializer(Func<Type, bool> isSupported)
        => SimulationClusterHost.AddJsonSerializer(isSupported);
}
