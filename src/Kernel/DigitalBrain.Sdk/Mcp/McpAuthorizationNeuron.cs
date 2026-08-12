using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Security;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Modules.Sdk.Mcp;

[GrainType("mcpauthorization")]
public sealed class McpAuthorizationNeuron :
    Neuron,
    IMcpAuthorization,
    IMcpAuthorizationCodes
{
    internal const string InstanceName = IMcpAuthorization.DefaultInstanceName;
    internal const int MaxPendingStates = 64;
    internal static readonly TimeSpan AuthorizationTtl = TimeSpan.FromMinutes(15);

    private const string PendingName = "mcp.authorization.pending";
    private const string CommandsName = "mcp.authorization.commands";
    // Wave 4: (serverKey, PrincipalId) ΓåÆ PKCE state so regenerated CommandIds still join.
    private const string SlotsName = "mcp.authorization.slots";
    private const string CodeProtectionPurposePrefix = "mcp/authorization/code";
    private const string VerifierProtectionPurposePrefix = "mcp/authorization/verifier";

    private readonly IDurableDictionary<string, byte[]> _pending;
    private readonly IDurableDictionary<Guid, byte[]> _commands;
    private readonly IDurableDictionary<string, string> _slots;
    private readonly Serializer<PendingAuthorization> _serializer;
    private readonly Serializer<CommandAuthorizationRecord> _commandsSerializer;
    private readonly IDurablePayloadProtector _protector;

    public McpAuthorizationNeuron()
    {
        _pending = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, byte[]>>(PendingName);
        _commands = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(CommandsName);
        _slots = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, string>>(SlotsName);
        _serializer = ServiceProvider.GetRequiredService<Serializer<PendingAuthorization>>();
        _commandsSerializer = ServiceProvider.GetRequiredService<Serializer<CommandAuthorizationRecord>>();
        _protector = ServiceProvider.GetRequiredService<IDurablePayloadProtector>();
    }

    public async Task<AuthorizationRequired> Begin(
        BeginMcpAuthorization request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ServerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ServerDisplayName);
        ArgumentNullException.ThrowIfNull(request.SignInUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.State);
        if (request.Actor is null)
        {
            throw new NeuronAuthorizationException("Authorization requires a verified actor.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await SweepExpiredAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (_commands.TryGetValue(request.CommandId.Value, out var commandSerialized))
        {
            var recorded = _commandsSerializer.Deserialize(commandSerialized);
            // Recovery requires the same principal that minted the pending auth.
            // Wrong principal refuses settled and looks like an unknown transaction
            // (no state / actor leak).
            if (recorded.Actor is null
                || recorded.Actor.PrincipalId != request.Actor.PrincipalId)
            {
                throw new NeuronAuthorizationException(
                    $"Authorization command '{request.CommandId}' is not pending.");
            }

            if (recorded.Outcome is PendingAuthorizationOutcome.Denied)
            {
                throw new McpAuthorizationDeniedException(
                    new AuthorizationDenied(recorded.CommandId, recorded.ServerKey, recorded.State, recorded.Actor));
            }

            return new AuthorizationRequired(
                recorded.CommandId,
                recorded.ServerKey,
                recorded.ServerDisplayName,
                recorded.SignInUrl,
                recorded.State,
                recorded.Actor);
        }

        // (serverKey, PrincipalId) slot: reuse open PKCE so a new CommandId joins the same card.
        var slotKey = SlotKey(request.ServerKey, request.Actor.PrincipalId);
        if (_slots.TryGetValue(slotKey, out var slottedState)
            && _pending.TryGetValue(slottedState, out var slottedSerialized))
        {
            var slotted = _serializer.Deserialize(slottedSerialized);
            if (IsExpired(slotted))
            {
                RemovePending(slotted);
                await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            }
            else if (slotted.Actor.PrincipalId == request.Actor.PrincipalId
                && string.Equals(slotted.ServerKey, request.ServerKey, StringComparison.OrdinalIgnoreCase)
                && slotted.Outcome is not PendingAuthorizationOutcome.Denied)
            {
                _commands[request.CommandId.Value] = _commandsSerializer.SerializeToArray(ToCommandRecord(slotted));
                await WriteStateAsync(cancellationToken).ConfigureAwait(true);
                return new AuthorizationRequired(
                    slotted.CommandId,
                    slotted.ServerKey,
                    slotted.ServerDisplayName,
                    slotted.SignInUrl,
                    slotted.State,
                    slotted.Actor);
            }
        }

        if (_pending.TryGetValue(request.State, out var existingSerialized))
        {
            var existing = _serializer.Deserialize(existingSerialized);
            if (IsExpired(existing))
            {
                RemovePending(existing);
                await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            }
            else if (existing.CommandId != request.CommandId
                || !string.Equals(existing.ServerKey, request.ServerKey, StringComparison.Ordinal)
                || existing.SignInUrl != request.SignInUrl
                || existing.Actor.PrincipalId != request.Actor.PrincipalId)
            {
                throw new InvalidOperationException(
                    $"Authorization state '{request.State}' is already pending for a different request.");
            }
            else
            {
                return new AuthorizationRequired(
                    existing.CommandId,
                    existing.ServerKey,
                    existing.ServerDisplayName,
                    existing.SignInUrl,
                    existing.State,
                    existing.Actor);
            }
        }

        if (CountOpenPending() >= MaxPendingStates)
        {
            throw new NeuronAuthorizationException(
                $"Authorization state capacity ({MaxPendingStates}) is full; complete or wait for expiry.");
        }

        var pending = new PendingAuthorization(
            request.CommandId,
            request.ServerKey,
            request.ServerDisplayName,
            request.SignInUrl,
            request.State,
            Outcome: PendingAuthorizationOutcome.Open,
            Code: null,
            Iss: null,
            CompletionTarget: null,
            CompletionNotified: false,
            RequestingNeuron: CaptureRequestingNeuron(),
            Actor: request.Actor,
            CodeChallenge: request.CodeChallenge,
            ProtectedCodeVerifier: ProtectVerifier(request.State, request.CodeVerifier),
            ExpiresAt: TimeProvider.GetUtcNow().Add(AuthorizationTtl),
            Consumed: false);
        _pending[request.State] = _serializer.SerializeToArray(pending);
        _commands[request.CommandId.Value] = _commandsSerializer.SerializeToArray(ToCommandRecord(pending));
        _slots[slotKey] = request.State;
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);

        var required = new AuthorizationRequired(
            request.CommandId,
            request.ServerKey,
            request.ServerDisplayName,
            request.SignInUrl,
            request.State,
            request.Actor);
        await EmitAsync(required).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await SendAsync(ResolvePrincipalChat(request.Actor, pending.RequestingNeuron), required)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return required;
    }

    public async Task BindCompletionTarget(
        BindMcpAuthorizationCompletionTarget request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(request));
        }

        if (request.CompletionTarget == default
            || string.IsNullOrWhiteSpace(request.CompletionTarget.Type)
            || string.IsNullOrWhiteSpace(request.CompletionTarget.Name)
            || request.CompletionTarget.Owner != Id.Owner)
        {
            throw new ArgumentException("A same-owner completion target is required.", nameof(request));
        }

        if (!_commands.TryGetValue(request.CommandId.Value, out var commandSerialized))
        {
            throw new InvalidOperationException($"Authorization command '{request.CommandId}' is not pending.");
        }

        var recorded = _commandsSerializer.Deserialize(commandSerialized);
        if (!_pending.TryGetValue(recorded.State, out var pendingSerialized))
        {
            throw new InvalidOperationException($"Authorization state '{recorded.State}' is not pending.");
        }

        var pending = _serializer.Deserialize(pendingSerialized);
        if (IsExpired(pending))
        {
            RemovePending(pending);
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            throw new InvalidOperationException($"Authorization state '{recorded.State}' has expired.");
        }

        RequireAuthorizedCompletionTargetBinder(pending, request.CommandId);

        if (pending.CompletionTarget is { } existing
            && existing != default
            && existing != request.CompletionTarget)
        {
            throw new InvalidOperationException(
                $"Authorization command '{request.CommandId}' already has a different completion target.");
        }

        pending = pending with { CompletionTarget = request.CompletionTarget };
        _pending[pending.State] = _serializer.SerializeToArray(pending);
        _commands[pending.CommandId.Value] = _commandsSerializer.SerializeToArray(ToCommandRecord(pending));
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);

        if (pending.Outcome is PendingAuthorizationOutcome.Completed
            || pending.Outcome is PendingAuthorizationOutcome.Denied)
        {
            await NotifyCompletionTargetAsync(pending).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    public async Task<McpAuthorizationCallbackDelivery> DeliverCallback(
        DeliverMcpAuthorizationCallback delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentException.ThrowIfNullOrWhiteSpace(delivery.State);
        cancellationToken.ThrowIfCancellationRequested();

        await SweepExpiredAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!_pending.TryGetValue(delivery.State, out var serialized))
        {
            // Unknown state: refuse. Do not park it in any static dictionary.
            return new McpAuthorizationCallbackDelivery(Accepted: false, Completed: false, Denied: false);
        }

        var pending = _serializer.Deserialize(serialized);
        if (IsExpired(pending))
        {
            RemovePending(pending);
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            return new McpAuthorizationCallbackDelivery(Accepted: false, Completed: false, Denied: false);
        }

        // One-shot: completed or consumed states refuse a second presentation.
        if (pending.Outcome is not PendingAuthorizationOutcome.Open || pending.Consumed)
        {
            return new McpAuthorizationCallbackDelivery(Accepted: false, Completed: false, Denied: false);
        }

        if (!string.IsNullOrWhiteSpace(delivery.Error)
            || string.IsNullOrWhiteSpace(delivery.Code))
        {
            pending = await PersistOutcomeAsync(pending, PendingAuthorizationOutcome.Denied, code: null, iss: null)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            McpAuthorizationCodeHub.Complete(delivery.State, result: null);
            McpAuthorizationCodeHub.AbortOpen(pending.CommandId);
            await EmitAsync(new AuthorizationDenied(pending.CommandId, pending.ServerKey, pending.State, pending.Actor))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await NotifyCompletionTargetAsync(pending).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return new McpAuthorizationCallbackDelivery(Accepted: true, Completed: false, Denied: true);
        }

        pending = await PersistOutcomeAsync(
            pending,
            PendingAuthorizationOutcome.Completed,
            delivery.Code,
            delivery.Iss).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        McpAuthorizationCodeHub.Complete(
            delivery.State,
            new McpAuthorizationCodeResult(
                delivery.Code,
                delivery.Iss,
                UnprotectVerifier(delivery.State, pending.ProtectedCodeVerifier),
                pending.Actor));
        await EmitAsync(new AuthorizationCompleted(pending.CommandId, pending.ServerKey, pending.State, pending.Actor))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await NotifyCompletionTargetAsync(pending).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return new McpAuthorizationCallbackDelivery(Accepted: true, Completed: true, Denied: false);
    }

    public async Task<McpAuthorizationClaim> Claim(
        CommandId commandId,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        if (actor is null)
        {
            throw new NeuronAuthorizationException("Authorization requires a verified actor.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!_commands.TryGetValue(commandId.Value, out var serialized))
        {
            throw new NeuronAuthorizationException($"Authorization command '{commandId}' is not pending.");
        }

        return await ClaimRecordedAsync(serialized, commandId.ToString(), actor, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task<McpAuthorizationClaim> ClaimForServer(
        string serverKey,
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverKey);
        if (actor is null)
        {
            throw new NeuronAuthorizationException("Authorization requires a verified actor.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await SweepExpiredAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var slotKey = SlotKey(serverKey, actor.PrincipalId);
        if (!_slots.TryGetValue(slotKey, out var state)
            || !_pending.TryGetValue(state, out var pendingSerialized))
        {
            throw new NeuronAuthorizationException(
                $"Authorization for server '{serverKey}' is not pending for this principal.");
        }

        var pending = _serializer.Deserialize(pendingSerialized);
        if (IsExpired(pending))
        {
            RemovePending(pending);
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            throw new NeuronAuthorizationException(
                $"Authorization for server '{serverKey}' is not pending for this principal.");
        }

        if (pending.Actor.PrincipalId != actor.PrincipalId
            || !string.Equals(pending.ServerKey, serverKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new NeuronAuthorizationException(
                $"Authorization for server '{serverKey}' is not pending for this principal.");
        }

        var recorded = ToCommandRecord(pending);
        return ClaimFromRecord(recorded);
    }

    private async Task<McpAuthorizationClaim> ClaimRecordedAsync(
        byte[] serialized,
        string label,
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        var recorded = _commandsSerializer.Deserialize(serialized);
        // Status must not leak existence / State to another principal.
        if (recorded.Actor is null
            || recorded.Actor.PrincipalId != actor.PrincipalId)
        {
            throw new NeuronAuthorizationException($"Authorization command '{label}' is not pending.");
        }

        if (_pending.TryGetValue(recorded.State, out var pendingSerialized))
        {
            var pending = _serializer.Deserialize(pendingSerialized);
            if (IsExpired(pending))
            {
                RemovePending(pending);
                await WriteStateAsync(cancellationToken).ConfigureAwait(true);
                throw new NeuronAuthorizationException($"Authorization command '{label}' is not pending.");
            }

            // Prefer live pending outcome over a stale command alias snapshot.
            recorded = ToCommandRecord(pending);
        }

        return ClaimFromRecord(recorded);
    }

    private static McpAuthorizationClaim ClaimFromRecord(CommandAuthorizationRecord recorded)
        => recorded.Outcome switch
        {
            PendingAuthorizationOutcome.Open => new McpAuthorizationClaim(
                McpAuthorizationClaimKind.Required,
                new AuthorizationRequired(
                    recorded.CommandId,
                    recorded.ServerKey,
                    recorded.ServerDisplayName,
                    recorded.SignInUrl,
                    recorded.State,
                    recorded.Actor),
                Denied: null),
            PendingAuthorizationOutcome.Completed => new McpAuthorizationClaim(
                McpAuthorizationClaimKind.Completed,
                Required: null,
                Denied: null),
            PendingAuthorizationOutcome.Denied => new McpAuthorizationClaim(
                McpAuthorizationClaimKind.Denied,
                Required: null,
                new AuthorizationDenied(recorded.CommandId, recorded.ServerKey, recorded.State, recorded.Actor)),
            _ => throw new InvalidOperationException(
                $"Authorization command '{recorded.CommandId}' has an unknown outcome."),
        };

    public async Task<McpAuthorizationCodeResult?> TakeCompletedCode(
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_pending.TryGetValue(state, out var serialized))
        {
            return null;
        }

        var pending = _serializer.Deserialize(serialized);
        if (IsExpired(pending))
        {
            RemovePending(pending);
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
            return null;
        }

        if (pending.Consumed
            || pending.Outcome is PendingAuthorizationOutcome.Denied
            || pending.Outcome is not PendingAuthorizationOutcome.Completed
            || string.IsNullOrWhiteSpace(pending.Code))
        {
            return null;
        }

        var code = UnprotectCode(state, pending.Code);
        var verifier = UnprotectVerifier(state, pending.ProtectedCodeVerifier);
        var result = new McpAuthorizationCodeResult(code, pending.Iss, verifier, pending.Actor);

        // One-shot: clear the code so a second take/replay yields nothing.
        var consumed = pending with { Consumed = true, Code = null };
        _pending[state] = _serializer.SerializeToArray(consumed);
        _commands[pending.CommandId.Value] = _commandsSerializer.SerializeToArray(ToCommandRecord(consumed));
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);
        return result;
    }

    private async Task NotifyCompletionTargetAsync(PendingAuthorization pending, bool force = false)
    {
        if (pending.CompletionTarget is not { } target
            || target == default
            || pending.Outcome is PendingAuthorizationOutcome.Open
            || (pending.CompletionNotified && !force))
        {
            return;
        }

        if (pending.Outcome is PendingAuthorizationOutcome.Completed)
        {
            await SendAsync(
                target,
                new AuthorizationCompleted(pending.CommandId, pending.ServerKey, pending.State, pending.Actor))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        else if (pending.Outcome is PendingAuthorizationOutcome.Denied)
        {
            await SendAsync(
                target,
                new AuthorizationDenied(pending.CommandId, pending.ServerKey, pending.State, pending.Actor))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (pending.CompletionNotified)
        {
            return;
        }

        var notified = pending with { CompletionNotified = true };
        _pending[notified.State] = _serializer.SerializeToArray(notified);
        _commands[notified.CommandId.Value] = _commandsSerializer.SerializeToArray(ToCommandRecord(notified));
        await WriteStateAsync().ConfigureAwait(true);
    }

    private async Task<PendingAuthorization> PersistOutcomeAsync(
        PendingAuthorization pending,
        PendingAuthorizationOutcome outcome,
        string? code,
        string? iss)
    {
        string? protectedCode = null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            protectedCode = Convert.ToBase64String(
                _protector.Protect(
                    CodePurpose(pending.State),
                    Encoding.UTF8.GetBytes(code)));
        }

        var updated = pending with { Outcome = outcome, Code = protectedCode, Iss = iss };
        var durablePayload = _serializer.SerializeToArray(updated);
        _pending[pending.State] = durablePayload;
        _commands[pending.CommandId.Value] = _commandsSerializer.SerializeToArray(ToCommandRecord(updated));
        await WriteStateAsync().ConfigureAwait(true);
        return updated;
    }

    private string UnprotectCode(string state, string protectedCode)
        => Encoding.UTF8.GetString(
            _protector.Unprotect(CodePurpose(state), Convert.FromBase64String(protectedCode)));

    private string? ProtectVerifier(string state, string? verifier)
    {
        if (string.IsNullOrWhiteSpace(verifier))
        {
            return null;
        }

        return Convert.ToBase64String(
            _protector.Protect(VerifierPurpose(state), Encoding.UTF8.GetBytes(verifier)));
    }

    private string? UnprotectVerifier(string state, string? protectedVerifier)
    {
        if (string.IsNullOrWhiteSpace(protectedVerifier))
        {
            return null;
        }

        return Encoding.UTF8.GetString(
            _protector.Unprotect(VerifierPurpose(state), Convert.FromBase64String(protectedVerifier)));
    }

    private static string CodePurpose(string state)
        => $"{CodeProtectionPurposePrefix}/{state}";

    private static string VerifierPurpose(string state)
        => $"{VerifierProtectionPurposePrefix}/{state}";

    private bool IsExpired(PendingAuthorization pending)
        => pending.ExpiresAt <= TimeProvider.GetUtcNow();

    private int CountOpenPending()
    {
        var count = 0;
        foreach (var pair in _pending)
        {
            var pending = _serializer.Deserialize(pair.Value);
            if (pending.Outcome is PendingAuthorizationOutcome.Open && !IsExpired(pending) && !pending.Consumed)
            {
                count++;
            }
        }

        return count;
    }

    private async Task SweepExpiredAsync(CancellationToken cancellationToken)
    {
        var removed = false;
        foreach (var pair in _pending.ToArray())
        {
            var pending = _serializer.Deserialize(pair.Value);
            if (!IsExpired(pending))
            {
                continue;
            }

            RemovePending(pending);
            removed = true;
        }

        if (removed)
        {
            await WriteStateAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    private void RemovePending(PendingAuthorization pending)
    {
        _pending.Remove(pending.State);
        // Drop every command alias that pointed at this PKCE state.
        foreach (var pair in _commands.ToArray())
        {
            var recorded = _commandsSerializer.Deserialize(pair.Value);
            if (string.Equals(recorded.State, pending.State, StringComparison.Ordinal))
            {
                _commands.Remove(pair.Key);
            }
        }

        var slotKey = SlotKey(pending.ServerKey, pending.Actor.PrincipalId);
        if (_slots.TryGetValue(slotKey, out var slotted)
            && string.Equals(slotted, pending.State, StringComparison.Ordinal))
        {
            _slots.Remove(slotKey);
        }
    }

    private static string SlotKey(string serverKey, PrincipalId principal)
        => $"{serverKey.Trim().ToLowerInvariant()}/{principal.Value:N}";

    private NeuronId ResolvePrincipalChat(ActorContext actor, NeuronId? requesting)
    {
        if (requesting is { } chat
            && string.Equals(chat.Type, "chat", StringComparison.OrdinalIgnoreCase)
            && chat.Owner == Id.Owner)
        {
            return chat;
        }

        // Principal-partitioned conversation (Wave 3 naming).
        return new NeuronId("chat", Id.Owner, PrincipalPartition.InstanceName(actor.PrincipalId, "main"));
    }

    private static NeuronId? CaptureRequestingNeuron()
    {
        if (GrainCallerContext.TryGetAuthorizationInitiator(out var initiator) && initiator != default)
        {
            return initiator;
        }

        if (GrainCallerContext.TryGetNeuronId(out var source) && source != default)
        {
            return source;
        }

        return null;
    }

    private void RequireAuthorizedCompletionTargetBinder(PendingAuthorization pending, CommandId commandId)
    {
        if (!GrainCallerContext.TryGetNeuronId(out var binder)
            || binder == default
            || binder.Owner != Id.Owner
            || pending.RequestingNeuron is not { } expected
            || expected == default
            || binder != expected)
        {
            throw new NeuronAuthorizationException(
                $"Caller is not authorized to bind the completion target for authorization command '{commandId}'.");
        }
    }

    private static CommandAuthorizationRecord ToCommandRecord(PendingAuthorization pending)
        => new(
            pending.CommandId,
            pending.ServerKey,
            pending.ServerDisplayName,
            pending.SignInUrl,
            pending.State,
            pending.Outcome,
            pending.CompletionTarget,
            pending.CompletionNotified,
            pending.RequestingNeuron,
            pending.Actor,
            pending.ExpiresAt,
            pending.Consumed);
}
