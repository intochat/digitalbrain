using DigitalBrain.Abstractions;
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
    // Must equal fixture.Sim.Brain.Owner (BrainSimulationOptions defaults to
    // DigitalBrainNames.DefaultOwner and SimulationCollection never overrides it): the
    // chatName instances built by NewChatInstance() are prefixed with the brain's actual
    // owner, and KitToolSource.ToolsFor(Owner) now refuses any chatName that doesn't start
    // with "{owner.Value}/", so the two must agree or every non-guard test would trip the
    // new owner guard.
    private static readonly OwnerId Owner = new(DigitalBrainNames.DefaultOwner);

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

    // Owner values are only forbidden from containing '/' or whitespace (IdentityPart.Validated),
    // so a dotted owner like "some.owner" is legal. Sibling must find the principal partition's
    // '.' after the owner/name '/' split, not the first '.' in the whole key, or a dotted owner
    // silently truncates (e.g. "vlad.horbachov" collapsing to "vlad").
    [Fact]
    public void KitEntityNamesSurviveADottedOwnerName()
    {
        var principalHex = Guid.NewGuid().ToString("N");
        var chat = $"some.owner/{principalHex}.main";

        var chart = KitInstanceNames.Sibling(chat, "chart-abc12345");

        Assert.Equal($"some.owner/{principalHex}.chart-abc12345", chart);
    }

    // The two prior tests pin Sibling's own math in isolation -- neither proves the TOOL's
    // write path (raw IGrainFactory.GetGrain<IChart>(Sibling(...))) and the ENDPOINT's read
    // path (IDigitalBrain.GetEntity, which resolves to EntityId.For<TEntity>(Owner, name).GrainKey)
    // land on the same grain. This test writes through the tool's exact call shape and reads
    // back through the kernel endpoint's exact call shape against the same running silo, which
    // covers both the key math and grain-TYPE resolution (GetGrain<T>(string) infers its
    // Orleans grain type from T directly; GetEntity goes through
    // EntityId.For<T>->GrainTypeNames.Of(T)->GrainId.Create). If Sibling's
    // owner/principal-boundary math and EntityId's "{owner}/{name}" scheme ever diverge,
    // tool-created cards would 404 forever from the kernel's /kit endpoints while every other
    // test here stays green.
    [Fact]
    public async Task ChartWrittenThroughTheToolsRawGrainKeyIsReadableThroughTheBrainClientEntityAccessor()
    {
        var principal = new PrincipalId(Guid.NewGuid());
        var chat = $"{fixture.Sim.Brain.Owner.Value}/"
            + PrincipalPartition.InstanceName(principal, fixture.Sim.UniqueId("chat"));
        var cardName = fixture.Sim.UniqueId("chart");

        var toolInstance = KitInstanceNames.Sibling(chat, cardName);
        var written = new ChartState("Sales", "bar", [new ChartPoint("Q1", 10)]);
        await fixture.Sim.Grains.GetGrain<IChart>(toolInstance).Render(written);

        var endpointInstance = PrincipalPartition.InstanceName(principal, cardName);
        var read = await fixture.Sim.Brain.GetEntity<IChart>(endpointInstance).Read();

        Assert.NotNull(read);
        Assert.Equal("Sales", read!.Title);
        Assert.Equal(written.Points.Count, read.Points.Count);
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

    // The model only ever echoes a chatName from its own conversation context, but nothing
    // stops it from echoing a chat that belongs to a different owner (whether by mistake or
    // by a crafted prompt). KitToolSource.ToolsFor closes over the caller's real owner and
    // must refuse any chatName outside that owner's partition before it ever reaches a
    // grain. The reply is asserted to equal the guard message exactly (not just "contains"),
    // which rules out the tool having fallen through to the success path -- the success
    // reply always embeds a generated card name, so an exact match on the guard text is
    // itself proof no chart entity was created.
    [Fact]
    public async Task RenderChartToolRefusesAChatNameFromADifferentOwnerWithoutTouchingTheChat()
    {
        var otherOwnerChat = OtherOwnerChatInstance();
        var tools = new KitToolSource(fixture.Sim.Grains, imageGeneration: null, imageStore: new MemoryKitImageStore());
        var render = tools.ToolsFor(Owner).Single(tool => tool.Name == "render_chart");

        var reply = await render.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = otherOwnerChat,
            ["title"] = "Sales",
            ["chartKind"] = "bar",
            ["labels"] = new[] { "Q1" },
            ["values"] = new[] { 10.0 },
        }, CancellationToken.None);

        Assert.Equal(
            $"chatName must be a chat key of this owner (starting with '{Owner.Value}/').",
            reply!.ToString());
        var transcript = await fixture.Sim.Grains.GetGrain<IChat>(otherOwnerChat).Read();
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

    [Fact]
    public async Task GenerateImageToolRefusesABlankPromptWithoutCallingTheGeneratorOrTouchingTheChat()
    {
        var chatInstance = NewChatInstance();
        var generator = new CountingImageGeneration();
        var tools = new KitToolSource(fixture.Sim.Grains, generator, new MemoryKitImageStore());
        var generate = tools.ToolsFor(Owner).Single(tool => tool.Name == "generate_image");

        var reply = await generate.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["prompt"] = "   ",
        }, CancellationToken.None);

        Assert.Contains("blank", reply!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, generator.CallCount);
        var transcript = await fixture.Sim.Grains.GetGrain<IChat>(chatInstance).Read();
        Assert.Empty(transcript.Turns);
    }

    // generate_image twin of RenderChartToolRefusesAChatNameFromADifferentOwnerWithoutTouchingTheChat
    // above -- see that test's comment for why the exact-message assertion doubles as proof
    // no image entity was created. CountingImageGeneration additionally proves the guard
    // fires before the (paid) image generator would ever be invoked.
    [Fact]
    public async Task GenerateImageToolRefusesAChatNameFromADifferentOwnerWithoutCallingTheGeneratorOrTouchingTheChat()
    {
        var otherOwnerChat = OtherOwnerChatInstance();
        var generator = new CountingImageGeneration();
        var tools = new KitToolSource(fixture.Sim.Grains, generator, new MemoryKitImageStore());
        var generate = tools.ToolsFor(Owner).Single(tool => tool.Name == "generate_image");

        var reply = await generate.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = otherOwnerChat,
            ["prompt"] = "a red fox",
        }, CancellationToken.None);

        Assert.Equal(
            $"chatName must be a chat key of this owner (starting with '{Owner.Value}/').",
            reply!.ToString());
        Assert.Equal(0, generator.CallCount);
        var transcript = await fixture.Sim.Grains.GetGrain<IChat>(otherOwnerChat).Read();
        Assert.Empty(transcript.Turns);
    }

    // A tool-supplied chatName is the raw grain key the model was told in its context:
    // "{owner}/{principal:N}.{local}" (owner/name form Neuron.Id requires, wrapping a
    // principal-scoped name — mirrors MapOwnerCommands.TryPrincipalResource composed
    // through DigitalBrainClient.GetGrainProxy). KitInstanceNames.Sibling only replaces the
    // local segment after the principal partition's '.', so tests need that full shape too.
    private string NewChatInstance()
        => $"{fixture.Sim.Brain.Owner.Value}/"
            + PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), fixture.Sim.UniqueId("chat"));

    // A well-formed chat key -- valid "{owner}/{principal:N}.{local}" shape, KitInstanceNames.Sibling
    // would happily parse it -- but scoped to an owner other than the one the tool was built
    // for (Owner). Exercises the owner guard itself rather than any other validation path.
    private string OtherOwnerChatInstance()
        => "someone-else/"
            + PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), fixture.Sim.UniqueId("chat"));

    private static string CardNameFrom(string replyText)
    {
        const string Marker = "card '";
        var markerIndex = replyText.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Reply did not name a card: {replyText}");

        var start = markerIndex + Marker.Length;
        var end = replyText.IndexOf('\'', start);
        Assert.True(end > start, $"Reply's card name was not quote-terminated: {replyText}");

        return replyText[start..end];
    }

    // Proves the blank-prompt guard short-circuits before ever reaching the image
    // generator (not just before the grain/chat calls that follow it).
    private sealed class CountingImageGeneration : IImageGeneration
    {
        private readonly TestImageGeneration _inner = new();

        public int CallCount { get; private set; }

        public Task<GeneratedKitImage> GenerateAsync(string prompt, CancellationToken cancellationToken)
        {
            CallCount++;
            return _inner.GenerateAsync(prompt, cancellationToken);
        }
    }
}
