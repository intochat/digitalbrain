using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.AI.Interactions;
using DigitalBrain.Core.Behaviors;
using DigitalBrain.Testing;
using DigitalBrain.Twitter;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorToolFacts
{
    private const string PostedSchema = """{"type":"object","properties":{"eventId":{"type":"string"},"author":{"type":"string"},"text":{"type":"string"}},"required":["eventId","author","text"],"additionalProperties":false}""";
    private static readonly NeuronId ChartId = new("chart", "assistant-posts");

    [Fact]
    public async Task Workspace_agent_composes_saved_behavior_and_twitter_receipts_reach_chart()
    {
        using var model = new ScriptedChatClient();
        await using var brain = await StartBrain(model);
        var definition = Definition(brain);
        var sourceContract = Assert.Single(brain.SiloServices.GetServices<BehaviorSourceContract>(), source => source.GrainType == "twitter");
        var postedContract = Assert.Single(sourceContract.Outputs);
        Assert.True(BehaviorSchema.IsAssignable(postedContract.Schema, PostedSchema), postedContract.Schema);
        Assert.Empty(BehaviorSchema.Validate(postedContract.Schema, JsonSerializer.Serialize(new Posted("42", "elonmusk", "rocket"), TwitterJson.Default.Posted)));
        model.CallTool("behavior_catalog", """{"neuron":"chart:assistant-posts"}""");
        model.CallTool("behavior_save", JsonSerializer.Serialize(new { name = "assistant-posts", definition = JsonSerializer.Serialize(definition, BehaviorJson.Default.BehaviorDefinition), commandId = Guid.NewGuid().ToString() }));
        model.CallTool("behavior_start", JsonSerializer.Serialize(new { name = "assistant-posts", commandId = Guid.NewGuid().ToString() }));
        model.CallTool("behavior_read", """{"name":"assistant-posts"}""");
        model.CallTool("behavior_list", "{}");
        model.Say("The saved behavior is running.");
        var agent = ConversationalAgent.Create(brain.SiloServices, "workspace-test");
        await agent.RunAsync([new ChatMessage(ChatRole.User, "Save and start a behavior that charts Elon Musk posts containing rocket.")], session: null, options: null, cancellationToken: TestContext.Current.CancellationToken);

        var tools = model.Options[0]!.Tools!.OfType<AIFunction>().Select(tool => tool.Name).ToArray();
        foreach (var name in new[] { "behavior_catalog", "behavior_save", "behavior_start", "behavior_stop", "behavior_read", "behavior_list", "salesforce_user_info" })
        {
            Assert.Contains(name, tools);
        }
        var results = model.Calls[^1].SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToArray();
        Assert.Equal(5, results.Length);
        Assert.All(results, result => Assert.Null(result.Exception));
        var resultText = string.Join("\n", results.Select(result => result.Result?.ToString()));
        Assert.Contains("Running", resultText, StringComparison.Ordinal);
        Assert.Contains("assistant-posts", resultText, StringComparison.Ordinal);
        Assert.Contains("rocket", resultText, StringComparison.Ordinal);
        using var readResult = JsonDocument.Parse(results[3].Result!.ToString()!);
        Assert.Equal("Running", readResult.RootElement.GetProperty("behavior").GetProperty("status").GetString());
        Assert.Equal(4, readResult.RootElement.GetProperty("behavior").GetProperty("definition").GetProperty("nodes").GetArrayLength());
        Assert.Contains("assistant-posts", results[4].Result!.ToString()!, StringComparison.Ordinal);
        var behavior = brain.Grains.GetGrain<IBehavior>(new NeuronId("behavior", "assistant-posts").ToGrainId());
        var saved = await behavior.Read();
        Assert.Equal(BehaviorStatus.Running, saved.Status);
        Assert.Equal(JsonSerializer.Serialize(definition, BehaviorJson.Default.BehaviorDefinition), JsonSerializer.Serialize(saved.Definition, BehaviorJson.Default.BehaviorDefinition));

        var source = brain.Grains.GetGrain<ITwitter>(new NeuronId("twitter", "elonmusk").ToGrainId());
        await source.Accept(new Post(CommandId.New(), "irrelevant", "elonmusk", "A quiet afternoon"));
        await source.Accept(new Post(CommandId.New(), "rocket-1", "@ElonMusk", "A rocket launch"));
        var chart = brain.Grains.GetGrain<IChart>(ChartId.ToGrainId());
        await ReactionWait.UntilAsync(async () => (await chart.Read()).Points.Count == 1, TestContext.Current.CancellationToken);
        var point = Assert.Single((await chart.Read()).Points);
        Assert.Equal("A rocket launch", point.Label);
        Assert.Equal(1, point.Value);
        await source.Accept(new Post(CommandId.New(), "rocket-1", "elonmusk", "A rocket launch"));
        await ReactionWait.UntilAsync(async () => await source.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        Assert.Single((await chart.Read()).Points);
    }

    [Fact]
    public async Task Twitter_source_contract_mismatch_is_rejected_before_start()
    {
        using var model = new ScriptedChatClient();
        await using var brain = await StartBrain(model);
        var definition = Definition(brain);
        var nodes = definition.Nodes.ToArray();
        nodes[0] = nodes[0] with { Output = new("InventedPost", PostedSchema) };
        var invalid = definition with { Nodes = [nodes[0]], Connections = [] };
        var behavior = brain.Grains.GetGrain<IBehavior>(new NeuronId("behavior", "mismatched-twitter").ToGrainId());
        Assert.False((await behavior.Validate(invalid)).Valid);
        await Assert.ThrowsAsync<CommandRejectedException>(() => behavior.Save(new(invalid, CommandId.New())));
        Assert.Null((await behavior.Read()).Definition);
    }

    [Fact]
    public async Task Invalid_definition_tool_fails_without_claiming_or_persisting_success()
    {
        using var model = new ScriptedChatClient();
        await using var brain = await StartBrain(model);
        // Use the actual production registration and AIFunction invocation boundary.
        var tool = Assert.Single(brain.SiloServices.GetRequiredService<NativeTools>().Resolve(["behavior_save"]));
        var invalid = Definition(brain) with { Connections = [new("missing", "map")] };
        await Assert.ThrowsAsync<CommandRejectedException>(async () =>
            await tool.InvokeAsync(new AIFunctionArguments
            {
                ["name"] = "invalid-assistant",
                ["definition"] = JsonSerializer.Serialize(invalid, BehaviorJson.Default.BehaviorDefinition),
                ["commandId"] = Guid.NewGuid().ToString()
            }, TestContext.Current.CancellationToken));
        var behavior = brain.Grains.GetGrain<IBehavior>(new NeuronId("behavior", "invalid-assistant").ToGrainId());
        var saved = await behavior.Read();
        Assert.Equal(BehaviorStatus.Draft, saved.Status);
        Assert.Null(saved.Definition);
        Assert.Empty(saved.Bindings);
    }

    private static Task<BrainSimulation> StartBrain(ScriptedChatClient model) => BrainSimulation.StartAsync(new()
    {
        Modules = new([typeof(AIModule), typeof(TwitterModule), typeof(UIModule)]),
        ConfigureSilo = silo =>
        {
            silo.Services.AddSingleton<IChatClient>(new ChatClientBuilder(model).UseFunctionInvocation().Build());
            silo.Services.AddSingleton<IUntrustedContentScreen, ScriptedContentScreen>();
            silo.Services.AddNativeTool("salesforce_user_info", _ => AIFunctionFactory.Create(() => "existing specialist", "salesforce_user_info"));
        }
    });

    private static BehaviorDefinition Definition(BrainSimulation brain)
    {
        var invoker = brain.SiloServices.GetRequiredService<INeuronInvoker>();
        var descriptor = invoker.Describe(ChartId).Single(method => method.InterfaceAlias == "ui.chart" && method.MethodAlias == "append");
        var schema = JsonNode.Parse(descriptor.ArgsSchema!.Value.GetRawText())!.AsObject();
        var identity = invoker.ArgumentContractOf("ui.chart", "append")!.CommandIdPropertyName;
        schema["properties"]!.AsObject().Remove(identity!);
        var required = schema["required"]!.AsArray();
        for (var index = required.Count - 1; index >= 0; index--)
        {
            if (required[index]!.GetValue<string>() == identity) { required.RemoveAt(index); }
        }
        var action = new PayloadContract("Append", schema.ToJsonString());
        return new("assistant-posts", [
            new("source", "source", "{}", Output: new("Posted", PostedSchema), SharedNeuron: new NeuronId("twitter", "elonmusk")),
            new("filter", "filter", """{"path":"text","operator":"contains","value":"rocket"}""", new("Posted", PostedSchema), new("Filtered", PostedSchema)),
            new("map", "map", """{"template":{"point":{"label":"$input.text","value":1,"eventId":"$input.eventId"},"title":"Rocket posts"}}""", new("Filtered", PostedSchema), action),
            new("action", "action", """{"target":"chart:assistant-posts","interface":"ui.chart","method":"append"}""", action)],
            [new("source", "filter"), new("filter", "map"), new("map", "action")]);
    }
}
