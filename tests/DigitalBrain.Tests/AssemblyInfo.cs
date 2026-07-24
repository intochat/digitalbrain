using System.Runtime.CompilerServices;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;

internal static class TestingAssemblyConfiguration
{
    [ModuleInitializer]
    internal static void ConfigureMeaiSerialization()
        => SimulationCluster.AddJsonSerializer(
            type => type == typeof(ChatMessage) || type == typeof(ChatResponse));
}
