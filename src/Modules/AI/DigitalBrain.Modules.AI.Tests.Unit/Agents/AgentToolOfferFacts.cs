using System.Runtime.CompilerServices;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AgentToolOfferFacts
{
    [Fact]
    public void ToolNamesAreModelSafeAndBounded()
    {
        Assert.Equal("supabase_table_CreateFromQuery", NeuronToolName.Of("supabase.table/CreateFromQuery"));
        var longName = NeuronToolName.Of(new string('a', 80) + "/Method");
        Assert.Equal(64, longName.Length);
        Assert.NotEqual(longName, NeuronToolName.Of(new string('a', 80) + "/Other"));
    }

    [Fact]
    public async Task AnOfferedToolJoinsTheRestOfTheTurn()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = new ServiceCollection().AddSingleton<IChatClient>(new FindThenCallClient())
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

    private sealed class FindThenCallClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var results = messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
            var reply = results.Length switch
            {
                0 => new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "find", new Dictionary<string, object?>())]),
                1 => new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-2", "extra", new Dictionary<string, object?>())]),
                _ => new ChatMessage(ChatRole.Assistant, results[^1].Result!.ToString()),
            };
            return Task.FromResult(new ChatResponse(reply));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.CompletedTask; yield break; }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}