using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Behaviors.Runtime;

internal sealed partial class BehaviorNeuron
{
    // Activation state is durable but the subscription registry is a separate grain, so a
    // registry that lost or never received this behavior's aliases leaves it silently deaf.
    // Republishing from the signed artifact on every activation repairs that divergence.
    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var data = LoadOrEmpty();
        if (data.ActiveArtifactHash is null)
        {
            return;
        }

        await PublishSubscriptionsAsync(data);
    }

    protected override async Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        // A behavior that declares the same fact on both sides would execute on its own emission.
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
            WakeCommandId(behaviorId),
            behaviorId,
            data.ActiveArtifactHash,
            outcome.Outcome));
    }

    // A wake has no command, so the triggering delivery is its identity: replaying the same
    // fact reproduces the same command id instead of minting a fresh one per attempt.
    private CommandId WakeCommandId(BehaviorId behavior)
    {
        if (CurrentDeliverySynapseId is not { } delivery)
        {
            return CommandId.New();
        }

        var material = Encoding.UTF8.GetBytes($"{Id.Owner.Value}|{behavior.Value}|{delivery.Value}");
        return new CommandId(new Guid(System.Security.Cryptography.SHA256.HashData(material).AsSpan(0, 16)));
    }

    public async Task<string> EmitFact(EmitBehaviorFact command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.EmitAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.PayloadJson);

        var data = LoadOrEmpty();
        var behaviorId = BehaviorIdOfName();

        // One correlation ties the spoken fact to its audit record and to any refusal, instead of
        // each emission resolving its own the moment no entry scope supplies one.
        var correlation = ResolveEmissionCorrelation();

        if (data.ActiveArtifactHash is null
            || data.ActiveArtifactBytes is null
            || !data.ActivationGateOpen
            || data.RunState is not BehaviorRunState.Running)
        {
            return await RefuseEmitAsync(command, behaviorId, correlation, BehaviorFactEmission.NotRunning);
        }

        // The signed manifest is the grant. Absent BroadcastEmitAliases means no emit rights.
        var manifest = CanonicalArtifactReader.Read(data.ActiveArtifactBytes).Manifest;
        if (manifest.EntryPoints.BroadcastEmitAliases is not { } granted
            || !granted.Contains(command.EmitAlias, StringComparer.Ordinal))
        {
            return await RefuseEmitAsync(command, behaviorId, correlation, BehaviorFactEmission.UndeclaredAlias);
        }

        if (!TryReifyFact(command.EmitAlias, command.PayloadJson, out var fact))
        {
            return await RefuseEmitAsync(command, behaviorId, correlation, BehaviorFactEmission.UnknownSynapse);
        }

        await EmitAsync(fact, correlation);
        await EmitAsync(
            new BehaviorFactEmitted(
                command.CommandId,
                behaviorId,
                data.ActiveArtifactHash,
                command.EmitAlias),
            correlation);
        return BehaviorFactEmission.Emitted;
    }

    private async Task<string> RefuseEmitAsync(
        EmitBehaviorFact command,
        BehaviorId behaviorId,
        CorrelationId correlation,
        string reason)
    {
        await EmitAsync(
            new BehaviorFactEmitRefused(
                command.CommandId,
                behaviorId,
                command.EmitAlias,
                reason),
            correlation);
        return reason;
    }

    private bool TryReifyFact(string emitAlias, string payloadJson, out Synapse fact)
    {
        fact = null!;
        var catalog = ServiceProvider.GetService<ActiveCapabilityCatalog>();
        var typeMap = ServiceProvider.GetService<ActiveModuleContractTypeMap>();
        if (catalog is null || typeMap is null)
        {
            return false;
        }

        var declared = catalog.Modules
            .SelectMany(static module => module.Neurons)
            .SelectMany(static neuron => neuron.Emitted)
            .Where(synapse => string.Equals(synapse.ContractId, emitAlias, StringComparison.Ordinal))
            .Select(static synapse => synapse.SchemaVersion)
            .Distinct()
            .ToArray();

        if (declared.Length != 1
            || !typeMap.TryGetSynapseType(emitAlias, declared[0], out var type)
            || type is null)
        {
            return false;
        }

        return BehaviorPayloadJson.Deserialize(Encoding.UTF8.GetBytes(payloadJson), type) is Synapse reified
            && (fact = reified) is not null;
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
