using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Kernel;

namespace DigitalBrain.Behaviors.Runtime;

internal sealed partial class BehaviorNeuron
{
    protected override async Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        // A behavior that subscribes to a fact it emits itself would wake itself without bound.
        if (CurrentDeliveryCaller == Id)
        {
            return;
        }

        var data = LoadOrEmpty();
        if (data.ActiveArtifactHash is null
            || data.ActiveArtifactBytes is null
            || !data.ActivationGateOpen
            || data.RunState is not BehaviorRunState.Running)
        {
            return;
        }

        // The signed manifest — not the registry — is what authorizes a wake.
        var manifest = CanonicalArtifactReader.Read(data.ActiveArtifactBytes).Manifest;
        if (SynapseAlias.Of(synapse.GetType()) is not { } alias
            || !manifest.EntryPoints.EventAliases.Contains(alias, StringComparer.Ordinal)
            || SubscribedCaseOf(manifest) is not { } subscribedCase)
        {
            return;
        }

        var behaviorId = BehaviorIdOfName();
        var outcome = await _executor.ExecuteLegacyAsync(
            new LegacyBehaviorExecutionRequest(
                new BehaviorExecutionMetadata(
                    Id.Owner,
                    behaviorId,
                    new BehaviorRevisionId(data.ActiveArtifactHash),
                    BehaviorExecutionId.New()),
                ReadOnlyMemory<byte>.Empty,
                data.ActiveArtifactHash,
                subscribedCase.CaseName,
                Encoding.UTF8.GetString(BehaviorPayloadJson.Serialize(synapse, synapse.GetType())),
                new GrainBehaviorCapabilityResolver(GrainFactory, Id.Owner),
                TimeProvider),
            cancellationToken);

        await SaveAsync(data with { LastExecutionOutcome = outcome.Outcome });
        await EmitAsync(new BehaviorExecuted(
            CommandId.New(),
            behaviorId,
            data.ActiveArtifactHash,
            outcome.Outcome));
    }

    private static BehaviorContractCaseManifest? SubscribedCaseOf(BehaviorDefinitionManifest manifest)
        => manifest.EntryPoints.Contract.Cases.Count == 1
            ? manifest.EntryPoints.Contract.Cases[0]
            : null;

    private async Task PublishSubscriptionsAsync(BehaviorData data)
    {
        var aliases = data is { ActivationGateOpen: true, RunState: BehaviorRunState.Running, ActiveArtifactBytes: not null }
            ? CanonicalArtifactReader.Read(data.ActiveArtifactBytes).Manifest.EntryPoints.EventAliases
            : [];

        await GrainFactory
            .GetGrain<IBehaviorSubscriptionRegistry>(
                BehaviorSubscriptionRegistry.ForOwner(Id.Owner).ToGrainId())
            .Replace(Id.Name, aliases, CancellationToken.None);
    }
}
