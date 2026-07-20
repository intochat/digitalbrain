using System.Runtime.CompilerServices;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

internal static class SimulationAssemblyConfiguration
{
    [ModuleInitializer]
    internal static void ConfigureMeaiSerialization()
        => SimulationCluster.AddJsonSerializer(
            type => type == typeof(ChatMessage) || type == typeof(ChatResponse));
}
