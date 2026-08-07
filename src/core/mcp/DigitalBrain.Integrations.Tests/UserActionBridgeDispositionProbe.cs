using System.Collections.Concurrent;
using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;

namespace DigitalBrain.Integrations.Tests;

[ClientEntryPoint]
[Alias(UserActionBridgeDispositionProbe.NeuronContractId)]
[Description("Probe that delivers AuthorizationCompleted as the MCP caller and records exception disposition")]
public partial interface IUserActionBridgeDispositionProbe : INeuron
{
    [Alias(nameof(ProbeAuthorizationCompleted))]
    Task ProbeAuthorizationCompleted(
        Guid probeId,
        NeuronId bridge,
        CommandId commandId,
        string serverKey,
        string state,
        CancellationToken cancellationToken = default);
}

[GrainType(UserActionBridgeDispositionProbe.GrainTypeName)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class UserActionBridgeDispositionProbeNeuron :
    Neuron,
    IUserActionBridgeDispositionProbe
{
    public async Task ProbeAuthorizationCompleted(
        Guid probeId,
        NeuronId bridge,
        CommandId commandId,
        string serverKey,
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        cancellationToken.ThrowIfCancellationRequested();
        UserActionBridgeDispositionProbe.Clear(probeId);

        var mcp = NeuronId.For<IMcpAuthorization>(Id.Owner, McpAuthorizationNeuron.InstanceName);
        try
        {
            await GrainFactory.GetGrain<INeuron>(bridge.ToGrainId()).Deliver(
                SynapseDelivery.Create(
                    new AuthorizationCompleted(commandId, serverKey, state),
                    mcp,
                    sequence: 1,
                    cause: null,
                    TimeProvider,
                    CorrelationId.New()),
                cancellationToken);
            UserActionBridgeDispositionProbe.Record(probeId, "accepted");
        }
        catch (NeuronAuthorizationException refusal)
        {
            UserActionBridgeDispositionProbe.Record(
                probeId,
                $"{nameof(NeuronAuthorizationException)}:{refusal.Message}");
        }
        catch (InvalidOperationException deferred)
        {
            UserActionBridgeDispositionProbe.Record(
                probeId,
                $"{nameof(InvalidOperationException)}:{deferred.Message}");
        }
    }
}

public sealed partial class UserActionBridgeDispositionProbeModule : IModule;

internal static class UserActionBridgeDispositionProbe
{
    public const string NeuronContractId = "integrations.user-action-bridge-disposition-probe";
    public const string GrainTypeName = "useractionbridgedispositionprobe";

    private static readonly ConcurrentDictionary<Guid, string> Dispositions = new();

    internal static void Clear(Guid probeId) => Dispositions.TryRemove(probeId, out _);

    internal static void Record(Guid probeId, string disposition)
        => Dispositions[probeId] = disposition;

    internal static bool TryRead(Guid probeId, out string? disposition)
        => Dispositions.TryGetValue(probeId, out disposition);
}
