using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using System.Text.Json;

namespace DigitalBrain.Tests.Runtime;

public sealed class TypedReadWorkflowRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_lists_only_requested_gmail_metadata_with_an_explicit_relative_day_window()
    {
        var intent = new SemanticIntentProposal(
            SemanticProvider.Gmail,
            SemanticOperation.List,
            Entity: "inbox",
            Limit: 3,
            RelativeDays: 7);
        var gmail = new RecordingGmailGrain
        {
            MessagesResult = new GmailMessageListResult(
                GmailReadStatus.Success,
                [
                    Message("message-secret-1", "Ada <ada@example.com>", "First", Now.AddHours(-1)),
                    Message("message-secret-2", "Grace <grace@example.com>", "Second", Now.AddHours(-2)),
                    Message("message-secret-3", "Linus <linus@example.com>", "Third", Now.AddHours(-3))
                ],
                new GmailResultCoverage(1, 3, 3, 3, 0, true, false))
        };
        var chat = new RecordingChatClient();
        var runner = Runner(intent, gmail, new RecordingSalesforceGrain(), chat);

        var result = await runner.ExecuteAsync(Request("List the latest three inbox messages from the last seven days."));

        Assert.Equal(GmailTools.ReadMessages, gmail.LastToolId);
        Assert.NotNull(gmail.LastMessagesRequest);
        Assert.Equal(GmailMailboxScope.Inbox, gmail.LastMessagesRequest.Selection.Mailbox);
        Assert.Equal(Now.AddDays(-7).ToUnixTimeMilliseconds(), gmail.LastMessagesRequest.Selection.ReceivedAfterInclusive);
        Assert.Equal(3, gmail.LastMessagesRequest.Limit);
        Assert.Equal(
            "1. Sender: Ada <ada@example.com>\n   Subject: First\n   Timestamp: 2026-07-13T09:30:00Z\n" +
            "2. Sender: Grace <grace@example.com>\n   Subject: Second\n   Timestamp: 2026-07-13T08:30:00Z\n" +
            "3. Sender: Linus <linus@example.com>\n   Subject: Third\n   Timestamp: 2026-07-13T07:30:00Z",
            result.Text);
        Assert.DoesNotContain("message-secret", result.Text, StringComparison.Ordinal);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_turns_an_internal_gmail_start_path_into_a_bounded_authorization_request()
    {
        var flowReference = new string('a', OAuthCallbackPaths.MinimumFlowReferenceLength);
        var gmail = new RecordingGmailGrain
        {
            MessagesResult = new GmailMessageListResult(
                GmailReadStatus.NeedsAuth,
                [],
                new GmailResultCoverage(0, 0, 0, 0, 0, true, false),
                "Connect Gmail to continue.",
                OAuthCallbackPaths.CreateInternalStartPath(OAuthCallbackPaths.GoogleProvider, flowReference))
        };
        var runner = Runner(
            new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.List, Entity: "inbox", Limit: 3),
            gmail,
            new RecordingSalesforceGrain(),
            new RecordingChatClient());

        var result = await runner.ExecuteAsync(Request("List my latest three inbox messages."));

        var authorization = Assert.IsType<InoAuthorizationRequest>(result.AuthorizationRequest);
        Assert.Equal(OAuthCallbackPaths.GoogleProvider, authorization.Provider);
        Assert.Equal(GmailTools.ReadMessages, authorization.ToolId);
        Assert.Equal(flowReference, authorization.AuthorizationFlowReference);
        Assert.True(Guid.TryParseExact(authorization.AuthorizationAttemptId, "N", out _));
        Assert.Equal(Now.AddMinutes(5), authorization.ExpiresAt);
        Assert.Equal("Connect Gmail to continue.", authorization.SafeSummary);
        Assert.DoesNotContain("/oauth/", authorization.AuthorizationFlowReference, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_resumes_the_original_typed_read_selected_by_the_authorization_handoff()
    {
        var gmail = new RecordingGmailGrain
        {
            MessagesResult = new GmailMessageListResult(
                GmailReadStatus.Success,
                [Message("message-secret", "Ada", "Ready", Now)],
                new GmailResultCoverage(1, 1, 1, 1, 0, true, false))
        };
        var runner = Runner(
            new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.Overview),
            gmail,
            new RecordingSalesforceGrain(),
            new RecordingChatClient());
        var resume = new InoAuthorizationResume(
            OAuthCallbackPaths.GoogleProvider,
            GmailTools.ReadMessages,
            Guid.NewGuid().ToString("N"),
            Now.AddMinutes(5));

        var result = await runner.ExecuteAsync(Request("List my inbox.", authorizationResume: resume));

        Assert.Equal(GmailTools.ReadMessages, gmail.LastToolId);
        Assert.Equal(1, gmail.MessageCalls);
        Assert.Equal(0, gmail.OverviewCalls);
        Assert.Contains("Subject: Ready", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_prepares_a_gmail_send_with_an_exact_body_preview_without_executing_it()
    {
        var model = new RecordingConversationModelGrain(
            new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.MutationPreview),
            new SemanticMutationProposal(
                SemanticMutationKind.GmailSend,
                Recipient: "safe-recipient@example.com",
                Subject: "Acceptance check",
                Body: "Sensitive acceptance body"));
        var gmail = new RecordingGmailGrain
        {
            AuthorizationResult = new ExternalAuthorizationResolution(ExternalAuthorizationResolutionState.Ready)
        };
        var plans = new RecordingEffectPlanStore();
        var runner = Runner(model, gmail, new RecordingSalesforceGrain(), new RecordingChatClient(), plans);

        var result = await runner.ExecuteAsync(Request(
            "Send safe-recipient@example.com an email with subject Acceptance check and body Sensitive acceptance body."));

        var toolRequest = Assert.IsType<InoToolRequest>(result.ToolRequest);
        Assert.Equal(GmailTools.Send, toolRequest.ToolId);
        Assert.Equal(InoToolAccess.Mutation, toolRequest.Access);
        Assert.Equal(GmailTools.Send, plans.ToolId);
        var prepared = JsonSerializer.Deserialize<GmailSendRequest>(
            plans.PayloadUtf8!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(prepared);
        Assert.Equal("safe-recipient@example.com", prepared.Recipient);
        Assert.Equal("Acceptance check", prepared.Subject);
        Assert.Equal("Sensitive acceptance body", prepared.Body);
        Assert.Matches("^ino-[a-f0-9]{48}$", prepared.UniqueTag);
        Assert.Contains(prepared.Recipient, toolRequest.SafeSummary, StringComparison.Ordinal);
        Assert.Contains(prepared.Subject, toolRequest.SafeSummary, StringComparison.Ordinal);
        Assert.Contains(prepared.Body, result.Text, StringComparison.Ordinal);
        Assert.Contains(prepared.Body, toolRequest.SafeSummary, StringComparison.Ordinal);
        Assert.Equal(0, gmail.SendCalls);
    }

    [Fact]
    public async Task ExecuteAsync_requests_gmail_authorization_before_extracting_or_preparing_a_send()
    {
        var flowReference = new string('a', OAuthCallbackPaths.MinimumFlowReferenceLength);
        var model = new RecordingConversationModelGrain(
            new SemanticIntentProposal(SemanticProvider.Gmail, SemanticOperation.MutationPreview),
            new SemanticMutationProposal(SemanticMutationKind.GmailSend));
        var gmail = new RecordingGmailGrain
        {
            AuthorizationResult = new ExternalAuthorizationResolution(ExternalAuthorizationResolutionState.Failed),
            OverviewResult = new GmailMailboxOverviewResult(
                GmailReadStatus.NeedsAuth,
                0,
                0,
                0,
                0,
                "Connect Gmail to continue.",
                OAuthCallbackPaths.CreateInternalStartPath(OAuthCallbackPaths.GoogleProvider, flowReference))
        };
        var plans = new RecordingEffectPlanStore();
        var runner = Runner(model, gmail, new RecordingSalesforceGrain(), new RecordingChatClient(), plans);

        var result = await runner.ExecuteAsync(Request("Send a test email."));

        Assert.Equal(GmailTools.Send, result.AuthorizationRequest?.ToolId);
        Assert.Equal(flowReference, result.AuthorizationRequest?.AuthorizationFlowReference);
        Assert.Equal(0, model.MutationCalls);
        Assert.Null(plans.PayloadUtf8);
    }

    [Fact]
    public async Task ExecuteAsync_previews_and_prepares_one_salesforce_field_without_applying_it()
    {
        var preparedPayload = "provider-prepared-update"u8.ToArray();
        var model = new RecordingConversationModelGrain(
            new SemanticIntentProposal(SemanticProvider.Salesforce, SemanticOperation.MutationPreview),
            new SemanticMutationProposal(
                SemanticMutationKind.SalesforceFieldUpdate,
                Entity: "Account",
                RecordId: "001000000000001AAA",
                Field: "Description",
                NewValue: "DigitalBrain acceptance"));
        var salesforce = new RecordingSalesforceGrain
        {
            AuthorizationResult = new ExternalAuthorizationResolution(ExternalAuthorizationResolutionState.Ready),
            MutationPreviewResult = new SalesforceMutationPreviewResult(
                SalesforceMutationStatus.Prepared,
                "Before",
                new SalesforcePreparedUpdate(preparedPayload),
                CanonicalDesiredValue: "DigitalBrain acceptance",
                ResolvedEntityLabel: "Account",
                ResolvedFieldLabel: "Description")
        };
        var plans = new RecordingEffectPlanStore();
        var runner = Runner(model, new RecordingGmailGrain(), salesforce, new RecordingChatClient(), plans);

        var result = await runner.ExecuteAsync(Request(
            "Update Account 001000000000001AAA Description to DigitalBrain acceptance."));

        Assert.Equal(SalesforceTools.UpdateRecord, result.ToolRequest?.ToolId);
        Assert.Equal(preparedPayload, plans.PayloadUtf8);
        Assert.Contains("Account", result.ToolRequest!.SafeSummary, StringComparison.Ordinal);
        Assert.Contains("Description", result.ToolRequest.SafeSummary, StringComparison.Ordinal);
        Assert.Contains("Before", result.ToolRequest.SafeSummary, StringComparison.Ordinal);
        Assert.Contains("DigitalBrain acceptance", result.ToolRequest.SafeSummary, StringComparison.Ordinal);
        Assert.Equal(1, salesforce.PreviewCalls);
        Assert.Equal(0, salesforce.ApplyCalls);
    }

    [Fact]
    public async Task ExecuteAsync_turns_an_internal_salesforce_start_path_into_a_bounded_authorization_request()
    {
        var flowReference = new string('s', OAuthCallbackPaths.MinimumFlowReferenceLength);
        var salesforce = new RecordingSalesforceGrain
        {
            Result = new SalesforceReadResult(
                SalesforceReadStatus.NeedsAuth,
                SafeReason: "Connect Salesforce to continue.",
                ConnectionUrl: OAuthCallbackPaths.CreateInternalStartPath(
                    OAuthCallbackPaths.SalesforceProvider,
                    flowReference))
        };
        var runner = Runner(
            new SemanticIntentProposal(
                SemanticProvider.Salesforce,
                SemanticOperation.Search,
                Entity: "account",
                SearchText: "Acme"),
            new RecordingGmailGrain(),
            salesforce,
            new RecordingChatClient());

        var result = await runner.ExecuteAsync(Request("Find Acme in Salesforce."));

        var authorization = Assert.IsType<InoAuthorizationRequest>(result.AuthorizationRequest);
        Assert.Equal(OAuthCallbackPaths.SalesforceProvider, authorization.Provider);
        Assert.Equal(SalesforceTools.SearchRecords, authorization.ToolId);
        Assert.Equal(flowReference, authorization.AuthorizationFlowReference);
        Assert.True(Guid.TryParseExact(authorization.AuthorizationAttemptId, "N", out _));
        Assert.Equal(Now.AddMinutes(5), authorization.ExpiresAt);
        Assert.Equal("Connect Salesforce to continue.", authorization.SafeSummary);
    }

    [Theory]
    [InlineData(SemanticOperation.Discover, SalesforceTools.DiscoverObjects)]
    [InlineData(SemanticOperation.Search, SalesforceTools.SearchRecords)]
    [InlineData(SemanticOperation.List, SalesforceTools.ReadRecords)]
    [InlineData(SemanticOperation.Aggregate, SalesforceTools.AggregateRecords)]
    [InlineData(SemanticOperation.NextPage, SalesforceTools.ContinueRecords)]
    public async Task ExecuteAsync_maps_salesforce_reads_to_the_existing_typed_grain(
        SemanticOperation operation,
        string expectedToolId)
    {
        var intent = new SemanticIntentProposal(
            SemanticProvider.Salesforce,
            operation,
            Entity: "account",
            Limit: 5,
            Aggregate: operation == SemanticOperation.Aggregate
                ? new SemanticAggregate(SemanticAggregateFunction.Count)
                : null,
            SearchText: operation == SemanticOperation.Search ? "Acme" : null);
        var salesforce = new RecordingSalesforceGrain
        {
            Result = new SalesforceReadResult(
                SalesforceReadStatus.Success,
                "{\"Records\":[{\"Id\":\"provider-secret-id\",\"Name\":\"Acme\"}]}",
                ReturnedCount: 1,
                TotalSize: 1)
        };
        var prior = operation == SemanticOperation.NextPage
            ? new WorkflowReference(
                "agent-framework",
                "agent-framework-operation-1",
                "existing-session",
                "opaque-continuation")
            : null;
        var runner = Runner(intent, new RecordingGmailGrain(), salesforce, new RecordingChatClient());

        var result = await runner.ExecuteAsync(Request("Read Salesforce.", prior: prior));

        Assert.Equal(expectedToolId, salesforce.LastToolId);
        Assert.Contains("Acme", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-secret-id", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Id\"", result.Text, StringComparison.Ordinal);
    }

    private static AgentFrameworkWorkflowRunner Runner(
        SemanticIntentProposal intent,
        RecordingGmailGrain gmail,
        RecordingSalesforceGrain salesforce,
        RecordingChatClient chat) => Runner(
            new RecordingConversationModelGrain(intent), gmail, salesforce, chat, new RecordingEffectPlanStore());

    private static AgentFrameworkWorkflowRunner Runner(
        RecordingConversationModelGrain model,
        RecordingGmailGrain gmail,
        RecordingSalesforceGrain salesforce,
        RecordingChatClient chat,
        IInoEffectPlanStore plans)
    {
        var factory = new RecordingGrainFactory(model, gmail, salesforce);
        var services = new ServiceCollection()
            .AddSingleton<IChatClient>(chat)
            .AddSingleton<IGrainFactory>(factory)
            .AddSingleton(plans)
            .AddSingleton<TimeProvider>(new FrozenTimeProvider(Now))
            .BuildServiceProvider();
        return new AgentFrameworkWorkflowRunner(services);
    }

    private static InoWorkflowRequest Request(
        string prompt,
        InoAuthorizationResume? authorizationResume = null,
        WorkflowReference? prior = null) => new(
        "operation-1",
        "conversation-1",
        prompt,
        [],
        "request-1",
        authorizationResume,
        prior,
        new string('b', 64));

    private static GmailMessageMetadata Message(
        string id,
        string sender,
        string subject,
        DateTimeOffset timestamp) => new(
        id,
        "thread-secret",
        timestamp.ToUnixTimeMilliseconds(),
        sender,
        null,
        "recipient-secret",
        ["recipient-secret"],
        subject,
        ["INBOX"],
        false);

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingConversationModelGrain(
        SemanticIntentProposal intent,
        SemanticMutationProposal? mutation = null) : IConversationModelGrain
    {
        public int MutationCalls { get; private set; }

        public Task<SemanticIntentProposal> ResolveIntentAsync(
            SemanticIntentRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(intent);

        public Task<SemanticMutationProposal> ResolveMutationAsync(
            SemanticMutationRequest request,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            return Task.FromResult(mutation ?? throw new NotSupportedException());
        }

        public Task<ConversationModelCompletionResponse> CompleteAsync(
            ConversationModelCompletionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingGmailGrain : IGmailReadToolGrain, IGmailMetadataToolGrain
    {
        public ExternalAuthorizationResolution AuthorizationResult { get; init; } =
            new(ExternalAuthorizationResolutionState.Ready);
        public GmailMailboxOverviewResult OverviewResult { get; init; } =
            new(GmailReadStatus.Success, 4, 2, 3, 1);
        public GmailMessageListResult MessagesResult { get; init; } = new(
            GmailReadStatus.Success, [], new GmailResultCoverage(0, 0, 0, 0, 0, true, false));
        public GmailMessageListRequest? LastMessagesRequest { get; private set; }
        public string? LastToolId { get; private set; }
        public int MessageCalls { get; private set; }
        public int OverviewCalls { get; private set; }
        public int SendCalls { get; private set; }

        public Task<GmailMessageListResult> ReadMessagesAsync(
            GmailMessageListRequest request,
            CancellationToken cancellationToken = default)
        {
            LastToolId = GmailTools.ReadMessages;
            LastMessagesRequest = request;
            MessageCalls++;
            return Task.FromResult(MessagesResult);
        }

        public Task<GmailMailboxOverviewResult> ReadMailboxOverviewAsync(CancellationToken cancellationToken = default)
        {
            LastToolId = GmailTools.ReadMailboxOverview;
            OverviewCalls++;
            return Task.FromResult(OverviewResult);
        }

        public Task<GmailThreadListResult> ReadThreadsAsync(
            GmailThreadListRequest request,
            CancellationToken cancellationToken = default)
        {
            LastToolId = GmailTools.ReadThreads;
            return Task.FromResult(new GmailThreadListResult(
                GmailReadStatus.Success, [], new GmailResultCoverage(0, 0, 0, 0, 0, true, false)));
        }

        public Task<GmailReadResult> ReadIncomingAtOffsetAsync(
            GmailReadRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExternalAuthorizationResolution> ResolveAuthorizationAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(AuthorizationResult);
        public Task<GmailReadResult> BeginAuthorizationAsync(
            string flowReference,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthResult> CompleteAuthorizationAsync(
            OAuthCallback callback,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingSalesforceGrain : ISalesforceReadToolGrain, ISalesforceMutationToolGrain
    {
        public ExternalAuthorizationResolution AuthorizationResult { get; init; } =
            new(ExternalAuthorizationResolutionState.Ready);
        public SalesforceMutationPreviewResult MutationPreviewResult { get; init; } =
            new(SalesforceMutationStatus.InvalidRequest, SafeReason: "Invalid preview.");
        public SalesforceReadResult Result { get; init; } = new(SalesforceReadStatus.Success, "{}");
        public string? LastToolId { get; private set; }
        public int PreviewCalls { get; private set; }
        public int ApplyCalls { get; private set; }

        public Task<SalesforceReadResult> DiscoverObjectsAsync(SalesforceDiscoveryRequest request, CancellationToken cancellationToken = default) => Record(SalesforceTools.DiscoverObjects);
        public Task<SalesforceReadResult> ReadRecordsAsync(SalesforceRecordReadRequest request, CancellationToken cancellationToken = default) => Record(SalesforceTools.ReadRecords);
        public Task<SalesforceReadResult> SearchRecordsAsync(SalesforceSearchRequest request, CancellationToken cancellationToken = default) => Record(SalesforceTools.SearchRecords);
        public Task<SalesforceReadResult> AggregateRecordsAsync(SalesforceAggregateRequest request, CancellationToken cancellationToken = default) => Record(SalesforceTools.AggregateRecords);
        public Task<SalesforceReadResult> ContinueRecordsAsync(SalesforceContinuationRequest request, CancellationToken cancellationToken = default) => Record(SalesforceTools.ContinueRecords);

        private Task<SalesforceReadResult> Record(string toolId)
        {
            LastToolId = toolId;
            return Task.FromResult(Result);
        }

        public Task<ExternalAuthorizationResolution> ResolveAuthorizationAsync(CancellationToken cancellationToken = default) => Task.FromResult(AuthorizationResult);
        public Task<SalesforceReadResult> BeginAuthorizationAsync(string startToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthResult> CompleteAuthorizationAsync(OAuthCallback callback, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesforceReadResult> ReadLatestAccountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesforceReadResult> ReadCurrentProfileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesforceReadResult> ReadRecentAccountsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesforceReadResult> ReadRecentContactsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesforceReadResult> ReadCrmSchemaAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SalesforceMutationPreviewResult> PreviewUpdateAsync(
            SalesforceUpdatePreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            PreviewCalls++;
            return Task.FromResult(MutationPreviewResult);
        }

        public Task<SalesforceMutationApplyResult> ApplyUpdateAsync(
            SalesforcePreparedUpdate preparedUpdate,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            throw new NotSupportedException();
        }

        public Task<SalesforceMutationVerificationResult> VerifyUpdateAsync(
            SalesforcePreparedUpdate preparedUpdate,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingGrainFactory(
        IConversationModelGrain conversationModel,
        RecordingGmailGrain gmail,
        RecordingSalesforceGrain salesforce) : IGrainFactory
    {
        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey => typeof(TGrainInterface) switch
            {
                var type when type == typeof(IConversationModelGrain) => (TGrainInterface)conversationModel,
                var type when type == typeof(IGmailReadToolGrain) => (TGrainInterface)(object)gmail,
                var type when type == typeof(IGmailMetadataToolGrain) => (TGrainInterface)(object)gmail,
                var type when type == typeof(ISalesforceReadToolGrain) => (TGrainInterface)(object)salesforce,
                var type when type == typeof(ISalesforceMutationToolGrain) => (TGrainInterface)(object)salesforce,
                _ => throw new NotSupportedException(typeof(TGrainInterface).FullName)
            };

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey => GetGrain<TGrainInterface>(primaryKey, grainClassNamePrefix);
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => throw new NotSupportedException();
        public IAddressable GetGrain(GrainId grainId) => throw new NotSupportedException();
        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotSupportedException();
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string? grainClassNamePrefix = null) => throw new NotSupportedException();
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey) => throw new NotSupportedException();
        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public int CallCount { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "general answer")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class RecordingEffectPlanStore : IInoEffectPlanStore
    {
        public string? ToolId { get; private set; }
        public byte[]? PayloadUtf8 { get; private set; }

        public Task<InoToolRequest> PrepareAsync(
            string actorScope,
            string operationId,
            string toolId,
            byte[] payloadUtf8,
            string safeSummary,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            ToolId = toolId;
            PayloadUtf8 = payloadUtf8.ToArray();
            return Task.FromResult(new InoToolRequest(toolId, InoToolAccess.Mutation, "test-plan", safeSummary));
        }
    }
}
