using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using Orleans.Runtime;

namespace DigitalBrain.AI;

[GenerateSerializer, Alias("db.ai.agent-invocations")]
internal sealed record AgentInvocationLedger(
    [property: Id(0)] IReadOnlyList<AgentMutationInvocation> Mutations,
    [property: Id(1)] string? Divergence = null);

[GenerateSerializer, Alias("db.ai.agent-mutation")]
internal sealed record AgentMutationInvocation(
    [property: Id(0)] string Fingerprint,
    [property: Id(1)] CommandId CommandId,
    [property: Id(2)] NeuronId Target,
    [property: Id(3)] string Interface,
    [property: Id(4)] string Method,
    [property: Id(5)] bool Completed = false,
    [property: Id(6)] JsonElement? Result = null);

internal sealed class AgentInvocationScope(INeuronInvoker inner, NeuronId task,
    IPersistentState<AgentInvocationLedger> storage, TaskScheduler scheduler) : INeuronInvoker, IDisposable
{
    private const int MaximumMutations = 20;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _position;
    private string? _failure;
    private AgentInvocationLedger Ledger => storage.RecordExists ? storage.State : new([]);

    public IReadOnlyList<MethodDescriptor> Describe(NeuronId neuron) => inner.Describe(neuron);
    public MethodDescriptor Describe(string interfaceAlias, string methodAlias) => inner.Describe(interfaceAlias, methodAlias);
    public ArgumentContract? ArgumentContractOf(string interfaceAlias, string methodAlias) => inner.ArgumentContractOf(interfaceAlias, methodAlias);

    public Task<JsonElement?> InvokeAsync(NeuronId neuron, string interfaceAlias, string methodAlias,
        JsonElement arguments, CancellationToken cancellationToken = default)
        => Task.Factory.StartNew(() => InvokeOnTurnAsync(neuron, interfaceAlias, methodAlias, arguments, cancellationToken),
            cancellationToken, TaskCreationOptions.DenyChildAttach, scheduler).Unwrap();

    internal void EnsureComplete()
    {
        ThrowIfFailed();
        if (_position < Ledger.Mutations.Count)
        {
            throw new InvalidOperationException("The retried agent did not replay all of its recorded typed mutations. No further mutations were issued; inspect the task's earlier command results.");
        }
    }

    private async Task<JsonElement?> InvokeOnTurnAsync(NeuronId neuron, string interfaceAlias, string methodAlias,
        JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfFailed();
        if (inner.Describe(interfaceAlias, methodAlias).IsReadOnly)
        {
            return await inner.InvokeAsync(neuron, interfaceAlias, methodAlias, arguments, cancellationToken).ConfigureAwait(true);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            ThrowIfFailed();
            var contract = inner.ArgumentContractOf(interfaceAlias, methodAlias);
            var commandProperty = contract?.CommandIdPropertyName
                ?? throw new InvalidOperationException("Managed agent mutations require a typed command-id contract.");
            var supplied = JsonNode.Parse(arguments.GetRawText()) as JsonObject
                ?? throw new ArgumentException("Typed mutation arguments must be an object.", nameof(arguments));
            foreach (var name in supplied.Select(static property => property.Key)
                .Where(name => string.Equals(name, commandProperty, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                supplied.Remove(name);
            }
            var canonical = Canonical(supplied);
            if (Encoding.UTF8.GetByteCount(canonical) > 32_000) { throw new InvalidOperationException("Typed mutation arguments exceed 32 KB."); }
            var fingerprint = AgentLifecycle.Fingerprint(new { Target = neuron, Interface = interfaceAlias, Method = methodAlias, Arguments = canonical });
            var position = _position++;
            if (position >= MaximumMutations) { throw new InvalidOperationException("An agent task can issue at most 20 typed mutations."); }
            var ledger = Ledger;
            AgentMutationInvocation invocation;
            if (position < ledger.Mutations.Count)
            {
                invocation = ledger.Mutations[position];
                if (invocation.Fingerprint != fingerprint)
                {
                    var error = $"Typed mutation replay diverged at invocation {position + 1}. Its target, method or arguments changed; no replacement mutation was issued.";
                    await WriteAsync(ledger with { Divergence = error }, cancellationToken).ConfigureAwait(true);
                    throw new InvalidOperationException(error);
                }
            }
            else
            {
                var id = new CommandId(AgentLifecycle.Signal(task + "/mutation/" + position.ToString(CultureInfo.InvariantCulture)).Value);
                invocation = new(fingerprint, id, neuron, interfaceAlias, methodAlias);
                await WriteAsync(ledger with { Mutations = [.. ledger.Mutations, invocation] }, cancellationToken).ConfigureAwait(true);
            }

            if (invocation.Completed) { return invocation.Result?.Clone(); }
            // The model's generated id never crosses the boundary: this identity is fixed before the effect.
            supplied[commandProperty] = JsonSerializer.SerializeToNode(invocation.CommandId, contract!.Options);
            using var document = JsonDocument.Parse(supplied.ToJsonString());
            var bound = document.RootElement.Clone();
            JsonElement? result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await inner.InvokeAsync(neuron, interfaceAlias, methodAlias, bound, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                _failure = $"Typed mutation {invocation.CommandId} failed or its outcome is uncertain: {error.Message}. Further mutations in this task were stopped.";
                throw;
            }
            if (result is { } value && Encoding.UTF8.GetByteCount(value.GetRawText()) > 32_000)
            {
                throw new InvalidOperationException($"Typed mutation {invocation.CommandId} returned more than 32 KB. Its command may have completed; inspect the target before starting another task.");
            }
            invocation = invocation with { Completed = true, Result = result?.Clone() };
            ledger = Ledger;
            await WriteAsync(ledger with { Mutations = ledger.Mutations.Select((item, index) => index == position ? invocation : item).ToArray() }, cancellationToken).ConfigureAwait(true);
            return result;
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            throw;
        }
        finally { _gate.Release(); }
    }

    private void ThrowIfFailed()
    {
        if ((_failure ?? Ledger.Divergence) is { } failure) { throw new InvalidOperationException(failure); }
    }

    private async Task WriteAsync(AgentInvocationLedger ledger, CancellationToken cancellationToken)
    {
        storage.State = ledger;
        try { await storage.WriteStateAsync(cancellationToken).ConfigureAwait(true); }
        catch
        {
            await storage.ReadStateAsync(CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }

    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject item => "{" + string.Join(',', item.OrderBy(static property => property.Key, StringComparer.Ordinal)
            .Select(static property => JsonSerializer.Serialize(property.Key) + ":" + Canonical(property.Value))) + "}",
        JsonArray array => "[" + string.Join(',', array.Select(Canonical)) + "]",
        _ => node?.ToJsonString() ?? "null",
    };

    public void Dispose() => _gate.Dispose();
}
