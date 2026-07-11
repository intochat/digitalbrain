using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class McpInoCommandHandler(
    IInoConversationStore conversations,
    WorkspaceSurfaceProducer surfaces,
    ConversationOwner owner) : ICommandHandler
{
    private const string SafeFailure = "I couldn’t finish that response. Please try a new message.";
    public const string CommandType = "ino.interact";

    public bool CanHandle(string commandType) => string.Equals(commandType, CommandType, StringComparison.Ordinal);

    public async Task<CommandExecutionResult> ExecuteAsync(
        CommandEnvelope command,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.conversation.execute", ActivityKind.Internal);
        activity?.SetTag("db.ino.command_type", command.Type);
        if (!TryGetPrompt(command.Payload, out var prompt))
        {
            activity?.SetTag("db.ino.outcome", "invalid");
            return new CommandExecutionResult(WorkflowState.Failed, "ino-request-invalid");
        }

        var snapshot = conversations.Begin(command.Context, command.CommandId, prompt);
        var prior = snapshot.Operations.Single(operation =>
            string.Equals(operation.CommandId, command.CommandId, StringComparison.Ordinal));
        if (string.Equals(prior.State, InoConversationStates.Succeeded, StringComparison.Ordinal))
        {
            activity?.SetTag("db.ino.replay", true);
            activity?.SetTag("db.ino.outcome", "succeeded");
            surfaces.PublishInoConversation(command.Context, snapshot);
            return CommandExecutionResult.Success();
        }
        if (string.Equals(prior.State, InoConversationStates.Failed, StringComparison.Ordinal))
        {
            activity?.SetTag("db.ino.replay", true);
            activity?.SetTag("db.ino.outcome", "failed");
            surfaces.PublishInoConversation(command.Context, snapshot);
            return new CommandExecutionResult(WorkflowState.Failed, prior.SafeReason);
        }

        surfaces.PublishInoConversation(command.Context, snapshot);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = conversations.Transition(command.Context, command.CommandId, InoConversationStates.Running);
            surfaces.PublishInoConversation(command.Context, snapshot);
            snapshot = conversations.Transition(command.Context, command.CommandId, InoConversationStates.Responding);
            surfaces.PublishInoConversation(command.Context, snapshot);

            var response = await owner.ExecuteDetailedAsync(new ConversationRequest(
                command.Context,
                snapshot.ConversationId,
                prompt,
                AllowTools: true), cancellationToken).ConfigureAwait(false);
            snapshot = conversations.Complete(
                command.Context,
                command.CommandId,
                response.Text,
                response.Action,
                response.Grounding,
                response.Groundings);
            activity?.SetTag("db.ino.grounding_count", response.Groundings?.Count ?? (response.Grounding is null ? 0 : 1));
            activity?.SetTag("db.ino.outcome", "succeeded");
            surfaces.PublishInoConversation(command.Context, snapshot);
            return CommandExecutionResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            activity?.SetTag("db.ino.outcome", "cancelled");
            snapshot = conversations.Fail(command.Context, command.CommandId, SafeFailure, retryable: false);
            surfaces.PublishInoConversation(command.Context, snapshot);
            return new CommandExecutionResult(WorkflowState.Failed, SafeFailure);
        }
        catch (Exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "failed");
            activity?.SetTag("db.ino.outcome", "failed");
            snapshot = conversations.Fail(command.Context, command.CommandId, SafeFailure, retryable: false);
            surfaces.PublishInoConversation(command.Context, snapshot);
            return new CommandExecutionResult(WorkflowState.Failed, SafeFailure);
        }
    }

    public static bool TryGetPrompt(JsonElement payload, out string prompt)
    {
        prompt = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object || payload.EnumerateObject().Count() != 1 ||
            !payload.TryGetProperty("prompt", out var value) || value.ValueKind != JsonValueKind.String)
            return false;
        prompt = value.GetString()?.Trim() ?? string.Empty;
        return prompt.Length is > 0 and <= 4096;
    }
}

