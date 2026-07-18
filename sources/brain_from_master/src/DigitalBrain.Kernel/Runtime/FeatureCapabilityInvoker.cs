using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public enum FeatureCapabilityInvocationStatus
{
    Started,
    Busy,
    Unavailable
}

public sealed record FeatureCapabilityInvocationResult(
    FeatureCapabilityInvocationStatus Status,
    InoToolRequest? ToolRequest = null);

public sealed class FeatureCapabilityOutcomeUnknownException()
    : Exception("The Feature invocation outcome could not be confirmed.");

public sealed record FeatureCapabilityInvocation(
    CapabilityDescriptor Descriptor,
    FeatureCapabilityBinding Binding,
    BrainOwnerId OwnerId,
    ActorId ActorId,
    string OperationId,
    string ConversationId,
    string RequestId,
    DateTimeOffset OccurredAt,
    RetainedInoCapabilityPayload Payload);

public interface IFeatureCapabilityInvoker
{
    Task<FeatureCapabilityInvocationResult> InvokeAsync(
        FeatureCapabilityInvocation invocation,
        CancellationToken cancellationToken = default);
}

public interface IFeatureRunGateway
{
    Task<FeatureAppendStatus> StartAsync(
        StartFeatureRun command,
        CancellationToken cancellationToken = default);
}

internal sealed class FeatureRunGateway(IFeatureGrainResolver grains) : IFeatureRunGateway
{
    public async Task<FeatureAppendStatus> StartAsync(
        StartFeatureRun command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return await grains.Hub(command.OwnerId).StartFeatureRunAsync(command).ConfigureAwait(false);
    }
}

internal sealed class FeatureCapabilityInvoker : IFeatureCapabilityInvoker
{
    private const int MaximumFacts = 32;
    private const int MaximumFactNameLength = 128;
    private const int MaximumFactValueBytes = 4_096;
    private const int MaximumPayloadBytes = 64 * 1_024;
    private const int MaximumInputKindLength = 128;
    private readonly IFeatureRunGateway _gateway;
    private readonly IOwnerConnectionHealth _connectionHealth;
    private readonly IFeatureEffectApprovalGateway[] _approvalGateways;

    public FeatureCapabilityInvoker(
        IFeatureRunGateway gateway,
        IOwnerConnectionHealth connectionHealth,
        IEnumerable<IFeatureEffectApprovalGateway> approvalGateways)
    {
        _gateway = gateway;
        _connectionHealth = connectionHealth;
        _approvalGateways = approvalGateways.ToArray();
    }

    internal FeatureCapabilityInvoker(
        IFeatureRunGateway gateway,
        IOwnerConnectionHealth connectionHealth)
        : this(gateway, connectionHealth, [])
    {
    }

