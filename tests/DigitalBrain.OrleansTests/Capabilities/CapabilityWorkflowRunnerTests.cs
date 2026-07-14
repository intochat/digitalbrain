using System.Text.Json;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class CapabilityWorkflowRunnerTests
{
    private static readonly BrainOwnerId Owner = new("owner-1");
    private readonly RecordingChatClient _chat = new();
    private readonly RecordingCapabilityParameterModel _parameterModel = new();

    [Fact]
    public async Task ExecuteAsync_resolves_before_calling_the_parameter_model()
    {
        var resolver = new RecordingCapabilityResolver(Match(GoogleCapabilityIds.GmailMessageRead, "Read Gmail messages"));
        var runner = Runner(resolver);

        var result = await runner.ExecuteAsync(Request("list my latest messages"));

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(GoogleCapabilityIds.GmailMessageRead, result.Capability?.CapabilityId);
        Assert.Equal(GoogleCapabilityIds.GmailMessageRead, _parameterModel.LastRequest?.CapabilityId);
        Assert.Equal(0, _chat.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_acknowledges_a_multi_line_prompt_with_a_single_line_bounded_prompt()
    {
        var resolver = new RecordingCapabilityResolver(Match(GoogleCapabilityIds.GmailMessageRead, "Read Gmail messages"));
        var runner = Runner(resolver);

        var result = await runner.ExecuteAsync(Request("list my latest messages\nfrom Anna\r\nthis week"));

        Assert.Equal(GoogleCapabilityIds.GmailMessageRead, result.Capability?.CapabilityId);
        Assert.Equal("list my latest messages from Anna  this week", resolver.LastRequest?.Prompt);
        Assert.Equal("list my latest messages from Anna  this week", _parameterModel.LastRequest?.Prompt);
    }

    [Fact]
    public async Task ExecuteAsync_resolves_an_over_length_prompt_with_a_bounded_prompt()
    {
        var resolver = new RecordingCapabilityResolver(Match(GoogleCapabilityIds.GmailMessageRead, "Read Gmail messages"));
        var runner = Runner(resolver);
        var prompt = new string('a', 5000);

        var result = await runner.ExecuteAsync(Request(prompt));

        Assert.Equal(GoogleCapabilityIds.GmailMessageRead, result.Capability?.CapabilityId);
        Assert.Equal(prompt[..4096], resolver.LastRequest?.Prompt);
        Assert.Equal(prompt[..4096], _parameterModel.LastRequest?.Prompt);
    }

    [Fact]
    public async Task ExecuteAsync_creates_a_control_character_free_draft_goal_for_a_multi_line_prompt()
    {
        var hub = new RecordingFeatureHubGrain();
        var runner = Runner(new RecordingCapabilityResolver(Missing()), new RecordingFeatureGrainResolver(hub));

        var result = await runner.ExecuteAsync(Request("Research Acme\r\nand create a text file", Owner));

        Assert.Equal(1, hub.CreateDraftCallCount);
        Assert.Equal("Research Acme  and create a text file", hub.LastCreateDraftRequest?.Goal);
        Assert.Equal("Open Studio", result.Proposal?.Label);
    }

    [Fact]
    public async Task ExecuteAsync_returns_clarification_for_ambiguous_capabilities()
    {
        var runner = Runner(new RecordingCapabilityResolver(Ambiguous(GoogleCapabilityIds.GmailMessageRead, GoogleCapabilityIds.GmailMailboxRead)));

        var result = await runner.ExecuteAsync(Request("show my mail"));

        Assert.Equal(CapabilityResolutionKind.Ambiguous, result.Capability?.Kind);
        Assert.Contains("choose", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _parameterModel.CallCount);
        Assert.Equal(0, _chat.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_call_the_general_agent_for_missing_actionable_work()
    {
        var runner = Runner(new RecordingCapabilityResolver(Missing()));

        var result = await runner.ExecuteAsync(Request("Research Acme and create a text file"));

        Assert.Equal(CapabilityResolutionKind.Missing, result.Capability?.Kind);
        Assert.Equal(0, _chat.CallCount);
        Assert.Equal(0, _parameterModel.CallCount);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task ExecuteAsync_creates_a_feature_draft_for_missing_actionable_work_when_an_owner_is_known()
    {
        var hub = new RecordingFeatureHubGrain();
        var runner = Runner(new RecordingCapabilityResolver(Missing()), new RecordingFeatureGrainResolver(hub));

        var result = await runner.ExecuteAsync(Request("Research Acme and create a text file", Owner));

        Assert.Equal(1, hub.CreateDraftCallCount);
        Assert.Equal(0, _chat.CallCount);
        Assert.Equal("Open Studio", result.Proposal?.Label);
        Assert.StartsWith("/features/proposals/proposal-", result.Proposal?.Route);
        Assert.Equal(result.Proposal?.ProposalId, result.Proposal?.Route["/features/proposals/".Length..]);
    }

    [Fact]
    public async Task ExecuteAsync_returns_the_same_draft_for_a_repeated_missing_capability_request()
    {
        var hub = new RecordingFeatureHubGrain();
        var runner = Runner(new RecordingCapabilityResolver(Missing()), new RecordingFeatureGrainResolver(hub));
        var request = Request("Research Acme and create a text file", Owner);

        var first = await runner.ExecuteAsync(request);
        var second = await runner.ExecuteAsync(request);

        Assert.Equal(2, hub.CreateDraftCallCount);
        Assert.Equal(first.Proposal?.ProposalId, second.Proposal?.ProposalId);
    }

    [Fact]
    public async Task ExecuteAsync_treats_a_greeting_as_ordinary_conversation_despite_a_missing_receipt()
    {
        var hub = new RecordingFeatureHubGrain();
        var runner = Runner(new RecordingCapabilityResolver(Missing()), new RecordingFeatureGrainResolver(hub));

        var result = await runner.ExecuteAsync(Request("hello", Owner));

        Assert.Equal(1, _chat.CallCount);
        Assert.Equal(CapabilityResolutionKind.Missing, result.Capability?.Kind);
        Assert.Null(result.Proposal);
        Assert.Equal(0, hub.CreateDraftCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_routes_an_assistant_answer_match_through_the_general_agent()
    {
        var runner = Runner(new RecordingCapabilityResolver(Match(BuiltInCapabilityCatalog.AssistantAnswerCapabilityId, "Assistant answer")));

        var result = await runner.ExecuteAsync(Request("what time zone is Kyiv in"));

        Assert.Equal(1, _chat.CallCount);
        Assert.Equal(CapabilityResolutionKind.Match, result.Capability?.Kind);
        Assert.Equal(BuiltInCapabilityCatalog.AssistantAnswerCapabilityId, result.Capability?.CapabilityId);
        Assert.Equal(0, _parameterModel.CallCount);
    }

    private AgentFrameworkWorkflowRunner Runner(ICapabilityResolver resolver, IFeatureGrainResolver? featureGrainResolver = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IChatClient>(_chat)
            .AddSingleton(resolver)
            .AddSingleton<ICapabilityParameterModel>(_parameterModel)
            .AddSingleton<ICapabilityCatalog>(new StubCapabilityCatalog());
        if (featureGrainResolver is not null)
            services.AddSingleton(featureGrainResolver);
        return new AgentFrameworkWorkflowRunner(services.BuildServiceProvider());
    }

    private static InoWorkflowRequest Request(string prompt, BrainOwnerId? ownerId = null) => new(
        "operation-1",
        "conversation-1",
        prompt,
        [],
        "request-1",
        OwnerId: ownerId);

    private static CapabilityResolution Match(string capabilityId, string name) => new(
        new CapabilityResolutionReceipt(CapabilityResolutionKind.Match, capabilityId, name, [capabilityId], 0.9),
        new CapabilityDescriptor(capabilityId, 1, name, name, [], [], [], CapabilityOrigin.Integration, CapabilityOperationKind.Query, true),
        []);

    private static CapabilityResolution Ambiguous(params string[] candidateIds) => new(
        new CapabilityResolutionReceipt(CapabilityResolutionKind.Ambiguous, null, null, candidateIds, 0.5),
        null,
        []);

    private static CapabilityResolution Missing() => new(
        new CapabilityResolutionReceipt(CapabilityResolutionKind.Missing, null, null, [], 0),
        null,
        []);

    private sealed class RecordingCapabilityResolver(CapabilityResolution result) : ICapabilityResolver
    {
        public int CallCount { get; private set; }
        public CapabilitySearchRequest? LastRequest { get; private set; }

        public Task<CapabilityResolution> ResolveAsync(CapabilitySearchRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingFeatureHubGrain : IFeatureHubGrain
    {
        private FeatureHubState _state = FeatureHubState.Empty;
        public int CreateDraftCallCount { get; private set; }
        public CreateFeatureDraft? LastCreateDraftRequest { get; private set; }

        public Task<FeatureDraftProposal> CreateDraftAsync(CreateFeatureDraft request)
        {
            CreateDraftCallCount++;
            LastCreateDraftRequest = request;
            var transition = FeatureHubTransitions.CreateDraft(_state, "owner-scope-1", request);
            _state = transition.State;
            return Task.FromResult(transition.Draft);
        }

        public Task RegisterAsync(FeatureInstallationRegistration registration) => throw new NotSupportedException();
        public Task<FeatureFanOutResult> PublishAsync(FeatureInput input) => throw new NotSupportedException();
        public Task<FeatureHubSnapshot> ReadAsync() => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> ProposeAsync(FeatureReleaseProposal proposal, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureApprovalSnapshot> DecideAsync(FeatureApprovalDecision decision, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> GrantAsync(FeatureGrantRequest request, long expectedRevision) => throw new NotSupportedException();
        public Task<FeatureAuthoritySnapshot> InstallAsync(FeatureInstallationRegistration registration, long expectedRevision) => throw new NotSupportedException();
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

    private sealed class RecordingCapabilityParameterModel : ICapabilityParameterModel
    {
        public int CallCount { get; private set; }
        public CapabilityParameterRequest? LastRequest { get; private set; }

        public Task<RetainedInoCapabilityPayload> ExtractAsync(CapabilityParameterRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new RetainedInoCapabilityPayload(request.CapabilityId, JsonElement.Parse("{}")));
        }
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

    private sealed class StubCapabilityCatalog : ICapabilityCatalog
    {
        public IReadOnlyList<CapabilityDescriptor> Snapshot() => [];
    }
}
