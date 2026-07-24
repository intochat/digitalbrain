using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Client;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class ClientGetLlmContracts
{
    [Fact(DisplayName = "IDigitalBrain.Get<ILlama32> returns the real neuron and Respond yields ChatResponse")]
    public async Task GetLlama32Responds()
    {
        using var chatClient = new ClientGetTracingChatClient();
        var cluster = await StartClusterAsync(chatClient);

        try
        {
            var brain = DigitalBrainClient.Connect(cluster.Client, "client-get-llm");
            var llama = brain.Get<ILlama32>("default");

            Assert.IsAssignableFrom<ILlama32>(llama);

            var response = await llama.Respond(
                [new ChatMessage(ChatRole.User, "hello from client")]);

            Assert.Equal("response:hello from client", response.Text);
            Assert.Equal(["hello from client"], chatClient.Calls.Single().Messages.Select(m => m.Text));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    private static async Task<InProcessTestCluster> StartClusterAsync(IChatClient chatClient)
    {
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.Configuration[DurablePayloadProtector.ConfigurationKey] =
                Convert.ToBase64String(new byte[32]);
            silo.AddDigitalBrain("client-get-llm");
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

internal sealed class ClientGetTracingChatClient : IChatClient
{
    private readonly ConcurrentQueue<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> _calls = new();

    internal IReadOnlyList<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls => [.. _calls];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = messages.ToArray();
        _calls.Enqueue((request, options));
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
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
