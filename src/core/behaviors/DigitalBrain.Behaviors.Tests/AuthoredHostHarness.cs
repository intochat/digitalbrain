using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Host;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Behaviors.Tests;

[ClientEntryPoint]
[Alias(AuthoredHostHarness.EmitterContractId)]
[Description("Harness neuron that opens an authored behavior cycle")]
public partial interface IAuthoredCycleOpener : INeuron
{
    [Alias(nameof(OpenCycle))]
    Task OpenCycle(string label);
}

[ClientEntryPoint]
[Alias(AuthoredHostHarness.ListenerContractId)]
[Description("Module neuron that handles the fact authored code speaks")]
public partial interface IProbeFactListener : INeuron
{
    [Alias(nameof(Touch))]
    Task Touch();
}

[GenerateSerializer]
[Alias(AuthoredHostHarness.HeardFactContractId)]
[Description("Broadcast fact an authored behavior speaks")]
public sealed record ProbeFactHeard([property: Id(0)] string Label) : Synapse;

[GenerateSerializer]
[Alias(AuthoredHostHarness.PingFactContractId)]
[Description("First leg of an authored behavior cycle")]
public sealed record ProbeCyclePing([property: Id(0)] string Label) : Synapse;

[GenerateSerializer]
[Alias(AuthoredHostHarness.PongFactContractId)]
[Description("Second leg of an authored behavior cycle")]
public sealed record ProbeCyclePong([property: Id(0)] string Label) : Synapse;

[GrainType(AuthoredHostHarness.OpenerGrainTypeName)]
internal sealed class AuthoredCycleOpenerNeuron : Neuron, IAuthoredCycleOpener
{
    public Task OpenCycle(string label) => EmitAsync(new ProbeCyclePing(label));
}

