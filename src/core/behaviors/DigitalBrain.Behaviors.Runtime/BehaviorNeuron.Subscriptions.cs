using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
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

        await PublishSubscriptionsAsync(data, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

        await DispatchWakeAsync(synapse, data, manifest, subscribedCase, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // Running the program here would hold this neuron's turn across the host call, and the first
    // thing authored code does with context.EmitAsync is call back into EmitFact on this same
    // grain — a reentrancy deadlock. The wake therefore only starts the attempt: the task rail
    // carries the execution on the Worker's and the relay's turns, long after this one ends, and
    // it is also where the Task and Attempt identity the emit broker demands actually comes from.
    private async Task DispatchWakeAsync(
        Synapse fact,
        BehaviorData data,
        BehaviorDefinitionManifest manifest,
        BehaviorContractCaseManifest subscribedCase,
        CancellationToken cancellationToken)
    {
        var behaviorId = BehaviorIdOfName();
        var revision = new BehaviorRevisionId(data.ActiveArtifactHash!);
        var command = WakeCommandId(behaviorId);
        var attemptName = $"wake-{command.Value:N}";
        var task = NeuronId.For<ITask>(Id.Owner, attemptName);
        var worker = NeuronId.For<IWorker>(Id.Owner, attemptName);

        var triggers = ServiceProvider.GetRequiredService<IBehaviorProtectedTriggerAccess>();
        var trigger = await triggers.StoreAsync(
            Id.Owner,
            task,
            behaviorId,
            revision,
            subscribedCase.CaseId,
            BehaviorPayloadJson.Serialize(fact, fact.GetType()),
            cancellationToken).ConfigureAwait(true);

        var capabilities = DeriveResultBearingEdges(Id.Owner, manifest.CapabilityGrants);
        var contractVersion = manifest.EntryPoints.Contract.ContractMajorVersion
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var activation = new BehaviorTaskActivation(
            behaviorId,
            revision,
            contractVersion,
            subscribedCase.CaseId,
            trigger,
            subscribedCase.CaseName,
            capabilities);
        var goal = new BehaviorActivationGoal(
            behaviorId,
            revision,
            contractVersion,
            subscribedCase.CaseId,
            trigger,
            subscribedCase.CaseName,
            capabilities)
        {
            HopsRemaining = InheritedHops(),
        };

        var snapshot = await GrainFactory
            .GetGrain<ITask>(task.ToGrainId())
            .Start(new StartTask(
                command,
                goal,
                worker,
                new TaskPolicy(1, TimeSpan.Zero, null),
                Activation: activation)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await SaveAsync(data with { ActiveTaskIds = TrackWakeTask(data.ActiveTaskIds, task) }).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await EmitAsync(new BehaviorWokeOnFact(
            command,
            behaviorId,
            data.ActiveArtifactHash!,
            task,
            snapshot.ActiveAttempt ?? default)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // Every wake starts its own one-shot attempt, so tracking them without a bound would grow
    // durable state forever and make StopRun read one Task grain per fact ever heard. StopRun
    // prunes settled entries; this bound keeps the newest window between stops, and an evicted
    // attempt is one StopRun no longer cancels rather than one that keeps running unnoticed.
    private static List<NeuronId> TrackWakeTask(IReadOnlyList<NeuronId> tracked, NeuronId task)
    {
        var next = new List<NeuronId>(tracked);
        if (next.Contains(task))
        {
            return next;
        }

        next.Add(task);
        while (next.Count > TrackedWakeTasks)
        {
            next.RemoveAt(0);
        }

        return next;
    }

    private const int TrackedWakeTasks = 64;

    // The delivery's own hop count is the budget it has already spent, so a woken program starts
    // from what is left rather than from a fresh ceiling.
    private int InheritedHops()
        => Math.Clamp(
            BehaviorFactEmission.MaximumHops - CurrentDeliveryDepth,
            0,
            BehaviorFactEmission.MaximumHops);

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

        // A retried request must not speak the fact twice; the receipt is the only thing that
        // can tell a retry apart from a fresh command once the transport has handed it over.
        // Only a spoken fact is receipted — see RefuseEmitAsync for why refusals are not.
        if (data.EmitReceipts.TryGetValue(command.CommandId.Value, out var receipted))
        {
            return receipted;
        }

        // One correlation ties the spoken fact to its audit record and to any refusal, instead of
        // each emission resolving its own the moment no entry scope supplies one.
        var correlation = ResolveEmissionCorrelation();

        if (command.HopsRemaining <= 0)
        {
            return await RefuseEmitAsync(
                command,
                behaviorId,
                correlation,
                BehaviorFactEmission.HopBudgetExhausted).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (data.ActiveArtifactHash is null
            || data.ActiveArtifactBytes is null
            || !data.ActivationGateOpen
            || data.RunState is not BehaviorRunState.Running)
        {
            return await RefuseEmitAsync(command, behaviorId, correlation, BehaviorFactEmission.NotRunning).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        // The signed manifest is the grant. Absent BroadcastEmitAliases means no emit rights.
        var manifest = CanonicalArtifactReader.Read(data.ActiveArtifactBytes).Manifest;
        if (manifest.EntryPoints.BroadcastEmitAliases is not { } granted
            || !granted.Contains(command.EmitAlias, StringComparer.Ordinal))
        {
            return await RefuseEmitAsync(command, behaviorId, correlation, BehaviorFactEmission.UndeclaredAlias).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (!TryReifyFact(command.EmitAlias, command.PayloadJson, out var fact))
        {
            return await RefuseEmitAsync(command, behaviorId, correlation, BehaviorFactEmission.UnknownSynapse).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        // The receipt is written only once the fact is durably spoken. EmitFact runs with no
        // delivery turn, so there is no checkpoint to roll a failed emission back: a receipt
        // written first survives a throw from the subscriber lookup and answers every retry
        // "emitted" for a fact that was never spoken. Ordered this way the failure mode is a
        // duplicate on a crash between the emission and the receipt, which the rail already
        // tolerates, instead of silent loss under a claim that it succeeded.
        // The spoken fact carries the budget it was charged as its delivery depth, so the next
        // behavior woken by it inherits what is left instead of a fresh ceiling.
        // A behavior emitting on a full budget has spent nothing, but the fact it speaks is still
        // one delivery deep, so the floor is one rather than the zero the arithmetic gives.
        await EmitAtDepthAsync(
            fact,
            correlation,
            Math.Max(BehaviorFactEmission.MaximumHops - command.HopsRemaining, 1)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await EmitAsync(
            new BehaviorFactEmitted(
                command.CommandId,
                behaviorId,
                data.ActiveArtifactHash,
                command.EmitAlias),
            correlation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SaveAsync(WithEmitReceipt(data, command.CommandId, BehaviorFactEmission.Emitted)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return BehaviorFactEmission.Emitted;
    }

    // No refusal is receipted, because none of the four reasons is terminal for a request the
    // command identity can distinguish. The hop budget is not part of that identity at all; the
    // run state changes; the granted aliases change with the active revision; and the reifiable
    // synapses change with the deployed module set. Receipting any of them answers a later,
    // healthy retry from a condition that has since gone away. A refusal spoke nothing, so
    // re-evaluating it costs at worst a repeated journal record and can never suppress a
    // legitimate emission — only a spoken fact is terminal, and only that is receipted.
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
            correlation).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

    private async Task PublishSubscriptionsAsync(BehaviorData data, CancellationToken cancellationToken)
    {
        var aliases = data is { ActivationGateOpen: true, RunState: BehaviorRunState.Running, ActiveArtifactBytes: not null }
            ? CanonicalArtifactReader.Read(data.ActiveArtifactBytes).Manifest.EntryPoints.EventAliases
            : [];
        var registry = GrainFactory.GetGrain<IBehaviorSubscriptionRegistry>(
            BehaviorSubscriptionRegistry.ForOwner(Id.Owner).ToGrainId());

        await BehaviorSubscriptionRegistry.WithinBoundAsync(
            token => registry.Replace(Id.Name, aliases, token),
            nameof(IBehaviorSubscriptionRegistry.Replace),
            DeliveryPolicy.SubscriptionRegistryTimeout,
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}
