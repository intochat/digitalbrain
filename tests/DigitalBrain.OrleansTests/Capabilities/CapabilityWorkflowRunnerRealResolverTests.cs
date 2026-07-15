using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class CapabilityWorkflowRunnerRealResolverTests
{
    private static readonly BrainOwnerId Owner = new("owner-real-resolver");
    private readonly RecordingChatClient _chat = new();

    [Fact]
    public async Task ExecuteAsync_answers_a_paraphrased_general_question_without_drafting()
    {
        var runner = Runner();

        var result = await runner.ExecuteAsync(Request("What is the difference between TCP and UDP?"));

        Assert.Equal(CapabilityResolutionKind.Missing, result.Capability?.Kind);
        Assert.Equal(1, _chat.CallCount);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task ExecuteAsync_answers_a_conversational_question_outside_the_exact_greeting_list()
    {
        var runner = Runner();

        var result = await runner.ExecuteAsync(Request("how are you"));

        Assert.Equal(CapabilityResolutionKind.Missing, result.Capability?.Kind);
        Assert.Equal(1, _chat.CallCount);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task ExecuteAsync_still_drafts_a_feature_for_actionable_research_work()
    {
        var hub = new RecordingFeatureHubGrain();
        var runner = Runner(hub);

        var result = await runner.ExecuteAsync(
            Request("Research Acme Corporation and create a text file with the findings.", Owner));

        Assert.Equal(CapabilityResolutionKind.Missing, result.Capability?.Kind);
        Assert.Equal(0, _chat.CallCount);
        Assert.Equal(1, hub.CreateDraftCallCount);
        Assert.Equal("Open Studio", result.Proposal?.Label);
    }

    private AgentFrameworkWorkflowRunner Runner(RecordingFeatureHubGrain? hub = null)
    {
        var catalog = new BuiltInCapabilityCatalog(
            [new GoogleCapabilityDescriptorSource(), new SalesforceCapabilityDescriptorSource()]);
        var services = new ServiceCollection()
            .AddSingleton<IChatClient>(_chat)
            .AddSingleton<ICapabilityCatalog>(catalog)
            .AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new NoOpEmbeddingGenerator())
            .AddSingleton<ICapabilityResolver, HybridCapabilityResolver>();
        if (hub is not null)
            services.AddSingleton<IFeatureGrainResolver>(new RecordingFeatureGrainResolver(hub));
        return new AgentFrameworkWorkflowRunner(services.BuildServiceProvider());
    }

    private static InoWorkflowRequest Request(string prompt, BrainOwnerId? ownerId = null) => new(
        "operation-1",
        "conversation-1",
        prompt,
        [],
        "request-1",
        OwnerId: ownerId);

    private sealed class RecordingFeatureHubGrain : IFeatureHubGrain
    {
        private FeatureHubState _state = FeatureHubState.Empty;
        public int CreateDraftCallCount { get; private set; }

        public Task<FeatureDraft> CreateDraftAsync(CreateFeatureDraft request)
        {
            CreateDraftCallCount++;
            var transition = FeatureHubTransitions.CreateDraft(_state, "owner-scope-1", request);
            _state = transition.State;
            return Task.FromResult(transition.Draft);
        }

        public Task RegisterAsync(FeatureInstallationRegistration registration) => throw new NotSupportedException();
        public Task<FeatureDraft?> ReadDraftAsync(FeatureDraftId draftId) => throw new NotSupportedException();
        public Task<FeatureDraft?> ReadInstalledDraftAsync(FeatureInstallationId installationId, ReleaseDigest release) => throw new NotSupportedException();
        public Task<FeatureDraft> ReviseBehaviorAsync(ReviseFeatureBehavior command) => throw new NotSupportedException();
        public Task<FeatureDraft> ReviseSourceAsync(ReviseFeatureSource command) => throw new NotSupportedException();
        public Task<FeatureDraft> AcceptSuggestedChangeAsync(AcceptSuggestedChange command) => throw new NotSupportedException();
        public Task<FeatureDraft> RejectSuggestedChangeAsync(RejectSuggestedChange command) => throw new NotSupportedException();
        public Task<FeatureDraft> RecordVerificationAsync(RecordFeatureVerification command) => throw new NotSupportedException();
        public Task<FeatureDraftInstallationReservation> AcquireDraftInstallationReservationAsync(InstallFeatureVersion command, ActorId actorId) => throw new NotSupportedException();
        public Task<FeatureDraftInstallationReservation?> ReadDraftInstallationReservationAsync(FeatureDraftId draftId) => throw new NotSupportedException();
        public Task<FeatureDraftInstallationResetObligation?> ReadDraftInstallationResetAsync(FeatureDraftId draftId) => throw new NotSupportedException();
        public Task<FeatureDraftInstallationResetPreparation> ResetDraftInstallationReservationAsync(ResetFeatureDraftInstallationReservation command, ActorId actorId) => throw new NotSupportedException();
        public Task<FeatureDraft> CompleteDraftInstallationReservationResetAsync(FeatureDraftId draftId, string idempotencyId, ActorId actorId) => throw new NotSupportedException();
        public Task<FeatureDraft> MarkDraftInstalledAsync(MarkFeatureDraftInstalled command) => throw new NotSupportedException();
        public Task<FeatureFanOutResult> PublishAsync(FeatureInput input) => throw new NotSupportedException();
        public Task<FeatureHubSnapshot> ReadAsync() => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> ProposeAsync(FeatureReleaseProposal proposal, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> DecideAsync(FeatureApprovalDecision decision, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> GrantAsync(FeatureGrantRequest request, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> InstallAsync(FeatureInstallationRegistration registration, long expectedRevision) => throw new NotSupportedException();
        public Task<FeaturePublicationTicket> PrepareActivePublicationAsync(FeatureInstallationId installationId) => throw new NotSupportedException();
        public Task<FeaturePublicationReceipt> ConfirmActivePublicationAsync(FeaturePublicationReceipt receipt) => throw new NotSupportedException();
        public Task RevokeAsync(FeatureGrantRevocation revocation, long expectedRevision) => throw new NotSupportedException();
        public Task PauseInstallationAsync(FeatureInstallationId installationId, string reason, long expectedRevision) => throw new NotSupportedException();
        public Task ResumeInstallationAsync(FeatureInstallationId installationId, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> RollbackInstallationAsync(FeatureInstallationId installationId, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureGrantSnapshot?> ReadGrantAsync(FeatureGrantLookup lookup) => throw new NotSupportedException();
    }

    private sealed class RecordingFeatureGrainResolver(IFeatureHubGrain hub) : IFeatureGrainResolver
    {
        public IFeatureHubGrain Hub(BrainOwnerId ownerId) => hub;
        public IFeatureInstallationGrain Installation(BrainOwnerId ownerId, FeatureInstallationId installationId) => throw new NotSupportedException();
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "safe response"))
            {
                ConversationId = "provider-conversation"
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
