using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Memory;

internal static class MemoryCapabilityIds
{
    public const string Recall = "memory.recall";
    public const string Remember = "memory.remember";
    public const int Version = 1;
}

internal sealed class MemoryRecallCapabilityHandler(MemoryService memory) : ICapabilityHandler
{
    public string CapabilityId => MemoryCapabilityIds.Recall;
    public int CapabilityVersion => MemoryCapabilityIds.Version;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;

    public async Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        MemoryCapabilityPayload.Validate(request, grant, CapabilityId);
        var payload = MemoryCapabilityPayload.Read<RecallPayload>(request.Payload);
        var recalled = await memory.RecallAsync(
            request.OwnerId,
            request.ActorId,
            new MemoryRecallRequest(payload.Query, payload.Tags ?? [], payload.Limit ?? 20),
            request.CorrelationId,
            cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            facts = recalled.Select(fact => new
            {
                factId = fact.FactId,
                text = fact.Text,
                tags = fact.Tags,
                updatedAt = fact.UpdatedAt
            })
        });
    }

    private sealed record RecallPayload(string Query, IReadOnlyList<string>? Tags, int? Limit);
}

internal sealed class MemoryRememberCapabilityHandler(MemoryService memory, TimeProvider timeProvider) : ICapabilityHandler
{
    public string CapabilityId => MemoryCapabilityIds.Remember;
    public int CapabilityVersion => MemoryCapabilityIds.Version;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.InternalWrite;

    public async Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        MemoryCapabilityPayload.Validate(request, grant, CapabilityId);
        var payload = MemoryCapabilityPayload.Read<RememberPayload>(request.Payload);
        var status = await memory.RememberAsync(
            request.OwnerId,
            request.ActorId,
            new MemoryRememberIntent(request.LogicalOperationKey, payload.FactId, payload.Text, payload.Tags ?? []),
            request.CorrelationId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return JsonSerializer.SerializeToElement(new { status = status.ToString() });
    }

    private sealed record RememberPayload(string FactId, string Text, IReadOnlyList<string>? Tags);
}

internal static class MemoryCapabilityPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static T Read<T>(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<T>(JsonOptions) ?? throw new ArgumentException("A Memory capability payload is required.", nameof(payload));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The Memory capability payload is invalid.", nameof(payload), exception);
        }
    }

    internal static void Validate(CapabilityRequest request, CapabilityGrant grant, string capabilityId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(grant);
        if (request.OwnerId != grant.OwnerId || request.InstallationId != grant.InstallationId || request.ReleaseDigest != grant.ReleaseDigest ||
            !string.Equals(request.CapabilityId, capabilityId, StringComparison.Ordinal) ||
            !string.Equals(grant.CapabilityId, capabilityId, StringComparison.Ordinal) ||
            request.CapabilityVersion != MemoryCapabilityIds.Version ||
            grant.CapabilityVersion != MemoryCapabilityIds.Version ||
            request.ProviderConnectionId is not null || grant.ProviderConnectionId is not null)
            throw new CapabilityDeniedException();
    }
}
