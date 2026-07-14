using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
namespace DigitalBrain.Integrations.Salesforce;

internal sealed record SalesforceFeatureEffectPayload(int Version, string PreparedUpdateBase64, string SafeSummary, DateTimeOffset ExpiresAt)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static SalesforceFeatureEffectPayload Create(SalesforcePreparedUpdate preparedUpdate, string safeSummary, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(preparedUpdate);
        Validate(preparedUpdate.Payload, safeSummary, expiresAt);
        return new(1, Convert.ToBase64String(preparedUpdate.Payload), safeSummary, expiresAt);
    }
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    internal SalesforcePreparedUpdate PreparedUpdate()
    {
        if (Version != 1) throw new ArgumentException("The Salesforce feature effect version is invalid.");
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(PreparedUpdateBase64);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The Salesforce feature effect payload is invalid.", exception);
        }
        Validate(payload, SafeSummary, ExpiresAt);
        return new SalesforcePreparedUpdate(payload);
    }
    internal static SalesforceFeatureEffectPayload Parse(string json)
    {
        var payload = JsonSerializer.Deserialize<SalesforceFeatureEffectPayload>(json, Json)
                      ?? throw new ArgumentException("The Salesforce feature effect payload is required.");
        _ = payload.PreparedUpdate();
        return payload;
    }
    private static void Validate(byte[] payload, string safeSummary, DateTimeOffset expiresAt)
    {
        if (payload is not { Length: > 0 and <= 65_536 })
            throw new ArgumentException("The prepared Salesforce update must be bounded.", nameof(payload));
        ArgumentException.ThrowIfNullOrWhiteSpace(safeSummary);
        if (safeSummary.Length > 512 || safeSummary.Any(char.IsControl) ||
            !string.Equals(safeSummary, safeSummary.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("The Salesforce effect summary must be bounded.", nameof(safeSummary));
        if (expiresAt == default || expiresAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("The Salesforce effect expiry must be UTC.", nameof(expiresAt));
    }
}
internal sealed record SalesforceFeatureEffectRequest(
    BrainOwnerId OwnerId,
    ActorId ActorId,
    FeatureInstallationId InstallationId,
    string InputId,
    string LogicalOperationKey,
    string CorrelationId,
    string TraceId);
internal sealed record SalesforceFeatureEffectProposal(
    SalesforceFeatureEffectRequest Request,
    InoToolRequest Approval,
    string ActorScope,
    string OperationId,
    string EffectId,
    string ProviderIdempotencyKey,
    string PersistedOperationKey);
internal sealed class SalesforceFeatureEffectRail(IFeatureGrainResolver grains, IInoEffectPlanStore plans, IInoEffectExecutor effects, TimeProvider timeProvider)
{
    public const string OutcomeKind = "salesforce.record.update.outcome.v1";
    public async Task<SalesforceFeatureEffectProposal> ProposeAsync(SalesforceFeatureEffectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var persistedOperationKey = FeatureIntentKeys.Create(request.InstallationId, request.InputId, request.LogicalOperationKey);
        var installation = grains.Installation(request.OwnerId, request.InstallationId);
        var intent = (await installation.ListPendingIntentsAsync().WaitAsync(cancellationToken)).SingleOrDefault(candidate =>
                candidate.Kind == FeatureIntentKind.ExternalEffect &&
                string.Equals(candidate.OperationKey, persistedOperationKey, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("The pending Salesforce feature effect does not exist.");
        var payload = SalesforceFeatureEffectPayload.Parse(EffectPayload(intent.PayloadJson));
        var prepared = payload.PreparedUpdate();
        var now = timeProvider.GetUtcNow();
        if (payload.ExpiresAt <= now || payload.ExpiresAt > now.AddHours(24))
            throw new InvalidOperationException("The Salesforce feature effect approval window is invalid.");
        var actorScope = RequestScope.Id(request.OwnerId, request.ActorId);
        var identity = Digest(request.OwnerId.Value, request.ActorId.Value, request.InstallationId.Value, request.InputId, request.LogicalOperationKey);
        var operationId = "feature-" + identity;
        var approval = await plans.PrepareIdempotentAsync(identity, actorScope, operationId, SalesforceTools.UpdateRecord, prepared.Payload, payload.SafeSummary, payload.ExpiresAt, cancellationToken);
        return new SalesforceFeatureEffectProposal(request, approval, actorScope, operationId, "effect-" + identity, "provider-" + identity, persistedOperationKey);
    }
    public async Task<InoToolEffectResult> ApplyAsync(SalesforceFeatureEffectProposal proposal, bool approved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!approved)
            return new InoToolEffectResult(InoToolEffectDisposition.Failed, "The Salesforce update was not approved. No external action was performed.");
        if (!effects.TryAuthorizeMutation(proposal.Approval, proposal.ActorScope, out var authorized) ||
            !string.Equals(authorized.ToolId, SalesforceTools.UpdateRecord, StringComparison.Ordinal) ||
            !string.Equals(authorized.Scope, proposal.Approval.Scope, StringComparison.Ordinal) ||
            !string.Equals(authorized.SafeSummary, proposal.Approval.SafeSummary, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Signed Salesforce approval evidence is required.");
        var result = await effects.ExecuteAsync(new InoToolEffectRequest(proposal.OperationId, proposal.EffectId, SalesforceTools.UpdateRecord, proposal.Approval.Scope, proposal.ActorScope, proposal.ProviderIdempotencyKey), cancellationToken);
        var request = proposal.Request;
        await grains.Installation(request.OwnerId, request.InstallationId).ApplyIntentAsync(proposal.PersistedOperationKey).WaitAsync(cancellationToken);
        var outcomeInput = new FeatureInput(
            "salesforce-outcome-" + Digest(proposal.OperationId),
            OutcomeKind,
            JsonSerializer.Serialize(new
            {
                installationId = request.InstallationId.Value,
                inputId = request.InputId,
                logicalOperationKey = request.LogicalOperationKey,
                disposition = result.Disposition.ToString()
            }),
            timeProvider.GetUtcNow(),
            request.CorrelationId,
            request.TraceId,
            request.InputId);
        await grains.Hub(request.OwnerId).PublishAsync(outcomeInput).WaitAsync(cancellationToken);
        return result;
    }
    private static void ValidateRequest(SalesforceFeatureEffectRequest request)
    {
        Validate(request.InstallationId.Value, nameof(request.InstallationId));
        Validate(request.InputId, nameof(request.InputId));
        Validate(request.LogicalOperationKey, nameof(request.LogicalOperationKey));
        Validate(request.CorrelationId, nameof(request.CorrelationId));
        Validate(request.TraceId, nameof(request.TraceId));
    }
    private static void Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(char.IsControl) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical feature effect identifier is required.", parameterName);
    }
    private static string Digest(params string[] values)
    {
        var canonical = new StringBuilder();
        foreach (var value in values)
            canonical.Append(value.Length).Append(':').Append(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
    private static string EffectPayload(string intentJson)
    {
        using var document = JsonDocument.Parse(intentJson);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("payload", out var payload)
            ? payload.GetRawText()
            : intentJson;
    }
}
