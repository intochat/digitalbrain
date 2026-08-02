using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
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
    private readonly IBehaviorArtifactTrust _artifactTrust;
    private readonly IBehaviorHostGateway? _host;

    public BehaviorNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<BehaviorData>>();
        _compiler = ServiceProvider.GetRequiredService<IBehaviorCompiler>();
        _bddGate = ServiceProvider.GetRequiredService<IBehaviorBddGate>();
        _executor = ServiceProvider.GetRequiredService<IBehaviorExecutor>();
        _artifactTrust = ServiceProvider.GetRequiredService<IBehaviorArtifactTrust>();
        _host = ServiceProvider.GetService<IBehaviorHostGateway>();
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
        if (!compile.Succeeded
            || compile.Contract is null
            || compile.Contract.Cases.Count == 0)
        {
            var diagnostics = compile.Succeeded
                ? "A successful compile must produce a non-empty behavior input contract."
                : compile.Diagnostics;
            data = data with
            {
                Status = BehaviorRevisionStatus.CompileFailed,
                ProposedArtifactHash = null,
                LastCompileFailure = diagnostics,
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
            await EmitAsync(new BehaviorCompileFailed(command.CommandId, behaviorId, diagnostics));
            return Snapshot(data);
        }

        var envelope = CreateProposalEnvelope(
            behaviorId,
            command.DisplayName,
            command.Description,
            command.ProgramSource,
            FeatureSourceOf(command.Features),
            compile.AssemblyBytes,
            compile.CompilerEvidenceJson,
            compile.Contract,
            compile.CapabilityGrants);

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
            || data.ArtifactBytes is null)
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no proposed revision for artifact '{command.ArtifactHash}'.");
        }

        var digest = BehaviorArtifactDigest.Compute(data.ArtifactBytes);
        if (!string.Equals(digest.Value, data.ProposedArtifactHash, StringComparison.Ordinal)
            || !string.Equals(digest.Value, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' artifact digest does not match the proposed revision '{command.ArtifactHash}'.");
        }

        var behaviorId = BehaviorIdOfName();
        var envelope = CanonicalArtifactReader.Read(data.ArtifactBytes);
        var report = _bddGate.Evaluate(
            envelope,
            envelope.BehaviorAssembly,
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

        if (data.ArtifactBytes is null)
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' cannot be signed: proposed artifact bytes are missing.");
        }

        var signature = _artifactTrust.Sign(approval.Fingerprint);

        data = data with
        {
            Status = BehaviorRevisionStatus.Approved,
            IsApproved = true,
            Approval = approval,
            ApprovalEvidence = approvalEvidence.SynapseId,
            ArtifactSignature = signature,
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
            || !string.Equals(data.ProposedArtifactHash, command.ArtifactHash, StringComparison.Ordinal)
            || data.ArtifactBytes is null
            || data.AssemblyBytes is null
            || data.ArtifactSignature is null)
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no signed approved revision '{command.ArtifactHash}' to activate.");
        }

        var behaviorId = BehaviorIdOfName();
        if (_host is not null)
        {
            try
            {
                await _host.DeployAsync(
                    new BehaviorHostDeployCommand(
                        Id.Owner,
                        behaviorId,
                        command.ArtifactHash,
                        data.ArtifactBytes,
                        data.AssemblyBytes,
                        data.ArtifactSignature),
                    CancellationToken.None);
                await _host.ActivateAsync(
                    new BehaviorHostActivationCommand(Id.Owner, behaviorId, command.ArtifactHash),
                    CancellationToken.None);
            }
            catch (BehaviorHostException exception)
            {
                await EmitAsync(new BehaviorRevisionDeployRefused(
                    command.CommandId,
                    behaviorId,
                    command.ArtifactHash,
                    exception.Reason));
                throw new InvalidOperationException(
                    $"Behavior host refused deploy of '{command.ArtifactHash}': {exception.Reason}.");
            }

            await EmitAsync(new BehaviorRevisionDeployed(command.CommandId, behaviorId, command.ArtifactHash));
        }

        var prior = data.ActiveArtifactHash;
        data = data with
        {
            Status = BehaviorRevisionStatus.Active,
            PriorArtifactHash = prior,
            ActiveArtifactHash = command.ArtifactHash,
            ActiveArtifactBytes = data.ArtifactBytes,
            ActiveAssemblyBytes = null,
            ActiveArtifactSignature = data.ArtifactSignature,
            ActiveProgramSource = data.ProgramSource,
            PriorArtifactBytes = data.ActiveArtifactBytes,
            PriorAssemblyBytes = null,
            PriorArtifactSignature = data.ActiveArtifactSignature,
            PriorProgramSource = data.ActiveProgramSource,
            RunState = BehaviorRunState.Running,
            ActivationGateOpen = true,
        };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);
        PublishExactCapability(data);
        await EmitAsync(new BehaviorRevisionActivated(
            command.CommandId,
            behaviorId,
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
        var behaviorId = BehaviorIdOfName();
        if (_host is not null)
        {
            await _host.DeactivateAsync(
                new BehaviorHostDeactivationCommand(Id.Owner, behaviorId, demoted),
                CancellationToken.None);
            await _host.ActivateAsync(
                new BehaviorHostActivationCommand(Id.Owner, behaviorId, restored),
                CancellationToken.None);
        }

        data = data with
        {
            Status = BehaviorRevisionStatus.Active,
            ActiveArtifactHash = restored,
            PriorArtifactHash = demoted,
            ActiveArtifactBytes = data.PriorArtifactBytes,
            ActiveAssemblyBytes = null,
            ActiveArtifactSignature = data.PriorArtifactSignature,
            ActiveProgramSource = data.PriorProgramSource,
            PriorArtifactBytes = data.ActiveArtifactBytes,
            PriorAssemblyBytes = null,
            PriorArtifactSignature = data.ActiveArtifactSignature,
            PriorProgramSource = data.ActiveProgramSource,
        };
        data = WithReceipt(data, command.CommandId, Snapshot(data));
        await SaveAsync(data);
        PublishExactCapability(data);
        await EmitAsync(new BehaviorRevisionRolledBack(
            command.CommandId,
            behaviorId,
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
            || data.ActiveArtifactBytes is null)
        {
            throw new InvalidOperationException($"Behavior '{Id}' has no active revision to execute.");
        }

        var metadata = new BehaviorExecutionMetadata(
            Id.Owner,
            BehaviorIdOfName(),
            new BehaviorRevisionId(data.ActiveArtifactHash),
            BehaviorExecutionId.New());

        var outcome = await _executor.ExecuteLegacyAsync(
            new LegacyBehaviorExecutionRequest(
                metadata,
                ReadOnlyMemory<byte>.Empty,
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

    Task INeuron.Deliver(SynapseDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        if (delivery.Synapse is BehaviorRevisionApproval approval
            && (delivery.Caller != approval.Approver
                || approval.Approver.Type != ISessionNeuron.GrainTypeName
                || approval.Approver.Owner != Id.Owner))
        {
            return Task.CompletedTask;
        }

        return base.Deliver(delivery, cancellationToken);
    }
}
