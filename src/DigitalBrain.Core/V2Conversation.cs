using System.Text.Json;

namespace DigitalBrain.Core.V2;

/// <summary>V2 conversation contracts are scoped by authenticated tenant/workspace identity.</summary>
public sealed record V2ConversationRequest(
    RequestContext Context,
    string ConversationId,
    string Text,
    bool AllowTools = true);

public sealed record V2ConversationContext(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    string ConversationId,
    IReadOnlyList<string> MemoryEvidence);

public sealed record V2ModelRequest(string Text, V2ConversationContext Context, bool StructuredOutput);
public sealed record V2ModelResponse(string Text, string Model, bool IsStructured);

public enum V2ToolOutcomeKind { Success, NeedsAuth, Denied, RetryableFailure, PermanentFailure, OutcomeUnknown, Cancelled }
public sealed record V2ToolOutcome(V2ToolOutcomeKind Kind, JsonElement? Content = null, string? SafeReason = null);
public sealed record V2ToolInvocation(string ToolId, JsonElement Input);

public interface IV2IntentCapabilityPlanner
{
    Task<IReadOnlyList<V2ToolInvocation>> PlanAsync(V2ConversationRequest request, CancellationToken cancellationToken = default);
}

public interface IV2ContextAssembler
{
    Task<V2ConversationContext> AssembleAsync(V2ConversationRequest request, CancellationToken cancellationToken = default);
}

public interface IV2MemoryQueryService
{
    Task<IReadOnlyList<string>> QueryAsync(TenantId tenantId, WorkspaceId workspaceId, string conversationId, string text, CancellationToken cancellationToken = default);
}

public interface IV2ModelRouter
{
    Task<V2ModelResponse> CompleteAsync(V2ModelRequest request, CancellationToken cancellationToken = default);
}

public interface IV2AuthorizedToolCatalog
{
    Task<V2ToolOutcome> InvokeAsync(RequestContext context, V2ToolInvocation invocation, CancellationToken cancellationToken = default);
}

public interface IV2ResponseSurfaceComposer
{
    Task<string> ComposeAsync(RequestContext context, V2ModelResponse response, IReadOnlyList<V2ToolOutcome> toolOutcomes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Scoped V2 conversation owner. It has no global journal access and never accepts a caller-supplied principal.
/// Side-effecting tools are delegated to an authorized durable catalog.
/// </summary>
public sealed class V2ConversationOwner(
    IV2ContextAssembler contextAssembler,
    IV2IntentCapabilityPlanner planner,
    IV2ModelRouter modelRouter,
    IV2AuthorizedToolCatalog toolCatalog,
    IV2ResponseSurfaceComposer composer)
{
    public async Task<string> ExecuteAsync(V2ConversationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Context.TenantId.Value.Length == 0 || request.Context.WorkspaceId.Value.Length == 0)
            throw new UnauthorizedAccessException("V2 conversation scope is required.");
        if (string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("ConversationId and Text are required.");

        var scoped = await contextAssembler.AssembleAsync(request, cancellationToken);
        if (scoped.TenantId != request.Context.TenantId || scoped.WorkspaceId != request.Context.WorkspaceId || scoped.ConversationId != request.ConversationId)
            throw new UnauthorizedAccessException("V2 context assembler returned an out-of-scope context.");

        var outcomes = new List<V2ToolOutcome>();
        if (request.AllowTools)
        {
            foreach (var invocation in await planner.PlanAsync(request, cancellationToken))
                outcomes.Add(await toolCatalog.InvokeAsync(request.Context, invocation, cancellationToken));
        }

        var model = await modelRouter.CompleteAsync(new V2ModelRequest(request.Text, scoped, StructuredOutput: true), cancellationToken);
        return await composer.ComposeAsync(request.Context, model, outcomes, cancellationToken);
    }
}
