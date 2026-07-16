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
    string DecisionId,
    string EffectId,
    string ProviderIdempotencyKey,
    string PersistedOperationKey);
internal sealed record SalesforceFeatureExecutionEnvelope(
    int Version,
    string OwnerId,
    string ActorId,
    string InstallationId,
    string InputId,
    string LogicalOperationKey,
    string CorrelationId,
    string TraceId,
    string PersistedOperationKey,
    string DecisionId,
    string PreparedUpdateBase64)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static SalesforceFeatureExecutionEnvelope Create(
        SalesforceFeatureEffectRequest request,
        string persistedOperationKey,
        string decisionId,
        SalesforcePreparedUpdate prepared) => new(
            1,
            request.OwnerId.Value,
            request.ActorId.Value,
            request.InstallationId.Value,
            request.InputId,
            request.LogicalOperationKey,
            request.CorrelationId,
            request.TraceId,
            persistedOperationKey,
            decisionId,
            Convert.ToBase64String(prepared.Payload));

    internal byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    internal SalesforcePreparedUpdate PreparedUpdate()
    {
        if (Version != 1)
            throw new ArgumentException("The Salesforce Feature execution envelope is invalid.");
        return new SalesforcePreparedUpdate(Convert.FromBase64String(PreparedUpdateBase64));
    }

    internal static bool TryParse(byte[] payload, out SalesforceFeatureExecutionEnvelope envelope)
    {
        try
        {
            envelope = JsonSerializer.Deserialize<SalesforceFeatureExecutionEnvelope>(payload, Json)!;
            _ = envelope.PreparedUpdate();
            return envelope is not null &&
                   !string.IsNullOrWhiteSpace(envelope.OwnerId) &&
                   !string.IsNullOrWhiteSpace(envelope.ActorId) &&
                   !string.IsNullOrWhiteSpace(envelope.InstallationId) &&
                   !string.IsNullOrWhiteSpace(envelope.InputId) &&
                   !string.IsNullOrWhiteSpace(envelope.PersistedOperationKey) &&
                   !string.IsNullOrWhiteSpace(envelope.DecisionId);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            envelope = null!;
            return false;
        }
    }
}
internal sealed class SalesforceFeatureEffectRail(IFeatureGrainResolver grains, IInoEffectPlanStore plans, IInoEffectExecutor effects, TimeProvider timeProvider)
{
    public const string OutcomeKind = "salesforce.record.update.outcome.v1";
    public async Task<SalesforceFeatureEffectProposal> WaitForProposalAsync(
        SalesforceFeatureEffectRequest request,
        CancellationToken cancellationToken = default)
    {
        var deadline = timeProvider.GetUtcNow().AddSeconds(30);
        while (timeProvider.GetUtcNow() < deadline)
        {
            try
            {
                return await ProposeAsync(request, cancellationToken);
            }
            catch (KeyNotFoundException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
        throw new TimeoutException("The Salesforce Feature did not produce an approval request in time.");
    }
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
        var decisionId = "decision-" + identity;
        var execution = SalesforceFeatureExecutionEnvelope.Create(
            request,
            persistedOperationKey,
            decisionId,
            prepared);
        var approval = await plans.PrepareIdempotentAsync(identity, actorScope, operationId, SalesforceTools.UpdateRecord, execution.ToBytes(), payload.SafeSummary, payload.ExpiresAt, cancellationToken);
        return new SalesforceFeatureEffectProposal(
            request,
            approval,
            actorScope,
            operationId,
            decisionId,
            "effect-" + identity,
            "provider-" + identity,
            persistedOperationKey);
    }
    public async Task<InoToolEffectResult> ApplyAsync(SalesforceFeatureEffectProposal proposal, bool approved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!approved)
        {
            var declined = await plans.DeclineAsync(
                proposal.Approval,
                proposal.ActorScope,
                proposal.DecisionId,
                cancellationToken);
            var declinedDecision = await plans.ReadDecisionAsync(
                proposal.Approval,
                proposal.ActorScope,
                cancellationToken);
            var declinedRequest = proposal.Request;
            var declinedResolution = Resolution(proposal, declined, declinedDecision);
            await grains.Installation(declinedRequest.OwnerId, declinedRequest.InstallationId)
                .ResolveIntentAsync(declinedResolution)
                .WaitAsync(cancellationToken);
            await PublishOutcomeAsync(proposal, declinedResolution, cancellationToken);
            return declined;
        }
        if (!effects.TryAuthorizeMutation(proposal.Approval, proposal.ActorScope, out var authorized) ||
            !string.Equals(authorized.ToolId, SalesforceTools.UpdateRecord, StringComparison.Ordinal) ||
            !string.Equals(authorized.Scope, proposal.Approval.Scope, StringComparison.Ordinal) ||
            !string.Equals(authorized.SafeSummary, proposal.Approval.SafeSummary, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Signed Salesforce approval evidence is required.");
        var result = await effects.ExecuteAsync(new InoToolEffectRequest(proposal.OperationId, proposal.EffectId, SalesforceTools.UpdateRecord, proposal.Approval.Scope, proposal.ActorScope, proposal.ProviderIdempotencyKey), cancellationToken);
        var request = proposal.Request;
        var terminalDecision = await plans.ReadDecisionAsync(proposal.Approval, proposal.ActorScope, cancellationToken);
        var installation = grains.Installation(request.OwnerId, request.InstallationId);
        var resolution = Resolution(proposal, result, terminalDecision);
        await installation.ResolveIntentAsync(resolution).WaitAsync(cancellationToken);
        await PublishOutcomeAsync(proposal, resolution, cancellationToken);
        return result;
    }
    private static FeatureEffectResolution Resolution(
        SalesforceFeatureEffectProposal proposal,
        InoToolEffectResult result,
        InoEffectDecision? decision)
    {
        if (decision is null || !string.Equals(decision.ActorScope, proposal.ActorScope, StringComparison.Ordinal))
            throw new RuntimeStateIntegrityException("A durable actor-bound Salesforce terminal decision is required.");
        var expectedDisposition = Disposition(decision.TerminalKind);
        if (result.Disposition != expectedDisposition)
            throw new RuntimeStateIntegrityException("The Salesforce terminal decision does not match its durable safe result.");
        return new FeatureEffectResolution(
            proposal.PersistedOperationKey,
            decision.DecisionId,
            decision.ActorScope,
            decision.TerminalKind,
            decision.ResolvedAt,
            result.SafeResult);
    }
    private async Task PublishOutcomeAsync(
        SalesforceFeatureEffectProposal proposal,
        FeatureEffectResolution resolution,
        CancellationToken cancellationToken)
    {
        var request = proposal.Request;
        var outcomeInput = new FeatureInput(
            "salesforce-outcome-" + Digest(proposal.OperationId),
            OutcomeKind,
            JsonSerializer.Serialize(new
            {
                installationId = request.InstallationId.Value,
                inputId = request.InputId,
                logicalOperationKey = request.LogicalOperationKey,
                operationKey = resolution.OperationKey,
                decisionId = resolution.DecisionId,
                actorScope = resolution.ActorScope,
                terminalKind = resolution.TerminalKind.ToString(),
                disposition = Disposition(resolution.TerminalKind).ToString(),
                safeResult = resolution.SafeResult
            }),
            resolution.ResolvedAt,
            request.CorrelationId,
            request.TraceId,
            request.InputId,
            FeatureRunOrigin.Event,
            new FeatureRunOriginReference(null, null, OutcomeKind));
        await grains.Hub(request.OwnerId).PublishAsync(outcomeInput).WaitAsync(cancellationToken);
    }
    private static InoToolEffectDisposition Disposition(InoEffectTerminalKind terminalKind) => terminalKind switch
    {
        InoEffectTerminalKind.Approved => InoToolEffectDisposition.Succeeded,
        InoEffectTerminalKind.Declined or InoEffectTerminalKind.Expired or InoEffectTerminalKind.Failed => InoToolEffectDisposition.Failed,
        InoEffectTerminalKind.OutcomeUnknown => InoToolEffectDisposition.OutcomeUnknown,
        _ => throw new RuntimeStateIntegrityException("The Salesforce terminal decision is invalid.")
    };
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

internal sealed class SalesforceFeatureEffectApprovalGateway(SalesforceFeatureEffectRail rail)
    : IFeatureEffectApprovalGateway
{
    private const string LogicalOperationKey = "enrich-salesforce-description";
    public bool Supports(CapabilityDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var feature = descriptor.Name + " " + descriptor.Description;
        return descriptor.Origin == CapabilityOrigin.Feature &&
               feature.Contains("enrich", StringComparison.OrdinalIgnoreCase) &&
               feature.Contains("salesforce", StringComparison.OrdinalIgnoreCase) &&
               feature.Contains("gmail", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<InoToolRequest> PrepareAsync(
        FeatureEffectApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var proposal = await rail.WaitForProposalAsync(
            new SalesforceFeatureEffectRequest(
                request.OwnerId,
                request.ActorId,
                request.InstallationId,
                request.InputId,
                LogicalOperationKey,
                request.CorrelationId,
                request.TraceId),
            cancellationToken);
        return proposal.Approval;
    }
}

internal sealed class SalesforceFeatureEffectCompletion(
    IFeatureGrainResolver grains,
    TimeProvider timeProvider)
{
    internal async Task CompleteAsync(
        SalesforceFeatureExecutionEnvelope execution,
        string actorScope,
        InoToolEffectResult result,
        CancellationToken cancellationToken)
    {
        var ownerId = new BrainOwnerId(execution.OwnerId);
        var actorId = new ActorId(execution.ActorId);
        if (!string.Equals(RequestScope.Id(ownerId, actorId), actorScope, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The Salesforce Feature effect actor does not match its approval.");
        var resolvedAt = timeProvider.GetUtcNow();
        var terminalKind = result.Disposition switch
        {
            InoToolEffectDisposition.Succeeded => InoEffectTerminalKind.Approved,
            InoToolEffectDisposition.Failed => InoEffectTerminalKind.Failed,
            _ => InoEffectTerminalKind.OutcomeUnknown
        };
        var resolution = new FeatureEffectResolution(
            execution.PersistedOperationKey,
            execution.DecisionId,
            actorScope,
            terminalKind,
            resolvedAt,
            result.SafeResult);
        await grains.Installation(ownerId, new FeatureInstallationId(execution.InstallationId))
            .ResolveIntentAsync(resolution)
            .WaitAsync(cancellationToken);
        await grains.Hub(ownerId).PublishAsync(new FeatureInput(
            "salesforce-outcome-" + Digest(execution.DecisionId),
            SalesforceFeatureEffectRail.OutcomeKind,
            JsonSerializer.Serialize(new
            {
                installationId = execution.InstallationId,
                inputId = execution.InputId,
                logicalOperationKey = execution.LogicalOperationKey,
                operationKey = resolution.OperationKey,
                decisionId = resolution.DecisionId,
                actorScope = resolution.ActorScope,
                terminalKind = resolution.TerminalKind.ToString(),
                disposition = result.Disposition.ToString(),
                safeResult = resolution.SafeResult
            }),
            resolvedAt,
            execution.CorrelationId,
            execution.TraceId,
            execution.InputId,
            FeatureRunOrigin.Event,
            new FeatureRunOriginReference(null, null, SalesforceFeatureEffectRail.OutcomeKind)))
            .WaitAsync(cancellationToken);
    }

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
