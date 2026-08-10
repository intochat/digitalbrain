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
    IMcpAuthorization
{
    internal const string InstanceName = IMcpAuthorization.DefaultInstanceName;
    private const string PendingName = "mcp.authorization.pending";
    private const string CommandsName = "mcp.authorization.commands";
    private const string CodeProtectionPurposePrefix = "mcp/authorization/code";
    private readonly IDurableDictionary<string, byte[]> _pending;
    private readonly IDurableDictionary<Guid, byte[]> _commands;
    private readonly Serializer<PendingAuthorization> _serializer;
    private readonly Serializer<CommandAuthorizationRecord> _commandsSerializer;
    private readonly IDurablePayloadProtector _protector;

    public McpAuthorizationNeuron()
    {
        _pending = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, byte[]>>(PendingName);
        _commands = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(CommandsName);
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
        cancellationToken.ThrowIfCancellationRequested();

        if (_commands.TryGetValue(request.CommandId.Value, out var commandSerialized))
        {
            var recorded = _commandsSerializer.Deserialize(commandSerialized);
            if (recorded.Outcome is PendingAuthorizationOutcome.Denied)
            {
                throw new McpAuthorizationDeniedException(
                    new AuthorizationDenied(recorded.CommandId, recorded.ServerKey, recorded.State));
            }

            if (recorded.Outcome is PendingAuthorizationOutcome.Completed)
            {
                return new AuthorizationRequired(
                    recorded.CommandId,
                    recorded.ServerKey,
                    recorded.ServerDisplayName,
                    recorded.SignInUrl,
                    recorded.State);
            }

            return new AuthorizationRequired(
                recorded.CommandId,
                recorded.ServerKey,
                recorded.ServerDisplayName,
                recorded.SignInUrl,
                recorded.State);
        }

        if (_pending.TryGetValue(request.State, out var existingSerialized))
        {
            var existing = _serializer.Deserialize(existingSerialized);
            if (existing.CommandId != request.CommandId
                || !string.Equals(existing.ServerKey, request.ServerKey, StringComparison.Ordinal)
                || existing.SignInUrl != request.SignInUrl)
            {
                throw new InvalidOperationException(
                    $"Authorization state '{request.State}' is already pending for a different request.");
            }

            return new AuthorizationRequired(
                existing.CommandId,
                existing.ServerKey,
                existing.ServerDisplayName,
                existing.SignInUrl,
                existing.State);
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
            RequestingNeuron: CaptureRequestingNeuron());
        _pending[request.State] = _serializer.SerializeToArray(pending);
        _commands[request.CommandId.Value] = _commandsSerializer.SerializeToArray(
            ToCommandRecord(pending));
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);

        var required = new AuthorizationRequired(
            request.CommandId,
            request.ServerKey,
            request.ServerDisplayName,
            request.SignInUrl,
            request.State);
        await EmitAsync(required).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        // Directed into the owner's main chat so the transcript shows a
        // "Sign in via {server}" button (Emit alone only hits broadcast ghosts).
        await SendAsync(new NeuronId("chat", Id.Owner, "main"), required)
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

        if (!_pending.TryGetValue(delivery.State, out var serialized))
        {

            McpAuthorizationCodeHub.Complete(delivery.State, result: null);
            return new McpAuthorizationCallbackDelivery(Accepted: false, Completed: false, Denied: false);
        }

        var pending = _serializer.Deserialize(serialized);
        if (pending.Outcome is not PendingAuthorizationOutcome.Open)
        {
            if (pending.Outcome is PendingAuthorizationOutcome.Completed
                && !string.IsNullOrWhiteSpace(pending.Code))
            {
                McpAuthorizationCodeHub.Complete(
                    delivery.State,
                    new McpAuthorizationCodeResult(UnprotectCode(delivery.State, pending.Code), pending.Iss));
            }
            else if (pending.Outcome is PendingAuthorizationOutcome.Denied)
            {

                McpAuthorizationCodeHub.Complete(delivery.State, result: null);
                McpAuthorizationCodeHub.AbortOpen(pending.CommandId);
            }

            await NotifyCompletionTargetAsync(pending, force: true).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return new McpAuthorizationCallbackDelivery(
                Accepted: true,
                Completed: pending.Outcome is PendingAuthorizationOutcome.Completed,
                Denied: pending.Outcome is PendingAuthorizationOutcome.Denied);
        }

        if (!string.IsNullOrWhiteSpace(delivery.Error)
            || string.IsNullOrWhiteSpace(delivery.Code))
        {
            pending = await PersistOutcomeAsync(pending, PendingAuthorizationOutcome.Denied, code: null, iss: null).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            McpAuthorizationCodeHub.Complete(delivery.State, result: null);
            McpAuthorizationCodeHub.AbortOpen(pending.CommandId);
            await EmitAsync(new AuthorizationDenied(pending.CommandId, pending.ServerKey, pending.State)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            new McpAuthorizationCodeResult(delivery.Code, delivery.Iss));
        await EmitAsync(new AuthorizationCompleted(pending.CommandId, pending.ServerKey, pending.State)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await NotifyCompletionTargetAsync(pending).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return new McpAuthorizationCallbackDelivery(Accepted: true, Completed: true, Denied: false);
    }

    public Task<McpAuthorizationClaim> Claim(CommandId commandId, CancellationToken cancellationToken = default)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(commandId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!_commands.TryGetValue(commandId.Value, out var serialized))
        {
            throw new InvalidOperationException($"Authorization command '{commandId}' is not pending.");
        }

        var recorded = _commandsSerializer.Deserialize(serialized);
        return Task.FromResult(recorded.Outcome switch
        {
            PendingAuthorizationOutcome.Open => new McpAuthorizationClaim(
                McpAuthorizationClaimKind.Required,
                new AuthorizationRequired(
                    recorded.CommandId,
                    recorded.ServerKey,
                    recorded.ServerDisplayName,
                    recorded.SignInUrl,
                    recorded.State),
                Denied: null),
            PendingAuthorizationOutcome.Completed => new McpAuthorizationClaim(
                McpAuthorizationClaimKind.Completed,
                Required: null,
                Denied: null),
            PendingAuthorizationOutcome.Denied => new McpAuthorizationClaim(
                McpAuthorizationClaimKind.Denied,
                Required: null,
                new AuthorizationDenied(recorded.CommandId, recorded.ServerKey, recorded.State)),
            _ => throw new InvalidOperationException($"Authorization command '{commandId}' has an unknown outcome."),
        });
    }

    public Task<McpAuthorizationCodeResult?> TakeCompletedCode(
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_pending.TryGetValue(state, out var serialized))
        {
            return Task.FromResult<McpAuthorizationCodeResult?>(null);
        }

        var pending = _serializer.Deserialize(serialized);
        if (pending.Outcome is PendingAuthorizationOutcome.Denied)
        {
            return Task.FromResult<McpAuthorizationCodeResult?>(null);
        }

        if (pending.Outcome is not PendingAuthorizationOutcome.Completed
            || string.IsNullOrWhiteSpace(pending.Code))
        {
            return Task.FromResult<McpAuthorizationCodeResult?>(null);
        }

        return Task.FromResult<McpAuthorizationCodeResult?>(
            new McpAuthorizationCodeResult(UnprotectCode(state, pending.Code), pending.Iss));
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
                new AuthorizationCompleted(pending.CommandId, pending.ServerKey, pending.State)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        else if (pending.Outcome is PendingAuthorizationOutcome.Denied)
        {
            await SendAsync(
                target,
                new AuthorizationDenied(pending.CommandId, pending.ServerKey, pending.State)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

    private static string CodePurpose(string state)
        => $"{CodeProtectionPurposePrefix}/{state}";

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
            pending.RequestingNeuron);
}

[GenerateSerializer]
[Alias("db.mcp.pending-authorization")]
internal sealed record PendingAuthorization(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string ServerDisplayName,
    [property: Id(3)] Uri SignInUrl,
    [property: Id(4)] string State,
    [property: Id(5)] PendingAuthorizationOutcome Outcome,
    [property: Id(6)] string? Code,
    [property: Id(7)] string? Iss,
    [property: Id(8)] NeuronId? CompletionTarget,
    [property: Id(9)] bool CompletionNotified,
    [property: Id(10)] NeuronId? RequestingNeuron = null);

[GenerateSerializer]
[Alias("db.mcp.pending-authorization-outcome")]
internal enum PendingAuthorizationOutcome
{
    Open = 0,
    Completed = 1,
    Denied = 2,
}

[GenerateSerializer]
[Alias("db.mcp.command-authorization-record")]
internal sealed record CommandAuthorizationRecord(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ServerKey,
    [property: Id(2)] string ServerDisplayName,
    [property: Id(3)] Uri SignInUrl,
    [property: Id(4)] string State,
    [property: Id(5)] PendingAuthorizationOutcome Outcome,
    [property: Id(6)] NeuronId? CompletionTarget = null,
    [property: Id(7)] bool CompletionNotified = false,
    [property: Id(8)] NeuronId? RequestingNeuron = null);
