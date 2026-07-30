using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Mcp;

[GrainType("mcpauthorization")]
internal sealed class McpAuthorizationNeuron :
    Neuron,
    IMcpAuthorization,
    IEmit<AuthorizationRequired>,
    IEmit<AuthorizationCompleted>,
    IEmit<AuthorizationDenied>
{
    internal const string InstanceName = "mcp";
    private const string PendingName = "mcp.authorization.pending";
    private const string CommandsName = "mcp.authorization.commands";
    private readonly IDurableDictionary<string, byte[]> _pending;
    private readonly IDurableDictionary<Guid, byte[]> _commands;
    private readonly Serializer<PendingAuthorization> _serializer;
    private readonly Serializer<CommandAuthorizationRecord> _commandsSerializer;

    public McpAuthorizationNeuron()
    {
        _pending = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, byte[]>>(PendingName);
        _commands = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(CommandsName);
        _serializer = ServiceProvider.GetRequiredService<Serializer<PendingAuthorization>>();
        _commandsSerializer = ServiceProvider.GetRequiredService<Serializer<CommandAuthorizationRecord>>();
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
                // Idempotent resume: the edge callback completed while the emitting grain was
                // still wiring OAuth. Returning the original required fact is enough for journal
                // observers; the durable token cache already holds the exchanged token.
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
            Iss: null);
        _pending[request.State] = _serializer.SerializeToArray(pending);
        _commands[request.CommandId.Value] = _commandsSerializer.SerializeToArray(
            new CommandAuthorizationRecord(
                pending.CommandId,
                pending.ServerKey,
                pending.ServerDisplayName,
                pending.SignInUrl,
                pending.State,
                pending.Outcome));
        await WriteStateAsync(cancellationToken);

        var required = new AuthorizationRequired(
            request.CommandId,
            request.ServerKey,
            request.ServerDisplayName,
            request.SignInUrl,
            request.State);
        await EmitAsync(required);
        return required;
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
                    new McpAuthorizationCodeResult(pending.Code, pending.Iss));
            }
            else if (pending.Outcome is PendingAuthorizationOutcome.Denied)
            {
                McpAuthorizationCodeHub.Complete(delivery.State, result: null);
            }

            return new McpAuthorizationCallbackDelivery(
                Accepted: true,
                Completed: pending.Outcome is PendingAuthorizationOutcome.Completed,
                Denied: pending.Outcome is PendingAuthorizationOutcome.Denied);
        }

        if (!string.IsNullOrWhiteSpace(delivery.Error)
            || string.IsNullOrWhiteSpace(delivery.Code))
        {
            await PersistOutcomeAsync(pending, PendingAuthorizationOutcome.Denied, code: null, iss: null);
            await EmitAsync(new AuthorizationDenied(pending.CommandId, pending.ServerKey, pending.State));
            McpAuthorizationCodeHub.Complete(delivery.State, result: null);
            return new McpAuthorizationCallbackDelivery(Accepted: true, Completed: false, Denied: true);
        }

        await PersistOutcomeAsync(pending, PendingAuthorizationOutcome.Completed, delivery.Code, delivery.Iss);
        await EmitAsync(new AuthorizationCompleted(pending.CommandId, pending.ServerKey, pending.State));
        McpAuthorizationCodeHub.Complete(
            delivery.State,
            new McpAuthorizationCodeResult(delivery.Code, delivery.Iss));
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
            new McpAuthorizationCodeResult(pending.Code, pending.Iss));
    }

    private async Task PersistOutcomeAsync(
        PendingAuthorization pending,
        PendingAuthorizationOutcome outcome,
        string? code,
        string? iss)
    {
        var updated = pending with { Outcome = outcome, Code = code, Iss = iss };
        _pending[pending.State] = _serializer.SerializeToArray(updated);
        _commands[pending.CommandId.Value] = _commandsSerializer.SerializeToArray(
            new CommandAuthorizationRecord(
                updated.CommandId,
                updated.ServerKey,
                updated.ServerDisplayName,
                updated.SignInUrl,
                updated.State,
                updated.Outcome));
        await WriteStateAsync();
    }
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
    [property: Id(7)] string? Iss);

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
    [property: Id(5)] PendingAuthorizationOutcome Outcome);
