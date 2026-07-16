using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
namespace DigitalBrain.Integrations.Salesforce;

internal static class SalesforceCapabilityJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static T Read<T>(CapabilityRequest request) =>
        request.Payload.Deserialize<T>(Options)
        ?? throw new ArgumentException("The Salesforce capability payload is invalid.", nameof(request));
    internal static NeuronScope Scope(CapabilityRequest request) =>
        new(new UserId(request.OwnerId.Value), request.ActorId.Value);
}
internal sealed class SalesforceRecordReadCapabilityHandler(ISalesforceApiClientFactory clients) : ICapabilityHandler
{
    public string CapabilityId => SalesforceCapabilityIds.RecordRead;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;
    public async Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        var client = await clients.CreateAsync(SalesforceCapabilityJson.Scope(request), cancellationToken);
        var result = await client.ReadRecordAsync(SalesforceCapabilityJson.Read<DigitalBrain.Integrations.Salesforce.Contracts.SalesforceRecordReadRequest>(request), cancellationToken);
        return JsonSerializer.SerializeToElement(result, SalesforceCapabilityJson.Options);
    }
}
internal sealed class SalesforceAccountSearchCapabilityHandler(ISalesforceApiClientFactory clients) : ICapabilityHandler
{
    public string CapabilityId => SalesforceCapabilityIds.AccountSearch;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;
    public async Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        var client = await clients.CreateAsync(SalesforceCapabilityJson.Scope(request), cancellationToken);
        var result = await client.SearchAccountsAsync(SalesforceCapabilityJson.Read<SalesforceAccountSearchRequest>(request), cancellationToken);
        return JsonSerializer.SerializeToElement(result, SalesforceCapabilityJson.Options);
    }
}
internal sealed class SalesforceUpdateProposalCapabilityHandler(ISalesforceMutationGateway gateway, TimeProvider timeProvider) : ICapabilityHandler
{
    public string CapabilityId => SalesforceCapabilityIds.RecordUpdatePropose;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.ExternalEffect;
    public async Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        var proposal = SalesforceCapabilityJson.Read<SalesforceUpdateProposalRequest>(request);
        var value = proposal.NewValue.ValueKind == JsonValueKind.String ? proposal.NewValue.GetString()! : proposal.NewValue.GetRawText();
        var preview = await gateway.PreviewAsync(
            RequestScope.Id(request.OwnerId, request.ActorId),
            new SalesforceUpdatePreviewRequest(new SalesforceSemanticEntity(proposal.Record.ObjectName), proposal.Record.RecordId, new SalesforceSemanticField(proposal.Field), value),
            cancellationToken);
        if (preview.Status != SalesforceMutationStatus.Prepared || preview.PreparedUpdate is null)
            throw new InvalidOperationException(preview.SafeReason ?? "The Salesforce update could not be prepared.");
        var payload = SalesforceFeatureEffectPayload.Create(
            preview.PreparedUpdate,
            SalesforceDiffSummary.Create(proposal, preview),
            timeProvider.GetUtcNow().AddHours(24));
        return JsonSerializer.SerializeToElement(payload, SalesforceCapabilityJson.Options);
    }
}
internal static class SalesforceDiffSummary
{
    internal static string Create(
        SalesforceUpdateProposalRequest proposal,
        SalesforceMutationPreviewResult preview)
    {
        var previous = preview.OriginalValue ?? "null";
        var next = preview.CanonicalDesiredValue ?? proposal.NewValue.GetRawText();
        var summary = $"{proposal.Record.ObjectName} {proposal.Record.RecordId} · {proposal.Field}: {previous} → {next}";
        return summary.Length <= 512 ? summary : summary[..512];
    }
}
internal sealed class SalesforceUpdateEffectHandler(
    ISalesforceMutationGateway gateway,
    SalesforceFeatureEffectCompletion? completion = null) : IInoEffectHandler
{
    public string ToolId => SalesforceTools.UpdateRecord;
    public async Task<InoToolEffectResult> ApplyAsync(string actorScope, byte[] payloadUtf8, CancellationToken cancellationToken = default)
    {
        var featureEffect = SalesforceFeatureExecutionEnvelope.TryParse(payloadUtf8, out var execution);
        var prepared = featureEffect ? execution.PreparedUpdate() : new SalesforcePreparedUpdate(payloadUtf8);
        var result = await gateway.ApplyAsync(actorScope, prepared, cancellationToken);
        if (result.Status is SalesforceMutationStatus.Applied or SalesforceMutationStatus.AlreadyApplied &&
            !(await gateway.VerifyAsync(actorScope, prepared, cancellationToken)).Verified)
        {
            var unknown = new InoToolEffectResult(InoToolEffectDisposition.OutcomeUnknown, "The Salesforce update could not be confirmed.");
            if (featureEffect)
                await (completion ?? throw new InvalidOperationException("Salesforce Feature effect completion is unavailable."))
                    .CompleteAsync(execution, actorScope, unknown, cancellationToken);
            return unknown;
        }
        InoToolEffectResult outcome = result.Status switch
        {
            SalesforceMutationStatus.Applied => new(InoToolEffectDisposition.Succeeded, "The Salesforce update was applied and verified."),
            SalesforceMutationStatus.AlreadyApplied => new(InoToolEffectDisposition.Succeeded, "The Salesforce value was already present."),
            SalesforceMutationStatus.Unavailable or SalesforceMutationStatus.VerificationFailed => new(InoToolEffectDisposition.OutcomeUnknown, "The Salesforce update could not be confirmed."),
            SalesforceMutationStatus.Conflict => new(InoToolEffectDisposition.Failed, "The Salesforce record changed after preparation."),
            SalesforceMutationStatus.NeedsAuth => new(InoToolEffectDisposition.Failed, "Reconnect Salesforce before updating records."),
            SalesforceMutationStatus.ConfigurationMissing => new(InoToolEffectDisposition.Failed, "Salesforce is not configured."),
            SalesforceMutationStatus.AccessDenied => new(InoToolEffectDisposition.Failed, "Salesforce denied the update."),
            _ => new(InoToolEffectDisposition.Failed, "The Salesforce update was rejected.")
        };
        if (featureEffect)
            await (completion ?? throw new InvalidOperationException("Salesforce Feature effect completion is unavailable."))
                .CompleteAsync(execution, actorScope, outcome, cancellationToken);
        return outcome;
    }
}
