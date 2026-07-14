using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
namespace DigitalBrain.Integrations.Google;

internal static class GmailCapabilityJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static T Read<T>(CapabilityRequest request) =>
        request.Payload.Deserialize<T>(Options)
        ?? throw new ArgumentException("The Gmail capability payload is invalid.", nameof(request));
    internal static NeuronScope Scope(CapabilityRequest request) =>
        new(new UserId(request.OwnerId.Value), request.ActorId.Value);
}
internal sealed class GmailMessageCapabilityHandler(IGmailApiClientFactory clients) : ICapabilityHandler
{
    public string CapabilityId => GoogleCapabilityIds.GmailMessageRead;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;
    public async Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        var client = await clients.CreateAsync(GmailCapabilityJson.Scope(request), cancellationToken);
        var result = await client.ReadMessageAsync(GmailCapabilityJson.Read<GmailMessageReadRequest>(request), cancellationToken);
        return JsonSerializer.SerializeToElement(result, GmailCapabilityJson.Options);
    }
}
internal sealed class GmailMailboxCapabilityHandler(IGmailApiClientFactory clients) : ICapabilityHandler
{
    public string CapabilityId => GoogleCapabilityIds.GmailMailboxRead;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;
    public async Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        var client = await clients.CreateAsync(GmailCapabilityJson.Scope(request), cancellationToken);
        var result = await client.ReadMailboxAsync(GmailCapabilityJson.Read<GmailMailboxReadRequest>(request), cancellationToken);
        return JsonSerializer.SerializeToElement(result, GmailCapabilityJson.Options);
    }
}
internal sealed class GmailSendProposalCapabilityHandler : ICapabilityHandler
{
    public string CapabilityId => GoogleCapabilityIds.GmailSendPropose;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.ExternalEffect;
    public Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var proposal = GmailCapabilityJson.Read<GmailSendProposalRequest>(request);
        var send = new GmailSendRequest(proposal.Recipient, proposal.Subject, proposal.Body, proposal.LogicalOperationKey);
        GmailSendRequestValidator.Validate(send);
        return Task.FromResult(JsonSerializer.SerializeToElement(send, GmailCapabilityJson.Options));
    }
}
internal sealed class GmailSendEffectHandler(IGrainFactory grains) : IInoEffectHandler
{
    public string ToolId => GmailTools.Send;
    public async Task<InoToolEffectResult> ApplyAsync(string actorScope, byte[] payloadUtf8, CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<GmailSendRequest>(payloadUtf8, GmailCapabilityJson.Options)
                      ?? throw new RuntimeStateIntegrityException("Gmail effect plan payload is empty");
        var result = await grains.GetGrain<IGmailMutationToolGrain>(actorScope).SendAsync(request, cancellationToken);
        return result.Status switch
        {
            GmailSendStatus.Applied => new(InoToolEffectDisposition.Succeeded, "The approved email was sent."),
            GmailSendStatus.AlreadyApplied => new(InoToolEffectDisposition.Succeeded, "The approved email was already sent."),
            GmailSendStatus.Unavailable => new(InoToolEffectDisposition.OutcomeUnknown, "The email outcome could not be confirmed."),
            GmailSendStatus.NeedsAuth => new(InoToolEffectDisposition.Failed, "Reconnect Gmail before sending email."),
            GmailSendStatus.ConfigurationMissing => new(InoToolEffectDisposition.Failed, "Gmail is not configured."),
            _ => new(InoToolEffectDisposition.Failed, "The approved email request was rejected.")
        };
    }
}
