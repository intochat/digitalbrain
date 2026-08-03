using System.Reflection;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Orleans;

namespace DigitalBrain.Behaviors.Host;

internal sealed class HostBehaviorSynapseBroker : IBehaviorSynapseBroker
{
    private readonly BehaviorExecutionMetadata metadata;
    private readonly NeuronId task;
    private readonly AttemptId attempt;
    private readonly BehaviorCapabilityEdge[] grants;
    private readonly IBehaviorHostBrokerClient client;
    private readonly BehaviorOperationBroker operations;
    private readonly int hopsRemaining;

    public HostBehaviorSynapseBroker(
        BehaviorExecutionMetadata metadata,
        NeuronId task,
        AttemptId attempt,
        IEnumerable<BehaviorCapabilityEdge> grants,
        IBehaviorHostBrokerClient client,
        int hopsRemaining = BehaviorFactEmission.MaximumHops)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(client);

        if (metadata.Owner == default)
        {
            throw new ArgumentException("Owner is required.", nameof(metadata));
        }

        if (task == default)
        {
            throw new ArgumentException("Task is required.", nameof(task));
        }

        if (attempt == default)
        {
            throw new ArgumentException("Attempt is required.", nameof(attempt));
        }

        this.metadata = metadata;
        this.task = task;
        this.attempt = attempt;
        this.grants = [.. grants];
        this.client = client;
        this.hopsRemaining = hopsRemaining;

        var history = new TaskOwnedOperationHistory(task, attempt, client);
        operations = new BehaviorOperationBroker(history, this.grants, client);
    }

    public Task SendAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken)
        where TNeuron : INeuron
    {
        throw new NotSupportedException(
            "One-way send is not supported; BehaviorCapabilityEdge requires a result identity.");
    }

    public async Task EmitAsync(Synapse fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        cancellationToken.ThrowIfCancellationRequested();

        // The host only asserts what it wants to say; the broker re-verifies the grant.
        await client
            .EmitFactAsync(
                metadata.Behavior,
                RequireAlias(fact.GetType()),
                BehaviorPayloadJson.Serialize(fact, fact.GetType()),
                hopsRemaining,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TResponse> SendAsync<TNeuron, TResponse>(
        string name,
        RequestSynapse<TResponse> request,
        CancellationToken cancellationToken)
        where TNeuron : INeuron
        where TResponse : Synapse
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var target = new NeuronId(RequireAlias(typeof(TNeuron)), metadata.Owner, name);
        var requestAlias = RequireAlias(request.GetType());
        var responseAlias = RequireAlias(typeof(TResponse));
        var grant = SelectGrant(target, requestAlias, responseAlias);

        var plaintext = BehaviorPayloadJson.Serialize(request, request.GetType());
        var requestPayload = await client
            .StorePayloadAsync(metadata.Owner, task, attempt, plaintext, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var operation = await operations
            .ExecuteAsync(
                grant.Target,
                grant.RequestSynapseId,
                grant.RequestSchemaVersion,
                grant.ResponseSynapseId,
                grant.ResponseSchemaVersion,
                requestPayload,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (operation.Phase != TaskOperationPhase.Completed)
        {
            throw new InvalidOperationException(
                $"Capability operation ended in phase '{operation.Phase}'.");
        }

        if (operation.ResponsePayload is not { } responsePayload)
        {
            throw new InvalidOperationException(
                "Completed operation returned an empty response payload reference.");
        }

        var responseBytes = await client
            .LoadPayloadAsync(metadata.Owner, task, attempt, responsePayload, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var response = BehaviorPayloadJson.Deserialize<TResponse>(responseBytes.Span);
        if (response is null)
        {
            throw new InvalidOperationException("Response payload deserialized to null.");
        }

        return response;
    }

    private BehaviorCapabilityEdge SelectGrant(
        NeuronId target,
        string requestAlias,
        string responseAlias)
    {
        BehaviorCapabilityEdge? selected = null;

        foreach (var grant in grants)
        {
            if (grant.Target != target
                || !string.Equals(grant.RequestSynapseId, requestAlias, StringComparison.Ordinal)
                || !string.Equals(grant.ResponseSynapseId, responseAlias, StringComparison.Ordinal))
            {
                continue;
            }

            if (selected is not null)
            {
                throw new InvalidOperationException(
                    "Multiple capability grants match the target synapse pair.");
            }

            selected = grant;
        }

        if (selected is null)
        {
            throw new InvalidOperationException(
                "No capability grant matches the target synapse pair.");
        }

        return selected;
    }

    private static string RequireAlias(Type type)
    {
        string? selected = null;

        foreach (var attribute in type.GetCustomAttributes<AliasAttribute>(inherit: false))
        {
            if (string.IsNullOrWhiteSpace(attribute.Alias))
            {
                continue;
            }

            if (selected is not null)
            {
                throw new InvalidOperationException(
                    $"Type '{type}' declares multiple [Alias] attributes.");
            }

            selected = attribute.Alias;
        }

        if (selected is null)
        {
            throw new InvalidOperationException(
                $"Type '{type}' does not declare a nonblank [Alias].");
        }

        return selected;
    }
}
