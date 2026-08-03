using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Behaviors.Tests;

[ClientEntryPoint]
[Alias(BroadcastHarness.EmitterContractId)]
[Description("Harness neuron that raises owner-scoped broadcast facts")]
public partial interface IBroadcastProbeEmitter : INeuron
{
    [Alias(nameof(BroadcastDeclared))]
    Task BroadcastDeclared(string label);

    [Alias(nameof(BroadcastUndeclared))]
    Task BroadcastUndeclared(string label);

    [Alias(nameof(BroadcastStalled))]
    Task BroadcastStalled(string label);
}

[GenerateSerializer]
[Alias(BroadcastHarness.DeclaredFactContractId)]
[Description("Broadcast fact a behavior may subscribe to")]
public sealed record ProbeFactRaised([property: Id(0)] string Label) : Synapse;

[GenerateSerializer]
[Alias(BroadcastHarness.UndeclaredFactContractId)]
[Description("Broadcast fact no behavior subscribes to")]
public sealed record ProbeFactUnwanted([property: Id(0)] string Label) : Synapse;

[GenerateSerializer]
[Alias(BroadcastHarness.StalledFactContractId)]
[Description("Broadcast fact whose subscriber lookup never answers")]
public sealed record ProbeFactStalled([property: Id(0)] string Label) : Synapse;

[GrainType(BroadcastHarness.GrainTypeName)]
internal sealed class BroadcastProbeEmitterNeuron : Neuron, IBroadcastProbeEmitter
{
    public Task BroadcastDeclared(string label) => EmitAsync(new ProbeFactRaised(label));

    public Task BroadcastUndeclared(string label) => EmitAsync(new ProbeFactUnwanted(label));

    public Task BroadcastStalled(string label) => EmitAsync(new ProbeFactStalled(label));
}

// Delegates every alias to the real registry lookup except the one the bound is proven against.
internal sealed class StallingBroadcastSubscribers : IBroadcastSubscribers
{
    private readonly BehaviorBroadcastSubscribers _registry;

    public StallingBroadcastSubscribers(IGrainFactory grains) => _registry = new(grains);

    public async ValueTask<IReadOnlyCollection<NeuronId>> ReceiversFor(
        OwnerId owner,
        string eventAlias,
        CancellationToken cancellationToken)
    {
        if (string.Equals(eventAlias, BroadcastHarness.StalledFactContractId, StringComparison.Ordinal))
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return await _registry.ReceiversFor(owner, eventAlias, cancellationToken);
    }
}

[ClientEntryPoint]
[Alias(BroadcastHarness.ActivationEmitterContractId)]
[Description("Harness neuron that emits a pair of facts from its own activation")]
public partial interface IActivationPairEmitter : INeuron
{
    [Alias(nameof(Touch))]
    Task Touch();
}

[GenerateSerializer]
[Alias(BroadcastHarness.ActivationHeadContractId)]
[Description("First fact of an activation-time emission pair")]
public sealed record ProbeActivationHead : Synapse;

[GenerateSerializer]
[Alias(BroadcastHarness.ActivationTailContractId)]
[Description("Second fact of an activation-time emission pair")]
public sealed record ProbeActivationTail : Synapse;

// Activation is the one reachable emission point with neither a delivery turn nor a client
// entry scope, so it is where an unbound correlation is observable.
[GrainType(BroadcastHarness.ActivationGrainTypeName)]
internal sealed class ActivationPairEmitterNeuron : Neuron, IActivationPairEmitter
{
    protected override async Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var correlation = ResolveEmissionCorrelation();
        await EmitAsync(new ProbeActivationHead(), correlation);
        await EmitAsync(new ProbeActivationTail(), correlation);
    }

    public Task Touch() => Task.CompletedTask;
}

public sealed class BehaviorBroadcastHarnessModule : IModule, ICompiledModule
{
    public static ModuleId Id { get; } = new(typeof(BehaviorBroadcastHarnessModule).FullName!);

    ModuleId ICompiledModule.Id => Id;

