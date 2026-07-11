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

public sealed record V2ModelRequest(
    string Text,
    V2ConversationContext Context,
    bool StructuredOutput,
    IReadOnlyList<V2ToolOutcome>? ToolOutcomes = null);
public sealed record V2ModelResponse(string Text, string Model, bool IsStructured);

public static class V2InoConversationIdentity
{
    public static string From(RequestContext context) => "ino-" + V2RequestScope.Id(context);
}

public static class V2InoConversationStates
{
    public const string Idle = "idle";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Responding = "responding";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    public static bool IsActive(string state) =>
        state is Queued or Running or Responding;
}

public sealed record V2InoConversationTurn(
    string CommandId,
    string Role,
    string Text,
    string State);

public sealed record V2InoConversationOperation(
    string CommandId,
    string Prompt,
    string State,
    string? SafeReason,
    bool Retryable,
    DateTimeOffset UpdatedAt,
    V2ToolAction? Action = null,
    V2ToolGrounding? Grounding = null,
    IReadOnlyList<V2ToolGrounding>? Groundings = null);

public sealed record V2InoConversationSnapshot(
    string ConversationId,
    int Revision,
    IReadOnlyList<V2InoConversationTurn> Turns,
    IReadOnlyList<V2InoConversationOperation> Operations)
{
    public V2InoConversationOperation? CurrentOperation => Operations.LastOrDefault();

    public static V2InoConversationSnapshot Empty(RequestContext context) =>
        new(V2InoConversationIdentity.From(context), 0, [], []);
}

public interface IV2InoConversationStore
{
    V2InoConversationSnapshot Read(RequestContext context);
    V2InoConversationSnapshot Begin(RequestContext context, string commandId, string prompt);
    V2InoConversationSnapshot Transition(RequestContext context, string commandId, string state);
    V2InoConversationSnapshot Complete(
        RequestContext context,
        string commandId,
        string response,
        V2ToolAction? action = null,
        V2ToolGrounding? grounding = null,
        IReadOnlyList<V2ToolGrounding>? groundings = null);
    V2InoConversationSnapshot Fail(RequestContext context, string commandId, string safeReason, bool retryable);
}

public enum V2ToolOutcomeKind { Success, NeedsAuth, Denied, RetryableFailure, PermanentFailure, OutcomeUnknown, Cancelled }
public sealed record V2ToolAction(string Kind, string Label, string Target);
public sealed record V2ToolGrounding(string ToolId, JsonElement Content);
public sealed record V2ToolOutcome(
    V2ToolOutcomeKind Kind,
    JsonElement? Content = null,
    string? SafeReason = null,
    V2ToolAction? Action = null,
    JsonElement? GroundingContent = null);
public sealed record V2ToolInvocation(string ToolId, JsonElement Input);
public sealed record V2ConversationExecutionResult(
    string Text,
    V2ToolAction? Action = null,
    V2ToolGrounding? Grounding = null,
    IReadOnlyList<V2ToolGrounding>? Groundings = null);

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
        => (await ExecuteDetailedAsync(request, cancellationToken)).Text;

    public async Task<V2ConversationExecutionResult> ExecuteDetailedAsync(
        V2ConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Context.TenantId.Value.Length == 0 || request.Context.WorkspaceId.Value.Length == 0)
            throw new UnauthorizedAccessException("V2 conversation scope is required.");
        if (string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("ConversationId and Text are required.");

        var scoped = await contextAssembler.AssembleAsync(request, cancellationToken);
        if (scoped.TenantId != request.Context.TenantId || scoped.WorkspaceId != request.Context.WorkspaceId || scoped.ConversationId != request.ConversationId)
            throw new UnauthorizedAccessException("V2 context assembler returned an out-of-scope context.");

        var outcomes = new List<V2ToolOutcome>();
        var invocations = new List<V2ToolInvocation>();
        if (request.AllowTools)
        {
            foreach (var invocation in await planner.PlanAsync(request, cancellationToken))
            {
                invocations.Add(invocation);
                outcomes.Add(await toolCatalog.InvokeAsync(request.Context, invocation, cancellationToken));
            }
        }

        var model = outcomes.Count == 0
            ? await modelRouter.CompleteAsync(
                new V2ModelRequest(request.Text, scoped, StructuredOutput: true),
                cancellationToken)
            : new V2ModelResponse(string.Empty, "deterministic-tool-response", IsStructured: true);
        var text = await composer.ComposeAsync(request.Context, model, outcomes, cancellationToken);
        var groundings = invocations
            .Zip(outcomes)
            .Where(static pair => pair.Second.Kind == V2ToolOutcomeKind.Success &&
                                  pair.Second.Content is not null &&
                                  pair.Second.GroundingContent is not null &&
                                  (pair.First.ToolId.StartsWith("gmail.", StringComparison.Ordinal) ||
                                   pair.First.ToolId.StartsWith("salesforce.", StringComparison.Ordinal) ||
                                   pair.First.ToolId.StartsWith("cross.", StringComparison.Ordinal)))
            .Select(static pair => new V2ToolGrounding(
                pair.First.ToolId,
                pair.Second.GroundingContent!.Value.Clone()))
            .ToArray();
        return new V2ConversationExecutionResult(
            text,
            outcomes.Select(static outcome => outcome.Action).FirstOrDefault(static action => action is not null),
            groundings.FirstOrDefault(),
            groundings);
    }
}
