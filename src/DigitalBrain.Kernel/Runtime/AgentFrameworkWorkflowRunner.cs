using System.Diagnostics;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

/// <summary>
/// A deliberately thin Agent Framework adapter. Orleans remains the lifecycle authority; this class owns only
/// one isolated agent/session invocation and returns its opaque identifiers to the caller.
/// </summary>
public sealed class AgentFrameworkWorkflowRunner(IServiceProvider services) : IAgentWorkflowRunner
{
    private const string RunnerName = "agent-framework";
    private const int MaximumSalesforceContentLength = 64 * 1024;
    private const int MaximumRenderedResultLength = 8 * 1024;
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EffectPlanLifetime = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions PlanJson = new(JsonSerializerDefaults.Web);
    private static readonly ActivitySource ActivitySource = new("DigitalBrain.Ino.Workflow");

    public async Task<InoWorkflowResult> ExecuteAsync(
        InoWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        var workflow = ResolveWorkflowReference(request);
        using var activity = ActivitySource.StartActivity("ino.workflow.execute", ActivityKind.Internal);
        activity?.SetTag("db.ino.operation_id", request.OperationId);
        activity?.SetTag("db.ino.workflow_id", workflow.WorkflowId);
        activity?.SetTag("db.ino.request_id", request.RequestId);
        if (request.AuthorizationResume is { } authorization)
        {
            activity?.SetTag("db.ino.authorization_provider", authorization.Provider);
            activity?.SetTag("db.ino.authorization_tool", authorization.ToolId);
        }

        var typedRead = await TryExecuteTypedReadAsync(request, workflow, cancellationToken).ConfigureAwait(false);
        if (typedRead is not null) return typedRead;

        var chatClient = services.GetService<IChatClient>()
            ?? throw new InvalidOperationException("INO requires a configured Microsoft.Extensions.AI chat client.");
        var agent = new ChatClientAgent(
            chatClient,
            instructions: "You are INO, a concise workspace assistant. Never expose credentials, tokens, raw provider payloads, internal identifiers, or infrastructure details.",
            name: "ino");
        var session = await agent.CreateSessionAsync(workflow.SessionId, cancellationToken).ConfigureAwait(false);
        var messages = request.History
            .TakeLast(12)
            .Select(static history => new ChatMessage(ChatRole.User, history))
            .Append(new ChatMessage(ChatRole.User, request.Prompt));
        if (request.AuthorizationResume is not null)
            messages = messages.Append(new ChatMessage(
                ChatRole.User,
                "The required connection is ready. Continue only with information you can safely verify; do not expose credentials or claim an external change you did not confirm."));
        var response = await agent.RunAsync(
            messages.ToArray(),
            session,
            options: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = response.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("The workflow returned an empty response.");

        return new InoWorkflowResult(text, workflow);
    }

    private async Task<InoWorkflowResult?> TryExecuteTypedReadAsync(
        InoWorkflowRequest request,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ActorScope)) return null;
        if (!IsActorScope(request.ActorScope))
            throw new ArgumentException("A valid actor scope is required for typed integration operations.", nameof(request));
        if (request.OwnerId is not { } ownerId || request.ActorId is not { } actorId)
            throw new ArgumentException("Owner and actor identities are required for typed integration operations.", nameof(request));

        var grains = services.GetRequiredService<IGrainFactory>();
        var model = grains.GetGrain<IConversationModelGrain>(request.ActorScope);
        var intent = await model.ResolveIntentAsync(new SemanticIntentRequest(
            ownerId,
            actorId,
            request.ConversationId,
            request.Prompt,
            []), cancellationToken).ConfigureAwait(false);

        if (intent.Operation == SemanticOperation.MutationPreview ||
            IsMutationTool(request.AuthorizationResume?.ToolId))
        {
            if (request.AuthorizationResume is { } mutationResume &&
                !ToolMatchesProvider(mutationResume.Provider, mutationResume.ToolId))
                return new InoWorkflowResult(
                    "The saved connection handoff does not match this change request. Send it again.",
                    workflow);
            return await ExecuteTypedMutationAsync(
                model,
                request,
                intent,
                workflow,
                cancellationToken).ConfigureAwait(false);
        }

        var toolId = request.AuthorizationResume?.ToolId ?? ToolFor(intent);

