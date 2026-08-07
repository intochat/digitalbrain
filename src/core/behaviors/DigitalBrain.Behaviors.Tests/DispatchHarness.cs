using System.Collections.Concurrent;
using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Behaviors.Tests;

[Alias(DispatchHarness.NeuronContractId)]
[Description("Harness result-bearing neuron for reverse-broker capability dispatch")]
public partial interface IDispatchProbe : INeuron;

[GenerateSerializer]
[Alias(DispatchHarness.RequestContractId)]
[Description("Dispatch probe request text")]
public sealed record DispatchProbeRequest([property: Id(0)] string Text) : RequestSynapse<DispatchProbeResponse>;

[GenerateSerializer]
[Alias(DispatchHarness.ResponseContractId)]
[Description("Dispatch probe response text and detail code")]
public sealed record DispatchProbeResponse(
    [property: Id(0)] string Text,
    [property: Id(1)] string? DetailCode = null) : Synapse;

[GrainType(DispatchHarness.GrainTypeName)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class DispatchProbeNeuron :
    Neuron,
    IDispatchProbe,
    IHandle<DispatchProbeRequest>,
    IEmit<DispatchProbeResponse>
{
    public Task HandleAsync(DispatchProbeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        DispatchHarness.RecordDelivery(request.Text);
        return ReplyAsync(new DispatchProbeResponse(request.Text, DetailCode: "once-code"), cancellationToken);
    }
}

public sealed class BehaviorDispatchHarnessModule : IModule, ICompiledModule
{
    public static ModuleId Id { get; } = new(typeof(BehaviorDispatchHarnessModule).FullName!);

    ModuleId ICompiledModule.Id => Id;

    public CapabilityManifest Capabilities { get; } = new(
        Id,
        "1.0.0",
        "Behavior dispatch harness module",
        Array.Empty<string>(),
        [
            new NeuronCapabilityDescriptor(
                DispatchHarness.NeuronContractId,
                "Harness result-bearing neuron for reverse-broker capability dispatch",
                "default",
                [
                    new SynapseCapabilityDescriptor(
                        DispatchHarness.RequestContractId,
                        1,
                        "Dispatch probe request text",
                        CapabilitySchema.For(typeof(DispatchProbeRequest)),
                        Array.Empty<string>()),
                ],
                [
                    new SynapseCapabilityDescriptor(
                        DispatchHarness.ResponseContractId,
                        1,
                        "Dispatch probe response text and detail code",
                        CapabilitySchema.For(typeof(DispatchProbeResponse)),
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
            typeof(BehaviorDispatchHarnessModule).Assembly);
    }
}

internal static class DispatchHarness
{
    public const string NeuronContractId = "behaviors.dispatch-probe";
    public const string RequestContractId = "behaviors.dispatch-probe-request";
    public const string ResponseContractId = "behaviors.dispatch-probe-response";
    public const string GrainTypeName = "dispatchprobe";

    private static readonly ConcurrentDictionary<string, int> Deliveries = new(StringComparer.Ordinal);

    public static int CountFor(string text)
        => Deliveries.TryGetValue(text, out var count) ? count : 0;

    public static void RecordDelivery(string text)
        => Deliveries.AddOrUpdate(text, 1, static (_, current) => current + 1);

    public static byte[] SerializeRequest(string text)
        => BehaviorPayloadJson.Serialize(new DispatchProbeRequest(text), typeof(DispatchProbeRequest));
}
