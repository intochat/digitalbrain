using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.Google;

public sealed class GmailMailboxCapabilityHandler(IGrainFactory grainFactory) : ICapabilityHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string CapabilityId => GoogleCapabilityIds.GmailMailboxRead;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;

    public async Task<JsonElement> ExecuteAsync(
        CapabilityRequest request,
        CapabilityGrant grant,
        CancellationToken cancellationToken = default)
    {
        var payload = request.Payload.Deserialize<RetainedInoCapabilityPayload>(Json)
                      ?? throw new ArgumentException("Gmail capability payload is required.", nameof(request));
        if (!grant.AllowsTool(payload.ToolId)) throw new CapabilityDeniedException();
        var actorScope = RequestScope.Id(request.OwnerId, request.ActorId);
        var gmail = grainFactory.GetGrain<IGmailMetadataToolGrain>(actorScope);
        object result = payload.ToolId switch
        {
            GmailTools.ReadMessages => await gmail.ReadMessagesAsync(
                Required<DigitalBrain.Kernel.Runtime.GmailMessageListRequest>(payload.Arguments), cancellationToken).ConfigureAwait(false),
            GmailTools.ReadMailboxOverview => await gmail.ReadMailboxOverviewAsync(cancellationToken).ConfigureAwait(false),
            GmailTools.ReadThreads => await gmail.ReadThreadsAsync(
                Required<DigitalBrain.Kernel.Runtime.GmailThreadListRequest>(payload.Arguments), cancellationToken).ConfigureAwait(false),
            _ => throw new CapabilityDeniedException()
        };
        return JsonSerializer.SerializeToElement(result, result.GetType(), Json);
    }

    private static T Required<T>(JsonElement value) =>
        value.Deserialize<T>(Json) ?? throw new ArgumentException("Gmail capability arguments are invalid.");
}

public sealed class GmailSendProposalCapabilityHandler : ICapabilityHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string CapabilityId => GoogleCapabilityIds.GmailSendPropose;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.ExternalEffect;

    public Task<JsonElement> ExecuteAsync(
        CapabilityRequest request,
        CapabilityGrant grant,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = request.Payload.Deserialize<RetainedInoCapabilityPayload>(Json)
                      ?? throw new ArgumentException("Gmail proposal payload is required.", nameof(request));
        if (!grant.AllowsTool(payload.ToolId) || !string.Equals(payload.ToolId, GmailTools.Send, StringComparison.Ordinal))
            throw new CapabilityDeniedException();
        var send = payload.Arguments.Deserialize<GmailSendRequest>(Json)
                   ?? throw new ArgumentException("Gmail send proposal is invalid.", nameof(request));
        GmailSendRequestValidator.Validate(send);
        return Task.FromResult(JsonSerializer.SerializeToElement(send, Json));
    }
}

public sealed class GmailSendEffectHandler(IGrainFactory grainFactory) : IInoEffectHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string ToolId => GmailTools.Send;

    public async Task<InoToolEffectResult> ApplyAsync(
        string actorScope,
        byte[] payloadUtf8,
        CancellationToken cancellationToken = default)
    {
        var request = JsonSerializer.Deserialize<GmailSendRequest>(payloadUtf8, Json)
                      ?? throw new RuntimeStateIntegrityException("Gmail effect plan payload is empty");
        var result = await grainFactory.GetGrain<IGmailMutationToolGrain>(actorScope)
            .SendAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            GmailSendStatus.Applied => new(
                InoToolEffectDisposition.Succeeded,
                "The approved email was sent."),
            GmailSendStatus.AlreadyApplied => new(
                InoToolEffectDisposition.Succeeded,
                "The approved email had already been sent; no duplicate was created."),
            GmailSendStatus.Unavailable => new(
                InoToolEffectDisposition.OutcomeUnknown,
                "The approved email could not be confirmed. Check Sent mail before trying again."),
            GmailSendStatus.NeedsAuth => new(
                InoToolEffectDisposition.Failed,
                "The Gmail connection is no longer ready. No retry was attempted."),
            GmailSendStatus.ConfigurationMissing => new(
                InoToolEffectDisposition.Failed,
                "Gmail is not configured for this workspace. No email was sent."),
            _ => new(
                InoToolEffectDisposition.Failed,
                "The approved email request was rejected before it could be sent.")
        };
    }
}
