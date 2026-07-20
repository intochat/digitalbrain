using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class AIAgentContracts
{
    [Fact(DisplayName = "LLM and ordinary Agent calls use typed models while Agent sessions remain stateless")]
    public async Task TypedModelAndStatelessAgentSemantics()
    {
        using var chatClient = new TracingChatClient();
        var cluster = await StartClusterAsync(chatClient);

        try
        {
            var owner = new OwnerId("ai-agent-semantics");
            var probeId = NeuronId.For<IAIAgentProbe>(owner, "probe");
            var llamaId = NeuronId.For<ILlama32>(owner, "llama");
            var agentId = NeuronId.For<ITracingAgent>(owner, "agent");
            var probe = cluster.Client.GetGrain<IAIAgentProbe>(probeId.ToGrainId());

            var direct = await probe.CallLlmAsync(llamaId, "direct llama");
            var directCall = Assert.Single(chatClient.Calls);

            Assert.Equal("response:direct llama", direct);
            Assert.Equal(["direct llama"], directCall.Messages.Select(message => message.Text));
            Assert.Null(directCall.Options);

            chatClient.Clear();

            var first = await probe.CallAgentAsync(agentId, "first turn");
            var second = await probe.CallAgentAsync(agentId, "second turn");
            var calls = chatClient.Calls;

            Assert.Equal("response:first turn", first);
            Assert.Equal("response:second turn", second);
            Assert.Equal(2, calls.Count);
            Assert.All(calls, call =>
            {
                Assert.Contains(call.Messages, message => message.Text == TracingAgent.AgentInstructions);
                Assert.Null(call.Options);
            });
            Assert.Contains(calls[0].Messages, message => message.Text == "first turn");
            Assert.DoesNotContain(calls[0].Messages, message => message.Text == "second turn");
            Assert.Contains(calls[1].Messages, message => message.Text == "second turn");
            Assert.DoesNotContain(calls[1].Messages, message => message.Text == "first turn");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    private static async Task<InProcessTestCluster> StartClusterAsync(TracingChatClient chatClient)
    {
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain("ai-agent-contracts");
            AIModule.Configure(silo);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            silo.Services.AddKeyedSingleton<IChatClient>(typeof(Llama32), chatClient);
        });
        builder.ConfigureClient(client =>
        {
            client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
                type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();

        return cluster;
    }
}

[Alias("db.test.ai-agent-probe")]
[ClientEntryPoint]
internal interface IAIAgentProbe : INeuron
{
    [Alias("CallLlm")]
    Task<string> CallLlmAsync(NeuronId target, string message);

    [Alias("CallAgent")]
    Task<string> CallAgentAsync(NeuronId target, string message);
}

internal sealed class AIAgentProbe : Neuron, IAIAgentProbe
{
    public async Task<string> CallLlmAsync(NeuronId target, string message)
    {
        var response = await GrainFactory
            .GetGrain<ILLM>(target.ToGrainId())
            .RespondAsync([new ChatMessage(ChatRole.User, message)]);

        return response.Text;
    }

    public async Task<string> CallAgentAsync(NeuronId target, string message)
    {
        var response = await GrainFactory
            .GetGrain<IAgent>(target.ToGrainId())
            .RespondAsync([new ChatMessage(ChatRole.User, message)]);

        return response.Text;
    }
}

[Alias("db.test.tracing-agent")]
internal interface ITracingAgent : IAgent;

internal sealed class TracingAgent(IGrainFactory grains, IGrainContext context) :
    Agent(ModelForOwner(grains, context)), ITracingAgent
{
    internal const string AgentInstructions = "Use the agent-owned instructions.";

    protected override string Instructions => AgentInstructions;

    private static ILlama32 ModelForOwner(IGrainFactory grains, IGrainContext context)
    {
        var owner = NeuronId.FromGrainKey(
            context.GrainId.Type.ToString()!,
            context.GrainId.Key.ToString()).Owner;
        var model = NeuronId.For<ILlama32>(owner, "llama");

        return grains.GetGrain<ILlama32>(model.ToGrainId());
    }
}

internal sealed record TracedChatCall(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);

internal sealed class TracingChatClient : IChatClient
{
    private readonly ConcurrentQueue<TracedChatCall> _calls = new();

    internal IReadOnlyList<TracedChatCall> Calls => [.. _calls];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = messages.ToArray();
        _calls.Enqueue(new(request, options));
        var user = request.Last(message => message.Role == ChatRole.User);

        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, $"response:{user.Text}")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    internal void Clear() => _calls.Clear();
}
