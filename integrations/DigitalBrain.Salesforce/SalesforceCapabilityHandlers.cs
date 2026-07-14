using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceRecordReadCapabilityHandler(IGrainFactory grainFactory) : ICapabilityHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string CapabilityId => SalesforceCapabilityIds.RecordRead;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;

    public async Task<JsonElement> ExecuteAsync(
        CapabilityRequest request,
        CapabilityGrant grant,
        CancellationToken cancellationToken = default)
    {
        var payload = request.Payload.Deserialize<RetainedInoCapabilityPayload>(Json)
                      ?? throw new ArgumentException("Salesforce capability payload is required.", nameof(request));
        if (!grant.AllowsTool(payload.ToolId)) throw new CapabilityDeniedException();
        var salesforce = grainFactory.GetGrain<ISalesforceReadToolGrain>(RequestScope.Id(request.OwnerId, request.ActorId));
        var result = payload.ToolId switch
        {
            SalesforceTools.ReadCurrentProfile => await salesforce.ReadCurrentProfileAsync(cancellationToken).ConfigureAwait(false),
            SalesforceTools.DiscoverObjects => await salesforce.DiscoverObjectsAsync(
                Required<SalesforceDiscoveryRequest>(payload.Arguments), cancellationToken).ConfigureAwait(false),
            SalesforceTools.ReadRecords => await salesforce.ReadRecordsAsync(
                Required<DigitalBrain.Kernel.Runtime.SalesforceRecordReadRequest>(payload.Arguments), cancellationToken).ConfigureAwait(false),
            SalesforceTools.SearchRecords => await salesforce.SearchRecordsAsync(
                Required<SalesforceSearchRequest>(payload.Arguments), cancellationToken).ConfigureAwait(false),
            SalesforceTools.AggregateRecords => await salesforce.AggregateRecordsAsync(
                Required<SalesforceAggregateRequest>(payload.Arguments), cancellationToken).ConfigureAwait(false),
            SalesforceTools.ContinueRecords => await salesforce.ContinueRecordsAsync(
                Required<SalesforceContinuationRequest>(payload.Arguments), cancellationToken).ConfigureAwait(false),
            _ => throw new CapabilityDeniedException()
        };
        return JsonSerializer.SerializeToElement(result, Json);
    }

    private static T Required<T>(JsonElement value) =>
        value.Deserialize<T>(Json) ?? throw new ArgumentException("Salesforce capability arguments are invalid.");
}

public sealed class SalesforceUpdateProposalCapabilityHandler(IGrainFactory grainFactory) : ICapabilityHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string CapabilityId => SalesforceCapabilityIds.RecordUpdatePropose;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.ExternalEffect;

    public async Task<JsonElement> ExecuteAsync(
        CapabilityRequest request,
        CapabilityGrant grant,
        CancellationToken cancellationToken = default)
    {
        var payload = request.Payload.Deserialize<RetainedInoCapabilityPayload>(Json)
                      ?? throw new ArgumentException("Salesforce proposal payload is required.", nameof(request));
        if (!grant.AllowsTool(payload.ToolId) ||
            !string.Equals(payload.ToolId, SalesforceTools.UpdateRecord, StringComparison.Ordinal))
            throw new CapabilityDeniedException();
        var preview = payload.Arguments.Deserialize<SalesforceUpdatePreviewRequest>(Json)
                      ?? throw new ArgumentException("Salesforce update proposal is invalid.", nameof(request));
        var result = await grainFactory
            .GetGrain<ISalesforceMutationToolGrain>(RequestScope.Id(request.OwnerId, request.ActorId))
            .PreviewUpdateAsync(preview, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToElement(result, Json);
    }
}

public sealed class SalesforceUpdateEffectHandler(ISalesforceMutationGateway gateway) : IInoEffectHandler
{
    public string ToolId => SalesforceTools.UpdateRecord;

    public async Task<InoToolEffectResult> ApplyAsync(
        string actorScope,
        byte[] payloadUtf8,
        CancellationToken cancellationToken = default)
    {
        var prepared = new SalesforcePreparedUpdate(payloadUtf8);
        var result = await gateway.ApplyAsync(actorScope, prepared, cancellationToken).ConfigureAwait(false);
        if (result.Status is SalesforceMutationStatus.Applied or SalesforceMutationStatus.AlreadyApplied)
        {
            var verification = await gateway.VerifyAsync(actorScope, prepared, cancellationToken).ConfigureAwait(false);
            if (!verification.Verified)
                return new InoToolEffectResult(
                    InoToolEffectDisposition.OutcomeUnknown,
                    "The Salesforce update could not be confirmed. Review the record before trying again.");
        }
        return result.Status switch
        {
            SalesforceMutationStatus.Applied => new(
                InoToolEffectDisposition.Succeeded,
                "The approved Salesforce field update was applied and verified."),
            SalesforceMutationStatus.AlreadyApplied => new(
                InoToolEffectDisposition.Succeeded,
                "The approved Salesforce value was already present; no duplicate update was made."),
            SalesforceMutationStatus.Unavailable or SalesforceMutationStatus.VerificationFailed => new(
                InoToolEffectDisposition.OutcomeUnknown,
                "The Salesforce update could not be confirmed. Review the record before trying again."),
            SalesforceMutationStatus.Conflict => new(
                InoToolEffectDisposition.Failed,
                "The Salesforce record changed after approval was prepared. No update was applied."),
            SalesforceMutationStatus.NeedsAuth => new(
                InoToolEffectDisposition.Failed,
                "The Salesforce connection is no longer ready. No retry was attempted."),
            SalesforceMutationStatus.ConfigurationMissing => new(
                InoToolEffectDisposition.Failed,
                "Salesforce is not configured for this workspace. No update was applied."),
            SalesforceMutationStatus.AccessDenied => new(
                InoToolEffectDisposition.Failed,
                "Salesforce denied the approved field update. No update was applied."),
            _ => new(
                InoToolEffectDisposition.Failed,
                "The approved Salesforce update was rejected before it could be applied.")
        };
    }
}
