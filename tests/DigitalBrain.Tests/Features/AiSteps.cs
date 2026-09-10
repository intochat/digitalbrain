using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.AI;
using DigitalBrain.AI.XAI;
using DigitalBrain.Google;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class AiSteps(BrainWorld world, BrainSteps brain)
{
    [Given("a running brain with AI and fake Google")]
    public async Task GivenAiAndGoogle()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(GoogleModule)]),
            ConfigureSilo = ScriptedAi.Configure(world),
            Configuration = new Dictionary<string, string?> { ["DigitalBrain:Fakes:Enabled"] = "true" },
        });

    [Given("a running brain with AI")]
    public async Task GivenAi()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule)]),
            ConfigureSilo = ScriptedAi.Configure(world),
        });

    [Given("a running brain with durable storage and AI")]
    public async Task GivenDurableAi()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule)]),
            PersistenceDirectory = Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N")),
            ConfigureSilo = ScriptedAi.Configure(world),
        });

    [Given(@"the scripted model will say ""(.*)""")]
    public void GivenSay(string text) => world.Scripted.Say(text);

    [Given(@"the scripted model will call tool ""(\w+)"" with (\{.*\}) then say ""(.*)""")]
    public void GivenCallToolThenSay(string tool, string arguments, string text)
    {
        world.Scripted.CallTool(tool, arguments);
        world.Scripted.Say(text);
    }

    [Given("the scripted model will time out")]
    public void GivenTimeOut() => world.Scripted.TimeOut();

    [Then(@"the scripted model was asked with model ""(.*)""")]
    public void ThenModelId(string model)
        => Assert.Contains(world.Scripted.Options, o => o?.ModelId == model);

    [Then(@"the scripted model's last request contained (\d+) user messages")]
    public void ThenLastRequestUserMessages(int count)
        => Assert.Equal(count, world.Scripted.Calls[^1].Count(m => m.Role == ChatRole.User));

    [Given("the scripted model will pause before its next answer")]
    public void GivenPause() => world.Scripted.Pause();

    [When("the scripted model is unpaused")]
    public void WhenUnpaused() => world.Scripted.Unpause();

    [When("the scripted model is paused")]
    public Task WhenPaused() => world.Scripted.Paused.WaitAsync(TimeSpan.FromSeconds(10));

    // A system prompt reaches a chat client as ChatOptions.Instructions, or as a system
    // message for a client that materialises it; either counts as the model having seen it.
    [Then(@"the scripted model received a system message ""(.*)""")]
    public void ThenSystemMessage(string text)
        => Assert.True(
            world.Scripted.Options.Any(o => o?.Instructions == text)
                || world.Scripted.Calls.Any(c => c.Any(m => m.Role == ChatRole.System && m.Text.Contains(text, StringComparison.Ordinal))),
            $"no request carried the system prompt '{text}'");

    [Then(@"the scripted model received a tool result containing ""(.*)""")]
    public void ThenToolResult(string fragment)
    {
        var results = world.Scripted.Calls
            .SelectMany(call => call)
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.Result?.ToString() ?? "")
            .ToList();
        Assert.True(
            results.Any(result => result.Contains(fragment, StringComparison.Ordinal)),
            $"no tool result contained '{fragment}'; results were: {string.Join(" | ", results)}");
    }

    [Then(@"the scripted model received no tool result containing ""(.*)""")]
    public void ThenNoToolResult(string fragment)
        => Assert.All(
            world.Scripted.Calls.SelectMany(call => call)
                .SelectMany(message => message.Contents.OfType<FunctionResultContent>()),
            result => Assert.DoesNotContain(fragment, result.Result?.ToString() ?? "", StringComparison.Ordinal));

    [Then(@"the scripted model saw (\d+) conversations with (\d+) user message each")]
    public void ThenConversations(int conversations, int userMessages)
    {
        var calls = world.Scripted.Calls;
        Assert.Equal(conversations, calls.Count);
        Assert.All(calls, call => Assert.Equal(userMessages, call.Count(m => m.Role == ChatRole.User)));
    }

    // Instruct is accepted before it is reacted to, so the anatomy it wires appears a turn
    // later. Waiting for the last synapse it creates is waiting for the whole reaction.
    [When(@"""(.*)"" waits up to (\d+) seconds for a synapse to ""(.*)"" for ""(\w+)""")]
    public async Task WhenSynapseAppears(string from, int seconds, string to, string type)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if ((await brain.Neuron(from).ReadSynapses()).Any(s => s.Target == BrainSteps.Id(to) && s.SignalType == type))
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"{from} has no synapse to {to} for {type} after {seconds}s");
    }

    [Then(@"the latest ""(.*)"" incoming ""(\w+)"" text contains ""(.*)""")]
    public async Task ThenLatestTextContains(string neuron, string type, string fragment)
    {
        var body = (await brain.Journal(neuron, JournalKind.Incoming)).Delta.Last(d => d.Signal.Type == type).Signal.Body;
        var text = (JsonNode.Parse(body) as JsonObject)?["text"]?.GetValue<string>() ?? string.Empty;
        Assert.Contains(fragment, text, StringComparison.Ordinal);
    }
}
