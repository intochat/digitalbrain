using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Scripted;
using Xunit;

namespace DigitalBrain.Modules.AI.Tests.Unit;

public sealed class PersistedCollectionFacts
{
    [Fact]
    public void AgentDefinitionCollectionsAndNullableOptionsRoundTripThroughJson()
    {
        var definition = new AgentDefinition
        {
            Tools = ["search", "read"],
            Capabilities = ["research"],
            RoutingExamples = ["find evidence"],
            ContextProviders = ["history"],
            Options = new(ProviderOptions: new Dictionary<string, string>() { ["mode"] = "precise" }),
        };
        var restored = JsonSerializer.Deserialize<AgentDefinition>(JsonSerializer.Serialize(definition))!;
        Assert.Equal(definition.Tools, restored.Tools);
        Assert.Equal(definition.Capabilities, restored.Capabilities);
        Assert.Equal(definition.RoutingExamples, restored.RoutingExamples);
        Assert.Equal(definition.ContextProviders, restored.ContextProviders);
        Assert.Equal("precise", restored.Options!.ProviderOptions!["mode"]);
        Assert.Null(JsonSerializer.Deserialize<InferenceOptions>("{}")!.ProviderOptions);
        Assert.Null(JsonSerializer.Deserialize<InferenceOptions>("{\"ProviderOptions\":null}")!.ProviderOptions);
        Assert.Empty(JsonSerializer.Deserialize<InferenceOptions>("{\"ProviderOptions\":{}}")!.ProviderOptions!);
    }

    [Fact]
    public async Task ScriptedRepliesAndPromptsSurviveReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(ct);
        var model = brain.Get<IScriptedLLM>("persisted-script");
        await model.Script(["first reply", "second reply"]);
        await model.Generate(new([new("user", [new AiText("first prompt")])]), ct);
        await brain.DeactivateAsync(model, ct);
        var result = await model.Generate(new([new("user", [new AiText("second prompt")])]), ct);
        Assert.Equal("second reply", Assert.IsType<AiText>(Assert.Single(Assert.Single(result.Messages).Content)).Text);
        Assert.Equal(["first prompt", "second prompt"], await model.Prompts());
    }
}
