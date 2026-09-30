using DigitalBrain.Assistant;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AssistantReactivationFacts
{
    [Fact]
    public async Task PersistedPrimitiveBindingsReactivateAssistantAndSubmitExactlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new StreamScenarioRunner();
        await using var brain = await UnitTest.Create().WithModule<AssistantModule>().RequireModules([typeof(DigitalBrain.Apps.AppsModule), typeof(DigitalBrain.AI.AIModule), typeof(DigitalBrain.Compute.ComputeModule), typeof(DigitalBrain.Flutter.FlutterModule)])
            .ConfigureSilo(silo => silo.Services.AddSingleton<IAgentTurnRunner>(runner)
                .AddSingleton<IAiCredentials>(new FixedAiCredentials().Ready("openai", "fixture"))
                .Configure<AIOptions>(options =>
                {
                    options.Default.Provider = "OpenAI";
                    options.Default.Model = "fixture";
                    options.Default.Capabilities = LlmCapabilities.Tools;
                })).StartAsync(ct);
        var key = AssistantSurface.Key("reactivation");
        var app = brain.Get<IAssistant>(key);
        var ui = new AssistantUiProbe(brain.Grains, key);
        await brain.Grains.GetGrain<IAssistant>(key).Activate();
        await ui.Input("saved draft");
        var composed = await ui.Tree();
        var thread = (await ui.Read()).Id;
        var input = ui.Draft;
        var send = ui.Button(AssistantSurface.SendPart);
        var inputBinding = brain.Get<IUiBinding>(UiParts.NameOf(key, AssistantSurface.DraftPart));
        var sendBinding = brain.Get<IUiBinding>(UiParts.NameOf(key, AssistantSurface.SendPart));
        await brain.DeactivateAsync(app, ct);
        await brain.DeactivateAsync(input, ct);
        await brain.DeactivateAsync(send, ct);
        await brain.DeactivateAsync(inputBinding, ct);
        await brain.DeactivateAsync(sendBinding, ct);
        foreach (var node in composed)
        {
            if (node.Kind == "surface") { await brain.DeactivateAsync(brain.Get<ISurface>(node.Name), ct); }
            if (node.Kind == "layout") { await brain.DeactivateAsync(brain.Get<ILayout>(node.Name), ct); }
        }
        // Read the persisted surface and every layout without rerunning composition.
        Assert.Equal(composed.Select(node => (node.Kind, node.Name)),
            (await ui.Tree()).Select(node => (node.Kind, node.Name)));
        await ui.AssertExplicitControls();
        // The saved window talks only to its primitives; no app-start endpoint or Watch is needed.
        Assert.Equal("saved draft", (await input.Read()).Value);
        await input.Input("after-reactivation");
        await send.Click();
        await ui.Until(state => state.Messages.Any(message => message.Text == "Hello world") && state.TurnId is null, ct);
        Assert.Equal(1, runner.Count("after-reactivation"));
        Assert.Single((await app.ReadConversation(thread, ct)).Turns);
        Assert.Equal(2, (await ui.Read()).Messages.Count);
        await brain.DeactivateAsync(app, ct);
        await brain.DeactivateAsync(input, ct);
        await brain.DeactivateAsync(inputBinding, ct);
        Assert.Equal(2, (await ui.Read()).Messages.Count);
        await brain.DeactivateAsync(app, ct);
        await ui.Input("new draft after reactivation");
        Assert.Equal(thread, (await ui.Read()).Id);
        Assert.Equal("new draft after reactivation", (await input.Read()).Value);
    }
}
