using System.Collections.Concurrent;
using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;

namespace DigitalBrain.Integrations.Tests;

[Alias(AuthRequiringDispatchProbe.NeuronContractId)]
[Description("StageDispatch target that parks via production McpAuthorizationRail.EnsureAuthorizedAsync")]
public partial interface IAuthRequiringProbe : INeuron;

[GenerateSerializer]
[Alias(AuthRequiringDispatchProbe.RequestContractId)]
[Description("Auth-requiring dispatch request")]
public sealed record AuthRequiringProbeRequest(
    [property: Id(0)] Guid CommandId,
    [property: Id(1)] string Text) : RequestSynapse<AuthRequiringProbeResponse>;

[GenerateSerializer]
[Alias(AuthRequiringDispatchProbe.ResponseContractId)]
[Description("Auth-requiring dispatch response")]
public sealed record AuthRequiringProbeResponse(
    [property: Id(0)] string Text,
    [property: Id(1)] string DetailCode) : Synapse;

[GrainType(AuthRequiringDispatchProbe.GrainTypeName)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class AuthRequiringProbeNeuron :
    Neuron,
    IAuthRequiringProbe,
    IHandle<AuthRequiringProbeRequest>,
    IEmit<AuthRequiringProbeResponse>
{
    private const string TokensName = "integrations.auth-probe.oauth";
    private static readonly McpServerDefinition Server = new(
        "google.gmail",
        "DigitalBrain Gmail",
        new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
        "DigitalBrain:Google:Gmail",
        ["https://www.googleapis.com/auth/gmail.readonly"]);

    private readonly IDurableValue<byte[]> _tokenState;

    public AuthRequiringProbeNeuron()
    {
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
    }

    public async Task HandleAsync(AuthRequiringProbeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.CommandId == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(request));
        }

        await McpAuthorizationRail.EnsureAuthorizedAsync(
            GrainFactory,
            Id.Owner,
            ServiceProvider,
            TimeProvider,
            new CommandId(request.CommandId),
            Server,
            _tokenState,
            () => WriteStateAsync(),
            durableIdentity: Id.ToString(),
            cancellationToken);

        AuthRequiringDispatchProbe.RecordDelivery(request.Text);
        await ReplyAsync(
            new AuthRequiringProbeResponse(request.Text, DetailCode: "authorized-once"),
            cancellationToken);
    }
}

public sealed partial class AuthRequiringDispatchModule : IModule;

internal static class AuthRequiringDispatchProbe
{
    public const string NeuronContractId = "integrations.auth-requiring-probe";
    public const string RequestContractId = "integrations.auth-requiring-probe-request";
    public const string ResponseContractId = "integrations.auth-requiring-probe-response";
    public const string GrainTypeName = "authrequiringprobe";

    private static readonly ConcurrentDictionary<string, int> Deliveries = new(StringComparer.Ordinal);

    public static int CountFor(string text)
        => Deliveries.TryGetValue(text, out var count) ? count : 0;

    public static void RecordDelivery(string text)
        => Deliveries.AddOrUpdate(text, 1, static (_, current) => current + 1);

    public static void Reset() => Deliveries.Clear();
}