        if (toolId is null)
        {
            if (intent.Provider == SemanticProvider.None && intent.Operation == SemanticOperation.Answer)
                return null;
            var clarification = SafeText(
                intent.Clarification,
                "I need a more specific Gmail or Salesforce read request before I can continue.");
            return new InoWorkflowResult(clarification, workflow);
        }

        if (request.AuthorizationResume is { } resume && !ToolMatchesProvider(resume.Provider, toolId))
            return new InoWorkflowResult("The saved connection handoff does not match this read request. Send it again.", workflow);

        return toolId.StartsWith("gmail.", StringComparison.Ordinal)
            ? await ExecuteGmailReadAsync(request, intent, toolId, workflow, cancellationToken).ConfigureAwait(false)
            : toolId.StartsWith("salesforce.", StringComparison.Ordinal)
                ? await ExecuteSalesforceReadAsync(request, intent, toolId, workflow, cancellationToken).ConfigureAwait(false)
                : new InoWorkflowResult("That integration read is not available through this workflow.", workflow);
    }

    private async Task<InoWorkflowResult> ExecuteTypedMutationAsync(
        IConversationModelGrain model,
        InoWorkflowRequest request,
        SemanticIntentProposal intent,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        var provider = IsMutationTool(request.AuthorizationResume?.ToolId)
            ? request.AuthorizationResume!.ToolId == GmailTools.Send
                ? SemanticProvider.Gmail
                : SemanticProvider.Salesforce
            : intent.Provider;
        if (provider is not (SemanticProvider.Gmail or SemanticProvider.Salesforce))
            return new InoWorkflowResult(
                "I need one specific Gmail send or Salesforce field update before I can continue.",
                workflow);

        var authorization = await EnsureMutationAuthorizationAsync(
            request,
            provider,
            workflow,
            cancellationToken).ConfigureAwait(false);
        if (authorization is not null) return authorization;

        var proposal = await model.ResolveMutationAsync(new SemanticMutationRequest(
            request.ActorScope!,
            request.ConversationId,
            provider,
            request.Prompt), cancellationToken).ConfigureAwait(false);
        if (proposal.Kind is SemanticMutationKind.Clarify or SemanticMutationKind.Unsupported)
            return new InoWorkflowResult(
                SafeText(
                    proposal.Clarification,
                    proposal.Kind == SemanticMutationKind.Unsupported
                        ? "That change is outside the supported single-action approval flow."
                        : "I need the exact target and value before I can prepare this change."),
                workflow);

        return provider == SemanticProvider.Gmail
            ? await PrepareGmailSendAsync(request, proposal, workflow, cancellationToken).ConfigureAwait(false)
            : await PrepareSalesforceUpdateAsync(request, proposal, workflow, cancellationToken).ConfigureAwait(false);
    }

    private async Task<InoWorkflowResult?> EnsureMutationAuthorizationAsync(
        InoWorkflowRequest request,
        SemanticProvider provider,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        if (provider == SemanticProvider.Gmail)
        {
            var probe = await DispatchInoAsync<GmailMailboxOverviewResult>(
                request,
                GoogleCapabilityIds.GmailMailboxRead,
                GmailTools.ReadMailboxOverview,
                JsonSerializer.SerializeToElement(new { }, PlanJson),
                CapabilityOperationKind.Query,
                cancellationToken).ConfigureAwait(false);
            if (probe.Status == GmailReadStatus.Success) return null;
            return probe.Status == GmailReadStatus.NeedsAuth
                ? AuthorizationResult(
                    OAuthCallbackPaths.GoogleProvider,
                    GmailTools.Send,
                    probe.ConnectionUrl,
                    probe.SafeReason,
                    workflow)
                : new InoWorkflowResult(
                    SafeText(probe.SafeReason, "Gmail is not available for this send request."),
                    workflow);
        }

        var salesforceProbe = await DispatchInoAsync<SalesforceReadResult>(
            request,
            SalesforceCapabilityIds.RecordRead,
            SalesforceTools.ReadCurrentProfile,
            JsonSerializer.SerializeToElement(new { }, PlanJson),
            CapabilityOperationKind.Query,
            cancellationToken).ConfigureAwait(false);
        if (salesforceProbe.Status == SalesforceReadStatus.Success) return null;
        return salesforceProbe.Status == SalesforceReadStatus.NeedsAuth
            ? AuthorizationResult(
                OAuthCallbackPaths.SalesforceProvider,
                SalesforceTools.UpdateRecord,
                salesforceProbe.ConnectionUrl,
                salesforceProbe.SafeReason,
                workflow)
            : new InoWorkflowResult(
                SafeText(salesforceProbe.SafeReason, "Salesforce is not available for this update request."),
                workflow);
    }

    private async Task<InoWorkflowResult> PrepareGmailSendAsync(
        InoWorkflowRequest request,
        SemanticMutationProposal proposal,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        if (proposal.Kind != SemanticMutationKind.GmailSend || !TryBuildGmailSend(request, proposal, out var send))
            return new InoWorkflowResult(
                "A Gmail send needs exactly one bare recipient, an explicit subject, and a non-empty body.",
                workflow);
        var summary =
            $"send an email to {send.Recipient} with subject “{send.Subject}” and body “{send.Body}”";
        send = await DispatchInoAsync<GmailSendRequest>(
            request,
            GoogleCapabilityIds.GmailSendPropose,
            GmailTools.Send,
            send,
            CapabilityOperationKind.ExternalEffect,
            cancellationToken).ConfigureAwait(false);
        var toolRequest = await services.GetRequiredService<IInoEffectPlanStore>().PrepareAsync(
            request.ActorScope!,
            request.OperationId,
            GmailTools.Send,
            JsonSerializer.SerializeToUtf8Bytes(send, PlanJson),
            summary,
            CurrentTime().Add(EffectPlanLifetime),
            cancellationToken).ConfigureAwait(false);
        return new InoWorkflowResult(summary, workflow, toolRequest);
    }

    private async Task<InoWorkflowResult> PrepareSalesforceUpdateAsync(
        InoWorkflowRequest request,
        SemanticMutationProposal proposal,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        if (proposal.Kind != SemanticMutationKind.SalesforceFieldUpdate || !ValidSalesforceProposal(proposal))
            return new InoWorkflowResult(
                "A Salesforce update needs one explicit entity, record id, field, and new value.",
                workflow);
        var result = await DispatchInoAsync<SalesforceMutationPreviewResult>(
            request,
            SalesforceCapabilityIds.RecordUpdatePropose,
            SalesforceTools.UpdateRecord,
            new SalesforceUpdatePreviewRequest(
                new SalesforceSemanticEntity(proposal.Entity!),
                proposal.RecordId!,
                new SalesforceSemanticField(proposal.Field!),
                proposal.NewValue!),
            CapabilityOperationKind.ExternalEffect,
            cancellationToken).ConfigureAwait(false);
        if (result.Status == SalesforceMutationStatus.NeedsAuth)
        {
            var probe = await DispatchInoAsync<SalesforceReadResult>(
                request,
                SalesforceCapabilityIds.RecordRead,
                SalesforceTools.ReadCurrentProfile,
                JsonSerializer.SerializeToElement(new { }, PlanJson),
                CapabilityOperationKind.Query,
                cancellationToken).ConfigureAwait(false);
            return probe.Status == SalesforceReadStatus.NeedsAuth
                ? AuthorizationResult(
                    OAuthCallbackPaths.SalesforceProvider,
                    SalesforceTools.UpdateRecord,
                    probe.ConnectionUrl,
                    probe.SafeReason,
                    workflow)
                : new InoWorkflowResult("The Salesforce connection changed before this update could be prepared.", workflow);
        }
        if (result.Status != SalesforceMutationStatus.Prepared ||
            result.PreparedUpdate?.Payload is not { Length: > 0 and <= InoEffectPlanTransitions.MaximumPayloadBytes } payload)
            return new InoWorkflowResult(
                SafeText(result.SafeReason, "The Salesforce field update could not be prepared safely."),
                workflow);

        if (!ExactApprovalValue(result.OriginalValue, 80) ||
            !BoundedApprovalValue(result.CanonicalDesiredValue, 80, allowEmpty: true) ||
            !BoundedApprovalValue(result.ResolvedEntityLabel, 80, allowEmpty: false) ||
            !BoundedApprovalValue(result.ResolvedFieldLabel, 80, allowEmpty: false))
            return new InoWorkflowResult(
                "The resolved Salesforce update cannot be presented exactly for approval. No update was prepared.",
                workflow);

        var original = string.IsNullOrEmpty(result.OriginalValue) ? "(empty)" : result.OriginalValue;
        var desired = string.IsNullOrEmpty(result.CanonicalDesiredValue) ? "(empty)" : result.CanonicalDesiredValue;
        var summary =
            $"update {result.ResolvedEntityLabel} record {proposal.RecordId}, field {result.ResolvedFieldLabel}, from “{original}” to “{desired}”";
        var toolRequest = await services.GetRequiredService<IInoEffectPlanStore>().PrepareAsync(
            request.ActorScope!,
            request.OperationId,
            SalesforceTools.UpdateRecord,
            payload,
            summary,
            CurrentTime().Add(EffectPlanLifetime),
            cancellationToken).ConfigureAwait(false);
        return new InoWorkflowResult(summary, workflow, toolRequest);
    }

    private async Task<InoWorkflowResult> ExecuteGmailReadAsync(
        InoWorkflowRequest request,
        SemanticIntentProposal intent,
        string toolId,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        return toolId switch
        {
            GmailTools.ReadMessages => await ReadGmailMessagesAsync(request, intent, workflow, cancellationToken).ConfigureAwait(false),
            GmailTools.ReadMailboxOverview => await ReadGmailOverviewAsync(request, workflow, cancellationToken).ConfigureAwait(false),
            GmailTools.ReadThreads => await ReadGmailThreadsAsync(request, intent, workflow, cancellationToken).ConfigureAwait(false),
            _ => new InoWorkflowResult("That Gmail read is not available through this workflow.", workflow)
        };
    }

    private async Task<InoWorkflowResult> ReadGmailMessagesAsync(
        InoWorkflowRequest request,
        SemanticIntentProposal intent,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        if (intent.RelativeDays is < 1 or > 365)
            return new InoWorkflowResult("The requested Gmail date window is outside the supported range.", workflow);

        var result = await DispatchInoAsync<GmailMessageListResult>(
            request,
            GoogleCapabilityIds.GmailMailboxRead,
            GmailTools.ReadMessages,
            new GmailMessageListRequest(
                GmailSelection(intent),
                Offset: Math.Max(0, (intent.Ordinal ?? 1) - 1),
                Limit: Math.Clamp(intent.Limit, 1, GmailTools.MaximumResultCount)),
            CapabilityOperationKind.Query,
            cancellationToken).ConfigureAwait(false);
        if (result.Status == GmailReadStatus.NeedsAuth)
            return AuthorizationResult(
                OAuthCallbackPaths.GoogleProvider,
                GmailTools.ReadMessages,
                result.ConnectionUrl,
                result.SafeReason,
                workflow);
        if (result.Status != GmailReadStatus.Success)
            return new InoWorkflowResult(SafeText(result.SafeReason, "I couldn’t read Gmail right now."), workflow);
        if (result.Messages.Length == 0)
            return new InoWorkflowResult("No matching Gmail messages were found.", workflow);

        var lines = result.Messages
            .Take(Math.Clamp(intent.Limit, 1, GmailTools.MaximumResultCount))
            .Select((message, index) =>
                $"{index + 1}. Sender: {SafeText(message.From, "Unknown")}\n" +
                $"   Subject: {SafeText(message.Subject, "(no subject)")}\n" +
                $"   Timestamp: {FormatTimestamp(message.InternalDate)}");
        return new InoWorkflowResult(string.Join('\n', lines), workflow);
    }

    private async Task<InoWorkflowResult> ReadGmailOverviewAsync(
        InoWorkflowRequest request,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        var result = await DispatchInoAsync<GmailMailboxOverviewResult>(
            request,
            GoogleCapabilityIds.GmailMailboxRead,
            GmailTools.ReadMailboxOverview,
            JsonSerializer.SerializeToElement(new { }, PlanJson),
            CapabilityOperationKind.Query,
            cancellationToken).ConfigureAwait(false);
        if (result.Status == GmailReadStatus.NeedsAuth)
            return AuthorizationResult(
                OAuthCallbackPaths.GoogleProvider,
                GmailTools.ReadMailboxOverview,
                result.ConnectionUrl,
                result.SafeReason,
                workflow);
        if (result.Status != GmailReadStatus.Success)
            return new InoWorkflowResult(SafeText(result.SafeReason, "I couldn’t read the Gmail mailbox overview right now."), workflow);
        return new InoWorkflowResult(
            $"Inbox messages: {result.InboxMessages.ToString(CultureInfo.InvariantCulture)}\n" +
            $"Unread inbox messages: {result.UnreadInboxMessages.ToString(CultureInfo.InvariantCulture)}\n" +
            $"Inbox threads: {result.InboxThreads.ToString(CultureInfo.InvariantCulture)}\n" +
            $"Unread inbox threads: {result.UnreadInboxThreads.ToString(CultureInfo.InvariantCulture)}",
            workflow);
    }

    private async Task<InoWorkflowResult> ReadGmailThreadsAsync(
        InoWorkflowRequest request,
        SemanticIntentProposal intent,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        if (intent.RelativeDays is < 1 or > 365)
            return new InoWorkflowResult("The requested Gmail date window is outside the supported range.", workflow);
        var limit = Math.Clamp(intent.Limit, 1, GmailTools.MaximumResultCount);
        var result = await DispatchInoAsync<GmailThreadListResult>(
            request,
            GoogleCapabilityIds.GmailMailboxRead,
            GmailTools.ReadThreads,
            new GmailThreadListRequest(
                GmailSelection(intent),
                Offset: Math.Max(0, (intent.Ordinal ?? 1) - 1),
                Limit: limit,
                MaxMessagesPerThread: limit),
            CapabilityOperationKind.Query,
            cancellationToken).ConfigureAwait(false);
        if (result.Status == GmailReadStatus.NeedsAuth)
            return AuthorizationResult(
                OAuthCallbackPaths.GoogleProvider,
                GmailTools.ReadThreads,
                result.ConnectionUrl,
                result.SafeReason,
                workflow);
        if (result.Status != GmailReadStatus.Success)
            return new InoWorkflowResult(SafeText(result.SafeReason, "I couldn’t read Gmail threads right now."), workflow);
        if (result.Threads.Length == 0)
            return new InoWorkflowResult("No matching Gmail threads were found.", workflow);

        var lines = result.Threads.Take(limit).Select((thread, index) =>
            $"{index + 1}. Participants: {SafeText(string.Join(", ", thread.ParticipantAddresses), "Unknown")}\n" +
            $"   Subject: {SafeText(thread.Subject, "(no subject)")}\n" +
            $"   Timestamp: {FormatTimestamp(thread.LatestInternalDate)}\n" +
            $"   Unread: {(thread.HasUnread ? "yes" : "no")}");
        return new InoWorkflowResult(string.Join('\n', lines), workflow);
    }

    private async Task<InoWorkflowResult> ExecuteSalesforceReadAsync(
        InoWorkflowRequest request,
        SemanticIntentProposal intent,
        string toolId,
        WorkflowReference workflow,
        CancellationToken cancellationToken)
    {
        object arguments;
        switch (toolId)
        {
            case SalesforceTools.DiscoverObjects:
                arguments = new SalesforceDiscoveryRequest(Math.Clamp(intent.Limit, 1, 50));
                break;
            case SalesforceTools.SearchRecords:
                if (string.IsNullOrWhiteSpace(intent.SearchText) || string.IsNullOrWhiteSpace(intent.Entity))
                    return new InoWorkflowResult("A Salesforce search needs both a search term and an entity.", workflow);
                arguments = new SalesforceSearchRequest(
                    intent.SearchText,
                    [new SalesforceSemanticEntity(intent.Entity)],
                    Math.Clamp(intent.Limit, 1, 50));
                break;
            case SalesforceTools.ReadRecords:
                if (string.IsNullOrWhiteSpace(intent.Entity))
                    return new InoWorkflowResult("A Salesforce record read needs an entity.", workflow);
                arguments = new SalesforceRecordReadRequest(
                    new SalesforceSemanticEntity(intent.Entity),
                    SalesforceRecordReadKind.List,
                    Filters: SalesforceFilters(intent.Filters),
                    Sorts: SalesforceSorts(intent.Sorts),
                    Limit: Math.Clamp(intent.Limit, 1, 50));
                break;
            case SalesforceTools.AggregateRecords:
                if (string.IsNullOrWhiteSpace(intent.Entity) || intent.Aggregate is null)
                    return new InoWorkflowResult("A Salesforce aggregate needs an entity and aggregate function.", workflow);
                arguments = new SalesforceAggregateRequest(
                    new SalesforceSemanticEntity(intent.Entity),
                    intent.Aggregate.Function,
                    OptionalSalesforceField(intent.Aggregate.Field),
                    OptionalSalesforceField(intent.Aggregate.GroupBy),
                    SalesforceFilters(intent.Filters),
                    Math.Clamp(intent.Limit, 1, 50));
                break;
            case SalesforceTools.ContinueRecords:
                var continuation = request.PriorWorkflow?.CheckpointId;
                if (!IsBoundedOpaqueValue(continuation))
                    return new InoWorkflowResult("That Salesforce continuation is no longer available.", workflow);
                arguments = new SalesforceContinuationRequest(continuation!);
                break;
            default:
                return new InoWorkflowResult("That Salesforce read is not available through this workflow.", workflow);
        }

        var result = await DispatchInoAsync<SalesforceReadResult>(
            request,
            SalesforceCapabilityIds.RecordRead,
            toolId,
            arguments,
            CapabilityOperationKind.Query,
            cancellationToken).ConfigureAwait(false);

        if (result.Status == SalesforceReadStatus.NeedsAuth)
            return AuthorizationResult(
                OAuthCallbackPaths.SalesforceProvider,
                toolId,
                result.ConnectionUrl,
                result.SafeReason,
                workflow);
        if (result.Status != SalesforceReadStatus.Success)
            return new InoWorkflowResult(SafeText(result.SafeReason, "I couldn’t read Salesforce right now."), workflow);

        var text = RenderSalesforceContent(result.Content);
        var nextWorkflow = result.Continuation is { Value: var value } && IsBoundedOpaqueValue(value)
            ? workflow with { CheckpointId = value }
            : workflow;
        return new InoWorkflowResult(text, nextWorkflow);
    }

    private InoWorkflowResult AuthorizationResult(
        string provider,
        string toolId,
        string? connectionUrl,
        string? safeReason,
        WorkflowReference workflow)
    {
        var summary = SafeText(safeReason, "Connect the requested account to continue.");
        if (!OAuthCallbackPaths.TryParseInternalStartPath(connectionUrl, provider, out var flowReference))
            return new InoWorkflowResult("The connection link could not be prepared safely. Send the request again.", workflow);

        var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        return new InoWorkflowResult(
            summary,
            workflow,
            AuthorizationRequest: new InoAuthorizationRequest(
                provider,
                toolId,
                Guid.NewGuid().ToString("N"),
                now.Add(AuthorizationLifetime),
                flowReference,
                summary));
    }

    private GmailMessageSelection GmailSelection(SemanticIntentProposal intent)
    {
        string? sender = null;
        string? recipient = null;
        string? subject = null;
        var readState = GmailMessageReadState.Any;
        foreach (var filter in intent.Filters ?? [])
        {
            if (filter.Operator is not (SemanticFilterOperator.Equals or SemanticFilterOperator.Contains)) continue;
            if (filter.Field.Equals("sender", StringComparison.OrdinalIgnoreCase) ||
                filter.Field.Equals("from", StringComparison.OrdinalIgnoreCase))
                sender = BoundedFilterValue(filter.Value, 320);
            else if (filter.Field.Equals("recipient", StringComparison.OrdinalIgnoreCase) ||
                     filter.Field.Equals("to", StringComparison.OrdinalIgnoreCase))
                recipient = BoundedFilterValue(filter.Value, 320);
            else if (filter.Field.Equals("subject", StringComparison.OrdinalIgnoreCase))
                subject = BoundedFilterValue(filter.Value, 256);
            else if (filter.Field.Equals("read", StringComparison.OrdinalIgnoreCase) &&
                     bool.TryParse(filter.Value, out var isRead))
                readState = isRead ? GmailMessageReadState.Read : GmailMessageReadState.Unread;
        }

        long? receivedAfter = null;
        if (intent.RelativeDays is { } relativeDays)
        {
            var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
            receivedAfter = now.AddDays(-relativeDays).ToUnixTimeMilliseconds();
        }

        return new GmailMessageSelection(
            Mailbox: GmailMailbox(intent.Entity),
            ReadState: readState,
            SenderAddress: sender,
            RecipientAddress: recipient,
            SubjectContains: subject,
            ReceivedAfterInclusive: receivedAfter,
            MaxPages: GmailTools.MaximumPageCount,
            MaxCandidates: GmailTools.MaximumCandidateCount);
    }

    private static string? ToolFor(SemanticIntentProposal intent) => intent.Provider switch
    {
        SemanticProvider.Gmail => intent.Operation switch
        {
            SemanticOperation.List => GmailTools.ReadMessages,
            SemanticOperation.Overview => GmailTools.ReadMailboxOverview,
            SemanticOperation.Threads => GmailTools.ReadThreads,
            _ => null
        },
        SemanticProvider.Salesforce => intent.Operation switch
        {
            SemanticOperation.Discover => SalesforceTools.DiscoverObjects,
            SemanticOperation.Search => SalesforceTools.SearchRecords,
            SemanticOperation.List or SemanticOperation.Details => SalesforceTools.ReadRecords,
            SemanticOperation.Aggregate => SalesforceTools.AggregateRecords,
            SemanticOperation.NextPage => SalesforceTools.ContinueRecords,
            _ => null
        },
        _ => null
    };

    private static IReadOnlyList<SalesforceFilter>? SalesforceFilters(IReadOnlyList<SemanticFilter>? filters) =>
        filters?.Take(12).Select(static filter => new SalesforceFilter(
            new SalesforceSemanticField(filter.Field),
            filter.Operator,
            filter.Value)).ToArray();

    private static IReadOnlyList<SalesforceSort>? SalesforceSorts(IReadOnlyList<SemanticSort>? sorts) =>
        sorts?.Take(5).Select(static sort => new SalesforceSort(
            new SalesforceSemanticField(sort.Field),
            sort.Direction)).ToArray();

    private static SalesforceSemanticField? OptionalSalesforceField(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new SalesforceSemanticField(value);

    private static GmailMailboxScope GmailMailbox(string? entity) => entity?.Trim().ToLowerInvariant() switch
    {
        "inbox" => GmailMailboxScope.Inbox,
        "sent" => GmailMailboxScope.Sent,
        "draft" or "drafts" => GmailMailboxScope.Drafts,
        "all" or "mail" => GmailMailboxScope.All,
        _ => GmailMailboxScope.Incoming
    };

    private static string RenderSalesforceContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "Salesforce returned no matching records.";
        if (content.Length > MaximumSalesforceContentLength)
            return "The Salesforce result was too large to display safely.";
        try
        {
            using var document = JsonDocument.Parse(content);
            var lines = new List<string>();
            AppendJson(lines, document.RootElement, null);
            var rendered = string.Join('\n', lines).Trim();
            if (rendered.Length == 0) return "Salesforce returned no displayable fields.";
            return rendered.Length <= MaximumRenderedResultLength
                ? rendered
                : rendered[..MaximumRenderedResultLength].TrimEnd() + "\nResult truncated.";
        }
        catch (JsonException)
        {
            return "The Salesforce result could not be displayed safely.";
        }
    }

    private static void AppendJson(List<string> lines, JsonElement element, string? label)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (label is not null) lines.Add(SafeText(label, "Result") + ":");
                foreach (var property in element.EnumerateObject())
                {
                    if (UnsafeProviderField(property.Name)) continue;
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        lines.Add(SafeText(property.Name, "Results") + ":");
                        foreach (var item in property.Value.EnumerateArray()) AppendArrayItem(lines, item);
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        AppendJson(lines, property.Value, property.Name);
                    }
                    else if (TryScalar(property.Value, out var value))
                    {
                        lines.Add(SafeText(property.Name, "Value") + ": " + value);
                    }
                }
                break;
            case JsonValueKind.Array:
                if (label is not null) lines.Add(SafeText(label, "Results") + ":");
                foreach (var item in element.EnumerateArray()) AppendArrayItem(lines, item);
                break;
            default:
                if (TryScalar(element, out var scalar)) lines.Add(scalar);
                break;
        }
    }

    private static void AppendArrayItem(List<string> lines, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            if (TryScalar(item, out var scalar)) lines.Add("- " + scalar);
            return;
        }

        var fields = new List<string>();
        foreach (var property in item.EnumerateObject())
        {
            if (UnsafeProviderField(property.Name) || !TryScalar(property.Value, out var value)) continue;
            fields.Add(SafeText(property.Name, "Value") + ": " + value);
        }
        if (fields.Count > 0) lines.Add("- " + string.Join("; ", fields));
    }

    private static bool TryScalar(JsonElement value, out string text)
    {
        text = value.ValueKind switch
        {
            JsonValueKind.String => SafeText(value.GetString(), string.Empty),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => string.Empty
        };
        if (Uri.TryCreate(text, UriKind.Absolute, out _)) text = string.Empty;
        return text.Length > 0;
    }

    private static bool UnsafeProviderField(string name)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized == "id" || normalized.EndsWith("id", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("url", StringComparison.Ordinal) ||
               normalized.Contains("attributes", StringComparison.Ordinal) ||
               normalized.Contains("raw", StringComparison.Ordinal);
    }

    private static string FormatTimestamp(long milliseconds)
    {
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { return "Unknown"; }
    }

    private static string SafeText(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Any(char.IsControl)) return fallback;
        return normalized.Length <= 512 ? normalized : normalized[..512];
    }

    private static string? BoundedFilterValue(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maximumLength && !normalized.Any(char.IsControl) ? normalized : null;
    }

    private static bool IsActorScope(string value) =>
        value.Length == 64 && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsBoundedOpaqueValue(string? value) =>
        value is { Length: > 0 and <= 1024 } && !value.Any(char.IsControl);

    private static bool IsMutationTool(string? toolId) =>
        string.Equals(toolId, GmailTools.Send, StringComparison.Ordinal) ||
        string.Equals(toolId, SalesforceTools.UpdateRecord, StringComparison.Ordinal);

    private static bool TryBuildGmailSend(
        InoWorkflowRequest request,
        SemanticMutationProposal proposal,
        out GmailSendRequest send)
    {
        send = default!;
        var recipient = proposal.Recipient?.Trim();
        if (recipient is not { Length: > 0 and <= 254 } || recipient.Any(char.IsControl) ||
            !MailAddress.TryCreate(recipient, out var parsed) ||
            !string.Equals(parsed.Address, recipient, StringComparison.OrdinalIgnoreCase) ||
            proposal.Subject is not { Length: > 0 and <= 80 } || proposal.Subject.Any(char.IsControl) ||
            proposal.Body is not { Length: > 0 and <= 100 } || proposal.Body.Any(char.IsControl))
            return false;
        var tagSource = Encoding.UTF8.GetBytes(request.ActorScope + "\0" + request.OperationId);
        var uniqueTag = "ino-" + Convert.ToHexStringLower(SHA256.HashData(tagSource))[..48];
        send = new GmailSendRequest(recipient, proposal.Subject, proposal.Body, uniqueTag);
        return true;
    }

    private static bool ValidSalesforceProposal(SemanticMutationProposal proposal) =>
        BoundedApprovalValue(proposal.Entity, 80, allowEmpty: false) &&
        BoundedApprovalValue(proposal.RecordId, 64, allowEmpty: false) &&
        BoundedApprovalValue(proposal.Field, 80, allowEmpty: false) &&
        BoundedApprovalValue(proposal.NewValue, 80, allowEmpty: true);

    private static bool ExactApprovalValue(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength && !value.Any(char.IsControl);

    private static bool BoundedApprovalValue(string? value, int maximumLength, bool allowEmpty) =>
        value is not null && (allowEmpty || !string.IsNullOrWhiteSpace(value)) &&
        value.Length <= maximumLength && !value.Any(char.IsControl);

    private DateTimeOffset CurrentTime() =>
        (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();

    private async Task<T> DispatchInoAsync<T>(
        InoWorkflowRequest workflowRequest,
        string capabilityId,
        string toolId,
        object arguments,
        CapabilityOperationKind expectedKind,
        CancellationToken cancellationToken)
    {
        if (workflowRequest.OwnerId is not { } ownerId || workflowRequest.ActorId is not { } actorId)
            throw new ArgumentException("Owner and actor identities are required for capability dispatch.", nameof(workflowRequest));
        var payload = new RetainedInoCapabilityPayload(
            toolId,
            JsonSerializer.SerializeToElement(arguments, arguments.GetType(), PlanJson));
        var request = RetainedInoCapabilityAuthority.CreateRequest(
            ownerId,
            actorId,
            workflowRequest.RequestId,
            $"{workflowRequest.OperationId}-{toolId}",
            capabilityId,
            JsonSerializer.SerializeToElement(payload, PlanJson),
            CurrentTime(),
            workflowRequest.RequestId,
            workflowRequest.OperationId);
        var result = await services.GetRequiredService<ICapabilityDispatcher>()
            .ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Kind != expectedKind)
            throw new InvalidOperationException("Capability handler returned an invalid operation kind.");
        return result.Payload.Deserialize<T>(PlanJson)
               ?? throw new InvalidOperationException("Capability handler returned an empty result.");
    }

    private static bool ToolMatchesProvider(string provider, string toolId) =>
        string.Equals(provider, OAuthCallbackPaths.GoogleProvider, StringComparison.Ordinal)
            ? toolId.StartsWith("gmail.", StringComparison.Ordinal)
            : string.Equals(provider, OAuthCallbackPaths.SalesforceProvider, StringComparison.Ordinal) &&
              toolId.StartsWith("salesforce.", StringComparison.Ordinal);

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
