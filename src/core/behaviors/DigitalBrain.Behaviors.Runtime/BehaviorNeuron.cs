using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime.Artifacts;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Behaviors;

[GrainType("behaviorneuron")]
internal sealed partial class BehaviorNeuron :
    Neuron,
    IBehaviorNeuron,
    IHandle<BehaviorRevisionApproval>
{
    private const string StateName = "behaviors.behavior";
    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<BehaviorData> _states;
    private readonly IBehaviorCompiler _compiler;
    private readonly IBehaviorBddGate _bddGate;
    private readonly IBehaviorExecutor _executor;

    public BehaviorNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<BehaviorData>>();
        _compiler = ServiceProvider.GetRequiredService<IBehaviorCompiler>();
        _bddGate = ServiceProvider.GetRequiredService<IBehaviorBddGate>();
        _executor = ServiceProvider.GetRequiredService<IBehaviorExecutor>();
    }

    public Task<BehaviorSnapshot> Read() => Task.FromResult(Snapshot(LoadOrEmpty()));

    public async Task<BehaviorSnapshot> Propose(ProposeBehaviorRevision command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ProgramSource);
        ArgumentNullException.ThrowIfNull(command.Features);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Description);

        var behaviorId = BehaviorIdOfName();
        var data = LoadOrEmpty();
        if (TryReceipt(data, command.CommandId, out var received))
        {
            return received;
        }

        var activeHash = data.ActiveArtifactHash;
        var priorHash = data.PriorArtifactHash;
        var compile = _compiler.Compile(command.ProgramSource, behaviorId);
        if (!compile.Succeeded)
        {
            data = data with
            {
                Status = BehaviorRevisionStatus.CompileFailed,
                ProposedArtifactHash = null,
                LastCompileFailure = compile.Diagnostics,
                TestsPassed = false,
                IsApproved = false,
                ProgramSource = command.ProgramSource,
                Features = command.Features.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
                DisplayName = command.DisplayName,
                Description = command.Description,
                ActiveArtifactHash = activeHash,
                PriorArtifactHash = priorHash,
            };
            data = WithReceipt(data, command.CommandId, Snapshot(data));
            await SaveAsync(data);
            await EmitAsync(new BehaviorCompileFailed(command.CommandId, behaviorId, compile.Diagnostics));
            return Snapshot(data);
        }

        var envelope = new BehaviorArtifactEnvelope(
            new BehaviorDefinitionManifest(
                behaviorId,
                command.DisplayName,
                command.Description,
                new BehaviorEntryPoints([], []),
                [],
                new BehaviorResourceLimits(1_000, 64 * 1024 * 1024, 30_000)),
            command.ProgramSource,
            """{"version":1,"libraries":{}}""",
            compile.AssemblyBytes,
            """{"runtimeTarget":{"name":"net11.0"}}""",
            command.Features.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            compile.CompilerEvidenceJson,
            """{"result":"pending","policy":"v1"}""",
            """{"scenarios":0,"passed":false}""");

        var written = CanonicalArtifactWriter.Write(envelope);
        var hash = written.Digest.Value;

        data = data with
        {
            Status = BehaviorRevisionStatus.Proposed,
            ProposedArtifactHash = hash,
            LastCompileFailure = null,
            TestsPassed = false,
            IsApproved = false,
            ProgramSource = command.ProgramSource,
            Features = command.Features.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            DisplayName = command.DisplayName,
            Description = command.Description,
            ArtifactBytes = written.Bytes,
            AssemblyBytes = compile.AssemblyBytes.ToArray(),
            ActiveArtifactHash = activeHash,
            PriorArtifactHash = priorHash,
            Approval = null,
            ApprovalEvidence = null,
        };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);
        await EmitAsync(new BehaviorRevisionProposed(command.CommandId, behaviorId, hash));
        await EmitAsync(new BehaviorCompileSucceeded(command.CommandId, behaviorId, hash));
        return Snapshot(data);
    }

    public async Task<BehaviorSnapshot> RunTests(RunBehaviorTests command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ArtifactHash);

        var data = LoadOrEmpty();
        if (TryReceipt(data, command.CommandId, out var received))
        {
            return received;
        }

        if (!string.Equals(data.ProposedArtifactHash, command.ArtifactHash, StringComparison.Ordinal)
            || data.ArtifactBytes is null
            || data.AssemblyBytes is null
            || data.Features is null
            || data.ProgramSource is null)
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no proposed revision for artifact '{command.ArtifactHash}'.");
        }

        var behaviorId = BehaviorIdOfName();
        var envelope = new BehaviorArtifactEnvelope(
            new BehaviorDefinitionManifest(
                behaviorId,
                data.DisplayName ?? behaviorId.Value,
                data.Description ?? behaviorId.Value,
                new BehaviorEntryPoints([], []),
                [],
                new BehaviorResourceLimits(1_000, 64 * 1024 * 1024, 30_000)),
            data.ProgramSource,
            """{"version":1,"libraries":{}}""",
            data.AssemblyBytes,
            """{"runtimeTarget":{"name":"net11.0"}}""",
            data.Features,
            """{"diagnostics":[],"sdk":"rail"}""",
            """{"result":"pending","policy":"v1"}""",
            """{"scenarios":0,"passed":false}""");

        var report = _bddGate.Evaluate(
            envelope,
            data.AssemblyBytes,
            command.ArtifactHash,
            new GrainBehaviorCapabilityResolver(GrainFactory, Id.Owner),
            TimeProvider);

        if (report.Passed)
        {
            data = data with
            {
                Status = BehaviorRevisionStatus.TestsPassed,
                TestsPassed = true,
            };
            data = WithReceipt(data, command.CommandId, Snapshot(data));
            await SaveAsync(data);
            await EmitAsync(new BehaviorTestsPassed(command.CommandId, behaviorId, command.ArtifactHash, report.ScenarioCount));
            return Snapshot(data);
        }

        data = data with
        {
            Status = BehaviorRevisionStatus.TestsFailed,
            TestsPassed = false,
            IsApproved = false,
        };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);
        await EmitAsync(new BehaviorTestsFailed(command.CommandId, behaviorId, command.ArtifactHash, report.Detail));
        return Snapshot(data);
    }

    public async Task<BehaviorSnapshot> Approve(BehaviorRevisionApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ValidateCommand(approval.CommandId);

        var data = LoadOrEmpty();
        var behaviorId = BehaviorIdOfName();

        if (TryReceipt(data, approval.CommandId, out var received)
            && data.IsApproved
            && string.Equals(data.ProposedArtifactHash, approval.Fingerprint, StringComparison.Ordinal))
        {
            return received;
        }

        if (!string.Equals(data.ProposedArtifactHash, approval.Fingerprint, StringComparison.Ordinal))
        {
            await EmitAsync(new BehaviorRevisionApprovalRefused(
                approval.CommandId,
                behaviorId,
                approval.Fingerprint,
                "stale-or-mismatched-artifact-hash"));
            throw new NeuronAuthorizationException(
                $"Behavior approval fingerprint '{approval.Fingerprint}' does not match the proposed artifact.");
        }

        if (!data.TestsPassed || data.Status is BehaviorRevisionStatus.TestsFailed or BehaviorRevisionStatus.CompileFailed)
        {
            await EmitAsync(new BehaviorRevisionApprovalRefused(
                approval.CommandId,
                behaviorId,
                approval.Fingerprint,
                "bdd-gate-not-green"));
            throw new InvalidOperationException(
                $"Behavior '{Id}' cannot be approved until the BDD gate is green for '{approval.Fingerprint}'.");
        }

        var approvalEvidence = await ApprovalEvidenceAsync(approval);
        ValidateApprovalEvidence(approval, approvalEvidence);

        data = data with
        {
            Status = BehaviorRevisionStatus.Approved,
            IsApproved = true,
            Approval = approval,
            ApprovalEvidence = approvalEvidence.SynapseId,
        };
        data = WithReceipt(data, approval.CommandId, Snapshot(data));
        await SaveAsync(data);
        await EmitAsync(new BehaviorRevisionApproved(
            approval.CommandId,
            behaviorId,
            approval.Fingerprint,
            approval.ApprovalId));
        return Snapshot(data);
    }

    public async Task<BehaviorSnapshot> Activate(ActivateBehaviorRevision command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ArtifactHash);

        var data = LoadOrEmpty();
        if (TryReceipt(data, command.CommandId, out var received))
        {
            return received;
        }

        if (!data.IsApproved
            || !string.Equals(data.ProposedArtifactHash, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no approved revision '{command.ArtifactHash}' to activate.");
        }

        var prior = data.ActiveArtifactHash;
        data = data with
        {
            Status = BehaviorRevisionStatus.Active,
            PriorArtifactHash = prior,
            ActiveArtifactHash = command.ArtifactHash,
            ActiveArtifactBytes = data.ArtifactBytes,
            ActiveAssemblyBytes = data.AssemblyBytes,
            ActiveProgramSource = data.ProgramSource,
            PriorArtifactBytes = data.ActiveArtifactBytes,
            PriorAssemblyBytes = data.ActiveAssemblyBytes,
            PriorProgramSource = data.ActiveProgramSource,
        };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);
        await EmitAsync(new BehaviorRevisionActivated(
            command.CommandId,
            BehaviorIdOfName(),
            command.ArtifactHash,
            prior));
        return Snapshot(data);
    }

    public async Task<BehaviorSnapshot> Rollback(RollbackBehaviorRevision command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);

        var data = LoadOrEmpty();
        if (TryReceipt(data, command.CommandId, out var received))
        {
            return received;
        }

        if (data.PriorArtifactHash is null
            || data.ActiveArtifactHash is null
            || data.PriorArtifactBytes is null)
        {
            throw new InvalidOperationException($"Behavior '{Id}' has no prior revision to restore.");
        }

        var demoted = data.ActiveArtifactHash;
        var restored = data.PriorArtifactHash;
        data = data with
        {
            Status = BehaviorRevisionStatus.Active,
            ActiveArtifactHash = restored,
            PriorArtifactHash = demoted,
            ActiveArtifactBytes = data.PriorArtifactBytes,
            ActiveAssemblyBytes = data.PriorAssemblyBytes,
            ActiveProgramSource = data.PriorProgramSource,
            PriorArtifactBytes = data.ActiveArtifactBytes,
            PriorAssemblyBytes = data.ActiveAssemblyBytes,
            PriorProgramSource = data.ActiveProgramSource,
        };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);
        await EmitAsync(new BehaviorRevisionRolledBack(
            command.CommandId,
            BehaviorIdOfName(),
            restored,
            demoted));
        return Snapshot(data);
    }

    public async Task<BehaviorExecutionResult> Execute(ExecuteBehaviorRevision command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommand(command.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TriggerTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TriggerJson);

        var data = LoadOrEmpty();
        if (data.ActiveArtifactHash is null
            || data.ActiveArtifactBytes is null
            || data.ActiveAssemblyBytes is null)
        {
            throw new InvalidOperationException($"Behavior '{Id}' has no active revision to execute.");
        }

        var metadata = new BehaviorExecutionMetadata(
            Id.Owner,
            BehaviorIdOfName(),
            new BehaviorRevisionId(data.ActiveArtifactHash),
            BehaviorExecutionId.New());

        var outcome = await _executor.ExecuteAsync(
            new BehaviorExecutionRequest(
                metadata,
                data.ActiveAssemblyBytes,
                data.ActiveArtifactHash,
                command.TriggerTypeName,
                command.TriggerJson,
                new GrainBehaviorCapabilityResolver(GrainFactory, Id.Owner),
                TimeProvider),
            CancellationToken.None);

        data = data with { LastExecutionOutcome = outcome.Outcome };
        await SaveAsync(data);
        await EmitAsync(new BehaviorExecuted(
            command.CommandId,
            BehaviorIdOfName(),
            data.ActiveArtifactHash,
            outcome.Outcome));

        return new BehaviorExecutionResult(
            command.CommandId,
            BehaviorIdOfName(),
            data.ActiveArtifactHash,
            outcome.Outcome,
            outcome.Succeeded);
    }

    public Task HandleAsync(BehaviorRevisionApproval synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    Task INeuron.Deliver(SynapseDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (delivery.Synapse is BehaviorRevisionApproval approval
            && (delivery.Caller != approval.Approver
                || approval.Approver.Type != ISessionNeuron.GrainTypeName
                || approval.Approver.Owner != Id.Owner))
        {
            return Task.CompletedTask;
        }

        return base.Deliver(delivery);
    }
}
