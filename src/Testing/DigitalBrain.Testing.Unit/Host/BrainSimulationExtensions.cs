using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans;

namespace DigitalBrain.Testing.Unit;

public static class BrainSimulationExtensions
{
    public static IGrainFactory Grains(this UnitBrain brain) => Requires(brain).Grains;

    public static IClusterClient Cluster(this UnitBrain brain) => (IClusterClient)Requires(brain).Grains;

    public static IDigitalBrain Client(this UnitBrain brain) => Requires(brain).Client;

    public static Task DeactivateAsync(this UnitBrain brain, INeuron neuron, CancellationToken cancellationToken = default)
        => Requires(brain).DeactivateAsync(neuron, cancellationToken);

    public static Task RestartSiloAsync(this UnitBrain brain, CancellationToken cancellationToken = default)
        => Requires(brain).RestartSiloAsync(cancellationToken);

    private static UnitBrain Requires(IDigitalBrain brain)
        => brain as UnitBrain ?? throw new InvalidOperationException("This operation requires a brain started by UnitTest.");
}