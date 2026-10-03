using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Orleans;

namespace DigitalBrain.Testing.Module;

public static class BrainSimulationExtensions
{
    public static IGrainFactory Grains(this ModuleBrain brain) => Requires(brain).Grains;

    public static IClusterClient Cluster(this ModuleBrain brain) => (IClusterClient)Requires(brain).Grains;

    public static IDigitalBrain Client(this ModuleBrain brain) => Requires(brain).Client;

    public static Task DeactivateAsync(this ModuleBrain brain, INeuron neuron, CancellationToken cancellationToken = default)
        => Requires(brain).DeactivateAsync(neuron, cancellationToken);

    public static Task RestartSiloAsync(this ModuleBrain brain, CancellationToken cancellationToken = default)
        => Requires(brain).RestartSiloAsync(cancellationToken);

    private static ModuleBrain Requires(IDigitalBrain brain)
        => brain as ModuleBrain ?? throw new InvalidOperationException("This operation requires a brain started by ModuleTest.");
}
