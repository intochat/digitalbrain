using System.Text.Json;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class CapabilityWorkflowRunnerTests
{
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

    private AgentFrameworkWorkflowRunner Runner(ICapabilityResolver resolver)
    {
        var services = new ServiceCollection()
            .AddSingleton<IChatClient>(_chat)
            .AddSingleton(resolver)
            .AddSingleton<ICapabilityParameterModel>(_parameterModel)
            .AddSingleton<ICapabilityCatalog>(new StubCapabilityCatalog())
            .BuildServiceProvider();
        return new AgentFrameworkWorkflowRunner(services);
    }

    private static InoWorkflowRequest Request(string prompt) => new(
        "operation-1",
        "conversation-1",
        prompt,
        [],
        "request-1");

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
