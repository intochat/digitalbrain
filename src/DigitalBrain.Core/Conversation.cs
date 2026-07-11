using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

/// <summary>Conversation contracts are scoped by authenticated tenant/workspace identity.</summary>
public sealed record ConversationRequest(
    RequestContext Context,
    string ConversationId,
    string Text,
    bool AllowTools = true);

public sealed record ConversationContext(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    string ConversationId,
    IReadOnlyList<string> MemoryEvidence);

public sealed record ModelRequest(
    string Text,
    ConversationContext Context,
    bool StructuredOutput,
    IReadOnlyList<ToolOutcome>? ToolOutcomes = null);
public sealed record ModelResponse(string Text, string Model, bool IsStructured);

public static class InoConversationIdentity
{
    public static string From(RequestContext context) => "ino-" + RequestScope.Id(context);
}

public static class InoConversationStates
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

public sealed record InoConversationTurn(
    string CommandId,
    string Role,
    string Text,
    string State);

public sealed record InoConversationOperation(
    string CommandId,
    string Prompt,
    string State,
    string? SafeReason,
    bool Retryable,
    DateTimeOffset UpdatedAt,
    ToolAction? Action = null,
    ToolGrounding? Grounding = null,
    IReadOnlyList<ToolGrounding>? Groundings = null);

public sealed record InoConversationSnapshot(
    string ConversationId,
    int Revision,
    IReadOnlyList<InoConversationTurn> Turns,
    IReadOnlyList<InoConversationOperation> Operations)
{
    public InoConversationOperation? CurrentOperation => Operations.LastOrDefault();

    public static InoConversationSnapshot Empty(RequestContext context) =>
        new(InoConversationIdentity.From(context), 0, [], []);
}

public interface IInoConversationStore
{
    InoConversationSnapshot Read(RequestContext context);
    InoConversationSnapshot Begin(RequestContext context, string commandId, string prompt);
    InoConversationSnapshot Transition(RequestContext context, string commandId, string state);
    InoConversationSnapshot Complete(
        RequestContext context,
        string commandId,
        string response,
        ToolAction? action = null,
        ToolGrounding? grounding = null,
        IReadOnlyList<ToolGrounding>? groundings = null);
    InoConversationSnapshot Fail(RequestContext context, string commandId, string safeReason, bool retryable);
}

public enum ToolOutcomeKind { Success, NeedsAuth, Denied, RetryableFailure, PermanentFailure, OutcomeUnknown, Cancelled }
public sealed record ToolAction(string Kind, string Label, string Target);
public sealed record ToolGrounding(string ToolId, JsonElement Content);
public sealed record ToolOutcome(
    ToolOutcomeKind Kind,
    JsonElement? Content = null,
    string? SafeReason = null,
    ToolAction? Action = null,
    JsonElement? GroundingContent = null);
public sealed record ToolInvocation(string ToolId, JsonElement Input);
public sealed record ConversationExecutionResult(
    string Text,
    ToolAction? Action = null,
    ToolGrounding? Grounding = null,
    IReadOnlyList<ToolGrounding>? Groundings = null);

public interface IIntentCapabilityPlanner
{
    Task<IReadOnlyList<ToolInvocation>> PlanAsync(ConversationRequest request, CancellationToken cancellationToken = default);
}

public interface IContextAssembler
{
    Task<ConversationContext> AssembleAsync(ConversationRequest request, CancellationToken cancellationToken = default);
}

public interface IMemoryQueryService
{
    Task<IReadOnlyList<string>> QueryAsync(TenantId tenantId, WorkspaceId workspaceId, string conversationId, string text, CancellationToken cancellationToken = default);
}

public interface IModelRouter
{
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken = default);
}

public interface IAuthorizedToolCatalog
{
    Task<ToolOutcome> InvokeAsync(RequestContext context, ToolInvocation invocation, CancellationToken cancellationToken = default);
}

public interface IResponseSurfaceComposer
{
    Task<string> ComposeAsync(RequestContext context, ModelResponse response, IReadOnlyList<ToolOutcome> toolOutcomes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Scoped conversation owner. It has no global journal access and never accepts a caller-supplied principal.
/// Side-effecting tools are delegated to an authorized durable catalog.
/// </summary>
public sealed class ConversationOwner(
    IContextAssembler contextAssembler,
    IIntentCapabilityPlanner planner,
    IModelRouter modelRouter,
    IAuthorizedToolCatalog toolCatalog,
    IResponseSurfaceComposer composer)
{
    public async Task<string> ExecuteAsync(ConversationRequest request, CancellationToken cancellationToken = default)
        => (await ExecuteDetailedAsync(request, cancellationToken)).Text;

    public async Task<ConversationExecutionResult> ExecuteDetailedAsync(
        ConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Context.TenantId.Value.Length == 0 || request.Context.WorkspaceId.Value.Length == 0)
            throw new UnauthorizedAccessException("Conversation scope is required.");
        if (string.IsNullOrWhiteSpace(request.ConversationId) || string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("ConversationId and Text are required.");

        var scoped = await contextAssembler.AssembleAsync(request, cancellationToken);
        if (scoped.TenantId != request.Context.TenantId || scoped.WorkspaceId != request.Context.WorkspaceId || scoped.ConversationId != request.ConversationId)
            throw new UnauthorizedAccessException("The context assembler returned an out-of-scope context.");

        var outcomes = new List<ToolOutcome>();
        var invocations = new List<ToolInvocation>();
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
                new ModelRequest(request.Text, scoped, StructuredOutput: true),
                cancellationToken)
            : new ModelResponse(string.Empty, "deterministic-tool-response", IsStructured: true);
        var text = await composer.ComposeAsync(request.Context, model, outcomes, cancellationToken);
        var groundings = invocations
            .Zip(outcomes)
            .Where(static pair => pair.Second.Kind == ToolOutcomeKind.Success &&
                                  pair.Second.Content is not null &&
                                  pair.Second.GroundingContent is not null &&
                                  (pair.First.ToolId.StartsWith("gmail.", StringComparison.Ordinal) ||
                                   pair.First.ToolId.StartsWith("salesforce.", StringComparison.Ordinal) ||
                                   pair.First.ToolId.StartsWith("cross.", StringComparison.Ordinal)))
            .Select(static pair => new ToolGrounding(
                pair.First.ToolId,
                pair.Second.GroundingContent!.Value.Clone()))
            .ToArray();
        return new ConversationExecutionResult(
            text,
            outcomes.Select(static outcome => outcome.Action).FirstOrDefault(static action => action is not null),
            groundings.FirstOrDefault(),
            groundings);
    }
}
