using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Capabilities;

public enum CapabilityOperationKind
{
    Query,
    InternalWrite,
    ExternalEffect
}

public sealed record CapabilityGrant
{
    [JsonConstructor]
    public CapabilityGrant(
        BrainOwnerId ownerId,
        FeatureInstallationId installationId,
        ReleaseDigest releaseDigest,
        string capabilityId,
        int capabilityVersion,
        ProviderConnectionId? providerConnectionId,
        GrantRevision revision,
        JsonElement constraints,
        bool enabled,
        bool paused)
    {
        OwnerId = ownerId;
        InstallationId = installationId;
        ReleaseDigest = releaseDigest;
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);
        CapabilityId = capabilityId;
        ArgumentOutOfRangeException.ThrowIfLessThan(capabilityVersion, 1);
        CapabilityVersion = capabilityVersion;
        ProviderConnectionId = providerConnectionId;
        Revision = revision;
        Constraints = CapabilityPayload.CopyBounded(constraints, nameof(constraints));
        Enabled = enabled;
        Paused = paused;
    }

    public BrainOwnerId OwnerId { get; }
    public FeatureInstallationId InstallationId { get; }
    public ReleaseDigest ReleaseDigest { get; }
    public string CapabilityId { get; }
    public int CapabilityVersion { get; }
    public ProviderConnectionId? ProviderConnectionId { get; }
    public GrantRevision Revision { get; }
    public JsonElement Constraints { get; }
    public bool Enabled { get; }
    public bool Paused { get; }

    public bool AllowsTool(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId) || Constraints.ValueKind != JsonValueKind.Object ||
            !Constraints.TryGetProperty("allowedToolIds", out var allowed) || allowed.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var candidate in allowed.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.String &&
                string.Equals(candidate.GetString(), toolId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

public interface ICapabilityHandler
{
    string CapabilityId { get; }
    int CapabilityVersion { get; }
    CapabilityOperationKind OperationKind { get; }
    Task<JsonElement> ExecuteAsync(
        CapabilityRequest request,
        CapabilityGrant grant,
        CancellationToken cancellationToken = default);
}

public sealed record CapabilityDispatchResult
{
    [JsonConstructor]
    public CapabilityDispatchResult(CapabilityOperationKind kind, JsonElement payload)
    {
        Kind = kind;
        Payload = CapabilityPayload.CopyBounded(payload, nameof(payload));
    }

    public CapabilityOperationKind Kind { get; }
    public JsonElement Payload { get; }
}

public interface ICapabilityDispatcher
{
    Task<CapabilityDispatchResult> ExecuteAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CapabilityDeniedException() : InvalidOperationException("Capability authority denied the operation.");

public sealed record RetainedInoCapabilityPayload
{
    [JsonConstructor]
    public RetainedInoCapabilityPayload(string toolId, JsonElement arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ToolId = toolId;
        Arguments = CapabilityPayload.CopyBounded(arguments, nameof(arguments));
    }

    public string ToolId { get; }
    public JsonElement Arguments { get; }
}

internal static class CapabilityPayload
{
    internal static JsonElement CopyBounded(JsonElement value, string parameterName)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A JSON value is required.", parameterName);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > CapabilityRequest.MaximumPayloadBytes)
            throw new ArgumentException("The JSON value exceeds 64 KiB.", parameterName);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }
}

public interface IInoEffectHandler
{
    string ToolId { get; }
    Task<InoToolEffectResult> ApplyAsync(
        string actorScope,
        byte[] payloadUtf8,
        CancellationToken cancellationToken = default);
}

public interface IInoEffectExecutor
{
    bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool);
    Task<InoToolEffectResult> ExecuteAsync(
        InoToolEffectRequest request,
        CancellationToken cancellationToken = default);
}
