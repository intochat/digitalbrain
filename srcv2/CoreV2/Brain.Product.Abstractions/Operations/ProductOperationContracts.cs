using System.Text.Json;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;
using Brain.Product.Abstractions.Activities;
using Brain.Product.Abstractions.Authority;

namespace Brain.Product.Abstractions.Operations;

public sealed record WorkspacePresentation
{
    public WorkspacePresentation(WorkspaceId workspace, string displayName)
    {
        if (workspace.IsEmpty)
        {
            throw new ArgumentException("A workspace presentation requires a workspace.", nameof(workspace));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName, nameof(displayName));
        Workspace = workspace;
        DisplayName = displayName;
    }

    public WorkspaceId Workspace { get; }

    public string DisplayName { get; }
}

public sealed record ProductInvocationContext
{
    public ProductInvocationContext(BrainAccessGrant accessGrant, IdempotencyKey idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(accessGrant);
        if (string.IsNullOrWhiteSpace(idempotencyKey.Value))
        {
            throw new ArgumentException("A product invocation requires an idempotency key.", nameof(idempotencyKey));
        }

        AccessGrant = accessGrant;
        IdempotencyKey = idempotencyKey;
    }

    public BrainAccessGrant AccessGrant { get; }

    public IdempotencyKey IdempotencyKey { get; }
}

public sealed record ProductOperationDescriptor
{
    public ProductOperationDescriptor(
        OperationDescriptor operation,
        string displayName,
        string inputSchema,
        string terminalResultSchema)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName, nameof(displayName));
        ArgumentException.ThrowIfNullOrWhiteSpace(inputSchema, nameof(inputSchema));
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalResultSchema, nameof(terminalResultSchema));

        Operation = operation;
        DisplayName = displayName;
        InputSchema = inputSchema;
        TerminalResultSchema = terminalResultSchema;
    }

    public OperationDescriptor Operation { get; }

    public string DisplayName { get; }

    public string InputSchema { get; }

    public string TerminalResultSchema { get; }
}

public interface IProductOperationAdapter
{
    IReadOnlyList<ProductOperationDescriptor> Operations { get; }

    Task<ProductActivityReceipt> InvokeAsync(
        OperationId operation,
        JsonElement input,
        ProductInvocationContext context,
        CancellationToken cancellationToken);

    Task<ProductActivityProjection> ObserveAsync(
        BrainActivityId activity,
        ProductInvocationContext context,
        CancellationToken cancellationToken);
}
