using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
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
    [Fact(DisplayName = "LLM calls use the chat client keyed by the concrete model neuron")]
    public async Task TypedModelSemantics()
    {
        using var chatClient = new TracingChatClient();
        var cluster = await StartClusterAsync(chatClient);

        try
        {
            var owner = new OwnerId("ai-agent-semantics");
            var probeId = NeuronId.For<IAIAgentProbe>(owner, "probe");
            var llamaId = NeuronId.For<ILlama32>(owner, "llama");
            var probe = cluster.Client.GetGrain<IAIAgentProbe>(probeId.ToGrainId());

            var direct = await probe.CallLlmAsync(llamaId, "direct llama");
            var directCall = Assert.Single(chatClient.Calls);

            Assert.Equal("response:direct llama", direct);
            Assert.Equal(["direct llama"], directCall.Messages.Select(message => message.Text));
            Assert.Null(directCall.Options);
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
            silo.Configuration[DurablePayloadProtector.ConfigurationKey] =
                Convert.ToBase64String(new byte[32]);
            silo.AddDigitalBrain("ai-agent-contracts");
            ((ICompiledModule)new AIModule()).Activate(silo);
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
internal partial interface IAIAgentProbe : INeuron
{
    [Alias("CallLlm")]
    Task<string> CallLlmAsync(NeuronId target, string message);
}

internal sealed class AIAgentProbe : Neuron, IAIAgentProbe
{
    public async Task<string> CallLlmAsync(NeuronId target, string message)
    {
        var response = await GrainFactory
            .GetGrain<ILLM>(target.ToGrainId())
            .Respond([new ChatMessage(ChatRole.User, message)]);

        return response.Text;
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
}
