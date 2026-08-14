using System.ComponentModel;
using Brain.Runtime.Abstractions;
using DigitalBrain.ProductHost.Protocol;
using ModelContextProtocol.Server;

namespace DigitalBrain.ProductHost.Mcp;

[McpServerToolType]
public sealed class ProductMcpTools(IProductRuntimeClient runtime)
{
    private readonly IProductRuntimeClient _runtime = runtime;

    [McpServerTool(Name = "digitalbrain_list_modules")]
    [Description("List DigitalBrain product modules and their readiness state.")]
    public Task<IReadOnlyList<RuntimeModuleDescriptor>> ListModulesAsync(
        CancellationToken cancellationToken = default)
        => _runtime.GetModulesAsync(cancellationToken);

    [McpServerTool(Name = "digitalbrain_list_operations")]
    [Description("List operations exposed by ready DigitalBrain modules.")]
    public Task<IReadOnlyList<RuntimeOperationDescriptor>> ListOperationsAsync(
        CancellationToken cancellationToken = default)
        => _runtime.GetOperationsAsync(cancellationToken);

    [McpServerTool(Name = "digitalbrain_invoke")]
    [Description("Invoke a DigitalBrain operation in the local workspace and return its durable activity receipt.")]
    public Task<RuntimeActivityReceipt> InvokeAsync(
        [Description("Canonical operation id, for example proof/run@1")] string operationId,
        [Description("Operation input as JSON")] string inputJson,
        [Description("Caller-supplied idempotency key")] string idempotencyKey,
        CancellationToken cancellationToken = default)
        => _runtime.InvokeAsync(
            new RuntimeInvocation(operationId, inputJson, "local", "owner", idempotencyKey),
            cancellationToken);

    [McpServerTool(Name = "digitalbrain_get_activity")]
    [Description("Read a durable DigitalBrain activity in the local workspace.")]
    public Task<RuntimeActivitySnapshot?> GetActivityAsync(
        [Description("Activity id returned by digitalbrain_invoke")] Guid activity,
        CancellationToken cancellationToken = default)
        => _runtime.GetActivityAsync(activity, "local", cancellationToken);
}
