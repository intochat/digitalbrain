using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// Proves the render_chart/generate_image tools from Task 7: each writes its kit entity
// under the chat's principal partition, then posts a KitCardOffer back to the chat so the
// card shows up in the transcript. Uses the shared fixture (no ConfigureSilo override
// needed) but constructs KitToolSource directly rather than through DI, matching how
// AgentToolTests probes IAgentToolSource in isolation.
[Collection(SimulationCollection.Name)]
public sealed class KitToolTests(SimulationFixture fixture)
{
    private static readonly OwnerId Owner = new("kit-tool-tests");

    // PrincipalScoped.InstanceName (src/Kernel/DigitalBrain.Kernel/Auth/Surfaces/PrincipalScoped.cs)
    // is an internal Kernel-side alias not reachable from this module test project; it
    // forwards straight to PrincipalPartition.InstanceName, so asserting against the
    // latter pins the exact same "{principal:N}.{local}" separator rule.
    [Fact]
    public void KitEntityNamesShareTheChatsPrincipalScope()
    {
        var principal = new PrincipalId(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));
        var chat = PrincipalPartition.InstanceName(principal, "main");
        var chart = KitInstanceNames.Sibling(chat, "chart-abc12345");

        Assert.Equal(PrincipalPartition.InstanceName(principal, "chart-abc12345"), chart);
    }

    [Fact]
    public async Task RenderChartToolCreatesTheEntityAndPostsACard()
    {
        var chatInstance = NewChatInstance();
        var tools = new KitToolSource(fixture.Sim.Grains, imageGeneration: null, imageStore: new MemoryKitImageStore());
        var render = tools.ToolsFor(Owner).Single(tool => tool.Name == "render_chart");

        var reply = await render.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["title"] = "Sales",
            ["chartKind"] = "bar",
            ["labels"] = new[] { "Q1", "Q2" },
            ["values"] = new[] { 10.0, 20.0 },
        }, CancellationToken.None);

        Assert.Contains("Sales", reply!.ToString());
        var transcript = await fixture.Sim.Grains.GetGrain<IChat>(chatInstance).Read();
        Assert.Contains(transcript.Turns, turn => turn.Text == "Sales");
    }

    [Fact]
    public async Task RenderChartToolRefusesABlankTitleWithoutTouchingTheChat()
    {
        var chatInstance = NewChatInstance();
        var tools = new KitToolSource(fixture.Sim.Grains, imageGeneration: null, imageStore: new MemoryKitImageStore());
        var render = tools.ToolsFor(Owner).Single(tool => tool.Name == "render_chart");

        var reply = await render.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["title"] = "   ",
            ["chartKind"] = "bar",
            ["labels"] = new[] { "Q1" },
            ["values"] = new[] { 10.0 },
        }, CancellationToken.None);

        Assert.Contains("blank", reply!.ToString(), StringComparison.OrdinalIgnoreCase);
        var transcript = await fixture.Sim.Grains.GetGrain<IChat>(chatInstance).Read();
        Assert.Empty(transcript.Turns);
    }

    [Fact]
    public void GenerateImageToolIsAbsentWithoutAnImageGenerator()
    {
        var tools = new KitToolSource(fixture.Sim.Grains, imageGeneration: null, imageStore: new MemoryKitImageStore());
        Assert.DoesNotContain(tools.ToolsFor(Owner), tool => tool.Name == "generate_image");
    }

    [Fact]
    public async Task GenerateImageToolStoresBytesEntityAndCard()
    {
        var chatInstance = NewChatInstance();
        var store = new MemoryKitImageStore();
        var tools = new KitToolSource(fixture.Sim.Grains, new TestImageGeneration(), store);
        var generate = tools.ToolsFor(Owner).Single(tool => tool.Name == "generate_image");

        var reply = await generate.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["prompt"] = "a red fox",
        }, CancellationToken.None);

        var replyText = reply!.ToString()!;
        Assert.Contains("image", replyText, StringComparison.OrdinalIgnoreCase);

        var transcript = await fixture.Sim.Grains.GetGrain<IChat>(chatInstance).Read();
        Assert.Contains(transcript.Turns, turn => turn.Text == "a red fox");

        var cardName = CardNameFrom(replyText);
        var imageInstance = KitInstanceNames.Sibling(chatInstance, cardName);
        var imageState = await fixture.Sim.Grains.GetGrain<IImage>(imageInstance).Read();
        Assert.NotNull(imageState);
        Assert.Equal("a red fox", imageState!.Prompt);

        var blob = await store.ReadAsync($"{cardName}.png", CancellationToken.None);
        Assert.NotNull(blob);
        Assert.Equal("image/png", blob!.Value.MediaType);
    }

    // A tool-supplied chatName is the raw grain key the model was told in its context:
    // "{owner}/{principal:N}.{local}" (owner/name form Neuron.Id requires, wrapping a
    // principal-scoped name — mirrors MapOwnerCommands.TryPrincipalResource composed
    // through DigitalBrainClient.GetGrainProxy). KitInstanceNames.Sibling only replaces the
    // local segment after the principal partition's '.', so tests need that full shape too.
    private string NewChatInstance()
        => $"{fixture.Sim.Brain.Owner.Value}/"
            + PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), fixture.Sim.UniqueId("chat"));

    private static string CardNameFrom(string replyText)
    {
        const string Marker = "card '";
        var start = replyText.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;
        var end = replyText.IndexOf('\'', start);
        return replyText[start..end];
    }
}
