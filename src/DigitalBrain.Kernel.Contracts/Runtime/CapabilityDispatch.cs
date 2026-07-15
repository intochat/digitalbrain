using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
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
        Constraints = CapabilityGrantConstraintPolicy.CopyValidated(constraints);
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
    internal bool Allows(CapabilityRequest request) => CapabilityGrantConstraintPolicy.Allows(Constraints, request);
}
public static class CapabilityGrantConstraintPolicy
{
    private const int MaximumAllowedValues = 256;
    private const int MaximumObjectProperties = 128;
    private static readonly HashSet<string> CredentialPropertyNames = new(StringComparer.Ordinal)
    {
        "password",
        "accesstoken",
        "refreshtoken",
        "authorization",
        "apikey",
        "privatekey",
        "credential",
        "credentials",
        "token",
        "clientsecret",
        "secret",
        "secretvalue",
        "actiontoken",
        "authtoken",
        "bearertoken",
        "idtoken",
        "sessiontoken",
        "secretkey",
        "connectionstring",
        "passphrase",
        "authorizationcode",
        "codeverifier",
        "secretaccesskey",
        "privatekeypem",
        "sastoken",
        "sessionid"
    };
    public static JsonElement CopyValidated(JsonElement constraints)
    {
        var copy = CapabilityPayload.CopyBounded(constraints, nameof(constraints));
        if (copy.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Capability constraints must be a JSON object.", nameof(constraints));
        ValidateObject(copy, root: true);
        if (!copy.TryGetProperty("allowedToolIds", out var tools) || tools.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Capability constraints require an allowedToolIds array.", nameof(constraints));
        var allowed = tools.EnumerateArray().ToArray();
        if (allowed.Length is 0 or > MaximumAllowedValues || allowed.Any(item =>
                item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) ||
                item.GetString()!.Length > 256 || item.GetString()!.Any(char.IsControl) ||
                !string.Equals(item.GetString(), item.GetString()!.Trim(), StringComparison.Ordinal)) ||
            allowed.Select(item => item.GetString()).Distinct(StringComparer.Ordinal).Count() != allowed.Length)
            throw new ArgumentException("Capability constraints require canonical unique allowed tool identifiers.", nameof(constraints));
        if (copy.TryGetProperty("payload", out var payload))
        {
            if (payload.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Capability payload constraints must be a JSON object.", nameof(constraints));
            ValidateExpression(payload);
        }
        return copy;
    }
    internal static bool Allows(JsonElement constraints, CapabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AllowsTool(constraints, request.CapabilityId)) return false;
        return !constraints.TryGetProperty("payload", out var payload) || Matches(payload, request.Payload);
    }
    public static bool AllowsTool(JsonElement constraints, string capabilityId) =>
        constraints.GetProperty("allowedToolIds").EnumerateArray().Any(candidate =>
            string.Equals(candidate.GetString(), capabilityId, StringComparison.Ordinal));
    private static void ValidateExpression(JsonElement expression)
    {
        if (expression.ValueKind == JsonValueKind.Object)
        {
            ValidateObject(expression, root: false);
            foreach (var property in expression.EnumerateObject()) ValidateExpression(property.Value);
            return;
        }
        if (expression.ValueKind == JsonValueKind.Array)
        {
            var values = expression.EnumerateArray().ToArray();
            if (values.Length is 0 or > MaximumAllowedValues)
                throw new ArgumentException("Capability payload allowlists must contain between 1 and 256 values.", nameof(expression));
            foreach (var value in values) ValidateExpression(value);
        }
    }
    private static void ValidateObject(JsonElement value, bool root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (CredentialPropertyNames.Contains(NormalizePropertyName(property.Name)) ||
                ++count > MaximumObjectProperties || !names.Add(property.Name) || root &&
                !string.Equals(property.Name, "allowedToolIds", StringComparison.Ordinal) &&
                !string.Equals(property.Name, "payload", StringComparison.Ordinal))
                throw new ArgumentException("Capability constraints contain duplicate, unknown, or excessive properties.", nameof(value));
        }
    }
    private static string NormalizePropertyName(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static bool Matches(JsonElement constraint, JsonElement value)
    {
        if (constraint.ValueKind == JsonValueKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in constraint.EnumerateObject())
                if (!value.TryGetProperty(property.Name, out var candidate) || !Matches(property.Value, candidate)) return false;
            return true;
        }
        if (constraint.ValueKind == JsonValueKind.Array)
        {
            var allowed = constraint.EnumerateArray().ToArray();
            return value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().All(candidate => allowed.Any(item => Matches(item, candidate)))
                : allowed.Any(item => Matches(item, value));
        }
        return JsonElement.DeepEquals(constraint, value);
    }
}
public interface ICapabilityHandler
{
    string CapabilityId { get; }
    int CapabilityVersion { get; }
    CapabilityOperationKind OperationKind { get; }
    Task<JsonElement> ExecuteAsync(CapabilityRequest request, CapabilityGrant grant, CancellationToken cancellationToken = default);
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
    Task<CapabilityDispatchResult> ExecuteAsync(CapabilityRequest request, CancellationToken cancellationToken = default);
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
    Task<InoToolEffectResult> ApplyAsync(string actorScope, byte[] payloadUtf8, CancellationToken cancellationToken = default);
}
public interface IInoEffectExecutor
{
    bool TryAuthorizeMutation(InoToolRequest request, string actorScope, out InoApprovedTool tool);
    Task<InoToolEffectResult> ExecuteAsync(InoToolEffectRequest request, CancellationToken cancellationToken = default);
}
