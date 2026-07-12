using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record RuntimeFeedPage(
    IReadOnlyList<StoredSurfaceRecord> Items,
    bool ResetRequired,
    long LatestSequence);

public sealed record PreparedRuntimeFeed(
    SurfaceFeedState State,
    IReadOnlyDictionary<string, SurfaceActionToken> ActionTokens);

public sealed record AuthorizedRuntimeAction(
    ActionSubmission Submission,
    SurfaceActionBinding Binding,
    string ConversationId);

public sealed class RuntimeSurfaceFeed(
    IClusterClient cluster,
    TimeProvider timeProvider) : IActiveConversationFeed
{
    private static readonly TimeSpan CursorLifetime = TimeSpan.FromDays(30);

    public async Task ProjectConversationAsync(
        RuntimeRequestContext context,
        InoConversationSnapshot conversation,
        string projectionId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        DemandPrincipal(context);
        ConversationStateClient.DemandConversationId(conversation.ConversationId);
        var neuron = Feed(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!string.Equals(ActiveConversationId(context, state), conversation.ConversationId, StringComparison.Ordinal))
                return;
            if (state.AppliedProjectionIds.Contains(projectionId, StringComparer.Ordinal)) return;
            var projection = CreateConversationProjection(context, state, conversation, projectionId, createdAt);
            try
            {
                await neuron.ApplyProjectionAsync(state.Revision, projection, timeProvider.GetUtcNow())
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("Conversation projection revision retry exhausted.");
    }

    public async Task<string> ResolveActiveConversationIdAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken)
    {
        DemandPrincipal(context);
        var neuron = Feed(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        return ActiveConversationId(context, state);
    }

    public async Task<bool> TryActivateConversationAsync(
        RuntimeRequestContext context,
        string expectedConversationId,
        InoConversationSnapshot conversation,
        string projectionId,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        DemandPrincipal(context);
        ConversationStateClient.DemandConversationId(expectedConversationId);
        ConversationStateClient.DemandConversationId(conversation.ConversationId);
        var neuron = Feed(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var activeConversationId = ActiveConversationId(context, state);
            if (string.Equals(activeConversationId, conversation.ConversationId, StringComparison.Ordinal))
                return true;
            if (!string.Equals(activeConversationId, expectedConversationId, StringComparison.Ordinal))
                return false;

            var projection = CreateConversationProjection(context, state, conversation, projectionId, createdAt);
            try
            {
                await neuron.ApplyProjectionAsync(state.Revision, projection, timeProvider.GetUtcNow())
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        return false;
    }

    public async Task<PreparedRuntimeFeed> PrepareSessionAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken = default)
    {
        DemandPrincipal(context);
        var neuron = Feed(context);
        var state = await EnsureInitializedAsync(context, neuron, cancellationToken).ConfigureAwait(false);
        if (state.CurrentSurfaces.Length == 0)
        {
            await ProjectConversationAsync(
                context,
                InoConversationSnapshot.Empty(context),
                "bootstrap-" + RequestScope.Id(context),
                timeProvider.GetUtcNow(),
                cancellationToken).ConfigureAwait(false);
            state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var current = state.CurrentSurfaces.Single(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        var presentation = ReadPresentation(current);
        var now = timeProvider.GetUtcNow();
        var descriptors = PresentationAllowsSend(presentation.Payload)
            ? ConversationSurfacePayload.Actions(InoConversationSnapshot.Empty(context), now).ToArray()
            : ConversationSurfacePayload.LifecycleActions(now);

        var nextRevision = checked(current.SurfaceRevision + 1);
        var projectionId = "session-actions-" + Guid.NewGuid().ToString("N");
        var bindings = CreateBindings(descriptors, nextRevision, out var tokens);
        var projection = new SurfaceFeedProjection(
            projectionId,
            current.SurfaceId,
            nextRevision,
            SurfaceContentHash.Compute(presentation.Payload, descriptors),
            current.PayloadUtf8,
            now,
            null,
            bindings);
        state = await RetryConflictAsync(
            neuron,
            state,
            latest => neuron.ApplyProjectionAsync(latest.Revision, projection, timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
        return new(state, tokens);
    }

    public RuntimeFeedPage ReadPage(
        RuntimeRequestContext context,
        SurfaceFeedState state,
        long afterSequence,
        int limit)
    {
        DemandPrincipal(context);
        if (afterSequence < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        var bounded = Math.Clamp(limit, 1, 100);
        var items = state.CurrentSurfaces
            .Where(surface => surface.Sequence > afterSequence)
            .OrderBy(surface => surface.Sequence)
            .Take(bounded)
            .Select(surface => ToStoredRecord(context, state, surface))
            .ToArray();
        var reset = afterSequence > 0 && afterSequence < state.LastSequence && items.Length == 0;
        if (reset)
        {
            items = state.CurrentSurfaces
                .OrderBy(surface => surface.Sequence)
                .TakeLast(bounded)
                .Select(surface => ToStoredRecord(context, state, surface))
                .ToArray();
        }
        return new(items, reset, state.LastSequence);
    }

    public async Task<SurfaceFeedState> ReadAsync(
        RuntimeRequestContext context,
        CancellationToken cancellationToken = default) =>
        await Feed(context).ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

    public async Task RecordDeliveredAsync(
        RuntimeRequestContext context,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        var neuron = Feed(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var deliveryId = DeliveryId(context.SessionId, sequence);
        await RetryConflictAsync(
            neuron,
            state,
            current => neuron.RecordDeliveryAsync(
                current.Revision,
                deliveryId,
                sequence,
                timeProvider.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> AcknowledgeAsync(
        RuntimeRequestContext context,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        var neuron = Feed(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var deliveryId = DeliveryId(context.SessionId, sequence);
        if (!state.DeliveryDedupe.Any(delivery =>
                string.Equals(delivery.DeliveryId, deliveryId, StringComparison.Ordinal) &&
                delivery.Sequence == sequence))
            throw new InvalidOperationException("The feed sequence has not been delivered to this session.");
        var now = timeProvider.GetUtcNow();
        state = await RetryConflictAsync(
            neuron,
            state,
            current => neuron.AcknowledgeAsync(
                current.Revision,
                RuntimeStateKeys.Session(context.SessionId),
                sequence,
                now.Add(CursorLifetime),
                now),
            cancellationToken).ConfigureAwait(false);
        return state.Acknowledgements.First(cursor =>
            string.Equals(cursor.SessionScopeHash, RuntimeStateKeys.Session(context.SessionId), StringComparison.Ordinal)).Sequence;
    }

    public async Task<AuthorizedRuntimeAction> AuthorizeActionAsync(
        RuntimeRequestContext context,
        string bindingId,
        string actionToken,
        string surfaceId,
        int surfaceRevision,
        JsonElement input,
        CancellationToken cancellationToken = default)
    {
        DemandPrincipal(context);
        var neuron = Feed(context);
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var binding = DemandBinding(state, context, bindingId, surfaceId, surfaceRevision);
        var authorizedInput = AuthorizeInput(binding, input);
        var activeConversationId = ActiveConversationId(context, state);
        var idempotencyKey = "surface-action-" + Hash(
            RequestScope.Id(context) + "\0" + bindingId + "\0" + surfaceRevision + "\0" + CanonicalJson(input));
        var operationId = ConversationStateClient.OperationId(
            context with { ConversationId = activeConversationId },
            idempotencyKey);
        SurfaceActionConsumption consumption;
        try
        {
            consumption = await neuron.ConsumeActionAsync(
                state.Revision,
                bindingId,
                Hash(actionToken),
                idempotencyKey,
                operationId,
                timeProvider.GetUtcNow()).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeStateConflictException)
        {
            state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            binding = DemandBinding(state, context, bindingId, surfaceId, surfaceRevision);
            authorizedInput = AuthorizeInput(binding, input);
            activeConversationId = ActiveConversationId(context, state);
            try
            {
                consumption = await neuron.ConsumeActionAsync(
                    state.Revision,
                    bindingId,
                    Hash(actionToken),
                    idempotencyKey,
                    operationId,
                    timeProvider.GetUtcNow()).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (RuntimeStateConflictException)
            {
                throw new ActionRejectedException(ActionRejection.WrongRevision);
            }
            catch (KeyNotFoundException)
            {
                throw new ActionRejectedException(ActionRejection.Unavailable);
            }
            catch (UnauthorizedAccessException)
            {
                throw new ActionRejectedException(ActionRejection.Forged);
            }
            catch (InvalidOperationException)
            {
                throw new ActionRejectedException(ActionRejection.Replay);
            }
        }
        catch (KeyNotFoundException)
        {
            throw new ActionRejectedException(ActionRejection.Unavailable);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ActionRejectedException(ActionRejection.Forged);
        }
        catch (InvalidOperationException)
        {
            throw new ActionRejectedException(ActionRejection.Replay);
        }
        return new(
            new ActionSubmission(
                consumption.OperationId,
                idempotencyKey,
                authorizedInput,
                consumption.AuthorizedBinding.ActionType),
            consumption.AuthorizedBinding,
            activeConversationId);
    }

    private static SurfaceActionBinding DemandBinding(
        SurfaceFeedState state,
        RuntimeRequestContext context,
        string bindingId,
        string surfaceId,
        int surfaceRevision)
    {
        var binding = state.ActionBindings.FirstOrDefault(candidate =>
            string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal))
            ?? throw new ActionRejectedException(ActionRejection.Unavailable);
        if (!string.Equals(binding.SurfaceId, surfaceId, StringComparison.Ordinal) ||
            binding.SurfaceRevision != surfaceRevision ||
            state.CurrentSurfaces.FirstOrDefault(surface =>
                string.Equals(surface.SurfaceId, surfaceId, StringComparison.Ordinal))?.SurfaceRevision != surfaceRevision)
            throw new ActionRejectedException(ActionRejection.WrongRevision);
        if (!context.Grants.Contains(binding.RequiredGrant))
            throw new ActionRejectedException(ActionRejection.PolicyDenied);
        return binding;
    }

    public async Task WaitForChangeAsync(
        RuntimeRequestContext context,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await Feed(context).ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (state.LastSequence > afterSequence) return;
            await Task.Delay(TimeSpan.FromMilliseconds(250), timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SurfaceFeedState> EnsureInitializedAsync(
        RuntimeRequestContext context,
        ISurfaceFeedNeuron neuron,
        CancellationToken cancellationToken)
    {
        var state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var identity = new SurfaceFeedIdentity(context.TenantId, context.WorkspaceId, context.Principal);
        if (state.Identity is not null)
        {
            if (state.Identity != identity) throw new UnauthorizedAccessException("Surface-feed identity denied.");
            return state;
        }
        try
        {
            return await neuron.InitializeAsync(state.Revision, identity)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeStateConflictException)
        {
            state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (state.Identity != identity) throw new UnauthorizedAccessException("Surface-feed identity denied.");
            return state;
        }
    }

    private ISurfaceFeedNeuron Feed(RuntimeRequestContext context) =>
        cluster.GetGrain<ISurfaceFeedNeuron>(RuntimeStateKeys.SurfaceFeed(
            context.TenantId,
            context.WorkspaceId,
            context.Principal));

    private static SurfaceActionBinding[] CreateBindings(
        IReadOnlyList<StoredActionBinding> descriptors,
        int surfaceRevision,
        out IReadOnlyDictionary<string, SurfaceActionToken> actionTokens)
    {
        var tokens = new Dictionary<string, SurfaceActionToken>(StringComparer.Ordinal);
        var bindings = descriptors.Select(descriptor =>
        {
            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            tokens[descriptor.BindingId] = new(token, descriptor.ExpiresAt);
            return new SurfaceActionBinding(
                descriptor.BindingId,
                ConversationSurfacePayload.HomeSurfaceId,
                surfaceRevision,
                descriptor.ActionType,
                descriptor.InputSchemaRef,
                descriptor.RequiredGrant,
                descriptor.ActionSchemaVersion,
                Hash(token),
                descriptor.MaxUses,
                0,
                descriptor.ExpiresAt,
                null,
                null);
        }).ToArray();
        actionTokens = tokens;
        return bindings;
    }

    private static StoredSurfaceRecord ToStoredRecord(
        RuntimeRequestContext context,
        SurfaceFeedState state,
        SurfaceFeedRecord surface)
    {
        var presentation = ReadPresentation(surface);
        var actions = state.ActionBindings
            .Where(binding => string.Equals(binding.SurfaceId, surface.SurfaceId, StringComparison.Ordinal) &&
                              binding.SurfaceRevision == surface.SurfaceRevision)
            .Select(ToStoredBinding)
            .ToArray();
        return new(
            surface.Sequence,
            context.TenantId,
            context.WorkspaceId,
            new SurfaceAudience(SurfaceAudienceKind.Principal, PrincipalScope.Id(context.Principal)),
            surface.SurfaceId,
            surface.SurfaceRevision,
            surface.ContentHash,
            surface.CreatedAt,
            surface.ExpiresAt,
            presentation.CorrelationId,
            presentation.CauseKind,
            presentation.CauseId,
            presentation.RequiredClientCapabilities,
            presentation.Payload,
            actions,
            AudiencePrincipalKind: context.Principal.Kind);
    }

    private static StoredActionBinding ToStoredBinding(SurfaceActionBinding binding) => new(
        binding.BindingId,
        binding.ActionType,
        binding.InputSchemaRef,
        binding.RequiredGrant,
        binding.MaxUses,
        binding.ExpiresAt,
        binding.ActionSchemaVersion);

    private static SurfaceFeedProjection CreateConversationProjection(
        RuntimeRequestContext context,
        SurfaceFeedState state,
        InoConversationSnapshot conversation,
        string projectionId,
        DateTimeOffset createdAt)
    {
        var payload = ConversationSurfacePayload.Build(conversation);
        var descriptors = ConversationSurfacePayload.Actions(conversation, createdAt);
        var revision = checked((state.CurrentSurfaces.FirstOrDefault(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal))?.SurfaceRevision ?? 0) + 1);
        var persisted = new PersistedSurfacePresentation(
            context.CorrelationId,
            "conversation",
            conversation.ConversationId,
            ConversationSurfacePayload.RequiredCapabilities,
            payload);
        return new(
            projectionId,
            ConversationSurfacePayload.HomeSurfaceId,
            revision,
            SurfaceContentHash.Compute(payload, descriptors),
            JsonSerializer.SerializeToUtf8Bytes(persisted),
            createdAt,
            null,
            CreateBindings(descriptors, revision, out _));
    }

    private static string ActiveConversationId(RuntimeRequestContext context, SurfaceFeedState state)
    {
        var current = state.CurrentSurfaces.FirstOrDefault(surface =>
            string.Equals(surface.SurfaceId, ConversationSurfacePayload.HomeSurfaceId, StringComparison.Ordinal));
        if (current is null) return InoConversationIdentity.From(context);
        var presentation = ReadPresentation(current);
        if (string.Equals(presentation.CauseKind, "conversation", StringComparison.Ordinal) &&
            IsCanonicalConversationId(presentation.CauseId))
            return presentation.CauseId;
        return InoConversationIdentity.From(context);
    }

    private static bool IsCanonicalConversationId(string? value)
    {
        if (value is null || value.Length != 68 || !value.StartsWith("ino-", StringComparison.Ordinal)) return false;
        foreach (var character in value.AsSpan(4))
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        return true;
    }

    private static PersistedSurfacePresentation ReadPresentation(SurfaceFeedRecord surface)
    {
        try
        {
            return JsonSerializer.Deserialize<PersistedSurfacePresentation>(surface.PayloadUtf8)
                   ?? throw new RuntimeStateIntegrityException("empty surface presentation");
        }
        catch (JsonException)
        {
            throw new RuntimeStateIntegrityException("invalid surface presentation");
        }
    }

    private static bool PresentationAllowsSend(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("operation", out var operation) || operation.ValueKind == JsonValueKind.Null)
            return true;
        if (!operation.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String) return true;
        return !InoConversationStates.IsActive(state.GetString() ?? string.Empty);
    }

    private static JsonElement AuthorizeInput(SurfaceActionBinding binding, JsonElement input)
    {
        if (binding.ActionSchemaVersion != UiProtocol.ActionSchemaVersion)
            throw new ActionRejectedException(ActionRejection.PolicyDenied);
        if (string.Equals(binding.ActionType, ConversationSurfacePayload.SendActionType, StringComparison.Ordinal) &&
            string.Equals(binding.InputSchemaRef, ConversationSurfacePayload.SendInputSchema, StringComparison.Ordinal) &&
            TryReadPrompt(input, out var prompt))
            return JsonSerializer.SerializeToElement(new { prompt });
        if ((string.Equals(binding.ActionType, ConversationSurfacePayload.NewActionType, StringComparison.Ordinal) ||
             string.Equals(binding.ActionType, ConversationSurfacePayload.DeleteActionType, StringComparison.Ordinal)) &&
            string.Equals(binding.InputSchemaRef, ConversationSurfacePayload.EmptyInputSchema, StringComparison.Ordinal) &&
            input.ValueKind == JsonValueKind.Object && !input.EnumerateObject().Any())
            return JsonSerializer.SerializeToElement(new { });
        throw new ActionRejectedException(ActionRejection.PolicyDenied);
    }

    private static bool TryReadPrompt(JsonElement input, out string prompt)
    {
        prompt = string.Empty;
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Count() != 1 ||
            !input.TryGetProperty("prompt", out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        prompt = value.GetString()?.Trim() ?? string.Empty;
        return prompt.Length is > 0 and <= 4096;
    }

    private static string CanonicalJson(JsonElement input)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteCanonical(writer, input);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static async Task<SurfaceFeedState> RetryConflictAsync(
        ISurfaceFeedNeuron neuron,
        SurfaceFeedState initial,
        Func<SurfaceFeedState, Task<SurfaceFeedState>> update,
        CancellationToken cancellationToken)
    {
        var state = initial;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await update(state).WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (RuntimeStateConflictException) when (attempt < 2)
            {
                state = await neuron.ReadAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        throw new InvalidOperationException("Surface-feed revision retry exhausted.");
    }

    private static string DeliveryId(string sessionId, long sequence) =>
        Hash(sessionId + "\0" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void DemandPrincipal(RuntimeRequestContext context)
    {
        if (context.Principal.Kind != PrincipalKind.User || string.IsNullOrWhiteSpace(context.SessionId))
            throw new UnauthorizedAccessException("A principal session is required for the surface feed.");
    }

    private sealed record PersistedSurfacePresentation(
        string CorrelationId,
        string CauseKind,
        string CauseId,
        string[] RequiredClientCapabilities,
        JsonElement Payload);
}
