using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Host;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorHostEngineHardenedExecutionTests
{
    private static readonly OwnerId Owner = new("owner-hardened-host");
    private static readonly BehaviorId Behavior = new("com.digitalbrain.hardened-host");
    private static readonly NeuronId TaskNeuron = NeuronId.For<ITask>(Owner, "hardened-task");
    private static readonly NeuronId WorkerNeuron = NeuronId.For<IWorker>(Owner, "hardened-worker");
    private static readonly AttemptId Attempt = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    private static readonly BehaviorExecutionId Execution =
        new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    private const string TriggerTypeName = "HardenedNoOpTrigger";
    private const string TriggerJson = """{"Label":"go"}""";

    [Fact(DisplayName = "deployed activated no-op with zero grants executes via bound protected-trigger broker")]
    public async Task NoOpZeroGrantsExecutesThroughBoundProtectedTriggerBroker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var artifact = CompileCanonicalArtifact(NoOpProgram(), grants: []);
        var factory = new RecordingBrokerFactory();
        var engine = new BehaviorHostEngine(new AcceptingTrust(), factory);

        await DeployAndActivateAsync(engine, artifact, cancellationToken);

        var triggerRef = new ProtectedPayloadReference(
            Guid.Parse("99999999-8888-7777-6666-555555555555"),
            DateTimeOffset.UtcNow.AddHours(1));
        factory.Client.Seed(triggerRef, Encoding.UTF8.GetBytes(TriggerJson));

        var outcome = await engine.ExecuteAsync(
            new BehaviorHostExecuteCommand(
                Metadata(artifact.Digest.Value),
                artifact.Digest.Value,
                TaskNeuron,
                Attempt,
                TriggerTypeName,
                triggerRef,
                Capabilities: [],
                DateTimeOffset.UtcNow,
                WorkerNeuron),
            cancellationToken);

        Assert.True(outcome.Succeeded, outcome.Outcome);
        Assert.Equal(BehaviorExecutionCodes.Succeeded, outcome.Outcome);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(Owner, factory.BoundOwner);
        Assert.Equal(TaskNeuron, factory.BoundTask);
        Assert.Equal(Attempt, factory.BoundAttempt);
        Assert.Equal(WorkerNeuron, factory.BoundWorker);
        Assert.Equal(1, factory.Client.LoadCount);
        Assert.Equal(triggerRef.Id, factory.Client.LastLoadReference!.Value.Id);
        Assert.Equal(Owner, factory.Client.LastLoadOwner);
        Assert.Equal(TaskNeuron, factory.Client.LastLoadTask);
        Assert.Equal(Attempt, factory.Client.LastLoadAttempt);
        Assert.Equal(Behavior, factory.Client.LastLoadBehavior);
        Assert.Equal(new BehaviorRevisionId(artifact.Digest.Value), factory.Client.LastLoadRevision);
        Assert.Equal("case.HardenedNoOpTrigger", factory.Client.LastLoadCaseId);
    }

    [Fact(DisplayName = "result-bearing signed grant rejects command capabilities that are not the exact signed set before broker create")]
    public async Task ResultBearingGrantRejectsCapabilityMismatchBeforeBrokerCreate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var signedGrant = ResultBearingGrant();
        var artifact = CompileCanonicalArtifact(NoOpProgram(), [signedGrant]);
        var factory = new RecordingBrokerFactory();
        var engine = new BehaviorHostEngine(new AcceptingTrust(), factory);

        await DeployAndActivateAsync(engine, artifact, cancellationToken);

        var triggerRef = new ProtectedPayloadReference(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000001"),
            DateTimeOffset.UtcNow.AddHours(1));
        factory.Client.Seed(triggerRef, Encoding.UTF8.GetBytes(TriggerJson));

        var wrongEdge = new BehaviorCapabilityEdge(
            NeuronId.For<ITask>(Owner, "mismatched-capability-target"),
            "test.gmail-other-request",
            signedGrant.AcceptedRequestSchemaVersion,
            signedGrant.EmittedResultSynapseId!,
            signedGrant.EmittedResultSchemaVersion!.Value);

        var exception = await Assert.ThrowsAsync<BehaviorHostException>(async () =>
            await engine.ExecuteAsync(
                new BehaviorHostExecuteCommand(
                    Metadata(artifact.Digest.Value),
                    artifact.Digest.Value,
                    TaskNeuron,
                    Attempt,
                    TriggerTypeName,
                    triggerRef,
                    [wrongEdge],
                    DateTimeOffset.UtcNow,
                    WorkerNeuron),
                cancellationToken));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.Equal(0, factory.CreateCount);
        Assert.Equal(0, factory.Client.LoadCount);
    }

    [Fact(DisplayName = "one-way signed grant fails closed before broker create")]
    public async Task OneWaySignedGrantFailsClosedBeforeBrokerCreate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var artifact = CompileCanonicalArtifact(NoOpProgram(), [OneWayGrant()]);
        var factory = new RecordingBrokerFactory();
        var engine = new BehaviorHostEngine(new AcceptingTrust(), factory);

        await DeployAndActivateAsync(engine, artifact, cancellationToken);

        var triggerRef = new ProtectedPayloadReference(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-000000000002"),
            DateTimeOffset.UtcNow.AddHours(1));
        factory.Client.Seed(triggerRef, Encoding.UTF8.GetBytes(TriggerJson));

        var exception = await Assert.ThrowsAsync<BehaviorHostException>(async () =>
            await engine.ExecuteAsync(
                new BehaviorHostExecuteCommand(
                    Metadata(artifact.Digest.Value),
                    artifact.Digest.Value,
                    TaskNeuron,
                    Attempt,
                    TriggerTypeName,
                    triggerRef,
                    Capabilities: [],
                    DateTimeOffset.UtcNow,
                    WorkerNeuron),
                cancellationToken));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.Equal(0, factory.CreateCount);
        Assert.Equal(0, factory.Client.LoadCount);
    }

    [Fact(DisplayName = "a bound attempt drives an IBehaviorProgram artifact and its context.EmitAsync reaches the broker")]
    public async Task BoundAttemptDrivesProgramInterfaceAndItsEmitReachesTheBroker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var artifact = CompileCanonicalArtifact(
            EmittingProgramInterfaceSource(),
            grants: [],
            FactCatalog());
        var factory = new RecordingBrokerFactory();
        var engine = new BehaviorHostEngine(new AcceptingTrust(), factory);

        await DeployAndActivateAsync(engine, artifact, cancellationToken);

        var triggerRef = new ProtectedPayloadReference(
            Guid.Parse("cccccccc-dddd-eeee-ffff-000000000003"),
            DateTimeOffset.UtcNow.AddHours(1));
        // Seeded through the rail's own codec, which is camelCase: a program handed its trigger
        // through raw JsonSerializer options binds every property to null instead.
        factory.Client.Seed(
            triggerRef,
            BehaviorPayloadJson.Serialize(new HardenedTriggerShape("spoken"), typeof(HardenedTriggerShape)));

        var outcome = await engine.ExecuteAsync(
            new BehaviorHostExecuteCommand(
                Metadata(artifact.Digest.Value),
                artifact.Digest.Value,
                TaskNeuron,
                Attempt,
                "HardenedEmitTrigger",
                triggerRef,
                Capabilities: [],
                DateTimeOffset.UtcNow,
                WorkerNeuron,
                HopsRemaining: 3),
            cancellationToken);

        Assert.True(outcome.Succeeded, outcome.Outcome);
        var emission = Assert.Single(factory.Client.Emissions);
        Assert.Equal(Behavior, emission.Behavior);
        Assert.Equal(FactContractId, emission.Alias);
        // The label only survives if the trigger was decoded with the rail's codec.
        Assert.Contains("spoken", emission.FactJson, StringComparison.Ordinal);
        Assert.Equal(3, emission.Hops);
    }

    [Fact(DisplayName = "a bound attempt hands a single-file BehaviorBrain entry its trigger through the rail's own codec")]
    public async Task SingleFileEntryIsHandedItsTriggerThroughTheRailCodec()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var artifact = CompileCanonicalArtifact(CodecSensitiveSingleFileProgram(), grants: []);
        var factory = new RecordingBrokerFactory();
        var engine = new BehaviorHostEngine(new AcceptingTrust(), factory);

        await DeployAndActivateAsync(engine, artifact, cancellationToken);

        var triggerRef = new ProtectedPayloadReference(
            Guid.Parse("dddddddd-eeee-ffff-aaaa-000000000004"),
            DateTimeOffset.UtcNow.AddHours(1));
        // Every trigger the rail stores travels through BehaviorPayloadJson, which is camelCase.
        factory.Client.Seed(
            triggerRef,
            BehaviorPayloadJson.Serialize(new HardenedTriggerShape("spoken"), typeof(HardenedTriggerShape)));

        var outcome = await engine.ExecuteAsync(
            new BehaviorHostExecuteCommand(
                Metadata(artifact.Digest.Value),
                artifact.Digest.Value,
                TaskNeuron,
                Attempt,
                "HardenedCodecTrigger",
                triggerRef,
                Capabilities: [],
                DateTimeOffset.UtcNow,
                WorkerNeuron),
            cancellationToken);

        // The entry refuses any label but the seeded one, so a trigger decoded with raw
        // JsonSerializer options arrives carrying null and the execution fails.
        Assert.True(outcome.Succeeded, outcome.Outcome);
        Assert.Equal(1, factory.Client.LoadCount);
    }

    [Fact(DisplayName = "deploy rejects assembly bytes that differ from the canonical artifact embedded Behavior.dll")]
    public async Task DeployRejectsAssemblyBytesDifferingFromCanonicalEmbeddedBehaviorDll()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var artifact = CompileCanonicalArtifact(NoOpProgram(), grants: []);
        var mutatedAssembly = artifact.AssemblyBytes.ToArray();
        mutatedAssembly[^1] ^= 0xFF;
        Assert.False(artifact.AssemblyBytes.AsSpan().SequenceEqual(mutatedAssembly));

        var engine = new BehaviorHostEngine(new AcceptingTrust(), new RecordingBrokerFactory());
        var trust = new AcceptingTrust();

        var exception = await Assert.ThrowsAsync<BehaviorHostException>(async () =>
            await engine.DeployAsync(
                new BehaviorHostDeployCommand(
                    Owner,
                    Behavior,
                    artifact.Digest.Value,
                    artifact.Bytes,
                    mutatedAssembly,
                    trust.Sign(artifact.Digest.Value)),
                cancellationToken));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));

        await Assert.ThrowsAsync<BehaviorHostException>(async () =>
            await engine.ActivateAsync(
                new BehaviorHostActivationCommand(Owner, Behavior, artifact.Digest.Value),
                cancellationToken));
    }

    private static async Task DeployAndActivateAsync(
        BehaviorHostEngine engine,
        CompiledArtifact artifact,
        CancellationToken cancellationToken)
    {
        var trust = new AcceptingTrust();
        await engine.DeployAsync(
            new BehaviorHostDeployCommand(
                Owner,
                Behavior,
                artifact.Digest.Value,
                artifact.Bytes,
                artifact.AssemblyBytes,
                trust.Sign(artifact.Digest.Value)),
            cancellationToken);

        await engine.ActivateAsync(
            new BehaviorHostActivationCommand(Owner, Behavior, artifact.Digest.Value),
            cancellationToken);
    }

    private static BehaviorExecutionMetadata Metadata(string artifactHash)
        => new(
            Owner,
            Behavior,
            new BehaviorRevisionId(artifactHash),
            Execution);

    private static CompiledArtifact CompileCanonicalArtifact(
        string program,
        IReadOnlyList<BehaviorCapabilityGrant> grants,
        ActiveCapabilityCatalog? catalog = null)
    {
        var compile = (catalog is null ? new BehaviorCompiler() : new BehaviorCompiler(catalog))
            .Compile(program, Behavior);
        Assert.True(compile.Succeeded, compile.Diagnostics);
        Assert.NotNull(compile.Contract);

        var envelope = BehaviorNeuron.CreateProposalEnvelope(
            Behavior,
            "Hardened host",
            "Hardened host execution fixture",
            program,
            """
            Feature: hardened host
              Scenario: install gate passes
                Then the install gate passes
            """,
            compile.AssemblyBytes,
            compile.CompilerEvidenceJson,
            compile.Contract!,
            grants,
            compile.EventAliases,
            compile.BroadcastEmitAliases);

        var written = CanonicalArtifactWriter.Write(envelope);
        return new CompiledArtifact(
            written.Bytes,
            written.Digest,
            compile.AssemblyBytes.ToArray());
    }

    private static BehaviorCapabilityGrant ResultBearingGrant()
        => new(
            "test.gmail",
            "test.gmail-request",
            1,
            "test.gmail-response",
            1,
            "named",
            "work");

    private static BehaviorCapabilityGrant OneWayGrant()
        => new(
            "test.notify",
            "test.notify-ping",
            1,
            null,
            null,
            "default",
            "default");

    private static string NoOpProgram()
        => """
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;

            public sealed record HardenedNoOpTrigger(string Label) : Synapse;

            public sealed class HardenedNoOpProgram : IBehaviorProgram<HardenedNoOpTrigger>
            {
                public ValueTask ExecuteAsync(
                    HardenedNoOpTrigger trigger,
                    IBehaviorContext context,
                    CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public static class BehaviorEntry
            {
                public static Task RunAsync(BehaviorBrain<HardenedNoOpTrigger> brain)
                    => Task.CompletedTask;
            }
            """;

    private static string CodecSensitiveSingleFileProgram()
        => """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;

            public sealed record HardenedCodecTrigger(string Label) : Synapse;

            public sealed class HardenedCodecProgram : IBehaviorProgram<HardenedCodecTrigger>
            {
                public ValueTask ExecuteAsync(
                    HardenedCodecTrigger trigger,
                    IBehaviorContext context,
                    CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public static class BehaviorEntry
            {
                public static Task RunAsync(BehaviorBrain<HardenedCodecTrigger> brain)
                    => brain.Trigger.Label == "spoken"
                        ? Task.CompletedTask
                        : Task.FromException(new InvalidOperationException(
                            "trigger label was '" + (brain.Trigger.Label ?? "<null>") + "'"));
            }
            """;

    private const string FactContractId = "test.hardened-fact";

    private sealed record HardenedTriggerShape(string Label);

    private static string EmittingProgramInterfaceSource()
        => $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            public sealed record HardenedEmitTrigger(string Label) : Synapse;

            [Alias("{{FactContractId}}")]
            public sealed record HardenedFact(string Label) : Synapse;

            public sealed class HardenedEmittingProgram : IBehaviorProgram<HardenedEmitTrigger>
            {
                public async ValueTask ExecuteAsync(
                    HardenedEmitTrigger trigger,
                    IBehaviorContext context,
                    CancellationToken cancellationToken)
                {
                    await context.EmitAsync(new HardenedFact(trigger.Label), cancellationToken);
                }
            }
            """;

    private static ActiveCapabilityCatalog FactCatalog()
        => ActiveCapabilityCatalog.Create(
        [
            new FactCatalogModule(
                new ModuleId("catalog.hardened-facts"),
                new CapabilityManifest(
                    new ModuleId("catalog.hardened-facts"),
                    "1.0.0",
                    "Hardened fact catalog",
                    [],
                    [
                        new NeuronCapabilityDescriptor(
                            "test.hardened-source",
                            "Neuron that broadcasts the hardened fact",
                            "default",
                            [],
                            [
                                new SynapseCapabilityDescriptor(
                                    FactContractId,
                                    1,
                                    "Hardened broadcast fact",
                                    """{"type":"object","properties":{"Label":{"type":"string"}}}""",
                                    []),
                            ]),
                    ])),
        ]);

    private sealed class FactCatalogModule(ModuleId id, CapabilityManifest capabilities) : ICompiledModule
    {
        public ModuleId Id { get; } = id;

        public CapabilityManifest Capabilities { get; } = capabilities;

        public void PrepareSerialization(IServiceCollection services)
        {
        }

        public void Activate(ISiloBuilder builder)
        {
        }
    }

    private sealed record CompiledArtifact(
        byte[] Bytes,
        BehaviorArtifactDigest Digest,
        byte[] AssemblyBytes);

    private sealed class AcceptingTrust : IBehaviorArtifactTrust
    {
        public byte[] Sign(string artifactHash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
            return Encoding.UTF8.GetBytes("sig:" + artifactHash);
        }

        public void Verify(string artifactHash, ReadOnlySpan<byte> signature)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
            if (signature.IsEmpty)
            {
                throw new BehaviorHostException("unsigned-artifact");
            }
        }
    }

    private sealed class RecordingBrokerFactory : IBehaviorHostBrokerClientFactory
    {
        public RecordingBrokerClient Client { get; } = new();

        public int CreateCount { get; private set; }

        public OwnerId BoundOwner { get; private set; }

        public NeuronId BoundTask { get; private set; }

        public AttemptId BoundAttempt { get; private set; }

        public NeuronId BoundWorker { get; private set; }

        public IBehaviorHostBrokerClient Create(OwnerId owner, NeuronId task, AttemptId attempt, NeuronId worker)
        {
            CreateCount++;
            BoundOwner = owner;
            BoundTask = task;
            BoundAttempt = attempt;
            BoundWorker = worker;
            return Client;
        }
    }

    private sealed class RecordingBrokerClient : IBehaviorHostBrokerClient
    {
        public List<(BehaviorId Behavior, string Alias, string FactJson, int Hops)> Emissions { get; } = [];

        public ValueTask EmitFactAsync(
            BehaviorId behavior,
            string emitAlias,
            ReadOnlyMemory<byte> factJson,
            int hopsRemaining,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Emissions.Add((
                behavior,
                emitAlias,
                Encoding.UTF8.GetString(factJson.Span),
                hopsRemaining));
            return ValueTask.CompletedTask;
        }

        private readonly Dictionary<Guid, byte[]> payloads = new();

        public int LoadCount { get; private set; }

        public ProtectedPayloadReference? LastLoadReference { get; private set; }

        public OwnerId LastLoadOwner { get; private set; }

        public NeuronId LastLoadTask { get; private set; }

        public AttemptId LastLoadAttempt { get; private set; }

        public void Seed(ProtectedPayloadReference reference, byte[] plaintext)
            => payloads[reference.Id] = plaintext;

        public ValueTask<ProtectedPayloadReference> StorePayloadAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            payloads[id] = plaintext.ToArray();
            return ValueTask.FromResult(
                new ProtectedPayloadReference(id, DateTimeOffset.UtcNow.AddHours(1)));
        }

        public ValueTask<ReadOnlyMemory<byte>> LoadPayloadAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            ProtectedPayloadReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!payloads.TryGetValue(reference.Id, out var bytes))
            {
                throw new InvalidOperationException($"Unknown payload reference '{reference.Id}'.");
            }

            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
        }

        public BehaviorId? LastLoadBehavior { get; private set; }

        public BehaviorRevisionId? LastLoadRevision { get; private set; }

        public string? LastLoadCaseId { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>> LoadTriggerAsync(
            OwnerId owner,
            NeuronId task,
            BehaviorId behavior,
            BehaviorRevisionId revision,
            string caseId,
            ProtectedPayloadReference reference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            LastLoadReference = reference;
            LastLoadOwner = owner;
            LastLoadTask = task;
            LastLoadAttempt = Attempt;
            LastLoadBehavior = behavior;
            LastLoadRevision = revision;
            LastLoadCaseId = caseId;

            if (!payloads.TryGetValue(reference.Id, out var bytes))
            {
                throw new InvalidOperationException($"Unknown trigger reference '{reference.Id}'.");
            }

            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
        }

        public ValueTask<TaskOperationSnapshot> PrepareAsync(
            PrepareTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new TaskOperationSnapshot(
                    command.Attempt,
                    command.Sequence,
                    command.Edge,
                    command.RequestPayload,
                    TaskOperationPhase.Prepared,
                    ResponsePayload: null,
                    RedactedSummary: null));
        }

        public ValueTask<ReadTaskOperationResult> ReadAsync(
            ReadTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ReadTaskOperationResult(null));
        }

        public ValueTask<TaskOperationSnapshot> TransitionAsync(
            TransitionTaskOperation command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                new TaskOperationSnapshot(
                    command.Attempt,
                    command.Sequence,
                    new TaskOperationEdge(
                        NeuronId.For<ITask>(Owner, "unused"),
                        "req",
                        1,
                        "res",
                        1),
                    command.ResponsePayload ?? new ProtectedPayloadReference(
                        Guid.Empty,
                        DateTimeOffset.UnixEpoch),
                    command.Phase,
                    command.ResponsePayload,
                    command.RedactedSummary));
        }

        public ValueTask<ProtectedPayloadReference> DispatchAsync(
            BehaviorCapabilityEdge edge,
            ProtectedPayloadReference requestPayload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("No-op hardened fixture must not dispatch capabilities.");
        }
    }
}
