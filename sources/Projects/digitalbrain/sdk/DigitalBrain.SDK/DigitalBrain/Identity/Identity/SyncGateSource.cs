using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.SDK.DigitalBrain.Identity.Identity;

public sealed class SyncGateSource : IInterpretedNeuronSource
{
    public const string SyncGateFqn = "DigitalBrain.SDK.Identity.GlobalBrainSyncGateNeuron";

    public Task<IReadOnlyList<InterpretedNeuronRegistration>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var descriptor = new NeuronDescriptor(
            SyncGateFqn,
            Incoming: Array.Empty<IncomingPort>(),
            Outgoing: Array.Empty<string>(),
            InoLangSource: "// Native sync gate neuron"
        );

        var registration = new InterpretedNeuronRegistration(
            descriptor,
            new[] { UserBrainSpawned.Fqn }
        );

        IReadOnlyList<InterpretedNeuronRegistration> results = new[] { registration };
        return Task.FromResult(results);
    }
}
