using System.ComponentModel;
using DigitalBrain.Abstractions;
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
}

[GenerateSerializer]
[Alias(BroadcastHarness.DeclaredFactContractId)]
[Description("Broadcast fact a behavior may subscribe to")]
public sealed record ProbeFactRaised([property: Id(0)] string Label) : Synapse;

[GenerateSerializer]
[Alias(BroadcastHarness.UndeclaredFactContractId)]
[Description("Broadcast fact no behavior subscribes to")]
public sealed record ProbeFactUnwanted([property: Id(0)] string Label) : Synapse;

[GrainType(BroadcastHarness.GrainTypeName)]
internal sealed class BroadcastProbeEmitterNeuron : Neuron, IBroadcastProbeEmitter
{
    public Task BroadcastDeclared(string label) => EmitAsync(new ProbeFactRaised(label));

    public Task BroadcastUndeclared(string label) => EmitAsync(new ProbeFactUnwanted(label));
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
    }
}

internal static class BroadcastHarness
{
    public const string EmitterContractId = "behaviors.broadcast-probe-emitter";
    public const string DeclaredFactContractId = "behaviors.probe-fact-raised";
    public const string UndeclaredFactContractId = "behaviors.probe-fact-unwanted";
    public const string GrainTypeName = "broadcastprobeemitter";

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
}
