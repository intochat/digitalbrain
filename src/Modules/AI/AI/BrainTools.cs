using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Mcp;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// The seven brain operations are bound to one agent's identity. The descriptions are the
// MCP server's, word for word: the same tools should read the same everywhere.
internal static class BrainTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static IEnumerable<AIFunction> For(AgentNeuron agent, BrainOperations operations, INeuronInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(invoker);

        yield return AIFunctionFactory.Create(
            (
                [Description("Signal type: letters only, e.g. Note")] string type,
                [Description("JSON body, e.g. {\"text\":\"run tests before commit\"}. Empty means {}.")] string body,
                [Description("Target neuron name, e.g. run-tests. Omit to follow all your synapses of this type.")] string? to = null,
                [Description("Optional correlation id (GUID) to tie this to an earlier signal.")] string? correlation = null)
                => agent.FireSignalAsync(type, body, to, correlation),
            "fire",
            "Fire a signal from your Session neuron. A signal is a `type` (letters only, vocabulary such as Note, Confirmed, Decision) "
            + "and a JSON `body` up to 64 KB. With `to`, it goes to exactly that neuron and creates the synapse if missing; "
            + "without `to`, it follows every synapse of that type you already have. Neurons exist as soon as they are named. "
            + "Put identity in the neuron name (run-tests-before-commit), never in the type. Returns the signal id, correlation and how many neurons accepted it or were busy.");

        yield return AIFunctionFactory.Create(
            (
                [Description("Source neuron name")] string from,
                [Description("Target neuron name")] string to,
                [Description("Signal type the synapse carries, letters only")] string type)
                => agent.ChangeSynapseAsync(from, to, type, connect: true),
            "connect",
            "Create a synapse: from one neuron to another for one signal type. Idempotent. Use it to build structure, "
            + "e.g. connect topic `git` to `run-tests-before-commit` for `Note`, then recall by reading `git`'s synapses and following them.");

        yield return AIFunctionFactory.Create(
            (
                [Description("Source neuron name")] string from,
                [Description("Target neuron name")] string to,
                [Description("Signal type the synapse carries, letters only")] string type)
                => agent.ChangeSynapseAsync(from, to, type, connect: false),
            "disconnect",
            "Remove a synapse: from one neuron to another for one signal type. Succeeds even if the synapse did not exist, "
            + "so it is safe to call twice. Nothing else about either neuron changes; state and journals are kept.");

        yield return AIFunctionFactory.Create(
            async (
                [Description("Neuron name, e.g. git or run-tests-before-commit")] string neuron,
                [Description("state | synapses | incoming | outgoing | commands; omit for all but commands")] string? what = null,
                [Description("Journal sequence to read after; 0 for the retained window")] long after = 0,
                CancellationToken cancellationToken = default)
                =>
            {
                // A read is a query, but it still runs on the neuron's own turn and its rejections
                // are advice the model must read, so it takes the same route as the writes.
                // A reaction may read but never wait: the timeout an MCP client may pass is 0 here.
                return JsonSerializer.Serialize(await operations.ReadAsync(new(neuron, what, after, 0), cancellationToken)
                    .ConfigureAwait(true), Json);
            },
            "read",
            "Read a neuron without changing anything. Returns its state (latest signal per type), its synapses, and its incoming and outgoing journals. "
            + "Recall pattern: read a topic's synapses, follow each target, read its state. "
            + "`what` narrows to state | synapses | incoming | outgoing | commands. Omitting `what` returns all but commands. `after` is a journal sequence to resume from.");

        yield return AIFunctionFactory.Create(
            async (
                [Description("Neuron name holding the work")] string neuron,
                [Description("Signal id (GUID) returned by an earlier fire")] string signal,
                CancellationToken cancellationToken = default)
                =>
            {
                await operations.CancelAsync(new(neuron, signal), cancellationToken).ConfigureAwait(true);
                return $"cancellation requested for {signal} on {neuron}";
            },
            "cancel",
            "Cancel a signal on a neuron. Drops pending work that has not been reacted to yet and asks a reaction already running "
            + "to stop at its next cooperative check. It is a no-op if the signal is neither pending nor running. "
            + "Pass the signalId returned by an earlier fire.");

        yield return AIFunctionFactory.Create(
            (
                [Description("Neuron name, e.g. counter:items; omit when selecting an interface and method")] string? neuron = null,
                [Description("Interface alias returned by describe; provide together with method")] string? @interface = null,
                [Description("Method alias within the interface; provide together with interface")] string? method = null)
                =>
            {
                IReadOnlyList<MethodDescriptor> descriptors = (neuron, @interface, method) switch
                {
                    (not null, null, null) => invoker.Describe(Parse(neuron)),
                    (null, not null, not null) => [invoker.Describe(@interface, method)],
                    _ => throw new ArgumentException("Use either neuron alone, or interface plus method, to describe callable methods."),
                };
                return JsonSerializer.Serialize(descriptors, Json);
            },
            "describe",
            "Discover the typed methods a neuron exposes before calling them. Pass `neuron` alone to list its module methods, "
            + "or pass `interface` and `method` aliases together to inspect one method. Returns interface and method aliases, "
            + "whether each method is read-only, JSON schemas for its arguments and result, and documentation when available. "
            + "Use the argument schema to construct the JSON for call; kernel operations are available as separate tools.");

        yield return AIFunctionFactory.Create(
            async (
                [Description("Target neuron name, including its type, e.g. counter:items")] string neuron,
                [Description("Interface alias returned by describe")] string @interface,
                [Description("Method alias returned by describe")] string method,
                [Description("JSON arguments matching the method's args schema; {} when there are none")] string args,
                CancellationToken cancellationToken = default)
                =>
            {
                using var document = JsonDocument.Parse(args);
                return JsonSerializer.Serialize(await invoker.InvokeAsync(Parse(neuron), @interface, method,
                    document.RootElement, cancellationToken).ConfigureAwait(true), Json);
            },
            "call",
            "Invoke a typed method on a neuron using aliases and the argument schema returned by describe. Pass the target `neuron`, "
            + "its `interface` alias, the `method` alias and an `args` JSON object matching that method's schema; use {} for no arguments. "
            + "Mutating methods take a command id for retries and record your Session neuron as caller. Returns the method's JSON result, "
            + "or null when it has no result. A wrong interface returns the interfaces the target actually implements.");
    }

    private static NeuronId Parse(string text)
        => NeuronId.TryParse(text, out var id)
            ? id
            : throw new ArgumentException($"'{text}' is not a neuron name. Use a bare name such as 'run-tests' or 'type:name'; no spaces.", nameof(text));
}
