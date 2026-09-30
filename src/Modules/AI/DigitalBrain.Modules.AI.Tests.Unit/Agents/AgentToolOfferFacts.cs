using System.Runtime.CompilerServices;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentToolOfferFacts
{
    [Fact]
    public async Task AnOfferedToolJoinsTheRestOfTheTurn()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = new ServiceCollection().AddSingleton<IChatClient>(FindThenCallClient())
            .AddSingleton<IAgentToolFactory>(new OfferingTools()).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();

        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("agent", "run", "scope", [], "ask", null, ToolNames: ["find"]), ct))
        { events.Add(item); }

        Assert.Equal(["find", "extra"], events.OfType<AgentTurnEvent.ToolCompleted>().Select(completed => completed.Name));
        Assert.Contains("extra ran", events.OfType<AgentTurnEvent.Text>().Last().Content, StringComparison.Ordinal);
    }

    private sealed class OfferingTools : IAgentToolFactory
    {
        public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context) =>
        [
            AIFunctionFactory.Create(() => new AgentToolOffer(["extra"], "found extra"), new AIFunctionFactoryOptions
            {
                Name = "find",
                MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
            }),
            AIFunctionFactory.Create(() => "extra ran", "extra"),
        ];
    }

    private static StubChatClient FindThenCallClient() => new((messages, _, _) =>
    {
        var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
        var reply = results.Length switch
        {
            0 => new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "find", new Dictionary<string, object?>())]),
            1 => new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-2", "extra", new Dictionary<string, object?>())]),
            _ => new ChatMessage(ChatRole.Assistant, results[^1].Result!.ToString()),
        };
        return Task.FromResult(new ChatResponse(reply));
    });
}