public sealed class McpConversationContextAssembler(IInoConversationStore conversations) : IContextAssembler
{
    public Task<ConversationContext> AssembleAsync(
        ConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = conversations.Read(request.Context);
        if (!string.Equals(snapshot.ConversationId, request.ConversationId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The conversation is outside the authenticated scope.");
        var history = snapshot.Turns
            .Where(turn => !(turn.Role == "user" && string.Equals(turn.Text, request.Text, StringComparison.Ordinal) &&
                             string.Equals(turn.CommandId, snapshot.CurrentOperation?.CommandId, StringComparison.Ordinal)))
            .TakeLast(12)
            .Select(static turn => $"{turn.Role}: {turn.Text}")
            .ToArray();
        return Task.FromResult(new ConversationContext(
            request.Context.TenantId,
            request.Context.WorkspaceId,
            request.ConversationId,
            history));
    }
}

public sealed class McpConversationModelRouter(IClusterClient cluster) : IModelRouter
{
    public async Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var grainId = GrainIds.Conversation(
            request.Context.TenantId,
            request.Context.WorkspaceId,
            request.Context.ConversationId);
        var model = cluster.GetGrain<IConversationModelGrain>(grainId);
        var response = await model.CompleteAsync(
            new ConversationModelCompletionRequest(
                request.Text,
                request.Context.MemoryEvidence,
                request.ToolOutcomes?.Select(static outcome => new ConversationModelToolOutcome(
                    outcome.Kind.ToString(),
                    outcome.Content?.GetRawText(),
                    outcome.SafeReason)).ToArray()),
            cancellationToken).ConfigureAwait(false);
        return new ModelResponse(response.Text, response.Model, IsStructured: false);
    }
}

public sealed class McpNoToolPlanner : IIntentCapabilityPlanner
{
    public Task<IReadOnlyList<ToolInvocation>> PlanAsync(
        ConversationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ToolInvocation>>([]);
}

public sealed class McpNoToolCatalog : IAuthorizedToolCatalog
{
    public Task<ToolOutcome> InvokeAsync(
        RuntimeRequestContext context,
        ToolInvocation invocation,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ToolOutcome(ToolOutcomeKind.Denied, SafeReason: "Tools are unavailable in this conversation."));
}

public interface ISemanticIntentResolver
{
    Task<SemanticIntentProposal> ResolveAsync(
        SemanticIntentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class McpSemanticIntentResolver(IClusterClient cluster) : ISemanticIntentResolver
{
    public async Task<SemanticIntentProposal> ResolveAsync(
        SemanticIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.intent.model", ActivityKind.Client);
        activity?.SetTag("db.ino.grounding_descriptor_count", request.Groundings.Count);
        var grainId = GrainIds.Conversation(
            new TenantId(request.TenantId),
            new WorkspaceId(request.WorkspaceId),
            request.ConversationId);
        var proposal = await cluster.GetGrain<IConversationModelGrain>(grainId)
            .ResolveIntentAsync(request, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.provider", proposal.Provider.ToString());
        activity?.SetTag("db.ino.operation", proposal.Operation.ToString());
        return proposal;
    }
}

public sealed class McpIntegrationPlanner : IIntentCapabilityPlanner
{
    private const int MaximumDescriptors = 12;
    private const int MaximumSemanticText = 256;
    private const string SalesforceCapabilityMessage =
        "I can safely discover and search Salesforce objects, read details and related records, aggregate, sort, and page results. Ask for a specific account, opportunity, or object; if Salesforce isn’t connected, I’ll ask you to connect it first.";
    private static readonly JsonSerializerOptions SemanticJson = CreateSemanticJson();
    private readonly ISemanticIntentResolver _semanticIntents;
    private readonly IInoConversationStore? _conversations;

    public McpIntegrationPlanner(
        ISemanticIntentResolver semanticIntents,
        IInoConversationStore? conversations = null)
    {
        _semanticIntents = semanticIntents;
        _conversations = conversations;
    }

    public McpIntegrationPlanner() : this(new UnavailableSemanticIntentResolver()) { }

    public McpIntegrationPlanner(IInoConversationStore conversations)
        : this(new UnavailableSemanticIntentResolver(), conversations) { }

    public async Task<IReadOnlyList<ToolInvocation>> PlanAsync(
        ConversationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.intent.plan", ActivityKind.Internal);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsSalesforceCapabilityHelp(request.Text))
        {
            activity?.SetTag("db.ino.provider", SemanticProvider.Salesforce.ToString());
            activity?.SetTag("db.ino.operation", SemanticOperation.Answer.ToString());
            activity?.SetTag("db.ino.outcome", "capability-help");
            return [Clarification(SalesforceCapabilityMessage)];
        }

        var descriptors = GroundingDescriptors(request);
        activity?.SetTag("db.ino.grounding_descriptor_count", descriptors.Count);
        var semanticRequest = new SemanticIntentRequest(
            request.Context.TenantId.Value,
            request.Context.WorkspaceId.Value,
            request.ConversationId,
            request.Text,
            descriptors);
        SemanticIntentProposal proposal;
        try
        {
            proposal = await _semanticIntents.ResolveAsync(semanticRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error, "intent-resolution-failed");
            activity?.SetTag("db.ino.outcome", "clarify");
            return [Clarification("I couldn’t safely determine which connected service you meant. Please name Gmail or Salesforce and the result you want.")];
        }

        if (!TryNormalize(proposal, out var normalized))
        {
            activity?.SetTag("db.ino.outcome", "invalid-proposal");
            return [Clarification("I need a little more detail before I can use a connected service safely.")];
        }
        activity?.SetTag("db.ino.provider", normalized.Provider.ToString());
        activity?.SetTag("db.ino.operation", normalized.Operation.ToString());
        if (normalized.Operation is SemanticOperation.QueryLanguage or SemanticOperation.Delete or
            SemanticOperation.MutationConfirm)
        {
            activity?.SetTag("db.ino.outcome", "denied");
            return [Clarification("I can use bounded typed reads and create a mutation preview, but I can’t run raw queries, deletes, or unbound confirmations.")];
        }
        if (normalized.Provider == SemanticProvider.None && normalized.Operation == SemanticOperation.Answer)
        {
            activity?.SetTag("db.ino.outcome", "general-answer");
            return [];
        }
        if (normalized.Provider == SemanticProvider.Ambiguous || normalized.Operation == SemanticOperation.Clarify)
        {
            activity?.SetTag("db.ino.outcome", "clarify");
            return [Clarification(SafeClarification(normalized.Provider))];
        }

        var toolId = ToolId(normalized);
        activity?.SetTag("db.ino.tool_id", toolId ?? "assistant.clarify");
        activity?.SetTag("db.ino.outcome", toolId is null ? "unsupported" : "planned");
        return toolId is null
            ? [Clarification(normalized.Provider == SemanticProvider.Salesforce &&
                             normalized.Operation == SemanticOperation.Answer
                ? SalesforceCapabilityMessage
                : "That connected-service operation isn’t available safely yet.")]
            : [new ToolInvocation(toolId, JsonSerializer.SerializeToElement(normalized, SemanticJson))];
    }

    private IReadOnlyList<GroundingDescriptor> GroundingDescriptors(ConversationRequest request)
    {
        if (_conversations is null) return [];
        var operations = _conversations.Read(request.Context).Operations;
        var result = new List<GroundingDescriptor>();
        var distance = 0;
        foreach (var operation in operations.Reverse())
        {
            if (InoConversationStates.IsActive(operation.State)) continue;
            distance++;
            if (!string.Equals(operation.State, InoConversationStates.Succeeded, StringComparison.Ordinal)) continue;
            var operationGroundings = operation.Groundings is { Count: > 0 }
                ? operation.Groundings
                : operation.Grounding is { } single
                    ? [single]
                    : [];
            foreach (var grounding in operationGroundings)
            {
                result.Add(new GroundingDescriptor(
                    Provider(grounding.ToolId),
                    grounding.ToolId,
                    ResultCount(grounding.Content),
                    HasContinuation(grounding.Content),
                    distance));
                if (result.Count == MaximumDescriptors) break;
            }
            if (result.Count == MaximumDescriptors) break;
        }
        return result;
    }

    private static string? ToolId(SemanticIntentProposal proposal) => proposal.Provider switch
    {
        SemanticProvider.Gmail => proposal.Operation switch
        {
            SemanticOperation.List or SemanticOperation.Refine or SemanticOperation.Previous => GmailTools.ReadMessages,
            SemanticOperation.Overview => GmailTools.ReadMailboxOverview,
            SemanticOperation.Threads => GmailTools.ReadThreads,
            SemanticOperation.Summarize => GmailTools.SummarizeThread,
            _ => null
        },
        SemanticProvider.Salesforce => proposal.Operation switch
        {
            SemanticOperation.Discover => SalesforceTools.DiscoverObjects,
            SemanticOperation.Search => SalesforceTools.SearchRecords,
            SemanticOperation.Aggregate => SalesforceTools.AggregateRecords,
            SemanticOperation.NextPage => SalesforceTools.ContinueRecords,
            SemanticOperation.MutationPreview => SalesforceTools.PreviewMutation,
            SemanticOperation.List or SemanticOperation.Refine or SemanticOperation.Related or
                SemanticOperation.Details or SemanticOperation.Previous => SalesforceTools.ReadRecords,
            _ => null
        },
        SemanticProvider.CrossProvider when proposal.Operation == SemanticOperation.Match &&
                                                 proposal.Reference == SemanticReference.LatestGmailSender =>
            CrossProviderTools.MatchSalesforceAccountToGmailSender,
        _ => null
    };

    private static bool TryNormalize(
        SemanticIntentProposal? proposal,
        out SemanticIntentProposal normalized)
    {
        normalized = default!;
        if (proposal is null || proposal.Limit is < 1 or > GmailTools.MaximumResultCount ||
            proposal.Ordinal is < 1 or > GmailTools.MaximumResultCount ||
            proposal.Filters is { Count: > 8 } || proposal.Sorts is { Count: > 8 } ||
            !ValidText(proposal.Entity, required: false) ||
            !ValidText(proposal.SearchText, required: false) ||
            proposal.Filters?.Any(static filter =>
                !ValidText(filter.Field, required: true) || !ValidText(filter.Value, required: false)) == true ||
            proposal.Sorts?.Any(static sort => !ValidText(sort.Field, required: true)) == true ||
            (proposal.Aggregate is { } aggregate &&
             (!ValidText(aggregate.Field, required: false) || !ValidText(aggregate.GroupBy, required: false))))
            return false;

        normalized = proposal with
        {
            Entity = NormalizeText(proposal.Entity),
            SearchText = NormalizeText(proposal.SearchText),
            Clarification = null,
            Filters = proposal.Filters?.Select(static filter => filter with
            {
                Field = filter.Field.Trim(),
                Value = NormalizeText(filter.Value)
            }).ToArray(),
            Sorts = proposal.Sorts?.Select(static sort => sort with { Field = sort.Field.Trim() }).ToArray(),
            Aggregate = proposal.Aggregate is null
                ? null
                : proposal.Aggregate with
                {
                    Field = NormalizeText(proposal.Aggregate.Field),
                    GroupBy = NormalizeText(proposal.Aggregate.GroupBy)
                }
        };
        return true;
    }

    private static bool ValidText(string? value, bool required) =>
        value is null ? !required : value.Trim().Length is > 0 and <= MaximumSemanticText && !value.Any(char.IsControl);

    private static string? NormalizeText(string? value) => value?.Trim();

    private static bool IsSalesforceCapabilityHelp(string prompt) => NormalizeCapabilityPrompt(prompt) is
        "tell me how salesforce works" or
        "tell me how my salesforce works" or
        "tell me how current salesforce works" or
        "tell me how my current salesforce works" or
        "how does salesforce work" or
        "how does my salesforce work" or
        "how does current salesforce work" or
        "how does my current salesforce work" or
        "what can salesforce do" or
        "what can my salesforce do" or
        "what can i do with salesforce" or
        "salesforce capabilities" or
        "what are salesforce capabilities" or
        "what are the salesforce capabilities";

    private static string NormalizeCapabilityPrompt(string prompt)
    {
        var characters = prompt.Select(static character =>
            char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ').ToArray();
        return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string SafeClarification(SemanticProvider provider) => provider switch
    {
        SemanticProvider.Ambiguous => "Do you mean Gmail or Salesforce?",
        SemanticProvider.Gmail => "What should I look up in Gmail?",
        SemanticProvider.Salesforce => "What should I look up in Salesforce?",
        SemanticProvider.CrossProvider => "What should I match between Gmail and Salesforce?",
        _ => "What should I look up, and in which connected service: Gmail or Salesforce?"
    };

    private static ToolInvocation Clarification(string message) =>
        new(AssistantTools.Clarify, JsonSerializer.SerializeToElement(new { message }));

    private static string Provider(string toolId) => toolId.StartsWith("gmail.", StringComparison.Ordinal)
        ? "gmail"
        : toolId.StartsWith("salesforce.", StringComparison.Ordinal)
            ? "salesforce"
            : "crossProvider";

    private static int ResultCount(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return 0;
        if (content.TryGetProperty("resultCount", out var directCount) && directCount.TryGetInt32(out var count))
            return Math.Max(0, count);
        foreach (var property in content.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array) return property.Value.GetArrayLength();
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            if (property.Value.TryGetProperty("resultCount", out var nestedCount) && nestedCount.TryGetInt32(out count))
                return Math.Max(0, count);
            foreach (var arrayName in new[] { "resultMessageIds", "threadIds", "recordIds" })
                if (property.Value.TryGetProperty(arrayName, out var values) && values.ValueKind == JsonValueKind.Array)
                    return values.GetArrayLength();
        }
        return 0;
    }

    private static bool HasContinuation(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return false;
        if (content.TryGetProperty("hasMore", out var direct) && direct.ValueKind == JsonValueKind.True) return true;
        return content.EnumerateObject().Any(static property =>
            property.Value.ValueKind == JsonValueKind.Object &&
            property.Value.TryGetProperty("hasMore", out var nested) && nested.ValueKind == JsonValueKind.True);
    }

    private static JsonSerializerOptions CreateSemanticJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class UnavailableSemanticIntentResolver : ISemanticIntentResolver
    {
        public Task<SemanticIntentProposal> ResolveAsync(
            SemanticIntentRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SemanticIntentProposal(SemanticProvider.None, SemanticOperation.Answer));
    }
}