    public CapabilityManifest Capabilities { get; } = new(
        Id,
        "1.0.0",
        "Behavior broadcast harness module",
        Array.Empty<string>(),
        [
            new NeuronCapabilityDescriptor(
                BroadcastHarness.EmitterContractId,
                "Harness neuron that raises owner-scoped broadcast facts",
                "default",
                Array.Empty<SynapseCapabilityDescriptor>(),
                [
                    new SynapseCapabilityDescriptor(
                        BroadcastHarness.DeclaredFactContractId,
                        1,
                        "Broadcast fact a behavior may subscribe to",
                        CapabilitySchema.For(typeof(ProbeFactRaised)),
                        Array.Empty<string>()),
                    new SynapseCapabilityDescriptor(
                        BroadcastHarness.UndeclaredFactContractId,
                        1,
                        "Broadcast fact no behavior subscribes to",
                        CapabilitySchema.For(typeof(ProbeFactUnwanted)),
                        Array.Empty<string>()),
                    new SynapseCapabilityDescriptor(
                        BroadcastHarness.StalledFactContractId,
                        1,
                        "Broadcast fact whose subscriber lookup never answers",
                        CapabilitySchema.For(typeof(ProbeFactStalled)),
                        Array.Empty<string>()),
                ]),
            new NeuronCapabilityDescriptor(
                BroadcastHarness.ActivationEmitterContractId,
                "Harness neuron that emits a pair of facts from its own activation",
                "default",
                Array.Empty<SynapseCapabilityDescriptor>(),
                [
                    new SynapseCapabilityDescriptor(
                        BroadcastHarness.ActivationHeadContractId,
                        1,
                        "First fact of an activation-time emission pair",
                        CapabilitySchema.For(typeof(ProbeActivationHead)),
                        Array.Empty<string>()),
                    new SynapseCapabilityDescriptor(
                        BroadcastHarness.ActivationTailContractId,
                        1,
                        "Second fact of an activation-time emission pair",
                        CapabilitySchema.For(typeof(ProbeActivationTail)),
                        Array.Empty<string>()),
                ]),
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
            typeof(BehaviorBroadcastHarnessModule).Assembly);
        builder.Services.AddSingleton<IBroadcastSubscribers>(static provider =>
            new StallingBroadcastSubscribers(provider.GetRequiredService<IGrainFactory>()));
    }
}

internal static class BroadcastHarness
{
    public const string EmitterContractId = "behaviors.broadcast-probe-emitter";
    public const string DeclaredFactContractId = "behaviors.probe-fact-raised";
    public const string UndeclaredFactContractId = "behaviors.probe-fact-unwanted";
    public const string StalledFactContractId = "behaviors.probe-fact-stalled";
    public const string GrainTypeName = "broadcastprobeemitter";
    public const string ActivationEmitterContractId = "behaviors.activation-pair-emitter";
    public const string ActivationHeadContractId = "behaviors.probe-activation-head";
    public const string ActivationTailContractId = "behaviors.probe-activation-tail";
    public const string ActivationGrainTypeName = "activationpairemitter";

    public const string SubscribingFeature =
        """
        Feature: subscribing behavior
          Scenario: install gate passes
            Then the install gate passes
        """;

    public static string SubscribingProgram(string alias = DeclaredFactContractId)
        => $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            [Alias("{{alias}}")]
            public sealed record ProbeFactRaised(string Label) : Synapse;

            public sealed class SubscribingProgram : IBehaviorProgram<ProbeFactRaised>
            {
                public ValueTask ExecuteAsync(ProbeFactRaised trigger, IBehaviorContext context, CancellationToken cancellationToken)
                {
                    context.SetState("outcome", "woke:" + trigger.Label);
                    return ValueTask.CompletedTask;
                }
            }

            public sealed class SubscribingInstallTests : IBehaviorInstallTests
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

    public static string SelfEmittingProgram()
        => $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            [Alias("{{DeclaredFactContractId}}")]
            public sealed record ProbeFactRaised(string Label) : Synapse;

            public sealed class SelfEmittingProgram : IBehaviorProgram<ProbeFactRaised>
            {
                public async ValueTask ExecuteAsync(ProbeFactRaised trigger, IBehaviorContext context, CancellationToken cancellationToken)
                {
                    await context.EmitAsync(new ProbeFactRaised(trigger.Label), cancellationToken);
                }
            }

            public sealed class SelfEmittingInstallTests : IBehaviorInstallTests
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

    public static string EmittingProgram()
        => $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            public sealed record EmitTrigger(string Label) : Synapse;

            [Alias("{{DeclaredFactContractId}}")]
            public sealed record ProbeFactRaised(string Label) : Synapse;

            public sealed class EmittingProgram : IBehaviorProgram<EmitTrigger>
            {
                public async ValueTask ExecuteAsync(EmitTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                {
                    await context.EmitAsync(new ProbeFactRaised(trigger.Label), cancellationToken);
                }
            }

            public sealed class EmittingInstallTests : IBehaviorInstallTests
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
}
