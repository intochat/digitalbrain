using System.ComponentModel;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Modules.UI.Contracts;
using DigitalBrain.ProductHost.Protocol;
using ModelContextProtocol.Server;

namespace DigitalBrain.ProductHost.Mcp;

[McpServerToolType]
public sealed class ProductMcpTools(IProductRuntimeClient runtime)
{
    private readonly IProductRuntimeClient _runtime = runtime;

    [McpServerTool(Name = "digitalbrain_list_modules")]
    [Description("List DigitalBrain product modules and their readiness state.")]
    public Task<IReadOnlyList<BrainModuleDescriptor>> ListModulesAsync(
        CancellationToken cancellationToken = default)
        => _runtime.GetModulesAsync(cancellationToken);

    [McpServerTool(Name = "digitalbrain_list_operations")]
    [Description("List operations exposed by ready DigitalBrain modules.")]
    public Task<IReadOnlyList<BrainOperationDescriptor>> ListOperationsAsync(
        CancellationToken cancellationToken = default)
        => _runtime.GetOperationsAsync(cancellationToken);

    [McpServerTool(Name = "digitalbrain_invoke")]
    [Description("Invoke a DigitalBrain operation in the local workspace and return its durable activity receipt.")]
    public Task<BrainActivityReceipt> InvokeAsync(
        [Description("Canonical operation id, for example Proof.Run@1")] string operationId,
        [Description("Operation input as JSON")] string inputJson,
        [Description("Caller-supplied idempotency key")] string idempotencyKey,
        CancellationToken cancellationToken = default)
        => _runtime.InvokeAsync(
            new BrainOperationInvocation(operationId, inputJson, "local", "owner", idempotencyKey),
            cancellationToken);

    [McpServerTool(Name = "digitalbrain_get_activity")]
    [Description("Read a durable DigitalBrain activity in the local workspace.")]
    public Task<BrainActivitySnapshot?> GetActivityAsync(
        [Description("Activity id returned by digitalbrain_invoke")] Guid activity,
        CancellationToken cancellationToken = default)
        => _runtime.GetActivityAsync(activity, "local", cancellationToken);

    [McpServerTool(Name = "digitalbrain_chat")]
    [Description("Chat with DigitalBrain through Chat.Send@1 and return the durable activity id plus assistant response.")]
    public Task<ChatTurnEnvelope> ChatAsync(
        [Description("Message for the operation-using assistant")] string message,
        [Description("Caller-supplied idempotency key; generated when omitted")] string idempotencyKey = "",
        CancellationToken cancellationToken = default)
        => ProductChat.SendAsync(
            _runtime,
            message,
            "local",
            "owner",
            string.IsNullOrWhiteSpace(idempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : idempotencyKey.Trim(),
            cancellationToken);

    [McpServerTool(Name = "digitalbrain_activity_journal")]
    [Description("Read ordered causal journal records for a DigitalBrain activity in the local workspace.")]
    public Task<BrainJournalPage> GetActivityJournalAsync(
        [Description("Activity id returned by digitalbrain_invoke or digitalbrain_chat")] Guid activity,
        [Description("Return records after this sequence")] long afterSequence = 0,
        [Description("Maximum records to return (1-500)")] int take = 100,
        CancellationToken cancellationToken = default)
        => _runtime.GetJournalAsync(activity, "local", afterSequence, take, cancellationToken);

    [McpServerTool(Name = "digitalbrain_brain_snapshot")]
    [Description("Read the live DigitalBrain Neuron and Synapse graph for the local workspace.")]
    public Task<BrainSnapshot> GetBrainSnapshotAsync(
        CancellationToken cancellationToken = default)
        => _runtime.GetBrainAsync("local", cancellationToken);
}
