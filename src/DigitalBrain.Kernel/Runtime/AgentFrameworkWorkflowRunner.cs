using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Features;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
namespace DigitalBrain.Kernel.Runtime;

internal sealed class AgentFrameworkWorkflowRunner(IServiceProvider services) : IAgentWorkflowRunner
{
    private const string RunnerName = "agent-framework";
    private const int MaximumCapabilityMatches = 3;
    private const int MaximumCandidateNameLength = 80;
    private static readonly int MaximumNormalizedPromptLength = Math.Min(
        HybridCapabilityResolver.MaximumPromptLength,
        Math.Min(CapabilityParameterRequest.MaximumPromptLength, FeatureLimits.DraftGoalCharacters));
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Ino.Workflow");
    private static readonly HashSet<string> ConversationalPrompts = new(StringComparer.Ordinal)
    {
        "hi",
        "hello",
        "hey",
        "hi there",
        "hello there",
        "thanks",
        "thank you",
        "help"
    };
    private static readonly HashSet<string> InterrogativeLeadWords = new(StringComparer.Ordinal)
    {
        "what", "whats", "how", "why", "when", "where", "who", "whos", "which",
        "is", "are", "am", "can", "could", "should", "would", "will", "do", "does", "did"
    };
    private static readonly HashSet<string> PoliteRequestLeadWords = new(StringComparer.Ordinal)
    {
        "can", "could", "would", "will"
    };
    private static readonly HashSet<string> ActionLeadWords = new(StringComparer.Ordinal)
    {
        "book", "buy", "cancel", "change", "create", "delete", "download", "order", "pay", "post",
        "publish", "remember", "research", "run", "save", "schedule", "send", "submit", "update", "upload", "write"
    };
    private static readonly Regex ControlCharacters = new(@"\p{Cc}", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    public async Task<InoWorkflowResult> ExecuteAsync(InoWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        var workflow = ResolveWorkflowReference(request);
        using var activity = ActivitySource.StartActivity("ino.workflow.execute", ActivityKind.Internal);
        activity?.SetTag("db.ino.operation_id", request.OperationId);
        activity?.SetTag("db.ino.workflow_id", workflow.WorkflowId);
        activity?.SetTag("db.ino.request_id", request.RequestId);
        if (services.GetService<ICapabilityResolver>() is not { } resolver)
            return await RunGeneralAgentAsync(request, workflow, capability: null, cancellationToken).ConfigureAwait(false);
        var normalizedPrompt = NormalizeCapabilityPrompt(request.Prompt);
        if (await TryOpenFeatureAsync(request, normalizedPrompt, workflow).ConfigureAwait(false) is { } openResult)
            return openResult;
        var snapshot = await ReadCapabilitySnapshotAsync(request, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return CapabilityCatalogUnavailableResult(workflow);
        var resolution = await resolver.ResolveAsync(
            new CapabilitySearchRequest(
                normalizedPrompt,
                (request.Grants ?? []).ToHashSet(StringComparer.Ordinal),
                snapshot.HealthyConnections,
                MaximumCapabilityMatches,
                snapshot.Descriptors),
            cancellationToken).ConfigureAwait(false);
        return resolution.Receipt.Kind switch
        {
            CapabilityResolutionKind.Ambiguous => AmbiguousResult(workflow, resolution),
            CapabilityResolutionKind.Missing => await ResolveMissingCapabilityAsync(
                request,
                normalizedPrompt,
                workflow,
                resolution.Receipt,
                resolver,
                snapshot,
                cancellationToken).ConfigureAwait(false),
            CapabilityResolutionKind.Match when !string.Equals(resolution.Receipt.CapabilityId, BuiltInCapabilityCatalog.AssistantAnswerCapabilityId, StringComparison.Ordinal) =>
                await ExecuteSelectedCapabilityAsync(request, normalizedPrompt, workflow, resolution, snapshot, cancellationToken).ConfigureAwait(false),
            _ => await RunGeneralAgentAsync(request, workflow, resolution.Receipt, cancellationToken).ConfigureAwait(false)
        };
    }
    private async Task<InoWorkflowResult> ResolveMissingCapabilityAsync(
        InoWorkflowRequest request,
        string normalizedPrompt,
        WorkflowReference workflow,
        CapabilityResolutionReceipt receipt,
        ICapabilityResolver resolver,
        OwnerCapabilityCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var unavailable = await ResolveUnavailableIntegrationAsync(
            request,
            normalizedPrompt,
            workflow,
            resolver,
            snapshot,
            cancellationToken).ConfigureAwait(false);
        return unavailable ?? await CreateMissingCapabilityResultAsync(
            request,
            normalizedPrompt,
            workflow,
            receipt,
            cancellationToken).ConfigureAwait(false);
    }
    private async Task<InoWorkflowResult?> ResolveUnavailableIntegrationAsync(
        InoWorkflowRequest request,
        string normalizedPrompt,
        WorkflowReference workflow,
        ICapabilityResolver resolver,
        OwnerCapabilityCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (request.OwnerId is not { } ownerId ||
            services.GetService<IOAuthStateProtector>() is not { } protector)
            return null;
        var grants = (request.Grants ?? []).ToHashSet(StringComparer.Ordinal);
        var descriptors = services.GetRequiredService<ICapabilityCatalog>().Snapshot()
            .Where(static descriptor =>
                descriptor.Available &&
                descriptor.Origin == CapabilityOrigin.Integration &&
                descriptor.RequiredConnections.Length == 1)
            .Where(descriptor => descriptor.RequiredGrants.All(grants.Contains))
            .Where(descriptor => !snapshot.HealthyConnections.Contains(descriptor.RequiredConnections[0]))
            .OrderBy(static descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToArray();
        if (descriptors.Length == 0)
            return null;
        var requiredConnections = descriptors
            .SelectMany(static descriptor => descriptor.RequiredConnections)
            .ToHashSet(StringComparer.Ordinal);
        var resolution = await resolver.ResolveAsync(
            new CapabilitySearchRequest(
                normalizedPrompt,
                grants,
                requiredConnections,
                MaximumCapabilityMatches,
                descriptors),
            cancellationToken).ConfigureAwait(false);
        if (resolution.Receipt.Kind == CapabilityResolutionKind.Ambiguous)
            return AmbiguousResult(workflow, resolution);
        if (resolution.Receipt.Kind != CapabilityResolutionKind.Match ||
            resolution.Selected is not { } descriptor)
            return null;
        var provider = descriptor.RequiredConnections[0];
        var authorization = new InoAuthorizationRequest(
            provider,
            descriptor.Id,
            StableAuthorizationAttemptId(request.OperationId, descriptor.Id),
            ResolveNow().AddMinutes(15),
            protector.Protect(new NeuronId(ownerId.Value)),
            $"Connect {ConnectionName(provider)} to continue with {descriptor.Name}.");
        return new InoWorkflowResult(
            authorization.SafeSummary,
            workflow,
            AuthorizationRequest: authorization,
            Capability: resolution.Receipt);
    }
    private static string NormalizeCapabilityPrompt(string prompt)
    {
        var withoutControlCharacters = ControlCharacters.Replace(prompt, " ");
        var collapsed = WhitespaceRun.Replace(withoutControlCharacters, " ").Trim();
        return collapsed.Length > MaximumNormalizedPromptLength ? collapsed[..MaximumNormalizedPromptLength] : collapsed;
    }
    private static InoWorkflowResult AmbiguousResult(
        WorkflowReference workflow,
        CapabilityResolution resolution)
    {
        var candidateIds = resolution.Receipt.CandidateIds
            .Take(MaximumCapabilityMatches)
            .ToHashSet(StringComparer.Ordinal);
        var names = resolution.Candidates
            .Where(candidate => candidateIds.Contains(candidate.Id))
            .Take(MaximumCapabilityMatches)
            .Select(static candidate => NormalizeCandidateName(candidate.Name))
            .Where(static name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var text = names.Length == 0
            ? "A few capabilities could match this request. Please choose one and ask again."
            : $"A few capabilities could match this request: {string.Join("; ", names)}. Please choose one and ask again.";
        return new InoWorkflowResult(text, workflow, Capability: resolution.Receipt);
    }
    private static string NormalizeCandidateName(string name)
    {
        var normalized = WhitespaceRun.Replace(ControlCharacters.Replace(name, " "), " ").Trim();
        return normalized.Length > MaximumCandidateNameLength ? normalized[..MaximumCandidateNameLength] : normalized;
    }
    private async Task<InoWorkflowResult> ExecuteSelectedCapabilityAsync(
        InoWorkflowRequest request,
        string normalizedPrompt,
        WorkflowReference workflow,
        CapabilityResolution resolution,
        OwnerCapabilityCatalogSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var descriptor = resolution.Selected
            ?? throw new InvalidOperationException("A matched capability must include its selected descriptor.");
        FeatureCapabilityBinding? featureBinding = null;
        IFeatureCapabilityInvoker? featureInvoker = null;
        if (descriptor.Origin == CapabilityOrigin.Feature)
        {
            featureBinding = snapshot.Bind(descriptor)?.Feature;
            featureInvoker = services.GetService<IFeatureCapabilityInvoker>();
            if (request.OwnerId is null || request.ActorId is null || request.OccurredAt is null ||
                featureBinding is null || featureInvoker is null)
                return FeatureUnavailableResult(workflow, resolution.Receipt);
        }
        var parameterModel = services.GetRequiredService<ICapabilityParameterModel>();
        var payload = await parameterModel.ExtractAsync(
            new CapabilityParameterRequest(descriptor, normalizedPrompt),
            cancellationToken).ConfigureAwait(false);
        if (descriptor.Origin == CapabilityOrigin.Feature)
        {
            var result = await featureInvoker!.InvokeAsync(
                new FeatureCapabilityInvocation(
                    descriptor,
                    featureBinding!,
                    request.OwnerId!.Value,
                    request.ActorId!.Value,
                    request.OperationId,
                    request.ConversationId,
                    request.RequestId,
                    request.OccurredAt!.Value,
                    payload),
                cancellationToken).ConfigureAwait(false);
            return result.Status switch
            {
                FeatureCapabilityInvocationStatus.Started => new InoWorkflowResult(
                    $"Started {resolution.Receipt.CapabilityName}.",
                    workflow,
                    ToolRequest: result.ToolRequest,
                    Capability: resolution.Receipt),
                FeatureCapabilityInvocationStatus.Busy => new InoWorkflowResult(
                    $"{resolution.Receipt.CapabilityName} is busy right now. Try again shortly.",
                    workflow,
                    Capability: resolution.Receipt),
                _ => FeatureUnavailableResult(workflow, resolution.Receipt)
            };
        }
        return new InoWorkflowResult(
            $"I can help with that using {resolution.Receipt.CapabilityName}.",
            workflow,
            Capability: resolution.Receipt);
    }
    private static InoWorkflowResult FeatureUnavailableResult(
        WorkflowReference workflow,
        CapabilityResolutionReceipt receipt) =>
        new("That Feature is not available right now. Check its access and connection, then try again.", workflow, Capability: receipt);
    private static InoWorkflowResult CapabilityCatalogUnavailableResult(WorkflowReference workflow) =>
        new("Capabilities are not available right now. Try again shortly.", workflow);
    private async Task<InoWorkflowResult> CreateMissingCapabilityResultAsync(
        InoWorkflowRequest request,
        string normalizedPrompt,
        WorkflowReference workflow,
        CapabilityResolutionReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (IsConversationalPrompt(normalizedPrompt))
            return await RunGeneralAgentAsync(request, workflow, receipt, cancellationToken).ConfigureAwait(false);
        if (services.GetService<IFeatureGrainResolver>() is not { } resolver || request.OwnerId is not { } ownerId)
            return MissingWithoutDraftResult(workflow, receipt);
        try
        {
            var hub = resolver.Hub(ownerId);
            var draft = await hub.CreateDraftAsync(
                new CreateFeatureDraft(request.OperationId, normalizedPrompt, ResolveNow(), request.ConversationId)).ConfigureAwait(false);
            var template = services.GetServices<IFeatureDraftTemplate>()
                .SingleOrDefault(candidate => candidate.SupportsDraft(normalizedPrompt));
            if (template is not null)
            {
                draft = await template.SeedAsync(
                    hub,
                    draft,
                    request.OperationId,
                    ResolveNow()).ConfigureAwait(false);
            }
            return new InoWorkflowResult(
                "I don’t have a trusted capability for that yet. I created a Feature draft. Open Studio to define and verify its behavior?",
                workflow,
                Capability: receipt,
                Proposal: new FeatureDraftReference(draft.DraftId.Value, "Open Studio", "/features/proposals/" + draft.DraftId.Value));
        }
        catch (InvalidOperationException)
        {
            return MissingWithoutDraftResult(workflow, receipt);
        }
    }
    private static InoWorkflowResult MissingWithoutDraftResult(WorkflowReference workflow, CapabilityResolutionReceipt receipt) =>
        new("I don't have a capability for that request yet.", workflow, Capability: receipt);
    private async Task<InoWorkflowResult?> TryOpenFeatureAsync(
        InoWorkflowRequest request,
        string normalizedPrompt,
        WorkflowReference workflow)
    {
        var prompt = normalizedPrompt.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
        if (request.OwnerId is not { } ownerId ||
            services.GetService<IFeatureGrainResolver>() is not { } grains)
            return null;
        var template = services.GetServices<IFeatureDraftTemplate>()
            .SingleOrDefault(candidate => candidate.SupportsOpen(prompt));
        if (template is null)
            return null;
        var drafts = await grains.Hub(ownerId).ReadDraftsAsync().ConfigureAwait(false);
        var matches = drafts.Where(template.MatchesDraft)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
            return null;
        var draft = matches[0];
        return new InoWorkflowResult(
            template.OpenedText,
            workflow,
            Proposal: new FeatureDraftReference(
                draft.DraftId.Value,
                "Open Studio",
                "/features/proposals/" + draft.DraftId.Value));
    }
    private DateTimeOffset ResolveNow() => services.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
    private static string StableAuthorizationAttemptId(string operationId, string capabilityId) =>
        new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(operationId + "\0" + capabilityId))[..16]).ToString("N");
    private static string ConnectionName(string provider) => provider switch
    {
        "google" => "Google",
        "salesforce" => "Salesforce",
        _ => provider
    };
    private static bool IsConversationalPrompt(string prompt)
    {
        var trimmed = prompt.Trim();
        if (IsActionablePrompt(trimmed)) return false;
        if (ConversationalPrompts.Contains(trimmed.TrimEnd('.', '!', '?').Trim().ToLowerInvariant())) return true;
        if (trimmed.EndsWith('?')) return true;
        return LeadWord(trimmed) is { } leadWord && InterrogativeLeadWords.Contains(leadWord);
    }
    private static bool IsActionablePrompt(string prompt)
    {
        var words = prompt
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(static token => new string(token.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(static token => token.Length > 0)
            .ToArray();
        if (words.Length == 0) return false;
        var actionIndex = 0;
        if (PoliteRequestLeadWords.Contains(words[0]))
        {
            actionIndex = 1;
            if (actionIndex < words.Length && string.Equals(words[actionIndex], "you", StringComparison.Ordinal)) actionIndex++;
            if (actionIndex < words.Length && string.Equals(words[actionIndex], "please", StringComparison.Ordinal)) actionIndex++;
        }
        return actionIndex < words.Length && ActionLeadWords.Contains(words[actionIndex]);
    }
    private static string? LeadWord(string trimmed)
    {
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end])) end++;
        var letters = new string(trimmed[..end].Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return letters.Length == 0 ? null : letters;
    }
    private async Task<InoWorkflowResult> RunGeneralAgentAsync(
        InoWorkflowRequest request,
        WorkflowReference workflow,
        CapabilityResolutionReceipt? capability,
        CancellationToken cancellationToken)
    {
        var chatClient = services.GetService<IChatClient>()
            ?? throw new InvalidOperationException("INO requires a configured Microsoft.Extensions.AI chat client.");
        var agent = new ChatClientAgent(
            chatClient,
            instructions: "You are INO, a concise workspace assistant. Never expose credentials, tokens, raw provider payloads, internal identifiers, or infrastructure details.",
            name: "ino");
        var session = await agent.CreateSessionAsync(workflow.SessionId, cancellationToken).ConfigureAwait(false);
        var messages = request.History.TakeLast(12).Select(static history => new ChatMessage(ChatRole.User, history))
            .Append(new ChatMessage(ChatRole.User, request.Prompt))
            .ToArray();
        var response = await agent.RunAsync(messages, session, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = response.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("The workflow returned an empty response.");
        return new InoWorkflowResult(text, workflow, Capability: capability);
    }
    private async Task<OwnerCapabilityCatalogSnapshot?> ReadCapabilitySnapshotAsync(
        InoWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OwnerId is { } ownerId && request.ActorId is { } actorId &&
            services.GetService<IOwnerCapabilityCatalog>() is { } ownerCatalog)
        {
            try
            {
                return await ownerCatalog.ReadAsync(ownerId, actorId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
        var entries = services.GetRequiredService<ICapabilityCatalog>().Snapshot()
            .Where(static descriptor => descriptor.Available &&
                descriptor.Origin == CapabilityOrigin.Platform &&
                descriptor.RequiredConnections.Length == 0)
            .Select(static descriptor => new CapabilityCatalogEntry(descriptor))
            .ToArray();
        return new OwnerCapabilityCatalogSnapshot(entries, new HashSet<string>(StringComparer.Ordinal));
    }
    private static WorkflowReference ResolveWorkflowReference(InoWorkflowRequest request)
    {
        var workflowId = RunnerName + "-" + request.OperationId;
        if (request.PriorWorkflow is not { } prior)
            return new WorkflowReference(RunnerName, workflowId, Guid.NewGuid().ToString("N"));
        if (!string.Equals(prior.Runner, RunnerName, StringComparison.Ordinal) ||
            !string.Equals(prior.WorkflowId, workflowId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(prior.SessionId))
            throw new ArgumentException("The prior workflow does not belong to this INO operation.", nameof(request));
        return prior;
    }
}
