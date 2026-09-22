using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Conversations;
using DigitalBrain.Contracts;
using IntoChat.Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Xunit;

namespace IntoChat.Tests;

public sealed class ConversationModelFacts
{
    [Fact]
    public async Task WorkspaceAssistantUsesItsConfiguredModel()
    {
        await using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["IntoChat:Assistant:Model"] = "IGpt56Luna" }).Build())
            .AddSingleton<IDigitalBrain, Brain>().AddSingleton<IAgentTurnRunner, Runner>().BuildServiceProvider();
        var coordinator = ActivatorUtilities.CreateInstance<ConversationCoordinator>(services);
        await coordinator.Run("scope", "thread", "run", "create a behavior", _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        Assert.Equal("IGpt56Luna", ((Runner)services.GetRequiredService<IAgentTurnRunner>()).Request!.Model?.Model);
    }

    private sealed class Runner : IAgentTurnRunner
    {
        public AgentTurnRequest? Request { get; private set; }
        public async IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Request = request;
            await Task.Yield();
            yield return new AgentTurnEvent.Finished();
        }
    }

    private sealed class Brain : IDigitalBrain
    {
        public T Get<T>(string id) where T : class, IGrainWithStringKey => DispatchProxy.Create<T, Conversation>();
        public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class Conversation : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "Read" => Task.FromResult(new ConversationState(0, null, [])),
            "Begin" => Task.FromResult(new ConversationState(1, (string)args![0]!, [])),
            "Complete" => Task.FromResult(new ConversationState(2, null, [(ConversationTurn)args![0]!])),
            _ => throw new NotSupportedException(method.Name),
        };
    }
}