// A compile-time IHandle subscriber, not a behavior: it is what proves an authored emission
// lands in the module vocabulary rather than only in the behavior rail.
[GrainType(AuthoredHostHarness.ListenerGrainTypeName)]
internal sealed class ProbeFactListenerNeuron : Neuron, IProbeFactListener, IHandle<ProbeFactHeard>
{
    public Task HandleAsync(ProbeFactHeard fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task Touch() => Task.CompletedTask;
}

// Stands in for the reverse-broker HTTP hop: same interface, same typed refusals surfacing as
// BehaviorHostException, but reaching IBehaviorCapabilityDispatchAccess directly so a real
// authored program can run inside the silo process without a second host.
internal sealed class InProcessHostBrokerClient(
    IServiceProvider services,
    OwnerId owner,
    NeuronId task,
    AttemptId attempt) : IBehaviorHostBrokerClient
{
    public async ValueTask EmitFactAsync(
        BehaviorId behavior,
        string emitAlias,
        ReadOnlyMemory<byte> factJson,
        int hopsRemaining,
        CancellationToken cancellationToken)
    {
        var outcome = await services.GetRequiredService<IBehaviorCapabilityDispatchAccess>()
            .EmitFactAsync(
                owner,
                task,
                attempt,
                behavior,
                emitAlias,
                System.Text.Encoding.UTF8.GetString(factJson.Span),
                hopsRemaining,
                cancellationToken);

        if (!string.Equals(outcome, BehaviorFactEmission.Emitted, StringComparison.Ordinal))
        {
            throw new BehaviorHostException(outcome);
        }
    }

    public ValueTask<ReadOnlyMemory<byte>> LoadTriggerAsync(
        OwnerId owner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
        => services.GetRequiredService<IBehaviorProtectedTriggerAccess>()
            .LoadAsync(owner, task, behavior, revision, caseId, reference, cancellationToken);

    public ValueTask<ProtectedPayloadReference> StorePayloadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
        => services.GetRequiredService<IBehaviorProtectedPayloadAccess>()
            .StoreAsync(owner, task, attempt, plaintext, cancellationToken);

    public ValueTask<ReadOnlyMemory<byte>> LoadPayloadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
        => services.GetRequiredService<IBehaviorProtectedPayloadAccess>()
            .LoadAsync(owner, task, attempt, reference, cancellationToken);

    public ValueTask<TaskOperationSnapshot> PrepareAsync(
        PrepareTaskOperation command,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Authored host harness programs take no directed edges.");

    public ValueTask<ReadTaskOperationResult> ReadAsync(
        ReadTaskOperation command,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Authored host harness programs take no directed edges.");

    public ValueTask<TaskOperationSnapshot> TransitionAsync(
        TransitionTaskOperation command,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Authored host harness programs take no directed edges.");

    public ValueTask<ProtectedPayloadReference> DispatchAsync(
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Authored host harness programs take no directed edges.");
}

internal sealed class InProcessHostBrokerClientFactory(IServiceProvider services)
    : IBehaviorHostBrokerClientFactory
{
    public IBehaviorHostBrokerClient Create(OwnerId owner, NeuronId task, AttemptId attempt, NeuronId worker)
    {
        _ = worker;
        return new InProcessHostBrokerClient(services, owner, task, attempt);
    }
}

public sealed class AuthoredHostHarnessModule : IModule, ICompiledModule
{
    public static ModuleId Id { get; } = new(typeof(AuthoredHostHarnessModule).FullName!);

    ModuleId ICompiledModule.Id => Id;

    public CapabilityManifest Capabilities { get; } = new(
        Id,
        "1.0.0",
        "Authored behavior host harness module",
        Array.Empty<string>(),
        [
            new NeuronCapabilityDescriptor(
                AuthoredHostHarness.EmitterContractId,
                "Harness neuron that opens an authored behavior cycle",
                "default",
                Array.Empty<SynapseCapabilityDescriptor>(),
                [
                    new SynapseCapabilityDescriptor(
                        AuthoredHostHarness.HeardFactContractId,
                        1,
                        "Broadcast fact an authored behavior speaks",
                        CapabilitySchema.For(typeof(ProbeFactHeard)),
                        Array.Empty<string>()),
                    new SynapseCapabilityDescriptor(
                        AuthoredHostHarness.PingFactContractId,
                        1,
                        "First leg of an authored behavior cycle",
                        CapabilitySchema.For(typeof(ProbeCyclePing)),
                        Array.Empty<string>()),
                    new SynapseCapabilityDescriptor(
                        AuthoredHostHarness.PongFactContractId,
                        1,
                        "Second leg of an authored behavior cycle",
                        CapabilitySchema.For(typeof(ProbeCyclePong)),
                        Array.Empty<string>()),
                ]),
            new NeuronCapabilityDescriptor(
                AuthoredHostHarness.ListenerContractId,
                "Module neuron that handles the fact authored code speaks",
                "default",
                [
                    new SynapseCapabilityDescriptor(
                        AuthoredHostHarness.HeardFactContractId,
                        1,
                        "Broadcast fact an authored behavior speaks",
                        CapabilitySchema.For(typeof(ProbeFactHeard)),
                        Array.Empty<string>()),
                ],
                Array.Empty<SynapseCapabilityDescriptor>()),
        ]);

    CapabilityManifest ICompiledModule.Capabilities => Capabilities;

    public void PrepareSerialization(IServiceCollection services)
    {
    }

    public void Activate(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        DigitalBrainSiloBuilderExtensions.AddBroadcastHandlers(
            builder,
            typeof(AuthoredHostHarnessModule).Assembly);
    }
}

// The real BehaviorHostEngine loading real compiled assemblies, wired to the real silo-side
// enforcement chain. The only stand-in is the transport between them.
public sealed class AuthoredHostFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<TasksModule>();
        brain.AddModule<AuthoredHostHarnessModule>();

        var silo = Silo;
        brain.ConfigureServiceEdge(
            services =>
            {
                services.RemoveAll<IBehaviorExecutor>();
                services.RemoveAll<IBehaviorHostGateway>();
                services.TryAddSingleton<IBehaviorHostBrokerClientFactory>(
                    provider =>
                    {
                        silo.Services = provider;
                        return new InProcessHostBrokerClientFactory(provider);
                    });
                services.AddSingleton(static provider => new BehaviorHostEngine(
                    provider.GetRequiredService<IBehaviorArtifactTrust>(),
                    provider.GetRequiredService<IBehaviorHostBrokerClientFactory>()));
                services.AddSingleton<IBehaviorHostGateway>(
                    static provider => provider.GetRequiredService<BehaviorHostEngine>());
                services.AddSingleton<IBehaviorExecutor>(
                    static provider => new HostedBehaviorExecutor(
                        provider.GetRequiredService<IBehaviorHostGateway>()));
            },
            silo,
            static _ => { });
    }

    // The silo provider is the only place IBehaviorCapabilityDispatchAccess lives, and a test that
    // must drive the reverse-broker leg the way a host would needs to reach it.
    public AuthoredHostSiloAccess Silo { get; } = new();
}

public sealed class AuthoredHostSiloAccess
{
    public IServiceProvider? Services { get; internal set; }
}

internal static class AuthoredHostHarness
{
    public const string EmitterContractId = "behaviors.authored-cycle-opener";
    public const string ListenerContractId = "behaviors.probe-fact-listener";
    public const string HeardFactContractId = "behaviors.probe-fact-heard";
    public const string PingFactContractId = "behaviors.probe-cycle-ping";
    public const string PongFactContractId = "behaviors.probe-cycle-pong";
    public const string OpenerGrainTypeName = "authoredcycleopener";
    public const string ListenerGrainTypeName = "probefactlistener";

    public const string InstallFeature =
        """
        Feature: authored behavior
          Scenario: install gate passes
            Then the install gate passes
        """;

    // Wakes on triggerAlias and speaks emitAlias: one authored program that both hears and talks,
    // which is what closes a behavior-to-behavior loop without any hand-written plumbing.
    public static string RelayProgram(
        string triggerName,
        string triggerAlias,
        string factName,
        string emitAlias)
        => $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            [Alias("{{triggerAlias}}")]
            public sealed record {{triggerName}}(string Label) : Synapse;

            [Alias("{{emitAlias}}")]
            public sealed record {{factName}}(string Label) : Synapse;

            public sealed class RelayProgram : IBehaviorProgram<{{triggerName}}>
            {
                public async ValueTask ExecuteAsync(
                    {{triggerName}} trigger,
                    IBehaviorContext context,
                    CancellationToken cancellationToken)
                {
                    await context.EmitAsync(new {{factName}}(trigger.Label), cancellationToken);
                }
            }

            public sealed class RelayInstallTests : IBehaviorInstallTests
            {
                public ValueTask<BehaviorInstallTestReport> RunAsync(
                    IBehaviorContext context,
                    IReadOnlyDictionary<string, string> features,
                    CancellationToken cancellationToken)
                    => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                    [
                        new BehaviorScenarioResult(
                            "scenario.install-gate-passes",
                            "install gate passes",
                            "bind.install-gate-passes",
                            true,
                            "green"),
                    ],
                    "green"));
            }
            """;

    public static async Task<BehaviorSnapshot> ActivateAsync(
        TestBrain test,
        TestNeuron<IBehaviorNeuron> behavior,
        string program,
        CancellationToken cancellationToken)
    {
        var proposed = await behavior.Reference.Propose(new ProposeBehaviorRevision(
            CommandId.New(),
            program,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["install"] = InstallFeature,
            },
            "Authored",
            "Authored behavior"));

        await behavior.Reference.RunTests(new RunBehaviorTests(CommandId.New(), proposed.ProposedArtifactHash!));

        var approval = new BehaviorRevisionApproval(
            Guid.NewGuid(),
            CommandId.New(),
            proposed.ProposedArtifactHash!,
            ISessionNeuron.ForOwner(test.Client.Owner),
            test.Clock.UtcNow);
        var deliveryWait = behavior.Incoming.NextAsync<BehaviorRevisionApproval>(cancellationToken);
        await test.Client.SendAsync(behavior.Id, approval, cancellationToken);
        _ = await deliveryWait;
        await behavior.Reference.Approve(approval);

        return await behavior.Reference.Activate(
            new ActivateBehaviorRevision(CommandId.New(), proposed.ProposedArtifactHash!));
    }
}
