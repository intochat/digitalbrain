using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Behaviors.Runtime;

internal sealed partial class BehaviorNeuron
{
    public async Task<BoundBehaviorActivationResult> ActivateBound(ActivateBoundBehavior command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Binding);
        ValidateCommand(command.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ArtifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Binding.ContractVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Binding.CaseId);

        var data = LoadOrEmpty();
        var binding = command.Binding;
        var behaviorId = BehaviorIdOfName();

        if (!data.ActivationGateOpen || data.RunState is BehaviorRunState.Stopping or BehaviorRunState.Stopped)
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' activation gate is closed; bound activations are refused.");
        }

        if (!string.Equals(data.ActiveArtifactHash, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no active revision '{command.ArtifactHash}' to bind.");
        }

        if (binding.BehaviorId != behaviorId
            || !string.Equals(binding.Revision.Value, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Behavior binding does not identify active behavior '{behaviorId}' at '{command.ArtifactHash}'.");
        }

        if (binding.TaskId.Owner != Id.Owner
            || binding.WorkerId.Owner != Id.Owner
            || !string.Equals(
                binding.TaskId.Type,
                NeuronId.GrainTypeNameOf(typeof(ITask)),
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                binding.WorkerId.Type,
                NeuronId.GrainTypeNameOf(typeof(IWorker)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Behavior activation requires an existing owner-scoped Task and Worker.");
        }

        if (data.ActiveArtifactBytes is null)
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no signed active artifact bytes for '{command.ArtifactHash}'.");
        }

        var envelope = CanonicalArtifactReader.Read(data.ActiveArtifactBytes);
        var contract = envelope.Manifest.EntryPoints.Contract;
        if (contract.ContractMajorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            != binding.ContractVersion)
        {
            throw new InvalidOperationException(
                $"Behavior binding contract version '{binding.ContractVersion}' does not match signed contract '{contract.ContractMajorVersion}'.");
        }

        var signedCase = contract.Cases.FirstOrDefault(
            item => string.Equals(item.CaseId, binding.CaseId, StringComparison.Ordinal));
        if (signedCase is null)
        {
            throw new InvalidOperationException(
                $"Behavior binding case '{binding.CaseId}' is not present on the active signed contract.");
        }

        var bindingId = BindingIdFor(binding);
        var registered = data.RegisteredBindings ?? [];
        var existing = registered.FirstOrDefault(
            item => string.Equals(item.BindingId, bindingId, StringComparison.Ordinal));
        if (existing is not null && !existing.Enabled)
        {
            throw new InvalidOperationException(
                $"Behavior binding '{bindingId}' is disabled; enable it before activation.");
        }

        var capabilities = DeriveResultBearingEdges(Id.Owner, envelope.Manifest.CapabilityGrants);
        var activation = new BehaviorTaskActivation(
            binding.BehaviorId,
            binding.Revision,
            binding.ContractVersion,
            binding.CaseId,
            binding.ProtectedPayload,
            signedCase.CaseName,
            capabilities);
        var goal = new BehaviorActivationGoal(
            binding.BehaviorId,
            binding.Revision,
            binding.ContractVersion,
            binding.CaseId,
            binding.ProtectedPayload,
            signedCase.CaseName,
            capabilities);
        var snapshot = await GrainFactory
            .GetGrain<ITask>(binding.TaskId.ToGrainId())
            .Start(new StartTask(
                command.CommandId,
                goal,
                binding.WorkerId,
                new TaskPolicy(1, TimeSpan.Zero, null),
                Activation: activation));

        if (snapshot.Worker != binding.WorkerId || snapshot.Activation != activation)
        {
            throw new InvalidOperationException(
                $"Task '{binding.TaskId}' is already bound to a different activation.");
        }

        var nextBindings = new List<BehaviorRegisteredBinding>(registered);
        nextBindings.RemoveAll(item => string.Equals(item.BindingId, bindingId, StringComparison.Ordinal));
        nextBindings.Add(new BehaviorRegisteredBinding(
            bindingId,
            SourceModule: binding.TaskId.Type,
            SourceSynapse: nameof(ActivateBoundBehavior),
            TargetCase: binding.CaseId,
            ContractVersion: binding.ContractVersion,
            Enabled: true,
            ConfigurationHint: "opaque"));

        var trackedTasks = new List<NeuronId>(data.ActiveTaskIds);
        if (!trackedTasks.Contains(binding.TaskId))
        {
            trackedTasks.Add(binding.TaskId);
        }

        data = data with
        {
            ActiveTaskIds = trackedTasks,
            RegisteredBindings = nextBindings,
        };
        await SaveAsync(data);

        return new BoundBehaviorActivationResult(
            binding.TaskId,
            snapshot.State,
            snapshot.ActiveAttempt,
            snapshot.Activation);
    }

    public async Task<BehaviorSnapshot> SetBindingEnabled(SetBehaviorBindingEnabled command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.BindingId);

        var data = LoadOrEmpty();
        if (TryReceipt(data, command.CommandId, out var received))
        {
            return received;
        }

        var bindings = new List<BehaviorRegisteredBinding>(data.RegisteredBindings ?? []);
        var index = bindings.FindIndex(
            item => string.Equals(item.BindingId, command.BindingId, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no registered binding '{command.BindingId}'.");
        }

        bindings[index] = bindings[index] with { Enabled = command.Enabled };
        data = data with { RegisteredBindings = bindings };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);
        return Snapshot(data);
    }

    private static string BindingIdFor(BehaviorActivationBinding binding)
        => $"{binding.TaskId.Name}__{binding.CaseId}";

    public async Task<BehaviorSnapshot> StopRun(StopBehavior command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);

        var data = LoadOrEmpty();
        if (TryReceipt(data, command.CommandId, out var received))
        {
            return received;
        }

        if (data.Status != BehaviorRevisionStatus.Active || data.ActiveArtifactHash is null)
        {
            throw new InvalidOperationException($"Behavior '{Id}' has no active revision to stop.");
        }

        if (data.RunState == BehaviorRunState.Stopped && !data.ActivationGateOpen)
        {
            data = WithReceipt(data, command.CommandId, Snapshot(data));
            await SaveAsync(data);
            return Snapshot(data);
        }

        var behaviorId = BehaviorIdOfName();

        data = data with
        {
            ActivationGateOpen = false,
            RunState = BehaviorRunState.Stopping,
        };
        await SaveAsync(data);
        await EmitAsync(new BehaviorActivationGateClosed(command.CommandId, behaviorId));
        await EmitAsync(new BehaviorStopping(command.CommandId, behaviorId));

        var remaining = new List<NeuronId>();
        foreach (var taskId in data.ActiveTaskIds)
        {
            var task = GrainFactory.GetGrain<ITask>(taskId.ToGrainId());
            TaskSnapshot snapshot;
            try
            {
                snapshot = await task.Read();
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (IsTaskSettledForStop(snapshot))
            {
                continue;
            }

            await EmitAsync(new BehaviorTaskCancelRequested(command.CommandId, behaviorId, taskId));
            try
            {
                snapshot = await task.Cancel(new CancelTask(CommandId.New(), snapshot.Revision));
            }
            catch (InvalidOperationException)
            {
                snapshot = await task.Read();
            }

            if (!IsTaskSettledForStop(snapshot))
            {
                remaining.Add(taskId);
            }
        }

        data = data with
        {
            ActiveTaskIds = remaining,
            RunState = remaining.Count == 0 ? BehaviorRunState.Stopped : BehaviorRunState.Stopping,
            ActivationGateOpen = false,
        };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);

        if (data.RunState == BehaviorRunState.Stopped)
        {
            UnpublishExactCapability();
            await EmitAsync(new BehaviorStopped(command.CommandId, behaviorId));
        }

        return Snapshot(data);
    }

    public async Task<BehaviorSnapshot> StartRun(StartBehavior command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);

        var data = LoadOrEmpty();
        if (TryReceipt(data, command.CommandId, out var received))
        {
            return received;
        }

        if (data.Status != BehaviorRevisionStatus.Active || data.ActiveArtifactHash is null)
        {
            throw new InvalidOperationException($"Behavior '{Id}' has no active revision to start.");
        }

        if (data.RunState is not (BehaviorRunState.Stopped or BehaviorRunState.Stopping))
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' can only start from Stopped/Stopping (current {data.RunState}).");
        }

        data = data with
        {
            RunState = BehaviorRunState.Running,
            ActivationGateOpen = true,
        };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);
        PublishExactCapability(data);
        await EmitAsync(new BehaviorStarted(command.CommandId, BehaviorIdOfName()));
        return Snapshot(data);
    }

    private void PublishExactCapability(BehaviorData data)
    {
        if (data.ActiveArtifactHash is null
            || string.IsNullOrWhiteSpace(data.DisplayName)
            || string.IsNullOrWhiteSpace(data.Description))
        {
            return;
        }

        var catalog = ServiceProvider.GetService<ActiveCapabilityCatalog>();
        if (catalog is null)
        {
            return;
        }

        var scenarios = data.Features?.Keys.Order(StringComparer.Ordinal).ToArray()
            ?? Array.Empty<string>();
        catalog.PublishBehavior(new ActiveBehaviorCapability(
            BehaviorIdOfName().Value,
            data.DisplayName,
            data.Description,
            data.ActiveArtifactHash,
            Id.Name,
            neuronContractId: "behaviors.behavior",
            jsonSchema: """{"type":"object","properties":{"triggerTypeName":{"type":"string"},"triggerJson":{"type":"string"}},"required":["triggerTypeName","triggerJson"]}""",
            scenarioTitles: scenarios));
    }

    private void UnpublishExactCapability()
    {
        var catalog = ServiceProvider.GetService<ActiveCapabilityCatalog>();
        catalog?.UnpublishBehavior(BehaviorIdOfName().Value);
    }

    private static bool IsTaskSettledForStop(TaskSnapshot snapshot)
        => snapshot.State is TaskState.Succeeded or TaskState.Failed or TaskState.Cancelled
            || (snapshot.State == TaskState.Waiting && snapshot.Blocker is OutcomeUncertain);

    private static TaskOperationEdge[] DeriveResultBearingEdges(
        OwnerId owner,
        IReadOnlyList<BehaviorCapabilityGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (grants.Count == 0)
        {
            return [];
        }

        var edges = new TaskOperationEdge[grants.Count];
        for (var index = 0; index < grants.Count; index++)
        {
            var grant = grants[index];
            if (string.IsNullOrWhiteSpace(grant.EmittedResultSynapseId)
                || grant.EmittedResultSchemaVersion is null)
            {
                throw new InvalidOperationException("one-way-capability-not-supported");
            }

            edges[index] = new TaskOperationEdge(
                new NeuronId(grant.TargetNeuronContractId, owner, grant.TargetInstanceName),
                grant.AcceptedRequestSynapseId,
                grant.AcceptedRequestSchemaVersion,
                grant.EmittedResultSynapseId,
                grant.EmittedResultSchemaVersion.Value);
        }

        return edges;
    }
}
