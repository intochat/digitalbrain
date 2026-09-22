using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

// Thin MCP wrappers over the typed BrainOperations. Only operations the
// neuron model supports survive: resolve, probe and observe. The old
// descriptor-driven describe/call and the signal fire/cancel/connect/
// disconnect/read tools were deleted with the removed invoker and journals.
[McpServerToolType]
public sealed class BrainTools(BrainOperations operations, SessionPrincipal session)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerTool(Name = "get"), Description(
        "Resolve a neuron by name through the typed brain. Returns your session principal and the neuron's canonical grain identity. "
        + "There is no generic method invocation any more: typed module methods are called from C# against their contracts, not from MCP.")]
    public Task<string> Get(
        [Description("Neuron name, e.g. memory-agent")] string neuron)
        => Guard(async () => JsonSerializer.Serialize(operations.Get(session.Name, neuron), Json));

    [McpServerTool(Name = "ping"), Description(
        "Probe a neuron by attaching a watch and immediately detaching it. Returns the activation id, which changes when the neuron "
        + "reactivates, so a caller can detect a restart between calls. Fails when the neuron cannot be reached.")]
    public Task<string> Ping(
        [Description("Neuron name, e.g. memory-agent")] string neuron,
        CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(
            await operations.PingAsync(neuron, cancellationToken).ConfigureAwait(false), Json));

    [McpServerTool(Name = "observe"), Description(
        "Observe the signals a neuron publishes for a bounded window. Subscribes to the neuron's Signal stream, waits up to `seconds` "
        + "(0-60), and returns every signal with its CLR type and JSON payload. It is a live window, not a replay of history.")]
    public Task<string> Observe(
        [Description("Neuron name, e.g. memory-agent")] string neuron,
        [Description("Seconds to listen, 0-60")] int seconds = 5,
        CancellationToken cancellationToken = default)
        => Guard(async () => JsonSerializer.Serialize(
            await operations.ObserveAsync(neuron, seconds, cancellationToken).ConfigureAwait(false), Json));

    // A rejection is advice, so the caller must see the kernel's own wording.
    // McpException is the one exception the SDK relays verbatim.
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