    public async Task<FeatureCapabilityInvocationResult> InvokeAsync(
        FeatureCapabilityInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ValidateInvocation(invocation);
        var requiredConnections = invocation.Binding.RequiredConnections
            .Distinct()
            .OrderBy(static connection => connection.Provider, StringComparer.Ordinal)
            .ThenBy(static connection => connection.ConnectionId?.Value, StringComparer.Ordinal)
            .ToArray();
        var healthy = await _connectionHealth.ReadHealthyAsync(
            invocation.OwnerId,
            requiredConnections,
            cancellationToken).ConfigureAwait(false);
        if (requiredConnections.Any(connection => !healthy.Contains(connection)))
            return new FeatureCapabilityInvocationResult(FeatureCapabilityInvocationStatus.Unavailable);

        var input = new FeatureInput(
            StableId("ino-input-", invocation, includeConversation: true),
            invocation.Binding.InputKind,
            CanonicalPayload(invocation.Payload.Arguments),
            invocation.OccurredAt.ToUniversalTime(),
            StableId("ino-correlation-", invocation, includeOperation: false),
            StableId("ino-trace-", invocation, includeConversation: false),
            StableId("ino-causation-", invocation, includeConversation: false, includeOperation: false),
            FeatureRunOrigin.Chat,
            new FeatureRunOriginReference(invocation.ConversationId, invocation.RequestId, null));
        var command = new StartFeatureRun(
            invocation.Binding.OwnerId,
            invocation.Binding.ActorId,
            invocation.Binding.InstallationId,
            invocation.Binding.Release,
            invocation.Binding.GrantRevision,
            invocation.Binding.PublicationFence,
            invocation.Binding.AuthorityDigest,
            invocation.Binding.AccessDigest,
            input);
        try
        {
            var status = await _gateway.StartAsync(command, cancellationToken).ConfigureAwait(false);
            if (status is not (FeatureAppendStatus.Accepted or FeatureAppendStatus.Duplicate))
                return new FeatureCapabilityInvocationResult(FeatureCapabilityInvocationStatus.Unavailable);
            var approvalGateway = _approvalGateways.SingleOrDefault(candidate => candidate.Supports(invocation.Descriptor));
            if (approvalGateway is null)
                return new FeatureCapabilityInvocationResult(FeatureCapabilityInvocationStatus.Started);
            var toolRequest = await approvalGateway.PrepareAsync(
                new FeatureEffectApprovalRequest(
                    invocation.Descriptor,
                    invocation.OwnerId,
                    invocation.ActorId,
                    invocation.Binding.InstallationId,
                    input.InputId,
                    input.CorrelationId,
                    input.TraceId),
                cancellationToken).ConfigureAwait(false);
            return status switch
            {
                FeatureAppendStatus.Accepted or FeatureAppendStatus.Duplicate =>
                    new FeatureCapabilityInvocationResult(FeatureCapabilityInvocationStatus.Started, toolRequest),
                _ => new FeatureCapabilityInvocationResult(FeatureCapabilityInvocationStatus.Unavailable)
            };
        }
        catch (FeatureCommandRejectedException exception)
            when (exception.Reason == FeatureCommandRejectionReason.Conflict)
        {
            throw new FeatureCapabilityOutcomeUnknownException();
        }
        catch (FeatureCommandRejectedException)
        {
            return new FeatureCapabilityInvocationResult(FeatureCapabilityInvocationStatus.Unavailable);
        }
        catch (FeatureAuthorityRejectedException)
        {
            return new FeatureCapabilityInvocationResult(FeatureCapabilityInvocationStatus.Unavailable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FeatureCapabilityOutcomeUnknownException();
        }
    }

    private static void ValidateInvocation(FeatureCapabilityInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(invocation.Descriptor);
        ArgumentNullException.ThrowIfNull(invocation.Binding);
        ArgumentNullException.ThrowIfNull(invocation.Payload);
        if (invocation.Descriptor.Origin != CapabilityOrigin.Feature || !invocation.Descriptor.Available)
            throw new ArgumentException("An available Feature capability is required.", nameof(invocation));
        if (!string.Equals(
                invocation.Descriptor.Id,
                OwnerCapabilityCatalog.FeatureDescriptorId(invocation.Binding.InstallationId),
                StringComparison.Ordinal) || invocation.Descriptor.Version != 1)
            throw new ArgumentException("The selected Feature capability does not match its installation binding.", nameof(invocation));
        if (invocation.OwnerId != invocation.Binding.OwnerId || invocation.ActorId != invocation.Binding.ActorId)
            throw new ArgumentException("The Feature capability binding does not belong to this owner and actor.", nameof(invocation));
        if (!string.Equals(invocation.Payload.ToolId, invocation.Descriptor.Id, StringComparison.Ordinal))
            throw new ArgumentException("The retained payload does not match the selected Feature capability.", nameof(invocation));
        if (invocation.Binding.RequiredConnections is null ||
            invocation.Binding.RequiredConnections.Any(static connection =>
                connection is null || connection.ConnectionId is null || string.IsNullOrWhiteSpace(connection.Provider)) ||
            !invocation.Descriptor.RequiredConnections.ToHashSet(StringComparer.Ordinal)
                .SetEquals(invocation.Binding.RequiredConnections.Select(static connection => connection.Provider)))
            throw new ArgumentException("The Feature capability connection binding is inconsistent.", nameof(invocation));
        if (string.IsNullOrWhiteSpace(invocation.Binding.InputKind) ||
            invocation.Binding.InputKind.Length > MaximumInputKindLength)
            throw new ArgumentException("The Feature input kind is invalid.", nameof(invocation));
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.ConversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocation.RequestId);
        if (invocation.OccurredAt == default)
            throw new ArgumentException("The originating occurrence time is required.", nameof(invocation));
    }

    private static string CanonicalPayload(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Feature arguments must be a JSON object.", nameof(arguments));
        var properties = arguments.EnumerateObject().ToArray();
        if (properties.Length > MaximumFacts)
            throw new ArgumentException("Feature arguments cannot contain more than 32 facts.", nameof(arguments));
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (!unique.Add(property.Name))
                throw new ArgumentException("Feature argument names must be unique.", nameof(arguments));
            if (string.IsNullOrWhiteSpace(property.Name) || property.Name.Length > MaximumFactNameLength)
                throw new ArgumentException("Feature argument names must contain 1 to 128 characters.", nameof(arguments));
            var valueBytes = property.Value.ValueKind == JsonValueKind.String
                ? Encoding.UTF8.GetByteCount(property.Value.GetString() ?? string.Empty)
                : CanonicalValue(property.Value).Length;
            if (valueBytes > MaximumFactValueBytes)
                throw new ArgumentException("Feature argument values cannot exceed 4096 UTF-8 bytes.", nameof(arguments));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var property in properties.OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        if (buffer.WrittenCount > MaximumPayloadBytes)
            throw new ArgumentException("Feature arguments cannot exceed 64 KiB.", nameof(arguments));
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static byte[] CanonicalValue(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            {
                if (!names.Add(property.Name))
                    throw new ArgumentException("JSON object property names must be unique.", nameof(value));
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
                WriteCanonical(writer, item);
            writer.WriteEndArray();
            return;
        }
        value.WriteTo(writer);
    }

    private static string StableId(
        string prefix,
        FeatureCapabilityInvocation invocation,
        bool includeConversation = true,
        bool includeOperation = true)
    {
        var canonical = new StringBuilder();
        Append(canonical, invocation.OwnerId.Value);
        Append(canonical, invocation.ActorId.Value);
        if (includeConversation)
            Append(canonical, invocation.ConversationId);
        if (includeOperation)
            Append(canonical, invocation.OperationId);
        Append(canonical, invocation.RequestId);
        return prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Append(StringBuilder target, string value) =>
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
}
