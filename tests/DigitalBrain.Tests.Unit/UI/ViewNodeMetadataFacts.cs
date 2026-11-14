using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Invocation;
using DigitalBrain.UI;

namespace DigitalBrain.Tests.Unit.UI;

public sealed class ViewNodeMetadataFacts
{
    private static readonly JsonSerializerOptions Json = RuntimeProtocol.OptionsFor(typeof(View));

    [Fact]
    public void EveryConcreteNodeCarriesAViewNodeAttributeMatchingNodeJsonDerivedTypes()
    {
        var concreteNodes = typeof(Node).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Node).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var jsonDerived = typeof(Node).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .ToDictionary(
                a => a.DerivedType,
                a => Assert.IsType<string>(a.TypeDiscriminator));

        var discovered = ViewNodeAttribute.DiscoverAll();

        Assert.Equal(concreteNodes.Length, jsonDerived.Count);
        Assert.Equal(concreteNodes.Length, discovered.Count);

        foreach (var nodeType in concreteNodes)
        {
            var attr = nodeType.GetCustomAttribute<ViewNodeAttribute>(inherit: false);
            Assert.NotNull(attr);
            Assert.True(jsonDerived.TryGetValue(nodeType, out var discriminator), $"{nodeType.Name} is missing [JsonDerivedType] on Node.");
            Assert.Equal(discriminator, attr!.Kind);
            Assert.Same(nodeType, discovered[attr.Kind]);
        }
    }

    [Fact]
    public void ViewNodeAttributeRejectsBlankKind()
    {
        Assert.Throws<ArgumentException>(() => new ViewNodeAttribute(""));
        Assert.Throws<ArgumentException>(() => new ViewNodeAttribute("   "));
    }

    [Fact]
    public void OptionalVisualMetadataFieldsAreOmittedWhenNullInJsonAndRoundTripCleanly()
    {
        var conversation = new Conversation(
            "conv",
            [
                new MessageNode(
                    "a1",
                    "assistant",
                    "Ready.",
                    Cards: [new ApprovalCardNode("app-1", "plan-1", "Run git push?", "Pushes branch", [])],
                    Activities: [new ActivityNode("act-1", "Checked status")]),
            ],
            new ComposerNode("composer", new Emit("app.ino", "ino.asked")));

        var view = new View("Ino", conversation);
        var wire = JsonSerializer.SerializeToElement(view, Json);

        var msgJson = wire.GetProperty("root").GetProperty("messages")[0];
        Assert.False(msgJson.TryGetProperty("modelLabel", out _));
        Assert.False(msgJson.TryGetProperty("providerIcon", out _));

        var actJson = msgJson.GetProperty("activities")[0];
        Assert.False(actJson.TryGetProperty("icon", out _));
        Assert.False(actJson.TryGetProperty("category", out _));

        var cardJson = msgJson.GetProperty("cards")[0];
        Assert.False(cardJson.TryGetProperty("icon", out _));
        Assert.False(cardJson.TryGetProperty("capabilityLabel", out _));

        var composerJson = wire.GetProperty("root").GetProperty("composer");
        Assert.False(composerJson.TryGetProperty("activeModelLabel", out _));
        Assert.False(composerJson.TryGetProperty("activeProviderIcon", out _));

        var roundTripped = JsonSerializer.Deserialize<View>(wire.GetRawText(), Json);
        Assert.Equal(view, roundTripped);
    }

    [Fact]
    public void PopulatedVisualMetadataFieldsRoundTripWithCamelCaseWireKeysAndEquality()
    {
        var approval = new ApprovalCardNode(
            "app-1",
            "plan-1",
            "Send email?",
            "Sends summary to Ada",
            [],
            Resolve: new Resolve("plan-1"),
            Icon: "mail",
            CapabilityLabel: "Gmail");

        var activity = new ActivityNode(
            "act-1",
            "Searched repository",
            Status: "done",
            Detail: "12 matches",
            Icon: "search",
            Category: "code");

        var message = new MessageNode(
            "a1",
            "assistant",
            "Found 12 matches.",
            Cards: [approval],
            Activities: [activity],
            Status: "completed",
            Model: "claude-opus-4-6",
            ModelLabel: "Claude Opus 4.6",
            ProviderIcon: "anthropic");

        var composer = new ComposerNode(
            "composer",
            new Emit("app.ino", "ino.asked"),
            Placeholder: "Ask anything",
            Enabled: true,
            ActiveModelLabel: "Claude Opus 4.6",
            ActiveProviderIcon: "anthropic");

        var view = new View("Ino", new Conversation("conv", [message], composer));
        var wire = JsonSerializer.SerializeToElement(view, Json);

        var msgJson = wire.GetProperty("root").GetProperty("messages")[0];
        Assert.Equal("Claude Opus 4.6", msgJson.GetProperty("modelLabel").GetString());
        Assert.Equal("anthropic", msgJson.GetProperty("providerIcon").GetString());

        var actJson = msgJson.GetProperty("activities")[0];
        Assert.Equal("search", actJson.GetProperty("icon").GetString());
        Assert.Equal("code", actJson.GetProperty("category").GetString());

        var cardJson = msgJson.GetProperty("cards")[0];
        Assert.Equal("mail", cardJson.GetProperty("icon").GetString());
        Assert.Equal("Gmail", cardJson.GetProperty("capabilityLabel").GetString());

        var composerJson = wire.GetProperty("root").GetProperty("composer");
        Assert.Equal("Claude Opus 4.6", composerJson.GetProperty("activeModelLabel").GetString());
        Assert.Equal("anthropic", composerJson.GetProperty("activeProviderIcon").GetString());

        var roundTripped = JsonSerializer.Deserialize<View>(wire.GetRawText(), Json);
        Assert.Equal(view, roundTripped);

        // Verify custom Equals detects visual metadata differences on MessageNode & ApprovalCardNode.
        Assert.NotEqual(message, message with { ModelLabel = "Other" });
        Assert.NotEqual(message, message with { ProviderIcon = "openai" });
        Assert.NotEqual(approval, approval with { Icon = "shield" });
        Assert.NotEqual(approval, approval with { CapabilityLabel = "GitHub" });
    }
}
