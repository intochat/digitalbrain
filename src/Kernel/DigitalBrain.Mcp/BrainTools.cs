using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
public sealed class BrainTools(BrainOperations operations, SessionPrincipal session)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerTool(Name = "fire"), Description(
        "Fire a signal from your Session neuron. A signal is a `type` (letters only, vocabulary such as Note, Confirmed, Decision) "
        + "and a JSON `body` up to 64 KB. With `to`, it goes to exactly that neuron and creates the synapse if missing; "
        + "without `to`, it follows every synapse of that type you already have. Neurons exist as soon as they are named. "
        + "Put identity in the neuron name (run-tests-before-commit), never in the type. Returns the signal id, correlation and how many neurons accepted it or were busy.")]
    public Task<string> Fire(
        [Description("Signal type: letters only, e.g. Note")] string type,
        [Description("JSON body, e.g. {\"text\":\"run tests before commit\"}. Empty means {}.")] string body,
        [Description("Target neuron name, e.g. run-tests. Omit to follow all your synapses of this type.")] string? to = null,
        [Description("Optional correlation id (GUID) to tie this to an earlier signal.")] string? correlation = null,
        CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(
            await operations.FireAsync(session.Name, new(type, body, to, correlation), cancellationToken).ConfigureAwait(false), Json));

    [McpServerTool(Name = "cancel"), Description(
        "Cancel a signal on a neuron. Drops pending work that has not been reacted to yet and asks a reaction already running "
        + "to stop at its next cooperative check. It is a no-op if the signal is neither pending nor running. "
        + "Pass the signalId returned by an earlier fire.")]
    public Task<string> Cancel(
        [Description("Neuron name holding the work")] string neuron,
        [Description("Signal id (GUID) returned by an earlier fire")] string signal,
        CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            await operations.CancelAsync(new(neuron, signal), cancellationToken).ConfigureAwait(false);
            return $"cancellation requested for {signal} on {neuron}";
        });

    [McpServerTool(Name = "connect"), Description(
        "Create a synapse: from one neuron to another for one signal type. Idempotent. Use it to build structure, "
        + "e.g. connect topic `git` to `run-tests-before-commit` for `Note`, then recall by reading `git`'s synapses and following them.")]
    public Task<string> Connect(
        [Description("Source neuron name")] string from,
        [Description("Target neuron name")] string to,
        [Description("Signal type the synapse carries, letters only")] string type,
        CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            await operations.ConnectAsync(new(from, to, type), cancellationToken).ConfigureAwait(false);
            return $"connected {from} --{type}--> {to}";
        });

    [McpServerTool(Name = "disconnect"), Description(
        "Remove a synapse: from one neuron to another for one signal type. Succeeds even if the synapse did not exist, "
        + "so it is safe to call twice. Nothing else about either neuron changes; state and journals are kept.")]
    public Task<string> Disconnect(
        [Description("Source neuron name")] string from,
        [Description("Target neuron name")] string to,
        [Description("Signal type the synapse carries, letters only")] string type,
        CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            await operations.DisconnectAsync(new(from, to, type), cancellationToken).ConfigureAwait(false);
            return $"disconnected {from} --{type}--> {to}";
        });

    [McpServerTool(Name = "read"), Description(
        "Read a neuron without changing anything. Returns its state (latest signal per type), its synapses, and its incoming and outgoing journals. "
        + "Recall pattern: read a topic's synapses, follow each target, read its state. "
        + "`what` narrows to state | synapses | incoming | outgoing | commands. Omitting `what` returns all but commands. `after` is a journal sequence to resume from. "
        + "`timeoutSeconds` makes an incoming/outgoing read wait for the next entry. Your own Session neuron is named after your principal (default `claude`).")]
    public Task<string> Read(
        [Description("Neuron name, e.g. git or run-tests-before-commit")] string neuron,
        [Description("state | synapses | incoming | outgoing | commands; omit for all but commands")] string? what = null,
        [Description("Journal sequence to read after; 0 for the retained window")] long after = 0,
        [Description("Seconds to wait for a new journal entry, at most 60; 0 returns immediately")] int timeoutSeconds = 0,
        CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(
            await operations.ReadAsync(new(neuron, what, after, timeoutSeconds), cancellationToken).ConfigureAwait(false), Json));

    [McpServerTool(Name = "describe"), Description(
        "Discover the typed methods a neuron exposes before calling them. Pass `neuron` alone to list its module methods, "
        + "or pass `interface` and `method` aliases together to inspect one method. Returns interface and method aliases, "
        + "whether each method is read-only, JSON schemas for its arguments and result, and documentation when available. "
        + "Use the argument schema to construct the JSON for call; kernel operations are available as separate tools.")]
    public Task<string> Describe(
        [Description("Neuron name, e.g. counter:items; omit when selecting an interface and method")] string? neuron = null,
        [Description("Interface alias returned by describe; provide together with method")] string? @interface = null,
        [Description("Method alias within the interface; provide together with interface")] string? method = null)
        => Guard(async () => JsonSerializer.Serialize(
            await operations.DescribeAsync(new(neuron, @interface, method)).ConfigureAwait(false), Json));

    [McpServerTool(Name = "call"), Description(
        "Invoke a typed method on a neuron using aliases and the argument schema returned by describe. Pass the target `neuron`, "
        + "its `interface` alias, the `method` alias and an `args` JSON object matching that method's schema; use {} for no arguments. "
        + "Mutating methods take a command id for retries and record your Session neuron as caller. Returns the method's JSON result, "
        + "or null when it has no result. A wrong interface returns the interfaces the target actually implements.")]
    public Task<string> Call(
        [Description("Target neuron name, including its type, e.g. counter:items")] string neuron,
        [Description("Interface alias returned by describe")] string @interface,
        [Description("Method alias returned by describe")] string method,
        [Description("JSON arguments matching the method's args schema; {} when there are none")] string args,
        CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            using var document = JsonDocument.Parse(args);
            return JsonSerializer.Serialize(await operations.CallAsync(session.Name,
                new(neuron, @interface, method, document.RootElement), cancellationToken).ConfigureAwait(false), Json);
        });

    // A rejection is advice, so the caller must see the kernel's own wording, not a generic
    // "an error occurred". McpException is the one exception the SDK relays verbatim.
    private static async Task<string> Guard(Func<Task<string>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new ModelContextProtocol.McpException(error.Message, error);
        }
    }
}
